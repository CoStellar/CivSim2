using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CivSim.Core.Content;

namespace CivSim.Sim.Content;

/// <summary>A good the player stores (food, wood, stone…). Ids are list indexes.</summary>
public sealed record SimGood(int Id, string Key, string Name)
{
    /// <summary>Worth of one unit for barter, percent of food.</summary>
    public int WorthPercent { get; init; } = 100;

    /// <summary>Building group whose buildings keep this good at half room (each such building holds twice as much of it); "" = none.</summary>
    public string StoredIn { get; init; } = "";
}

/// <summary>A deposit every start has between <paramref name="Min"/> and <paramref name="Max"/> hexes, at depth <paramref name="Depth"/>.</summary>
public sealed record StartDeposit(int Deposit, int Min, int Max, int Depth, int Size = -1);

/// <summary>A size of deposit (2026-11-22): what a mine or a bonus on it gives, percent of the usual; weights where drawn.</summary>
public sealed record LodeSize(string Key, string Name, int Percent, int Weight, int RegionWeight);

/// <summary>Deposit sizes, regions of rare ores and ore signs (Patryk, 2026-11-22): <c>lodes</c>.</summary>
public sealed record LodeRules
{
    /// <summary>Sizes, smallest first.</summary>
    public LodeSize[] Sizes { get; init; } = [];

    /// <summary>Deposit ids that gather in regions.</summary>
    public int[] Regional { get; init; } = [];

    /// <summary>Region centres per 10000 land hexes (each regional deposit has its own).</summary>
    public int Regions { get; init; } = 4;

    /// <summary>Radius of a region in hexes.</summary>
    public int RegionHexes { get; init; } = 10;

    /// <summary>Share of the deposits outside their regions that stay (percent); as many as go are added inside.</summary>
    public int OutsideKeepPercent { get; init; } = 100;

    /// <summary>Technology that shows ore signs (-1 = none).</summary>
    public int SignTech { get; init; } = -1;

    /// <summary>Radius of a sign circle in hexes.</summary>
    public int SignRadius { get; init; } = 3;

    /// <summary>How far the circle's centre lies from the ore, at most (hexes).</summary>
    public int OffsetHexes { get; init; } = 2;

    /// <summary>Signs per ore at most.</summary>
    public int PerOre { get; init; } = 3;

    /// <summary>Signs only within this many hexes of our settlements.</summary>
    public int ReachHexes { get; init; } = 30;

    /// <summary>Signs are worked out again this often (ticks).</summary>
    public int RecountTicks { get; init; } = 200;

    /// <summary>Ores shown as signs in each era (index = era): deposit ids.</summary>
    public int[][] EraOres { get; init; } = [];
}

/// <summary>Percent of the price, of the experience and goods made, and of the time of research, by era (index = era).</summary>
public sealed record ResearchPace(int[] PricePercent, int[] ExperiencePercent, int[] SecondsPercent)
{
    /// <summary>Percent of the price in an era.</summary>
    public int Price(int era) => (uint)era < (uint)PricePercent.Length ? PricePercent[era] : 100;

    /// <summary>Percent of the experience and goods made asked for in an era.</summary>
    public int Experience(int era) => (uint)era < (uint)ExperiencePercent.Length ? ExperiencePercent[era] : 100;

    /// <summary>Percent of the research time in an era.</summary>
    public int Seconds(int era) => (uint)era < (uint)SecondsPercent.Length ? SecondsPercent[era] : 100;
}

/// <summary>
/// Rules of battle (Patryk, 2026-11-19, claude/walka-propozycja.md): every number of combat in the data (section combat).
/// Percent values are of the damage; Weapon and Armour are multipliers in percent by level 0–4; Flank by how many enemies touch.
/// </summary>
public sealed record CombatRules
{
    /// <summary>Ticks between two steps of battle.</summary>
    public int StepTicks { get; init; } = 5;

    /// <summary>Engaged people × strength × multipliers / this = hundredths of a person killed a step.</summary>
    public long KillDivisor { get; init; } = 400;

    /// <summary>People of a unit that fight one enemy it touches, at most.</summary>
    public int FrontPerContact { get; init; } = 12;

    /// <summary>Weapon multiplier in percent by level.</summary>
    public int[] Weapon { get; init; } = [60, 100, 140, 180, 220];

    /// <summary>Armour divisor in percent by level.</summary>
    public int[] Armour { get; init; } = [100, 130, 170, 210, 260];

    /// <summary>Armour counts this much more (percent of its level's extra) against arrows.</summary>
    public int ArmourVsRangedBonus { get; init; } = 20;

    /// <summary>Damage taken in percent by the number of enemy units touching (index = count; the last for more).</summary>
    public int[] Flank { get; init; } = [100, 100, 125, 150, 180];

    /// <summary>Percent per level of relief above the target, and the most.</summary>
    public int ReliefPercent { get; init; } = 8;

    /// <summary>Most percent from relief.</summary>
    public int ReliefMaxPercent { get; init; } = 16;

    /// <summary>Height difference (elevation units) for one step of ElevationPercent.</summary>
    public int ElevationStep { get; init; } = 20;

    /// <summary>Percent per step of height above the target.</summary>
    public int ElevationPercent { get; init; } = 3;

    /// <summary>Most percent from height.</summary>
    public int ElevationMaxPercent { get; init; } = 9;

    /// <summary>Cover of a hex with trees: damage change from ranged and melee attackers.</summary>
    public (int Ranged, int Melee) TreeCover { get; init; } = (-30, -15);

    /// <summary>Cover of a palisade.</summary>
    public (int Ranged, int Melee) PalisadeCover { get; init; } = (-50, -30);

    /// <summary>Cover of a wall.</summary>
    public (int Ranged, int Melee) WallCover { get; init; } = (-50, -30);

    /// <summary>Melee across a river with no bridge.</summary>
    public int RiverPercent { get; init; } = -25;

    /// <summary>More taken on roads and in swamps.</summary>
    public int OpenPercent { get; init; } = 15;

    /// <summary>Biomes where a unit stands open (swamps).</summary>
    public bool[] OpenBiomes { get; init; } = [];

    /// <summary>A strike on a unit leaving, percent of a step.</summary>
    public int DisengagePercent { get; init; } = 50;

    /// <summary>The same for cavalry.</summary>
    public int CavalryDisengagePercent { get; init; } = 25;

    /// <summary>Hexes cavalry walks before contact to charge.</summary>
    public int ChargeHexes { get; init; } = 2;

    /// <summary>Damage of the charging strike, percent.</summary>
    public int ChargePercent { get; init; } = 200;

    /// <summary>Shares of its full size (percent) at which a unit's people are warned it weakens.</summary>
    public int[] WarnPercents { get; init; } = [50, 25];

    /// <summary>Seconds with no fighting after which a new battle slows a single-player game to ×1 again.</summary>
    public int CalmSeconds { get; init; } = 30;

    /// <summary>Seconds before a unit with no way to its target searches again.</summary>
    public int ChaseRetrySeconds { get; init; } = 5;

    /// <summary>Failed searches after which a unit gives its target up.</summary>
    public int ChaseGiveUp { get; init; } = 3;

    /// <summary>Training time of a unit with weapons only (no armour), percent.</summary>
    public int ShockTrainPercent { get; init; } = 40;
}

/// <summary>A level of a settlement: territory radius, housing and storage it gives, price of reaching it.</summary>
public sealed record SettlementLevel(int Id, string Key, string Name, int Radius, int Housing, long Storage, long[] UpgradePrice)
{
    /// <summary>Technology needed to reach this level (−1 = none).</summary>
    public int Tech { get; init; } = -1;

    /// <summary>Other technologies needed as well (a level can need an ordinary and a political technology).</summary>
    public int[] AlsoTechs { get; init; } = [];

    /// <summary>People the settlement must have to reach this level (E2, 2026-11-19).</summary>
    public int MinPeople { get; init; }

    /// <summary>9B (Patryk, 2026-11-20): most homes a settlement of this level has, whatever it builds (0 = no limit).</summary>
    public int MaxHousing { get; init; }
}

/// <summary>An era (Stone Age … Middle Ages).</summary>
public sealed record SimEra(int Id, string Key, string Name);

/// <summary>What a technology effect changes.</summary>
public enum EffectKind
{
    /// <summary>Production of a building (target: building key or *), percent.</summary>
    Production,

    /// <summary>Homes of a building, added.</summary>
    Housing,

    /// <summary>Room of a building (target: key or *), percent.</summary>
    Storage,

    /// <summary>Strength of a unit (target: unit key or *), percent.</summary>
    Strength,

    /// <summary>Walking speed of a unit (target: unit key, carriers or *), percent faster.</summary>
    Speed,

    /// <summary>Training time of a unit (target: unit key or *), percent shorter.</summary>
    Training,

    /// <summary>Research price, percent cheaper.</summary>
    Research,

    /// <summary>Growth, percent of people per minute added.</summary>
    Growth,

    /// <summary>Harm of an event (target: event key), percent less.</summary>
    Resist,

    /// <summary>Damage to settlements (target: unit key or *), percent.</summary>
    Siege,

    /// <summary>Defence of fortifications (target: building key or *), percent.</summary>
    Defense,

    /// <summary>Chance of rarer thinkers and advisors, percent.</summary>
    Luck,

    /// <summary>Load a carrier person takes (target: unit key or carriers), whole units added.</summary>
    Cargo,

    /// <summary>Least fertility a building needs (target: building key or group), points lower.</summary>
    Fertility,
}

/// <summary>One effect of a technology.</summary>
public sealed record TechEffect(EffectKind Kind, string Target, int Value);

/// <summary>A technology: experience needed in earlier technologies (worker-ticks), price (hundredths) and time (ticks).</summary>
public sealed record SimTech(int Id, string Key, string Name, int Era, bool KnownAtStart, (int Tech, long WorkerTicks)[] Experience, long[] Price, int ResearchTicks)
{
    /// <summary>What it changes besides what it unlocks.</summary>
    public TechEffect[] Effects { get; init; } = [];

    /// <summary>An upgrade of its era (better arms, tools…) rather than a base technology.</summary>
    public bool Upgrade { get; init; }

    /// <summary>One line on what it gives, for the player.</summary>
    public string Gives { get; init; } = "";

    /// <summary>Branch of the tree (basics, food, industry, building, military, governance, trade, science).</summary>
    public string Category { get; init; } = "";

    /// <summary>Technologies that must be known first, with no experience asked in them.</summary>
    public int[] Requires { get; init; } = [];

    /// <summary>Goods the people must have made in all (hundredths): experience gained by making, not by working time.</summary>
    public (int Good, long Amount)[] Made { get; init; } = [];

    /// <summary>A first step of the basics branch: it does not count toward the next era.</summary>
    public bool Basic => Category == SimContent.BasicsCategory;
}

/// <summary>A work drawn on hexes (ditch, palisade, wall, aqueduct): price and time per hex and what it does.</summary>
public sealed record WorkKind(int Id, string Key, string Name, int Tech, long[] Price, int TicksPerHex)
{
    /// <summary>Its bit in <c>SimGame.WorksAt</c>.</summary>
    public int Bit => 1 << Id;

    /// <summary>Longest reach from water, hexes (ditches, aqueducts; 0 = needs no water).</summary>
    public int FromWater { get; init; }

    /// <summary>Farms on a watered hex make this much more, percent.</summary>
    public int FarmPercent { get; init; }

    /// <summary>Enemies at war standing on it lose this share of their people a second, percent.</summary>
    public int WoundPercentPerSecond { get; init; }

    /// <summary>Damage it takes before it breaks, hundredths (walls; 0 = does not stop anyone).</summary>
    public long Strength { get; init; }

    /// <summary>Growth a settlement it reaches gains, percent of people a minute.</summary>
    public int GrowthBonus { get; init; }

    /// <summary>Epidemics in a settlement it reaches are this much milder, percent.</summary>
    public int ResistEpidemicPercent { get; init; }
}

/// <summary>A level of road: price and time per hex, walking cost on it (percent).</summary>
public sealed record RoadLevel(int Level, string Key, string Name, int Tech, long[] Price, int TicksPerHex, int WalkPercent);

/// <summary>A rarity of thinkers and advisors.</summary>
public sealed record Rarity(int Id, string Key, string Name, int Weight, int CostPercent, int ExperiencePercent, int PriceTimes);

/// <summary>A kind of unit (settlers, scouts; armies in stage E). Price in hundredths.</summary>
public sealed record UnitType(int Id, string Key, string Name, int People, long[] Price, int TrainTicks, int TicksPerHex, int HiddenTicksPerHex, int Sight, bool Founds)
{
    /// <summary>Technology that allows it.</summary>
    public int Tech { get; init; }

    /// <summary>Other technologies it needs as well (caravans: the wheel and horseback riding).</summary>
    public int[] AlsoTechs { get; init; } = [];

    /// <summary>Unit kind it turns into when a technology becomes known (hunters become archers with Archery): (tech, unit kind).</summary>
    public (int Tech, int Kind)[] Becomes { get; set; } = [];

    /// <summary>Ships that carry armies.</summary>
    public bool CarriesArmies { get; init; }

    /// <summary>What it finds when it stands still: 0 nothing, 1 rock (prospectors), 2 soil (surveyors).</summary>
    public int Survey { get; init; }

    /// <summary>People of armies a ship carries.</summary>
    public int Carries { get; init; }

    /// <summary>Horses a caravan takes with it (they come back with it).</summary>
    public long HorsesPerCaravan { get; init; }

    /// <summary>Tags technology effects aim at (melee, ranged, cavalry, carriers…).</summary>
    public string[] Groups { get; init; } = [];

    /// <summary>Building the settlement must have to train it (empty = none).</summary>
    public string NeedsBuilding { get; init; } = "";

    /// <summary>Extra damage against settlements, percent.</summary>
    public int SiegePercent { get; init; }

    /// <summary>Made by the game, not trained (porters, caravans).</summary>
    public bool System { get; init; }

    /// <summary>Moves on water only (ships).</summary>
    public bool Naval { get; init; }

    /// <summary>Goods carried per person, hundredths (porters, caravans).</summary>
    public long CargoPerPerson { get; init; }

    /// <summary>A fighting unit.</summary>
    public bool Army { get; init; }

    /// <summary>Called-up workers: free, weak, go back to work when disbanded.</summary>
    public bool Levy { get; init; }

