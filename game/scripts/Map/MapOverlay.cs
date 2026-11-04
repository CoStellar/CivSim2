using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Map;

/// <summary>What the map shows (GDD 12.4 "Widoki mapy" in their developer form).</summary>
public enum MapOverlay
{
    /// <summary>Biomes with relief and features.</summary>
    Biomes,

    /// <summary>Height.</summary>
    Elevation,

    /// <summary>Temperature.</summary>
    Temperature,

    /// <summary>Humidity.</summary>
    Humidity,

    /// <summary>Soil fertility.</summary>
    Fertility,

    /// <summary>Deposits.</summary>
    Deposits,

    /// <summary>Owner civilization (from Stage 3).</summary>
    Owner,

    /// <summary>Influence zones (from Stage 3; until then the same as owner).</summary>
    Influence,
}

/// <summary>Colours of each overlay. Developer preview colours, not game art.</summary>
public static class OverlayColors
{
    private static readonly Color Unknown = new(0.05f, 0.05f, 0.06f);
    private static readonly Color Water = Rgb(0x0A2647);

    /// <summary>Colour of cell <paramref name="i"/>.</summary>
    public static Color Of(MapOverlay overlay, ClientWorld w, ContentMessage? content, int i)
    {
        if (w.Visibility[i] == MapChunkMessage.Unknown)
        {
            return Unknown;
        }

        int biome = w.Biome[i];
        bool water = content is not null && biome < content.Biomes.Count && content.Biomes[biome].IsWater;
        Color c = overlay switch
        {
            MapOverlay.Biomes => BiomeColor(w, content, i),
            MapOverlay.Elevation => ElevationColor(w.Elevation[i], water),
            MapOverlay.Temperature => TemperatureColor(w.Temperature[i], water),
            MapOverlay.Humidity => water ? Water : Rgb(0xC8A060).Lerp(Rgb(0x1E6E32), w.Humidity[i] / 100f),
            MapOverlay.Fertility => water ? Water : Rgb(0x5A4A3A).Lerp(Rgb(0x40D040), w.Fertility[i] / 100f),
            MapOverlay.Deposits => DepositColor(w, content, i, water),
            _ => OwnerColor(w.Owner[i], water),
        };
        return w.Visibility[i] == MapChunkMessage.Remembered ? c.Darkened(0.4f) : c;
    }

    /// <summary>0xRRGGBB to a Godot colour.</summary>
    public static Color Rgb(uint rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    /// <summary>Stable distinct colour of a civilization.</summary>
    public static Color CivColor(int civ) => Color.FromHsv((civ * 0.618034f) % 1f, 0.75f, 0.95f);

    private static Color BiomeColor(ClientWorld w, ContentMessage? content, int i)
    {
        int b = w.Biome[i];
        Color c = content is not null && b < content.Biomes.Count ? Rgb(content.Biomes[b].Color) : Colors.Magenta;
        c = w.Feature[i] switch
        {
            1 => Rgb(0x4FB04F), // oasis
            2 => Rgb(0x2FA88A), // reef
            3 => Rgb(0xC8DCE6), // ice field
            _ => c,
        };
        return w.Relief[i] switch
        {
            1 => c.Darkened(0.18f),
            2 => c.Lerp(Rgb(0x5A4E44), 0.6f),
            _ => c,
        };
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

    private static Color DepositColor(ClientWorld w, ContentMessage? content, int i, bool water)
    {
        int d = w.Deposit[i];
        if (d == 0 || content is null || d > content.Deposits.Count)
        {
            return water ? Rgb(0x16283A) : w.Relief[i] == 2 ? Rgb(0x5C5C5C) : Rgb(0x3C3C3C);
        }

        Color c = Rgb(content.Deposits[d - 1].Color);
        return w.DepositDepth[i] == 0 ? c : c.Darkened(0.5f);
    }

    private static Color OwnerColor(short owner, bool water)
    {
        if (owner < 0)
        {
            return water ? Rgb(0x16283A) : Rgb(0x3C3C3C);
        }

        return CivColor(owner);
    }
}
