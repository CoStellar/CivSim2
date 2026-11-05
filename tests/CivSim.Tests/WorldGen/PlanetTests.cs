using System;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.WorldGen;

/// <summary>Planets, sunlight and seasons (stage 1b).</summary>
public class PlanetTests
{
    private static readonly GameContent Content = ContentLoader.Load();

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
        var climate = new ClimateModel(PlanetParameters.Earth);
        Assert.Equal(28, climate.MeanTemperature(0));
        Assert.InRange(climate.MeanTemperature(FixedTrig.Sin(FixedTrig.Degrees(45))), 10, 18);
        Assert.InRange(climate.MeanTemperature(FixedTrig.One), -28, -25);
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

    [Fact]
    public void LongerYearsSwingFurther()
    {
        long lat = FixedTrig.Sin(FixedTrig.Degrees(50));
        int earth = new ClimateModel(PlanetParameters.Earth).SeasonalSwing(lat, 100);
        int slow = new ClimateModel(PlanetParameters.Earth with { YearHundredths = 1000 }).SeasonalSwing(lat, 100);
        int fast = new ClimateModel(PlanetParameters.Earth with { YearHundredths = 25 }).SeasonalSwing(lat, 100);
        Assert.True(fast < earth && earth < slow, $"{fast} / {earth} / {slow}");
    }

    [Fact]
    public void SofterSeasonsSwingLess()
    {
        long lat = FixedTrig.Sin(FixedTrig.Degrees(45));
        int normal = new ClimateModel(PlanetParameters.Earth).SeasonalSwing(lat, 100);
        int soft = new ClimateModel(PlanetParameters.Earth with { SeasonStrengthPercent = 50 }).SeasonalSwing(lat, 100);
        Assert.InRange(soft, (normal / 2) - 1, (normal / 2) + 1);
        Assert.Equal(100, Content.Planet("uranus").Planet.SeasonStrengthPercent);
    }

    [Fact]
    public void PresetsLoadAndSetTheWorld()
    {
        Assert.Equal(["earth", "mars", "uranus"], Content.Planets.Select(p => p.Key));
        PlanetPreset mars = Content.Planet("mars");
        Assert.Equal(new PlanetParameters(252, 188, 85, 93), mars.Planet);
        GameSettings s = GameSettings.Test(1).WithPreset(mars);
        Assert.Equal(30, s.WaterPercent);
        Assert.Equal(WorldMoisture.Dry, s.Moisture);
        Assert.Equal("Mars", s.PlanetName);
        Assert.Throws<ArgumentOutOfRangeException>(() => (PlanetParameters.Earth with { StarBrightnessPercent = 200 }).Validate());
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
