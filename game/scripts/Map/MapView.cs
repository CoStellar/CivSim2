using System;
using System.Collections.Generic;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// Draws one world as coloured hexes. Every 32×32 chunk owns two MultiMeshes (hexes and river segments) that are
/// rebuilt only when the server sends that chunk again or the overlay changes. Each frame only the chunk copies
/// that touch the screen get a node, so the map wraps in both directions and a huge map costs the same per frame as
/// a small one.
/// </summary>
public partial class MapView : Node2D
{
    private const float RiverBaseWidth = 4f;

    // Recolouring is spread over frames so switching the overlay on a huge map never stalls one frame.
    private const int ChunkUpdatesPerFrame = 32;

    private readonly List<ChunkGfx> _chunks = [];
    private readonly List<MultiMeshInstance2D> _hexPool = [];
    private readonly List<MultiMeshInstance2D> _riverPool = [];
    private ClientSession? _session;
    private ClientWorld? _world;
    private ArrayMesh? _hexMesh;
    private ArrayMesh? _hexMeshGrid;
    private bool _grid;
    private ArrayMesh? _quadMesh;
    private MapOverlay _overlay = MapOverlay.Biomes;
    private Vector2 _period;
    private (int Col, int Row)? _selected;
    private SelectionOutline? _outline;
    private SettlementMarkers? _markers;

    /// <summary>Chunk copies drawn in the last frame (for the performance read-out).</summary>
    public int VisibleChunks { get; private set; }

    /// <summary>Size of one full map copy in pixels; the map repeats every <c>Period.X</c> (east–west only).</summary>
    public Vector2 Period => _period;

    /// <summary>Currently selected cell (wrapped), if any.</summary>
    public (int Col, int Row)? Selected => _selected;

    /// <summary>The world being drawn.</summary>
    public ClientWorld? World => _world;

    /// <summary>Current overlay.</summary>
    public MapOverlay Overlay
    {
        get => _overlay;
        set
        {
            if (_overlay != value)
            {
                _overlay = value;
                foreach (ChunkGfx c in _chunks)
                {
                    c.ColorsDirty = true;
                }
            }
        }
    }

    /// <summary>Starts drawing <paramref name="world"/> from <paramref name="session"/>.</summary>
    public void Bind(ClientSession session, ClientWorld world)
    {
        _session = session;
        _world = world;
        _period = HexLayout.MapSize(world.Width, world.Height);
        _hexMesh ??= BuildHexMesh(1f);
        _hexMeshGrid ??= BuildHexMesh(0.93f);
        _quadMesh ??= BuildQuadMesh();
        _chunks.Clear();
        for (int cy = 0; cy < world.ChunksY; cy++)
        {
            for (int cx = 0; cx < world.ChunksX; cx++)
            {
                int x0 = cx * ClientWorld.ChunkSize;
                int y0 = cy * ClientWorld.ChunkSize;
                int w = Math.Min(ClientWorld.ChunkSize, world.Width - x0);
                int h = Math.Min(ClientWorld.ChunkSize, world.Height - y0);
                _chunks.Add(new ChunkGfx(x0, y0, w, h, _hexMesh, _quadMesh));
            }
        }

        _markers ??= new SettlementMarkers { ZIndex = 5 };
        if (_markers.GetParent() is null)
        {
            AddChild(_markers);
        }

        _outline ??= new SelectionOutline { ZIndex = 10 };
        if (_outline.GetParent() is null)
        {
            AddChild(_outline);
        }
    }

    /// <summary>
    /// Shows thin gaps between hexes (a grid). Only useful when zoomed in; far away the gaps would flicker.
    /// </summary>
    public void SetGrid(bool on)
    {
        if (on == _grid || _hexMesh is null || _hexMeshGrid is null)
        {
            return;
        }

        _grid = on;
        foreach (ChunkGfx c in _chunks)
        {
            c.Hexes.Mesh = on ? _hexMeshGrid : _hexMesh;
        }
    }

    /// <summary>Selects the cell under a world position. Returns the wrapped cell.</summary>
    public (int Col, int Row)? SelectAt(Vector2 worldPos)
    {
        if (_world is null)
        {
            return null;
        }

        (int col, int row) = HexLayout.CellAt(worldPos);
        int c = Mod(col, _world.Width);
        if (row < 0 || row >= _world.Height)
        {
            return null; // above the north pole or below the south pole of the drawing
        }

        int r = row;
        _selected = (c, r);
        return _selected;
    }

