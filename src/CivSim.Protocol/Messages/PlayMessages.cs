using System.Collections.Generic;
using MessagePack;

namespace CivSim.Protocol.Messages;

/// <summary>A good of the new game (ADR 0028).</summary>
[MessagePackObject]
public sealed record PlayGoodInfo([property: Key(0)] int Id, [property: Key(1)] string Key, [property: Key(2)] string Name);

/// <summary>A building type of the new game. Amounts in hundredths, rates per worker per minute.</summary>
[MessagePackObject]
public sealed record PlayBuildingInfo(
    [property: Key(0)] int Id,
    [property: Key(1)] string Key,
    [property: Key(2)] string Name,
    [property: Key(3)] int Workers,
    [property: Key(4)] long[] Price,
    [property: Key(5)] int BuildSeconds,
    [property: Key(6)] long[] Produces,
    [property: Key(7)] long[] Consumes,
    [property: Key(8)] int Housing,
    [property: Key(9)] long Storage,
    [property: Key(10)] string Needs,
    [property: Key(11)] int Tech = 0,
    [property: Key(12)] string[]? Groups = null,
    [property: Key(13)] int Size = 1,
    [property: Key(14)] int UpgradesTo = 0,
    [property: Key(15)] int Carriers = 0);

/// <summary>A unit kind of the new game.</summary>
[MessagePackObject]
public sealed record PlayUnitInfo(
    [property: Key(0)] int Id,
    [property: Key(1)] string Key,
    [property: Key(2)] string Name,
    [property: Key(3)] int People,
    [property: Key(4)] long[] Price,
    [property: Key(5)] int TrainSeconds,
    [property: Key(6)] bool CanHide,
    [property: Key(7)] bool Founds,
    [property: Key(8)] int Tech = 0,
    [property: Key(9)] bool Army = false,
    [property: Key(10)] int Strength = 0,
    [property: Key(11)] int Range = 1,
    [property: Key(12)] bool System = false,
    [property: Key(13)] string NeedsBuilding = "",
    [property: Key(14)] bool Naval = false,
    [property: Key(15)] int Carries = 0,
    [property: Key(16)] int Survey = 0,
    [property: Key(17)] int MinPeople = 0,
    [property: Key(18)] int MaxPeople = 0,
    [property: Key(19)] string[]? Groups = null);

/// <summary>A unit as the player sees it. State: 0 training, 1 standing, 2 walking.</summary>
[MessagePackObject]
public sealed record PlayUnitView(
    [property: Key(0)] int Id,
    [property: Key(1)] int Kind,
    [property: Key(2)] int Civ,
    [property: Key(3)] int Col,
    [property: Key(4)] int Row,
    [property: Key(5)] int People,
    [property: Key(6)] int State,
    [property: Key(7)] bool Hidden,
    [property: Key(8)] int ToCol,
    [property: Key(9)] int ToRow,
    [property: Key(10)] bool Fighting = false,
    [property: Key(11)] string? Cargo = null,
    [property: Key(12)] int[]? Path = null,
    [property: Key(13)] int[]? Patrol = null,
    [property: Key(14)] bool Attack = false,
    [property: Key(15)] bool Carrier = false,
    [property: Key(16)] int Injured = 0,
    [property: Key(17)] int Aboard = 0,
    [property: Key(18)] int Hunger = 0,
    [property: Key(19)] int Home = -1,
    [property: Key(20)] int[]? LastPatrol = null,
    [property: Key(21)] int[]? Found = null,
    [property: Key(22)] int Weapon = 0,
    [property: Key(23)] int Armour = 0,
    [property: Key(24)] int Stance = 0,
    [property: Key(25)] int LastLoss = 0,
    [property: Key(26)] int[]? Guard = null,
    [property: Key(27)] int Size = 0,
    [property: Key(28)] int[]? Struck = null,
    [property: Key(29)] bool Charging = false,
    [property: Key(30)] long Provisions = 0,
    [property: Key(31)] long ProvisionsMax = 0,
    [property: Key(32)] bool InSupply = true);

/// <summary>A settlement level of the new game.</summary>
[MessagePackObject]
public sealed record PlayLevelInfo([property: Key(0)] string Key, [property: Key(1)] string Name, [property: Key(2)] int Radius, [property: Key(3)] int Housing, [property: Key(4)] long[] UpgradePrice, [property: Key(5)] int Tech = -1);

