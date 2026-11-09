using System;
using System.Collections.Generic;
using CivSim.Core.Numerics;
using CivSim.Core.State;

namespace CivSim.Core.Units;

/// <summary>What a unit is doing (stage 6 PR 3a, T5). Values are part of the save format: only append.</summary>
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

    /// <summary>Walks back to its home settlement and joins it (an army or carriers: stands there for its campaign).</summary>
    Return = 4,

    /// <summary>An army: walks towards <see cref="Unit.Target"/> until it is within its kind's range, and stands (stage 6 PR 3b).</summary>
    Attack = 5,

    /// <summary>An army beaten in battle: flees home; nobody can turn it (stage 6 PR 3b).</summary>
    Flee = 6,
}

/// <summary>
/// A group of people walking over the map in the ticks of the live game (stage 6 PR 3a, ADR 0024): settlers, scouts and,
/// later, warriors and carriers. Its people left their settlement: they do not work there while they walk.
/// A handle (book and slot) into the flat arrays of <see cref="UnitBook"/> (T4): copying it copies no data, and it is valid
/// until the end of the tick in which the unit is gone.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    private readonly UnitBook _book;

    internal Unit(UnitBook book, int slot)
    {
        _book = book;
        Slot = slot;
    }

    /// <summary>The slot in the book's arrays (reused after the unit is gone).</summary>
    public int Slot { get; }

    /// <summary>Its id (unique in a game, never reused).</summary>
    public long Id => _book.IdOf[Slot];

    /// <summary>Its kind (<see cref="UnitKindDefinition.Id"/> in units.json).</summary>
    public int Kind => _book.KindOf[Slot];

    /// <summary>What its kind is for.</summary>
    public UnitRole Role => _book.RoleOf[Slot];

    /// <summary>The civilization it belongs to.</summary>
    public CivId Owner { get => new(_book.OwnerOf[Slot]); set => _book.OwnerOf[Slot] = value.Value; }

    /// <summary>The world it walks on.</summary>
    public WorldId World => new(_book.WorldOf[Slot]);

    /// <summary>The cell it stands on (index on its world).</summary>
    public int Cell { get => _book.CellOf[Slot]; set => _book.CellOf[Slot] = value; }

    /// <summary>The cell it walks to, or -1.</summary>
    public int Target { get => _book.TargetOf[Slot]; set => _book.TargetOf[Slot] = value; }

    /// <summary>What it is doing.</summary>
    public UnitOrder Order { get => _book.OrderOf[Slot]; set => _book.OrderOf[Slot] = value; }

    /// <summary>Id of the settlement it came from (it goes back there), or -1.</summary>
    public int Home { get => _book.HomeOf[Slot]; set => _book.HomeOf[Slot] = value; }

    /// <summary>People by stratum (index = stratum).</summary>
    public Span<int> People => _book.PeopleOf(Slot);

    /// <summary>Skilled people by skill (index = skill).</summary>
    public Span<int> Skilled => _book.SkilledOf(Slot);

    /// <summary>Trainees by skill (index = skill).</summary>
    public Span<int> Trainees => _book.TraineesOf(Slot);

    /// <summary>Goods it carries (index = good).</summary>
    public Span<Amount> Cargo => _book.CargoOf(Slot);

    /// <summary>
    /// Ticks the step to <see cref="Next"/> costs (set while it walks; for views, to draw it between cells; not saved).
    /// </summary>
    public int StepCost { get => _book.CostOf[Slot]; set => _book.CostOf[Slot] = value; }

    /// <summary>Ticks spent on the way to the next cell.</summary>
    public int Progress { get => _book.ProgressOf[Slot]; set => _book.ProgressOf[Slot] = value; }

    /// <summary>The next cell of its way, or -1 when it has none yet.</summary>
    public int Next { get => _book.NextOf[Slot]; set => _book.NextOf[Slot] = value; }

    /// <summary>Scouts: the direction it walks out in (0–5).</summary>
    public int Direction { get => _book.DirectionOf[Slot]; set => _book.DirectionOf[Slot] = (byte)value; }

    /// <summary>Scouts: the cells it walked out over, in order (it comes back over them).</summary>
    public List<int> Trail => _book.TrailOf[Slot];

    /// <summary>
    /// Its way (T3): the cells to walk, found once for <see cref="PathGoal"/>; <see cref="PathAt"/> is the next one to step on.
    /// </summary>
    public List<int> Path => _book.PathOf[Slot];

    /// <summary>The index in <see cref="Path"/> of the next cell to step on.</summary>
    public int PathAt { get => _book.PathAtOf[Slot]; set => _book.PathAtOf[Slot] = value; }

    /// <summary>The goal <see cref="Path"/> was found for, or -1 when it has none.</summary>
    public int PathGoal { get => _book.PathGoalOf[Slot]; set => _book.PathGoalOf[Slot] = value; }

    /// <summary>Id of the campaign (episode) it belongs to, or -1 (stage 6 PR 3b: armies, gathering warriors, carriers).</summary>
    public long Episode { get => _book.EpisodeOf[Slot]; set => _book.EpisodeOf[Slot] = value; }

    /// <summary>True once it is gone (founded its camp, joined its home, died out); removed at the end of the tick.</summary>
    public bool Gone { get => _book.GoneOf[Slot]; set => _book.GoneOf[Slot] = value; }

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

    /// <summary>Forgets its way (a new goal or order): the next step finds a new one.</summary>
    public void ForgetPath()
    {
        Path.Clear();
        PathAt = 0;
        PathGoal = -1;
        Next = -1;
        Progress = 0;
    }

    /// <inheritdoc />
    public bool Equals(Unit other) => ReferenceEquals(_book, other._book) && Slot == other.Slot;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit u && Equals(u);

    /// <inheritdoc />
    public override int GetHashCode() => Slot;

    /// <summary>Same book and slot.</summary>
    public static bool operator ==(Unit a, Unit b) => a.Equals(b);

    /// <summary>Another book or slot.</summary>
    public static bool operator !=(Unit a, Unit b) => !a.Equals(b);
}

