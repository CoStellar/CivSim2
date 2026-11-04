using System;
using System.Collections.Generic;
using System.IO;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.State;
using CivSim.Core.WorldGen;

namespace CivSim.Headless.Png;

/// <summary>Map previews the PNG export can draw.</summary>
public enum MapView
{
    /// <summary>Biomes with relief shading, rivers and start positions.</summary>
    Biomes,

    /// <summary>Height above and below the sea.</summary>
    Elevation,

    /// <summary>Temperature, blue (cold) to red (hot).</summary>
    Temperature,

    /// <summary>Humidity, brown (dry) to green (wet).</summary>
    Humidity,

    /// <summary>Soil fertility.</summary>
    Fertility,

    /// <summary>Surface deposits as coloured dots over a grey map.</summary>
    Resources,
}

/// <summary>
/// Draws a world as flat-top hexes (developer preview, not game art). Each hex is 4k px wide, columns every 3k px,
/// rows every 2·round(k·√3) px, odd columns shifted half a row down, like the game grid (GDD 3).
/// </summary>
public sealed class MapRenderer
{
    private const uint RiverColor = 0x4FC3F7;
    private const uint StartColor = 0xFF00FF;

    private readonly WorldState _world;
    private readonly GameContent _content;
    private readonly IReadOnlyList<HexCoord> _starts;
    private readonly int _k;
    private readonly int _halfW;
    private readonly int _halfH;

    /// <summary>Creates a renderer. <paramref name="scale"/> (k) of 3 gives 12-px-wide hexes.</summary>
    public MapRenderer(WorldState world, GameContent content, IReadOnlyList<HexCoord> starts, int scale = 3)
    {
        _world = world;
        _content = content;
        _starts = starts;
        _k = Math.Clamp(scale, 1, 12);
        _halfW = 2 * _k;
        _halfH = (int)Math.Round(_k * 1.7320508);
    }

    /// <summary>Writes every view as <c>&lt;prefix&gt;_&lt;view&gt;.png</c> in <paramref name="folder"/>. Returns the paths.</summary>
    public IReadOnlyList<string> SaveAll(string folder, string prefix)
    {
        var paths = new List<string>();
        foreach (MapView view in Enum.GetValues<MapView>())
        {
            string path = Path.Combine(folder, $"{prefix}_{view.ToString().ToLowerInvariant()}.png");
            PngWriter.Save(Render(view), path);
            paths.Add(path);
        }

        return paths;
    }

    /// <summary>Renders one view.</summary>
    public RgbImage Render(MapView view)
    {
        HexTorus geo = _world.Geometry;
        var image = new RgbImage((geo.Width * 3 * _k) + _k, (geo.Height * 2 * _halfH) + _halfH);
        Array.Fill(image.Pixels, (byte)20);
        for (int i = 0; i < geo.CellCount; i++)
        {
            FillHex(image, geo.FromIndex(i), CellColor(view, i));
        }

        if (view is MapView.Biomes or MapView.Elevation or MapView.Resources)
        {
            DrawRivers(image);
        }

        if (view == MapView.Resources)
        {
            for (int i = 0; i < geo.CellCount; i++)
            {
                int d = _world.Map.Deposit[i];
                if (d != 0)
                {
                    (int cx, int cy) = Center(geo.FromIndex(i));
                    uint color = _content.Deposit(d).Color;
                    int r = Math.Max(1, _k - 1);
                    FillRect(image, cx - r, cy - r, (2 * r) + 1, (2 * r) + 1, _world.Map.DepositDepth[i] == 0 ? color : Darken(color, 50));
                }
            }
        }

        if (view == MapView.Biomes || view == MapView.Resources)
        {
            foreach (HexCoord s in _starts)
            {
                (int cx, int cy) = Center(s);
                int r = _k + 1;
                FillRect(image, cx - r - 1, cy - r - 1, (2 * r) + 3, (2 * r) + 3, 0x000000);
                FillRect(image, cx - r, cy - r, (2 * r) + 1, (2 * r) + 1, StartColor);
            }
        }

        return image;
    }

