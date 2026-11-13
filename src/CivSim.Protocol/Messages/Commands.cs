using MessagePack;

namespace CivSim.Protocol.Messages;

/// <summary>
/// Wire form of a game command. The server turns it into a core command and checks it.
/// Union keys are part of the wire format: never reuse or renumber them.
/// </summary>
[Union(0, typeof(RenameCivilizationDto))]
[Union(1, typeof(MoveSettlementDto))]
[Union(2, typeof(SplitSettlementDto))]
[Union(3, typeof(AbandonSettlementDto))]
[Union(4, typeof(OccupySettlementDto))]
[Union(5, typeof(PlunderSettlementDto))]
[Union(6, typeof(ConquerSettlementDto))]
[Union(7, typeof(RenameSettlementDto))]
[Union(8, typeof(MoveUnitDto))]
[Union(9, typeof(FoundCampDto))]
[Union(10, typeof(ReturnUnitDto))]
[Union(11, typeof(AttackDto))]
[Union(12, typeof(StartTaskDto))]
[Union(13, typeof(SetLeverDto))]
[Union(14, typeof(SetResearchDto))]
[Union(15, typeof(DeclareWarDto))]
[Union(16, typeof(ProposeTreatyDto))]
[Union(17, typeof(AnswerProposalDto))]
[Union(18, typeof(LaunchCampaignDto))]
[Union(19, typeof(ChangeGovernmentDto))]
[Union(20, typeof(SettleDto))]
public interface ICommandDto
{
}

/// <summary>Rename a settlement of the player's civilization (GDD 5.6). Observers may rename any settlement.</summary>
[MessagePackObject]
public sealed record RenameSettlementDto([property: Key(0)] int SettlementId, [property: Key(1)] string NewName) : ICommandDto;

/// <summary>Rename the player's civilization. Observers may rename any civilization (the target is the message's civilization id).</summary>
[MessagePackObject]
public sealed record RenameCivilizationDto([property: Key(0)] string NewName) : ICommandDto;

/// <summary>Move a camp to another cell (GDD 5.4).</summary>
[MessagePackObject]
public sealed record MoveSettlementDto([property: Key(0)] int SettlementId, [property: Key(1)] int Col, [property: Key(2)] int Row) : ICommandDto;

/// <summary>Send part of a settlement's people to found a new camp (GDD 5.4).</summary>
[MessagePackObject]
public sealed record SplitSettlementDto([property: Key(0)] int SettlementId, [property: Key(1)] int Col, [property: Key(2)] int Row, [property: Key(3)] int Percent) : ICommandDto;

/// <summary>Everyone leaves a settlement and joins another one of the same civilization (GDD 5.5).</summary>
[MessagePackObject]
public sealed record AbandonSettlementDto([property: Key(0)] int SettlementId, [property: Key(1)] int IntoSettlementId) : ICommandDto;

/// <summary>Send part (or all) of a settlement's people into an abandoned settlement or ruins (GDD 5.5).</summary>
[MessagePackObject]
public sealed record OccupySettlementDto([property: Key(0)] int SettlementId, [property: Key(1)] int TargetSettlementId, [property: Key(2)] int Percent) : ICommandDto;

/// <summary>Take what was left in an abandoned settlement or ruins (GDD 5.5).</summary>
[MessagePackObject]
public sealed record PlunderSettlementDto([property: Key(0)] int SettlementId, [property: Key(1)] int TargetSettlementId) : ICommandDto;

/// <summary>Attack a camp of another civilization and take its land (GDD 5.4: migration with conquest).</summary>
[MessagePackObject]
public sealed record ConquerSettlementDto([property: Key(0)] int SettlementId, [property: Key(1)] int TargetSettlementId) : ICommandDto;

/// <summary>Send a unit to a known cell (stage 6 PR 3a).</summary>
[MessagePackObject]
public sealed record MoveUnitDto([property: Key(0)] long UnitId, [property: Key(1)] int Col, [property: Key(2)] int Row) : ICommandDto;