/// <summary>
/// All units of a game (stage 6 PR 3a, T4): flat arrays indexed by slot (a pool: the slot of a unit that is gone is reused,
/// with its lists), people, skills and goods of all units in one buffer each. Units are walked in the order they set out
/// (ascending id), which is also the order of the save and the hash. A tick with units allocates nothing once the pool is
/// big enough.
/// </summary>
public sealed class UnitBook
{
    private int _strata = -1;
    private int _skills = -1;
    private int _goods = -1;
    private int _slots;                 // slots ever used
    private int[] _free = [];           // slots of gone units, reused last-in first-out
    private int _freeCount;
    private int[] _order = [];          // live slots by ascending id
    private int _count;

    internal long[] IdOf = [];
    internal byte[] KindOf = [];
    internal UnitRole[] RoleOf = [];
    internal int[] OwnerOf = [];
    internal int[] WorldOf = [];
    internal int[] CellOf = [];
    internal int[] TargetOf = [];
    internal UnitOrder[] OrderOf = [];
    internal int[] HomeOf = [];
    internal int[] ProgressOf = [];
    internal int[] NextOf = [];
    internal int[] CostOf = [];
    internal byte[] DirectionOf = [];
    internal bool[] GoneOf = [];
    internal long[] EpisodeOf = [];
    internal List<int>[] TrailOf = [];
    internal List<int>[] PathOf = [];
    internal int[] PathAtOf = [];
    internal int[] PathGoalOf = [];
    private int[] _people = [];
    private int[] _skilled = [];
    private int[] _trainees = [];
    private Amount[] _cargo = [];

    /// <summary>How many units walk.</summary>
    public int Count => _count;

    /// <summary>The id the next unit gets.</summary>
    public long NextId { get; private set; }

    /// <summary>The unit at <paramref name="index"/> in the order they set out (0 = oldest).</summary>
    public Unit this[int index] =>
        (uint)index < (uint)_count ? new Unit(this, _order[index]) : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>The units, oldest first (allocates an enumerator: not for the hot loops, which use the indexer).</summary>
    public IEnumerable<Unit> All
    {
        get
        {
            for (int k = 0; k < _count; k++)
            {
                yield return new Unit(this, _order[k]);
            }
        }
    }

    /// <summary>
    /// Adds a unit with the next id: it stands on <paramref name="cell"/> with no people and no goods yet (sized by
    /// <paramref name="strata"/>, <paramref name="skills"/> and <paramref name="goods"/>, the same for every unit of a game).
    /// </summary>
    public Unit Add(UnitKindDefinition kind, CivId owner, WorldId world, int cell, int home, UnitOrder order, int strata, int skills, int goods) =>
        Place(NextId++, kind, owner, world, cell, home, order, strata, skills, goods);

    /// <summary>Restores a saved unit with its id, kind and role (loading; units arrive oldest first).</summary>
    public Unit Restore(long id, int kind, UnitRole role, CivId owner, WorldId world, int cell, int home, UnitOrder order, int strata, int skills, int goods)
    {
        if (_count > 0 && IdOf[_order[_count - 1]] >= id)
        {
            throw new InvalidOperationException("Units must be restored in ascending id order.");
        }

        NextId = Math.Max(NextId, id + 1);
        return Place(id, new UnitKindDefinition { Id = kind, Role = role }, owner, world, cell, home, order, strata, skills, goods);
    }

    /// <summary>Sets the next id (loading).</summary>
    public void SetNextIdForLoad(long nextId) => NextId = Math.Max(NextId, nextId);

