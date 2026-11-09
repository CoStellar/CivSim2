using System;

namespace CivSim.Core.Numerics;

/// <summary>
/// Base-2 logarithm and power on integers in millionths, identical on every platform (CLAUDE.md rule 4). Used by the
/// climate (greenhouse warming grows with every doubling of CO₂, rock weathering doubles every 10 °C).
/// </summary>
public static class FixedLog
{
    /// <summary>Fixed-point one: values are in millionths.</summary>
    public const long Unit = 1_000_000;

    private const int FractionBits = 60;
    private static readonly Int128 OneQ = (Int128)1 << FractionBits;

    // 2^(1/2^k) for k = 1..40 in Q60, by repeated exact integer square roots of 2.
    private static readonly Int128[] Roots = BuildRoots();

    /// <summary>log₂(value / <see cref="Unit"/>) in millionths. <paramref name="valueMillionths"/> must be positive.</summary>
    public static long Log2(long valueMillionths)
    {
        if (valueMillionths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(valueMillionths), "The logarithm needs a positive value.");
        }

        // Integer part: shift the value into [1, 2) in Q60.
        Int128 y = ((Int128)valueMillionths << FractionBits) / Unit;
        long whole = 0;
        while (y >= 2 * OneQ)
        {
            y >>= 1;
            whole++;
        }

        while (y < OneQ)
        {
            y <<= 1;
            whole--;
        }

        // Fraction: square repeatedly; each time the square reaches 2, the next binary digit is 1.
        long fraction = 0;
        long bit = Unit;
        for (int k = 0; k < 40; k++)
        {
            y = (y * y) >> FractionBits;
            bit /= 2;
            if (y >= 2 * OneQ)
            {
                y >>= 1;
                fraction += bit;
            }

            if (bit == 0)
            {
                break;
            }
        }

        return (whole * Unit) + fraction;
    }

    /// <summary>2^(x / <see cref="Unit"/>) in millionths (saturates instead of overflowing).</summary>
    public static long Pow2(long xMillionths)
    {
        long whole = Math.DivRem(xMillionths, Unit, out long frac);
        if (frac < 0)
        {
            frac += Unit;
            whole--;
        }

        if (whole > 40)
        {
            return long.MaxValue / 2;
        }

        if (whole < -40)
        {
            return 0;
        }

        // Fraction in binary digits: multiply by 2^(1/2^k) for every 1 digit.
        Int128 r = OneQ;
        Int128 f = ((Int128)frac << FractionBits) / Unit;
        for (int k = 0; k < Roots.Length; k++)
        {
            Int128 digit = (Int128)1 << (FractionBits - 1 - k);
            if (f >= digit)
            {
                f -= digit;
                r = (r * Roots[k]) >> FractionBits;
            }
        }

        Int128 scaled = r * Unit;
        scaled = whole >= 0 ? scaled << (int)whole : scaled >> (int)-whole;
        return (long)((scaled + (OneQ / 2)) >> FractionBits);
    }

    private static Int128[] BuildRoots()
    {
        var roots = new Int128[40];
        Int128 v = 2 * OneQ;
        for (int k = 0; k < roots.Length; k++)
        {
            v = IntMath.Sqrt(v << FractionBits); // √(v) in Q60
            roots[k] = v;
        }

        return roots;
    }
}
