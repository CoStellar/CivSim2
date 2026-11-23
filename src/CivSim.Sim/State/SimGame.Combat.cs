using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Split a unit: <paramref name="People"/> of it become a new unit of the same kind and arms on a hex beside it.</summary>
public sealed record SplitCommand(int Civ, int Unit, int People) : SimCommand(Civ);

/// <summary>Give a unit standing on our land the best weapons and armour its settlement holds (its old ones go back).</summary>
public sealed record RefitCommand(int Civ, int Unit) : SimCommand(Civ);

/// <summary>Set how a unit fights on its own.</summary>
public sealed record StanceCommand(int Civ, int Unit, Stance Stance) : SimCommand(Civ);

/// <summary>Hexes a unit guards (empty: none): it goes at enemies on them and comes back.</summary>
public sealed record GuardCommand(int Civ, int Unit, int[] Cells) : SimCommand(Civ);

/// <summary>
/// Send units to a hex in formation: melee in a line on it across the way, ranged a row behind, cavalry at the ends of the line,
/// siege engines behind the archers. <paramref name="Facing"/> is the direction the line faces (−1 = the way they walk).
/// </summary>
public sealed record FormationCommand(int Civ, int[] Units, int Cell, int Facing = -1) : SimCommand(Civ);

/// <summary>
/// Battle (Patryk, 2026-11-19, claude/walka-propozycja.md). Every step of battle (combat.stepTicks) each army unit strikes: melee
/// units every enemy next to them (at most frontPerContact of their people at each), ranged units one enemy in reach, only while
/// standing. All strikes of a step are counted first and taken together, so the order of units does not matter. Weapons and
/// armour come from the stores when a unit trains. Walking next to an enemy stops a unit (zone of control); leaving an enemy
/// costs a strike from it; cavalry after a run strikes harder once.
/// </summary>
public sealed partial class SimGame
{
    private readonly List<Unit> _fighters = [];
    private readonly Dictionary<int, List<int>> _contacts = [];
    private readonly Stack<List<int>> _spareLists = [];
    private long[] _pending = [];
    private int[] _pendingBy = [];
    private int[] _pendingKind = [];
    private long _contactsTick = -1;
    private long[] _lastFight = [];

    /// <summary>Tick a unit of the people last struck or was struck (−1 = never).</summary>
    public long LastFightTick(int civ) => (uint)civ < (uint)_lastFight.Length ? _lastFight[civ] : -1;

    private void Fought(int civ)
    {
        if (_lastFight.Length < Civs.Count)
        {
            int old = _lastFight.Length;
            Array.Resize(ref _lastFight, Civs.Count);
            Array.Fill(_lastFight, -1, old, Civs.Count - old);
        }

        _lastFight[civ] = Ticks;
    }

    /// <summary>True for units that take weapons and armour from the stores (soldiers on foot and horse).</summary>
    public static bool Geared(UnitType t) => t.MaxPeople > 0;

    /// <summary>Weapon multiplier of a unit in percent.</summary>
    public int WeaponPercent(Unit u) => Level(Content.Combat.Weapon, u.Weapon);

    /// <summary>Armour divisor of a unit in percent.</summary>
    public int ArmourPercent(Unit u) => Level(Content.Combat.Armour, u.Armour);

    private static int Level(int[] table, int level) => table.Length == 0 ? 100 : table[Math.Clamp(level, 0, table.Length - 1)];

    /// <summary>The best weapon and armour levels a store holds enough of for <paramref name="people"/> (armour 0 when shock).</summary>
    public (byte Weapon, byte Armour) BestGear(long[] stock, int people, bool shock)
    {
        ArgumentNullException.ThrowIfNull(stock);
        return (Best(Content.WeaponGoods), shock ? (byte)0 : Best(Content.ArmourGoods));

        byte Best(int[] goods)
        {
            for (int level = goods.Length - 1; level >= 1; level--)
            {
                if (goods[level] >= 0 && stock[goods[level]] >= people * SimContent.One)
                {
                    return (byte)level;
                }
            }

            return 0;
        }
    }

    // Takes the arms for the unit from the store (the levels BestGear found).
    private void TakeGear(long[] stock, Unit u, bool shock)
    {
        (u.Weapon, u.Armour) = BestGear(stock, u.People, shock);
        if (u.Weapon > 0)
        {
            stock[Content.WeaponGoods[u.Weapon]] -= u.People * SimContent.One;
        }

        if (u.Armour > 0)
        {
            stock[Content.ArmourGoods[u.Armour]] -= u.People * SimContent.One;
        }
    }