    /// <summary>Strength per person (warriors = 10).</summary>
    public int Strength { get; init; }

    /// <summary>Reach in hexes (1 = next hex).</summary>
    public int Range { get; init; } = 1;

    /// <summary>Biomes where it fights better.</summary>
    public bool[] BonusBiomes { get; init; } = [];

    /// <summary>Reliefs where it fights better.</summary>
    public bool[] BonusReliefs { get; init; } = [];

    /// <summary>Bonus on its terrain, percent.</summary>
    public int BonusPercent { get; init; }

    /// <summary>Technologies that make it stronger, with the percent.</summary>
    public (int Tech, int Percent)[] Upgrades { get; init; } = [];

    /// <summary>More damage against units of a group, percent (spearmen against cavalry, riders against archers).</summary>
    public (string Group, int Percent)[] BonusVs { get; init; } = [];

    /// <summary>Charges do not work against it (spearmen).</summary>
    public bool StopsCharge { get; init; }

    /// <summary>Armour levels of the target it ignores (crossbows).</summary>
    public int Pierce { get; init; }

    /// <summary>Smallest size the player may train (0 = the default size only).</summary>
    public int MinPeople { get; init; }

    /// <summary>Largest size the player may train.</summary>
    public int MaxPeople { get; init; }

    /// <summary>True for riders (charge, light disengage).</summary>
    public bool Cavalry => Array.IndexOf(Groups, "cavalry") >= 0;

    /// <summary>True for archers and other units that shoot.</summary>
    public bool Ranged => Range > 1;
}

/// <summary>Rules of fighting.</summary>
/// <summary>
/// Sieges and garrisons (Patryk, 2026-11-20, questions 3, 5, 7, 8): see the siege section of the data.
/// </summary>
public sealed record SiegeRules(
    int BlockadeRange,
    int BlockadeWorkPercent,
    int LiftSeconds,
    int GuardPerPalisadeHexPercent,
    int GuardPerWallHexPercent,
    int GuardStrength,
    int GuardRegrowPerMinute,
    int ResidentsFloorPercent,
    int SackPercent,
    int SackImmuneSeconds,
    int ChoiceSeconds)
{
    /// <summary>13B (Patryk, 2026-11-20): the attackers strike the defenders only this long after the blockade began (ticks).</summary>
    public int AssaultAfterTicks { get; init; }

    /// <summary>2B: AI takes a fallen settlement within this many hexes of its own (farther: sacks it).</summary>
    public int AiTakeWithinHexes { get; init; } = 20;
}

/// <summary>How many army units a people may have (Patryk, 2026-11-20, question 11): see the armyLimit section of the data.</summary>
public sealed record ArmyLimitRules(int[] PerLevel, (int Building, int Add)[] PerBuilding, (int Tech, int Add)[] PerTech);

/// <summary>One of the bonuses a kind of unit may take at a level of experience (veterans).</summary>
public sealed record VeteranOption(string Key, string Name, string Text, int StrengthPercent, int TakenPercent, int SpeedPercent);

/// <summary>Experience of each kind of unit (Patryk, 2026-11-20, question 9): see the veterans section of the data.</summary>
public sealed record VeteranRules(int[] Levels, VeteranOption[] Options)
{
    /// <summary>14C (Patryk, 2026-11-20): a kill counts for every kind of ours fighting this close to the enemy (0 = only the killer's kind).</summary>
    public int ShareNearHexes { get; init; }
}

/// <summary>Supplies of the army in the field (Patryk, 2026-11-20, question 1): see the supply section of the data.</summary>
public sealed record SupplyRules(int RangeHexes, int CarryMinutes, int LandHands, int LandPercent)
{
    /// <summary>A caravan to a unit with no way to it is refused; the AI sends none to that unit for this long (ticks).</summary>
    public int NoWayTicks { get; init; } = 60 * SimContent.TicksPerSecond;
}

/// <summary>Caravans for a settlement's upgrade (A1, tests 2026-11-20): margin carried, and the AI's wait after a failure.</summary>
public sealed record UpgradeCaravanRules(int ExtraPercent, int AiRetryTicks);

public sealed record WarRules(int CombatEveryTicks, int LowLuckPercent, int HighLuckPercent, int DefenderPercent, int DefenderStrength, int CaptureBelowPeople, int SurpriseReputation)
{
    /// <summary>Reputation every declaration of war costs (E1, 2026-11-19).</summary>
    public int DeclareReputation { get; init; } = -10;

    /// <summary>People a people must have lost to the war before its AI agrees to pay tribute.</summary>
    public int TributeNeedsKills { get; init; } = 5;

    /// <summary>1C (Patryk, 2026-11-20): peace with tribute only this long after the war began (ticks).</summary>
    public int TributeAfterTicks { get; init; } = 120 * SimContent.TicksPerSecond;

    /// <summary>6C: reputation never falls below this.</summary>
    public int ReputationFloor { get; init; } = -200;

    /// <summary>6C: below this reputation the AI declares new wars half as often.</summary>
    public int BadReputation { get; init; } = -100;

    /// <summary>P2 (tests 2026-11-23): a bad name heals by this much a minute while the people starts no war (up to 0).</summary>
    public int ReputationRegenPerMinute { get; init; }

    /// <summary>Most food, percent of the payer's, a tribute takes (E3).</summary>
    public int TributeFoodMaxPercent { get; init; } = 10;

    /// <summary>Most wars a people may be in at once (design, chapter 8: 2-3).</summary>
    public int MaxWars { get; init; } = 3;
}

/// <summary>
/// Peace from negotiation (ADR 0030, Patryk 2026-11-23): goods and a settlement as terms, a chosen length of peace, and terms
/// valued in worth (the <c>peace</c> section of the data).
/// </summary>
public sealed record PeaceRules
{
    /// <summary>3B: the lengths of peace one may choose, minutes.</summary>
    public int[] Minutes { get; init; } = [3, 6, 10];

    /// <summary>3B: reputation for breaking a peace of each length, percent of the surprise attack's cost.</summary>
    public int[] BreakPercent { get; init; } = [100, 100, 200];

    /// <summary>2B: a settlement may be demanded when our army besieges it or it lies this close to our land.</summary>
    public int SettlementHexes { get; init; } = 12;

    /// <summary>5A: worth of a settlement: this many food units per person ...</summary>
    public int SettlementWorthPerPerson { get; init; } = 10;

    /// <summary>5A: ... plus this percent of the price of its finished buildings.</summary>
    public int SettlementBuildingPercent { get; init; } = 50;

    /// <summary>5A: one percent of the loser's wealth per this many points of war score lead.</summary>
    public int ScorePerPercent { get; init; } = 5;

    /// <summary>7.1A: one percent of the loser's wealth per this many points of weariness lead.</summary>
    public int WearinessPerPercent { get; init; } = 4;

    /// <summary>Most of its wealth (goods and settlements but its first), percent, the course of a war asks a loser to give.</summary>
    public int MaxPercent { get; init; } = 40;

    /// <summary>7.3A: a loser who declared the war gives this much more, percent.</summary>
    public int AggressorExtraPercent { get; init; } = 25;

    /// <summary>5A: the most peaceful AI gives or forgives this much more than the course of the war asks, percent (0 for the most warlike).</summary>
    public int AiGiveMarginPercent { get; init; } = 20;

    /// <summary>3B: the AI's margin changes by these percents for each length of peace while it loses (longer is welcome).</summary>
    public int[] AiLosingLengthPercent { get; init; } = [-10, 0, 10];

    /// <summary>3B: ... and by these while it wins (shorter is welcome).</summary>
    public int[] AiWinningLengthPercent { get; init; } = [10, 0, -10];
}

/// <summary>
/// 7 (ADR 0030, Patryk 2026-11-23): war weariness of a people in each war, 0-100 points: it grows with the length of the war,
/// losses, hunger, besieged, sacked and taken settlements and raided caravans, and falls in peace.
/// </summary>
public sealed record WearinessRules
{
    /// <summary>Points a minute of war.</summary>
    public int PerMinute { get; init; } = 1;

    /// <summary>Points per one percent of the people killed by the enemy.</summary>
    public int PerLostPercent { get; init; } = 2;

    /// <summary>Points a minute while some settlement of ours is hungry.</summary>
    public int HungerPerMinute { get; init; } = 2;

    /// <summary>Points a minute for each settlement of ours the enemy besieges.</summary>
    public int BesiegedPerMinute { get; init; } = 2;

    /// <summary>Points when the enemy sacks a settlement of ours.</summary>
    public int Sacked { get; init; } = 10;

    /// <summary>Points when the enemy takes or razes a settlement of ours.</summary>
    public int Taken { get; init; } = 15;

    /// <summary>Points when the enemy raids a caravan of ours.</summary>
    public int CaravanRaided { get; init; } = 3;

    /// <summary>Points a minute it falls in peace.</summary>
    public int PeaceFallPerMinute { get; init; } = 5;

    /// <summary>7.2A: from this weariness our workshops (buildings that use goods) work less ...</summary>
    public int WorkFrom { get; init; } = 50;

    /// <summary>7.2A: ... down to this many percent less at 100.</summary>
    public int WorkMaxLossPercent { get; init; } = 20;

    /// <summary>7.1A: at this weariness the AI looks for peace.</summary>
    public int AiPeaceAt { get; init; } = 60;

    /// <summary>At this weariness a losing AI offers goods for peace, not only a white peace.</summary>
    public int AiPayAt { get; init; } = 80;
}

/// <summary>
/// Known issue 15 (2026-11-23): soldiers marching on the same goal share one search of the way. A soldier within
/// <see cref="ShareHexes"/> of where the last search for that goal started, less than <see cref="ShareTicks"/> ago, walks
/// to that start (a search of at most <see cref="JoinSearch"/> hexes) and then the same way.
/// </summary>
public sealed record MarchRules(int ShareTicks = 50, int ShareHexes = 3, int JoinSearch = 300);

/// <summary>Choosing the start (startPhase in the data): ticks to pick, the block radius and its fuzz, the scoring radii.</summary>
public sealed record StartPhaseRules(int PickTicks, int BlockRadius, int BlockFuzz, int ScoreRadius, int RoomRadius)
{
    /// <summary>12B (Patryk, 2026-11-20): every people starts at most this many hexes by land from another (0 = anywhere), on the main continent.</summary>
    public int NeighbourMaxHexes { get; init; }

    /// <summary>12B: placed starts (and the AI's choices) keep at least this distance from other starts when the land allows.</summary>
    public int NeighbourMinHexes { get; init; }
}

/// <summary>Rations: every <paramref name="EveryTicks"/> a settlement feeds all its people, residents and units (Patryk, 2026-11-19).</summary>
public sealed record RationRules(int EveryTicks, int HungryPercent, int StarvingPercent, int StarveLosePercent);

/// <summary>A building type from <c>data/sim/game.json</c>. Amounts in hundredths; rates per worker per minute.</summary>
public sealed class BuildingType
{
    /// <summary>Index in <see cref="SimContent.Buildings"/>, from 1 (0 means no building).</summary>
    public int Id { get; init; }

    /// <summary>Data key.</summary>
    public string Key { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Jobs.</summary>
    public int Workers { get; init; }

    /// <summary>Price per good, hundredths.</summary>
    public long[] Price { get; init; } = [];

    /// <summary>Construction time in ticks.</summary>
    public int BuildTicks { get; init; }

    /// <summary>Allowed biomes (empty = any land).</summary>
    public bool[] Biomes { get; init; } = [];

    /// <summary>Allowed reliefs (empty = any).</summary>
    public bool[] Reliefs { get; init; } = [];

    /// <summary>Needs water next to the hex (sea, lake or a river edge).</summary>
    public bool NeedsWater { get; init; }

    /// <summary>A surface deposit that also allows the building where terrain does not (0 = none).</summary>
    public int OrDeposit { get; init; }

    /// <summary>Production per worker per minute, hundredths.</summary>
    public long[] Produces { get; init; } = [];

    /// <summary>Consumption per worker per minute, hundredths.</summary>
    public long[] Consumes { get; init; } = [];

    /// <summary>Deposit on the hex that raises production (0 = none).</summary>
    public int BonusDeposit { get; init; }

    /// <summary>Extra production in percent with the bonus deposit.</summary>
    public int BonusPercent { get; init; }

    /// <summary>Housing added.</summary>
    public int Housing { get; init; }

    /// <summary>Storage per good added, hundredths.</summary>
    public long Storage { get; init; }

    /// <summary>True when the building has inputs.</summary>
    public bool HasInputs { get; init; }

    /// <summary>Technology that allows it; its workers give experience in that technology.</summary>
    public int Tech { get; init; }

    /// <summary>Least fertility of the hex (0 = any).</summary>
    public int MinFertility { get; init; }

    /// <summary>Research is this much cheaper while the building works (percent).</summary>
    public int ResearchDiscountPercent { get; init; }

    /// <summary>Tags technology effects aim at (food, farm, tools, craft, store, fort…).</summary>
    public string[] Groups { get; init; } = [];

    /// <summary>Reliefs that raise production.</summary>
    public bool[] BonusReliefs { get; init; } = [];

    /// <summary>Extra production in percent on those reliefs.</summary>
    public int BonusReliefPercent { get; init; }

    /// <summary>Defence of its settlement, percent (fortifications).</summary>
    public int DefensePercent { get; init; }

    /// <summary>Standing guard this building gives its settlement (siege; a castle's garrison).</summary>
    public int Guard { get; init; }

    /// <summary>Training time in its settlement, percent (barracks).</summary>
    public int TrainPercent { get; init; }

    /// <summary>Places it takes on its hex: 1, 2 or 4 (the whole hex) of the four (decision of 2026-11-18).</summary>
    public int Size { get; init; } = 1;

    /// <summary>Its production in winter, percent, on top of every worker's winter slowdown (gathering and fields).</summary>
    public int WinterPercent { get; init; } = 100;

    /// <summary>Other technologies it needs as well.</summary>
    public int[] AlsoTechs { get; init; } = [];

    /// <summary>Research of the branches its workers are set to is this much faster, percent at full staff (schools, academies, universities).</summary>
    public int ResearchPercent { get; init; }

    /// <summary>Building in its settlement is this much faster at full staff, percent (builders' yards).</summary>
    public int BuildPercent { get; init; }

    /// <summary>Epidemics in its settlement are this much milder at full staff, percent (bathhouses).</summary>
    public int ResistEpidemicPercent { get; init; }

    /// <summary>Growth in its settlement, percent of people a minute added at full staff (bathhouses).</summary>
    public int GrowthBonus { get; init; }

    /// <summary>Its homes are warm (stone houses): people living in them lose less work in winter.</summary>
    public bool Warm { get; init; }

    /// <summary>Most of a good it can hold (stables: 16 horses each), hundredths per good; 0 = no limit of its own.</summary>
    public long[] GoodLimit { get; init; } = [];

    /// <summary>It may also stand on a grove outside its biomes (woodcutters), giving the grove's share of wood.</summary>
    public bool OrTrees { get; init; }

    /// <summary>Better kind it can be upgraded to in place (0 = none).</summary>
    public int UpgradesTo { get; set; }

    internal string UpgradeKey { get; init; } = "";

    /// <summary>Its people may cross the rivers next to it (raft landings).</summary>
    public bool Crossing { get; init; }

    /// <summary>Buildings of this group in the same settlement produce <see cref="SupportPercent"/> more while it works.</summary>
    public string SupportGroup { get; init; } = "";

    /// <summary>See <see cref="SupportGroup"/>.</summary>
    public int SupportPercent { get; init; }

    /// <summary>Wounded soldiers of its people it heals a minute when they stand in its settlement's land.</summary>
    public int HealsPerMinute { get; init; }
}

/// <summary>The rules of the new game, loaded from <c>data/sim/game.json</c> plus the world content of the legacy loader.</summary>
public sealed class SimContent
{
    /// <summary>Hundredths in one unit.</summary>
    public const long One = 100;

