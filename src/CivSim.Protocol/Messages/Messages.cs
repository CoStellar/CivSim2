using System.Collections.Generic;
using MessagePack;

namespace CivSim.Protocol.Messages;

/// <summary>
/// Any message between client and server. Union keys are part of the wire format: never reuse or renumber them.
/// </summary>
[Union(0, typeof(HelloMessage))]
[Union(1, typeof(WelcomeMessage))]
[Union(2, typeof(ConnectionRejectedMessage))]
[Union(3, typeof(SubmitCommandMessage))]
[Union(4, typeof(CommandRejectedMessage))]
[Union(5, typeof(EndTurnMessage))]
[Union(6, typeof(GameViewMessage))]
[Union(7, typeof(ContentMessage))]
[Union(8, typeof(MapChunkMessage))]
[Union(9, typeof(SetSpeedMessage))]
[Union(10, typeof(ObserverDetailsMessage))]
[Union(11, typeof(WatchCivilizationMessage))]
[Union(12, typeof(FogMessage))]
[Union(13, typeof(CivilizationDetailsMessage))]
[Union(14, typeof(NameSuggestionsRequestMessage))]
[Union(15, typeof(NameSuggestionsMessage))]
public interface IProtocolMessage
{
}

/// <summary>Client → server: first message after connecting.</summary>
[MessagePackObject]
public sealed record HelloMessage(
    [property: Key(0)] int ProtocolVersion,
    [property: Key(1)] string PlayerName,
    [property: Key(2)] ClientRole Role = ClientRole.Player) : IProtocolMessage;

/// <summary>What a client wants to be.</summary>
public enum ClientRole
{
    /// <summary>Leads one civilization and sees only what it has discovered.</summary>
    Player = 0,

    /// <summary>Simulator mode (GDD 13): every civilization is AI, the observer sees the whole world and sets the speed.</summary>
    Observer = 1,
}

/// <summary>
/// Server → client: connection accepted; tells the client who it is and which civilization it leads
/// (<see cref="NoCiv"/> for an observer).
/// </summary>
[MessagePackObject]
public sealed record WelcomeMessage([property: Key(0)] int ProtocolVersion, [property: Key(1)] int PlayerId, [property: Key(2)] int CivId) : IProtocolMessage
{
    /// <summary>Civilization id sent to observers.</summary>
    public const int NoCiv = -1;
}

/// <summary>Server → client: connection refused (wrong version, game full...).</summary>
[MessagePackObject]
public sealed record ConnectionRejectedMessage([property: Key(0)] string Reason) : IProtocolMessage;

/// <summary>Client → server: a command for the next turn. <see cref="ClientSequence"/> lets the client match rejections.</summary>
[MessagePackObject]
public sealed record SubmitCommandMessage([property: Key(0)] int CivId, [property: Key(1)] long ClientSequence, [property: Key(2)] ICommandDto Command) : IProtocolMessage;

/// <summary>Server → client: a submitted command was refused.</summary>
[MessagePackObject]
public sealed record CommandRejectedMessage([property: Key(0)] long ClientSequence, [property: Key(1)] string Reason) : IProtocolMessage;

/// <summary>Client → server: this player is done with the current turn.</summary>
[MessagePackObject]
public sealed record EndTurnMessage([property: Key(0)] long Turn) : IProtocolMessage;

/// <summary>
/// Server → client: what this player is allowed to see after a turn. Filtered by the server; never the full state.
/// </summary>
[MessagePackObject]
public sealed record GameViewMessage(
    [property: Key(0)] long Turn,
    [property: Key(1)] int ViewerCivId,
    [property: Key(2)] IReadOnlyList<WorldView> Worlds,
    [property: Key(3)] IReadOnlyList<CivilizationView> Civilizations,
    [property: Key(4)] long TurnMicroseconds = 0,
    [property: Key(5)] SimulationSpeed Speed = SimulationSpeed.Paused,
    [property: Key(6)] IReadOnlyList<ChronicleEntry>? Chronicle = null,
    [property: Key(7)] IReadOnlyList<SettlementView>? Settlements = null,
    [property: Key(8)] long Month = 0,
    [property: Key(9)] bool LiveSeasons = false) : IProtocolMessage;

