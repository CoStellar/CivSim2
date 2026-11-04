namespace CivSim.Core.Map;

/// <summary>The six neighbours / edges of a flat-top hex, clockwise from north. Values are stable (used in data).</summary>
public enum HexDirection
{
    /// <summary>North (top edge).</summary>
    N = 0,

    /// <summary>North-east.</summary>
    NE = 1,

    /// <summary>South-east.</summary>
    SE = 2,

    /// <summary>South (bottom edge).</summary>
    S = 3,

    /// <summary>South-west.</summary>
    SW = 4,

    /// <summary>North-west.</summary>
    NW = 5,
}

/// <summary>Helpers for <see cref="HexDirection"/>.</summary>
public static class HexDirections
{
    /// <summary>Number of directions.</summary>
    public const int Count = 6;

    private static readonly AxialCoord[] Vectors =
    [
        new(0, -1),  // N
        new(1, -1),  // NE
        new(1, 0),   // SE
        new(0, 1),   // S
        new(-1, 1),  // SW
        new(-1, 0),  // NW
    ];

    /// <summary>The axial step for a direction.</summary>
    public static AxialCoord Vector(this HexDirection direction) => Vectors[(int)direction];

    /// <summary>The direction pointing the other way.</summary>
    public static HexDirection Opposite(this HexDirection direction) => (HexDirection)(((int)direction + 3) % Count);

    /// <summary>The next direction clockwise.</summary>
    public static HexDirection Clockwise(this HexDirection direction) => (HexDirection)(((int)direction + 1) % Count);
}