    private uint CellColor(MapView view, int i)
    {
        WorldMap m = _world.Map;
        int biome = m.Biome[i];
        bool water = _content.Biomes[biome].IsWater;
        switch (view)
        {
            case MapView.Biomes:
            {
                uint c = _content.Biomes[biome].Color;
                if (m.Feature[i] == (byte)TerrainFeature.IceField)
                {
                    c = 0xC8DCE6;
                }
                else if (m.Feature[i] == (byte)TerrainFeature.Reef)
                {
                    c = 0x2FA88A;
                }
                else if (m.Feature[i] == (byte)TerrainFeature.Oasis)
                {
                    c = 0x4FB04F;
                }

                return (Relief)m.Relief[i] switch
                {
                    Relief.Hills => Darken(c, 18),
                    Relief.Mountains => Blend(c, 0x5A4E44, 60),
                    _ => c,
                };
            }

            case MapView.Elevation:
            {
                int e = m.Elevation[i];
                return water
                    ? Lerp(0x061A33, 0x2E7FC4, e, WorldGenerator.SeaLevel)
                    : e < 170 ? Lerp(0x3B7A2A, 0xC9B458, e - 100, 70) : Lerp(0xC9B458, 0xF4F4F4, e - 170, 85);
            }

            case MapView.Temperature:
            {
                int t = m.Temperature[i];
                uint c = t < 10 ? Lerp(0x2040C0, 0xE8E8E8, t + 30, 40) : Lerp(0xE8E8E8, 0xD02010, t - 10, 30);
                return water ? Darken(c, 35) : c;
            }

            case MapView.Humidity:
                return water ? 0x0A2647u : Lerp(0xC8A060, 0x1E6E32, m.Humidity[i], 100);
            case MapView.Fertility:
                return water ? 0x0A2647u : Lerp(0x5A4A3A, 0x40D040, m.Fertility[i], 100);
            case MapView.Resources:
                return water ? 0x16283Au : (Relief)m.Relief[i] == Relief.Mountains ? 0x5C5C5Cu : 0x3C3C3Cu;
            default:
                return 0;
        }
    }

    private (int X, int Y) Center(HexCoord c) => ((c.Col * 3 * _k) + _halfW, (c.Row * 2 * _halfH) + _halfH + ((c.Col & 1) * _halfH));

    // Flat-top hexagon: corners at (±R, 0) and (±R/2, ±H). Inside when 2|dx|·H ≤ R·(2H − |dy|).
    private void FillHex(RgbImage image, HexCoord c, uint color)
    {
        (int cx, int cy) = Center(c);
        for (int dy = -_halfH; dy <= _halfH; dy++)
        {
            int ady = Math.Abs(dy);
            for (int dx = -_halfW; dx <= _halfW; dx++)
            {
                if (2 * Math.Abs(dx) * _halfH <= _halfW * ((2 * _halfH) - ady))
                {
                    image.Set(cx + dx, cy + dy, color);
                }
            }
        }
    }

    private void DrawRivers(RgbImage image)
    {
        HexTorus geo = _world.Geometry;
        int r = _halfW;
        int h = _halfH;

        // Corner offsets of a flat-top hex, clockwise from the east corner.
        (int X, int Y)[] corner = [(r, 0), (r / 2, h), (-r / 2, h), (-r, 0), (-r / 2, -h), (r / 2, -h)];

        // Canonical sides N, NE, SE and the corners they join.
        (int A, int B)[] side = [(4, 5), (5, 0), (0, 1)];
        for (int i = 0; i < geo.CellCount; i++)
        {
            HexCoord c = geo.FromIndex(i);
            (int cx, int cy) = Center(c);
            for (int s = 0; s < HexEdge.PerCell; s++)
            {
                int size = _world.Map.River[(i * HexEdge.PerCell) + s];
                if (size == 0)
                {
                    continue;
                }

                (int ax, int ay) = corner[side[s].A];
                (int bx, int by) = corner[side[s].B];
                DrawLine(image, cx + ax, cy + ay, cx + bx, cy + by, size >= 3 ? 3 : size >= 2 ? 2 : 1, RiverColor);
            }
        }
    }

    private static void DrawLine(RgbImage image, int x0, int y0, int x1, int y1, int thickness, uint color)
    {
        int dx = Math.Abs(x1 - x0);
        int dy = -Math.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            FillRect(image, x0 - (thickness / 2), y0 - (thickness / 2), thickness + 1, thickness + 1, color);
            if (x0 == x1 && y0 == y1)
            {
                return;
            }

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static void FillRect(RgbImage image, int x, int y, int w, int h, uint color)
    {
        for (int yy = y; yy < y + h; yy++)
        {
            for (int xx = x; xx < x + w; xx++)
            {
                image.Set(xx, yy, color);
            }
        }
    }

    private static uint Lerp(uint a, uint b, int t, int max)
    {
        t = Math.Clamp(t, 0, max);
        uint Channel(int shift)
        {
            int ca = (int)((a >> shift) & 0xFF);
            int cb = (int)((b >> shift) & 0xFF);
            return (uint)(ca + ((cb - ca) * t / Math.Max(1, max))) << shift;
        }

        return Channel(16) | Channel(8) | Channel(0);
    }

    private static uint Blend(uint a, uint b, int percentB) => Lerp(a, b, percentB, 100);

    private static uint Darken(uint c, int percent) => Lerp(c, 0x000000, percent, 100);
}
