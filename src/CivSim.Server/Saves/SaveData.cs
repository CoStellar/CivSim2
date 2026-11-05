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
    [property: Key(10)] int Climate,
    [property: Key(11)] int Moisture,
    [property: Key(12)] int Relief,
    [property: Key(13)] int Resources,
    [property: Key(14)] int Inland,
    [property: Key(15)] long Month = 0,
    [property: Key(16)] int NextSettlementId = 0,
    [property: Key(17)] IReadOnlyList<SettlementSaveData>? Settlements = null,
    [property: Key(18)] long NextEffectId = 0,
    [property: Key(19)] IReadOnlyList<EffectSaveData>? Effects = null);

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
    [property: Key(4)] IReadOnlyList<MapLayerSaveData> Layers);

/// <summary>Raw bytes of one map layer, identified by name.</summary>
[MessagePackObject]
public sealed record MapLayerSaveData([property: Key(0)] string Name, [property: Key(1)] byte[] Bytes);

/// <summary>Saved civilization. Controllers are not saved: on load every civilization starts AI-controlled until players rejoin.</summary>
[MessagePackObject]
public sealed record CivilizationSaveData([property: Key(0)] int Id, [property: Key(1)] string Name, [property: Key(2)] bool IsAlive,
    [property: Key(3)] int StartCol,
    [property: Key(4)] int StartRow,
    [property: Key(5)] int Era = 0);

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
    [property: Key(8)] long[] Stock);

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
