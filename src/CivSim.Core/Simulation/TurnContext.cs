using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>Everything a system may use while a turn runs.</summary>
public sealed class TurnContext
{
    internal TurnContext(GameState state)
    {
        State = state;
    }

    /// <summary>The game state being advanced.</summary>
    public GameState State { get; }

    /// <summary>Per-system random streams.</summary>
    public RandomStreams Random => State.Random;

    /// <summary>The turn being computed (completed turns + 1).</summary>
    public long CurrentTurn => State.Turn + 1;

    /// <summary>Commands for this turn, in resolution order. Filled before phase 4.</summary>
    public IReadOnlyList<CommandEnvelope> Commands => CommandList;

    internal List<CommandEnvelope> CommandList { get; } = [];

    /// <summary>Chronicle lines written during this turn (cleared when the turn starts).</summary>
    public IReadOnlyList<string> ChronicleLines => ChronicleList;

    internal List<string> ChronicleList { get; } = [];

    /// <summary>Writes a line to the chronicle (GDD 11.1, phase 11). Use for rare, notable events only.</summary>
    public void Chronicle(string text) => ChronicleList.Add(text);
}
