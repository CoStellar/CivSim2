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
public interface IProtocolMessage
{
}

/// <summary>Client → server: first message after connecting.</summary>
[MessagePackObject]
public sealed record HelloMessage([property: Key(0)] int ProtocolVersion, [property: Key(1)] string PlayerName) : IProtocolMessage;

/// <summary>Server → client: connection accepted; tells the client who it is and which civilization it leads.</summary>
[MessagePackObject]
public sealed record WelcomeMessage([property: Key(0)] int ProtocolVersion, [property: Key(1)] int PlayerId, [property: Key(2)] int CivId) : IProtocolMessage;

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
    [property: Key(3)] IReadOnlyList<CivilizationView> Civilizations) : IProtocolMessage;

/// <summary>Public data about one world.</summary>
[MessagePackObject]
public sealed record WorldView([property: Key(0)] int Id, [property: Key(1)] string Name, [property: Key(2)] int Width, [property: Key(3)] int Height);

/// <summary>What the viewer knows about one civilization.</summary>
[MessagePackObject]
public sealed record CivilizationView([property: Key(0)] int Id, [property: Key(1)] string Name, [property: Key(2)] bool IsAlive);
