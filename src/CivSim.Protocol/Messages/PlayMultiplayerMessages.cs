using System.Collections.Generic;
using MessagePack;

namespace CivSim.Protocol.Messages;

/// <summary>
/// Server → client, and back unchanged: measures the time a message takes there and back (the ping in the list of players,
/// MP4). <see cref="Stamp"/> is the server's clock; the client only echoes it.
/// </summary>
[MessagePackObject]
public sealed record PlayPingMessage([property: Key(0)] long Stamp) : IProtocolMessage;

/// <summary>One player of a game with others (MP4). <see cref="SuffixLength"/>: characters the game added to a repeated nickname.</summary>
[MessagePackObject]
public sealed record PlayPlayerInfo(
    [property: Key(0)] string Name,
    [property: Key(1)] int SuffixLength,
    [property: Key(2)] int Civ,
    [property: Key(3)] int PingMs,
    [property: Key(4)] int PausesLeft,
    [property: Key(5)] bool Host,
    [property: Key(6)] int Role = PlayPlayerInfo.Player)
{
    /// <summary>Leads a civilization.</summary>
    public const int Player = 0;

    /// <summary>Joined a running game and waits for the vote on a civilization; sees no map (plan, chapter 19).</summary>
    public const int Candidate = 1;

    /// <summary>Watches the whole map and cannot take a civilization in this game (R3-7).</summary>
    public const int Observer = 2;

    /// <summary>Its civilization fell: watches the whole map (P6).</summary>
    public const int Fallen = 3;
}

/// <summary>
/// A vote going on (MP4). <see cref="Kind"/> 0: the speed (<see cref="Value"/> is the speed asked for). The change passes when
/// more than half of <see cref="Eligible"/> say yes; no vote counts as no.
/// </summary>
[MessagePackObject]
public sealed record PlayVoteInfo(
    [property: Key(0)] int Id,
    [property: Key(1)] int Kind,
    [property: Key(2)] int Value,
    [property: Key(3)] string By,
    [property: Key(4)] int SecondsLeft,
    [property: Key(5)] int Yes,
    [property: Key(6)] int No,
    [property: Key(7)] int Eligible,
    [property: Key(8)] bool CanVote,
    [property: Key(9)] bool Voted,
    [property: Key(10)] string About = "")
{
    /// <summary>The speed of the game (Value: the speed).</summary>
    public const int Speed = 0;

    /// <summary>A candidate asks to lead a civilization (Value: the civilization, By: the candidate, About: the civilization's name).</summary>
    public const int Join = 1;

    /// <summary>A player is to be removed (Value: the civilization, About: the player).</summary>
    public const int Kick = 2;
}

/// <summary>
/// Server → client: the players of a game with others, the vote going on (or null) and the pause (MP4). Sent about once a
/// second and after every change. <see cref="PausedBy"/> is empty when the game runs; <see cref="PauseProtectedSeconds"/> is how
/// long others still cannot end the pause.
/// </summary>
[MessagePackObject]
public sealed record PlayPlayersMessage(
    [property: Key(0)] IReadOnlyList<PlayPlayerInfo> Players,
    [property: Key(1)] PlayVoteInfo? Vote,
    [property: Key(2)] string PausedBy,
    [property: Key(3)] int PauseProtectedSeconds,
    [property: Key(4)] int You,
    [property: Key(5)] IReadOnlyList<PlayTakeableCiv>? Takeable = null) : IProtocolMessage;

/// <summary>A civilization led by the AI that a candidate may ask for (plan, chapter 19): what a candidate sees instead of the map.</summary>
[MessagePackObject]
public sealed record PlayTakeableCiv(
    [property: Key(0)] int Civ,
    [property: Key(1)] string Name,
    [property: Key(2)] int Settlements,
    [property: Key(3)] int People);

/// <summary>Client → server: a candidate asks to lead this civilization; the players vote (30 s).</summary>
[MessagePackObject]
public sealed record PlayJoinMessage([property: Key(0)] int Civ) : IProtocolMessage;

/// <summary>Client → server: a candidate becomes an observer of the whole map, for good in this game (R3-7).</summary>
[MessagePackObject]
public sealed record PlayWatchMessage([property: Key(0)] int Unused = 0) : IProtocolMessage;

/// <summary>
/// Client → server: remove a player. The host removes at once; another player starts a vote (30 s). The civilization goes to the AI
/// and nobody else may take it; the player may come back as a candidate, by a vote (R3-9).
/// </summary>
[MessagePackObject]
public sealed record PlayKickMessage([property: Key(0)] string Name) : IProtocolMessage;

