namespace CivSim.Core.WorldGen;

/// <summary>Shape of the land masses (GDD 3, generation step 1).</summary>
public enum WorldType
{
    /// <summary>A few large continents (default).</summary>
    Continents = 0,

    /// <summary>One or two huge land masses.</summary>
    Pangea = 1,

    /// <summary>Many islands.</summary>
    Archipelago = 2,
}

/// <summary>Terrain features on top of a biome. Values are stored on the map.</summary>
public enum TerrainFeature : byte
{
    /// <summary>No feature.</summary>
    None = 0,

    /// <summary>Oasis in a desert: fresh water and fertile soil.</summary>
    Oasis = 1,

    /// <summary>Coral reef in warm shallow water.</summary>
    Reef = 2,

    /// <summary>Sea ice in polar water.</summary>
    IceField = 3,
}
