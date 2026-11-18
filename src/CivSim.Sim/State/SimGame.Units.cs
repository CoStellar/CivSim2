using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Train a unit of <paramref name="Kind"/> in a settlement (its people leave the settlement).</summary>
public sealed record TrainCommand(int Civ, int Settlement, int Kind) : SimCommand(Civ);

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

    private string? Train(Civ civ, int sid, int kind)
    {
        if ((uint)sid >= (uint)Settlements.Count || Settlements[sid].Civ != civ.Id || Settlements[sid].Abandoned)
        {
            return "Not your settlement.";
        }

        if ((uint)kind >= (uint)Content.Units.Count)
        {
            return "Unknown unit.";
        }

        Settlement s = Settlements[sid];
        UnitType t = Content.Units[kind];
        if (!civ.KnownTech[t.Tech])
        {
            return $"{t.Name} need {Content.Techs[t.Tech].Name}.";
        }

        if (s.People - t.People < 5)
        {
            return $"{s.Name} has {s.People} people; {t.Name.ToLowerInvariant()} take {t.People} and 5 must stay.";
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

        if (!CanAfford(s.Stock, t.Price, 100))
        {
            return $"{t.Name} cost {PriceText(t.Price)} in {s.Name}.";
        }

        Pay(s.Stock, t.Price, 100);
        SyncTotals(civ);
        s.People -= t.People;
        civ.Dirty = true;
        int trainPct = 100;
        foreach (int bid in civ.Buildings)
        {
            Building b = Buildings[bid];
            if (b.Settlement == s.Id && b.State == BuildingState.Active)
            {
                trainPct = Math.Min(trainPct, Content.Buildings[b.Type].TrainPercent);
            }
        }

        trainPct = trainPct * Math.Max(20, 100 - Bonus(civ, EffectKind.Training, t.Key, t.Groups)) / 100;
        var u = new Unit
        {
            Id = Units.Count, Civ = civ.Id, Kind = kind, People = t.People, Cell = spawn, State = UnitState.Training, NextTick = Ticks + Math.Max(1, t.TrainTicks * trainPct / 100),
            Cargo = t.Founds ? (long[])t.Price.Clone() : null, // settlers carry their provisions to the new camp
            Home = s.Id,
        };
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
            return MoveDrawn(civ, u, drawn, hidden);
        }

        if (!FindPath(u.Cell, cell, u.Path, t.Naval))
        {
            return "There is no way there on foot.";
        }

        u.Patrol.Clear();
        u.PatrolStage = PatrolStage.None;

        u.Hidden = hidden && t.HiddenTicksPerHex > 0;
        u.TargetUnit = -1;
        u.TargetSettlement = -1;
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
        int speed = 100 + Bonus(Civs[u.Civ], EffectKind.Speed, t.Key, t.Groups);
        return Math.Max(1, per * Math.Max(50, cost) / speed);
    }

    // Walking cost of a hex in percent (0 = impassable): terrain, halved on a road.
    private int CellCost(int cell)
    {
        int cost = Content.WalkCost[(Map.Biome[cell] * 3) + Map.Relief[cell]];
        int b = BuildingAt[cell];
        if (cost > 0 && b >= 0 && Buildings[b].State == BuildingState.Active && Array.IndexOf(Content.Buildings[Buildings[b].Type].Groups, "road") >= 0)
        {
            cost = cost * Content.RoadCostPercent / 100;
        }

        return cost;
    }

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
        u.Cell = u.Path[u.PathIndex++];
        Watch(u);
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

            return;
        }

        u.NextTick = Ticks + StepTicks(u, u.Path[u.PathIndex]);
        ScheduleUnit(u, UnitStep);
    }

    private int Cost(int cell, bool naval) => naval ? (Content.World.Biomes[Map.Biome[cell]].IsWater ? 100 : 0) : CellCost(cell);

    /// <summary>
    /// A* over land hexes with walking costs. Fills <paramref name="path"/> with the hexes after <paramref name="from"/> up to
    /// <paramref name="to"/>. Arrays are reused between calls (no allocation per search).
    /// </summary>
    public bool FindPath(int from, int to, List<int> path, bool naval = false)
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
                if (step == 0)
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
