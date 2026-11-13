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
    [property: Key(2)] ClientRole Role = ClientRole.Player,
    [property: Key(3)] int CivId = -1) : IProtocolMessage
{
    /// <summary>A player who names no civilization gets the first free one.</summary>
    public const int AnyCiv = -1;
}

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
    [property: Key(9)] bool LiveSeasons = false,
    [property: Key(10)] IReadOnlyList<UnitView>? Units = null,
    [property: Key(11)] int PlayerLevers = 0,
    [property: Key(12)] IReadOnlyList<AiDecisionView>? AiDecisions = null,
    [property: Key(13)] ResearchView? Research = null,
    [property: Key(14)] IReadOnlyList<ProposalView>? Proposals = null) : IProtocolMessage;

/// <summary>
/// What the player's civilization researches and what it may research next (stage 6 PR P1, lever 2): the technology now (key, or empty),
/// how far it got (0–1000) and the technologies it may start now, cheapest first.
/// </summary>
[MessagePackObject]
public sealed record ResearchView(
    [property: Key(0)] string Researching,
    [property: Key(1)] int ProgressPermille,
    [property: Key(2)] IReadOnlyList<TechnologyOption> Options);

/// <summary>A technology the player may research now: key (for SetResearchDto), name, era and base cost in research points.</summary>
[MessagePackObject]
public sealed record TechnologyOption([property: Key(0)] string Key, [property: Key(1)] string Name, [property: Key(2)] int Era, [property: Key(3)] long Cost);

/// <summary>A pending proposal to the player's civilization (stage 6 PR P1, lever 4): from whom, which treaty (1 peace, 2 trade, 3 non-aggression, 4 alliance), since when.</summary>
[MessagePackObject]
public sealed record ProposalView([property: Key(0)] int From, [property: Key(1)] int Kind, [property: Key(2)] long Month);

/// <summary>
/// A decision the AI made for the player's civilization (ADR 0027, stage 6 PR P): when, on which lever (1–5; 0 = the simulation's,
/// such as recipes) and what, with its first reasons. Players see the last ones in the panel "what the AI did for me".
/// </summary>
[MessagePackObject]
public sealed record AiDecisionView([property: Key(0)] long Turn, [property: Key(1)] int Lever, [property: Key(2)] string Text);

/// <summary>The five levers of the helmsman (ADR 0027): bit flags of <see cref="GameViewMessage.PlayerLevers"/> and <see cref="SetLeverDto"/>.</summary>
public static class LeverFlags
{
    /// <summary>1. Where we live.</summary>
    public const int WhereWeLive = 1;

    /// <summary>2. Where we are heading.</summary>
    public const int WhereWeHead = 2;

    /// <summary>3. What we do on the land.</summary>
    public const int OnTheLand = 4;

    /// <summary>4. With whom and how.</summary>
    public const int WithWhom = 8;

    /// <summary>5. Answers to cards.</summary>
    public const int Cards = 16;

    /// <summary>The five flags in the order of the levers (1–5).</summary>
    public static readonly int[] All = [WhereWeLive, WhereWeHead, OnTheLand, WithWhom, Cards];

    /// <summary>Names shown on the tiles.</summary>
    public static readonly string[] Names = ["Where we live", "Where we are heading", "What we do on the land", "With whom and how", "Answers to cards"];
}

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
    [property: Key(6)] IReadOnlyList<string>? Strata = null,
    [property: Key(7)] IReadOnlyList<TaskInfo>? Tasks = null) : IProtocolMessage;

/// <summary>A kind of area task as the client sees it (stage 6 PR 4a): ids match <see cref="UnitView.Task"/>; the key goes into StartTaskDto.</summary>
[MessagePackObject]
public sealed record TaskInfo([property: Key(0)] int Id, [property: Key(1)] string Key, [property: Key(2)] string Name, [property: Key(3)] int Good);

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

/// <summary>
/// A unit as a viewer sees it (stage 6 PR 3a–3b): kind (index in units.json), owner, the cell it stands on, the cell it walks
/// to next and how far it got (0–1000), its people and what it does (0 idle, 1 move, 2 found, 3 scout, 4 return, 5 attack, 6 flee, 7 work).
/// </summary>
[MessagePackObject]
public sealed record UnitView(
    [property: Key(0)] long Id,
    [property: Key(1)] int Kind,
    [property: Key(2)] int Owner,
    [property: Key(3)] int World,
    [property: Key(4)] int Col,
    [property: Key(5)] int Row,
    [property: Key(6)] int NextCol,
    [property: Key(7)] int NextRow,
    [property: Key(8)] int ProgressPermille,
    [property: Key(9)] int People,
    [property: Key(10)] int Order)
{
    /// <summary>What its kind is for: 0 settlers, 1 scouts, 2 army, 3 carriers (stage 6 PR 3b), 4 work group (stage 6 PR 4a).</summary>
    [Key(11)]
    public int Role { get; init; }

    /// <summary>A work group's task (index in the content's task kinds), or -1 (stage 6 PR 4a).</summary>
    [Key(12)]
    public int Task { get; set; } = -1;

    /// <summary>Column of the cell a work group works on, or -1.</summary>
    [Key(13)]
    public int TargetCol { get; set; } = -1;

    /// <summary>Row of the cell a work group works on, or -1.</summary>
    [Key(14)]
    public int TargetRow { get; set; } = -1;
}
