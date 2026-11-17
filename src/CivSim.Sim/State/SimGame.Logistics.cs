using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Send a caravan with goods from one of our settlements to another.</summary>
public sealed record SendCaravanCommand(int Civ, int From, int To, int Good, long Amount) : SimCommand(Civ);

/// <summary>Set up a standing route: a caravan keeps carrying the good from one settlement to the other.</summary>
public sealed record RouteCommand(int Civ, int From, int To, int Good, long Amount) : SimCommand(Civ);

/// <summary>End a standing route (by its index in <see cref="SimGame.Routes"/>).</summary>
public sealed record EndRouteCommand(int Civ, int Route) : SimCommand(Civ);

/// <summary>
/// Goods move on the map (decision of 2026-11-17): porters carry construction materials from the nearest storehouse to the
/// site, caravans carry goods between settlements and to trade partners. Carriers are units: they walk, can be seen and can
/// be attacked; whoever destroys them takes the load.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Standing caravan routes of every people.</summary>
    public List<TradeRoute> Routes { get; } = [];

    /// <summary>The storehouse of a settlement nearest to a hex, or the settlement centre when it has none.</summary>
    public int NearestStore(Settlement s, int cell)
    {
        ArgumentNullException.ThrowIfNull(s);
        int best = s.Center;
        int bestD = Geo.Distance(Geo.FromIndex(s.Center), Geo.FromIndex(cell));
        foreach (int id in Civs[s.Civ].Buildings)
        {
            Building b = Buildings[id];
            if (b.Settlement != s.Id || b.State != BuildingState.Active || Array.IndexOf(Content.Buildings[b.Type].Groups, "store") < 0)
            {
                continue;
            }

            int d = Geo.Distance(Geo.FromIndex(b.Cell), Geo.FromIndex(cell));
            if (d < bestD)
            {
                best = b.Cell;
                bestD = d;
            }
        }

        return best;
    }

    // Takes the materials from the settlement's store and sends porters with them. False when the store lacks them.
    private bool SendMaterials(Building b)
    {
        Settlement s = Settlements[b.Settlement];
        BuildingType t = Content.Buildings[b.Type];
        if (!CanAfford(s.Stock, t.Price, 100))
        {
            return false;
        }

        int from = NearestStore(s, b.Cell);
        Unit porters = NewCarrier(Civs[b.Civ], Content.Porters, s, from, t.Price, 100);
        porters.Job = CarrierJob.ToBuilding;
        porters.Target = b.Id;
        b.Porters = porters.Id;
        b.State = BuildingState.Delivery;
        Walk(porters, b.Cell);
        return true;
    }

    // A carrier unit leaves a settlement with goods from its store; its people stop working until they are back.
    private Unit NewCarrier(Civ civ, int kind, Settlement home, int fromCell, long[] goods, int percent)
    {
        var cargo = new long[goods.Length];
        long total = 0;
        for (int g = 0; g < goods.Length; g++)
        {
            cargo[g] = goods[g] * percent / 100;
            home.Stock[g] -= cargo[g];
            total += cargo[g];
        }

        UnitType t = Content.Units[kind];
        int people = (int)Math.Clamp((total + t.CargoPerPerson - 1) / Math.Max(1, t.CargoPerPerson), 1, Math.Max(1, home.People - 1));
        home.People = Math.Max(1, home.People - people);
        var u = new Unit { Id = Units.Count, Civ = civ.Id, Kind = kind, People = people, Cell = fromCell, State = UnitState.Idle, Cargo = cargo, Home = home.Id, ReturnCell = fromCell };
        Units.Add(u);
        civ.Units.Add(u.Id);
        civ.Dirty = true;
        return u;
    }

    // Starts a unit walking to a hex; arrives at once when already there. A caravan with no way there turns back with its load.
    private void Walk(Unit u, int cell)
    {
        bool way = FindPath(u.Cell, cell, u.Path, Content.Units[u.Kind].Naval);
        if (!way && u.Job == CarrierJob.ToSettlement)
        {
            u.Path.Clear();
            u.Job = CarrierJob.Returning;
            Arrive(u);
            return;
        }

        if (!way || u.Path.Count == 0)
        {
            u.Path.Clear();
            Arrive(u);
            return;
        }

        u.PathIndex = 0;
        u.State = UnitState.Moving;
        u.NextTick = Ticks + StepTicks(u, u.Path[0]);
        ScheduleUnit(u, UnitStep);
    }

    // A carrier reached the end of its walk.
    private void Arrive(Unit u)
    {
        Civ civ = Civs[u.Civ];
        switch (u.Job)
        {
            case CarrierJob.ToBuilding:
                Building b = Buildings[u.Target];
                if (b.State == BuildingState.Delivery && b.Porters == u.Id)
                {
                    // Materials are here: the building time starts now.
                    Array.Clear(u.Cargo!);
                    b.Porters = -1;
                    b.State = BuildingState.Construction;
                    b.FinishTick = Ticks + Math.Max(1, Content.Buildings[b.Type].BuildTicks);
                    Schedule(b);
                }

                GoHome(u);
                break;
            case CarrierJob.ToSettlement:
                Settlement to = Settlements[u.Target];
                long lost = Unload(to, u.Cargo!);
                string what = CargoText(u.Cargo!);
                Array.Clear(u.Cargo!);
                if (to.Civ != u.Civ)
                {
                    News.Add((to.Civ, $"A caravan of {civ.Name} delivered {what} to {to.Name}."));
                    News.Add((u.Civ, $"Our caravan delivered {what} to {to.Name} of {Civs[to.Civ].Name}."));
                }
                else if (lost > 0)
                {
                    News.Add((u.Civ, $"{to.Name} had no room for part of the caravan's load."));
                }

                GoHome(u);
                break;
            case CarrierJob.Returning:
                Settlement home = u.Home >= 0 && Settlements[u.Home].Civ == u.Civ ? Settlements[u.Home] : NearestOwn(civ, u.Cell) ?? Settlements[u.Home];
                home.People += u.People;
                if (u.Cargo is not null)
                {
                    Unload(home, u.Cargo);
                }

                Gone(u);
                break;
            default:
                u.State = UnitState.Idle;
                break;
        }
    }

    private void GoHome(Unit u)
    {
        u.Job = CarrierJob.Returning;
        Walk(u, u.ReturnCell);
    }

    private void Gone(Unit u)
    {
        if (u.Job == CarrierJob.ToBuilding && u.Target >= 0 && Buildings[u.Target].Porters == u.Id)
        {
            Buildings[u.Target].Porters = -1; // the construction will ask for its materials again
        }

        u.State = UnitState.Gone;
        Civs[u.Civ].Units.Remove(u.Id);
        Civs[u.Civ].Dirty = true;
        if (u.Route >= 0 && Routes[u.Route].Caravan == u.Id)
        {
            Routes[u.Route].Caravan = -1;
        }
    }

    // Puts goods into a settlement's store up to its room; returns what did not fit (lost).
    private static long Unload(Settlement s, long[] cargo)
    {
        long lost = 0;
        for (int g = 0; g < cargo.Length; g++)
        {
            long room = Math.Max(0, s.Capacity[g] - s.Stock[g]);
            long put = Math.Min(room, cargo[g]);
            s.Stock[g] += put;
            lost += cargo[g] - put;
        }

        return lost;
    }

    /// <summary>A load as text, e.g. "20 wood, 5 stone".</summary>
    public string CargoText(long[] cargo)
    {
        ArgumentNullException.ThrowIfNull(cargo);
        var parts = new List<string>();
        for (int g = 0; g < cargo.Length; g++)
        {
            if (cargo[g] >= SimContent.One)
            {
                parts.Add($"{cargo[g] / SimContent.One} {Content.Goods[g].Key}");
            }
        }

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    /// <summary>The settlement of a people nearest to a hex (null when it has none).</summary>
    public Settlement? NearestOwn(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        Settlement? best = null;
        int bestD = int.MaxValue;
        foreach (int sid in civ.Settlements)
        {
            int d = Geo.Distance(Geo.FromIndex(Settlements[sid].Center), Geo.FromIndex(cell));
            if (d < bestD)
            {
                best = Settlements[sid];
                bestD = d;
            }
        }

        return best;
    }

    // Whoever destroys carriers takes their load into their nearest settlement.
    private void Loot(Unit carrier, int takerCiv)
    {
        if (carrier.Cargo is null || Civs[takerCiv].Settlements.Count == 0)
        {
            return;
        }

        Settlement? s = NearestOwn(Civs[takerCiv], carrier.Cell);
        if (s is null)
        {
            return;
        }

        string what = CargoText(carrier.Cargo);
        Unload(s, carrier.Cargo);
        Array.Clear(carrier.Cargo);
        if (what != "nothing")
        {
            News.Add((takerCiv, $"We seized {what} from {Civs[carrier.Civ].Name}' {Content.Units[carrier.Kind].Name.ToLowerInvariant()}."));
            News.Add((carrier.Civ, $"Our {Content.Units[carrier.Kind].Name.ToLowerInvariant()} were robbed of {what}."));
        }
    }

    private string? SendCaravan(Civ civ, int from, int to, int good, long amount, int route = -1)
    {
        if ((uint)from >= (uint)Settlements.Count || (uint)to >= (uint)Settlements.Count || Settlements[from].Civ != civ.Id || Settlements[to].Civ != civ.Id || from == to)
        {
            return "A caravan goes between two of our settlements.";
        }

        if ((uint)good >= (uint)Content.Goods.Count || amount <= 0)
        {
            return "Choose a good and an amount.";
        }

        Settlement s = Settlements[from];
        long send = Math.Min(amount, s.Stock[good]);
        if (send < SimContent.One)
        {
            return $"{s.Name} has no {Content.Goods[good].Key} to send.";
        }

        var load = new long[Content.Goods.Count];
        load[good] = send;
        Unit c = NewCarrier(civ, Content.Caravans, s, NearestStore(s, Settlements[to].Center), load, 100);
        c.Job = CarrierJob.ToSettlement;
        c.Target = to;
        c.Route = route;
        if (route >= 0)
        {
            Routes[route].Caravan = c.Id;
        }

        Walk(c, NearestStore(Settlements[to], s.Center));
        return null;
    }

    private string? AddRoute(Civ civ, int from, int to, int good, long amount)
    {
        if ((uint)from >= (uint)Settlements.Count || (uint)to >= (uint)Settlements.Count || Settlements[from].Civ != civ.Id || Settlements[to].Civ != civ.Id || from == to)
        {
            return "A route goes between two of our settlements.";
        }

        if ((uint)good >= (uint)Content.Goods.Count || amount <= 0)
        {
            return "Choose a good and an amount.";
        }

        Routes.Add(new TradeRoute { Civ = civ.Id, From = from, To = to, Good = good, Amount = amount });
        return null;
    }

    private string? EndRoute(Civ civ, int index)
    {
        if ((uint)index >= (uint)Routes.Count || Routes[index].Civ != civ.Id || Routes[index].Ended)
        {
            return "No such route.";
        }

        Routes[index].Ended = true;
        return null;
    }

    // Once a second per people: constructions whose porters were lost ask for new materials; idle routes send a caravan.
    private void LogisticsStep(Civ civ)
    {
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            if (b.State == BuildingState.Delivery && b.Porters < 0)
            {
                SendMaterials(b);
            }
        }

        for (int r = 0; r < Routes.Count; r++)
        {
            TradeRoute route = Routes[r];
            if (route.Civ != civ.Id || route.Ended || route.Caravan >= 0)
            {
                continue;
            }

            if (Settlements[route.From].Civ != civ.Id || Settlements[route.To].Civ != civ.Id)
            {
                route.Ended = true;
                continue;
            }

            // A trip leaves when there are at least ten units to carry.
            if (Settlements[route.From].Stock[route.Good] >= 10 * SimContent.One)
            {
                SendCaravan(civ, route.From, route.To, route.Good, route.Amount, r);
            }
        }
    }

    // A trade between two peoples: each side's caravan carries its part to the nearest settlement of the other.
    private void TradeCaravans(Civ a, Civ b, int giveGood, long giveAmount, int wantGood, long wantAmount)
    {
        (Settlement sa, Settlement sb) = ClosestPair(a, b);
        var load = new long[Content.Goods.Count];
        load[giveGood] = giveAmount;
        TakeFromCiv(a, sa, giveGood, giveAmount);
        Unit ca = NewCarrier(a, Content.Caravans, sa, NearestStore(sa, sb.Center), load, 100);
        ca.Job = CarrierJob.ToSettlement;
        ca.Target = sb.Id;
        Walk(ca, NearestStore(sb, sa.Center));

        var back = new long[Content.Goods.Count];
        back[wantGood] = wantAmount;
        TakeFromCiv(b, sb, wantGood, wantAmount);
        Unit cb = NewCarrier(b, Content.Caravans, sb, NearestStore(sb, sa.Center), back, 100);
        cb.Job = CarrierJob.ToSettlement;
        cb.Target = sa.Id;
        Walk(cb, NearestStore(sa, sb.Center));
        string text = $"Caravans set out: {giveAmount / SimContent.One} {Content.Goods[giveGood].Key} of {a.Name} for {wantAmount / SimContent.One} {Content.Goods[wantGood].Key} of {b.Name}.";
        News.Add((a.Id, text));
        News.Add((b.Id, text));
        SyncTotals(a);
        SyncTotals(b);
    }

    // Gathers a good into one settlement from the people's others (for a trade the people agreed to as a whole).
    private void TakeFromCiv(Civ civ, Settlement into, int good, long amount)
    {
        long need = amount - into.Stock[good];
        foreach (int sid in civ.Settlements)
        {
            if (need <= 0)
            {
                break;
            }

            Settlement s = Settlements[sid];
            if (s == into)
            {
                continue;
            }

            long take = Math.Min(need, s.Stock[good]);
            s.Stock[good] -= take;
            into.Stock[good] += take;
            need -= take;
        }
    }

    private (Settlement A, Settlement B) ClosestPair(Civ a, Civ b)
    {
        Settlement bestA = Settlements[a.Settlements[0]];
        Settlement bestB = Settlements[b.Settlements[0]];
        int best = int.MaxValue;
        foreach (int i in a.Settlements)
        {
            foreach (int j in b.Settlements)
            {
                int d = Geo.Distance(Geo.FromIndex(Settlements[i].Center), Geo.FromIndex(Settlements[j].Center));
                if (d < best)
                {
                    best = d;
                    bestA = Settlements[i];
                    bestB = Settlements[j];
                }
            }
        }

        return (bestA, bestB);
    }
}
