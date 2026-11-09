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
