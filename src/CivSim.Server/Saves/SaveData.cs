using System.Collections.Generic;
using MessagePack;

namespace CivSim.Server.Saves;

/// <summary>On-disk form of a saved game. Keys are part of the save format: only append new keys.</summary>
[MessagePackObject]
public sealed record SaveData(
    [property: Key(0)] ulong Seed,
    [property: Key(1)] int MapWidth,
    [property: Key(2)] int MapHeight,
    [property: Key(3)] int CivilizationCount,
    [property: Key(4)] long Turn,
    [property: Key(5)] IReadOnlyList<RandomStateData> RandomStreams,
    [property: Key(6)] IReadOnlyList<WorldSaveData> Worlds,
    [property: Key(7)] IReadOnlyList<CivilizationSaveData> Civilizations,
    [property: Key(8)] int WorldType,
    [property: Key(9)] int WaterPercent,
    [property: Key(10)] int UnusedClimate,
    [property: Key(11)] int Moisture,
    [property: Key(12)] int Relief,
    [property: Key(13)] int Resources,
    [property: Key(14)] int Inland,
    [property: Key(15)] long Month = 0,
    [property: Key(16)] int NextSettlementId = 0,
    [property: Key(17)] IReadOnlyList<SettlementSaveData>? Settlements = null,
    [property: Key(18)] long NextEffectId = 0,
    [property: Key(19)] IReadOnlyList<EffectSaveData>? Effects = null,
    [property: Key(20)] int AxialTiltTenths = 234,
    [property: Key(21)] int LuminosityThousandths = 1000,
    [property: Key(22)] int DistanceThousandths = 1000,
    [property: Key(23)] int EccentricityThousandths = 17,
    [property: Key(24)] string PlanetName = "Earth",
    [property: Key(25)] int Co2Microbar = 280,
    [property: Key(26)] int BackgroundPressureHundredths = 100,
    [property: Key(27)] int VolcanismPercent = 100,
    [property: Key(28)] int StarVariabilityPerMille = 0,
    [property: Key(29)] int StarCycleYears = 100_000,
    [property: Key(30)] IReadOnlyList<TreatySaveData>? Treaties = null,
    [property: Key(31)] IReadOnlyList<ProposalSaveData>? Proposals = null,
    [property: Key(32)] int StepMonths = 0,
    [property: Key(33)] int WorldMonths = 0,
    [property: Key(34)] IReadOnlyList<EpisodeSaveData>? Episodes = null,
    [property: Key(35)] long NextEpisodeId = 0);

/// <summary>Saved treaty or war between two civilizations (stage 4d).</summary>
[MessagePackObject]
public sealed record TreatySaveData(
    [property: Key(0)] int A,
    [property: Key(1)] int B,
    [property: Key(2)] byte Kind,
    [property: Key(3)] int Initiator,
    [property: Key(4)] long SinceMonth,
    [property: Key(5)] long UntilMonth,
    [property: Key(6)] int GatewayA,
    [property: Key(7)] int GatewayB,
    [property: Key(8)] long LastFlowMonth,
    [property: Key(9)] long LastWorth,
    [property: Key(10)] long TotalWorth,
    [property: Key(11)] byte Goal = 0,
    [property: Key(12)] int GoalSettlement = -1,
    [property: Key(13)] int Score = 0,
    [property: Key(14)] int DeadA = 0,
    [property: Key(15)] int DeadB = 0,
    [property: Key(16)] int PeopleA = 0,
    [property: Key(17)] int PeopleB = 0,
    [property: Key(18)] int WearinessA = 0,
    [property: Key(19)] int WearinessB = 0,
    [property: Key(20)] PeaceTermsSaveData? Terms = null,
    [property: Key(21)] int Campaigns = 0,
    [property: Key(22)] int CampaignMonths = 0,
    [property: Key(23)] long LastCampaignMonth = -1,
    [property: Key(24)] long ReadyMonth = -1);

