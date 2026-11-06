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
        foreach ((MapLayer layer, float opacity) in active)
        {
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
        MapLayer.Elevation => (ElevationColor(w.Elevation[i], water), 1),
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

    private static (Color, float) FogColor(byte[]? fog, int i)
    {
        if (fog is null || i >= fog.Length)
        {
            return (Colors.Black, 0);
        }

        return fog[i] switch
        {
            MapChunkMessage.Unknown => (Unknown, 1),
            MapChunkMessage.Remembered => (Unknown, 0.55f),
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

    private static Color ElevationColor(int e, bool water)
    {
        if (water)
        {
            return Rgb(0x061A33).Lerp(Rgb(0x2E7FC4), e / 100f);
        }

        return e < 170 ? Rgb(0x3B7A2A).Lerp(Rgb(0xC9B458), (e - 100) / 70f) : Rgb(0xC9B458).Lerp(Rgb(0xF4F4F4), (e - 170) / 85f);
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
