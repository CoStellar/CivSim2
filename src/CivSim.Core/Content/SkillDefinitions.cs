using System.Collections.Generic;

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
public sealed record SkillDefinition(int Id, string Key, string Name, int StratumId, int TechnologyId, int TrainingYears)
{
    /// <summary>Share of the stratum's people of a settlement the skill can use, percent.</summary>
    public int JobsPercent { get; init; } = 30;
}

/// <summary>How people learn and pass on skills (<c>data/skills.json</c> rules, stage 4F PR 2, ADR 0020).</summary>
public sealed record SkillRules
{
    /// <summary>Masters who appear when a civilization learns the technology of a skill.</summary>
    public int FirstMasters { get; init; } = 2;

    /// <summary>Apprentices one skilled person teaches at a time.</summary>
    public int ApprenticesPerMaster { get; init; } = 3;

    /// <summary>Work of an apprentice against a skilled worker, percent.</summary>
    public int ApprenticeWorkPercent { get; init; } = 50;

    /// <summary>Skilled people a settlement keeps before any move away.</summary>
    public int MoveMinMasters { get; init; } = 3;

    /// <summary>Skilled people above what the settlement can use before any move away.</summary>
    public int MoveSpareAbove { get; init; } = 2;

    /// <summary>Share of the spare skilled people that moves at most once per own step, percent.</summary>
    public int MoveSharePercent { get; init; } = 50;

    /// <summary>
    /// While the civilization has settlements that need a skill and have no master of it, masters teach this much more
    /// than their own settlement can use (percent), plus <see cref="MoveSpareAbove"/>, so that skilled people can move.
    /// </summary>
    public int TeachForOthersPercent { get; init; } = 50;

    /// <summary>Skill whose people farm a village's fields (-1: every producer farms, as before stage 4F).</summary>
    public int FarmingSkill { get; init; } = -1;
}

/// <summary>Which skill works which recipe and which technology's gathering (derived from <c>data/skills.json</c>).</summary>
public sealed record SkillLinks(IReadOnlyList<int> SkillOfRecipe, IReadOnlyList<int> SkillOfTechnology)
{
    /// <summary>No skills: every recipe and technology works without skilled people.</summary>
    public static SkillLinks None { get; } = new([], []);

    /// <summary>The skill that works <paramref name="recipe"/>, or -1.</summary>
    public int ForRecipe(int recipe) => recipe >= 0 && recipe < SkillOfRecipe.Count ? SkillOfRecipe[recipe] : -1;

    /// <summary>The skill opened by <paramref name="technology"/>, or -1.</summary>
    public int ForTechnology(int technology) => technology >= 0 && technology < SkillOfTechnology.Count ? SkillOfTechnology[technology] : -1;
}
