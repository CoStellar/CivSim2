using System;
using System.Collections.Generic;
using CivSim.Core.Ai.Utility;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// The states and treaties between two civilizations (GDD 9.1; stage 4d). Peace without treaties is the default once
/// two civilizations have met; war can start from any state. Values are part of the save format and index
/// <see cref="TreatyRules.Kinds"/>: only append.
/// </summary>
public enum TreatyKind : byte
{
    /// <summary>The two are at war (a state, not a signed treaty).</summary>
    War = 0,

    /// <summary>Peace signed after a war: a truce for some years, during which neither attacks the other.</summary>
    Peace = 1,

    /// <summary>Trade agreement: merchants exchange surpluses between the two (GDD 9.2, without caravans).</summary>
    Trade = 2,

    /// <summary>Non-aggression pact: neither drives out the other's camps.</summary>
    NonAggression = 3,

    /// <summary>Defensive alliance: when someone declares war on one, the other joins (or lets its ally down).</summary>
    DefensiveAlliance = 4,
}

/// <summary>Rules of one treaty kind (data/diplomacy.json).</summary>
public sealed record TreatyKindDefinition
{
    /// <summary>The kind this definition describes.</summary>
    public TreatyKind Kind { get; init; }

    /// <summary>Technology both sides must know to sign it (GDD 9.1: treaties unlocked by technologies), or -1.</summary>
    public int RequiresTechnology { get; init; } = -1;

    /// <summary>Years the treaty lasts once signed; 0 = until it is broken or ended (the truce of peace lasts some years).</summary>
    public int Years { get; init; }

    /// <summary>True for treaties whose end by a declaration of war counts as breaking them (GDD 9.1: reputation).</summary>
    public bool BindsAgainstWar { get; init; }
}

/// <summary>Diplomacy and trade between civilizations (data/diplomacy.json, GDD 9.1-9.2; stage 4d). Scores are per mille.</summary>
public sealed record TreatyRules
{
    /// <summary>How often a civilization's diplomat reviews its relations, in years (staggered over civilizations).</summary>
    public int ReviewEveryYears { get; init; } = 50;

    /// <summary>Lowest score of a treaty the diplomat proposes.</summary>
    public int ProposeAbove { get; init; } = 1000;

    /// <summary>Lowest score of a proposal the other side's diplomat accepts.</summary>
    public int AcceptAbove { get; init; } = 800;

    /// <summary>Lowest score of a declaration of war.</summary>
    public int DeclareWarAbove { get; init; } = 1500;

    /// <summary>Years a declined proposal is not made again to the same civilization.</summary>
    public int RetryYears { get; init; } = 100;

    /// <summary>Years before a declined proposal of <paramref name="kind"/> may be made again.</summary>
    public int RetryYearsFor(TreatyKind kind) => kind == TreatyKind.Peace ? PeaceRetryYears : RetryYears;

    /// <summary>Most cells between the nearest settlements of two civilizations for trade and pacts to make sense.</summary>
    public int ReachCells { get; init; } = 16;

    /// <summary>Share of a gateway settlement's surplus that merchants offer per turn, percent.</summary>
    public int TradeSharePercent { get; init; } = 50;

    /// <summary>Strength of a broken treaty in the memory of civilizations that know both sides, percent of the victim's.</summary>
    public int WitnessPercent { get; init; } = 50;

    /// <summary>Chance per mille that an ally joins a defensive war when its loyalty is 0; loyalty 1000 always joins.</summary>
    public int AllyJoinBasePerMille { get; init; } = 300;

    /// <summary>Raids on an enemy's camps are worth this much more during a war, percent.</summary>
    public int WarRaidPercent { get; init; } = 150;

    /// <summary>Sympathy (hundredths) to factor for treaties: hatred rules them out, friendship doubles them.</summary>
    public ResponseCurve Opinion { get; init; } = ResponseCurve.Linear(-5000, 0, 5000, 2000);

    /// <summary>Trust (hundredths) to factor for pacts.</summary>
    public ResponseCurve Trust { get; init; } = ResponseCurve.Linear(-5000, 0, 5000, 2000);

    /// <summary>Trust (hundredths) to factor for an alliance: without trust built up, no alliance.</summary>
    public ResponseCurve AllianceTrust { get; init; } = ResponseCurve.Linear(0, 0, 3000, 1500);

