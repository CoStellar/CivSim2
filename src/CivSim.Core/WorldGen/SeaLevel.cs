using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Population;
using CivSim.Core.State;

namespace CivSim.Core.WorldGen;

/// <summary>
/// The sea level follows the ice (stage 1c-C, decision of 2026-11-05). Water locked in the ice sheets lowers the sea by
/// up to <see cref="ClimateCycleRules.GlacialSeaDropMeters"/>: shallow shelves fall dry and become land bridges, and
/// when the ice melts the sea takes them back. People on a flooded cell move with what they can carry to the nearest
/// dry cell; what they leave behind, and any empty settlement there, become ruins under the sea (Patryk's decision).
/// Only land that was sea at the start can flood again: the sea never rises above its level at the start of the game.
/// </summary>
public static class SeaLevel
{
    /// <summary>Settlements look this far for dry land when the sea takes their cell.</summary>
    public const int RefugeRadius = 30;

    /// <summary>Sea level (elevation, 100 at the start) for the mean ice-sheet share of both hemispheres (millionths).</summary>
    public static int Target(ClimateCycleRules rules, long meanSheetShare)
    {
        ArgumentNullException.ThrowIfNull(rules);
        long dropMicro = rules.GlacialSeaDropMeters * Math.Clamp(meanSheetShare, 0, 1_000_000);
        long steps = (dropMicro + (rules.SeaMetersPerStep * 500_000L)) / (rules.SeaMetersPerStep * 1_000_000L);
        return WorldGenerator.SeaLevel - (int)steps;
    }

    /// <summary>
    /// Moves the coast of <paramref name="world"/> to <paramref name="level"/>: cells of the sea at or above it fall dry,
    /// dry sea floor below it floods. Fixes the kinds of water, rivers on flooded edges and settlements on flooded cells.
    /// Returns plain-English chronicle lines.
    /// </summary>
    public static List<string> Apply(GameContent content, GameState state, WorldState world, int level)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        var lines = new List<string>();
        WorldMap map = world.Map;
        HexGlobe geo = world.Geometry;
        int landId = content.Biome("grassland").Id;
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        int exposed = 0;
        int flooded = 0;
        for (int i = 0; i < geo.CellCount; i++)
        {
            BiomeDefinition b = content.Biomes[map.Biome[i]];
            int e = map.Elevation[i];
            if (b.IsWater && b.Kind != BiomeKind.Lake && e >= level)
            {
                // New land: the climate update right after gives it its real biome.
                geo.GetNeighbors(geo.FromIndex(i), n6);
                int humidity = 0;
                int count = 0;
                foreach (HexCoord c in n6)
                {
                    int j = geo.Index(c);
                    if (!content.Biomes[map.Biome[j]].IsWater)
                    {
                        humidity += map.Humidity[j];
                        count++;
                    }
                }

                map.Biome.Set(i, (byte)landId);
                map.Humidity.Set(i, (byte)(count == 0 ? 70 : humidity / count));
                map.Relief.Set(i, (byte)Relief.Flat);
                map.Fertility.Set(i, (byte)content.Biomes[landId].BaseFertility);
                map.Continentality.Set(i, 35);
                map.WildFood.Set(i, WorldMap.FullWildFood);
                exposed++;
            }
            else if (!b.IsWater && e < level && e < WorldGenerator.SeaLevel)
            {
                map.Biome.Set(i, (byte)content.WaterBiome(BiomeKind.Coast).Id);
                map.Humidity.Set(i, 100);
                map.Feature.Set(i, 0);
                map.Fertility.Set(i, 0);
                map.Continentality.Set(i, 25);
                map.Owner.Set(i, WorldMap.NoOwner);
                map.Influence.Set(i, 0);
                for (int d = 0; d < 6; d++)
                {
                    int edge = geo.EdgeIndex(geo.Edge(geo.FromIndex(i), (HexDirection)d));
                    if (map.River[edge] != 0)
                    {
                        map.River.Set(edge, 0);
                    }
                }

                flooded++;
            }
        }

        world.Climate.SeaLevel = level;
        if (exposed == 0 && flooded == 0)
        {
            return lines;
        }

