using System;
using System.Collections.Generic;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// The drawn layers above the hexes (stage 5): fields, deposit icons, settlements, units, events and relations.
/// Each is its own node, so its place in the layer stack is its z-index and its opacity a shader parameter. Sprites
/// come from <see cref="ArtLibrary"/>; the civilization colour replaces the reserved magentas in a shader. Far away
/// (below <see cref="MapView.TexturedZoom"/>) simple shapes are drawn instead of pictures. Every layer is drawn on
/// each copy of the wrapped map that is on screen.
/// </summary>
public partial class MapObjects : Node2D
{
    private readonly ArtLibrary _art;
    private readonly List<LayerPainter> _painters = [];
    private ClientSession? _session;
    private ClientWorld? _world;
    private Vector2 _period;
    private Rect2 _view;
    private float _zoom = 1;
    private object? _lastView;
    private object? _lastDetails;
    private int _lastLayers = -1;
    private int _lastFog = -1;
    private float _fogAlpha;

    /// <summary>Creates the layers.</summary>
    public MapObjects(ArtLibrary art)
    {
        _art = art;
        Shader shader = GD.Load<Shader>("res://shaders/civ_sprite.gdshader");
        // Every layer has a sprite node (pictures through the civilization-colour shader) and a shape node
        // (lines, far-zoom squares and dots in plain colours).
        foreach (MapLayer layer in new[] { MapLayer.Fields, MapLayer.Deposits, MapLayer.Settlements, MapLayer.Units, MapLayer.Events, MapLayer.Relations })
        {
            foreach (bool lines in new[] { true, false })
            {
                var p = new LayerPainter { Source = this, Layer = layer, Lines = lines };
                if (!lines)
                {
                    p.Material = new ShaderMaterial { Shader = shader };
                }

                _painters.Add(p);
                AddChild(p);
            }
        }
    }

    /// <summary>Required by Godot; not used.</summary>
    public MapObjects()
        : this(new ArtLibrary(ArtLibrary.FindArtDirectory()))
    {
    }

    private bool Textured => _zoom >= MapView.TexturedZoom;

    // True when the fog at full opacity hides the cell: nothing is drawn there.
    private bool Fogged(int col, int row)
    {
        if (_world is null || _fogAlpha < 0.99f || _session is null || !_session.Fog.TryGetValue(_world.Id, out FogMessage? fog))
        {
            return false;
        }

        return row >= 0 && row < _world.Height && OverlayColors.Hidden(fog.Cells, _fogAlpha, _world.Index(col, row));
    }

    /// <summary>Sets what to draw; redraws only when the data, the layers or the visible area changed.</summary>
    public void Show(ClientSession session, ClientWorld world, LayerStack layers, Vector2 period, Rect2 view, float zoom)
    {
        bool changed = !ReferenceEquals(session.View, _lastView) || !ReferenceEquals(session.Details, _lastDetails)
            || layers.Version != _lastLayers || session.FogVersion != _lastFog || view != _view || period != _period || !ReferenceEquals(world, _world);
        if (!changed)
        {
            return;
        }

        _session = session;
        _world = world;
        _period = period;
        _view = view;
        _zoom = zoom;
        _lastView = session.View;
        _lastDetails = session.Details;
        _lastLayers = layers.Version;
        _lastFog = session.FogVersion;
        _fogAlpha = layers.Alpha(MapLayer.Fog);
        foreach (LayerPainter p in _painters)
        {
            Place(p, layers, p.Layer, sprites: !p.Lines);
        }
    }

    private static void Place(LayerPainter p, LayerStack layers, MapLayer layer, bool sprites)
    {
        float alpha = layers.Alpha(layer);
        p.Visible = alpha > 0;

        // Deposit icons lie with the objects, just above the fields; all drawn layers sit above the hexes.
        int index = layer == MapLayer.Deposits ? layers.IndexOf(MapLayer.Fields) : layers.IndexOf(layer);
        p.ZIndex = 10 + (2 * index) + (sprites ? 1 : 0);
        if (p.Material is ShaderMaterial m)
        {
            m.SetShaderParameter("layer_opacity", alpha);
        }
        else
        {
            p.Modulate = new Color(1, 1, 1, alpha);
        }

        p.QueueRedraw();
    }

