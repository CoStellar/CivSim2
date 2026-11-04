using CivSim.Core.Commands;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>Translates wire commands into core commands. Unknown commands are rejected, never guessed.</summary>
public static class CommandMapper
{
    /// <summary>Returns the core command for <paramref name="dto"/>, or null when the type is unknown.</summary>
    public static ICommand? ToCore(ICommandDto? dto)
    {
        return dto switch
        {
            RenameCivilizationDto r => new RenameCivilizationCommand(r.NewName ?? string.Empty),
            _ => null,
        };
    }
}
