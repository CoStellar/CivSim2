using System;
using System.Collections.Generic;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Economy;
using CivSim.Core.Episodes;
using CivSim.Core.Numerics;
using CivSim.Core.Population;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// The diplomat advisor (GDD 8.1, 9.1; stage 4d). Every turn it answers the proposals made to its civilization; every
/// <see cref="TreatyRules.ReviewEveryYears"/> years (staggered over civilizations) it weighs one step towards each
/// civilization it has met: a trade agreement, a non-aggression pact, a defensive alliance, war, or peace when at war,
/// and takes one of the best that pass the threshold. A proposal is weighed by the other side with the very same
/// evaluation (GDD 9.1), so both sides want a treaty for the same reasons, each from its own view. The AI judges
/// strength only from the camps it has seen (no hidden knowledge) and prices from both gateways (merchants know them).
/// </summary>
public sealed class DiplomatAdvisor(GameContent content) : Advisor(content, AdvisorKind.Diplomat)
{
    /// <summary>Score of a step before its factors, per mille.</summary>
    public const long Base = 1000;

    // Keys of the wars of greed among the steps of one review (key = civilization index × 8 + kind; treaty kinds use 0–4).
    private const int LootKey = 6;
    private const int ConquestKey = 7;

    private readonly GameContent _content = content;
    private readonly SettlementsByCiv _byCiv = new();
    private readonly Dictionary<int, Amount[]> _prices = [];
    private int[] _near = [];
    private int[] _gateA = [];
    private int[] _gateB = [];
    private long[] _seen = [];
    private long[] _own = [];
    private long[] _seenFood = [];
    private long[] _food = [];
    private int[] _fed = [];
    private int[] _conquest = [];
    private int _n;
    private List<List<int>>? _networks;
    private int[] _networkOf = [];

    /// <inheritdoc />
    public override void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        int n = state.Civilizations.Count;
        if (n < 2)
        {
            return;
        }

        bool prepared = false;
        TreatyBook book = state.Treaties;
        for (int k = 0; k < book.Proposals.Count; k++)
        {
            TreatyProposal p = book.Proposals[k];
            CivilizationState? to = state.FindCivilization(p.To);
            if (p.Status != ProposalStatus.Pending || to is not { IsAlive: true } || to.Controller != PlayerId.Ai)
            {
                continue;
            }

            if (!prepared)
            {
                Prepare(state);
                prepared = true;
            }

            Evaluation e = Evaluate(state, to, p.From, p.Kind, p.Terms);
            bool accept = e.Value >= _content.Treaties.AcceptAbove && Treaties.CannotSign(_content, state, p.From, p.To, p.Kind) is null;
            Issue(state, queue, to.Id, new AnswerProposalCommand(_content, p.From, p.Kind, accept),
                accept ? AiAction.AcceptTreaty : AiAction.DeclineTreaty, -1, p.From.Value, e);
        }

