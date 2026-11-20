using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>One command a player gave, with the tick it was applied at (a save is the game's options and these).</summary>
public sealed record SavedCommand(long Tick, int Civ, PlayCommandKind Kind, int A, int Col, int Row, bool Flag, int B, int C, int[]? Cells, int D)
{
    /// <summary>The command as it came from the client.</summary>
    public PlayCommandMessage ToMessage() => new(0, Kind, A, Col, Row, Flag, B, C, Cells, D);

    /// <summary>Records a command given at a tick.</summary>
    public static SavedCommand Of(long tick, int civ, PlayCommandMessage m) => new(tick, civ, m.Kind, m.A, m.Col, m.Row, m.Flag, m.B, m.C, m.Cells, m.D);
}

/// <summary>
/// A saved game (sprint to the demo, 2026-11-20). The simulation is deterministic, so a save holds what started the game and every
/// command its players gave; loading starts the same game and plays the commands again at their ticks. <see cref="Hash"/> checks
/// that the result is the game that was saved (a save from other rules or data plays on, but differently).
/// </summary>
public sealed record SavedGame
{
    /// <summary>Format of the file.</summary>
    public int Format { get; init; } = 1;

    /// <summary>Name the player gave it.</summary>
    public string Name { get; init; } = "";

    /// <summary>When it was written (UTC, for the list only).</summary>
    public DateTime SavedAt { get; init; }

    /// <summary>The options of the game.</summary>
    public required PlayOptions Options { get; init; }

    /// <summary>Peoples led by players.</summary>
    public int[] Players { get; init; } = [0];

    /// <summary>Tick of the save.</summary>
    public long Tick { get; init; }

    /// <summary>State hash at <see cref="Tick"/>.</summary>
    public ulong Hash { get; init; }

    /// <summary>Era of the first player's people (for the list).</summary>
    public string Era { get; init; } = "";

    /// <summary>The players' commands in the order they were applied.</summary>
    public List<SavedCommand> Commands { get; init; } = [];
}

/// <summary>Where saves live and how they are written, read and listed.</summary>
public static class PlaySaves
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    /// <summary>The saves folder (user data, outside the game's folder).</summary>
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CivSim2", "saves");

    /// <summary>Name of the automatic save.</summary>
    public const string AutoName = "autosave";

    /// <summary>The path of a save by its name.</summary>
    public static string PathOf(string name) => Path.Combine(Folder, Clean(name) + ".civsave");

    /// <summary>Writes a save; returns where.</summary>
    public static string Write(SavedGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        Directory.CreateDirectory(Folder);
        string path = PathOf(save.Name);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(save, Json));
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Reads a save.</summary>
    public static SavedGame Read(string path) =>
        JsonSerializer.Deserialize<SavedGame>(File.ReadAllText(path), Json) ?? throw new InvalidDataException($"{path} is not a save.");

    /// <summary>Saves in the folder, newest first: path, name, when, game minute, era.</summary>
    public static IReadOnlyList<(string Path, string Name, DateTime SavedAt, long Minute, string Era)> List()
    {
        var list = new List<(string, string, DateTime, long, string)>();
        if (!Directory.Exists(Folder))
        {
            return list;
        }

        foreach (string path in Directory.GetFiles(Folder, "*.civsave"))
        {
            try
            {
                SavedGame s = Read(path);
                list.Add((path, s.Name, s.SavedAt, s.Tick / Sim.Content.SimContent.TicksPerMinute, s.Era));
            }
            catch (Exception e) when (e is JsonException or InvalidDataException or IOException)
            {
                // A broken file is left out of the list.
            }
        }

        return [.. list.OrderByDescending(x => x.Item3)];
    }

    // A name safe for a file.
    private static string Clean(string name)
    {
        string cleaned = new([.. (name ?? "").Trim().Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ' ? ch : '_')]);
        return cleaned.Length == 0 ? "save " + DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture) : cleaned;
    }
}