    // Draws `draw` at every copy of `pos` that can be on screen (the map repeats every period east–west).
    private void Each(Vector2 pos, float margin, Action<Vector2> draw)
    {
        if (_period.X <= 0)
        {
            return;
        }

        int k0 = Mathf.FloorToInt((_view.Position.X - margin - pos.X) / _period.X) + 1;
        int k1 = Mathf.FloorToInt((_view.End.X + margin - pos.X) / _period.X);
        if (pos.Y < _view.Position.Y - margin || pos.Y > _view.End.Y + margin)
        {
            return;
        }

        for (int k = k0; k <= k1; k++)
        {
            draw(pos + new Vector2(k * _period.X, 0));
        }
    }

    /// <summary>Draws one layer (called from its node's _Draw).</summary>
    internal void Paint(LayerPainter p)
    {
        if (_session is null || _world is null)
        {
            return;
        }

        switch (p.Layer)
        {
            case MapLayer.Fields:
                PaintFields(p);
                break;
            case MapLayer.Deposits:
                PaintDeposits(p);
                break;
            case MapLayer.Settlements:
                PaintSettlements(p);
                if (p.Lines)
                {
                    PaintCivilizationNames(p);
                }

                break;
            case MapLayer.Units:
                PaintUnits(p);
                if (p.Lines)
                {
                    PaintUnitPaths(p);
                }

                break;
            case MapLayer.Events:
                PaintEvents(p);
                break;
            case MapLayer.Relations when p.Lines:
                PaintRelations(p);
                break;
        }
    }

    private void PaintFields(LayerPainter p)
    {
        if (_session?.Details is not { } details)
        {
            return;
        }

        Texture2D? field = Textured ? _art.Get("objects", "improvement_field_01") : null;
        Color far = OverlayColors.Rgb(0xEAD220);
        foreach (FieldView f in details.Fields)
        {
            if (f.World != _world!.Id || Fogged(f.Col, f.Row))
            {
                continue;
            }

            Vector2 c = HexLayout.Center(f.Col, f.Row);
            Each(c, 48, at =>
            {
                if (field is not null)
                {
                    if (!p.Lines)
                    {
                        p.DrawTexture(field, at - new Vector2(24, 21), new Color(1, 1, 1, 0.45f + (0.55f * f.WorkedPercent / 100f)));
                    }
                }
                else if (p.Lines)
                {
                    p.DrawRect(new Rect2(at - new Vector2(10, 8), new Vector2(20, 16)), far);
                }
            });
        }
    }

    // Deposit icons only when zoomed in; far away the Deposits cell layer shows them as colours.
    private void PaintDeposits(LayerPainter p)
    {
        if (_zoom < 0.6f || _session?.Content is not { } content || _world is not { } w)
        {
            return;
        }

        (int c0, int r0) = HexLayout.CellAt(_view.Position);
        (int c1, int r1) = HexLayout.CellAt(_view.End);
        for (int row = Math.Max(0, r0 - 1); row <= Math.Min(w.Height - 1, r1 + 1); row++)
        {
            for (int col = c0 - 1; col <= c1 + 1; col++)
            {
                int i = w.Index(col, row);
                int d = w.Deposit[i];
                if (d == 0 || d > content.Deposits.Count || w.Visibility[i] == MapChunkMessage.Unknown || Fogged(col, row))
                {
                    continue;
                }

                Texture2D? icon = _art.Get("objects", "deposit_" + content.Deposits[d - 1].Key);
                Vector2 at = HexLayout.Center(col, row) + new Vector2(-20, -2);
                Color tint = w.DepositDepth[i] == 0 ? Colors.White : new Color(0.55f, 0.55f, 0.55f);
                if (icon is not null)
                {
                    if (!p.Lines)
                    {
                        p.DrawTexture(icon, at, tint);
                    }
                }
                else if (p.Lines)
                {
                    p.DrawRect(new Rect2(at + new Vector2(4, 4), new Vector2(16, 16)), OverlayColors.Rgb(content.Deposits[d - 1].Color));
                }
            }
        }
    }

