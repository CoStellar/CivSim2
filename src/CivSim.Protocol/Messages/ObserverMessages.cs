using System.Collections.Generic;
using MessagePack;

namespace CivSim.Protocol.Messages;

/// <summary>
/// Server → observer, after every view (stage 5, layered map view): what the developer layers draw on top of the
/// map. Players never get this message; they will see units and fields through their own fog of war in stage 13.
/// </summary>
[MessagePackObject]
public sealed record ObserverDetailsMessage(
    [property: Key(0)] long Turn,
    [property: Key(1)] IReadOnlyList<JourneyView> Journeys,
    [property: Key(2)] IReadOnlyList<FieldView> Fields,
    [property: Key(3)] IReadOnlyList<CellEventView> Events,
    [property: Key(4)] IReadOnlyList<RelationView> Relations,
    [property: Key(5)] IReadOnlyList<ArmyView>? Armies = null) : IProtocolMessage;

/// <summary>
/// A war campaign on the march (stage 4e PR 3): where the army stands, where it goes, how strong it is and its carriers.
/// <see cref="Phase"/>: 0 march, 1 return, 2 muster. <see cref="Cells"/> is the width of its front. The carriers are at
/// <see cref="ConvoyCol"/>, <see cref="ConvoyRow"/> when <see cref="Carriers"/> is above 0.
/// </summary>
[MessagePackObject]
public sealed record ArmyView(
    [property: Key(0)] long Id,
    [property: Key(1)] int Owner,
    [property: Key(2)] int Enemy,
    [property: Key(3)] int Col,
    [property: Key(4)] int Row,
    [property: Key(5)] int TargetCol,
    [property: Key(6)] int TargetRow,
    [property: Key(7)] int Warriors,
    [property: Key(8)] int Cells,
    [property: Key(9)] int ArmsPercent,
    [property: Key(10)] int Morale,
    [property: Key(11)] int FoodMonths,
    [property: Key(12)] byte Phase,
    [property: Key(13)] int Carriers,
    [property: Key(14)] int ConvoyCol,
    [property: Key(15)] int ConvoyRow);

/// <summary>A camp walking to a far place over several turns (stage 3g): where it is and where it goes.</summary>
[MessagePackObject]
public sealed record JourneyView(
    [property: Key(0)] int Settlement,
    [property: Key(1)] int Owner,
    [property: Key(2)] int World,
    [property: Key(3)] int Col,
    [property: Key(4)] int Row,
    [property: Key(5)] int TargetCol,
    [property: Key(6)] int TargetRow,
    [property: Key(7)] int People);

/// <summary>A farmed cell (stage 3, agriculture): owner and how many of the cell's farmer places are worked, 0–100%.</summary>
[MessagePackObject]
public sealed record FieldView(
    [property: Key(0)] int World,
    [property: Key(1)] int Col,
    [property: Key(2)] int Row,
    [property: Key(3)] int Owner,
    [property: Key(4)] int WorkedPercent);

/// <summary>
/// An active effect of an event (stage 3h) on a cell or a settlement. <see cref="Effect"/> is the effect key from
/// data/effects.json (for example <c>extraction_reduction</c>); <see cref="Strength"/> is its signed strength.
/// </summary>
[MessagePackObject]
public sealed record CellEventView(
    [property: Key(0)] int World,
    [property: Key(1)] int Col,
    [property: Key(2)] int Row,
    [property: Key(3)] string Effect,
    [property: Key(4)] int Strength,
    [property: Key(5)] long EndMonth);

/// <summary>
/// How two civilizations stand (stage 4c–4d). <see cref="SympathyAB"/> is what A thinks of B (hundredths, −10 000 …
/// +10 000), <see cref="SympathyBA"/> the other way; 0 when one does not know the other. <see cref="Treaties"/>
/// is a bit set of <see cref="RelationView"/> flags.
/// </summary>
[MessagePackObject]
public sealed record RelationView(
    [property: Key(0)] int A,
    [property: Key(1)] int B,
    [property: Key(2)] int Contact,
    [property: Key(3)] int SympathyAB,
    [property: Key(4)] int SympathyBA,
    [property: Key(5)] int Treaties)
{
    /// <summary>Contact: only a rumor.</summary>
    public const int Rumor = 1;

    /// <summary>Contact: the two have met.</summary>
    public const int Met = 2;

    /// <summary>Treaty flag: at war.</summary>
    public const int War = 1 << 0;

    /// <summary>Treaty flag: peace (truce after a war).</summary>
    public const int Peace = 1 << 1;

    /// <summary>Treaty flag: trade agreement.</summary>
    public const int Trade = 1 << 2;

    /// <summary>Treaty flag: non-aggression pact.</summary>
    public const int NonAggression = 1 << 3;

    /// <summary>Treaty flag: defensive alliance.</summary>
    public const int Alliance = 1 << 4;
}

