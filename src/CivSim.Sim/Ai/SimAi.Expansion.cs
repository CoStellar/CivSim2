using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Ai;

// The AI's expansion: scouts, prospectors and surveyors, settlers and where they settle (split from SimAi.cs, 2026-11-22).
public static partial class SimAi
{
    // Hexes a scout's way search may look at: a target ~14 hexes off needs far fewer; one beyond a river or a sea fails fast.
    private const int ScoutSearchLimit = 1500;

    // 17B (Patryk, 2026-11-20): a civilization this small needs every person at home.
    private static bool Small(SimGame g, Civ civ) => civ.People < g.Content.AiSmallPeople;

    private static void Scouts(SimGame g, Civ civ)
    {
        UnitType scouts = g.Content.Unit("scouts");
        if (Own(g, civ, t => t.Key == "scouts") == 0 && g.Ticks < 30 * SimContent.TicksPerMinute && Affordable(civ, scouts.Price, 50) && !Small(g, civ)) // 17B: later, once 30 people
        {
            g.Apply(new TrainCommand(civ.Id, civ.Settlements[0], scouts.Id));
        }

        foreach (int id in civ.Units.ToArray())
        {
            Unit u = g.Units[id];
            if (u.Kind == scouts.Id && u.State == UnitState.Idle && g.Ticks >= civ.AiScoutWait)
            {
                // Walk to an unknown land hex some way off; the hex is picked from the AI's own random stream. Tick spikes
                // (2026-11-20): each refused try searched a way over the whole map (water, other continents), up to 12 a
                // thought; now only land hexes are tried, and one search a thought.
                int searches = 0;
                for (int tries = 0; tries < 12 && searches < 1; tries++)
                {
                    HexCoord c = g.Geo.FromIndex(u.Cell);
                    int dc = g.AiRandom.NextInt(-14, 15);
                    int dr = g.AiRandom.NextInt(-10, 11);
                    int cell = g.Geo.Index(g.Geo.Wrap(c.Col + dc, Math.Clamp(c.Row + dr, 0, g.Geo.Height - 1)));
                    if (civ.Known[cell] || g.Content.World.Biomes[g.Map.Biome[cell]].IsWater)
                    {
                        continue;
                    }

                    searches++;
                    if (g.Apply(new MoveCommand(civ.Id, u.Id, cell, false, null, ScoutSearchLimit)) is null)
                    {
                        searches = -1;
                        break;
                    }
                }

                if (searches > 0)
                {
                    civ.AiScoutWait = g.Ticks + (30 * SimContent.TicksPerSecond); // nothing reachable around: look again later
                }
            }
        }
    }

