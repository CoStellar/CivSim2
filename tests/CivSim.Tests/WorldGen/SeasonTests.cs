using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.WorldGen;

/// <summary>Live seasons and the two clocks (decision of 2026-11-05).</summary>
public class SeasonTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static TurnRunner NewGame(string planet)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(3).WithPreset(Content.Planet(planet)), Content, out _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        return runner;
    }

    [Fact]
    public void OnlyLongYearsArePlayedSeasonBySeason()
    {
        Assert.False(Content.Seasons.Live(Content.Planet("earth").Planet));
        Assert.False(Content.Seasons.Live(Content.Planet("mars").Planet));
        Assert.True(Content.Seasons.Live(Content.Planet("uranus").Planet));

        Assert.Equal(600, NewGame("earth").State.MonthsPerTurn(Content));
        GameState uranus = NewGame("uranus").State;
        Assert.Equal(126, uranus.MonthsPerTurn(Content)); // 84 years / 8
        Assert.Equal(600, uranus.EraMonthsPerTurn(Content));
    }

    [Fact]
    public void SlowDecisionsComeOncePerEraLengthPeriod()
    {
        TurnRunner runner = NewGame("uranus");
        int slow = 0;
        for (int t = 0; t < 24; t++)
        {
            slow += runner.State.IsDecisionTurn(Content) ? 1 : 0;
            runner.RunTurn();
        }

        // 24 turns × 10.5 years = 252 years: one slow turn in each 50-year period.
        Assert.InRange(slow, 5, 6);
        Assert.True(NewGame("earth").State.IsDecisionTurn(Content));
    }

    [Fact]
    public void EachHemisphereGetsItsSummerAndWinterInTurn()
    {
        var climate = new ClimateModel(Content.Planet("uranus").Planet);
        long north = FixedTrig.Sin(FixedTrig.Degrees(50));
        int coldestStep = Enumerable.Range(0, Insolation.Steps).MinBy(k => climate.TemperatureAt(north, k, 20, 100));
        int warmestStep = Enumerable.Range(0, Insolation.Steps).MaxBy(k => climate.TemperatureAt(north, k, 20, 100));
        Assert.True(climate.TemperatureAt(north, warmestStep, 20, 100) - climate.TemperatureAt(north, coldestStep, 20, 100) > 80);

        // When the north has its winter, the south has its summer, about half a year apart.
        Assert.True(climate.TemperatureAt(-north, coldestStep, 20, 100) > climate.TemperatureAt(north, coldestStep, 20, 100) + 60);
        Assert.InRange((warmestStep - coldestStep + Insolation.Steps) % Insolation.Steps, 18, 30);

        // On the map, the season layers change from turn to turn.
        TurnRunner runner = NewGame("uranus");
        WorldMap map = runner.State.Worlds[0].Map;
        runner.RunTurn();
        sbyte[] before = map.SeasonTemperature.Values.ToArray();
        for (int t = 0; t < 4; t++)
        {
            runner.RunTurn();
        }

        Assert.NotEqual(before, map.SeasonTemperature.Values.ToArray());
    }

    [Fact]
    public void WinterCutsWildFoodOnlyWithLiveSeasons()
    {
        TurnRunner runner = NewGame("uranus");
        WorldMap map = runner.State.Worlds[0].Map;
        var gathering = new Core.Economy.GatheringSystem(Content, new Core.Economy.YieldYears(0));
        runner.RunTurn();
        int cell = Enumerable.Range(0, map.Biome.Length).First(i => !Content.Biomes[map.Biome[i]].IsWater && Content.Biomes[map.Biome[i]].Food > 1);
        map.SeasonGrowth.Set(cell, 0);
        long winter = gathering.CellFoodPerYear(map, cell).Hundredths;
        map.SeasonGrowth.Set(cell, 100);
        long summer = gathering.CellFoodPerYear(map, cell).Hundredths;
        Assert.InRange(winter, (summer * Content.Seasons.WinterFoodPercent / 100) - 1, (summer * Content.Seasons.WinterFoodPercent / 100) + 1);
    }

    [Fact]
    public void ColdToleranceMakesColdLandWorthMore()
    {
        GameState state = NewGame("earth").State;
        WorldState world = state.Worlds[0];
        CivilizationState civ = state.Civilizations[0];
        int cold = Enumerable.Range(0, world.Map.Temperature.Length).First(i => world.Map.Temperature[i] - (world.Map.Seasonality[i] / 2) < -25);
        var ai = new GovernorAdvisor(Content);
        int before = ai.Comfort(state, world, civ, cold);
        state.Effects.Add(Content.Effect("cold_tolerance"), WorldId.Earth, civ.Id.Value, 15, state.Month + 12_000);
        Assert.True(ai.Comfort(state, world, civ, cold) > before);
        Assert.True(before < 100);
    }
}
