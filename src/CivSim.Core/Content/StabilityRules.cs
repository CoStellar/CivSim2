namespace CivSim.Core.Content;

/// <summary>
/// Stability and administration (<c>data/stability.json</c>; GDD 7.3, stage 4f PR 3). Stability (0–100) is the mood of
/// the settlements weighed by the political influence of their strata, plus the government's bonus, minus an overloaded
/// administration, a succession crisis, anarchy after a change of government and a lost capital.
/// </summary>
public sealed record StabilityRules
{
    /// <summary>Below this stability unrest begins: strikes lower the output of every settlement.</summary>
    public int UnrestBelow { get; init; } = 30;

    /// <summary>Output percent lost for every point of stability below <see cref="UnrestBelow"/>.</summary>
    public int UnrestOutputPercentPerPoint { get; init; } = 1;

    /// <summary>Key of the effect (<c>data/effects.json</c>) that carries the unrest; it must change output percent of a civilization.</summary>
    public string UnrestEffect { get; init; } = string.Empty;

    /// <summary>Stability lost while a succession crisis lasts.</summary>
    public int CrisisPenalty { get; init; } = 15;

    /// <summary>Stability lost during anarchy after a change of government.</summary>
    public int AnarchyPenalty { get; init; } = 20;

    /// <summary>Years of anarchy after a change of government: lower stability and no state decisions (GDD 7.2).</summary>
    public int AnarchyYears { get; init; } = 2;

    /// <summary>Stability lost after the capital fell to another civilization.</summary>
    public int CapitalLostPenalty { get; init; } = 15;

    /// <summary>Years <see cref="CapitalLostPenalty"/> lasts.</summary>
    public int CapitalLostYears { get; init; } = 5;

    /// <summary>Administration points every settlement uses.</summary>
    public int AdminPerSettlement { get; init; } = 10;

    /// <summary>Administration points used per point of road cost from the capital (movement cost, not a straight line).</summary>
    public int AdminPerRoadCost { get; init; } = 1;

    /// <summary>The farthest road cost the search goes; a settlement farther away, or across water, counts as this far.</summary>
    public int RoadSearchCost { get; init; } = 60;

    /// <summary>Stability lost when the administration is twice its capacity (100% over); proportional below, capped by <see cref="OverloadMaxPenalty"/>.</summary>
    public int OverloadPenaltyPer100Percent { get; init; } = 40;

    /// <summary>The largest stability loss from an overloaded administration.</summary>
    public int OverloadMaxPenalty { get; init; } = 40;

    /// <summary>Loyalty of a settlement lost per 10 points of road cost from the capital (loyalty = stability minus this).</summary>
    public int LoyaltyPer10RoadCost { get; init; } = 5;
}
