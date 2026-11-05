using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivSim.Core.State;
using CivSim.Core.WorldGen;

namespace CivSim.Headless;

/// <summary>Command-line options of the headless runner.</summary>
public sealed record HeadlessOptions(ulong Seed, int Turns, MapSize MapSize, string? ReportPath, bool Quiet)
{
    /// <summary>CSV file with population and food per civilization, or null for none.</summary>
    public string? StatsPath { get; init; }

    /// <summary>Write a stats row every this many turns.</summary>
    public int StatsEvery { get; init; } = 10;

    /// <summary>Folder for PNG map previews, or null for none.</summary>
    public string? PngFolder { get; init; }

    /// <summary>Hex size in the PNG preview (k: hexes are 4k px wide).</summary>
    public int PngScale { get; init; } = 3;

    /// <summary>Shape of the land.</summary>
    public WorldType WorldType { get; init; } = WorldType.Continents;

    /// <summary>Share of water in percent, or null for the planet preset's.</summary>
    public int? WaterPercent { get; init; }

    /// <summary>Planet preset key from data/planets.json.</summary>
    public string Planet { get; init; } = "earth";

    /// <summary>Axial tilt in tenths of a degree, or null for the preset's.</summary>
    public int? AxialTiltTenths { get; init; }

    /// <summary>Year length in Earth years × 100, or null for the preset's.</summary>
    public int? YearHundredths { get; init; }

    /// <summary>Star brightness in percent, or null for the preset's.</summary>
    public int? StarBrightnessPercent { get; init; }

    /// <summary>Orbit eccentricity in thousandths, or null for the preset's.</summary>
    public int? EccentricityPerMille { get; init; }

    /// <summary>How much rain the planet gets, or null for the preset's.</summary>
    public WorldMoisture? Moisture { get; init; }

    /// <summary>How fast humidity falls away from the sea.</summary>
    public WorldInland Inland { get; init; }

    /// <summary>Share of hills and mountains, or null for the preset's.</summary>
    public WorldRelief? Relief { get; init; }

    /// <summary>How many deposits there are.</summary>
    public WorldResources Resources { get; init; }

    /// <summary>Number of civilizations, or null for the map size default.</summary>
    public int? Civilizations { get; init; }

    /// <summary>Game settings for these options: the planet preset first, then every value given on the command line.</summary>
    public GameSettings ToSettings(Core.Content.GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Core.Content.PlanetPreset preset = content.Planets.FirstOrDefault(p => p.Key == Planet)
            ?? throw new ArgumentException($"Unknown planet '{Planet}'. Known: {string.Join(", ", content.Planets.Select(p => p.Key))}.");
        GameSettings s = MapSizes.Settings(MapSize, Seed).WithPreset(preset);
        var planet = new PlanetParameters(
            AxialTiltTenths ?? preset.Planet.AxialTiltTenths,
            YearHundredths ?? preset.Planet.YearHundredths,
            StarBrightnessPercent ?? preset.Planet.StarBrightnessPercent,
            EccentricityPerMille ?? preset.Planet.EccentricityThousandths);
        planet.Validate();
        return s with
        {
            Planet = planet,
            CivilizationCount = Civilizations ?? s.CivilizationCount,
            WorldType = WorldType,
            WaterPercent = WaterPercent ?? s.WaterPercent,
            Moisture = Moisture ?? s.Moisture,
            Inland = Inland,
            Relief = Relief ?? s.Relief,
            Resources = Resources,
        };
    }

    /// <summary>Usage text.</summary>
    public const string Usage =
        "Usage: CivSim.Headless [--seed N] [--turns N] [--map-size test|standard|large|huge] [--report out.csv] [--stats stats.csv] [--stats-every N] [--quiet]\n" +
        "       [--world-type continents|pangea|archipelago] [--water 30-80] [--png folder] [--png-scale 1-12]\n" +
        "       [--planet earth|mars|uranus] [--tilt DEGREES] [--year EARTH_YEARS] [--brightness 80-120] [--eccentricity 0-0.3]\n" +
        "       [--moisture normal|dry|wet] [--inland normal|dry|humid] [--relief normal|flat|mountainous]\n" +
        "       [--resources normal|scarce|rich] [--civs 1-64]\n" +
        "       --turns 0 only generates the world.";

