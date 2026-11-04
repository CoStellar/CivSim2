namespace CivSim.Core.Random;

/// <summary>
/// SplitMix64 mixer. Used only to expand seeds into generator state; never for gameplay rolls.
/// </summary>
public static class SplitMix64
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    /// <summary>Advances <paramref name="state"/> and returns the next mixed value.</summary>
    public static ulong Next(ref ulong state)
    {
        state += GoldenGamma;
        return Mix(state);
    }

    /// <summary>Stateless finalizer: maps any 64-bit value to a well-mixed 64-bit value.</summary>
    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Combines several keys into one seed in an order-sensitive, platform-independent way.</summary>
    public static ulong Combine(ulong a, ulong b)
    {
        return Mix(a ^ Mix(b + GoldenGamma));
    }
}
