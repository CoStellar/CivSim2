using System;
using CivSim.Core.Numerics;

namespace CivSim.Core.WorldGen;

/// <summary>
/// The carbonate–silicate cycle (stage 1c-A, Patryk's decision of 2026-11-05, ADR 0014). Volcanoes add CO₂ to the air
/// all the time; rain and rock take it out again (weathering), faster where it is warm and where there is more CO₂,
/// but only where water is liquid. When the two match, the CO₂ stops changing. A world starts in that balance, so
/// planets far from their star gather CO₂ until their warmest belts thaw, and close ones lose most of it.
/// <para>
/// CO₂ is a value of the world, not a constant: later stages change it during the game (the cycle answers in hundreds
/// of thousands of years; burning fuel and clearing forests will add CO₂ through effects).
/// </para>
/// <para>
/// Greenhouse warming grows by <see cref="WarmingPerDoublingTenths"/> per doubling of CO₂ up to
/// <see cref="ThickAirMicrobar"/>, and faster above (pressure broadening in thick CO₂ air), calibrated so Earth's
/// 280 µbar give 33 °C.
/// </para>
/// </summary>
public static class CarbonCycle
{
    /// <summary>Earth's CO₂ before industry, millionths of a bar (280 ppm).</summary>
    public const int EarthCo2Microbar = 280;

    /// <summary>Most CO₂ the cycle may gather, millionths of a bar (10 bar).</summary>
    public const int MaxCo2Microbar = 10_000_000;

    /// <summary>Greenhouse warming per doubling of CO₂ in thin air, tenths of °C.</summary>
    public const int WarmingPerDoublingTenths = 18;

    /// <summary>Above this much CO₂ (0.01 bar) each doubling warms by <see cref="ThickWarmingPerDoublingTenths"/>.</summary>
    public const int ThickAirMicrobar = 10_000;

    /// <summary>Greenhouse warming per doubling of CO₂ in thick air, tenths of °C.</summary>
    public const int ThickWarmingPerDoublingTenths = 47;

    /// <summary>How much faster weathering is per 10 °C warmer: twice.</summary>
    public const int WeatheringDoublesEveryDegrees = 10;

    /// <summary>How weathering grows with CO₂: proportional to (CO₂ / Earth's)^0.3.</summary>
    public const long WeatheringCo2Exponent = 300_000; // millionths

    private static readonly long EarthWeathering = BandWeathering(EnergyBalance.Solve(PlanetParameters.Earth with { Co2Microbar = EarthCo2Microbar }));

    /// <summary>Greenhouse warming for an amount of CO₂, tenths of °C (Earth: 330).</summary>
    public static int GreenhouseTenths(int co2Microbar)
    {
        int c = Math.Max(1, co2Microbar);
        long thin = Math.Min(c, ThickAirMicrobar);
        long warming = (EnergyBalance.EarthGreenhouseTenths * FixedLog.Unit)
            + (WarmingPerDoublingTenths * FixedLog.Log2(thin * FixedLog.Unit / EarthCo2Microbar));
        if (c > ThickAirMicrobar)
        {
            warming += ThickWarmingPerDoublingTenths * FixedLog.Log2((long)c * FixedLog.Unit / ThickAirMicrobar);
        }

        long tenths = (warming + (FixedLog.Unit / 2)) / FixedLog.Unit;
        return (int)Math.Clamp(tenths, PlanetParameters.GreenhouseRange.Min, PlanetParameters.GreenhouseRange.Max);
    }

    /// <summary>The CO₂ that gives this greenhouse warming (the inverse of <see cref="GreenhouseTenths"/>), millionths of a bar.</summary>
    public static int Co2ForGreenhouse(int greenhouseTenths)
    {
        long thinTop = GreenhouseTenths(ThickAirMicrobar);
        long doublings = greenhouseTenths <= thinTop
            ? (greenhouseTenths - EnergyBalance.EarthGreenhouseTenths) * FixedLog.Unit / WarmingPerDoublingTenths
            : 0;
        long c = greenhouseTenths <= thinTop
            ? FixedLog.Pow2(doublings) * EarthCo2Microbar / FixedLog.Unit
            : FixedLog.Pow2((greenhouseTenths - thinTop) * FixedLog.Unit / ThickWarmingPerDoublingTenths) * ThickAirMicrobar / FixedLog.Unit;
        return (int)Math.Clamp(c, 1, MaxCo2Microbar);
    }

    /// <summary>
    /// CO₂ at which weathering takes out what the volcanoes put in, millionths of a bar (bisection on the logarithm,
    /// 24 steps). <paramref name="volcanismPercent"/> is the volcanic activity compared with Earth's.
    /// </summary>
    public static int EquilibriumCo2Microbar(int tiltTenths, int eccentricityThousandths, long lightMillionths, int backgroundPressureHundredths, int volcanismPercent)
    {
        long[] forcing = EnergyBalance.Forcing(tiltTenths, eccentricityThousandths, lightMillionths);
        long target = volcanismPercent * FixedLog.Unit / 100;
        long lo = 0;                                                   // log₂ of 1 µbar
        long hi = FixedLog.Log2((long)MaxCo2Microbar * FixedLog.Unit); // log₂ of 10 bar
        for (int k = 0; k < 24; k++)
        {
            long mid = lo + ((hi - lo) / 2);
            int c = (int)Math.Clamp(FixedLog.Pow2(mid) / FixedLog.Unit, 1, MaxCo2Microbar);
            if (Weathering(forcing, c, backgroundPressureHundredths) < target)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (int)Math.Clamp(FixedLog.Pow2(hi) / FixedLog.Unit, 1, MaxCo2Microbar);
    }

    /// <summary>Total air pressure with this CO₂ on top of the other gases, hundredths of a bar.</summary>
    public static int PressureHundredths(int backgroundPressureHundredths, int co2Microbar) =>
        backgroundPressureHundredths + (int)(((long)co2Microbar + 5_000) / 10_000);

    // Weathering compared with Earth's, millionths: (CO₂ / Earth's)^0.3 × band weathering / Earth's band weathering.
    private static long Weathering(long[] forcing, int co2Microbar, int backgroundPressureHundredths)
    {
        long[] bands = EnergyBalance.Solve(forcing, GreenhouseTenths(co2Microbar), PressureHundredths(backgroundPressureHundredths, co2Microbar));
        long co2Factor = FixedLog.Pow2(WeatheringCo2Exponent * FixedLog.Log2((long)co2Microbar * FixedLog.Unit / EarthCo2Microbar) / FixedLog.Unit);
        return (long)((Int128)co2Factor * BandWeathering(bands) / EarthWeathering);
    }

    // Mean over bands of 2^((T − 15) / 10) where the water is liquid (annual mean above 0 °C), millionths.
    private static long BandWeathering(long[] bands)
    {
        long sum = 0;
        foreach (long t in bands)
        {
            if (t > 0)
            {
                sum += FixedLog.Pow2((t - (15 * EnergyBalance.Unit)) / WeatheringDoublesEveryDegrees);
            }
        }

        return Math.Max(1, sum / bands.Length);
    }
}
