using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Population;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// Simple rule-based tribe AI (decision of 2026-11-05, until the advisors of Stage 4). For every camp of an
/// AI-controlled civilization it compares the food its land gives now with the best place within reach (GDD 5.4):
/// a hungry camp moves when another place is clearly better (into an old campsite, onto free land, or by driving out
/// a weaker camp); a crowded camp sends a group to land that can feed it; a tiny camp joins a camp of its own; an idle
/// camp takes what was left in empty settlements nearby.
/// It issues the same commands a player would. It only reads the state and only looks at cells within
/// <see cref="PopulationRules.MaxMoveCells"/> of its own camps (a stand-in for map knowledge until Stage 3g).
/// </summary>
public sealed class TribeAi : ICommandSource
{
    private readonly GameContent _content;
    private readonly PopulationRules _rules;
    private readonly GatheringSystem _yields;
    private readonly FarmingSystem _farming;
    private readonly HexCoord[] _reach;
    private readonly HexCoord[] _farmArea;
    private readonly int[] _ringOf;
    private readonly HexCoord[] _near;
    private long[] _occupiedAt = [];
    private int[] _settlementAt = [];
    private readonly List<int> _emptySites = [];
    private readonly HashSet<int> _joined = [];
    private readonly long[] _foodPerPerson;
    private long[] _nearOwnAt = [];
    private long _civStamp;
    private readonly List<long[]> _cellFood = [];
    private long _cellFoodStamp;
    private readonly List<long> _cellFoodClimate = [];
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
        _farming = new FarmingSystem(content);
        _reach = new HexCoord[HexGlobe.RangeSize(_rules.MaxMoveCells)];
        _farmArea = new HexCoord[HexGlobe.RangeSize(Math.Max(1, _rules.InfluenceMaxRange))];
        _influence = new Influence(content);
        _near = new HexCoord[HexGlobe.RangeSize(Math.Max(0, _rules.MinDistanceBetweenOwnCamps - 1))];
        _foodPerPerson = new long[content.Strata.Count];
        foreach (StratumDefinition stratum in content.Strata)
        {
            foreach (NeedDefinition need in stratum.Needs)
            {
                if (need.Basic && need.AnyOfCategory && need.Category == GoodCategory.Food)
                {
                    _foodPerPerson[stratum.Id] += need.PerYear.Hundredths;
                }
            }
        }

