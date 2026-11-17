using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Core.Random;
using CivSim.Core.WorldGen;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Settings of a new game.</summary>
public sealed record SimSettings(ulong Seed, int LegacyWidth = 98, int LegacyHeight = 60, int Civilizations = 4)
{
    /// <summary>World type.</summary>
    public WorldType WorldType { get; init; } = WorldType.Continents;

    /// <summary>Water percent.</summary>
    public int WaterPercent { get; init; } = 60;
}

/// <summary>
/// The whole state of a game of the new simulation (ADR 0028) and its tick loop. Changes only through commands
/// (<see cref="Apply"/>) and <see cref="Tick"/>. Follows docs/wydajnosc.md: the economy is a set of rates per civilization
/// recomputed only when something changes, constructions end at a scheduled tick, and the rhythms are staggered by civilization.
/// </summary>
public sealed partial class SimGame
{
    private const int DoneBuilding = 0;
    private const int DoneTraining = 1;
    private const int UnitStep = 2;
    private readonly PriorityQueue<(int Kind, int Id), (long Tick, int Kind, int Id)> _schedule = new();

    private SimGame(SimContent content, SimSettings settings, WorldMap map)
    {
        Content = content;
        Settings = settings;
        Map = map;
        Geo = map.Geometry;
        OwnerAt = new short[Geo.CellCount];
        SettlementAt = new int[Geo.CellCount];
        BuildingAt = new int[Geo.CellCount];
        Array.Fill(OwnerAt, (short)-1);
        Array.Fill(SettlementAt, -1);
        Array.Fill(BuildingAt, -1);
    }

    /// <summary>Rules.</summary>
    public SimContent Content { get; }

    /// <summary>Settings the game was created with.</summary>
    public SimSettings Settings { get; }

    /// <summary>The world map (terrain from the world generator).</summary>
    public WorldMap Map { get; }

    /// <summary>Map geometry.</summary>
    public HexGlobe Geo { get; }

    /// <summary>Ticks since the start.</summary>
    public long Ticks { get; private set; }

    /// <summary>Owner civilization of each hex (−1 = nobody).</summary>
    public short[] OwnerAt { get; }

    /// <summary>Settlement whose territory each hex is (−1 = none).</summary>
    public int[] SettlementAt { get; }

    /// <summary>Building on each hex (−1 = none).</summary>
    public int[] BuildingAt { get; }

    /// <summary>Civilizations.</summary>
    public List<Civ> Civs { get; } = [];

    /// <summary>Settlements, by id.</summary>
    public List<Settlement> Settlements { get; } = [];

    /// <summary>Buildings, by id.</summary>
    public List<Building> Buildings { get; } = [];

    /// <summary>Units, by id.</summary>
    public List<Unit> Units { get; } = [];

    /// <summary>Events of the last tick for the chronicle (civ −1 = everyone).</summary>
    public List<(int Civ, string Text)> News { get; } = [];

    /// <summary>Map size in hexes for a legacy size and a density: the area grows by the density.</summary>
    public static (int Width, int Height) SizeFor(int legacyWidth, int legacyHeight, int density)
    {
        double k = Math.Sqrt(density);
        int w = (int)Math.Round(legacyWidth * k);
        w -= ((w - 2) % 4 + 4) % 4; // width = 4k + 2
        int h = (int)Math.Round(legacyHeight * k);
        h -= h % 2;
        return (Math.Max(6, w), Math.Max(4, h));
    }

    /// <summary>Creates a game: generates the world and places one camp for each civilization.</summary>
    public static SimGame Create(SimContent content, SimSettings settings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(settings);
        (int w, int h) = SizeFor(settings.LegacyWidth, settings.LegacyHeight, content.Density);
        var map = new WorldMap(new HexGlobe(w, h));
        var streams = new RandomStreams(settings.Seed);
        var options = new WorldGenOptions(settings.WorldType, settings.WaterPercent, settings.Civilizations);
        WorldGenReport report = WorldGenerator.Generate(content.World, map, streams[RandomStreamId.World], settings.Seed, options);
        var game = new SimGame(content, settings, map);
        for (int c = 0; c < settings.Civilizations && c < report.Starts.Count; c++)
        {
            var civ = new Civ(c, $"People {c + 1}", content.Goods.Count) { Known = new bool[map.Geometry.CellCount] };
            game.Civs.Add(civ);
            Array.Copy(content.StartStock, civ.Stock, civ.Stock.Length);
            game.FoundSettlement(civ, map.Geometry.Index(report.Starts[c]), content.StartPeople);
        }

        return game;
    }

