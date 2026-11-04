namespace CivSim.Core.State;

/// <summary>One world (planet). The map itself is added in Stage 1.</summary>
public sealed class WorldState
{
    /// <summary>Creates a world.</summary>
    public WorldState(WorldId id, string name, int width, int height)
    {
        Id = id;
        Name = name;
        Width = width;
        Height = height;
    }

    /// <summary>World id; Earth is 0.</summary>
    public WorldId Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Map width in hexes.</summary>
    public int Width { get; }

    /// <summary>Map height in hexes (always even, see CLAUDE.md rule 8).</summary>
    public int Height { get; }
}
