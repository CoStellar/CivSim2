using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Send a caravan with goods from one of our settlements to another.</summary>
public sealed record SendCaravanCommand(int Civ, int From, int To, int Good, long Amount) : SimCommand(Civ);

/// <summary>Set up a standing route: a caravan keeps carrying the good from one settlement to the other.</summary>
public sealed record RouteCommand(int Civ, int From, int To, int Good, long Amount, int[]? Path = null) : SimCommand(Civ);

/// <summary>Send porters with the materials a construction is still missing (after porters were killed on the way).</summary>
public sealed record BringMaterialsCommand(int Civ, int Building) : SimCommand(Civ);

/// <summary>End a standing route (by its index in <see cref="SimGame.Routes"/>).</summary>
public sealed record EndRouteCommand(int Civ, int Route) : SimCommand(Civ);

/// <summary>
/// Goods move on the map (decision of 2026-11-17): porters carry construction materials from the nearest storehouse to the
/// site, caravans carry goods between settlements and to trade partners. Carriers are units: they walk, can be seen and can
/// be attacked. Killed carriers lose their load (decision of 2026-11-17, late evening); the owner is told and decides what next.
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

    /// <summary>What a construction still lacks: its price minus what is at the site and on the way.</summary>
    public long[] Missing(Building b)
    {
        ArgumentNullException.ThrowIfNull(b);
        long[] price = Content.Buildings[b.Type].Price;
        var missing = new long[price.Length];
        for (int g = 0; g < price.Length; g++)
        {
            missing[g] = Math.Max(0, price[g] - b.Delivered[g] - b.OnTheWay[g]);
        }

        return missing;
    }

    // Takes the missing materials from the settlement's store and sends them to the site: one group of porters per good, so
    // they arrive one after another and the work can start with the first. False when the store lacks them.
    private bool SendMaterials(Building b)
    {
        Settlement s = Settlements[b.Settlement];
        long[] missing = Missing(b);
        if (!CanAfford(s.Stock, missing, 100))
        {
            return false;
        }

        int from = NearestStore(s, b.Cell);
        for (int g = 0; g < missing.Length; g++)
        {
            if (missing[g] <= 0)
            {
                continue;
            }

            var load = new long[missing.Length];
            load[g] = missing[g];
            Unit porters = NewCarrier(Civs[b.Civ], Content.Porters, s, from, load, 100);
            porters.Job = CarrierJob.ToBuilding;
            porters.Target = b.Id;
            b.OnTheWay[g] += missing[g];
            b.Porters++;
            PackFood(porters, s, b.Cell);
            Walk(porters, b.Cell);
        }

        return true;
    }

    private string? BringMaterials(Civ civ, int id) => WithOwn(civ, id, b =>
    {
        if (b.State is not (BuildingState.Delivery or BuildingState.Construction or BuildingState.ConstructionPaused))
        {
            return "Only a construction needs materials.";
        }

        long[] missing = Missing(b);
        if (Array.TrueForAll(missing, x => x <= 0))
        {
            return "Nothing is missing.";
        }

        if (!SendMaterials(b))
        {
            return $"{Settlements[b.Settlement].Name} lacks {CargoText(missing)}.";
        }

        SyncTotals(civ);
        return null;
    });

    // Materials reached the site: the work may go on as far as they allow.
    private void Deliver(Building b, long[] cargo)
    {
        long[] price = Content.Buildings[b.Type].Price;
        long need = 0;
        long have = 0;
        for (int g = 0; g < cargo.Length; g++)
        {
            b.OnTheWay[g] = Math.Max(0, b.OnTheWay[g] - cargo[g]);
            b.Delivered[g] = Math.Min(price[g], b.Delivered[g] + cargo[g]);
            need += price[g];
            have += b.Delivered[g];
        }

        int total = Math.Max(1, Content.Buildings[b.Type].BuildTicks);
        if (b.State == BuildingState.Construction)
        {
            b.RemainingTicks = total - WorkDone(b);
        }

        b.WorkCap = need <= 0 || have >= need ? total : (int)(total * have / need);
        if (b.State is BuildingState.Delivery or BuildingState.Construction)
        {
            Work(b);
        }
    }

    /// <summary>Ticks of building work done so far.</summary>
    public int WorkDone(Building b)
    {
        ArgumentNullException.ThrowIfNull(b);
        int total = Math.Max(1, Content.Buildings[b.Type].BuildTicks);
        return b.State == BuildingState.Construction ? (int)Math.Clamp(b.WorkCap - (b.FinishTick - Ticks), 0, total) : total - b.RemainingTicks;
    }

    // Starts (or goes on with) the work up to what the materials at the site allow; otherwise waits for materials.
    private void Work(Building b)
    {
        int done = Math.Max(1, Content.Buildings[b.Type].BuildTicks) - b.RemainingTicks;
        if (done < b.WorkCap)
        {
            b.State = BuildingState.Construction;
            b.FinishTick = Ticks + (b.WorkCap - done);
            Schedule(b);
        }
        else
        {
            b.State = BuildingState.Delivery;
        }
    }

    // A carrier unit leaves a settlement with goods from its store; its people stop working until they are back.
    // Goods between settlements go in caravans once the people has the wheel, horseback riding and the horses a caravan takes;
    // before that porters carry them (decision of 2026-11-18). The horses come back with the caravan.
    private Unit NewCarrier(Civ civ, int kind, Settlement home, int fromCell, long[] goods, int percent)
    {
        long horses = 0;
        if (kind == Content.Caravans)
        {
            UnitType caravan = Content.Units[Content.Caravans];
            int h = GoodId("horses");
            if (Knows(civ, caravan) && (caravan.HorsesPerCaravan == 0 || (h >= 0 && home.Stock[h] >= caravan.HorsesPerCaravan)))
            {
                horses = caravan.HorsesPerCaravan;
                if (h >= 0)
                {
                    home.Stock[h] -= horses;
                }
            }
            else
            {
                kind = Content.Porters;
            }
        }

        var cargo = new long[goods.Length];
        long total = 0;
        for (int g = 0; g < goods.Length; g++)
        {
            cargo[g] = goods[g] * percent / 100;
            home.Stock[g] -= cargo[g];
            total += cargo[g];
        }

        UnitType t = Content.Units[kind];
        long each = t.CargoPerPerson + (Bonus(civ, EffectKind.Cargo, t.Key, t.Groups) * SimContent.One); // baskets: Carrying
        int people = (int)Math.Clamp((total + each - 1) / Math.Max(1, each), 1, Math.Max(1, home.People - 1));
        home.People = Math.Max(1, home.People - people);
        var u = new Unit { Id = Units.Count, Civ = civ.Id, Kind = kind, People = people, Cell = fromCell, State = UnitState.Idle, Cargo = cargo, Home = home.Id, ReturnCell = fromCell, Horses = horses };
        Units.Add(u);
        civ.Units.Add(u.Id);
        civ.Dirty = true;
        Watch(u);
        return u;
    }

    // Carriers take food for the whole way there and back; it is part of the cost of sending them (taken from home, as far as
    // there is food: they never wait for it).
    private void PackFood(Unit u, Settlement home, int toCell)
    {
        long hexes = Geo.Distance(Geo.FromIndex(u.Cell), Geo.FromIndex(toCell));
        long food = Math.Min(home.Stock[Content.Food], u.People * hexes * 2 * Content.TripFoodPer10Hexes / 10);
        home.Stock[Content.Food] -= food;
    }

    // Starts a unit walking to a hex; arrives at once when already there. A caravan with no way there turns back with its load.
    private void Walk(Unit u, int cell)
    {
        bool way = FindPath(u.Cell, cell, u.Path, Content.Units[u.Kind].Naval, u.Civ);
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
                b.Porters = Math.Max(0, b.Porters - 1);
                if (b.Civ == u.Civ && b.State is BuildingState.Delivery or BuildingState.Construction or BuildingState.ConstructionPaused)
                {
                    Deliver(b, u.Cargo!);
                    Array.Clear(u.Cargo!);
                }

                GoHome(u); // with the load when the site is gone
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

                if (u.Horses > 0 && GoodId("horses") is int h and >= 0)
                {
                    home.Stock[h] += u.Horses;
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
        if (u.Route >= 0 && Routes[u.Route].Path.Count > 0)
        {
            var back = new List<int>(Routes[u.Route].Path);
            back.Reverse();
            WalkVia(u, back, u.ReturnCell);
            return;
        }

        Walk(u, u.ReturnCell);
    }

    // Walks a carrier along a drawn path, then the shortest way from its end to the destination. Falls back to the
    // shortest way when the drawing cannot be walked any more (a building on it, land changed hands…).
    private void WalkVia(Unit u, IReadOnlyList<int> drawn, int dest)
    {
        var tail = new List<int>();
        if (DrawnPath(u.Cell, drawn, false, u.Path, u.Civ) is not null || !FindPath(u.Path.Count > 0 ? u.Path[^1] : u.Cell, dest, tail, false, u.Civ))
        {
            Walk(u, dest);
            return;
        }

        u.Path.AddRange(tail);
        StartWalk(u, false);
        if (u.State == UnitState.Idle)
        {
            Arrive(u);
        }
    }

    private void Gone(Unit u)
    {
        if (u.Job == CarrierJob.ToBuilding && u.Target >= 0)
        {
            // Killed on the way: the site will not get this load. Nothing is sent again by itself.
            Building b = Buildings[u.Target];
            b.Porters = Math.Max(0, b.Porters - 1);
            for (int g = 0; g < b.OnTheWay.Length && u.Cargo is not null; g++)
            {
                b.OnTheWay[g] = Math.Max(0, b.OnTheWay[g] - u.Cargo[g]);
            }
        }

        Unwatch(u);
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

    // Carriers were killed: their load is lost, and both sides hear of it.
    private void LoseLoad(Unit carrier, int killerCiv)
    {
        if (carrier.Cargo is null)
        {
            return;
        }

        string what = CargoText(carrier.Cargo);
        Array.Clear(carrier.Cargo);
        if (what == "nothing")
        {
            return;
        }

        string kind = Content.Units[carrier.Kind].Name.ToLowerInvariant();
        string where = carrier.Job == CarrierJob.ToBuilding
            ? $" on the way to the {Content.Buildings[Buildings[carrier.Target].Type].Name.ToLowerInvariant()}; it needs the missing materials brought again"
            : "";
        News.Add((carrier.Civ, $"Our {kind} were killed by {Civs[killerCiv].Name} and {what} is lost{where}."));
        News.Add((killerCiv, $"We destroyed {Civs[carrier.Civ].Name}' {kind}; their {what} is lost."));
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

        int dest = NearestStore(Settlements[to], s.Center);
        PackFood(c, s, dest);
        if (route >= 0 && Routes[route].Path.Count > 0)
        {
            WalkVia(c, Routes[route].Path, dest);
        }
        else
        {
            Walk(c, dest);
        }

        return null;
    }

    private string? AddRoute(Civ civ, int from, int to, int good, long amount, int[]? drawn = null)
    {
        if ((uint)from >= (uint)Settlements.Count || (uint)to >= (uint)Settlements.Count || Settlements[from].Civ != civ.Id || Settlements[to].Civ != civ.Id || from == to)
        {
            return "A route goes between two of our settlements.";
        }

        if ((uint)good >= (uint)Content.Goods.Count || amount <= 0)
        {
            return "Choose a good and an amount.";
        }

        var route = new TradeRoute { Civ = civ.Id, From = from, To = to, Good = good, Amount = amount };
        if (drawn is { Length: > 0 })
        {
            var check = new List<int>();
            string? why = DrawnPath(drawn[0], drawn, false, check, civ.Id);
            if (why is not null)
            {
                return why;
            }

            route.Path.AddRange(drawn);
        }

        Routes.Add(route);
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

    // Once a second per people: idle routes send a caravan. (Lost materials are not sent again by themselves.)
    private void LogisticsStep(Civ civ)
    {
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
        PackFood(ca, sa, NearestStore(sb, sa.Center));
        Walk(ca, NearestStore(sb, sa.Center));

        var back = new long[Content.Goods.Count];
        back[wantGood] = wantAmount;
        TakeFromCiv(b, sb, wantGood, wantAmount);
        Unit cb = NewCarrier(b, Content.Caravans, sb, NearestStore(sb, sa.Center), back, 100);
        cb.Job = CarrierJob.ToSettlement;
        cb.Target = sa.Id;
        PackFood(cb, sb, NearestStore(sa, sb.Center));
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
