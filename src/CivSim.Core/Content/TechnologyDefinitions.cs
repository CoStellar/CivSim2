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

    /// <summary>The goal food serves (hunger is a matter of safety): technologies and recipes that give food are weighed by it.</summary>
    public Ai.Goal GoalOfFood { get; init; } = Ai.Goal.Security;

    /// <summary>The goal every other good serves (tools, clothing, goods to trade).</summary>
    public Ai.Goal GoalOfGoods { get; init; } = Ai.Goal.Prosperity;

    /// <summary>How the scholar values technologies (GDD 6.1, 8.1; PR B of 2026-11-06).</summary>
    public ScholarRules Scholar { get; init; } = new();

    /// <summary>
    /// Outputs of the valuables category are worth this much more, at full tradefulness, to a civilization whose goods go
    /// to a trade partner (production for exchange).
    /// </summary>
    public int TradeGoodsBonusPercent { get; init; } = 50;

    /// <summary>Technologies the best-known civilization met is ahead by that give the highest science weight.</summary>
    public int ScienceBehindTechs { get; init; } = 4;

    /// <summary>Highest science weight, per mille, when the civilization lags behind those it met.</summary>
    public int ScienceWeightMax { get; init; } = 1500;

    /// <summary>Civilizations met that give the highest influence weight (standing matters among many).</summary>
    public int InfluenceMetCivs { get; init; } = 4;

    /// <summary>Highest influence weight, per mille.</summary>
    public int InfluenceWeightMax { get; init; } = 1300;

    /// <summary>
    /// Share of the expansion weight and expansiveness that a move gets over staying (G2, G3): 100 = staying has no
    /// expansion weight at all, 0 = both sides are weighed alike (before 2026-11-06).
    /// </summary>
    public int MoveExpansionSharePercent { get; init; } = 50;

    /// <summary>A journey (G3) must beat staying and the best place within a move by this much.</summary>
    public int JourneyBetterByPercent { get; init; } = 60;

    /// <summary>A place a journey away loses this much of its value per cell of distance, per mille.</summary>
    public int JourneyPenaltyPerMillePerCell { get; init; } = 10;

    /// <summary>Far places (journeys, the strategist's target) are searched on a lattice of every this many cells each way.</summary>
    public int FarSearchEveryCells { get; init; } = 3;

    /// <summary>The strategist's target loses all value at this many journey ranges from the largest settlement.</summary>
    public int TargetZeroAtJourneys { get; init; } = 2;

    /// <summary>A camp settles when farming its zone would feed this share of its people (G4).</summary>
    public int SettleFarmYieldPercent { get; init; } = 150;

    /// <summary>A camp takes left goods only while its warehouse is fuller than this (G6).</summary>
    public int PlunderBelowStoragePercent { get; init; } = 50;

    /// <summary>A camp whose comfort is below this looks for a warmer or cooler place (G2).</summary>
    public int UncomfortableBelowPercent { get; init; } = 100;

    /// <summary>How much full sympathy or hatred moves the wish to drive out a foreign camp, percent (G2).</summary>
    public int OpinionSwingPercent { get; init; } = 50;

    /// <summary>Planned diplomatic reviews of civilizations are this many years apart (spread, D2).</summary>
    public int ReviewStaggerYears { get; init; } = 7;
}

/// <summary>
/// How the scholar values a technology (PR B of 2026-11-06): the worth of what it gives this civilization per year, by the
/// goal each gain serves, times the goal weight and the character; the cost is an obstacle (cheapness factor).
/// Worths are in barter units per year (hundredths), like the values of goods.
/// </summary>
public sealed record ScholarRules
{
    /// <summary>Worth of any technology per person of the civilization: knowledge for its own sake (science).</summary>
    public Amount BaseWorthPerPerson { get; init; } = Amount.FromHundredths(1);

    /// <summary>Worth per person of a technology of the science, society or military field (science, influence, conquest).</summary>
    public Amount FieldWorthPerPerson { get; init; } = Amount.FromHundredths(2);

    /// <summary>Share of the yields of the zone's cells that a gathering technology adds, percent.</summary>
    public int GatherWorthPercent { get; init; } = 100;

    /// <summary>Share of a recipe's worth while the civilization neither has nor gathers one of its inputs, percent.</summary>
    public int MissingInputPercent { get; init; } = 25;

    /// <summary>
    /// Worth per person of a technology that lets camps settle into villages (security: farming feeds more) on land of
    /// the best fertility; the zone's average farming yield scales it (barren land counts nothing).
    /// </summary>
    public Amount VillageWorthPerPerson { get; init; } = Amount.FromHundredths(10);

    /// <summary>A technology is also worth this share of the best technology it opens the way to, percent.</summary>
    public int LeadsToPercent { get; init; } = 25;

    /// <summary>
    /// How much a fully curious civilization is spared the fear of cost, percent: without curiosity a technology twice
    /// as dear is worth a quarter (cheapness squared), with full curiosity and 100 here a half.
    /// </summary>
    public int CuriosityCostReliefPercent { get; init; } = 100;

    /// <summary>Worths of the effects technologies give, by stat.</summary>
    public IReadOnlyList<EffectWorth> Effects { get; init; } = [];
}

/// <summary>What one point of an effect stat is worth to the scholar.</summary>
/// <param name="Stat">The stat.</param>
/// <param name="Goal">The goal it serves.</param>
/// <param name="PerHundredPeoplePerPoint">Worth per hundred people per year of one point of strength.</param>
/// <param name="LowerIsBetter">True when a negative strength helps (less spoilage).</param>
/// <param name="OnlyWhenCold">True for cold tolerance: only the degrees the civilization's coldest camp lacks count.</param>
public sealed record EffectWorth(EffectStat Stat, Ai.Goal Goal, Amount PerHundredPeoplePerPoint, bool LowerIsBetter, bool OnlyWhenCold);