    /// <summary>Their known warriors against ours, percent, to factor for a non-aggression pact.</summary>
    public ResponseCurve Threat { get; init; } = ResponseCurve.Linear(50, 500, 200, 1500);

    /// <summary>What both sides would gain from trade, percent of the goods' worth, to factor for a trade agreement.</summary>
    public ResponseCurve TradeGain { get; init; } = ResponseCurve.Linear(0, 0, 40, 1500);

    /// <summary>Sympathy (hundredths) to factor for war: only dislike leads to war.</summary>
    public ResponseCurve Hostility { get; init; } = ResponseCurve.Linear(-5000, 2000, 0, 0);

    /// <summary>Our warriors against theirs and their allies', percent, to factor for war (GDD 8.3: below 1.2 : 1 almost zero).</summary>
    public ResponseCurve Advantage { get; init; } = ResponseCurve.Linear(120, 0, 250, 1500);

    /// <summary>Years at war to factor for peace: war weariness until stage 4e brings war score and losses.</summary>
    public ResponseCurve WarWeariness { get; init; } = ResponseCurve.Linear(0, 0, 100, 1500);

    /// <summary>Sympathy (hundredths) to factor for peace: even enemies make peace, friends sooner.</summary>
    public ResponseCurve PeaceOpinion { get; init; } = ResponseCurve.Linear(-10000, 500, 5000, 1500);

    /// <summary>War score from our side to factor for peace: a side that is losing wants peace more (stage 4e PR 6).</summary>
    public ResponseCurve WarScore { get; init; } = ResponseCurve.Linear(-50, 5000, 0, 1000);

    /// <summary>A declined peace is not proposed again for this many years (other treaties: <see cref="RetryYears"/>; stage 4e PR 6).</summary>
    public int PeaceRetryYears { get; init; } = 1;

    /// <summary>Peace: what its terms are worth to us in war score points (negative when we give; stage 4e).</summary>
    public ResponseCurve PeaceTerms { get; init; } = ResponseCurve.Linear(-50, 400, 50, 1600);

    /// <summary>Rules per treaty kind, index = <see cref="TreatyKind"/>.</summary>
    public IReadOnlyList<TreatyKindDefinition> Kinds { get; init; } = Defaults();

    /// <summary>The treaties of GDD 9.1 that are CORE: none needs a technology yet; the truce of peace lasts 100 years.</summary>
    public static IReadOnlyList<TreatyKindDefinition> Defaults() =>
    [
        new() { Kind = TreatyKind.War },
        new() { Kind = TreatyKind.Peace, Years = 100, BindsAgainstWar = true },
        new() { Kind = TreatyKind.Trade },
        new() { Kind = TreatyKind.NonAggression, BindsAgainstWar = true },
        new() { Kind = TreatyKind.DefensiveAlliance, BindsAgainstWar = true },
    ];
}

/// <summary>
/// One treaty or war between two civilizations (GDD 9.1). The pair is stored once with the smaller id first; for a
/// war <see cref="Initiator"/> is the side that declared it. Trade bookkeeping (gateways, flow) lives here too.
/// </summary>
public sealed class Treaty
{
    /// <summary>Creates a treaty between <paramref name="a"/> and <paramref name="b"/> (any order).</summary>
    public Treaty(CivId a, CivId b, TreatyKind kind, CivId initiator, long sinceMonth)
    {
        if (a == b)
        {
            throw new ArgumentException("A treaty needs two civilizations.", nameof(b));
        }

        (A, B) = a.Value < b.Value ? (a, b) : (b, a);
        Kind = kind;
        Initiator = initiator;
        SinceMonth = sinceMonth;
    }

    /// <summary>Civilization with the smaller id.</summary>
    public CivId A { get; }

    /// <summary>Civilization with the larger id.</summary>
    public CivId B { get; }

    /// <summary>Kind of treaty, or war.</summary>
    public TreatyKind Kind { get; }

    /// <summary>Who proposed the treaty or declared the war.</summary>
    public CivId Initiator { get; }

    /// <summary>Game month it was signed or declared.</summary>
    public long SinceMonth { get; }

    /// <summary>Game month it ends by itself (the truce of peace), or 0 for no end.</summary>
    public long UntilMonth { get; set; }

    /// <summary>Trade: settlement of <see cref="A"/> where goods last changed hands, or -1.</summary>
    public int GatewayA { get; set; } = -1;