    /// <summary>Ticks per second.</summary>
    public const int TicksPerSecond = 10;

    /// <summary>Ticks per minute.</summary>
    public const int TicksPerMinute = 600;

    /// <summary>World content (biomes, deposits, climate) used by the world generator.</summary>
    public required GameContent World { get; init; }

    /// <summary>Hex density against the legacy map.</summary>
    public int Density { get; init; }

    /// <summary>Economy rhythm.</summary>
    public int EconomyEveryTicks { get; init; }

    /// <summary>Population rhythm.</summary>
    public int PopulationEveryTicks { get; init; }

    /// <summary>Goods.</summary>
    public required IReadOnlyList<SimGood> Goods { get; init; }

    /// <summary>Settlement levels, from the camp up.</summary>
    public required IReadOnlyList<SettlementLevel> Levels { get; init; }

    /// <summary>Building types; index 0 is an empty placeholder so that ids start at 1.</summary>
    public required IReadOnlyList<BuildingType> Buildings { get; init; }

    /// <summary>People of a new tribe.</summary>
    public int StartPeople { get; init; }

    /// <summary>Stock of a new tribe, hundredths.</summary>
    public required long[] StartStock { get; init; }

    /// <summary>Food per person per minute, hundredths.</summary>
    public long FoodPerPersonPerMinute { get; init; }

    /// <summary>Goods a gatherer picks up per minute around a settlement of richness 100, hundredths, per good.</summary>
    public required long[] ForagePerMinute { get; init; }

    /// <summary>
    /// Patryk, 2026-11-23: idle people told to gather find at least this share (percent) of the food they eat, in any land and
    /// season, so a people left alone survives but gathers nothing to grow on (<c>forage.subsistencePercent</c>).
    /// </summary>
    public int ForageSubsistencePercent { get; init; }

    /// <summary>Food and wood richness of each biome: index biome * 2 (+0 food, +1 wood).</summary>
    public required int[] ForageBiome { get; init; }

    /// <summary>Stone richness of each relief (flat, hills, mountains).</summary>
    public int[] ForageRelief { get; init; } = [0, 0, 0];

    /// <summary>Richness a surface deposit adds: index deposit * goods + good.</summary>
    public required int[] ForageDeposit { get; init; }

    /// <summary>Rules of battle (section combat).</summary>
    public CombatRules Combat { get; init; } = new();

    private bool[][]? _halfRoom;

    /// <summary>Per building type and good: the building keeps the good at half room (an armoury its weapons).</summary>
    public bool HalfRoom(int building, int good)
    {
        _halfRoom ??= [.. Buildings.Select(b => Goods.Select(g => g.StoredIn.Length > 0 && Array.IndexOf(b.Groups, g.StoredIn) >= 0).ToArray())];
        return _halfRoom[building][good];
    }

    /// <summary>Weapon goods by level 1–4 (index 0 unused, −1 = missing).</summary>
    public int[] WeaponGoods { get; init; } = [-1, -1, -1, -1, -1];

    /// <summary>Armour goods by level 1–4 (index 0 unused, −1 = missing).</summary>
    public int[] ArmourGoods { get; init; } = [-1, -1, -1, -1, -1];

    /// <summary>Food richness a river by the hex adds.</summary>
    public int ForageRiverFood { get; init; }

    /// <summary>Winter comes every this many ticks (0 = never).</summary>
    public int SeasonCycleTicks { get; init; }

    /// <summary>Winter lasts this many ticks, at the end of each cycle.</summary>
    public int WinterTicks { get; init; }

    /// <summary>Peoples are told this many ticks before winter.</summary>
    public int WinterWarnTicks { get; init; }

    /// <summary>Every worker's output in winter, percent.</summary>
    public int WinterWorkPercent { get; init; } = 100;

    /// <summary>Work is rounded down to steps of this many permille (seasons.workStepPermille).</summary>
    public int WorkStepPermille { get; init; } = 25;

    /// <summary>Schools and academies speed a branch of research at most this much, percent (research.maxSupportPercent).</summary>
    public int MaxResearchSupportPercent { get; init; } = 50;

    /// <summary>
    /// Pace of research by era (research.pace, 2026-11-19: about 60 minutes to the Middle Ages): percent of each technology's
    /// price, of the experience and goods made it asks for, and of its time. Index = era; missing eras 100.
    /// </summary>
    public ResearchPace Pace { get; init; } = new([], [], []);

    /// <summary>Choosing the start: time, the block around taken starts and the scoring radii (startPhase).</summary>
    public StartPhaseRules StartPhase { get; init; } = new(300, 10, 3, 3, 6);

    /// <summary>Id of clothing (−1 = none).</summary>
    public int Clothing { get; init; } = -1;

    /// <summary>Output a set of clothing adds to a worker, percent.</summary>
    public int ClothingBonusPercent { get; init; }

    /// <summary>Highest output with clothing in winter, percent.</summary>
    public int ClothingWinterMaxPercent { get; init; } = 100;

    /// <summary>Clothing worn out per person per minute, hundredths.</summary>
    public long ClothingWearPerPersonPerMinute { get; init; }

    /// <summary>Clothing wears out only in winter (clothing.wearOnlyInWinter; G5 test, 2026-11-19).</summary>
    public bool ClothingWearOnlyInWinter { get; init; }

    /// <summary>Share of the fallen who are wounded instead of dead, percent (herbalists heal them).</summary>
    public int InjuredPercent { get; init; }

    /// <summary>From this era rivers no longer stop walkers; before it they cross only by a crossing building of their own.</summary>
    public int RiversFreeEra { get; init; } = int.MaxValue;

    /// <summary>Technologies of the basics branch: cheap first steps that do not count toward the next era.</summary>
    public const string BasicsCategory = "basics";

    /// <summary>Growth percent of people per minute.</summary>
    public int GrowthPercentPerMinute { get; init; }

    /// <summary>Least growth per minute (people).</summary>
    public int MinGrowthPerMinute { get; init; }

    /// <summary>
    /// Patryk, 2026-11-22 (variant C): a settlement whose food falls stops growing while less than this many rations of food
    /// are left (growth.stopBelowRations; 0 = only an empty store stops it).
    /// </summary>
    public int GrowthStopRations { get; init; } = 2;

    /// <summary>Starvation loss percent per minute.</summary>
    public int StarvePercentPerMinute { get; init; }

    /// <summary>Extra price, percent, for resuming a paused construction.</summary>
    public int ResumeCostPercent { get; init; }

    /// <summary>Patryk, 2026-11-24: the "nothing is being researched" reminder waits this long after a research ends (alerts.noResearchSeconds).</summary>
    public int NoResearchSeconds { get; init; } = 60;

    /// <summary>11B (2026-11-20): the food alert comes when a settlement's food lasts this many minutes or less (alerts.foodLowMinutes).</summary>
    public int FoodLowMinutes { get; init; } = 3;

    /// <summary>16B (2026-11-20): the AI builds new homes only with food for this many minutes of eating (ai.homesFoodMinutes).</summary>
    public int AiHomesFoodMinutes { get; init; } = 3;

    /// <summary>
    /// 2026-11-22: outside winter the AI wants food made to exceed its eating by this percent (ai.foodMarginPercent), so the
    /// summer surplus covers the next winter; short of it, it builds food and no new homes.
    /// </summary>
    public int AiFoodMarginPercent { get; init; } = 30;

    /// <summary>2026-11-22: the AI builds one more food building at a time per this many people (ai.foodSitesPerPeople).</summary>
    public int AiFoodSitesPerPeople { get; init; } = 400;

    /// <summary>
    /// Known issue 13 (2026-11-23): a standing route the AI opens for a workshop's input carries this many minutes of the
    /// target's use a trip, while the target holds less than <see cref="AiInputRouteTargetMinutes"/>; the source keeps
    /// <see cref="AiInputRouteKeepMinutes"/> of its own use (ai.inputRoute*).
    /// </summary>
    public int AiInputRouteTripMinutes { get; init; } = 3;

    /// <summary>See <see cref="AiInputRouteTripMinutes"/>.</summary>
    public int AiInputRouteTargetMinutes { get; init; } = 6;

    /// <summary>See <see cref="AiInputRouteTripMinutes"/>.</summary>
    public int AiInputRouteKeepMinutes { get; init; } = 2;

    /// <summary>
    /// 2026-11-23: the AI builds another maker of an input its workshops use more of than its people makes, until the store
    /// lasts this many minutes of the shortfall (ai.inputMakerStoreMinutes).
    /// </summary>
    public int AiInputMakerStoreMinutes { get; init; } = 10;

    /// <summary>Amounts the AI decides by, in hundredths (A3, tests 2026-11-24: they were written in its code; ai.amounts).</summary>
    public AiAmounts AiAmount { get; init; } = new();

    /// <summary>Prospectors the AI keeps while workshops lack an ore no known free deposit gives (known-issues 13); else one.</summary>
    public int AiStarvedProspectors { get; init; } = 2;

    /// <summary>Most standing input routes the AI keeps, per settlement it has.</summary>
    public int AiInputRoutesPerSettlement { get; init; } = 4;

    /// <summary>17B (2026-11-20): below this many people the AI trains no units but settlers and builds no workplace while jobs are free (ai.smallPeople).</summary>
    public int AiSmallPeople { get; init; } = 30;

    /// <summary>The AI researches only while this many minutes of its eating stay in store after the price (ai.researchKeepFoodMinutes).</summary>
    public int AiResearchKeepFoodMinutes { get; init; } = 3;

    /// <summary>Ticks between two thoughts of one AI people (ai.thinkEveryTicks).</summary>
    public int AiThinkEveryTicks { get; init; } = 30;

    /// <summary>The AI remembers for this many minutes whether its armies can reach a settlement (ai.wayToMinutes).</summary>
    public int AiWayToMinutes { get; init; } = 2;

    /// <summary>A6 (tests 2026-11-23): people a settlement needs before the AI builds a school, bathhouse, academy, university there (ai.careSettlementPeople).</summary>
    public IReadOnlyDictionary<string, int> AiCarePeople { get; init; } = new Dictionary<string, int> { ["school"] = 40, ["bathhouse"] = 50, ["academy"] = 70, ["university"] = 90 };

    /// <summary>Patryk, 2026-11-21: the AI declares no war in the first this many minutes of a game (ai.peacefulStartMinutes).</summary>
    public int AiPeacefulStartMinutes { get; init; } = 15;

    /// <summary>How the AI fights (roadmap 7, 2026-11-21): <c>aiBattle</c>.</summary>
    public AiBattleRules AiBattle { get; init; } = new();

    /// <summary>Hexes a settlement sees beyond its own land (P3, Patryk 2026-11-23: 2, was 1): <c>vision.settlementSightBeyond</c>.</summary>
    public int SettlementSightBeyond { get; init; } = 1;

    /// <summary>When the AI starts a war (tests 2026-11-23): <c>aiWar</c>.</summary>
    public AiWarRules AiWar { get; init; } = new();

    /// <summary>Deposit sizes, regions and ore signs (2026-11-22).</summary>
    public LodeRules Lodes { get; init; } = new();

    /// <summary>Caravans for upgrades (upgradeCaravans).</summary>
    public UpgradeCaravanRules UpgradeCaravans { get; init; } = new(10, 60 * TicksPerSecond);

    /// <summary>Rations of the units, paid by their home settlement.</summary>
    public RationRules Rations { get; init; } = new(600, 75, 50, 20);

    /// <summary>Technologies of its era a people must know to enter the next era (eras are levels, not one gate technology).</summary>
    public int EraAdvanceTechs { get; init; } = 5;

    /// <summary>Deposits every start has near it (startDeposits; M4, 2026-11-19).</summary>
    public StartDeposit[] StartDeposits { get; init; } = [];

    /// <summary>How many army units a people may have (armyLimit).</summary>
    public ArmyLimitRules ArmyLimit { get; init; } = new([1, 2, 4, 6], [], []);

    /// <summary>Experience of each kind of unit (veterans).</summary>
    public VeteranRules Veterans { get; init; } = new([], []);

    /// <summary>Supplies of the army in the field (supply).</summary>
    public SupplyRules Supply { get; init; } = new(3, 4, 10, 100);

    /// <summary>Sieges and garrisons (siege).</summary>
    /// <summary>Peace from negotiation (ADR 0030).</summary>
    public PeaceRules Peace { get; init; } = new();

    /// <summary>A shared way for soldiers marching on one goal (known issue 15).</summary>
    public MarchRules March { get; init; } = new();

    /// <summary>War weariness (ADR 0030, point 7).</summary>
    public WearinessRules Weariness { get; init; } = new();

    public SiegeRules Siege { get; init; } = new(2, 50, 30, 50, 100, 8, 3, 40, 50, 180, 20);

    /// <summary>Seconds a settlement stays empty before it falls to ruin (ruins.emptySeconds; G2, 2026-11-19).</summary>
    public int RuinEmptySeconds { get; init; } = 60;