/// <summary>
/// A settlement the viewer may see. <see cref="Stock"/> (hundredths per good id), <see cref="Population"/> (people per
/// stratum id), health, mood and food (hundredths, last turn) are filled only for the viewer's own settlements and
/// for observers; foreign settlements show only where they are. Status (0 active, 1 abandoned, 2 ruins) and the claim
/// (civilization id or -1, last month) are public.
/// </summary>
[MessagePackObject]
public sealed record SettlementView(
    [property: Key(0)] int Id,
    [property: Key(1)] int Owner,
    [property: Key(2)] int World,
    [property: Key(3)] int Col,
    [property: Key(4)] int Row,
    [property: Key(5)] string Name,
    [property: Key(6)] int Level,
    [property: Key(7)] long[]? Stock,
    [property: Key(8)] int[]? Population = null,
    [property: Key(9)] int Health = 0,
    [property: Key(10)] int Mood = 0,
    [property: Key(11)] long FoodGathered = 0,
    [property: Key(12)] long FoodNeeded = 0,
    [property: Key(13)] long FoodEaten = 0,
    [property: Key(14)] int Status = 0,
    [property: Key(15)] long AbandonedMonth = 0,
    [property: Key(16)] int ClaimCiv = -1,
    [property: Key(17)] long ClaimUntilMonth = 0,
    [property: Key(18)] int Loyalty = 0);

/// <summary>One line of the chronicle (GDD 11.1, phase 11).</summary>
[MessagePackObject]
public sealed record ChronicleEntry([property: Key(0)] long Turn, [property: Key(1)] string Text);

/// <summary>Public data about one world.</summary>
[MessagePackObject]
public sealed record WorldView([property: Key(0)] int Id, [property: Key(1)] string Name, [property: Key(2)] int Width, [property: Key(3)] int Height);

/// <summary>
/// What the viewer knows about one civilization. Era, people (in lived-in settlements), settlements, known
/// technologies, the title with the government (e.g. "Chiefdom of Karrut") and the government are filled for
/// observers only (stages 5 and 4f); otherwise empty or -1. <see cref="Capital"/> is the settlement id of the capital
/// when the viewer sees that settlement, otherwise -1 (stage 5b).
/// </summary>
[MessagePackObject]
public sealed record CivilizationView(
    [property: Key(0)] int Id,
    [property: Key(1)] string Name,
    [property: Key(2)] bool IsAlive,
    [property: Key(3)] long DiedOutMonth = 0,
    [property: Key(4)] string Era = "",
    [property: Key(5)] long People = -1,
    [property: Key(6)] int Settlements = -1,
    [property: Key(7)] int Technologies = -1,
    [property: Key(8)] string Title = "",
    [property: Key(9)] string Government = "",
    [property: Key(10)] int Capital = -1);

/// <summary>Observer speeds (GDD 11.2).</summary>
public enum SimulationSpeed
{
    /// <summary>No turns run.</summary>
    Paused = 0,

    /// <summary>Live speed (ADR 0025): data/time.json ticksPerSecond ticks a second.</summary>
    X1 = 1,

    /// <summary>Twice the live speed.</summary>
    X2 = 2,

    /// <summary>Four times the live speed.</summary>
    X4 = 4,

    /// <summary>As fast as possible; the map is refreshed every few ticks.</summary>
    Max = 100,
}

/// <summary>Observer → server: change the simulation speed. Players cannot do this.</summary>
[MessagePackObject]
public sealed record SetSpeedMessage([property: Key(0)] SimulationSpeed Speed) : IProtocolMessage;

