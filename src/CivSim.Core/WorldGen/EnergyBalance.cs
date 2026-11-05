using System;
using CivSim.Core.Numerics;

namespace CivSim.Core.WorldGen;

/// <summary>
/// One-dimensional energy balance climate in latitude bands (Budyko–Sellers, North 1975; stage 1c-A, decision of
/// 2026-11-05). In every band of equal area, with x the sine of the latitude:
/// <code>Q·s(x)·(1 − albedo) = A + B·T − d/dx[D(x)·(1 − x²)·dT/dx]</code>
/// Sunlight Q·s(x) comes from <see cref="Insolation"/>; the planet sends heat back to space as A + B·T; winds and sea
/// currents carry heat towards the poles (D). The greenhouse effect lowers A (A = A_Earth − B·(greenhouse − 33 °C)),
/// the air pressure scales D. Ice (annual mean below −10 °C) reflects more sunlight, which is what makes ice caps grow.
/// <para>
/// Calibrated to Earth (ADR 0014): about 27 °C at the equator, 10 °C at 45°, −22 °C at the poles. To get that, the
/// albedo rises a little towards the poles (low sun) and heat moves faster in the tropics (Hadley cells): D is 0.3 at
/// the poles and 1.2 at the equator, about 0.55 on average.
/// </para>
/// <para>
/// Integers only (millionths, 128-bit products), a tridiagonal solve per step and a fixed number of steps, so every
/// platform gets the same climate (CLAUDE.md rule 4).
/// </para>
/// </summary>
public static class EnergyBalance
{
    /// <summary>Latitude bands of equal area (equal steps of the sine of latitude).</summary>
    public const int Bands = 90;

    /// <summary>Fixed-point one: values are in millionths.</summary>
    public const long Unit = 1_000_000;

    /// <summary>Sunlight at Earth's distance, W/m².</summary>
    public const long SolarConstant = 1361;

    /// <summary>Outgoing heat at 0 °C on Earth, millionths of W/m² (North 1975: 203.3).</summary>
    public const long EarthA = 203_300_000;

    /// <summary>Extra outgoing heat per degree, millionths of W/m²/K (North 1975: 2.09).</summary>
    public const long B = 2_090_000;

    /// <summary>Greenhouse warming of Earth, tenths of °C.</summary>
    public const int EarthGreenhouseTenths = 330;

    /// <summary>Annual mean temperature below which a band is ice-covered, °C.</summary>
    public const int IceBelow = -10;

    /// <summary>Steps of the ice–albedo iteration (fixed, for determinism).</summary>
    public const int Iterations = 40;

    private const long PolarTransport = 300_000;     // D at the poles, millionths of W/m²/K
    private const long TropicsBoost = 3;             // D = D_pole × (1 + 3·(1 − x²)²)
    private const long FreeAlbedo = 320_000;         // mean albedo without ice
    private const long AlbedoTowardsPoles = 160_000; // × P2(x) = (3x² − 1) / 2
    private const long IceAlbedo = 620_000;

    /// <summary>
    /// Annual mean temperature of every band, millionths of a degree Celsius, from the south pole (band 0) to the north
    /// pole. The warm state: the iteration starts without ice, so a planet that can keep open water keeps it.
    /// </summary>
    public static long[] Solve(PlanetParameters planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        return Solve(planet.AxialTiltTenths, planet.EccentricityThousandths, planet.LightMillionths, planet.GreenhouseTenths, planet.PressureHundredths);
    }

    /// <summary>Like <see cref="Solve(PlanetParameters)"/>, from the values the climate depends on.</summary>
    public static long[] Solve(int tiltTenths, int eccentricityThousandths, long lightMillionths, int greenhouseTenths, int pressureHundredths)
    {
        long[] forcing = Forcing(tiltTenths, eccentricityThousandths, lightMillionths);
        return Solve(forcing, greenhouseTenths, pressureHundredths);
    }

