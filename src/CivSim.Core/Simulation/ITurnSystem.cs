namespace CivSim.Core.Simulation;

/// <summary>
/// A simulation system that runs in one phase of every turn (economy, population...). Systems are registered
/// with <see cref="TurnRunner"/> and run in registration order within their phase.
/// </summary>
public interface ITurnSystem
{
    /// <summary>The phase this system runs in.</summary>
    TurnPhase Phase { get; }

    /// <summary>Runs the system for the current turn.</summary>
    void Execute(TurnContext context);
}
