using System;
using System.Text;

namespace CivSim.Core.Hashing;

/// <summary>
/// Platform-independent 64-bit FNV-1a hash builder. Values are fed in little-endian order, strings as UTF-8.
/// Used for determinism tests and desync checks; not for security.
/// </summary>
public struct StateHasher
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _hash;
    private bool _started;

    /// <summary>The hash of everything added so far.</summary>
    public readonly ulong Value => _started ? _hash : OffsetBasis;

    /// <summary>Adds one byte.</summary>
    public void Add(byte value)
    {
        if (!_started)
        {
            _hash = OffsetBasis;
            _started = true;
        }

        _hash = (_hash ^ value) * Prime;
    }

    /// <summary>Adds a 64-bit value.</summary>
    public void Add(ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            Add((byte)(value >> (8 * i)));
        }
    }

    /// <summary>Adds a 64-bit value.</summary>
    public void Add(long value) => Add((ulong)value);

    /// <summary>Adds a 32-bit value.</summary>
    public void Add(int value)
    {
        uint v = (uint)value;
        for (int i = 0; i < 4; i++)
        {
            Add((byte)(v >> (8 * i)));
        }
    }

    /// <summary>Adds a boolean.</summary>
    public void Add(bool value) => Add(value ? (byte)1 : (byte)0);

    /// <summary>Adds a string (length-prefixed UTF-8, so "ab"+"c" differs from "a"+"bc").</summary>
    public void Add(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        int byteCount = Encoding.UTF8.GetByteCount(value);
        Add(byteCount);
        Span<byte> buffer = byteCount <= 256 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(value, buffer);
        foreach (byte b in buffer)
        {
            Add(b);
        }
    }
}
