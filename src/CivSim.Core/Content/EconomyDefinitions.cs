using System.Collections.Generic;
using CivSim.Core.Numerics;

namespace CivSim.Core.Content;

/// <summary>Storage class of a good (GDD 4.3). CORE uses one shared capacity; the class is kept for later.</summary>
public enum GoodCategory
{
    /// <summary>Food (granary).</summary>
    Food = 0,

    /// <summary>Bulk materials (store yard).</summary>
    Bulk = 1,

    /// <summary>Valuable goods (treasury).</summary>
    Valuable = 2,

    /// <summary>Liquids and gases (tanks, from the industrial era).</summary>
    Liquid = 3,
}

/// <summary>A good from <c>data/goods.json</c> (GDD 4.1-4.3).</summary>
/// <param name="Id">Dense id from 0, the index into every stock array.</param>
/// <param name="Key">Stable key, e.g. "grain".</param>
/// <param name="Name">Display name.</param>
/// <param name="Era">Era number (1 = Stone Age) in which the good first appears.</param>
/// <param name="Category">Storage class.</param>
/// <param name="SpoilageBasisPointsPerYear">Share lost per year in hundredths of a percent (100 = 1%).</param>
/// <param name="Color">Preview colour (0xRRGGBB).</param>
public sealed record GoodDefinition(int Id, string Key, string Name, int Era, GoodCategory Category, int SpoilageBasisPointsPerYear, uint Color)
{
    /// <summary>True when the good is food.</summary>
    public bool IsFood => Category == GoodCategory.Food;

    /// <summary>Base worth in barter units (wild plants = 1); internal prices move around it (stage 3e).</summary>
    public Amount Value { get; init; } = Amount.FromUnits(1);
}

/// <summary>Trade network rules (data/trade.json, GDD 4, CORE simplified: no physical transport; stage 3e).</summary>
public sealed record TradeRules
{
    /// <summary>Two settlements of a civilization are linked when at most this many cells apart.</summary>
    public int LinkCells { get; init; } = 8;

    /// <summary>Percent of the goods lost per cell of distance when passed on (at most 90% in all).</summary>
    public int LossPercentPerCell { get; init; } = 2;

    /// <summary>Years of food needs a settlement keeps before it gives food away.</summary>
    public Amount FoodReserveYears { get; init; } = Amount.FromUnits(1);

    /// <summary>Internal prices stay within these percentages of the good's value.</summary>
    public (int Min, int Max) PriceRangePercent { get; init; } = (25, 400);
}

/// <summary>An amount of one good.</summary>
public readonly record struct GoodQuantity(int GoodId, Amount Amount);

/// <summary>A one-step production recipe from <c>data/recipes.json</c> (GDD 4.4).</summary>
public sealed record RecipeDefinition(
    int Id,
    string Key,
    string Name,
    IReadOnlyList<GoodQuantity> Inputs,
    IReadOnlyList<GoodQuantity> Outputs,
    int Workers,
    int Years);

/// <summary>A settlement level from <c>data/settlements.json</c> (GDD 5.3).</summary>
/// <summary>
/// What a settlement looks like on the map (stage 6 PR P2b, ADR 0026 rule 12): from <paramref name="MinLevel"/> on and, when
/// <see cref="Technology"/> is set, once the owner knows it. Looks are ordered from the simplest; the last that fits is drawn.
/// </summary>
public sealed record SettlementLookDefinition(int Id, string Key, string Name, int MinLevel)
{
    /// <summary>Key of the technology the owner needs, or empty.</summary>
    public string TechnologyKey { get; init; } = string.Empty;

    /// <summary>Technology id resolved from <see cref="TechnologyKey"/>, or -1.</summary>
    public int Technology { get; init; } = -1;
}

/// <summary>A level of settlement (GDD 5.3).</summary>
public sealed record SettlementLevelDefinition(
    int Id,
    string Key,
    string Name,
    int MinPopulation,
    int MaxPopulation,
    Amount StorageCapacity,
    bool Mobile)
{
    /// <summary>Key of the technology a camp needs to settle down as this level (GDD 5.3), or empty.</summary>
    public string RequiresKey { get; init; } = string.Empty;

    /// <summary>Technology id resolved from <see cref="RequiresKey"/>, or -1.</summary>
    public int RequiresTechnology { get; init; } = -1;

    /// <summary>Years an empty settlement of this level stays abandoned (buildings standing) before it becomes ruins (GDD 5.5).</summary>
    public int AbandonedYears { get; init; } = 50;

    /// <summary>Years after abandonment when the ruins are gone from the map (archaeology is a later module).</summary>
    public int RuinsUntilYears { get; init; } = 300;
}

