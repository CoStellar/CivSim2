using System;

namespace CivSim.Core.Numerics;

/// <summary>
/// Trigonometry on integers, identical on every platform (CLAUDE.md rule 4: no floating point in game logic).
/// Angles are in <see cref="FullTurn"/> parts of a full circle; sines and cosines are in parts of <see cref="One"/>.
/// The quarter-wave table is built once from an exact integer Taylor series.
/// </summary>
public static class FixedTrig
{
    /// <summary>Fixed-point one for sines, cosines and ratios.</summary>
    public const long One = 1 << 20;

    /// <summary>A full circle (360°).</summary>
    public const int FullTurn = 1 << 16;

    /// <summary>A quarter circle (90°).</summary>
    public const int QuarterTurn = FullTurn / 4;

    /// <summary>π in parts of <see cref="One"/>.</summary>
    public const long Pi = 3_294_199; // round(π × 2^20)

    private static readonly long[] Quarter = BuildQuarter();

    /// <summary>Angle for whole degrees (may be negative or above 360).</summary>
    public static int Degrees(long degrees) => (int)(degrees * FullTurn / 360);

    /// <summary>Angle for tenths of a degree.</summary>
    public static int TenthsOfDegree(long tenths) => (int)(tenths * FullTurn / 3600);

    /// <summary>Angle in radians, scaled by <see cref="One"/>.</summary>
    public static long ToRadians(int angle) => (long)angle * 2 * Pi / FullTurn;

    /// <summary>Sine of <paramref name="angle"/>.</summary>
    public static long Sin(int angle)
    {
        int a = angle & (FullTurn - 1);
        int quadrant = a / QuarterTurn;
        int rest = a % QuarterTurn;
        return quadrant switch
        {
            0 => Quarter[rest],
            1 => Quarter[QuarterTurn - rest],
            2 => -Quarter[rest],
            _ => -Quarter[QuarterTurn - rest],
        };
    }

    /// <summary>Cosine of <paramref name="angle"/>.</summary>
    public static long Cos(int angle) => Sin(angle + QuarterTurn);

    /// <summary>Arcsine (−quarter turn…quarter turn) of a value in parts of <see cref="One"/>; clamped to ±1.</summary>
    public static int Asin(long value)
    {
        long v = Math.Clamp(value, -One, One);
        bool negative = v < 0;
        v = Math.Abs(v);
        if (v == One)
        {
            return negative ? -QuarterTurn : QuarterTurn;
        }

        // Near 90° many table entries round to the same value: take the middle of the run of equal entries.
        int first = FirstAtLeast(v);
        int last = FirstAtLeast(v + 1) - 1;
        int angle = last >= first ? (first + last + 1) / 2 : first;
        return negative ? -angle : angle;
    }

    // First table index whose sine is at least `value` (QuarterTurn + 1 when none is).
    private static int FirstAtLeast(long value)
    {
        int lo = 0;
        int hi = QuarterTurn + 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >>> 1;
            if (Quarter[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    /// <summary>Arccosine (0…half turn) of a value in parts of <see cref="One"/>; clamped to ±1.</summary>
    public static int Acos(long value) => QuarterTurn - Asin(value);

    /// <summary>Square root of 1 − x², the cosine for a known sine (both in parts of <see cref="One"/>).</summary>
    public static long Complement(long sine)
    {
        long s = Math.Clamp(sine, -One, One);
        return IntMath.Sqrt((One * One) - (s * s));
    }

    // sin(k/Q × π/2) for k = 0..Q, by a Taylor series in 2^60 fixed point, rounded to One.
    private static long[] BuildQuarter()
    {
        const int Shift = 60;
        Int128 scale = Int128.One << Shift;
        Int128 halfPi = (Int128)1_811_004_864_519_280_711L; // round(π/2 × 2^60)
        var table = new long[QuarterTurn + 1];
        for (int k = 0; k <= QuarterTurn; k++)
        {
            Int128 x = halfPi * k / QuarterTurn;
            Int128 x2 = x * x / scale;
            Int128 term = x;
            Int128 sum = x;
            for (int n = 1; n < 14; n++)
            {
                term = -term * x2 / scale / ((2 * n) * ((2 * n) + 1));
                sum += term;
            }

            table[k] = (long)((sum + (scale / One / 2)) / (scale / One));
        }

        table[QuarterTurn] = One;
        return table;
    }
}