/// <summary>Saved episode (ADR 0022): today a war campaign.</summary>
[MessagePackObject]
public sealed record EpisodeSaveData(
    [property: Key(0)] long Id,
    [property: Key(1)] byte Kind,
    [property: Key(2)] int Civ,
    [property: Key(3)] int Other,
    [property: Key(4)] long StartMonth,
    [property: Key(5)] long NextMonth,
    [property: Key(6)] int TickMonths,
    [property: Key(7)] int Home,
    [property: Key(8)] int Target,
    [property: Key(9)] int Warriors,
    [property: Key(10)] int Cell,
    [property: Key(11)] byte Phase,
    [property: Key(12)] int Dead,
    [property: Key(13)] long[] Loot,
    [property: Key(14)] int GoalTarget = -1,
    [property: Key(15)] int[]? Members = null,
    [property: Key(16)] int[]? MemberWarriors = null,
    [property: Key(17)] int Mustered = 0,
    [property: Key(18)] int ArmsPercent = 100,
    [property: Key(19)] int Morale = 100,
    [property: Key(20)] long MusterUntilMonth = 0,
    [property: Key(21)] long[]? Food = null,
    [property: Key(22)] int HungryMonths = 0,
    [property: Key(23)] int Forays = 0,
    [property: Key(24)] bool Foraging = false,
    [property: Key(25)] int ConvoyFrom = -1,
    [property: Key(26)] int ConvoyCell = 0,
    [property: Key(27)] int ConvoyCarriers = 0,
    [property: Key(28)] long[]? ConvoyFood = null,
    [property: Key(29)] int Convoys = 0,
    [property: Key(30)] int LastPrey = -1,
    [property: Key(31)] int Battles = 0,
    [property: Key(32)] int Wins = 0,
    [property: Key(33)] int Killed = 0);

/// <summary>Saved terms of a peace (stage 4e).</summary>
[MessagePackObject]
public sealed record PeaceTermsSaveData(
    [property: Key(0)] byte Kind,
    [property: Key(1)] int Giver,
    [property: Key(2)] int Settlement,
    [property: Key(3)] int RansomPercent);

/// <summary>Saved treaty proposal (stage 4d).</summary>
[MessagePackObject]
public sealed record ProposalSaveData(
    [property: Key(0)] int From,
    [property: Key(1)] int To,
    [property: Key(2)] byte Kind,
    [property: Key(3)] long Month,
    [property: Key(4)] byte Status,
    [property: Key(5)] PeaceTermsSaveData? Terms = null);

/// <summary>Saved state of one random stream.</summary>
[MessagePackObject]
public sealed record RandomStateData([property: Key(0)] ulong S0, [property: Key(1)] ulong S1, [property: Key(2)] ulong S2, [property: Key(3)] ulong S3);

/// <summary>Saved world.</summary>
[MessagePackObject]
public sealed record WorldSaveData(
    [property: Key(0)] int Id,
    [property: Key(1)] string Name,
    [property: Key(2)] int Width,
    [property: Key(3)] int Height,
    [property: Key(4)] IReadOnlyList<MapLayerSaveData> Layers,
    [property: Key(5)] ClimateSaveData? Climate = null);

/// <summary>Saved slow climate of a world (stage 1c-B).</summary>
[MessagePackObject]
public sealed record ClimateSaveData(
    [property: Key(0)] long Year,
    [property: Key(1)] int NorthRegime,
    [property: Key(2)] int SouthRegime,
    [property: Key(3)] long NorthIce,
    [property: Key(4)] long SouthIce,
    [property: Key(5)] int Co2Microbar,
    [property: Key(6)] long GlobalMeanMillionths,
    [property: Key(7)] int SeaLevel = 100,
    [property: Key(8)] int StartLandIce = 0);

/// <summary>Raw bytes of one map layer, identified by name.</summary>
[MessagePackObject]
public sealed record MapLayerSaveData([property: Key(0)] string Name, [property: Key(1)] byte[] Bytes);

