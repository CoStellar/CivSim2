using System;

namespace CivSim.Core.Map;

/// <summary>
/// The corners (vertices) of the hex grid, where three cells meet. Rivers flow from corner to corner along
/// edges. Every corner has one owner cell: id = cell × 2 + 0 for the upper-right corner, + 1 for the east corner.
/// </summary>
public sealed class HexCorners
{
    /// <summary>Corners owned by each cell.</summary>
    public const int PerCell = 2;

    private readonly HexTorus _map;

    /// <summary>Creates the corner graph of <paramref name="map"/>.</summary>
    public HexCorners(HexTorus map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
    }

    /// <summary>Number of corners.</summary>
    public int Count => _map.CellCount * PerCell;

    /// <summary>Upper-right corner of a cell (between its N and NE edges).</summary>
    public int UpperRight(HexCoord c) => _map.Index(c) * PerCell;

    /// <summary>East corner of a cell (between its NE and SE edges).</summary>
    public int East(HexCoord c) => (_map.Index(c) * PerCell) + 1;

    /// <summary>Writes the 3 cells that meet at <paramref name="corner"/>.</summary>
    public void Cells(int corner, Span<HexCoord> destination)
    {
        HexCoord owner = _map.FromIndex(corner / PerCell);
        destination[0] = owner;
        if (corner % PerCell == 0)
        {
            destination[1] = _map.Neighbor(owner, HexDirection.N);
            destination[2] = _map.Neighbor(owner, HexDirection.NE);
        }
        else
        {
            destination[1] = _map.Neighbor(owner, HexDirection.NE);
            destination[2] = _map.Neighbor(owner, HexDirection.SE);
        }
    }

    /// <summary>Writes the 3 neighbouring corners and the canonical edge leading to each.</summary>
    public void Neighbors(int corner, Span<int> corners, Span<HexEdge> edges)
    {
        HexCoord c = _map.FromIndex(corner / PerCell);
        if (corner % PerCell == 0)
        {
            // Upper-right: N edge → upper-left (= east corner of NW), NE edge → east, SE edge of N → east corner of N.
            corners[0] = East(_map.Neighbor(c, HexDirection.NW));
            edges[0] = _map.Edge(c, HexDirection.N);
            corners[1] = East(c);
            edges[1] = _map.Edge(c, HexDirection.NE);
            HexCoord n = _map.Neighbor(c, HexDirection.N);
            corners[2] = East(n);
            edges[2] = _map.Edge(n, HexDirection.SE);
        }
        else
        {
            // East: NE edge → upper-right, SE edge → lower-right (= upper-right of S), N edge of SE → upper-right of SE.
            corners[0] = UpperRight(c);
            edges[0] = _map.Edge(c, HexDirection.NE);
            corners[1] = UpperRight(_map.Neighbor(c, HexDirection.S));
            edges[1] = _map.Edge(c, HexDirection.SE);
            HexCoord se = _map.Neighbor(c, HexDirection.SE);
            corners[2] = UpperRight(se);
            edges[2] = _map.Edge(se, HexDirection.N);
        }
    }
}
