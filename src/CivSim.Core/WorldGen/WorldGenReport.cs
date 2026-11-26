using System.Collections.Generic;
using CivSim.Core.Map;

namespace CivSim.Core.WorldGen;

/// <summary>Summary of a generated world, for reports, validation and tuning.</summary>
public sealed class WorldGenReport
{
    /// <summary>Mean temperature of the planet at the start of the game, millionths of °C (stage 1c-B).</summary>
    public long GlobalMeanMillionths { get; internal set; }

    /// <summary>Percent of cells that are water (0–100).</summary>
    public int WaterPercent { get; internal set; }

    /// <summary>Cell count per biome id.</summary>
    public int[] BiomeCounts { get; internal set; } = [];

    /// <summary>Rivers that reached the sea or a lake.</summary>
    public int Rivers { get; internal set; }

    /// <summary>Edges carrying a river.</summary>
    public int RiverEdges { get; internal set; }

    /// <summary>Lakes created where a river got stuck.</summary>
    public int Lakes { get; internal set; }

    /// <summary>Count per deposit id (index 0 unused).</summary>
    public int[] DepositCounts { get; internal set; } = [];

    /// <summary>Start positions, one per civilization.</summary>
    public List<HexCoord> Starts { get; } = [];

    /// <summary>Start score of each start position (food, water and resources within 3 cells).</summary>
    public List<int> StartScores { get; } = [];

    /// <summary>Smallest distance between two start positions.</summary>
    public int MinStartDistance { get; internal set; }

    /// <summary>Lowest start score as a percentage of the highest (100 = perfectly fair).</summary>
    public int StartFairnessPercent { get; internal set; }

    /// <summary>Time the generation took, in milliseconds (measurement only, never used by the game).</summary>
    public double ElapsedMs { get; internal set; }
}
