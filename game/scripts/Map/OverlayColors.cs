using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// Colours of the cell layers (stage 5). Each layer gives a colour and a coverage for a cell; coverage 0 lets the
/// layers below show through (e.g. deposits only where there is one), so several layers can be on at once.
/// Developer preview colours, not game art.
/// </summary>
public static class OverlayColors
{
    private static readonly Color Water = Rgb(0x0A2647);
    private static readonly Color Unknown = new(0.05f, 0.05f, 0.06f);

    /// <summary>Colour of the fog of war (palette dark grey): softer on the eyes than black.</summary>
    public static readonly Color FogGrey = Rgb(0x3E3C3A);

    /// <summary>Cover of a remembered cell in the fog, at 100% layer opacity.</summary>
    public const float RememberedCover = 0.6f;

    /// <summary>0xRRGGBB to a Godot colour.</summary>
    public static Color Rgb(uint rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    /// <summary>Stable distinct colour of a civilization.</summary>
    public static Color CivColor(int civ) => Color.FromHsv((civ * 0.618034f) % 1f, 0.75f, 0.95f);

    /// <summary>
    /// Blends every enabled cell layer above the biomes into one tint for cell <paramref name="i"/> ("over"
    /// compositing, bottom first). The shader mixes the biome tile with the tint by its alpha.
    /// </summary>
    public static Color Tint(LayerStack layers, ClientWorld w, ContentMessage? content, byte[]? fog, int i) =>
        Tint(ActiveCellLayers(layers), w, content, fog, i);

    /// <summary>The cell layers to blend, bottom first, with their opacity (worked out once per chunk, not per cell).</summary>
    public static (MapLayer Layer, float Alpha)[] ActiveCellLayers(LayerStack layers)
    {
        var list = new System.Collections.Generic.List<(MapLayer, float)>();
        foreach (LayerSetting s in layers.Layers)
        {
            if (s.Enabled && s.Opacity > 0 && s.Layer != MapLayer.Biomes && LayerStack.IsCellLayer(s.Layer))
            {
                list.Add((s.Layer, s.Opacity / 100f));
            }
        }

        return [.. list];
    }

    /// <summary>Blends the given active cell layers for cell <paramref name="i"/>.</summary>
    public static Color Tint((MapLayer Layer, float Alpha)[] active, ClientWorld w, ContentMessage? content, byte[]? fog, int i)
    {
        if (w.Visibility[i] == MapChunkMessage.Unknown)
        {
            return Unknown; // the server sent nothing about the cell
        }

        float r = 0, g = 0, b = 0, a = 0;
        int biome = w.Biome[i];
        bool water = content is not null && biome < content.Biomes.Count && content.Biomes[biome].IsWater;
        float fogOpacity = 0;
        foreach ((MapLayer layer, float opacity) in active)
        {
            // The fog always goes over every other cell layer, wherever it is in the list.
            if (layer == MapLayer.Fog)
            {
                fogOpacity = opacity;
                continue;
            }

            (Color c, float cover) = Of(layer, w, content, fog, i, water);
            float alpha = cover * opacity;
            if (alpha <= 0)
            {
                continue;
            }

            // Premultiplied "over": the new layer covers what is below by its alpha.
            r = (c.R * alpha) + (r * (1 - alpha));
            g = (c.G * alpha) + (g * (1 - alpha));
            b = (c.B * alpha) + (b * (1 - alpha));
            a = alpha + (a * (1 - alpha));
        }

        if (fogOpacity > 0)
        {
            (Color c, float cover) = FogColor(fog, i);
            float alpha = cover * fogOpacity;
            r = (c.R * alpha) + (r * (1 - alpha));
            g = (c.G * alpha) + (g * (1 - alpha));
            b = (c.B * alpha) + (b * (1 - alpha));
            a = alpha + (a * (1 - alpha));
        }

        if (w.Visibility[i] == MapChunkMessage.Remembered)
        {
            // A player's remembered cells are darker, like the fog layer's remembered cells.
            r *= 0.6f;
            g *= 0.6f;
            b *= 0.6f;
            a = 0.4f + (a * 0.6f);
        }

        return a <= 0 ? new Color(0, 0, 0, 0) : new Color(r / a, g / a, b / a, a);
    }

    /// <summary>Flat colour of a biome (when no tile is loaded and for the minimal zoom).</summary>
    public static Color BiomeColor(ContentMessage? content, int biome) =>
        content is not null && biome < content.Biomes.Count ? Rgb(content.Biomes[biome].Color) : Colors.Magenta;

    private static (Color Color, float Cover) Of(MapLayer layer, ClientWorld w, ContentMessage? content, byte[]? fog, int i, bool water) => layer switch
    {
        MapLayer.Elevation => (ElevationColor(w, content, i, water), 1),
        MapLayer.Hillshade => water ? (Colors.Black, 0) : Hillshade(w, content, i),
        MapLayer.Temperature => (TemperatureColor(w.Temperature[i], water), 1),
        MapLayer.Humidity => water ? (Water, 1) : (Rgb(0xC8A060).Lerp(Rgb(0x1E6E32), w.Humidity[i] / 100f), 1),
        MapLayer.Fertility => water ? (Water, 0) : (Rgb(0x5A4A3A).Lerp(Rgb(0x40D040), w.Fertility[i] / 100f), 1),
        MapLayer.Growing => water ? (Water, 0) : (Rgb(0x6A5236).Lerp(Rgb(0x2FA83A), w.GrowingSeason[i] / 100f), 1),
        MapLayer.Seasons => (SeasonColor(w.Seasonality[i], water), 1),
        MapLayer.SeasonNow => (TemperatureColor(w.SeasonTemperature[i], water), 1),
        MapLayer.WildFood => water ? (Water, 0) : (Rgb(0x7A4A1E).Lerp(Rgb(0x2E8B3A), w.WildFood[i] / 1000f), 1),
        MapLayer.Deposits => DepositColor(w, content, i),
        MapLayer.Owner => w.Owner[i] < 0 ? (Colors.Black, 0) : (CivColor(w.Owner[i]), 1),
        MapLayer.Influence => w.Owner[i] < 0 ? (Colors.Black, 0) : (CivColor(w.Owner[i]), 0.25f + (0.75f * Mathf.Min(1f, w.Influence[i] / 240f))),
        MapLayer.Fog => FogColor(fog, i),
        _ => (Colors.Black, 0),
    };

    /// <summary>
    /// True when the fog hides everything on a cell: the cell is unknown to the followed civilization and the fog
    /// layer is at full opacity. Drawn layers (settlements, borders, units...) are left out there too.
    /// </summary>
    public static bool Hidden(byte[]? fog, float fogAlpha, int i) =>
        fog is not null && fogAlpha >= 0.99f && i < fog.Length && fog[i] == MapChunkMessage.Unknown;

    private static (Color, float) FogColor(byte[]? fog, int i)
    {
        if (fog is null || i >= fog.Length)
        {
            return (Colors.Black, 0);
        }

        return fog[i] switch
        {
            MapChunkMessage.Unknown => (FogGrey, 1),
            MapChunkMessage.Remembered => (FogGrey, RememberedCover),
            _ => (Colors.Black, 0),
        };
    }

    private static Color SeasonColor(byte swing, bool water)
    {
        Color c = swing < 40
            ? Rgb(0x2E6FB8).Lerp(Rgb(0xE8E8E8), swing / 40f)
            : Rgb(0xE8E8E8).Lerp(Rgb(0x8A1E8C), System.Math.Min(80, swing - 40) / 80f);
        return water ? c.Darkened(0.35f) : c;
    }

    /// <summary>Sea level of the elevation scale (GDD 3: about 100; lower in ice ages).</summary>
    public const int SeaLevel = 100;

    /// <summary>
    /// Depth of a water cell in elevation steps: below sea level for seas; for lakes, below the lowest land around
    /// (the lake surface), so it is only an estimate. 0 for land.
    /// </summary>
    public static int Depth(ClientWorld w, ContentMessage? content, int i)
    {
        if (!IsWater(w, content, i))
        {
            return 0;
        }

        int e = w.Elevation[i];
        if (e < SeaLevel)
        {
            return SeaLevel - e;
        }

        int shore = int.MaxValue;
        (int col, int row) = (i % w.Width, i / w.Width);
        for (int edge = 0; edge < 6; edge++)
        {
            (int nc, int nr) = Neighbor(col, row, edge);
            if (nr >= 0 && nr < w.Height)
            {
                int n = w.Index(nc, nr);
                if (!IsWater(w, content, n))
                {
                    shore = System.Math.Min(shore, w.Elevation[n]);
                }
            }
        }

        return shore == int.MaxValue ? 1 : System.Math.Max(1, shore - e + 1);
    }

    /// <summary>Neighbour of a cell across edge 0–5 (N, NE, SE, S, SW, NW) on the odd-q grid, not wrapped.</summary>
    public static (int Col, int Row) Neighbor(int col, int row, int edge)
    {
        bool odd = (col & 1) == 1;
        return edge switch
        {
            0 => (col, row - 1),
            1 => (col + 1, odd ? row : row - 1),
            2 => (col + 1, odd ? row + 1 : row),
            3 => (col, row + 1),
            4 => (col - 1, odd ? row + 1 : row),
            _ => (col - 1, odd ? row : row - 1),
        };
    }

    private static bool IsWater(ClientWorld w, ContentMessage? content, int i) =>
        content is not null && w.Biome[i] < content.Biomes.Count && content.Biomes[w.Biome[i]].IsWater;

    // Land: green lowlands, yellow and brown hills, grey and white peaks. Water: light shallows to dark deeps.
    private static Color ElevationColor(ClientWorld w, ContentMessage? content, int i, bool water)
    {
        if (water)
        {
            int depth = Depth(w, content, i);
            return Rgb(0x9FD8EE).Lerp(Rgb(0x0A1F44), Mathf.Clamp(Mathf.Sqrt(depth / 60f), 0, 1));
        }

        float h = (w.Elevation[i] - SeaLevel) / 155f;
        (float At, uint Rgb)[] stops = [(0f, 0x2E7D32), (0.12f, 0x7CB342), (0.3f, 0xE6D36A), (0.5f, 0xB7763C), (0.7f, 0x7D6B5D), (0.85f, 0xBDB6B0), (1f, 0xFAFAFA)];
        for (int k = 1; k < stops.Length; k++)
        {
            if (h <= stops[k].At)
            {
                float t = (h - stops[k - 1].At) / (stops[k].At - stops[k - 1].At);
                return Rgb(stops[k - 1].Rgb).Lerp(Rgb(stops[k].Rgb), Mathf.Clamp(t, 0, 1));
            }
        }

        return Rgb(0xFAFAFA);
    }

    // Light from the top left (north-west): compare the height uphill and downhill of the light.
    private static (Color, float) Hillshade(ClientWorld w, ContentMessage? content, int i)
    {
        (int col, int row) = (i % w.Width, i / w.Width);
        int nw = Height(w, content, Neighbor(col, row, 5), i);
        int no = Height(w, content, Neighbor(col, row, 0), i);
        int se = Height(w, content, Neighbor(col, row, 2), i);
        int so = Height(w, content, Neighbor(col, row, 3), i);
        float slope = ((se + so) - (nw + no)) / 2f; // > 0: rises away from the light, so the face looks at it
        float strength = Mathf.Clamp(Mathf.Abs(slope) / 14f, 0, 1);
        return slope > 0 ? (Colors.White, strength * 0.7f) : (Colors.Black, strength);
    }

    private static int Height(ClientWorld w, ContentMessage? content, (int Col, int Row) c, int self)
    {
        if (c.Row < 0 || c.Row >= w.Height)
        {
            return w.Elevation[self];
        }

        int n = w.Index(c.Col, c.Row);
        return IsWater(w, content, n) ? SeaLevel : w.Elevation[n];
    }

    private static Color TemperatureColor(int t, bool water)
    {
        Color c = t < 10 ? Rgb(0x2040C0).Lerp(Rgb(0xE8E8E8), (t + 30) / 40f) : Rgb(0xE8E8E8).Lerp(Rgb(0xD02010), (t - 10) / 30f);
        return water ? c.Darkened(0.35f) : c;
    }

    // Only cells with a deposit are coloured; deposits hidden deeper are darker.
    private static (Color, float) DepositColor(ClientWorld w, ContentMessage? content, int i)
    {
        int d = w.Deposit[i];
        if (d == 0 || content is null || d > content.Deposits.Count)
        {
            return (Colors.Black, 0);
        }

        Color c = Rgb(content.Deposits[d - 1].Color);
        return (w.DepositDepth[i] == 0 ? c : c.Darkened(0.5f), 1);
    }
}