    private void ReturnGear(long[] stock, Unit u)
    {
        if (u.Weapon > 0 && Content.WeaponGoods[u.Weapon] >= 0)
        {
            stock[Content.WeaponGoods[u.Weapon]] += u.People * SimContent.One;
        }

        if (u.Armour > 0 && Content.ArmourGoods[u.Armour] >= 0)
        {
            stock[Content.ArmourGoods[u.Armour]] += u.People * SimContent.One;
        }

        u.Weapon = 0;
        u.Armour = 0;
    }

    private string? Refit(Civ civ, int id)
    {
        if (OwnReady(civ, id) is not Unit u || !Geared(Content.Units[u.Kind]))
        {
            return "Not a soldier of ours, or not ready.";
        }

        int sid = SettlementAt[u.Cell];
        if (sid < 0 || Settlements[sid].Civ != civ.Id || u.State != UnitState.Idle)
        {
            return "Units take arms standing on our own land.";
        }

        Settlement s = Settlements[sid];
        (byte w, byte a) = (u.Weapon, u.Armour);
        ReturnGear(s.Stock, u);
        TakeGear(s.Stock, u, shock: false);
        if (u.Weapon < w || u.Armour < a)
        {
            // Never worse: give the new ones back and keep the old.
            ReturnGear(s.Stock, u);
            (u.Weapon, u.Armour) = (w, a);
            if (w > 0)
            {
                s.Stock[Content.WeaponGoods[w]] -= u.People * SimContent.One;
            }

            if (a > 0)
            {
                s.Stock[Content.ArmourGoods[a]] -= u.People * SimContent.One;
            }
        }

        SyncTotals(civ);
        return u.Weapon == w && u.Armour == a ? $"{s.Name} holds no better arms for {u.People}." : null;
    }

    private string? Split(Civ civ, int id, int people)
    {
        if (OwnReady(civ, id) is not Unit u || !Content.Units[u.Kind].Army || Content.Units[u.Kind].Naval)
        {
            return "Not a soldier of ours, or not ready.";
        }

        UnitType t = Content.Units[u.Kind];
        int min = Math.Max(1, t.MinPeople);
        if (people < min || u.People - people < min)
        {
            return $"Both parts need at least {min} people.";
        }

        if (ArmyUnits(civ) >= ArmyLimit(civ))
        {
            return $"Our army is at its limit of {ArmyLimit(civ)} units.";
        }

        int cell = FreeBeside(u.Cell, u.Civ);
        var part = new Unit
        {
            Id = Units.Count, Civ = civ.Id, Kind = u.Kind, People = people, Size = people, Cell = u.Cell, State = UnitState.Idle, Home = u.Home,
            Weapon = u.Weapon, Armour = u.Armour, Stance = u.Stance, Hunger = u.Hunger,
            Injured = u.Injured * people / u.People,
            Provisions = u.Provisions * people / u.People,
        };
        u.Injured -= part.Injured;
        u.Provisions -= part.Provisions;
        u.People -= people;
        u.Size = u.People;
        Units.Add(part);
        civ.Units.Add(part.Id);
        Watch(part);
        civ.Dirty = true;
        if (cell != u.Cell && FindPath(part.Cell, cell, part.Path, false, part.Civ) && part.Path.Count > 0)
        {
            part.PathIndex = 0;
            part.State = UnitState.Moving;
            part.NextTick = Ticks + StepTicks(part, part.Path[0]);
            ScheduleUnit(part, UnitStep);
        }

        return null;
    }

    // A walkable hex next to the cell with no army unit on it (the cell itself when there is none).
    private int FreeBeside(int cell, int civ)
    {
        HexCoord c = Geo.FromIndex(cell);
        for (int d = 0; d < 6; d++)
        {
            int n = Geo.Index(Geo.Neighbor(c, (HexDirection)d));
            if (n != cell && CellCost(n) > 0 && !Content.World.Biomes[Map.Biome[n]].IsWater && !ArmyOn(n) && Crossable(civ, cell, d))
            {
                return n;
            }
        }

        return cell;
    }

