using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Core.State;

namespace CivSim.Core.Ai.Utility;

/// <summary>The advisors of a civilization (GDD 8.1). Values are part of decision logs: only append.</summary>
public enum AdvisorKind : byte
{
    /// <summary>Sets goal weights and the direction of growth (stage 4b).</summary>
    Strategist = 0,

    /// <summary>Treaties, war and peace (stage 4d).</summary>
    Diplomat = 1,

    /// <summary>Goods priorities and production.</summary>
    Treasurer = 2,

    /// <summary>One per settlement: where the camp lives, splits and settling down.</summary>
    Governor = 3,

    /// <summary>Armies (stage 4e).</summary>
    Commander = 4,

    /// <summary>The research queue.</summary>
    Scholar = 5,
}

/// <summary>What an advisor decided to do. Values are part of decision logs: only append.</summary>
public enum AiAction : byte
{
    /// <summary>The camp moves to another place.</summary>
    MoveCamp = 0,

    /// <summary>A group leaves to found a new camp.</summary>
    SplitCamp = 1,

    /// <summary>The camp sets out on a journey of several turns.</summary>
    Journey = 2,

    /// <summary>The camp settles down as a farming village.</summary>
    Settle = 3,

    /// <summary>A tiny camp joins another camp of its own.</summary>
    JoinCamp = 4,

    /// <summary>The camp takes goods left in an empty settlement.</summary>
    Plunder = 5,

    /// <summary>The camp drives out a weaker foreign camp and takes its place.</summary>
    Conquer = 6,

    /// <summary>People move into an empty settlement.</summary>
    Occupy = 7,

    /// <summary>A settlement's artisans switch to another recipe.</summary>
    SetRecipe = 8,

    /// <summary>The civilization starts researching a technology.</summary>
    SetResearch = 9,

    /// <summary>The strategist sets goal weights and the expansion target.</summary>
    SetStrategy = 10,

    /// <summary>The diplomat proposes a treaty, or peace (stage 4d).</summary>
    ProposeTreaty = 11,

    /// <summary>The diplomat accepts a proposed treaty.</summary>
    AcceptTreaty = 12,

    /// <summary>The diplomat declines a proposed treaty.</summary>
    DeclineTreaty = 13,

    /// <summary>The diplomat declares war.</summary>
    DeclareWar = 14,

    /// <summary>A village sends settlers to found a camp (PR C).</summary>
    SendSettlers = 15,

    /// <summary>A camp sends explorers towards unknown land (PR C).</summary>
    Explore = 16,

    /// <summary>The commander sends warriors on a campaign (stage 4e).</summary>
    LaunchCampaign = 17,
}

/// <summary>
/// One AI decision with its "Why?" (GDD 8.7): who decided what, the score, and the three factors that shaped it most.
/// </summary>
/// <param name="Turn">Turn in which the command was issued (it resolves in the same turn).</param>
/// <param name="Civ">Deciding civilization.</param>
/// <param name="Advisor">Advisor that decided.</param>
/// <param name="Action">What it decided.</param>
/// <param name="Subject">Settlement id the decision is about, or -1 for the whole civilization.</param>
/// <param name="Target">Cell index, recipe id or technology id the action aims at, or -1.</param>
/// <param name="Score">Final score of the chosen action.</param>
/// <param name="First">Most important factor.</param>
/// <param name="Second">Second most important factor.</param>
/// <param name="Third">Third most important factor.</param>
public readonly record struct Decision(
    long Turn, CivId Civ, AdvisorKind Advisor, AiAction Action, int Subject, int Target, long Score, Factor First, Factor Second, Factor Third)
{
    /// <summary>Builds a decision from the evaluation that won.</summary>
    public static Decision From(long turn, CivId civ, AdvisorKind advisor, AiAction action, int subject, int target, in Evaluation why)
    {
        Span<Factor> top = stackalloc Factor[3];
        int n = why.Top(top);
        return new Decision(turn, civ, advisor, action, subject, target, why.Value, n > 0 ? top[0] : default, n > 1 ? top[1] : default, n > 2 ? top[2] : default);
    }

    /// <summary>The recorded factors, most important first, without empty slots.</summary>
    public IEnumerable<Factor> Factors()
    {
        if (First.Id != FactorId.None)
        {
            yield return First;
        }

        if (Second.Id != FactorId.None)
        {
            yield return Second;
        }

        if (Third.Id != FactorId.None)
        {
            yield return Third;
        }
    }

    /// <summary>One factor as short English text, e.g. "comfort 80%" or "food 4512".</summary>
    public static string Describe(Factor factor) => factor.Id == FactorId.ServesGoal
        ? "serves " + ((CivSim.Core.Ai.Goal)factor.Input).ToString().ToLowerInvariant()
        : factor.PerMille == Factor.NoteOnly
        ? string.Create(CultureInfo.InvariantCulture, $"{Name(factor.Id)} {factor.Input}{(InPercent(factor.Id) ? "%" : string.Empty)}")
        : string.Create(CultureInfo.InvariantCulture, $"{Name(factor.Id)} {factor.PerMille / 10}% ({factor.Input})");

    // Notes whose input is a percentage (farm grain against what the people eat, people against the level's room...).
    private static bool InPercent(FactorId id) => id is FactorId.FarmYield or FactorId.Crowding or FactorId.Strength or FactorId.Comfort or FactorId.Threat or FactorId.TradeGain;

    /// <summary>Lower-case English name of a factor.</summary>
    public static string Name(FactorId id) => id switch
    {
        FactorId.OwnCampsite => "own old campsite",
        FactorId.BetterThanHere => "better than here",
        FactorId.FarmYield => "farm yield",
        FactorId.LeftGoods => "goods left",
        FactorId.ValueAdded => "value added",
        FactorId.InputsAvailable => "inputs available",
        FactorId.ResearchCost => "research cost",
        FactorId.GoalWeight => "goal weight",
        FactorId.TradeGain => "trade gain",
        FactorId.WarYears => "years at war",
        FactorId.Weariness => "war weariness",
        FactorId.Terms => "peace terms",
        FactorId.WinChance => "win chance",
        FactorId.FoodMonths => "food months",
        FactorId.LeadsTo => "leads to",
        FactorId.ForTrade => "for trade",
        FactorId.OverGathering => "over gathering",
        FactorId.WornLand => "worn land",
        _ => id.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// The decisions of the last Decisions phase, in the order they were made. It lives with the AI, not in the game state:
/// it explains, it never steers the simulation, so it is neither hashed nor saved. The list is reused every turn.
/// </summary>
public sealed class DecisionLog
{
    private readonly List<Decision> _entries = [];

    /// <summary>Decisions of the last turn.</summary>
    public IReadOnlyList<Decision> Entries => _entries;

    /// <summary>Forgets the previous turn's decisions.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>Adds a decision.</summary>
    public void Add(in Decision decision) => _entries.Add(decision);
}