    // Prospectors and surveyors (2026-11-19): one at a time once the civilization has three settlements (prospectors from the
    // Neolithic, when ores can be found; surveyors before); it walks to ground around
    // our settlements that it has not looked at yet, stands there (surveys), then goes on. Same rules as for the player.
    private static void Surveys(SimGame g, Civ civ)
    {
        // Not before the civilization has spread a little: early on every person is needed at home.
        // Tests 2026-11-23: an ore the way to the next era needs sends prospectors from the first settlement on.
        bool needOre = civ.Era >= 1 && Array.IndexOf(WantedDeposits(g, civ), true) >= 0;
        if (g.Ticks < 15 * SimContent.TicksPerMinute || (civ.Settlements.Count < 3 && !needOre) || civ.Hungry)
        {
            return;
        }

        int surveying = Own(g, civ, x => x.Survey > 0); // one at a time: prospectors once ores matter, else surveyors
        // Known-issues 13 (2026-11-23): while workshops stand idle for an ore no known deposit gives, more prospectors.
        bool starved = civ.Era >= 1 && Array.IndexOf(StarvedDeposits(g, civ), true) >= 0;

        foreach (UnitType t in g.Content.Units)
        {
            if (t.Survey == 0 || !SimGame.Knows(civ, t))
            {
                continue;
            }

            bool wanted = t.Survey == 1 ? civ.Era >= 1 : civ.Era == 0;
            int cap = t.Survey == 1 && starved ? g.Content.AiStarvedProspectors : 1;
            if (surveying < cap && wanted && !Small(g, civ) && Own(g, civ, x => x.Founds) == 0 && Affordable(civ, t.Price, 50)
                && Own(g, civ, _ => true) == Own(g, civ, _ => true, includeTraining: false))
            {
                g.Apply(new TrainCommand(civ.Id, civ.Settlements[0], t.Id));
            }

            foreach (int id in civ.Units.ToArray())
            {
                Unit u = g.Units[id];
                if (u.Kind != t.Id || u.SearchingSigns || u.State != UnitState.Idle || u.SurveyedAt != (u.Cell * 16) + civ.Era)
                {
                    continue; // still walking, or not yet looked around where it stands
                }

                // 2026-11-22: with ore signs, prospectors search them first (the same order as the player's).
                if (t.Survey == 1 && !u.SearchingSigns && civ.Signs.Count > 0 && g.Apply(new SearchSignsCommand(civ.Id, u.Id)) is null)
                {
                    continue;
                }

                // Targets come from a hash of the unit and the time, not from the AI's random stream, so surveys do not
                // change the AI's other choices.
                ulong z = ((ulong)g.Ticks * 0x9E3779B97F4A7C15UL) ^ ((ulong)(u.Id + 1) * 0xBF58476D1CE4E5B9UL);
                Settlement home = g.Settlements[civ.Settlements[(int)(Mix(ref z) % (ulong)civ.Settlements.Count)]];
                HexCoord c = g.Geo.FromIndex(home.Center);
                // 7A (2026-11-20): while a deposit we need is not found (iron 15-20 hexes away), prospectors look farther.
                bool far = t.Survey == 1 && (starved || Array.IndexOf(WantedDeposits(g, civ), true) >= 0);
                int wide = far ? 22 : 12;
                int tall = far ? 18 : 9;
                bool moved = false;
                for (int tries = 0; tries < 48 && !moved; tries++)
                {
                    // Tests 2026-11-23: with every near hex looked at, the box grows (prospectors stood idle 12 minutes).
                    if (tries == 16 || tries == 32)
                    {
                        wide += 12;
                        tall += 9;
                    }

                    int dc = (int)(Mix(ref z) % (ulong)((2 * wide) + 1)) - wide;
                    int dr = (int)(Mix(ref z) % (ulong)((2 * tall) + 1)) - tall;
                    int cell = g.Geo.Index(g.Geo.Wrap(c.Col + dc, Math.Clamp(c.Row + dr, 0, g.Geo.Height - 1)));
                    bool looked = t.Survey == 1 ? g.RockLookedAt(civ, cell) : g.KnowsSoil(civ, cell);
                    if (civ.Known[cell] && !looked && g.OwnerSeen(civ, cell) < 0 && !g.Content.World.Biomes[g.Map.Biome[cell]].IsWater
                        && g.Apply(new MoveCommand(civ.Id, u.Id, cell, false)) is null)
                    {
                        moved = true;
                    }
                }

                if (!moved && g.AiLogOn)
                {
                    g.AiLog(civ, AiLogKind.Expansion, $"{t.Name.ToLowerInvariant()} find no new ground to look at");
                }
            }
        }
    }

    private static void Settlers(SimGame g, Civ civ)
    {
        UnitType settlers = g.Content.Unit("settlers");
        int cap = 2 + (civ.Expansion / 25) + g.EraOf(civ);
        if (DepositSite(g, civ, g.Settlements[civ.Settlements[0]].Center) >= 0)
        {
            cap++; // 7A (2026-11-20): one more camp for a deposit the way on needs (tin, iron) when one is known
        }

        if (Own(g, civ, t => t.Founds) == 0 && civ.Settlements.Count < cap && NetFood(g, civ) > 0)
        {
            foreach (int sid in civ.Settlements)
            {
                Settlement s = g.Settlements[sid];
                if (s.People >= 20 && Affordable(civ, settlers.Price, 30, keepSaving: false)) // spreading goes before saving
                {
                    g.Apply(new TrainCommand(civ.Id, sid, settlers.Id));
                    break;
                }
            }
        }

        foreach (int id in civ.Units.ToArray())
        {
            Unit u = g.Units[id];
            if (u.Kind != settlers.Id || u.State != UnitState.Idle)
            {
                continue;
            }

            if (g.CannotFound(u.Cell, civ) is null)
            {
                if (g.Apply(new FoundCommand(civ.Id, u.Id)) is null && g.AiLogOn)
                {
                    g.AiLog(civ, AiLogKind.Expansion, $"founds a settlement at {g.Geo.FromIndex(u.Cell)}");
                }

                continue;
            }

            int site = BestSite(g, civ, u.Cell);
            if (site >= 0)
            {
                g.Apply(new MoveCommand(civ.Id, u.Id, site, false));
            }
        }
    }

