using System;
using System.Collections.Generic;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// Draws one world as layered hexes (stage 5). Every 32×32 chunk owns three MultiMeshes: hexes (biome tile, relief,
/// feature and river tiles in a shader, plus the blended tint of the cell layers), river lines for far zoom, and
/// border lines. They are rebuilt only when the server sends that chunk again or a layer changes. Each frame only
/// the chunk copies that touch the screen get a node, so the map wraps east–west and a huge map costs the same per
/// frame as a small one. Settlements, units, events and relations are separate sprite layers on top.
/// </summary>
public partial class MapView : Node2D
{
    /// <summary>From this zoom on, tiles are drawn as pictures; below it as average colours.</summary>
    public const float TexturedZoom = 0.4f;

    private const float RiverBaseWidth = 4f;
    private const float BorderWidth = 4f;

    // Recolouring is spread over frames so switching a layer on a huge map never stalls one frame: chunks about to
    // be drawn for the first time are always done, the rest only while this much time is left in the frame.
    private const double RebuildMillisecondsPerFrame = 3.0;

    private readonly List<ChunkGfx> _chunks = [];
    private readonly List<MultiMeshInstance2D> _hexPool = [];
    private readonly List<MultiMeshInstance2D> _riverPool = [];
    private readonly List<MultiMeshInstance2D> _borderPool = [];
    private ClientSession? _session;
    private ClientWorld? _world;
    private ArrayMesh? _hexMesh;
    private ArrayMesh? _hexMeshGrid;
    private bool _grid;
    private bool _textured = true;
    private ArrayMesh? _quadMesh;
    private ShaderMaterial? _hexMaterial;
    private Vector2 _period;
    private (int Col, int Row)? _selected;
    private SelectionOutline? _outline;
    private MapObjects? _objects;
    private int _layersVersion = -1;
    private int _fogVersion = -1;
    private bool _hasArt;
    private bool _fogHides;

    /// <summary>Creates the view with the given layer stack and pictures.</summary>
    public MapView(LayerStack layers, ArtLibrary art)
    {
        Layers = layers;
        Art = art;
    }

    /// <summary>Required by Godot for scene instancing; not used.</summary>
    public MapView()
        : this(new LayerStack(), new ArtLibrary(ArtLibrary.FindArtDirectory()))
    {
    }

    /// <summary>The layers and their settings.</summary>
    public LayerStack Layers { get; }

    /// <summary>Map pictures.</summary>
    public ArtLibrary Art { get; }

    /// <summary>Chunk copies drawn in the last frame (for the performance read-out).</summary>
    public int VisibleChunks { get; private set; }

    /// <summary>Size of one full map copy in pixels; the map repeats every <c>Period.X</c> (east–west only).</summary>
    public Vector2 Period => _period;

    /// <summary>Currently selected cell (wrapped), if any.</summary>
    public (int Col, int Row)? Selected => _selected;

    /// <summary>The world being drawn.</summary>
    public ClientWorld? World => _world;

    /// <summary>Starts drawing <paramref name="world"/> from <paramref name="session"/>.</summary>
    public void Bind(ClientSession session, ClientWorld world)
    {
        _session = session;
        _world = world;
        _period = HexLayout.MapSize(world.Width, world.Height);
        _hexMesh ??= BuildHexMesh(1f);
        _hexMeshGrid ??= BuildHexMesh(0.93f);
        _quadMesh ??= BuildQuadMesh();
        _hexMaterial ??= BuildHexMaterial(session.Content);
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

        _objects ??= new MapObjects(Art);
        if (_objects.GetParent() is null)
        {
            AddChild(_objects);
        }

        _outline ??= new SelectionOutline { ZIndex = 100 };
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

        _selected = (c, row);
        return _selected;
    }

