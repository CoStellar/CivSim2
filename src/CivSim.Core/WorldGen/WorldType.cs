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

/// <summary>Average temperature of the planet (world setting).</summary>
public enum WorldClimate
{
    /// <summary>Default: Earth-like.</summary>
    Temperate = 0,

    /// <summary>6 °C colder: bigger ice caps, more tundra and taiga.</summary>
    Cold = 1,

    /// <summary>6 °C warmer: small ice caps, more jungle, savanna and desert.</summary>
    Hot = 2,
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
