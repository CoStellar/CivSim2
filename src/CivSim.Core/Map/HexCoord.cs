namespace CivSim.Core.Map;

/// <summary>
/// A cell position as stored in memory: column and row of the flat-top "odd-q" layout, where odd columns are
/// shifted half a hex down. Coordinates produced by <see cref="HexTorus"/> are always wrapped into the map.
/// </summary>
public readonly record struct HexCoord(int Col, int Row)
{
    /// <inheritdoc />
    public override string ToString() => $"({Col},{Row})";
}

/// <summary>
/// Axial hex coordinates (q, r) for flat-top hexes, used for math (directions, distances, lines).
/// Axial values of the same cell are not unique on a torus; use <see cref="HexTorus.FromAxial"/> to wrap them.
/// </summary>
public readonly record struct AxialCoord(int Q, int R)
{
    /// <summary>The third cube coordinate, s = -q - r.</summary>
    public int S => -Q - R;

    /// <summary>Adds two axial vectors.</summary>
    public static AxialCoord operator +(AxialCoord a, AxialCoord b) => new(a.Q + b.Q, a.R + b.R);

    /// <summary>Subtracts two axial vectors.</summary>
    public static AxialCoord operator -(AxialCoord a, AxialCoord b) => new(a.Q - b.Q, a.R - b.R);

    /// <summary>Scales an axial vector.</summary>
    public static AxialCoord operator *(AxialCoord a, int k) => new(a.Q * k, a.R * k);

    /// <summary>Hex distance on an infinite plane.</summary>
    public static int PlaneDistance(AxialCoord a, AxialCoord b)
    {
        AxialCoord d = a - b;
        return (System.Math.Abs(d.Q) + System.Math.Abs(d.R) + System.Math.Abs(d.S)) / 2;
    }
}
