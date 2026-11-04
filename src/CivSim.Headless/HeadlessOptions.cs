using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>Command-line options of the headless runner.</summary>
public sealed record HeadlessOptions(ulong Seed, int Turns, MapSize MapSize, string? ReportPath, bool Quiet)
{
    /// <summary>Usage text.</summary>
    public const string Usage =
        "Usage: CivSim.Headless [--seed N] [--turns N] [--map-size test|standard|large|huge] [--report out.csv] [--quiet]";

    /// <summary>Parses arguments. Returns null and an error message when they are invalid.</summary>
    public static HeadlessOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        ulong seed = 1;
        int turns = 100;
        MapSize size = MapSize.Standard;
        string? report = null;
        bool quiet = false;
        error = null;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string? Next()
            {
                return i + 1 < args.Count ? args[++i] : null;
            }

            switch (arg)
            {
                case "--seed":
                    if (!ulong.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                    {
                        error = "--seed needs a non-negative whole number.";
                        return null;
                    }

                    break;
                case "--turns":
                    if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out turns) || turns < 1)
                    {
                        error = "--turns needs a positive whole number.";
                        return null;
                    }

                    break;
                case "--map-size":
                    if (!MapSizes.TryParse(Next(), out size))
                    {
                        error = "--map-size must be test, standard, large or huge.";
                        return null;
                    }

                    break;
                case "--report":
                    report = Next();
                    if (string.IsNullOrWhiteSpace(report))
                    {
                        error = "--report needs a file path.";
                        return null;
                    }

                    break;
                case "--quiet":
                    quiet = true;
                    break;
                case "--help" or "-h":
                    error = string.Empty;
                    return null;
                default:
                    error = $"Unknown option '{arg}'.";
                    return null;
            }
        }

        return new HeadlessOptions(seed, turns, size, report, quiet);
    }
}

/// <summary>Map size presets from CLAUDE.md (performance targets).</summary>
public enum MapSize
{
    /// <summary>64×40, 4 civilizations: quick checks.</summary>
    Test,

    /// <summary>256×160, 12 civilizations.</summary>
    Standard,

    /// <summary>384×240, 20 civilizations.</summary>
    Large,

    /// <summary>512×320, 32 civilizations.</summary>
    Huge,
}

/// <summary>Helpers for <see cref="MapSize"/>.</summary>
public static class MapSizes
{
    /// <summary>Settings for a preset.</summary>
    public static GameSettings Settings(MapSize size, ulong seed) => size switch
    {
        MapSize.Test => new GameSettings(seed, 64, 40, 4),
        MapSize.Standard => new GameSettings(seed, 256, 160, 12),
        MapSize.Large => new GameSettings(seed, 384, 240, 20),
        MapSize.Huge => new GameSettings(seed, 512, 320, 32),
        _ => throw new ArgumentOutOfRangeException(nameof(size)),
    };

    /// <summary>Parses a preset name (case-insensitive).</summary>
    public static bool TryParse(string? text, out MapSize size)
    {
        switch (text?.ToLowerInvariant())
        {
            case "test":
                size = MapSize.Test;
                return true;
            case "standard":
                size = MapSize.Standard;
                return true;
            case "large":
                size = MapSize.Large;
                return true;
            case "huge":
                size = MapSize.Huge;
                return true;
            default:
                size = MapSize.Standard;
                return false;
        }
    }
}
