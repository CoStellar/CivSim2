using CivSim.Core.State;

namespace CivSim.Core.Commands;

/// <summary>Renames the issuing civilization. The first real command; also used by determinism tests.</summary>
public sealed record RenameCivilizationCommand(string NewName) : ICommand
{
    /// <summary>Longest allowed name.</summary>
    public const int MaxLength = 40;

    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Diplomacy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        CivilizationState? target = state.FindCivilization(civ);
        if (target is null || !target.IsAlive)
        {
            return CommandResult.Fail("Civilization does not exist.");
        }

        if (string.IsNullOrWhiteSpace(NewName) || NewName.Length > MaxLength)
        {
            return CommandResult.Fail($"Name must be 1-{MaxLength} characters.");
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        state.FindCivilization(civ)!.Name = NewName.Trim();
    }
}
