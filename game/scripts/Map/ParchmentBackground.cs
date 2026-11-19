using System;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// What lies behind the map (Patryk, 2026-11-19: the darkness around us is weak): instead of black, an old map. Parchment
/// with faint sepia hexes on the same grid as the map, so the unknown world reads as a blank sheet waiting to be drawn.
/// The tile is made in code once (no art needed), repeats every 7 columns and 12 rows, and lines up with the real hexes.
/// </summary>
public partial class ParchmentBackground : Node2D
{
    /// <summary>Tile size in pixels: 7 pairs of columns (72 px) by 12 rows (42 px).</summary>
    public const int Tile = 504;

    private ImageTexture? _texture;
    private Rect2 _drawn;

    /// <inheritdoc/>
    public override void _Ready()
    {
        ZIndex = -100;
        TextureRepeat = TextureRepeatEnum.Enabled;
        TextureFilter = TextureFilterEnum.Linear;
        _texture = ImageTexture.CreateFromImage(MakeTile());
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        Rect2 view = VisibleRect();
        if (!_drawn.Encloses(view))
        {
            QueueRedraw();
        }
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (_texture is null)
        {
            return;
        }

        // A margin of one tile around the view, on the tile grid, so the hexes stay under the map's hexes.
        Rect2 view = VisibleRect();
        float x0 = (MathF.Floor(view.Position.X / Tile) - 1) * Tile;
        float y0 = (MathF.Floor(view.Position.Y / Tile) - 1) * Tile;
        float x1 = (MathF.Ceiling(view.End.X / Tile) + 1) * Tile;
        float y1 = (MathF.Ceiling(view.End.Y / Tile) + 1) * Tile;
        _drawn = new Rect2(x0, y0, x1 - x0, y1 - y0);
        DrawTextureRectRegion(_texture, _drawn, new Rect2(0, 0, _drawn.Size));
    }

    // The part of the world the camera shows.
    private Rect2 VisibleRect()
    {
        Transform2D inverse = GetViewport().GetCanvasTransform().AffineInverse();
        Vector2 size = GetViewportRect().Size;
        Vector2 a = inverse * Vector2.Zero;
        Vector2 b = inverse * size;
        return new Rect2(a, b - a).Abs();
    }

    /// <summary>The parchment tile: warm paper with soft blotches and fibres, the hex grid in faded ink.</summary>
    public static Image MakeTile()
    {
        var img = Image.CreateEmpty(Tile, Tile, false, Image.Format.Rgb8);
        var paper = new Color(0.78f, 0.70f, 0.54f);
        var dark = new Color(0.62f, 0.52f, 0.37f);
        var ink = new Color(0.42f, 0.32f, 0.20f);
        for (int y = 0; y < Tile; y++)
        {
            for (int x = 0; x < Tile; x++)
            {
                // Blotches: smooth noise at two sizes, tileable because the noise lattice divides the tile.
                float blot = (0.6f * Noise(x, y, 84)) + (0.4f * Noise(x, y, 28));
                float fibre = Hash((x * 7) + (y / 3), y) * 0.06f;
                Color c = paper.Lerp(dark, Math.Clamp(((blot - 0.45f) * 1.4f) + fibre, 0f, 1f));

                // The hex grid: distance to the edge of the nearest hex, 1 on the edge.
                float h = HexEdge(x, y);
                if (h > 0.93f)
                {
                    float line = Math.Clamp((h - 0.93f) / 0.05f, 0f, 1f);
                    c = c.Lerp(ink, 0.35f * line);
                }

                img.SetPixel(x, y, c);
            }
        }

        return img;
    }

    // 0 at a hex centre, 1 on its edge (flat-top hexes of HexLayout: corners at (±24, 0) and (±12, ±21)).
    private static float HexEdge(int x, int y)
    {
        float best = float.MaxValue;
        int col0 = (int)MathF.Floor((x - 6) / HexLayout.ColumnStep);
        for (int col = col0 - 1; col <= col0 + 1; col++)
        {
            float cx = (col * HexLayout.ColumnStep) + 24;
            float rowF = (y - 21 - ((col & 1) * 21)) / HexLayout.RowStep;
            int row0 = (int)MathF.Round(rowF);
            for (int row = row0 - 1; row <= row0 + 1; row++)
            {
                float cy = (row * HexLayout.RowStep) + 21 + ((col & 1) * 21);
                float dx = MathF.Abs(x + 0.5f - cx);
                float dy = MathF.Abs(y + 0.5f - cy);
                float h = MathF.Max(dy / 21f, (dx + (dy * 12f / 21f)) / 24f);
                best = MathF.Min(best, h);
            }
        }

        return best;
    }

    // Value noise on a lattice of the given step (the step divides the tile, so the noise repeats with it).
    private static float Noise(int x, int y, int step)
    {
        int cells = Tile / step;
        int gx = x / step;
        int gy = y / step;
        float fx = Smooth((x % step) / (float)step);
        float fy = Smooth((y % step) / (float)step);
        float a = Hash(gx % cells, gy % cells);
        float b = Hash((gx + 1) % cells, gy % cells);
        float c = Hash(gx % cells, (gy + 1) % cells);
        float d = Hash((gx + 1) % cells, (gy + 1) % cells);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    private static float Smooth(float t) => t * t * (3 - (2 * t));

    private static float Hash(int x, int y)
    {
        uint h = (uint)((x * 374761393) + (y * 668265263));
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }
}