    /// <summary>Share of a ruined building's price its rebuilding costs, percent.</summary>
    public int RuinRebuildPercent { get; init; } = 50;

    /// <summary>3B (Patryk, 2026-11-20): percent of a razed settlement's store that stays in its ruins (the rest is lost).</summary>
    public int RuinKeepPercent { get; init; } = 100;

    /// <summary>Buildings a new camp starts with, finished, paid from the settlers' provisions (foundWith).</summary>
    public int[] FoundWith { get; init; } = [];

    /// <summary>Per era: the technology that must be known to leave it (−1 = none; M1, 2026-11-19).</summary>
    public int[] EraGateway { get; init; } = [];

    /// <summary>Per era: goods (hundredths) the people must have made in all to enter it (7A, 2026-11-20; empty = none).</summary>
    public long[][] EraEntryMade { get; init; } = [];

    /// <summary>The gateway technology of an era, −1 when it has none.</summary>
    public int GatewayOf(int era) => (uint)era < (uint)EraGateway.Length ? EraGateway[era] : -1;

    /// <summary>Branches of the technology tree, in display order.</summary>
    public string[] TechCategories { get; init; } = [];

    /// <summary>Id of food.</summary>
    public int Food { get; init; }

    /// <summary>Unit kinds.</summary>
    public required IReadOnlyList<UnitType> Units { get; init; }

    /// <summary>Least distance between settlement centres.</summary>
    public int FoundMinDistance { get; init; }

    /// <summary>Share of a building's price that counts toward its upgrade, percent.</summary>
    public int UpgradeCreditPercent { get; init; } = 50;

    /// <summary>Eras.</summary>
    public required IReadOnlyList<SimEra> Eras { get; init; }

    /// <summary>Technologies.</summary>
    public required IReadOnlyList<SimTech> Techs { get; init; }

    /// <summary>Discount per known people that knows a technology, percent.</summary>
    public int NeighbourDiscountPercent { get; init; }

    /// <summary>Most discount from neighbours, percent.</summary>
    public int MaxNeighbourDiscountPercent { get; init; }

    /// <summary>Rarities of thinkers and advisors.</summary>
    public required IReadOnlyList<Rarity> Rarities { get; init; }

    /// <summary>Thinker rules.</summary>
    public int ThinkerRetireTicks { get; init; }

    /// <summary>Ticks between thinker candidates.</summary>
    public int ThinkerOfferTicks { get; init; }

    /// <summary>Edge lost per thinker generation, percent.</summary>
    public int GenerationLossPercent { get; init; }

    /// <summary>Base price of hiring a thinker.</summary>
    public required long[] ThinkerPrice { get; init; }

    /// <summary>Ticks between advisor candidates.</summary>
    public int AdvisorOfferTicks { get; init; }

    /// <summary>Base price of hiring an advisor.</summary>
    public required long[] AdvisorPrice { get; init; }

    /// <summary>Food an advisor eats per minute, hundredths.</summary>
    public long AdvisorUpkeep { get; init; }

    /// <summary>Rules of fighting.</summary>
    public required WarRules War { get; init; }

    /// <summary>Research discount per trade partner, percent.</summary>
    public int TradeResearchDiscount { get; init; }

    /// <summary>Least worth (percent of what it gives) the AI wants from a barter.</summary>
    public int AcceptWorthPercent { get; init; }

    /// <summary>How long peoples must have known each other before a treaty, ticks.</summary>
    public int TreatyKnownTicks { get; init; }

    /// <summary>Farthest distance between settlements for a treaty, hexes.</summary>
    public int TreatyBorderHexes { get; init; }

    /// <summary>Time without fighting before a treaty, ticks.</summary>
    public int TreatyPeaceTicks { get; init; }

    /// <summary>Least reputation of the other people for a treaty.</summary>
    public int TreatyMinReputation { get; init; }

    /// <summary>Least time between two offers to the same people, ticks.</summary>
    public int OfferCooldownTicks { get; init; }

    /// <summary>Wait after a refusal, ticks.</summary>
    public int RefusalCooldownTicks { get; init; }

    /// <summary>The shared pool of events.</summary>
    public required IReadOnlyList<State.SimEvent> Events { get; init; }

    /// <summary>Ticks between two events of a people (at least).</summary>
    public int EventEveryTicks { get; init; }

    /// <summary>Random extra ticks between events.</summary>
    public int EventSpreadTicks { get; init; }

    /// <summary>Advisor fields.</summary>
    public required IReadOnlyList<string> AdvisorFields { get; init; }

    /// <summary>Room every finished building adds when its type gives none of its own, hundredths.</summary>
    public long BuildingStorage { get; init; }

    /// <summary>A good counts as full from this percent of its room (P1, tests 2026-11-24).</summary>
    public int StoreFullPercent { get; init; } = 98;

    /// <summary>A full good stays full until it falls below this percent of its room.</summary>
    public int StoreStillFullPercent { get; init; } = 90;

    /// <summary>An empty good (below one unit) stays empty until it reaches this many hundredths.</summary>
    public long StoreStillEmptyBelow { get; init; } = 3 * One;

    /// <summary>Unit kind of porters.</summary>
    public int Porters { get; init; }

    /// <summary>Unit kind of caravans.</summary>
    public int Caravans { get; init; }

    /// <summary>Technology needed to trade with other peoples (caravans abroad, trade agreements).</summary>
    public int TradeTech { get; init; }

    /// <summary>Technology needed to build roads (−1 = none).</summary>
    public int RoadTech { get; init; } = -1;

    /// <summary>Walking cost on a road, percent of the hex's normal cost.</summary>
    public int RoadCostPercent { get; init; } = 50;

    /// <summary>Levels of roads (index + 1 is the level on the map: 1 track, 2 paved road).</summary>
    public IReadOnlyList<RoadLevel> RoadLevels { get; init; } = [];

    /// <summary>How far from our nearest settlement roads may be built, in hexes.</summary>
    public int RoadReach { get; init; } = 25;

    /// <summary>Goods kept only in buildings that give room for them (horses: stables), per good.</summary>
    public bool[] LimitedGoods => _limited ??= LimitedOf();

    private bool[]? _limited;

    private bool[] LimitedOf()
    {
        var f = new bool[Goods.Count];
        foreach (BuildingType b in Buildings)
        {
            for (int g = 0; g < b.GoodLimit.Length; g++)
            {
                f[g] |= b.GoodLimit[g] > 0;
            }
        }

        return f;
    }

    /// <summary>Share of each biome's land hexes with a grove, percent (open land; forests are all trees).</summary>
    public int[] GroveChance { get; init; } = [];

    /// <summary>Wood a grove gives woodcutters and gatherers, percent of a forest's.</summary>
    public int GroveWoodPercent { get; init; } = 60;

    /// <summary>Hexes around a standing prospector or surveyor that it looks at.</summary>
    public int SurveyRadius { get; init; } = 3;

    /// <summary>Per deposit id: 0 seen on the surface, 1 found by prospectors (rock), 2 by surveyors (soil).</summary>
    public int[] DepositSurvey { get; init; } = [];

    /// <summary>Per deposit id: the era from which a people recognises it (rock); int.MaxValue = not in these eras.</summary>
    public int[] DepositEra { get; init; } = [];

    /// <summary>Biomes that are forest (all trees).</summary>
    public bool[] ForestBiomes { get; init; } = [];

    /// <summary>Works drawn on hexes, in data order (ditch, palisade, wall, aqueduct).</summary>
    public IReadOnlyList<WorkKind> Works { get; init; } = [];

    /// <summary>Technology after which roads bridge the rivers they cross (−1 = never).</summary>
    public int BridgeTech { get; init; } = -1;

    /// <summary>Price of a bridge, hundredths per good.</summary>
    public long[] BridgePrice { get; init; } = [];

    /// <summary>Finds a work by key (null when the data has none).</summary>
    public WorkKind? Work(string key)
    {
        foreach (WorkKind w in Works)
        {
            if (w.Key == key)
            {
                return w;
            }
        }

        return null;
    }

    /// <summary>Goods a caravan brings from a settlement to a new camp joined to it by road.</summary>
    public long[] RoadSupply { get; init; } = [];

    /// <summary>Finds a technology by key.</summary>
    public int TechId(string key)
    {
        foreach (SimTech t in Techs)
        {
            if (t.Key == key)
            {
                return t.Id;
            }
        }

        throw new KeyNotFoundException($"Unknown technology '{key}'.");
    }

    /// <summary>Walking cost of each hex in percent of a flat open hex.</summary>
    public required int[] WalkCost { get; init; }

    /// <summary>Finds a unit kind by key.</summary>
    public UnitType Unit(string key)
    {
        foreach (UnitType u in Units)
        {
            if (u.Key == key)
            {
                return u;
            }
        }

        throw new KeyNotFoundException($"Unknown unit '{key}'.");
    }

    /// <summary>Finds a good by key.</summary>
    public int GoodId(string key)
    {
        foreach (SimGood g in Goods)
        {
            if (g.Key == key)
            {
                return g.Id;
            }
        }

        throw new KeyNotFoundException($"Unknown good '{key}'.");
    }

    /// <summary>Finds a building type by key.</summary>
    public BuildingType Building(string key)
    {
        foreach (BuildingType b in Buildings)
        {
            if (b.Key == key)
            {
                return b;
            }
        }

        throw new KeyNotFoundException($"Unknown building '{key}'.");
    }

    /// <summary>Loads the rules from a data folder (default: the folder next to the program).</summary>
    private static int[] Ints(JsonElement e, string name) => e.TryProp(name, out JsonElement v) ? [.. v.EnumerateArray().Select(x => x.GetInt32())] : [];

    private static CombatRules ReadCombat(JsonElement c)
    {
        int I(string name, int fallback) => c.TryProp(name, out JsonElement v) ? v.GetInt32() : fallback;
        int[] A(string name, int[] fallback) => c.TryProp(name, out JsonElement v) ? [.. v.EnumerateArray().Select(x => x.GetInt32())] : fallback;
        (int, int) Cover(string name, (int, int) fallback) => c.TryProp("cover", out JsonElement cv) && cv.TryProp(name, out JsonElement k)
            ? (k.Prop("ranged").GetInt32(), k.Prop("melee").GetInt32()) : fallback;
        var d = new CombatRules();
        return new CombatRules
        {
            StepTicks = Math.Max(1, I("stepTicks", d.StepTicks)),
            KillDivisor = Math.Max(1, I("killDivisor", (int)d.KillDivisor)),
            FrontPerContact = Math.Max(1, I("frontPerContact", d.FrontPerContact)),
            Weapon = A("weapon", d.Weapon),
            Armour = A("armour", d.Armour),
            ArmourVsRangedBonus = I("armourVsRangedBonus", d.ArmourVsRangedBonus),
            Flank = A("flank", d.Flank),
            ReliefPercent = I("reliefPercent", d.ReliefPercent),
            ReliefMaxPercent = I("reliefMaxPercent", d.ReliefMaxPercent),
            ElevationStep = Math.Max(1, I("elevationStep", d.ElevationStep)),
            ElevationPercent = I("elevationPercent", d.ElevationPercent),
            ElevationMaxPercent = I("elevationMaxPercent", d.ElevationMaxPercent),
            TreeCover = Cover("trees", d.TreeCover),
            PalisadeCover = Cover("palisade", d.PalisadeCover),
            WallCover = Cover("wall", d.WallCover),
            RiverPercent = I("riverPercent", d.RiverPercent),
            OpenPercent = I("openPercent", d.OpenPercent),
            DisengagePercent = I("disengagePercent", d.DisengagePercent),
            CavalryDisengagePercent = I("cavalryDisengagePercent", d.CavalryDisengagePercent),
            ChargeHexes = I("chargeHexes", d.ChargeHexes),
            ChargePercent = I("chargePercent", d.ChargePercent),
            ShockTrainPercent = I("shockTrainPercent", d.ShockTrainPercent),
            CalmSeconds = I("calmSeconds", d.CalmSeconds),
            WarnPercents = A("warnPercents", d.WarnPercents),
            ChaseRetrySeconds = I("chaseRetrySeconds", d.ChaseRetrySeconds),
            ChaseGiveUp = Math.Max(1, I("chaseGiveUp", d.ChaseGiveUp)),
        };
    }

    // Per deposit id: how it is found (see SimContent.DepositSurvey). Without a survey section everything is on the surface.
    private static int[] SurveyKinds(GameContent world, JsonElement root)
    {
        int max = world.Deposits.Count == 0 ? 0 : world.Deposits.Max(d => d.Id);
        var kinds = new int[max + 1];
        if (!root.TryProp("survey", out JsonElement sv))
        {
            return kinds;
        }

        var surface = sv.Prop("surface").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        var soil = sv.Prop("soil").EnumerateArray().Select(x => x.GetString()).ToHashSet();
        foreach (DepositDefinition d in world.Deposits)
        {
            kinds[d.Id] = surface.Contains(d.Key) ? 0 : soil.Contains(d.Key) ? 2 : 1;
        }

        return kinds;
    }

    // Per deposit id: the era from which prospectors recognise it.
    private static int[] SurveyEras(GameContent world, JsonElement root, List<SimEra> eras)
    {
        int max = world.Deposits.Count == 0 ? 0 : world.Deposits.Max(d => d.Id);
        var at = new int[max + 1];
        if (!root.TryProp("survey", out JsonElement sv))
        {
            return at;
        }

        JsonElement byEra = sv.Prop("eras");
        foreach (DepositDefinition d in world.Deposits)
        {
            at[d.Id] = byEra.TryProp(d.Key, out JsonElement e) && eras.FindIndex(x => x.Key == e.GetString()) is int i && i >= 0 ? i : int.MaxValue;
        }

        return at;
    }

