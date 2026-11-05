using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Population;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// Simple rule-based tribe AI (decision of 2026-11-05, until the advisors of Stage 4). For every camp of an
/// AI-controlled civilization it compares the food its land gives now with the best place within reach (GDD 5.4):
/// a hungry camp moves when another place is clearly better; a crowded camp sends a split group to found a new camp.
/// It issues the same commands a player would. It only reads the state and only looks at cells within
/// <see cref="PopulationRules.MaxMoveCells"/> of its own camps (a stand-in for map knowledge until Stage 3g).
/// </summary>
public sealed class TribeAi : ICommandSource
{
    private readonly GameContent _content;
    private readonly PopulationRules _rules;
    private readonly GatheringSystem _yields;
    private readonly HexCoord[] _reach;
    private readonly HexCoord[] _site;
    private readonly int[] _ringOf;
    private readonly HexCoord[] _near;
    private long[] _occupiedAt = [];
    private long[] _nearOwnAt = [];
    private long _civStamp;
    private readonly List<long[]> _cellFood = [];
    private long[] _siteFood = [];
    private long[] _siteStamp = [];
    private long _stamp;

    /// <summary>Creates the AI.</summary>
    public TribeAi(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _rules = content.Population;
        _yields = new GatheringSystem(content, new YieldYears(0));
        _reach = new HexCoord[HexTorus.RangeSize(_rules.MaxMoveCells)];
        _site = new HexCoord[HexTorus.RangeSize(SiteRadius)];
        _near = new HexCoord[HexTorus.RangeSize(Math.Max(0, _rules.MinDistanceBetweenOwnCamps - 1))];

        // Range() lists cells ring by ring, so the distance of the k-th cell is known without computing it.
        _ringOf = new int[_reach.Length];
        for (int r = 0, k = 0; r <= _rules.MaxMoveCells; r++)
        {
            for (int n = 0; n < HexTorus.RingSize(r); n++)
            {
                _ringOf[k++] = r;
            }
        }
    }

    /// <summary>Radius around a place that the AI counts as its land when it compares places.</summary>
    public const int SiteRadius = 2;

    /// <inheritdoc />
    public void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        _stamp++;
        WorldState earth = state.Worlds[0];
        if (_occupiedAt.Length != earth.Geometry.CellCount)
        {
            _occupiedAt = new long[earth.Geometry.CellCount];
            _nearOwnAt = new long[earth.Geometry.CellCount];
        }

        foreach (SettlementState s in state.Settlements)
        {
            if (s.TotalPopulation() > 0 && s.World == WorldId.Earth)
            {
                _occupiedAt[earth.Geometry.Index(s.Cell)] = _stamp;
            }
        }

