using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>What a war is fought for (GDD 9.3; stage 4e, ADR 0021). Values are part of the save format: only append.</summary>
public enum WarGoal : byte
{
    /// <summary>A settlement of the enemy (often one they took from us).</summary>
    Land = 0,

    /// <summary>The enemy's stores.</summary>
    Loot = 1,

    /// <summary>Revenge for an attack or a lost settlement.</summary>
    Revenge = 2,
}

/// <summary>What the loser gives at peace (GDD 9.3). Values are part of the save format: only append.</summary>
public enum PeaceTermsKind : byte
{
    /// <summary>Nobody gives anything.</summary>
    White = 0,

    /// <summary>The giver hands over a settlement with its people.</summary>
    Cede = 1,

    /// <summary>The giver pays once a share of all its stores.</summary>
    Ransom = 2,
}

/// <summary>Conditions of a peace (GDD 9.3: settlement, one-time ransom or white peace).</summary>
/// <param name="Kind">What is given.</param>
/// <param name="Giver">Who gives (one of the two sides); ignored for a white peace.</param>
/// <param name="Settlement">Settlement handed over (<see cref="PeaceTermsKind.Cede"/>), or -1.</param>
/// <param name="RansomPercent">Share of every store paid (<see cref="PeaceTermsKind.Ransom"/>).</param>
public readonly record struct PeaceTerms(PeaceTermsKind Kind, CivId Giver, int Settlement, int RansomPercent)
{
    /// <summary>A peace where nobody gives anything.</summary>
    public static PeaceTerms White => new(PeaceTermsKind.White, new CivId(-1), -1, 0);
}

/// <summary>One war goal in data/war.json.</summary>
public sealed record WarGoalDefinition
{
    /// <summary>The goal.</summary>
    public WarGoal Goal { get; init; }

    /// <summary>Name in the game.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Strength of the victim's memory of the aggression, percent (0: none).</summary>
    public int VictimPercent { get; init; } = 100;

    /// <summary>Strength of the memory of everyone who knows both sides, percent (reputation).</summary>
    public int WitnessPercent { get; init; }
}

/// <summary>
/// One level of arms in data/war.json (GDD 9.3; stage 4e PR 3, decision of 2026-11-07): the technology that opens it,
/// the good a warrior uses up on one campaign and the strength of a warrior with it, percent of a warrior with
/// stone-tipped spears. A level without a technology and a good (bare hands) is always there.
/// </summary>
public sealed record ArmamentDefinition
{
    /// <summary>Index in <see cref="WarRules.Armaments"/>.</summary>
    public int Id { get; init; }

    /// <summary>Key in the data.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Name in the game.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Technology the civilization needs, or -1.</summary>
    public int Technology { get; init; } = -1;

    /// <summary>Good a warrior uses up on one campaign, or -1.</summary>
    public int Good { get; init; } = -1;

    /// <summary>How much of <see cref="Good"/> one warrior uses up on one campaign.</summary>
    public Amount PerWarrior { get; init; }

    /// <summary>Strength of one warrior with these arms, percent.</summary>
    public int StrengthPercent { get; init; } = 100;
}

/// <summary>Rules of war (data/war.json; GDD 9.3, stage 4e, ADR 0021). Scores run from -100 to 100, weariness from 0 to 100.</summary>
public sealed record WarRules
{
    /// <summary>Levels of arms; every warrior takes the strongest one the civilization knows and the settlement can afford (stage 4e PR 3).</summary>
    public IReadOnlyList<ArmamentDefinition> Armaments { get; init; } =
    [
        new() { Id = 0, Key = "bare_hands", Name = "Bare hands and sticks", StrengthPercent = 50 },
    ];

    /// <summary>Battles in rounds (stage 4e PR 4).</summary>
    public Episodes.BattleRules Battle { get; init; } = new();

    /// <summary>Settlements whose warriors join a campaign lie at most this many cells from the settlement it leaves from.</summary>
    public int MusterCells { get; init; } = 6;

    /// <summary>Food one warrior on a campaign eats in a year.</summary>
    public Amount FoodPerWarriorPerYear { get; init; } = Amount.FromUnits(1);

