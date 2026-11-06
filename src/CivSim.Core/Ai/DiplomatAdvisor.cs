using System;
using System.Collections.Generic;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Economy;
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

    private readonly GameContent _content = content;
    private readonly SettlementsByCiv _byCiv = new();
    private readonly Dictionary<int, Amount[]> _prices = [];
    private int[] _near = [];
    private int[] _gateA = [];
    private int[] _gateB = [];
    private long[] _seen = [];
    private long[] _own = [];
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

            Evaluation e = Evaluate(state, to, p.From, p.Kind);
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
            bool planned = Simulation.WorldClock.Steps(civ) && (state.Month + (civ.Id.Value * (long)_content.Ai.ReviewStaggerYears * 12)) % every < civ.StepMonths;
            bool alarmed = (civ.Alarms & (Simulation.Alarm.WarDeclared | Simulation.Alarm.Attacked)) != 0;
            if (!planned && !alarmed)
            {
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
    private Evaluation Evaluate(GameState state, CivilizationState civ, CivId other, TreatyKind kind)
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
                e.Apply(FactorId.Cause, Cause(r) ? Factor.Neutral : 0, Cause(r) ? 1 : 0);
                e.Apply(FactorId.Strength, rules.Advantage, Percent(_own[x], EnemyStrength(state, civ, x, other)));
                if (Binds(state, civ.Id, other))
                {
                    e.Apply(FactorId.Treaty, Math.Max(100, Factor.Neutral - civ.Trait(Trait.Loyalty)), civ.Trait(Trait.Loyalty));
                }

                e.Weigh(GoalWeight(civ, Goal.Conquest), CharacterMultiplier(civ, Goal.Conquest));
                break;
            case TreatyKind.Peace:
                Treaty? war = state.Treaties.Find(civ.Id, other, TreatyKind.War);
                long warYears = war is null ? 0 : (state.Month - war.SinceMonth) / 12;
                e.Apply(FactorId.WarYears, rules.WarWeariness, warYears);
                e.Apply(FactorId.Opinion, rules.PeaceOpinion, sympathy);
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
                Consider(state, civ, r.Other, y, TreatyKind.Peace, rules.ProposeAbove);
                continue;
            }

            Consider(state, civ, r.Other, y, TreatyKind.Trade, rules.ProposeAbove);
            Consider(state, civ, r.Other, y, TreatyKind.NonAggression, rules.ProposeAbove);
            Consider(state, civ, r.Other, y, TreatyKind.DefensiveAlliance, rules.ProposeAbove);
            if (Treaties.CannotDeclareWar(state, civ.Id, r.Other) is null)
            {
                Evaluation war = Evaluate(state, civ, r.Other, TreatyKind.War);
                if (war.Value >= rules.DeclareWarAbove)
                {
                    Chooser.Offer((y * 8) + (int)TreatyKind.War, war);
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
        if (kind == TreatyKind.War)
        {
            Issue(state, queue, civ.Id, new DeclareWarCommand(_content, other), AiAction.DeclareWar, -1, other.Value, why);
        }
        else
        {
            Issue(state, queue, civ.Id, new ProposeTreatyCommand(_content, other, kind), AiAction.ProposeTreaty, -1, other.Value, why);
        }
    }

    private void Consider(GameState state, CivilizationState civ, CivId other, int y, TreatyKind kind, int threshold)
    {
        if (Treaties.CannotPropose(_content, state, civ.Id, other, kind) is not null)
        {
            return;
        }

        Evaluation e = Evaluate(state, civ, other, kind);
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
            _own = new long[_n];
        }

        _byCiv.Build(state);
        for (int x = 0; x < _n; x++)
        {
            _own[x] = 0;
            foreach (int s in _byCiv.Of(x))
            {
                _own[x] += SettlementAssets.Warriors(_content, state.Settlements[s]);
            }

            for (int y = 0; y < _n; y++)
            {
                int k = (x * _n) + y;
                _near[k] = x == y ? 0 : _byCiv.Nearest(state, x, y, out _gateA[k], out _gateB[k]);
                _seen[k] = x == y ? 0 : SeenWarriors(state, state.Civilizations[x], y);
            }
        }

        _prices.Clear();
        _networks = null;
    }

    // Warriors of civilization `y` in camps `civ` saw in its last own step: the AI knows only what it saw, and only what
    // it sees now tells it how strong they are today.
    private long SeenWarriors(GameState state, CivilizationState civ, int y)
    {
        long sum = 0;
        WorldState earth = state.Worlds[0];
        foreach (int s in _byCiv.Of(y))
        {
            SettlementState st = state.Settlements[s];
            int cell = earth.Geometry.Index(st.Cell);
            if (Perception.MapMemory.SeesNow(state, civ, cell))
            {
                sum += SettlementAssets.Warriors(_content, st);
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

            int f = state.Treaties.AtWar(civ.Id, r.Other)
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

    // GDD 9.3: no war without a cause: a settlement of ours they live in, or an attack of theirs to avenge.
    private static bool Cause(Relation? r)
    {
        if (r is null)
        {
            return false;
        }

        foreach (RelationMemory m in r.Memories)
        {
            if ((m.Kind == RelationEvent.BorderDispute && m.IsOngoing) || m.Kind == RelationEvent.Attack)
            {
                return true;
            }
        }

        return false;
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
