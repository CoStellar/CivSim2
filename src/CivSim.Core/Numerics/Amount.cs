using System;
using System.Globalization;

namespace CivSim.Core.Numerics;

/// <summary>
/// A quantity of a resource (or any economic value) stored as a whole number of hundredths.
/// <c>Amount.FromUnits(3)</c> is 3.00, <c>Amount.FromHundredths(125)</c> is 1.25.
/// Arithmetic is exact and overflow-checked; division always names its rounding.
/// </summary>
public readonly struct Amount : IEquatable<Amount>, IComparable<Amount>
{
    /// <summary>Hundredths per whole unit.</summary>
    public const long Scale = 100;

    private Amount(long hundredths)
    {
        Hundredths = hundredths;
    }

    /// <summary>Zero.</summary>
    public static Amount Zero => default;

    /// <summary>The smallest positive amount (0.01).</summary>
    public static Amount Epsilon => new(1);

    /// <summary>Largest representable amount.</summary>
    public static Amount MaxValue => new(long.MaxValue);

    /// <summary>Raw value in hundredths of a unit.</summary>
    public long Hundredths { get; }

    /// <summary>Whole units, with the fraction dropped toward zero.</summary>
    public long WholeUnits => Hundredths / Scale;

    /// <summary>True when the amount is below zero.</summary>
    public bool IsNegative => Hundredths < 0;

    /// <summary>Creates an amount from whole units.</summary>
    public static Amount FromUnits(long units)
    {
        return new Amount(checked(units * Scale));
    }

    /// <summary>Creates an amount from hundredths.</summary>
    public static Amount FromHundredths(long hundredths)
    {
        return new Amount(hundredths);
    }

    /// <summary>
    /// Parses a decimal string with at most two fractional digits, e.g. "12", "-0.5", "3.25".
    /// Used by data files; never culture-dependent.
    /// </summary>
    public static Amount Parse(string text)
    {
        if (!TryParse(text, out Amount value))
        {
            throw new FormatException($"'{text}' is not a valid amount (at most two decimal places).");
        }

        return value;
    }

    /// <summary>Tries to parse a decimal string with at most two fractional digits.</summary>
    public static bool TryParse(string? text, out Amount value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ReadOnlySpan<char> s = text.AsSpan().Trim();
        bool negative = false;
        if (s[0] is '-' or '+')
        {
            negative = s[0] == '-';
            s = s[1..];
        }

        int dot = s.IndexOf('.');
        ReadOnlySpan<char> whole = dot < 0 ? s : s[..dot];
        ReadOnlySpan<char> fraction = dot < 0 ? ReadOnlySpan<char>.Empty : s[(dot + 1)..];
        if (whole.IsEmpty || fraction.Length > 2 || (dot >= 0 && fraction.IsEmpty))
        {
            return false;
        }

        if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out long units))
        {
            return false;
        }

        long hundredths = 0;
        if (!fraction.IsEmpty)
        {
            if (!long.TryParse(fraction, NumberStyles.None, CultureInfo.InvariantCulture, out hundredths))
            {
                return false;
            }

            if (fraction.Length == 1)
            {
                hundredths *= 10;
            }
        }

        try
        {
            long raw = checked((units * Scale) + hundredths);
            value = new Amount(negative ? -raw : raw);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Adds two amounts.</summary>
    public static Amount operator +(Amount a, Amount b) => new(checked(a.Hundredths + b.Hundredths));

    /// <summary>Subtracts two amounts.</summary>
    public static Amount operator -(Amount a, Amount b) => new(checked(a.Hundredths - b.Hundredths));

    /// <summary>Negates an amount.</summary>
    public static Amount operator -(Amount a) => new(checked(-a.Hundredths));

    /// <summary>Multiplies by a whole number.</summary>
    public static Amount operator *(Amount a, long factor) => new(checked(a.Hundredths * factor));

    /// <summary>Multiplies by a whole number.</summary>
    public static Amount operator *(long factor, Amount a) => a * factor;

    /// <summary>Compares two amounts.</summary>
    public static bool operator ==(Amount a, Amount b) => a.Hundredths == b.Hundredths;

    /// <summary>Compares two amounts.</summary>
    public static bool operator !=(Amount a, Amount b) => a.Hundredths != b.Hundredths;

    /// <summary>Compares two amounts.</summary>
    public static bool operator <(Amount a, Amount b) => a.Hundredths < b.Hundredths;

    /// <summary>Compares two amounts.</summary>
    public static bool operator >(Amount a, Amount b) => a.Hundredths > b.Hundredths;

    /// <summary>Compares two amounts.</summary>
    public static bool operator <=(Amount a, Amount b) => a.Hundredths <= b.Hundredths;

    /// <summary>Compares two amounts.</summary>
    public static bool operator >=(Amount a, Amount b) => a.Hundredths >= b.Hundredths;

    /// <summary>The smaller of two amounts.</summary>
    public static Amount Min(Amount a, Amount b) => a <= b ? a : b;

    /// <summary>The larger of two amounts.</summary>
    public static Amount Max(Amount a, Amount b) => a >= b ? a : b;

    /// <summary>Limits the amount to [<paramref name="min"/>, <paramref name="max"/>].</summary>
    public Amount Clamp(Amount min, Amount max)
    {
        if (min > max)
        {
            throw new ArgumentException("min must not exceed max.");
        }

        return Max(min, Min(max, this));
    }

    /// <summary>Multiplies by the fraction <paramref name="numerator"/>/<paramref name="denominator"/>.</summary>
    public Amount MultiplyRatio(long numerator, long denominator, Rounding rounding = Rounding.HalfAwayFromZero)
    {
        return new Amount(IntMath.MulDiv(Hundredths, numerator, denominator, rounding));
    }

    /// <summary>Multiplies by a percentage, e.g. 40 for 40%.</summary>
    public Amount MultiplyPercent(long percent, Rounding rounding = Rounding.HalfAwayFromZero)
    {
        return MultiplyRatio(percent, 100, rounding);
    }

    /// <summary>Multiplies two amounts (e.g. price × quantity). Result keeps two decimals.</summary>
    public Amount Multiply(Amount other, Rounding rounding = Rounding.HalfAwayFromZero)
    {
        return new Amount(IntMath.MulDiv(Hundredths, other.Hundredths, Scale, rounding));
    }

    /// <summary>Divides by a whole number.</summary>
    public Amount Divide(long divisor, Rounding rounding = Rounding.HalfAwayFromZero)
    {
        return new Amount(IntMath.Divide(Hundredths, divisor, rounding));
    }

    /// <summary>Divides by another amount, giving an amount (e.g. 3.00 / 2.00 = 1.50).</summary>
    public Amount Divide(Amount divisor, Rounding rounding = Rounding.HalfAwayFromZero)
    {
        return new Amount(IntMath.MulDiv(Hundredths, Scale, divisor.Hundredths, rounding));
    }

    /// <summary>
    /// Splits this amount into <paramref name="shares"/>.Length parts proportional to the weights,
    /// so that the parts always add up exactly to this amount (largest-remainder method, ties to the lowest index).
    /// </summary>
    public void SplitProportionally(ReadOnlySpan<long> weights, Span<Amount> shares)
    {
        if (shares.Length != weights.Length)
        {
            throw new ArgumentException("weights and shares must have the same length.");
        }

        if (IsNegative)
        {
            throw new InvalidOperationException("Cannot split a negative amount.");
        }

        long totalWeight = 0;
        foreach (long w in weights)
        {
            if (w < 0)
            {
                throw new ArgumentException("Weights must not be negative.", nameof(weights));
            }

            totalWeight = checked(totalWeight + w);
        }

        if (totalWeight == 0)
        {
            shares.Clear();
            return;
        }

        long assigned = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            long part = IntMath.MulDiv(Hundredths, weights[i], totalWeight, Rounding.Floor);
            shares[i] = new Amount(part);
            assigned += part;
        }

        long left = Hundredths - assigned;
        while (left > 0)
        {
            int best = -1;
            Int128 bestRemainder = -1;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] == 0)
                {
                    continue;
                }

                Int128 remainder = ((Int128)Hundredths * weights[i]) - ((Int128)shares[i].Hundredths * totalWeight);
                if (remainder > bestRemainder)
                {
                    bestRemainder = remainder;
                    best = i;
                }
            }

            shares[best] = new Amount(shares[best].Hundredths + 1);
            left--;
        }
    }

    /// <inheritdoc />
    public bool Equals(Amount other) => Hundredths == other.Hundredths;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Amount other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Hundredths.GetHashCode();

    /// <inheritdoc />
    public int CompareTo(Amount other) => Hundredths.CompareTo(other.Hundredths);

    /// <summary>Formats as a culture-invariant decimal with two places, e.g. "-3.05".</summary>
    public override string ToString()
    {
        long abs = Math.Abs(Hundredths);
        string sign = Hundredths < 0 ? "-" : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}{abs / Scale}.{abs % Scale:D2}");
    }
}
