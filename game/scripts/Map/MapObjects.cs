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

    /// <summary>
    /// Sets what to draw; redraws only when the layers or the fog changed, the view left the area drawn last time (drawn
    /// with a margin, so panning and following a civilization rarely redraw) or the zoom changed, and for new data at most
    /// every <see cref="DataRedrawSeconds"/> (stage 5b: a steadier frame rate).
    /// </summary>
    public void Show(ClientSession session, ClientWorld world, LayerStack layers, Vector2 period, Rect2 view, float zoom)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        bool data = !ReferenceEquals(session.View, _lastView) || !ReferenceEquals(session.Details, _lastDetails);
        bool dataDue = data && System.Diagnostics.Stopwatch.GetElapsedTime(_lastDataRedraw, now).TotalSeconds >= DataRedrawSeconds;
        bool moved = !_view.Encloses(view) || Mathf.Abs(zoom - _zoom) > _zoom * 0.02f;
        bool changed = dataDue || moved || layers.Version != _lastLayers || session.FogVersion != _lastFog || period != _period || !ReferenceEquals(world, _world);
        if (!changed)
        {
            // Walking units (stage 6 PR 3a) glide between the views: only their layer is drawn again, every frame.
            if (_session is not null && _walkers.Count > 0)
            {
                foreach (LayerPainter p in _painters)
                {
                    if (p.Layer == MapLayer.Units && p.Visible)
                    {
                        p.QueueRedraw();
                    }
                }
            }

            return;
        }

        if (data)
        {
            _lastDataRedraw = now;
        }

        _session = session;
        _world = world;
        _period = period;
        _view = view.Grow(Mathf.Max(view.Size.X, view.Size.Y) * ViewMargin);
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

    // The picture of a settlement of a level and status, looked up once (no new name string per settlement and redraw).
    private Texture2D? SettlementArt(int level, int status)
    {
        int key = (level * 3) + status;
        if (!_settlementArt.TryGetValue(key, out Texture2D? tex))
        {
            tex = _art.Get("objects", $"settlement_{LevelArt[level]}_{StatusArt[status]}");
            _settlementArt[key] = tex;
        }

        return tex;
    }

    private readonly Dictionary<int, Texture2D?> _settlementArt = [];

    // New data redraws the objects at most this often (the server sends views a few times a second at top speed).
    private const double DataRedrawSeconds = 0.25;

    // The objects are drawn this far (share of the view's larger side) beyond each edge of the view.
    private const float ViewMargin = 0.35f;

    private long _lastDataRedraw;

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

    // Every copy of `pos` that can be on screen (the map repeats every period east–west); no allocation, unlike a callback.
    private CopyList Copies(Vector2 pos, float margin)
    {
        if (_period.X <= 0 || pos.Y < _view.Position.Y - margin || pos.Y > _view.End.Y + margin)
        {
            return new CopyList(pos, 0, 1, 0);
        }

        int k0 = Mathf.FloorToInt((_view.Position.X - margin - pos.X) / _period.X) + 1;
        int k1 = Mathf.FloorToInt((_view.End.X + margin - pos.X) / _period.X);
        return new CopyList(pos, _period.X, k0, k1);
    }

    private readonly struct CopyList(Vector2 pos, float period, int k0, int k1)
    {
        public CopyEnumerator GetEnumerator() => new(pos, period, k0, k1);
    }

    private struct CopyEnumerator(Vector2 pos, float period, int k0, int k1)
    {
        private int _k = k0 - 1;

        public readonly Vector2 Current => pos + new Vector2(_k * period, 0);

        public bool MoveNext() => ++_k <= k1;
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
            foreach (Vector2 at in Copies(c, 48))
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
            }
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

        _capitals.Clear();
        foreach (CivilizationView c in _session.View.Civilizations)
        {
            if (c.IsAlive && c.Capital >= 0)
            {
                _capitals.Add(c.Capital);
            }
        }

        foreach (SettlementView s in settlements)
        {
            if (s.World != _world!.Id || Fogged(s.Col, s.Row))
            {
                continue;
            }

            Color civ = OverlayColors.CivColor(s.Owner);
            bool capital = s.Status == 0 && _capitals.Contains(s.Id);
            Texture2D? tex = Textured
                ? SettlementArt(Math.Clamp(s.Level, 0, LevelArt.Length - 1), Math.Clamp(s.Status, 0, 2))
                : null;
            foreach (Vector2 at in Copies(HexLayout.Center(s.Col, s.Row), 48))
            {
                if (tex is not null)
                {
                    if (!p.Lines)
                    {
                        p.DrawTexture(tex, at - new Vector2(24, 21), civ);
                        if (capital)
                        {
                            CapitalMark(p, at + new Vector2(0, -26), 7);
                        }
                    }

                    continue;
                }

                if (!p.Lines)
                {
                    continue;
                }

                // Far away: a square in the owner's colour, larger for higher levels; abandoned grey, ruins dark (GDD 5.5).
                float half = (7 + (3 * s.Level)) / Mathf.Max(0.35f, _zoom * 2);
                Color fill = s.Status switch { 1 => new Color(0.45f, 0.45f, 0.45f), 2 => new Color(0.25f, 0.22f, 0.2f), _ => civ };
                var rect = new Rect2(at - new Vector2(half, half), new Vector2(2 * half, 2 * half));
                p.DrawRect(rect, Colors.Black);
                p.DrawRect(rect.Grow(-Mathf.Max(1, half / 5)), fill);
                if (capital)
                {
                    CapitalMark(p, at - new Vector2(0, half * 2.2f), half * 0.9f);
                }
            }
        }
    }

    // The capital: a gold diamond with a dark outline above the settlement (GDD 7.3, stage 5b).
    private static void CapitalMark(LayerPainter p, Vector2 at, float r)
    {
        Vector2[] outer = [at + new Vector2(0, -r - 2), at + new Vector2(r + 2, 0), at + new Vector2(0, r + 2), at + new Vector2(-r - 2, 0)];
        Vector2[] inner = [at + new Vector2(0, -r), at + new Vector2(r, 0), at + new Vector2(0, r), at + new Vector2(-r, 0)];
        p.DrawColoredPolygon(outer, new Color(0.11f, 0.09f, 0.06f));
        p.DrawColoredPolygon(inner, new Color(1f, 0.82f, 0.25f));
    }

    // Settlements of one civilization this close (pixels, about six hexes) form one cluster around its capital.
    private const float ClusterLinkPixels = 240;

    // A banner stays where it is while its new place is this close (pixels, about three hexes): no jumping.
    private const float BannerKeepPixels = 130;

    private readonly Dictionary<int, Vector2> _bannerAt = [];
    private readonly Dictionary<int, List<(Vector2 At, int People, int Id)>> _byOwner = [];
    private readonly List<Vector2> _cluster = [];
    private readonly HashSet<int> _capitals = [];
    private bool[] _taken = [];

    // The name of each civilization on a banner over the middle of the cluster of settlements around its capital (or its
    // largest settlement before it has one). The banner moves only when that middle moves by a few hexes (stage 5b).
    private void PaintCivilizationNames(LayerPainter p)
    {
        if (_session?.View is not { } view || view.Settlements is not { } settlements)
        {
            return;
        }

        foreach (List<(Vector2 At, int People, int Id)> list in _byOwner.Values)
        {
            list.Clear();
        }

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

            if (!_byOwner.TryGetValue(s.Owner, out List<(Vector2 At, int People, int Id)>? own))
            {
                own = [];
                _byOwner[s.Owner] = own;
            }

            own.Add((HexLayout.Center(s.Col, s.Row), people, s.Id));
        }

        Font font = Ui.PixelTheme.Current?.DefaultFont ?? ThemeDB.FallbackFont;
        int size = (int)Mathf.Clamp(26 / _zoom, 32, 320);
        size -= size % 16; // whole multiples of the pixel font's grid stay sharp
        foreach (CivilizationView c in view.Civilizations)
        {
            if (!c.IsAlive || !_byOwner.TryGetValue(c.Id, out List<(Vector2 At, int People, int Id)>? own) || own.Count == 0)
            {
                _bannerAt.Remove(c.Id);
                continue;
            }

            Vector2 target = ClusterMiddle(own, c.Capital);
            if (!_bannerAt.TryGetValue(c.Id, out Vector2 at) || Wrapped(target - at).Length() > BannerKeepPixels)
            {
                at = target;
                _bannerAt[c.Id] = at;
            }

            Vector2 text = font.GetStringSize(c.Name, HorizontalAlignment.Left, -1, size);
            Color color = OverlayColors.CivColor(c.Id);
            foreach (Vector2 pos in Copies(at, text.X + 48))
            {
                Vector2 origin = pos - new Vector2(text.X / 2, 30 + (size * 0.6f));
                var plate = new Rect2(origin - new Vector2(size * 0.4f, text.Y * 0.8f), new Vector2(text.X + (size * 0.8f), text.Y * 1.1f));
                p.DrawRect(plate, new Color(0.11f, 0.09f, 0.06f));
                p.DrawRect(plate, color, filled: false, width: Mathf.Max(2, size / 8f));
                p.DrawString(font, origin, c.Name, HorizontalAlignment.Left, -1, size, color);
            }
        }
    }

    // The middle of the settlements linked to the capital (each within ClusterLinkPixels of another one of them).
    private Vector2 ClusterMiddle(List<(Vector2 At, int People, int Id)> own, int capital)
    {
        int start = 0;
        for (int k = 0; k < own.Count; k++)
        {
            if (own[k].Id == capital)
            {
                start = k;
                break;
            }

            if (capital < 0 && own[k].People > own[start].People)
            {
                start = k;
            }
        }

        // Positions relative to the capital, unwrapped east–west, so a cluster across the map's edge stays whole.
        Vector2 origin = own[start].At;
        _cluster.Clear();
        _cluster.Add(Vector2.Zero);
        if (_taken.Length < own.Count)
        {
            _taken = new bool[own.Count * 2];
        }

        bool[] taken = _taken;
        Array.Clear(taken, 0, own.Count);
        taken[start] = true;
        for (int head = 0; head < _cluster.Count; head++)
        {
            for (int k = 0; k < own.Count; k++)
            {
                if (taken[k])
                {
                    continue;
                }

                Vector2 rel = Wrapped(own[k].At - origin);
                if (_cluster[head].DistanceTo(rel) <= ClusterLinkPixels)
                {
                    taken[k] = true;
                    _cluster.Add(rel);
                }
            }
        }

        Vector2 sum = Vector2.Zero;
        foreach (Vector2 v in _cluster)
        {
            sum += v;
        }

        Vector2 middle = origin + (sum / _cluster.Count);
        return _period.X > 0 ? new Vector2(Mathf.PosMod(middle.X, _period.X), middle.Y) : middle;
    }

    // The shortest east–west form of a difference between two points (the map repeats every period).
    private Vector2 Wrapped(Vector2 d)
    {
        if (_period.X > 0)
        {
            d.X = Mathf.PosMod(d.X + (_period.X / 2), _period.X) - (_period.X / 2);
        }

        return d;
    }

    // Where each walking unit was drawn when the last view came and where that view puts it (stage 6 PR 3a): it glides from
    // the one to the other in the time between two views, so it moves smoothly though the server sends a few views a second.
    private readonly Dictionary<long, (Vector2 From, Vector2 To)> _walkers = [];
    private readonly HashSet<long> _walkersSeen = [];
    private GameViewMessage? _walkersView;
    private long _walkersAt;

    // Seconds between two views of the server (its view interval) the glide lasts.
    private const double GlideSeconds = 0.1;

    private float Glide()
    {
        double t = System.Diagnostics.Stopwatch.GetElapsedTime(_walkersAt).TotalSeconds / GlideSeconds;
        return (float)Math.Clamp(t, 0, 1);
    }

    private void TrackWalkers(GameViewMessage view)
    {
        if (ReferenceEquals(view, _walkersView))
        {
            return;
        }

        float g = Glide();
        _walkersSeen.Clear();
        foreach (UnitView u in view.Units ?? [])
        {
            Vector2 at = HexLayout.Center(u.Col, u.Row);
            Vector2 to = at + (Wrapped(HexLayout.Center(u.NextCol, u.NextRow) - at) * (u.ProgressPermille / 1000f));
            Vector2 from = _walkers.TryGetValue(u.Id, out (Vector2 From, Vector2 To) old) ? old.From + (Wrapped(old.To - old.From) * g) : to;
            _walkers[u.Id] = (from, from + Wrapped(to - from));
            _walkersSeen.Add(u.Id);
        }

        if (_walkersSeen.Count != _walkers.Count)
        {
            var gone = new List<long>();
            foreach (long id in _walkers.Keys)
            {
                if (!_walkersSeen.Contains(id))
                {
                    gone.Add(id);
                }
            }

            foreach (long id in gone)
            {
                _walkers.Remove(id);
            }
        }

        _walkersView = view;
        _walkersAt = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    // Units walking (stage 6 PR 3a–3b) by role: settlers and carriers as migrants, scouts as a scout, armies as one to
    // three warriors by size (GDD 9.3), in the owner's colour; dots from afar.
    private void PaintWalkers(LayerPainter p)
    {
        if (_session?.View is not { Units: { Count: > 0 } units } view)
        {
            _walkers.Clear();
            return;
        }

        TrackWalkers(view);
        float g = Glide();
        Texture2D? settlers = Textured ? _art.Get("units", "unit_migrants_idle_01") : null;
        Texture2D? scout = Textured ? _art.Get("units", "unit_scout_idle_01") : null;
        Texture2D? workers = Textured ? _art.Get("units", "unit_workers_idle_01") : null;
        foreach (UnitView u in units)
        {
            if (u.World != _world!.Id || Fogged(u.Col, u.Row) || !_walkers.TryGetValue(u.Id, out (Vector2 From, Vector2 To) w))
            {
                continue;
            }

            Vector2 pos = w.From + ((w.To - w.From) * g);
            if (_period.X > 0)
            {
                pos.X = Mathf.PosMod(pos.X, _period.X);
            }

            Color civ = OverlayColors.CivColor(u.Owner);
            Texture2D? tex = u.Role is 1 or 2 ? scout : u.Role == 4 ? workers ?? settlers : settlers;
            int figures = u.Role == 2 ? (u.People >= 40 ? 3 : u.People >= 15 ? 2 : 1) : 1;
            foreach (Vector2 at in Copies(pos, 48))
            {
                for (int f = 0; f < figures; f++)
                {
                    Vector2 spot = at + new Vector2((f - ((figures - 1) / 2f)) * 10, f == 1 ? -4 : 0);
                    if (tex is not null)
                    {
                        if (!p.Lines)
                        {
                            p.DrawTexture(tex, spot + new Vector2(-16, -11), civ);
                        }
                    }
                    else if (p.Lines)
                    {
                        float r = (u.Role == 1 ? 5 : u.Role == 2 ? 7 : u.Role == 4 ? 6 : 8) / Mathf.Max(0.35f, _zoom * 2);
                        p.DrawCircle(spot, r + 1, Colors.Black);
                        p.DrawCircle(spot, r, civ);
                    }
                }
            }
        }
    }

    private void PaintUnits(LayerPainter p)
    {
        PaintWalkers(p);
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
            foreach (Vector2 at in Copies(HexLayout.Center(j.Col, j.Row), 48))
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
            }
        }

        // Armies and carriers are units since stage 6 PR 3b (PaintWalkers); the campaign layer still carries their details.
    }

    // The way of each wandering camp to its target: a line in the owner's colour ending in an arrow head.
    private void PaintUnitPaths(LayerPainter p)
    {
        float width = Mathf.Max(2, 2.5f / _zoom);
        PaintTaskCells(p, width);
        if (_session?.Details is not { } details)
        {
            return;
        }

        foreach (JourneyView j in details.Journeys)
        {
            if (j.World != _world!.Id || Fogged(j.Col, j.Row))
            {
                continue;
            }

            Arrow(p, j.Col, j.Row, j.TargetCol, j.TargetRow, OverlayColors.CivColor(j.Owner), width);
        }

        if (details.Armies is { Count: > 0 } armies && _world!.Id == 0)
        {
            foreach (ArmyView a in armies)
            {
                if (!Fogged(a.Col, a.Row) && (a.Col != a.TargetCol || a.Row != a.TargetRow))
                {
                    Arrow(p, a.Col, a.Row, a.TargetCol, a.TargetRow, OverlayColors.CivColor(a.Owner), width * 1.5f);
                }
            }
        }
    }

    // Area tasks (stage 6 PR 4a): the cell a work group works on, outlined in its owner's colour, and its way there while it walks.
    private void PaintTaskCells(LayerPainter p, float width)
    {
        if (_session?.View is not { Units: { Count: > 0 } units })
        {
            return;
        }

        foreach (UnitView u in units)
        {
            if (u.Role != 4 || u.TargetCol < 0 || u.World != _world!.Id || Fogged(u.TargetCol, u.TargetRow))
            {
                continue;
            }

            Color civ = OverlayColors.CivColor(u.Owner);
            if (u.Col != u.TargetCol || u.Row != u.TargetRow)
            {
                Arrow(p, u.Col, u.Row, u.TargetCol, u.TargetRow, civ, width);
            }

            foreach (Vector2 at in Copies(HexLayout.Center(u.TargetCol, u.TargetRow), 48))
            {
                Vector2[] corners =
                [
                    at + new Vector2(-24, 0), at + new Vector2(-12, -21), at + new Vector2(12, -21),
                    at + new Vector2(24, 0), at + new Vector2(12, 21), at + new Vector2(-12, 21), at + new Vector2(-24, 0),
                ];
                p.DrawPolyline(corners, civ, width);
            }
        }
    }

    // A dashed line in the owner's colour ending in an arrow head.
    private void Arrow(LayerPainter p, int col, int row, int targetCol, int targetRow, Color civ, float width)
    {
        Vector2 from = HexLayout.Center(col, row);
        Vector2 to = Nearest(from, HexLayout.Center(targetCol, targetRow));
        foreach (Vector2 at in Copies(from, (to - from).Length() + 48))
        {
            Vector2 end = at + (to - from);
            p.DrawDashedLine(at, end, civ, width, 4 * width);
            Vector2 dir = (end - at).Normalized();
            Vector2 side = new(-dir.Y, dir.X);
            float head = 5 * width;
            p.DrawColoredPolygon([end, end - (dir * head) + (side * head * 0.6f), end - (dir * head) - (side * head * 0.6f)], civ);
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
                foreach (Vector2 at in Copies(HexLayout.Center(e.Col, e.Row), 48))
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
                }
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
            foreach (Vector2 at in Copies(a.At, (to - a.At).Length() + 48))
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
            }
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