        // Range() lists cells ring by ring, so the distance of the k-th cell is known without computing it.
        _ringOf = new int[_reach.Length];
        for (int r = 0, k = 0; r <= _rules.MaxMoveCells; r++)
        {
            for (int n = 0; n < HexGlobe.RingSize(r); n++)
            {
                _ringOf[k++] = r;
            }
        }
    }

    // The AI counts as the land of a place the cells a camp without warriors would reach from it, paying the movement
    // cost of the terrain like the influence zone does (GDD 5.4). Counting a plain radius made tribes on hills think
    // their tiny zone was rich, so they never left.
    private readonly Influence _influence;
    private readonly PriorityQueue<int, int> _open = new();
    private int[] _costAt = [];
    private long[] _costStamp = [];
    private long _costRun;

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
            _settlementAt = new int[earth.Geometry.CellCount];
        }

        // Which settlement stands on each cell this turn: people first, an empty one only where nobody lives.
        _emptySites.Clear();
        IReadOnlyList<SettlementState> all = state.Settlements;
        for (int n = 0; n < all.Count; n++)
        {
            SettlementState s = all[n];
            if (s.World != WorldId.Earth)
            {
                continue;
            }

            int i = earth.Geometry.Index(s.Cell);
            if (s.IsActive)
            {
                _occupiedAt[i] = _stamp;
                _settlementAt[i] = n;
            }
            else
            {
                _emptySites.Add(n);
            }
        }

        foreach (int n in _emptySites)
        {
            int i = earth.Geometry.Index(all[n].Cell);
            if (_occupiedAt[i] != _stamp)
            {
                _occupiedAt[i] = -_stamp;
                _settlementAt[i] = n;
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
            foreach (SettlementState s in all)
            {
                if (s.Owner == civ.Id && s.IsActive && s.World == WorldId.Earth)
                {
                    MarkNearOwn(earth, s.Cell);
                }
            }

            foreach (SettlementState s in all)
            {
                if (s.Owner == civ.Id && s.IsActive && s.TotalPopulation() > 0 && _content.SettlementLevels[s.Level].Mobile && !_joined.Contains(s.Id.Value))
                {
                    Decide(state, queue, civ, s);
                }
            }

            _joined.Clear();
        }
    }

    private void Decide(GameState state, CommandQueue queue, CivilizationState civ, SettlementState s)
    {
        WorldState world = state.Worlds[s.World.Value];
        int people = s.TotalPopulation();

        // Two clocks: survival moves are weighed every turn, slow choices (joining, splitting, plundering) once per
        // era-length period. Without live seasons every turn is such a turn.
        bool slowTurn = state.IsDecisionTurn(_content);

        // A handful of people does not survive on its own: they join the nearest camp of their own.
        if (slowTurn && people < _rules.MergeBelowPeople && NearestOwnCamp(state, world, civ, s) is { } into)
        {
            queue.Enqueue(PlayerId.Ai, civ.Id, new AbandonSettlementCommand(_content, s.Id, into.Id));
            _joined.Add(into.Id.Value);
            return;
        }

        // A camp looks for a better place when it went hungry or when its place is getting too cold or too hot.
        bool hungry = s.FedPerMille < _rules.MoveWhenFedBelowPercent * 10;
        bool uncomfortable = Comfort(state, world, civ, world.Geometry.Index(s.Cell)) < 100;
        bool rested = (state.Month - s.MovedMonth) >= _rules.MinTurnsBetweenMoves * (long)state.MonthsPerTurn(_content);
        bool mobile = _content.SettlementLevels[s.Level].Mobile;
        if (mobile && (hungry || uncomfortable) && rested)
        {
            long here = SiteValue(state, world, civ, s.Cell);
            (HexCoord best, long bestValue) = BestSite(state, world, civ, s, splitting: false);
            if (bestValue * 100 > here * (100 + _rules.MoveWhenBetterByPercent))
            {
                queue.Enqueue(PlayerId.Ai, civ.Id, MoveTo(state, world, s, best, 100));
                Take(world, best);
                return;
            }
        }

        if (!slowTurn)
        {
            return;
        }

        // Agriculture: a camp whose land could feed half again as many people by farming settles down (GDD 5.3).
        if (mobile && people >= _content.Assets.SettleMinPeople && CanSettle(civ, s.Level)
            && FarmFood(world, s) * 2 >= GroupFoodNeed(s, 100) * 3)
        {
            queue.Enqueue(PlayerId.Ai, civ.Id, new SettleCommand(_content, s.Id));
            return;
        }

        int max = _content.SettlementLevels[s.Level].MaxPopulation;
        bool crowded = people >= _rules.MinSplitPeople && people * 100L >= (long)max * _rules.SplitAbovePercentOfLevel;
        if (crowded)
        {
            // No fixed limit of camps: a group leaves only for land that can feed it.
            long need = GroupFoodNeed(s, _rules.SplitPercent) * _rules.SplitNeedsFoodPercent / 100;
            (HexCoord site, long value) = BestSite(state, world, civ, s, splitting: true);
            if (value > 0 && SiteFood(world, site) >= need)
            {
                queue.Enqueue(PlayerId.Ai, civ.Id, MoveTo(state, world, s, site, _rules.SplitPercent));
                Take(world, site);
                return;
            }
        }

        // Nothing better to do: take what others left nearby while there is room in the warehouse.
        if (s.TotalStock() * 2 < _content.SettlementLevels[s.Level].StorageCapacity)
        {
            foreach (int n in _emptySites)
            {
                SettlementState site = state.Settlements[n];
                if (site.World == s.World && site.TotalStock() > Amount.Zero && world.Geometry.Distance(s.Cell, site.Cell) <= _rules.MaxMoveCells
                    && OpenLand(world, civ, site.Cell))
                {
                    queue.Enqueue(PlayerId.Ai, civ.Id, new PlunderSettlementCommand(_content, s.Id, site.Id));
                    return;
                }
            }
        }
    }

    private bool CanSettle(CivilizationState civ, int level)
    {
        int village = SettleCommand.VillageLevel(_content, level);
        if (village < 0)
        {
            return false;
        }

        int tech = _content.SettlementLevels[village].RequiresTechnology;
        return tech < 0 || civ.Knows(tech);
    }

    // Grain per year the camp's zone would give if every cell were farmed in full, hundredths.
    private long FarmFood(WorldState world, SettlementState s)
    {
        int claim = s.Id.Value + 1;
        int count = world.Geometry.Range(s.Cell, Math.Min(_rules.InfluenceMaxRange, world.Geometry.MaxUniqueRadius), _farmArea);
        long food = 0;
        for (int k = 0; k < count; k++)
        {
            int i = world.Geometry.Index(_farmArea[k]);
            if (world.Map.Claim[i] == claim)
            {
                food += _farming.GrainPerFarmer(world.Map, i) * _rules.FarmersPerCell;
            }
        }

        return food;
    }

    // The command that brings people to the chosen cell: into an empty settlement there, against the camp living
    // there, or a plain move or split.
    private ICommand MoveTo(GameState state, WorldState world, SettlementState s, HexCoord cell, int percent)
    {
        int i = world.Geometry.Index(cell);
        if (world.Id == WorldId.Earth && Math.Abs(_occupiedAt[i]) == _stamp && _settlementAt[i] >= 0)
        {
            SettlementState there = state.Settlements[_settlementAt[i]];
            if (there.IsActive)
            {
                return new ConquerSettlementCommand(_content, s.Id, there.Id);
            }

            return new OccupySettlementCommand(_content, s.Id, there.Id, percent);
        }

        return percent >= 100
            ? new MoveSettlementCommand(_content, s.Id, cell)
            : new SplitSettlementCommand(_content, s.Id, cell, percent);
    }

    // Best place within reach: food of its land minus a penalty for distance. Old campsites of the tribe count a bit
    // more (known land); a hungry camp also weighs foreign camps it is strong enough to drive out. Split groups keep
    // away from own camps and never attack.
    private (HexCoord Cell, long Value) BestSite(GameState state, WorldState world, CivilizationState civ, SettlementState from, bool splitting)
    {
        HexGlobe geo = world.Geometry;
        int count = geo.Range(from.Cell, Math.Min(_rules.MaxMoveCells, geo.MaxUniqueRadius), _reach);
        HexCoord best = from.Cell;
        long bestValue = 0;
        long attack = splitting ? 0 : SettlementAssets.Warriors(_content, from);
        bool earth = world.Id == WorldId.Earth;
        for (int k = 0; k < count; k++)
        {
            HexCoord c = _reach[k];
            int i = geo.Index(c);
            if (_content.Biomes[world.Map.Biome[i]].IsWater || c == from.Cell)
            {
                continue;
            }

            SettlementState? there = earth && Math.Abs(_occupiedAt[i]) == _stamp && _settlementAt[i] >= 0 ? state.Settlements[_settlementAt[i]] : null;
            bool foreignCamp = there is { IsActive: true } && there.Owner != civ.Id;
            if (!foreignCamp && !OpenLand(world, civ, c))
            {
                continue;
            }

            int distance = _ringOf[k];
            long value = SiteValue(state, world, civ, c) * Math.Max(0, 100 - (distance * _rules.DistancePenaltyPercentPerCell)) / 100;
            if (there is { IsActive: false } && (there.Owner == civ.Id || there.ClaimCiv == civ.Id.Value))
            {
                value = value * (100 + _rules.OwnCampsiteBonusPercent) / 100;
            }

            // The costly checks run only for places that would be the new best.
            if (value <= bestValue)
            {
                continue;
            }

            if (foreignCamp)
            {
                if (attack > 0 && _content.SettlementLevels[there!.Level].Mobile
                    && attack * 100 >= ConquerSettlementCommand.DefenceStrength(_content, there) * _rules.AttackAboveStrengthPercent)
                {
                    best = c;
                    bestValue = value;
                }
            }
            else if (Free(world, from, c, splitting))
            {
                best = c;
                bestValue = value;
            }
        }

        return (best, bestValue);
    }

    // Land the civilization holds or nobody holds.
    private static bool OpenLand(WorldState world, CivilizationState civ, HexCoord c)
    {
        short owner = world.Map.Owner[world.Geometry.Index(c)];
        return owner == WorldMap.NoOwner || owner == civ.Id.Value;
    }

    private SettlementState? NearestOwnCamp(GameState state, WorldState world, CivilizationState civ, SettlementState from)
    {
        SettlementState? nearest = null;
        int best = _rules.MaxMoveCells + 1;
        foreach (SettlementState other in state.Settlements)
        {
            if (other.Owner == civ.Id && other.IsActive && other.Id != from.Id && other.World == from.World
                && other.TotalPopulation() >= _rules.MergeBelowPeople)
            {
                int d = world.Geometry.Distance(from.Cell, other.Cell);
                if (d < best)
                {
                    best = d;
                    nearest = other;
                }
            }
        }

        return nearest;
    }

    // Food per year the leaving share of the people needs (hundredths).
    private long GroupFoodNeed(SettlementState s, int percent)
    {
        long need = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length && k < _foodPerPerson.Length; k++)
        {
            need += people[k] * percent / 100 * _foodPerPerson[k];
        }

        return need;
    }

    // A camp may not stand where another camp stands; a split group also keeps away from its own camps.
    private bool Free(WorldState world, SettlementState from, HexCoord c, bool splitting)
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
            // Taken for this turn: no other camp picks it, and an empty settlement there is no longer offered.
            int i = world.Geometry.Index(cell);
            _occupiedAt[i] = _stamp;
            _settlementAt[i] = -1;
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

    // How good a place is to live in: the food its land gives, times how pleasant its temperature is for this
    // civilization (GDD 8.3: the AI weighs places; nothing here is a fixed "flee the winter" rule).
    private long SiteValue(GameState state, WorldState world, CivilizationState civ, HexCoord center) =>
        SiteFood(world, center) * Comfort(state, world, civ, world.Geometry.Index(center)) / 100;

    /// <summary>
    /// Comfort of a cell, 0–100%: the coming season's temperature with live seasons, otherwise the winter temperature,
    /// against the limits in <c>data/population.json</c>. The cold_tolerance effect (fire, clothing, houses) lowers the cold
    /// limit, so better technology makes cold land worth more.
    /// </summary>
    public int Comfort(GameState state, WorldState world, CivilizationState civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(civ);
        WorldMap map = world.Map;
        int temperature = map.LiveSeasons ? map.NextSeasonTemperature[cell] : map.Temperature[cell] - (map.Seasonality[cell] / 2);
        int tolerance = state.Effects.Count == 0
            ? 0
            : state.Effects.Total(_content.Effects, EffectStat.ColdToleranceDegrees, EffectTarget.Civilization, WorldId.Earth, civ.Id.Value);
        int cold = Math.Max(0, _rules.ComfortColdBelow - tolerance - temperature);
        int hot = Math.Max(0, temperature - _rules.ComfortHotAbove);
        return Math.Max(_rules.ComfortMinPercent, 100 - ((cold + hot) * _rules.ComfortPercentPerDegree));
    }

    private long ComputeSiteFood(WorldState world, HexCoord center)
    {
        WorldMap map = world.Map;
        HexGlobe geo = world.Geometry;
        long[] full = CellFood(world);
        if (_costAt.Length != geo.CellCount)
        {
            _costAt = new int[geo.CellCount];
            _costStamp = new long[geo.CellCount];
        }

        int range = _rules.InfluenceBaseRange;
        _costRun++;
        int start = geo.Index(center);
        _costAt[start] = 0;
        _costStamp[start] = _costRun;
        _open.Clear();
        _open.Enqueue(start, 0);
        long food = 0;
        Span<HexCoord> neighbours = stackalloc HexCoord[6];
        while (_open.TryDequeue(out int cell, out int cost))
        {
            if (cost > _costAt[cell])
            {
                continue;
            }

            food += full[cell] * map.WildFood[cell] / WorldMap.FullWildFood;
            geo.GetNeighbors(geo.FromIndex(cell), neighbours);
            foreach (HexCoord nc in neighbours)
            {
                int next = geo.Index(nc);
                int step = _influence.EnterCost(map, next);
                int total = cost + step;
                if (step >= 0 && total <= range && (_costStamp[next] != _costRun || total < _costAt[next]))
                {
                    _costStamp[next] = _costRun;
                    _costAt[next] = total;
                    _open.Enqueue(next, total);
                }
            }
        }

        return food;
    }

    // Food of every cell when untouched; computed once per world, or every turn when seasons are live.
    private long[] CellFood(WorldState world)
    {
        int w = world.Id.Value;
        while (_cellFood.Count <= w)
        {
            _cellFood.Add([]);
        }

        bool live = world.Map.LiveSeasons;

        // The climate changes every few centuries (stage 1c-B): then the food of the land changes too.
        bool climateChanged = _cellFoodClimate.Count <= w || _cellFoodClimate[w] != world.Climate.Year;
        if (_cellFood[w].Length != world.Geometry.CellCount || climateChanged || (live && _cellFoodStamp != _stamp))
        {
            long[] food = _cellFood[w].Length == world.Geometry.CellCount ? _cellFood[w] : new long[world.Geometry.CellCount];
            for (int i = 0; i < food.Length; i++)
            {
                // With live seasons the AI plans for this turn and the next one.
                food[i] = live
                    ? (_yields.CellFoodPerYear(world.Map, i).Hundredths + _yields.CellFoodPerYear(world.Map, i, nextTurn: true).Hundredths) / 2
                    : _yields.CellFoodPerYear(world.Map, i).Hundredths;
            }

            _cellFood[w] = food;
            _cellFoodStamp = _stamp;
            while (_cellFoodClimate.Count <= w)
            {
                _cellFoodClimate.Add(-1);
            }

            _cellFoodClimate[w] = world.Climate.Year;
        }

        return _cellFood[w];
    }
}
