using CivSim.Core.WorldGen;

namespace CivSim.Core.State;

/// <summary>Settings chosen when a game is created. Fixed for the whole game.</summary>
public sealed record GameSettings(ulong Seed, int MapWidth, int MapHeight, int CivilizationCount)
{
    /// <summary>Shape of the land (GDD 3: continents, pangea, archipelago).</summary>
    public WorldType WorldType { get; init; } = WorldType.Continents;

    /// <summary>Share of the map covered by water, in percent (GDD 3: "procent wody").</summary>
    public int WaterPercent { get; init; } = 60;

    /// <summary>The planet: axial tilt, year length, star brightness and orbit (GDD 3, stage 1b).</summary>
    public PlanetParameters Planet { get; init; } = PlanetParameters.Earth;

    /// <summary>Name of the first world (from the planet preset).</summary>
    public string PlanetName { get; init; } = "Earth";

    /// <summary>How much rain the planet gets.</summary>
    public WorldMoisture Moisture { get; init; } = WorldMoisture.Normal;

    /// <summary>How fast humidity falls away from the sea (world setting).</summary>
    public WorldInland Inland { get; init; } = WorldInland.Normal;

    /// <summary>Share of hills and mountains.</summary>
    public WorldRelief Relief { get; init; } = WorldRelief.Normal;

    /// <summary>How many natural deposits there are.</summary>
    public WorldResources Resources { get; init; } = WorldResources.Normal;

    /// <summary>
    /// These settings with a planet preset from <c>data/planets.json</c>: its planet values and its land, water, rain and
    /// relief. Call it before changing single values, which then win over the preset.
    /// </summary>
    public GameSettings WithPreset(Content.PlanetPreset preset)
    {
        System.ArgumentNullException.ThrowIfNull(preset);
        return this with
        {
            Planet = preset.Planet,
            PlanetName = preset.Name,
            WaterPercent = preset.WaterPercent,
            Moisture = preset.Moisture,
            Relief = preset.Relief,
        };
    }

    /// <summary>Settings for quick tests: small map, four civilizations.</summary>
    public static GameSettings Test(ulong seed) => new(seed, 66, 40, 4);
}
