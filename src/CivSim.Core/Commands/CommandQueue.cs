using System;
using System.Collections.Generic;
using CivSim.Core.State;

namespace CivSim.Core.Commands;

/// <summary>
/// Collects commands for the current turn and hands them out in a fixed, deterministic order:
/// category, then civilization (rotating every turn so nobody always goes first), then arrival order.
/// </summary>
public sealed class CommandQueue
{
    private readonly List<CommandEnvelope> _pending = [];
    private long _nextSequence;

    /// <summary>Number of commands waiting for the next turn.</summary>
    public int Count => _pending.Count;

    /// <summary>Adds a command. Returns the envelope with its assigned sequence number.</summary>
    public CommandEnvelope Enqueue(PlayerId player, CivId civ, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var envelope = new CommandEnvelope(player, civ, _nextSequence++, command);
        _pending.Add(envelope);
        return envelope;
    }

    /// <summary>
    /// Moves all pending commands into <paramref name="destination"/> sorted for resolution in <paramref name="turn"/>.
    /// <paramref name="destination"/> is cleared first; reuse it between turns to avoid allocations.
    /// </summary>
    public void DrainSorted(GameState state, long turn, List<CommandEnvelope> destination)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        destination.AddRange(_pending);
        _pending.Clear();
        destination.Sort(new ResolutionOrder(state, turn));
    }

    /// <summary>The order position of <paramref name="civ"/> in <paramref name="turn"/>; unknown civilizations go last.</summary>
    public static int CivOrder(GameState state, CivId civ, long turn)
    {
        int count = state.Civilizations.Count;
        int index = state.IndexOfCivilization(civ);
        if (index < 0 || count == 0)
        {
            return int.MaxValue;
        }

        int offset = (int)(turn % count);
        return ((index - offset) % count + count) % count;
    }

    private readonly struct ResolutionOrder(GameState state, long turn) : IComparer<CommandEnvelope>
    {
        public int Compare(CommandEnvelope x, CommandEnvelope y)
        {
            int c = x.Command.Category.CompareTo(y.Command.Category);
            if (c != 0)
            {
                return c;
            }

            c = CivOrder(state, x.Civ, turn).CompareTo(CivOrder(state, y.Civ, turn));
            if (c != 0)
            {
                return c;
            }

            return x.Sequence.CompareTo(y.Sequence);
        }
    }
}