/// <summary>A technology of the new game. Needs: earlier technology names with the worker-minutes of experience.</summary>
[MessagePackObject]
public sealed record PlayTechInfo(
    [property: Key(0)] int Id,
    [property: Key(1)] string Key,
    [property: Key(2)] string Name,
    [property: Key(3)] int Era,
    [property: Key(4)] string Needs,
    [property: Key(5)] int Seconds,
    [property: Key(6)] string Gives = "",
    [property: Key(7)] bool Upgrade = false,
    [property: Key(8)] string Category = "",
    [property: Key(9)] string Unlocks = "",
    [property: Key(10)] int[]? From = null,
    [property: Key(11)] string Gateway = "");

/// <summary>Where a people stands with one technology: known, or what it would cost now and why it cannot start.</summary>
[MessagePackObject]
public sealed record PlayTechState([property: Key(0)] int Id, [property: Key(1)] bool Known, [property: Key(2)] long[] Price, [property: Key(3)] string? Blocked, [property: Key(4)] int Seconds = 0);

/// <summary>A thinker or advisor as the player sees them.</summary>
[MessagePackObject]
public sealed record PlayPersonView(
    [property: Key(0)] string Name,
    [property: Key(1)] string Rarity,
    [property: Key(2)] int CostPercent,
    [property: Key(3)] int ExperiencePercent,
    [property: Key(4)] string Fields,
    [property: Key(5)] long[] HirePrice);

/// <summary>Server → client, once: the rules a player needs to see (goods, buildings, levels).</summary>
[MessagePackObject]
public sealed record PlayContentMessage(
    [property: Key(0)] IReadOnlyList<PlayGoodInfo> Goods,
    [property: Key(1)] IReadOnlyList<PlayBuildingInfo> Buildings,
    [property: Key(2)] IReadOnlyList<PlayLevelInfo> Levels,
    [property: Key(3)] int MapWidth,
    [property: Key(4)] int MapHeight,
    [property: Key(5)] string WorldName,
    [property: Key(6)] IReadOnlyList<PlayUnitInfo> Units,
    [property: Key(7)] IReadOnlyList<PlayTechInfo> Techs,
    [property: Key(8)] IReadOnlyList<string> Eras,
    [property: Key(9)] IReadOnlyList<PlayRoadInfo>? Roads = null,
    [property: Key(10)] IReadOnlyList<PlayWorkInfo>? Works = null,
    [property: Key(11)] IReadOnlyList<string>? Categories = null,
    [property: Key(12)] int HungryPercent = 100,
    [property: Key(13)] int StarvingPercent = 100,
    [property: Key(14)] int[]? WeaponGoods = null,
    [property: Key(15)] int[]? ArmourGoods = null,
    [property: Key(16)] int[]? WeaponPercent = null,
    [property: Key(17)] int[]? ArmourPercent = null,
    [property: Key(18)] PlayCombatInfo? Combat = null,
    [property: Key(19)] string[]? VeteranOptions = null,
    [property: Key(20)] int[]? VeteranLevels = null,
    [property: Key(21)] int[]? PeaceMinutes = null) : IProtocolMessage;

/// <summary>
/// Numbers of battle the client shows on hexes (cover markers): cover of trees, palisade and wall against ranged and melee,
/// open ground, height (relief levels and elevation), river; percent changes of the damage taken. Flank by enemies touching.
/// </summary>
[MessagePackObject]
public sealed record PlayCombatInfo(
    [property: Key(0)] int TreeRanged,
    [property: Key(1)] int TreeMelee,
    [property: Key(2)] int PalisadeRanged,
    [property: Key(3)] int PalisadeMelee,
    [property: Key(4)] int WallRanged,
    [property: Key(5)] int WallMelee,
    [property: Key(6)] int OpenPercent,
    [property: Key(7)] int[] OpenBiomes,
    [property: Key(8)] int ReliefPercent,
    [property: Key(9)] int ReliefMaxPercent,
    [property: Key(10)] int ElevationStep,
    [property: Key(11)] int ElevationPercent,
    [property: Key(12)] int ElevationMaxPercent,
    [property: Key(13)] int RiverPercent,
    [property: Key(14)] int[] Flank);

