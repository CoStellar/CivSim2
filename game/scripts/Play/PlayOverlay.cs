using System;
using System.Collections.Generic;
using CivSim.Game.Map;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// Draws the new game's objects over the map in layers (decisions of 2026-11-18), from the bottom:
/// <list type="number">
/// <item>ground: places to build, the land of the selected settlement, buildings and settlement centres;</item>
/// <item>routes: paths of units, patrol loops and caravan routes, in a lighter shade of the people's colour, the kind told
/// by the line (solid with arrows: a march; X at the end: an attack; dashed loop: a patrol; thick: a trade route; thin:
/// carriers), over buildings but under units and names;</item>
/// <item>lift: what stands on the selected hex, raised with its tile (the map draws the tile), and the pulsing rings of the
/// selected units;</item>
/// <item>units;</item>
/// <item>effects: the hex under the mouse, the path being drawn, the flash of a given order;</item>
/// <item>labels: names of settlements and their alert marks, always on top.</item>
/// </list>
/// The ground, routes, units and labels are drawn again only when the view or the camera changes; the lift and effects
/// layers, which move, every frame. Placeholder shapes and letters until pixel art comes.
/// </summary>
public partial class PlayOverlay : Node2D
{
    private static readonly Color[] CivColors =
    [
        new(0.95f, 0.85f, 0.25f), new(0.85f, 0.25f, 0.25f), new(0.3f, 0.55f, 0.95f), new(0.6f, 0.35f, 0.85f),
        new(0.3f, 0.8f, 0.45f), new(0.95f, 0.55f, 0.2f), new(0.4f, 0.85f, 0.85f), new(0.9f, 0.45f, 0.7f),
    ];

    private readonly HashSet<(int, int)> _places = [];
    private readonly List<(Vector2 At, Color Color, double Born)> _flashes = [];
    private PlayViewMessage? _view;
    private PlayContentMessage? _content;
    private Vector2 _period;
    private Rect2 _visible;
    private (int Col, int Row)? _hover;
    private (int Col, int Row)? _selected;
    private (int Col, int Row)? _liftedCell;
    private double _time;
    private Layer _ground = null!;
    private Layer _routes = null!;
    private Layer _lift = null!;
    private Layer _units = null!;
    private Layer _effects = null!;
    private Layer _labels = null!;
    private string _staticSig = "";

    /// <summary>Id of the selected unit (−1 = none).</summary>
    public int SelectedUnit { get; set; } = -1;

    /// <summary>All selected units (several with Shift).</summary>
    public HashSet<int> SelectedUnits { get; } = [];

    /// <summary>Hexes being drawn (a path, a patrol, a route), in order.</summary>
    public List<(int Col, int Row)> DrawPath { get; } = [];

    /// <summary>Colour of the drawn path.</summary>
    public Color DrawColor { get; set; } = new(1f, 0.95f, 0.5f);

    /// <summary>Show all our standing routes, patrols and marches (toggled by the player); otherwise only the selected ones.</summary>
    public bool ShowAllRoutes { get; set; }

    /// <summary>How far the selected hex is lifted, 0–1 (grows over a tenth of a second after a click).</summary>
    public float Lift { get; private set; }

    /// <summary>Colour of a civilization.</summary>
    public static Color CivColor(int civ) => CivColors[((civ % CivColors.Length) + CivColors.Length) % CivColors.Length];

    /// <inheritdoc/>
    public override void _Ready()
    {
        _ground = Add(0, PaintGround);
        _routes = Add(5, PaintRoutes);
        _lift = Add(7, PaintLift);
        _units = Add(10, PaintUnits);
        _effects = Add(15, PaintEffects);
        _labels = Add(20, PaintLabels);
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _time += delta;
        if (_selected != _liftedCell)
        {
            _liftedCell = _selected;
            Lift = 0;
            Restatic();
        }

        Lift = Math.Min(1f, Lift + ((float)delta * 10f));
        _flashes.RemoveAll(f => _time - f.Born > 0.4);
        _lift.QueueRedraw();
        _effects.QueueRedraw();
    }

    /// <summary>Sets what to draw.</summary>
    public void Show(PlayContentMessage content, PlayViewMessage? view, Vector2 period, Rect2 visible)
    {
        _content = content;
        _period = period;
        Rect2 grown = visible.Grow(80);
        bool changed = view != _view || grown != _visible;
        _view = view;
        _visible = grown;
        if (changed)
        {
            Restatic();
        }
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

        Restatic();
    }

