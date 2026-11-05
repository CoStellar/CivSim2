using System;
using System.Collections.Generic;

namespace CivSim.Core.Map;

/// <summary>
/// All per-cell data of one world (GDD 3, "Dane jednego pola") in flat arrays, plus chunk dirty flags.
/// Layers are added here as stages need them; the order of <see cref="Layers"/> is part of hashing and saves.
/// </summary>
public sealed class WorldMap
{
    /// <summary>Value of <see cref="Owner"/> for a cell nobody owns.</summary>
    public const short NoOwner = -1;

    /// <summary>Value of <see cref="WildFood"/> when game and wild plants are untouched (per mille).</summary>
    public const ushort FullWildFood = 1000;

    private readonly List<IMapLayerInfo> _layers = [];

    /// <summary>Creates an empty map (all zeros, no owners) for <paramref name="geometry"/>. Every chunk starts dirty.</summary>
    public WorldMap(HexGlobe geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Geometry = geometry;
        Chunks = new MapChunks(geometry);
        Dirty = new DirtyChunks(Chunks.Count);

        Elevation = Add(new MapLayer<byte>("elevation", this, 1, 0));
        Temperature = Add(new MapLayer<sbyte>("temperature", this, 1, 0));
        Humidity = Add(new MapLayer<byte>("humidity", this, 1, 0));
        Biome = Add(new MapLayer<byte>("biome", this, 1, 0));
        Relief = Add(new MapLayer<byte>("relief", this, 1, 0));
        Feature = Add(new MapLayer<byte>("feature", this, 1, 0));
        Fertility = Add(new MapLayer<byte>("fertility", this, 1, 0));
        Deposit = Add(new MapLayer<ushort>("deposit", this, 1, 0));
        DepositRichness = Add(new MapLayer<int>("deposit_richness", this, 1, 0));
        DepositDepth = Add(new MapLayer<byte>("deposit_depth", this, 1, 0));
        Owner = Add(new MapLayer<short>("owner", this, 1, NoOwner));
        Pollution = Add(new MapLayer<byte>("pollution", this, 1, 0));
        River = Add(new MapLayer<byte>("river", this, HexEdge.PerCell, 0));
        WildFood = Add(new MapLayer<ushort>("wild_food", this, 1, FullWildFood));
        Claim = Add(new MapLayer<int>("claim", this, 1, 0));
        Influence = Add(new MapLayer<byte>("influence", this, 1, 0));
        Seasonality = Add(new MapLayer<byte>("seasonality", this, 1, 0));
        GrowingSeason = Add(new MapLayer<byte>("growing_season", this, 1, 100));
        SeasonTemperature = Add(new MapLayer<sbyte>("season_temperature", this, 1, 0));
        SeasonGrowth = Add(new MapLayer<byte>("season_growth", this, 1, 100));
        NextSeasonTemperature = Add(new MapLayer<sbyte>("next_season_temperature", this, 1, 0));
        NextSeasonGrowth = Add(new MapLayer<byte>("next_season_growth", this, 1, 100));
        MarkAllDirty();
    }

    /// <summary>Geometry (size, neighbours, distances).</summary>
    public HexGlobe Geometry { get; }

    /// <summary>Chunk grid.</summary>
    public MapChunks Chunks { get; }

    /// <summary>Chunks changed since the last snapshot.</summary>
    public DirtyChunks Dirty { get; }

    /// <summary>Height 0–255; sea level is about 100 (GDD 3).</summary>
    public MapLayer<byte> Elevation { get; }

    /// <summary>Average yearly temperature in °C (−30…+40).</summary>
    public MapLayer<sbyte> Temperature { get; }

    /// <summary>Humidity 0–100.</summary>
    public MapLayer<byte> Humidity { get; }

    /// <summary>Biome id (index into biomes.json).</summary>
    public MapLayer<byte> Biome { get; }

    /// <summary>Relief: 0 flat, 1 hills, 2 mountains.</summary>
    public MapLayer<byte> Relief { get; }

    /// <summary>Terrain feature id (0 = none).</summary>
    public MapLayer<byte> Feature { get; }

    /// <summary>Soil fertility 0–100.</summary>
    public MapLayer<byte> Fertility { get; }

