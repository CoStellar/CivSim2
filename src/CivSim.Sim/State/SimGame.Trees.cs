using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// Trees on the map (Patryk, 2026-11-18: too few trees, too little wood). Forest biomes are all trees; on open land groves
/// grow in clumps, a share of each biome's hexes from the data. The clumps come from the seed: a random value per hex smoothed
/// twice over its neighbours, and the highest share of each biome becomes groves. Worked out once when the game is made.
/// Woodcutters may stand on groves (at a share of a forest's wood) and gatherers pick their wood up.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>No trees on the hex.</summary>
    public const byte NoTrees = 0;

    /// <summary>A grove on open land.</summary>
    public const byte Grove = 1;

    /// <summary>Forest: all trees.</summary>
    public const byte Forest = 2;

    /// <summary>Trees of each hex: none, a grove or forest.</summary>
    public byte[] Trees { get; private set; } = [];

    private void GrowTrees()
    {
        int n = Geo.CellCount;
        Trees = new byte[n];
        var value = new int[n];
        ulong seed = Settings.Seed * 0x9E3779B97F4A7C15UL;
        for (int i = 0; i < n; i++)
        {
            ulong z = seed + ((ulong)i * 0xBF58476D1CE4E5B9UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            value[i] = (int)((z ^ (z >> 31)) % 1000);
        }

        // Two passes of the mean over the hex and its six neighbours make clumps.
        var next = new int[n];
        Span<HexCoord> nb = stackalloc HexCoord[6];
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                Geo.GetNeighbors(Geo.FromIndex(i), nb);
                int sum = value[i] * 2;
                for (int d = 0; d < 6; d++)
                {
                    sum += value[Geo.Index(nb[d])];
                }

                next[i] = sum / 8;
            }

            (value, next) = (next, value);
        }

        // The highest share of each biome's land hexes become groves; forests are all trees.
        var byBiome = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int b = Map.Biome[i];
            if (Content.World.Biomes[b].IsWater)
            {
                continue;
            }

            if (b < Content.ForestBiomes.Length && Content.ForestBiomes[b])
            {
                Trees[i] = Forest;
            }
            else if (b < Content.GroveChance.Length && Content.GroveChance[b] > 0)
            {
                if (!byBiome.TryGetValue(b, out List<int>? list))
                {
                    list = [];
                    byBiome[b] = list;
                }

                list.Add(i);
            }
        }

        foreach ((int b, List<int> cells) in byBiome)
        {
            cells.Sort((x, y) => value[y] != value[x] ? value[y].CompareTo(value[x]) : x.CompareTo(y));
            int take = cells.Count * Content.GroveChance[b] / 100;
            for (int k = 0; k < take; k++)
            {
                Trees[cells[k]] = Grove;
            }
        }
    }

    /// <summary>Wood richness of a hex for gatherers, percent: its biome's, or a grove's share of a forest's if more.</summary>
    public int WoodRichness(int cell)
    {
        int biome = Content.ForageBiome[(Map.Biome[cell] * 2) + 1];
        return Trees[cell] == Grove ? Math.Max(biome, Content.GroveWoodPercent) : biome;
    }
}
