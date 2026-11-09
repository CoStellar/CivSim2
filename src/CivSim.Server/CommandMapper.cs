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
            RenameSettlementDto r => new RenameSettlementCommand(new SettlementId(r.SettlementId), r.NewName ?? string.Empty),
            MoveSettlementDto m when content is not null => new MoveSettlementCommand(content, new SettlementId(m.SettlementId), new HexCoord(m.Col, m.Row)),
            SplitSettlementDto s when content is not null => new SplitSettlementCommand(content, new SettlementId(s.SettlementId), new HexCoord(s.Col, s.Row), s.Percent),
            AbandonSettlementDto a when content is not null => new AbandonSettlementCommand(content, new SettlementId(a.SettlementId), new SettlementId(a.IntoSettlementId)),
            OccupySettlementDto o when content is not null => new OccupySettlementCommand(content, new SettlementId(o.SettlementId), new SettlementId(o.TargetSettlementId), o.Percent),
            PlunderSettlementDto p when content is not null => new PlunderSettlementCommand(content, new SettlementId(p.SettlementId), new SettlementId(p.TargetSettlementId)),
            ConquerSettlementDto c when content is not null => new ConquerSettlementCommand(content, new SettlementId(c.SettlementId), new SettlementId(c.TargetSettlementId)),
            MoveUnitDto m when content is not null => new Core.Units.MoveUnitCommand(content, m.UnitId, new HexCoord(m.Col, m.Row)),
            FoundCampDto f when content is not null => new Core.Units.FoundCampCommand(content, f.UnitId),
            ReturnUnitDto r when content is not null => new Core.Units.ReturnUnitCommand(content, r.UnitId),
            _ => null,
        };
    }
}
