using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Train a unit of <paramref name="Kind"/> in a settlement (its people leave the settlement).</summary>
/// <summary>
/// Train a unit. <paramref name="People"/>: its size for soldiers (0 = the default; within the kind's minPeople–maxPeople), the
/// price follows the size. <paramref name="Shock"/>: weapons only, no armour, trained faster (combat.shockTrainPercent).
/// </summary>
public sealed record TrainCommand(int Civ, int Settlement, int Kind, int People = 0, bool Shock = false) : SimCommand(Civ);

/// <summary>Send a unit to a hex, hidden or in the open (scouts).</summary>
public sealed record MoveCommand(int Civ, int Unit, int Cell, bool Hidden, int[]? Path = null) : SimCommand(Civ);

/// <summary>Settlers found a camp where they stand.</summary>
public sealed record FoundCommand(int Civ, int Unit) : SimCommand(Civ);

public sealed partial class SimGame
{
    private const int MaxPathNodes = 60_000;
    private int[]? _pathCost;
    private int[]? _pathFrom;
    private int[]? _pathStamp;
    private int _stamp;
    private readonly PriorityQueue<int, int> _open = new();

    /// <summary>Why settlers cannot found a camp on <paramref name="cell"/> (null = they can).</summary>
    public string? CannotFound(int cell)
    {
        if (Content.World.Biomes[Map.Biome[cell]].IsWater)
        {
            return "Settlers found camps on land.";
        }

        if (OwnerAt[cell] >= 0)
        {
            return "This land already belongs to a people.";
        }

        foreach (Settlement s in Settlements)
        {
            if (!s.Abandoned && Geo.Distance(Geo.FromIndex(s.Center), Geo.FromIndex(cell)) < Content.FoundMinDistance)
            {
                return $"Too close to {s.Name}: a camp needs {Content.FoundMinDistance} hexes from other settlements.";
            }
        }

        return null;
    }

    /// <summary>The kind a unit is now, after the technologies that change it (hunters are archers once Archery is known).</summary>
    public int Upgraded(Civ civ, int kind)
    {
        ArgumentNullException.ThrowIfNull(civ);
        for (int guard = 0; guard < 4; guard++)
        {
            int next = kind;
            foreach ((int tech, int to) in Content.Units[kind].Becomes)
            {
                if (civ.KnownTech[tech])
                {
                    next = to;
                }
            }

            if (next == kind)
            {
                break;
            }

            kind = next;
        }

        return kind;
    }

    // A technology that changes a kind of unit changes the units already in the field too.
    private void UpgradeUnits(Civ civ, int tech)
    {
        foreach (int uid in civ.Units)
        {
            Unit u = Units[uid];
            if (u.State == UnitState.Gone)
            {
                continue;
            }

            int to = Upgraded(civ, u.Kind);
            if (to != u.Kind)
            {
                News.Add((civ.Id, $"Our {Content.Units[u.Kind].Name.ToLowerInvariant()} are now {Content.Units[to].Name.ToLowerInvariant()} ({Content.Techs[tech].Name})."));
                Unwatch(u);
                u.Kind = to;
                Watch(u);
            }
        }
    }

