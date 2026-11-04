using CivSim.Core.WorldGen;

namespace CivSim.Core.State;

/// <summary>Settings chosen when a game is created. Fixed for the whole game.</summary>
public sealed record GameSettings(ulong Seed, int MapWidth, int MapHeight, int CivilizationCount)
{
    /// <summary>Shape of the land (GDD 3: continents, pangea, archipelago).</summary>
    public WorldType WorldType { get; init; } = WorldType.Continents;

    /// <summary>Share of the map covered by water, in percent (GDD 3: "procent wody").</summary>
    public int WaterPercent { get; init; } = 60;

    /// <summary>Settings for quick tests: small map, four civilizations.</summary>
    public static GameSettings Test(ulong seed) => new(seed, 64, 40, 4);
}