    // One army unit to a hex (Patryk, 2026-11-19): a unit that stops where another stands steps to a free hex beside (walking
    // through is allowed).
    private void MakeRoom(Unit u)
    {
        UnitType t = Content.Units[u.Kind];
        if (!t.Army || t.Naval || u.State != UnitState.Idle)
        {
            return;
        }

        foreach (int id in UnitsAt(u.Cell))
        {
            if (id != u.Id && Units[id].State == UnitState.Idle && Content.Units[Units[id].Kind].Army && !Content.Units[Units[id].Kind].Naval)
            {
                int free = FreeBeside(u.Cell, u.Civ);
                if (free != u.Cell)
                {
                    Go(u, free);
                }

                return;
            }
        }
    }

    private bool ArmyOn(int cell)
    {
        foreach (int id in UnitsAt(cell))
        {
            if (Content.Units[Units[id].Kind].Army)
            {
                return true;
            }
        }

        return false;
    }

    private Unit? OwnReady(Civ civ, int id) =>
        (uint)id < (uint)Units.Count && Units[id].Civ == civ.Id && Units[id].State is UnitState.Idle or UnitState.Moving && Units[id].Aboard < 0
            ? Units[id] : null;

    private string? SetStance(Civ civ, int id, Stance stance)
    {
        if (!Enum.IsDefined(stance))
        {
            return "Unknown stance."; // B8 (tests 2026-11-23)
        }

        if (OwnReady(civ, id) is not Unit u || !Content.Units[u.Kind].Army)
        {
            return "Not a soldier of ours, or not ready.";
        }

        u.Stance = stance;
        return null;
    }

    private string? SetGuard(Civ civ, int id, int[] cells)
    {
        if (OwnReady(civ, id) is not Unit u || !Content.Units[u.Kind].Army || Content.Units[u.Kind].Naval)
        {
            return "Not a soldier of ours, or not ready.";
        }

        u.Guard.Clear();
        foreach (int c in cells ?? [])
        {
            if ((uint)c < (uint)Geo.CellCount && !u.Guard.Contains(c) && u.Guard.Count < 64)
            {
                u.Guard.Add(c);
            }
        }

        return null;
    }

    /// <summary>The hexes of a formation of <paramref name="count"/> places in a row, facing <paramref name="facing"/> (0–5).</summary>
    public List<int> FormationRow(int center, int facing, int count)
    {
        // A straight line across a hex direction zigzags: one step 60° to the side, one 120° (their sum is at right angles).
        var row = new List<int> { center };
        HexCoord right = Geo.FromIndex(center);
        HexCoord left = right;
        for (int k = 0; row.Count < count; k++)
        {
            right = Geo.Neighbor(right, (HexDirection)((facing + (k % 2 == 0 ? 2 : 1)) % 6));
            row.Add(Geo.Index(right));
            if (row.Count < count)
            {
                left = Geo.Neighbor(left, (HexDirection)((facing + (k % 2 == 0 ? 4 : 5)) % 6));
                row.Add(Geo.Index(left));
            }
        }

        return row;
    }

    private string? Formation(Civ civ, int[] ids, int cell, int facing)
    {
        var units = new List<Unit>();
        foreach (int id in ids ?? [])
        {
            if (OwnReady(civ, id) is Unit u && Content.Units[u.Kind].Army && !Content.Units[u.Kind].Naval && !units.Contains(u))
            {
                units.Add(u);
            }
        }

        if (units.Count == 0 || (uint)cell >= (uint)Geo.CellCount)
        {
            return "No soldiers to send.";
        }

        foreach ((Unit u, int goal) in FormationGoals(units, cell, facing))
        {
            Move(civ, u.Id, goal, hidden: false);
        }

        return null;
    }

