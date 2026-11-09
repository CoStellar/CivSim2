using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Tests.Simulation;

/// <summary>Records the order in which phases run.</summary>
internal sealed class PhaseRecorder(TurnPhase phase, List<TurnPhase> log) : ITurnSystem
{
    public TurnPhase Phase => phase;

    public void Execute(TurnContext context) => log.Add(phase);
}

/// <summary>Draws random numbers every turn, like a real system would.</summary>
internal sealed class RandomConsumer : ITurnSystem
{
    public TurnPhase Phase => TurnPhase.World;

    public void Execute(TurnContext context)
    {
        DeterministicRandom rng = context.Random[RandomStreamId.Events];
        int draws = rng.NextInt(1, 5);
        for (int i = 0; i < draws; i++)
        {
            rng.NextULong();
        }
    }
}

/// <summary>A fake AI: each civilization sometimes renames itself, using its own derived generator.</summary>
internal sealed class RandomRenamer : ICommandSource
{
    public void IssueCommands(GameState state, CommandQueue queue)
    {
        foreach (CivilizationState civ in state.Civilizations)
        {
            DeterministicRandom rng = state.Random.Derive(RandomStreamId.Ai, (ulong)civ.Id.Value, state.Turn);
            if (rng.ChancePercent(20))
            {
                queue.Enqueue(PlayerId.Ai, civ.Id, new RenameCivilizationCommand($"Tribe {rng.NextInt(1000)}"));
            }
        }
    }
}

/// <summary>A command in a chosen category that logs when it is applied.</summary>
internal sealed record LoggingCommand(CommandCategory Category, string Tag, List<string> Log) : ICommand
{
    public CommandResult Validate(GameState state, CivId civ) => CommandResult.Ok;

    public void Apply(GameState state, CivId civ) => Log.Add(Tag);
}

/// <summary>Writes the given chronicle lines in groups each turn: (text, group, civilization).</summary>
internal sealed class GroupedChronicler(params (string Text, string Group, int Civ)[] lines) : ITurnSystem
{
    public TurnPhase Phase => TurnPhase.World;

    public void Execute(TurnContext context)
    {
        context.Chronicle("Before.");
        foreach ((string text, string group, int civ) in lines)
        {
            context.Chronicle(text, group, civ);
        }

        context.Chronicle("After.");
    }
}