    /// <summary>
    /// Checks data/sim/game.json (2026-11-22): it must load, and every key in it must be one the game reads ("comment"
    /// keys aside). Returns the problems, empty when the file is fine. The start menu and Reload data show them.
    /// </summary>
    public static IReadOnlyList<string> Check(string? directory = null)
    {
        directory ??= CivSim.Data.ContentLoader.DefaultDirectory;
        var errors = new List<string>();
        HashSet<string> read = JsonRead.Begin();
        try
        {
            SimContent content = Load(directory);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "sim", "game.json")));
            UnknownKeys(doc.RootElement, string.Empty, read, errors);
            MapKeys(doc.RootElement, content, errors);
        }
        catch (CivSim.Data.ContentException ex)
        {
            errors.AddRange(ex.Errors);
        }
        catch (Exception ex) when (ex is InvalidDataException or KeyNotFoundException or JsonException or InvalidOperationException or FormatException or IOException)
        {
            errors.Add($"sim/game.json: {ex.Message}");
        }
        finally
        {
            JsonRead.End();
        }

        return errors;
    }

    // Keys the game never asked for, with their place in the file.
    private static void UnknownKeys(JsonElement e, string path, HashSet<string> read, List<string> errors)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in e.EnumerateObject())
            {
                string at = path.Length == 0 ? p.Name : path + "." + p.Name;
                bool note = p.Name == "comment" || (path.Length == 0 && p.Name == "version"); // notes for people, the file's version
                if (!note && !read.Contains(p.Name))
                {
                    errors.Add($"sim/game.json: unknown key '{at}' (the game does not read it: a typo or an old setting?)");
                    continue;
                }

                UnknownKeys(p.Value, at, read, errors);
            }
        }
        else if (e.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (JsonElement x in e.EnumerateArray())
            {
                UnknownKeys(x, $"{path}[{i++}]", read, errors);
            }
        }
    }

    // Maps whose keys name buildings or technologies and that the loader skips quietly when a key is unknown.
    private static void MapKeys(JsonElement root, SimContent content, List<string> errors)
    {
        if (!root.TryGetProperty("armyLimit", out JsonElement limit))
        {
            return;
        }

        foreach (var (map, keys) in new[] { ("perBuilding", content.Buildings.Select(b => b.Key)), ("perTech", content.Techs.Select(t => t.Key)) })
        {
            if (limit.TryGetProperty(map, out JsonElement m))
            {
                var known = keys.ToHashSet();
                foreach (JsonProperty p in m.EnumerateObject())
                {
                    if (!known.Contains(p.Name))
                    {
                        errors.Add($"sim/game.json: unknown {(map == "perBuilding" ? "building" : "technology")} '{p.Name}' in armyLimit.{map}");
                    }
                }
            }
        }
    }

    /// <summary>Reads the rules of the new game from data/sim/game.json (the legacy content from the same folder too).</summary>
    public static SimContent Load(string? directory = null)
    {
        directory ??= CivSim.Data.ContentLoader.DefaultDirectory;
        GameContent world = CivSim.Data.ContentLoader.Load(directory);
        string path = Path.Combine(directory, "sim", "game.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        var goods = new List<SimGood>();
        foreach (JsonElement g in root.Prop("goods").EnumerateArray())
        {
            goods.Add(new SimGood(goods.Count, g.Prop("key").GetString()!, g.Prop("name").GetString()!)
            {
                WorthPercent = g.TryProp("worth", out JsonElement w) ? (int)(Hundredths(w)) : 100,
                StoredIn = g.TryProp("storedIn", out JsonElement si) ? si.GetString() ?? "" : "",
            });
        }

        int GoodOf(string key)
        {
            int i = goods.FindIndex(x => x.Key == key);
            return i >= 0 ? i : throw new InvalidDataException($"sim/game.json: unknown good '{key}'.");
        }

        long[] Amounts(JsonElement e)
        {
            var a = new long[goods.Count];
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in e.Entries())
                {
                    a[GoodOf(p.Name)] += Hundredths(p.Value);
                }
            }

            return a;
        }

        int DepositOf(JsonElement b, string name)
        {
            if (!b.TryProp(name, out JsonElement e) || string.IsNullOrEmpty(e.GetString()))
            {
                return 0;
            }

            string key = e.GetString()!;
            foreach (DepositDefinition d in world.Deposits)
            {
                if (d.Key == key)
                {
                    return d.Id;
                }
            }

            throw new InvalidDataException($"sim/game.json: unknown deposit '{key}'.");
        }

        var eras = new List<SimEra>();
        foreach (JsonElement e in root.Prop("eras").EnumerateArray())
        {
            eras.Add(new SimEra(eras.Count, e.Prop("key").GetString()!, e.Prop("name").GetString()!));
        }

        var techKeys = new List<string>();
        foreach (JsonElement t in root.Prop("technologies").EnumerateArray())
        {
            techKeys.Add(t.Prop("key").GetString()!);
        }

        int TechOf(JsonElement e, string name)
        {
            if (!e.TryProp(name, out JsonElement v))
            {
                return -1;
            }

            int i = techKeys.IndexOf(v.GetString()!);
            return i >= 0 ? i : throw new InvalidDataException($"sim/game.json: unknown technology '{v.GetString()}'.");
        }

        int[] TechList(JsonElement e, string name)
        {
            var list = new List<int>();
            if (e.TryProp(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    int i = techKeys.IndexOf(k.GetString()!);
                    list.Add(i >= 0 ? i : throw new InvalidDataException($"sim/game.json: unknown technology '{k.GetString()}'."));
                }
            }

            return [.. list];
        }

        (int, long)[] MadeOf(JsonElement t)
        {
            var list = new List<(int, long)>();
            if (t.TryProp("made", out JsonElement m))
            {
                foreach (JsonProperty p in m.Entries())
                {
                    list.Add((GoodOf(p.Name), Hundredths(p.Value)));
                }
            }

            return [.. list];
        }

        static TechEffect[] EffectsOf(JsonElement t)
        {
            var list = new List<TechEffect>();
            if (t.TryProp("effects", out JsonElement ef))
            {
                foreach (JsonElement e in ef.EnumerateArray())
                {
                    string kind = e.Prop("kind").GetString()!;
                    if (!Enum.TryParse(kind, ignoreCase: true, out EffectKind k))
                    {
                        throw new InvalidDataException($"sim/game.json: unknown effect '{kind}'.");
                    }

                    list.Add(new TechEffect(k, e.TryProp("target", out JsonElement tg) ? tg.GetString()! : "*", e.Prop("value").GetInt32()));
                }
            }

            return [.. list];
        }

        var techs = new List<SimTech>();
        foreach (JsonElement t in root.Prop("technologies").EnumerateArray())
        {
            var exp = new List<(int, long)>();
            if (t.TryProp("experience", out JsonElement xe))
            {
                foreach (JsonProperty p in xe.Entries())
                {
                    int ti = techKeys.IndexOf(p.Name);
                    if (ti < 0)
                    {
                        throw new InvalidDataException($"sim/game.json: unknown technology '{p.Name}' in experience.");
                    }

                    exp.Add((ti, p.Value.GetInt64() * TicksPerMinute));
                }
            }

            string eraKey = t.Prop("era").GetString()!;
            int era = eras.FindIndex(x => x.Key == eraKey);
            techs.Add(new SimTech(
                techs.Count, techKeys[techs.Count], t.Prop("name").GetString()!, era >= 0 ? era : throw new InvalidDataException($"sim/game.json: unknown era '{eraKey}'."),
                t.TryProp("known", out JsonElement k) && k.GetBoolean(), [.. exp],
                t.TryProp("price", out JsonElement tp) ? Amounts(tp) : new long[goods.Count],
                t.TryProp("seconds", out JsonElement ts) ? ts.GetInt32() * TicksPerSecond : 0)
            {
                Effects = EffectsOf(t),
                Upgrade = t.TryProp("upgrade", out JsonElement ug) && ug.GetBoolean(),
                Gives = t.TryProp("gives", out JsonElement gv) ? gv.GetString() ?? "" : "",
                Category = t.TryProp("category", out JsonElement cat) ? cat.GetString() ?? "" : "",
                Requires = TechList(t, "requires"),
                Made = MadeOf(t),
            });
        }

        var levels = new List<SettlementLevel>();
        foreach (JsonElement l in root.Prop("settlementLevels").EnumerateArray())
        {
            levels.Add(new SettlementLevel(
                levels.Count, l.Prop("key").GetString()!, l.Prop("name").GetString()!, l.Prop("radius").GetInt32(),
                l.Prop("housing").GetInt32(), Hundredths(l.Prop("storage")), Amounts(l.Prop("upgradePrice")))
            {
                Tech = TechOf(l, "tech"),
                AlsoTechs = TechList(l, "alsoTechs"),
                MinPeople = l.TryProp("minPeople", out JsonElement mp) ? mp.GetInt32() : 0,
                MaxHousing = l.TryProp("maxHousing", out JsonElement mh) ? mh.GetInt32() : 0,
            });
        }

        static string[] Strings(JsonElement e, string name)
        {
            var list = new List<string>();
            if (e.TryProp(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    list.Add(k.GetString()!);
                }
            }

            return [.. list];
        }

        static bool[] ReliefsOf(JsonElement e, string name)
        {
            var f = new bool[3];
            if (e.TryProp(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[k.GetString() switch { "flat" => 0, "hills" => 1, "mountains" => 2, _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'.") }] = true;
                }
            }

            return f;
        }

        var buildings = new List<BuildingType> { new() { Id = 0, Key = "", Name = "" } };
        foreach (JsonElement b in root.Prop("buildings").EnumerateArray())
        {
            var biomes = new bool[world.Biomes.Count];
            if (b.TryProp("biomes", out JsonElement be))
            {
                foreach (JsonElement k in be.EnumerateArray())
                {
                    biomes[world.Biome(k.GetString()!).Id] = true;
                }
            }

            var reliefs = new bool[3];
            if (b.TryProp("relief", out JsonElement re))
            {
                foreach (JsonElement k in re.EnumerateArray())
                {
                    reliefs[k.GetString() switch
                    {
                        "flat" => 0,
                        "hills" => 1,
                        "mountains" => 2,
                        _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'."),
                    }] = true;
                }
            }

            long[] consumes = b.TryProp("consumes", out JsonElement ce) ? Amounts(ce) : new long[goods.Count];
            bool hasInputs = false;
            foreach (long c in consumes)
            {
                hasInputs |= c > 0;
            }

            buildings.Add(new BuildingType
            {
                Id = buildings.Count,
                Key = b.Prop("key").GetString()!,
                Name = b.Prop("name").GetString()!,
                Workers = b.Prop("workers").GetInt32(),
                Price = Amounts(b.Prop("price")),
                BuildTicks = (int)(Hundredths(b.Prop("buildSeconds")) * TicksPerSecond / One),
                Biomes = biomes,
                Reliefs = reliefs,
                NeedsWater = b.TryProp("needsWater", out JsonElement w) && w.GetBoolean(),
                OrDeposit = DepositOf(b, "orDeposit"),
                Produces = b.TryProp("produces", out JsonElement pe) ? Amounts(pe) : new long[goods.Count],
                Consumes = consumes,
                HasInputs = hasInputs,
                BonusDeposit = DepositOf(b, "bonusDeposit"),
                BonusPercent = b.TryProp("bonusPercent", out JsonElement bp) ? bp.GetInt32() : 0,
                Housing = b.TryProp("housing", out JsonElement h) ? h.GetInt32() : 0,
                Storage = b.TryProp("storage", out JsonElement s) ? Hundredths(s) : 0,
                Tech = Math.Max(0, TechOf(b, "tech")),
                MinFertility = b.TryProp("minFertility", out JsonElement mf) ? mf.GetInt32() : 0,
                ResearchDiscountPercent = b.TryProp("researchDiscountPercent", out JsonElement rd) ? rd.GetInt32() : 0,
                DefensePercent = b.TryProp("defensePercent", out JsonElement dp) ? dp.GetInt32() : 0,
                Guard = b.TryProp("guard", out JsonElement gd) ? gd.GetInt32() : 0,
                BonusReliefs = ReliefsOf(b, "bonusRelief"),
                Groups = Strings(b, "groups"),
                BonusReliefPercent = b.TryProp("bonusReliefPercent", out JsonElement brp) ? brp.GetInt32() : 0,
                TrainPercent = b.TryProp("trainPercent", out JsonElement tp2) ? tp2.GetInt32() : 100,
                WinterPercent = b.TryProp("winterPercent", out JsonElement wp) ? wp.GetInt32() : 100,
                Size = b.TryProp("size", out JsonElement sz) ? sz.GetInt32() is 1 or 2 or 4 ? sz.GetInt32() : throw new InvalidDataException($"sim/game.json: size of '{b.Prop("key").GetString()}' must be 1, 2 or 4.") : 1,
                AlsoTechs = TechList(b, "alsoTechs"),
                ResearchPercent = b.TryProp("researchPercent", out JsonElement rpc) ? rpc.GetInt32() : 0,
                BuildPercent = b.TryProp("buildPercent", out JsonElement bpc2) ? bpc2.GetInt32() : 0,
                ResistEpidemicPercent = b.TryProp("resistEpidemicPercent", out JsonElement rep) ? rep.GetInt32() : 0,
                GrowthBonus = b.TryProp("growthBonus", out JsonElement gb) ? gb.GetInt32() : 0,
                GoodLimit = b.TryProp("goodLimit", out JsonElement gl) ? Amounts(gl) : new long[goods.Count],
                Warm = b.TryProp("warm", out JsonElement wm) && wm.GetBoolean(),
                Crossing = b.TryProp("crossing", out JsonElement cr) && cr.GetBoolean(),
                OrTrees = b.TryProp("orTrees", out JsonElement ot) && ot.GetBoolean(),
                UpgradeKey = b.TryProp("upgradesTo", out JsonElement ut) ? ut.GetString() ?? "" : "",
                SupportGroup = b.TryProp("supportGroup", out JsonElement sgp) ? sgp.GetString() ?? "" : "",
                SupportPercent = b.TryProp("supportPercent", out JsonElement spc) ? spc.GetInt32() : 0,
                HealsPerMinute = b.TryProp("healsPerMinute", out JsonElement hpm) ? hpm.GetInt32() : 0,
            });
        }

        foreach (BuildingType upgradable in buildings)
        {
            if (upgradable.UpgradeKey.Length > 0)
            {
                upgradable.UpgradesTo = buildings.FindIndex(x => x.Key == upgradable.UpgradeKey) is int up && up > 0
                    ? up : throw new InvalidDataException($"sim/game.json: '{upgradable.Key}' upgrades to unknown '{upgradable.UpgradeKey}'.");
            }
        }

        bool[] Flags(JsonElement e, string name)
        {
            var f = new bool[world.Biomes.Count];
            if (e.TryProp(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[world.Biome(k.GetString()!).Id] = true;
                }
            }

            return f;
        }

        bool[] Reliefs(JsonElement e, string name)
        {
            var f = new bool[3];
            if (e.TryProp(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[k.GetString() switch { "flat" => 0, "hills" => 1, "mountains" => 2, _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'.") }] = true;
                }
            }

            return f;
        }

        (int, int)[] Upgrades(JsonElement e)
        {
            var list = new List<(int, int)>();
            if (e.TryProp("upgrades", out JsonElement up))
            {
                foreach (JsonProperty p in up.Entries())
                {
                    int ti = techKeys.IndexOf(p.Name);
                    list.Add((ti >= 0 ? ti : throw new InvalidDataException($"sim/game.json: unknown technology '{p.Name}'."), p.Value.GetInt32()));
                }
            }

            return [.. list];
        }

        var units = new List<UnitType>();
        foreach (JsonElement u in root.Prop("units").EnumerateArray())
        {
            int perHex = u.Prop("ticksPerHex").GetInt32();
            units.Add(new UnitType(
                units.Count, u.Prop("key").GetString()!, u.Prop("name").GetString()!, u.Prop("people").GetInt32(), Amounts(u.Prop("price")),
                (int)(Hundredths(u.Prop("trainSeconds")) * TicksPerSecond / One), perHex,
                u.TryProp("hiddenTicksPerHex", out JsonElement hp) ? hp.GetInt32() : 0, u.Prop("sight").GetInt32(),
                u.TryProp("founds", out JsonElement f) && f.GetBoolean())
            {
                Tech = Math.Max(0, TechOf(u, "tech")),
                System = u.TryProp("system", out JsonElement sy) && sy.GetBoolean(),
                Groups = Strings(u, "groups"),
                NeedsBuilding = u.TryProp("needsBuilding", out JsonElement nb) ? nb.GetString() ?? "" : "",
                SiegePercent = u.TryProp("siege", out JsonElement sg) ? sg.GetInt32() : 0,
                Naval = u.TryProp("naval", out JsonElement nv) && nv.GetBoolean(),
                CargoPerPerson = u.TryProp("cargoPerPerson", out JsonElement cp) ? Hundredths(cp) : 0,
                Army = u.TryProp("army", out JsonElement ar) && ar.GetBoolean(),
                Levy = u.TryProp("levy", out JsonElement lv) && lv.GetBoolean(),
                Strength = u.TryProp("strength", out JsonElement st) ? st.GetInt32() : 0,
                Range = u.TryProp("range", out JsonElement rg) ? rg.GetInt32() : 1,
                BonusBiomes = Flags(u, "bonusBiomes"),
                BonusReliefs = Reliefs(u, "bonusRelief"),
                BonusPercent = u.TryProp("bonusPercent", out JsonElement bpc) ? bpc.GetInt32() : 0,
                Upgrades = Upgrades(u),
                AlsoTechs = TechList(u, "alsoTechs"),
                CarriesArmies = u.TryProp("carriesArmies", out JsonElement ca) && ca.GetBoolean(),
                BonusVs = u.TryProp("bonusVs", out JsonElement bv) ? [.. bv.Entries().Select(p => (p.Name, p.Value.GetInt32()))] : [],
                StopsCharge = u.TryProp("stopsCharge", out JsonElement sc) && sc.GetBoolean(),
                Pierce = u.TryProp("pierce", out JsonElement pi) ? pi.GetInt32() : 0,
                MinPeople = u.TryProp("minPeople", out JsonElement mnp) ? mnp.GetInt32() : 0,
                MaxPeople = u.TryProp("maxPeople", out JsonElement mxp) ? mxp.GetInt32() : 0,
                Survey = u.TryProp("survey", out JsonElement sv) ? sv.GetString() switch { "rock" => 1, "soil" => 2, _ => 0 } : 0,
                Carries = u.TryProp("carries", out JsonElement cr2) ? cr2.GetInt32() : 0,
                HorsesPerCaravan = u.TryProp("horsesPerCaravan", out JsonElement hpc) ? Hundredths(hpc) : 0,
            });
        }

        int ui = 0;
        foreach (JsonElement u in root.Prop("units").EnumerateArray())
        {
            if (u.TryProp("becomes", out JsonElement bec))
            {
                var list = new List<(int, int)>();
                foreach (JsonProperty p in bec.Entries())
                {
                    int ti = techKeys.IndexOf(p.Name);
                    int to = units.FindIndex(x => x.Key == p.Value.GetString());
                    list.Add((ti >= 0 ? ti : throw new InvalidDataException($"sim/game.json: unknown technology '{p.Name}'."), to >= 0 ? to : throw new InvalidDataException($"sim/game.json: unknown unit '{p.Value.GetString()}'.")));
                }

                units[ui].Becomes = [.. list];
            }

            ui++;
        }

        JsonElement tc = root.Prop("terrainCost");
        var forest = new bool[world.Biomes.Count];
        foreach (JsonElement k in tc.Prop("forestBiomes").EnumerateArray())
        {
            forest[world.Biome(k.GetString()!).Id] = true;
        }

        // Walking cost per biome × relief, flattened: index = biome * 3 + relief; 0 = impassable (water).
        var walk = new int[world.Biomes.Count * 3];
        for (int b = 0; b < world.Biomes.Count; b++)
        {
            for (int r = 0; r < 3; r++)
            {
                int cost = world.Biomes[b].IsWater ? 0 : 100;
                if (cost > 0)
                {
                    cost = cost * (r == 1 ? tc.Prop("hills").GetInt32() : r == 2 ? tc.Prop("mountains").GetInt32() : 100) / 100;
                    cost = forest[b] ? cost * tc.Prop("forest").GetInt32() / 100 : cost;
                }

                walk[(b * 3) + r] = cost;
            }
        }

        JsonElement research = root.Prop("research");
        JsonElement thinkers = root.Prop("thinkers");
        JsonElement advisors = root.Prop("advisors");
        var rarities = new List<Rarity>();
        foreach (JsonElement r in thinkers.Prop("rarities").EnumerateArray())
        {
            rarities.Add(new Rarity(
                rarities.Count, r.Prop("key").GetString()!, r.Prop("name").GetString()!, r.Prop("weight").GetInt32(),
                r.Prop("costPercent").GetInt32(), r.Prop("experiencePercent").GetInt32(), r.Prop("priceTimes").GetInt32()));
        }

        var fields = new List<string>();
        foreach (JsonElement f in advisors.Prop("fields").EnumerateArray())
        {
            fields.Add(f.GetString()!);
        }

        JsonElement seasons = root.TryProp("seasons", out JsonElement se) ? se : default;
        JsonElement clothing = root.TryProp("clothing", out JsonElement cl) ? cl : default;
        JsonElement forage = root.TryProp("forage", out JsonElement fo) ? fo : default;
        var forageBiome = new int[world.Biomes.Count * 2];
        var forageRelief = new int[3];
        var forageDeposit = new int[(world.Deposits.Count + 1) * goods.Count];
        if (forage.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in forage.Prop("biomes").Entries())
            {
                int bi = world.Biome(p.Name).Id;
                forageBiome[bi * 2] = p.Value[0].GetInt32();
                forageBiome[(bi * 2) + 1] = p.Value[1].GetInt32();
            }

            for (int r = 0; r < 3; r++)
            {
                forageRelief[r] = forage.Prop("relief")[r].GetInt32();
            }

            foreach (JsonProperty p in forage.Prop("deposits").Entries())
            {
                DepositDefinition dep = world.Deposits.First(x => x.Key == p.Name);
                foreach (JsonProperty gp in p.Value.Entries())
                {
                    forageDeposit[(dep.Id * goods.Count) + GoodOf(gp.Name)] = gp.Value.GetInt32();
                }
            }
        }

        JsonElement start = root.Prop("start");
        JsonElement food = root.Prop("food");
        JsonElement growth = root.Prop("growth");
        return new SimContent
        {
            World = world,
            Density = root.Prop("density").GetInt32(),
            EconomyEveryTicks = root.Prop("economyEveryTicks").GetInt32(),
            PopulationEveryTicks = root.Prop("populationEveryTicks").GetInt32(),
            Goods = goods,
            Levels = levels,
            Buildings = buildings,
            StartPeople = start.Prop("people").GetInt32(),
            StartStock = Amounts(start.Prop("stock")),
            FoodPerPersonPerMinute = Hundredths(food.Prop("foodPerPersonPerMinute")),
            ForagePerMinute = forage.TryProp("perMinute", out JsonElement fpm) ? Amounts(fpm) : new long[goods.Count],
            ForageSubsistencePercent = forage.TryProp("subsistencePercent", out JsonElement fsub) ? fsub.GetInt32() : 0,
            ForageBiome = forageBiome,
            ForageRelief = forageRelief,
            ForageDeposit = forageDeposit,
            Combat = root.TryProp("combat", out JsonElement cbt) ? ReadCombat(cbt) with { OpenBiomes = Flags(cbt, "openBiomes") } : new CombatRules(),
            WeaponGoods = [-1, .. new[] { "stone_weapons", "bronze_weapons", "iron_weapons", "steel_weapons" }.Select(k => goods.FindIndex(x => x.Key == k))],
            ArmourGoods = [-1, .. new[] { "hide_armour", "bronze_armour", "iron_armour", "steel_armour" }.Select(k => goods.FindIndex(x => x.Key == k))],
            ForageRiverFood = forage.TryProp("riverFood", out JsonElement frf) ? frf.GetInt32() : 0,
            SeasonCycleTicks = seasons.ValueKind == JsonValueKind.Object ? seasons.Prop("cycleMinutes").GetInt32() * TicksPerMinute : 0,
            WinterTicks = seasons.ValueKind == JsonValueKind.Object ? seasons.Prop("winterMinutes").GetInt32() * TicksPerMinute : 0,
            WinterWarnTicks = seasons.ValueKind == JsonValueKind.Object ? seasons.Prop("warnSeconds").GetInt32() * TicksPerSecond : 0,
            WinterWorkPercent = seasons.ValueKind == JsonValueKind.Object ? seasons.Prop("winterWorkPercent").GetInt32() : 100,
            WorkStepPermille = seasons.ValueKind == JsonValueKind.Object && seasons.TryProp("workStepPermille", out JsonElement wsp) ? Math.Max(1, wsp.GetInt32()) : 25,
            MaxResearchSupportPercent = research.TryProp("maxSupportPercent", out JsonElement msp) ? msp.GetInt32() : 50,
            Pace = research.TryProp("pace", out JsonElement pace)
                ? new ResearchPace(Ints(pace, "pricePercent"), Ints(pace, "experiencePercent"), Ints(pace, "secondsPercent"))
                : new ResearchPace([], [], []),
            StartPhase = root.TryProp("startPhase", out JsonElement sp)
                ? new StartPhaseRules(sp.Prop("pickSeconds").GetInt32() * TicksPerSecond, sp.Prop("blockRadius").GetInt32(), sp.Prop("blockFuzz").GetInt32(),
                    sp.Prop("scoreRadius").GetInt32(), sp.Prop("roomRadius").GetInt32())
                {
                    NeighbourMaxHexes = sp.TryProp("neighbourMaxHexes", out JsonElement nmh) ? nmh.GetInt32() : 0,
                    NeighbourMinHexes = sp.TryProp("neighbourMinHexes", out JsonElement nmn) ? nmn.GetInt32() : 0,
                }
                : new StartPhaseRules(300, 10, 3, 3, 6),
            Clothing = clothing.ValueKind == JsonValueKind.Object ? GoodOf(clothing.Prop("good").GetString()!) : -1,
            ClothingBonusPercent = clothing.ValueKind == JsonValueKind.Object ? clothing.Prop("bonusPercent").GetInt32() : 0,
            ClothingWinterMaxPercent = clothing.ValueKind == JsonValueKind.Object ? clothing.Prop("winterMaxPercent").GetInt32() : 100,
            ClothingWearPerPersonPerMinute = clothing.ValueKind == JsonValueKind.Object ? Hundredths(clothing.Prop("wearPerPersonPerMinute")) : 0,
            ClothingWearOnlyInWinter = clothing.ValueKind == JsonValueKind.Object && clothing.TryProp("wearOnlyInWinter", out JsonElement woiw) && woiw.GetBoolean(),
            InjuredPercent = root.Prop("war").TryProp("injuredPercent", out JsonElement inj) ? inj.GetInt32() : 0,
            RiversFreeEra = root.TryProp("rivers", out JsonElement rv) ? eras.FindIndex(e => e.Key == rv.Prop("freeFromEra").GetString()) is int rfe && rfe >= 0 ? rfe : int.MaxValue : int.MaxValue,
            GrowthPercentPerMinute = growth.Prop("growthPercentPerMinute").GetInt32(),
            MinGrowthPerMinute = growth.Prop("minGrowthPerMinute").GetInt32(),
            GrowthStopRations = growth.TryProp("stopBelowRations", out JsonElement sbr) ? sbr.GetInt32() : 2,
            StarvePercentPerMinute = growth.Prop("starvePercentPerMinute").GetInt32(),
            ResumeCostPercent = root.Prop("resumeCostPercent").GetInt32(),
            Rations = root.TryProp("rations", out JsonElement ra)
                ? new RationRules(ra.Prop("everySeconds").GetInt32() * TicksPerSecond, ra.Prop("hungryPercent").GetInt32(),
                    ra.Prop("starvingPercent").GetInt32(), ra.Prop("starveLosePercent").GetInt32())
                : new RationRules(600, 75, 50, 20),
            EraAdvanceTechs = root.TryProp("eraAdvanceTechs", out JsonElement eat) ? eat.GetInt32() : 5,
            StartDeposits = root.TryProp("startDeposits", out JsonElement sd)
                ? [.. sd.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => new StartDeposit(world.Deposits.First(dd => dd.Key == x.Prop("deposit").GetString()).Id,
                    x.Prop("min").GetInt32(), x.Prop("max").GetInt32(), x.TryProp("depth", out JsonElement dp) ? dp.GetInt32() : 1,
                    x.TryProp("size", out JsonElement sz) && root.TryProp("lodes", out JsonElement lz) ? lz.Prop("sizes").EnumerateArray().Select((e, k) => (e, k)).First(p => p.e.Prop("key").GetString() == sz.GetString()).k : -1))]
                : [],
            ArmyLimit = root.TryProp("armyLimit", out JsonElement limitEl)
                ? new ArmyLimitRules([.. limitEl.Prop("perLevel").EnumerateArray().Select(x => x.GetInt32())],
                    [.. limitEl.Prop("perBuilding").Entries().Select(p => (buildings.FindIndex(b => b.Key == p.Name), p.Value.GetInt32())).Where(x => x.Item1 > 0)],
                    [.. limitEl.Prop("perTech").Entries().Select(p => (techs.FindIndex(t => t.Key == p.Name), p.Value.GetInt32())).Where(x => x.Item1 >= 0)])
                : new ArmyLimitRules([1, 2, 4, 6], [], []),
            Veterans = root.TryProp("veterans", out JsonElement vetEl)
                ? new VeteranRules([.. vetEl.Prop("levels").EnumerateArray().Select(x => x.GetInt32())],
                    [.. vetEl.Prop("options").EnumerateArray().Select(o => new VeteranOption(o.Prop("key").GetString()!, o.Prop("name").GetString()!,
                        o.Prop("text").GetString()!, o.TryProp("strengthPercent", out JsonElement sp) ? sp.GetInt32() : 0,
                        o.TryProp("takenPercent", out JsonElement tk) ? tk.GetInt32() : 0, o.TryProp("speedPercent", out JsonElement spd) ? spd.GetInt32() : 0))])
                {
                    ShareNearHexes = vetEl.TryProp("shareNearHexes", out JsonElement snh) ? snh.GetInt32() : 0,
                }
                : new VeteranRules([], []),
            Supply = root.TryProp("supply", out JsonElement supplyEl)
                ? new SupplyRules(supplyEl.Prop("rangeHexes").GetInt32(), supplyEl.Prop("carryMinutes").GetInt32(),
                    supplyEl.Prop("landHands").GetInt32(), supplyEl.Prop("landPercent").GetInt32())
                {
                    NoWayTicks = (supplyEl.TryProp("noWaySeconds", out JsonElement nws) ? nws.GetInt32() : 60) * TicksPerSecond,
                }
                : new SupplyRules(3, 4, 10, 100),
            AiHomesFoodMinutes = root.TryProp("ai", out JsonElement aiEl) && aiEl.TryProp("homesFoodMinutes", out JsonElement hfm) ? hfm.GetInt32() : 3,
            Lodes = root.TryProp("lodes", out JsonElement lodeEl) ? new LodeRules
            {
                Sizes = [.. lodeEl.Prop("sizes").EnumerateArray().Select(e => new LodeSize(e.Prop("key").GetString()!, e.Prop("name").GetString()!,
                    e.Prop("percent").GetInt32(), e.Prop("weight").GetInt32(), e.Prop("regionWeight").GetInt32()))],
                Regional = [.. lodeEl.Prop("regional").Prop("deposits").EnumerateArray().Select(e => world.Deposits.First(dd => dd.Key == e.GetString()).Id)],
                Regions = lodeEl.Prop("regional").Prop("regions").GetInt32(),
                RegionHexes = lodeEl.Prop("regional").Prop("regionHexes").GetInt32(),
                OutsideKeepPercent = lodeEl.Prop("regional").Prop("outsideKeepPercent").GetInt32(),
                SignTech = techs.FindIndex(t => t.Key == lodeEl.Prop("signs").Prop("tech").GetString()),
                SignRadius = lodeEl.Prop("signs").Prop("signRadius").GetInt32(),
                OffsetHexes = lodeEl.Prop("signs").Prop("offsetHexes").GetInt32(),
                PerOre = lodeEl.Prop("signs").Prop("perOre").GetInt32(),
                ReachHexes = lodeEl.Prop("signs").Prop("reachHexes").GetInt32(),
                RecountTicks = lodeEl.Prop("signs").Prop("recountSeconds").GetInt32() * TicksPerSecond,
                EraOres = [.. eras.Select(era => lodeEl.Prop("signs").Prop("eras").TryProp(era.Key, out JsonElement list)
                    ? list.EnumerateArray().Select(e => world.Deposits.First(dd => dd.Key == e.GetString()).Id).ToArray() : Array.Empty<int>())],
            } : new LodeRules(),
            AiBattle = root.TryProp("aiBattle", out JsonElement abEl) ? new AiBattleRules
            {
                RetreatInjuredPercent = abEl.Prop("retreatInjuredPercent").GetInt32(),
                RetreatSizePercent = abEl.Prop("retreatSizePercent").GetInt32(),
                EngageHexes = abEl.Prop("engageHexes").GetInt32(),
                MusterHexes = abEl.Prop("musterHexes").GetInt32(),
                MusterFromHexes = abEl.Prop("musterFromHexes").GetInt32(),
                MusterPercent = abEl.Prop("musterPercent").GetInt32(),
            } : new AiBattleRules(),
            SettlementSightBeyond = root.TryProp("vision", out JsonElement visEl) ? visEl.Prop("settlementSightBeyond").GetInt32() : 1,
            AiWar = root.TryProp("aiWar", out JsonElement awEl) ? new AiWarRules
            {
                IntelMinutes = awEl.Prop("intelMinutes").GetInt32(),
                PowerTimes = awEl.Prop("powerTimes").GetInt32(),
                GainPercent = awEl.Prop("gainPercent").GetInt32(),
                SmallTargetPercent = awEl.Prop("smallTargetPercent").GetInt32(),
                SmallTargetPeople = awEl.Prop("smallTargetPeople").GetInt32(),
                FearArmyPercent = awEl.Prop("fearArmyPercent").GetInt32(),
                RandomPeacePercent = awEl.Prop("randomPeacePercent").GetInt32(),
                LosingScoreMargin = awEl.Prop("losingScoreMargin").GetInt32(),
                SurpriseAggression = awEl.Prop("surpriseAggression").GetInt32(),
            } : new AiWarRules(),
            AiResearchKeepFoodMinutes = root.TryProp("ai", out JsonElement aiElKeep) && aiElKeep.TryProp("researchKeepFoodMinutes", out JsonElement rkf) ? rkf.GetInt32() : 3,
            AiThinkEveryTicks = root.TryProp("ai", out JsonElement aiElThink) && aiElThink.TryProp("thinkEveryTicks", out JsonElement tet) ? Math.Max(1, tet.GetInt32()) : 30,
            AiWayToMinutes = root.TryProp("ai", out JsonElement aiElWay) && aiElWay.TryProp("wayToMinutes", out JsonElement wtm) ? wtm.GetInt32() : 2,
            AiCarePeople = root.TryProp("ai", out JsonElement aiElCare) && aiElCare.TryProp("careSettlementPeople", out JsonElement csp)
                ? csp.Entries().Where(e => e.Name != "comment").ToDictionary(e => e.Name, e => e.Value.GetInt32())
                : new Dictionary<string, int> { ["school"] = 40, ["bathhouse"] = 50, ["academy"] = 70, ["university"] = 90 },
            AiPeacefulStartMinutes = root.TryProp("ai", out JsonElement aiEl3) && aiEl3.TryProp("peacefulStartMinutes", out JsonElement psm) ? psm.GetInt32() : 15,
            AiSmallPeople = root.TryProp("ai", out JsonElement aiEl2) && aiEl2.TryProp("smallPeople", out JsonElement spp) ? spp.GetInt32() : 30,
            AiFoodMarginPercent = root.TryProp("ai", out JsonElement aiEl4) && aiEl4.TryProp("foodMarginPercent", out JsonElement fmp) ? fmp.GetInt32() : 30,
            AiFoodSitesPerPeople = root.TryProp("ai", out JsonElement aiEl5) && aiEl5.TryProp("foodSitesPerPeople", out JsonElement fsp) ? fsp.GetInt32() : 400,
            AiInputRouteTripMinutes = root.TryProp("ai", out JsonElement aiEl6) && aiEl6.TryProp("inputRouteTripMinutes", out JsonElement irt) ? irt.GetInt32() : 3,
            AiInputRouteTargetMinutes = root.TryProp("ai", out JsonElement aiEl7) && aiEl7.TryProp("inputRouteTargetMinutes", out JsonElement irg) ? irg.GetInt32() : 6,
            AiInputRouteKeepMinutes = root.TryProp("ai", out JsonElement aiEl8) && aiEl8.TryProp("inputRouteKeepMinutes", out JsonElement irk) ? irk.GetInt32() : 2,
            AiAmount = AiAmounts.Read(root.Prop("ai").Prop("amounts"), Hundredths),
            AiInputMakerStoreMinutes = root.TryProp("ai", out JsonElement aiEl10) && aiEl10.TryProp("inputMakerStoreMinutes", out JsonElement ims) ? ims.GetInt32() : 10,
            AiStarvedProspectors = root.TryProp("ai", out JsonElement aiEl11) && aiEl11.TryProp("starvedProspectors", out JsonElement asp) ? asp.GetInt32() : 2,
            AiInputRoutesPerSettlement = root.TryProp("ai", out JsonElement aiEl9) && aiEl9.TryProp("inputRoutesPerSettlement", out JsonElement irs) ? irs.GetInt32() : 4,
            NoResearchSeconds = root.TryProp("alerts", out JsonElement nrEl) && nrEl.TryProp("noResearchSeconds", out JsonElement nrs) ? nrs.GetInt32() : 60,
            FoodLowMinutes = root.TryProp("alerts", out JsonElement alEl) && alEl.TryProp("foodLowMinutes", out JsonElement flm) ? flm.GetInt32() : 3,
            UpgradeCaravans = root.TryProp("upgradeCaravans", out JsonElement ucEl)
                ? new UpgradeCaravanRules(ucEl.Prop("extraPercent").GetInt32(), ucEl.Prop("aiRetrySeconds").GetInt32() * TicksPerSecond)
                : new UpgradeCaravanRules(10, 60 * TicksPerSecond),
            Siege = root.TryProp("siege", out JsonElement siegeEl)
                ? new SiegeRules(siegeEl.Prop("blockadeRange").GetInt32(), siegeEl.Prop("blockadeWorkPercent").GetInt32(), siegeEl.Prop("liftSeconds").GetInt32(),
                    siegeEl.Prop("guardPerPalisadeHexPercent").GetInt32(), siegeEl.Prop("guardPerWallHexPercent").GetInt32(), siegeEl.Prop("guardStrength").GetInt32(),
                    siegeEl.Prop("guardRegrowPerMinute").GetInt32(), siegeEl.Prop("residentsFloorPercent").GetInt32(), siegeEl.Prop("sackPercent").GetInt32(),
                    siegeEl.Prop("sackImmuneSeconds").GetInt32(), siegeEl.Prop("choiceSeconds").GetInt32())
                {
                    AssaultAfterTicks = (siegeEl.TryProp("assaultAfterSeconds", out JsonElement aas) ? aas.GetInt32() : 0) * TicksPerSecond,
                    AiTakeWithinHexes = siegeEl.TryProp("aiTakeWithinHexes", out JsonElement atw) ? atw.GetInt32() : 20,
                }
                : new SiegeRules(2, 50, 30, 50, 100, 8, 3, 40, 50, 180, 20),
            RuinEmptySeconds = root.TryProp("ruins", out JsonElement ru) && ru.TryProp("emptySeconds", out JsonElement res) ? res.GetInt32() : 60,
            RuinRebuildPercent = root.TryProp("ruins", out JsonElement ru2) && ru2.TryProp("rebuildPercent", out JsonElement rrp) ? rrp.GetInt32() : 50,
            RuinKeepPercent = root.TryProp("ruins", out JsonElement ru3) && ru3.TryProp("razedKeepPercent", out JsonElement rkp) ? rkp.GetInt32() : 100,
            FoundWith = root.TryProp("foundWith", out JsonElement fw) ? [.. fw.EnumerateArray().Select(x => buildings.FindIndex(b => b.Key == x.GetString())).Where(i => i > 0)] : [],
            EraGateway = [.. eras.Select(e => root.TryProp("eraGateways", out JsonElement gw) && gw.TryProp(e.Key, out JsonElement gk)
                ? techs.FindIndex(t => t.Key == gk.GetString()) : -1)],
            EraEntryMade = [.. eras.Select(e => root.TryProp("eraEntryMade", out JsonElement em) && em.TryProp(e.Key, out JsonElement ek)
                ? Amounts(ek) : new long[goods.Count])],
            TechCategories = Strings(root, "techCategories"),
            Food = GoodOf("food"),
            Units = units,
            FoundMinDistance = root.Prop("foundSettlement").Prop("minDistance").GetInt32(),
            UpgradeCreditPercent = root.TryProp("upgradeCreditPercent", out JsonElement ucp) ? ucp.GetInt32() : 50,
            WalkCost = walk,
            Eras = eras,
            Techs = techs,
            NeighbourDiscountPercent = research.Prop("neighbourDiscountPercent").GetInt32(),
            MaxNeighbourDiscountPercent = research.Prop("maxNeighbourDiscountPercent").GetInt32(),
            Rarities = rarities,
            ThinkerRetireTicks = thinkers.Prop("retireMinutes").GetInt32() * TicksPerMinute,
            ThinkerOfferTicks = thinkers.Prop("offerMinutes").GetInt32() * TicksPerMinute,
            GenerationLossPercent = thinkers.Prop("generationLossPercent").GetInt32(),
            ThinkerPrice = Amounts(thinkers.Prop("hirePrice")),
            AdvisorOfferTicks = advisors.Prop("offerMinutes").GetInt32() * TicksPerMinute,
            AdvisorPrice = Amounts(advisors.Prop("hirePrice")),
            AdvisorUpkeep = Hundredths(advisors.Prop("upkeepFoodPerMinute")),
            AdvisorFields = fields,
            BuildingStorage = Hundredths(root.Prop("buildingStorage")),
            StoreFullPercent = root.Prop("stores").Prop("fullPercent").GetInt32(),
            StoreStillFullPercent = root.Prop("stores").Prop("stillFullPercent").GetInt32(),
            StoreStillEmptyBelow = Hundredths(root.Prop("stores").Prop("stillEmptyBelow")),
            Porters = units.FindIndex(x => x.Key == "porters"),
            Caravans = units.FindIndex(x => x.Key == "caravan"),
            TradeTech = techKeys.IndexOf(root.Prop("diplomacy").Prop("tradeTech").GetString()!),
            RoadTech = techKeys.IndexOf("roads"),
            RoadLevels = RoadLevelsOf(root, techKeys, Amounts),
            Works = WorksOf(root, techKeys, Amounts),
            GroveChance = GroveChanceOf(root, world),
            GroveWoodPercent = root.TryProp("trees", out JsonElement tr) && tr.TryProp("groveWoodPercent", out JsonElement gwp) ? gwp.GetInt32() : 60,
            SurveyRadius = root.TryProp("survey", out JsonElement svy) ? svy.Prop("radius").GetInt32() : 3,
            DepositSurvey = SurveyKinds(world, root),
            DepositEra = SurveyEras(world, root, eras),
            ForestBiomes = ForestOf(root, world),
            BridgeTech = root.TryProp("works", out JsonElement wb) && wb.TryProp("bridgeTech", out JsonElement bt) ? techKeys.IndexOf(bt.GetString()!) : -1,
            BridgePrice = root.TryProp("works", out JsonElement wbp) && wbp.TryProp("bridgePrice", out JsonElement bp2) ? Amounts(bp2) : new long[goods.Count],
            RoadReach = root.TryProp("roads", out JsonElement rr) ? rr.Prop("reach").GetInt32() : 25,
            RoadSupply = root.TryProp("roads", out JsonElement rs) ? Amounts(rs.Prop("supply")) : new long[goods.Count],
            Events = State.SimGame.LoadEvents(root, goods),
            TradeResearchDiscount = root.Prop("diplomacy").Prop("researchDiscountPerPartner").GetInt32(),
            AcceptWorthPercent = root.Prop("diplomacy").Prop("acceptWorthPercent").GetInt32(),
            TreatyKnownTicks = root.Prop("diplomacy").Prop("knownMinutes").GetInt32() * TicksPerMinute,
            TreatyBorderHexes = root.Prop("diplomacy").Prop("borderHexes").GetInt32(),
            TreatyPeaceTicks = root.Prop("diplomacy").Prop("peaceMinutes").GetInt32() * TicksPerMinute,
            TreatyMinReputation = root.Prop("diplomacy").Prop("minReputation").GetInt32(),
            OfferCooldownTicks = root.Prop("diplomacy").Prop("offerCooldownMinutes").GetInt32() * TicksPerMinute,
            RefusalCooldownTicks = root.Prop("diplomacy").Prop("refusalCooldownMinutes").GetInt32() * TicksPerMinute,
            EventEveryTicks = root.TryProp("events", out JsonElement evr) ? evr.Prop("everyMinutes").GetInt32() * TicksPerMinute : 0,
            EventSpreadTicks = root.TryProp("events", out JsonElement evs) ? evs.Prop("spreadMinutes").GetInt32() * TicksPerMinute : 0,
            War = new WarRules(
                root.Prop("war").Prop("combatEveryTicks").GetInt32(), root.Prop("war").Prop("lowLuckPercent").GetInt32(),
                root.Prop("war").Prop("highLuckPercent").GetInt32(), root.Prop("war").Prop("defenderPercent").GetInt32(),
                root.Prop("war").Prop("defenderStrength").GetInt32(), root.Prop("war").Prop("captureBelowPeople").GetInt32(),
                root.Prop("war").Prop("surpriseReputation").GetInt32())
            {
                DeclareReputation = root.Prop("war").TryProp("declareReputation", out JsonElement dr) ? dr.GetInt32() : -10,
                TributeNeedsKills = root.Prop("war").TryProp("tributeNeedsKills", out JsonElement tk) ? tk.GetInt32() : 5,
                TributeFoodMaxPercent = root.Prop("war").TryProp("tributeFoodMaxPercent", out JsonElement tf) ? tf.GetInt32() : 10,
                MaxWars = root.Prop("war").TryProp("maxWars", out JsonElement mw) ? mw.GetInt32() : 3,
                TributeAfterTicks = (root.Prop("war").TryProp("tributeAfterSeconds", out JsonElement tas) ? tas.GetInt32() : 120) * TicksPerSecond,
                ReputationFloor = root.Prop("war").TryProp("reputationFloor", out JsonElement rf) ? rf.GetInt32() : -200,
                BadReputation = root.Prop("war").TryProp("badReputation", out JsonElement br) ? br.GetInt32() : -100,
                ReputationRegenPerMinute = root.Prop("war").TryProp("reputationRegenPerMinute", out JsonElement rrm) ? rrm.GetInt32() : 0,
            },
            Peace = root.TryProp("peace", out JsonElement peaceEl) ? new PeaceRules
            {
                Minutes = Ints(peaceEl, "minutes"),
                BreakPercent = Ints(peaceEl, "breakPercent"),
                SettlementHexes = peaceEl.Prop("settlementHexes").GetInt32(),
                SettlementWorthPerPerson = peaceEl.Prop("settlementWorthPerPerson").GetInt32(),
                SettlementBuildingPercent = peaceEl.Prop("settlementBuildingPercent").GetInt32(),
                ScorePerPercent = Math.Max(1, peaceEl.Prop("scorePerPercent").GetInt32()),
                WearinessPerPercent = Math.Max(1, peaceEl.Prop("wearinessPerPercent").GetInt32()),
                MaxPercent = peaceEl.Prop("maxPercent").GetInt32(),
                AggressorExtraPercent = peaceEl.Prop("aggressorExtraPercent").GetInt32(),
                AiGiveMarginPercent = peaceEl.Prop("aiGiveMarginPercent").GetInt32(),
                AiLosingLengthPercent = Ints(peaceEl, "aiLosingLengthPercent"),
                AiWinningLengthPercent = Ints(peaceEl, "aiWinningLengthPercent"),
            } : new PeaceRules(),
            March = root.TryProp("march", out JsonElement mEl)
                ? new MarchRules(mEl.Prop("shareSeconds").GetInt32() * TicksPerSecond, mEl.Prop("shareHexes").GetInt32(), mEl.Prop("joinSearch").GetInt32())
                : new MarchRules(),
            Weariness = root.TryProp("weariness", out JsonElement wEl) ? new WearinessRules
            {
                PerMinute = wEl.Prop("perMinute").GetInt32(),
                PerLostPercent = wEl.Prop("perLostPercent").GetInt32(),
                HungerPerMinute = wEl.Prop("hungerPerMinute").GetInt32(),
                BesiegedPerMinute = wEl.Prop("besiegedPerMinute").GetInt32(),
                Sacked = wEl.Prop("sacked").GetInt32(),
                Taken = wEl.Prop("taken").GetInt32(),
                CaravanRaided = wEl.Prop("caravanRaided").GetInt32(),
                PeaceFallPerMinute = wEl.Prop("peaceFallPerMinute").GetInt32(),
                WorkFrom = wEl.Prop("workFrom").GetInt32(),
                WorkMaxLossPercent = wEl.Prop("workMaxLossPercent").GetInt32(),
                AiPeaceAt = wEl.Prop("aiPeaceAt").GetInt32(),
                AiPayAt = wEl.Prop("aiPayAt").GetInt32(),
            } : new WearinessRules(),
        };
    }

    private static int[] GroveChanceOf(JsonElement root, GameContent world)
    {
        var chance = new int[world.Biomes.Count];
        if (root.TryProp("trees", out JsonElement trees) && trees.TryProp("groveChance", out JsonElement gc))
        {
            foreach (JsonProperty p in gc.Entries())
            {
                chance[world.Biome(p.Name).Id] = p.Value.GetInt32();
            }
        }

        return chance;
    }

    private static bool[] ForestOf(JsonElement root, GameContent world)
    {
        var forest = new bool[world.Biomes.Count];
        foreach (JsonElement k in root.Prop("terrainCost").Prop("forestBiomes").EnumerateArray())
        {
            forest[world.Biome(k.GetString()!).Id] = true;
        }

        return forest;
    }

    private static List<WorkKind> WorksOf(JsonElement root, List<string> techKeys, Func<JsonElement, long[]> amounts)
    {
        var list = new List<WorkKind>();
        if (!root.TryProp("works", out JsonElement works))
        {
            return list;
        }

        foreach (JsonElement w in works.Prop("kinds").EnumerateArray())
        {
            int Int(string name) => w.TryProp(name, out JsonElement e) ? e.GetInt32() : 0;
            string tech = w.Prop("tech").GetString()!;
            int ti = techKeys.IndexOf(tech);
            list.Add(new WorkKind(list.Count, w.Prop("key").GetString()!, w.Prop("name").GetString()!,
                ti >= 0 ? ti : throw new InvalidDataException($"sim/game.json: unknown technology '{tech}'."), amounts(w.Prop("price")),
                Int("seconds") * TicksPerSecond)
            {
                FromWater = Int("fromWater"),
                FarmPercent = Int("farmPercent"),
                WoundPercentPerSecond = Int("woundPercentPerSecond"),
                Strength = Int("strength") * One,
                GrowthBonus = Int("growthBonus"),
                ResistEpidemicPercent = Int("resistEpidemicPercent"),
            });
        }

        return list;
    }

    // Decimal text → hundredths without floating point (the JSON number's raw text is parsed).
    private static List<RoadLevel> RoadLevelsOf(JsonElement root, List<string> techKeys, Func<JsonElement, long[]> amounts)
    {
        var list = new List<RoadLevel>();
        if (!root.TryProp("roads", out JsonElement roads))
        {
            return list;
        }

        foreach (JsonElement l in roads.Prop("levels").EnumerateArray())
        {
            list.Add(new RoadLevel(list.Count + 1, l.Prop("key").GetString()!, l.Prop("name").GetString()!,
                Math.Max(0, techKeys.IndexOf(l.Prop("tech").GetString()!)), amounts(l.Prop("price")),
                l.Prop("seconds").GetInt32() * TicksPerSecond, l.Prop("walkPercent").GetInt32()));
        }

        return list;
    }

    private static long Hundredths(JsonElement e)
    {
        string text = e.GetRawText();
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return long.Parse(text, CultureInfo.InvariantCulture) * One;
        }

        string frac = (text[(dot + 1)..] + "00")[..2];
        long whole = long.Parse(text[..dot], CultureInfo.InvariantCulture);
        long f = long.Parse(frac, CultureInfo.InvariantCulture);
        return (whole * One) + (text.StartsWith('-') ? -f : f);
    }
}