    /// <summary>
    /// Places of a formation: melee in a line on the cell across the facing, cavalry at both ends, archers a row behind,
    /// engines two rows behind. <paramref name="facing"/> −1: the way from the first unit to the cell.
    /// </summary>
    public List<(Unit Unit, int Cell)> FormationGoals(IReadOnlyList<Unit> units, int cell, int facing)
    {
        ArgumentNullException.ThrowIfNull(units);
        var melee = new List<Unit>();
        var ranged = new List<Unit>();
        var riders = new List<Unit>();
        var engines = new List<Unit>();
        foreach (Unit u in units)
        {
            UnitType t = Content.Units[u.Kind];
            (Array.IndexOf(t.Groups, "siege") >= 0 ? engines : t.Cavalry ? riders : t.Ranged ? ranged : melee).Add(u);
        }

        if (facing is < 0 or > 5)
        {
            // Face the way they walk: the hex behind the goal nearest to where the first of them stands.
            int from = (melee.Count > 0 ? melee : ranged.Count > 0 ? ranged : riders.Count > 0 ? riders : engines)[0].Cell;
            HexCoord g = Geo.FromIndex(cell);
            int best = int.MaxValue;
            for (int d = 0; d < 6; d++)
            {
                int dist = Distance(Geo.Index(Geo.Neighbor(g, (HexDirection)((d + 3) % 6))), from);
                if (dist < best)
                {
                    best = dist;
                    facing = d;
                }
            }
        }

        int back = (facing + 3) % 6;
        int Behind(int c, int rows)
        {
            HexCoord x = Geo.FromIndex(c);
            for (int k = 0; k < rows; k++)
            {
                x = Geo.Neighbor(x, (HexDirection)back);
            }

            return Geo.Index(x);
        }

        var goals = new List<(Unit, int)>();
        int frontCount = melee.Count + riders.Count;
        List<int> front = FormationRow(cell, facing, frontCount);
        Add(melee, front, 0);
        Add(riders, front, melee.Count);
        Add(ranged, FormationRow(Behind(cell, frontCount > 0 ? 1 : 0), facing, ranged.Count), 0);
        Add(engines, FormationRow(Behind(cell, (frontCount > 0 ? 1 : 0) + (ranged.Count > 0 ? 1 : 0)), facing, engines.Count), 0);
        return goals;

        void Add(List<Unit> list, List<int> places, int first)
        {
            for (int k = 0; k < list.Count; k++)
            {
                int goal = places[Math.Min(first + k, places.Count - 1)];
                if (CellCost(goal) <= 0 || Content.World.Biomes[Map.Biome[goal]].IsWater)
                {
                    goal = cell;
                }

                goals.Add((list[k], goal));
            }
        }
    }

    /// <summary>
    /// Puts the armies of two peoples on a field near the first settlement of <paramref name="a"/>, in formation facing each other
    /// <paramref name="gap"/> hexes apart (test scenarios): the first direction from the settlement with land and no river all the way. Scouts
    /// of the first people stand two hexes ahead of its line.
    /// </summary>
    public bool PlaceField(int a, int b, int gap)
    {
        Civ ca = Civs[a];
        Civ cb = Civs[b];
        if (ca.Settlements.Count == 0)
        {
            return false;
        }

        HexCoord home = Geo.FromIndex(Settlements[ca.Settlements[0]].Center);
        int toward = -1;
        int reach = 4 + gap + 2;
        for (int d = 0; d < 6 && toward < 0; d++)
        {
            bool land = true;
            HexCoord x = home;
            for (int k = 0; k < reach && land; k++)
            {
                int from = Geo.Index(x);
                x = Geo.Neighbor(x, (HexDirection)d);
                int i = Geo.Index(x);
                land = !Content.World.Biomes[Map.Biome[i]].IsWater && CellCost(i) > 0 && Crossable(a, from, d) && Crossable(b, from, d);
            }

            toward = land ? d : -1;
        }

        if (toward < 0)
        {
            return false;
        }

        HexCoord pa = home;
        for (int k = 0; k < 4; k++)
        {
            pa = Geo.Neighbor(pa, (HexDirection)toward);
        }

        HexCoord pb = pa;
        for (int k = 0; k < gap; k++)
        {
            pb = Geo.Neighbor(pb, (HexDirection)toward);
        }

        Place(ca, Geo.Index(pa), toward);
        Place(cb, Geo.Index(pb), (toward + 3) % 6);

        // Scouts of the first people stand two hexes ahead of its line, so it sees the other army.
        HexCoord ahead = Geo.Neighbor(Geo.Neighbor(pa, (HexDirection)toward), (HexDirection)toward);
        foreach (int id in ca.Units)
        {
            Unit u = Units[id];
            if (u.State == UnitState.Idle && !Content.Units[u.Kind].Army && !Content.Units[u.Kind].System && !Content.Units[u.Kind].Founds)
            {
                this.Place(u, Geo.Index(ahead));
            }
        }

        return true;

        void Place(Civ civ, int cell, int facing)
        {
            var army = new List<Unit>();
            foreach (int id in civ.Units)
            {
                Unit u = Units[id];
                if (u.State is UnitState.Idle or UnitState.Moving && Content.Units[u.Kind].Army && !Content.Units[u.Kind].Naval && u.Aboard < 0)
                {
                    army.Add(u);
                }
            }

            if (army.Count == 0)
            {
                return;
            }

            foreach ((Unit u, int goal) in FormationGoals(army, cell, facing))
            {
                this.Place(u, goal);
            }
        }
    }