    private string? Train(Civ civ, int sid, int kind, int size = 0, bool shock = false)
    {
        if ((uint)sid >= (uint)Settlements.Count || Settlements[sid].Civ != civ.Id || Settlements[sid].Abandoned)
        {
            return "Not your settlement.";
        }

        if ((uint)kind >= (uint)Content.Units.Count)
        {
            return "Unknown unit.";
        }

        kind = Upgraded(civ, kind);
        Settlement s = Settlements[sid];
        UnitType t = Content.Units[kind];
        if (!Knows(civ, t))
        {
            return $"{t.Name} need {NeedText(t.Tech, t.AlsoTechs)}.";
        }

        int people = size > 0 && t.MaxPeople > 0 ? Math.Clamp(size, t.MinPeople, t.MaxPeople) : t.People;
        int pricePct = people * 100 / Math.Max(1, t.People);
        shock &= Geared(t);
        if (s.People - people < 5)
        {
            return $"{s.Name} has {s.People} people; {t.Name.ToLowerInvariant()} take {people} and 5 must stay.";
        }

        if (t.System)
        {
            return "Porters and caravans are sent, not trained.";
        }

        if (t.NeedsBuilding.Length > 0 && !HasBuilding(s, t.NeedsBuilding))
        {
            return $"{t.Name} need a {Content.Building(t.NeedsBuilding).Name.ToLowerInvariant()} in {s.Name}.";
        }

        int spawn = s.Center;
        if (t.Naval)
        {
            spawn = WaterNear(s.Center);
            if (spawn < 0)
            {
                return $"{s.Name} is not by the water.";
            }
        }

        if (!CanAfford(s.Stock, t.Price, pricePct))
        {
            return $"{people} {t.Name.ToLowerInvariant()} cost {PriceText(t.Price)} × {pricePct}% in {s.Name}.";
        }

        Pay(s.Stock, t.Price, pricePct);
        s.People -= people;
        civ.Dirty = true;
        // Training halls speed their own kind (siege workshops their engines, shipyards their ships), barracks all others; each
        // by the share of its jobs filled.
        int trainPct = 100;
        foreach (int bid in civ.Buildings)
        {
            Building b = Buildings[bid];
            BuildingType bt = Content.Buildings[b.Type];
            if (b.Settlement != s.Id || b.State != BuildingState.Active || bt.TrainPercent >= 100)
            {
                continue;
            }

            bool own = t.NeedsBuilding.Length > 0 ? t.NeedsBuilding == bt.Key : !TrainsOwnKind(bt);
            if (own)
            {
                int filled = bt.Workers == 0 ? 100 : b.Workers * 100 / bt.Workers;
                trainPct = Math.Min(trainPct, 100 - ((100 - bt.TrainPercent) * filled / 100));
            }
        }

        trainPct = trainPct * Math.Max(20, 100 - Bonus(civ, EffectKind.Training, t.Key, t.Groups)) / 100;
        trainPct = trainPct * pricePct / 100 * (shock ? Content.Combat.ShockTrainPercent : 100) / 100;
        var u = new Unit
        {
            Id = Units.Count, Civ = civ.Id, Kind = kind, People = people, Size = people, Cell = spawn, State = UnitState.Training, NextTick = Ticks + Math.Max(1, t.TrainTicks * trainPct / 100),
            Cargo = t.Founds ? (long[])t.Price.Clone() : null, // settlers carry their provisions to the new camp
            Home = s.Id,
        };
        if (Geared(t))
        {
            TakeGear(s.Stock, u, shock); // the best arms the stores hold for all of them
        }

        SyncTotals(civ);
        Units.Add(u);
        civ.Units.Add(u.Id);
        ScheduleUnit(u, DoneTraining);
        return null;
    }