    private static readonly string[] LevelArt = ["camp", "village", "town"];
    private static readonly string[] StatusArt = ["active", "abandoned", "ruins"];

    private void PaintSettlements(LayerPainter p)
    {
        if (_session?.View?.Settlements is not { } settlements)
        {
            return;
        }

        foreach (SettlementView s in settlements)
        {
            if (s.World != _world!.Id || Fogged(s.Col, s.Row))
            {
                continue;
            }

            Color civ = OverlayColors.CivColor(s.Owner);
            Texture2D? tex = Textured
                ? _art.Get("objects", $"settlement_{LevelArt[Math.Clamp(s.Level, 0, LevelArt.Length - 1)]}_{StatusArt[Math.Clamp(s.Status, 0, 2)]}")
                : null;
            Each(HexLayout.Center(s.Col, s.Row), 48, at =>
            {
                if (tex is not null)
                {
                    if (!p.Lines)
                    {
                        p.DrawTexture(tex, at - new Vector2(24, 21), civ);
                    }

                    return;
                }

                if (!p.Lines)
                {
                    return;
                }

                // Far away: a square in the owner's colour, larger for higher levels; abandoned grey, ruins dark (GDD 5.5).
                float half = (7 + (3 * s.Level)) / Mathf.Max(0.35f, _zoom * 2);
                Color fill = s.Status switch { 1 => new Color(0.45f, 0.45f, 0.45f), 2 => new Color(0.25f, 0.22f, 0.2f), _ => civ };
                var rect = new Rect2(at - new Vector2(half, half), new Vector2(2 * half, 2 * half));
                p.DrawRect(rect, Colors.Black);
                p.DrawRect(rect.Grow(-Mathf.Max(1, half / 5)), fill);
            });
        }
    }

    // The name of each civilization on a banner above its most populous settlement, so peoples stand out at any zoom.
    private void PaintCivilizationNames(LayerPainter p)
    {
        if (_session?.View is not { } view || view.Settlements is not { } settlements)
        {
            return;
        }

        var best = new Dictionary<int, (Vector2 At, int People)>();
        foreach (SettlementView s in settlements)
        {
            if (s.World != _world!.Id || s.Status != 0 || Fogged(s.Col, s.Row))
            {
                continue;
            }

            int people = 0;
            foreach (int n in s.Population ?? [])
            {
                people += n;
            }

            if (!best.TryGetValue(s.Owner, out var b) || people > b.People)
            {
                best[s.Owner] = (HexLayout.Center(s.Col, s.Row), people);
            }
        }

        Font font = Ui.PixelTheme.Current?.DefaultFont ?? ThemeDB.FallbackFont;
        int size = (int)Mathf.Clamp(26 / _zoom, 32, 320);
        size -= size % 16; // whole multiples of the pixel font's grid stay sharp
        foreach (CivilizationView c in view.Civilizations)
        {
            if (!c.IsAlive || !best.TryGetValue(c.Id, out var at))
            {
                continue;
            }

            Vector2 text = font.GetStringSize(c.Name, HorizontalAlignment.Left, -1, size);
            Color color = OverlayColors.CivColor(c.Id);
            Each(at.At, text.X + 48, pos =>
            {
                Vector2 origin = pos - new Vector2(text.X / 2, 30 + (size * 0.6f));
                var plate = new Rect2(origin - new Vector2(size * 0.4f, text.Y * 0.8f), new Vector2(text.X + (size * 0.8f), text.Y * 1.1f));
                p.DrawRect(plate, new Color(0.11f, 0.09f, 0.06f));
                p.DrawRect(plate, color, filled: false, width: Mathf.Max(2, size / 8f));
                p.DrawString(font, origin, c.Name, HorizontalAlignment.Left, -1, size, color);
            });
        }
    }

