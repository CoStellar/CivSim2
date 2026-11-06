namespace CivSim.Core.Content;

/// <summary>
/// A skill from <c>data/skills.json</c> (stage 4F, ADR 0020, GDD 5.1): what people of one stratum know. People of a
/// stratum without any skill are its plain members (gatherers, warriors).
/// </summary>
/// <param name="Id">Dense id from 0, the index into every skill array of a settlement.</param>
/// <param name="Key">Stable key, e.g. "potter".</param>
/// <param name="Name">Display name.</param>
/// <param name="StratumId">Stratum the skilled people belong to.</param>
/// <param name="TechnologyId">Technology that opens the skill.</param>
/// <param name="TrainingYears">Years an apprentice learns before the skill is theirs.</param>
public sealed record SkillDefinition(int Id, string Key, string Name, int StratumId, int TechnologyId, int TrainingYears);
