using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Protocol.Messages;
using MessagePack;

namespace CivSim.Server.Replays;

/// <summary>What a replay event does (T11).</summary>
public enum ReplayEventKind : byte
{
    /// <summary>A player's command, queued for the tick (as <see cref="GameServer"/> queues it).</summary>
    Command = 0,

    /// <summary>A rename an observer made between ticks (applied at once, GDD 5.6).</summary>
    Rename = 1,

    /// <summary>A civilization changed hands: a player took it, or the AI took it back (<see cref="ReplayEvent.Player"/> 0).</summary>
    Control = 2,
}

/// <summary>
/// One thing from outside the simulation that changed it (T11): a command, a rename or a change of control, with the tick
/// before which it happened. Everything else (the AI, the world) follows from the seed.
/// </summary>
[MessagePackObject]
public sealed record ReplayEvent(
    [property: Key(0)] long Tick,
    [property: Key(1)] ReplayEventKind Kind,
    [property: Key(2)] int Player,
    [property: Key(3)] int Civ,
    [property: Key(4)] ICommandDto? Command);

/// <summary>The state hash at a tick, whole and per part (<see cref="Core.Hashing.GameStateHash.Sections"/>).</summary>
[MessagePackObject]
public sealed record ReplayCheckpoint(
    [property: Key(0)] long Tick,
    [property: Key(1)] ulong Hash,
    [property: Key(2)] ulong[] Sections);

/// <summary>The settings a new game is generated from (seed, map, planet, world).</summary>
[MessagePackObject]
public sealed record ReplaySettings(
    [property: Key(0)] ulong Seed,
    [property: Key(1)] int MapWidth,
    [property: Key(2)] int MapHeight,
    [property: Key(3)] int CivilizationCount,
    [property: Key(4)] int WorldType,
    [property: Key(5)] int WaterPercent,
    [property: Key(6)] int AxialTiltTenths,
    [property: Key(7)] int EccentricityThousandths,
    [property: Key(8)] int LuminosityThousandths,
    [property: Key(9)] int DistanceThousandths,
    [property: Key(10)] int Co2Microbar,
    [property: Key(11)] int BackgroundPressureHundredths,
    [property: Key(12)] int VolcanismPercent,
    [property: Key(13)] int StarVariabilityPerMille,
    [property: Key(14)] int StarCycleYears,
    [property: Key(15)] string PlanetName,
    [property: Key(16)] int Moisture,
    [property: Key(17)] int Inland,
    [property: Key(18)] int Relief,
    [property: Key(19)] int Resources)
{
    /// <summary>The settings of <paramref name="s"/>.</summary>
    public static ReplaySettings From(GameSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);
        PlanetParameters p = s.Planet;
        return new ReplaySettings(s.Seed, s.MapWidth, s.MapHeight, s.CivilizationCount, (int)s.WorldType, s.WaterPercent,
            p.AxialTiltTenths, p.EccentricityThousandths, p.LuminosityThousandths, p.DistanceThousandths, p.Co2Microbar,
            p.BackgroundPressureHundredths, p.VolcanismPercent, p.StarVariabilityPerMille, p.StarCycleYears, s.PlanetName,
            (int)s.Moisture, (int)s.Inland, (int)s.Relief, (int)s.Resources);
    }

    /// <summary>The game settings again.</summary>
    public GameSettings ToSettings() => new(Seed, MapWidth, MapHeight, CivilizationCount)
    {
        WorldType = (WorldType)WorldType,
        WaterPercent = WaterPercent,
        Planet = new PlanetParameters(AxialTiltTenths, EccentricityThousandths, LuminosityThousandths, DistanceThousandths,
            Co2Microbar, BackgroundPressureHundredths, VolcanismPercent)
        {
            StarVariabilityPerMille = StarVariabilityPerMille,
            StarCycleYears = StarCycleYears,
        },
        PlanetName = PlanetName,
        Moisture = (WorldMoisture)Moisture,
        Inland = (WorldInland)Inland,
        Relief = (WorldRelief)Relief,
        Resources = (WorldResources)Resources,
    };
}

/// <summary>
/// A recorded game (T11): how it began (the settings of a new game, or a save it was loaded from), every event from outside
/// the simulation, and state hashes every few ticks to find where a replay drifts apart.
/// </summary>
[MessagePackObject]
public sealed record ReplayData(
    [property: Key(0)] int ProtocolVersion,
    [property: Key(1)] int SaveFormat,
    [property: Key(2)] ReplaySettings Settings,
    [property: Key(3)] byte[]? StartSave,
    [property: Key(4)] long StartTick,
    [property: Key(5)] long EndTick,
    [property: Key(6)] int CheckpointTicks,
    [property: Key(7)] string[] SectionNames,
    [property: Key(8)] List<ReplayEvent> Events,
    [property: Key(9)] List<ReplayCheckpoint> Checkpoints);

/// <summary>
/// Writes and reads replay files: the 4-byte magic "CS2R", a 4-byte format version (little-endian), then the MessagePack
/// body (LZ4-compressed).
/// </summary>
public static class ReplaySerializer
{
    /// <summary>Current replay file format.</summary>
    public const int FormatVersion = 1;

    private static readonly byte[] Magic = "CS2R"u8.ToArray();

    private static readonly MessagePackSerializerOptions Options = MessagePackSerializerOptions.Standard
        .WithCompression(MessagePackCompression.Lz4BlockArray)
        .WithSecurity(MessagePackSecurity.UntrustedData);

    /// <summary>Writes <paramref name="data"/> to <paramref name="stream"/>.</summary>
    public static void Write(ReplayData data, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[8];
        Magic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], FormatVersion);
        stream.Write(header);
        MessagePackSerializer.Serialize(stream, data, Options);
    }

    /// <summary>Reads a replay. Throws <see cref="InvalidDataException"/> for foreign, newer or damaged files.</summary>
    public static ReplayData Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);
        if (!header[..4].SequenceEqual(Magic))
        {
            throw new InvalidDataException("Not a CivSim 2 replay file.");
        }

        int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Replay format {version} is not supported (this build reads {FormatVersion}).");
        }

        try
        {
            return MessagePackSerializer.Deserialize<ReplayData>(stream, Options);
        }
        catch (MessagePackSerializationException ex)
        {
            throw new InvalidDataException("The replay file is damaged.", ex);
        }
    }
}
