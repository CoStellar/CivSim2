using System;
using System.Collections.Generic;
using System.IO;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// Loads pictures by name (GDD 12.3), in this order: the chosen art pack (<see cref="ArtPack"/>), then
/// <c>art/export/&lt;layer&gt;/</c> (Patryk's pixel art), then <c>art/placeholder/&lt;layer&gt;/</c> (stage 5
/// placeholders). The pack and art/export are checked when the library is made (<see cref="ArtCheck"/>): only files with
/// a known name and the exact size of <c>art/placeholder/manifest.json</c> are used, the rest are listed in
/// <see cref="Issues"/>. Pictures are read as plain PNG files at run time, so they need no Godot import.
/// </summary>
public sealed class ArtLibrary
{
    /// <summary>Variants of each biome tile (GDD 12.2).</summary>
    public const int BiomeVariants = 3;

    /// <summary>Hex tile size in pixels.</summary>
    public const int TileWidth = 48;

    /// <summary>Hex tile height in pixels.</summary>
    public const int TileHeight = 42;

    /// <summary>River edges in drawing order (N, NE, SE, S, SW, NW) as used in the file names.</summary>
    public static readonly string[] RiverEdges = ["n", "ne", "se", "s", "sw", "nw"];

    private readonly string _placeholder;
    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly Dictionary<string, (int Width, int Height)> _expected = new(StringComparer.Ordinal);
    private readonly ArtCheck _export;
    private readonly ArtCheck _pack;

    /// <summary>
    /// Looks for pictures in <paramref name="artDirectory"/> (the repository's art/ folder) and, when given, in
    /// <paramref name="pack"/> first.
    /// </summary>
    public ArtLibrary(string artDirectory, ArtPack? pack = null)
    {
        _placeholder = Path.Combine(artDirectory, "placeholder");
        ReadManifest(Path.Combine(_placeholder, "manifest.json"));
        Pack = pack;
        _export = ArtCheck.Run("art/export", Path.Combine(artDirectory, "export"), _expected);
        _pack = pack is null ? new ArtCheck() : ArtCheck.Run(pack.Name, pack.Directory, _expected);
        if (pack is not null)
        {
            WriteReport(pack);
        }

        foreach (ArtIssue issue in Issues)
        {
            GD.PrintErr($"art: {issue.Source}/{issue.File}: {issue.Problem}");
        }
    }

    /// <summary>The art pack in use, or null for the default pictures.</summary>
    public ArtPack? Pack { get; }

    /// <summary>Problems of the pack and of art/export found at start, in file order.</summary>
    public IReadOnlyList<ArtIssue> Issues => [.. _pack.Issues, .. _export.Issues];

    /// <summary>Pictures the pack replaces.</summary>
    public int PackFiles => _pack.Accepted.Count;

    /// <summary>Pictures art/export replaces.</summary>
    public int ExportFiles => _export.Accepted.Count;

    /// <summary>Pictures the game knows (manifest entries).</summary>
    public int KnownFiles => _expected.Count;

    /// <summary>Terrain atlas index of the first hills tile (two variants, then two of mountains).</summary>
    public const int HillsTile = 0;

    /// <summary>Terrain atlas index of the first mountains tile.</summary>
    public const int MountainsTile = 2;

    /// <summary>Terrain atlas index of the first feature tile (feature id 1 = oasis, 2 = reef, 3 = ice field).</summary>
    public const int FeatureTile = 4;

    /// <summary>Terrain atlas index of the first river tile: edge × 2 + (0 stream, 1 river).</summary>
    public const int RiverTile = 7;

    /// <summary>Tiles in the terrain atlas.</summary>
    public const int TerrainTiles = RiverTile + 12;

    /// <summary>Columns of the terrain atlas.</summary>
    public const int TerrainColumns = 8;

    /// <summary>Feature file names by feature id (0 = none).</summary>
    public static readonly string[] Features = [string.Empty, "oasis", "reef", "ice_field"];

    /// <summary>Where the art folder is: next to game/ while developing, next to the executable in builds.</summary>
    public static string FindArtDirectory()
    {
        string[] candidates =
        [
            ProjectSettings.GlobalizePath("res://../art"),
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "art"),
        ];
        foreach (string dir in candidates)
        {
            if (Directory.Exists(Path.Combine(dir, "placeholder")) || Directory.Exists(Path.Combine(dir, "export")))
            {
                return dir;
            }
        }

