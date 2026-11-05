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
    [property: Key(29)] int StarCycleYears = 100_000);

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
    [property: Key(7)] int SeaLevel = 100);

/// <summary>Raw bytes of one map layer, identified by name.</summary>
[MessagePackObject]
public sealed record MapLayerSaveData([property: Key(0)] string Name, [property: Key(1)] byte[] Bytes);

/// <summary>Saved civilization. Controllers are not saved: on load every civilization starts AI-controlled until players rejoin.</summary>
[MessagePackObject]
public sealed record CivilizationSaveData([property: Key(0)] int Id, [property: Key(1)] string Name, [property: Key(2)] bool IsAlive,
    [property: Key(3)] int StartCol,
    [property: Key(4)] int StartRow,
    [property: Key(5)] int Era = 0,
    [property: Key(6)] long DiedOutMonth = 0);

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
    [property: Key(20)] long ClaimUntilMonth = 0);

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
