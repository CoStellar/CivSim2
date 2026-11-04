using System;
using System.Collections.Generic;

namespace CivSim.Core.Map;

/// <summary>
/// Geometry of one world: flat-top hexes on a torus (the map wraps east–west and north–south).
/// Every neighbour, distance, line and area on the map must go through this class (CLAUDE.md rule 8).
/// Storage order is row-major: <c>index = row * Width + col</c>. Methods do not allocate unless they return a list.
/// </summary>
public sealed class HexTorus
{
    /// <summary>Creates the geometry. Both dimensions must be even and at least 4.</summary>
    /// <remarks>
    /// Even height is required by the design (GDD 3). Even width is required because odd columns are shifted:
    /// with an odd width, column 0 and the last column would both be "unshifted" and the seam would not close.
    /// </remarks>
    public HexTorus(int width, int height)
    {
        if (width < 4 || height < 4 || width % 2 != 0 || height % 2 != 0)
        {
            throw new ArgumentException($"Map size {width}×{height} is invalid: both sides must be even and at least 4.");
        }

        Width = width;
        Height = height;
    }

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
    public int MaxUniqueRadius => (Math.Min(Width, Height) - 1) / 2;

    /// <summary>Wraps any column/row into the map.</summary>
    public HexCoord Wrap(int col, int row) => new(Mod(col, Width), Mod(row, Height));

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
    /// The shortest number of steps between two cells, going across the seams when that is shorter.
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

    /// <summary>The canonical edge on <paramref name="side"/> of <paramref name="cell"/>.</summary>
    public HexEdge Edge(HexCoord cell, HexDirection side)
    {
        return (int)side < HexEdge.PerCell
            ? new HexEdge(cell, side)
            : new HexEdge(Neighbor(cell, side), side.Opposite());
    }

    /// <summary>Flat array index of a canonical edge.</summary>
    public int EdgeIndex(HexEdge edge)
    {
        if ((int)edge.Side >= HexEdge.PerCell)
        {
            throw new ArgumentException("Edge is not canonical; create it with HexTorus.Edge.", nameof(edge));
        }

        return (Index(edge.Cell) * HexEdge.PerCell) + (int)edge.Side;
    }

    /// <summary>The two cells an edge separates.</summary>
    public (HexCoord A, HexCoord B) EdgeCells(HexEdge edge) => (edge.Cell, Neighbor(edge.Cell, edge.Side));

    private static AxialCoord ToAxial(int col, int row) => new(col, row - ((col - (col & 1)) / 2));

    // Axial position of the copy of b (b shifted by whole map widths/heights) closest to a.
    private AxialCoord NearestImage(HexCoord a, HexCoord b)
    {
        AxialCoord origin = ToAxial(a);
        AxialCoord best = default;
        int bestDistance = int.MaxValue;
        for (int dc = -1; dc <= 1; dc++)
        {
            for (int dr = -1; dr <= 1; dr++)
            {
                AxialCoord image = ToAxial(b.Col + (dc * Width), b.Row + (dr * Height));
                int d = AxialCoord.PlaneDistance(origin, image);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = image;
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
