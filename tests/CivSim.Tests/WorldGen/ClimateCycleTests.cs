using System.IO;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.WorldGen;

/// <summary>Milankovitch cycles, ice ages and the climate changing during the game (stage 1c-B).</summary>
public class ClimateCycleTests
{
    private static readonly GameContent Content = TestContent.Load();

    [Fact]
    public void TheGameStartsAtAWarmMomentOfTheCycles()
    {
        var cycles = new ClimateCycles(PlanetParameters.Earth, Content.ClimateCycles);
        PlanetParameters start = cycles.OrbitAt(0);
        Assert.Equal(246, start.AxialTiltTenths);         // largest tilt: mild summers and winters
        Assert.Equal(17, start.EccentricityThousandths);  // smallest eccentricity
        Assert.Equal(2700, start.PerihelionTenths);

        // Half a tilt cycle later the tilt is smallest; the eccentricity reaches Earth's real top of about 0.059.
        Assert.Equal(222, cycles.OrbitAt(20_500).AxialTiltTenths);
        int highest = 0;
        for (long y = 0; y < 405_000; y += 1000)
        {
            highest = System.Math.Max(highest, cycles.OrbitAt(y).EccentricityThousandths);
        }

        Assert.InRange(highest, 55, 59);
        Assert.Equal(cycles.OrbitAt(0).PerihelionTenths, cycles.OrbitAt(23_000).PerihelionTenths);
    }

    [Fact]
    public void EarthGetsIceAgesThatEndAbruptly()
    {
        var cycles = new ClimateCycles(PlanetParameters.Earth, Content.ClimateCycles);
        var climate = new ClimateState();
        bool full = false;
        bool ended = false;
        for (long y = 5_000; y <= 150_000; y += 5_000)
        {
            cycles.Advance(climate, y);
            full |= climate.NorthRegime == IceRegime.FullGlacial;
            ended |= full && climate.NorthRegime == IceRegime.Interglacial;
            if (y == 10_000)
            {
                Assert.Equal(IceRegime.Interglacial, climate.NorthRegime); // still warm after the start
            }
        }

        Assert.True(full, "a full glacial within 150 000 years");
        Assert.True(ended, "and it ends again");
    }

    [Fact]
    public void APlanetLyingOnItsSideHasNoIceAges()
    {
        var cycles = new ClimateCycles(Content.Planet("uranus").Planet, Content.ClimateCycles);
        (long north, long south) = ClimateSystem.Potentials(cycles);
        Assert.Equal(0, north);
        Assert.Equal(0, south);
        (north, _) = ClimateSystem.Potentials(new ClimateCycles(PlanetParameters.Earth, Content.ClimateCycles));
        Assert.True(north > 900_000, $"{north}");
    }

    [Fact]
    public void SolvingTheStartingClimateAgainChangesNothing()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(11), Content, out _);
        WorldState world = state.Worlds[0];
        ulong before = GameStateHash.Compute(state);
        var cycles = new ClimateCycles(state.Settings.Planet, Content.ClimateCycles);
        (long n, long s) = ClimateSystem.Potentials(cycles);
        ClimateSystem.Apply(Content, world, cycles, ClimateSystem.LandShare(Content, world.Map), n, s);
        Assert.Equal(before, GameStateHash.Compute(state));
    }

    [Fact]
    public void AnIceAgeCoolsThePlanetAndSpreadsIceAndTundra()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(12), Content, out _);
        WorldState world = state.Worlds[0];
        int coldBefore = CountCold(world);
        long meanBefore = world.Climate.GlobalMeanMillionths;

        world.Climate.NorthIce = world.Climate.SouthIce = Content.ClimateCycles.FullSheetVolume;
        var cycles = new ClimateCycles(state.Settings.Planet, Content.ClimateCycles);
        (long n, long s) = ClimateSystem.Potentials(cycles);
        ClimateSystem.Apply(Content, world, cycles, ClimateSystem.LandShare(Content, world.Map), n, s);

        long cooling = meanBefore - world.Climate.GlobalMeanMillionths;
        Assert.InRange(cooling, 3_500_000, 7_000_000); // decision: ice ages are 4–6 °C colder on average
        Assert.True(world.Climate.Co2Microbar < 220, $"{world.Climate.Co2Microbar}");
        Assert.True(CountCold(world) > coldBefore * 5 / 4, $"{CountCold(world)} vs {coldBefore}");
    }

    [Fact]
    public void ClimateUpdatesAreDeterministicAndSurviveSaving()
    {
        GameState a = Run(12);
        GameState b = Run(12);
        Assert.Equal(GameStateHash.Compute(a), GameStateHash.Compute(b));
        Assert.True(a.Worlds[0].Climate.Year > 0);

        using var ms = new MemoryStream();
        SaveGameSerializer.Save(a, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(GameStateHash.Compute(a), GameStateHash.Compute(loaded));
    }

    private static GameState Run(int turns)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(13), Content, out _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        for (int i = 0; i < turns; i++)
        {
            runner.RunTurn();
        }

        return state;
    }

    private static int CountCold(WorldState world)
    {
        int ice = Content.Biome("ice").Id;
        int tundra = Content.Biome("tundra").Id;
        int n = 0;
        for (int i = 0; i < world.Map.Biome.Length; i++)
        {
            n += world.Map.Biome[i] == ice || world.Map.Biome[i] == tundra ? 1 : 0;
        }

        return n;
    }
}
