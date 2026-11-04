namespace CivSim.Core.Map;

/// <summary>
/// An edge between two neighbouring cells, in canonical form: every edge is owned by exactly one cell through
/// one of its N, NE or SE sides. Rivers live on edges (GDD 3). Create edges with <see cref="HexTorus.Edge"/>.
/// </summary>
public readonly record struct HexEdge(HexCoord Cell, HexDirection Side)
{
    /// <summary>Number of canonical edges per cell (N, NE, SE).</summary>
    public const int PerCell = 3;
}
