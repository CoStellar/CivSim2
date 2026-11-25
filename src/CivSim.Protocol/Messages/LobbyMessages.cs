using System.Collections.Generic;
using MessagePack;

namespace CivSim.Protocol.Messages;

/// <summary>
/// One place in the lobby of a multiplayer game (MP3). <see cref="SuffixLength"/>: how many characters at the end of
/// <see cref="Name"/> the server added to a repeated nickname (with the space before them), so the lobby can show them apart.
/// </summary>
[MessagePackObject]
public sealed record LobbySeat(
    [property: Key(0)] string Name,
    [property: Key(1)] bool Ready,
    [property: Key(2)] bool Host,
    [property: Key(3)] int SuffixLength = 0);

/// <summary>
/// Server → client: the lobby as it is now (MP3). Sent after every change. <see cref="Settings"/> are lines to show, read-only
/// (the host chose them before opening the lobby); <see cref="You"/> is this client's place in <see cref="Seats"/>.
/// </summary>
[MessagePackObject]
public sealed record LobbyStateMessage(
    [property: Key(0)] string GameName,
    [property: Key(1)] IReadOnlyList<LobbySeat> Seats,
    [property: Key(2)] int HumanSeats,
    [property: Key(3)] IReadOnlyList<string> Settings,
    [property: Key(4)] IReadOnlyList<string> Chat,
    [property: Key(5)] int You,
    [property: Key(6)] bool CanStart) : IProtocolMessage;

/// <summary>Client → server: ready (or not) to start.</summary>
[MessagePackObject]
public sealed record LobbyReadyMessage([property: Key(0)] bool Ready) : IProtocolMessage;

/// <summary>Client → server: a line of chat in the lobby (plain text, cut to <see cref="MaxLength"/> characters).</summary>
[MessagePackObject]
public sealed record LobbyChatMessage([property: Key(0)] string Text) : IProtocolMessage
{
    /// <summary>Longest line of chat.</summary>
    public const int MaxLength = 200;
}

/// <summary>Client → server: the host starts the game.</summary>
[MessagePackObject]
public sealed record LobbyStartMessage([property: Key(0)] int Unused = 0) : IProtocolMessage;

/// <summary>
/// Server → client: the game starts now. The client sends <see cref="HelloMessage"/> again on the same connection (with the same
/// password) and gets its people in <see cref="WelcomeMessage"/>.
/// </summary>
[MessagePackObject]
public sealed record GameStartingMessage([property: Key(0)] int Unused = 0) : IProtocolMessage;
