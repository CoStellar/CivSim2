using CivSim.Core.Map;

namespace CivSim.Core.State;

/// <summary>One world (planet): its geometry and its per-cell map data.</summary>
public sealed class WorldState
{
    /// <summary>Creates a world.</summary>
    public WorldState(WorldId id, string name, int width, int height)
    {
        Id = id;
        Name = name;
        Width = width;
        Height = height;
        Geometry = new HexGlobe(width, height);
        Map = new WorldMap(Geometry);
    }

    /// <summary>World id; Earth is 0.</summary>
    public WorldId Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Map width in hexes.</summary>
    public int Width { get; }

    /// <summary>Map height in hexes (always even, see CLAUDE.md rule 8).</summary>
    public int Height { get; }

    /// <summary>Neighbours, distances and areas on this world.</summary>
    public HexGlobe Geometry { get; }

    /// <summary>Per-cell data (elevation, biome, rivers...).</summary>
    public WorldMap Map { get; }
}
