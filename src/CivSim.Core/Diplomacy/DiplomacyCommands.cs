using System;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// Offers a treaty to another civilization (GDD 9.1; stage 4d). The other side's diplomat (or player) answers in its
/// next decisions phase; an unanswered proposal lapses. Peace is the proposal that ends a war.
/// </summary>
public sealed record ProposeTreatyCommand(GameContent Content, CivId Other, TreatyKind Kind, PeaceTerms Terms = default) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        if (Treaties.CannotPropose(Content, state, civ, Other, Kind) is { } reason)
        {
            return CommandResult.Fail(reason);
        }

        if (Kind != TreatyKind.Peace && Terms.Kind != PeaceTermsKind.White)
        {
            return CommandResult.Fail("Only peace has terms.");
        }

        return Wars.CannotKeep(Content, state, civ, Other, Terms) is { } bad ? CommandResult.Fail(bad) : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Treaties.Propose(new TreatyProposal(civ, Other, Kind, state.ActionMonth, ProposalStatus.Pending, Terms));
    }
}

/// <summary>Accepts or declines a treaty another civilization proposed (GDD 9.1; stage 4d).</summary>
public sealed record AnswerProposalCommand(GameContent Content, CivId From, TreatyKind Kind, bool Accept) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        int k = state.Treaties.FindProposal(From, civ, Kind);
        if (k < 0 || state.Treaties.Proposals[k].Status != ProposalStatus.Pending)
        {
            return CommandResult.Fail("There is no such proposal waiting for an answer.");
        }

        if (!Accept)
        {
            return CommandResult.Ok;
        }

        if (Treaties.CannotSign(Content, state, From, civ, Kind) is { } reason)
        {
            return CommandResult.Fail(reason);
        }

        return Wars.CannotKeep(Content, state, From, civ, state.Treaties.Proposals[k].Terms) is { } bad ? CommandResult.Fail(bad) : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        TreatyBook book = state.Treaties;
        int k = book.FindProposal(From, civ, Kind);
        if (!Accept)
        {
            book.SetProposal(k, book.Proposals[k] with { Status = ProposalStatus.Declined, Month = state.ActionMonth });
            return null;
        }

        PeaceTerms terms = book.Proposals[k].Terms;
        book.RemoveProposalAt(k);
        return Treaties.Sign(Content, state, From, civ, Kind, terms);
    }
}

/// <summary>
/// Declares war on another civilization for a goal (GDD 9.1, 9.3; stage 4e): land (a settlement of theirs we know of),
/// loot, or revenge (needs a cause).
/// </summary>
public sealed record DeclareWarCommand(GameContent Content, CivId Target, WarGoal Goal = WarGoal.Revenge, int GoalSettlement = -1) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ) =>
        Treaties.CannotDeclareWar(Content, state, civ, Target, Goal, GoalSettlement) is { } reason ? CommandResult.Fail(reason) : CommandResult.Ok;

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        string line = Treaties.DeclareWar(Content, state, civ, Target, Goal, GoalSettlement);
        // GDD 7.2 (stage 4f): the declaration takes effect after the government's enactment delay; until then our
        // armies stay at home.
        int months = state.FindCivilization(civ) is { } c ? Governance.Governments.EnactMonths(Content, c) : 0;
        if (months > 0 && state.Treaties.Find(civ, Target, TreatyKind.War) is { } war)
        {
            war.ReadyTick = state.Turn + Content.Time.ActionTicks(months); // ADR 0025: months of action, in ticks
            line += string.Create(System.Globalization.CultureInfo.InvariantCulture, $" Its warriors will be ready in {months / 12} year{(months == 12 ? string.Empty : "s")}.");
        }

        return line;
    }
}