/// <summary>How the AI fights (roadmap 7, 2026-11-21).</summary>
public sealed record AiBattleRules
{
    /// <summary>A unit at war with this share of its people wounded (percent) goes home to heal.</summary>
    public int RetreatInjuredPercent { get; init; } = 50;

    /// <summary>A unit at war with fewer people than this share of its size (percent) goes home and guards it.</summary>
    public int RetreatSizePercent { get; init; } = 30;

    /// <summary>A seen enemy army this close (hexes) to a soldier of ours is fought first.</summary>
    public int EngageHexes { get; init; } = 5;

    /// <summary>Soldiers this close to the muster (hexes) are there.</summary>
    public int MusterHexes { get; init; } = 3;

    /// <summary>Soldiers farther than this from the muster (hexes) are not waited for.</summary>
    public int MusterFromHexes { get; init; } = 30;

    /// <summary>Share of the power (percent) that must stand at the muster before the march.</summary>
    public int MusterPercent { get; init; } = 70;
}

/// <summary>
/// When the AI starts a war (tests 2026-11-23, P1): it looks at the target first, judges the gain against the cost and spares
/// small peoples; a people with a bad name makes it careful, not warlike (P2).
/// </summary>
public sealed record AiWarRules
{
    /// <summary>A target seen within this many minutes counts as scouted; an older memory sends a scout to look first.</summary>
    public int IntelMinutes { get; init; } = 3;