        foreach (CivilizationState civ in state.Civilizations)
        {
            if (!civ.IsAlive || civ.Controller != PlayerId.Ai)
            {
                continue;
            }

            // Cells too close to this civilization's camps for a new camp.
            _civStamp++;
            foreach (SettlementState s in state.Settlements)
            {
                if (s.Owner == civ.Id && s.TotalPopulation() > 0 && s.World == WorldId.Earth)
                {
                    MarkNearOwn(earth, s.Cell);
                }
            }

            int camps = 0;
            foreach (SettlementState s in state.Settlements)
            {
                camps += s.Owner == civ.Id && s.TotalPopulation() > 0 ? 1 : 0;
            }

            foreach (SettlementState s in state.Settlements)
            {
                if (s.Owner == civ.Id && s.TotalPopulation() > 0 && _content.SettlementLevels[s.Level].Mobile)
                {
                    if (Decide(state, queue, civ, s, camps))
                    {
                        camps++;
                    }
                }
            }
        }
    }

    // Returns true when a new camp was ordered.
    private bool Decide(GameState state, CommandQueue queue, CivilizationState civ, SettlementState s, int camps)
    {
        WorldState world = state.Worlds[s.World.Value];
        int people = s.TotalPopulation();
        bool hungry = s.FedPerMille < _rules.MoveWhenFedBelowPercent * 10;
        bool rested = (state.Month - s.MovedMonth) >= _rules.MinYearsBetweenMoves * 12L;
        if (hungry && rested)
        {
            long here = SiteFood(world, s.Cell);
            (HexCoord best, long bestValue) = BestSite(state, world, civ, s, splitting: false);
            if (bestValue * 100 > here * (100 + _rules.MoveWhenBetterByPercent))
            {
                queue.Enqueue(PlayerId.Ai, civ.Id, new MoveSettlementCommand(_content, s.Id, best));
                Take(world, best);
                return false;
            }
        }

        int max = _content.SettlementLevels[s.Level].MaxPopulation;
        bool crowded = people >= _rules.MinSplitPeople && people * 100L >= (long)max * _rules.SplitAbovePercentOfLevel;
        if (crowded && camps < _rules.MaxSettlementsPerCivilization)
        {
            (HexCoord site, long value) = BestSite(state, world, civ, s, splitting: true);
            if (value > 0)
            {
                queue.Enqueue(PlayerId.Ai, civ.Id, new SplitSettlementCommand(_content, s.Id, site, _rules.SplitPercent));
                Take(world, site);
                return true;
            }
        }

        return false;
    }

    // Best place within reach: food of its land minus a penalty for distance. Split groups keep away from own camps.
    private (HexCoord Cell, long Value) BestSite(GameState state, WorldState world, CivilizationState civ, SettlementState from, bool splitting)
    {
        HexTorus geo = world.Geometry;
        int count = geo.Range(from.Cell, Math.Min(_rules.MaxMoveCells, geo.MaxUniqueRadius), _reach);
        HexCoord best = from.Cell;
        long bestValue = 0;
        for (int k = 0; k < count; k++)
        {
            HexCoord c = _reach[k];
            int i = geo.Index(c);
            if (_content.Biomes[world.Map.Biome[i]].IsWater || c == from.Cell)
            {
                continue;
            }

            short owner = world.Map.Owner[i];
            if (owner != WorldMap.NoOwner && owner != civ.Id.Value)
            {
                continue;
            }

            int distance = _ringOf[k];
            long value = SiteFood(world, c) * Math.Max(0, 100 - (distance * _rules.DistancePenaltyPercentPerCell)) / 100;

            // The costly check runs only for places that would be the new best.
            if (value > bestValue && Free(world, civ, from, c, splitting))
            {
                best = c;
                bestValue = value;
            }
        }

        return (best, bestValue);
    }

    // A camp may not stand where another camp stands; a split group also keeps away from its own camps.
    private bool Free(WorldState world, CivilizationState civ, SettlementState from, HexCoord c, bool splitting)
    {
        if (world.Id != WorldId.Earth)
        {
            return true;
        }

        int i = world.Geometry.Index(c);
        return _occupiedAt[i] != _stamp && (!splitting || _nearOwnAt[i] != _civStamp);
    }

    private void Take(WorldState world, HexCoord cell)
    {
        if (world.Id == WorldId.Earth)
        {
            _occupiedAt[world.Geometry.Index(cell)] = _stamp;
            MarkNearOwn(world, cell);
        }
    }

    private void MarkNearOwn(WorldState world, HexCoord cell)
    {
        int count = world.Geometry.Range(cell, Math.Min(_rules.MinDistanceBetweenOwnCamps - 1, world.Geometry.MaxUniqueRadius), _near);
        for (int k = 0; k < count; k++)
        {
            _nearOwnAt[world.Geometry.Index(_near[k])] = _civStamp;
        }
    }

    // Food per year the land around a cell gives now, wild food taken into account (hundredths). Cached for the turn:
    // wild food does not change while the AI decides. Only Earth is cached; other worlds come with space travel.
    private long SiteFood(WorldState world, HexCoord center)
    {
        if (world.Id != WorldId.Earth)
        {
            return ComputeSiteFood(world, center);
        }

        int cell = world.Geometry.Index(center);
        if (_siteFood.Length != world.Geometry.CellCount)
        {
            _siteFood = new long[world.Geometry.CellCount];
            _siteStamp = new long[world.Geometry.CellCount];
        }

        if (_siteStamp[cell] != _stamp)
        {
            _siteFood[cell] = ComputeSiteFood(world, center);
            _siteStamp[cell] = _stamp;
        }

        return _siteFood[cell];
    }

    private long ComputeSiteFood(WorldState world, HexCoord center)
    {
        WorldMap map = world.Map;
        long[] full = CellFood(world);
        int count = world.Geometry.Range(center, SiteRadius, _site);
        long food = 0;
        for (int k = 0; k < count; k++)
        {
            int i = world.Geometry.Index(_site[k]);
            food += full[i] * map.WildFood[i] / WorldMap.FullWildFood;
        }

        return food;
    }

    // Food of every cell when untouched; the map does not change in eras 1-2, so it is computed once per world.
    private long[] CellFood(WorldState world)
    {
        int w = world.Id.Value;
        while (_cellFood.Count <= w)
        {
            _cellFood.Add([]);
        }

        if (_cellFood[w].Length != world.Geometry.CellCount)
        {
            var food = new long[world.Geometry.CellCount];
            for (int i = 0; i < food.Length; i++)
            {
                food[i] = _yields.CellFoodPerYear(world.Map, i).Hundredths;
            }

            _cellFood[w] = food;
        }

        return _cellFood[w];
    }
}
