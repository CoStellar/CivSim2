using System;
using System.Linq;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Pick the start hex during the start phase.</summary>
public sealed record PickStartCommand(int Civ, int Cell) : SimCommand(Civ);

/// <summary>
/// Scores of every land hex as a place to start, computed once after the world is generated (decision of Patryk, 2026-11-17):
/// several criteria from 0 to 100, where 100 is the best hex of this map. The AI picks from them without computing anything,
/// and the summary after the game tells the player how good the chosen place was.
/// </summary>
public sealed class SiteScores
{
    /// <summary>Names of the criteria, in the order of <see cref="Values"/>.</summary>
    public static readonly string[] Criteria = ["people", "farming", "army", "materials", "room", "future", "safety"];

    /// <summary>Creates empty scores for <paramref name="cells"/> hexes.</summary>
    public SiteScores(int cells)
    {
        Values = new byte[Criteria.Length][];
        for (int k = 0; k < Values.Length; k++)
        {
            Values[k] = new byte[cells];
        }

        Total = new byte[cells];
        Seen = new byte[cells];
    }

    /// <summary>Score per criterion per hex (0 = water or worthless, 100 = best of the map).</summary>
    public byte[][] Values { get; }

    /// <summary>Overall score per hex, 0–100 (with deposits and soil: for the summary after the game).</summary>
    public byte[] Total { get; }

    /// <summary>
    /// Overall score per hex, 0–100, from what anyone sees while choosing (terrain, water, trees; no deposits or soil):
    /// what the AI chooses by, so it knows no more than the player (known issue 23, 2026-11-19).
    /// </summary>
    public byte[] Seen { get; }
}

public sealed partial class SimGame
{

    /// <summary>Site scores (computed once at creation).</summary>
    public SiteScores? Sites { get; private set; }

    /// <summary>True while civilizations are still choosing where to start.</summary>
    public bool Picking { get; private set; }

    /// <summary>Order in which civilizations choose (civ ids).</summary>
    public List<int> PickOrder { get; } = [];

    /// <summary>Index in <see cref="PickOrder"/> of the people choosing now.</summary>
    public int PickIndex { get; private set; }

    /// <summary>Real ticks the current chooser has left.</summary>
    public int PickTicksLeft { get; private set; }

    /// <summary>Ticks a person gets to choose (startPhase.pickSeconds).</summary>
    public int PickTicks => Content.StartPhase.PickTicks;

    /// <summary>Start hex chosen by each civilization (−1 = not yet).</summary>
    public int[] StartCell { get; private set; } = [];

    /// <summary>Hexes around a chosen start where no other people may start (Patryk, 2026-11-19: 15 took too much of a continent).</summary>
    public int BlockRadius => Content.StartPhase.BlockRadius;

    /// <summary>
    /// How far the shown circle of a taken start lies from the start itself (Patryk, 2026-11-19: the circles must not give the
    /// start away). The circle is this much wider, so it still covers every hex too close to the start.
    /// </summary>
    public int BlockFuzz => Content.StartPhase.BlockFuzz;

    /// <summary>Centre of each taken start's circle: a hex near the start, from the seed (−1 = not chosen).</summary>
    public int[] BlockCenter { get; private set; } = [];

    /// <summary>Radius of the shown circles: the block plus the fuzz.</summary>
    public int ShownBlockRadius => BlockRadius + BlockFuzz;

