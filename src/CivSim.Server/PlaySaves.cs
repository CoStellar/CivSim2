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
    public static SavedGame Read(string path)
    {
        SavedGame save;
        try
        {
            save = JsonSerializer.Deserialize<SavedGame>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("It is not a save.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("The file is damaged: " + e.Message.Split('.')[0] + ".", e);
        }

        Check(save);
        return save;
    }

    /// <summary>The longest game a save may hold: replaying more would keep the game loading for minutes (abuse tests 2026-11-24).</summary>
    public const long MaxTicks = 24L * 60 * 60 * 10;

    /// <summary>
    /// Refuses a save that cannot be the record of a real game (abuse tests 2026-11-24: an edited file crashed the game, froze it
    /// replaying two billion ticks, read a file outside the scenarios folder or replayed commands for the AI).
    /// </summary>
    public static void Check(SavedGame save)
    {
        ArgumentNullException.ThrowIfNull(save);
        PlayOptions o = save.Options ?? throw new InvalidDataException("The save has no game options.");
        if (o.Civilizations is < 2 or > 8)
        {
            throw new InvalidDataException($"The save has {o.Civilizations} civilizations; a game has 2 to 8.");
        }

        if (o.LegacyWidth is < 20 or > 400 || o.LegacyHeight is < 12 or > 250)
        {
            throw new InvalidDataException($"The save's map {o.LegacyWidth}x{o.LegacyHeight} is not one the game makes.");
        }

        if (o.Scenario is { Length: > 0 } sc && (sc.IndexOfAny(['/', '\\', ':']) >= 0 || sc.Contains("..", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The save names a scenario outside the scenarios folder.");
        }

        if (save.Tick is < 0 or > MaxTicks)
        {
            throw new InvalidDataException("The save's time is not one a game reaches.");
        }

        foreach (SavedCommand c in save.Commands ?? [])
        {
            if (c.Tick < 0 || c.Tick > save.Tick || Array.IndexOf(save.Players ?? [], c.Civ) < 0)
            {
                throw new InvalidDataException("The save holds a command that no player of it could have given.");
            }
        }
    }

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
        cleaned = cleaned.Length > 80 ? cleaned[..80].TrimEnd() : cleaned; // abuse tests 2026-11-24: a 400-letter name could not be written
        return cleaned.Length == 0 ? "save " + DateTime.UtcNow.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture) : cleaned;
    }
}
