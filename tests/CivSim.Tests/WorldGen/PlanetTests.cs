using System;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.WorldGen;

/// <summary>Planets, sunlight, climate and seasons (stages 1b and 1c-A).</summary>
public class PlanetTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static double Ratio(long fixedValue) => fixedValue / (double)FixedTrig.One;

    [Fact]
    public void IntegerTrigonometryIsAccurate()
    {
        for (int deg = -360; deg <= 360; deg += 7)
        {
            double rad = deg * Math.PI / 180;
            Assert.InRange(Ratio(FixedTrig.Sin(FixedTrig.Degrees(deg))) - Math.Sin(rad), -1e-4, 1e-4);
            Assert.InRange(Ratio(FixedTrig.Cos(FixedTrig.Degrees(deg))) - Math.Cos(rad), -1e-4, 1e-4);
        }

        Assert.InRange(FixedTrig.Asin(FixedTrig.One / 2) * 360.0 / FixedTrig.FullTurn, 29.99, 30.01);
        Assert.Equal(FixedTrig.FullTurn / 2, FixedTrig.Acos(-FixedTrig.One));
    }

    [Fact]
    public void EarthSunlightMatchesTheKnownValues()
    {
        // Annual mean top-of-atmosphere sunlight: about 417 W/m² at the equator and 173 W/m² at the poles (of 1361).
        (long equator, _, _) = Insolation.At(0, PlanetParameters.Earth);
        (long pole, long polarNight, long polarDay) = Insolation.At(FixedTrig.One, PlanetParameters.Earth);
        Assert.InRange(Ratio(equator) * 1361, 405, 425);
        Assert.InRange(Ratio(pole) * 1361, 165, 180);
        Assert.Equal(0, polarNight);
        Assert.True(polarDay > equator);
    }

    [Fact]
    public void EarthClimateHasWarmTropicsAndColdPoles()
    {
        // Calibration of the energy balance model (ADR 0014): equator about 28 °C, poles −20 to −27 °C.
        var climate = new ClimateModel(PlanetParameters.Earth);
        Assert.InRange(climate.MeanTemperature(0), 26, 29);
        Assert.InRange(climate.MeanTemperature(FixedTrig.Sin(FixedTrig.Degrees(45))), 8, 15);
        Assert.InRange(climate.MeanTemperature(FixedTrig.One), -27, -20);
        Assert.InRange(climate.GlobalMeanMillionths, 13_000_000, 16_000_000);
        Assert.True(climate.SeasonalSwing(FixedTrig.Sin(FixedTrig.Degrees(60)), 100) > 3 * climate.SeasonalSwing(FixedTrig.Sin(FixedTrig.Degrees(60)), 30) / 2);
        Assert.Equal(100, ClimateModel.GrowingSeason(28, 4));
        Assert.Equal(0, ClimateModel.GrowingSeason(-20, 30));
        Assert.Equal(50, ClimateModel.GrowingSeason(5, 30));
    }

    [Fact]
    public void ALyingAxisMakesThePolesWarmerThanTheEquatorWithExtremeSeasons()
    {
        var uranus = new ClimateModel(Content.Planet("uranus").Planet);
        long pole = FixedTrig.Sin(FixedTrig.Degrees(80));
        Assert.True(uranus.MeanTemperature(pole) > uranus.MeanTemperature(0));
        Assert.True(uranus.SeasonalSwing(pole, 100) > 2 * new ClimateModel(PlanetParameters.Earth).SeasonalSwing(pole, 100));
    }

    // Earth's light and air with a year of this many thousandths (the star and distance follow).
    private static PlanetParameters WithYear(long yearThousandths) =>
        PlanetSetup.Build(PlanetParameters.Earth, autoCo2: false, new PlanetOverrides { YearThousandths = yearThousandths }).Planet;

    [Fact]
    public void LongerYearsSwingFurther()
    {
        long lat = FixedTrig.Sin(FixedTrig.Degrees(50));
        int earth = new ClimateModel(PlanetParameters.Earth).SeasonalSwing(lat, 100);
        int slow = new ClimateModel(WithYear(10_000)).SeasonalSwing(lat, 100);
        int fast = new ClimateModel(WithYear(250)).SeasonalSwing(lat, 100);
        Assert.True(fast < earth && earth < slow, $"{fast} / {earth} / {slow}");
    }

    [Fact]
    public void SofterSeasonsSwingLess()
    {
        long lat = FixedTrig.Sin(FixedTrig.Degrees(45));
        int normal = new ClimateModel(PlanetParameters.Earth).SeasonalSwing(lat, 100);
        // Seasons follow from the pressure: four times Earth's air gives half the seasons.
        PlanetParameters thick = PlanetParameters.Earth with { BackgroundPressureHundredths = 400 };
        Assert.Equal(50, thick.SeasonStrengthPercent);
        int soft = new ClimateModel(thick).SeasonalSwing(lat, 100);
        Assert.InRange(soft, (normal / 2) - 1, (normal / 2) + 1);
        Assert.Equal(100, Content.Planet("uranus").Planet.SeasonStrengthPercent);
    }

    [Fact]
    public void PresetsLoadAndSetTheWorld()
    {
        Assert.Equal(["earth", "mars", "uranus"], Content.Planets.Select(p => p.Key));
        PlanetPreset mars = Content.Planet("mars");
        Assert.Equal(252, mars.Planet.AxialTiltTenths);
        Assert.Equal(1524, mars.Planet.DistanceThousandths);
        Assert.Equal(188, mars.Planet.YearHundredths);
        Assert.Equal(431, (mars.Planet.LightMillionths + 500) / 1000);
        Assert.True(mars.AutoCo2);
        GameSettings s = GameSettings.Test(1).WithPreset(mars);
        Assert.Equal(30, s.WaterPercent);
        Assert.Equal(WorldMoisture.Dry, s.Moisture);
        Assert.Equal("Mars", s.PlanetName);
        Assert.Throws<ArgumentOutOfRangeException>(() => (PlanetParameters.Earth with { DistanceThousandths = 500 }).Validate());

        // Uranus's preset gives only the year and the light: the star follows (about 7 Suns' mass, 1300 Suns' light).
        PlanetParameters uranus = Content.Planet("uranus").Planet;
        Assert.Equal(8400, uranus.YearHundredths);
        Assert.InRange(uranus.StarMassThousandths, 6_800, 7_500);
        Assert.InRange(uranus.LuminosityThousandths, 1_200_000, 1_500_000);
    }

    [Fact]
    public void EveryPresetGivesAValidWorld()
    {
        foreach (PlanetPreset preset in Content.Planets)
        {
            GameState state = GameState.CreateNew(GameSettings.Test(5).WithPreset(preset), Content, out WorldGenReport report);
            Assert.Empty(WorldValidator.Validate(Content, state.Worlds[0].Map, report, WorldGenOptions.From(state.Settings)));
            Assert.Equal(preset.Name, state.Worlds[0].Name);
        }
    }
}
