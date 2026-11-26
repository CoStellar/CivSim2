using CivSim.Core.Commands;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>
/// Something that issues commands during the Decisions phase, e.g. an AI controller. It may only read the state.
/// </summary>
public interface ICommandSource
{
    /// <summary>Adds this turn's commands to <paramref name="queue"/>. Must not modify <paramref name="state"/>.</summary>
    void IssueCommands(GameState state, CommandQueue queue);
}
