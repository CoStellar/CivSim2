using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Perception;
using CivSim.Core.State;

namespace CivSim.Core.Units;

/// <summary>
/// A* over the hex map with terrain costs (stage 6 PR 3a): the cheapest way over walkable cells the civilization knows.
/// A step into a cell costs the ticks to cross it (<see cref="StepTicks"/>: base ticks × terrain percent, plus a river on
/// the edge crossed); the estimate is the hex distance × the base ticks, which never overestimates (no cell is cheaper
/// than flat open land), so the way found is the cheapest. Arrays are kept between searches and reset by a run stamp, and
/// the open set is a binary heap on plain arrays: a search allocates nothing but its result. A search looks at most at
/// <see cref="UnitRules.RouteNodeLimit"/> cells. Deterministic: ties are broken by the order cells were reached.
/// </summary>
public sealed class RouteFinder
{
    private int[] _cost = [];
    private int[] _came = [];
    private int[] _stamp = [];
    private bool[] _closed = [];
    private int[] _heapCell = [];
    private long[] _heapKey = [];
    private int _heapCount;
    private int _run;
    private long _order;
    private readonly System.Collections.Generic.List<int> _path = [];

    /// <summary>
    /// Ticks to step from <paramref name="from"/> into <paramref name="to"/> (neighbours) for a unit that needs
    /// <paramref name="baseTicks"/> for a flat, open cell; 0 when <paramref name="to"/> is not walkable.
    /// </summary>
    public static int StepTicks(GameContent content, WorldState world, HexCoord from, HexDirection side, int to, int baseTicks)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(world);
        WorldMap map = world.Map;
        if (!MapMemory.Walkable(content, map, to))
        {
            return 0;
        }

        UnitRules rules = content.Units;
        int percent = 100;
        if (content.Biomes[map.Biome[to]].IsWater)
        {
            percent = rules.IcePercent; // walkable water is sea ice
        }
        else
        {
            byte relief = map.Relief[to];
            percent = relief >= (byte)Relief.Mountains ? rules.MountainsPercent : relief == (byte)Relief.Hills ? rules.HillsPercent : 100;
            if (IsForest(rules, map.Biome[to]))
            {
                percent = Math.Max(percent, rules.ForestPercent);
            }
        }

        if (rules.RiverPercent > 0 && map.River[world.Geometry.EdgeIndex(world.Geometry.Edge(from, side))] != 0)
        {
            percent += rules.RiverPercent;
        }

