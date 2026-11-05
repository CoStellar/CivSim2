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
public sealed record StratumDefinition(int Id, string Key, string Name, bool Gathers, int TargetSharePercent, IReadOnlyList<NeedDefinition> Needs)
{
    /// <summary>True when the stratum fights and so widens the settlement's influence zone (GDD 5.4).</summary>
    public bool Fights { get; init; }
}

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

    /// <summary>Wild food (per mille of a full cell) removed per year by gathering at full strength.</summary>
    public int WildDepletionPerMillePerYear { get; init; } = 10;

    /// <summary>Share of the missing wild food that grows back every year, in hundredths of a percent.</summary>
    public int WildRegrowthBasisPointsPerYear { get; init; } = 150;

    /// <summary>Largest change of yields from good and bad years, in percent up or down.</summary>
    public int YieldSwingPercent { get; init; } = 15;

    /// <summary>Influence range of a settlement without warriors, in movement cost.</summary>
    public int InfluenceBaseRange { get; init; } = 3;

    /// <summary>Warriors needed for every extra point of influence range.</summary>
    public int WarriorsPerRange { get; init; } = 20;

    /// <summary>Largest influence range.</summary>
    public int InfluenceMaxRange { get; init; } = 8;

    /// <summary>Extra movement cost of hills for influence.</summary>
    public int HillsCost { get; init; } = 1;

    /// <summary>Extra movement cost of mountains for influence.</summary>
    public int MountainsCost { get; init; } = 2;

    /// <summary>Influence strength per point of range left on a cell.</summary>
    public int InfluencePerStep { get; init; } = 30;

    /// <summary>Furthest a camp moves in one turn, in cells.</summary>
    public int MaxMoveCells { get; init; } = 10;

    /// <summary>The tribe AI looks for a new place when less than this share of people was fed.</summary>
    public int MoveWhenFedBelowPercent { get; init; } = 95;

    /// <summary>The tribe AI moves a camp at most once in this many years.</summary>
    public int MinYearsBetweenMoves { get; init; } = 100;

    /// <summary>A new place must give this much more food to be worth the move, in percent.</summary>
    public int MoveWhenBetterByPercent { get; init; } = 30;

    /// <summary>Value lost per cell of distance when the AI compares places, in percent.</summary>
    public int DistancePenaltyPercentPerCell { get; init; } = 3;

    /// <summary>A settlement sends out a split group above this share of its level's maximum population.</summary>
    public int SplitAbovePercentOfLevel { get; init; } = 80;

    /// <summary>Share of the people who leave with a split group, in percent.</summary>
    public int SplitPercent { get; init; } = 40;

    /// <summary>A settlement needs at least this many people to split.</summary>
    public int MinSplitPeople { get; init; } = 60;

    /// <summary>New camps of the same civilization keep at least this many cells apart.</summary>
    public int MinDistanceBetweenOwnCamps { get; init; } = 4;

    /// <summary>AI: a new camp needs land that feeds at least this share of the leaving group (instead of a fixed limit of camps).</summary>
    public int SplitNeedsFoodPercent { get; init; } = 100;

    /// <summary>AI: a camp with fewer people than this joins another camp of its civilization within reach.</summary>
    public int MergeBelowPeople { get; init; } = 15;

    /// <summary>AI: an abandoned camp of the tribe's own counts this much more when the tribe chooses where to go (known land).</summary>
    public int OwnCampsiteBonusPercent { get; init; } = 10;

    /// <summary>AI: a hungry camp attacks a camp in its way only with at least this much of the defenders' strength.</summary>
    public int AttackAboveStrengthPercent { get; init; } = 150;

    /// <summary>Conquest (GDD 5.4, simplified): share of non-warriors that count in the defence of their camp.</summary>
    public int DefenderNonWarriorPercent { get; init; } = 25;

    /// <summary>Conquest: share of the winners' warriors lost in the fight.</summary>
    public int WinnerWarriorLossPercent { get; init; } = 10;

    /// <summary>Conquest: share of the losers' warriors lost in the fight.</summary>
    public int LoserWarriorLossPercent { get; init; } = 40;

    /// <summary>Conquest: share of all expelled people lost while fleeing.</summary>
    public int FleeingLossPercent { get; init; } = 20;

    /// <summary>People in a new tribe, per stratum id.</summary>
    public IReadOnlyList<int> NewTribe { get; init; } = [];
}