    /// <summary>The unit with <paramref name="id"/> that is not gone, or null.</summary>
    public Unit? Find(long id)
    {
        int lo = 0;
        int hi = _count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            long at = IdOf[_order[mid]];
            if (at == id)
            {
                int slot = _order[mid];
                return GoneOf[slot] ? null : new Unit(this, slot);
            }

            if (at < id)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return null;
    }

    /// <summary>True when a unit of <paramref name="role"/> from settlement <paramref name="home"/> is out.</summary>
    public bool OutFrom(UnitRole role, int home)
    {
        for (int k = 0; k < _count; k++)
        {
            int s = _order[k];
            if (!GoneOf[s] && RoleOf[s] == role && HomeOf[s] == home)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Removes the units that are gone (their slots go back to the pool); returns how many.</summary>
    public int RemoveGone()
    {
        int kept = 0;
        for (int k = 0; k < _count; k++)
        {
            int s = _order[k];
            if (GoneOf[s])
            {
                _free[_freeCount++] = s;
            }
            else
            {
                _order[kept++] = s;
            }
        }

        int removed = _count - kept;
        _count = kept;
        return removed;
    }

    internal Span<int> PeopleOf(int slot) => _people.AsSpan(slot * _strata, _strata);

    internal Span<int> SkilledOf(int slot) => _skilled.AsSpan(slot * _skills, _skills);

    internal Span<int> TraineesOf(int slot) => _trainees.AsSpan(slot * _skills, _skills);

    internal Span<Amount> CargoOf(int slot) => _cargo.AsSpan(slot * _goods, _goods);

    private Unit Place(long id, UnitKindDefinition kind, CivId owner, WorldId world, int cell, int home, UnitOrder order, int strata, int skills, int goods)
    {
        ArgumentNullException.ThrowIfNull(kind);
        if (_strata < 0)
        {
            _strata = Math.Max(0, strata);
            _skills = Math.Max(0, skills);
            _goods = Math.Max(0, goods);
        }
        else if (strata != _strata || skills != _skills || goods != _goods)
        {
            throw new ArgumentException("Every unit of a game has the same strata, skills and goods.");
        }

        int slot = _freeCount > 0 ? _free[--_freeCount] : NewSlot();
        if (_order.Length <= _count)
        {
            Array.Resize(ref _order, Math.Max(16, _order.Length * 2));
        }

        _order[_count++] = slot;
        IdOf[slot] = id;
        KindOf[slot] = (byte)kind.Id;
        RoleOf[slot] = kind.Role;
        OwnerOf[slot] = owner.Value;
        WorldOf[slot] = world.Value;
        CellOf[slot] = cell;
        TargetOf[slot] = -1;
        OrderOf[slot] = order;
        HomeOf[slot] = home;
        ProgressOf[slot] = 0;
        NextOf[slot] = -1;
        CostOf[slot] = 0;
        DirectionOf[slot] = 0;
        GoneOf[slot] = false;
        EpisodeOf[slot] = -1;
        TrailOf[slot].Clear();
        PathOf[slot].Clear();
        PathAtOf[slot] = 0;
        PathGoalOf[slot] = -1;
        PeopleOf(slot).Clear();
        SkilledOf(slot).Clear();
        TraineesOf(slot).Clear();
        CargoOf(slot).Clear();
        return new Unit(this, slot);
    }

    private int NewSlot()
    {
        int slot = _slots++;
        if (slot >= IdOf.Length)
        {
            int size = Math.Max(16, IdOf.Length * 2);
            Array.Resize(ref IdOf, size);
            Array.Resize(ref KindOf, size);
            Array.Resize(ref RoleOf, size);
            Array.Resize(ref OwnerOf, size);
            Array.Resize(ref WorldOf, size);
            Array.Resize(ref CellOf, size);
            Array.Resize(ref TargetOf, size);
            Array.Resize(ref OrderOf, size);
            Array.Resize(ref HomeOf, size);
            Array.Resize(ref ProgressOf, size);
            Array.Resize(ref NextOf, size);
            Array.Resize(ref CostOf, size);
            Array.Resize(ref DirectionOf, size);
            Array.Resize(ref GoneOf, size);
            Array.Resize(ref EpisodeOf, size);
            Array.Resize(ref PathAtOf, size);
            Array.Resize(ref PathGoalOf, size);
            int old = TrailOf.Length;
            Array.Resize(ref TrailOf, size);
            Array.Resize(ref PathOf, size);
            for (int k = old; k < size; k++)
            {
                TrailOf[k] = [];
                PathOf[k] = [];
            }

            Array.Resize(ref _free, size);
            Array.Resize(ref _people, size * _strata);
            Array.Resize(ref _skilled, size * _skills);
            Array.Resize(ref _trainees, size * _skills);
            Array.Resize(ref _cargo, size * _goods);
        }

        return slot;
    }
}