    /// <summary>Creates a settlement for <paramref name="civ"/> at <paramref name="center"/> and claims its territory.</summary>
    public Settlement FoundSettlement(Civ civ, int center, int people)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var s = new Settlement { Id = Settlements.Count, Civ = civ.Id, Center = center, People = people, Name = $"{civ.Name} {civ.Settlements.Count + 1}" };
        Settlements.Add(s);
        civ.Settlements.Add(s.Id);
        Claim(s);
        Reveal(civ, center, Content.Levels[s.Level].Radius + 4);
        civ.Dirty = true;
        return s;
    }

    /// <summary>Runs one tick: due constructions, then the economy and population of the civilizations whose turn it is.</summary>
    public void Tick()
    {
        Ticks++;
        News.Clear();
        while (_schedule.TryPeek(out (int Kind, int Id) item, out (long Tick, int Kind, int Id) due) && due.Tick <= Ticks)
        {
            _schedule.Dequeue();
            switch (item.Kind)
            {
                case DoneBuilding:
                    Building b = Buildings[item.Id];
                    if (b.State == BuildingState.Construction && b.FinishTick == due.Tick)
                    {
                        b.State = BuildingState.Active;
                        Civs[b.Civ].Dirty = true;
                        News.Add((b.Civ, $"{Content.Buildings[b.Type].Name} is finished."));
                    }

                    break;
                case DoneTraining:
                    Unit trained = Units[item.Id];
                    if (trained.State == UnitState.Training && trained.NextTick == due.Tick)
                    {
                        trained.State = UnitState.Idle;
                        Reveal(Civs[trained.Civ], trained.Cell, Content.Units[trained.Kind].Sight);
                        News.Add((trained.Civ, $"{Content.Units[trained.Kind].Name} are ready."));
                    }

                    break;
                default:
                    Unit walker = Units[item.Id];
                    if (walker.State == UnitState.Moving && walker.NextTick == due.Tick)
                    {
                        Step(walker);
                    }

                    break;
            }
        }

        foreach (Civ civ in Civs)
        {
            if (civ.Dead)
            {
                continue;
            }

            if ((Ticks + civ.Id) % Content.EconomyEveryTicks == 0)
            {
                EconomyStep(civ);
            }

            if ((Ticks + civ.Id) % Content.PopulationEveryTicks == 0)
            {
                PopulationStep(civ);
            }
        }
    }

    private void Claim(Settlement s)
    {
        int radius = Content.Levels[s.Level].Radius;
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(radius)];
        int n = Geo.Range(Geo.FromIndex(s.Center), radius, area);
        for (int k = 0; k < n; k++)
        {
            int i = Geo.Index(area[k]);
            if (OwnerAt[i] >= 0 || Content.World.Biomes[Map.Biome[i]].IsWater)
            {
                continue;
            }

            OwnerAt[i] = (short)s.Civ;
            Map.Owner.Set(i, (short)s.Civ);
            SettlementAt[i] = s.Id;
            s.Cells.Add(i);
            Map.MarkCellDirty(i);
        }
    }

    private void Schedule(Building b) => _schedule.Enqueue((DoneBuilding, b.Id), (b.FinishTick, DoneBuilding, b.Id));

    private void ScheduleUnit(Unit u, int kind) => _schedule.Enqueue((kind, u.Id), (u.NextTick, kind, u.Id));

    /// <summary>Marks the hexes within <paramref name="radius"/> of <paramref name="cell"/> as seen by <paramref name="civ"/>.</summary>
    public void Reveal(Civ civ, int cell, int radius)
    {
        ArgumentNullException.ThrowIfNull(civ);
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(radius)];
        int n = Geo.Range(Geo.FromIndex(cell), radius, area);
        for (int k = 0; k < n; k++)
        {
            int i = Geo.Index(area[k]);
            if (!civ.Known[i])
            {
                civ.Known[i] = true;
                civ.NewlyKnown.Add(Map.Chunks.ChunkOfIndex(i));
            }
        }
    }
}