    /// <summary>True when the hex is a valid place in the current placement.</summary>
    public bool IsPlace(int col, int row) => _places.Contains((col, row));

    /// <summary>Hex under the mouse (an outline only).</summary>
    public void SetHover((int Col, int Row)? hover) => _hover = hover;

    /// <summary>Selected hex.</summary>
    public void SetSelected((int Col, int Row)? selected) => _selected = selected;

    /// <summary>A short ring on a hex: the order was given (like an RTS).</summary>
    public void Flash(int col, int row, Color color) => _flashes.Add((HexLayout.Center(col, row), color, _time));

    /// <summary>Draws the slow layers again (after a selection or a toggle).</summary>
    public void Restatic()
    {
        string sig = $"{SelectedUnit}|{string.Join(",", SelectedUnits)}|{ShowAllRoutes}|{_selected}";
        _staticSig = sig;
        _ground?.QueueRedraw();
        _routes?.QueueRedraw();
        _units?.QueueRedraw();
        _labels?.QueueRedraw();
    }

    // ---- Layers ----------------------------------------------------------------------------------------------------

    private void PaintGround(CanvasItem g)
    {
        if (_content is null || _period.X <= 0)
        {
            return;
        }

        foreach ((int col, int row) in _places)
        {
            foreach (Vector2 c in Copies(col, row))
            {
                g.DrawPolygon(Hex(c, 0.9f), [new Color(0.3f, 1f, 0.4f, 0.28f)]);
            }
        }

        if (_view is { Picking: true, TakenStarts: { } taken })
        {
            for (int i = 0; i + 1 < taken.Length; i += 2)
            {
                foreach (Vector2 c in Copies(taken[i], taken[i + 1]))
                {
                    g.DrawCircle(c, 14, new Color(1, 0.2f, 0.2f, 0.9f));
                    g.DrawArc(c, _view.BlockRadius * HexLayout.ColumnStep, 0, MathF.Tau, 64, new Color(1, 0.2f, 0.2f, 0.8f), 4);
                }
            }
        }

        if (_view is null)
        {
            return;
        }

        // The land of the selected settlement, lightly.
        if (SelectedSettlement() is { } sel && sel.Civ == _view.Civ)
        {
            int radius = _content.Levels[sel.Level].Radius;
            Color tint = new(Light(CivColor(sel.Civ)), 0.12f);
            foreach (Vector2 c in Copies(sel.Col, sel.Row))
            {
                g.DrawCircle(c, (radius + 0.5f) * HexLayout.ColumnStep, tint);
            }
        }

        foreach (PlayBuildingView b in _view.Buildings)
        {
            if (_selected == (b.Col, b.Row))
            {
                continue; // drawn lifted
            }

            PlayBuildingInfo t = _content.Buildings[b.Type];
            foreach (Vector2 c in Copies(b.Col, b.Row))
            {
                DrawBuilding(g, c, b, t, 1f);
            }
        }

        foreach (PlaySettlementView s in _view.Settlements)
        {
            if (_selected == (s.Col, s.Row))
            {
                continue;
            }

            foreach (Vector2 c in Copies(s.Col, s.Row))
            {
                DrawSettlement(g, c, s, 1f);
            }
        }
    }

