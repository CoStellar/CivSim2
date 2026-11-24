using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Ai;

/// <summary>
/// What the AI does with the Stone Age to Middle Ages rules of 2026-11-18: it lays food by before winter and makes clothing,
/// builds the support buildings where they have something to raise, keeps its settlements gathering, and draws works: ditches
/// to its farms, palisades and walls around settlements at war, aqueducts to its towns. Everything it does is a command a
/// person could give; it looks only at its own people.
/// </summary>
public static partial class SimAi
{
    // Minutes of eating a people wants in store when winter comes (winter lasts two).
    private const int WinterStoreMinutes = 3;

    /// <summary>True when winter is near or here and the food in store would not last it out.</summary>
    internal static bool LeanTimes(SimGame g, Civ civ)
    {
        bool near = g.IsWinter || g.TicksToSeasonChange <= SimContent.TicksPerMinute;
        if (!near || g.Content.SeasonCycleTicks <= 0)
        {
            return false;
        }

        // As much as the stores can hold, at most: a fuller store than the room would never be reached.
        long need = Math.Min((long)civ.People * g.Content.FoodPerPersonPerMinute * WinterStoreMinutes, civ.Capacity[g.Content.Food] * 8 / 10);
        return civ.Stock[g.Content.Food] < need;
    }

    // Before and in winter: hunters and fishers (winter hurts them least), then any food maker.
    private static bool WinterFood(SimGame g, Civ civ)
    {
        if (Building(g, civ, FoodBuildings) || Count(g, civ, FoodBuildings) > (civ.People / 5) + 1)
        {
            return false;
        }

        foreach (string k in new[] { "hunters_lodge", "fishing_hut", "pasture", "farm", "gatherers_hut" })
        {
            if (TryBuild(g, civ, k))
            {
                return true;
            }
        }

        return false;
    }

