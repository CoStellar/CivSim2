using System;
using System.Collections.Generic;
using System.Diagnostics;
using CivSim.Core.Commands;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>
/// Advances a <see cref="GameState"/> one turn at a time through the 11 phases of GDD 11.1,
/// measures each phase and publishes an immutable snapshot at the end.
/// </summary>
public sealed class TurnRunner
{
    private readonly List<ITurnSystem>[] _systems = new List<ITurnSystem>[TurnPhases.Count];
    private readonly List<ICommandSource> _commandSources = [];
    private readonly TurnContext _context;

    /// <summary>Creates a runner for <paramref name="state"/>.</summary>
    public TurnRunner(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        State = state;
        _context = new TurnContext(state);
        for (int i = 0; i < _systems.Length; i++)
        {
            _systems[i] = [];
        }
    }

    /// <summary>The state this runner advances.</summary>
    public GameState State { get; }

    /// <summary>Commands for the next turn. Players' commands go here (through the server).</summary>
    public CommandQueue Commands { get; } = new();

    /// <summary>Latest finished snapshot for readers on other threads.</summary>
    public SnapshotBuffer<GameSnapshot> Snapshots { get; } = new();

    /// <summary>Report of the last completed turn (reused object).</summary>
    public TurnReport LastReport { get; } = new();

    /// <summary>Registers a system. Systems in the same phase run in registration order.</summary>
    public void AddSystem(ITurnSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems[(int)system.Phase].Add(system);
    }

    /// <summary>Registers a command source (e.g. AI) that runs in the Decisions phase.</summary>
    public void AddCommandSource(ICommandSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _commandSources.Add(source);
    }

    /// <summary>Runs one full turn and returns its report.</summary>
    public TurnReport RunTurn()
    {
        long turnStart = Stopwatch.GetTimestamp();
        LastReport.Reset(State.Turn + 1);

        for (int p = 0; p < TurnPhases.Count; p++)
        {
            var phase = (TurnPhase)p;
            long phaseStart = Stopwatch.GetTimestamp();

            switch (phase)
            {
                case TurnPhase.Decisions:
                    RunSystems(phase);
                    foreach (ICommandSource source in _commandSources)
                    {
                        source.IssueCommands(State, Commands);
                    }

                    break;
                case TurnPhase.CommandResolution:
                    Commands.DrainSorted(State, _context.CurrentTurn, _context.CommandList);
                    ResolveCommands();
                    RunSystems(phase);
                    break;
                case TurnPhase.Record:
                    State.Turn++;
                    RunSystems(phase);
                    Snapshots.Publish(GameSnapshot.From(State));
                    break;
                default:
                    RunSystems(phase);
                    break;
            }

            LastReport.SetPhaseTicks(phase, Stopwatch.GetTimestamp() - phaseStart);
        }

        LastReport.TotalTicks = Stopwatch.GetTimestamp() - turnStart;
        return LastReport;
    }

    private void RunSystems(TurnPhase phase)
    {
        List<ITurnSystem> systems = _systems[(int)phase];
        for (int i = 0; i < systems.Count; i++)
        {
            systems[i].Execute(_context);
        }
    }

    private void ResolveCommands()
    {
        foreach (CommandEnvelope envelope in _context.CommandList)
        {
            CommandResult result = envelope.Command.Validate(State, envelope.Civ);
            if (result.Success)
            {
                envelope.Command.Apply(State, envelope.Civ);
                LastReport.AppliedCommands++;
            }
            else
            {
                LastReport.AddRejected(envelope, result.Reason ?? "Rejected.");
            }
        }
    }
}
