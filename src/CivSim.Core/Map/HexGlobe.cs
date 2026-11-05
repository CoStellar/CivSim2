using System;
using System.Collections.Generic;

namespace CivSim.Core.Map;

/// <summary>
/// Geometry of one world: flat-top hexes on a globe (ADR 0012). The map is a cylinder that wraps east–west; row 0
/// touches the north pole and the last row the south pole. Walking north across the pole you come down on the other
/// side of the planet: the cell beyond the top of column c is in column c + Width/2, and north and south swap there
/// (a glide reflection of the hex lattice). Rows are equal-area latitude bands (<see cref="SinLatitude"/>).
/// Every neighbour, distance, line and area on the map must go through this class (CLAUDE.md rule 8).
/// Storage order is row-major: <c>index = row * Width + col</c>. Methods do not allocate unless they return a list.
/// </summary>
public sealed class HexGlobe
{
    /// <summary>Creates the geometry. The width must be 2 more than a multiple of 4 (6, 10…, 258) and the height even.</summary>
    /// <remarks>
    /// Even width is required because odd columns are shifted half a row down. Half the width must be odd so that
    /// crossing a pole lands on a column of the other parity: only then do the shifted and unshifted columns meet
    /// over the pole without a gap or overlap. Even height keeps the equator on a row boundary.
    /// </remarks>
    public HexGlobe(int width, int height)
    {
        if (!IsValidSize(width, height))
        {
            throw new ArgumentException($"Map size {width}×{height} is invalid: the width must be 2 more than a multiple of 4 (at least 6) and the height even (at least 4).");
        }

        Width = width;
        Height = height;
    }

    /// <summary>True when a map of this size can be built (see the constructor).</summary>
    public static bool IsValidSize(int width, int height) => width >= 6 && width % 4 == 2 && height >= 4 && height % 2 == 0;

    /// <summary>
    /// Sine of the latitude of the cell centre in parts per <paramref name="one"/> (north positive). Rows split the
    /// sphere into bands of equal area, so the sine of the latitude falls in equal steps from the north pole to the
    /// south pole; odd columns sit half a row lower, like on the screen.
    /// </summary>
    public long SinLatitude(HexCoord c, long one) => one - ((((2L * c.Row) + 1 + (c.Col & 1)) * one) / Height);

    /// <summary>Longitude of the cell centre as a share of the full circle, in parts per <paramref name="one"/>.</summary>
    public long Longitude(HexCoord c, long one) => (((2L * c.Col) + 1) * one) / (2L * Width);

    /// <summary>Number of columns.</summary>
    public int Width { get; }

    /// <summary>Number of rows.</summary>
    public int Height { get; }

    /// <summary>Number of cells.</summary>
    public int CellCount => Width * Height;

    /// <summary>Number of canonical edges (3 per cell).</summary>
    public int EdgeCount => CellCount * HexEdge.PerCell;

    /// <summary>
    /// Largest radius for which <see cref="Ring"/> and <see cref="Range"/> never visit a cell twice.
    /// </summary>
    public int MaxUniqueRadius => Math.Min((Width - 2) / 4, Height - 1);

    /// <summary>
    /// Brings any column/row onto the map: east–west around the globe, and across a pole onto the other side of the
    /// planet (column + Width/2, rows mirrored).
    /// </summary>
    public HexCoord Wrap(int col, int row)
    {
        while (row < 0 || row >= Height)
        {
            row = row < 0 ? -1 - row : (2 * Height) - 1 - row;
            col += Width / 2;
        }

        return new(Mod(col, Width), row);
    }

    /// <summary>
    /// True when the step from <paramref name="c"/> in <paramref name="direction"/> crosses a pole. Across a pole the
    /// way back is the mirrored direction (north stays north), not the opposite one.
    /// </summary>
    public bool CrossesPole(HexCoord c, HexDirection direction)
    {
        AxialCoord a = ToAxial(c) + direction.Vector();
        int row = a.R + ((a.Q - (a.Q & 1)) / 2);
        return row < 0 || row >= Height;
    }

    /// <summary>The direction that leads from the neighbour in <paramref name="direction"/> back to <paramref name="c"/>.</summary>
    public HexDirection Back(HexCoord c, HexDirection direction) =>
        CrossesPole(c, direction) ? direction.Opposite().Mirror() : direction.Opposite();

    /// <summary>True when the coordinate is already inside the map.</summary>
    public bool Contains(HexCoord c) => (uint)c.Col < (uint)Width && (uint)c.Row < (uint)Height;

    /// <summary>Flat array index of a cell.</summary>
    public int Index(HexCoord c)
    {
        if (!Contains(c))
        {
            throw new ArgumentOutOfRangeException(nameof(c), $"{c} is outside a {Width}×{Height} map; wrap it first.");
        }

        return (c.Row * Width) + c.Col;
    }