        long every = Math.Max(1, _content.Treaties.ReviewEveryYears) * 12L;
        for (int x = 0; x < n; x++)
        {
            CivilizationState civ = state.Civilizations[x];
            if (!civ.IsAlive || civ.Controller != PlayerId.Ai || civ.Relations.Count == 0)
            {
                continue;
            }

            // Planned review (proactive): in the own step that holds the civilization's review month, once every
            // reviewEveryYears (spread over civilizations). Reaction (reactive, ADR 0018): at once when someone declared
            // war on it or attacked one of its camps, whatever its rhythm.
            // The government sets the pace (GDD 7.2): a council reviews less often, a chief more often.
            long own = Math.Max(1, every * Governance.Governments.ReviewPercent(_content, civ) / 100);
            bool planned = Simulation.WorldClock.Steps(civ) && (state.Month + (civ.Id.Value * (long)_content.Ai.ReviewStaggerYears * 12)) % own < civ.StepMonths;
            bool alarmed = (civ.Alarms & (Simulation.Alarm.WarDeclared | Simulation.Alarm.Attacked)) != 0;
            if (!planned && !alarmed)
            {
                // ADR 0022: between campaigns a side worn out, or one far enough ahead to ask for terms, thinks of peace
                // in any world step (monthly while campaigns run), not only in its own long steps.
                if (WantsPeace(state, civ))
                {
                    if (!prepared)
                    {
                        Prepare(state);
                        prepared = true;
                    }

                    ReviewWars(state, queue, civ);
                }

                continue;
            }

            if (!prepared)
            {
                Prepare(state);
                prepared = true;
            }

            Review(state, queue, civ);
        }
    }

    /// <summary>
    /// The score <paramref name="civ"/> gives <paramref name="kind"/> with <paramref name="other"/> now (GDD 8.3): the
    /// same for proposing and for answering. Call only inside <see cref="IssueCommands"/> (it reads the turn's facts).
    /// </summary>
    private Evaluation Evaluate(GameState state, CivilizationState civ, CivId other, TreatyKind kind, PeaceTerms terms = default)
    {
        TreatyRules rules = _content.Treaties;
        int x = state.IndexOfCivilization(civ.Id);
        int y = state.IndexOfCivilization(other);
        Relation? r = civ.Relation(other);
        int sympathy = r is { HasMet: true } ? r.Sympathy : 0;
        int trust = r is { HasMet: true } ? r.Trust : 0;
        Evaluation e = Evaluation.Of(Base);
        if (x < 0 || y < 0)
        {
            e.Apply(FactorId.Opinion, 0);
            return e;
        }

        int near = _near[(x * _n) + y];
        switch (kind)
        {
            case TreatyKind.Trade:
                e.Apply(FactorId.Opinion, rules.Opinion, sympathy);
                e.Apply(FactorId.Distance, near <= rules.ReachCells ? Factor.Neutral : 0, near);
                e.Apply(FactorId.TradeGain, rules.TradeGain, near <= rules.ReachCells ? TradeGain(state, x, y, near) : 0);
                e.Weigh(GoalWeight(civ, Goal.Prosperity), CharacterMultiplier(civ, Goal.Prosperity));
                break;
            case TreatyKind.NonAggression:
                e.Apply(FactorId.Opinion, rules.Opinion, sympathy);
                e.Apply(FactorId.Trust, rules.Trust, trust);
                e.Apply(FactorId.Distance, near <= rules.ReachCells ? Factor.Neutral : 0, near);
                e.Apply(FactorId.Threat, rules.Threat, Percent(_seen[(x * _n) + y], _own[x]));
                e.Weigh(GoalWeight(civ, Goal.Security), CharacterMultiplier(civ, Goal.Security));
                break;
            case TreatyKind.DefensiveAlliance:
                e.Apply(FactorId.Opinion, rules.Opinion, sympathy);
                e.Apply(FactorId.Trust, rules.AllianceTrust, trust);
                e.Apply(FactorId.Threat, OtherThreat(state, civ, x, y), 0);
                e.Weigh(GoalWeight(civ, Goal.Security), 500 + civ.Trait(Trait.Loyalty));
                break;
            case TreatyKind.War:
                e.Apply(FactorId.Opinion, rules.Hostility, sympathy);
                bool cause = Treaties.HasGrievance(state, civ.Id, other);
                e.Apply(FactorId.Cause, cause ? Factor.Neutral : 0, cause ? 1 : 0);
                e.Apply(FactorId.Strength, rules.Advantage, Percent(_own[x], EnemyStrength(state, civ, x, other)));
                HoldBack(ref e, state, civ, other);
                e.Weigh(GoalWeight(civ, Goal.Conquest), CharacterMultiplier(civ, Goal.Conquest));
                break;
            case TreatyKind.Peace:
                Treaty? war = state.Treaties.Find(civ.Id, other, TreatyKind.War);
                e.Apply(FactorId.Weariness, rules.WarWeariness, war is null ? 0 : Wars.WearinessOf(war, civ.Id));
                e.Apply(FactorId.WarScore, rules.WarScore, war is null ? 0 : Wars.ScoreFor(war, civ.Id));
                e.Apply(FactorId.Opinion, rules.PeaceOpinion, sympathy);
                e.Apply(FactorId.Terms, rules.PeaceTerms, Wars.TermsWorth(_content, terms, civ.Id));
                e.Weigh(GoalWeight(civ, Goal.Security), CharacterMultiplier(civ, Goal.Security));
                break;
        }

        return e;
    }

    // One review: the best steps towards every civilization met, one of them taken.
    private void Review(GameState state, CommandQueue queue, CivilizationState civ)
    {
        TreatyBook book = state.Treaties;
        TreatyRules rules = _content.Treaties;
        Chooser.Clear();
        foreach (Relation r in civ.Relations)
        {
            int y = state.IndexOfCivilization(r.Other);
            if (!r.HasMet || y < 0)
            {
                continue;
            }

            if (book.AtWar(civ.Id, r.Other))
            {
                // GDD 9.3: a side worn out by the war looks for peace whatever the score.
                Treaty war = book.Find(civ.Id, r.Other, TreatyKind.War)!;
                int threshold = Wars.WearinessOf(war, civ.Id) >= _content.War.PeaceAbove ? 0 : rules.ProposeAbove;
                Consider(state, civ, r.Other, y, TreatyKind.Peace, threshold);
                continue;
            }

            Consider(state, civ, r.Other, y, TreatyKind.Trade, rules.ProposeAbove);
            Consider(state, civ, r.Other, y, TreatyKind.NonAggression, rules.ProposeAbove);
            Consider(state, civ, r.Other, y, TreatyKind.DefensiveAlliance, rules.ProposeAbove);
            if (Treaties.CannotDeclareWar(state, civ.Id, r.Other) is null && civ.PendingGovernment == CivilizationState.NoGovernment
                && !Governance.Stability.InAnarchy(civ, state.Month))
            {
                Evaluation war = Evaluate(state, civ, r.Other, TreatyKind.War);
                if (war.Value >= rules.DeclareWarAbove)
                {
                    Chooser.Offer((y * 8) + (int)TreatyKind.War, war);
                }

                // Wars of greed (AI fixes after stage 4g): for the food of a richer, weaker neighbour, or for a village of
                // theirs our army would take.
                Evaluation loot = EvaluateLoot(state, civ, r.Other);
                if (loot.Value >= rules.DeclareWarAbove)
                {
                    Chooser.Offer((y * 8) + LootKey, loot);
                }

                Evaluation land = EvaluateConquest(state, civ, r.Other);
                if (land.Value >= rules.DeclareWarAbove)
                {
                    Chooser.Offer((y * 8) + ConquestKey, land);
                }
            }
        }

        int pick = Pick(civ, RandomFor(state, civ.Id.Value));
        if (pick < 0)
        {
            return;
        }

        int key = Chooser.KeyAt(pick);
        CivId other = state.Civilizations[key / 8].Id;
        var kind = (TreatyKind)(key % 8);
        Evaluation why = Chooser.EvaluationAt(pick);
        if (key % 8 == LootKey)
        {
            Issue(state, queue, civ.Id, new DeclareWarCommand(_content, other, WarGoal.Loot), AiAction.DeclareWar, -1, other.Value, why);
        }
        else if (key % 8 == ConquestKey)
        {
            int target = _conquest[key / 8];
            Issue(state, queue, civ.Id, new DeclareWarCommand(_content, other, WarGoal.Land, target), AiAction.DeclareWar, target, other.Value, why);
        }
        else if (kind == TreatyKind.War)
        {
            // GDD 9.3: the goal follows the cause: land when they hold a settlement we claim, otherwise revenge.
            int claimed = Treaties.ClaimedSettlement(state, civ.Id, other);
            WarGoal goal = claimed >= 0 ? WarGoal.Land : WarGoal.Revenge;
            Issue(state, queue, civ.Id, new DeclareWarCommand(_content, other, goal, claimed), AiAction.DeclareWar, -1, other.Value, why);
        }
        else
        {
            PeaceTerms terms = kind == TreatyKind.Peace ? Wars.TermsFor(_content, state, civ.Id, other) : default;
            Issue(state, queue, civ.Id, new ProposeTreatyCommand(_content, other, kind, terms), AiAction.ProposeTreaty, -1, other.Value, why);
        }
    }

    // A war in which this side has no campaign out and is worn out or far enough ahead (or behind) for terms.
    private bool WantsPeace(GameState state, CivilizationState civ)
    {
        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind == TreatyKind.War && t.Involves(civ.Id) && !state.Episodes.Campaigning(civ.Id, t.Other(civ.Id))
                && (Wars.WearinessOf(t, civ.Id) >= _content.War.PeaceAbove || Math.Abs(t.Score) >= _content.War.TermsAboveScore))
            {
                return true;
            }
        }

        return false;
    }

    // Only peace, with the wars this side wants to end (worn out: whatever the score; otherwise at proposeAbove).
    private void ReviewWars(GameState state, CommandQueue queue, CivilizationState civ)
    {
        Chooser.Clear();
        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind != TreatyKind.War || !t.Involves(civ.Id))
            {
                continue;
            }

            CivId other = t.Other(civ.Id);
            int y = state.IndexOfCivilization(other);
            if (y < 0 || state.Episodes.Campaigning(civ.Id, other))
            {
                continue;
            }

            int threshold = Wars.WearinessOf(t, civ.Id) >= _content.War.PeaceAbove ? 0 : _content.Treaties.ProposeAbove;
            Consider(state, civ, other, y, TreatyKind.Peace, threshold);
        }

        int pick = Pick(civ, RandomFor(state, civ.Id.Value));
        if (pick < 0)
        {
            return;
        }

        int key = Chooser.KeyAt(pick);
        CivId target = state.Civilizations[key / 8].Id;
        Evaluation why = Chooser.EvaluationAt(pick);
        PeaceTerms terms = Wars.TermsFor(_content, state, civ.Id, target);
        Issue(state, queue, civ.Id, new ProposeTreatyCommand(_content, target, TreatyKind.Peace, terms), AiAction.ProposeTreaty, -1, target.Value, why);
    }

    private void Consider(GameState state, CivilizationState civ, CivId other, int y, TreatyKind kind, int threshold)
    {
        if (Treaties.CannotPropose(_content, state, civ.Id, other, kind) is not null)
        {
            return;
        }

        PeaceTerms terms = kind == TreatyKind.Peace ? Wars.TermsFor(_content, state, civ.Id, other) : default;
        Evaluation e = Evaluate(state, civ, other, kind, terms);
        if (e.Value >= threshold)
        {
            Chooser.Offer((y * 8) + (int)kind, e);
        }
    }

    // The facts of this turn: nearest settlements of every pair, warriors seen and own.
    private void Prepare(GameState state)
    {
        _n = state.Civilizations.Count;
        int pairs = _n * _n;
        if (_near.Length != pairs)
        {
            _near = new int[pairs];
            _gateA = new int[pairs];
            _gateB = new int[pairs];
            _seen = new long[pairs];
            _seenFood = new long[pairs];
            _own = new long[_n];
            _food = new long[_n];
            _fed = new int[_n];
            _conquest = new int[_n];
        }

        _byCiv.Build(state);
        for (int x = 0; x < _n; x++)
        {
            _own[x] = 0;
            _food[x] = 0;
            long eaten = 0;
            long needed = 0;
            foreach (int s in _byCiv.Of(x))
            {
                SettlementState st = state.Settlements[s];
                _own[x] += SettlementAssets.Warriors(_content, st);
                _food[x] += Campaigns.FoodIn(_content, st.Stock).Hundredths;
                eaten += Math.Min(st.FoodEaten, st.FoodNeeded);
                needed += st.FoodNeeded;
            }

            _fed[x] = needed <= 0 ? 100 : (int)(eaten * 100 / needed);

            for (int y = 0; y < _n; y++)
            {
                int k = (x * _n) + y;
                _near[k] = x == y ? 0 : _byCiv.Nearest(state, x, y, out _gateA[k], out _gateB[k]);
                _seen[k] = x == y ? 0 : SeenWarriors(state, state.Civilizations[x], y, out _seenFood[k]);
            }
        }

        _prices.Clear();
        _networks = null;
    }

    // Warriors of civilization `y` in camps `civ` saw in its last own step: the AI knows only what it saw, and only what
    // it sees now tells it how strong they are today.
    private long SeenWarriors(GameState state, CivilizationState civ, int y, out long food)
    {
        long sum = 0;
        food = 0;
        WorldState earth = state.Worlds[0];
        foreach (int s in _byCiv.Of(y))
        {
            SettlementState st = state.Settlements[s];
            int cell = earth.Geometry.Index(st.Cell);
            if (Perception.MapMemory.SeesNow(state, civ, cell))
            {
                sum += SettlementAssets.Warriors(_content, st);
                food += Campaigns.FoodIn(_content, st.Stock).Hundredths; // the stores are in sight too
            }
        }

        return sum;
    }

    // What trade between the gateways of x and y would gain, percent.
    private int TradeGain(GameState state, int x, int y, int near)
    {
        int k = (x * _n) + y;
        Amount[]? pa = PricesAt(state, _gateA[k]);
        Amount[]? pb = PricesAt(state, _gateB[k]);
        return pa is null || pb is null ? 0 : ForeignTradeSystem.GainPercent(_content, pa, pb, TradeNetworks.ArrivingPercent(_content, near));
    }

    private Amount[]? PricesAt(GameState state, int settlement)
    {
        if (settlement < 0)
        {
            return null;
        }

        if (_networks is null)
        {
            _networks = TradeNetworks.Build(_content, state);
            if (_networkOf.Length < state.Settlements.Count)
            {
                _networkOf = new int[state.Settlements.Count];
            }

            Array.Fill(_networkOf, -1);
            for (int i = 0; i < _networks.Count; i++)
            {
                foreach (int m in _networks[i])
                {
                    _networkOf[m] = i;
                }
            }
        }

        int network = _networkOf[settlement];
        if (network < 0)
        {
            return null;
        }

        if (!_prices.TryGetValue(network, out Amount[]? prices))
        {
            prices = Market.Prices(_content, state, _networks[network]);
            _prices[network] = prices;
        }

        return prices;
    }

    // The strongest threat from a third civilization `civ` has met, as a factor: an enemy at war counts in full.
    private int OtherThreat(GameState state, CivilizationState civ, int x, int y)
    {
        int threat = _content.Treaties.Threat.Evaluate(0);
        foreach (Relation r in civ.Relations)
        {
            int z = state.IndexOfCivilization(r.Other);
            if (!r.HasMet || z < 0 || z == y || !state.Civilizations[z].IsAlive)
            {
                continue;
            }

            // A common enemy counts in full (AI fixes after stage 4g): a people at war with us or with the partner (wars are
            // known to all), or one we have a grievance against.
            bool enemy = state.Treaties.AtWar(civ.Id, r.Other) || state.Treaties.AtWar(state.Civilizations[y].Id, r.Other)
                || Treaties.HasGrievance(state, civ.Id, r.Other);
            int f = enemy
                ? _content.Treaties.Threat.Evaluate(long.MaxValue / 2)
                : _content.Treaties.Threat.Evaluate(Percent(_seen[(x * _n) + z], _own[x]));
            threat = Math.Max(threat, f);
        }

        return threat;
    }

    // Seen warriors of the enemy and of its defensive allies `civ` has met (GDD 8.3: B's allies).
    private long EnemyStrength(GameState state, CivilizationState civ, int x, CivId enemy)
    {
        long sum = _seen[(x * _n) + state.IndexOfCivilization(enemy)];
        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind != TreatyKind.DefensiveAlliance || !t.Involves(enemy))
            {
                continue;
            }

            CivId ally = t.Other(enemy);
            int z = state.IndexOfCivilization(ally);
            if (ally != civ.Id && z >= 0 && civ.Relation(ally) is { HasMet: true })
            {
                sum += _seen[(x * _n) + z];
            }
        }

        return sum;
    }

    // A war for loot (AI fixes after stage 4g; GDD 9.3 allows it without a cause): a hungry people against a neighbour
    // whose stores it sees fuller than its own and whom it can beat. The witnesses remember it the most (data/war.json).
    private Evaluation EvaluateLoot(GameState state, CivilizationState civ, CivId other)
    {
        TreatyRules rules = _content.Treaties;
        int x = state.IndexOfCivilization(civ.Id);
        int y = state.IndexOfCivilization(other);
        int k = (x * _n) + y;
        Evaluation e = Evaluation.Of(Base);
        e.Apply(FactorId.Opinion, rules.Greed, civ.Relation(other)?.Sympathy ?? 0);
        e.Apply(FactorId.Distance, _near[k] <= rules.ReachCells ? Factor.Neutral : 0, _near[k]);
        e.Apply(FactorId.Fed, rules.LootNeed, _fed[x]);
        e.Apply(FactorId.Loot, rules.LootGain, Percent(_seenFood[k], _food[x]));
        e.Apply(FactorId.Strength, rules.Advantage, Percent(_own[x], EnemyStrength(state, civ, x, other)));
        Greedy(ref e, state, civ, other);
        e.Weigh(GoalWeight(civ, Goal.Conquest), CharacterMultiplier(civ, Goal.Conquest));
        return e;
    }

    // A war for land (conquest, AI fixes after stage 4g): the enemy village in sight and within the commander's reach that
    // our army, gathered as the commander would gather it, would most likely take; worth more the more we want to expand.
    private Evaluation EvaluateConquest(GameState state, CivilizationState civ, CivId other)
    {
        int x = state.IndexOfCivilization(civ.Id);
        int y = state.IndexOfCivilization(other);
        int chance = BestConquest(state, civ, x, y, out _conquest[y]);
        Evaluation e = Evaluation.Of(Base);
        if (_conquest[y] < 0)
        {
            e.Apply(FactorId.WinChance, 0, 0);
            return e;
        }

        int expansion = GoalWeight(civ, Goal.Expansion);
        e.Apply(FactorId.Opinion, _content.Treaties.Greed, civ.Relation(other)?.Sympathy ?? 0);
        e.Apply(FactorId.WinChance, _content.Treaties.ConquestChance, chance);
        e.Apply(FactorId.Expansion, expansion, expansion);
        Greedy(ref e, state, civ, other);
        e.Weigh(GoalWeight(civ, Goal.Conquest), CharacterMultiplier(civ, Goal.Conquest));
        return e;
    }

    // Greed never breaks a treaty (the first 100 games with greed: a war in three broke a pact); unrest holds it back.
    private void Greedy(ref Evaluation e, GameState state, CivilizationState civ, CivId other)
    {
        if (Binds(state, civ.Id, other))
        {
            e.Apply(FactorId.Treaty, 0, civ.Trait(Trait.Loyalty));
        }

        if (civ.Stability < _content.Stability.UnrestBelow)
        {
            e.Apply(FactorId.Stability, _content.Ai.UnrestWarPercent * 10, civ.Stability);
        }
    }

    // A treaty that war would break, and unrest (stage 4f PR 5), hold a war back.
    private void HoldBack(ref Evaluation e, GameState state, CivilizationState civ, CivId other)
    {
        if (Binds(state, civ.Id, other))
        {
            e.Apply(FactorId.Treaty, Math.Max(100, Factor.Neutral - civ.Trait(Trait.Loyalty)), civ.Trait(Trait.Loyalty));
        }

        if (civ.Stability < _content.Stability.UnrestBelow)
        {
            e.Apply(FactorId.Stability, _content.Ai.UnrestWarPercent * 10, civ.Stability);
        }
    }

    // The village of civilization `y` that `civ` would most likely take, as its commander sees it, and that chance, percent;
    // `target` = -1 when none is in sight and in reach.
    private int BestConquest(GameState state, CivilizationState civ, int x, int y, out int target)
    {
        target = -1;
        int best = -1;
        int limit = Campaigns.Era(_content, civ).ArmyLimit;
        foreach (int t in _byCiv.Of(y))
        {
            SettlementState village = state.Settlements[t];
            if (_content.SettlementLevels[village.Level].Mobile || village.World != WorldId.Earth)
            {
                continue;
            }

            WorldState world = state.Worlds[0];
            SettlementState? home = null;
            int nearest = int.MaxValue;
            foreach (int s in _byCiv.Of(x))
            {
                SettlementState own = state.Settlements[s];
                int d = own.World == world.Id ? world.Geometry.Distance(own.Cell, village.Cell) : int.MaxValue;
                if (d < nearest && Campaigns.Sendable(_content, own) > 0)
                {
                    nearest = d;
                    home = own;
                }
            }

            if (home is null || nearest > _content.Perception.JourneyCells)
            {
                continue;
            }

            // The army: warriors of our settlements within the muster range of the one nearest to the village.
            int warriors = 0;
            long attack = 0;
            foreach (int s in _byCiv.Of(x))
            {
                SettlementState own = state.Settlements[s];
                int n = Math.Min(Campaigns.Sendable(_content, own), limit - warriors);
                if (n > 0 && own.World == world.Id && world.Geometry.Distance(home.Cell, own.Cell) <= _content.War.MusterCells)
                {
                    warriors += n;
                    attack += Campaigns.ArmedStrength(_content, civ, own, n, useUp: false);
                }
            }

            if (warriors < _content.War.MinWarriors || Campaigns.Expect(_content, state, civ, village, warriors, attack, 100, -1) is not { } battle)
            {
                continue;
            }

            if (battle.ChancePercent > best)
            {
                best = battle.ChancePercent;
                target = village.Id.Value;
            }
        }

        return best;
    }

    private bool Binds(GameState state, CivId civ, CivId other)
    {
        for (int k = 0; k < _content.Treaties.Kinds.Count; k++)
        {
            if (_content.Treaties.Kinds[k].BindsAgainstWar && state.Treaties.Has(civ, other, (TreatyKind)k))
            {
                return true;
            }
        }

        return false;
    }

    // a against b in percent; very large when b is zero.
    private static long Percent(long a, long b) => b <= 0 ? (a <= 0 ? 100 : 1000) : a * 100 / b;
}