    /// <summary>Trade: settlement of <see cref="B"/> where goods last changed hands, or -1.</summary>
    public int GatewayB { get; set; } = -1;

    /// <summary>Trade: game month goods last changed hands, or -1 when they never did.</summary>
    public long LastFlowMonth { get; set; } = -1;

    /// <summary>Trade: worth of the goods exchanged in the last turn (both ways, at the mean of both sides' prices), hundredths.</summary>
    public long LastWorth { get; set; }

    /// <summary>Trade: worth of all goods exchanged so far, hundredths.</summary>
    public long TotalWorth { get; set; }

    /// <summary>War (and the peace after it): what the war is fought for (stage 4e).</summary>
    public WarGoal Goal { get; set; }

    /// <summary>War: the settlement a war for land is about, or -1.</summary>
    public int GoalSettlement { get; set; } = -1;

    /// <summary>War: the score from <see cref="A"/>'s side, -100..100 (GDD 9.3).</summary>
    public int Score { get; set; }

    /// <summary>War: people of <see cref="A"/> killed in it.</summary>
    public int DeadA { get; set; }

    /// <summary>War: people of <see cref="B"/> killed in it.</summary>
    public int DeadB { get; set; }

    /// <summary>War: people of <see cref="A"/> when it began.</summary>
    public int PeopleA { get; set; }

    /// <summary>War: people of <see cref="B"/> when it began.</summary>
    public int PeopleB { get; set; }

    /// <summary>War: weariness of <see cref="A"/>, 0..100.</summary>
    public int WearinessA { get; set; }

    /// <summary>War: weariness of <see cref="B"/>, 0..100.</summary>
    public int WearinessB { get; set; }

    /// <summary>War: campaigns sent by either side (stage 4e, ADR 0022).</summary>
    public int Campaigns { get; set; }

    /// <summary>War: months of campaigning by either side.</summary>
    public int CampaignMonths { get; set; }

    /// <summary>War: game month of the last campaign step, or -1 when nobody has campaigned.</summary>
    public long LastCampaignMonth { get; set; } = -1;

    /// <summary>Peace: the terms it was made on.</summary>
    public PeaceTerms Terms { get; set; } = PeaceTerms.White;

    /// <summary>True when <paramref name="civ"/> is one of the two sides.</summary>
    public bool Involves(CivId civ) => civ == A || civ == B;

    /// <summary>The other side for <paramref name="civ"/>.</summary>
    public CivId Other(CivId civ) => civ == A ? B : A;
}

/// <summary>Where a proposal stands. Values are part of the save format: only append.</summary>
public enum ProposalStatus : byte
{
    /// <summary>Waiting for the other side's answer (in its next decisions phase).</summary>
    Pending = 0,

    /// <summary>Declined or not answered in time: not proposed again for a while.</summary>
    Declined = 1,
}

/// <summary>A treaty one civilization offered another (GDD 9.1: every proposal is weighed by the other side's diplomat).</summary>
/// <param name="From">Who proposes.</param>
/// <param name="To">Who answers.</param>
/// <param name="Kind">The treaty offered (peace ends a war).</param>
/// <param name="Month">Game month it was made, or declined.</param>
/// <param name="Status">Waiting or declined.</param>
/// <param name="Terms">Peace: the conditions offered or asked for (stage 4e).</param>
public readonly record struct TreatyProposal(CivId From, CivId To, TreatyKind Kind, long Month, ProposalStatus Status, PeaceTerms Terms = default);

/// <summary>
/// Every treaty, war and proposal of the game (GDD 9.1; stage 4d). Treaties are kept sorted by the pair and kind and
/// proposals in the order they were made, so iteration never depends on hashing. Lists are small (at most a few per
/// pair of civilizations that have met).
/// </summary>
public sealed class TreatyBook
{
    private readonly List<Treaty> _treaties = [];
    private readonly List<TreatyProposal> _proposals = [];

    /// <summary>Treaties and wars, sorted by <see cref="Treaty.A"/>, <see cref="Treaty.B"/> and kind.</summary>
    public IReadOnlyList<Treaty> All => _treaties;

    /// <summary>Proposals waiting for an answer and recently declined ones, in the order they were made.</summary>
    public IReadOnlyList<TreatyProposal> Proposals => _proposals;