        return Math.Max(1, (int)((long)baseTicks * percent / 100));
    }

    /// <summary>
    /// The next cell of the cheapest way from <paramref name="from"/> to <paramref name="to"/> over walkable cells
    /// <paramref name="civ"/> knows (the goal itself may be unknown), or -1 when there is none within the search limit.
    /// </summary>
    public int NextStep(GameContent content, GameState state, CivilizationState civ, WorldState world, int from, int to, int baseTicks)
    {
        int found = Search(content, state, civ, world, from, to, baseTicks);
        if (found < 0 || found == from)
        {
            return -1;
        }

        int c = found;
        while (_came[c] != from)
        {
            c = _came[c];
        }

        return c;
    }

    /// <summary>
    /// The cells of the cheapest way (as <see cref="NextStep"/>), from the first step to <paramref name="to"/>, or null
    /// when there is none. The list is reused by the next call.
    /// </summary>
    public System.Collections.Generic.List<int>? Path(GameContent content, GameState state, CivilizationState civ, WorldState world, int from, int to, int baseTicks)
    {
        int found = Search(content, state, civ, world, from, to, baseTicks);
        if (found < 0)
        {
            return null;
        }

        _path.Clear();
        for (int c = found; c != from && c >= 0; c = _came[c])
        {
            _path.Add(c);
        }

        _path.Reverse();
        return _path;
    }

    /// <summary>Ticks of the cheapest way (as <see cref="NextStep"/>), or -1 when there is none.</summary>
    public int RouteTicks(GameContent content, GameState state, CivilizationState civ, WorldState world, int from, int to, int baseTicks)
    {
        int found = Search(content, state, civ, world, from, to, baseTicks);
        return found < 0 ? -1 : _cost[found];
    }

    private int Search(GameContent content, GameState state, CivilizationState civ, WorldState world, int from, int to, int baseTicks)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(world);
        HexGlobe geo = world.Geometry;
        int n = geo.CellCount;
        if (_cost.Length != n)
        {
            _cost = new int[n];
            _came = new int[n];
            _stamp = new int[n];
            _closed = new bool[n];
            _heapCell = new int[n];
            _heapKey = new long[n];
            _run = 0;
        }

        if (from == to)
        {
            return from;
        }

        _run++;
        _heapCount = 0;
        _order = 0;
        HexCoord goal = geo.FromIndex(to);
        Visit(from, -1, 0, Estimate(geo, geo.FromIndex(from), goal, baseTicks));
        int looked = 0;
        int limit = content.Units.RouteNodeLimit;
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        while (_heapCount > 0)
        {
            int cur = Pop();
            if (_closed[cur])
            {
                continue;
            }

            _closed[cur] = true;
            if (cur == to)
            {
                return to;
            }

            if (++looked > limit)
            {
                return -1;
            }

            HexCoord at = geo.FromIndex(cur);
            geo.GetNeighbors(at, n6);
            for (int d = 0; d < 6; d++)
            {
                int ni = geo.Index(n6[d]);
                if (_stamp[ni] == _run && _closed[ni])
                {
                    continue;
                }

                if (ni != to && !MapMemory.Knows(content, state, civ, ni))
                {
                    continue;
                }

                int step = StepTicks(content, world, at, (HexDirection)d, ni, baseTicks);
                if (step == 0)
                {
                    continue;
                }

                int cost = _cost[cur] + step;
                if (_stamp[ni] == _run && cost >= _cost[ni])
                {
                    continue;
                }

                Visit(ni, cur, cost, cost + Estimate(geo, n6[d], goal, baseTicks));
            }
        }

        return -1;
    }

    private static int Estimate(HexGlobe geo, HexCoord a, HexCoord b, int baseTicks) => geo.Distance(a, b) * baseTicks;

    private void Visit(int cell, int came, int cost, int priority)
    {
        if (_stamp[cell] != _run)
        {
            _stamp[cell] = _run;
            _closed[cell] = false;
        }

        _cost[cell] = cost;
        _came[cell] = came;

        // Key: priority first, then the order the cell was reached (deterministic ties).
        long key = ((long)priority << 32) | (uint)(_order++ & 0xFFFFFFFF);
        Push(cell, key);
    }

    private void Push(int cell, long key)
    {
        if (_heapCount == _heapCell.Length)
        {
            Array.Resize(ref _heapCell, _heapCell.Length * 2);
            Array.Resize(ref _heapKey, _heapKey.Length * 2);
        }

        int i = _heapCount++;
        while (i > 0)
        {
            int parent = (i - 1) >> 1;
            if (_heapKey[parent] <= key)
            {
                break;
            }

            _heapCell[i] = _heapCell[parent];
            _heapKey[i] = _heapKey[parent];
            i = parent;
        }

        _heapCell[i] = cell;
        _heapKey[i] = key;
    }

    private int Pop()
    {
        int top = _heapCell[0];
        int last = --_heapCount;
        if (last > 0)
        {
            int cell = _heapCell[last];
            long key = _heapKey[last];
            int i = 0;
            while (true)
            {
                int child = (2 * i) + 1;
                if (child >= last)
                {
                    break;
                }

                if (child + 1 < last && _heapKey[child + 1] < _heapKey[child])
                {
                    child++;
                }

                if (_heapKey[child] >= key)
                {
                    break;
                }

                _heapCell[i] = _heapCell[child];
                _heapKey[i] = _heapKey[child];
                i = child;
            }

            _heapCell[i] = cell;
            _heapKey[i] = key;
        }

        return top;
    }

    private static bool IsForest(UnitRules rules, int biome)
    {
        foreach (int f in rules.ForestBiomes)
        {
            if (f == biome)
            {
                return true;
            }
        }

        return false;
    }
}
