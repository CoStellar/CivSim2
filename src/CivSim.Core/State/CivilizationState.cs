using System;
using System.Collections.Generic;
using CivSim.Core.Map;

namespace CivSim.Core.State;

/// <summary>One civilization. Economy, population and AI state are added in later stages.</summary>
public sealed class CivilizationState
{
    /// <summary>Creates a civilization.</summary>
    public CivilizationState(CivId id, string name, PlayerId controller)
    {
        Id = id;
        Name = name;
        Controller = controller;
        IsAlive = true;
    }

    /// <summary>Civilization id.</summary>
    public CivId Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; set; }

    /// <summary>Who issues this civilization's commands (<see cref="PlayerId.Ai"/> for AI).</summary>
    public PlayerId Controller { get; set; }

    /// <summary>False once the civilization has been destroyed.</summary>
    public bool IsAlive { get; set; }

    /// <summary>Game month in which the civilization died out (meaningful when not alive).</summary>
    public long DiedOutMonth { get; set; }

    /// <summary>Cell on Earth where the tribe starts (chosen by the world generator).</summary>
    public HexCoord StartCell { get; set; }

    /// <summary>Current era id (index into <c>GameContent.Eras</c>); everyone starts in the Stone Age (0).</summary>
    public int Era { get; set; }

    /// <summary>Technologies known, by technology id (GDD 6; stage 3f). Grows to the number of technologies on first use.</summary>
    public bool[] KnownTechnologies { get; set; } = [];

    /// <summary>Number of technologies known (changes when one is learned; caches key on it).</summary>
    public int KnownCount { get; set; }

    /// <summary>Technology being researched, or <see cref="NoResearch"/>.</summary>
    public int Researching { get; set; } = NoResearch;

    /// <summary>Research points collected towards <see cref="Researching"/>, hundredths.</summary>
    public long ResearchProgress { get; set; }

    /// <summary>Research points gained in the last turn, hundredths (for reports).</summary>
    public long LastScience { get; set; }

    /// <summary>
    /// Map memory of Earth (GDD 8.2, stage 3g): per cell the game year it was last seen plus one, 0 = never seen.
    /// Grows to the number of cells on first use.
    /// </summary>
    public int[] SeenYear { get; set; } = [];

    /// <summary>Character traits, per mille, indexed by <see cref="Ai.Trait"/> (GDD 8.4; stage 4b). Empty = all average.</summary>
    public int[] Traits { get; set; } = [];

    /// <summary>Goal weights set by the strategist, per mille, indexed by <see cref="Ai.Goal"/> (GDD 8.1). Empty = all neutral.</summary>
    public int[] GoalWeights { get; set; } = [];

    /// <summary>Earth cell the strategist points expansion towards (GDD 7.6), or <see cref="NoTarget"/>.</summary>
    public int ExpansionTarget { get; set; } = NoTarget;

    /// <summary>Game month from which the strategist reviews the goals again.</summary>
    public long StrategyReviewMonth { get; set; }

    /// <summary>Seed of the people's language (GDD 5.6); every name of the civilization comes from it.</summary>
    public ulong LanguageSeed { get; set; }

    /// <summary>Names the language has handed out so far (the next name's number).</summary>
    public long NamesIssued { get; set; }

    /// <summary>Era of the last strategy review: a new era is a shock that brings a review at once.</summary>
    public int StrategyEra { get; set; } = -1;

    /// <summary>Game month in which this civilization's next own step starts (ADR 0018). 0 for a new one: it steps at once.</summary>
    public long NextStepMonth { get; set; }

    /// <summary>Game month in which its last own step started.</summary>
    public long StepStartMonth { get; set; }

    /// <summary>
    /// Months this civilization computes in the current world step: the length of its own step when it steps now, else 0
    /// (set by <see cref="Simulation.WorldClock.Begin"/>).
    /// </summary>
    public int StepMonths { get; set; }

    /// <summary>Own steps taken since the start (the counter of its own rhythm).</summary>
    public long Steps { get; set; }

    /// <summary>The form of government, index into <see cref="Content.GameContent.Governments"/> (GDD 7.2; stage 4f).</summary>
    public int Government { get; set; }

    /// <summary>The government being brought in (GDD 7.2: enactment), or <see cref="NoGovernment"/> when none.</summary>
    public int PendingGovernment { get; set; } = NoGovernment;

    /// <summary>The month <see cref="PendingGovernment"/> takes effect.</summary>
    public long PendingGovernmentMonth { get; set; }

    /// <summary>The leader's name, from the people's language (GDD 7.5; stage 4f PR 2); empty before the first leader.</summary>
    public string LeaderName { get; set; } = string.Empty;

    /// <summary>The month the leader was born.</summary>
    public long LeaderBornMonth { get; set; }

    /// <summary>The month the leader came to power.</summary>
    public long LeaderSinceMonth { get; set; }

    /// <summary>The leader's traits, indexes into <see cref="Content.LeaderRules.Traits"/>.</summary>
    public int[] LeaderTraits { get; set; } = [];

    /// <summary>
    /// What the leader adds to each character trait, per mille (index = <see cref="Ai.Trait"/>): the sum of the shifts of
    /// <see cref="LeaderTraits"/>, kept so that the character can be read without the game content.
    /// </summary>
    public int[] LeaderShift { get; set; } = [];

    /// <summary>How many leaders this civilization has had (also numbers their names).</summary>
    public long Leaders { get; set; }

    /// <summary>The month a succession crisis ends (stability, stage 4f PR 3); 0 or past when there is none.</summary>
    public long CrisisUntilMonth { get; set; }

    /// <summary>The capital, a settlement id (GDD 7.3; stage 4f PR 3), or <see cref="NoCapital"/> when none.</summary>
    public int Capital { get; set; } = NoCapital;

    /// <summary>No capital.</summary>
    public const int NoCapital = -1;

    /// <summary>Stability 0–100 (GDD 7.3), computed in each own step.</summary>
    public int Stability { get; set; } = 50;

    /// <summary>Administration points the settlements use (GDD 7.3), computed in each own step.</summary>
    public int AdminUsed { get; set; }

    /// <summary>The month anarchy after a change of government ends (GDD 7.2); 0 or past when there is none.</summary>
    public long AnarchyUntilMonth { get; set; }

    /// <summary>The month the loss of the capital stops lowering stability; 0 or past when it never fell.</summary>
    public long CapitalLostUntilMonth { get; set; }

    /// <summary>Reasons to decide before its next own step (reactive decisions, ADR 0018).</summary>
    public Simulation.Alarm Alarms { get; set; }

    /// <summary>
    /// Civilizations this one knows of, sorted by their id (GDD 8.5; stage 4c). Empty at the start: nobody knows anybody.
    /// </summary>
    public List<Diplomacy.Relation> Relations { get; } = [];

    /// <summary>The relation with <paramref name="other"/>, or null when this civilization has never heard of it.</summary>
    public Diplomacy.Relation? Relation(CivId other)
    {
        int k = RelationIndex(other);
        return k >= 0 ? Relations[k] : null;
    }

    /// <summary>The relation with <paramref name="other"/>, added (in id order) when it is new.</summary>
    public Diplomacy.Relation AddRelation(CivId other)
    {
        int k = RelationIndex(other);
        if (k >= 0)
        {
            return Relations[k];
        }

        var relation = new Diplomacy.Relation(other);
        Relations.Insert(~k, relation);
        return relation;
    }

    // Binary search by the other civilization's id; the complement of the insertion point when missing.
    private int RelationIndex(CivId other)
    {
        int lo = 0;
        int hi = Relations.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int v = Relations[mid].Other.Value;
            if (v == other.Value)
            {
                return mid;
            }

            if (v < other.Value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return ~lo;
    }

    /// <summary>Value of <see cref="Researching"/> when nothing is researched.</summary>
    public const int NoResearch = -1;

    /// <summary>Value of <see cref="PendingGovernment"/> when no change of government is under way.</summary>
    public const int NoGovernment = -1;

    /// <summary>Value of <see cref="ExpansionTarget"/> when there is no direction.</summary>
    public const int NoTarget = -1;

    /// <summary>The people's own trait, per mille, without the leader (average when never rolled).</summary>
    public int OwnTrait(Ai.Trait trait) => (int)trait < Traits.Length ? Traits[(int)trait] : Ai.Character.Average;

    /// <summary>
    /// A trait, per mille: the people's own (<see cref="Traits"/>, average when never rolled) plus what the present
    /// leader adds (GDD 8.4), kept within 0–1000.
    /// </summary>
    public int Trait(Ai.Trait trait)
    {
        int own = OwnTrait(trait);
        int shift = (int)trait < LeaderShift.Length ? LeaderShift[(int)trait] : 0;
        return shift == 0 ? own : Math.Clamp(own + shift, 0, 1000);
    }

    /// <summary>The strategist's weight for a goal, per mille; neutral before the first review.</summary>
    public int GoalWeight(Ai.Goal goal) => (int)goal < GoalWeights.Length ? GoalWeights[(int)goal] : 1000;

    /// <summary>True when the technology is known.</summary>
    public bool Knows(int technology) => technology >= 0 && technology < KnownTechnologies.Length && KnownTechnologies[technology];
}
