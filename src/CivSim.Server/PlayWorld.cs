using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.WorldGen;

namespace CivSim.Server;

/// <summary>
/// Advanced world settings of a new game (Patryk, 2026-11-25, PR W1): the planet preset, its rain, inland humidity, relief and
/// deposits, and the numbers of the planet and its star. Plain values only (the client sends them, the save keeps them).
/// Text keys as in data/planets.json; null or empty numbers mean "as the preset".
/// </summary>
public sealed record PlayWorld
{
    /// <summary>Planet preset key (data/planets.json).</summary>
    public string Planet { get; init; } = "earth";

    /// <summary>normal, dry or wet; null = the preset's.</summary>
    public string? Moisture { get; init; }

    /// <summary>How fast humidity falls away from the sea: normal, dry or humid.</summary>
    public string Inland { get; init; } = "normal";

    /// <summary>normal, flat or mountainous; null = the preset's.</summary>
    public string? Relief { get; init; }

    /// <summary>How many natural deposits: normal, scarce or rich.</summary>
    public string Resources { get; init; } = "normal";

    /// <summary>Axial tilt, tenths of a degree.</summary>
    public int? AxialTiltTenths { get; init; }

    /// <summary>Orbit eccentricity, thousandths.</summary>
    public int? EccentricityPerMille { get; init; }

    /// <summary>Star brightness, thousandths of the Sun's.</summary>
    public long? LuminosityThousandths { get; init; }

    /// <summary>Distance from the star, thousandths of an astronomical unit.</summary>
    public long? DistanceThousandths { get; init; }

    /// <summary>Year, thousandths of an Earth year.</summary>
    public long? YearThousandths { get; init; }

    /// <summary>Sunlight, thousandths of Earth's.</summary>
    public long? LightThousandths { get; init; }

    /// <summary>CO2, millionths of a bar; null = from the carbon cycle.</summary>
    public int? Co2Microbar { get; init; }

    /// <summary>Air pressure, hundredths of a bar.</summary>
    public int? PressureHundredths { get; init; }

    /// <summary>Volcanism, percent of Earth's.</summary>
    public int? VolcanismPercent { get; init; }

    /// <summary>Swing of the star's brightness, per mille.</summary>
    public int? StarVariabilityPerMille { get; init; }

    /// <summary>Period of the star's cycle, years.</summary>
    public int? StarCycleYears { get; init; }

    /// <summary>True when nothing differs from a normal game (the world is then generated exactly as without these settings).</summary>
    public bool IsDefault => this == new PlayWorld();

    /// <summary>True when any number of the planet or its star is set (not only the preset and the choices).</summary>
    public bool HasNumbers => AxialTiltTenths is not null || EccentricityPerMille is not null || LuminosityThousandths is not null
        || DistanceThousandths is not null || YearThousandths is not null || LightThousandths is not null || Co2Microbar is not null
        || PressureHundredths is not null || VolcanismPercent is not null || StarVariabilityPerMille is not null || StarCycleYears is not null;
}

/// <summary>What the advanced settings give the world generator.</summary>
/// <param name="Planet">The planet with the overrides applied.</param>
/// <param name="Moisture">Rain.</param>
/// <param name="Inland">Inland humidity.</param>
/// <param name="Relief">Hills and mountains.</param>
/// <param name="Resources">Deposits.</param>
/// <param name="Notes">Values that had to be adjusted, in plain English (shown in the menu).</param>
public sealed record PlayWorldSetup(PlanetParameters Planet, WorldMoisture Moisture, WorldInland Inland, WorldRelief Relief, WorldResources Resources,
    IReadOnlyList<string> Notes);

/// <summary>Turns <see cref="PlayWorld"/> into generator settings and checks them.</summary>
public static class PlayWorlds
{
    /// <summary>Keys and names of the planet presets, for the menu.</summary>
    public static IReadOnlyList<(string Key, string Name)> Presets(string? dataDirectory = null) =>
        [.. CivSim.Data.ContentLoader.Load(dataDirectory).Planets.Select(p => (p.Key, p.Name))];

    /// <summary>
    /// Checks the settings as the game would use them. Returns null when they are fine (with notes on adjusted values),
    /// or the reason they cannot make a planet.
    /// </summary>
    public static string? Check(PlayWorld world, string? dataDirectory, out IReadOnlyList<string> notes)
    {
        ArgumentNullException.ThrowIfNull(world);
        notes = [];
        try
        {
            PlayWorldSetup setup = Build(CivSim.Data.ContentLoader.Load(dataDirectory), world);
            notes = setup.Notes;
            return null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException)
        {
            return e.Message;
        }
    }

    /// <summary>The generator settings for a world (throws <see cref="ArgumentException"/> when they cannot make a planet).</summary>
    public static PlayWorldSetup Build(GameContent content, PlayWorld world)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(world);
        PlanetPreset preset = content.Planets.FirstOrDefault(p => p.Key == world.Planet)
            ?? throw new ArgumentException($"Unknown planet '{world.Planet}'.", nameof(world));
        (PlanetParameters planet, IReadOnlyList<string> notes) = PlanetSetup.Build(preset.Planet, preset.AutoCo2, new PlanetOverrides
        {
            AxialTiltTenths = world.AxialTiltTenths,
            EccentricityThousandths = world.EccentricityPerMille,
            LuminosityThousandths = world.LuminosityThousandths,
            DistanceThousandths = world.DistanceThousandths,
            YearThousandths = world.YearThousandths,
            LightThousandths = world.LightThousandths,
            Co2Microbar = world.Co2Microbar,
            PressureHundredths = world.PressureHundredths,
            VolcanismPercent = world.VolcanismPercent,
            StarVariabilityPerMille = world.StarVariabilityPerMille,
            StarCycleYears = world.StarCycleYears,
        });
        planet.Validate();
        return new PlayWorldSetup(
            planet,
            world.Moisture is null ? preset.Moisture : Parse<WorldMoisture>(world.Moisture, "Moisture"),
            Parse<WorldInland>(world.Inland, "Inland humidity"),
            world.Relief is null ? preset.Relief : Parse<WorldRelief>(world.Relief, "Relief"),
            Parse<WorldResources>(world.Resources, "Deposits"),
            notes);
    }

    /// <summary>The settings with the advanced world settings applied (none or default: unchanged, so old games stay the same).</summary>
    public static CivSim.Sim.State.SimSettings WithWorld(this CivSim.Sim.State.SimSettings settings, PlayWorld? world, CivSim.Sim.Content.SimContent content)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(content);
        if (world is null || world.IsDefault)
        {
            return settings;
        }

        PlayWorldSetup setup = Build(content.World, world);
        return settings with { Planet = setup.Planet, Moisture = setup.Moisture, Inland = setup.Inland, Relief = setup.Relief, Resources = setup.Resources };
    }

    private static T Parse<T>(string value, string what)
        where T : struct, Enum =>
        Enum.TryParse(value, ignoreCase: true, out T v) && Enum.IsDefined(v)
            ? v
            : throw new ArgumentException($"{what}: '{value}' is not one of {string.Join(", ", Enum.GetNames<T>()).ToLowerInvariant()}.");
}