    // The first hungry settlement without a food maker under way builds one in its own land.
    private static bool HungryCamp(SimGame g, Civ civ)
    {
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            // Hungry now, or eating more than it makes with less than two minutes of food left.
            // Less than two rations (residents and units) left.
            long ration = g.RationOf(s).Food;
            bool running = (s.Rate[g.Content.Food] < 0 || s.RationShort > 0) && s.Stock[g.Content.Food] < 2 * ration;
            if (!s.Hungry && !running)
            {
                continue;
            }

            bool rising = false;
            foreach (int id in civ.Buildings)
            {
                Building b = g.Buildings[id];
                rising |= b.Settlement == s.Id && b.State is BuildingState.Delivery or BuildingState.Construction && Array.IndexOf(FoodBuildings, g.Content.Buildings[b.Type].Key) >= 0;
            }

            if (rising)
            {
                continue;
            }

            // Tests 2026-11-23: a food maker here with free jobs gets the workers first (priority) instead of another empty one:
            // new huts got no workers while older woodcutters took them all (47 huts, 2 with workers).
            bool freeFoodJobs = false;
            foreach (int id in civ.Buildings)
            {
                Building b = g.Buildings[id];
                BuildingType bt = g.Content.Buildings[b.Type];
                if (b.Settlement != s.Id || b.State != BuildingState.Active || Array.IndexOf(FoodBuildings, bt.Key) < 0 || b.Workers >= bt.Workers)
                {
                    continue;
                }

                freeFoodJobs = true;
                if (!b.Priority)
                {
                    g.Apply(new PriorityCommand(civ.Id, id, true));
                    return true;
                }
            }

            if (freeFoodJobs)
            {
                continue; // every food job here is first in line already: more huts would stand empty
            }

            foreach (string k in new[] { "gatherers_hut", "hunters_lodge", "fishing_hut", "farm", "pasture" })
            {
                if (TryBuildIn(g, civ, s, k))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Clothing for the winter and the support buildings, each where it has something to raise.
    private static bool Care(SimGame g, Civ civ)
    {
        if (Under(g, civ) >= 1 + civ.Settlements.Count)
        {
            return false;
        }

        // Tools first when there are none: farms, tailors and mines need them.
        int tools = g.Content.GoodId("tools");
        if (civ.Stock[tools] < g.Content.AiAmount.LowTools && civ.Rate[tools] <= 0 && Count(g, civ, ["toolmaker", "blacksmith", "steelworks"]) == 0
            && Has(g, civ, "quarry") && TryBuild(g, civ, "toolmaker"))
        {
            return true;
        }

        int clothing = g.Content.Clothing;
        long sets = clothing >= 0 ? civ.Stock[clothing] : 0;
        bool winterSoon = g.IsWinter || g.TicksToSeasonChange <= 3 * SimContent.TicksPerMinute;
        if (clothing >= 0 && winterSoon && sets < (long)civ.People * SimContent.One && Count(g, civ, ["tailor"]) < civ.Settlements.Count && Has(g, civ, "hunters_lodge")
            && TryBuild(g, civ, "tailor"))
        {
            return true;
        }

        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if ((InSettlement(g, civ, s, "hunt") >= 1 && Here(g, civ, s, "herbalist") == 0 && TryBuildIn(g, civ, s, "herbalist"))
                || (InSettlement(g, civ, s, "quarry") >= 1 && Here(g, civ, s, "mason") == 0 && TryBuildIn(g, civ, s, "mason"))
                || (InSettlement(g, civ, s, "farm") >= 2 && Here(g, civ, s, "mill") == 0 && TryBuildIn(g, civ, s, "mill"))
                || (InSettlement(g, civ, s, "craft") >= 3 && Here(g, civ, s, "guildhall") == 0 && TryBuildIn(g, civ, s, "guildhall"))
                || (s.People >= g.Content.AiCarePeople["bathhouse"] && Here(g, civ, s, "bathhouse") == 0 && TryBuildIn(g, civ, s, "bathhouse"))
                || (s.People >= g.Content.AiCarePeople["school"] && Here(g, civ, s, "school") == 0 && TryBuildIn(g, civ, s, "school"))
                || (s.People >= g.Content.AiCarePeople["academy"] && Here(g, civ, s, "academy") == 0 && TryBuildIn(g, civ, s, "academy"))
                || (s.People >= g.Content.AiCarePeople["university"] && Here(g, civ, s, "university") == 0 && TryBuildIn(g, civ, s, "university")))
            {
                return true;
            }

            if (!s.Gathering)
            {
                g.Apply(new GatherCommand(civ.Id, s.Id, true));
            }
        }

        return false;
    }

    // Finished or rising buildings of a group in one settlement.
    private static int InSettlement(SimGame g, Civ civ, Settlement s, string group)
    {
        int n = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            n += b.Settlement == s.Id && Array.IndexOf(g.Content.Buildings[b.Type].Groups, group) >= 0 ? 1 : 0;
        }

        return n;
    }

    private static int Here(SimGame g, Civ civ, Settlement s, string key)
    {
        int n = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            n += b.Settlement == s.Id && g.Content.Buildings[b.Type].Key == key ? 1 : 0;
        }