/// <summary>A work drawn on hexes (ditch, palisade, wall, aqueduct): its bit in the works of a hex, price and seconds per hex.</summary>
[MessagePackObject]
public sealed record PlayWorkInfo([property: Key(0)] int Id, [property: Key(1)] string Key, [property: Key(2)] string Name, [property: Key(3)] long[] Price, [property: Key(4)] int Seconds, [property: Key(5)] int Tech, [property: Key(6)] int FromWater, [property: Key(7)] string Text);

/// <summary>A level of road (1 track, 2 paved road): price and seconds per hex, needed technology.</summary>
[MessagePackObject]
public sealed record PlayRoadInfo([property: Key(0)] int Level, [property: Key(1)] string Name, [property: Key(2)] long[] Price, [property: Key(3)] int Seconds, [property: Key(4)] int Tech);

/// <summary>A settlement as the player sees it.</summary>
[MessagePackObject]
public sealed record PlaySettlementView(
    [property: Key(0)] int Id,
    [property: Key(1)] int Civ,
    [property: Key(2)] string Name,
    [property: Key(3)] int Col,
    [property: Key(4)] int Row,
    [property: Key(5)] int Level,
    [property: Key(6)] int People,
    [property: Key(7)] int Housing,
    [property: Key(8)] int Idle,
    [property: Key(9)] bool Besieged = false,
    [property: Key(10)] int DefensePercent = 100,
    [property: Key(11)] long[]? Stock = null,
    [property: Key(12)] long[]? Capacity = null,
    [property: Key(13)] long[]? Rate = null,
    [property: Key(14)] bool Remembered = false,
    [property: Key(15)] bool Gathering = false,
    [property: Key(16)] int[]? Richness = null,
    [property: Key(17)] int WorkPermille = 1000,
    [property: Key(18)] int HealPerMinute = 0,
    [property: Key(19)] int RationUnits = 0,
    [property: Key(20)] int RationPeople = 0,
    [property: Key(21)] long RationFood = 0,
    [property: Key(22)] int RationShort = 0,
    [property: Key(23)] int RationDead = 0,
    [property: Key(24)] int HungryPeople = 0,
    [property: Key(25)] int HungryWorkers = 0,
    [property: Key(26)] int Guard = 0,
    [property: Key(27)] int GuardMax = 0,
    [property: Key(28)] bool FallenToUs = false,
    [property: Key(29)] int Fighters = 0,
    [property: Key(30)] string? FoodBalance = null);

/// <summary>A standing caravan route of the player.</summary>
[MessagePackObject]
public sealed record PlayRouteView([property: Key(0)] int Index, [property: Key(1)] int From, [property: Key(2)] int To, [property: Key(3)] int Good, [property: Key(4)] int Amount, [property: Key(5)] bool OnTheRoad, [property: Key(6)] int[]? Path = null);

/// <summary>
/// Another people as the player knows it. At war (ADR 0030): <c>Favour</c> is how much the course of the war favours us, percent
/// of the loser's wealth (above 0 we may demand); weariness 0-100 of both sides; their goods we know (whole units, by good index);
/// settlements of theirs we may demand; the suggested terms as [we give 0/1, length index, settlement or -1, good, amount, ...].
/// </summary>
[MessagePackObject]
public sealed record PlayPeopleView(
    [property: Key(0)] int Civ,
    [property: Key(1)] string Name,
    [property: Key(2)] bool AtWar,
    [property: Key(3)] int OurScore,
    [property: Key(4)] int TheirScore,
    [property: Key(5)] int Favour,
    [property: Key(6)] int Reputation,
    [property: Key(7)] string? TheirPeace,
    [property: Key(8)] bool Dead,
    [property: Key(9)] bool Trade = false,
    [property: Key(10)] bool Alliance = false,
    [property: Key(11)] bool TheyOfferTrade = false,
    [property: Key(12)] bool TheyOfferAlliance = false,
    [property: Key(13)] string? TheirBarter = null,
    [property: Key(14)] int OurWeariness = 0,
    [property: Key(15)] int TheirWeariness = 0,
    [property: Key(16)] long[]? TheirGoods = null,
    [property: Key(17)] int[]? Demandable = null,
    [property: Key(18)] int[]? Suggested = null,
    [property: Key(19)] int OurWorkLoss = 0,
    [property: Key(20)] bool WeStarted = false,
    [property: Key(21)] bool TheyStarted = false,
    [property: Key(22)] long FairWorth = 0);

