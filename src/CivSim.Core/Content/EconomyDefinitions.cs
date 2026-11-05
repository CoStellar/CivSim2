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
public sealed record SettlementLevelDefinition(
    int Id,
    string Key,
    string Name,
    int MinPopulation,
    int MaxPopulation,
    Amount StorageCapacity,
    bool Mobile);

/// <summary>An era from <c>data/eras.json</c> (GDD 6.2).</summary>
public sealed record EraDefinition(int Id, string Key, string Name, int MonthsPerTurn);

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