/// <summary>Client → server: a line of chat to everyone or only to our allies (plain text, cut to <see cref="MaxLength"/>).</summary>
[MessagePackObject]
public sealed record PlayChatMessage([property: Key(0)] bool Allies, [property: Key(1)] string Text) : IProtocolMessage
{
    /// <summary>Longest line of chat.</summary>
    public const int MaxLength = 200;
}

/// <summary>
/// Server → client: a line of chat (MP4). <see cref="From"/> is empty for a line of the game itself (votes, pauses, players coming
/// and going); <see cref="FromCiv"/> is −1 then. Shown as plain text, never as markup.
/// </summary>
[MessagePackObject]
public sealed record PlayChatLineMessage(
    [property: Key(0)] string From,
    [property: Key(1)] int SuffixLength,
    [property: Key(2)] int FromCiv,
    [property: Key(3)] bool Allies,
    [property: Key(4)] string Text,
    [property: Key(5)] bool Spectators = false) : IProtocolMessage;

/// <summary>Client → server: yes or no in the vote <see cref="Id"/>.</summary>
[MessagePackObject]
public sealed record PlayVoteMessage([property: Key(0)] int Id, [property: Key(1)] bool Yes) : IProtocolMessage;

/// <summary>
/// Server → observers and fallen players: every civilization at a glance and the new lines of the intentions log (observer full view,
/// Patryk 2026-11-26). Players never get it: it shows what everyone intends.
/// </summary>
[MessagePackObject]
public sealed record PlayOverviewMessage(
    [property: Key(0)] IReadOnlyList<PlayOverviewRow> Civs,
    [property: Key(1)] IReadOnlyList<PlayIntentLine> Intents) : IProtocolMessage;

/// <summary>
/// One civilization in the observers' table. <see cref="Player"/> is the nickname of its player (null: the AI leads it). Research:
/// the technology (−1: none) and how far it is, percent. Army: units and the people in them. Stock: whole units by good index.
/// Wars, alliances and trade: the other civilizations. The capital is its biggest settlement (−1, −1: none).
/// </summary>
[MessagePackObject]
public sealed record PlayOverviewRow(
    [property: Key(0)] int Civ,
    [property: Key(1)] string Name,
    [property: Key(2)] string? Player,
    [property: Key(3)] bool Dead,
    [property: Key(4)] int People,
    [property: Key(5)] int Settlements,
    [property: Key(6)] int Era,
    [property: Key(7)] int Researching,
    [property: Key(8)] int ResearchPercent,
    [property: Key(9)] int ArmyUnits,
    [property: Key(10)] int Soldiers,
    [property: Key(11)] long[] Stock,
    [property: Key(12)] int Score,
    [property: Key(13)] int[] Wars,
    [property: Key(14)] int[] Alliances,
    [property: Key(15)] int[] Trade,
    [property: Key(16)] int CapitalCol,
    [property: Key(17)] int CapitalRow);

/// <summary>
/// A line of the intentions log: an AI decision, or a summary of a player's orders (3a: effects, not every command). Kind:
/// <see cref="War"/> … <see cref="Trade"/>.
/// </summary>
[MessagePackObject]
public sealed record PlayIntentLine(
    [property: Key(0)] long Tick,
    [property: Key(1)] int Civ,
    [property: Key(2)] bool Player,
    [property: Key(3)] int Kind,
    [property: Key(4)] string Text)
{
    /// <summary>War: starting, sparing or scouting a target, attacks.</summary>
    public const int War = 0;

    /// <summary>Peace offers and answers.</summary>
    public const int Peace = 1;

    /// <summary>Treaties.</summary>
    public const int Diplomacy = 2;

    /// <summary>Research.</summary>
    public const int Research = 3;

    /// <summary>Settlers and new settlements.</summary>
    public const int Expansion = 4;

    /// <summary>Buildings.</summary>
    public const int Building = 5;

    /// <summary>Training and moving the army.</summary>
    public const int Army = 6;

    /// <summary>Caravans, routes, barter.</summary>
    public const int Trade = 7;
}

/// <summary>Client → server, from an observer or a fallen player: watch through the eyes of civilization <see cref="Civ"/>, read only
/// (−1: the whole map again).</summary>
[MessagePackObject]
public sealed record PlayEyeMessage([property: Key(0)] int Civ) : IProtocolMessage;
