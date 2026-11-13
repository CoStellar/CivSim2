using System;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>Goals the strategist weighs (GDD 8.1). Values are part of the save format from stage 4b: only append.</summary>
public enum Goal : byte
{
    /// <summary>More land and settlements.</summary>
    Expansion = 0,

    /// <summary>Safety from enemies and hunger.</summary>
    Security = 1,

    /// <summary>Wealth and well-fed people.</summary>
    Prosperity = 2,

    /// <summary>Knowledge.</summary>
    Science = 3,

    /// <summary>Standing among other civilizations.</summary>
    Influence = 4,

    /// <summary>Taking what others hold.</summary>
    Conquest = 5,
}

/// <summary>
/// Base of every AI advisor (GDD 8.1): one area of decisions for all AI civilizations, scored with the shared
/// <see cref="Evaluation"/> and picked with a <see cref="Utility.Chooser"/>. Advisors only read the state; every
/// decision becomes the same command a player would send, recorded with its "Why?" in the <see cref="Log"/>.
/// </summary>
public abstract class Advisor : ICommandSource
{
    private readonly DeterministicRandom _random = new(0);

    /// <summary>Creates an advisor.</summary>
    protected Advisor(GameContent content, AdvisorKind kind)
    {
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Kind = kind;
        Chooser = new Chooser(content.Ai.PickAmongTop);
    }

    /// <summary>Which advisor this is.</summary>
    public AdvisorKind Kind { get; }

    /// <summary>Where decisions are recorded; null records nothing (e.g. when an advisor runs alone in a test).</summary>
    public DecisionLog? Log { get; set; }

    /// <summary>Game content.</summary>
    protected GameContent Content { get; }

    /// <summary>The chooser this advisor reuses for every decision.</summary>
    protected Chooser Chooser { get; }

    /// <inheritdoc />
    public abstract void IssueCommands(GameState state, CommandQueue queue);

    /// <summary>The weight the strategist gives <paramref name="goal"/> for <paramref name="civ"/>, per mille (GDD 8.1).</summary>
    protected static int GoalWeight(CivilizationState civ, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.GoalWeight(goal);
    }

    /// <summary>The character multiplier of <paramref name="civ"/> for an action serving <paramref name="goal"/> (GDD 8.3, 8.4).</summary>
    protected static int CharacterMultiplier(CivilizationState civ, Goal goal) => Character.Multiplier(civ, goal);

    /// <summary>
    /// Rank of the chosen action (GDD 8.3): a cautious civilization takes the best one with a chance equal to its
    /// caution (shifted by its government, GDD 7.2), otherwise it draws among the kept actions with chances by score.
    /// </summary>
    protected int Pick(CivilizationState civ, DeterministicRandom? random)
    {
        ArgumentNullException.ThrowIfNull(civ);
        if (random is null || Chooser.Count <= 1 || random.NextInt(1000) < Governance.Governments.Caution(Content, civ))
        {
            return Chooser.Count == 0 ? -1 : 0;
        }

        return Chooser.Pick(random);
    }

    /// <summary>
    /// The generator for one decision about <paramref name="entity"/>, or null when the chooser keeps only the best
    /// action and so never draws. It depends only on the seed, the entity and the turn (the AI stream is never
    /// advanced), so advisors could run in parallel.
    /// </summary>
    protected DeterministicRandom? RandomFor(GameState state, int entity)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Chooser.Room == 1)
        {
            return null;
        }

        _random.Reseed(state.Random.DeriveSeed(RandomStreamId.Ai, ((ulong)Kind << 32) | (uint)entity, state.Turn));
        return _random;
    }

    /// <summary>Sends <paramref name="command"/> for <paramref name="civ"/> and records why.</summary>
    protected void Issue(
        GameState state, CommandQueue queue, CivId civ, ICommand command, AiAction action, int subject, int target, in Evaluation why)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        if (state.FindCivilization(civ) is { } c && !Levers.AiLeads(c, action))
        {
            return; // the player holds this lever (ADR 0027): the AI keeps off it
        }

        queue.Enqueue(PlayerId.Ai, civ, command);
        Log?.Add(Decision.From(state.Turn + 1, civ, Kind, action, subject, target, why));
    }
}

/// <summary>
/// The advisors of all AI civilizations together (GDD 8.1), run in a fixed order every Decisions phase. It owns the
/// decision log of the turn.
/// </summary>
public sealed class AiCouncil : ICommandSource
{
    private readonly Advisor[] _advisors;

    /// <summary>
    /// Creates the council of the core advisors. The governor runs first, so the strategist reads this turn's place
    /// values; the strategy it sets applies from the next turn.
    /// </summary>
    public AiCouncil(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Governor = new GovernorAdvisor(content);
        _advisors = [Governor, new StrategistAdvisor(content, Governor), new TreasurerAdvisor(content), new Research.ScholarAdvisor(content), new DiplomatAdvisor(content), new CommanderAdvisor(content)];
        foreach (Advisor advisor in _advisors)
        {
            advisor.Log = Log;
        }
    }

    /// <summary>The governor (camps, splits, settling); also answers comfort questions.</summary>
    public GovernorAdvisor Governor { get; }

    /// <summary>Decisions of the last Decisions phase with their reasons.</summary>
    public DecisionLog Log { get; } = new();

    /// <inheritdoc />
    public void IssueCommands(GameState state, CommandQueue queue)
    {
        Log.Clear();
        foreach (Advisor advisor in _advisors)
        {
            advisor.IssueCommands(state, queue);
        }

        // Every alarm got its reaction in this decisions phase (reactive decisions, ADR 0018).
        foreach (CivilizationState civ in state.Civilizations)
        {
            civ.Alarms = Simulation.Alarm.None;
        }
    }
}
