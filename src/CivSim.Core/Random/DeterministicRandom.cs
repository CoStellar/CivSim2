using System;

namespace CivSim.Core.Random;

/// <summary>
/// Deterministic pseudo-random generator (xoshiro256**). Gives identical sequences on every
/// platform for the same seed. Game logic must use this type instead of <c>System.Random</c>.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    /// <summary>Creates a generator whose state is expanded from <paramref name="seed"/> with SplitMix64.</summary>
    public DeterministicRandom(ulong seed)
    {
        Reseed(seed);
    }

    /// <summary>Creates a generator from an exact saved state.</summary>
    public DeterministicRandom(RandomState state)
    {
        SetState(state);
    }

    /// <summary>Current state, for saving and state hashing.</summary>
    public RandomState State => new(_s0, _s1, _s2, _s3);

    /// <summary>Resets the generator as if it had just been created with <paramref name="seed"/>.</summary>
    public void Reseed(ulong seed)
    {
        ulong sm = seed;
        _s0 = SplitMix64.Next(ref sm);
        _s1 = SplitMix64.Next(ref sm);
        _s2 = SplitMix64.Next(ref sm);
        _s3 = SplitMix64.Next(ref sm);
    }

    /// <summary>Restores an exact state (e.g. after loading a save).</summary>
    public void SetState(RandomState state)
    {
        if (state.IsZero)
        {
            throw new ArgumentException("An all-zero state is not valid for xoshiro256**.", nameof(state));
        }

        _s0 = state.S0;
        _s1 = state.S1;
        _s2 = state.S2;
        _s3 = state.S3;
    }

    /// <summary>Returns the next 64 random bits.</summary>
    public ulong NextULong()
    {
        ulong result = RotateLeft(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = RotateLeft(_s3, 45);

        return result;
    }

    /// <summary>Returns the next 32 random bits (the high half of a 64-bit output).</summary>
    public uint NextUInt()
    {
        return (uint)(NextULong() >> 32);
    }

    /// <summary>Returns a uniform integer in [0, <paramref name="maxExclusive"/>). Unbiased.</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be positive.");
        }

        return (int)NextBounded((uint)maxExclusive);
    }

    /// <summary>Returns a uniform integer in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>). Unbiased.</summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be greater than minInclusive.");
        }

        uint range = (uint)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + NextBounded(range));
    }

    /// <summary>Returns true with probability <paramref name="numerator"/> / <paramref name="denominator"/>.</summary>
    public bool Chance(int numerator, int denominator)
    {
        if (denominator <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(denominator), "Must be positive.");
        }

        if (numerator <= 0)
        {
            return false;
        }

        if (numerator >= denominator)
        {
            return true;
        }

        return NextInt(denominator) < numerator;
    }

    /// <summary>Returns true with probability <paramref name="percent"/>% (0–100).</summary>
    public bool ChancePercent(int percent)
    {
        return Chance(percent, 100);
    }

    /// <summary>Returns a random boolean.</summary>
    public bool NextBool()
    {
        return (NextULong() >> 63) != 0;
    }

    /// <summary>Shuffles <paramref name="items"/> in place (Fisher–Yates).</summary>
    public void Shuffle<T>(Span<T> items)
    {
        for (int i = items.Length - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    // Lemire's nearly-divisionless method with rejection: exact uniform distribution.
    private uint NextBounded(uint range)
    {
        ulong m = (ulong)NextUInt() * range;
        uint low = (uint)m;
        if (low < range)
        {
            uint threshold = (uint)(-range) % range;
            while (low < threshold)
            {
                m = (ulong)NextUInt() * range;
                low = (uint)m;
            }
        }

        return (uint)(m >> 32);
    }

    private static ulong RotateLeft(ulong x, int k)
    {
        return (x << k) | (x >> (64 - k));
    }
}