/// <summary>A building as the player sees it. State: 0 construction, 1 paused construction, 2 working, 3 stopped.</summary>
[MessagePackObject]
public sealed record PlayBuildingView(
    [property: Key(0)] int Id,
    [property: Key(1)] int Type,
    [property: Key(2)] int Civ,
    [property: Key(3)] int Col,
    [property: Key(4)] int Row,
    [property: Key(5)] int State,
    [property: Key(6)] int Workers,
    [property: Key(7)] bool Starved,
    [property: Key(8)] bool Priority,
    [property: Key(9)] int ProgressPermille,
    [property: Key(10)] int YieldPercent,
    [property: Key(11)] string? Missing = null,
    [property: Key(12)] int Porters = 0,
    [property: Key(13)] bool Remembered = false,
    [property: Key(14)] int Slots = 15,
    [property: Key(15)] string[]? Focus = null,
    [property: Key(16)] string? UpgradeBlocked = null,
    [property: Key(17)] string? UpgradeCost = null);

/// <summary>
/// Server → client: hexes whose fog or owner changed for this player (indices row × width + column). Terrain comes in
/// <see cref="MapChunkMessage"/> once, when a hex is first explored; after that only these small changes.
/// </summary>
[MessagePackObject]
public sealed record PlayCellsMessage(
    [property: Key(0)] int[] Cells,
    [property: Key(1)] byte[] Visibility,
    [property: Key(2)] short[] Owner,
    [property: Key(3)] int[] Chunks,
    [property: Key(4)] byte[]? Road = null,
    [property: Key(5)] ushort[]? Works = null) : IProtocolMessage;

/// <summary>Server → client, several times a second: what the player sees of the game.</summary>
[MessagePackObject]
public sealed record PlayViewMessage(
    [property: Key(0)] long Tick,
    [property: Key(1)] SimulationSpeed Speed,
    [property: Key(2)] int Civ,
    [property: Key(3)] long[] Stock,
    [property: Key(4)] long[] Capacity,
    [property: Key(5)] long[] RatePerMinute,
    [property: Key(6)] int People,
    [property: Key(7)] bool Hungry,
    [property: Key(8)] IReadOnlyList<PlaySettlementView> Settlements,
    [property: Key(9)] IReadOnlyList<PlayBuildingView> Buildings,
    [property: Key(10)] IReadOnlyList<string> News,
    [property: Key(11)] IReadOnlyList<PlayUnitView> Units,
    [property: Key(12)] int Era,
    [property: Key(13)] int Researching,
    [property: Key(14)] int ResearchPermille,
    [property: Key(15)] IReadOnlyList<PlayTechState> Techs,
    [property: Key(16)] PlayPersonView? Thinker,
    [property: Key(17)] PlayPersonView? ThinkerOffer,
    [property: Key(18)] PlayPersonView? Advisor,
    [property: Key(19)] PlayPersonView? AdvisorOffer,
    [property: Key(20)] string? Advice,
    [property: Key(21)] IReadOnlyList<PlayPeopleView> Peoples,
    [property: Key(22)] bool Lost,
    [property: Key(23)] bool Picking = false,
    [property: Key(24)] bool MyPick = false,
    [property: Key(25)] int PickSecondsLeft = 0,
    [property: Key(26)] int[]? TakenStarts = null,
    [property: Key(27)] int BlockRadius = 0,
    [property: Key(28)] string? WhoPicks = null,
    [property: Key(29)] string? Event = null,
    [property: Key(30)] int EventSecondsLeft = 0,
    [property: Key(31)] string? Goal = null,
    [property: Key(32)] string? Winner = null,
    [property: Key(33)] int Score = 0,
    [property: Key(34)] IReadOnlyList<PlayRouteView>? Routes = null,
    [property: Key(35)] IReadOnlyList<PlayAlertView>? Alerts = null,
    [property: Key(36)] int EraKnown = 0,
    [property: Key(37)] int EraNeed = 0,
    [property: Key(38)] bool ObjectsApart = false,
    [property: Key(39)] bool TechsSame = false,
    [property: Key(40)] bool Winter = false,
    [property: Key(41)] int SeasonSeconds = 0,
    [property: Key(42)] int RationSeconds = 0,
    [property: Key(43)] bool Debug = false,
    [property: Key(44)] int ArmyUnits = 0,
    [property: Key(45)] int ArmyLimit = 0,
    [property: Key(46)] int[]? KindKills = null,
    [property: Key(47)] int[]? KindLevel = null,
    [property: Key(48)] int[]? KindPending = null,
    [property: Key(49)] int[]? KindPicks = null,
    [property: Key(50)] bool Won = false,
    [property: Key(51)] string? EventInfo = null,
    [property: Key(52)] IReadOnlyList<PlayTutorialStep>? Tutorial = null,
    [property: Key(53)] bool TutorialOn = false,
    [property: Key(54)] bool PickNoLimit = false,
    [property: Key(55)] int PickMaxHexes = 0,
    [property: Key(56)] int[]? Signs = null,
    [property: Key(57)] int SignRadius = 0) : IProtocolMessage;