    /// <summary>Cell at a flat array index.</summary>
    public HexCoord FromIndex(int index)
    {
        if ((uint)index >= (uint)CellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new HexCoord(index % Width, index / Width);
    }

    /// <summary>Axial coordinates of a cell (the representative inside the map).</summary>
    public static AxialCoord ToAxial(HexCoord c) => ToAxial(c.Col, c.Row);

    /// <summary>The map cell for any axial coordinate (wrapped).</summary>
    public HexCoord FromAxial(AxialCoord a)
    {
        int col = a.Q;
        int row = a.R + ((col - (col & 1)) / 2);
        return Wrap(col, row);
    }

    /// <summary>The neighbouring cell in <paramref name="direction"/>.</summary>
    public HexCoord Neighbor(HexCoord c, HexDirection direction) => FromAxial(ToAxial(c) + direction.Vector());

    /// <summary>Writes the 6 neighbours (in <see cref="HexDirection"/> order) into <paramref name="destination"/>.</summary>
    public void GetNeighbors(HexCoord c, Span<HexCoord> destination)
    {
        if (destination.Length < HexDirections.Count)
        {
            throw new ArgumentException("Need room for 6 neighbours.", nameof(destination));
        }

        AxialCoord a = ToAxial(c);
        for (int d = 0; d < HexDirections.Count; d++)
        {
            destination[d] = FromAxial(a + ((HexDirection)d).Vector());
        }
    }

    /// <summary>
    /// The shortest number of steps between two cells, going around the globe or over a pole when that is shorter.
    /// </summary>
    public int Distance(HexCoord a, HexCoord b) => AxialCoord.PlaneDistance(ToAxial(a), NearestImage(a, b));

    /// <summary>
    /// The cells on the shortest straight line from <paramref name="a"/> to <paramref name="b"/>, both included
    /// (Distance + 1 cells). Exact integer math, so identical on every platform.
    /// </summary>
    public int Line(HexCoord a, HexCoord b, Span<HexCoord> destination)
    {
        AxialCoord start = ToAxial(a);
        AxialCoord end = NearestImage(a, b);
        int n = AxialCoord.PlaneDistance(start, end);
        if (destination.Length < n + 1)
        {
            throw new ArgumentException($"Need room for {n + 1} cells.", nameof(destination));
        }

        if (n == 0)
        {
            destination[0] = a;
            return 1;
        }

        // Interpolate in cube coordinates scaled by 6n. Sample points then sit on multiples of 6 and rounding
        // ties on 3 (mod 6), so a nudge of (+1, +1, -2) breaks ties the same way every time without fractions.
        long scale = 6L * n;
        for (int i = 0; i <= n; i++)
        {
            long x = (start.Q * scale) + ((long)(end.Q - start.Q) * 6 * i) + 1;
            long z = (start.R * scale) + ((long)(end.R - start.R) * 6 * i) + 1;
            long y = -x - z;
            destination[i] = FromAxial(CubeRound(x, y, z, scale));
        }

        return n + 1;
    }

    /// <summary>Convenience overload of <see cref="Line(HexCoord, HexCoord, Span{HexCoord})"/> that allocates a list.</summary>
    public List<HexCoord> Line(HexCoord a, HexCoord b)
    {
        var buffer = new HexCoord[Distance(a, b) + 1];
        int count = Line(a, b, buffer);
        return new List<HexCoord>(buffer.AsSpan(0, count).ToArray());
    }

    /// <summary>Number of cells in a ring of <paramref name="radius"/> (1 for radius 0).</summary>
    public static int RingSize(int radius) => radius == 0 ? 1 : 6 * radius;

    /// <summary>Number of cells within <paramref name="radius"/> (the hexagon around a cell).</summary>
    public static int RangeSize(int radius) => 1 + (3 * radius * (radius + 1));

    /// <summary>
    /// Writes the cells exactly <paramref name="radius"/> steps away, clockwise starting north of the centre.
    /// Cells repeat when the radius is larger than <see cref="MaxUniqueRadius"/>.
    /// </summary>
    public int Ring(HexCoord center, int radius, Span<HexCoord> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        int size = RingSize(radius);
        if (destination.Length < size)
        {
            throw new ArgumentException($"Need room for {size} cells.", nameof(destination));
        }

        if (radius == 0)
        {
            destination[0] = center;
            return 1;
        }

        // Start at the north corner, then walk along the six sides.
        AxialCoord cursor = ToAxial(center) + (HexDirection.N.Vector() * radius);
        int k = 0;
        for (int side = 0; side < HexDirections.Count; side++)
        {
            AxialCoord step = ((HexDirection)side).Clockwise().Clockwise().Vector();
            for (int i = 0; i < radius; i++)
            {
                destination[k++] = FromAxial(cursor);
                cursor += step;
            }
        }

        return k;
    }

    /// <summary>
    /// Writes all cells within <paramref name="radius"/> steps: the centre first, then ring 1, ring 2…
    /// Cells repeat when the radius is larger than <see cref="MaxUniqueRadius"/>.
    /// </summary>
    public int Range(HexCoord center, int radius, Span<HexCoord> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        int size = RangeSize(radius);
        if (destination.Length < size)
        {
            throw new ArgumentException($"Need room for {size} cells.", nameof(destination));
        }

        int k = 0;
        for (int r = 0; r <= radius; r++)
        {
            k += Ring(center, r, destination[k..]);
        }

        return k;
    }

    /// <summary>
    /// The canonical edge on <paramref name="side"/> of <paramref name="cell"/>: owned through an N, NE or SE side.
    /// Over the north pole two N sides meet; the western cell (column &lt; Width/2) owns the edge. Over the south pole
    /// two S sides meet; that edge is written as the western cell's S side.
    /// </summary>
    public HexEdge Edge(HexCoord cell, HexDirection side)
    {
        HexCoord other = Neighbor(cell, side);
        HexDirection back = Back(cell, side);
        bool mine = (int)side < HexEdge.PerCell;
        bool theirs = (int)back < HexEdge.PerCell;
        if (mine == theirs)
        {
            // Both N (north pole) or both S (south pole): the cell in the western half owns it.
            return cell.Col < Width / 2 ? new HexEdge(cell, side) : new HexEdge(other, back);
        }

        return mine ? new HexEdge(cell, side) : new HexEdge(other, back);
    }

    /// <summary>
    /// Flat array index of a canonical edge. There are exactly 3 edges per cell: the north-pole edges leave the N
    /// slots of the eastern half of row 0 free, and the south-pole edges use them.
    /// </summary>
    public int EdgeIndex(HexEdge edge)
    {
        if (edge.Side == HexDirection.S && edge.Cell.Row == Height - 1 && edge.Cell.Col < Width / 2)
        {
            return (Index(new HexCoord(edge.Cell.Col + (Width / 2), 0)) * HexEdge.PerCell) + (int)HexDirection.N;
        }

        if ((int)edge.Side >= HexEdge.PerCell)
        {
            throw new ArgumentException("Edge is not canonical; create it with HexGlobe.Edge.", nameof(edge));
        }

        return (Index(edge.Cell) * HexEdge.PerCell) + (int)edge.Side;
    }

    /// <summary>The two cells an edge separates.</summary>
    public (HexCoord A, HexCoord B) EdgeCells(HexEdge edge) => (edge.Cell, Neighbor(edge.Cell, edge.Side));

    private static AxialCoord ToAxial(int col, int row) => new(col, row - ((col - (col & 1)) / 2));

    // Axial position of the copy of b (around the globe, or mirrored over a pole) closest to a in the plane.
    private AxialCoord NearestImage(HexCoord a, HexCoord b)
    {
        AxialCoord origin = ToAxial(a);
        AxialCoord best = default;
        int bestDistance = int.MaxValue;
        int half = Width / 2;
        ReadOnlySpan<int> cols = [b.Col, b.Col + half, b.Col + half, b.Col, b.Col];
        ReadOnlySpan<int> rows = [b.Row, -1 - b.Row, (2 * Height) - 1 - b.Row, b.Row + (2 * Height), b.Row - (2 * Height)];
        for (int image = 0; image < cols.Length; image++)
        {
            for (int k = -1; k <= 1; k++)
            {
                AxialCoord p = ToAxial(cols[image] + (k * Width), rows[image]);
                int d = AxialCoord.PlaneDistance(origin, p);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }
        }

        return best;
    }

    private static AxialCoord CubeRound(long x, long y, long z, long scale)
    {
        long rx = DivRound(x, scale);
        long ry = DivRound(y, scale);
        long rz = DivRound(z, scale);
        long dx = Math.Abs((rx * scale) - x);
        long dy = Math.Abs((ry * scale) - y);
        long dz = Math.Abs((rz * scale) - z);
        if (dx > dy && dx > dz)
        {
            rx = -ry - rz;
        }
        else if (dy > dz)
        {
            ry = -rx - rz;
        }
        else
        {
            rz = -rx - ry;
        }

        return new AxialCoord((int)rx, (int)rz);
    }

    private static long DivRound(long value, long divisor)
    {
        long q = Math.DivRem(value, divisor, out long rem);
        if (rem * 2 >= divisor)
        {
            q++;
        }
        else if (rem * 2 < -divisor)
        {
            q--;
        }

        return q;
    }

    private static int Mod(int value, int m)
    {
        int r = value % m;
        return r < 0 ? r + m : r;
    }
}
