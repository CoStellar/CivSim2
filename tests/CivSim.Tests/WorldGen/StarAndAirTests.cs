using System;
using CivSim.Core.Numerics;
using CivSim.Core.WorldGen;
using Xunit;

namespace CivSim.Tests.WorldGen;

/// <summary>Star, orbit, habitable zone, carbon cycle and energy balance (stage 1c-A).</summary>
public class StarAndAirTests
{
    [Fact]
    public void FixedLogarithmsAreAccurate()
    {
        foreach (long v in new long[] { 1_000, 280_000, 1_000_000, 1_524_000, 9_000_000, 84_000_000_000 })
        {
            double expected = Math.Log2(v / 1e6);
            Assert.InRange((FixedLog.Log2(v) / 1e6) - expected, -5e-6, 5e-6);
        }

        foreach (long x in new long[] { -13_700_000, -1_000_000, -300_000, 0, 500_000, 1_000_000, 7_250_000 })
        {
            double expected = Math.Pow(2, x / 1e6);
            Assert.InRange((FixedLog.Pow2(x) / 1e6) - expected, -5e-6 - (expected * 1e-6), 5e-6 + (expected * 1e-6));
        }
    }

    [Fact]
    public void KeplerGivesTheKnownYears()
    {
        Assert.Equal(1000, StarOrbit.YearThousandths(1000, 1000));
        Assert.InRange(StarOrbit.YearThousandths(1000, 1524), 1879, 1883); // Mars: 1.881 years
        Assert.Equal(1000, StarOrbit.MassThousandths(1000));
        Assert.InRange(StarOrbit.MassThousandths(8_000), 1_680, 1_684);   // L = M⁴: 8 Suns of light = 1.682 solar masses
        Assert.InRange(StarOrbit.MassThousandths(1_270_000), 6_990, 7_010); // L = 1.4·M³·⁵: 7 solar masses
        Assert.InRange(StarOrbit.LightMillionths(1000, 1524), 430_000, 431_000);
    }

    [Fact]
    public void HabitableZoneFollowsTheStar()
    {
        (int inner, int outer) = StarOrbit.ZoneThousandths(1000);
        Assert.InRange(inner, 949, 951);
        Assert.InRange(outer, 1675, 1677);
        (int inner4, int outer4) = StarOrbit.ZoneThousandths(4000);
        Assert.InRange(inner4, (2 * inner) - 2, (2 * inner) + 2); // four times the light, twice the distance
        Assert.InRange(outer4, (2 * outer) - 2, (2 * outer) + 2);
    }

    [Fact]
    public void AnyTwoQuantitiesGiveTheThird()
    {
        // Year and light: the 84-year Uranus at Earth's light needs a bright star far away.
        (int l, int d, bool clamped) = StarOrbit.Solve(OrbitQuantity.Year, 84_000, OrbitQuantity.Light, 1000);
        Assert.False(clamped);
        Assert.InRange(StarOrbit.YearThousandths(l, d), 83_950, 84_050);
        Assert.InRange(StarOrbit.LightMillionths(l, d), 998_000, 1_002_000);

        // Distance and year give the star.
        (l, d, clamped) = StarOrbit.Solve(OrbitQuantity.Distance, 1000, OrbitQuantity.Year, 1000);
        Assert.False(clamped);
        Assert.InRange(l, 995, 1005);
        Assert.Equal(1000, d);
    }

    [Fact]
    public void LeavingTheZonePutsThePlanetOnItsEdge()
    {
        // Moving Earth to 2 AU around the Sun: the planet stops at the outer edge, the star stays.
        (int l, int d, bool clamped) = StarOrbit.Adjust(1000, 1000, OrbitQuantity.Distance, 2000, OrbitQuantity.Year);
        Assert.True(clamped);
        Assert.Equal(1000, l);
        Assert.InRange(d, 1675, 1677);

        // A year of 84 with the Sun kept: the distance follows, hits the edge, and the year stops there.
        (l, d, clamped) = StarOrbit.Adjust(1000, 1000, OrbitQuantity.Year, 84_000, OrbitQuantity.Distance);
        Assert.True(clamped);
        Assert.Equal(1000, l);
        Assert.InRange(d, 1675, 1677);
    }

