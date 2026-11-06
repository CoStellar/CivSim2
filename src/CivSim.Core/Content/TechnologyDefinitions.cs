using System.Collections.Generic;
using CivSim.Core.Numerics;

namespace CivSim.Core.Content;

/// <summary>Field of a technology (GDD 6.1).</summary>
public enum TechField
{
    /// <summary>Economy.</summary>
    Economy = 0,

    /// <summary>Science.</summary>
    Science = 1,

    /// <summary>Military.</summary>
    Military = 2,

    /// <summary>Society.</summary>
    Society = 3,
}

/// <summary>Kind of situation that makes a technology cheaper (GDD 6.1, inspirations).</summary>
public enum InspirationKind
{
    /// <summary>The civilization's warehouses hold at least <see cref="Inspiration.Amount"/> of a good.</summary>
    GoodInNetwork = 0,

    /// <summary>A deposit lies in the civilization's influence zone.</summary>
    DepositInZone = 1,

    /// <summary>A biome lies in the influence zone.</summary>
    BiomeInZone = 2,

    /// <summary>A river runs through the influence zone.</summary>
    RiverInZone = 3,

    /// <summary>One of its camps has a winter at least this cold (°C).</summary>
    WinterBelow = 4,

    /// <summary>The civilization has at least this many people.</summary>
    PeopleAtLeast = 5,
}

/// <summary>A situation that cuts a technology's cost by <see cref="Percent"/>.</summary>
/// <param name="Kind">What is checked.</param>
/// <param name="Id">Good, deposit or biome id for the kinds that name one; otherwise -1.</param>
/// <param name="Amount">Amount of the good (GoodInNetwork).</param>
/// <param name="Value">Degrees (WinterBelow) or people (PeopleAtLeast).</param>
/// <param name="Percent">Cost cut in percent.</param>
public sealed record Inspiration(InspirationKind Kind, int Id, Amount Amount, int Value, int Percent);

/// <summary>Goods a gathered cell gives per year on top of its base yield once a technology is known.</summary>
/// <param name="DepositId">Deposit the bonus is for, or 0.</param>
/// <param name="BiomeId">Biome the bonus is for, or -1.</param>
/// <param name="Goods">Goods per year from an untouched cell.</param>
public sealed record GatherBonus(int DepositId, int BiomeId, IReadOnlyList<GoodQuantity> Goods);

/// <summary>A permanent effect on the civilization once a technology is known.</summary>
public sealed record TechEffect(int EffectId, int Strength);

/// <summary>A technology (GDD 6.1; stage 3f).</summary>
public sealed record TechnologyDefinition(int Id, string Key, string Name, int Era, TechField Field, Amount Cost)
{
    /// <summary>Technologies that must all be known first.</summary>
    public IReadOnlyList<int> Requires { get; init; } = [];

    /// <summary>When not empty, at least one of these must be known too.</summary>
    public IReadOnlyList<int> RequiresAny { get; init; } = [];

    /// <summary>Situations that make the technology cheaper.</summary>
    public IReadOnlyList<Inspiration> Inspirations { get; init; } = [];

    /// <summary>Recipes the technology unlocks.</summary>
    public IReadOnlyList<int> Recipes { get; init; } = [];

    /// <summary>Extra gathering the technology brings.</summary>
    public IReadOnlyList<GatherBonus> Gather { get; init; } = [];

    /// <summary>Permanent effects on the civilization.</summary>
    public IReadOnlyList<TechEffect> Effects { get; init; } = [];
}

/// <summary>Research rules (data/technologies.json, GDD 6; stage 3f).</summary>
public sealed record ResearchRules
{
    /// <summary>Cost cut per neighbour that knows the technology, percent.</summary>
    public int DiffusionPercent { get; init; } = 20;

    /// <summary>Neighbours counted for diffusion at most.</summary>
    public int MaxNeighbours { get; init; } = 2;

    /// <summary>Another civilization is a neighbour when one of its settlements is this close to one of ours.</summary>
    public int ContactCells { get; init; } = 12;

    /// <summary>A technology never costs less than this share of its base cost, percent.</summary>
    public int MinCostPercent { get; init; } = 25;

    /// <summary>Share of the current era's technologies needed (with the threshold technology) to enter the next era.</summary>
    public int EraSharePercent { get; init; } = 60;
}

/// <summary>What civilizations see and remember (data/perception.json, GDD 8.2; stage 3g).</summary>
public sealed record PerceptionRules
{
    /// <summary>Cells a lived-in settlement sees around it every turn.</summary>
    public int SightCells { get; init; } = 6;

    /// <summary>Years a seen cell stays known without being seen again (oral memory).</summary>
    public int OralMemoryYears { get; init; } = 300;

    /// <summary>Cells a camp's scout walks every turn.</summary>
    public int ScoutCells { get; init; } = 15;

    /// <summary>Farthest a camp may set out for in one journey.</summary>
    public int JourneyCells { get; init; } = 40;
}

/// <summary>How the AI advisors decide (data/ai.json, GDD 8.3; stage 4a).</summary>
public sealed record AiRules
{
    /// <summary>
    /// How many of the best actions an advisor draws among, chances by score (1-3). 1 always takes the best one.
    /// </summary>
    public int PickAmongTop { get; init; } = 1;

    /// <summary>Own steps of a civilization between two of its strategy reviews (GDD 8.1: 10-20; ADR 0018); reviews are spread over the civilizations.</summary>
    public int ReviewEverySteps { get; init; } = 15;

    /// <summary>Boost for places that lie closer to the strategist's expansion target (GDD 7.6: a suggestion).</summary>
    public int DirectionBonusPercent { get; init; } = 15;

    /// <summary>A new recipe must beat the one the artisans make by this much before they switch.</summary>
    public int RecipeSwitchBetterByPercent { get; init; } = 25;

    /// <summary>Camps this full (people against the most their level holds, in all camps) give the lowest expansion weight.</summary>
    public int CrowdingLowPercent { get; init; } = 40;

    /// <summary>Camps this full give the highest expansion weight.</summary>
    public int CrowdingHighPercent { get; init; } = 90;

    /// <summary>Lowest and highest expansion weight from crowding, per mille.</summary>
    public int ExpansionWeightMin { get; init; } = 750;

    /// <summary>Highest expansion weight from crowding, per mille.</summary>
    public int ExpansionWeightMax { get; init; } = 1500;

    /// <summary>People fed this well (per cent, all camps) need no extra care.</summary>
    public int FedWellPercent { get; init; } = 95;

    /// <summary>People fed this badly push prosperity and security to their highest weight.</summary>
    public int FedBadlyPercent { get; init; } = 60;

    /// <summary>Highest prosperity and security weight from hunger, per mille.</summary>
    public int HungerWeightMax { get; init; } = 1500;

    /// <summary>A hungry or cold camp that is not starving looks for a better place every this many own steps of its civilization.</summary>
    public int SearchEverySteps { get; init; } = 3;

    /// <summary>A camp fed worse than this (per cent) is starving and looks for a better place every turn.</summary>
    public int StarvingFedPercent { get; init; } = 85;

    /// <summary>A camp never splits below this share of its level's people, however expansive its civilization.</summary>
    public int SplitMinPercentOfLevel { get; init; } = 50;
}