    // Starts a unit walking to the cell (no new orders: keeps its target).
    private void Go(Unit u, int cell)
    {
        if (!PathFor(u, cell) || u.Path.Count == 0)
        {
            return;
        }

        u.PathIndex = 0;
        bool wasMoving = u.State == UnitState.Moving;
        u.State = UnitState.Moving;
        if (!wasMoving)
        {
            u.NextTick = Ticks + StepTicks(u, u.Path[0]);
            ScheduleUnit(u, UnitStep);
        }
    }

    // An enemy that can be struck: out in the world, of a people at war with us.
    private bool Foe(Unit u, Unit other) =>
        other.Civ != u.Civ && other.State is UnitState.Idle or UnitState.Moving && other.Aboard < 0 && AtWar(u.Civ, other.Civ);

    // Clears the enemies counted next to each unit: they are counted once a tick.
    private void ForgetContacts()
    {
        foreach (List<int> list in _contacts.Values)
        {
            _spareLists.Push(list);
        }

        _contacts.Clear();
        _contactsTick = Ticks;
    }

    private List<int> Contacts(Unit u)
    {
        if (_contactsTick != Ticks)
        {
            ForgetContacts();
        }

        if (!_contacts.TryGetValue(u.Id, out List<int>? list))
        {
            list = _spareLists.Count > 0 ? _spareLists.Pop() : [];
            list.Clear();
            HexCoord c = Geo.FromIndex(u.Cell);
            for (int d = -1; d < 6; d++)
            {
                int cell = d < 0 ? u.Cell : Geo.Index(Geo.Neighbor(c, (HexDirection)d));
                foreach (int id in UnitsAt(cell))
                {
                    if (Foe(u, Units[id]) && !list.Contains(id))
                    {
                        list.Add(id);
                    }
                }
            }

            _contacts[u.Id] = list;
        }

        return list;
    }

    // The army enemies touching a unit: the ones whose strikes flank it.
    private int Flankers(Unit u)
    {
        int n = 0;
        foreach (int id in Contacts(u))
        {
            Unit e = Units[id];
            if (Content.Units[e.Kind].Army && !Content.Units[e.Kind].Ranged && Content.Units[e.Kind].Strength > 0)
            {
                n++;
            }
        }

        return n;
    }

