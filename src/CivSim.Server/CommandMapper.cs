using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.State;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>Translates wire commands into core commands. Unknown commands are rejected, never guessed.</summary>
public static class CommandMapper
{
    /// <summary>
    /// Returns the core command for <paramref name="dto"/>, or null when the type is unknown (or needs game content
    /// the server does not have).
    /// </summary>
    public static ICommand? ToCore(ICommandDto? dto, GameContent? content = null)
    {
        return dto switch
        {
            RenameCivilizationDto r => new RenameCivilizationCommand(r.NewName ?? string.Empty),
            MoveSettlementDto m when content is not null => new MoveSettlementCommand(content, new SettlementId(m.SettlementId), new HexCoord(m.Col, m.Row)),
            SplitSettlementDto s when content is not null => new SplitSettlementCommand(content, new SettlementId(s.SettlementId), new HexCoord(s.Col, s.Row), s.Percent),
            _ => null,
        };
    }
}