/// <summary>
/// Observer → server: follow one civilization (stage 5), or stop with <see cref="WelcomeMessage.NoCiv"/>. The server
/// then sends its fog of war (layer "memory and fog") and its <see cref="CivilizationDetailsMessage"/> after every
/// view. The observer still gets the whole map.
/// </summary>
[MessagePackObject]
public sealed record WatchCivilizationMessage([property: Key(0)] int CivId) : IProtocolMessage;

/// <summary>
/// Server → observer: what the watched civilization knows of a world, one byte per cell in row order
/// (<see cref="MapChunkMessage.Unknown"/>, <see cref="MapChunkMessage.Remembered"/>, <see cref="MapChunkMessage.Visible"/>).
/// Sent after every view while a civilization is watched.
/// </summary>
[MessagePackObject]
public sealed record FogMessage(
    [property: Key(0)] int CivId,
    [property: Key(1)] int WorldId,
    [property: Key(2)] long Turn,
    [property: Key(3)] byte[] Cells) : IProtocolMessage;

/// <summary>
/// Server → observer: everything about the followed civilization (stage 5 civilization panel). Lists of names and
/// values have the same length. Amounts are in hundredths.
/// </summary>
[MessagePackObject]
public sealed record CivilizationDetailsMessage(
    [property: Key(0)] int CivId,
    [property: Key(1)] long Turn,
    [property: Key(2)] string Name,
    [property: Key(3)] bool IsAlive,
    [property: Key(4)] string Era,
    [property: Key(5)] IReadOnlyList<string> Technologies,
    [property: Key(6)] string Researching,
    [property: Key(7)] int ResearchPercent,
    [property: Key(8)] long ScienceLastTurn,
    [property: Key(9)] IReadOnlyList<string> TraitNames,
    [property: Key(10)] IReadOnlyList<int> Traits,
    [property: Key(11)] IReadOnlyList<string> GoalNames,
    [property: Key(12)] IReadOnlyList<int> GoalWeights,
    [property: Key(13)] IReadOnlyList<string> Strata,
    [property: Key(14)] IReadOnlyList<long> People,
    [property: Key(15)] IReadOnlyList<string> SettlementLevels,
    [property: Key(16)] IReadOnlyList<int> Settlements,
    [property: Key(17)] long FoodGathered,
    [property: Key(18)] long FoodNeeded,
    [property: Key(19)] IReadOnlyList<string> Goods,
    [property: Key(20)] IReadOnlyList<long> Stock,
    [property: Key(21)] IReadOnlyList<RelationDetail> Relations,
    [property: Key(22)] string Title = "",
    [property: Key(23)] string Government = "",
    [property: Key(24)] string Leader = "",
    [property: Key(25)] int Stability = 0,
    [property: Key(26)] string StabilityParts = "",
    [property: Key(27)] string Capital = "",
    [property: Key(28)] int AdminUsed = 0,
    [property: Key(29)] int AdminCapacity = 0) : IProtocolMessage;

/// <summary>
/// What the followed civilization thinks of another: contact, sympathy and trust (hundredths, −10 000 … +10 000),
/// treaty flags of <see cref="RelationView"/>, and its memories as "kind strength‰" texts, strongest first.
/// </summary>
[MessagePackObject]
public sealed record RelationDetail(
    [property: Key(0)] int Other,
    [property: Key(1)] int Contact,
    [property: Key(2)] int Sympathy,
    [property: Key(3)] int Trust,
    [property: Key(4)] int Treaties,
    [property: Key(5)] IReadOnlyList<string> Memories);

/// <summary>What a name suggestion is for.</summary>
public enum NameTarget
{
    /// <summary>A civilization (people's name).</summary>
    Civilization = 0,

    /// <summary>A settlement (place name).</summary>
    Settlement = 1,
}

/// <summary>
/// Client → server: other names for a civilization or a settlement in the same people's language (GDD 5.6), for the
/// rename dialog. <see cref="Page"/> 0, 1, 2... gives further names. Players may ask only about their own.
/// </summary>
[MessagePackObject]
public sealed record NameSuggestionsRequestMessage(
    [property: Key(0)] NameTarget Target,
    [property: Key(1)] int Id,
    [property: Key(2)] int Page,
    [property: Key(3)] int Count) : IProtocolMessage;

/// <summary>Server → client: free names in the right language, for <see cref="Id"/>.</summary>
[MessagePackObject]
public sealed record NameSuggestionsMessage(
    [property: Key(0)] NameTarget Target,
    [property: Key(1)] int Id,
    [property: Key(2)] int Page,
    [property: Key(3)] IReadOnlyList<string> Names) : IProtocolMessage;