    /// <summary>Our power must be this many times the target's defence as we know it.</summary>
    public int PowerTimes { get; init; } = 2;

    /// <summary>The target settlement's people must be at least this share (percent) of the soldiers we would send: the gain.</summary>
    public int GainPercent { get; init; } = 60;

    /// <summary>A people whose known people is below this share (percent) of ours is not attacked first (small peoples).</summary>
    public int SmallTargetPercent { get; init; } = 40;

    /// <summary>A people with fewer known people than this is never attacked first (unless its name is bad).</summary>
    public int SmallTargetPeople { get; init; } = 40;

    /// <summary>The AI keeps this much more army (percent) while a people it met has a bad name (badReputation).</summary>
    public int FearArmyPercent { get; init; } = 50;

    /// <summary>The AI at war offers peace now and then: this chance (percent) a thought.</summary>
    public int RandomPeacePercent { get; init; } = 3;

    /// <summary>The AI offers peace when the enemy's war score is this much above its own.</summary>
    public int LosingScoreMargin { get; init; } = 20;

    /// <summary>Only the most warlike (aggression at least this) attack without declaring war.</summary>
    public int SurpriseAggression { get; init; } = 75;
}

/// <summary>Amounts the AI decides by (ai.amounts in game.json, whole units there, hundredths here); see the comment in the data.</summary>
public sealed class AiAmounts
{
    public long LowFood { get; init; } = 3 * SimContent.One;

