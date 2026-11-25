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
    [property: Key(5)] bool Host);

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
    [property: Key(9)] bool Voted);

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
    [property: Key(4)] int You) : IProtocolMessage;

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
    [property: Key(4)] string Text) : IProtocolMessage;

/// <summary>Client → server: yes or no in the vote <see cref="Id"/>.</summary>
[MessagePackObject]
public sealed record PlayVoteMessage([property: Key(0)] int Id, [property: Key(1)] bool Yes) : IProtocolMessage;
