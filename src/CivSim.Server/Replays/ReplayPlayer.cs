using System;
using System.Collections.Generic;
using System.IO;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Server.Saves;

namespace CivSim.Server.Replays;

/// <summary>Where a replay drifted apart from its recording: the first checkpoint that differs and its parts.</summary>
/// <param name="Tick">Tick of the checkpoint.</param>
/// <param name="Expected">The recorded hash.</param>
/// <param name="Actual">The hash of the replay.</param>
/// <param name="Sections">Names of the parts whose hashes differ (the systems to look at).</param>
public sealed record ReplayDivergence(long Tick, ulong Expected, ulong Actual, IReadOnlyList<string> Sections);

/// <summary>What a replay found.</summary>
/// <param name="State">The state at the end of the replay.</param>
/// <param name="Checked">Checkpoints compared.</param>
/// <param name="Divergence">The first checkpoint that differs, or null when the replay matches the recording.</param>
public sealed record ReplayResult(GameState State, int Checked, ReplayDivergence? Divergence)
{
    /// <summary>True when every checkpoint matched.</summary>
    public bool Matches => Divergence is null;
}

/// <summary>
/// Plays a recorded game again (T11): the same start, the same events before the same ticks, the same systems as the
/// server (<see cref="CoreSystems.Register"/>), and compares the state hash at every checkpoint. Deterministic games
/// (CLAUDE.md, rule 4) replay exactly; a difference names the parts of the state that drifted first.
/// </summary>
public static class ReplayPlayer
{
    /// <summary>
    /// Replays <paramref name="data"/> up to its end (or <paramref name="untilTick"/>). With <paramref name="stopAtDivergence"/>
    /// it stops at the first checkpoint that differs.
    /// </summary>
    public static ReplayResult Play(ReplayData data, GameContent content, long untilTick = long.MaxValue, bool stopAtDivergence = true)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(content);
        GameState state = Start(data, content);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, content);
        long end = Math.Min(data.EndTick, untilTick);
        int nextEvent = 0;
        int nextCheck = 0;
        int checkedCount = 0;
        ReplayDivergence? divergence = null;
        while (true)
        {
            while (nextCheck < data.Checkpoints.Count && data.Checkpoints[nextCheck].Tick < state.Turn)
            {
                nextCheck++;
            }

            if (nextCheck < data.Checkpoints.Count && data.Checkpoints[nextCheck].Tick == state.Turn)
            {
                ReplayDivergence? d = Compare(data, data.Checkpoints[nextCheck], state);
                checkedCount++;
                nextCheck++;
                if (d is not null && divergence is null)
                {
                    divergence = d;
                    if (stopAtDivergence)
                    {
                        break;
                    }
                }
            }

            while (nextEvent < data.Events.Count && data.Events[nextEvent].Tick <= state.Turn)
            {
                Apply(content, runner, data.Events[nextEvent]);
                nextEvent++;
            }

            if (state.Turn >= end)
            {
                break;
            }

            runner.RunTurn();
        }

        return new ReplayResult(state, checkedCount, divergence);
    }

    /// <summary>Reads a replay file and plays it (<see cref="Play"/>).</summary>
    public static ReplayResult PlayFile(string path, GameContent content, long untilTick = long.MaxValue)
    {
        using FileStream file = File.OpenRead(path);
        return Play(ReplaySerializer.Read(file), content, untilTick);
    }

    private static GameState Start(ReplayData data, GameContent content)
    {
        if (data.StartSave is { } save)
        {
            using var stream = new MemoryStream(save, writable: false);
            return SaveGameSerializer.Load(stream);
        }

        return GameState.CreateNew(data.Settings.ToSettings(), content, out _);
    }

    // The event as the server handled it: commands queued for the tick, renames applied at once, control set.
    private static void Apply(GameContent content, TurnRunner runner, ReplayEvent e)
    {
        GameState state = runner.State;
        var civ = new CivId(e.Civ);
        switch (e.Kind)
        {
            case ReplayEventKind.Command:
                if (CommandMapper.ToCore(e.Command, content) is { } command)
                {
                    runner.Commands.Enqueue(new PlayerId(e.Player), civ, command);
                }

                break;
            case ReplayEventKind.Rename:
                if (CommandMapper.ToCore(e.Command, content) is { } rename && rename.Validate(state, civ).Success)
                {
                    rename.Apply(state, civ);
                }

                break;
            case ReplayEventKind.Control:
                if (state.FindCivilization(civ) is { } c)
                {
                    c.Controller = new PlayerId(e.Player);
                }

                break;
        }
    }

    private static ReplayDivergence? Compare(ReplayData data, ReplayCheckpoint expected, GameState state)
    {
        ulong hash = GameStateHash.Compute(state);
        if (hash == expected.Hash)
        {
            return null;
        }

        ulong[] now = GameStateHash.Sections(state);
        var parts = new List<string>();
        for (int k = 0; k < expected.Sections.Length; k++)
        {
            if (k >= now.Length || now[k] != expected.Sections[k])
            {
                parts.Add(k < data.SectionNames.Length ? data.SectionNames[k] : $"part {k}");
            }
        }

        return new ReplayDivergence(expected.Tick, expected.Hash, hash, parts);
    }
}
