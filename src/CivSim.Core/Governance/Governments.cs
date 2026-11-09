using System;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Governance;

/// <summary>
/// Forms of government (GDD 7.2; stage 4f, ADR 0023): the government a people starts with, its title, the delay before
/// state decisions take effect, and the change of government.
/// </summary>
public static class Governments
{
    /// <summary>The government of <paramref name="civ"/>, or null when the content has none (hand-made test content).</summary>
    public static GovernmentDefinition? Of(GameContent content, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        return civ.Government >= 0 && civ.Government < content.Governments.Count ? content.Governments[civ.Government] : null;
    }

    /// <summary>The full name of <paramref name="civ"/> with the title of its government, e.g. "Chiefdom of Karrut" (GDD 5.6, 7.2).</summary>
    public static string Title(GameContent content, CivilizationState civ) =>
        Of(content, civ) is { } g ? g.TitleOf(civ.Name) : civ.Name;

    /// <summary>Months before a state decision of <paramref name="civ"/> takes effect (GDD 7.2: enactment).</summary>
    public static int EnactMonths(GameContent content, CivilizationState civ) => (Of(content, civ)?.EnactYears ?? 0) * 12;

    /// <summary>Percent of the usual interval between reviews of the strategist and the diplomat of <paramref name="civ"/>.</summary>
    public static int ReviewPercent(GameContent content, CivilizationState civ) => Of(content, civ)?.ReviewPercent ?? 100;

    /// <summary>The caution the AI of <paramref name="civ"/> decides with, per mille: its trait shifted by the government.</summary>
    public static int Caution(GameContent content, CivilizationState civ) =>
        Math.Clamp(civ.Trait(Ai.Trait.Caution) + (Of(content, civ)?.CautionShift ?? 0), 0, 1000);

    /// <summary>The goal weight <paramref name="weight"/> the strategist sets, changed by the government of <paramref name="civ"/>.</summary>
    public static int Weight(GameContent content, CivilizationState civ, Ai.Goal goal, int weight) =>
        Of(content, civ) is { } g ? (int)((long)weight * g.PercentFor(goal) / 100) : weight;

    /// <summary>
    /// Gives every civilization of a new game the government its character leans to (GDD 7.2): among those open from the
    /// first era, the highest sum of trait × leaning; ties go to the first in the data.
    /// </summary>
    public static void ChooseAtStart(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        foreach (CivilizationState civ in state.Civilizations)
        {
            civ.Government = Leaning(content, civ, civ.Era);
        }
    }

    /// <summary>The government open in <paramref name="era"/> that the character of <paramref name="civ"/> leans to most (0 when none).</summary>
    public static int Leaning(GameContent content, CivilizationState civ, int era) => Leaning(content, civ, era, -1);

    /// <summary>
    /// The government open in <paramref name="era"/>, other than <paramref name="except"/>, that the character of
    /// <paramref name="civ"/> leans to most; <paramref name="except"/> itself when no other is open (0 when there are none).
    /// </summary>
    public static int Leaning(GameContent content, CivilizationState civ, int era, int except)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        int best = except >= 0 ? except : 0;
        long bestScore = long.MinValue;
        foreach (GovernmentDefinition g in content.Governments)
        {
            if (g.FromEra > era || g.Id == except)
            {
                continue;
            }

            long score = 0;
            for (int t = 0; t < g.Leaning.Count; t++)
            {
                score += (long)g.Leaning[t] * civ.OwnTrait((Ai.Trait)t); // the people's character, not the leader's
            }

            if (score > bestScore)
            {
                best = g.Id;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>Why <paramref name="civ"/> may not bring in <paramref name="government"/> now, or null when it may.</summary>
    public static string? CannotChange(GameContent content, GameState state, CivId civ, int government)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? c = state.FindCivilization(civ);
        if (c is not { IsAlive: true })
        {
            return "Only a living civilization has a government.";
        }

        if (government < 0 || government >= content.Governments.Count)
        {
            return "Unknown government.";
        }

        if (government == c.Government)
        {
            return "That is our government already.";
        }

        if (c.PendingGovernment != CivilizationState.NoGovernment)
        {
            return "A change of government is already under way.";
        }

        if (Stability.InAnarchy(c, state.Month))
        {
            return "Our state is in anarchy.";
        }

        return content.Governments[government].FromEra > c.Era ? $"{content.Governments[government].Name} comes in a later era." : null;
    }

    /// <summary>
    /// Starts bringing in <paramref name="government"/>: it takes effect after the enactment delay of the present one
    /// (at once when that is 0). Returns the chronicle line.
    /// </summary>
    public static string Change(GameContent content, GameState state, CivId civ, int government)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState c = state.FindCivilization(civ)!;
        int months = EnactMonths(content, c);
        string name = content.Governments[government].Name;
        if (months == 0)
        {
            return Enact(content, state, c, government);
        }

        c.PendingGovernment = government;
        c.PendingGovernmentMonth = state.Month + months;
        return $"{Title(content, c)} began to change its government to {name.ToLowerInvariant()}.";
    }

    /// <summary>Brings in every change of government that is due (GDD 7.2). Returns how many took effect.</summary>
    public static int EnactDue(GameContent content, GameState state, Action<string>? chronicle)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        int count = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            if (c.PendingGovernment != CivilizationState.NoGovernment && state.Month >= c.PendingGovernmentMonth)
            {
                int government = c.PendingGovernment;
                c.PendingGovernment = CivilizationState.NoGovernment;
                c.PendingGovernmentMonth = 0;
                if (c.IsAlive)
                {
                    chronicle?.Invoke(Enact(content, state, c, government));
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Brings in <paramref name="government"/> at once (a coup, stage 4f PR 4), with the anarchy that follows any change.
    /// Returns the chronicle line.
    /// </summary>
    public static string EnactNow(GameContent content, GameState state, CivilizationState civ, int government)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        civ.PendingGovernment = CivilizationState.NoGovernment;
        civ.PendingGovernmentMonth = 0;
        return Enact(content, state, civ, government);
    }

    // The new government takes effect; anarchy follows for some years (GDD 7.2, stage 4f PR 3).
    private static string Enact(GameContent content, GameState state, CivilizationState c, int government)
    {
        string before = Title(content, c);
        c.Government = government;
        int years = content.Stability.AnarchyYears;
        if (years > 0)
        {
            c.AnarchyUntilMonth = Math.Max(c.AnarchyUntilMonth, state.Month + (years * 12L));
            return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{before} became the {Title(content, c)}; {years} years of anarchy follow.");
        }

        return $"{before} became the {Title(content, c)}.";
    }
}

/// <summary>Brings in another form of government (GDD 7.2; stage 4f). It takes effect after the present government's enactment delay.</summary>
public sealed record ChangeGovernmentCommand(GameContent Content, int Government) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ) =>
        Governments.CannotChange(Content, state, civ, Government) is { } reason ? CommandResult.Fail(reason) : CommandResult.Ok;

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ) => Governments.Change(Content, state, civ, Government);
}

/// <summary>State phase of government (GDD 7.2; stage 4f): changes of government that are due take effect.</summary>
public sealed class GovernmentSystem : ITurnSystem
{
    private readonly GameContent _content;

    /// <summary>Creates the system.</summary>
    public GovernmentSystem(GameContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.State;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Governments.EnactDue(_content, context.State, context.Chronicle);
    }
}