    private void PaintRoutes(CanvasItem g)
    {
        if (_view is null || _content is null || _period.X <= 0)
        {
            return;
        }

        PlaySettlementView? sel = SelectedSettlement();
        foreach (PlayUnitView u in _view.Units)
        {
            if (u.Civ != _view.Civ)
            {
                continue;
            }

            bool picked = u.Id == SelectedUnit || SelectedUnits.Contains(u.Id);
            bool fromSel = sel is not null && u.Carrier && Near(u, sel);
            if (!picked && !fromSel && !ShowAllRoutes)
            {
                continue;
            }

            Color color = Light(CivColor(u.Civ));
            if (u.Patrol is { Length: >= 4 } loop)
            {
                StrokePath(g, Points(loop, (u.Col, u.Row), closed: true), color, 2.5f, dashed: true, arrows: 3, end: EndMark.None);
            }

            if (u.Path is { Length: >= 2 } path)
            {
                EndMark end = u.Attack ? EndMark.Cross : EndMark.Dot;
                StrokePath(g, Points(path, (u.Col, u.Row), closed: false), color, u.Carrier ? 1.5f : 3f, dashed: false, arrows: u.Carrier ? 0 : 3, end);
            }
        }

        foreach (PlayRouteView r in _view.Routes ?? [])
        {
            PlaySettlementView? from = SettlementById(r.From);
            PlaySettlementView? to = SettlementById(r.To);
            if (from is null || to is null)
            {
                continue;
            }

            bool shown = ShowAllRoutes || (sel is not null && (sel.Id == r.From || sel.Id == r.To));
            if (!shown)
            {
                continue;
            }

            Color color = Light(CivColor(from.Civ));
            List<Vector2> pts = r.Path is { Length: >= 2 } drawn
                ? Points(drawn, (from.Col, from.Row), closed: false)
                : [HexLayout.Center(from.Col, from.Row), Unwrapped(HexLayout.Center(from.Col, from.Row), to.Col, to.Row)];
            StrokePath(g, pts, color, 5f, dashed: r.Path is null, arrows: 4, EndMark.Dot);
        }
    }