/// <summary>An era from <c>data/eras.json</c> (GDD 6.2).</summary>
public sealed record EraDefinition(int Id, string Key, string Name, int MonthsPerTurn)
{
    /// <summary>Technology that opens this era (stage 3f), or -1 when none is defined yet.</summary>
    public int ThresholdTechnology { get; init; } = -1;

    /// <summary>Most warriors one leader leads in a campaign (GDD 9.3; stage 4e PR 3).</summary>
    public int ArmyLimit { get; init; } = 50;

    /// <summary>Most warriors on one cell; a bigger army stands on several cells next to each other (a front).</summary>
    public int TroopsPerCell { get; init; } = 50;
}

/// <summary>What an effect is attached to (GDD 10.1).</summary>
public enum EffectTarget
{
    /// <summary>One map cell.</summary>
    Cell = 0,

    /// <summary>One settlement.</summary>
    Settlement = 1,

    /// <summary>A whole civilization.</summary>
    Civilization = 2,
}

/// <summary>The value an effect changes. Systems ask for the total of a stat on a target.</summary>
public enum EffectStat
{
    /// <summary>Settlement and building output, in percent (negative = damage).</summary>
    OutputPercent = 0,

    /// <summary>Non-zero = the cell cannot be entered.</summary>
    MovementBlocked = 1,

    /// <summary>Extra movement cost to enter the cell.</summary>
    MovementCost = 2,

    /// <summary>Change of extraction from the cell, in percent (−50 halves it).</summary>
    ExtractionPercent = 3,

    /// <summary>People lost per year, in per mille of the population.</summary>
    DepopulationPerMillePerYear = 4,

    /// <summary>Change of mood (satisfaction).</summary>
    Mood = 5,

    /// <summary>Change of how much people can carry when they leave or move, in percent (pack animals, carts, boats).</summary>
    CarryCapacityPercent = 6,

    /// <summary>Degrees of cold a civilization bears without losing comfort (fire, clothing, houses from technologies).</summary>
    ColdToleranceDegrees = 7,

    /// <summary>Percent change of how fast food spoils (granaries and pots lower it; stage 3f).</summary>
    SpoilagePercent = 8,

    /// <summary>Change of the share of children who die before 15, in tenths of a percent (per mille of children; ADR 0020).</summary>
    ChildMortalityPerMille = 9,

    /// <summary>Change of adult deaths per year, in people per ten thousand (−6 = −0.6‰; ADR 0020).</summary>
    AdultDeathsPerTenThousand = 10,

    /// <summary>Change of births per year, in children per ten thousand people (ADR 0020).</summary>
    BirthsPerTenThousand = 11,
}

/// <summary>How effects of the same type on the same target combine.</summary>
public enum EffectStacking
{
    /// <summary>Strengths add up.</summary>
    Sum = 0,

    /// <summary>A new effect removes the older one.</summary>
    Replace = 1,

    /// <summary>Only the effect with the largest absolute strength counts.</summary>
    Strongest = 2,
}

/// <summary>An effect type from <c>data/effects.json</c> (GDD 10.1).</summary>
public sealed record EffectDefinition(int Id, string Key, string Name, EffectTarget Target, EffectStat Stat, EffectStacking Stacking);

/// <summary>
/// Rules for settlements as property (GDD 5.5) from <c>data/settlements.json</c>: claims on abandoned settlements and
/// how much people carry when they leave. Carrying is a general rule, not only for abandonment: every move, split and
/// raid uses it, and technologies or animals raise it through the <see cref="EffectStat.CarryCapacityPercent"/> effect.
/// </summary>
public sealed record AssetRules
{
    /// <summary>People a camp needs to settle down as a village (stage 3f).</summary>
    public int SettleMinPeople { get; init; } = 60;

    /// <summary>Years the former owner keeps a claim on a settlement it abandoned.</summary>
    public int ClaimYears { get; init; } = 100;

    /// <summary>Units of goods one person carries when moving.</summary>
    public Amount CarryPerPerson { get; init; } = Amount.FromUnits(10);
}

/// <summary>A planet preset from <c>data/planets.json</c> (stage 1b): Earth, Mars, Uranus… Every value can be changed after choosing it.</summary>
public sealed record PlanetPreset(string Key, string Name, WorldGen.PlanetParameters Planet, int WaterPercent, WorldGen.WorldMoisture Moisture, WorldGen.WorldRelief Relief)
{
    /// <summary>True when the preset's CO₂ comes from the carbon cycle, so it follows changes of star, orbit and air.</summary>
    public bool AutoCo2 { get; init; } = true;
}
