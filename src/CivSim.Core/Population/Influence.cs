using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Population;

/// <summary>
/// Influence zones (GDD 5.4): the cells a settlement's people gather on, hunt on and defend. A settlement reaches as
/// far as its range allows, paying the movement cost of every cell entered; its strength fades with distance. Where
/// zones overlap the stronger settlement takes the cell (ties go to the lower settlement id). Water stops influence
/// until boats arrive with technologies.
/// </summary>
public sealed class Influence
{
    private readonly GameContent _content;
    private readonly PopulationRules _rules;
    private readonly PriorityQueue<int, int> _queue = new();
    private readonly List<int> _reached = [];
    private int[] _cost = [];
    private int[] _bestStrength = [];
    private int[] _bestClaim = [];

    /// <summary>Creates the calculator.</summary>
    public Influence(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _rules = content.Population;
    }

    /// <summary>Range of a settlement in movement cost: base plus one point per <see cref="PopulationRules.WarriorsPerRange"/> warriors.</summary>
    public int RangeOf(SettlementState settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        int warriors = 0;
        ReadOnlySpan<int> people = settlement.PopulationView;
        for (int k = 0; k < people.Length; k++)
        {
            if (_content.Strata[k].Fights)
            {
                warriors += people[k];
            }
        }

        return Math.Min(_rules.InfluenceMaxRange, _rules.InfluenceBaseRange + (warriors / Math.Max(1, _rules.WarriorsPerRange)));
    }

    /// <summary>Cost to enter a cell; -1 when influence cannot enter it (water).</summary>
    public int EnterCost(WorldMap map, int cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        BiomeDefinition biome = _content.Biomes[map.Biome[cell]];
        if (biome.IsWater)
        {
            return -1;
        }

        return biome.MovementCost + map.Relief[cell] switch
        {
            (byte)Relief.Hills => _rules.HillsCost,
            (byte)Relief.Mountains => _rules.MountainsCost,
            _ => 0,
        };
    }

    /// <summary>Recomputes owner, claim and influence strength of every cell on every world.</summary>
    public void Recompute(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (WorldState world in state.Worlds)
        {
            Recompute(state, world);
        }
    }

    private void Recompute(GameState state, WorldState world)
    {
        WorldMap map = world.Map;
        int n = world.Geometry.CellCount;
        if (_cost.Length != n)
        {
            _cost = new int[n];
            _bestStrength = new int[n];
            _bestClaim = new int[n];
            Array.Fill(_cost, int.MaxValue);
        }

        Array.Clear(_bestStrength);
        Array.Clear(_bestClaim);
        foreach (SettlementState s in state.Settlements)
        {
            if (s.World != world.Id || s.TotalPopulation() == 0)
            {
                continue;
            }

            Spread(world, s);
        }

        ReadOnlySpan<int> claims = map.Claim.Values;
        for (int i = 0; i < n; i++)
        {
            int claim = _bestClaim[i];
            if (claim == claims[i] && (claim == 0 || map.Influence[i] == (byte)Math.Min(255, _bestStrength[i])))
            {
                continue;
            }

            map.Claim.Set(i, claim);
            map.Influence.Set(i, (byte)Math.Min(255, _bestStrength[i]));
            short owner = claim == 0 ? WorldMap.NoOwner : (short)state.FindSettlement(new SettlementId(claim - 1))!.Owner.Value;
            map.Owner.Set(i, owner);
        }
    }

    // Dijkstra over movement cost from the settlement's cell, up to its range.
    private void Spread(WorldState world, SettlementState s)
    {
        WorldMap map = world.Map;
        HexTorus geo = world.Geometry;
        int range = RangeOf(s);
        int start = geo.Index(s.Cell);
        _reached.Clear();
        _queue.Clear();
        _cost[start] = 0;
        _reached.Add(start);
        _queue.Enqueue(start, 0);
        Span<HexCoord> neighbours = stackalloc HexCoord[6];
        while (_queue.TryDequeue(out int cell, out int cost))
        {
            if (cost > _cost[cell])
            {
                continue;
            }

            int strength = (range - cost + 1) * _rules.InfluencePerStep;
            if (strength > _bestStrength[cell])
            {
                _bestStrength[cell] = strength;
                _bestClaim[cell] = s.Id.Value + 1;
            }

            geo.GetNeighbors(geo.FromIndex(cell), neighbours);
            foreach (HexCoord nc in neighbours)
            {
                int next = geo.Index(nc);
                int step = EnterCost(map, next);
                if (step < 0)
                {
                    continue;
                }

                int total = cost + step;
                if (total <= range && total < _cost[next])
                {
                    if (_cost[next] == int.MaxValue)
                    {
                        _reached.Add(next);
                    }

                    _cost[next] = total;
                    _queue.Enqueue(next, total);
                }
            }
        }

        foreach (int cell in _reached)
        {
            _cost[cell] = int.MaxValue;
        }
    }
}

/// <summary>
/// Recomputes influence zones in the science and influence phase (GDD 11.1, phase 10). A second instance in the
/// movement phase (5) recomputes them right after camps moved or split, so the same turn's gathering uses the new land.
/// </summary>
public sealed class InfluenceSystem(GameContent content, TurnPhase phase = TurnPhase.ScienceAndInfluence) : ITurnSystem
{
    private readonly Influence _influence = new(content);

    /// <inheritdoc />
    public TurnPhase Phase => phase;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (phase == TurnPhase.Movement && !AnySettlementMoved(context))
        {
            return;
        }

        _influence.Recompute(context.State);
    }

    private static bool AnySettlementMoved(TurnContext context)
    {
        foreach (Commands.CommandEnvelope e in context.Commands)
        {
            if (e.Command.Category == Commands.CommandCategory.Movement)
            {
                return true;
            }
        }

        return false;
    }
}
