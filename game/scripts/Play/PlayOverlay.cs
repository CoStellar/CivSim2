using System;
using System.Collections.Generic;
using System.Linq;
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
/// Ground, units and labels are split by map chunk (32×32 hexes): each chunk is its own node, drawn again only when what is
/// in it changes, and Godot skips the nodes off screen. How much is drawn depends on the zoom (far: marks only; middle: no
/// small numbers; near: everything). The routes layer is drawn again when the view changes; the lift and effects layers,
/// which move, every frame. Placeholder shapes and letters until pixel art comes.
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
    private Layer _marks = null!;
    private Layer _routes = null!;
    private Layer _lift = null!;
    private Layer _effects = null!;
    private readonly Dictionary<(int Chunk, int Copy), ChunkNodes> _chunkNodes = [];
    private readonly Dictionary<int, ChunkContent> _content2 = [];
    private float _zoom = 1;
    private bool _rebucket = true;

    private const int ChunkSize = 32;

    /// <summary>Id of the selected unit (−1 = none).</summary>
    public int SelectedUnit { get; set; } = -1;

    /// <summary>All selected units (several with Shift).</summary>
    public HashSet<int> SelectedUnits { get; } = [];

    /// <summary>Hexes being painted as a guard zone (combat mode), or null.</summary>
    public List<(int Col, int Row)>? GuardPaint { get; set; }

    /// <summary>Hexes being drawn (a path, a patrol, a route), in order.</summary>
    public List<(int Col, int Row)> DrawPath { get; } = [];

    /// <summary>Planned ways of units, each from the unit's hex through its waypoints (sent when Ctrl is let go).</summary>
    public List<List<(int Col, int Row)>> Plans { get; } = [];

    /// <summary>Colour of the drawn path.</summary>
    public Color DrawColor { get; set; } = new(1f, 0.95f, 0.5f);

    /// <summary>Which of our routes show besides those of what is selected (G cycles): 0 none, 1 orders under way (marches,
    /// carriers on their way), 2 those and the standing ones (patrols, caravan routes).</summary>
    public int RouteDisplay { get; set; }

    /// <summary>The map as the player knows it (for roads).</summary>
    public CivSim.Game.Net.ClientWorld? World { get; set; }

    /// <summary>How far the selected hex is lifted, 0–1 (grows over a tenth of a second after a click).</summary>
    public float Lift { get; private set; }

    /// <summary>Pictures of buildings, settlements and units (decisions G1-G6, 2026-11-23); null: shapes and letters.</summary>
    public ArtLibrary? Art { get; set; }

    private readonly Dictionary<string, Texture2D?> _sprites = [];

    /// <summary>Forgets the coloured pictures (after the art pack changed).</summary>
    public void ClearSprites() => _sprites.Clear();

    // A picture with the people's colour in place of the reserved magentas (light, middle, dark: as civ_sprite.gdshader),
    // or grey all over for what we only remember; made once per picture, colour and greyness.
    internal Texture2D? Sprite(string layer, string name, int civ, bool grey = false)
    {
        if (Art is null)
        {
            return null;
        }

        string key = $"{layer}/{name}/{civ}/{grey}";
        if (_sprites.TryGetValue(key, out Texture2D? cached))
        {
            return cached;
        }

        Image? image = Art.LoadImage(layer, name);
        Texture2D? tex = null;
        if (image is not null)
        {
            Color color = CivColor(civ);
            for (int y = 0; y < image.GetHeight(); y++)
            {
                for (int x = 0; x < image.GetWidth(); x++)
                {
                    Color p = image.GetPixel(x, y);
                    if (p.A < 0.5f)
                    {
                        continue;
                    }

                    if (p.G < 0.02f && MathF.Abs(p.R - p.B) < 0.02f && p.R > 0.4f)
                    {
                        p = p.R > 0.9f ? color.Lerp(Colors.White, 0.25f) : p.R > 0.6f ? color : color * 0.6f;
                        p.A = 1;
                    }

                    if (grey)
                    {
                        float l = (p.R * 0.3f) + (p.G * 0.59f) + (p.B * 0.11f);
                        p = new Color(l * 0.95f, l * 0.95f, l * 0.95f, 1);
                    }

                    image.SetPixel(x, y, p);
                }
            }

            tex = ImageTexture.CreateFromImage(image);
        }

        _sprites[key] = tex;
        return tex;
    }

    // Draws a picture as large as fits in the area, keeping its shape, standing on the area's bottom edge.
    private static void DrawFit(CanvasItem g, Texture2D tex, Rect2 area, Color modulate)
    {
        Vector2 size = tex.GetSize();
        float k = MathF.Min(area.Size.X / size.X, area.Size.Y / size.Y);
        Vector2 drawn = size * k;
        var at = new Vector2(area.Position.X + ((area.Size.X - drawn.X) / 2), area.End.Y - drawn.Y);
        g.DrawTextureRect(tex, new Rect2(at, drawn), false, modulate);
    }

    // The whole hex around a centre, as the hex pictures (96x84) cover it.
    private static Rect2 HexRect(Vector2 c, float scale) =>
        new(c - (new Vector2(HexLayout.HexWidth, HexLayout.HexHeight) * 0.5f * scale), new Vector2(HexLayout.HexWidth, HexLayout.HexHeight) * scale);

    // Where a unit's body is drawn on its hex: the middle with pictures (Patryk, 2026-11-23), beside the centre as a token.
    private Vector2 UnitBody => Art is null ? new Vector2(10, -8) : new Vector2(0, -6);

    // A soft shadow on the ground under a picture, a little to the right (light from the top left).
    private static void GroundShadow(CanvasItem g, Rect2 area, float flat = 0.13f)
    {
        var c = new Vector2(area.Position.X + (area.Size.X * 0.56f), area.End.Y - (area.Size.Y * 0.06f));
        Ellipse(g, c, area.Size.X * 0.48f, area.Size.Y * flat, new Color(0, 0, 0, 0.26f));
    }

    // The hex rectangle shrunk about its centre (a hex picture drawn there stays inside the hex, clear of the border).
    private static Rect2 HexInner(Vector2 c, float scale, float k)
    {
        Vector2 size = new Vector2(HexLayout.HexWidth, HexLayout.HexHeight) * scale * k;
        return new Rect2(c - (size * 0.5f), size);
    }

    // G5: one picture per building; its state is shown over it (faded with scaffolding while built, darker when stopped,
    // a red mark without inputs, a gold frame for priority).
    private static Color StateModulate(PlayBuildingView b) => b.State switch
    {
        0 or 1 or 5 => new Color(1, 1, 1, 0.55f),
        3 => new Color(0.6f, 0.6f, 0.6f, 1),
        _ => Colors.White,
    };

    private static void Scaffold(CanvasItem g, Rect2 r)
    {
        var wood = new Color(0.55f, 0.36f, 0.18f, 0.95f);
        for (float x = r.Position.X + 2; x < r.End.X - 1; x += MathF.Max(5, r.Size.X / 4))
        {
            g.DrawLine(new Vector2(x, r.Position.Y + 2), new Vector2(x, r.End.Y), wood, 1.2f);
        }

        g.DrawLine(new Vector2(r.Position.X + 1, r.Position.Y + (r.Size.Y * 0.45f)), new Vector2(r.End.X - 1, r.Position.Y + (r.Size.Y * 0.45f)), wood, 1.2f);
    }

    private static void NoInputMark(CanvasItem g, Vector2 at)
    {
        g.DrawCircle(at, 4.5f, new Color(0.75f, 0.1f, 0.08f));
        g.DrawString(UiKit.GlyphFont, at + new Vector2(-4, 4), "!", HorizontalAlignment.Center, 8, 10, Colors.White);
    }

    /// <summary>Colour of a civilization.</summary>
    public static Color CivColor(int civ) => CivColors[(((civ + ColorShift) % CivColors.Length) + CivColors.Length) % CivColors.Length];

    /// <summary>How far this game turns the colours round (2026-11-24, Patryk: our people were always yellow).</summary>
    public static int ColorShift { get; private set; }

    /// <summary>How far this game turns the emblems' drawings round.</summary>
    public static int MotifShift { get; private set; }

    /// <summary>
    /// Picks the peoples' colours and emblems for a game from its seed: every game turns both lists by its own steps, so each
    /// people still has its own colour and drawing, but ours is not always the first. Returns whether they changed.
    /// </summary>
    public static bool ChooseLooks(ulong seed)
    {
        ulong z = (seed ^ 0x4C4F4F4B53UL) * 0x9E3779B97F4A7C15UL;
        z ^= z >> 29;
        int colour = (int)(z % (ulong)CivColors.Length);
        int motif = (int)((z >> 16) % 8);
        bool changed = colour != ColorShift || motif != MotifShift;
        ColorShift = colour;
        MotifShift = motif;
        return changed;
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _marks = Add(0, PaintMarks);
        _routes = Add(5, PaintRoutes);
        _lift = Add(7, PaintLift);
        _effects = Add(15, PaintEffects);
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
        _popups.RemoveAll(p => _time - p.Born > PopupSeconds);
        _lift.QueueRedraw();
        _effects.QueueRedraw();
    }

    /// <summary>Sets what to draw; <paramref name="zoom"/> picks how much detail.</summary>
    public void Show(PlayContentMessage content, PlayViewMessage? view, Vector2 period, Rect2 visible, float zoom = 1)
    {
        if (!ReferenceEquals(content, _content) && ChooseLooks(content.LookSeed))
        {
            ClearSprites();
            UiKit.ClearPictures();
        }

        _content = content;
        _period = period;
        Rect2 grown = visible.Grow(80);
        bool changed = view != _view;
        bool moved = grown != _visible || zoom != _zoom;
        if (changed && view is not null)
        {
            BattleNews(view);
        }

        _view = view;
        _visible = grown;
        _zoom = zoom;
        if (changed)
        {
            _rebucket = true;
            _routes?.QueueRedraw();
            _marks?.QueueRedraw();
        }

        if (changed || moved)
        {
            UpdateChunks();
        }
    }

    // Detail by zoom: 0 far (marks only), 1 middle (no small numbers), 2 near (everything).
    private int Detail => _zoom < 0.35f ? 0 : _zoom < 0.8f ? 1 : 2;

    private readonly Dictionary<(int, int), int> _freeSlots = [];

    /// <summary>Hexes to highlight while placing (null = not placing), with the free building places on each.</summary>
    public void SetPlaces(int[]? cells, int[]? free = null)
    {
        _places.Clear();
        _freeSlots.Clear();
        if (cells is not null)
        {
            for (int i = 0; i + 1 < cells.Length; i += 2)
            {
                _places.Add((cells[i], cells[i + 1]));
                if (free is not null && i / 2 < free.Length)
                {
                    _freeSlots[(cells[i], cells[i + 1])] = free[i / 2];
                }
            }
        }

        Restatic();
    }

    /// <summary>True when the hex is a valid place in the current placement.</summary>
    public bool IsPlace(int col, int row) => _places.Contains((col, row));

    /// <summary>Hex under the mouse (an outline only).</summary>
    public void SetHover((int Col, int Row)? hover) => _hover = hover;

    /// <summary>While choosing the start: why a hex cannot be our start (null = it can).</summary>
    public Func<(int Col, int Row), string?>? PickProblem { get; set; }

    /// <summary>Selected hex.</summary>
    public void SetSelected((int Col, int Row)? selected) => _selected = selected;

    /// <summary>A short ring on a hex: the order was given (like an RTS).</summary>
    public void Flash(int col, int row, Color color) => _flashes.Add((HexLayout.Center(col, row), color, _time));

    /// <summary>Draws the slow layers again (after a selection or a toggle).</summary>
    public void Restatic()
    {
        _rebucket = true;
        _marks?.QueueRedraw();
        _routes?.QueueRedraw();
        UpdateChunks();
    }

    // ---- Chunks ----------------------------------------------------------------------------------------------------

    // What one chunk holds, and a signature that changes when any of it does.
    private sealed class ChunkContent
    {
        public List<PlayBuildingView> Buildings { get; } = [];

        public List<PlaySettlementView> Settlements { get; } = [];

        public List<PlayUnitView> Units { get; } = [];

        public int Sig { get; set; }
    }

    // The three nodes of one chunk copy: ground, units and labels (each its own z).
    private sealed class ChunkNodes
    {
        public required Layer Ground { get; init; }

        public required Layer Units { get; init; }

        public required Layer Labels { get; init; }

        public int DrawnSig { get; set; } = int.MinValue;

        public bool Used { get; set; }
    }

    private int ChunksX => World is { } w ? (w.Width + ChunkSize - 1) / ChunkSize : 1;

    private int ChunkOf(int col, int row) => ((row / ChunkSize) * ChunksX) + (Mod(col, World?.Width ?? 1) / ChunkSize);

    private static int Mod(int a, int m) => ((a % m) + m) % m;

    // Sorts the objects into chunks (when the view changed) and gives each visible chunk copy its nodes; a node is drawn
    // again only when its chunk's signature changed.
    private void UpdateChunks()
    {
        if (_view is null || _content is null || World is not { } w || _period.X <= 0 || _marks is null)
        {
            return;
        }

        if (_rebucket)
        {
            _rebucket = false;
            foreach (ChunkContent cc in _content2.Values)
            {
                cc.Buildings.Clear();
                cc.Settlements.Clear();
                cc.Units.Clear();
            }

            foreach (PlayBuildingView b in _view.Buildings)
            {
                Bucket(ChunkOf(b.Col, b.Row)).Buildings.Add(b);
            }

            foreach (PlaySettlementView st in _view.Settlements)
            {
                Bucket(ChunkOf(st.Col, st.Row)).Settlements.Add(st);
            }

            foreach (PlayUnitView u in _view.Units)
            {
                if (u.State != 0)
                {
                    Bucket(ChunkOf(u.Col, u.Row)).Units.Add(u);
                }
            }

            foreach ((int chunk, ChunkContent cc) in _content2)
            {
                cc.Sig = ContentSig(chunk, cc);
            }
        }

        foreach (ChunkNodes n in _chunkNodes.Values)
        {
            n.Used = false;
        }

        int rows = (w.Height + ChunkSize - 1) / ChunkSize;
        float cw = ChunkSize * HexLayout.ColumnStep;
        float ch = ChunkSize * HexLayout.RowStep;
        for (int cy = 0; cy < rows; cy++)
        {
            float y0 = (cy * ch) - 40;
            if (y0 + ch + 80 < _visible.Position.Y || y0 > _visible.End.Y)
            {
                continue;
            }

            for (int cx = 0; cx < ChunksX; cx++)
            {
                float x0 = (cx * cw) - 40;
                int k0 = (int)MathF.Floor((_visible.Position.X - (x0 + cw + 80)) / _period.X) + 1;
                int k1 = (int)MathF.Floor((_visible.End.X - x0) / _period.X);
                int chunk = (cy * ChunksX) + cx;
                for (int k = k0; k <= k1; k++)
                {
                    ChunkNodes nodes = NodesFor(chunk, k);
                    nodes.Used = true;
                    int zoomStep = Detail == 0 ? (int)(_zoom * 40) : 0; // far names follow the zoom
                    int sig = HashCode.Combine(_content2.TryGetValue(chunk, out ChunkContent? cc) ? cc.Sig : 0, RoadSig(w, cx, cy), Detail, zoomStep);
                    if (sig != nodes.DrawnSig)
                    {
                        nodes.DrawnSig = sig;
                        nodes.Ground.QueueRedraw();
                        nodes.Units.QueueRedraw();
                        nodes.Labels.QueueRedraw();
                    }
                }
            }
        }

        foreach (ChunkNodes n in _chunkNodes.Values)
        {
            n.Ground.Visible = n.Units.Visible = n.Labels.Visible = n.Used;
        }
    }

    private ChunkContent Bucket(int chunk)
    {
        if (!_content2.TryGetValue(chunk, out ChunkContent? cc))
        {
            cc = new ChunkContent();
            _content2[chunk] = cc;
        }

        return cc;
    }

    private ChunkNodes NodesFor(int chunk, int copy)
    {
        if (_chunkNodes.TryGetValue((chunk, copy), out ChunkNodes? n))
        {
            return n;
        }

        var offset = new Vector2(copy * _period.X, 0);
        n = new ChunkNodes
        {
            Ground = Add(1, g => PaintChunkGround(g, chunk)),
            Units = Add(10, g => PaintChunkUnits(g, chunk)),
            Labels = Add(20, g => PaintChunkLabels(g, chunk)),
        };
        n.Ground.Position = n.Units.Position = n.Labels.Position = offset;
        _chunkNodes[(chunk, copy)] = n;
        return n;
    }

    // Everything drawn in a chunk, plus the selection (the selected hex is drawn lifted, selected units ringed).
    private int ContentSig(int chunk, ChunkContent cc)
    {
        var h = new HashCode();
        foreach (PlayBuildingView b in cc.Buildings)
        {
            h.Add(HashCode.Combine(b.Id, b.Type, b.State, b.Workers, b.Starved, b.Priority, b.ProgressPermille, b.Remembered));
            h.Add(HashCode.Combine(b.Missing, b.Porters, _selected == (b.Col, b.Row), b.Slots));
        }

        foreach (PlaySettlementView s in cc.Settlements)
        {
            h.Add(HashCode.Combine(s.Id, s.Civ, s.Level, s.Besieged, s.Remembered, s.Name, AlertOf(s.Id), _selected == (s.Col, s.Row)));
        }

        foreach (PlayUnitView u in cc.Units)
        {
            h.Add(HashCode.Combine(u.Id, u.Col, u.Row, u.People, u.Hidden, u.Fighting, u.Cargo, HashCode.Combine(u.Id == SelectedUnit || SelectedUnits.Contains(u.Id), u.Size, u.Injured, u.Stance, u.Hunger)));
        }

        h.Add(chunk);
        return h.ToHashCode();
    }

    // Roads and fog of a chunk, as a number that changes when they do.
    private static int RoadSig(CivSim.Game.Net.ClientWorld w, int cx, int cy)
    {
        var h = new HashCode();
        int r1 = Math.Min(w.Height, (cy + 1) * ChunkSize);
        int c1 = Math.Min(w.Width, (cx + 1) * ChunkSize);
        for (int r = cy * ChunkSize; r < r1; r++)
        {
            for (int c = cx * ChunkSize; c < c1; c++)
            {
                int i = w.Index(c, r);
                if (w.Trees[i] == 1)
                {
                    h.Add((i * 8) + 7 + (w.Visibility[i] * 1000000));
                }

                if (w.Deposit[i] != 0 || (w.Survey[i] != 255 && (w.Survey[i] & MapChunkMessage.DepositHidden) != 0))
                {
                    h.Add((i * 16) + w.Deposit[i] + (w.Survey[i] * 7919) + (w.Visibility[i] * 1000000)); // deposit badges
                }

                if (w.Road[i] != 0 || w.Works[i] != 0)
                {
                    h.Add((i * 4) + w.Road[i] + (w.Visibility[i] * 1000000));
                    h.Add((i * 65536) + w.Works[i]);
                }
            }
        }

        return h.ToHashCode();
    }

    // ---- Layers ----------------------------------------------------------------------------------------------------

    // Under the chunks: places to build, the starts taken while choosing, the land of the selected settlement.
    private void PaintMarks(CanvasItem g)
    {
        if (_content is null || _period.X <= 0)
        {
            return;
        }

        foreach ((int col, int row) in _places)
        {
            foreach (Vector2 c in Copies(col, row))
            {
                // E12 (2026-11-20): a light outline, not green on green grass.
                Vector2[] hex = Hex(c, 0.9f);
                g.DrawPolygon(hex, [new Color(1f, 1f, 0.85f, 0.08f)]);
                g.DrawPolyline([.. hex, hex[0]], new Color(1f, 0.95f, 0.55f, 0.8f), 1.5f);

                // 2026-11-23 (Patryk): the free building places on the hex, as slots.
                if (_freeSlots.TryGetValue((col, row), out int free))
                {
                    for (int k = 0; k < 4; k++)
                    {
                        if ((free & (1 << k)) != 0)
                        {
                            Rect2 slot = SlotRect(c, 1 << k, 1f).Grow(-1.5f);
                            g.DrawRect(slot, new Color(1f, 0.95f, 0.6f, 0.18f));
                            g.DrawRect(slot, new Color(1f, 0.93f, 0.5f, 0.95f), false, 1.2f);
                        }
                    }
                }
            }
        }

        if (_view is { Picking: true, TakenStarts: { } taken })
        {
            for (int i = 0; i + 1 < taken.Length; i += 2)
            {
                foreach (Vector2 c in Copies(taken[i], taken[i + 1]))
                {
                    // Only the circle: its centre is near the start, not on it, so it does not give the start away.
                    g.DrawCircle(c, _view.BlockRadius * HexLayout.ColumnStep, new Color(1, 0.2f, 0.2f, 0.12f));
                    g.DrawArc(c, _view.BlockRadius * HexLayout.ColumnStep, 0, MathF.Tau, 64, new Color(1, 0.2f, 0.2f, 0.8f), 4);
                }
            }
        }

        // Ore signs (2026-11-22, Prospecting): a circle in the ore's colour where an ore of our era lies.
        if (_view is { Signs: { } signs } && World is { } sw)
        {
            for (int i = 0; i + 1 < signs.Length; i += 2)
            {
                var ore = new Color(1f, 0.78f, 0.25f);
                foreach (Vector2 c in Copies(signs[i + 1] % sw.Width, signs[i + 1] / sw.Width))
                {
                    float r = (_view.SignRadius + 0.5f) * HexLayout.ColumnStep;
                    g.DrawCircle(c, r, new Color(ore, 0.10f));
                    g.DrawArc(c, r, 0, MathF.Tau, 48, new Color(ore, 0.85f), 3);
                }
            }
        }

        if (_view is not null && SelectedSettlement() is { } sel && sel.Civ == _view.Civ)
        {
            int radius = _content.Levels[sel.Level].Radius;
            Color tint = new(Light(CivColor(sel.Civ)), 0.12f);
            foreach (Vector2 c in Copies(sel.Col, sel.Row))
            {
                g.DrawCircle(c, (radius + 0.5f) * HexLayout.ColumnStep, tint);
            }
        }
    }

    // One chunk's roads, buildings and settlement centres (in the coordinates of the map's first copy), then its walls and
    // palisades over all of them (Patryk, 2026-11-23: walls above everything).
    private void PaintChunkGround(CanvasItem g, int chunk)
    {
        PaintChunkBelowWalls(g, chunk);
        if (_content is not null && World is { } w)
        {
            int cx = chunk % ChunksX;
            int cy = chunk / ChunksX;
            PaintWorks(g, w, cx * ChunkSize, cy * ChunkSize, Math.Min(w.Width, (cx + 1) * ChunkSize) - 1, Math.Min(w.Height, (cy + 1) * ChunkSize) - 1, walls: true);
        }
    }

    private void PaintChunkBelowWalls(CanvasItem g, int chunk)
    {
        if (_content is null || World is not { } w)
        {
            return;
        }

        int cx = chunk % ChunksX;
        int cy = chunk / ChunksX;
        PaintGroves(g, w, cx * ChunkSize, cy * ChunkSize, Math.Min(w.Width, (cx + 1) * ChunkSize) - 1, Math.Min(w.Height, (cy + 1) * ChunkSize) - 1);
        PaintWorks(g, w, cx * ChunkSize, cy * ChunkSize, Math.Min(w.Width, (cx + 1) * ChunkSize) - 1, Math.Min(w.Height, (cy + 1) * ChunkSize) - 1);
        PaintRoads(g, w, cx * ChunkSize, cy * ChunkSize, Math.Min(w.Width, (cx + 1) * ChunkSize) - 1, Math.Min(w.Height, (cy + 1) * ChunkSize) - 1);
        foreach ((Texture2D span, Vector2 mid) in _bridges)
        {
            g.DrawTextureRect(span, new Rect2(mid - new Vector2(10, 10), new Vector2(20, 20)), false); // 40x40 at 2x
        }

        _bridges.Clear();
        if (!_content2.TryGetValue(chunk, out ChunkContent? cc))
        {
            return;
        }

        int detail = Detail;
        if (detail == 2)
        {
            // Close: every building in its places on the hex (four places a hex, decision of 2026-11-18).
            foreach (PlayBuildingView b in cc.Buildings)
            {
                if (_selected != (b.Col, b.Row))
                {
                    DrawBuilding(g, HexLayout.Center(b.Col, b.Row), b, _content.Buildings[b.Type], 1f, detail);
                }
            }
        }
        else
        {
            // Farther: one mark per hex, for its largest building or the kind there is most of.
            foreach (List<PlayBuildingView> hex in ByHex(cc.Buildings))
            {
                PlayBuildingView b = Dominant(hex);
                if (_selected == (b.Col, b.Row))
                {
                    continue;
                }

                Vector2 c = HexLayout.Center(b.Col, b.Row);
                if (detail == 0)
                {
                    Color col = b.Remembered ? Grey(CivColor(b.Civ)) : CivColor(b.Civ).Darkened(0.3f);
                    g.DrawRect(new Rect2(c - new Vector2(7, 7), new Vector2(14, 14)), col);
                    continue;
                }

                DrawBuilding(g, c, b with { Slots = 15 }, _content.Buildings[b.Type], 1f, detail);
                int same = hex.Count(x => x.Type == b.Type);
                if (hex.Count > 1)
                {
                    g.DrawString(UiKit.GlyphFont, c + new Vector2(-24, 20), same > 1 ? $"x{same}" + (same < hex.Count ? "+" : "") : "+", HorizontalAlignment.Center, 48, 12, new Color(1f, 0.95f, 0.7f));
                }
            }
        }

        foreach (PlaySettlementView st in cc.Settlements)
        {
            if (_selected != (st.Col, st.Row))
            {
                DrawSettlement(g, HexLayout.Center(st.Col, st.Row), st, 1f);
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
            if (!picked && !fromSel && RouteDisplay == 0)
            {
                continue;
            }

            Color color = Light(CivColor(u.Civ));
            if (u.Patrol is { Length: >= 4 } loop && (picked || RouteDisplay >= 2))
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

            bool shown = RouteDisplay >= 2 || (sel is not null && (sel.Id == r.From || sel.Id == r.To));
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
                Vector2 p = c + (Art is null ? new Vector2(10, -2) : new Vector2(0, 8)); // under the feet of the group
                g.DrawSetTransform(p, 0, new Vector2(1f, 0.45f));
                g.DrawCircle(Vector2.Zero, 15 + (2 * pulse), new Color(CivColor(u.Civ), 0.35f + (0.25f * pulse)));
                g.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
        }

        // Guard hexes of the selected units, and those being painted.
        var guard = new Color(1f, 0.6f, 0.2f, 0.55f + (0.25f * pulse));
        foreach (PlayUnitView u in _view.Units)
        {
            if ((u.Id == SelectedUnit || SelectedUnits.Contains(u.Id)) && u.Guard is { } cells)
            {
                for (int k = 0; k + 1 < cells.Length; k += 2)
                {
                    foreach (Vector2 c in Copies(cells[k], cells[k + 1]))
                    {
                        g.DrawPolyline(Closed(Hex(c, 0.8f)), guard, 2f);
                    }
                }
            }
        }

        foreach ((int col, int row) in GuardPaint ?? [])
        {
            foreach (Vector2 c in Copies(col, row))
            {
                g.DrawPolyline(Closed(Hex(c, 0.8f)), new Color(1f, 0.75f, 0.3f, 0.9f), 3f);
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

    private void PaintChunkUnits(CanvasItem g, int chunk)
    {
        if (_content is null || !_content2.TryGetValue(chunk, out ChunkContent? cc))
        {
            return;
        }

        Font font = UiKit.GlyphFont;
        int detail = Detail;
        foreach (PlayUnitView u in cc.Units)
        {
            Color civ = CivColor(u.Civ);
            bool picked = u.Id == SelectedUnit || SelectedUnits.Contains(u.Id);
            Vector2 p = HexLayout.Center(u.Col, u.Row) + new Vector2(10, -8);
            if (detail == 0)
            {
                g.DrawCircle(p, 8, picked ? new Color(1f, 0.9f, 0.3f) : civ);
                continue;
            }

            if (Sprite("units", "unit_" + _content.Units[u.Kind].Key, u.Civ) is { } pic)
            {
                // G3: a group of figures standing on its own over the hex: a soft shadow under it, a ring when selected.
                // Patryk (2026-11-23): the group stands on the middle of its hex, its feet and shadow just below the centre.
                Vector2 feet = HexLayout.Center(u.Col, u.Row) + new Vector2(0, 8);
                Ellipse(g, feet, 12, 4, new Color(0, 0, 0, 0.4f));
                if (picked || u.Fighting)
                {
                    EllipseLine(g, feet, 14, 5, picked ? new Color(1f, 0.9f, 0.3f) : Colors.Red, 2);
                }

                DrawFit(g, pic, new Rect2(feet - new Vector2(16, 30), new Vector2(32, 31)), u.Hidden ? new Color(1, 1, 1, 0.55f) : Colors.White);
                if (_content.Units[u.Kind].Army && u.Size > 0)
                {
                    HealthBar(g, feet + new Vector2(-12, -33), u);
                }

                UnitMarks(g, feet + new Vector2(13, -32), u);
                if (detail == 2)
                {
                    g.DrawString(font, feet + new Vector2(-11, 13), u.People.ToString(System.Globalization.CultureInfo.InvariantCulture), HorizontalAlignment.Center, 22, 11, Colors.White);
                }

                continue;
            }

            g.DrawCircle(p, 11, u.Hidden ? civ.Darkened(0.6f) : civ.Darkened(0.3f));
            g.DrawArc(p, 11, 0, MathF.Tau, 20, picked ? new Color(1f, 0.9f, 0.3f) : u.Fighting ? Colors.Red : Colors.Black, picked || u.Fighting ? 3 : 1.5f);
            if (u.Cargo is { Length: > 0 } && u.Cargo != "nothing")
            {
                g.DrawRect(new Rect2(p + new Vector2(-14, -16), new Vector2(8, 6)), new Color(0.85f, 0.7f, 0.35f));
            }

            g.DrawString(font, p + new Vector2(-11, 5), _content.Units[u.Kind].Name[..1], HorizontalAlignment.Center, 22, 12, Colors.White);
            if (_content.Units[u.Kind].Army && u.Size > 0)
            {
                HealthBar(g, p + new Vector2(-12, -18), u); // above the token: the number is below
            }
            if (detail == 2)
            {
                g.DrawString(font, p + new Vector2(-11, 22), u.People.ToString(System.Globalization.CultureInfo.InvariantCulture), HorizontalAlignment.Center, 22, 12, Colors.White);
            }
        }
    }

    // Marks beside a unit, one under another (2026-11-23): its stance when not the usual hold, wounded, hungry or starving.
    private void UnitMarks(CanvasItem g, Vector2 at, PlayUnitView u)
    {
        Span<int> marks = stackalloc int[3];
        int n = 0;
        if (_content!.Units[u.Kind].Army && u.Stance is 1 or 2)
        {
            marks[n++] = u.Stance; // 1 aggressive, 2 passive
        }

        if (u.Injured > 0)
        {
            marks[n++] = 3;
        }

        if (u.Hunger > 0)
        {
            marks[n++] = u.Hunger >= 2 ? 5 : 4;
        }

        for (int k = 0; k < n; k++)
        {
            string name = marks[k] switch { 1 => "mark_stance_aggressive", 2 => "mark_stance_passive", 3 => "mark_wounded", 4 => "mark_hungry", _ => "mark_starving" };
            if (Sprite("ui", name, 0) is { } pic)
            {
                g.DrawTextureRect(pic, new Rect2(at + new Vector2(0, k * 10.5f), new Vector2(10, 10)), false);
            }
        }
    }

    private static void Ellipse(CanvasItem g, Vector2 c, float rx, float ry, Color color)
    {
        var pts = new Vector2[16];
        for (int k = 0; k < pts.Length; k++)
        {
            float a = k * MathF.Tau / pts.Length;
            pts[k] = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
        }

        g.DrawColoredPolygon(pts, color);
    }

    private static void EllipseLine(CanvasItem g, Vector2 c, float rx, float ry, Color color, float width)
    {
        var pts = new Vector2[17];
        for (int k = 0; k < pts.Length; k++)
        {
            float a = k * MathF.Tau / (pts.Length - 1);
            pts[k] = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
        }

        g.DrawPolyline(pts, color, width);
    }

    private void PaintEffects(CanvasItem g)
    {
        if (_period.X <= 0)
        {
            return;
        }

        PaintBattle(g);

        if (_hover is { } h && h != _selected)
        {
            Color color = _places.Count == 0 || _places.Contains(h) ? new Color(1, 1, 1, 0.55f) : new Color(1, 0.3f, 0.3f, 0.8f);
            if (_view is { Picking: true, MyPick: true } && PickProblem is { } problem)
            {
                // Choosing the start: green where we may start, red with the reason where not.
                string? why = problem(h);
                color = why is null ? new Color(0.35f, 1f, 0.4f, 0.95f) : new Color(1f, 0.3f, 0.3f, 0.95f);
                foreach (Vector2 c in Copies(h.Col, h.Row))
                {
                    g.DrawPolyline(Closed(Hex(c, 0.97f)), color, 3f);
                    g.DrawString(UiKit.GlyphFont, c + new Vector2(-150, 40), why ?? "Start here", HorizontalAlignment.Center, 300, 16, color);
                }
            }

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

        // Planned ways (2026-11-24, Ctrl held): from each unit through its waypoints, dashed, a ring on every point.
        foreach (List<(int Col, int Row)> plan in Plans)
        {
            if (plan.Count < 2)
            {
                continue;
            }

            var pts = new List<Vector2>();
            Vector2 prev = Vector2.Zero;
            foreach ((int col, int row) in plan)
            {
                Vector2 p = pts.Count == 0 ? HexLayout.Center(col, row) : Unwrapped(prev, col, row);
                pts.Add(p);
                prev = p;
            }

            foreach (Vector2 shift in Shifts(pts[0]))
            {
                var moved = pts.ConvertAll(p => p + shift);
                StrokePath(g, moved, new Color(0.6f, 0.9f, 1f, 0.9f), 2.5f, dashed: true, arrows: 1, EndMark.Dot);
                for (int k = 1; k < moved.Count; k++)
                {
                    g.DrawArc(moved[k], 9, 0, MathF.Tau, 20, new Color(0.6f, 0.9f, 1f), 2f);
                }
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

    /// <summary>Keys of the world's deposits by id − 1 (pictures "objects/deposit_key").</summary>
    public string[] DepositKeys { get; set; } = [];

    /// <summary>Names of the world's deposits by id − 1.</summary>
    public string[] DepositNames { get; set; } = [];

    // Patryk, 2026-11-24: a deposit we know shows on its own hex, in the top right corner, as a dark round badge with a light
    // ring and the deposit's picture, so it stands out on any land (before, one had to click hex after hex). A deposit of a
    // later era is a "?" badge; far away a small coloured dot remains. Its size: one to three light dots under the badge.
    private void PaintDepositBadges(CanvasItem g, int chunk)
    {
        if (World is not { } w)
        {
            return;
        }

        int cx = chunk % ChunksX;
        int cy = chunk / ChunksX;
        int r1 = Math.Min(w.Height, (cy + 1) * ChunkSize);
        int c1 = Math.Min(w.Width, (cx + 1) * ChunkSize);
        for (int row = cy * ChunkSize; row < r1; row++)
        {
            for (int col = cx * ChunkSize; col < c1; col++)
            {
                int i = w.Index(col, row);
                int d = w.Deposit[i];
                bool hidden = d == 0 && w.Survey[i] != 255 && (w.Survey[i] & MapChunkMessage.DepositHidden) != 0;
                if ((d == 0 && !hidden) || w.Visibility[i] == MapChunkMessage.Unknown)
                {
                    continue;
                }

                Vector2 at = HexLayout.Center(col, row) + new Vector2(12, -11);
                var ring = new Color(1f, 0.92f, 0.55f);
                if (Detail == 0)
                {
                    g.DrawCircle(at, 3.5f, new Color(0.06f, 0.05f, 0.04f));
                    g.DrawCircle(at, 2.3f, hidden ? new Color(0.8f, 0.8f, 0.8f) : ring);
                    continue;
                }

                g.DrawCircle(at, 7.5f, new Color(0.06f, 0.05f, 0.04f, 0.95f));
                g.DrawArc(at, 7f, 0, Mathf.Tau, 20, ring, 1.2f);
                Texture2D? icon = d > 0 && d <= DepositKeys.Length ? UiKit.Picture?.Invoke("objects", "deposit_" + DepositKeys[d - 1]) : null;
                if (icon is not null)
                {
                    g.DrawTextureRect(icon, new Rect2(at - new Vector2(5.5f, 5.5f), new Vector2(11, 11)), false);
                }
                else
                {
                    string letter = hidden ? "?" : d <= DepositNames.Length ? DepositNames[d - 1][..1] : "?";
                    g.DrawString(UiKit.GlyphFont, at + new Vector2(-5, 4), letter, HorizontalAlignment.Center, 10, 10, ring);
                }

                int rich = w.DepositRichness[i];
                int dots = hidden || rich <= 0 ? 0 : rich < 100 ? 1 : rich == 100 ? 2 : 3;
                for (int k = 0; k < dots; k++)
                {
                    Vector2 dot = at + new Vector2((k - ((dots - 1) / 2f)) * 3.5f, 9.5f);
                    g.DrawCircle(dot, 1.5f, new Color(0.06f, 0.05f, 0.04f));
                    g.DrawCircle(dot, 1f, ring);
                }
            }
        }
    }

    private void PaintChunkLabels(CanvasItem g, int chunk)
    {
        PaintDepositBadges(g, chunk);
        // Names of settlements show at every zoom (Patryk, 2026-11-18); far away only the names and alert marks remain.
        if (_content is null || !_content2.TryGetValue(chunk, out ChunkContent? cc))
        {
            return;
        }

        Font font = UiKit.Font;
        int size = Ui.PixelTheme.FontSize;
        // Far away the names keep their size on screen: drawn larger in the world.
        float k = Detail == 0 ? Math.Min(5f, 0.85f / Math.Max(0.05f, _zoom)) : 1f;
        foreach (PlaySettlementView s in cc.Settlements)
        {
            Vector2 centre = HexLayout.Center(s.Col, s.Row);
            g.DrawSetTransform(centre, 0, new Vector2(k, k));
            Vector2 c = Vector2.Zero;
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

            g.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
    }

    // Roads as part of the land, like rivers: from each road hex half a segment to each neighbouring road hex; tracks thin
    // and brown, paved roads wide and pale; remembered ones grey.
    private void PaintRoads(CanvasItem g, CivSim.Game.Net.ClientWorld w, int c0, int r0, int c1, int r1)
    {
        bool thin = Detail == 0;
        for (int row = r0; row <= r1; row++)
        {
            for (int col = c0; col <= c1; col++)
            {
                int i = w.Index(col, row);
                int level = w.Road[i];
                if (level == 0 || w.Visibility[i] == MapChunkMessage.Unknown)
                {
                    continue;
                }

                bool grey = w.Visibility[i] == MapChunkMessage.Remembered;
                Vector2 c = HexLayout.Center(col, row);
                bool odd = (col & 1) == 1;
                if (!thin && WayPictures(g, w, col, row, level == 1 ? "track" : "road", "", grey, j => w.Road[j] != 0))
                {
                    continue;
                }

                int linked = 0;
                foreach ((int dc, int dr) in odd ? OddNeighbours : EvenNeighbours)
                {
                    int nr = row + dr;
                    if (nr < 0 || nr >= w.Height || w.Road[w.Index(col + dc, nr)] == 0)
                    {
                        continue;
                    }

                    linked++;
                    Vector2 mid = c.Lerp(HexLayout.Center(col + dc, nr), 0.5f);
                    RoadSegment(g, c, mid, level, grey, thin);
                }

                if (linked == 0)
                {
                    g.DrawCircle(c, level == 1 ? 4 : 6, RoadColor(level, grey));
                }
            }
        }
    }

    // Groves on open land: a few small trees at the hex's corners, around the building places (forests have trees in their tiles).
    private static readonly Vector2[] GroveSpots = [new(-19, -2), new(18, 3), new(-6, -17), new(8, 16), new(-12, 13), new(13, -14)];

    private void PaintGroves(CanvasItem g, CivSim.Game.Net.ClientWorld w, int c0, int r0, int c1, int r1)
    {
        if (Detail == 0)
        {
            return;
        }

        for (int row = r0; row <= r1; row++)
        {
            for (int col = c0; col <= c1; col++)
            {
                int i = w.Index(col, row);
                if (w.Trees[i] != 1 || w.Visibility[i] == MapChunkMessage.Unknown)
                {
                    continue;
                }

                bool grey = w.Visibility[i] == MapChunkMessage.Remembered;
                if (Sprite("terrain", $"terrain_grove_{1 + (int)((uint)(i * 2654435761u) % 3u):D2}", 0, grey) is { } trees)
                {
                    g.DrawTextureRect(trees, HexRect(HexLayout.Center(col, row), 1f), false);
                    continue;
                }

                Color crown = grey ? Grey(new Color(0.22f, 0.42f, 0.18f)) : new Color(0.22f, 0.42f, 0.18f);
                Color dark = grey ? Grey(new Color(0.13f, 0.27f, 0.11f)) : new Color(0.13f, 0.27f, 0.11f);
                Color trunk = grey ? Grey(new Color(0.36f, 0.25f, 0.14f)) : new Color(0.36f, 0.25f, 0.14f);
                Vector2 c = HexLayout.Center(col, row);
                int count = 3 + (int)((uint)(i * 2654435761u) % 4u);
                for (int k = 0; k < count; k++)
                {
                    Vector2 p = c + GroveSpots[(k + i) % GroveSpots.Length];
                    g.DrawRect(new Rect2(p + new Vector2(-1, 1), new Vector2(2, 4)), trunk);
                    g.DrawColoredPolygon([p + new Vector2(0, -7), p + new Vector2(5, 2), p + new Vector2(-5, 2)], dark);
                    g.DrawColoredPolygon([p + new Vector2(0, -6), p + new Vector2(4, 1), p + new Vector2(-4, 1)], crown);
                }
            }
        }
    }

    // Works on hexes, under the roads: ditches and aqueducts run between neighbouring hexes of their kind along the cross between
    // the building places (blue when water reaches them); palisades and walls line the inside of the hex's edges; bridges are
    // planks over the river edges.
    // walls: only the palisades and walls (drawn last); otherwise everything but them.
    private void PaintWorks(CanvasItem g, CivSim.Game.Net.ClientWorld w, int c0, int r0, int c1, int r1, bool walls = false)
    {
        var enclosed = new Dictionary<(int, int), bool>();
        var towers = new Dictionary<(int, int), (Vector2 At, bool Grey)>();
        for (int row = r0; row <= r1; row++)
        {
            for (int col = c0; col <= c1; col++)
            {
                int i = w.Index(col, row);
                int works = w.Works[i];
                if (works == 0 || w.Visibility[i] == MapChunkMessage.Unknown)
                {
                    continue;
                }

                bool grey = w.Visibility[i] == MapChunkMessage.Remembered;
                Vector2 c = HexLayout.Center(col, row);
                bool odd = (col & 1) == 1;
                if (walls)
                {
                    if ((works & (2 | 4)) != 0)
                    {
                        WallEdges(g, w, col, row, c, (works & 4) != 0 ? 4 : 2, grey, enclosed, towers);
                    }

                    continue;
                }

                foreach ((int bit, Color wet, Color dry, float width) in new[] { (1, new Color(0.35f, 0.62f, 0.95f), new Color(0.45f, 0.4f, 0.32f), 3f), (8, new Color(0.55f, 0.75f, 0.95f), new Color(0.62f, 0.6f, 0.56f), 5f) })
                {
                    if ((works & bit) == 0)
                    {
                        continue;
                    }

                    bool flowing = (works & (bit == 1 ? 16 : 32)) != 0;
                    if (Detail > 0 && WayPictures(g, w, col, row, bit == 1 ? "ditch" : "aqueduct", flowing ? "_wet" : "_dry", grey, j => (w.Works[j] & bit) != 0))
                    {
                        continue;
                    }

                    Color col2 = grey ? Grey(flowing ? wet : dry) : flowing ? wet : dry;
                    int linked = 0;
                    foreach ((int dc, int dr) in odd ? OddNeighbours : EvenNeighbours)
                    {
                        int nr = row + dr;
                        if (nr < 0 || nr >= w.Height || (w.Works[w.Index(col + dc, nr)] & bit) == 0)
                        {
                            continue;
                        }

                        linked++;
                        g.DrawLine(c, c.Lerp(HexLayout.Center(col + dc, nr), 0.5f), col2, width, true);
                    }

                    if (linked == 0)
                    {
                        g.DrawLine(c + new Vector2(0, -16), c + new Vector2(0, 16), col2, width, true);
                    }
                }

                for (int e = 0; e < 3; e++)
                {
                    if ((works & (1 << (8 + e))) == 0)
                    {
                        continue;
                    }

                    (int a, int b) = HexLayout.EdgeCorners[e];
                    Vector2 mid = c + ((HexLayout.Corners[a] + HexLayout.Corners[b]) * 0.5f);
                    if (Detail > 0 && Sprite("works", "work_bridge_" + BridgeEdges[e], 0, grey) is { } span)
                    {
                        _bridges.Add((span, mid)); // drawn over the roads, after them
                        continue;
                    }

                    Vector2 along = (HexLayout.Corners[b] - HexLayout.Corners[a]).Normalized() * 6;
                    Vector2 across = (mid - c).Normalized() * 7;
                    Color plank = grey ? Grey(new Color(0.6f, 0.45f, 0.28f)) : new Color(0.6f, 0.45f, 0.28f);
                    g.DrawLine(mid - across - along, mid + across - along, plank, 2);
                    g.DrawLine(mid - across + along, mid + across + along, plank, 2);
                    g.DrawRect(new Rect2(mid - new Vector2(4, 4), new Vector2(8, 8)), plank);
                }
            }
        }

        DrawTowers(g, towers);
    }

    // Neighbour across edge e (0 N, 1 NE, 2 SE, 3 S, 4 SW, 5 NW) of a flat-top hex, as the map's borders count them.
    private static (int Col, int Row) Across(int col, int row, int e)
    {
        bool odd = (col & 1) == 1;
        return e switch
        {
            0 => (col, row - 1),
            1 => (col + 1, odd ? row : row - 1),
            2 => (col + 1, odd ? row + 1 : row),
            3 => (col, row + 1),
            4 => (col - 1, odd ? row + 1 : row),
            _ => (col - 1, odd ? row : row - 1),
        };
    }

    // Corners of the six edges N, NE, SE, S, SW, NW (indexes into HexLayout.Corners).
    private static readonly (int A, int B)[] EdgeCornerPairs = [(4, 5), (5, 0), (0, 1), (1, 2), (2, 3), (3, 4)];

    // True when a hex without this work lies inside a ring of it: a search over hexes without the work runs out before
    // it gets far (Patryk, 2026-11-23: a wall is drawn only on the outer edges of the area it closes, like a border).
    private static bool Enclosed(CivSim.Game.Net.ClientWorld w, int col, int row, int bit, Dictionary<(int, int), bool> cache)
    {
        if (row < 0 || row >= w.Height)
        {
            return false;
        }

        int start = w.Index(col, row);
        if (cache.TryGetValue((start, bit), out bool known))
        {
            return known;
        }

        var seen = new HashSet<int> { start };
        var open = new Queue<(int, int)>();
        open.Enqueue((col, row));
        bool inside = true;
        while (open.Count > 0)
        {
            (int cc, int rr) = open.Dequeue();
            if (seen.Count > 400)
            {
                inside = false; // it goes on: open land, not a closed courtyard
                break;
            }

            for (int e = 0; e < 6; e++)
            {
                (int nc, int nr) = Across(cc, rr, e);
                if (nr < 0 || nr >= w.Height)
                {
                    inside = false;
                    break;
                }

                int n = w.Index(nc, nr);
                if ((w.Works[n] & bit) == 0 && seen.Add(n))
                {
                    open.Enqueue((nc, nr));
                }
            }

            if (!inside)
            {
                break;
            }
        }

        foreach (int cell in seen)
        {
            cache[(cell, bit)] = inside;
        }

        return inside;
    }

    // A wall or palisade on the hex's edges that face out of the walled area: not between two walled hexes, not
    // towards the courtyard it closes. Pictures along the edge, round towers where segments of a stone wall meet.
    private void WallEdges(CanvasItem g, CivSim.Game.Net.ClientWorld w, int col, int row, Vector2 c, int bit, bool grey,
        Dictionary<(int, int), bool> enclosed, Dictionary<(int, int), (Vector2 At, bool Grey)> towers)
    {
        bool wall = bit == 4;
        Texture2D? tex = Sprite("works", wall ? "work_wall_edge" : "work_palisade_edge", 0, grey);
        for (int e = 0; e < 6; e++)
        {
            (int nc, int nr) = Across(col, row, e);
            bool outside = nr < 0 || nr >= w.Height || ((w.Works[w.Index(nc, nr)] & bit) == 0 && !Enclosed(w, nc, nr, bit, enclosed));
            if (!outside)
            {
                continue;
            }

            (int ia, int ib) = EdgeCornerPairs[e];
            Vector2 a = c + HexLayout.Corners[ia];
            Vector2 b = c + HexLayout.Corners[ib];
            Vector2 mid = (a + b) * 0.5f;
            Vector2 outward = (mid - c).Normalized();
            float angle = (b - a).Angle();
            // The picture's top (battlements, the logs' points) faces out of the walled area.
            if (new Vector2(MathF.Sin(angle), -MathF.Cos(angle)).Dot(outward) < 0)
            {
                angle += MathF.PI;
            }

            float length = (b - a).Length();
            if (tex is not null)
            {
                g.DrawSetTransform(mid - (outward * 3f), angle, Vector2.One);
                g.DrawTextureRect(tex, new Rect2(-length / 2, -4, length, 8), false);
                g.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            }
            else
            {
                Color edge = wall ? new Color(0.72f, 0.72f, 0.7f) : new Color(0.55f, 0.38f, 0.2f);
                g.DrawLine(a - (outward * 3f), b - (outward * 3f), grey ? Grey(edge) : edge, 4);
            }

            if (wall)
            {
                foreach (Vector2 p in new[] { a, b })
                {
                    Vector2 at = p - (outward * 3f);
                    towers[((int)MathF.Round(p.X), (int)MathF.Round(p.Y))] = (at, grey);
                }
            }
        }
    }

    private void DrawTowers(CanvasItem g, Dictionary<(int, int), (Vector2 At, bool Grey)> towers)
    {
        foreach (((int, int) _, (Vector2 at, bool grey)) in towers)
        {
            if (Sprite("works", "work_wall_tower", 0, grey) is { } tower)
            {
                g.DrawTextureRect(tower, new Rect2(at - new Vector2(5, 5), new Vector2(10, 10)), false);
            }
        }
    }

    // The arms of a way in the order of the neighbour tables, as the pictures name them (art/placeholder/game_art.py).
    private static readonly string[] ArmNames = ["n", "s", "ne", "se", "nw", "sw"];
    private static readonly string[] BridgeEdges = ["n", "ne", "se"];
    private readonly List<(Texture2D Span, Vector2 Mid)> _bridges = [];

    // A way on a hex from its pictures (2026-11-23, Patryk: roads drawn, not lines): an arm to every neighbour on the
    // same way, their banks first so junctions stay clean, a round end alone; false when the pictures are missing.
    private bool WayPictures(CanvasItem g, CivSim.Game.Net.ClientWorld w, int col, int row, string kind, string surface, bool grey, Func<int, bool> on)
    {
        Span<int> arms = stackalloc int[6];
        int count = 0;
        (int, int)[] near = (col & 1) == 1 ? OddNeighbours : EvenNeighbours;
        for (int k = 0; k < near.Length; k++)
        {
            int nr = row + near[k].Item2;
            if (nr >= 0 && nr < w.Height && on(w.Index(col + near[k].Item1, nr)))
            {
                arms[count++] = k;
            }
        }

        Rect2 rect = HexRect(HexLayout.Center(col, row), 1f);
        for (int pass = 0; pass < 2; pass++)
        {
            for (int k = 0; k < Math.Max(1, count); k++)
            {
                string arm = count == 0 ? "c" : ArmNames[arms[k]];
                string name = pass == 0 ? $"work_{kind}_{arm}_bank" : $"work_{kind}_{arm}{surface}";
                if (Sprite("works", name, 0, grey) is not { } pic)
                {
                    return false;
                }

                g.DrawTextureRect(pic, rect, false);
            }
        }

        return true;
    }

    private static readonly (int, int)[] EvenNeighbours = [(0, -1), (0, 1), (1, -1), (1, 0), (-1, -1), (-1, 0)];
    private static readonly (int, int)[] OddNeighbours = [(0, -1), (0, 1), (1, 0), (1, 1), (-1, 0), (-1, 1)];

    private static void RoadSegment(CanvasItem g, Vector2 a, Vector2 b, int level, bool grey, bool thin)
    {
        float width = (level == 1 ? 4f : 8f) * (thin ? 2f : 1f); // far away lines would vanish: thicker in the world
        g.DrawLine(a, b, new Color(0.15f, 0.11f, 0.08f, 0.8f), width + 3, true);
        g.DrawLine(a, b, RoadColor(level, grey), width, true);
        g.DrawCircle(a, (width + 3) / 2, new Color(0.15f, 0.11f, 0.08f, 0.8f));
        g.DrawCircle(a, width / 2, RoadColor(level, grey));
    }

    private static Color RoadColor(int level, bool grey)
    {
        Color c = level == 1 ? new Color(0.66f, 0.52f, 0.34f) : new Color(0.78f, 0.74f, 0.64f);
        return grey ? Grey(c) : c;
    }

    // ---- Pieces --------------------------------------------------------------------------------------------------

    private enum EndMark
    {
        None,
        Dot,
        Cross,
    }

    // Buildings grouped by hex, in their order.
    private static List<List<PlayBuildingView>> ByHex(List<PlayBuildingView> all)
    {
        var map = new Dictionary<(int, int), List<PlayBuildingView>>();
        var order = new List<List<PlayBuildingView>>();
        foreach (PlayBuildingView b in all)
        {
            if (!map.TryGetValue((b.Col, b.Row), out List<PlayBuildingView>? list))
            {
                list = [];
                map[(b.Col, b.Row)] = list;
                order.Add(list);
            }

            list.Add(b);
        }

        return order;
    }

    // The building that stands for its hex from afar: the largest, else the kind there is most of.
    private PlayBuildingView Dominant(List<PlayBuildingView> hex)
    {
        PlayBuildingView best = hex[0];
        int bestScore = -1;
        foreach (PlayBuildingView b in hex)
        {
            int size = System.Numerics.BitOperations.PopCount((uint)b.Slots);
            int score = (size * 10) + hex.Count(x => x.Type == b.Type);
            if (score > bestScore)
            {
                best = b;
                bestScore = score;
            }
        }

        return best;
    }

    // The places of a hex (top left, top right, bottom left, bottom right) as rectangles of the 48×42 tile: 15×13 each, 2 apart.
    private static Rect2 SlotRect(Vector2 c, int slots, float scale)
    {
        Rect2? r = null;
        for (int k = 0; k < 4; k++)
        {
            if ((slots & (1 << k)) == 0)
            {
                continue;
            }

            var one = new Rect2(c + (new Vector2(k % 2 == 0 ? -16 : 1, k < 2 ? -14 : 1) * scale), new Vector2(15, 13) * scale);
            r = r is { } x ? x.Merge(one) : one;
        }

        return r ?? new Rect2(c, Vector2.Zero);
    }

    private void DrawBuilding(CanvasItem g, Vector2 c, PlayBuildingView b, PlayBuildingInfo t, float scale, int detail = 2)
    {
        if (b.Slots != 15)
        {
            DrawInPlaces(g, c, b, t, scale);
            return;
        }

        Font font = UiKit.GlyphFont;
        Color civ = CivColor(b.Civ);
        if (Sprite("buildings", "building_" + t.Key, b.Civ, b.Remembered) is { } pic)
        {
            // Patryk (2026-11-23): pictures stay inside their hex, clear of the border: a whole-hex building keeps a margin,
            // a small one alone on its hex (farther zoom) more.
            Rect2 area = HexInner(c, scale, t.Size < 4 ? 0.62f : 0.86f);
            if (!b.Remembered && t.Size < 4)
            {
                GroundShadow(g, area, 0.1f);
            }

            DrawFit(g, pic, area, b.Remembered ? Colors.White : StateModulate(b));
            if (b.Remembered)
            {
                return;
            }

            if (b.State is 0 or 1)
            {
                Scaffold(g, area.Grow(-10 * scale));
            }

            if (b.Priority)
            {
                g.DrawPolyline(Closed(Hex(c, 0.92f * scale)), new Color(1f, 0.85f, 0.2f), 2);
            }

            if (b.Starved && b.State == 2)
            {
                NoInputMark(g, c + (new Vector2(14, -12) * scale));
            }

            if (detail < 2)
            {
                return;
            }

            if (b.State == 5)
            {
                bool lost = b.Missing is not null && b.Porters == 0;
                g.DrawString(font, c + new Vector2(-24, 18), lost ? "missing!" : "goods...", HorizontalAlignment.Center, 48, 12, lost ? new Color(1f, 0.5f, 0.4f) : new Color(0.8f, 0.85f, 1f));
            }
            else if (b.State is 0 or 1)
            {
                float p = b.ProgressPermille / 1000f;
                g.DrawLine(c + new Vector2(-14, 16), c + new Vector2(14, 16), new Color(0, 0, 0, 0.8f), 4);
                g.DrawLine(c + new Vector2(-14, 16), c + new Vector2(-14 + (28 * p), 16), b.State == 1 ? Colors.Orange : Colors.LightGreen, 4);
            }
            else if (t.Workers > 0 && b.Workers < t.Workers)
            {
                Color w = b.Workers == 0 ? Colors.Red : Colors.Yellow;
                g.DrawString(font, c + new Vector2(-24, 19), $"{b.Workers}/{t.Workers}", HorizontalAlignment.Center, 48, 11, w);
            }

            return;
        }

        if (b.Remembered)
        {
            // A building we saw before: grey, as we last saw it (like Age of Empires).
            g.DrawPolygon(Hex(c, 0.7f * scale), [new Color(0.46f, 0.46f, 0.46f, 0.8f)]);
            g.DrawPolyline(Closed(Hex(c, 0.7f * scale)), Grey(civ), 2);
            g.DrawString(font, c + new Vector2(-24, 5), UiKit.Short(t.Name), HorizontalAlignment.Center, 48, 12, new Color(0.88f, 0.88f, 0.88f));
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
        if (detail < 2)
        {
            return; // middle distance: no small numbers
        }

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

    // A building that takes one or two places of its hex: a small block with its short name and a mark of its state.
    private void DrawInPlaces(CanvasItem g, Vector2 c, PlayBuildingView b, PlayBuildingInfo t, float scale)
    {
        Rect2 r = SlotRect(c, b.Slots, scale);
        if (Sprite("buildings", "building_" + t.Key, b.Civ, b.Remembered) is { } pic)
        {
            if (!b.Remembered)
            {
                GroundShadow(g, r.Grow(-1.5f * scale), 0.12f);
            }

            DrawFit(g, pic, r.Grow(-1.5f * scale), b.Remembered ? Colors.White : StateModulate(b)); // inside its places, clear of the hex edges
            if (b.Remembered)
            {
                return;
            }

            if (b.State is 0 or 1)
            {
                Scaffold(g, r);
            }

            if (b.Priority)
            {
                g.DrawRect(r, new Color(1f, 0.85f, 0.2f), false, 1.5f);
            }

            if (b.Starved && b.State == 2)
            {
                NoInputMark(g, r.Position + new Vector2(r.Size.X - 2, 2));
            }

            return;
        }

        Color civ = b.Remembered ? Grey(CivColor(b.Civ)) : CivColor(b.Civ);
        bool built = b.State is 2 or 3;
        Color fill = b.Remembered ? new Color(0.46f, 0.46f, 0.46f, 0.85f) : b.State switch
        {
            5 => new Color(0.2f, 0.2f, 0.3f, 0.75f),
            0 or 1 => new Color(0.35f, 0.3f, 0.25f, 0.9f),
            3 => new Color(0.25f, 0.25f, 0.25f, 0.9f),
            _ => b.Starved ? new Color(0.55f, 0.15f, 0.12f, 0.95f) : new Color(0.18f, 0.16f, 0.14f, 0.92f),
        };
        g.DrawRect(r, fill);
        g.DrawRect(r, b.Priority && !b.Remembered ? new Color(1f, 0.85f, 0.2f) : civ, false, b.Priority ? 2 : 1);
        g.DrawString(UiKit.GlyphFont, new Vector2(r.Position.X, r.Position.Y + (r.Size.Y * 0.5f) + 4), UiKit.Short(t.Name), HorizontalAlignment.Center, r.Size.X, 12,
            b.Remembered ? new Color(0.88f, 0.88f, 0.88f) : built ? Colors.White : new Color(0.8f, 0.8f, 0.8f));
        if (b.Remembered)
        {
            return;
        }

        Vector2 bl = r.Position + new Vector2(1, r.Size.Y - 2);
        if (!built)
        {
            float p = b.State == 5 ? 0 : b.ProgressPermille / 1000f;
            g.DrawLine(bl, bl + new Vector2(r.Size.X - 2, 0), new Color(0, 0, 0, 0.8f), 2);
            g.DrawLine(bl, bl + new Vector2((r.Size.X - 2) * p, 0), b.State == 5 ? (b.Porters == 0 && b.Missing is not null ? Colors.Red : new Color(0.6f, 0.7f, 1f)) : b.State == 1 ? Colors.Orange : Colors.LightGreen, 2);
        }
        else if (t.Workers > 0)
        {
            Color w = b.Workers >= t.Workers ? Colors.LightGreen : b.Workers == 0 ? Colors.Red : Colors.Yellow;
            g.DrawCircle(r.Position + new Vector2(r.Size.X - 3, 3), 2, w);
        }
    }

    private void DrawSettlement(CanvasItem g, Vector2 c, PlaySettlementView s, float scale)
    {
        Color civ = s.Remembered ? Grey(CivColor(s.Civ)) : CivColor(s.Civ);
        if (Sprite("buildings", "settlement_" + _content!.Levels[s.Level].Key, s.Civ, s.Remembered) is { } pic)
        {
            GroundShadow(g, HexInner(c, scale, 0.8f), 0.1f);
            DrawFit(g, pic, HexInner(c, scale, 0.88f), Colors.White);
            if (s.Besieged)
            {
                g.DrawPolyline(Closed(Hex(c, 0.9f * scale)), Colors.Red, 3);
            }

            return;
        }

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
        float l = 0.25f + (0.75f * ((c.R * 0.3f) + (c.G * 0.59f) + (c.B * 0.11f)));
        return new Color(l, l, l, 0.9f);
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
