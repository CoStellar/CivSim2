using System;
using CivSim.Core.Numerics;

namespace CivSim.Core.WorldGen;

/// <summary>A quantity of the star and orbit that a player or a preset can set (stage 1c-A, decision of 2026-11-05).</summary>
public enum OrbitQuantity
{
    /// <summary>Brightness of the star, in Suns.</summary>
    Luminosity,

    /// <summary>Distance of the planet from its star, in astronomical units (Earth 1).</summary>
    Distance,

    /// <summary>Length of the planet's year, in Earth years.</summary>
    Year,

    /// <summary>Sunlight reaching the planet compared with Earth (S = L / d²).</summary>
    Light,
}

/// <summary>
/// A star and the planet's distance from it, with everything that follows from them on the real formulas (decision of
/// 2026-11-05): light S = L / d², star mass from its brightness on the main sequence (L = M⁴ up to 2 Suns, L = 1.4·M³·⁵
/// above), the year from Kepler's third law (P² = d³ / M) and the habitable zone of Kopparapu et al. (2013) for a
/// Sun-like star: light from <see cref="ZoneOuterMillionths"/> (maximum greenhouse) to <see cref="ZoneInnerMillionths"/>
/// (runaway greenhouse).
/// <para>
/// All arithmetic is on integers in millionths (<see cref="Unit"/>) with 128-bit products, so every platform gets the
/// same results (CLAUDE.md rule 4). Stored values are thousandths of a Sun and of an astronomical unit.
/// </para>
/// </summary>
public static class StarOrbit
{
    /// <summary>Fixed-point one: values are in millionths.</summary>
    public const long Unit = 1_000_000;

    /// <summary>Light at the outer edge of the habitable zone (maximum CO₂ greenhouse), millionths of Earth's.</summary>
    public const long ZoneOuterMillionths = 356_000;

    /// <summary>Light at the inner edge of the habitable zone (runaway greenhouse), millionths of Earth's.</summary>
    public const long ZoneInnerMillionths = 1_107_000;

    /// <summary>Smallest and largest star brightness, thousandths of a Sun (0.04–7000 Suns: years stay within 0.1–500).</summary>
    public static (int Min, int Max) LuminosityRange => (40, 7_000_000);

