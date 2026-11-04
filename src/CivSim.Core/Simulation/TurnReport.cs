using System;
using System.Collections.Generic;
using CivSim.Core.Commands;

namespace CivSim.Core.Simulation;

/// <summary>What happened in the last turn: timings and command outcomes. Reused between turns.</summary>
public sealed class TurnReport
{
    private readonly long[] _phaseTicks = new long[TurnPhases.Count];
    private readonly List<RejectedCommand> _rejected = [];

    /// <summary>The turn number this report describes.</summary>
    public long Turn { get; internal set; }

    /// <summary>Commands applied this turn.</summary>
    public int AppliedCommands { get; internal set; }

    /// <summary>Commands rejected this turn, with reasons.</summary>
    public IReadOnlyList<RejectedCommand> Rejected => _rejected;

    /// <summary>Total duration of the turn in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public long TotalTicks { get; internal set; }

    /// <summary>Duration of one phase in <see cref="System.Diagnostics.Stopwatch"/> ticks.</summary>
    public long PhaseTicks(TurnPhase phase) => _phaseTicks[(int)phase];

    /// <summary>Duration of one phase.</summary>
    public TimeSpan PhaseDuration(TurnPhase phase) => System.Diagnostics.Stopwatch.GetElapsedTime(0, _phaseTicks[(int)phase]);

    internal void Reset(long turn)
    {
        Turn = turn;
        AppliedCommands = 0;
        TotalTicks = 0;
        _rejected.Clear();
        Array.Clear(_phaseTicks);
    }

    internal void SetPhaseTicks(TurnPhase phase, long ticks) => _phaseTicks[(int)phase] = ticks;

    internal void AddRejected(CommandEnvelope envelope, string reason) => _rejected.Add(new RejectedCommand(envelope, reason));
}

/// <summary>A command that failed validation.</summary>
public readonly record struct RejectedCommand(CommandEnvelope Envelope, string Reason);
