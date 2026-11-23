using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Ai;

// The AI's supply: food and inputs by caravan between settlements, food for armies in the field (split from SimAi.cs, 2026-11-22).
public static partial class SimAi
{
    // Feeds a hungry settlement from the one with the most food, with a caravan.
    internal static void Supply(SimGame g, Civ civ)
    {
        // Constructions whose porters were killed: the AI sends the missing materials again when it has them.
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            if (b.State == BuildingState.Delivery && b.Porters == 0)
            {
                g.Apply(new BringMaterialsCommand(civ.Id, b.Id));
            }
        }

        // 2026-11-22: a settlement that is hungry, or eats more than it makes with less than a minute of food, gets two
        // minutes of its eating from the settlement with the most to spare (it keeps two minutes of its own); a fixed 40 fed
        // nobody in a town of hundreds.
        int food = g.Content.Food;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            bool low = s.Rate[food] < 0 && s.Stock[food] < s.Eating;
            if (!s.Hungry && !low)
            {
                continue;
            }

            Settlement? rich = null;
            long richSpare = 0;
            foreach (int o in civ.Settlements)
            {
                Settlement r = g.Settlements[o];
                long spare = r.Stock[food] - Math.Max(60 * SimContent.One, 2 * r.Eating);
                if (r != s && spare > richSpare)
                {
                    rich = r;
                    richSpare = spare;
                }
            }