    // Touched from two sides that do not touch each other: cover no longer holds.
    private bool Surrounded(Unit u)
    {
        List<int> list = Contacts(u);
        for (int i = 0; i < list.Count; i++)
        {
            for (int j = i + 1; j < list.Count; j++)
            {
                if (Distance(Units[list[i]].Cell, Units[list[j]].Cell) >= 2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Damage in hundredths of a person that <paramref name="a"/> deals <paramref name="d"/> in one step of battle with
    /// <paramref name="engaged"/> of its people, before luck; <paramref name="extraPercent"/> scales it (charges, strikes on leaving).
    /// </summary>
    public long StrikeDamage(Unit a, Unit d, int engaged, bool ranged, int extraPercent = 100)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(d);
        CombatRules r = Content.Combat;
        UnitType at = Content.Units[a.Kind];
        UnitType dt = Content.Units[d.Kind];
        Civ civ = Civs[a.Civ];

        // Drill: the kind's terrain, technologies of the people and bonuses against the target's kind.
        int drill = 100;
        if ((at.BonusBiomes.Length > 0 && at.BonusBiomes[Map.Biome[a.Cell]]) || (at.BonusReliefs.Length > 0 && at.BonusReliefs[Map.Relief[a.Cell]]))
        {
            drill += at.BonusPercent;
        }

        foreach ((int tech, int up) in at.Upgrades)
        {
            if (civ.KnownTech[tech])
            {
                drill += up;
            }
        }

        drill += Bonus(civ, EffectKind.Strength, at.Key, at.Groups);
        drill += VetStrength(a); // veterans of the kind
        foreach ((string group, int pct) in at.BonusVs)
        {
            if (Array.IndexOf(dt.Groups, group) >= 0)
            {
                drill += pct;
            }
        }

        // Armour: crossbows go through some of it; it counts more against arrows.
        int armour = Level(r.Armour, d.Armour - (ranged ? at.Pierce : 0));
        if (ranged)
        {
            armour = 100 + ((armour - 100) * (100 + r.ArmourVsRangedBonus) / 100);
        }

        int ground = 100 + Ground(a, d, ranged);

        // Siege (2026-11-20): our units on our settlement's land defend from behind its fortifications.
        int held = SettlementAt[d.Cell] is int hs and >= 0 && Settlements[hs].Civ == d.Civ ? Math.Max(100, Settlements[hs].DefensePct) : 100;
        int flank = r.Flank.Length == 0 ? 100 : r.Flank[Math.Min(Flankers(d), r.Flank.Length - 1)];
        long damage = (long)engaged * at.Strength * WeaponPercent(a) / 100;
        damage = damage * drill / 100 * FedPercent(a) / 100;
        damage = damage * 100 / Math.Max(1, armour) * 100 / held;
        damage = damage * Math.Max(10, ground) / 100 * flank / 100 * extraPercent / 100;
        return damage * 100 / r.KillDivisor;
    }

    /// <summary>The ground's change of damage from <paramref name="a"/> to <paramref name="d"/>, percent: height, cover, river, open ground.</summary>
    public int Ground(Unit a, Unit d, bool ranged)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(d);
        CombatRules r = Content.Combat;
        int relief = Math.Clamp((Map.Relief[a.Cell] - Map.Relief[d.Cell]) * r.ReliefPercent, -r.ReliefMaxPercent, r.ReliefMaxPercent);
        int height = Math.Clamp((Map.Elevation[a.Cell] - Map.Elevation[d.Cell]) / r.ElevationStep * r.ElevationPercent, -r.ElevationMaxPercent, r.ElevationMaxPercent);
        int pct = relief + height;

        // Cover of the target's hex: its people's palisade or wall, else trees; lost when struck from two sides.
        if (!Surrounded(d))
        {
            (int Ranged, int Melee) cover = (0, 0);
            ushort works = WorksAt.Length > 0 && WorksCiv[d.Cell] == d.Civ ? WorksAt[d.Cell] : (ushort)0;
            if (WallBit != 0 && (works & WallBit) != 0)
            {
                cover = r.WallCover;
            }
            else if (PalisadeBit != 0 && (works & PalisadeBit) != 0)
            {
                cover = r.PalisadeCover;
            }
            else if (Trees.Length > 0 && Trees[d.Cell] != NoTrees)
            {
                cover = r.TreeCover;
            }

            pct += ranged ? cover.Ranged : cover.Melee;
        }

        // Striking over a river with no bridge.
        if (!ranged && a.Cell != d.Cell && DirectionTo(a.Cell, d.Cell) is int dir and >= 0)
        {
            int edge = Geo.EdgeIndex(Geo.Edge(Geo.FromIndex(a.Cell), (HexDirection)dir));
            if (Map.River[edge] != 0 && (BridgeAt.Length == 0 || BridgeAt[edge] == 0))
            {
                pct += r.RiverPercent;
            }
        }

        // Open ground: roads and swamps.
        if ((RoadAt.Length > 0 && RoadAt[d.Cell] > 0) || (r.OpenBiomes.Length > 0 && r.OpenBiomes[Map.Biome[d.Cell]]))
        {
            pct += r.OpenPercent;
        }

        return pct;
    }

    private void Strike(Unit a, Unit d, int engaged, bool ranged, int extraPercent)
    {
        long damage = Luck(StrikeDamage(a, d, engaged, ranged, extraPercent));
        if (damage <= 0)
        {
            return;
        }

        if (_pending.Length < Units.Count)
        {
            Array.Resize(ref _pending, Units.Count * 2);
            Array.Resize(ref _pendingBy, Units.Count * 2);
            Array.Resize(ref _pendingKind, Units.Count * 2);
        }

        _pending[d.Id] += damage;
        _pendingBy[d.Id] = a.Civ;
        _pendingKind[d.Id] = a.Kind;
        if (!a.Struck.Contains(d.Id))
        {
            a.Struck.Add(d.Id);
        }

        a.Fighting = true;
        d.Fighting = true;
        a.FoughtTick = Ticks;
        d.FoughtTick = Ticks;
        Fought(a.Civ);
        Fought(d.Civ);
    }

    // Every step of battle: units strike, then all the damage of the step is taken at once.
    private void CombatStep()
    {
        _fighters.Clear();
        _fighters.AddRange(ActiveUnits());
        ForgetContacts(); // units walked this tick
        foreach (Unit u in _fighters)
        {
            u.Fighting = false;
            u.LastLoss = 0;
            u.Struck.Clear();
        }

        CombatRules r = Content.Combat;
        foreach (Unit u in _fighters)
        {
            UnitType t = Content.Units[u.Kind];
            if (!t.Army || t.Strength <= 0)
            {
                continue;
            }

            Unit? target = u.TargetUnit >= 0 && Foe(u, Units[u.TargetUnit]) ? Units[u.TargetUnit] : null;
            if (t.Ranged)
            {
                Unit? shot = target is not null && Distance(u.Cell, target.Cell) <= t.Range ? target
                    : u.Stance == Stance.Passive ? null : NearestEnemy(u, t.Range);
                if (shot is not null && u.State == UnitState.Idle)
                {
                    Strike(u, shot, u.People, ranged: true, 100); // archers shoot only standing
                }
            }
            else
            {
                List<int> touching = Contacts(u);
                if (touching.Count > 0 && (u.Stance != Stance.Passive || (target is not null && touching.Contains(target.Id))))
                {
                    int each = Math.Max(1, Math.Min(r.FrontPerContact, u.People / touching.Count));
                    bool charging = t.Cavalry && u.Run >= r.ChargeHexes && Ticks - u.StepTick <= 2 * r.StepTicks;
                    foreach (int id in touching)
                    {
                        Unit e = Units[id];
                        if (u.Stance == Stance.Passive && e != target)
                        {
                            continue;
                        }

                        bool charge = charging && !Content.Units[e.Kind].StopsCharge;
                        Strike(u, e, each, ranged: false, charge ? r.ChargePercent : 100);
                        if (charge)
                        {
                            u.ChargeTick = Ticks;
                        }
                    }
                }
            }

            if (u.Fighting)
            {
                u.Run = 0;
            }
        }

        // Take the damage, then orders: chase targets out of reach, guards and the aggressive go at what they see.
        foreach (Unit u in _fighters)
        {
            if (u.Id < _pending.Length && _pending[u.Id] > 0)
            {
                long damage = _pending[u.Id];
                _pending[u.Id] = 0;
                Take(u, damage, _pendingBy[u.Id], _pendingKind[u.Id]);
            }
        }

        foreach (Unit u in _fighters)
        {
            if (u.State is UnitState.Gone or UnitState.Training || !Content.Units[u.Kind].Army)
            {
                continue;
            }

            Orders(u);
        }
    }

    // Losses of a unit from the damage of a step.
    private void Take(Unit u, long damage, int byCiv, int byKind = -1)
    {
        damage = damage * (100 + VetTaken(u)) / 100; // veterans: a kind's shield wall
        u.Wounds += damage;
        int dead = (int)Math.Min(u.People, u.Wounds / SimContent.One);
        if (dead <= 0)
        {
            return;
        }

        u.Wounds -= dead * SimContent.One;
        int before = u.People;
        u.People -= dead;
        u.LastLoss += dead;
        int size = Math.Max(u.Size, before);
        foreach (int pct in Content.Combat.WarnPercents)
        {
            if (before * 100 > size * pct && u.People * 100 <= size * pct && u.People > 0)
            {
                News.Add((u.Civ, $"Our {Content.Units[u.Kind].Name.ToLowerInvariant()} are down to {u.People} of {size}."));
            }
        }

        Wound(u, dead);
        Civs[u.Civ].Dirty = true;
        if (byCiv >= 0)
        {
            Scores()[(byCiv * Civs.Count) + u.Civ] += dead;
            AddLoss(u.Civ, byCiv, dead);
            CreditNear(byCiv, byKind, u.Cell, dead);
        }

        if (u.People <= 0)
        {
            Destroyed(u, byCiv);
        }
    }

    private void Orders(Unit u)
    {
        UnitType t = Content.Units[u.Kind];
        int reach = t.Range;
        if (u.TargetUnit >= 0)
        {
            Unit e = Units[u.TargetUnit];
            if (!Foe(u, e))
            {
                u.TargetUnit = -1;
            }
            else if (Distance(u.Cell, e.Cell) > reach && !(u.Fighting && !t.Ranged))
            {
                ChaseTarget(u);
            }

            if (u.TargetUnit >= 0)
            {
                return;
            }
        }

        if (u.TargetSettlement >= 0)
        {
            Settlement target = Settlements[u.TargetSettlement];
            if (target.Abandoned || target.Civ == u.Civ || !AtWar(u.Civ, target.Civ))
            {
                u.TargetSettlement = -1;
            }
            else if (u.Fighting)
            {
                return; // enemies in the way first
            }
            else if (Distance(u.Cell, target.Center) <= reach)
            {
                Siege(u, target);
            }
            else if (u.State == UnitState.Idle)
            {
                ChaseTarget(u);
            }

            return;
        }

        if (u.Fighting || u.State != UnitState.Idle || u.Stance == Stance.Passive)
        {
            return;
        }

        // Guards: an enemy on a guarded hex; back to the guard when none.
        if (u.Guard.Count > 0)
        {
            Civ civ = Civs[u.Civ];
            foreach (int cell in u.Guard)
            {
                foreach (int id in UnitsAt(cell))
                {
                    if (Foe(u, Units[id]) && Sees(civ, Units[id]))
                    {
                        u.TargetUnit = id;
                        ChaseTarget(u);
                        return;
                    }
                }
            }

            if (!u.Guard.Contains(u.Cell))
            {
                Go(u, u.Guard[0]);
            }

            return;
        }

        if (u.Stance == Stance.Aggressive && NearestSeenEnemy(u, Math.Max(reach, t.Sight)) is Unit seen)
        {
            u.TargetUnit = seen.Id;
            ChaseTarget(u);
        }
    }

    private Unit? NearestSeenEnemy(Unit u, int radius)
    {
        Unit? best = NearestEnemy(u, radius);
        return best is not null && Sees(Civs[u.Civ], best) ? best : null;
    }

    /// <summary>True when an army enemy stands next to the cell (zone of control).</summary>
    public bool EnemyBeside(Unit u, int cell)
    {
        ArgumentNullException.ThrowIfNull(u);
        HexCoord c = Geo.FromIndex(cell);
        for (int d = -1; d < 6; d++)
        {
            int n = d < 0 ? cell : Geo.Index(Geo.Neighbor(c, (HexDirection)d));
            foreach (int id in UnitsAt(n))
            {
                Unit e = Units[id];
                if (Foe(u, e) && Content.Units[e.Kind].Army && !Content.Units[e.Kind].Naval && !e.Hidden)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // A unit steps from its hex to the next: every enemy it leaves strikes it once, unless one of ours stays beside that enemy.
    private void Disengage(Unit u, int to)
    {
        long damage = LeavingDamage(u, to, out int by);
        if (damage > 0)
        {
            u.FoughtTick = Ticks;
            Fought(u.Civ);
            Fought(by);
            Take(u, Luck(damage), by);
        }
    }

    /// <summary>
    /// Damage before luck a unit takes stepping to <paramref name="to"/>: a strike of disengagePercent (cavalry
    /// cavalryDisengagePercent) of a step from each melee enemy it leaves, unless another unit of ours stays beside that enemy.
    /// </summary>
    public long LeavingDamage(Unit u, int to, out int byCiv)
    {
        ArgumentNullException.ThrowIfNull(u);
        CombatRules r = Content.Combat;
        UnitType t = Content.Units[u.Kind];
        long damage = 0;
        byCiv = -1;
        if (t.Naval)
        {
            return 0;
        }

        HexCoord c = Geo.FromIndex(u.Cell);
        for (int d = -1; d < 6; d++)
        {
            int n = d < 0 ? u.Cell : Geo.Index(Geo.Neighbor(c, (HexDirection)d));
            foreach (int id in UnitsAt(n))
            {
                Unit e = Units[id];
                UnitType et = Content.Units[e.Kind];
                if (!Foe(u, e) || !et.Army || et.Ranged || et.Strength <= 0 || e.Stance == Stance.Passive || Distance(to, e.Cell) <= 1 || Covered(u, e))
                {
                    continue;
                }

                int pct = t.Cavalry ? r.CavalryDisengagePercent : r.DisengagePercent;
                damage += StrikeDamage(e, u, Math.Min(r.FrontPerContact, e.People), ranged: false, pct);
                byCiv = e.Civ;
                e.FoughtTick = Ticks;
            }
        }

        return damage;
    }

    // Another unit of ours stays beside the enemy: it keeps the enemy busy.
    private bool Covered(Unit leaving, Unit enemy)
    {
        HexCoord c = Geo.FromIndex(enemy.Cell);
        for (int d = -1; d < 6; d++)
        {
            int n = d < 0 ? enemy.Cell : Geo.Index(Geo.Neighbor(c, (HexDirection)d));
            foreach (int id in UnitsAt(n))
            {
                Unit f = Units[id];
                if (f != leaving && f.Civ == leaving.Civ && f.State is UnitState.Idle or UnitState.Moving && f.Aboard < 0 && Content.Units[f.Kind].Army && !Content.Units[f.Kind].Ranged)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