    /// <summary>
    /// Updates dirty chunks and assigns nodes to the chunk copies visible in <paramref name="view"/>.
    /// <paramref name="zoom"/> decides between pictures and flat colours.
    /// </summary>
    public void Refresh(Rect2 view, float zoom)
    {
        if (_world is null || _session is null || _hexMaterial is null)
        {
            return;
        }

        // A layer switch or a new fog recolours every chunk; the map data marks only the chunks that changed.
        bool layersChanged = Layers.Version != _layersVersion;
        bool fogChanged = _session.FogVersion != _fogVersion;
        if (layersChanged || fogChanged)
        {
            _layersVersion = Layers.Version;
            _fogVersion = _session.FogVersion;
            // Lines (rivers, borders) depend on the fog only when it hides cells completely.
            float fogAlpha = Layers.Alpha(MapLayer.Fog);
            bool hides = fogAlpha >= 0.99f && _session.Fog.Count > 0;
            bool linesToo = fogChanged || hides != _fogHides;
            _fogHides = hides;
            foreach (ChunkGfx c in _chunks)
            {
                c.ColorsDirty = true;
                c.LinesDirty |= linesToo;
            }

            _hexMaterial.SetShaderParameter("biome_opacity", Layers.Alpha(MapLayer.Biomes));
            _hexMaterial.SetShaderParameter("river_opacity", Layers.Alpha(MapLayer.Rivers));
        }

        bool textured = zoom >= TexturedZoom;
        if (textured != _textured)
        {
            _textured = textured;
            _hexMaterial.SetShaderParameter("textured", textured && _hasArt);
        }

        for (int k = 0; k < _chunks.Count; k++)
        {
            if (_world.DirtyChunks[k])
            {
                _world.DirtyChunks[k] = false;
                _chunks[k].ColorsDirty = true;
                _chunks[k].LinesDirty = true;
            }
        }

        byte[]? fog = _session.Fog.TryGetValue(_world.Id, out FogMessage? f) && f.Cells.Length == _world.Width * _world.Height ? f.Cells : null;
        int used = 0;
        int usedRivers = 0;
        int usedBorders = 0;
        long frameStart = System.Diagnostics.Stopwatch.GetTimestamp();
        (MapLayer, float)[] active = OverlayColors.ActiveCellLayers(Layers);
        bool riverLines = !_textured || !_hasArt;
        int riverZ = 1 + Layers.IndexOf(MapLayer.Rivers);
        int borderZ = 1 + Layers.IndexOf(MapLayer.Borders);
        float riverAlpha = Layers.Alpha(MapLayer.Rivers);
        float borderAlpha = Layers.Alpha(MapLayer.Borders);
        for (int k = 0; k < _chunks.Count; k++)
        {
            ChunkGfx c = _chunks[k];
            Rect2 bounds = c.Bounds;
            int kx0 = (int)MathF.Floor((view.Position.X - bounds.End.X) / _period.X) + 1;
            int kx1 = (int)MathF.Floor((view.End.X - bounds.Position.X) / _period.X);

            // The globe wraps only east–west (ADR 0012): copies side by side, never above or below.
            if (kx1 < kx0 || bounds.End.Y < view.Position.Y || bounds.Position.Y > view.End.Y)
            {
                continue;
            }

            // A chunk drawn for the first time must be coloured now; later updates wait for the budget.
            bool time = System.Diagnostics.Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds < RebuildMillisecondsPerFrame;
            if (c.ColorsDirty && (time || !c.EverColored))
            {
                c.UpdateColors(_world, _session.Content, active, fog);
            }

            if (c.LinesDirty && (time || !c.EverLined))
            {
                c.UpdateLines(_world, RiverBaseWidth, BorderWidth, _fogHides ? fog : null);
            }

            for (int kx = kx0; kx <= kx1; kx++)
            {
                var offset = new Vector2(kx * _period.X, 0);
                MultiMeshInstance2D node = Take(_hexPool, used++, 0);
                node.Multimesh = c.Hexes;
                node.Material = _hexMaterial;
                node.Position = c.Origin + offset;
                if (riverLines && riverAlpha > 0 && c.Rivers.InstanceCount > 0)
                {
                    MultiMeshInstance2D rn = Take(_riverPool, usedRivers++, riverZ);
                    rn.Multimesh = c.Rivers;
                    rn.Position = c.Origin + offset;
                    rn.ZIndex = riverZ;
                    rn.Modulate = new Color(1, 1, 1, riverAlpha);
                }

                if (borderAlpha > 0 && c.Borders.InstanceCount > 0)
                {
                    MultiMeshInstance2D bn = Take(_borderPool, usedBorders++, borderZ);
                    bn.Multimesh = c.Borders;
                    bn.Position = c.Origin + offset;
                    bn.ZIndex = borderZ;
                    bn.Modulate = new Color(1, 1, 1, borderAlpha);
                }
            }
        }

        Release(_hexPool, used);
        Release(_riverPool, usedRivers);
        Release(_borderPool, usedBorders);
        VisibleChunks = used;
        _objects?.Show(_session, _world, Layers, _period, view, zoom);
        UpdateOutline(view);
    }

    private ShaderMaterial BuildHexMaterial(ContentMessage? content)
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/map_hex.gdshader") };
        if (content is not null)
        {
            (ImageTexture tiles, ImageTexture flat) = Art.BuildBiomeAtlas(content);
            material.SetShaderParameter("biome_atlas", tiles);
            material.SetShaderParameter("biome_flat", flat);
            material.SetShaderParameter("biome_grid", new Vector2(ArtLibrary.BiomeVariants, Math.Max(1, content.Biomes.Count)));
            material.SetShaderParameter("terrain_atlas", Art.BuildTerrainAtlas());
            material.SetShaderParameter("terrain_grid", new Vector2(ArtLibrary.TerrainColumns, (ArtLibrary.TerrainTiles + ArtLibrary.TerrainColumns - 1) / ArtLibrary.TerrainColumns));
            material.SetShaderParameter("river_tile", (float)ArtLibrary.RiverTile);
            _hasArt = true;
        }