    /// <summary>Area-weighted mean of band temperatures (all bands have the same area), millionths of °C.</summary>
    public static long GlobalMean(long[] bands)
    {
        ArgumentNullException.ThrowIfNull(bands);
        long sum = 0;
        foreach (long t in bands)
        {
            sum += t;
        }

        return sum / bands.Length;
    }

    /// <summary>Sine of the latitude at the centre of a band, millionths.</summary>
    public static long BandSine(int band) => -Unit + (((2L * band) + 1) * Unit / Bands);

    /// <summary>Sunlight reaching each band before the albedo, millionths of W/m².</summary>
    public static long[] Forcing(int tiltTenths, int eccentricityThousandths, long lightMillionths)
    {
        var forcing = new long[Bands];
        for (int i = 0; i < Bands; i++)
        {
            long sin = BandSine(i) * FixedTrig.One / Unit;
            long q = Insolation.At(sin, tiltTenths, eccentricityThousandths, lightMillionths, []).Mean;
            forcing[i] = SolarConstant * q * Unit / FixedTrig.One;
        }

        return forcing;
    }

    /// <summary>Band temperatures (millionths of °C) for a given sunlight per band (see <see cref="Forcing"/>).</summary>
    public static long[] Solve(long[] forcing, int greenhouseTenths, int pressureHundredths)
    {
        ArgumentNullException.ThrowIfNull(forcing);
        long a = EarthA - (B * (greenhouseTenths - EarthGreenhouseTenths) / 10);
        long pressureScale = (long)IntMath.Sqrt((Int128)pressureHundredths * Unit * Unit / 100); // √(p / 1 bar), millionths

        // Diffusion weight on each of the Bands + 1 band edges: D(x)·(1 − x²) / dx², zero at the poles.
        var w = new long[Bands + 1];
        long invDx2 = (long)Bands * Bands / 4;
        for (int e = 1; e < Bands; e++)
        {
            long x = -Unit + (2L * e * Unit / Bands);
            long open = Unit - Mul(x, x);
            long d = Mul(PolarTransport, Unit + (TropicsBoost * Mul(open, open)));
            w[e] = Mul(Mul(d, pressureScale), open) * invDx2;
        }

        var albedo = new long[Bands];
        var free = new long[Bands];
        for (int i = 0; i < Bands; i++)
        {
            long x = BandSine(i);
            free[i] = FreeAlbedo + Mul(AlbedoTowardsPoles, ((3 * Mul(x, x)) - Unit) / 2);
            albedo[i] = free[i];
        }

        var t = new long[Bands];
        var cPrime = new long[Bands];
        var dPrime = new long[Bands];
        for (int it = 0; it < Iterations; it++)
        {
            // Thomas algorithm for the tridiagonal system (B + w⁻ + w⁺)·Tᵢ − w⁻·Tᵢ₋₁ − w⁺·Tᵢ₊₁ = F·(1 − albedo) − A.
            for (int i = 0; i < Bands; i++)
            {
                long rhs = Mul(forcing[i], Unit - albedo[i]) - a;
                long diag = B + w[i] + w[i + 1];
                long lower = -w[i];
                long upper = -w[i + 1];
                long m = i == 0 ? diag : diag - Mul(lower, cPrime[i - 1]);
                cPrime[i] = Div(upper, m);
                dPrime[i] = Div(i == 0 ? rhs : rhs - Mul(lower, dPrime[i - 1]), m);
            }

            t[Bands - 1] = dPrime[Bands - 1];
            for (int i = Bands - 2; i >= 0; i--)
            {
                t[i] = dPrime[i] - Mul(cPrime[i], t[i + 1]);
            }

            // Damped update of the ice cover, so the ice edge settles instead of flickering between two bands.
            for (int i = 0; i < Bands; i++)
            {
                long target = t[i] < IceBelow * Unit ? IceAlbedo : free[i];
                albedo[i] = (albedo[i] + target) / 2;
            }
        }

        return t;
    }

    private static long Mul(long a, long b) => (long)((Int128)a * b / Unit);

    private static long Div(long a, long b) => (long)((Int128)a * Unit / b);
}