/// <summary>Settlers found a camp where they stand (stage 6 PR 3a).</summary>
[MessagePackObject]
public sealed record FoundCampDto([property: Key(0)] long UnitId) : ICommandDto;

/// <summary>A unit goes back to its home settlement and joins it (stage 6 PR 3a).</summary>
[MessagePackObject]
public sealed record ReturnUnitDto([property: Key(0)] long UnitId) : ICommandDto;

/// <summary>Send the army of a campaign against another settlement of the same enemy (stage 6 PR 3b).</summary>
[MessagePackObject]
public sealed record AttackDto([property: Key(0)] long UnitId, [property: Key(1)] int TargetSettlementId) : ICommandDto;

/// <summary>
/// Start an area task (stage 6 PR 4a, GDD 4.4): the settlement sends a work group of <c>People</c> people (0 = the default size) to the cell
/// to do the task <c>Kind</c> (a key of tasks.json, e.g. <c>woodcutting</c>). Call the group back with <see cref="ReturnUnitDto"/>.
/// </summary>
[MessagePackObject]
public sealed record StartTaskDto([property: Key(0)] int SettlementId, [property: Key(1)] string Kind, [property: Key(2)] int Col, [property: Key(3)] int Row, [property: Key(4)] int People = 0) : ICommandDto;

/// <summary>
/// The player takes a lever from the AI (<c>Take</c> true) or gives it back (ADR 0027, stage 6 PR P). <c>Lever</c> is one flag of
/// <see cref="LeverFlags"/>. The AI decides every lever the player does not hold.
/// </summary>
[MessagePackObject]
public sealed record SetLeverDto([property: Key(0)] int Lever, [property: Key(1)] bool Take) : ICommandDto;

/// <summary>Lever 2 (stage 6 PR P1): research <c>Technology</c> next (a key of technologies.json). The AI's scholar keeps the choice until it is learned.</summary>
[MessagePackObject]
public sealed record SetResearchDto([property: Key(0)] string Technology) : ICommandDto;

/// <summary>
/// Lever 4 (stage 6 PR P1): declare war on civilization <c>Target</c> with a goal: 0 land (<c>GoalSettlement</c> = the settlement wanted),
/// 1 loot, 2 revenge (war.json).
/// </summary>
[MessagePackObject]
public sealed record DeclareWarDto([property: Key(0)] int Target, [property: Key(1)] int Goal = 2, [property: Key(2)] int GoalSettlement = -1) : ICommandDto;

/// <summary>Lever 4: propose a treaty to <c>Other</c>: 1 peace (white), 2 trade, 3 non-aggression, 4 defensive alliance (diplomacy.json).</summary>
[MessagePackObject]
public sealed record ProposeTreatyDto([property: Key(0)] int Other, [property: Key(1)] int Kind) : ICommandDto;

/// <summary>Lever 4: accept or refuse the pending proposal of <c>From</c> of treaty <c>Kind</c> (see <see cref="ProposalView"/>).</summary>
[MessagePackObject]
public sealed record AnswerProposalDto([property: Key(0)] int From, [property: Key(1)] int Kind, [property: Key(2)] bool Accept) : ICommandDto;

/// <summary>Lever 4: send <c>Warriors</c> warriors of settlement <c>Home</c> on a campaign against settlement <c>Target</c> of an enemy at war.</summary>
[MessagePackObject]
public sealed record LaunchCampaignDto([property: Key(0)] int Home, [property: Key(1)] int Target, [property: Key(2)] int Warriors) : ICommandDto;

/// <summary>Lever 5 (until cards, PR 7): bring in government <c>Government</c> (a key of governments.json) open in the civilization's era.</summary>
[MessagePackObject]
public sealed record ChangeGovernmentDto([property: Key(0)] string Government) : ICommandDto;

/// <summary>Lever 1: a camp of farmers settles down as a village where it stands (GDD 5.4).</summary>
[MessagePackObject]
public sealed record SettleDto([property: Key(0)] int SettlementId) : ICommandDto;
