using System;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// The five levers of the helmsman (ADR 0027, GDD 1.2). A player who takes a lever decides it; the AI decides every lever the player
/// has not taken, with the same commands. Flags: a civilization keeps the levers its player holds. Values are part of the save format
/// and the protocol: never renumber.
/// </summary>
[Flags]
public enum Lever : byte
{
    /// <summary>No lever: decisions the player never makes (recipes, prices); the AI and the simulation always make them.</summary>
    None = 0,

    /// <summary>1. Where we live: the start, settlers and new settlements, camps that move, split or join, scouting.</summary>
    WhereWeLive = 1,

    /// <summary>2. Where we are heading: the direction of development (strategy) and the next technology.</summary>
    WhereWeHead = 2,

    /// <summary>3. What we do on the land: area tasks and building on the spot (stage 6 PR 4a; the AI gives tasks from PR 4b).</summary>
    OnTheLand = 4,

    /// <summary>4. With whom and how: war, peace, treaties, campaigns, raids and conquest.</summary>
    WithWhom = 8,

    /// <summary>5. Answers to cards: events, and from the Bronze Age laws and governments (PR 7); today the change of government.</summary>
    Cards = 16,
}

/// <summary>Which lever each AI decision belongs to, and whether the AI may make it for a civilization (ADR 0027).</summary>
public static class Levers
{
    /// <summary>The five levers in their order (1–5).</summary>
    public static readonly Lever[] All = [Lever.WhereWeLive, Lever.WhereWeHead, Lever.OnTheLand, Lever.WithWhom, Lever.Cards];

    /// <summary>Every lever together.</summary>
    public const Lever Every = Lever.WhereWeLive | Lever.WhereWeHead | Lever.OnTheLand | Lever.WithWhom | Lever.Cards;

    /// <summary>The lever of an AI action; <see cref="Lever.None"/> for decisions the player never makes (recipes).</summary>
    public static Lever Of(AiAction action) => action switch
    {
        AiAction.MoveCamp or AiAction.SplitCamp or AiAction.Journey or AiAction.Settle or AiAction.JoinCamp
            or AiAction.SendSettlers or AiAction.Explore => Lever.WhereWeLive,
        AiAction.SetResearch or AiAction.SetStrategy => Lever.WhereWeHead,
        AiAction.Plunder or AiAction.Conquer or AiAction.Occupy or AiAction.ProposeTreaty or AiAction.AcceptTreaty
            or AiAction.DeclineTreaty or AiAction.DeclareWar or AiAction.LaunchCampaign or AiAction.RecallCampaign => Lever.WithWhom,
        AiAction.ChangeGovernment => Lever.Cards,
        _ => Lever.None, // SetRecipe: the economy is the simulation's (GDD 1.2: the player never sets recipes)
    };

    /// <summary>True when the AI decides <paramref name="lever"/> for <paramref name="civ"/>: an AI civilization, or a lever its player has not taken.</summary>
    public static bool AiLeads(CivilizationState civ, Lever lever)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return lever == Lever.None || civ.Controller == PlayerId.Ai || (civ.PlayerLevers & lever) == 0;
    }

    /// <summary>True when the AI decides <paramref name="action"/> for <paramref name="civ"/>.</summary>
    public static bool AiLeads(CivilizationState civ, AiAction action) => AiLeads(civ, Of(action));

    /// <summary>The lever's number for players (1–5), or 0.</summary>
    public static int Number(Lever lever) => lever switch
    {
        Lever.WhereWeLive => 1,
        Lever.WhereWeHead => 2,
        Lever.OnTheLand => 3,
        Lever.WithWhom => 4,
        Lever.Cards => 5,
        _ => 0,
    };

    /// <summary>The lever's name for players (English: texts in the game are English).</summary>
    public static string Name(Lever lever) => lever switch
    {
        Lever.WhereWeLive => "Where we live",
        Lever.WhereWeHead => "Where we are heading",
        Lever.OnTheLand => "What we do on the land",
        Lever.WithWhom => "With whom and how",
        Lever.Cards => "Answers to cards",
        _ => "Simulation",
    };
}

/// <summary>
/// The player takes a lever from the AI or gives it back (ADR 0027): the five tiles "I take it / I give it back". A command like any
/// other, so a replay plays it in the same tick. Only a civilization led by a player has levers; the AI keeps all of an AI civilization.
/// </summary>
public sealed record SetLeverCommand(Lever Lever, bool PlayerTakes) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy; // AI commands issued in the same tick still apply; from the next tick the AI keeps off the lever

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? c = state.FindCivilization(civ);
        if (c is not { IsAlive: true })
        {
            return CommandResult.Fail("That civilization is gone.");
        }

        if (c.Controller == PlayerId.Ai)
        {
            return CommandResult.Fail("Only a civilization led by a player has levers.");
        }

        return Levers.Number(Lever) == 0 ? CommandResult.Fail("There is no such lever.") : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState c = state.FindCivilization(civ)!;
        c.PlayerLevers = PlayerTakes ? c.PlayerLevers | Lever : c.PlayerLevers & ~Lever;
    }
}