        return n;
    }

    // Builds in one settlement, on the free hex nearest its centre.
    private static bool TryBuildIn(SimGame g, Civ civ, Settlement s, string key)
    {
        int id = Key(g, key);
        if (id < 0)
        {
            return false;
        }

        BuildingType t = g.Content.Buildings[id];
        if (!SimGame.Knows(civ, t) || !SimGame.CanAfford(s.Stock, t.Price, 100))
        {
            return false;
        }

        int best = -1;
        int bestD = int.MaxValue;
        foreach (int cell in s.Cells)
        {
            int d = g.Geo.Distance(g.Geo.FromIndex(cell), g.Geo.FromIndex(s.Center));
            if (d < bestD && g.CannotBuild(civ, cell, t, checkPrice: false) is null)
            {
                best = cell;
                bestD = d;
            }
        }

        return best >= 0 && g.Apply(new BuildCommand(civ.Id, best, id)) is null;
    }

    // One work at a time: a ditch to an unwatered farm, a palisade or wall ring around a settlement at war, an aqueduct to a town.
    private static void Works(SimGame g, Civ civ)
    {
        foreach (RoadProject p in g.RoadProjects)
        {
            if (p.Civ == civ.Id && !p.Over)
            {
                return;
            }
        }

        bool atWar = false;
        foreach (Civ other in g.Civs)
        {
            atWar |= other.Id != civ.Id && !other.Dead && g.AtWar(civ.Id, other.Id);
        }

        WorkKind? ditch = g.Content.Work("ditch");
        WorkKind? palisade = g.Content.Work("palisade");
        WorkKind? wall = g.Content.Work("wall");
        WorkKind? aqueduct = g.Content.Work("aqueduct");
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (atWar && wall is not null && civ.KnownTech[wall.Tech] && Ring(g, civ, s, wall, 1))
            {
                return;
            }

            if (atWar && palisade is not null && civ.KnownTech[palisade.Tech] && Ring(g, civ, s, palisade, 2))
            {
                return;
            }

            if (ditch is not null && civ.KnownTech[ditch.Tech])
            {
                foreach (int id in civ.Buildings)
                {
                    Building b = g.Buildings[id];
                    if (b.Settlement == s.Id && b.State == BuildingState.Active && Array.IndexOf(g.Content.Buildings[b.Type].Groups, "farm") >= 0
                        && (g.WorksAt[b.Cell] & SimGame.WateredBit) == 0 && FromWater(g, civ, b.Cell, ditch) is { } line
                        && g.Apply(new BuildWorkCommand(civ.Id, [.. line], ditch.Id)) is null)
                    {
                        return;
                    }
                }
            }

            if (aqueduct is not null && civ.KnownTech[aqueduct.Tech] && s.Level >= 2 && !s.Aqueduct
                && FromWater(g, civ, s.Center, aqueduct) is { } water && g.Apply(new BuildWorkCommand(civ.Id, [.. water], aqueduct.Id)) is null)
            {
                return;
            }
        }
    }

    // Draws a ring of a work at a distance around the settlement's centre, where it is missing (true when work began).
    private static bool Ring(SimGame g, Civ civ, Settlement s, WorkKind kind, int radius)
    {
        var ring = new HexCoord[HexGlobe.RingSize(radius)];
        int n = g.Geo.Ring(g.Geo.FromIndex(s.Center), radius, ring);
        var cells = new List<int>();
        for (int k = 0; k < n; k++)
        {
            int c = g.Geo.Index(ring[k]);
            if (g.OwnerAt[c] == civ.Id && (g.WorksAt[c] & kind.Bit) == 0 && !g.Content.World.Biomes[g.Map.Biome[c]].IsWater)
            {
                cells.Add(c);
            }
        }

        // A ring is drawn hex after neighbouring hex: build each missing hex as its own short stroke.
        foreach (int c in cells)
        {
            if (g.Apply(new BuildWorkCommand(civ.Id, [c], kind.Id)) is null)
            {
                return true;
            }
        }

        return false;
    }

    // The shortest line of our own land from a hex next to water to the goal, within the work's reach (null = none).
    private static List<int>? FromWater(SimGame g, Civ civ, int goal, WorkKind kind)
    {
        var from = new Dictionary<int, int> { [goal] = -1 };
        var queue = new Queue<(int Cell, int Steps)>();
        queue.Enqueue((goal, 1));
        var nb = new HexCoord[6];
        while (queue.Count > 0)
        {
            (int cell, int steps) = queue.Dequeue();
            if (g.NextToWater(cell))
            {
                var line = new List<int>();
                for (int c = cell; c >= 0; c = from[c])
                {
                    line.Add(c);
                }

                return line; // from the water to the goal
            }

            if (steps >= kind.FromWater)
            {
                continue;
            }

            g.Geo.GetNeighbors(g.Geo.FromIndex(cell), nb);
            foreach (HexCoord h in nb)
            {
                int n = g.Geo.Index(h);
                if (!from.ContainsKey(n) && g.OwnerAt[n] == civ.Id && !g.Content.World.Biomes[g.Map.Biome[n]].IsWater)
                {
                    from[n] = cell;
                    queue.Enqueue((n, steps + 1));
                }
            }
        }

        return null;
    }
}