    private string? Move(Civ civ, int id, int cell, bool hidden, int[]? drawn = null)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State is UnitState.Gone or UnitState.Training)
        {
            return "Not your unit, or it is not ready.";
        }

        if ((uint)cell >= (uint)Geo.CellCount)
        {
            return "Outside the map.";
        }

        Unit u = Units[id];
        UnitType t = Content.Units[u.Kind];
        if (t.System)
        {
            return "Porters and caravans follow their task.";
        }

        if (drawn is { Length: > 0 })
        {
            u.TargetUnit = -1;
            u.TargetSettlement = -1;
            u.TargetWall = -1;
            return MoveDrawn(civ, u, drawn, hidden);
        }

        // Found into a spare list first: a refused move leaves the unit on its old way (B1, 2026-11-19 tests).
        if (!FindPath(u.Cell, cell, _way, t.Naval, u.Civ))
        {
            return "There is no way there on foot.";
        }

        u.Path.Clear();
        u.Path.AddRange(_way);
        u.Patrol.Clear();
        u.PatrolStage = PatrolStage.None;

        u.Hidden = hidden && t.HiddenTicksPerHex > 0;
        u.TargetUnit = -1;
        u.TargetSettlement = -1;
        u.TargetWall = -1;
        u.PathIndex = 0;
        if (u.Path.Count == 0)
        {
            u.State = UnitState.Idle;
            return null;
        }

        u.State = UnitState.Moving;
        u.NextTick = Ticks + StepTicks(u, u.Path[0]);
        ScheduleUnit(u, UnitStep);
        return null;
    }

    private string? Found(Civ civ, int id)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State != UnitState.Idle)
        {
            return "The settlers must stand still to found a camp.";
        }

        Unit u = Units[id];
        if (!Content.Units[u.Kind].Founds)
        {
            return "Only settlers found camps.";
        }

        string? why = CannotFound(u.Cell);
        if (why is not null)
        {
            return why;
        }

        Unwatch(u);
        u.State = UnitState.Gone;
        civ.Units.Remove(u.Id);
        Settlement s = FoundSettlement(civ, u.Cell, u.People, u.Cargo ?? new long[Content.Goods.Count]);
        News.Add((civ.Id, $"Settlers founded {s.Name}."));

        // G1 (2026-11-19): the camp starts with what feeds it (foundWith), paid from what the settlers carried.
        foreach (int kind in Content.FoundWith)
        {
            BuildingType t = Content.Buildings[kind];
            if (Knows(civ, t) && CanAfford(s.Stock, t.Price, 100) && PlaceFinished(civ, s, t))
            {
                Pay(s.Stock, t.Price, 100);
            }
        }

        SyncTotals(civ);
        RoadSupply(civ, s);
        return null;
    }

    /// <summary>
    /// True when <paramref name="civ"/> sees another people's unit now: within the sight of one of its units or near one of its
    /// settlements. Hidden units are seen only from the next hex.
    /// </summary>
    public bool Sees(Civ civ, Unit other)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(other);
        if (other.State is UnitState.Gone or UnitState.Training)
        {
            return false;
        }

        if (civ.Vision[other.Cell] <= 0)
        {
            return false;
        }

        if (!other.Hidden)
        {
            return true;
        }

        // Hidden units: only on our own land or right next to one of our units.
        if (SettlementAt[other.Cell] >= 0 && Settlements[SettlementAt[other.Cell]].Civ == civ.Id)
        {
            return true;
        }

        HexCoord at = Geo.FromIndex(other.Cell);
        foreach (int id in civ.Units)
        {
            Unit u = Units[id];
            if (u.State != UnitState.Training && Geo.Distance(Geo.FromIndex(u.Cell), at) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    private int StepTicks(Unit u, int cell)
    {
        UnitType t = Content.Units[u.Kind];
        int per = u.Hidden ? t.HiddenTicksPerHex : t.TicksPerHex;
        int cost = t.Naval ? 100 : CellCost(cell);
        int speed = (100 + Bonus(Civs[u.Civ], EffectKind.Speed, t.Key, t.Groups)) * FedPercent(u) / 100;
        return Math.Max(1, per * Math.Max(50, cost) / speed);
    }

    // Walking cost of a hex in percent (0 = impassable): terrain, halved on a road.
    private int CellCost(int cell) => RoadCost(cell, Content.WalkCost[(Map.Biome[cell] * 3) + Map.Relief[cell]]);

    private bool HasBuilding(Settlement s, string key)
    {
        foreach (int id in Civs[s.Civ].Buildings)
        {
            Building b = Buildings[id];
            if (b.Settlement == s.Id && b.State == BuildingState.Active && Content.Buildings[b.Type].Key == key)
            {
                return true;
            }
        }

        return false;
    }

    // A water hex within two hexes of a settlement centre, where its ships are launched (−1 = none).
    private int WaterNear(int center)
    {
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(2)];
        int n = Geo.Range(Geo.FromIndex(center), 2, area);
        for (int k = 0; k < n; k++)
        {
            int i = Geo.Index(area[k]);
            if (Content.World.Biomes[Map.Biome[i]].IsWater)
            {
                return i;
            }
        }

        return -1;
    }

    private void Step(Unit u)
    {
        if (u.PathIndex >= u.Path.Count)
        {
            u.State = UnitState.Idle; // the way was cut short while the step waited
            u.Path.Clear();
            return;
        }

        int next = u.Path[u.PathIndex];
        if (Content.Units[u.Kind].Army)
        {
            Disengage(u, next); // each enemy left behind strikes once
            if (u.State == UnitState.Gone)
            {
                return;
            }
        }

        u.Cell = next;
        u.PathIndex++;
        u.Run++;
        u.StepTick = Ticks;
        Watch(u);
        if (Content.Units[u.Kind].Army && !Content.Units[u.Kind].Naval && u.PathIndex < u.Path.Count && EnemyBeside(u, u.Cell))
        {
            // Zone of control: next to an enemy a unit stops; from here it goes on one hex at a time.
            u.State = UnitState.Idle;
            u.Path.Clear();
            MakeRoom(u);
            return;
        }

        int goal = u.TargetUnit >= 0 ? Units[u.TargetUnit].Cell : u.TargetSettlement >= 0 ? Settlements[u.TargetSettlement].Center : -1;
        if (u.PathIndex >= u.Path.Count || (goal >= 0 && Distance(u.Cell, goal) <= Content.Units[u.Kind].Range))
        {
            u.State = UnitState.Idle;
            u.Path.Clear();
            if (u.Job != CarrierJob.None)
            {
                Arrive(u);
            }
            else if (u.PatrolStage != PatrolStage.None)
            {
                PatrolArrived(u);
            }
            else
            {
                MakeRoom(u);
            }

            return;
        }

        u.NextTick = Ticks + StepTicks(u, u.Path[u.PathIndex]);
        ScheduleUnit(u, UnitStep);
    }

    /// <summary>
    /// True when a walker of the people may go from the hex in the direction: rivers stop walkers until the era given in the data,
    /// except next to a crossing building of their own (raft landings) on either bank (decision of 2026-11-18).
    /// </summary>
    public bool Crossable(int civ, int from, int dir)
    {
        if (civ < 0 || Civs[civ].Era >= Content.RiversFreeEra)
        {
            return true;
        }

        HexCoord c = Geo.FromIndex(from);
        int edge = Geo.EdgeIndex(Geo.Edge(c, (HexDirection)dir));
        if (Map.River[edge] == 0 || (BridgeAt.Length > 0 && BridgeAt[edge] != 0))
        {
            return true;
        }

        int to = Geo.Index(Geo.Neighbor(c, (HexDirection)dir));
        return CrossingAt(civ, from) || CrossingAt(civ, to);
    }

    private bool CrossingAt(int civ, int cell)
    {
        for (int k = 0; k < SlotsPerHex; k++)
        {
            int b = SlotAt[(cell * SlotsPerHex) + k];
            if (b >= 0 && Buildings[b].Civ == civ && Buildings[b].State == BuildingState.Active && Content.Buildings[Buildings[b].Type].Crossing)
            {
                return true;
            }
        }

        return false;
    }

    // Direction from one hex to its neighbour (−1 when they are not neighbours).
    private int DirectionTo(int from, int to)
    {
        HexCoord c = Geo.FromIndex(from);
        for (int d = 0; d < 6; d++)
        {
            if (Geo.Index(Geo.Neighbor(c, (HexDirection)d)) == to)
            {
                return d;
            }
        }

        return -1;
    }

    private int Cost(int cell, bool naval) => naval ? (Content.World.Biomes[Map.Biome[cell]].IsWater ? 100 : 0) : CellCost(cell);

    /// <summary>
    /// A* over land hexes with walking costs. Fills <paramref name="path"/> with the hexes after <paramref name="from"/> up to
    /// <paramref name="to"/>. Arrays are reused between calls (no allocation per search).
    /// </summary>
    private readonly List<int> _way = [];

    /// <summary>Finds a way into a unit's path only when there is one (the old way stays otherwise).</summary>
    private bool PathFor(Unit u, int goal)
    {
        if (!FindPath(u.Cell, goal, _way, Content.Units[u.Kind].Naval, u.Civ))
        {
            return false;
        }

        u.Path.Clear();
        u.Path.AddRange(_way);
        return true;
    }

    /// <summary>Finds a way from a hex to another (cleared first), true when there is one.</summary>
    public bool FindPath(int from, int to, List<int> path, bool naval = false, int civ = -1)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        if (from == to)
        {
            return true;
        }

        if (Cost(to, naval) == 0)
        {
            return false;
        }

        int n = Geo.CellCount;
        _pathCost ??= new int[n];
        _pathFrom ??= new int[n];
        _pathStamp ??= new int[n];
        _stamp++;
        _open.Clear();
        HexCoord goal = Geo.FromIndex(to);
        _pathStamp[from] = _stamp;
        _pathCost[from] = 0;
        _pathFrom[from] = -1;
        _open.Enqueue(from, Geo.Distance(Geo.FromIndex(from), goal) * 50);
        Span<HexCoord> nb = stackalloc HexCoord[6];
        int visited = 0;
        while (_open.TryDequeue(out int cur, out int _))
        {
            if (cur == to)
            {
                for (int c = to; c != from; c = _pathFrom[c])
                {
                    path.Add(c);
                }

                path.Reverse();
                return true;
            }

            if (++visited > MaxPathNodes)
            {
                return false;
            }

            Geo.GetNeighbors(Geo.FromIndex(cur), nb);
            for (int d = 0; d < 6; d++)
            {
                int next = Geo.Index(nb[d]);
                int step = Cost(next, naval);
                if (step == 0 || (!naval && (!Crossable(civ, cur, d) || WalledAgainst(civ, next))))
                {
                    continue;
                }

                int cost = _pathCost[cur] + step;
                if (_pathStamp[next] != _stamp || cost < _pathCost[next])
                {
                    _pathStamp[next] = _stamp;
                    _pathCost[next] = cost;
                    _pathFrom[next] = cur;
                    _open.Enqueue(next, cost + (Geo.Distance(nb[d], goal) * 50));
                }
            }
        }

        return false;
    }
}
