using System;

namespace CivSim.Core.Numerics;

/// <summary>Exact integer helpers with explicit rounding. No floating point.</summary>
public static class IntMath
{
    /// <summary>
    /// Computes <paramref name="value"/> × <paramref name="numerator"/> / <paramref name="denominator"/>
    /// without intermediate overflow, rounded with <paramref name="rounding"/>.
    /// </summary>
    /// <exception cref="DivideByZeroException">When <paramref name="denominator"/> is zero.</exception>
    /// <exception cref="OverflowException">When the result does not fit in a <see cref="long"/>.</exception>
    public static long MulDiv(long value, long numerator, long denominator, Rounding rounding = Rounding.HalfAwayFromZero)
    {
        if (denominator == 0)
        {
            throw new DivideByZeroException();
        }

        Int128 product = (Int128)value * numerator;
        return checked((long)Divide(product, denominator, rounding));
    }

    /// <summary>Divides <paramref name="dividend"/> by <paramref name="divisor"/> with the given rounding.</summary>
    public static long Divide(long dividend, long divisor, Rounding rounding = Rounding.HalfAwayFromZero)
    {
        if (divisor == 0)
        {
            throw new DivideByZeroException();
        }

        return checked((long)Divide((Int128)dividend, divisor, rounding));
    }

    private static Int128 Divide(Int128 dividend, Int128 divisor, Rounding rounding)
    {
        Int128 quotient = dividend / divisor; // truncates toward zero
        Int128 remainder = dividend % divisor;
        if (remainder == 0)
        {
            return quotient;
        }

        bool negative = (dividend < 0) ^ (divisor < 0);
        switch (rounding)
        {
            case Rounding.TowardZero:
                return quotient;
            case Rounding.Floor:
                return negative ? quotient - 1 : quotient;
            case Rounding.Ceiling:
                return negative ? quotient : quotient + 1;
            case Rounding.HalfAwayFromZero:
                Int128 twiceRemainder = Int128.Abs(remainder) * 2;
                if (twiceRemainder >= Int128.Abs(divisor))
                {
                    return negative ? quotient - 1 : quotient + 1;
                }

                return quotient;
            default:
                throw new ArgumentOutOfRangeException(nameof(rounding));
        }
    }
}
