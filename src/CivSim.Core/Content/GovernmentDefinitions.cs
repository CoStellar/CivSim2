using System;
using System.Collections.Generic;

namespace CivSim.Core.Content;

/// <summary>
/// One form of government (GDD 7.2; stage 4f, ADR 0023), from <c>data/governments.json</c>. It sets how fast the state
/// acts (the delay before its decisions take effect and how often its advisors review), how impulsive its AI is and which
/// goals it favours, and the title the civilization carries with it ("Chiefdom of Karrut").
/// </summary>
public sealed record GovernmentDefinition(int Id, string Key, string Name)
{
    /// <summary>The title, with <c>{people}</c> standing for the people's name, e.g. "Chiefdom of {people}".</summary>
    public string Title { get; init; } = "{people}";

    /// <summary>The first era (index into <see cref="GameContent.Eras"/>) in which a civilization may take this government.</summary>
    public int FromEra { get; init; }

    /// <summary>Years before a state decision (a new government, a declared war) takes effect (GDD 7.2: enactment).</summary>
    public int EnactYears { get; init; }

    /// <summary>The strategist and the diplomat review this many percent of their usual interval (GDD 7.2: pace of the AI).</summary>
    public int ReviewPercent { get; init; } = 100;

    /// <summary>
    /// Added to the civilization's caution, per mille, when the AI decides whether to take its best option outright
    /// (GDD 7.2: a despot reacts impulsively, a council steadily).
    /// </summary>
    public int CautionShift { get; init; }

    /// <summary>Percent applied to each goal weight the strategist sets, index = goal (<see cref="Ai.Goal"/>); 100 = no change.</summary>
    public IReadOnlyList<int> GoalPercent { get; init; } = [];

    /// <summary>How much each trait (index = <see cref="Ai.Trait"/>) draws a new people to this government at the start of the game.</summary>
    public IReadOnlyList<int> Leaning { get; init; } = [];

    /// <summary>The goal percent for <paramref name="goal"/> (100 when not set).</summary>
    public int PercentFor(Ai.Goal goal) => (int)goal < GoalPercent.Count ? GoalPercent[(int)goal] : 100;

    /// <summary>The title of a people called <paramref name="people"/> under this government.</summary>
    public string TitleOf(string people) => Title.Replace("{people}", people, StringComparison.Ordinal);
}