    [Fact]
    public void TheCarbonCycleKeepsEarthAtItsCo2AndWarmsColdPlanets()
    {
        int earth = CarbonCycle.EquilibriumCo2Microbar(234, 17, 1_000_000, 100, 100);
        Assert.InRange(earth, 270, 290);
        Assert.Equal(330, CarbonCycle.GreenhouseTenths(CarbonCycle.EarthCo2Microbar));

        // Farther from the star, weathering slows down and the CO₂ builds up.
        int far = CarbonCycle.EquilibriumCo2Microbar(234, 17, 600_000, 100, 100);
        Assert.True(far > 100 * earth, $"{far}");

        // More volcanoes, more CO₂, a warmer planet.
        int volcanic = CarbonCycle.EquilibriumCo2Microbar(234, 17, 1_000_000, 100, 500);
        Assert.True(volcanic > 2 * earth, $"{volcanic}");
        Assert.True(CarbonCycle.GreenhouseTenths(volcanic) > 330);

        // The greenhouse warming and the CO₂ are each other's inverse.
        foreach (int c in new[] { 100, 280, 5_000, 50_000, 2_000_000 })
        {
            int back = CarbonCycle.Co2ForGreenhouse(CarbonCycle.GreenhouseTenths(c));
            Assert.InRange(back, c * 96 / 100, c * 104 / 100);
        }
    }

    [Fact]
    public void ALyingAxisDoesNotNeedTheEdgeOfTheZoneToShapeItsAir()
    {
        // Without cold poles the whole of Uranus weathers, so its CO₂ differs from Earth's at the same light.
        int uranus = CarbonCycle.EquilibriumCo2Microbar(978, 46, 1_000_000, 100, 100);
        Assert.NotInRange(uranus, 250, 320);
    }

    [Fact]
    public void ThickAirEvensOutTheClimate()
    {
        var earth = new ClimateModel(PlanetParameters.Earth);
        var thick = new ClimateModel(PlanetParameters.Earth with { BackgroundPressureHundredths = 900 });
        long earthSpread = earth.MeanTemperatureMillionths(0) - earth.MeanTemperatureMillionths(FixedTrig.One);
        long thickSpread = thick.MeanTemperatureMillionths(0) - thick.MeanTemperatureMillionths(FixedTrig.One);
        Assert.True(thickSpread < earthSpread * 3 / 4, $"{thickSpread} vs {earthSpread}");
    }

    [Fact]
    public void BuildingAPlanetFollowsTheStatedRules()
    {
        // One value: a year alone keeps the light; a distance alone keeps the star.
        (PlanetParameters longYear, _) = PlanetSetup.Build(PlanetParameters.Earth, true, new PlanetOverrides { YearThousandths = 84_000 });
        Assert.InRange(longYear.LightMillionths, 995_000, 1_005_000);
        Assert.Equal(8400, longYear.YearHundredths);
        (PlanetParameters far, var notes) = PlanetSetup.Build(PlanetParameters.Earth, true, new PlanetOverrides { DistanceThousandths = 1300 });
        Assert.Equal(1000, far.LuminosityThousandths);
        Assert.Empty(notes);
        Assert.True(far.Co2Microbar > PlanetParameters.Earth.Co2Microbar, "a colder orbit gathers CO2");

        // Three values are too many, and leaving the zone is noted.
        Assert.Throws<ArgumentException>(() => PlanetSetup.Build(PlanetParameters.Earth, true, new PlanetOverrides { LuminosityThousandths = 1, DistanceThousandths = 1, YearThousandths = 1 }));
        (_, notes) = PlanetSetup.Build(PlanetParameters.Earth, true, new PlanetOverrides { DistanceThousandths = 3000 });
        Assert.Single(notes);

        // A pressure asked for counts the CO₂ in.
        (PlanetParameters set, _) = PlanetSetup.Build(PlanetParameters.Earth, false, new PlanetOverrides { Co2Microbar = 500_000, PressureHundredths = 200 });
        Assert.Equal(200, set.PressureHundredths);
        Assert.Equal(150, set.BackgroundPressureHundredths);
    }

    [Fact]
    public void TheClimateIsTheSameEveryTime()
    {
        long[] a = EnergyBalance.Solve(PlanetParameters.Earth);
        long[] b = EnergyBalance.Solve(PlanetParameters.Earth);
        Assert.Equal(a, b);

        // Pinned values: a change here changes every map (update together with ADR 0014).
        Assert.Equal(27, new ClimateModel(PlanetParameters.Earth).MeanTemperature(0));
        Assert.Equal(-22, new ClimateModel(PlanetParameters.Earth).MeanTemperature(FixedTrig.One));
    }
}
