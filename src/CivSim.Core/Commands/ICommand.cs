using CivSim.Core.State;

namespace CivSim.Core.Commands;

/// <summary>
/// A request to change the game state. AI and players issue the same commands; nothing else changes the state.
/// Implementations must be immutable and must not keep references to game state.
/// </summary>
public interface ICommand
{
    /// <summary>Resolution group (diplomacy, economy, movement).</summary>
    CommandCategory Category { get; }

    /// <summary>Checks game rules for <paramref name="civ"/> issuing this command against the current state.</summary>
    CommandResult Validate(GameState state, CivId civ);

    /// <summary>Applies the command. Called only after <see cref="Validate"/> succeeded in the same turn.</summary>
    void Apply(GameState state, CivId civ);
}