    // A known free land hex 6â€“12 hexes away where a camp may stand, with the most food-friendly land around: the biome and
    // water, plus what our prospectors and surveyors found within 2 hexes (known soil and deposits only, 2026-11-19).
    // 7A (2026-11-20): a free place to found a camp within 2 hexes of a known deposit the way on needs and none of our
    // settlements holds, the nearest to us within 25 hexes (â’1 = none).
    private static int DepositSite(SimGame g, Civ civ, int from)
    {
        bool[] wanted = WantedDeposits(g, civ);
        if (Array.IndexOf(wanted, true) < 0)
        {
            return -1;
        }

        HexCoord c = g.Geo.FromIndex(from);
        Span<HexCoord> around = stackalloc HexCoord[HexGlobe.RangeSize(2)];
        var area = new HexCoord[HexGlobe.RangeSize(25)];
        int n = g.Geo.Range(c, 25, area);
        int best = -1;
        int bestD = int.MaxValue;
        for (int k = 0; k < n; k++)
        {
            int cell = g.Geo.Index(area[k]);
            int d = g.Map.Deposit[cell];
            if (d <= 0 || d >= wanted.Length || !wanted[d] || g.OwnerSeen(civ, cell) >= 0 || !g.KnowsDeposit(civ, cell))
            {
                continue;
            }

            int m = g.Geo.Range(area[k], 2, around);
            for (int j = 0; j < m; j++)
            {
                int site = g.Geo.Index(around[j]);
                int dist = g.Geo.Distance(c, around[j]);
                if (dist < bestD && civ.Known[site] && g.Map.Relief[site] < 2 && g.CannotFound(site, civ) is null)
                {
                    best = site;
                    bestD = dist;
                }
            }
        }

        return best;
    }

    private static int BestSite(SimGame g, Civ civ, int from)
    {
        if (DepositSite(g, civ, from) is int deposit and >= 0)
        {
            return deposit;
        }

        HexCoord c = g.Geo.FromIndex(from);
        int best = -1;
        int bestScore = int.MinValue;
        bool[] wanted = WantedDeposits(g, civ);
        var ring = new HexCoord[HexGlobe.RingSize(12)];
        for (int r = 6; r <= 12; r += 2)
        {
            int n = g.Geo.Ring(c, r, ring);
            for (int k = 0; k < n; k += 2)
            {
                int cell = g.Geo.Index(ring[k]);
                if (!civ.Known[cell] || g.CannotFound(cell, civ) is not null)
                {
                    continue;
                }

                int score = (g.Content.World.Biomes[g.Map.Biome[cell]].Food * 10) + (g.NextToWater(cell) ? 15 : 0) - r + KnownGround(g, civ, cell, wanted);
                if (score > bestScore)
                {
                    best = cell;
                    bestScore = score;
                }
            }
        }

        return best;
    }

    // What we know of the ground around a hex is worth: fertile soil, food in the soil and on it, stone, clay and ores.
    private static int KnownGround(SimGame g, Civ civ, int cell, bool[] wanted)
    {
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(2)];
        int n = g.Geo.Range(g.Geo.FromIndex(cell), 2, area);
        int score = 0;
        for (int k = 0; k < n; k++)
        {
            int c = g.Geo.Index(area[k]);
            if (g.KnowsSoil(civ, c))
            {
                score += g.Map.Fertility[c] / 40;
            }

            if (g.KnowsDeposit(civ, c))
            {
                int kind = g.Content.DepositSurvey[g.Map.Deposit[c]];
                score += kind == 1 ? 6 : 4; // rock (stone, clay, ores) or food (game, fish, grain, herds)
                score += g.Map.Deposit[c] < wanted.Length && wanted[g.Map.Deposit[c]] && g.Mineable(c) ? 60 : 0; // what the gateway waits for
            }
        }

        return score;
    }
}
