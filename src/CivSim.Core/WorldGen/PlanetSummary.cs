using System.Collections.Generic;
using System.Globalization;
using CivSim.Core.Numerics;

namespace CivSim.Core.WorldGen;

/// <summary>Plain-English summary of a planet's star, orbit, air and climate (for the headless run, saves and the viewer).</summary>
public static class PlanetSummary
{
    /// <summary>One line per fact, e.g. "star: 1.000 Suns, 1.000 solar masses".</summary>
    public static IReadOnlyList<string> Lines(PlanetParameters planet)
    {
        System.ArgumentNullException.ThrowIfNull(planet);
        var climate = new ClimateModel(planet);
        (int inner, int outer) = StarOrbit.ZoneThousandths(planet.LuminosityThousandths);
        int co2 = planet.Co2Microbar;
        string co2Text = co2 < 100_000
            ? string.Create(CultureInfo.InvariantCulture, $"{co2} ppm")
            : $"{Fixed(co2 / 10, 5)} bar";
        return
        [
            $"star:               {Fixed(planet.LuminosityThousandths, 3)} Suns, {Fixed(planet.StarMassThousandths, 3)} solar masses",
            $"orbit:              {Fixed(planet.DistanceThousandths, 3)} AU (habitable zone {Fixed(inner, 3)}-{Fixed(outer, 3)} AU), year {Fixed(planet.YearHundredths, 2)} Earth years",
            $"light:              {Fixed((planet.LightMillionths + 500) / 1000, 3)} of Earth's",
            $"air:                CO2 {co2Text}, greenhouse {Fixed(planet.GreenhouseTenths, 1)} C, pressure {Fixed(planet.PressureHundredths, 2)} bar, volcanism {planet.VolcanismPercent}%, seasons {planet.SeasonStrengthPercent}% of Earth's",
            $"climate (sea level): mean {Degrees(climate.GlobalMeanMillionths)}, equator {Degrees(climate.MeanTemperatureMillionths(0))}, 45N {Degrees(climate.MeanTemperatureMillionths(FixedTrig.Sin(FixedTrig.Degrees(45))))}, 60N {Degrees(climate.MeanTemperatureMillionths(FixedTrig.Sin(FixedTrig.Degrees(60))))}, north pole {Degrees(climate.MeanTemperatureMillionths(FixedTrig.One))}",
        ];
    }

    // An integer with `decimals` implied decimal places, e.g. Fixed(1524, 3) = "1.524".
    private static string Fixed(long value, int decimals)
    {
        long scale = 1;
        for (int i = 0; i < decimals; i++)
        {
            scale *= 10;
        }

        string sign = value < 0 ? "-" : string.Empty;
        long a = System.Math.Abs(value);
        return decimals == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{sign}{a}")
            : string.Create(CultureInfo.InvariantCulture, $"{sign}{a / scale}.{(a % scale).ToString(CultureInfo.InvariantCulture).PadLeft(decimals, '0')}");
    }

    private static string Degrees(long millionths) => Fixed((millionths >= 0 ? millionths + 50_000 : millionths - 50_000) / 100_000, 1) + " C";
}
