using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Train a unit of <paramref name="Kind"/> in a settlement (its people leave the settlement).</summary>
public sealed record TrainCommand(int Civ, int Settlement, int Kind) : SimCommand(Civ);

/// <summary>Send a unit to a hex, hidden or in the open (scouts).</summary>
public sealed record MoveCommand(int Civ, int Unit, int Cell, bool Hidden) : SimCommand(Civ);

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

        if (!CanAfford(civ, t.Price, 100))
        {
            return $"{t.Name} cost {PriceText(t.Price)}.";
        }

        Pay(civ, t.Price, 100);
        s.People -= t.People;
        civ.Dirty = true;
        var u = new Unit { Id = Units.Count, Civ = civ.Id, Kind = kind, People = t.People, Cell = s.Center, State = UnitState.Training, NextTick = Ticks + Math.Max(1, t.TrainTicks) };
        Units.Add(u);
        civ.Units.Add(u.Id);
        ScheduleUnit(u, DoneTraining);
        return null;
    }

    private string? Move(Civ civ, int id, int cell, bool hidden)
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
        if (!FindPath(u.Cell, cell, u.Path))
        {
            return "There is no way there on foot.";
        }

        u.Hidden = hidden && t.HiddenTicksPerHex > 0;
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

        u.State = UnitState.Gone;
        civ.Units.Remove(u.Id);
        Settlement s = FoundSettlement(civ, u.Cell, u.People);
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

        HexCoord at = Geo.FromIndex(other.Cell);
        foreach (int id in civ.Units)
        {
            Unit u = Units[id];
            if (u.State != UnitState.Training && Geo.Distance(Geo.FromIndex(u.Cell), at) <= (other.Hidden ? 1 : Content.Units[u.Kind].Sight))
            {
                return true;
            }
        }

        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            if (!s.Abandoned && Geo.Distance(Geo.FromIndex(s.Center), at) <= (other.Hidden ? Content.Levels[s.Level].Radius : Content.Levels[s.Level].Radius + 3))
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
        int cost = Content.WalkCost[(Map.Biome[cell] * 3) + Map.Relief[cell]];
        return Math.Max(1, per * Math.Max(100, cost) / 100);
    }

    private void Step(Unit u)
    {
        u.Cell = u.Path[u.PathIndex++];
        Reveal(Civs[u.Civ], u.Cell, Content.Units[u.Kind].Sight);
        if (u.PathIndex >= u.Path.Count)
        {
            u.State = UnitState.Idle;
            u.Path.Clear();
            return;
        }

        u.NextTick = Ticks + StepTicks(u, u.Path[u.PathIndex]);
        ScheduleUnit(u, UnitStep);
    }

    /// <summary>
    /// A* over land hexes with walking costs. Fills <paramref name="path"/> with the hexes after <paramref name="from"/> up to
    /// <paramref name="to"/>. Arrays are reused between calls (no allocation per search).
    /// </summary>
    public bool FindPath(int from, int to, List<int> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        if (from == to)
        {
            return true;
        }

        if (Content.WalkCost[(Map.Biome[to] * 3) + Map.Relief[to]] == 0)
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
        _open.Enqueue(from, Geo.Distance(Geo.FromIndex(from), goal) * 100);
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
                int step = Content.WalkCost[(Map.Biome[next] * 3) + Map.Relief[next]];
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
                    _open.Enqueue(next, cost + (Geo.Distance(nb[d], goal) * 100));
                }
            }
        }

        return false;
    }
}
