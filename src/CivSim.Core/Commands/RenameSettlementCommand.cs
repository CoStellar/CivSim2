using CivSim.Core.State;

namespace CivSim.Core.Commands;

/// <summary>
/// Renames a settlement of the issuing civilization (GDD 5.6: whoever dislikes a generated name changes it). The name
/// must be free: no other settlement or civilization may carry it.
/// </summary>
public sealed record RenameSettlementCommand(SettlementId Settlement, string NewName) : ICommand
{
    /// <summary>Longest allowed name.</summary>
    public const int MaxLength = 40;

    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        System.ArgumentNullException.ThrowIfNull(state);
        SettlementState? s = state.FindSettlement(Settlement);
        if (s is null || s.Owner != civ)
        {
            return CommandResult.Fail("No such settlement of this civilization.");
        }

        string name = NewName?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > MaxLength)
        {
            return CommandResult.Fail($"Name must be 1-{MaxLength} characters.");
        }

        if (!string.Equals(name, s.Name, System.StringComparison.OrdinalIgnoreCase) && Naming.Names.Used(state).Contains(name))
        {
            return CommandResult.Fail($"The name {name} is already taken.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        System.ArgumentNullException.ThrowIfNull(state);
        state.FindSettlement(Settlement)!.Name = NewName.Trim();
    }
}