    private void PaintLift(CanvasItem g)
    {
        if (_view is null || _content is null || _period.X <= 0)
        {
            return;
        }

        // Pulsing rings under the selected units.
        float pulse = 0.5f + (0.5f * MathF.Sin((float)_time * 5f));
        foreach (PlayUnitView u in _view.Units)
        {
            if (u.Id != SelectedUnit && !SelectedUnits.Contains(u.Id))
            {
                continue;
            }

            foreach (Vector2 c in Copies(u.Col, u.Row))
            {
                Vector2 p = c + new Vector2(10, -2);
                g.DrawSetTransform(p, 0, new Vector2(1f, 0.45f));
                g.DrawCircle(Vector2.Zero, 15 + (2 * pulse), new Color(CivColor(u.Civ), 0.35f + (0.25f * pulse)));
                g.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        // What stands on the selected hex rises with its tile.
        if (_selected is not { } sel)
        {
            return;
        }

        float scale = 1f + (0.12f * Lift);
        Vector2 up = new(0, -5f * Lift);
        foreach (PlayBuildingView b in _view.Buildings)
        {
            if ((b.Col, b.Row) == sel)
            {
                foreach (Vector2 c in Copies(b.Col, b.Row))
                {
                    DrawBuilding(g, c + up, b, _content.Buildings[b.Type], scale);
                }
            }
        }

        foreach (PlaySettlementView s in _view.Settlements)
        {
            if ((s.Col, s.Row) == sel)
            {
                foreach (Vector2 c in Copies(s.Col, s.Row))
                {
                    DrawSettlement(g, c + up, s, scale);
                }
            }
        }

        foreach (Vector2 c in Copies(sel.Col, sel.Row))
        {
            g.DrawPolyline(Closed(Hex(c + up, scale)), new Color(1f, 0.92f, 0.55f, 0.95f), 2.5f);
        }
    }

    private void PaintUnits(CanvasItem g)
    {
        if (_view is null || _content is null || _period.X <= 0)
        {
            return;
        }

        Font font = UiKit.GlyphFont;
        foreach (PlayUnitView u in _view.Units)
        {
            if (u.State == 0)
            {
                continue;
            }

            Color civ = CivColor(u.Civ);
            bool picked = u.Id == SelectedUnit || SelectedUnits.Contains(u.Id);
            foreach (Vector2 c in Copies(u.Col, u.Row))
            {
                Vector2 p = c + new Vector2(10, -8);
                g.DrawCircle(p, 11, u.Hidden ? civ.Darkened(0.6f) : civ.Darkened(0.3f));
                g.DrawArc(p, 11, 0, MathF.Tau, 20, picked ? new Color(1f, 0.9f, 0.3f) : u.Fighting ? Colors.Red : Colors.Black, picked || u.Fighting ? 3 : 1.5f);
                if (u.Cargo is { Length: > 0 } && u.Cargo != "nothing")
                {
                    g.DrawRect(new Rect2(p + new Vector2(-14, -16), new Vector2(8, 6)), new Color(0.85f, 0.7f, 0.35f));
                }

                g.DrawString(font, p + new Vector2(-11, 5), _content.Units[u.Kind].Name[..1], HorizontalAlignment.Center, 22, 12, Colors.White);
                g.DrawString(font, p + new Vector2(-11, 22), u.People.ToString(System.Globalization.CultureInfo.InvariantCulture), HorizontalAlignment.Center, 22, 12, Colors.White);
            }
        }
    }

    private void PaintEffects(CanvasItem g)
    {
        if (_period.X <= 0)
        {
            return;
        }

        if (_hover is { } h && h != _selected)
        {
            Color color = _places.Count == 0 || _places.Contains(h) ? new Color(1, 1, 1, 0.55f) : new Color(1, 0.3f, 0.3f, 0.8f);
            foreach (Vector2 c in Copies(h.Col, h.Row))
            {
                g.DrawPolyline(Closed(Hex(c, 0.97f)), color, 1.5f);
            }
        }

        // The path being drawn pulses a little.
        if (DrawPath.Count > 0)
        {
            float pulse = 0.75f + (0.25f * MathF.Sin((float)_time * 8f));
            var pts = new List<Vector2>();
            Vector2 prev = Vector2.Zero;
            foreach ((int col, int row) in DrawPath)
            {
                Vector2 p = pts.Count == 0 ? HexLayout.Center(col, row) : Unwrapped(prev, col, row);
                pts.Add(p);
                prev = p;
            }

            foreach (Vector2 shift in Shifts(pts[0]))
            {
                var moved = pts.ConvertAll(p => p + shift);
                foreach (Vector2 p in moved)
                {
                    g.DrawPolygon(Hex(p, 0.42f), [new Color(DrawColor, 0.3f * pulse)]);
                }

                StrokePath(g, moved, new Color(DrawColor, pulse), 3f, dashed: false, arrows: 2, EndMark.Dot);
            }
        }

        foreach ((Vector2 at, Color color, double born) in _flashes)
        {
            float t = (float)((_time - born) / 0.4);
            foreach (Vector2 shift in Shifts(at))
            {
                g.DrawArc(at + shift, 6 + (26 * t), 0, MathF.Tau, 32, new Color(color, 1f - t), 3f);
            }
        }
    }

    private void PaintLabels(CanvasItem g)
    {
        if (_view is null || _content is null || _period.X <= 0)
        {
            return;
        }

        Font font = UiKit.Font;
        int size = Ui.PixelTheme.FontSize;
        foreach (PlaySettlementView s in _view.Settlements)
        {
            foreach (Vector2 c in Copies(s.Col, s.Row))
            {
                Vector2 at = c + new Vector2(-100, -30);
                Color ink = s.Remembered ? new Color(0.75f, 0.75f, 0.75f) : Colors.White;
                float w = font.GetStringSize(s.Name, HorizontalAlignment.Left, -1, size).X + 12;
                var plate = new Rect2(c.X - (w / 2), at.Y - size + 2, w, size + 4);
                g.DrawRect(plate, new Color(0.08f, 0.07f, 0.06f, 0.75f));
                g.DrawRect(plate, s.Remembered ? Grey(CivColor(s.Civ)) : CivColor(s.Civ), false, 1.5f);
                g.DrawString(font, at, s.Name, HorizontalAlignment.Center, 200, size, ink);
                int worst = AlertOf(s.Id);
                if (worst >= 0)
                {
                    // A mark over the settlement: red for danger and hunger, yellow for the rest.
                    Color mark = worst <= 2 ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.85f, 0.3f);
                    g.DrawCircle(c + new Vector2(0, -30 - size - 8), 9, mark);
                    g.DrawString(UiKit.GlyphFont, c + new Vector2(-6, -30 - size - 3), "!", HorizontalAlignment.Center, 12, 12, Colors.Black);
                }
            }
        }
    }

    // ---- Pieces --------------------------------------------------------------------------------------------------

    private enum EndMark
    {
        None,
        Dot,
        Cross,
    }

    private void DrawBuilding(CanvasItem g, Vector2 c, PlayBuildingView b, PlayBuildingInfo t, float scale)
    {
        Font font = UiKit.GlyphFont;
        Color civ = CivColor(b.Civ);
        if (b.Remembered)
        {
            // A building we saw before: grey, as we last saw it (like Age of Empires).
            g.DrawPolygon(Hex(c, 0.7f * scale), [new Color(0.32f, 0.32f, 0.32f, 0.75f)]);
            g.DrawPolyline(Closed(Hex(c, 0.7f * scale)), Grey(civ), 2);
            g.DrawString(font, c + new Vector2(-24, 5), UiKit.Short(t.Name), HorizontalAlignment.Center, 48, 12, new Color(0.7f, 0.7f, 0.7f));
            return;
        }

        bool built = b.State is 2 or 3;
        Color fill = b.State switch
        {
            5 => new Color(0.2f, 0.2f, 0.3f, 0.6f),
            0 => new Color(0.35f, 0.3f, 0.25f, 0.85f),
            1 => new Color(0.3f, 0.25f, 0.2f, 0.85f),
            3 => new Color(0.25f, 0.25f, 0.25f, 0.9f),
            _ => b.Starved ? new Color(0.55f, 0.15f, 0.12f, 0.92f) : new Color(0.18f, 0.16f, 0.14f, 0.9f),
        };
        g.DrawPolygon(Hex(c, 0.7f * scale), [fill]);
        g.DrawPolyline(Closed(Hex(c, 0.7f * scale)), b.Priority ? new Color(1f, 0.85f, 0.2f) : civ, b.Priority ? 3 : 2);
        g.DrawString(font, c + new Vector2(-24, 5), UiKit.Short(t.Name), HorizontalAlignment.Center, 48, 12, built ? Colors.White : new Color(0.8f, 0.8f, 0.8f));
        if (b.State == 5)
        {
            bool lost = b.Missing is not null && b.Porters == 0;
            g.DrawString(font, c + new Vector2(-24, 18), lost ? "missing!" : "goods...", HorizontalAlignment.Center, 48, 12, lost ? new Color(1f, 0.5f, 0.4f) : new Color(0.8f, 0.85f, 1f));
        }
        else if (!built)
        {
            float p = b.ProgressPermille / 1000f;
            g.DrawLine(c + new Vector2(-14, 12), c + new Vector2(14, 12), new Color(0, 0, 0, 0.8f), 4);
            g.DrawLine(c + new Vector2(-14, 12), c + new Vector2(-14 + (28 * p), 12), b.State == 1 ? Colors.Orange : Colors.LightGreen, 4);
        }
        else if (t.Workers > 0)
        {
            Color w = b.Workers >= t.Workers ? Colors.LightGreen : b.Workers == 0 ? Colors.Red : Colors.Yellow;
            g.DrawString(font, c + new Vector2(-24, -8), $"{b.Workers}/{t.Workers}", HorizontalAlignment.Center, 48, 12, w);
        }
    }

    private void DrawSettlement(CanvasItem g, Vector2 c, PlaySettlementView s, float scale)
    {
        Color civ = s.Remembered ? Grey(CivColor(s.Civ)) : CivColor(s.Civ);
        float size = (0.55f + (s.Level * 0.1f)) * scale;
        g.DrawPolygon(Hex(c, size), [civ.Darkened(0.45f)]);
        g.DrawPolyline(Closed(Hex(c, size)), s.Besieged ? Colors.Red : civ, s.Besieged ? 5 : 3);
        g.DrawString(UiKit.GlyphFont, c + new Vector2(-24, 8), _content!.Levels[s.Level].Name[..1], HorizontalAlignment.Center, 48, 24, Colors.White);
    }

    // A path through hex centres, with arrowheads every few hexes and a mark at its end.
    private static void StrokePath(CanvasItem g, List<Vector2> pts, Color color, float width, bool dashed, int arrows, EndMark end)
    {
        if (pts.Count < 2)
        {
            return;
        }

        for (int k = 1; k < pts.Count; k++)
        {
            if (dashed)
            {
                g.DrawDashedLine(pts[k - 1], pts[k], color, width, 7);
            }
            else
            {
                g.DrawLine(pts[k - 1], pts[k], color, width, true);
            }

            if (arrows > 0 && k % arrows == 0)
            {
                Arrow(g, pts[k - 1], pts[k], color, width);
            }
        }

        Vector2 last = pts[^1];
        switch (end)
        {
            case EndMark.Dot:
                Arrow(g, pts[^2], last, color, width);
                g.DrawCircle(last, width + 3, color);
                break;
            case EndMark.Cross:
                g.DrawLine(last + new Vector2(-8, -8), last + new Vector2(8, 8), new Color(1f, 0.35f, 0.3f), width + 1);
                g.DrawLine(last + new Vector2(-8, 8), last + new Vector2(8, -8), new Color(1f, 0.35f, 0.3f), width + 1);
                break;
        }
    }

    // An arrowhead in the middle of a segment, pointing along it.
    private static void Arrow(CanvasItem g, Vector2 a, Vector2 b, Color color, float width)
    {
        Vector2 d = b - a;
        if (d.LengthSquared() < 1)
        {
            return;
        }

        d = d.Normalized();
        Vector2 n = new(-d.Y, d.X);
        Vector2 tip = a.Lerp(b, 0.6f) + (d * 5);
        float s = 4 + width;
        g.DrawPolygon([tip, tip - (d * s * 1.6f) + (n * s), tip - (d * s * 1.6f) - (n * s)], [color]);
    }

    // Hex pairs (col, row) to points, continuing across the east–west seam, from the hex a unit stands on.
    private List<Vector2> Points(int[] pairs, (int Col, int Row) start, bool closed)
    {
        var pts = new List<Vector2> { HexLayout.Center(start.Col, start.Row) };
        for (int k = 0; k + 1 < pairs.Length; k += 2)
        {
            pts.Add(Unwrapped(pts[^1], pairs[k], pairs[k + 1]));
        }

        if (closed && pairs.Length >= 2)
        {
            pts.RemoveAt(0); // a loop is drawn on its own hexes
            pts.Add(Unwrapped(pts[^1], pairs[0], pairs[1]));
        }

        return pts;
    }

    // The centre of a hex nearest to a point (the map repeats east–west).
    private Vector2 Unwrapped(Vector2 near, int col, int row)
    {
        Vector2 p = HexLayout.Center(col, row);
        p.X += MathF.Round((near.X - p.X) / _period.X) * _period.X;
        return p;
    }

    // Offsets of the copies of a point that are on screen.
    private IEnumerable<Vector2> Shifts(Vector2 p)
    {
        float k0 = MathF.Floor((_visible.Position.X - p.X) / _period.X) - 1;
        for (float k = k0; p.X + (k * _period.X) <= _visible.End.X + _period.X; k++)
        {
            float x = p.X + (k * _period.X);
            if (x >= _visible.Position.X - _period.X && x <= _visible.End.X + _period.X)
            {
                yield return new Vector2(k * _period.X, 0);
            }
        }
    }

    private bool Near(PlayUnitView u, PlaySettlementView s) => Math.Abs(u.Col - s.Col) + Math.Abs(u.Row - s.Row) <= 40;

    private PlaySettlementView? SelectedSettlement()
    {
        if (_view is null || _selected is not { } sel)
        {
            return null;
        }

        foreach (PlaySettlementView s in _view.Settlements)
        {
            if ((s.Col, s.Row) == sel)
            {
                return s;
            }
        }

        return null;
    }

    private PlaySettlementView? SettlementById(int id)
    {
        foreach (PlaySettlementView s in _view?.Settlements ?? [])
        {
            if (s.Id == id && !s.Remembered)
            {
                return s;
            }
        }

        return null;
    }

    private static Color Light(Color c) => c.Lerp(Colors.White, 0.35f);

    private static Color Grey(Color c)
    {
        float l = (c.R * 0.3f) + (c.G * 0.59f) + (c.B * 0.11f);
        return new Color(l, l, l, 0.85f);
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

    // The most urgent alert kind of a settlement (−1 = none).
    private int AlertOf(int settlement)
    {
        int worst = -1;
        foreach (PlayAlertView a in _view?.Alerts ?? [])
        {
            if (a.Settlement == settlement && (worst < 0 || a.Kind < worst))
            {
                worst = a.Kind;
            }
        }

        return worst;
    }

    private Layer Add(int z, Action<CanvasItem> paint)
    {
        var layer = new Layer(paint) { ZIndex = z };
        AddChild(layer);
        return layer;
    }

    // One drawing layer; it calls back into the overlay to paint.
    private sealed partial class Layer(Action<CanvasItem> paint) : Node2D
    {
        public override void _Draw() => paint(this);
    }
}