/// <summary>Saved civilization. Controllers are not saved: on load every civilization starts AI-controlled until players rejoin.</summary>
[MessagePackObject]
public sealed record CivilizationSaveData([property: Key(0)] int Id, [property: Key(1)] string Name, [property: Key(2)] bool IsAlive,
    [property: Key(3)] int StartCol,
    [property: Key(4)] int StartRow,
    [property: Key(5)] int Era = 0,
    [property: Key(6)] long DiedOutMonth = 0,
    [property: Key(7)] bool[]? KnownTechnologies = null,
    [property: Key(8)] int Researching = -1,
    [property: Key(9)] long ResearchProgress = 0,
    [property: Key(10)] long LastScience = 0,
    [property: Key(11)] int[]? SeenYear = null,
    [property: Key(12)] int[]? Traits = null,
    [property: Key(13)] int[]? GoalWeights = null,
    [property: Key(14)] int ExpansionTarget = -1,
    [property: Key(15)] long StrategyReviewMonth = 0,
    [property: Key(16)] int StrategyEra = -1,
    [property: Key(17)] List<RelationSaveData>? Relations = null,
    [property: Key(18)] long NextStepMonth = 0,
    [property: Key(19)] long StepStartMonth = 0,
    [property: Key(20)] int StepMonths = 0,
    [property: Key(21)] long Steps = 0,
    [property: Key(22)] byte Alarms = 0,
    [property: Key(23)] ulong LanguageSeed = 0,
    [property: Key(24)] long NamesIssued = 0,
    [property: Key(25)] int Government = 0,
    [property: Key(26)] int PendingGovernment = -1,
    [property: Key(27)] long PendingGovernmentMonth = 0,
    [property: Key(28)] string LeaderName = "",
    [property: Key(29)] long LeaderBornMonth = 0,
    [property: Key(30)] long LeaderSinceMonth = 0,
    [property: Key(31)] int[]? LeaderTraits = null,
    [property: Key(32)] int[]? LeaderShift = null,
    [property: Key(33)] long Leaders = 0,
    [property: Key(34)] long CrisisUntilMonth = 0,
    [property: Key(35)] int Capital = -1,
    [property: Key(36)] int Stability = 50,
    [property: Key(37)] int AdminUsed = 0,
    [property: Key(38)] long AnarchyUntilMonth = 0,
    [property: Key(39)] long CapitalLostUntilMonth = 0,
    [property: Key(40)] int ParentCiv = -1,
    [property: Key(41)] long FoundedMonth = 0,
    [property: Key(42)] long RevoltCalmUntilMonth = 0,
    [property: Key(43)] ulong[]? LanguageMutations = null);

/// <summary>Saved relation of a civilization with another (stage 4c).</summary>
[MessagePackObject]
public sealed record RelationSaveData(
    [property: Key(0)] int Other,
    [property: Key(1)] byte Level,
    [property: Key(2)] byte Way,
    [property: Key(3)] long FirstMonth,
    [property: Key(4)] long MetMonth,
    [property: Key(5)] long LastContactMonth,
    [property: Key(6)] int Opening,
    [property: Key(7)] int Sympathy,
    [property: Key(8)] int Trust,
    [property: Key(9)] int RumorCell,
    [property: Key(10)] int RumorPeople,
    [property: Key(11)] int RumorFrom,
    [property: Key(12)] List<RelationMemorySaveData> Memories);

/// <summary>Saved remembered event of a relation (stage 4c).</summary>
[MessagePackObject]
public sealed record RelationMemorySaveData(
    [property: Key(0)] byte Kind,
    [property: Key(1)] int Sympathy,
    [property: Key(2)] int Trust,
    [property: Key(3)] long EndMonth,
    [property: Key(4)] int FadeMonths);

/// <summary>Saved settlement. <see cref="Stock"/> holds hundredths per good id.</summary>
[MessagePackObject]
public sealed record SettlementSaveData(
    [property: Key(0)] int Id,
    [property: Key(1)] int Owner,
    [property: Key(2)] int World,
    [property: Key(3)] int Col,
    [property: Key(4)] int Row,
    [property: Key(5)] string Name,
    [property: Key(6)] int Level,
    [property: Key(7)] long FoundedTurn,
    [property: Key(8)] long[] Stock,
    [property: Key(9)] int[]? Population = null,
    [property: Key(10)] int Health = 100,
    [property: Key(11)] int Mood = 50,
    [property: Key(12)] long FoodGathered = 0,
    [property: Key(13)] long FoodNeeded = 0,
    [property: Key(14)] long FoodEaten = 0,
    [property: Key(15)] int HigherNeedsPerMille = 0,
    [property: Key(16)] long MovedMonth = 0,
    [property: Key(17)] int Status = 0,
    [property: Key(18)] long AbandonedMonth = 0,
    [property: Key(19)] int ClaimCiv = -1,
    [property: Key(20)] long ClaimUntilMonth = 0,
    [property: Key(21)] int RecipeId = -1,
    [property: Key(22)] long RunsMade = 0,
    [property: Key(23)] int JourneyTarget = -1,
    [property: Key(24)] int[]? Skilled = null,
    [property: Key(25)] int[]? Trainees = null,
    [property: Key(26)] int Loyalty = 50);

/// <summary>Saved active effect.</summary>
[MessagePackObject]
public sealed record EffectSaveData(
    [property: Key(0)] long Id,
    [property: Key(1)] int Definition,
    [property: Key(2)] int Target,
    [property: Key(3)] int World,
    [property: Key(4)] int TargetId,
    [property: Key(5)] int Strength,
    [property: Key(6)] long EndMonth);
