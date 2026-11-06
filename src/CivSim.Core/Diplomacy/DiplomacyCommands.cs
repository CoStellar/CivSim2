using System;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// Offers a treaty to another civilization (GDD 9.1; stage 4d). The other side's diplomat (or player) answers in its
/// next decisions phase; an unanswered proposal lapses. Peace is the proposal that ends a war.
/// </summary>
public sealed record ProposeTreatyCommand(GameContent Content, CivId Other, TreatyKind Kind) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ) =>
        Treaties.CannotPropose(Content, state, civ, Other, Kind) is { } reason ? CommandResult.Fail(reason) : CommandResult.Ok;

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Treaties.Propose(new TreatyProposal(civ, Other, Kind, state.Month, ProposalStatus.Pending));
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

        return Accept && Treaties.CannotSign(Content, state, From, civ, Kind) is { } reason ? CommandResult.Fail(reason) : CommandResult.Ok;
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
            book.SetProposal(k, book.Proposals[k] with { Status = ProposalStatus.Declined, Month = state.Month });
            return null;
        }

        book.RemoveProposalAt(k);
        return Treaties.Sign(Content, state, From, civ, Kind);
    }
}

/// <summary>Declares war on another civilization (GDD 9.1, 9.3; stage 4d: a state of war, armies come in stage 4e).</summary>
public sealed record DeclareWarCommand(GameContent Content, CivId Target) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ) =>
        Treaties.CannotDeclareWar(state, civ, Target) is { } reason ? CommandResult.Fail(reason) : CommandResult.Ok;

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ) => Treaties.DeclareWar(Content, state, civ, Target);
}
