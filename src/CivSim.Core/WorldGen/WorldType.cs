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

/// <summary>How much rain the planet gets (world setting).</summary>
public enum WorldMoisture
{
    /// <summary>Default.</summary>
    Normal = 0,

    /// <summary>15 points less humidity: more desert and steppe, fewer rivers.</summary>
    Dry = 1,

    /// <summary>15 points more humidity: more forest, jungle and swamp, more rivers.</summary>
    Wet = 2,
}

/// <summary>How fast humidity falls away from the sea, inland (world setting).</summary>
public enum WorldInland
{
    /// <summary>Default: −3 per cell from water, at most −36.</summary>
    Normal = 0,

    /// <summary>−4 per cell, at most −60: big deserts and steppes inside continents.</summary>
    Dry = 1,

    /// <summary>−2 per cell, at most −20: forests reach deep inland.</summary>
    Humid = 2,
}

/// <summary>Share of hills and mountains on land (world setting).</summary>
public enum WorldRelief
{
    /// <summary>Default: 8% mountains, 20% hills.</summary>
    Normal = 0,

    /// <summary>4% mountains, 12% hills.</summary>
    Flat = 1,

    /// <summary>14% mountains, 26% hills.</summary>
    Mountainous = 2,
}

/// <summary>How many natural deposits the generator places (world setting).</summary>
public enum WorldResources
{
    /// <summary>Default: as in data/resources.json.</summary>
    Normal = 0,

    /// <summary>60% of the normal amount.</summary>
    Scarce = 1,

    /// <summary>160% of the normal amount.</summary>
    Rich = 2,
}