    private void PaintUnits(LayerPainter p)
    {
        if (_session?.Details is not { } details)
        {
            return;
        }

        Texture2D? tex = Textured ? _art.Get("units", "unit_migrants_idle_01") : null;
        foreach (JourneyView j in details.Journeys)
        {
            if (j.World != _world!.Id || Fogged(j.Col, j.Row))
            {
                continue;
            }

            Color civ = OverlayColors.CivColor(j.Owner);
            Each(HexLayout.Center(j.Col, j.Row), 48, at =>
            {
                if (tex is not null)
                {
                    // Units stand at the bottom of the cell so they do not hide the settlement (GDD 12.1).
                    if (!p.Lines)
                    {
                        p.DrawTexture(tex, at + new Vector2(-16, -11), civ);
                    }
                }
                else if (p.Lines)
                {
                    p.DrawCircle(at, 9 / Mathf.Max(0.35f, _zoom * 2), civ);
                }
            });
        }
    }

    // The way of each wandering camp to its target: a line in the owner's colour ending in an arrow head.
    private void PaintUnitPaths(LayerPainter p)
    {
        if (_session?.Details is not { } details)
        {
            return;
        }

        float width = Mathf.Max(2, 2.5f / _zoom);
        foreach (JourneyView j in details.Journeys)
        {
            if (j.World != _world!.Id || Fogged(j.Col, j.Row))
            {
                continue;
            }

            Vector2 from = HexLayout.Center(j.Col, j.Row);
            Vector2 to = Nearest(from, HexLayout.Center(j.TargetCol, j.TargetRow));
            Color civ = OverlayColors.CivColor(j.Owner);
            Each(from, (to - from).Length() + 48, at =>
            {
                Vector2 end = at + (to - from);
                p.DrawDashedLine(at, end, civ, width, 4 * width);
                Vector2 dir = (end - at).Normalized();
                Vector2 side = new(-dir.Y, dir.X);
                float head = 5 * width;
                p.DrawColoredPolygon([end, end - (dir * head) + (side * head * 0.6f), end - (dir * head) - (side * head * 0.6f)], civ);
            });
        }
    }

    private void PaintEvents(LayerPainter p)
    {
        if (_session?.Details is not { } details)
        {
            return;
        }

        // One event covers many cells (same effect, same end): the icon goes on the middle cell of the group, the
        // other cells get a small dot, so a big drought does not bury the map in icons.
        var groups = new Dictionary<(string, long), List<CellEventView>>();
        foreach (CellEventView e in details.Events)
        {
            if (e.World != _world!.Id || Fogged(e.Col, e.Row))
            {
                continue;
            }

            if (!groups.TryGetValue((e.Effect, e.EndMonth), out List<CellEventView>? list))
            {
                list = [];
                groups[(e.Effect, e.EndMonth)] = list;
            }

            list.Add(e);
        }

        Color dot = OverlayColors.Rgb(0xEC271E);
        foreach (((string effect, _), List<CellEventView> cells) in groups)
        {
            Vector2 first = HexLayout.Center(cells[0].Col, cells[0].Row);
            Vector2 sum = Vector2.Zero;
            foreach (CellEventView e in cells)
            {
                sum += Nearest(first, HexLayout.Center(e.Col, e.Row));
            }

            Vector2 middle = sum / cells.Count;
            CellEventView center = cells[0];
            float best = float.MaxValue;
            foreach (CellEventView e in cells)
            {
                float d = Nearest(middle, HexLayout.Center(e.Col, e.Row)).DistanceSquaredTo(middle);
                if (d < best)
                {
                    best = d;
                    center = e;
                }
            }

            Texture2D? icon = Textured ? _art.Get("effects", "event_" + effect) : null;
            foreach (CellEventView e in cells)
            {
                bool main = ReferenceEquals(e, center);
                Each(HexLayout.Center(e.Col, e.Row), 48, at =>
                {
                    if (main && icon is not null)
                    {
                        if (!p.Lines)
                        {
                            p.DrawTexture(icon, at - new Vector2(12, 12), Colors.White);
                        }
                    }
                    else if (p.Lines)
                    {
                        p.DrawCircle(at + new Vector2(10, -10), (main ? 6 : 2.5f) / Mathf.Max(0.35f, _zoom * 2), dot);
                    }
                });
            }
        }
    }