/// <summary>
/// Server → client, once after Welcome: the content the client needs to draw the map (names and preview colours).
/// Ids match the values in <see cref="MapChunkMessage"/>.
/// </summary>
[MessagePackObject]
public sealed record ContentMessage(
    [property: Key(0)] IReadOnlyList<BiomeInfo> Biomes,
    [property: Key(1)] IReadOnlyList<DepositInfo> Deposits,
    [property: Key(2)] IReadOnlyList<string> Features,
    [property: Key(3)] IReadOnlyList<string> Reliefs,
    [property: Key(4)] IReadOnlyList<GoodInfo>? Goods = null,
    [property: Key(5)] IReadOnlyList<string>? SettlementLevels = null,
    [property: Key(6)] IReadOnlyList<string>? Strata = null) : IProtocolMessage;

/// <summary>A good as the client sees it. Ids match the indexes of <see cref="SettlementView.Stock"/>.</summary>
[MessagePackObject]
public sealed record GoodInfo([property: Key(0)] int Id, [property: Key(1)] string Key, [property: Key(2)] string Name, [property: Key(3)] bool IsFood, [property: Key(4)] uint Color);

/// <summary>A biome as the client sees it.</summary>
[MessagePackObject]
public sealed record BiomeInfo([property: Key(0)] int Id, [property: Key(1)] string Key, [property: Key(2)] string Name, [property: Key(3)] bool IsWater, [property: Key(4)] uint Color);

/// <summary>A deposit type as the client sees it. Deposit id 0 on the map means "none".</summary>
[MessagePackObject]
public sealed record DepositInfo([property: Key(0)] int Id, [property: Key(1)] string Key, [property: Key(2)] string Name, [property: Key(3)] uint Color);

/// <summary>
/// Server → client: the cells of one 32×32 map chunk (edge chunks can be smaller), row by row inside the chunk.
/// Sent for every chunk after Welcome and for changed chunks after each turn. Cells the viewer has never seen
/// have <see cref="Visibility"/> 0 and all other values zeroed: the client never receives hidden data.
/// </summary>
[MessagePackObject]
public sealed record MapChunkMessage(
    [property: Key(0)] int WorldId,
    [property: Key(1)] long Turn,
    [property: Key(2)] int ChunkIndex,
    [property: Key(3)] int X0,
    [property: Key(4)] int Y0,
    [property: Key(5)] int Width,
    [property: Key(6)] int Height,
    [property: Key(7)] byte[] Visibility,
    [property: Key(8)] byte[] Elevation,
    [property: Key(9)] sbyte[] Temperature,
    [property: Key(10)] byte[] Humidity,
    [property: Key(11)] byte[] Biome,
    [property: Key(12)] byte[] Relief,
    [property: Key(13)] byte[] Feature,
    [property: Key(14)] byte[] Fertility,
    [property: Key(15)] ushort[] Deposit,
    [property: Key(16)] int[] DepositRichness,
    [property: Key(17)] byte[] DepositDepth,
    [property: Key(18)] short[] Owner,
    [property: Key(19)] byte[] River,
    [property: Key(20)] ushort[]? WildFood = null,
    [property: Key(21)] byte[]? Influence = null,
    [property: Key(22)] byte[]? Seasonality = null,
    [property: Key(23)] byte[]? GrowingSeason = null,
    [property: Key(24)] sbyte[]? SeasonTemperature = null,
    [property: Key(25)] byte[]? SeasonGrowth = null) : IProtocolMessage
{
    /// <summary>Visibility: never seen.</summary>
    public const byte Unknown = 0;

    /// <summary>Visibility: seen before (values are what the viewer remembers).</summary>
    public const byte Remembered = 1;

    /// <summary>Visibility: visible now.</summary>
    public const byte Visible = 2;

    /// <summary>River edges per cell (N, NE, SE sides; the other three belong to neighbours).</summary>
    public const int RiverEdgesPerCell = 3;
}