    /// <summary>Updates dirty chunks and assigns nodes to the chunk copies visible in <paramref name="view"/>.</summary>
    public void Refresh(Rect2 view)
    {
        if (_world is null || _session is null)
        {
            return;
        }

        for (int k = 0; k < _chunks.Count; k++)
        {
            ChunkGfx c = _chunks[k];
            if (_world.DirtyChunks[k])
            {
                _world.DirtyChunks[k] = false;
                c.ColorsDirty = true;
                c.RiversDirty = true;
            }

            // Colours are rebuilt lazily, only for chunks that are about to be drawn.
        }

        int used = 0;
        int usedRivers = 0;
        int budget = ChunkUpdatesPerFrame;
        for (int k = 0; k < _chunks.Count; k++)
        {
            ChunkGfx c = _chunks[k];
            Rect2 bounds = c.Bounds;
            int kx0 = (int)MathF.Floor((view.Position.X - bounds.End.X) / _period.X) + 1;
            int kx1 = (int)MathF.Floor((view.End.X - bounds.Position.X) / _period.X);
            // The globe wraps only east–west (ADR 0012): copies side by side, never above or below.
            const int ky0 = 0;
            const int ky1 = 0;
            if (kx1 < kx0 || bounds.End.Y < view.Position.Y || bounds.Position.Y > view.End.Y)
            {
                continue;
            }

            // A chunk drawn for the first time must be coloured now; later updates wait for the budget.
            if (c.ColorsDirty && (budget > 0 || !c.EverColored))
            {
                c.UpdateColors(_world, _session.Content, _overlay);
                budget--;
            }

            if (c.RiversDirty && (budget > 0 || !c.EverColored))
            {
                c.UpdateRivers(_world, RiverBaseWidth);
                budget--;
            }

            for (int ky = ky0; ky <= ky1; ky++)
            {
                for (int kx = kx0; kx <= kx1; kx++)
                {
                    var offset = new Vector2(kx * _period.X, ky * _period.Y);
                    MultiMeshInstance2D node = Take(_hexPool, used++, 0);
                    node.Multimesh = c.Hexes;
                    node.Position = c.Origin + offset;
                    if (c.Rivers.InstanceCount > 0)
                    {
                        MultiMeshInstance2D rn = Take(_riverPool, usedRivers++, 1);
                        rn.Multimesh = c.Rivers;
                        rn.Position = c.Origin + offset;
                    }
                }
            }
        }

        Release(_hexPool, used);
        Release(_riverPool, usedRivers);
        VisibleChunks = used;
        _markers?.Show(_session.View?.Settlements, _world.Id, _period, view);
        UpdateOutline(view);
    }

    private void UpdateOutline(Rect2 view)
    {
        if (_outline is null)
        {
            return;
        }

        if (_selected is not { } sel)
        {
            _outline.Visible = false;
            return;
        }

        // Put the outline on the copy of the cell nearest to the centre of the screen.
        Vector2 center = HexLayout.Center(sel.Col, sel.Row);
        Vector2 screen = view.GetCenter();
        center.X += MathF.Round((screen.X - center.X) / _period.X) * _period.X;
        _outline.Position = center;
        _outline.Visible = true;
    }

    private MultiMeshInstance2D Take(List<MultiMeshInstance2D> pool, int index, int z)
    {
        if (index < pool.Count)
        {
            MultiMeshInstance2D existing = pool[index];
            existing.Visible = true;
            return existing;
        }

        var node = new MultiMeshInstance2D { ZIndex = z };
        AddChild(node);
        pool.Add(node);
        return node;
    }

    private static void Release(List<MultiMeshInstance2D> pool, int used)
    {
        for (int i = used; i < pool.Count; i++)
        {
            pool[i].Visible = false;
        }
    }

    private static ArrayMesh BuildHexMesh(float scale)
    {
        var verts = new Vector2[7];
        verts[0] = Vector2.Zero;
        for (int i = 0; i < 6; i++)
        {
            verts[i + 1] = HexLayout.Corners[i] * scale;
        }

        var idx = new int[18];
        for (int i = 0; i < 6; i++)
        {
            idx[i * 3] = 0;
            idx[(i * 3) + 1] = i + 1;
            idx[(i * 3) + 2] = ((i + 1) % 6) + 1;
        }

        return BuildMesh(verts, idx);
    }

