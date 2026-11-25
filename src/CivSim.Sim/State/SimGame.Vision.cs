using System;
using System.Collections.Generic;
using CivSim.Core.Map;

namespace CivSim.Sim.State;

/// <summary>What a civilization remembers of a settlement it no longer sees.</summary>
/// <summary>A settlement as last seen; People, Guard and DefensePercent are what the AI judges its defence by (A9, tests 2026-11-20).</summary>
/// <remarks>SeenAt: the tick it was last seen (tests 2026-11-23, P1: the AI attacks only what it has looked at lately).</remarks>
public sealed record SettlementMemory(int Settlement, int Civ, string Name, int Level, int Center, int People = 0, int Guard = 0, int DefensePercent = 100, long SeenAt = 0);

/// <summary>What a civilization remembers of a building it no longer sees.</summary>
public sealed record BuildingMemory(int Building, int Type, int Civ, BuildingState State, int Cell, int Slots = 15);

/// <summary>
/// The fog of war (decisions of 2026-11-18): every hex is unknown, remembered or seen now. Each civilization counts its observers
/// per hex: its units (by their sight) and settlements (their land and a few hexes beyond). A hex is seen while its count is
/// above zero. When the last observer leaves, the people remembers what was there (buildings, settlements, owner) and sees it
/// grey until it looks again, like Age of Empires. Moving one unit changes only the hexes of its old and new sight.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>True when <paramref name="civ"/> sees the hex now (always true while the start is being chosen).</summary>
    public bool Visible(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return Picking ? civ.Known[cell] : civ.Vision[cell] > 0;
    }

    /// <summary>Owner of a hex as the civilization knows it: live where seen, remembered elsewhere.</summary>
    public short OwnerSeen(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return Visible(civ, cell) ? OwnerAt[cell] : civ.SeenOwner[cell];
    }

    /// <summary>Puts a unit on a hex at once, with its sight (tests and scripted scenes).</summary>
    internal void Place(Unit u, int cell)
    {
        u.Cell = cell;
        Watch(u);
    }

    /// <summary>Works of a hex as the civilization knows them (now where it looks, else as last seen).</summary>
    public ushort WorksSeen(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return Visible(civ, cell) ? WorksView(cell) : civ.SeenWorks[cell];
    }

    /// <summary>Road level of a hex as the civilization knows it: live where seen, remembered elsewhere.</summary>
    public byte RoadSeen(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return Visible(civ, cell) ? RoadAt[cell] : civ.SeenRoad[cell];
    }

    // Units standing on each hex (those out in the world: not training, not gone), kept with their sight: who is where is
    // then a look at a few hexes instead of a walk over every unit ever made.
    private readonly Dictionary<int, List<int>> _unitsAt = [];

    /// <summary>Units out in the world on a hex (empty when none).</summary>
    public IReadOnlyList<int> UnitsAt(int cell) => _unitsAt.TryGetValue(cell, out List<int>? list) ? list : [];

    // A unit starts or moves on: its sight goes with it.
    private void Watch(Unit u)
    {
        _contactsTick = -1; // who touches whom changed
        Unwatch(u);
        if (u.State is UnitState.Gone or UnitState.Training)
        {
            return;
        }

        u.WatchCell = u.Cell;
        u.WatchRadius = Content.Units[u.Kind].Sight;
        See(Civs[u.Civ], u.WatchCell, u.WatchRadius, +1);
        if (!_unitsAt.TryGetValue(u.Cell, out List<int>? here))
        {
            here = [];
            _unitsAt[u.Cell] = here;
        }

        here.Add(u.Id);
    }

    // The unit stops seeing (gone, or about to see from elsewhere).
    private void Unwatch(Unit u)
    {
        if (u.WatchCell >= 0)
        {
            See(Civs[u.Civ], u.WatchCell, u.WatchRadius, -1);
            if (_unitsAt.TryGetValue(u.WatchCell, out List<int>? here))
            {
                here.Remove(u.Id);
                if (here.Count == 0)
                {
                    _unitsAt.Remove(u.WatchCell);
                }
            }

            u.WatchCell = -1;
        }
    }

    /// <summary>Units out in the world, people by people in id order of their lists (training and gone ones left out).</summary>
    public IEnumerable<Unit> ActiveUnits()
    {
        foreach (Civ c in Civs)
        {
            foreach (int id in c.Units)
            {
                Unit u = Units[id];
                if (u.State is not (UnitState.Gone or UnitState.Training) && u.Aboard < 0)
                {
                    yield return u;
                }
            }
        }
    }

    // A settlement sees its land and a few hexes beyond; called when founded, raised or taken.
    private void Watch(Settlement s)
    {
        Unwatch(s);
        if (s.Abandoned)
        {
            return;
        }

        s.WatchCiv = s.Civ;
        s.WatchRadius = Content.Levels[s.Level].Radius + Content.SettlementSightBeyond;
        See(Civs[s.Civ], s.Center, s.WatchRadius, +1);
    }

    private void Unwatch(Settlement s)
    {
        if (s.WatchCiv >= 0)
        {
            See(Civs[s.WatchCiv], s.Center, s.WatchRadius, -1);
            s.WatchCiv = -1;
        }
    }

    // Adds or removes one observer over a disc of hexes.
    private void See(Civ civ, int center, int radius, int delta)
    {
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(radius)];
        int n = Geo.Range(Geo.FromIndex(center), radius, area);
        for (int k = 0; k < n; k++)
        {
            int i = Geo.Index(area[k]);
            int before = civ.Vision[i];
            int after = before + delta;
            civ.Vision[i] = (short)Math.Max(0, after);
            if (before == 0 && after > 0)
            {
                // Seen now: explored for good; the memory of the hex gives way to what is there.
                civ.Known[i] = true;
                Forget(civ, i);
                civ.NewlyKnown.Add(Map.Chunks.ChunkOfIndex(i));
            }
            else if (before > 0 && after <= 0)
            {
                Remember(civ, i);
                civ.NewlyKnown.Add(Map.Chunks.ChunkOfIndex(i));
            }
        }
    }

    // The last observer left the hex: keep what was there.
    private void Remember(Civ civ, int cell)
    {
        civ.SeenOwner[cell] = OwnerAt[cell];
        civ.SeenRoad[cell] = RoadAt[cell];
        civ.SeenWorks[cell] = WorksView(cell);
        // One memory per place: key cell × 4 + first place of the building.
        for (int k = 0; k < SlotsPerHex; k++)
        {
            civ.SeenBuildings.Remove((cell * SlotsPerHex) + k);
        }

        foreach (int b in BuildingsOn(cell))
        {
            Building x = Buildings[b];
            if (x.State != BuildingState.Removed)
            {
                civ.SeenBuildings[(cell * SlotsPerHex) + System.Numerics.BitOperations.TrailingZeroCount(x.Slots)] = new BuildingMemory(x.Id, x.Type, x.Civ, x.State, cell, x.Slots);
            }
        }

        int sid = SettlementAt[cell];
        if (sid >= 0 && Settlements[sid].Center == cell && !Settlements[sid].Abandoned)
        {
            Settlement s = Settlements[sid];
            civ.SeenSettlements[cell] = new SettlementMemory(s.Id, s.Civ, s.Name, s.Level, cell, s.People, s.Guard, Defense(s), Ticks);
        }
        else
        {
            civ.SeenSettlements.Remove(cell);
        }
    }

    private static void Forget(Civ civ, int cell)
    {
        for (int k = 0; k < SlotsPerHex; k++)
        {
            civ.SeenBuildings.Remove((cell * SlotsPerHex) + k);
        }

        civ.SeenSettlements.Remove(cell);
    }

    /// <summary>Remembered buildings of a civilization, in the order of their hexes.</summary>
    public IEnumerable<BuildingMemory> RememberedBuildings(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.SeenBuildings.Values;
    }

    /// <summary>Remembered settlements of a civilization.</summary>
    public IEnumerable<SettlementMemory> RememberedSettlements(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.SeenSettlements.Values;
    }
}
