using System;
using System.Collections.Generic;
using CivSim.Core.Numerics;
using CivSim.Core.State;

namespace CivSim.Core.Units;

/// <summary>What a unit is doing (stage 6 PR 3a). Values are part of the save format: only append.</summary>
public enum UnitOrder : byte
{
    /// <summary>Stands where it is.</summary>
    Idle = 0,

    /// <summary>Walks to <see cref="Unit.Target"/> and stands there.</summary>
    Move = 1,

    /// <summary>Settlers: walk to <see cref="Unit.Target"/> and found a camp there.</summary>
    Found = 2,

    /// <summary>Scouts: walk out in <see cref="Unit.Direction"/> until the trip is done.</summary>
    Scout = 3,

    /// <summary>Walks back to its home settlement and joins it.</summary>
    Return = 4,
}

/// <summary>
/// A group of people walking over the map in the ticks of the live game (stage 6 PR 3a, ADR 0024): settlers, scouts and,
/// later, warriors and carriers. Its people left their settlement: they do not work there while they walk.
/// </summary>
public sealed class Unit
{
    /// <summary>Its id (unique in a game, never reused).</summary>
    public long Id { get; set; }

    /// <summary>What kind of unit.</summary>
    public UnitKind Kind { get; init; }

    /// <summary>The civilization it belongs to.</summary>
    public CivId Owner { get; set; }

    /// <summary>The world it walks on.</summary>
    public WorldId World { get; init; }

    /// <summary>The cell it stands on (index on its world).</summary>
    public int Cell { get; set; }

    /// <summary>The cell it walks to, or -1.</summary>
    public int Target { get; set; } = -1;

    /// <summary>What it is doing.</summary>
    public UnitOrder Order { get; set; }

    /// <summary>Id of the settlement it came from (it goes back there), or -1.</summary>
    public int Home { get; set; } = -1;

    /// <summary>People by stratum (index = stratum).</summary>
    public int[] People { get; set; } = [];

    /// <summary>Skilled people by skill (index = skill).</summary>
    public int[] Skilled { get; set; } = [];

    /// <summary>Trainees by skill (index = skill).</summary>
    public int[] Trainees { get; set; } = [];

    /// <summary>Goods it carries (index = good).</summary>
    public Amount[] Cargo { get; set; } = [];

    /// <summary>Ticks spent on the way to the next cell.</summary>
    public int Progress { get; set; }

    /// <summary>The next cell of its way, or -1 when it has none yet.</summary>
    public int Next { get; set; } = -1;

    /// <summary>Scouts: the direction it walks out in (0–5).</summary>
    public int Direction { get; set; }

    /// <summary>Scouts: the cells it walked out over, in order (it comes back over them).</summary>
    public List<int> Trail { get; set; } = [];

    /// <summary>True once it is gone (founded its camp, joined its home, died out); removed at the end of the tick.</summary>
    public bool Gone { get; set; }

    /// <summary>All its people.</summary>
    public int TotalPeople()
    {
        int total = 0;
        foreach (int p in People)
        {
            total += p;
        }

        return total;
    }
}

/// <summary>All units of a game (stage 6 PR 3a), in the order they set out.</summary>
public sealed class UnitBook
{
    private readonly List<Unit> _units = [];

    /// <summary>The units, oldest first.</summary>
    public IReadOnlyList<Unit> All => _units;

    /// <summary>The id the next unit gets.</summary>
    public long NextId { get; private set; }

    /// <summary>How many units walk.</summary>
    public int Count => _units.Count;

    /// <summary>Adds <paramref name="unit"/> with the next id and returns it.</summary>
    public Unit Add(Unit unit)
    {
        ArgumentNullException.ThrowIfNull(unit);
        unit.Id = NextId++;
        _units.Add(unit);
        return unit;
    }

    /// <summary>The unit with <paramref name="id"/>, or null.</summary>
    public Unit? Find(long id)
    {
        foreach (Unit u in _units)
        {
            if (u.Id == id && !u.Gone)
            {
                return u;
            }
        }

        return null;
    }

    /// <summary>True when a unit of <paramref name="kind"/> from settlement <paramref name="home"/> is out.</summary>
    public bool OutFrom(UnitKind kind, int home)
    {
        foreach (Unit u in _units)
        {
            if (!u.Gone && u.Kind == kind && u.Home == home)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Removes the units that are gone; returns how many.</summary>
    public int RemoveGone() => _units.RemoveAll(u => u.Gone);

    /// <summary>Restores a saved unit with its id (loading).</summary>
    public void Restore(Unit unit, long nextId)
    {
        ArgumentNullException.ThrowIfNull(unit);
        _units.Add(unit);
        NextId = Math.Max(NextId, nextId);
    }

    /// <summary>Sets the next id (loading a game without units).</summary>
    public void SetNextIdForLoad(long nextId) => NextId = Math.Max(NextId, nextId);
}