    /// <summary>The treaty of <paramref name="kind"/> between the two, or null.</summary>
    public Treaty? Find(CivId x, CivId y, TreatyKind kind)
    {
        int k = IndexOf(x, y, kind);
        return k >= 0 ? _treaties[k] : null;
    }

    /// <summary>True when the two have a treaty of <paramref name="kind"/> (or are at war, for <see cref="TreatyKind.War"/>).</summary>
    public bool Has(CivId x, CivId y, TreatyKind kind) => IndexOf(x, y, kind) >= 0;

    /// <summary>True when the two are at war.</summary>
    public bool AtWar(CivId x, CivId y) => Has(x, y, TreatyKind.War);

    /// <summary>True when <paramref name="civ"/> is at war with anyone.</summary>
    public bool AtWarWithAnyone(CivId civ)
    {
        foreach (Treaty t in _treaties)
        {
            if (t.Kind == TreatyKind.War && t.Involves(civ))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when a treaty keeps <paramref name="attacker"/> from driving out camps of <paramref name="victim"/>: a
    /// non-aggression pact, a defensive alliance or the truce after a war.
    /// </summary>
    public bool ForbidsAttack(CivId attacker, CivId victim) =>
        attacker != victim && (Has(attacker, victim, TreatyKind.NonAggression) || Has(attacker, victim, TreatyKind.DefensiveAlliance)
            || Has(attacker, victim, TreatyKind.Peace));

    /// <summary>Adds a treaty in sorted place; an existing treaty of the same pair and kind is kept and returned.</summary>
    public Treaty Add(Treaty treaty)
    {
        ArgumentNullException.ThrowIfNull(treaty);
        int k = IndexOf(treaty.A, treaty.B, treaty.Kind);
        if (k >= 0)
        {
            return _treaties[k];
        }

        _treaties.Insert(~k, treaty);
        return treaty;
    }

    /// <summary>Removes the treaty of <paramref name="kind"/> between the two; true when there was one.</summary>
    public bool Remove(CivId x, CivId y, TreatyKind kind)
    {
        int k = IndexOf(x, y, kind);
        if (k < 0)
        {
            return false;
        }

        _treaties.RemoveAt(k);
        return true;
    }

    /// <summary>Removes every treaty matching <paramref name="gone"/>; returns how many.</summary>
    public int RemoveAll(Predicate<Treaty> gone) => _treaties.RemoveAll(gone);

    /// <summary>The proposal of <paramref name="kind"/> from <paramref name="from"/> to <paramref name="to"/>, or -1.</summary>
    public int FindProposal(CivId from, CivId to, TreatyKind kind)
    {
        for (int k = 0; k < _proposals.Count; k++)
        {
            TreatyProposal p = _proposals[k];
            if (p.From == from && p.To == to && p.Kind == kind)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>Adds or replaces the proposal of the same sides and kind.</summary>
    public void Propose(TreatyProposal proposal)
    {
        int k = FindProposal(proposal.From, proposal.To, proposal.Kind);
        if (k >= 0)
        {
            _proposals[k] = proposal;
        }
        else
        {
            _proposals.Add(proposal);
        }
    }

    /// <summary>Replaces the proposal at <paramref name="index"/>.</summary>
    public void SetProposal(int index, TreatyProposal proposal) => _proposals[index] = proposal;

    /// <summary>Removes the proposal at <paramref name="index"/>.</summary>
    public void RemoveProposalAt(int index) => _proposals.RemoveAt(index);

    /// <summary>Removes every proposal matching <paramref name="gone"/>.</summary>
    public int RemoveProposals(Predicate<TreatyProposal> gone) => _proposals.RemoveAll(gone);

    /// <summary>Adds a proposal as it was saved (loading).</summary>
    public void AddProposalForLoad(TreatyProposal proposal) => _proposals.Add(proposal);

    // Binary search by pair and kind; the complement of the insertion point when missing.
    private int IndexOf(CivId x, CivId y, TreatyKind kind)
    {
        (int a, int b) = x.Value < y.Value ? (x.Value, y.Value) : (y.Value, x.Value);
        long key = Key(a, b, kind);
        int lo = 0;
        int hi = _treaties.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            Treaty t = _treaties[mid];
            long v = Key(t.A.Value, t.B.Value, t.Kind);
            if (v == key)
            {
                return mid;
            }

            if (v < key)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    private static long Key(int a, int b, TreatyKind kind) => ((long)a << 40) | ((long)b << 8) | (byte)kind;
}