        ClassifyWater(content, map);
        MoveSettlements(content, state, world, lines);
        string meters = (Math.Abs(level - WorldGenerator.SeaLevel) * content.ClimateCycles.SeaMetersPerStep).ToString(System.Globalization.CultureInfo.InvariantCulture);
        lines.Insert(0, exposed > 0
            ? $"The sea fell to {meters} m below its old level: {exposed} cells of sea floor became land."
            : $"The sea rose to {meters} m below its old level and took back {flooded} cells of land.");
        return lines;
    }

    // Coast next to land, shallow sea up to 3 cells out, deep ocean beyond (the generator's rule); lakes stay as they are.
    private static void ClassifyWater(GameContent content, WorldMap map)
    {
        HexGlobe geo = map.Geometry;
        var dist = new int[geo.CellCount];
        Array.Fill(dist, int.MaxValue);
        var queue = new Queue<int>();
        for (int i = 0; i < dist.Length; i++)
        {
            if (!content.Biomes[map.Biome[i]].IsWater)
            {
                dist[i] = 0;
                queue.Enqueue(i);
            }
        }

        Span<HexCoord> n6 = stackalloc HexCoord[6];
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            geo.GetNeighbors(geo.FromIndex(cur), n6);
            foreach (HexCoord n in n6)
            {
                int ni = geo.Index(n);
                if (dist[ni] == int.MaxValue)
                {
                    dist[ni] = dist[cur] + 1;
                    queue.Enqueue(ni);
                }
            }
        }

        byte coast = (byte)content.WaterBiome(BiomeKind.Coast).Id;
        byte shallow = (byte)content.WaterBiome(BiomeKind.ShallowSea).Id;
        byte deep = (byte)content.WaterBiome(BiomeKind.DeepOcean).Id;
        for (int i = 0; i < dist.Length; i++)
        {
            BiomeDefinition b = content.Biomes[map.Biome[i]];
            if (!b.IsWater || b.Kind == BiomeKind.Lake)
            {
                continue;
            }

            byte kind = dist[i] <= 1 ? coast : dist[i] <= 3 ? shallow : deep;
            if (map.Biome[i] != kind)
            {
                map.Biome.Set(i, kind);
            }
        }
    }

    private static void MoveSettlements(GameContent content, GameState state, WorldState world, List<string> lines)
    {
        WorldMap map = world.Map;
        HexGlobe geo = world.Geometry;
        foreach (SettlementState s in new List<SettlementState>(state.Settlements))
        {
            if (s.World != world.Id || !content.Biomes[map.Biome[geo.Index(s.Cell)]].IsWater)
            {
                continue;
            }

            if (!s.IsActive)
            {
                s.Status = SettlementStatus.Ruins;
                continue;
            }

            HexCoord? refuge = NearestDryCell(content, state, world, s.Cell);
            if (refuge is not HexCoord target)
            {
                continue; // nowhere to go within reach: the people stay on the water's edge until next time
            }

            SettlementState? left = SettlementAssets.Relocate(content, state, s, target);
            if (left is not null)
            {
                left.Status = SettlementStatus.Ruins;
            }

            lines.Add($"The sea took the land under {s.Name}; its people moved to dry land.");
        }
    }

    // Closest land cell without a lived-in settlement, by rings around the flooded cell (fixed ring order).
    private static HexCoord? NearestDryCell(GameContent content, GameState state, WorldState world, HexCoord from)
    {
        HexGlobe geo = world.Geometry;
        Span<HexCoord> ring = stackalloc HexCoord[6 * RefugeRadius];
        for (int r = 1; r <= RefugeRadius; r++)
        {
            int n = geo.Ring(from, r, ring);
            for (int k = 0; k < n; k++)
            {
                HexCoord c = ring[k];
                if (content.Biomes[world.Map.Biome[geo.Index(c)]].IsWater)
                {
                    continue;
                }

                bool taken = false;
                foreach (SettlementState other in state.Settlements)
                {
                    taken |= other.IsActive && other.World == world.Id && other.Cell == c;
                }

                if (!taken)
                {
                    return c;
                }
            }
        }

        return null;
    }
}
