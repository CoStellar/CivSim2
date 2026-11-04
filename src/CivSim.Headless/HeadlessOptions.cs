using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Core.State;
using CivSim.Core.WorldGen;

namespace CivSim.Headless;

/// <summary>Command-line options of the headless runner.</summary>
public sealed record HeadlessOptions(ulong Seed, int Turns, MapSize MapSize, string? ReportPath, bool Quiet)
{
    /// <summary>Folder for PNG map previews, or null for none.</summary>
    public string? PngFolder { get; init; }

    /// <summary>Hex size in the PNG preview (k: hexes are 4k px wide).</summary>
    public int PngScale { get; init; } = 3;

    /// <summary>Shape of the land.</summary>
    public WorldType WorldType { get; init; } = WorldType.Continents;

    /// <summary>Share of water in percent.</summary>
    public int WaterPercent { get; init; } = 60;

    /// <summary>Average temperature of the planet.</summary>
    public WorldClimate Climate { get; init; }

    /// <summary>How much rain the planet gets.</summary>
    public WorldMoisture Moisture { get; init; }

    /// <summary>Share of hills and mountains.</summary>
    public WorldRelief Relief { get; init; }

    /// <summary>How many deposits there are.</summary>
    public WorldResources Resources { get; init; }

    /// <summary>Number of civilizations, or null for the map size default.</summary>
    public int? Civilizations { get; init; }

    /// <summary>Game settings for these options.</summary>
    public GameSettings ToSettings()
    {
        GameSettings s = MapSizes.Settings(MapSize, Seed);
        return s with
        {
            CivilizationCount = Civilizations ?? s.CivilizationCount,
            WorldType = WorldType,
            WaterPercent = WaterPercent,
            Climate = Climate,
            Moisture = Moisture,
            Relief = Relief,
            Resources = Resources,
        };
    }

    /// <summary>Usage text.</summary>
    public const string Usage =
        "Usage: CivSim.Headless [--seed N] [--turns N] [--map-size test|standard|large|huge] [--report out.csv] [--quiet]\n" +
        "       [--world-type continents|pangea|archipelago] [--water 30-80] [--png folder] [--png-scale 1-12]\n" +
        "       [--climate temperate|cold|hot] [--moisture normal|dry|wet] [--relief normal|flat|mountainous]\n" +
        "       [--resources normal|scarce|rich] [--civs 1-64]\n" +
        "       --turns 0 only generates the world.";

    /// <summary>Parses arguments. Returns null and an error message when they are invalid.</summary>
    public static HeadlessOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        ulong seed = 1;
        int turns = 100;
        MapSize size = MapSize.Standard;
        string? report = null;
        bool quiet = false;
        string? png = null;
        int pngScale = 3;
        WorldType worldType = WorldType.Continents;
        int water = 60;
        WorldClimate climate = WorldClimate.Temperate;
        WorldMoisture moisture = WorldMoisture.Normal;
        WorldRelief relief = WorldRelief.Normal;
        WorldResources resources = WorldResources.Normal;
        int? civs = null;
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
                    if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out turns))
                    {
                        error = "--turns needs a whole number (0 = only generate the world).";
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
                case "--png":
                    png = Next();
                    if (string.IsNullOrWhiteSpace(png))
                    {
                        error = "--png needs a folder.";
                        return null;
                    }

                    break;
                case "--png-scale":
                    if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out pngScale) || pngScale is < 1 or > 12)
                    {
                        error = "--png-scale must be 1 to 12.";
                        return null;
                    }

                    break;
                case "--water":
                    if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out water) || water is < 30 or > 80)
                    {
                        error = "--water must be 30 to 80 (percent).";
                        return null;
                    }

                    break;
                case "--world-type":
                    switch (Next()?.ToLowerInvariant())
                    {
                        case "continents":
                            worldType = WorldType.Continents;
                            break;
                        case "pangea":
                            worldType = WorldType.Pangea;
                            break;
                        case "archipelago":
                            worldType = WorldType.Archipelago;
                            break;
                        default:
                            error = "--world-type must be continents, pangea or archipelago.";
                            return null;
                    }

                    break;
                case "--climate":
                    if (!TryEnum(Next(), out climate))
                    {
                        error = "--climate must be temperate, cold or hot.";
                        return null;
                    }

                    break;
                case "--moisture":
                    if (!TryEnum(Next(), out moisture))
                    {
                        error = "--moisture must be normal, dry or wet.";
                        return null;
                    }

                    break;
                case "--relief":
                    if (!TryEnum(Next(), out relief))
                    {
                        error = "--relief must be normal, flat or mountainous.";
                        return null;
                    }

                    break;
                case "--resources":
                    if (!TryEnum(Next(), out resources))
                    {
                        error = "--resources must be normal, scarce or rich.";
                        return null;
                    }

                    break;
                case "--civs":
                    if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out int c) || c is < 1 or > 64)
                    {
                        error = "--civs must be 1 to 64.";
                        return null;
                    }

                    civs = c;
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

        return new HeadlessOptions(seed, turns, size, report, quiet)
        {
            PngFolder = png,
            PngScale = pngScale,
            WorldType = worldType,
            WaterPercent = water,
            Climate = climate,
            Moisture = moisture,
            Relief = relief,
            Resources = resources,
            Civilizations = civs,
        };
    }

    // Parses a lower-case enum name; numbers are rejected so only the documented words work.
    private static bool TryEnum<T>(string? text, out T value)
        where T : struct, Enum
    {
        value = default;
        return !string.IsNullOrEmpty(text) && !char.IsDigit(text[0]) && Enum.TryParse(text, ignoreCase: true, out value) && Enum.IsDefined(value);
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