    /// <summary>
    /// Solves the star and the distance from any two different quantities (decision of 2026-11-05: any two of brightness,
    /// distance and year give the third; light can stand in for one of them). Values are in thousandths (years too).
    /// <paramref name="first"/> is the value the player just changed, <paramref name="kept"/> the one that stays. When
    /// the result would leave the habitable zone, the planet is put on the nearest edge of the zone and only
    /// <paramref name="kept"/> is honoured: the changed value goes as far as the zone allows.
    /// </summary>
    /// <returns>Brightness and distance in thousandths, and whether the planet had to be moved to the edge of the zone.</returns>
    public static (int LuminosityThousandths, int DistanceThousandths, bool Clamped) Solve(
        OrbitQuantity first, long firstThousandths, OrbitQuantity kept, long keptThousandths)
    {
        if (first == kept)
        {
            throw new ArgumentException("Two different quantities are needed to solve the orbit.", nameof(kept));
        }

        if (firstThousandths <= 0 || keptThousandths <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstThousandths), "Orbit values must be positive.");
        }

        bool clamped = false;
        long a = firstThousandths * 1000;
        long b = keptThousandths * 1000;
        if (first == OrbitQuantity.Light)
        {
            a = ClampLight(a, ref clamped);
        }

        if (kept == OrbitQuantity.Light)
        {
            b = ClampLight(b, ref clamped);
        }

        (long l, long d) = Pair(first, a, kept, b);
        long light = LightOf(l, d);
        if (light < ZoneOuterMillionths || light > ZoneInnerMillionths || l < LuminosityRange.Min * 1000L || l > LuminosityRange.Max * 1000L)
        {
            clamped = true;
            long edge = light < ZoneOuterMillionths ? ZoneOuterMillionths : light > ZoneInnerMillionths ? ZoneInnerMillionths : light;
            (l, d) = kept == OrbitQuantity.Light ? Pair(first, a, OrbitQuantity.Light, edge) : Pair(kept, b, OrbitQuantity.Light, edge);
            long lMin = LuminosityRange.Min * 1000L;
            long lMax = LuminosityRange.Max * 1000L;
            if (l < lMin || l > lMax)
            {
                // The star itself is out of range: use the closest allowed star at the same light.
                l = Math.Clamp(l, lMin, lMax);
                d = Sqrt(Div(l, edge));
            }
        }

        return ((int)Round(l), (int)Round(d), clamped);
    }

    /// <summary>
    /// The player changed <paramref name="changed"/>; <paramref name="follower"/> adjusts and the remaining one of
    /// brightness, distance and year stays (decision of 2026-11-05). Outside the zone the planet goes to its edge.
    /// </summary>
    public static (int LuminosityThousandths, int DistanceThousandths, bool Clamped) Adjust(
        int luminosityThousandths, int distanceThousandths, OrbitQuantity changed, long valueThousandths, OrbitQuantity follower)
    {
        if (changed == OrbitQuantity.Light || follower == OrbitQuantity.Light || changed == follower)
        {
            throw new ArgumentException("Adjust works on two different ones of brightness, distance and year.");
        }

        OrbitQuantity kept = (OrbitQuantity)(3 - (int)changed - (int)follower);
        long keptValue = kept switch
        {
            OrbitQuantity.Luminosity => luminosityThousandths,
            OrbitQuantity.Distance => distanceThousandths,
            _ => YearThousandths(luminosityThousandths, distanceThousandths),
        };
        return Solve(changed, valueThousandths, kept, keptValue);
    }

    /// <summary>Light on the planet, millionths of Earth's: S = L / d².</summary>
    public static long LightMillionths(int luminosityThousandths, int distanceThousandths) =>
        LightOf(luminosityThousandths * 1000L, distanceThousandths * 1000L);

    /// <summary>Mass of a main-sequence star of this brightness, thousandths of a Sun.</summary>
    public static int MassThousandths(int luminosityThousandths) => (int)Round(MassOf(luminosityThousandths * 1000L));

    /// <summary>Length of the year, thousandths of an Earth year (Kepler: P = √(d³ / M)).</summary>
    public static long YearThousandths(int luminosityThousandths, int distanceThousandths) =>
        Round(YearOf(luminosityThousandths * 1000L, distanceThousandths * 1000L));

    /// <summary>Inner and outer edge of the habitable zone of a star, thousandths of an astronomical unit.</summary>
    public static (int Inner, int Outer) ZoneThousandths(int luminosityThousandths)
    {
        long l = luminosityThousandths * 1000L;
        return ((int)Round(Sqrt(Div(l, ZoneInnerMillionths))), (int)Round(Sqrt(Div(l, ZoneOuterMillionths))));
    }

    /// <summary>Brightness of a main-sequence star of this mass, thousandths of a Sun (for tests and reports).</summary>
    public static int LuminosityThousandths(int massThousandths) => (int)Round(LuminosityOf(massThousandths * 1000L));

    // ---- solving pairs (all values in millionths) ------------------------------------------------------------------

    private static (long L, long D) Pair(OrbitQuantity x, long xv, OrbitQuantity y, long yv)
    {
        if (x > y)
        {
            (x, y) = (y, x);
            (xv, yv) = (yv, xv);
        }

        return (x, y) switch
        {
            (OrbitQuantity.Luminosity, OrbitQuantity.Distance) => (xv, yv),
            (OrbitQuantity.Luminosity, OrbitQuantity.Year) => (xv, Cbrt(Mul(Mul(yv, yv), MassOf(xv)))),
            (OrbitQuantity.Luminosity, OrbitQuantity.Light) => (xv, Sqrt(Div(xv, yv))),
            (OrbitQuantity.Distance, OrbitQuantity.Year) => (LuminosityOf(Div(Mul(Mul(xv, xv), xv), Mul(yv, yv))), xv),
            (OrbitQuantity.Distance, OrbitQuantity.Light) => (Mul(yv, Mul(xv, xv)), xv),
            (OrbitQuantity.Year, OrbitQuantity.Light) => FromYearAndLight(xv, yv),
            _ => throw new ArgumentException("Two different quantities are needed."),
        };
    }

    // For a fixed light the year grows with the star (P² ≈ M⁵ for small stars), so bisection finds the one star.
    private static (long L, long D) FromYearAndLight(long year, long light)
    {
        long lo = LuminosityRange.Min * 1000L / 2;
        long hi = LuminosityRange.Max * 1000L * 2;
        for (int k = 0; k < 64; k++)
        {
            long mid = lo + ((hi - lo) / 2);
            if (YearOf(mid, Sqrt(Div(mid, light))) < year)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (hi, Sqrt(Div(hi, light)));
    }

    private static long ClampLight(long light, ref bool clamped)
    {
        long c = Math.Clamp(light, ZoneOuterMillionths, ZoneInnerMillionths);
        clamped |= c != light;
        return c;
    }

    private static long LightOf(long l, long d) => Div(l, Mul(d, d));

    private static long YearOf(long l, long d) => Sqrt(Div(Mul(Mul(d, d), d), MassOf(l)));

    private static long LuminosityOf(long m)
    {
        long m2 = Mul(m, m);
        return m <= 2 * Unit ? Mul(m2, m2) : Mul(Mul(m2, m), Sqrt(m)) * 14 / 10;
    }

    // Inverse of the mass–luminosity relation by bisection (the two pieces meet at 2 Suns).
    private static long MassOf(long l)
    {
        long lo = Unit / 10;
        long hi = 100 * Unit;
        for (int k = 0; k < 40; k++)
        {
            long mid = lo + ((hi - lo) / 2);
            if (LuminosityOf(mid) < l)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return hi;
    }

    // ---- fixed-point helpers ---------------------------------------------------------------------------------------

    private static long Mul(long a, long b) => (long)((Int128)a * b / Unit);

    private static long Div(long a, long b) => (long)((Int128)a * Unit / b);

    private static long Sqrt(long a) => (long)IntMath.Sqrt((Int128)a * Unit);

    private static long Cbrt(long a)
    {
        Int128 x = (Int128)a * Unit * Unit;
        Int128 lo = 0;
        Int128 hi = 1;
        while (hi * hi * hi <= x)
        {
            hi *= 2;
        }

        while (hi - lo > 1)
        {
            Int128 mid = (lo + hi) / 2;
            if (mid * mid * mid <= x)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (long)lo;
    }


    // Millionths to thousandths, half away from zero.
    private static long Round(long millionths) => (millionths + 500) / 1000;
}
