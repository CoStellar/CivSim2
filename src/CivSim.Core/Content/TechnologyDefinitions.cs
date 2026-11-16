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

    /// <summary>People the civilization must have before it researches this (all of them; stage 4F, ADR 0020).</summary>
    public IReadOnlyList<PeopleRequirement> RequiresPeople { get; init; } = [];

    /// <summary>When not empty, at least one of these people requirements must be met too.</summary>
    public IReadOnlyList<PeopleRequirement> RequiresPeopleAny { get; init; } = [];
}

/// <summary>
/// People a technology needs (stage 4F, ADR 0020): <see cref="Count"/> people of a skill, or of a stratum when
/// <see cref="SkillId"/> is -1, in the whole civilization or, when <see cref="InOneSettlement"/>, in one settlement.
/// Apprentices count at their share of work.
/// </summary>
public sealed record PeopleRequirement(int SkillId, int StratumId, int Count, bool InOneSettlement);

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

    /// <summary>
    /// How long a player's order on a settlement holds the AI off it, in years of action (stage 6 PR P1, Ai.PlayerOrders): the AI does
    /// not move, split, join or raid from a settlement the player just gave an order to, nor from a camp the player's settlers founded.
    /// </summary>
    public int PlayerOrderHoldYears { get; init; } = 10;

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

    /// <summary>
    /// The AI sends settlers (a crowded camp's group, a village's settlers) only while its administration uses less than this percent of
    /// what its government manages (stage 6 PR T2, ADR 0026): a people fills what it can rule and then grows in place up to the
    /// limit of its settlements' levels (population.json levelLimitScatters). A hungry village still sends mouths away. 0 = no limit.
    /// </summary>
    public int SettlersBelowAdminPercent { get; init; }

    /// <summary>The goal food serves (hunger is a matter of safety): technologies and recipes that give food are weighed by it.</summary>
    public Ai.Goal GoalOfFood { get; init; } = Ai.Goal.Security;

    /// <summary>The goal every other good serves (tools, clothing, goods to trade).</summary>
    public Ai.Goal GoalOfGoods { get; init; } = Ai.Goal.Prosperity;

    /// <summary>How the scholar values technologies (GDD 6.1, 8.1; PR B of 2026-11-06).</summary>
    public ScholarRules Scholar { get; init; } = new();

    /// <summary>How the commander decides on campaigns (stage 4e PR 5).</summary>
    public CommanderRules Commander { get; init; } = new();

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
    /// The least percent of the expansion weight an overloaded state keeps (S1, stage 4f PR 5): the weight is multiplied by
    /// capacity / administration used, but not below this.
    /// </summary>
    public int OverloadExpansionMinPercent { get; init; } = 50;

    /// <summary>Percent applied to the security weight while the civilization is in unrest (S1, stage 4f PR 5).</summary>
    public int UnrestSecurityPercent { get; init; } = 150;

    /// <summary>Percent applied to the score of a war the diplomat considers while its civilization is in unrest (D2, stage 4f PR 5).</summary>
    public int UnrestWarPercent { get; init; } = 50;

    /// <summary>
    /// How much better, percent, another government must suit the civilization than the present one before the strategist
    /// brings it in (S2, stage 4f PR 5; hysteresis against changing back and forth).
    /// </summary>
    public int GovernmentChangeMarginPercent { get; init; } = 30;

    /// <summary>
    /// Points of suitability per point of a government's stability bonus while the civilization is in unrest (S2): a
    /// troubled state looks for a calmer government.
    /// </summary>
    public int GovernmentStabilityWeight { get; init; } = 40;

    /// <summary>Years a new government is kept before the strategist weighs another one (stage 4f PR 5: reforms need time).</summary>
    public int GovernmentKeepYears { get; init; } = 200;

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

    /// <summary>
    /// Settling also needs farming to grow this share of what the camp gathers now, times the expansion weight and
    /// expansiveness: an expansive civilization wanders longer (percent, PR C).
    /// </summary>
    public int SettleOverGatheringPercent { get; init; } = 100;

    /// <summary>A village sends settlers from this share of its level's people on (lower for an expansive civilization, percent).</summary>
    public int VillageSettlersAbovePercent { get; init; } = 80;

    /// <summary>A village whose fields are below this share of their natural fertility sends settlers (percent).</summary>
    public int VillageWornBelowPercent { get; init; } = 70;

    /// <summary>People who leave a village as settlers (at most 90% of it).</summary>
    public int VillageSettlersPeople { get; init; } = 60;

    /// <summary>A village sends at most one group of settlers in this many years (rule 6; staggered by village).</summary>
    public int VillageSettlersEveryYears { get; init; } = 100;

    /// <summary>A village fed below this share (percent) sends settlers to feed fewer mouths (AI fixes after stage 4g).</summary>
    public int VillageHungryFedPercent { get; init; } = 70;

    /// <summary>A hungry village sends at most one group of settlers in this many years (staggered by village).</summary>
    public int VillageHungrySettlersEveryYears { get; init; } = 20;

    /// <summary>
    /// A camp sends explorers when expansion weight × expansiveness × curiosity (per mille, 1000 = neutral) reaches this.
    /// </summary>
    public int ExploreAbovePerMille { get; init; } = 1500;

    /// <summary>Radius around a candidate place in which explorers count unknown land (cells).</summary>
    public int ExploreLookCells { get; init; } = 3;

    /// <summary>Explorers go only where at least this share of the land around is unknown (percent).</summary>
    public int ExploreUnknownPercent { get; init; } = 30;

    /// <summary>Share of a camp's people who leave as explorers (percent).</summary>
    public int ExploreGroupPercent { get; init; } = 25;

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
/// How the commander decides (stage 4e PR 5, decisions of 2026-11-07): it plays the battle through in its head from what
/// it sees (<see cref="Episodes.Battles.Expect"/>), counts the road and the food, and looks again every month of a campaign.
/// </summary>
public sealed record CommanderRules
{
    /// <summary>An army is sent only with at least this expected chance to win, percent (no war it is sure to lose).</summary>
    public int MinWinChancePercent { get; init; } = 60;

    /// <summary>An army is sent only when it expects to lose at most this share of its warriors, percent.</summary>
    public int MaxLossPercent { get; init; } = 50;

    /// <summary>
    /// Months of food an army must have to spare after the months it marches beyond the reach of carriers there and
    /// back; it is not sent otherwise, and on campaign it turns back when its food falls short of that.
    /// </summary>
    public int MinFoodMonths { get; init; } = 1;

    /// <summary>On campaign, the army turns back when the chance it expects from what it sees falls below this, percent.</summary>
    public int RecallChancePercent { get; init; } = 45;
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
