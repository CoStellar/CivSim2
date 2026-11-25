using System;
using System.Collections.Generic;
using CivSim.Core.Map;

namespace CivSim.Sim.State;

/// <summary>
/// 12B (Patryk, 2026-11-20): until the AI uses ships, every civilization starts on the main continent (the largest land), at most
/// <c>startPhase.neighbourMaxHexes</c> hexes by land from a civilization that started before it. Distances are steps over land hexes
/// (rivers do not count: they are crossed later), from the centres of the shown circles while choosing, so nobody learns more
/// than the circles show.
/// </summary>
public sealed partial class SimGame
{
    private int[]? _landPart;
    private int _mainLand = -1;
    private int[]? _nearSteps;

    // Land parts (once): hexes connected over walkable land; the largest is the main continent.
    private void LandParts()
    {
        if (_landPart is not null)
        {
            return;
        }

        int n = Geo.CellCount;
        Tables();
        _landPart = new int[n];
        Array.Fill(_landPart, -1);
        var sizes = new List<int>();
        var queue = new Queue<int>();
        for (int start = 0; start < n; start++)
        {
            if (_landPart[start] >= 0 || !Walkable(start))
            {
                continue;
            }

            int part = sizes.Count;
            int size = 0;
            _landPart[start] = part;
            queue.Enqueue(start);
            while (queue.TryDequeue(out int cur))
            {
                size++;
                for (int d = 0; d < 6; d++)
                {
                    int next = _next[(cur * 6) + d];
                    if (_landPart[next] < 0 && Walkable(next))
                    {
                        _landPart[next] = part;
                        queue.Enqueue(next);
                    }
                }
            }

            sizes.Add(size);
        }

        for (int p = 0; p < sizes.Count; p++)
        {
            if (_mainLand < 0 || sizes[p] > sizes[_mainLand])
            {
                _mainLand = p;
            }
        }
    }

    private bool Walkable(int cell) => !Content.World.Biomes[Map.Biome[cell]].IsWater && CellCost(cell) > 0;

    /// <summary>True when the hex lies on the main continent (always true when the rule is off).</summary>
    public bool OnMainLand(int cell)
    {
        if (Content.StartPhase.NeighbourMaxHexes <= 0)
        {
            return true;
        }

        LandParts();
        return _landPart![cell] == _mainLand;
    }

    // Steps over land from the given hexes, as far as limit (int.MaxValue beyond).
    private int[] StepsFrom(IEnumerable<int> sources, int limit)
    {
        int n = Geo.CellCount;
        Tables();
        var steps = new int[n];
        Array.Fill(steps, int.MaxValue);
        var queue = new Queue<int>();
        foreach (int s in sources)
        {
            if (s >= 0 && steps[s] != 0)
            {
                steps[s] = 0;
                queue.Enqueue(s);
            }
        }

        while (queue.TryDequeue(out int cur))
        {
            if (steps[cur] >= limit)
            {
                continue;
            }

            for (int d = 0; d < 6; d++)
            {
                int next = _next[(cur * 6) + d];
                if (steps[next] == int.MaxValue && Walkable(next))
                {
                    steps[next] = steps[cur] + 1;
                    queue.Enqueue(next);
                }
            }
        }

        return steps;
    }

    // While choosing: the steps from the circles taken so far (null before the first choice), refreshed after each choice.
    private void RefreshNear()
    {
        _nearSteps = null;
        if (Content.StartPhase.NeighbourMaxHexes <= 0)
        {
            return;
        }

        var taken = new List<int>();
        foreach (int c in BlockCenter)
        {
            if (c >= 0)
            {
                taken.Add(c);
            }
        }

        if (taken.Count > 0)
        {
            _nearSteps = StepsFrom(taken, Content.StartPhase.NeighbourMaxHexes + BlockFuzz);
        }
    }

    /// <summary>Why the hex is too far from the other civilizations or off the main continent (null = near enough, or the rule is off).</summary>
    public string? TooFar(int cell)
    {
        int most = Content.StartPhase.NeighbourMaxHexes;
        if (most <= 0)
        {
            return null;
        }

        if (!OnMainLand(cell))
        {
            return "Start on the main continent: civilizations cannot cross the sea yet.";
        }

        if (_nearSteps is not null && _nearSteps[cell] > most + BlockFuzz)
        {
            return $"Too far from the other civilizations: start within {most} hexes by land of a red circle.";
        }

        return null;
    }

    /// <summary>
    /// The starts of a game without the start phase: the first civilization keeps the generator's start (or the best place of the main
    /// continent when it is elsewhere); each next one keeps its generator's start when that fits the rule, else takes the best place by the seen score that is at least startPhase.neighbourMinHexes
    /// (else the shown circle) away from every other start and within the limit by land of one of them. When no place fits, the limit grows by half again.
    /// </summary>
    internal int[] NearStarts(int[] generated)
    {
        ArgumentNullException.ThrowIfNull(generated);
        if (Sites is null)
        {
            ScoreSites();
        }

        LandParts();
        int n = Geo.CellCount;
        var starts = new int[generated.Length];
        Array.Fill(starts, -1);
        starts[0] = OnMainLand(generated[0]) && Map.Relief[generated[0]] < 2 ? generated[0] : Best(-1, null, ShownBlockRadius);
        for (int c = 1; c < starts.Length; c++)
        {
            int limit = Content.StartPhase.NeighbourMaxHexes;
            int pick = -1;
            int[] first = StepsFrom(starts[..c], limit);
            int g = generated[c];
            if (OnMainLand(g) && Map.Relief[g] < 2 && first[g] <= limit && Array.TrueForAll(starts[..c], s => Geo.Distance(Geo.FromIndex(s), Geo.FromIndex(g)) >= ShownBlockRadius))
            {
                pick = g; // the generator's start already fits: it keeps its good land
            }

            for (int widen = 0; widen < 6 && pick < 0; widen++)
            {
                int[] steps = StepsFrom(starts[..c], limit);
                pick = Best(limit, steps, Math.Max(ShownBlockRadius, Content.StartPhase.NeighbourMinHexes)); // room to grow first
                if (pick < 0)
                {
                    pick = Best(limit, steps, ShownBlockRadius);
                }

                limit += limit / 2;
            }

            starts[c] = pick >= 0 ? pick : generated[c];
        }

        return starts;

        int Best(int limit, int[]? steps, int apartHexes)
        {
            int best = -1;
            for (int i = 0; i < n; i++)
            {
                if (Content.World.Biomes[Map.Biome[i]].IsWater || Map.Relief[i] == 2 || !OnMainLand(i) || (steps is not null && steps[i] > limit))
                {
                    continue;
                }

                bool apart = true;
                HexCoord h = Geo.FromIndex(i);
                foreach (int s in starts)
                {
                    apart &= s < 0 || Geo.Distance(Geo.FromIndex(s), h) >= apartHexes;
                }

                if (apart && (best < 0 || Sites!.Seen[i] > Sites.Seen[best]))
                {
                    best = i;
                }
            }

            return best;
        }
    }
}
