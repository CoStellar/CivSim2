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

    /// <summary>Value of <see cref="ExpansionTarget"/> when there is no direction.</summary>
    public const int NoTarget = -1;

    /// <summary>A trait, per mille; average when traits were never rolled.</summary>
    public int Trait(Ai.Trait trait) => (int)trait < Traits.Length ? Traits[(int)trait] : Ai.Character.Average;

    /// <summary>The strategist's weight for a goal, per mille; neutral before the first review.</summary>
    public int GoalWeight(Ai.Goal goal) => (int)goal < GoalWeights.Length ? GoalWeights[(int)goal] : 1000;

    /// <summary>True when the technology is known.</summary>
    public bool Knows(int technology) => technology >= 0 && technology < KnownTechnologies.Length && KnownTechnologies[technology];
}