    /// <summary>Deposit resource id (0 = none).</summary>
    public MapLayer<ushort> Deposit { get; }

    /// <summary>Deposit size in whole units.</summary>
    public MapLayer<int> DepositRichness { get; }

    /// <summary>Deposit depth: 0 visible at once, higher needs technology.</summary>
    public MapLayer<byte> DepositDepth { get; }

    /// <summary>Owning civilization id, or <see cref="NoOwner"/>.</summary>
    public MapLayer<short> Owner { get; }

    /// <summary>Pollution 0–100 (from the industrial era).</summary>
    public MapLayer<byte> Pollution { get; }

    /// <summary>River size on each canonical edge (index = cell × 3 + side), 0 = no river.</summary>
    public MapLayer<byte> River { get; }

    /// <summary>
    /// What is left of the cell's game and wild plants, per mille of an untouched cell (GDD 4.4). Gathering eats it,
    /// unused cells grow back.
    /// </summary>
    public MapLayer<ushort> WildFood { get; }

    /// <summary>Settlement whose people work the cell: settlement id + 1, or 0 for none (GDD 5.4).</summary>
    public MapLayer<int> Claim { get; }

    /// <summary>Strength of the owner's influence on the cell, 0–255 (GDD 5.4); 0 where nobody owns it.</summary>
    public MapLayer<byte> Influence { get; }

    /// <summary>Difference between the warmest and the coldest time of the year, °C (GDD 3, stage 1b).</summary>
    public MapLayer<byte> Seasonality { get; }

    /// <summary>Share of the year warm enough for plants to grow, 0–100%.</summary>
    public MapLayer<byte> GrowingSeason { get; }

    /// <summary>
    /// Length of this planet's year in Earth years × 100. The dead season (no growth) lasts
    /// <c>(100 − growing season)% × year</c>: a long year means winters of many years (stage 1b).
    /// </summary>
    public int YearHundredths { get; set; } = 100;

    /// <summary>True when seasons are played turn by turn on this world (long years, <see cref="WorldGen.SeasonRules"/>).</summary>
    public bool LiveSeasons { get; set; }

    /// <summary>Temperature in the middle of the current turn, °C (live seasons only).</summary>
    public MapLayer<sbyte> SeasonTemperature { get; }

    /// <summary>Share of the current turn in which plants grow, 0–100% (live seasons only).</summary>
    public MapLayer<byte> SeasonGrowth { get; }

    /// <summary>Temperature in the middle of the next turn, °C (live seasons only; what the AI expects).</summary>
    public MapLayer<sbyte> NextSeasonTemperature { get; }

    /// <summary>Share of the next turn in which plants grow, 0–100% (live seasons only).</summary>
    public MapLayer<byte> NextSeasonGrowth { get; }

    /// <summary>All layers in their fixed order (for hashing and saving).</summary>
    public IReadOnlyList<IMapLayerInfo> Layers => _layers;

    /// <summary>Marks the chunk of a cell dirty.</summary>
    public void MarkCellDirty(int cellIndex) => Dirty.Mark(Chunks.ChunkOfIndex(cellIndex));

    /// <summary>Marks every chunk dirty.</summary>
    public void MarkAllDirty() => Dirty.MarkAll();

    private MapLayer<T> Add<T>(MapLayer<T> layer)
        where T : unmanaged, IEquatable<T>
    {
        _layers.Add(new LayerInfo<T>(layer));
        return layer;
    }

    private sealed class LayerInfo<T>(MapLayer<T> layer) : IMapLayerInfo
        where T : unmanaged, IEquatable<T>
    {
        public string Name => layer.Name;

        public ReadOnlySpan<byte> AsBytes() => layer.AsBytes();

        public void LoadBytes(ReadOnlySpan<byte> bytes) => layer.LoadBytes(bytes);
    }
}

/// <summary>Type-independent access to a map layer for hashing and saves.</summary>
public interface IMapLayerInfo
{
    /// <summary>Layer name.</summary>
    string Name { get; }

    /// <summary>Raw bytes of the layer.</summary>
    ReadOnlySpan<byte> AsBytes();

    /// <summary>Replaces the layer from raw bytes (loading only).</summary>
    void LoadBytes(ReadOnlySpan<byte> bytes);
}
