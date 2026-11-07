using System;
using System.Collections.Generic;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Population;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// The governor advisor of tribes (GDD 8.1; rules of stage 3c-3g, on the shared evaluation since stage 4a). For every camp of an
/// AI-controlled civilization it compares the food its land gives now with the best place within reach (GDD 5.4):
/// a hungry camp moves when another place is clearly better (into an old campsite, onto free land, or by driving out
/// a weaker camp); a crowded camp sends a group to land that can feed it; a tiny camp joins a camp of its own; an idle
/// camp takes what was left in empty settlements nearby.
/// Places are scored as an <see cref="Evaluation"/>: the food of their land times comfort, nearness and an old-campsite
/// boost, so every move records why. It issues the same commands a player would, reads only the state, and weighs
/// only cells its civilization knows.
/// </summary>
public sealed class GovernorAdvisor : Advisor
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
    // The land as each set of gathering technologies sees it (PR C): fishers count the water, hunters the game.
    // Civilizations with the same set share it; the set changes only when a gathering technology is learned.
    private readonly Dictionary<string, Land> _lands = [];
    private readonly List<string?> _landKeyOf = [];
    private readonly List<int> _landKnownOf = [];
    private Land _land = null!;
    private int _landCiv = -1;
    private long _stamp = 1; // never 0, the value of a fresh cache entry
    private readonly ResponseCurve _nearness;
    private readonly ResponseCurve _farNearness;

    /// <summary>Creates the advisor.</summary>
    public GovernorAdvisor(GameContent content)
        : base(content, AdvisorKind.Governor)
    {
        _content = content;
        _rules = content.Population;
        _yields = new GatheringSystem(content, new YieldYears(0));
        _farming = new FarmingSystem(content);
        _reach = new HexCoord[HexGlobe.RangeSize(_rules.MaxMoveCells)];
        _farmArea = new HexCoord[HexGlobe.RangeSize(Math.Max(1, _rules.InfluenceMaxRange))];
        _influence = new Influence(content);
        _otherWorld = new Land(Research.TechBenefits.Empty(content));
        _near = new HexCoord[HexGlobe.RangeSize(Math.Max(0, _rules.MinDistanceBetweenOwnCamps - 1))];
        // Nearness: 1.0 next door, falling by the distance penalty per cell (data/population.json), 0 from 100/penalty cells on.
        _nearness = ResponseCurve.Linear(0, Factor.Neutral, 100, Factor.Neutral - (Factor.Neutral * _rules.DistancePenaltyPercentPerCell));
        // A place a journey away loses a share of its value per cell (data/ai.json), nothing from 1000/share cells on.
        int perCell = content.Ai.JourneyPenaltyPerMillePerCell;
        _farNearness = perCell == 0
            ? ResponseCurve.Linear(0, Factor.Neutral, 1, Factor.Neutral)
            : ResponseCurve.Linear(0, Factor.Neutral, Math.Max(1, Factor.Neutral / perCell), 0);
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
    public override void IssueCommands(GameState state, CommandQueue queue)
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
            // Planned decisions in the civilization's own steps; between them only a reaction to an attack on one of
            // its camps: the camps look for safer land at once (reactive decision, ADR 0018).
            bool steps = Simulation.WorldClock.Steps(civ);
            bool attacked = civ.IsAlive && (civ.Alarms & Simulation.Alarm.Attacked) != 0;
            if (!civ.IsAlive || civ.Controller != PlayerId.Ai || !(steps || attacked))
            {
                continue;
            }

            UseLand(civ);

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
                if (s.Owner != civ.Id || !s.IsActive || s.TotalPopulation() == 0 || _joined.Contains(s.Id.Value))
                {
                    continue;
                }

                if (_content.SettlementLevels[s.Level].Mobile)
                {
                    Decide(state, queue, civ, s, reacting: !steps);
                }
                else if (steps && Simulation.WorldClock.PlansNow(_content, state, civ))
                {
                    DecideVillage(state, queue, civ, s);
                }
            }

            _joined.Clear();
        }
    }

    private void Decide(GameState state, CommandQueue queue, CivilizationState civ, SettlementState s, bool reacting)
    {
        WorldState world = state.Worlds[s.World.Value];
        int people = s.TotalPopulation();
        if (s.JourneyTarget != SettlementState.NoJourney)
        {
            return; // on the way: the journey system walks it
        }

        // Two clocks: survival moves are weighed every own step (and in a reaction to an attack), slow choices (joining,
        // splitting, plundering) once per era-length period. Without live seasons every own step is such a step.
        bool slowTurn = !reacting && Simulation.WorldClock.PlansNow(_content, state, civ);

        // A handful of people does not survive on its own: they join the nearest camp of their own.
        if (slowTurn && people < _rules.MergeBelowPeople && NearestOwnCamp(state, world, civ, s) is { } into)
        {
            Evaluation join = Evaluation.Of(people);
            join.Note(FactorId.People, people);
            Issue(state, queue, civ.Id, new AbandonSettlementCommand(_content, s.Id, into.Id), AiAction.JoinCamp, s.Id.Value, into.Id.Value, join);
            _joined.Add(into.Id.Value);
            return;
        }

        // A camp looks for a better place when it went hungry or when its place is getting too cold or too hot.
        bool hungry = s.FedPerMille < _rules.MoveWhenFedBelowPercent * 10;
        bool uncomfortable = Comfort(state, world, civ, world.Geometry.Index(s.Cell)) < _content.Ai.UncomfortableBelowPercent;
        bool rested = (state.Month - s.MovedMonth) >= _rules.MinStepsBetweenMoves * (long)Simulation.WorldClock.RhythmMonths(_content, state, civ);
        bool mobile = _content.SettlementLevels[s.Level].Mobile;

        // A camp that is not starving looks around every few own steps (staggered by camp), not every step: a search that
        // found nothing rarely finds something the next step. With live seasons it looks every step to follow the summer.
        // A camp of a civilization that was just attacked looks at once, rested or not.
        bool starving = s.FedPerMille < _content.Ai.StarvingFedPercent * 10;
        bool searchTurn = reacting || starving || world.Map.LiveSeasons || (civ.Steps - 1 + s.Id.Value) % _content.Ai.SearchEverySteps == 0;
        if (mobile && (hungry || uncomfortable || reacting) && (rested || reacting) && searchTurn)
        {
            // Staying is the incumbent: another place must be better by a margin (GDD 8.6, hysteresis).
            // Staying gets only part of the expansion weight and expansiveness that moving gets, so an expansive
            // civilization moves more readily and a home-loving one stays (PR B of 2026-11-06).
            Evaluation stay = SiteEvaluation(state, world, civ, s.Cell);
            WeighStay(ref stay, civ);
            long here = stay.Value;
            DeterministicRandom? random = RandomFor(state, s.Id.Value);
            (HexCoord best, Evaluation move) = BestSite(state, world, civ, s, splitting: false, random);
            if (move.Value * 100 > here * (100 + _rules.MoveWhenBetterByPercent))
            {
                move.Note(FactorId.BetterThanHere, here);
                move.Note(FactorId.Fed, s.FedPerMille);
                (ICommand command, AiAction action) = MoveTo(state, world, s, best, 100);
                Issue(state, queue, civ.Id, command, action, s.Id.Value, world.Geometry.Index(best), move);
                Take(world, best);
                return;
            }

            // Nothing good within one move: set out for the best known land farther away (stage 3g), once per era turn.
            if (slowTurn && world.Id == WorldId.Earth)
            {
                (HexCoord far, Evaluation journey) = FarSite(state, world, civ, s, random);
                long incumbent = Math.Max(here, move.Value);
                if (journey.Value * 100 > incumbent * (100 + _content.Ai.JourneyBetterByPercent))
                {
                    journey.Note(FactorId.BetterThanHere, incumbent);
                    Issue(state, queue, civ.Id, new Perception.JourneyCommand(_content, s.Id, far), AiAction.Journey, s.Id.Value, world.Geometry.Index(far), journey);
                    Take(world, far);
                    return;
                }
            }
        }

        if (!slowTurn)
        {
            return;
        }

        // Agriculture (GDD 5.3): a camp settles when its producers, farming the best land of its zone, would feed the
        // camp with a margin and grow more than they gather now. An expansive civilization asks for more before it
        // gives up wandering (PR C of 2026-11-06).
        if (mobile && people >= _content.Assets.SettleMinPeople && CanSettle(civ, s.Level))
        {
            int producers = Producers(s);
            // Only farmers farm (stage 4F): a camp settles only with a farmer among its people, and counts the farmers
            // its village will have once the masters have taught them (the farming skill's share of the producers).
            int farmers = producers;
            int farming = _content.SkillRules.FarmingSkill;
            if (farming >= 0)
            {
                farmers = s.SkilledView[farming] == 0 ? 0 : (int)((long)producers * _content.Skills[farming].JobsPercent / 100);
            }

            long gather = Math.Min(SiteFood(world, s.Cell), _rules.FoodPerWorkerPerYear.Hundredths * producers);

            // The village's food: what its farmers grow, and what its other producers go on gathering (stage 4F).
            long farm = FarmFood(world, s, farmers) + (producers == 0 ? 0 : gather * (producers - farmers) / producers);
            long eat = GroupFoodNeed(s, 100);
            long wander = (long)GoalWeight(civ, Goal.Expansion) * CharacterMultiplier(civ, Goal.Expansion) / Factor.Neutral;
            if (farm * 100 >= eat * _content.Ai.SettleFarmYieldPercent
                && farm * 100 * Factor.Neutral >= gather * _content.Ai.SettleOverGatheringPercent * wander)
            {
                Evaluation settle = Evaluation.Of(farm);
                settle.Note(FactorId.FarmYield, eat > 0 ? farm * 100 / eat : 0);
                settle.Note(FactorId.OverGathering, gather > 0 ? farm * 100 / gather : 0);
                settle.Note(FactorId.People, people);
                Issue(state, queue, civ.Id, new SettleCommand(_content, s.Id), AiAction.Settle, s.Id.Value, world.Geometry.Index(s.Cell), settle);
                return;
            }
        }

        int max = _content.SettlementLevels[s.Level].MaxPopulation;
        // An expansive civilization (strategist's weight times character) sends groups out of fuller camps earlier.
        long drive = Math.Max(1, (long)GoalWeight(civ, Goal.Expansion) * CharacterMultiplier(civ, Goal.Expansion) / Factor.Neutral);
        long splitAbove = Math.Clamp(_rules.SplitAbovePercentOfLevel * Factor.Neutral / drive, _content.Ai.SplitMinPercentOfLevel, 100);
        bool crowded = people >= _rules.MinSplitPeople && people * 100L >= max * splitAbove;
        if (crowded)
        {
            // No fixed limit of camps: a group leaves only for land that can feed it.
            long need = GroupFoodNeed(s, _rules.SplitPercent) * _rules.SplitNeedsFoodPercent / 100;
            (HexCoord site, Evaluation split) = BestSite(state, world, civ, s, splitting: true, RandomFor(state, s.Id.Value));
            if (split.Value > 0 && SiteFood(world, site) >= need)
            {
                split.Note(FactorId.Crowding, people * 100L / Math.Max(1, max));
                (ICommand command, AiAction action) = MoveTo(state, world, s, site, _rules.SplitPercent);
                Issue(state, queue, civ.Id, command, action, s.Id.Value, world.Geometry.Index(site), split);
                Take(world, site);
                return;
            }
        }

        // A camp that has room and food sends a group of explorers towards land nobody of its people has seen, when its
        // civilization is curious and expansive enough (PR C: settling empty continents without hunger driving them).
        if (people >= _rules.MinSplitPeople && s.FedPerMille >= _rules.MoveWhenFedBelowPercent * 10 && Explore(state, queue, civ, s, world))
        {
            return;
        }

        // Nothing better to do: take what others left nearby while there is room in the warehouse.
        if (s.TotalStock().Hundredths * 100 < _content.SettlementLevels[s.Level].StorageCapacity.Hundredths * _content.Ai.PlunderBelowStoragePercent)
        {
            foreach (int n in _emptySites)
            {
                SettlementState site = state.Settlements[n];
                if (site.World == s.World && site.TotalStock() > Amount.Zero && world.Geometry.Distance(s.Cell, site.Cell) <= _rules.MaxMoveCells
                    && OpenLand(world, civ, site.Cell)
                    && (site.World != WorldId.Earth || Perception.MapMemory.Knows(_content, state, civ, world.Geometry.Index(site.Cell))))
                {
                    Evaluation plunder = Evaluation.Of(site.TotalStock().Hundredths);
                    plunder.Note(FactorId.LeftGoods, site.TotalStock().Hundredths);
                    Issue(state, queue, civ.Id, new PlunderSettlementCommand(_content, s.Id, site.Id), AiAction.Plunder, s.Id.Value, site.Id.Value, plunder);
                    return;
                }
            }
        }
    }

    // A village (GDD 5.3, PR C) does not move, but it sends settlers out: when it is crowded (earlier for an expansive
    // civilization), or when the land it farms is worn out. They found a camp on the best known free land within one
    // move, and that camp chooses where to live and whether to settle by itself.
    private void DecideVillage(GameState state, CommandQueue queue, CivilizationState civ, SettlementState s)
    {
        WorldState world = state.Worlds[s.World.Value];
        int people = s.TotalPopulation();
        if (people < _rules.MinSplitPeople)
        {
            return;
        }

        // At most one group per period of years (rule 6), staggered by village so that not all villages go at once.
        // The village plans once per own step, or once per era-length period with live seasons. A hungry village sends
        // settlers sooner (AI fixes after stage 4g): fewer mouths to feed.
        bool hungry = s.FedPerMille < _content.Ai.VillageHungryFedPercent * 10;
        long period = (hungry ? _content.Ai.VillageHungrySettlersEveryYears : _content.Ai.VillageSettlersEveryYears) * 12L;
        long window = Math.Max(civ.StepMonths, Simulation.WorldClock.EraMonths(_content, civ.Era));
        long offset = s.Id.Value * 97L % period;
        if (window < period && (state.Month + offset) / period == (state.Month - window + offset) / period)
        {
            return;
        }

        int max = _content.SettlementLevels[s.Level].MaxPopulation;
        long drive = Math.Max(1, (long)GoalWeight(civ, Goal.Expansion) * CharacterMultiplier(civ, Goal.Expansion) / Factor.Neutral);
        long settleAbove = Math.Clamp(_content.Ai.VillageSettlersAbovePercent * Factor.Neutral / drive, _content.Ai.SplitMinPercentOfLevel, 100);
        bool crowded = people * 100L >= max * settleAbove;
        int worn = WornPercent(world, s);
        bool wornOut = worn < _content.Ai.VillageWornBelowPercent;
        if (!crowded && !wornOut && !hungry)
        {
            return;
        }

        // A group the size of a new camp leaves, however big the village is; a hungry village sends the share of mouths it
        // cannot feed, at most half of it. Each stratum gives its share rounded down, so the share grows until the group is whole.
        int percent = (int)Math.Clamp(_content.Ai.VillageSettlersPeople * 100L / people, 1, 90);
        while (percent < 90 && GroupPeople(s, percent) < _content.Ai.VillageSettlersPeople)
        {
            percent++;
        }

        int least = percent;
        if (hungry)
        {
            percent = Math.Max(percent, Math.Min(50, (1000 - s.FedPerMille) / 10));
        }

        (HexCoord site, Evaluation go) = BestSite(state, world, civ, s, splitting: true, RandomFor(state, s.Id.Value));
        long food = go.Value > 0 ? SiteFood(world, site) : 0;
        while (percent > least && GroupFoodNeed(s, percent) * _rules.SplitNeedsFoodPercent / 100 > food)
        {
            percent--; // a hungry village sends as many as the new place can feed
        }

        long need = GroupFoodNeed(s, percent) * _rules.SplitNeedsFoodPercent / 100;
        if (go.Value > 0 && food >= need)
        {
            go.Note(FactorId.Crowding, people * 100L / Math.Max(1, max));
            go.Note(FactorId.WornLand, worn);
            go.Note(FactorId.Fed, s.FedPerMille);
            (ICommand command, AiAction action) = MoveTo(state, world, s, site, percent);
            Issue(state, queue, civ.Id, command, action == AiAction.SplitCamp ? AiAction.SendSettlers : action, s.Id.Value, world.Geometry.Index(site), go);
            Take(world, site);
        }
    }

    // Fertility of the village's farmable land against its natural fertility, % (100 = as it was, lower = worn out).
    private int WornPercent(WorldState world, SettlementState s)
    {
        int claim = s.Id.Value + 1;
        int count = world.Geometry.Range(s.Cell, Math.Min(_rules.InfluenceMaxRange, world.Geometry.MaxUniqueRadius), _farmArea);
        long now = 0;
        long natural = 0;
        for (int k = 0; k < count; k++)
        {
            int i = world.Geometry.Index(_farmArea[k]);
            if (world.Map.Claim[i] == claim && world.Map.NaturalFertility[i] >= _rules.MinFarmFertility)
            {
                now += world.Map.Fertility[i];
                natural += world.Map.NaturalFertility[i];
            }
        }

        return natural == 0 ? 100 : (int)(now * 100 / natural);
    }

    // Explorers (PR C): a group goes to the known free cell within one move that has the most unknown land around it,
    // when curiosity and expansiveness together (with the expansion weight) reach the threshold of data/ai.json.
    private bool Explore(GameState state, CommandQueue queue, CivilizationState civ, SettlementState s, WorldState world)
    {
        if (world.Id != WorldId.Earth)
        {
            return false;
        }

        long drive = (long)GoalWeight(civ, Goal.Expansion) * CharacterMultiplier(civ, Goal.Expansion) / Factor.Neutral
            * CharacterMultiplier(civ, Goal.Science) / Factor.Neutral;
        if (drive < _content.Ai.ExploreAbovePerMille)
        {
            return false;
        }

        HexGlobe geo = world.Geometry;
        int count = geo.Range(s.Cell, Math.Min(_rules.MaxMoveCells, geo.MaxUniqueRadius), _reach);
        int look = Math.Min(_content.Ai.ExploreLookCells, geo.MaxUniqueRadius);
        if (_look.Length < HexGlobe.RangeSize(look))
        {
            _look = new HexCoord[HexGlobe.RangeSize(look)];
        }

        long need = GroupFoodNeed(s, _content.Ai.ExploreGroupPercent) * _rules.SplitNeedsFoodPercent / 100;
        Chooser.Clear();
        for (int k = 0; k < count; k++)
        {
            HexCoord c = _reach[k];
            int i = geo.Index(c);
            if (_content.Biomes[world.Map.Biome[i]].IsWater || !Perception.MapMemory.Knows(_content, state, civ, i)
                || Math.Abs(_occupiedAt[i]) == _stamp || !OpenLand(world, civ, c) || !Free(world, s, c, splitting: true))
            {
                continue;
            }

            int around = geo.Range(c, look, _look);
            int unknown = 0;
            for (int n = 0; n < around; n++)
            {
                int j = geo.Index(_look[n]);
                unknown += !_content.Biomes[world.Map.Biome[j]].IsWater && !Perception.MapMemory.Knows(_content, state, civ, j) ? 1 : 0;
            }

            if (unknown * 100 < around * _content.Ai.ExploreUnknownPercent || SiteFood(world, c) < need)
            {
                continue;
            }

            Evaluation e = Evaluation.Of(unknown);
            e.Note(FactorId.Unknown, unknown);
            Toward(ref e, world, civ, s.Cell, c);
            e.Weigh(Factor.Neutral, (int)Math.Min(int.MaxValue, drive));
            Chooser.Offer(i, e);
        }

        int pick = Pick(civ, RandomFor(state, s.Id.Value));
        if (pick < 0)
        {
            return false;
        }

        HexCoord site = geo.FromIndex(Chooser.KeyAt(pick));
        Issue(state, queue, civ.Id, new SplitSettlementCommand(_content, s.Id, site, _content.Ai.ExploreGroupPercent), AiAction.Explore, s.Id.Value, Chooser.KeyAt(pick), Chooser.EvaluationAt(pick));
        Take(world, site);
        return true;
    }

    private HexCoord[] _look = [];

    // Known free land beyond one move, on a sparse lattice (every third cell each way) to keep the search cheap;
    // a small penalty per cell of distance.
    private (HexCoord Cell, Evaluation Why) FarSite(GameState state, WorldState world, CivilizationState civ, SettlementState from, DeterministicRandom? random)
    {
        HexGlobe geo = world.Geometry;
        int radius = Math.Min(_content.Perception.JourneyCells, geo.MaxUniqueRadius);
        if (_far.Length < HexGlobe.RangeSize(radius))
        {
            _far = new HexCoord[HexGlobe.RangeSize(radius)];
        }

        int count = geo.Range(from.Cell, radius, _far);
        int every = _content.Ai.FarSearchEveryCells;
        Chooser.Clear();
        for (int k = 0; k < count; k++)
        {
            HexCoord c = _far[k];
            if ((c.Col % every) != 0 || (c.Row % every) != 0)
            {
                continue;
            }

            int i = geo.Index(c);
            int distance = geo.Distance(from.Cell, c);
            if (distance <= _rules.MaxMoveCells || _content.Biomes[world.Map.Biome[i]].IsWater
                || !Perception.MapMemory.Knows(_content, state, civ, i) || Math.Abs(_occupiedAt[i]) == _stamp || !OpenLand(world, civ, c))
            {
                continue;
            }

            int comfort = Comfort(state, world, civ, i);
            long untouched = UntouchedSiteFood(world, c);
            if (untouched != NoBound)
            {
                Evaluation bound = Rate(untouched, comfort);
                Far(ref bound, world, civ, from.Cell, c, distance);
                if (!Chooser.WouldKeep(bound.Value))
                {
                    continue;
                }
            }

            Evaluation e = Rate(SiteFood(world, c), comfort);
            Far(ref e, world, civ, from.Cell, c, distance);
            Chooser.Offer(i, e);
        }

        // Only the chosen place needs a known way there.
        int pick = Pick(civ, random);
        if (pick < 0)
        {
            return (from.Cell, default);
        }

        int target = Chooser.KeyAt(pick);
        return _paths.Find(_content, state, civ, world, geo.Index(from.Cell), target) is null
            ? (from.Cell, default)
            : (geo.FromIndex(target), Chooser.EvaluationAt(pick));
    }

    // Moving to free land serves expansion, driving out a foreign camp serves conquest: the strategist's weight and the
    // character apply.
    private static void Weigh(ref Evaluation e, CivilizationState civ, Goal goal) =>
        e.Weigh(GoalWeight(civ, goal), CharacterMultiplier(civ, goal));

    // Staying keeps (100 - moveExpansionSharePercent)% of the distance of the expansion weight and character from neutral.
    private void WeighStay(ref Evaluation e, CivilizationState civ)
    {
        int keep = 100 - _content.Ai.MoveExpansionSharePercent;
        e.Weigh(
            Factor.Neutral + ((GoalWeight(civ, Goal.Expansion) - Factor.Neutral) * keep / 100),
            Factor.Neutral + ((CharacterMultiplier(civ, Goal.Expansion) - Factor.Neutral) * keep / 100));
    }

    // GDD 7.6: a place closer to the strategist's expansion target than the camp is now gets a small boost.
    private void Toward(ref Evaluation e, WorldState world, CivilizationState civ, HexCoord from, HexCoord to)
    {
        int target = civ.ExpansionTarget;
        if (target < 0 || world.Id != WorldId.Earth || target >= world.Geometry.CellCount || _content.Ai.DirectionBonusPercent == 0)
        {
            return;
        }

        HexCoord goal = world.Geometry.FromIndex(target);
        int now = world.Geometry.Distance(from, goal);
        int then = world.Geometry.Distance(to, goal);
        if (then < now)
        {
            e.Apply(FactorId.Direction, (100 + _content.Ai.DirectionBonusPercent) * 10, now - then);
        }
    }

    /// <summary>
    /// How good a place is to live in for <paramref name="civ"/> this turn: the food of its land times comfort.
    /// Valid after this advisor's <see cref="IssueCommands"/> ran this turn (the strategist reads it).
    /// </summary>
    public long SiteValue(GameState state, WorldState world, CivilizationState civ, HexCoord center)
    {
        UseLand(civ);
        return SiteEvaluation(state, world, civ, center).Value;
    }

    private HexCoord[] _far = [];
    private readonly Perception.PathFinder _paths = new();

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

    // Grain per year the camp's producers would grow on its zone, best land first and at most farmersPerCell on a
    // cell, the way the farming system works a village's fields (hundredths).
    private long FarmFood(WorldState world, SettlementState s, int producers)
    {
        int claim = s.Id.Value + 1;
        int count = world.Geometry.Range(s.Cell, Math.Min(_rules.InfluenceMaxRange, world.Geometry.MaxUniqueRadius), _farmArea);
        _farmKeys.Clear();
        for (int k = 0; k < count; k++)
        {
            int i = world.Geometry.Index(_farmArea[k]);
            long perFarmer = world.Map.Claim[i] == claim ? _farming.GrainPerFarmer(world.Map, i) : 0;
            if (perFarmer > 0)
            {
                _farmKeys.Add(-perFarmer);
            }
        }

        _farmKeys.Sort();
        long food = 0;
        int left = producers;
        foreach (long key in _farmKeys)
        {
            int farmers = Math.Min(left, _rules.FarmersPerCell);
            food += -key * farmers;
            left -= farmers;
            if (left == 0)
            {
                break;
            }
        }

        return food;
    }

    private readonly List<long> _farmKeys = [];

    // People of the strata that gather (and would farm).
    private int Producers(SettlementState s)
    {
        int producers = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length && k < _content.Strata.Count; k++)
        {
            producers += _content.Strata[k].Gathers ? people[k] : 0;
        }

        return producers;
    }

    // The command that brings people to the chosen cell: into an empty settlement there, against the camp living
    // there, or a plain move or split.
    private (ICommand Command, AiAction Action) MoveTo(GameState state, WorldState world, SettlementState s, HexCoord cell, int percent)
    {
        int i = world.Geometry.Index(cell);
        if (world.Id == WorldId.Earth && Math.Abs(_occupiedAt[i]) == _stamp && _settlementAt[i] >= 0)
        {
            SettlementState there = state.Settlements[_settlementAt[i]];
            if (there.IsActive)
            {
                return (new ConquerSettlementCommand(_content, s.Id, there.Id), AiAction.Conquer);
            }

            return (new OccupySettlementCommand(_content, s.Id, there.Id, percent), AiAction.Occupy);
        }

        return percent >= 100
            ? (new MoveSettlementCommand(_content, s.Id, cell), AiAction.MoveCamp)
            : (new SplitSettlementCommand(_content, s.Id, cell, percent), AiAction.SplitCamp);
    }

    // Best place within reach: food of its land minus a penalty for distance. Old campsites of the tribe count a bit
    // more (known land); a hungry camp also weighs foreign camps it is strong enough to drive out. Split groups keep
    // away from own camps and never attack.
    private (HexCoord Cell, Evaluation Why) BestSite(
        GameState state, WorldState world, CivilizationState civ, SettlementState from, bool splitting, DeterministicRandom? random)
    {
        HexGlobe geo = world.Geometry;
        int count = geo.Range(from.Cell, Math.Min(_rules.MaxMoveCells, geo.MaxUniqueRadius), _reach);
        Chooser.Clear();
        long attack = splitting ? 0 : SettlementAssets.Warriors(_content, from);
        bool earth = world.Id == WorldId.Earth;
        for (int k = 0; k < count; k++)
        {
            HexCoord c = _reach[k];
            int i = geo.Index(c);
            if (_content.Biomes[world.Map.Biome[i]].IsWater || c == from.Cell || (earth && !Perception.MapMemory.Knows(_content, state, civ, i)))
            {
                continue;
            }

            SettlementState? there = earth && Math.Abs(_occupiedAt[i]) == _stamp && _settlementAt[i] >= 0 ? state.Settlements[_settlementAt[i]] : null;
            bool foreignCamp = there is { IsActive: true } && there.Owner != civ.Id;

            // A foreign camp counts only where the civilization saw it in its last own step: what it remembers from
            // long ago tells it nothing about who lives there now (CLAUDE.md: no hidden knowledge).
            if (foreignCamp && !Perception.MapMemory.SeesNow(state, civ, i))
            {
                continue;
            }

            // GDD 9.1: a pact, an alliance or the truce after a war keeps the camp from driving out theirs.
            if (foreignCamp && state.Treaties.ForbidsAttack(civ.Id, there!.Owner))
            {
                continue;
            }

            if (!foreignCamp && !OpenLand(world, civ, c))
            {
                continue;
            }

            int distance = _ringOf[k];
            int comfort = Comfort(state, world, civ, i);
            bool campsite = there is { IsActive: false } && (there.Owner == civ.Id || there.ClaimCiv == civ.Id.Value);

            // First the same score from untouched land: an upper bound, since gathering only lowers the wild food.
            // A place whose bound the chooser would not keep is skipped without counting its food now.
            long untouched = UntouchedSiteFood(world, c);
            if (untouched != NoBound)
            {
                Evaluation bound = Rate(untouched, comfort);
                Place(ref bound, state, world, civ, from.Cell, c, distance, campsite, foreignCamp ? there!.Owner : (CivId?)null);
                if (!Chooser.WouldKeep(bound.Value))
                {
                    continue;
                }
            }

            Evaluation e = Rate(SiteFood(world, c), comfort);
            Place(ref e, state, world, civ, from.Cell, c, distance, campsite, foreignCamp ? there!.Owner : (CivId?)null);

            // The costly checks run only for places the chooser would keep.
            if (!Chooser.WouldKeep(e.Value))
            {
                continue;
            }

            if (foreignCamp)
            {
                if (attack > 0 && _content.SettlementLevels[there!.Level].Mobile)
                {
                    long defence = ConquerSettlementCommand.DefenceStrength(_content, there);
                    if (attack * 100 >= defence * _rules.AttackAboveStrengthPercent)
                    {
                        e.Note(FactorId.Strength, defence > 0 ? attack * 100 / defence : attack);
                        Chooser.Offer(i, e);
                    }
                }
            }
            else if (Free(world, from, c, splitting))
            {
                Chooser.Offer(i, e);
            }
        }

        int pick = Pick(civ, random);
        return pick < 0 ? (from.Cell, default) : (geo.FromIndex(Chooser.KeyAt(pick)), Chooser.EvaluationAt(pick));
    }

    // The factors of a place a journey away: nearness, the direction, goal and character.
    private void Far(ref Evaluation e, WorldState world, CivilizationState civ, HexCoord from, HexCoord to, int distance)
    {
        e.Apply(FactorId.Distance, _farNearness, distance);
        Toward(ref e, world, civ, from, to);
        Weigh(ref e, civ, Goal.Expansion);
    }

    // The factors of a place within one move: nearness, an old campsite of the tribe, the direction, the opinion of
    // the people living there (a foreign camp), goal and character.
    private void Place(ref Evaluation e, GameState state, WorldState world, CivilizationState civ, HexCoord from, HexCoord to, int distance, bool campsite, CivId? foreigners)
    {
        bool foreignCamp = foreigners is not null;
        e.Apply(FactorId.Distance, _nearness, distance);
        if (campsite)
        {
            e.Apply(FactorId.OwnCampsite, (100 + _rules.OwnCampsiteBonusPercent) * 10, 1);
        }

        // The direction points to free land: it never makes a foreign camp look better.
        if (!foreignCamp)
        {
            Toward(ref e, world, civ, from, to);
        }
        else
        {
            // GDD 8.3: the worse the opinion of them, the readier to drive them out (0.5 at full sympathy, 1.5 at full hatred).
            int sympathy = Diplomacy.Contacts.Sympathy(civ, foreigners!.Value);
            e.Apply(FactorId.Opinion, Math.Max(0, Factor.Neutral - (sympathy * _content.Ai.OpinionSwingPercent / 1000)), sympathy / 100);

            // At war (stage 4d), the enemy's camps are fair game.
            if (state.Treaties.AtWar(civ.Id, foreigners.Value))
            {
                e.Apply(FactorId.War, _content.Treaties.WarRaidPercent * 10, 1);
            }
        }

        Weigh(ref e, civ, foreignCamp ? Goal.Conquest : Goal.Expansion);
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
    // People who go when percent of each stratum leaves (rounded down per stratum, as the split commands do).
    private static int GroupPeople(SettlementState s, int percent)
    {
        int going = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length; k++)
        {
            going += (int)((long)people[k] * percent / 100);
        }

        return going;
    }

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

        // A split group also keeps out of the zones of its own settlements: the zone of the bigger settlement takes the
        // land, and a new camp inside it would gather nothing (found in PR C measurements: settlers starved).
        int i = world.Geometry.Index(c);
        return _occupiedAt[i] != _stamp
            && (!splitting || (_nearOwnAt[i] != _civStamp && world.Map.Owner[i] != from.Owner.Value));
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

    // Food per year the land around a cell gives now to this civilization's gatherers, wild food taken into account
    // (hundredths). Cached for the turn: wild food does not change while the AI decides. Only Earth is cached; other
    // worlds come with space travel.
    private long SiteFood(WorldState world, HexCoord center)
    {
        if (world.Id != WorldId.Earth)
        {
            return ComputeSiteFood(world, center);
        }

        int cell = world.Geometry.Index(center);
        Land land = _land;
        if (land.Site.Length != world.Geometry.CellCount)
        {
            land.Site = new long[world.Geometry.CellCount];
            land.SiteStamp = new long[world.Geometry.CellCount];
        }

        if (land.SiteStamp[cell] != _stamp)
        {
            land.Site[cell] = ComputeSiteFood(world, center);
            land.SiteStamp[cell] = _stamp;
        }

        return land.Site[cell];
    }

    // How good a place is to live in: the food its land gives, times how pleasant its temperature is for this
    // civilization (GDD 8.3: the AI weighs places; nothing here is a fixed "flee the winter" rule).
    private Evaluation SiteEvaluation(GameState state, WorldState world, CivilizationState civ, HexCoord center) =>
        Rate(SiteFood(world, center), Comfort(state, world, civ, world.Geometry.Index(center)));

    private static Evaluation Rate(long food, int comfort)
    {
        Evaluation e = Evaluation.Of(food);
        e.Note(FactorId.Food, food);
        e.Apply(FactorId.Comfort, comfort * 10, comfort);
        return e;
    }

    private const long NoBound = -1;

    // Food per year the land around a cell would give if nobody had gathered there: SiteFood never exceeds it. Kept
    // until the climate changes the land; NoBound with live seasons (the land changes every turn) and off Earth.
    private long UntouchedSiteFood(WorldState world, HexCoord center)
    {
        if (world.Id != WorldId.Earth || world.Map.LiveSeasons)
        {
            return NoBound;
        }

        _ = CellFood(world);
        Land land = _land;
        int cell = world.Geometry.Index(center);
        if (land.Untouched.Length != world.Geometry.CellCount)
        {
            land.Untouched = new long[world.Geometry.CellCount];
            land.UntouchedVersion = new long[world.Geometry.CellCount];
        }

        if (land.UntouchedVersion[cell] != land.Version)
        {
            land.Untouched[cell] = ComputeSiteFood(world, center, wild: false);
            land.UntouchedVersion[cell] = land.Version;
        }

        return land.Untouched[cell];
    }

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

    private long ComputeSiteFood(WorldState world, HexCoord center, bool wild = true)
    {
        WorldMap map = world.Map;
        HexGlobe geo = world.Geometry;
        long[] full = CellFood(world);
        bool fishing = world.Id == WorldId.Earth && _land.Benefits.FromWater;
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

            food += wild ? full[cell] * map.WildFood[cell] / WorldMap.FullWildFood : full[cell];
            geo.GetNeighbors(geo.FromIndex(cell), neighbours);
            foreach (HexCoord nc in neighbours)
            {
                int next = geo.Index(nc);
                int step = _influence.EnterCost(map, next);

                // Fishers also work the water next to the land of the place (zones do not cross water), once per cell.
                if (step < 0 && fishing && _costStamp[next] != _costRun && _content.Biomes[map.Biome[next]].IsWater)
                {
                    _costStamp[next] = _costRun;
                    _costAt[next] = int.MaxValue;
                    food += wild ? full[next] * map.WildFood[next] / WorldMap.FullWildFood : full[next];
                    continue;
                }

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

    // Food of every cell when untouched for the current civilization's gathering technologies; computed once per set
    // and climate, or every turn when seasons are live. Other worlds (space travel) are computed without technologies.
    private long[] CellFood(WorldState world)
    {
        Land land = world.Id == WorldId.Earth ? _land : _otherWorld;
        bool live = world.Map.LiveSeasons;

        // The climate changes every few centuries (stage 1c-B): then the food of the land changes too.
        if (land.Cell.Length != world.Geometry.CellCount || land.Climate != world.Climate.Year || land.World != world.Id.Value || (live && land.CellStamp != _stamp))
        {
            long[] food = land.Cell.Length == world.Geometry.CellCount ? land.Cell : new long[world.Geometry.CellCount];
            for (int i = 0; i < food.Length; i++)
            {
                // With live seasons the AI plans for this turn and the next one.
                food[i] = live
                    ? (_yields.CellFoodPerYear(world.Map, i, false, land.Benefits).Hundredths + _yields.CellFoodPerYear(world.Map, i, true, land.Benefits).Hundredths) / 2
                    : _yields.CellFoodPerYear(world.Map, i, false, land.Benefits).Hundredths;
            }

            land.Cell = food;
            land.CellStamp = _stamp;
            land.Version++;
            land.Climate = world.Climate.Year;
            land.World = world.Id.Value;
        }

        return land.Cell;
    }

    // Picks the land view of a civilization's gathering technologies; computed again only after it learned something.
    private void UseLand(CivilizationState civ)
    {
        int c = civ.Id.Value;
        if (_landCiv == c && _landKnownOf[c] == civ.KnownCount)
        {
            return;
        }

        _landCiv = c;
        while (_landKeyOf.Count <= c)
        {
            _landKeyOf.Add(null);
            _landKnownOf.Add(-1);
        }

        if (_landKeyOf[c] is null || _landKnownOf[c] != civ.KnownCount)
        {
            var key = new System.Text.StringBuilder();
            foreach (TechnologyDefinition t in _content.Technologies)
            {
                if (t.Gather.Count > 0 && civ.Knows(t.Id))
                {
                    key.Append(t.Id).Append(',');
                }
            }

            _landKeyOf[c] = key.ToString();
            _landKnownOf[c] = civ.KnownCount;
        }

        string k = _landKeyOf[c]!;
        if (!_lands.TryGetValue(k, out Land? land))
        {
            land = new Land(Research.TechBenefits.Of(_content, civ));
            _lands.Add(k, land);
        }

        _land = land;
    }

    private readonly Land _otherWorld;

    // Food of the land for one set of gathering technologies: per cell, around each place this turn, and untouched.
    private sealed class Land(Research.TechBenefits benefits)
    {
        public Research.TechBenefits Benefits { get; } = benefits;

        public long[] Cell { get; set; } = [];

        public long CellStamp { get; set; } = -1;

        public long Climate { get; set; } = long.MinValue;

        public int World { get; set; } = -1;

        public long Version { get; set; }

        public long[] Site { get; set; } = [];

        public long[] SiteStamp { get; set; } = [];

        public long[] Untouched { get; set; } = [];

        public long[] UntouchedVersion { get; set; } = [];
    }
}