/// <summary>One step of the tutorial (E6, 2026-11-20): what to do, whether it is done, the building key to highlight ("" = none).</summary>
[MessagePackObject]
public sealed record PlayTutorialStep([property: Key(0)] string Text, [property: Key(1)] bool Done, [property: Key(2)] string Highlight);

/// <summary>
/// Server → client, up to ten times a second: only the buildings, settlements and units that appeared or changed for this
/// player since the last message, and the ids of those gone from its view. The client keeps the rest; <see
/// cref="PlayViewMessage"/> then comes without them (<c>ObjectsApart</c>).
/// </summary>
[MessagePackObject]
public sealed record PlayObjectsMessage(
    [property: Key(0)] IReadOnlyList<PlayBuildingView> Buildings,
    [property: Key(1)] int[] GoneBuildings,
    [property: Key(2)] IReadOnlyList<PlaySettlementView> Settlements,
    [property: Key(3)] int[] GoneSettlements,
    [property: Key(4)] IReadOnlyList<PlayUnitView> Units,
    [property: Key(5)] int[] GoneUnits) : IProtocolMessage;

/// <summary>Something in one of the player's settlements that needs a reaction. Kind: 0 attacked, 1 enemy near, 2 hungry,
/// 3 needs a good, 4 no homes, 5 store full, 6 construction lacks materials. Good is −1 when not about a good.</summary>
[MessagePackObject]
public sealed record PlayAlertView(
    [property: Key(0)] int Kind,
    [property: Key(1)] int Settlement,
    [property: Key(2)] int Col,
    [property: Key(3)] int Row,
    [property: Key(4)] int Good,
    [property: Key(5)] string Text);

/// <summary>What a player command does.</summary>
public enum PlayCommandKind
{
    /// <summary>Build type <c>A</c> on (<c>Col</c>, <c>Row</c>).</summary>
    Build = 0,

    /// <summary>Pause (<c>Flag</c>) or resume the construction of building <c>A</c>.</summary>
    PauseConstruction = 1,

    /// <summary>Stop (<c>Flag</c>) or start building <c>A</c>.</summary>
    Stop = 2,

    /// <summary>Give building <c>A</c> its workers first (<c>Flag</c>) or not.</summary>
    Priority = 3,

    /// <summary>Tear down building <c>A</c>.</summary>
    Demolish = 4,

    /// <summary>Raise settlement <c>A</c> one level.</summary>
    Upgrade = 5,

    /// <summary>Train unit kind <c>B</c> in settlement <c>A</c>, <c>C</c> people (0 = the default); <c>Flag</c>: weapons only.</summary>
    Train = 6,

    /// <summary>Send unit <c>A</c> to (<c>Col</c>, <c>Row</c>), hidden when <c>Flag</c>.</summary>
    Move = 7,

    /// <summary>Settlers <c>A</c> found a camp where they stand.</summary>
    Found = 8,

    /// <summary>Research technology <c>A</c>.</summary>
    Research = 9,

    /// <summary>Hire the thinker on offer, or the advisor when <c>Flag</c>.</summary>
    Hire = 10,

    /// <summary>Let the advisor go.</summary>
    Dismiss = 11,

    /// <summary>Declare war on people <c>A</c>.</summary>
    DeclareWar = 12,

    /// <summary>Unit <c>A</c> attacks what stands on (<c>Col</c>, <c>Row</c>).</summary>
    Attack = 13,

    /// <summary>
    /// Offer peace to people <c>A</c> (ADR 0030): <c>B</c> the length (index into the content's peace minutes), <c>C</c> the
    /// settlement demanded or given plus one (0 = none), <c>Cells</c> pairs of good and whole amount; <c>Flag</c>: we give them.
    /// </summary>
    ProposePeace = 14,

