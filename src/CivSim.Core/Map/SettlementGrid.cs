using System;
using System.Collections.Generic;
using CivSim.Core.State;

namespace CivSim.Core.Map;

/// <summary>
/// Spatial grid of settlements (stage 6 PR 3a, T2): every world is cut into buckets of <see cref="Bucket"/> × <see cref="Bucket"/>
/// cells, and a query "settlements within N cells" looks only at the buckets such a cell can be in, not at every settlement.
/// The grid is a cache of <see cref="GameState.Settlements"/>: it is rebuilt when a settlement is founded, removed or moves
/// (<see cref="SettlementState.LayoutVersion"/>), it is neither saved nor hashed, and a query returns exactly what comparing
/// with every settlement returns, in the same order (ascending index in <see cref="GameState.Settlements"/>), so using it
/// changes no result.
/// </summary>
public sealed class SettlementGrid
{
    /// <summary>Bucket side in cells.</summary>
    public const int Bucket = 8;

    private readonly GameState _state;
    private long _version = -1;
    private int _count = -1;
    private int[][] _head = [];      // per world: first entry of each bucket, -1 when empty
    private int[] _next = [];        // entry chain (entry = settlement index), ascending within a bucket
    private int[] _tail = [];
    private int[] _stamp = [];
    private int _run;
    private readonly List<int> _found = [];

    /// <summary>A grid over the settlements of <paramref name="state"/>.</summary>
    public SettlementGrid(GameState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>
    /// Indices (in <see cref="GameState.Settlements"/>, ascending) of the settlements on <paramref name="world"/> at most
    /// <paramref name="radius"/> cells from <paramref name="center"/>, active or not (callers filter by owner and status).
    /// The list is reused by the next query.
    /// </summary>
    public List<int> Within(WorldState world, HexCoord center, int radius)
    {
        ArgumentNullException.ThrowIfNull(world);
        Refresh();
        _found.Clear();
        if (radius < 0 || world.Id.Value >= _head.Length)
        {
            return _found;
        }

        HexGlobe geo = world.Geometry;
        int[] head = _head[world.Id.Value];
        int bx = (geo.Width + Bucket - 1) / Bucket;
        if (_stamp.Length < head.Length)
        {
            _stamp = new int[head.Length];
            _run = 0;
        }

        _run++;
        IReadOnlyList<SettlementState> all = _state.Settlements;

        // A cell within the radius has an image (around the globe or across a pole) whose column and offset row each differ
        // by at most the radius (HexGlobe.NearestImage), so for every row of the band the columns around the wrapped
        // column hold every candidate.
        int span = Math.Min((2 * radius) + 1, geo.Width);
        for (int r = center.Row - radius; r <= center.Row + radius; r++)
        {
            HexCoord w = geo.Wrap(center.Col, r);
            int rowStart = w.Row / Bucket * bx;
            int col = span == geo.Width ? 0 : Mod(w.Col - radius, geo.Width);
            for (int left = span; left > 0;)
            {
                Visit(head, rowStart + (col / Bucket), geo, center, radius, all);
                int step = Math.Min(Bucket - (col % Bucket), geo.Width - col);
                left -= step;
                col = (col + step) % geo.Width;
            }
        }

        _found.Sort();
        return _found;
    }

    /// <summary>
    /// The same as <see cref="Within"/> by comparing with every settlement (tests check the grid against it).
    /// </summary>
    public static List<int> WithinByScan(GameState state, WorldState world, HexCoord center, int radius)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        var found = new List<int>();
        for (int i = 0; i < state.Settlements.Count; i++)
        {
            SettlementState s = state.Settlements[i];
            if (s.World == world.Id && world.Geometry.Distance(center, s.Cell) <= radius)
            {
                found.Add(i);
            }
        }

        return found;
    }

    private void Visit(int[] head, int bucket, HexGlobe geo, HexCoord center, int radius, IReadOnlyList<SettlementState> all)
    {
        if (bucket < 0 || bucket >= head.Length || _stamp[bucket] == _run)
        {
            return;
        }

        _stamp[bucket] = _run;
        for (int e = head[bucket]; e >= 0; e = _next[e])
        {
            if (geo.Distance(center, all[e].Cell) <= radius)
            {
                _found.Add(e);
            }
        }
    }

    // Rebuilds the buckets when a settlement was founded, removed or moved since the last query.
    private void Refresh()
    {
        IReadOnlyList<SettlementState> all = _state.Settlements;
        long version = SettlementState.LayoutVersion;
        if (version == _version && all.Count == _count && _head.Length == _state.Worlds.Count)
        {
            return;
        }

        _version = version;
        _count = all.Count;
        if (_head.Length != _state.Worlds.Count)
        {
            _head = new int[_state.Worlds.Count][];
        }

        for (int w = 0; w < _state.Worlds.Count; w++)
        {
            HexGlobe geo = _state.Worlds[w].Geometry;
            int buckets = ((geo.Width + Bucket - 1) / Bucket) * ((geo.Height + Bucket - 1) / Bucket);
            if (_head[w] is null || _head[w].Length != buckets)
            {
                _head[w] = new int[buckets];
            }

            Array.Fill(_head[w], -1);
        }

        if (_next.Length < all.Count)
        {
            _next = new int[Math.Max(all.Count, _next.Length * 2)];
        }

        for (int w = 0; w < _head.Length; w++)
        {
            int[] head = _head[w];
            if (_tail.Length < head.Length)
            {
                _tail = new int[head.Length];
            }

            HexGlobe geo = _state.Worlds[w].Geometry;
            int bx = (geo.Width + Bucket - 1) / Bucket;
            for (int i = 0; i < all.Count; i++)
            {
                SettlementState s = all[i];
                if (s.World.Value != w)
                {
                    continue;
                }

                int bucket = (s.Cell.Row / Bucket * bx) + (s.Cell.Col / Bucket);
                _next[i] = -1;
                if (head[bucket] < 0)
                {
                    head[bucket] = i;
                }
                else
                {
                    _next[_tail[bucket]] = i;
                }

                _tail[bucket] = i;
            }
        }
    }

    private static int Mod(int a, int m) => ((a % m) + m) % m;
}
