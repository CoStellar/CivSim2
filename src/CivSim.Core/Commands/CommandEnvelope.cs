using CivSim.Core.State;

namespace CivSim.Core.Commands;

/// <summary>
/// A command plus who sent it. Every command carries a player and a civilization, even in single player.
/// <see cref="Sequence"/> is assigned by the queue and makes the resolution order total.
/// </summary>
public readonly record struct CommandEnvelope(PlayerId Player, CivId Civ, long Sequence, ICommand Command);
