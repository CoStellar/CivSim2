namespace CivSim.Protocol.Hosting;

/// <summary>
/// Settings for a new local game, as the client chooses them on screen (GDD 3, "Ustawienia świata").
/// Plain values only, so the client never needs core types. Words are the lower-case setting names
/// (for example <c>"continents"</c>, <c>"mars"</c>). Values left null come from the planet preset.
/// </summary>
public sealed record LocalGameOptions(ulong Seed, int Width, int Height, int Civilizations)
{
    /// <summary>continents, pangea or archipelago.</summary>
    public string WorldType { get; init; } = "continents";

    /// <summary>Planet preset from data/planets.json: earth, mars or uranus.</summary>
    public string Planet { get; init; } = "earth";

    /// <summary>Axial tilt in tenths of a degree (0–1800), or null for the preset's.</summary>
    public int? AxialTiltTenths { get; init; }

    /// <summary>Orbit eccentricity in thousandths (0–300), or null for the preset's.</summary>
    public int? EccentricityPerMille { get; init; }

    /// <summary>
    /// Star brightness in thousandths of the Sun's. Of brightness, distance, year and light give at most two; the rest
    /// follows from them (real formulas, stage 1c-A). Null for the preset's.
    /// </summary>
    public long? LuminosityThousandths { get; init; }

    /// <summary>Distance from the star in thousandths of an astronomical unit, or null.</summary>
    public long? DistanceThousandths { get; init; }

    /// <summary>Year length in thousandths of an Earth year, or null.</summary>
    public long? YearThousandths { get; init; }

    /// <summary>Light on the planet in thousandths of Earth's (356–1107), or null.</summary>
    public long? LightThousandths { get; init; }

    /// <summary>CO₂ in millionths of a bar, or null for the carbon cycle (or the preset's value).</summary>
    public int? Co2Microbar { get; init; }

    /// <summary>Total air pressure in hundredths of a bar, or null for the preset's other gases plus the CO₂.</summary>
    public int? PressureHundredths { get; init; }

    /// <summary>Volcanic activity in percent of Earth's, or null for the preset's.</summary>
    public int? VolcanismPercent { get; init; }

    /// <summary>Swing of the star's brightness either way, per mille (0–100), or null for the preset's.</summary>
    public int? StarVariabilityPerMille { get; init; }

    /// <summary>Period of the star's brightness cycle in years, or null for the preset's.</summary>
    public int? StarCycleYears { get; init; }

    /// <summary>Share of water, 30–80, or null for the preset's.</summary>
    public int? WaterPercent { get; init; }

    /// <summary>normal, dry or wet, or null for the preset's.</summary>
    public string? Moisture { get; init; }

    /// <summary>How fast humidity falls away from the sea: normal, dry or humid.</summary>
    public string Inland { get; init; } = "normal";

    /// <summary>normal, flat or mountainous, or null for the preset's.</summary>
    public string? Relief { get; init; }

    /// <summary>normal, scarce or rich.</summary>
    public string Resources { get; init; } = "normal";

    /// <summary>Map size presets from GDD 3.</summary>
    public static LocalGameOptions ForSize(string size, ulong seed) => size switch
    {
        "test" => new LocalGameOptions(seed, 66, 40, 4),
        "large" => new LocalGameOptions(seed, 386, 240, 20),
        "huge" => new LocalGameOptions(seed, 514, 320, 32),
        _ => new LocalGameOptions(seed, 258, 160, 12),
    };
}
