using System.Collections.Generic;
using CivSim.Core.Numerics;

namespace CivSim.Core.Content;

/// <summary>
/// One need of a stratum (GDD 5.1): either any good of a <see cref="Category"/> (when <see cref="GoodId"/> is -1)
/// or one good, <see cref="PerYear"/> per person.
/// </summary>
public sealed record NeedDefinition(int GoodId, GoodCategory Category, Amount PerYear, bool Basic)
{
    /// <summary>True when any good of <see cref="Category"/> satisfies the need.</summary>
    public bool AnyOfCategory => GoodId < 0;
}

/// <summary>A population stratum from <c>data/strata.json</c> (GDD 5.1).</summary>
/// <param name="Id">Dense id from 0, the index into every population array.</param>
/// <param name="Key">Stable key, e.g. "producers".</param>
/// <param name="Name">Display name.</param>
/// <param name="Gathers">True when the stratum works the land.</param>
/// <param name="TargetSharePercent">Share of the population the stratum drifts towards (all add up to 100).</param>
/// <param name="Needs">Needs per person per year, basic first as written in the file.</param>
public sealed record StratumDefinition(int Id, string Key, string Name, bool Gathers, int TargetSharePercent, IReadOnlyList<NeedDefinition> Needs);

/// <summary>Growth, hunger, health and gathering rules from <c>data/population.json</c> (GDD 5.2, 4.4).</summary>
public sealed record PopulationRules
{
    /// <summary>Births per thousand people per year when everyone is fed.</summary>
    public int BirthsPerMille { get; init; } = 35;

    /// <summary>Natural deaths per thousand people per year.</summary>
    public int DeathsPerMille { get; init; } = 32;

    /// <summary>Share of the people who got no food in a turn who die, in percent.</summary>
    public int StarvationDeathPercent { get; init; } = 80;

    /// <summary>Extra deaths per thousand per year for every health point below <see cref="LowHealthBelow"/>.</summary>
    public Amount LowHealthDeathsPerMillePerPoint { get; init; } = Amount.FromHundredths(20);

    /// <summary>Extra births per thousand per year for every mood point above 50 (fewer below).</summary>
    public Amount MoodBirthsPerMillePerPoint { get; init; } = Amount.FromHundredths(5);

    /// <summary>Share of the gap to the target strata mix that closes every year, in percent.</summary>
    public Amount StrataShiftPercentPerYear { get; init; } = Amount.FromUnits(1);

    /// <summary>Health lost at full hunger, in percent of 100 points.</summary>
    public int HungerPenaltyPercent { get; init; } = 100;

    /// <summary>Health lost per 100% of population above the settlement level's maximum.</summary>
    public int CrowdingPenaltyPercent { get; init; } = 50;

    /// <summary>Health below which extra deaths start.</summary>
    public int LowHealthBelow { get; init; } = 60;

    /// <summary>How far from the settlement people gather, in cells.</summary>
    public int GatherRadius { get; init; } = 2;

    /// <summary>Most food one gathering worker brings in per year.</summary>
    public Amount FoodPerWorkerPerYear { get; init; } = Amount.FromUnits(2);

    /// <summary>Food per year a land cell gives per point of its biome's food value.</summary>
    public Amount FoodPerBiomeFoodPerYear { get; init; } = Amount.FromUnits(4);

    /// <summary>Share of a cell's food that is wild plants; the rest is meat.</summary>
    public int PlantsPercent { get; init; } = 60;

    /// <summary>Hides gathered with every unit of meat.</summary>
    public Amount HidesPerMeat { get; init; } = Amount.FromHundredths(20);

    /// <summary>Yield of hills in percent of flat land.</summary>
    public int HillsPercent { get; init; } = 75;

    /// <summary>Yield of mountains in percent of flat land.</summary>
    public int MountainsPercent { get; init; } = 25;

    /// <summary>Good id of the plant share of a cell's food (-1 = none).</summary>
    public int PlantsGood { get; init; } = -1;

    /// <summary>Good id of the meat share of a cell's food (-1 = none).</summary>
    public int MeatGood { get; init; } = -1;

    /// <summary>Good id of the hides that come with meat (-1 = none).</summary>
    public int HidesGood { get; init; } = -1;

    /// <summary>People in a new tribe, per stratum id.</summary>
    public IReadOnlyList<int> NewTribe { get; init; } = [];
}
