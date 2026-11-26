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
            AttackDto a when content is not null => new Core.Units.AttackCommand(content, a.UnitId, new SettlementId(a.TargetSettlementId)),
            SetLeverDto l => new Core.Ai.SetLeverCommand((Core.Ai.Lever)(byte)l.Lever, l.Take),
            SetResearchDto r when content is not null => new Core.Research.SetResearchCommand(content, IndexOf(content.Technologies, t => t.Key == r.Technology)),
            DeclareWarDto w when content is not null && w.Goal is >= 0 and <= 2 => new Core.Diplomacy.DeclareWarCommand(content, new CivId(w.Target), (Core.Diplomacy.WarGoal)w.Goal, w.GoalSettlement),
            ProposeTreatyDto p when content is not null && p.Kind is >= 1 and <= 4 => new Core.Diplomacy.ProposeTreatyCommand(content, new CivId(p.Other), (Core.Diplomacy.TreatyKind)p.Kind),
            AnswerProposalDto a when content is not null && a.Kind is >= 1 and <= 4 => new Core.Diplomacy.AnswerProposalCommand(content, new CivId(a.From), (Core.Diplomacy.TreatyKind)a.Kind, a.Accept),
            LaunchCampaignDto c when content is not null => new Core.Episodes.LaunchCampaignCommand(content, new SettlementId(c.Home), new SettlementId(c.Target), c.Warriors),
            ChangeGovernmentDto g when content is not null => new Core.Governance.ChangeGovernmentCommand(content, IndexOf(content.Governments, x => x.Key == g.Government)),
            SettleDto s when content is not null => new Core.Economy.SettleCommand(content, new SettlementId(s.SettlementId)),
            StartTaskDto t when content is not null => new Core.Units.StartTaskCommand(content, new SettlementId(t.SettlementId), t.Kind ?? string.Empty, new HexCoord(t.Col, t.Row), t.People),
            _ => null,
        };
    }

    // Index of the first item that matches, or -1 (an unknown key: the command refuses it).
    private static int IndexOf<T>(System.Collections.Generic.IReadOnlyList<T> items, System.Func<T, bool> match)
    {
        for (int k = 0; k < items.Count; k++)
        {
            if (match(items[k]))
            {
                return k;
            }
        }

        return -1;
    }
}