    /// <summary>Parses arguments. Returns null and an error message when they are invalid.</summary>
    public static HeadlessOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        ulong seed = 1;
        int turns = 100;
        MapSize size = MapSize.Standard;
        string? report = null;
        string? stats = null;
        int statsEvery = 10;
        bool quiet = false;
        string? png = null;
        int pngScale = 3;
        WorldType worldType = WorldType.Continents;
        int? water = null;
        string planet = "earth";
        int? tilt = null;
        int? year = null;
        int? brightness = null;
        int? eccentricity = null;
        WorldMoisture? moisture = null;
        WorldInland inland = WorldInland.Normal;
        WorldRelief? relief = null;
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
                case "--stats":
                    stats = Next();
                    if (string.IsNullOrWhiteSpace(stats))
                    {
                        error = "--stats needs a file path.";
                        return null;
                    }

                    break;
                case "--stats-every":
                    if (!int.TryParse(Next(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out statsEvery) || statsEvery < 1)
                    {
                        error = "--stats-every must be a positive whole number.";
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
                    if (!int.TryParse(Next(), NumberStyles.None, CultureInfo.InvariantCulture, out int w) || w is < 30 or > 80)
                    {
                        error = "--water must be 30 to 80 (percent).";
                        return null;
                    }

                    water = w;

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
                case "--planet":
                    planet = Next()?.ToLowerInvariant() ?? string.Empty;
                    break;
                case "--tilt":
                    if (!TryScaled(Next(), 10, PlanetParameters.TiltRange, out int t))
                    {
                        error = "--tilt must be 0 to 180 degrees (one decimal place).";
                        return null;
                    }

                    tilt = t;
                    break;
                case "--year":
                    if (!TryScaled(Next(), 100, PlanetParameters.YearRange, out int y))
                    {
                        error = "--year must be 0.1 to 500 Earth years (two decimal places).";
                        return null;
                    }

                    year = y;
                    break;
                case "--brightness":
                    if (!TryScaled(Next(), 1, PlanetParameters.BrightnessRange, out int b))
                    {
                        error = "--brightness must be 80 to 120 (percent of Earth's sunlight).";
                        return null;
                    }

                    brightness = b;
                    break;
                case "--eccentricity":
                    if (!TryScaled(Next(), 1000, PlanetParameters.EccentricityRange, out int e))
                    {
                        error = "--eccentricity must be 0 to 0.3.";
                        return null;
                    }

                    eccentricity = e;
                    break;
                case "--inland":
                    if (!TryEnum(Next(), out inland))
                    {
                        error = "--inland must be normal, dry or humid.";
                        return null;
                    }

                    break;
                case "--moisture":
                    if (!TryEnum(Next(), out WorldMoisture m))
                    {
                        error = "--moisture must be normal, dry or wet.";
                        return null;
                    }

                    moisture = m;

                    break;
                case "--relief":
                    if (!TryEnum(Next(), out WorldRelief r))
                    {
                        error = "--relief must be normal, flat or mountainous.";
                        return null;
                    }

                    relief = r;

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
            StatsPath = stats,
            StatsEvery = statsEvery,
            WorldType = worldType,
            WaterPercent = water,
            Planet = planet,
            AxialTiltTenths = tilt,
            YearHundredths = year,
            StarBrightnessPercent = brightness,
            EccentricityPerMille = eccentricity,
            Moisture = moisture,
            Inland = inland,
            Relief = relief,
            Resources = resources,
            Civilizations = civs,
        };
    }

    // Parses a decimal like "23.4" into a whole number of 1/scale units inside the range.
    private static bool TryScaled(string? text, int scale, (int Min, int Max) range, out int value)
    {
        value = 0;
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal d))
        {
            return false;
        }

        decimal scaled = d * scale;
        if (scaled != decimal.Truncate(scaled) || scaled < range.Min || scaled > range.Max)
        {
            return false;
        }

        value = (int)scaled;
        return true;
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

    /// <summary>258×160, 12 civilizations.</summary>
    Standard,

    /// <summary>386×240, 20 civilizations.</summary>
    Large,

    /// <summary>514×320, 32 civilizations.</summary>
    Huge,
}

/// <summary>Helpers for <see cref="MapSize"/>.</summary>
public static class MapSizes
{
    /// <summary>Settings for a preset.</summary>
    public static GameSettings Settings(MapSize size, ulong seed) => size switch
    {
        MapSize.Test => new GameSettings(seed, 66, 40, 4),
        MapSize.Standard => new GameSettings(seed, 258, 160, 12),
        MapSize.Large => new GameSettings(seed, 386, 240, 20),
        MapSize.Huge => new GameSettings(seed, 514, 320, 32),
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
