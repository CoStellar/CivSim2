using System;
using System.Collections.Generic;
using CivSim.Game.Map;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// Draws the new game's objects over the map (ADR 0028): settlement centres, buildings with their state, the hexes where a
/// building can go while placing, and the selection. Placeholder shapes and letters until pixel art comes.
/// </summary>
public partial class PlayOverlay : Node2D
{
    private static readonly Color[] CivColors =
    [
        new(0.95f, 0.85f, 0.25f), new(0.85f, 0.25f, 0.25f), new(0.3f, 0.55f, 0.95f), new(0.6f, 0.35f, 0.85f),
        new(0.3f, 0.8f, 0.45f), new(0.95f, 0.55f, 0.2f), new(0.4f, 0.85f, 0.85f), new(0.9f, 0.45f, 0.7f),
    ];

    private readonly HashSet<(int, int)> _places = [];
    private PlayViewMessage? _view;
    private PlayContentMessage? _content;
    private Vector2 _period;
    private Rect2 _visible;
    private (int Col, int Row)? _hover;
    private (int Col, int Row)? _selected;

    /// <summary>Colour of a civilization.</summary>
    public static Color CivColor(int civ) => CivColors[((civ % CivColors.Length) + CivColors.Length) % CivColors.Length];

    /// <summary>Sets what to draw.</summary>
    public void Show(PlayContentMessage content, PlayViewMessage? view, Vector2 period, Rect2 visible)
    {
        _content = content;
        _view = view;
        _period = period;
        _visible = visible.Grow(80);
        QueueRedraw();
    }

    /// <summary>Hexes to highlight while placing (null = not placing).</summary>
    public void SetPlaces(int[]? cells)
    {
        _places.Clear();
        if (cells is not null)
        {
            for (int i = 0; i + 1 < cells.Length; i += 2)
            {
                _places.Add((cells[i], cells[i + 1]));
            }
        }

        QueueRedraw();
    }

    /// <summary>True when the hex is a valid place in the current placement.</summary>
    public bool IsPlace(int col, int row) => _places.Contains((col, row));

    /// <summary>Hex under the mouse while placing.</summary>
    public void SetHover((int Col, int Row)? hover)
    {
        if (hover != _hover)
        {
            _hover = hover;
            QueueRedraw();
        }
    }

    /// <summary>Selected hex.</summary>
    public void SetSelected((int Col, int Row)? selected)
    {
        _selected = selected;
        QueueRedraw();
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (_content is null || _period.X <= 0)
        {
            return;
        }

        Font font = ThemeDB.FallbackFont;
        foreach ((int col, int row) in _places)
        {
            foreach (Vector2 c in Copies(col, row))
            {
                DrawPolygon(Hex(c, 0.9f), [new Color(0.3f, 1f, 0.4f, 0.28f)]);
            }
        }

        if (_view is not null)
        {
            foreach (PlayBuildingView b in _view.Buildings)
            {
                PlayBuildingInfo t = _content.Buildings[b.Type];
                foreach (Vector2 c in Copies(b.Col, b.Row))
                {
                    DrawBuilding(font, c, b, t);
                }
            }

            foreach (PlaySettlementView s in _view.Settlements)
            {
                foreach (Vector2 c in Copies(s.Col, s.Row))
                {
                    Color civ = CivColor(s.Civ);
                    float size = 0.55f + (s.Level * 0.1f);
                    DrawPolygon(Hex(c, size), [civ.Darkened(0.45f)]);
                    DrawPolyline(Closed(Hex(c, size)), civ, 3);
                    DrawString(font, c + new Vector2(-24, 6), _content.Levels[s.Level].Name[..1], HorizontalAlignment.Center, 48, 18, Colors.White);
                    DrawString(font, c + new Vector2(-80, -26), s.Name, HorizontalAlignment.Center, 160, 14, Colors.White);
                }
            }
        }

        if (_hover is { } h)
        {
            Color color = _places.Count == 0 || _places.Contains(h) ? new Color(1, 1, 1, 0.9f) : new Color(1, 0.3f, 0.3f, 0.9f);
            foreach (Vector2 c in Copies(h.Col, h.Row))
            {
                DrawPolyline(Closed(Hex(c, 0.95f)), color, 2);
            }
        }

        if (_selected is { } s2)
        {
            foreach (Vector2 c in Copies(s2.Col, s2.Row))
            {
                DrawPolyline(Closed(Hex(c, 1.0f)), new Color(1f, 0.9f, 0.3f), 3);
            }
        }
    }

    private void DrawBuilding(Font font, Vector2 c, PlayBuildingView b, PlayBuildingInfo t)
    {
        Color civ = CivColor(b.Civ);
        bool built = b.State >= 2;
        Color fill = b.State switch
        {
            0 => new Color(0.35f, 0.3f, 0.25f, 0.85f),
            1 => new Color(0.3f, 0.25f, 0.2f, 0.85f),
            3 => new Color(0.25f, 0.25f, 0.25f, 0.9f),
            _ => b.Starved ? new Color(0.55f, 0.15f, 0.12f, 0.92f) : new Color(0.18f, 0.16f, 0.14f, 0.9f),
        };
        DrawPolygon(Hex(c, 0.7f), [fill]);
        DrawPolyline(Closed(Hex(c, 0.7f)), b.Priority ? new Color(1f, 0.85f, 0.2f) : civ, b.Priority ? 3 : 2);
        string letters = Abbreviation(t.Name);
        DrawString(font, c + new Vector2(-24, 5), letters, HorizontalAlignment.Center, 48, 14, built ? Colors.White : new Color(0.8f, 0.8f, 0.8f));
        if (!built)
        {
            float p = b.ProgressPermille / 1000f;
            DrawLine(c + new Vector2(-14, 12), c + new Vector2(14, 12), new Color(0, 0, 0, 0.8f), 4);
            DrawLine(c + new Vector2(-14, 12), c + new Vector2(-14 + (28 * p), 12), b.State == 1 ? Colors.Orange : Colors.LightGreen, 4);
        }
        else if (t.Workers > 0)
        {
            Color w = b.Workers >= t.Workers ? Colors.LightGreen : b.Workers == 0 ? Colors.Red : Colors.Yellow;
            DrawString(font, c + new Vector2(-24, -6), $"{b.Workers}/{t.Workers}", HorizontalAlignment.Center, 48, 10, w);
        }
    }

    private static string Abbreviation(string name)
    {
        string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 ? $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}" : name.Length >= 2 ? name[..2] : name;
    }

    // The map repeats east–west; draw an object in every copy that is on screen.
    private IEnumerable<Vector2> Copies(int col, int row)
    {
        Vector2 c = HexLayout.Center(col, row);
        float k0 = MathF.Floor((_visible.Position.X - c.X) / _period.X);
        for (float k = k0; c.X + (k * _period.X) <= _visible.End.X; k++)
        {
            var p = new Vector2(c.X + (k * _period.X), c.Y);
            if (p.Y >= _visible.Position.Y && p.Y <= _visible.End.Y && p.X >= _visible.Position.X)
            {
                yield return p;
            }
        }
    }

    private static Vector2[] Hex(Vector2 c, float scale)
    {
        var pts = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            pts[i] = c + (HexLayout.Corners[i] * scale);
        }

        return pts;
    }

    private static Vector2[] Closed(Vector2[] pts)
    {
        var all = new Vector2[pts.Length + 1];
        pts.CopyTo(all, 0);
        all[^1] = pts[0];
        return all;
    }
}
