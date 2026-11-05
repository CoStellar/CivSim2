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

    /// <summary>Value of <see cref="Researching"/> when nothing is researched.</summary>
    public const int NoResearch = -1;

    /// <summary>True when the technology is known.</summary>
    public bool Knows(int technology) => technology >= 0 && technology < KnownTechnologies.Length && KnownTechnologies[technology];
}
