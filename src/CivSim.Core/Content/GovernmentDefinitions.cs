using System;
using System.Collections.Generic;

namespace CivSim.Core.Content;

/// <summary>How a government chooses its next leader (GDD 7.5; stage 4f PR 2). Values are part of the data format.</summary>
public enum Succession : byte
{
    /// <summary>The council picks one of several candidates, the one closest to the people's character; no crisis.</summary>
    Council = 0,

    /// <summary>The strongest warrior takes over; the fight for it may bring a crisis.</summary>
    Strongest = 1,

    /// <summary>The heir inherits, whatever the heir is like; a weak heir may bring a crisis.</summary>
    Hereditary = 2,

    /// <summary>Elected for a term; traits change every term.</summary>
    Election = 3,
}

/// <summary>A trait a leader may have (GDD 7.5): it shifts the civilization's character while the leader rules.</summary>
public sealed record LeaderTraitDefinition(int Id, string Key, string Name)
{
    /// <summary>Per mille added to each character trait (index = <see cref="Ai.Trait"/>) while the leader rules.</summary>
    public IReadOnlyList<int> Shifts { get; init; } = [];
}

/// <summary>Rules of leaders (<c>data/leaders.json</c>; GDD 7.5, stage 4f PR 2).</summary>
public sealed record LeaderRules
{
    /// <summary>Leader traits, index = id.</summary>
    public IReadOnlyList<LeaderTraitDefinition> Traits { get; init; } = [];

    /// <summary>How many traits a leader has.</summary>
    public (int Min, int Max) TraitCount { get; init; } = (2, 3);

    /// <summary>Age range of the first leaders, in years.</summary>
    public (int Min, int Max) StartAge { get; init; } = (25, 45);

    /// <summary>Candidates a council weighs when it picks a leader.</summary>
    public int CouncilCandidates { get; init; } = 3;

    /// <summary>Chance of dying in a year, per mille, by age: points (age, per mille), linear between them.</summary>
    public IReadOnlyList<(int Age, int PerMille)> DeathByAge { get; init; } = [(20, 10), (80, 250)];

    /// <summary>The chance of dying in a year at <paramref name="age"/>, per mille.</summary>
    public int DeathPerMille(int age)
    {
        if (DeathByAge.Count == 0)
        {
            return 0;
        }

        if (age <= DeathByAge[0].Age)
        {
            return DeathByAge[0].PerMille;
        }

        for (int k = 1; k < DeathByAge.Count; k++)
        {
            (int a1, int p1) = DeathByAge[k];
            if (age <= a1)
            {
                (int a0, int p0) = DeathByAge[k - 1];
                return p0 + (int)((long)(p1 - p0) * (age - a0) / Math.Max(1, a1 - a0));
            }
        }

        return Math.Min(1000, DeathByAge[^1].PerMille);
    }
}

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

    /// <summary>How the leader is styled, e.g. "Chief" (stage 4f PR 2).</summary>
    public string LeaderTitle { get; init; } = "Leader";

    /// <summary>How a new leader is chosen (GDD 7.5).</summary>
    public Succession Succession { get; init; }

    /// <summary>The age range of a new leader, in years.</summary>
    public (int Min, int Max) SuccessorAge { get; init; } = (25, 45);

    /// <summary>Chance, percent, that a change of leader brings a succession crisis.</summary>
    public int CrisisPercent { get; init; }

    /// <summary>Years a succession crisis lasts (it lowers stability, stage 4f PR 3).</summary>
    public int CrisisYears { get; init; }

    /// <summary>Years an elected leader rules before the next election; 0 = for life.</summary>
    public int TermYears { get; init; }

    /// <summary>Stability this government adds (or takes away when negative), points (GDD 7.3; stage 4f PR 3).</summary>
    public int StabilityBonus { get; init; }

    /// <summary>Administration points this government can manage before the state is overloaded (GDD 7.3).</summary>
    public int Administration { get; init; } = 100;

    /// <summary>Political influence of each stratum (index = stratum id) in stability (GDD 7.3); 1 when not set.</summary>
    public IReadOnlyList<int> StrataInfluence { get; init; } = [];

    /// <summary>The influence of stratum <paramref name="stratum"/> (1 when not set).</summary>
    public int InfluenceOf(int stratum) => stratum < StrataInfluence.Count ? StrataInfluence[stratum] : 1;

    /// <summary>The goal percent for <paramref name="goal"/> (100 when not set).</summary>
    public int PercentFor(Ai.Goal goal) => (int)goal < GoalPercent.Count ? GoalPercent[(int)goal] : 100;

    /// <summary>The title of a people called <paramref name="people"/> under this government.</summary>
    public string TitleOf(string people) => Title.Replace("{people}", people, StringComparison.Ordinal);
}