    // Lines between the largest settlements of civilizations that know each other, coloured by their strongest tie.
    private void PaintRelations(LayerPainter p)
    {
        if (_session?.Details is not { } details || _session.View?.Settlements is not { } settlements)
        {
            return;
        }

        var anchors = new Dictionary<int, (Vector2 At, int People)>();
        foreach (SettlementView s in settlements)
        {
            if (s.World != _world!.Id || s.Status != 0 || Fogged(s.Col, s.Row))
            {
                continue;
            }

            int people = 0;
            if (s.Population is { } pop)
            {
                foreach (int n in pop)
                {
                    people += n;
                }
            }

            if (!anchors.TryGetValue(s.Owner, out var best) || people > best.People)
            {
                anchors[s.Owner] = (HexLayout.Center(s.Col, s.Row), people);
            }
        }

        float width = Mathf.Max(2, 3f / _zoom);
        foreach (RelationView r in details.Relations)
        {
            if (r.Contact < RelationView.Met || !anchors.TryGetValue(r.A, out var a) || !anchors.TryGetValue(r.B, out var b))
            {
                continue;
            }

            (Color color, bool dashed) = RelationStyle(r);
            Vector2 to = Nearest(a.At, b.At);
            Each(a.At, (to - a.At).Length() + 48, at =>
            {
                Vector2 end = at + (to - a.At);
                if (dashed)
                {
                    p.DrawDashedLine(at, end, color, width, 3 * width);
                }
                else
                {
                    p.DrawLine(at, end, color, width);
                }
            });
        }
    }

    /// <summary>Colour of a relation line: war red, alliance blue, pact teal, trade gold, peace white, else grey and dashed.</summary>
    public static (Color Color, bool Dashed) RelationStyle(RelationView r)
    {
        if ((r.Treaties & RelationView.War) != 0)
        {
            return (OverlayColors.Rgb(0xEC271E), false);
        }

        if ((r.Treaties & RelationView.Alliance) != 0)
        {
            return (OverlayColors.Rgb(0x1A4AD0), false);
        }

        if ((r.Treaties & RelationView.NonAggression) != 0)
        {
            return (OverlayColors.Rgb(0x3FA7C9), false);
        }

        if ((r.Treaties & RelationView.Trade) != 0)
        {
            return (OverlayColors.Rgb(0xEAD220), false);
        }

        if ((r.Treaties & RelationView.Peace) != 0)
        {
            return (OverlayColors.Rgb(0xFBF9FB), false);
        }

        return (OverlayColors.Rgb(0xA9A2A0), true);
    }

    // The copy of `to` nearest to `from` on the wrapped map (shortest way east or west).
    private Vector2 Nearest(Vector2 from, Vector2 to)
    {
        if (_period.X > 0)
        {
            to.X += MathF.Round((from.X - to.X) / _period.X) * _period.X;
        }

        return to;
    }
}

/// <summary>One drawn layer node; <see cref="MapObjects"/> paints it.</summary>
public partial class LayerPainter : Node2D
{
    /// <summary>Who paints this node.</summary>
    public MapObjects? Source { get; set; }

    /// <summary>Which layer it draws.</summary>
    public MapLayer Layer { get; set; }

    /// <summary>True for the line part of a layer (no sprite shader).</summary>
    public bool Lines { get; set; }

    /// <inheritdoc />
    public override void _Draw() => Source?.Paint(this);
}