            long need = Math.Max(40 * SimContent.One, (2 * s.Eating) - s.Stock[food]);
            if (rich is not null && !Carrying(g, civ, s.Id))
            {
                g.Apply(new SendCaravanCommand(civ.Id, rich.Id, s.Id, food, Math.Min(need, richSpare)));
            }
        }

        Inputs(g, civ);
    }

    // AI goods (2026-11-20): goods lie in each settlement. A working building whose inputs run short at home gets them by
    // caravan from the settlement that has the most to spare: one caravan per good and target at a time, at most two new
    // caravans a thought.
    private static void Inputs(SimGame g, Civ civ)
    {
        if (civ.Settlements.Count < 2)
        {
            return;
        }

        InputRoutes(g, civ);

        const long Low = 10 * SimContent.One;
        const long Keep = 20 * SimContent.One;
        const long Trip = 30 * SimContent.One;
        int sent = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            BuildingType t = g.Content.Buildings[b.Type];
            if (b.State != BuildingState.Active || t.Workers == 0 || b.Workers == 0)
            {
                continue;
            }

            Settlement s = g.Settlements[b.Settlement];
            for (int i = 0; i < t.Consumes.Length; i++)
            {
                if (t.Consumes[i] <= 0 || s.Stock[i] >= Low || Carrying(g, civ, s.Id, i) || HasInputRoute(g, civ, s.Id, i))
                {
                    continue;
                }

                Settlement? rich = null;
                foreach (int o in civ.Settlements)
                {
                    Settlement r = g.Settlements[o];
                    if (r != s && !r.Hungry && r.Stock[i] > Keep + (5 * SimContent.One) && (rich is null || r.Stock[i] > rich.Stock[i]))
                    {
                        rich = r;
                    }
                }

                if (rich is null || g.Apply(new SendCaravanCommand(civ.Id, rich.Id, s.Id, i, Math.Min(Trip, rich.Stock[i] - Keep))) is not null)
                {
                    continue;
                }

                if (++sent >= 2)
                {
                    return;
                }
            }
        }
    }

    // Known issue 13 (2026-11-23): single caravans came too late for steady workshops (41% stood without inputs after 120
    // minutes). A settlement whose workshops use more of a good than it makes gets a standing route from our settlement with
    // the most to spare of it; a route whose source has nothing to spare any more is ended, so a better source can take over.
    private static void InputRoutes(SimGame g, Civ civ)
    {
        SimContent c = g.Content;
        int goods = c.Goods.Count;
        int n = civ.Settlements.Count;
        var use = new long[n * goods]; // per settlement (by index in civ.Settlements) and good: full-staff use a minute
        for (int k = 0; k < n; k++)
        {
            int sid = civ.Settlements[k];
            foreach (int id in civ.Buildings)
            {
                Building b = g.Buildings[id];
                BuildingType t = c.Buildings[b.Type];
                if (b.Settlement != sid || b.State != BuildingState.Active || !t.HasInputs)
                {
                    continue;
                }

                for (int i = 0; i < t.Consumes.Length; i++)
                {
                    use[(k * goods) + i] += t.Consumes[i] * t.Workers;
                }
            }
        }

        int routes = 0;
        for (int r = 0; r < g.Routes.Count; r++)
        {
            TradeRoute route = g.Routes[r];
            if (route.Civ != civ.Id || route.Ended) // the AI opens routes only for inputs
            {
                continue;
            }

            int from = civ.Settlements.IndexOf(route.From);
            long keep = from < 0 ? 0 : use[(from * goods) + route.Good] * c.AiInputRouteKeepMinutes;
            if (from >= 0 && route.Caravan < 0 && g.Settlements[route.From].Stock[route.Good] - Math.Max(keep, route.KeepAtSource) < 10 * SimContent.One
                && g.Settlements[route.From].Rate[route.Good] <= 0)
            {
                g.Apply(new EndRouteCommand(civ.Id, r)); // nothing to spare there any more
                continue;
            }

            routes++;
        }

        int most = n * c.AiInputRoutesPerSettlement;
        for (int k = 0; k < n && routes < most; k++)
        {
            Settlement s = g.Settlements[civ.Settlements[k]];
            for (int i = 0; i < goods && routes < most; i++)
            {
                long need = use[(k * goods) + i];
                // A starved workshop uses nothing, so the rate looks fine: what counts is the store against the full use.
                if (need <= 0 || s.Stock[i] >= need * c.AiInputRouteTargetMinutes || HasInputRoute(g, civ, s.Id, i))
                {
                    continue;
                }

                int best = -1;
                long bestSpare = 0;
                for (int o = 0; o < n; o++)
                {
                    Settlement src = g.Settlements[civ.Settlements[o]];
                    long spare = src.Stock[i] - (use[(o * goods) + i] * c.AiInputRouteKeepMinutes);
                    if (o != k && !src.Hungry && (src.Rate[i] > 0 || spare >= 20 * SimContent.One) && spare > bestSpare)
                    {
                        best = o;
                        bestSpare = spare;
                    }
                }

                if (best < 0)
                {
                    continue;
                }

                int src2 = civ.Settlements[best];
                long trip = Math.Max(10 * SimContent.One, need * c.AiInputRouteTripMinutes);
                long keepAtSource = use[(best * goods) + i] * c.AiInputRouteKeepMinutes;
                if (g.Apply(new RouteCommand(civ.Id, src2, s.Id, i, trip, null, keepAtSource, need * c.AiInputRouteTargetMinutes)) is null)
                {
                    routes++;
                }
            }
        }
    }

    private static bool HasInputRoute(SimGame g, Civ civ, int to, int good)
    {
        foreach (TradeRoute r in g.Routes)
        {
            if (r.Civ == civ.Id && !r.Ended && r.To == to && r.Good == good)
            {
                return true;
            }
        }

        return false;
    }

    // Supply (2026-11-20): an army unit in the field with less than a minute and a half of provisions gets a food caravan from
    // home when home can spare it; a hungry one with no food coming walks home.
    private static void FieldSupply(SimGame g, Civ civ)
    {
        foreach (int id in civ.Units.ToArray())
        {
            Unit u = g.Units[id];
            if (!g.Content.Units[u.Kind].Army || u.State is UnitState.Gone or UnitState.Training || u.Aboard >= 0 || g.InSupply(u))
            {
                continue;
            }

            long perMinute = (long)u.People * g.Content.FoodPerPersonPerMinute;
            if (u.Provisions >= perMinute * 3 / 2)
            {
                continue;
            }

            if (u.SupplyNoWayTick >= 0 && g.Ticks - u.SupplyNoWayTick < g.Content.Supply.NoWayTicks)
            {
                continue; // A2 (tests 2026-11-20): no way to it a moment ago; no caravan and no march home for a while
            }

            bool coming = false;
            foreach (int cid in civ.Units)
            {
                coming |= g.Units[cid].Job == CarrierJob.ToUnit && g.Units[cid].Target == u.Id && g.Units[cid].State != UnitState.Gone;
            }

            if (coming)
            {
                continue;
            }

            long want = g.ProvisionsMax(u) - u.Provisions;
            if ((uint)u.Home < (uint)g.Settlements.Count && g.Settlements[u.Home].Civ == civ.Id && g.Settlements[u.Home].Stock[g.Content.Food] > (2 * want) + (40 * SimContent.One)
                && g.Apply(new SupplyUnitCommand(civ.Id, u.Home, u.Id, want)) is null)
            {
                continue;
            }

            if (u.Hunger > 0 && (uint)u.Home < (uint)g.Settlements.Count)
            {
                g.Apply(new MoveCommand(civ.Id, u.Id, g.Settlements[u.Home].Center, false));
            }
        }
    }

    private static bool Carrying(SimGame g, Civ civ, int to, int good)
    {
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (u.Job == CarrierJob.ToSettlement && u.Target == to && u.Cargo is { } load && load[good] > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Carrying(SimGame g, Civ civ, int to)
    {
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (u.Job == CarrierJob.ToSettlement && u.Target == to)
            {
                return true;
            }
        }

        return false;
    }
}
