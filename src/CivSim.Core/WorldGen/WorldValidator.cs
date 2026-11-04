using System.Linq;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;

namespace CivSim.Core.WorldGen;

/// <summary>Checks that a generated world follows the rules of GDD 3. Returns problems; an empty list means valid.</summary>
public static class WorldValidator
{
    /// <summary>Allowed difference between the requested and the generated share of water, in percentage points.</summary>
    public const int WaterTolerance = 4;

    /// <summary>Validates <paramref name="map"/> and its start positions.</summary>
    public static List<string> Validate(GameContent content, WorldMap map, WorldGenReport report, WorldGenOptions options)
    {
        var problems = new List<string>();
        HexTorus geo = map.Geometry;
        int water = 0;
        for (int i = 0; i < geo.CellCount; i++)
        {
            int b = map.Biome[i];
            if (b >= content.Biomes.Count)
            {
                problems.Add($"cell {i}: unknown biome {b}");
                continue;
            }

            BiomeDefinition biome = content.Biomes[b];
            water += biome.IsWater ? 1 : 0;
            bool waterByHeight = map.Elevation[i] < WorldGenerator.SeaLevel;
            if (biome.Kind != BiomeKind.Lake && waterByHeight != biome.IsWater)
            {
                problems.Add($"cell {i}: biome {biome.Key} does not match elevation {map.Elevation[i]}");
            }

            if (biome.IsWater && (map.Relief[i] != (byte)Relief.Flat || map.Fertility[i] != 0))
            {
                problems.Add($"cell {i}: water with relief or fertility");
            }

            int d = map.Deposit[i];
            if (d != 0)
            {
                if (d > content.Deposits.Count)
                {
                    problems.Add($"cell {i}: unknown deposit {d}");
                }
                else
                {
                    DepositDefinition def = content.Deposit(d);
                    if (!def.Biomes.Contains(biome.Key) || !def.Relief.Contains((Relief)map.Relief[i]))
                    {
                        problems.Add($"cell {i}: deposit {def.Key} not allowed on {biome.Key}");
                    }

                    int r = map.DepositRichness[i];
                    if (r < def.MinRichness || r > def.MaxRichness)
                    {
                        problems.Add($"cell {i}: deposit {def.Key} richness {r} out of range");
                    }
                }
            }
        }

        // Rivers run between two land cells (they end where a corner touches water).
        for (int e = 0; e < geo.EdgeCount; e++)
        {
            int size = map.River[e];
            if (size == 0)
            {
                continue;
            }

            if (size > 3)
            {
                problems.Add($"edge {e}: river size {size}");
            }

            var edge = new HexEdge(geo.FromIndex(e / HexEdge.PerCell), (HexDirection)(e % HexEdge.PerCell));
            (HexCoord a, HexCoord b) = geo.EdgeCells(edge);
            if (content.Biomes[map.Biome[geo.Index(a)]].IsWater || content.Biomes[map.Biome[geo.Index(b)]].IsWater)
            {
                problems.Add($"edge {e}: river touches water");
            }
        }

        int waterPercent = water * 100 / geo.CellCount;
        if (System.Math.Abs(waterPercent - options.WaterPercent) > WaterTolerance)
        {
            problems.Add($"water {waterPercent}% but {options.WaterPercent}% requested");
        }

        if (report.Starts.Count != options.CivilizationCount)
        {
            problems.Add($"{report.Starts.Count} start positions for {options.CivilizationCount} civilizations");
        }

        var seen = new HashSet<int>();
        foreach (HexCoord s in report.Starts)
        {
            int i = geo.Index(s);
            if (!seen.Add(i))
            {
                problems.Add($"start {s} used twice");
            }

            if (content.Biomes[map.Biome[i]].IsWater)
            {
                problems.Add($"start {s} is in water");
            }
        }

        return problems;
    }
}
