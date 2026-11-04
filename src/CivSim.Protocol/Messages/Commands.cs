using MessagePack;

namespace CivSim.Protocol.Messages;

/// <summary>
/// Wire form of a game command. The server turns it into a core command and checks it.
/// Union keys are part of the wire format: never reuse or renumber them.
/// </summary>
[Union(0, typeof(RenameCivilizationDto))]
public interface ICommandDto
{
}

/// <summary>Rename the player's civilization.</summary>
[MessagePackObject]
public sealed record RenameCivilizationDto([property: Key(0)] string NewName) : ICommandDto;
