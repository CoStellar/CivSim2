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

    /// <summary>Cell on Earth where the tribe starts (chosen by the world generator).</summary>
    public HexCoord StartCell { get; set; }

    /// <summary>Current era id (index into <c>GameContent.Eras</c>); everyone starts in the Stone Age (0).</summary>
    public int Era { get; set; }
}
