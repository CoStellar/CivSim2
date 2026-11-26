using System;
using System.Collections.Generic;
using System.IO;
using CivSim.Core.Hashing;
using CivSim.Core.State;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Server.Saves;

namespace CivSim.Server.Replays;

/// <summary>
/// Records a game as it is played (T11): the start (seed and settings, or a save), every event from outside the simulation
/// and, every <see cref="CheckpointTicks"/> ticks, the state hash whole and per part. A few kilobytes for a game of hours:
/// commands are few, and checkpoints are 10 numbers each.
/// </summary>
public sealed class ReplayRecorder
{
    /// <summary>Default ticks between two checkpoints: one minute of the game at x1.</summary>
    public const int DefaultCheckpointTicks = 600;

    private readonly List<ReplayEvent> _events = [];
    private readonly List<ReplayCheckpoint> _checkpoints = [];
    private readonly ReplaySettings _settings;
    private readonly byte[]? _startSave;
    private long _lastTick;

    /// <summary>
    /// Starts recording <paramref name="state"/> as it is now. With <paramref name="newGame"/> the replay starts from the
    /// settings (the world is generated again from the seed); otherwise from a save of the state (a loaded game).
    /// </summary>
    public ReplayRecorder(GameState state, bool newGame, int checkpointTicks = DefaultCheckpointTicks)
    {
        ArgumentNullException.ThrowIfNull(state);
        _settings = ReplaySettings.From(state.Settings);
        if (!newGame)
        {
            using var save = new MemoryStream();
            SaveGameSerializer.Save(state, save);
            _startSave = save.ToArray();
        }

        StartTick = state.Turn;
        _lastTick = state.Turn;
        CheckpointTicks = Math.Max(1, checkpointTicks);
        foreach (CivilizationState c in state.Civilizations)
        {
            if (c.Controller != PlayerId.Ai)
            {
                Control(state.Turn, c.Id, c.Controller); // controllers are session data: the replay sets them again
            }
        }

        Checkpoint(state);
    }

    /// <summary>The tick the recording started at.</summary>
    public long StartTick { get; }

    /// <summary>Ticks between two checkpoints.</summary>
    public int CheckpointTicks { get; }

    /// <summary>Events recorded so far.</summary>
    public IReadOnlyList<ReplayEvent> Events => _events;

    /// <summary>Checkpoints recorded so far.</summary>
    public IReadOnlyList<ReplayCheckpoint> Checkpoints => _checkpoints;

    /// <summary>A player's command queued before <paramref name="tick"/>.</summary>
    public void Command(long tick, PlayerId player, CivId civ, ICommandDto command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _events.Add(new ReplayEvent(tick, ReplayEventKind.Command, player.Value, civ.Value, command));
    }

    /// <summary>A rename applied at once before <paramref name="tick"/>, for civilization <paramref name="target"/>.</summary>
    public void Rename(long tick, CivId target, ICommandDto command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _events.Add(new ReplayEvent(tick, ReplayEventKind.Rename, 0, target.Value, command));
    }

    /// <summary><paramref name="civ"/> is played by <paramref name="player"/> from <paramref name="tick"/> on (the AI: <see cref="PlayerId.Ai"/>).</summary>
    public void Control(long tick, CivId civ, PlayerId player) =>
        _events.Add(new ReplayEvent(tick, ReplayEventKind.Control, player.Value, civ.Value, null));

    /// <summary>Call after every tick: keeps the end of the recording and takes a checkpoint every <see cref="CheckpointTicks"/> ticks.</summary>
    public void AfterTick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _lastTick = state.Turn;
        if ((state.Turn - StartTick) % CheckpointTicks == 0)
        {
            Checkpoint(state);
        }
    }

    /// <summary>The recording so far, ending at the state after the last tick.</summary>
    public ReplayData ToData() => new(ProtocolInfo.Version, SaveGameSerializer.FormatVersion, _settings, _startSave, StartTick, _lastTick,
        CheckpointTicks, [.. GameStateHash.SectionNames], [.. _events], [.. _checkpoints]);

    /// <summary>Writes the recording so far to <paramref name="path"/>.</summary>
    public void Write(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        using FileStream file = File.Create(path);
        ReplaySerializer.Write(ToData(), file);
    }

    private void Checkpoint(GameState state) =>
        _checkpoints.Add(new ReplayCheckpoint(state.Turn, GameStateHash.Compute(state), GameStateHash.Sections(state)));
}