    // Unit segment along +X, centred on the Y axis; instances scale and rotate it onto a hex edge.
    private static ArrayMesh BuildQuadMesh() =>
        BuildMesh([new(0, -0.5f), new(1, -0.5f), new(1, 0.5f), new(0, 0.5f)], [0, 1, 2, 0, 2, 3]);

    private static ArrayMesh BuildMesh(Vector2[] verts, int[] idx)
    {
        var colors = new Color[verts.Length];
        Array.Fill(colors, Colors.White);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static int Mod(int a, int m)
    {
        int r = a % m;
        return r < 0 ? r + m : r;
    }

    private sealed class ChunkGfx
    {
        private const int Stride = 12;
        private static readonly Color RiverColor = OverlayColors.Rgb(0x4FC3F7);
        private readonly float[] _buffer;

        public ChunkGfx(int x0, int y0, int w, int h, Mesh hex, Mesh quad)
        {
            X0 = x0;
            Y0 = y0;
            W = w;
            H = h;
            Origin = new Vector2(x0 * HexLayout.ColumnStep, y0 * HexLayout.RowStep);

            // Bounds include the half-hex overhang of odd columns and the right edge.
            Bounds = new Rect2(Origin, new Vector2((w * HexLayout.ColumnStep) + 12, (h * HexLayout.RowStep) + 21));
            Hexes = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true,
                Mesh = hex,
                InstanceCount = w * h,
            };

            // The whole chunk goes to the GPU as one float buffer: 8 floats of 2D transform + 4 of colour per hex.
            _buffer = new float[w * h * Stride];
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    Vector2 p = HexLayout.Center(x0 + c, y0 + r) - Origin;
                    int o = ((r * w) + c) * Stride;
                    _buffer[o] = 1;      // basis.x.x
                    _buffer[o + 3] = p.X; // origin.x
                    _buffer[o + 5] = 1;  // basis.y.y
                    _buffer[o + 7] = p.Y; // origin.y
                }
            }

            Rivers = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
                UseColors = true,
                Mesh = quad,
                InstanceCount = 0,
            };
        }

        public int X0 { get; }

        public int Y0 { get; }

        public int W { get; }

        public int H { get; }

        public Vector2 Origin { get; }

        public Rect2 Bounds { get; }

        public MultiMesh Hexes { get; }

        public MultiMesh Rivers { get; }

        public bool ColorsDirty { get; set; } = true;

        public bool RiversDirty { get; set; } = true;

        public bool EverColored { get; private set; }

        public void UpdateColors(ClientWorld world, ContentMessage? content, MapOverlay overlay)
        {
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    int i = world.Index(X0 + c, Y0 + r);
                    Color col = OverlayColors.Of(overlay, world, content, i);
                    int o = (((r * W) + c) * Stride) + 8;
                    _buffer[o] = col.R;
                    _buffer[o + 1] = col.G;
                    _buffer[o + 2] = col.B;
                    _buffer[o + 3] = col.A;
                }
            }

            Hexes.Buffer = _buffer;
            ColorsDirty = false;
            EverColored = true;
        }

        public void UpdateRivers(ClientWorld world, float baseWidth)
        {
            var segments = new List<Transform2D>();
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    int i = world.Index(X0 + c, Y0 + r);
                    Vector2 center = HexLayout.Center(X0 + c, Y0 + r) - Origin;
                    for (int e = 0; e < MapChunkMessage.RiverEdgesPerCell; e++)
                    {
                        int size = world.River[(i * MapChunkMessage.RiverEdgesPerCell) + e];
                        if (size == 0)
                        {
                            continue;
                        }

                        Vector2 a = center + HexLayout.Corners[HexLayout.EdgeCorners[e].A];
                        Vector2 b = center + HexLayout.Corners[HexLayout.EdgeCorners[e].B];
                        Vector2 x = b - a;
                        Vector2 y = new Vector2(-x.Y, x.X).Normalized() * (baseWidth * size);
                        segments.Add(new Transform2D(x, y, a));
                    }
                }
            }

            Rivers.InstanceCount = segments.Count;
            for (int k = 0; k < segments.Count; k++)
            {
                Rivers.SetInstanceTransform2D(k, segments[k]);
                Rivers.SetInstanceColor(k, RiverColor);
            }

            RiversDirty = false;
        }
    }
}
