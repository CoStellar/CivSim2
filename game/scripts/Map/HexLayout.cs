using System;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// Pixel geometry of the flat-top hex grid, matching the art template (CLAUDE.md, GDD 12): hexes are 48×42 px,
/// columns every 36 px, rows every 42 px, odd columns shifted down by 21 px.
/// </summary>
public static class HexLayout
{
    /// <summary>Hex width in pixels.</summary>
    public const float HexWidth = 48;

    /// <summary>Hex height in pixels.</summary>
    public const float HexHeight = 42;

    /// <summary>Horizontal distance between columns.</summary>
    public const float ColumnStep = 36;

    /// <summary>Vertical distance between rows.</summary>
    public const float RowStep = 42;

    /// <summary>Corner offsets from the centre, clockwise from east: E, SE, SW, W, NW, NE.</summary>
    public static readonly Vector2[] Corners =
    [
        new(24, 0), new(12, 21), new(-12, 21), new(-24, 0), new(-12, -21), new(12, -21),
    ];

    /// <summary>Corners joined by the canonical edges N, NE and SE (indexes into <see cref="Corners"/>).</summary>
    public static readonly (int A, int B)[] EdgeCorners = [(4, 5), (5, 0), (0, 1)];

    /// <summary>Size of one full copy of the map in pixels (the wrap period).</summary>
    public static Vector2 MapSize(int width, int height) => new(width * ColumnStep, height * RowStep);

    /// <summary>Centre of a cell (unwrapped column and row).</summary>
    public static Vector2 Center(int col, int row) =>
        new((col * ColumnStep) + 24, (row * RowStep) + 21 + ((col & 1) * 21));

    /// <summary>Cell under a pixel position (not wrapped; the caller wraps).</summary>
    public static (int Col, int Row) CellAt(Vector2 p)
    {
        int col0 = (int)MathF.Floor((p.X - 6) / ColumnStep);
        (int Col, int Row) best = (col0, 0);
        float bestD = float.MaxValue;
        for (int c = col0 - 1; c <= col0 + 1; c++)
        {
            int r0 = (int)MathF.Floor((p.Y - ((c & 1) * 21)) / RowStep);
            for (int r = r0 - 1; r <= r0 + 1; r++)
            {
                float d = Center(c, r).DistanceSquaredTo(p);
                if (d < bestD)
                {
                    bestD = d;
                    best = (c, r);
                }
            }
        }

        return best;
    }
}