    /// <summary>A campaign sets out with food for at most this many months (and at most what its warriors carry).</summary>
    public int CarryFoodMonths { get; init; } = 3;

    /// <summary>At most this share of a settlement's food goes with a campaign or its carriers, percent.</summary>
    public int SupplySharePercent { get; init; } = 50;

    /// <summary>Carriers set out when a campaign has food for fewer than this many months.</summary>
    public int ConvoyBelowMonths { get; init; } = 2;

    /// <summary>Carriers come from a settlement of the attacker at most this many cells from the campaign.</summary>
    public int CarrierCells { get; init; } = 12;

    /// <summary>At most this share of a settlement's people who do not fight carry food, percent.</summary>
    public int CarriersPercent { get; init; } = 10;

    /// <summary>Cells carriers walk per month (lighter than an army on the way back, faster than it on the way out).</summary>
    public int ConvoyCellsPerMonth { get; init; } = 4;

    /// <summary>Morale points a hungry campaign loses per month (morale runs from 0 to 100).</summary>
    public int HungerMoralePerMonth { get; init; } = 20;

    /// <summary>Morale points a fed campaign wins back per month.</summary>
    public int FedMoralePerMonth { get; init; } = 10;

    /// <summary>A campaign whose morale falls below this turns back.</summary>
    public int RetreatMorale { get; init; } = 30;

    /// <summary>A hungry campaign raids a settlement of the enemy at most this many cells away for food.</summary>
    public int ForageCells { get; init; } = 4;

    /// <summary>The goals, index = <see cref="WarGoal"/>.</summary>
    public IReadOnlyList<WarGoalDefinition> Goals { get; init; } =
    [
        new() { Goal = WarGoal.Land, Name = "Land", VictimPercent = 100, WitnessPercent = 50 },
        new() { Goal = WarGoal.Loot, Name = "Loot", VictimPercent = 100, WitnessPercent = 100 },
        new() { Goal = WarGoal.Revenge, Name = "Revenge", VictimPercent = 50, WitnessPercent = 0 },
    ];

    /// <summary>War score for a fight won (to the winner's side).</summary>
    public int ScorePerWin { get; init; } = 10;

    /// <summary>War score for a settlement taken, on top of the fight.</summary>
    public int ScorePerSettlement { get; init; } = 15;

    /// <summary>Weariness per month of campaigning in the war (either side's), hundredths of a point (ADR 0022).</summary>
    public int WearinessPerCampaignMonth { get; init; } = 500;

    /// <summary>Length of a campaign's steps, months (ADR 0022).</summary>
    public int CampaignStepMonths { get; init; } = 1;

    /// <summary>Cells a campaign marches per month.</summary>
    public int MarchCellsPerMonth { get; init; } = 3;

    /// <summary>A war in which nobody campaigned for this many years ends in a white peace.</summary>
    public int QuietPeaceYears { get; init; } = 5;

    /// <summary>The commander sends a campaign only with at least this chance to win, percent (moves to ai.json with the commander, PR 5).</summary>
    public int MinWinChancePercent { get; init; } = 60;

    /// <summary>The commander sends an army only with food for at least this many months to take along (moves to ai.json with the commander, PR 5).</summary>
    public int MinFoodMonths { get; init; } = 1;

    /// <summary>Largest share of a settlement's warriors that marches out, percent (decision of 2026-11-07: a third stays home).</summary>
    public int SendPercent { get; init; } = 67;

    /// <summary>Fewest warriors that make a campaign.</summary>
    public int MinWarriors { get; init; } = 5;

    /// <summary>Weariness per percent of the side's people lost in the war, hundredths of a point.</summary>
    public int WearinessPerLossPercent { get; init; } = 300;

    /// <summary>Weariness from which a side looks for peace at once.</summary>
    public int PeaceAbove { get; init; } = 60;

    /// <summary>Mood points lost per 100 points of weariness (applied as the war_weariness effect).</summary>
    public int MoodAtFullWeariness { get; init; } = 20;

    /// <summary>Score from which the winner asks for terms; below it peace is white.</summary>
    public int TermsAboveScore { get; init; } = 20;

