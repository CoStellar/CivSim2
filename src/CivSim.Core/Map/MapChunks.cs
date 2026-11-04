using System;

namespace CivSim.Core.Map;

/// <summary>
/// Splits a map into square chunks of <see cref="Size"/>×<see cref="Size"/> cells (GDD 3). Chunks on the right
/// and bottom border may be smaller when the map size is not a multiple of 32. Chunk ids are row-major.
/// </summary>
public sealed class MapChunks
{
    /// <summary>Chunk side in cells.</summary>
    public const int Size = 32;

    /// <summary>Creates the chunk grid for <paramref name="geometry"/>.</summary>
    public MapChunks(HexTorus geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Geometry = geometry;
        ChunksX = (geometry.Width + Size - 1) / Size;
        ChunksY = (geometry.Height + Size - 1) / Size;
    }

    /// <summary>The map geometry.</summary>
    public HexTorus Geometry { get; }

    /// <summary>Chunks per row.</summary>
    public int ChunksX { get; }

    /// <summary>Chunk rows.</summary>
    public int ChunksY { get; }

    /// <summary>Total number of chunks.</summary>
    public int Count => ChunksX * ChunksY;

    /// <summary>Chunk id of a cell.</summary>
    public int ChunkOf(HexCoord cell) => ((cell.Row / Size) * ChunksX) + (cell.Col / Size);

    /// <summary>Chunk id of a cell given by its flat index.</summary>
    public int ChunkOfIndex(int cellIndex)
    {
        int width = Geometry.Width;
        return (((cellIndex / width) / Size) * ChunksX) + ((cellIndex % width) / Size);
    }

    /// <summary>First column, first row, width and height (in cells) of a chunk.</summary>
    public (int Col, int Row, int Width, int Height) Bounds(int chunk)
    {
        if ((uint)chunk >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(chunk));
        }

        int col = (chunk % ChunksX) * Size;
        int row = (chunk / ChunksX) * Size;
        return (col, row, Math.Min(Size, Geometry.Width - col), Math.Min(Size, Geometry.Height - row));
    }
}