    /// <summary>Answer the peace offer of people <c>A</c> (accept when <c>Flag</c>).</summary>
    AnswerPeace = 15,

    /// <summary>Unit <c>A</c> (not an army unit) goes home and its people join the settlement there.</summary>
    Disband = 16,

    /// <summary>Start the people on (<c>Col</c>, <c>Row</c>) during the start phase.</summary>
    PickStart = 17,

    /// <summary>Offer people <c>A</c> a treaty of kind <c>B</c> (0 trade, 1 alliance).</summary>
    ProposeTreaty = 18,

    /// <summary>Answer people <c>A</c>'s offer of treaty <c>B</c> (accept when <c>Flag</c>).</summary>
    AnswerTreaty = 19,

    /// <summary>End treaty <c>B</c> with people <c>A</c>.</summary>
    CancelTreaty = 20,

    /// <summary>Offer people <c>A</c>: <c>Col</c> units of good <c>B</c> for <c>C</c> units of good <c>Row</c>.</summary>
    Barter = 21,

    /// <summary>Answer people <c>A</c>'s barter offer (accept when <c>Flag</c>).</summary>
    AnswerBarter = 22,

    /// <summary>Caravan from settlement <c>A</c> to settlement <c>B</c> with <c>C</c> units of good <c>Col</c>.</summary>
    SendCaravan = 23,

    /// <summary>
    /// Standing route from settlement <c>A</c> to settlement <c>B</c>: <c>C</c> units of good <c>Col</c> a trip, leaving <c>Row</c>
    /// at the source, while the target has fewer than <c>D</c> (0 = no limit).
    /// </summary>
    Route = 24,

    /// <summary>End standing route number <c>A</c>.</summary>
    EndRoute = 25,

    /// <summary>Send porters with the materials construction <c>A</c> is missing.</summary>
    BringMaterials = 26,

    /// <summary>Build a road of level <c>A</c> over the drawn hexes <c>Cells</c> (col, row pairs).</summary>
    BuildRoad = 28,

    /// <summary>Removed 2026-11-24 (gathering is always on); the server refuses it. The number stays so the others keep theirs.</summary>
    Gather = 29,

    /// <summary>Build work <c>A</c> (ditch, palisade, wall, aqueduct) over the drawn hexes <c>Cells</c> (col, row pairs).</summary>
    BuildWork = 30,

    /// <summary>Remove work <c>A</c> from the hex at <c>Col</c>, <c>Row</c>.</summary>
    RemoveWork = 31,

    /// <summary>Unit <c>A</c> breaks the enemy wall at <c>Col</c>, <c>Row</c>.</summary>
    AttackWall = 32,

    /// <summary>Worker <c>B</c> of school (academy, university) <c>A</c> speeds research branch number <c>C</c> of the content's categories.</summary>
    SetFocus = 33,

    /// <summary>Land unit <c>A</c> goes aboard ship <c>B</c> next to it.</summary>
    Board = 34,

    /// <summary>Ship <c>A</c> lands the units aboard on the hex at <c>Col</c>, <c>Row</c> next to it.</summary>
    Land = 35,

    /// <summary>Upgrade building <c>A</c> in place to its better kind.</summary>
    UpgradeBuilding = 36,

    /// <summary>Unit <c>A</c> walks its last patrol again.</summary>
    ResumePatrol = 37,

    /// <summary>Debug action <c>A</c> (test scenarios only): 0 goods, 1 finish research, 2 reveal map, 3 people, 4 next era.</summary>
    Debug = 38,

    /// <summary>Unit <c>A</c> splits <c>B</c> people off into a new unit beside it.</summary>
    Split = 39,

    /// <summary>Unit <c>A</c> takes the best arms of the settlement it stands in.</summary>
    Refit = 40,

    /// <summary>Unit <c>A</c> fights with stance <c>B</c>: 0 hold, 1 aggressive, 2 passive.</summary>
    Stance = 41,

    /// <summary>Unit <c>A</c> guards the hexes <c>Cells</c> (col, row pairs; none = stop guarding).</summary>
    Guard = 42,

    /// <summary>Units <c>Cells</c> (unit ids) go in formation to (Col, Row) facing direction <c>B</c> (−1 = the way they walk).</summary>
    Formation = 43,

