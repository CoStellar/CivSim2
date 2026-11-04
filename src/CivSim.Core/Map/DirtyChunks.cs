using System;
using System.Collections.Generic;
using System.Numerics;

namespace CivSim.Core.Map;

/// <summary>
/// One bit per chunk: "something in this chunk changed since the last snapshot". Systems recompute only dirty
/// chunks and the viewer redraws only them. Iteration is always in ascending chunk id (deterministic).
/// </summary>
public sealed class DirtyChunks
{
    private readonly ulong[] _bits;

    /// <summary>Creates a set for <paramref name="count"/> chunks, all clean.</summary>
    public DirtyChunks(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Count = count;
        _bits = new ulong[(count + 63) / 64];
    }

    /// <summary>Number of chunks tracked.</summary>
    public int Count { get; }

    /// <summary>True when no chunk is dirty.</summary>
    public bool IsEmpty
    {
        get
        {
            foreach (ulong word in _bits)
            {
                if (word != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Marks one chunk.</summary>
    public void Mark(int chunk)
    {
        if ((uint)chunk >= (uint)Count)
        {
            throw new ArgumentOutOfRangeException(nameof(chunk));
        }

        _bits[chunk >> 6] |= 1UL << (chunk & 63);
    }

    /// <summary>Marks every chunk (e.g. after world generation or loading).</summary>
    public void MarkAll()
    {
        for (int c = 0; c < Count; c++)
        {
            _bits[c >> 6] |= 1UL << (c & 63);
        }
    }

    /// <summary>True when the chunk is dirty.</summary>
    public bool IsDirty(int chunk) => (_bits[chunk >> 6] & (1UL << (chunk & 63))) != 0;

    /// <summary>Number of dirty chunks.</summary>
    public int DirtyCount
    {
        get
        {
            int n = 0;
            foreach (ulong word in _bits)
            {
                n += BitOperations.PopCount(word);
            }

            return n;
        }
    }

    /// <summary>Appends dirty chunk ids in ascending order to <paramref name="destination"/>.</summary>
    public void CopyTo(List<int> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        for (int w = 0; w < _bits.Length; w++)
        {
            ulong word = _bits[w];
            while (word != 0)
            {
                int bit = BitOperations.TrailingZeroCount(word);
                destination.Add((w << 6) + bit);
                word &= word - 1;
            }
        }
    }

    /// <summary>Marks every chunk clean.</summary>
    public void Clear() => Array.Clear(_bits);
}