        return candidates[0];
    }

    /// <summary>A picture as an image (pack, then art/export, then placeholder), or null when none has that name.</summary>
    public Image? LoadImage(string layer, string name)
    {
        string rel = layer + "/" + name + ".png";
        string[] candidates =
        [
            _pack.Accepted.GetValueOrDefault(rel) ?? string.Empty,
            _export.Accepted.GetValueOrDefault(rel) ?? string.Empty,
            Path.Combine(_placeholder, layer, name + ".png"),
        ];
        foreach (string path in candidates)
        {
            if (path.Length == 0 || !File.Exists(path))
            {
                continue;
            }

            var image = new Image();
            if (image.Load(path) == Error.Ok && !image.IsEmpty())
            {
                image.Convert(Image.Format.Rgba8);
                return image;
            }
        }

        return null;
    }

    // The sizes every picture must have, from the placeholder manifest (written by art/placeholder/generate.py).
    private void ReadManifest(string path)
    {
        if (!File.Exists(path))
        {
            GD.PrintErr($"art: {path} is missing, art packs cannot be checked and are not used");
            return;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        foreach (System.Text.Json.JsonElement e in doc.RootElement.GetProperty("files").EnumerateArray())
        {
            _expected[e.GetProperty("file").GetString()!] = (e.GetProperty("width").GetInt32(), e.GetProperty("height").GetInt32());
        }
    }

    // art_check.txt in the pack folder, so whoever draws the pack sees what was wrong without the game open.
    private void WriteReport(ArtPack pack)
    {
        try
        {
            File.WriteAllText(Path.Combine(pack.Directory, "art_check.txt"), ArtCheck.Report(pack.Name, _pack, _expected.Count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"art: cannot write the check report into {pack.Directory}: {ex.Message}");
        }
    }

    /// <summary>A picture as a texture (cached), or null.</summary>
    public Texture2D? Get(string layer, string name)
    {
        string key = layer + "/" + name;
        if (!_textures.TryGetValue(key, out Texture2D? texture))
        {
            Image? image = LoadImage(layer, name);
            texture = image is null ? null : ImageTexture.CreateFromImage(image);
            _textures[key] = texture;
        }

        return texture;
    }

    /// <summary>
    /// The biome atlas: one row per biome id, <see cref="BiomeVariants"/> tiles each. A missing variant repeats the
    /// first one; a biome without any file gets a hex in its data colour. Also a 1-pixel-per-tile atlas of average
    /// colours for far zoom, where single pixels would shimmer.
    /// </summary>
    public (ImageTexture Tiles, ImageTexture Flat) BuildBiomeAtlas(ContentMessage content)
    {
        ArgumentNullException.ThrowIfNull(content);
        int biomes = Math.Max(1, content.Biomes.Count);
        Image atlas = Image.CreateEmpty(TileWidth * BiomeVariants, TileHeight * biomes, false, Image.Format.Rgba8);
        Image flat = Image.CreateEmpty(BiomeVariants, biomes, false, Image.Format.Rgba8);
        for (int b = 0; b < content.Biomes.Count; b++)
        {
            BiomeInfo info = content.Biomes[b];
            Image? first = null;
            for (int v = 0; v < BiomeVariants; v++)
            {
                Image? tile = LoadImage("biome", $"biome_{info.Key}_{v + 1:D2}") ?? first ?? HexOf(OverlayColors.Rgb(info.Color));
                first ??= tile;
                atlas.BlitRect(tile, new Rect2I(0, 0, TileWidth, TileHeight), new Vector2I(v * TileWidth, b * TileHeight));
                flat.SetPixel(v, b, Average(tile));
            }
        }

        return (ImageTexture.CreateFromImage(atlas), ImageTexture.CreateFromImage(flat));
    }

    /// <summary>The terrain atlas: hills, mountains, features and the 12 river edges (see the tile constants).</summary>
    public ImageTexture BuildTerrainAtlas()
    {
        int rows = (TerrainTiles + TerrainColumns - 1) / TerrainColumns;
        Image atlas = Image.CreateEmpty(TileWidth * TerrainColumns, TileHeight * rows, false, Image.Format.Rgba8);
        var names = new List<string> { "terrain_hills_01", "terrain_hills_02", "terrain_mountains_01", "terrain_mountains_02" };
        for (int f = 1; f < Features.Length; f++)
        {
            names.Add($"terrain_{Features[f]}_01");
        }

        foreach (string edge in RiverEdges)
        {
            names.Add($"terrain_river_{edge}_stream");
            names.Add($"terrain_river_{edge}_river");
        }

        for (int k = 0; k < names.Count; k++)
        {
            Image? tile = LoadImage("terrain", names[k]);
            if (tile is null && names[k].EndsWith("_02", StringComparison.Ordinal))
            {
                tile = LoadImage("terrain", names[k][..^3] + "_01");
            }

            if (tile is not null)
            {
                atlas.BlitRect(tile, new Rect2I(0, 0, TileWidth, TileHeight), new Vector2I(k % TerrainColumns * TileWidth, k / TerrainColumns * TileHeight));
            }
        }

        return ImageTexture.CreateFromImage(atlas);
    }

    private static Image HexOf(Color color)
    {
        Image image = Image.CreateEmpty(TileWidth, TileHeight, false, Image.Format.Rgba8);
        for (int y = 0; y < TileHeight; y++)
        {
            for (int x = 0; x < TileWidth; x++)
            {
                // Flat-top hex: the slanted sides run from the middle of the left and right edges to 12 px in.
                float dy = MathF.Abs(y + 0.5f - (TileHeight / 2f)) / (TileHeight / 2f);
                float inset = 12 * dy;
                if (x + 0.5f >= inset && x + 0.5f <= TileWidth - inset)
                {
                    image.SetPixel(x, y, color);
                }
            }
        }

        return image;
    }

    private static Color Average(Image tile)
    {
        float r = 0, g = 0, b = 0;
        int n = 0;
        for (int y = 0; y < tile.GetHeight(); y++)
        {
            for (int x = 0; x < tile.GetWidth(); x++)
            {
                Color c = tile.GetPixel(x, y);
                if (c.A > 0.5f)
                {
                    r += c.R;
                    g += c.G;
                    b += c.B;
                    n++;
                }
            }
        }

        return n == 0 ? Colors.Magenta : new Color(r / n, g / n, b / n);
    }
}