    /// <summary>Ransom: percent of every store per point of score above <see cref="TermsAboveScore"/>, hundredths.</summary>
    public int RansomPercentPerScorePoint { get; init; } = 50;

    /// <summary>Largest ransom, percent of every store.</summary>
    public int MaxRansomPercent { get; init; } = 40;

    /// <summary>What handing over a settlement is worth in score points when weighing terms.</summary>
    public int CedeWorth { get; init; } = 40;
}

/// <summary>
/// The rules of the war itself (GDD 9.3; stage 4e, ADR 0021): score, weariness, the terms of peace and carrying them
/// out. Commands and systems call these.
/// </summary>
public static class Wars
{
    /// <summary>The war score from <paramref name="civ"/>'s side (-100..100).</summary>
    public static int ScoreFor(Treaty war, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(war);
        return civ == war.A ? war.Score : -war.Score;
    }

    /// <summary>Weariness of <paramref name="civ"/> in this war (0..100).</summary>
    public static int WearinessOf(Treaty war, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(war);
        return civ == war.A ? war.WearinessA : war.WearinessB;
    }

    /// <summary>The highest weariness of <paramref name="civ"/> over all its wars.</summary>
    public static int Weariness(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        int w = 0;
        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind == TreatyKind.War && t.Involves(civ))
            {
                w = Math.Max(w, WearinessOf(t, civ));
            }
        }

        return w;
    }

    /// <summary>
    /// A fight between <paramref name="winner"/> and <paramref name="loser"/> during their war: the winner's side gains
    /// score (more when it took a settlement), both count their dead.
    /// </summary>
    public static void RecordFight(GameContent content, GameState state, CivId winner, CivId loser, bool settlementTaken, int winnerDead, int loserDead)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        Treaty? war = state.Treaties.Find(winner, loser, TreatyKind.War);
        if (war is null)
        {
            return;
        }

        int gain = content.War.ScorePerWin + (settlementTaken ? content.War.ScorePerSettlement : 0);
        war.Score = Math.Clamp(war.Score + (winner == war.A ? gain : -gain), -100, 100);
        AddDead(war, winner, winnerDead);
        AddDead(war, loser, loserDead);
    }

    /// <summary>Weariness of <paramref name="civ"/> from the months of campaigning and its dead (0..100; ADR 0022).</summary>
    public static int ComputeWeariness(WarRules rules, Treaty war, CivId civ, long month)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(war);
        int dead = civ == war.A ? war.DeadA : war.DeadB;
        int people = Math.Max(1, civ == war.A ? war.PeopleA : war.PeopleB);
        long lossPercent = (long)dead * 100 / people;
        long hundredths = ((long)war.CampaignMonths * rules.WearinessPerCampaignMonth) + (lossPercent * rules.WearinessPerLossPercent);
        return (int)Math.Min(100, hundredths / 100);
    }

    /// <summary>
    /// The terms <paramref name="civ"/> asks for or offers in a peace with <paramref name="other"/> (GDD 9.3): the
    /// winner by enough score takes the settlement the war was about, or else a ransom growing with the score; a close
    /// war ends in a white peace.
    /// </summary>
    public static PeaceTerms TermsFor(GameContent content, GameState state, CivId civ, CivId other)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        Treaty? war = state.Treaties.Find(civ, other, TreatyKind.War);
        if (war is null)
        {
            return PeaceTerms.White;
        }

        int score = ScoreFor(war, civ);
        WarRules rules = content.War;
        if (Math.Abs(score) < rules.TermsAboveScore)
        {
            return PeaceTerms.White;
        }

        CivId giver = score > 0 ? other : civ;
        if (war.Goal == WarGoal.Land && war.GoalSettlement >= 0 && state.FindSettlement(new SettlementId(war.GoalSettlement)) is { IsActive: true } s && s.Owner == giver)
        {
            return new PeaceTerms(PeaceTermsKind.Cede, giver, s.Id.Value, 0);
        }

        int percent = (Math.Abs(score) - rules.TermsAboveScore + 1) * rules.RansomPercentPerScorePoint / 100;
        return new PeaceTerms(PeaceTermsKind.Ransom, giver, -1, Math.Clamp(percent, 1, rules.MaxRansomPercent));
    }

    /// <summary>What the terms are worth to <paramref name="civ"/> in score points: positive when it receives.</summary>
    public static int TermsWorth(GameContent content, PeaceTerms terms, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        int worth = terms.Kind switch
        {
            PeaceTermsKind.Cede => content.War.CedeWorth,
            PeaceTermsKind.Ransom => content.War.TermsAboveScore + (terms.RansomPercent * 100 / Math.Max(1, content.War.RansomPercentPerScorePoint)),
            _ => 0,
        };
        return terms.Kind == PeaceTermsKind.White ? 0 : terms.Giver == civ ? -worth : worth;
    }

    /// <summary>Why the terms cannot be carried out between <paramref name="x"/> and <paramref name="y"/>, or null.</summary>
    public static string? CannotKeep(GameContent content, GameState state, CivId x, CivId y, PeaceTerms terms)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        switch (terms.Kind)
        {
            case PeaceTermsKind.White:
                return null;
            case PeaceTermsKind.Cede:
                if (terms.Giver != x && terms.Giver != y)
                {
                    return "The giver must be one of the two sides.";
                }

                return state.FindSettlement(new SettlementId(terms.Settlement)) is { IsActive: true } s && s.Owner == terms.Giver
                    ? null
                    : "The giver does not hold that settlement.";
            case PeaceTermsKind.Ransom:
                if (terms.Giver != x && terms.Giver != y)
                {
                    return "The giver must be one of the two sides.";
                }

                return terms.RansomPercent < 1 || terms.RansomPercent > content.War.MaxRansomPercent ? "That ransom is out of bounds." : null;
            default:
                return "Unknown terms.";
        }
    }

    /// <summary>
    /// Carries out the terms between <paramref name="x"/> and <paramref name="y"/>: a ceded settlement changes hands with its
    /// people and skills and nobody claims it any more; a ransom moves a share of every store of the giver to the
    /// receiver's largest settlement. Returns the chronicle words, or an empty string.
    /// </summary>
    public static string Keep(GameContent content, GameState state, CivId x, CivId y, PeaceTerms terms)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        if (terms.Kind == PeaceTermsKind.White || CannotKeep(content, state, x, y, terms) is not null)
        {
            return string.Empty;
        }

        CivId receiver = terms.Giver == x ? y : x;
        string giverName = state.FindCivilization(terms.Giver)?.Name ?? terms.Giver.ToString();
        if (terms.Kind == PeaceTermsKind.Cede)
        {
            SettlementState s = state.FindSettlement(new SettlementId(terms.Settlement))!;
            s.Owner = receiver;
            s.ClaimCiv = SettlementState.NoClaim;
            return $" {giverName} handed over {s.Name}.";
        }

        SettlementState? to = Largest(state, receiver);
        if (to is null)
        {
            return string.Empty;
        }

        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.Owner != terms.Giver)
            {
                continue;
            }

            Span<Amount> stock = s.Stock;
            for (int g = 0; g < stock.Length; g++)
            {
                Amount paid = Amount.FromHundredths(stock[g].Hundredths * terms.RansomPercent / 100);
                stock[g] -= paid;
                to.Stock[g] += paid;
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $" {giverName} paid a ransom of {terms.RansomPercent}% of its stores.");
    }

    // The receiver's settlement with the most people (ties: lowest id).
    private static SettlementState? Largest(GameState state, CivId civ)
    {
        SettlementState? best = null;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == civ && (best is null || s.TotalPopulation() > best.TotalPopulation()))
            {
                best = s;
            }
        }

        return best;
    }

    private static void AddDead(Treaty war, CivId civ, int dead)
    {
        if (civ == war.A)
        {
            war.DeadA += Math.Max(0, dead);
        }
        else
        {
            war.DeadB += Math.Max(0, dead);
        }
    }

    /// <summary>People of <paramref name="civ"/> in all its active settlements.</summary>
    public static int PeopleOf(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        long n = 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == civ)
            {
                n += s.TotalPopulation();
            }
        }

        return (int)Math.Min(int.MaxValue, n);
    }
}
