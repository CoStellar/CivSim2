using System;
using System.Collections.Generic;

namespace CivSim.Core.Content;

/// <summary>
/// All game content loaded from <c>data/</c>. Created by <c>CivSim.Data</c>; the core only reads it.
/// Lists are ordered by id and ids are dense from 0 (biomes) or 1 (deposits; 0 means "none" on the map).
/// </summary>
public sealed class GameContent
{
    private readonly Dictionary<string, BiomeDefinition> _biomesByKey = new(StringComparer.Ordinal);

    /// <summary>Creates the content. Call only with validated data.</summary>
    public GameContent(IReadOnlyList<BiomeDefinition> biomes, IReadOnlyList<DepositDefinition> deposits)
    {
        Biomes = biomes;
        Deposits = deposits;
        foreach (BiomeDefinition b in biomes)
        {
            _biomesByKey.Add(b.Key, b);
        }
    }

    /// <summary>Biomes, index = id.</summary>
    public IReadOnlyList<BiomeDefinition> Biomes { get; }

    /// <summary>Deposits; <c>Deposits[i].Id == i + 1</c>.</summary>
    public IReadOnlyList<DepositDefinition> Deposits { get; }

    /// <summary>Finds a biome by key, e.g. "grassland".</summary>
    public BiomeDefinition Biome(string key) =>
        _biomesByKey.TryGetValue(key, out BiomeDefinition? b) ? b : throw new KeyNotFoundException($"Unknown biome '{key}'.");

    /// <summary>The single biome of a water kind.</summary>
    public BiomeDefinition WaterBiome(BiomeKind kind)
    {
        foreach (BiomeDefinition b in Biomes)
        {
            if (b.Kind == kind)
            {
                return b;
            }
        }

        throw new KeyNotFoundException($"No biome of kind {kind}.");
    }

    /// <summary>The deposit with this map id (1-based).</summary>
    public DepositDefinition Deposit(int id) => Deposits[id - 1];
}
