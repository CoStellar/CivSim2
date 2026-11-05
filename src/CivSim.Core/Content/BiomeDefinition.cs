using System.Collections.Generic;

namespace CivSim.Core.Content;

/// <summary>Kind of biome, used by the generator to know where a biome may appear.</summary>
public enum BiomeKind
{
    /// <summary>Land biome chosen by temperature × humidity.</summary>
    Land,

    /// <summary>Open ocean far from land.</summary>
    DeepOcean,

    /// <summary>Sea a few cells from land.</summary>
    ShallowSea,

    /// <summary>Water directly next to land.</summary>
    Coast,

    /// <summary>Inland water.</summary>
    Lake,
}

/// <summary>
/// One biome from <c>data/biomes.json</c>. Land biomes are picked by the first rule whose temperature and
/// humidity ranges contain the cell (a simplified Whittaker diagram, GDD 3).
/// </summary>
public sealed record BiomeDefinition(
    int Id,
    string Key,
    string Name,
    BiomeKind Kind,
    int MinTemperature,
    int MaxTemperature,
    int MinHumidity,
    int MaxHumidity,
    int MovementCost,
    int BaseFertility,
    int Food,
    uint Color)
{
    /// <summary>True for water biomes.</summary>
    public bool IsWater => Kind != BiomeKind.Land;
}

/// <summary>Terrain relief on top of the biome (GDD 3).</summary>
public enum Relief : byte
{
    /// <summary>Flat terrain.</summary>
    Flat = 0,

    /// <summary>Hills.</summary>
    Hills = 1,

    /// <summary>Mountains.</summary>
    Mountains = 2,
}

/// <summary>A natural deposit from <c>data/resources.json</c> (GDD 4.1, 4.2).</summary>
public sealed record DepositDefinition(
    int Id,
    string Key,
    string Name,
    IReadOnlyList<string> Biomes,
    IReadOnlyList<Relief> Relief,
    int PerThousandCells,
    int MinRichness,
    int MaxRichness,
    int Depth,
    bool Renewable,
    uint Color)
{
    /// <summary>Goods per year gatherers take from the cell before any technology (meat from game...).</summary>
    public IReadOnlyList<GoodQuantity> Gather { get; init; } = [];
}
