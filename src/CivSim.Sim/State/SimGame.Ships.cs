using System;
using System.Collections.Generic;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>A land unit next to one of our transport ships goes aboard.</summary>
public sealed record BoardCommand(int Civ, int Unit, int Ship) : SimCommand(Civ);

/// <summary>A transport ship puts the units aboard on a land hex next to it.</summary>
public sealed record LandCommand(int Civ, int Ship, int Cell) : SimCommand(Civ);

/// <summary>
/// Ships (decision of 2026-11-18): transport ships and warships carry armies, trade ships carry goods. A land unit next to a
/// transport goes aboard: it leaves the map (no sight, no fighting) and moves with the ship until it lands on a hex next to it.
/// A ship that sinks takes those aboard with it. Barter between peoples with no way on land goes by trade ships between their
/// harbours.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>People aboard a ship.</summary>
    public int Aboard(Unit ship)
    {
        ArgumentNullException.ThrowIfNull(ship);
        int n = 0;
        foreach (int id in Civs[ship.Civ].Units)
        {
            n += Units[id].Aboard == ship.Id ? Units[id].People : 0;
        }

        return n;
    }

    private string? Board(Civ civ, int id, int shipId)
    {
        if ((uint)id >= (uint)Units.Count || (uint)shipId >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[shipId].Civ != civ.Id)
        {
            return "Not our units.";
        }

        Unit u = Units[id];
        Unit ship = Units[shipId];
        UnitType st = Content.Units[ship.Kind];
        UnitType ut = Content.Units[u.Kind];
        if (!st.CarriesArmies || ship.State is UnitState.Gone or UnitState.Training)
        {
            return "Only transport ships and warships carry armies.";
        }

        if (ut.Naval || ut.System || u.Aboard >= 0 || u.State is UnitState.Gone or UnitState.Training)
        {
            return "Only land units can go aboard.";
        }

        if (Distance(u.Cell, ship.Cell) > 1)
        {
            return "The ship must be next to the unit.";
        }

        if (Aboard(ship) + u.People > st.Carries)
        {
            return $"The ship carries at most {st.Carries} people ({Aboard(ship)} aboard).";
        }

        Unwatch(u);
        u.Path.Clear();
        u.State = UnitState.Idle;
        u.TargetUnit = -1;
        u.TargetSettlement = -1;
        u.TargetWall = -1;
        u.Aboard = ship.Id;
        u.Cell = ship.Cell;
        News.Add((civ.Id, $"Our {ut.Name.ToLowerInvariant()} went aboard the {st.Name.ToLowerInvariant()}."));
        return null;
    }

    private string? Land(Civ civ, int shipId, int cell)
    {
        if ((uint)shipId >= (uint)Units.Count || Units[shipId].Civ != civ.Id || (uint)cell >= (uint)Geo.CellCount)
        {
            return "Not our ship.";
        }

        Unit ship = Units[shipId];
        if (Distance(ship.Cell, cell) > 1 || Cost(cell, false) == 0 || WalledAgainst(civ.Id, cell))
        {
            return "Land on a free land hex next to the ship.";
        }

        int landed = 0;
        foreach (int id in new List<int>(civ.Units))
        {
            Unit u = Units[id];
            if (u.Aboard == ship.Id)
            {
                u.Aboard = -1;
                u.Cell = cell;
                Watch(u);
                landed++;
            }
        }

        return landed == 0 ? "Nobody is aboard." : null;
    }

    // A ship gone takes those aboard with it.
    private void Sink(Unit ship)
    {
        if (!Content.Units[ship.Kind].CarriesArmies)
        {
            return;
        }

        foreach (int id in new List<int>(Civs[ship.Civ].Units))
        {
            Unit u = Units[id];
            if (u.Aboard == ship.Id)
            {
                u.Aboard = -1;
                u.State = UnitState.Gone;
                Civs[u.Civ].Units.Remove(u.Id);
                News.Add((u.Civ, $"Our {Content.Units[u.Kind].Name.ToLowerInvariant()} went down with their ship."));
            }
        }
    }

    // The settlement of a people with a working harbour nearest to a hex (null = none).
    private Settlement? HarbourNear(Civ civ, int cell)
    {
        Settlement? best = null;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            if (HasBuilding(s, "harbour") && WaterNear(s.Center) >= 0 && (best is null || Distance(s.Center, cell) < Distance(best.Center, cell)))
            {
                best = s;
            }
        }

        return best;
    }

    // True when trade ships of a can sail between its harbour and one of b's.
    private bool SeaWay(Civ a, Civ b)
    {
        int sailing = Content.Units.Count > 0 ? TradeShipKind() : -1;
        if (sailing < 0 || !Knows(a, Content.Units[sailing]))
        {
            return false;
        }

        Settlement? from = a.Settlements.Count > 0 ? HarbourNear(a, Settlements[b.Settlements.Count > 0 ? b.Settlements[0] : a.Settlements[0]].Center) : null;
        Settlement? to = from is null ? null : HarbourNear(b, from.Center);
        return from is not null && to is not null && FindPath(WaterNear(from.Center), WaterNear(to.Center), _probe, naval: true);
    }

    private int TradeShipKind()
    {
        for (int k = 0; k < Content.Units.Count; k++)
        {
            if (Content.Units[k].Key == "trade_ship")
            {
                return k;
            }
        }

        return -1;
    }

    // Goods of a barter that go by sea: a trade ship from our harbour to theirs.
    private void ShipGoods(Civ from, Civ to, int good, long amount)
    {
        Settlement? home = HarbourNear(from, Settlements[to.Settlements[0]].Center);
        Settlement? there = home is null ? null : HarbourNear(to, home.Center);
        if (home is null || there is null)
        {
            return;
        }

        var load = new long[Content.Goods.Count];
        load[good] = amount;
        TakeFromCiv(from, home, good, amount);
        int water = WaterNear(home.Center);
        Unit ship = NewCarrier(from, TradeShipKind(), home, water, load, 100);
        ship.Job = CarrierJob.ToSettlement;
        ship.Target = there.Id;
        Walk(ship, WaterNear(there.Center));
    }
}
