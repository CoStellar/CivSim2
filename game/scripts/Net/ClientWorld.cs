using System;
using CivSim.Protocol.Messages;

namespace CivSim.Game.Net;

/// <summary>
/// The client's copy of one world map, built only from <see cref="MapChunkMessage"/>s the server sent.
/// Same cell layout as the server: odd-q columns, index = row * width + column.
/// </summary>
public sealed class ClientWorld
{
    /// <summary>Chunk size in cells (matches the server).</summary>
    public const int ChunkSize = 32;

    /// <summary>Creates an empty (unknown) world.</summary>
    public ClientWorld(int id, string name, int width, int height)
    {
        Id = id;
        Name = name;
        Width = width;
        Height = height;
        int n = width * height;
        Visibility = new byte[n];
        Elevation = new byte[n];
        Temperature = new sbyte[n];
        Humidity = new byte[n];
        Biome = new byte[n];
        Relief = new byte[n];
        Feature = new byte[n];
        Fertility = new byte[n];
        Deposit = new ushort[n];
        DepositRichness = new int[n];
        DepositDepth = new byte[n];
        Owner = new short[n];
        Array.Fill(Owner, (short)-1);
        WildFood = new ushort[n];
        Influence = new byte[n];
        River = new byte[n * MapChunkMessage.RiverEdgesPerCell];
        ChunksX = (width + ChunkSize - 1) / ChunkSize;
        ChunksY = (height + ChunkSize - 1) / ChunkSize;
        DirtyChunks = new bool[ChunksX * ChunksY];
    }

    /// <summary>World id (0 = Earth).</summary>
    public int Id { get; }

    /// <summary>World name.</summary>
    public string Name { get; }

    /// <summary>Columns.</summary>
    public int Width { get; }

    /// <summary>Rows.</summary>
    public int Height { get; }

    /// <summary>Chunks across.</summary>
    public int ChunksX { get; }

    /// <summary>Chunks down.</summary>
    public int ChunksY { get; }

    /// <summary>Chunks whose data changed since the renderer last looked.</summary>
    public bool[] DirtyChunks { get; }

    /// <summary>Per-cell visibility (see <see cref="MapChunkMessage.Visible"/>).</summary>
    public byte[] Visibility { get; }

    /// <summary>Elevation 0–255 (sea level 100).</summary>
    public byte[] Elevation { get; }

    /// <summary>Temperature in °C.</summary>
    public sbyte[] Temperature { get; }

    /// <summary>Humidity 0–100.</summary>
    public byte[] Humidity { get; }

    /// <summary>Biome id.</summary>
    public byte[] Biome { get; }

    /// <summary>Relief (0 flat, 1 hills, 2 mountains).</summary>
    public byte[] Relief { get; }

    /// <summary>Terrain feature id.</summary>
    public byte[] Feature { get; }

    /// <summary>Fertility 0–100.</summary>
    public byte[] Fertility { get; }

    /// <summary>Deposit id (0 = none).</summary>
    public ushort[] Deposit { get; }

    /// <summary>Deposit richness in whole units.</summary>
    public int[] DepositRichness { get; }

    /// <summary>Deposit depth (0 visible, 1 shallow, 2 deep).</summary>
    public byte[] DepositDepth { get; }

    /// <summary>Owner civilization id or -1.</summary>
    public short[] Owner { get; }

    /// <summary>Wild food left, per mille of an untouched cell.</summary>
    public ushort[] WildFood { get; }

    /// <summary>Influence strength of the owner, 0–255.</summary>
    public byte[] Influence { get; }

    /// <summary>River size on the N, NE and SE edge of each cell.</summary>
    public byte[] River { get; }

    /// <summary>Cell index of a column and row (wrapped).</summary>
    public int Index(int col, int row) => (Mod(row, Height) * Width) + Mod(col, Width);

    /// <summary>Copies a chunk message into the cache.</summary>
    public void Apply(MapChunkMessage m)
    {
        ArgumentNullException.ThrowIfNull(m);
        for (int r = 0; r < m.Height; r++)
        {
            for (int c = 0; c < m.Width; c++)
            {
                int k = (r * m.Width) + c;
                int i = Index(m.X0 + c, m.Y0 + r);
                Visibility[i] = m.Visibility[k];
                Elevation[i] = m.Elevation[k];
                Temperature[i] = m.Temperature[k];
                Humidity[i] = m.Humidity[k];
                Biome[i] = m.Biome[k];
                Relief[i] = m.Relief[k];
                Feature[i] = m.Feature[k];
                Fertility[i] = m.Fertility[k];
                Deposit[i] = m.Deposit[k];
                DepositRichness[i] = m.DepositRichness[k];
                DepositDepth[i] = m.DepositDepth[k];
                Owner[i] = m.Owner[k];
                WildFood[i] = m.WildFood is { } wild ? wild[k] : (ushort)1000;
                Influence[i] = m.Influence is { } inf ? inf[k] : (byte)0;
                for (int e = 0; e < MapChunkMessage.RiverEdgesPerCell; e++)
                {
                    River[(i * MapChunkMessage.RiverEdgesPerCell) + e] = m.River[(k * MapChunkMessage.RiverEdgesPerCell) + e];
                }
            }
        }

        if (m.ChunkIndex >= 0 && m.ChunkIndex < DirtyChunks.Length)
        {
            DirtyChunks[m.ChunkIndex] = true;
        }
    }

    private static int Mod(int a, int m)
    {
        int r = a % m;
        return r < 0 ? r + m : r;
    }
}