    // A hex within the fuzz of the start, chosen by the seed and the people: the same in every replay.
    private int FuzzedCenter(int civ, int cell)
    {
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(BlockFuzz)];
        int n = Geo.Range(Geo.FromIndex(cell), BlockFuzz, area);
        ulong z = (Settings.Seed * 0x9E3779B97F4A7C15UL) + ((ulong)(civ + 1) * 0xBF58476D1CE4E5B9UL) + (ulong)cell;
        z = (z ^ (z >> 31)) * 0x94D049BB133111EBUL;
        return Geo.Index(area[(int)((z ^ (z >> 29)) % (ulong)n)]);
    }

    /// <summary>Scores the whole map for starting (once).</summary>
    public void ScoreSites()
    {
        int n = Geo.CellCount;
        var scores = new SiteScores(n);
        Score(scores.Total, scores.Values, ground: true);
        Score(scores.Seen, null, ground: false);
        Sites = scores;
    }

    // Scores every hex into total (and, with values, each criterion). Without ground, deposits and soil count as unknown.
    private void Score(byte[] totals, byte[][]? values, bool ground)
    {
        int n = Geo.CellCount;
        var raw = new int[SiteScores.Criteria.Length][];
        for (int k = 0; k < raw.Length; k++)
        {
            raw[k] = new int[n];
        }

        var area = new HexCoord[HexGlobe.RangeSize(Content.StartPhase.RoomRadius)];
        int copper = DepositId("copper");
        int tin = DepositId("tin");
        int iron = DepositId("iron_ore");
        int stone = DepositId("stone");
        int clay = DepositId("clay");
        int flint = DepositId("flint");
        int fish = DepositId("fish");
        int game = DepositId("game");
        int grain = DepositId("wild_grain");
        for (int i = 0; i < n; i++)
        {
            if (Content.World.Biomes[Map.Biome[i]].IsWater)
            {
                continue;
            }

            int m = Geo.Range(Geo.FromIndex(i), Content.StartPhase.RoomRadius, area);
            int food = 0, farm = 0, army = 0, mat = 0, room = 0, future = 0, safe = 0;
            for (int k = 0; k < m; k++)
            {
                int c = Geo.Index(area[k]);
                bool near = Geo.Distance(Geo.FromIndex(i), area[k]) <= Content.StartPhase.ScoreRadius;
                bool water = Content.World.Biomes[Map.Biome[c]].IsWater;
                int dep = ground ? Map.Deposit[c] : 0;
                if (!water)
                {
                    room++;
                }

                if (dep == copper || dep == tin || dep == iron)
                {
                    future += 3;
                }

                if (!near)
                {
                    continue;
                }

                food += Content.World.Biomes[Map.Biome[c]].Food * 2 + (dep == fish || dep == game || dep == grain ? 4 : 0);
                if (!water)
                {
                    farm += ground ? Map.Fertility[c] / 10 : 0;
                    army += (Map.Relief[c] > 0 ? 3 : 0) + (Content.WalkCost[(Map.Biome[c] * 3) + Map.Relief[c]] > 100 ? 1 : 0);
                    mat += (Map.Relief[c] > 0 ? 2 : 0) + (dep == stone || dep == clay || dep == flint ? 4 : 0) + (Trees[c] == Forest ? 2 : Trees[c] == Grove ? 1 : 0);
                    safe += Map.GrowingSeason[c] / 10;
                }

                if (dep == copper || dep == tin || dep == iron)
                {
                    army += 2;
                }
            }

            if (NextToWater(i))
            {
                food += 6;
                farm += 6;
            }

            raw[0][i] = food;
            raw[1][i] = farm;
            raw[2][i] = army;
            raw[3][i] = mat;
            raw[4][i] = room;
            raw[5][i] = future;
            raw[6][i] = safe;
        }

        var weights = new[] { 30, 20, 10, 15, 10, 5, 10 };
        var max = new int[raw.Length];
        for (int k = 0; k < raw.Length; k++)
        {
            foreach (int v in raw[k])
            {
                max[k] = Math.Max(max[k], v);
            }
        }

        int bestTotal = 1;
        var total = new int[n];
        for (int i = 0; i < n; i++)
        {
            int t = 0;
            for (int k = 0; k < raw.Length; k++)
            {
                int v = max[k] == 0 ? 0 : raw[k][i] * 100 / max[k];
                if (values is not null)
                {
                    values[k][i] = (byte)v;
                }

                t += v * weights[k];
            }

            total[i] = t / 100;
            bestTotal = Math.Max(bestTotal, total[i]);
        }

        for (int i = 0; i < n; i++)
        {
            totals[i] = (byte)(total[i] * 100 / bestTotal);
        }
    }

    private int DepositId(string key)
    {
        foreach (Core.Content.DepositDefinition d in Content.World.Deposits)
        {
            if (d.Key == key)
            {
                return d.Id;
            }
        }

        return -1;
    }

    /// <summary>Why a civilization cannot start on <paramref name="cell"/> now (null = it can).</summary>
    public string? CannotStart(int cell) => CannotStart(cell, near: true);

    // near: also the 12B rule (main continent, near the others); the AI drops it only when no place at all fits it.
    private string? CannotStart(int cell, bool near)
    {
        if ((uint)cell >= (uint)Geo.CellCount || Content.World.Biomes[Map.Biome[cell]].IsWater)
        {
            return "A civilization starts on land.";
        }

        if (Map.Relief[cell] == 2)
        {
            return "Not in the mountains.";
        }

        // The shown circles are the rule: nobody learns more than they see.
        HexCoord c = Geo.FromIndex(cell);
        foreach (int s in BlockCenter)
        {
            if (s >= 0 && Geo.Distance(Geo.FromIndex(s), c) < ShownBlockRadius)
            {
                return "Too close to another civilization.";
            }
        }

        return near ? TooFar(cell) : null;
    }

    private void BeginPicking()
    {
        Picking = true;
        StartCell = new int[Civs.Count];
        Array.Fill(StartCell, -1);
        BlockCenter = new int[Civs.Count];
        Array.Fill(BlockCenter, -1);
        PickOrder.Clear();
        for (int c = 0; c < Civs.Count; c++)
        {
            PickOrder.Add(c);
        }

        for (int i = PickOrder.Count - 1; i > 0; i--)
        {
            int j = _events.NextInt(i + 1);
            (PickOrder[i], PickOrder[j]) = (PickOrder[j], PickOrder[i]);
        }

        PickIndex = 0;
        PickTicksLeft = Civs[PickOrder[0]].IsAi ? SimContent.TicksPerSecond : PickTicks;
        RefreshNear();
        foreach (Civ civ in Civs)
        {
            Array.Fill(civ.Known, true); // the whole map, but the server hides deposits and fertility while picking
        }
    }

    /// <summary>
    /// Gives the people choosing now its full time again (a player gets <see cref="PickTicks"/>, the AI one second). Called when the
    /// server has handed civilizations to players before any tick: the phase began while every civilization was still the AI's, so a player who
    /// chose first had one second instead of thirty (found while fixing A13, 2026-11-20).
    /// </summary>
    public void RestartPickClock()
    {
        if (Picking && Ticks == 0)
        {
            PickTicksLeft = Civs[PickOrder[PickIndex]].IsAi ? SimContent.TicksPerSecond : PickTicks;
        }
    }

    /// <summary>E13 (Patryk, 2026-11-20): a player alone chooses the start without a time limit (the AI keeps its second).</summary>
    public bool PickWaitsForPlayers { get; set; }

    // One tick of the start phase: the chooser's time runs out, then the AI picks for it.
    private void PickingTick()
    {
        if (PickWaitsForPlayers && !Civs[PickOrder[PickIndex]].IsAi)
        {
            return; // waits for the player's choice
        }

        if (--PickTicksLeft > 0)
        {
            return;
        }

        Civ civ = Civs[PickOrder[PickIndex]];
        PlaceStart(civ, AiStart(civ));
    }

    /// <summary>The hex the AI would choose for a civilization: from the best free places, with this civilization's error.</summary>
    public int AiStart(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        SiteScores sites = Sites!;
        int best = 0;
        for (int i = 0; i < Geo.CellCount; i++)
        {
            if (sites.Seen[i] > best && CannotStart(i) is null)
            {
                best = sites.Seen[i];
            }
        }

        bool near = best > 0;
        if (!near)
        {
            for (int i = 0; i < Geo.CellCount; i++)
            {
                if (sites.Seen[i] > best && CannotStart(i, near: false) is null)
                {
                    best = sites.Seen[i]; // no place near the others is free: anywhere (12B, a fallback that should not happen)
                }
            }
        }

        int error = civ.StartError;
        int bar = best * (100 - error) / 100;
        var pool = new List<int>();
        for (int i = 0; i < Geo.CellCount; i++)
        {
            if (sites.Seen[i] >= bar && CannotStart(i, near) is null)
            {
                pool.Add(i);
            }
        }

        // 12B: room to grow first, when such places are left.
        int room = Content.StartPhase.NeighbourMinHexes - BlockFuzz;
        if (room > ShownBlockRadius)
        {
            var roomy = pool.FindAll(i => Array.TrueForAll(BlockCenter, b => b < 0 || Geo.Distance(Geo.FromIndex(b), Geo.FromIndex(i)) >= room));
            if (roomy.Count > 0)
            {
                pool = roomy;
            }
        }

        return pool.Count == 0 ? -1 : pool[AiRandom.NextInt(pool.Count)];
    }

    private string? PickStart(Civ civ, int cell)
    {
        if (!Picking || PickOrder[PickIndex] != civ.Id)
        {
            return "It is not our turn to choose.";
        }

        string? why = CannotStart(cell);
        if (why is not null)
        {
            return why;
        }

        PlaceStart(civ, cell);
        return null;
    }

    private void PlaceStart(Civ civ, int cell)
    {
        int best = 0;
        for (int i = 0; i < Geo.CellCount; i++)
        {
            if (Sites!.Total[i] > best && CannotStart(i) is null)
            {
                best = Sites.Total[i];
            }
        }

        civ.BestStartAtPick = best;
        if (cell < 0)
        {
            // No free land left: fall back to the generator's start hex.
            cell = Settlements.Count > 0 ? Settlements[0].Center : 0;
        }

        StartCell[civ.Id] = cell;
        BlockCenter[civ.Id] = FuzzedCenter(civ.Id, cell);
        RefreshNear();
        PickIndex++;
        if (PickIndex < PickOrder.Count)
        {
            PickTicksLeft = Civs[PickOrder[PickIndex]].IsAi ? SimContent.TicksPerSecond : PickTicks;
            return;
        }

        // Everyone has chosen: the game begins.
        Picking = false;
        foreach (Civ c in Civs)
        {
            Array.Fill(c.Known, false);
            c.NewlyKnown.Clear();
            for (int chunk = 0; chunk < Map.Chunks.Count; chunk++)
            {
                c.NewlyKnown.Add(chunk);
            }

            StartMetals(c, StartCell[c.Id]);
            FoundSettlement(c, StartCell[c.Id], Content.StartPeople);
            News.Add((c.Id, StoryOf(c, StartCell[c.Id])));
            News.Add((c.Id, StartNote())); // E14 (2026-11-20)
        }
    }

    /// <summary>E14: what the civilization knows after the start, and how to see more.</summary>
    public string StartNote()
    {
        int scouts = Content.Units.ToList().FindIndex(u => u.Key == "scouts");
        string tech = scouts >= 0 ? Content.Techs[Content.Units[scouts].Tech].Name : "Scouting";
        return $"Our people know only this valley. Train scouts ({tech}) to see the world.";
    }

    /// <summary>Three sentences of the tribe's past, from the land it chose.</summary>
    public string StoryOf(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        string biome = Content.World.Biomes[Map.Biome[cell]].Name.ToLowerInvariant();
        string relief = Map.Relief[cell] == 1 ? "among low hills" : "on open ground";
        string water = NextToWater(cell) ? "by fresh water that never runs dry" : "far from any river, trusting the rains";
        string past = civ.Aggression > 60 ? "They drove off the bands that hunted here before them."
            : civ.Expansion > 60 ? "They walked many moons, always toward the next valley."
            : "They followed the herds until the elders said: here.";
        return $"The {civ.Name} remember the long walk to the {biome}. {past} They raised their first fires {relief}, {water}.";
    }

    /// <summary>Score of a civilization's start compared with the best place free when it chose (for the summary).</summary>
    public (int Mine, int Best) StartQuality(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        if (Sites is null || StartCell.Length <= civ.Id || StartCell[civ.Id] < 0)
        {
            return (0, 0);
        }

        return (Sites.Total[StartCell[civ.Id]], civ.BestStartAtPick);
    }

    /// <summary>
    /// M4 (2026-11-19): every start has the metals of the first metal age near it (startDeposits: a deposit within min–max hexes,
    /// placed on free land when the map has none there).
    /// </summary>
    internal void StartMetals(Civ civ, int center)
    {
        if (!Settings.FairOres)
        {
            return; // realistic ores (2026-11-24): only what the world made
        }

        foreach (StartDeposit want in Content.StartDeposits)
        {
            var ring = new HexCoord[HexGlobe.RangeSize(want.Max)];
            int n = Geo.Range(Geo.FromIndex(center), want.Max, ring);
            bool have = false;
            var spots = new List<int>();
            for (int k = 0; k < n; k++)
            {
                int cell = Geo.Index(ring[k]);
                int d = Distance(center, cell);
                if (Map.Deposit[cell] == want.Deposit)
                {
                    have = true;
                    break;
                }

                if (d >= want.Min && Map.Deposit[cell] == 0 && !Content.World.Biomes[Map.Biome[cell]].IsWater && CellCost(cell) > 0)
                {
                    spots.Add(cell);
                }
            }

            if (have || spots.Count == 0)
            {
                continue;
            }

            spots.Sort();
            int first = (int)((((ulong)(civ.Id + 1) * 0x9E3779B97F4A7C15UL) ^ (ulong)want.Deposit) % (ulong)spots.Count);
            int pick = spots[first];

            // 7A (Patryk, 2026-11-20): a far deposit (iron 15-20 hexes) must lie on the same land, not over the sea.
            var way = new List<int>();
            for (int k = 0; k < Math.Min(12, spots.Count); k++)
            {
                int c = spots[(first + (k * 7919)) % spots.Count];
                if (FindPath(center, c, way, false, -1, 6000))
                {
                    pick = c;
                    break;
                }
            }

            Map.Deposit.Set(pick, (ushort)want.Deposit);
            Map.DepositDepth.Set(pick, (byte)want.Depth);
            if (want.Size >= 0 && want.Size < Content.Lodes.Sizes.Length)
            {
                Map.DepositRichness.Set(pick, Content.Lodes.Sizes[want.Size].Percent); // 2026-11-22: the start's tin is small
            }
            else if (Content.Lodes.Sizes.Length > 0)
            {
                Map.DepositRichness.Set(pick, 100);
            }
        }
    }
}