    /// <summary>Unit <c>A</c> patrols the drawn loop <c>Cells</c> (col, row pairs) round and round.</summary>
    Patrol = 27,

    /// <summary>Settlement <c>A</c> fell to us: <c>B</c> 0 take it, 1 sack it, 2 raze it.</summary>
    TakeSettlement = 44,

    /// <summary>A caravan with <c>C</c> food from settlement <c>A</c> to our army unit <c>B</c> in the field.</summary>
    SupplyUnit = 45,

    /// <summary>Our units of kind <c>A</c> learn veteran option <c>B</c> for a level they reached.</summary>
    ChooseVeteran = 46,

    /// <summary>Replace finished building <c>A</c> by a building of kind <c>B</c> on its places (half of its price counts).</summary>
    ReplaceBuilding = 47,

    /// <summary>Prospectors <c>A</c> search the ore signs one by one, nearest first.</summary>
    SearchSigns = 48,
}

/// <summary>Client → server: a command of the new game. Refusals come back as <see cref="CommandRejectedMessage"/>.</summary>
[MessagePackObject]
public sealed record PlayCommandMessage(
    [property: Key(0)] long Sequence,
    [property: Key(1)] PlayCommandKind Kind,
    [property: Key(2)] int A,
    [property: Key(3)] int Col,
    [property: Key(4)] int Row,
    [property: Key(5)] bool Flag,
    [property: Key(6)] int B = 0,
    [property: Key(7)] int C = 0,
    [property: Key(8)] int[]? Cells = null,
    [property: Key(9)] int D = 0) : IProtocolMessage;

/// <summary>Client → server: the tutorial's task list on or off for this game (kept in its saves).</summary>
[MessagePackObject]
public sealed record SetTutorialMessage([property: Key(0)] bool On) : IProtocolMessage;

/// <summary>Client → server: save the game under <c>Name</c> (file name without folder or extension).</summary>
[MessagePackObject]
public sealed record SaveGameRequestMessage([property: Key(0)] string Name) : IProtocolMessage;

/// <summary>Server → client: whether the save worked, and what to tell the player.</summary>
[MessagePackObject]
public sealed record SaveGameResultMessage([property: Key(0)] bool Saved, [property: Key(1)] string Text) : IProtocolMessage;

/// <summary>Client → server: where can building type <c>Type</c> go?</summary>
[MessagePackObject]
public sealed record PlacesRequestMessage([property: Key(0)] int Type) : IProtocolMessage;

/// <summary>
/// Server → client: hexes (col, row pairs) of the player's land where <c>Type</c> can be built (price not checked), and for
/// each hex the free building places on it (bits 1, 2, 4, 8: top left, top right, bottom left, bottom right).
/// </summary>
[MessagePackObject]
public sealed record PlacesMessage([property: Key(0)] int Type, [property: Key(1)] int[] Cells, [property: Key(2)] int[]? Free = null) : IProtocolMessage;

/// <summary>Client → server: send the summary (history and start quality).</summary>
[MessagePackObject]
public sealed record PlaySummaryRequestMessage([property: Key(0)] int Unused = 0) : IProtocolMessage;

/// <summary>One minute of a people's history.</summary>
[MessagePackObject]
public sealed record PlayHistoryRow(
    [property: Key(0)] int Minute,
    [property: Key(1)] int People,
    [property: Key(2)] int Settlements,
    [property: Key(3)] int Buildings,
    [property: Key(4)] int Techs,
    [property: Key(5)] int Soldiers,
    [property: Key(6)] long FoodPerMinute,
    [property: Key(7)] int Era,
    [property: Key(8)] string? Event);

/// <summary>A people's history as the summary shows it.</summary>
[MessagePackObject]
public sealed record PlayHistoryView([property: Key(0)] int Civ, [property: Key(1)] string Name, [property: Key(2)] IReadOnlyList<PlayHistoryRow> Rows);

/// <summary>Server → client: the summary: our history and that of the peoples we met, and how good our start was.</summary>
[MessagePackObject]
public sealed record PlaySummaryMessage(
    [property: Key(0)] IReadOnlyList<PlayHistoryView> Peoples,
    [property: Key(1)] int StartScore,
    [property: Key(2)] int BestStartScore,
    [property: Key(3)] IReadOnlyList<string> Criteria,
    [property: Key(4)] int[] StartCriteria) : IProtocolMessage;
