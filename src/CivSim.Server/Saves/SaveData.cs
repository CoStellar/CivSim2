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
    [property: Key(13)] int Resources);

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
    [property: Key(4)] int StartRow);