        material.SetShaderParameter("has_art", _hasArt);
        material.SetShaderParameter("textured", _textured && _hasArt);
        return material;
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

    // Hex polygon with UVs over the 48×42 tile, so the shader can sample the tile pictures.
    private static ArrayMesh BuildHexMesh(float scale)
    {
        var verts = new Vector2[7];
        var uvs = new Vector2[7];
        verts[0] = Vector2.Zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int i = 0; i < 6; i++)
        {
            verts[i + 1] = HexLayout.Corners[i] * scale;
            uvs[i + 1] = new Vector2(0.5f + (HexLayout.Corners[i].X * scale / HexLayout.HexWidth), 0.5f + (HexLayout.Corners[i].Y * scale / HexLayout.HexHeight));
        }

        var idx = new int[18];
        for (int i = 0; i < 6; i++)
        {
            idx[i * 3] = 0;
            idx[(i * 3) + 1] = i + 1;
            idx[(i * 3) + 2] = ((i + 1) % 6) + 1;
        }

        return BuildMesh(verts, idx, uvs);
    }

    // Unit segment along +X, centred on the Y axis; instances scale and rotate it onto a hex edge.
    private static ArrayMesh BuildQuadMesh() =>
        BuildMesh([new(0, -0.5f), new(1, -0.5f), new(1, 0.5f), new(0, 0.5f)], [0, 1, 2, 0, 2, 3], null);

    private static ArrayMesh BuildMesh(Vector2[] verts, int[] idx, Vector2[]? uvs)
    {
        var colors = new Color[verts.Length];
        Array.Fill(colors, Colors.White);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        if (uvs is not null)
        {
            arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        }

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
        // 8 floats of 2D transform + 4 of colour + 4 of custom data per hex.
        private const int Stride = 16;
        private static readonly Color RiverColor = OverlayColors.Rgb(0x1977D0);
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
                UseCustomData = true,
                Mesh = hex,
                InstanceCount = w * h,
            };

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

            Rivers = NewLines(quad);
            Borders = NewLines(quad);
        }

        public Vector2 Origin { get; }

        public Rect2 Bounds { get; }

        public MultiMesh Hexes { get; }

        public MultiMesh Rivers { get; }

        public MultiMesh Borders { get; }

        public bool ColorsDirty { get; set; } = true;

        public bool LinesDirty { get; set; } = true;

        public bool EverColored { get; private set; }

        public bool EverLined { get; private set; }

        private int X0 { get; }

        private int Y0 { get; }

        private int W { get; }

        private int H { get; }

        public void UpdateColors(ClientWorld world, ContentMessage? content, (MapLayer, float)[] active, byte[]? fog)
        {
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    int col = X0 + c;
                    int row = Y0 + r;
                    int i = world.Index(col, row);
                    Color tint = OverlayColors.Tint(active, world, content, fog, i);
                    int o = (((r * W) + c) * Stride) + 8;
                    _buffer[o] = tint.R;
                    _buffer[o + 1] = tint.G;
                    _buffer[o + 2] = tint.B;
                    _buffer[o + 3] = tint.A;

                    // Tiles: a variant picked by the cell (stable), relief and feature tiles, river edges.
                    int variant = (int)(((uint)i * 2654435761u) >> 30) % ArtLibrary.BiomeVariants;
                    int relief = world.Relief[i];
                    _buffer[o + 4] = (world.Biome[i] * ArtLibrary.BiomeVariants) + variant + (1000 * relief);
                    _buffer[o + 5] = relief switch
                    {
                        1 => ArtLibrary.HillsTile + (variant & 1),
                        2 => ArtLibrary.MountainsTile + (variant & 1),
                        _ => -1,
                    };
                    int feature = world.Feature[i];
                    _buffer[o + 6] = feature > 0 && feature < ArtLibrary.Features.Length ? ArtLibrary.FeatureTile + feature - 1 : -1;
                    _buffer[o + 7] = RiverBits(world, col, row);
                }
            }

            Hexes.Buffer = _buffer;
            ColorsDirty = false;
            EverColored = true;
        }

        // River sizes on all six edges, 2 bits each (N, NE, SE own; S, SW, NW belong to the neighbours, ADR 0012 layout).
        private static int RiverBits(ClientWorld w, int col, int row)
        {
            const int per = MapChunkMessage.RiverEdgesPerCell;
            int i = w.Index(col, row);
            int bits = 0;
            for (int e = 0; e < per; e++)
            {
                bits |= Math.Min(3, (int)w.River[(i * per) + e]) << (e * 2);
            }

            bool odd = (col & 1) == 1;
            bits |= Edge(w, col, row + 1, 0) << 6;                  // S = north edge of the cell below
            bits |= Edge(w, col - 1, odd ? row + 1 : row, 1) << 8;    // SW = north-east edge of the south-west neighbour
            bits |= Edge(w, col - 1, odd ? row : row - 1, 2) << 10;   // NW = south-east edge of the north-west neighbour
            return bits;
        }

        private static int Edge(ClientWorld w, int col, int row, int edge)
        {
            if (row < 0 || row >= w.Height)
            {
                return 0;
            }

            return Math.Min(3, (int)w.River[(w.Index(col, row) * MapChunkMessage.RiverEdgesPerCell) + edge]);
        }

        public void UpdateLines(ClientWorld world, float riverWidth, float borderWidth, byte[]? hiddenBy)
        {
            // Shared scratch lists (the main thread only): no new lists for every chunk update.
            List<(Transform2D, Color)> rivers = ScratchRivers;
            List<(Transform2D, Color)> borders = ScratchBorders;
            rivers.Clear();
            borders.Clear();
            for (int r = 0; r < H; r++)
            {
                for (int c = 0; c < W; c++)
                {
                    int col = X0 + c;
                    int row = Y0 + r;
                    int i = world.Index(col, row);
                    Vector2 center = HexLayout.Center(col, row) - Origin;
                    if (OverlayColors.Hidden(hiddenBy, 1f, i))
                    {
                        continue; // the followed civilization does not know this cell
                    }

                    for (int e = 0; e < MapChunkMessage.RiverEdgesPerCell; e++)
                    {
                        int size = world.River[(i * MapChunkMessage.RiverEdgesPerCell) + e];
                        if (size > 0)
                        {
                            rivers.Add((Segment(center, HexLayout.EdgeCorners[e], riverWidth * size, 0), RiverColor));
                        }
                    }

                    // Borders: every edge where the neighbour belongs to someone else, drawn on this cell's side.
                    short owner = world.Owner[i];
                    if (owner < 0)
                    {
                        continue;
                    }

                    bool odd = (col & 1) == 1;
                    for (int e = 0; e < 6; e++)
                    {
                        (int nc, int nr) = e switch
                        {
                            0 => (col, row - 1),
                            1 => (col + 1, odd ? row : row - 1),
                            2 => (col + 1, odd ? row + 1 : row),
                            3 => (col, row + 1),
                            4 => (col - 1, odd ? row + 1 : row),
                            _ => (col - 1, odd ? row : row - 1),
                        };
                        if (nr < 0 || nr >= world.Height || world.Owner[world.Index(nc, nr)] == owner)
                        {
                            continue;
                        }

                        // A dark outline under the colour, so borders read on any biome.
                        borders.Add((Segment(center, AllEdges[e], borderWidth + 3, 0.1f), new Color(0.11f, 0.09f, 0.06f)));
                        borders.Add((Segment(center, AllEdges[e], borderWidth, 0.12f), OverlayColors.CivColor(owner)));
                    }
                }
            }

            Fill(Rivers, rivers);
            Fill(Borders, borders);
            LinesDirty = false;
            EverLined = true;
        }

        private static readonly List<(Transform2D, Color)> ScratchRivers = [];
        private static readonly List<(Transform2D, Color)> ScratchBorders = [];

        // Corners of the six edges N, NE, SE, S, SW, NW (indexes into HexLayout.Corners).
        private static readonly (int A, int B)[] AllEdges = [(4, 5), (5, 0), (0, 1), (1, 2), (2, 3), (3, 4)];

        // A segment along a hex edge, moved inwards by `inset` of the way to the centre.
        private static Transform2D Segment(Vector2 center, (int A, int B) edge, float width, float inset)
        {
            Vector2 a = center + (HexLayout.Corners[edge.A] * (1 - inset));
            Vector2 b = center + (HexLayout.Corners[edge.B] * (1 - inset));
            Vector2 x = b - a;
            Vector2 y = new Vector2(-x.Y, x.X).Normalized() * width;
            return new Transform2D(x, y, a);
        }

        private static MultiMesh NewLines(Mesh quad) => new()
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseColors = true,
            Mesh = quad,
            InstanceCount = 0,
        };

        private static void Fill(MultiMesh mesh, List<(Transform2D T, Color C)> items)
        {
            mesh.InstanceCount = items.Count;
            for (int k = 0; k < items.Count; k++)
            {
                mesh.SetInstanceTransform2D(k, items[k].T);
                mesh.SetInstanceColor(k, items[k].C);
            }
        }
    }
}