    public long LowGood { get; init; } = 20 * SimContent.One;

    public long LowTools { get; init; } = 10 * SimContent.One;

    public long FoodSourceKeep { get; init; } = 60 * SimContent.One;

    public long FoodSendAtLeast { get; init; } = 40 * SimContent.One;

    public long ShareLow { get; init; } = 10 * SimContent.One;

    public long ShareKeep { get; init; } = 20 * SimContent.One;

    public long ShareMargin { get; init; } = 5 * SimContent.One;

    public long ShareTrip { get; init; } = 30 * SimContent.One;

    public long RouteSourceLow { get; init; } = 10 * SimContent.One;

    public long UnitSupplyHomeKeep { get; init; } = 40 * SimContent.One;

    public long WarNetFood { get; init; } = 2 * SimContent.One;

    internal static AiAmounts Read(System.Text.Json.JsonElement e, Func<System.Text.Json.JsonElement, long> hundredths) => new()
    {
        LowFood = hundredths(e.Prop("lowFood")),
        LowGood = hundredths(e.Prop("lowGood")),
        LowTools = hundredths(e.Prop("lowTools")),
        FoodSourceKeep = hundredths(e.Prop("foodSourceKeep")),
        FoodSendAtLeast = hundredths(e.Prop("foodSendAtLeast")),
        ShareLow = hundredths(e.Prop("shareLow")),
        ShareKeep = hundredths(e.Prop("shareKeep")),
        ShareMargin = hundredths(e.Prop("shareMargin")),
        ShareTrip = hundredths(e.Prop("shareTrip")),
        RouteSourceLow = hundredths(e.Prop("routeSourceLow")),
        UnitSupplyHomeKeep = hundredths(e.Prop("unitSupplyHomeKeep")),
        WarNetFood = hundredths(e.Prop("warNetFood")),
    };
}
