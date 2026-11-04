using CivSim.Core.WorldGen;

namespace CivSim.Core.State;

/// <summary>Settings chosen when a game is created. Fixed for the whole game.</summary>
public sealed record GameSettings(ulong Seed, int MapWidth, int MapHeight, int CivilizationCount)
{
    /// <summary>Shape of the land (GDD 3: continents, pangea, archipelago).</summary>
    public WorldType WorldType { get; init; } = WorldType.Continents;

    /// <summary>Share of the map covered by water, in percent (GDD 3: "procent wody").</summary>
    public int WaterPercent { get; init; } = 60;

    /// <summary>Average temperature of the planet.</summary>
    public WorldClimate Climate { get; init; } = WorldClimate.Temperate;

    /// <summary>How much rain the planet gets.</summary>
    public WorldMoisture Moisture { get; init; } = WorldMoisture.Normal;

    /// <summary>Share of hills and mountains.</summary>
    public WorldRelief Relief { get; init; } = WorldRelief.Normal;

    /// <summary>How many natural deposits there are.</summary>
    public WorldResources Resources { get; init; } = WorldResources.Normal;

    /// <summary>Settings for quick tests: small map, four civilizations.</summary>
    public static GameSettings Test(ulong seed) => new(seed, 64, 40, 4);
}
