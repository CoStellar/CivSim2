using System.Linq;
using System.IO;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Hashing;
using CivSim.Core.Numerics;
using CivSim.Core.Population;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Population;

public class PopulationTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static TurnRunner NewGame(ulong seed, GameSettings? settings = null)
    {
        GameState state = GameState.CreateNew(settings ?? GameSettings.Test(seed), Content, out WorldGenReport _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        return runner;
    }

    private static int Stratum(string key) => Content.Strata.Count == 0 ? -1 : FindStratum(key);

    private static int FindStratum(string key)
    {
        foreach (StratumDefinition s in Content.Strata)
        {
            if (s.Key == key)
            {
                return s.Id;
            }
        }

        return -1;
    }

    [Fact]
    public void PopulationDataLoads()
    {
        Assert.Equal(5, Content.Strata.Count);   // producers, artisans, warriors, thinkers, leaders (stage 4F)
        Assert.True(Content.Strata[Stratum("producers")].Gathers);
        Assert.False(Content.Strata[Stratum("warriors")].Gathers);
        Assert.Equal(3, Content.Population.InfluenceBaseRange);
        Assert.Equal(53, Content.Population.NewTribe.Sum());
        Assert.Equal("meat", Content.Goods[Content.Population.MeatGood].Key);
        Assert.NotEmpty(Content.Deposits[0].Gather);
    }

    [Fact]
    public void BrokenPopulationDataReportsEveryError()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-population-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            Replace(dir, "strata.json", "\"targetShare\": \"14\"", "\"targetShare\": \"24\"");
            Replace(dir, "strata.json", "\"good\": \"hide_clothing\"", "\"good\": \"fur_coat\"");
            Replace(dir, "population.json", "\"meatGood\": \"meat\"", "\"meatGood\": \"mammoth\"");

            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("add up to 100", System.StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("fur_coat", System.StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("mammoth", System.StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void Replace(string dir, string file, string from, string to)
    {
        string path = Path.Combine(dir, file);
        string text = File.ReadAllText(path);
        Assert.Contains(from, text, System.StringComparison.Ordinal);
        File.WriteAllText(path, text.Replace(from, to, System.StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(300, 600)] // +3 per mille a year for 50 years ≈ +16.2%
    [InlineData(-500, 300)] // −5 per mille a year for 25 years ≈ −11.8%
    public void GrowthCompoundsPerYear(long hundredthsPerMille, int months)
    {
        double expected = System.Math.Pow(1 + (hundredthsPerMille / 100_000.0), months / 12) * Spoilage.One;
        Assert.InRange(PopulationSystem.GrowthFactor(hundredthsPerMille, months), expected - 1000, expected + 1000);
    }

    [Fact]
    public void EveryTribeStartsWithFiftyThreePeople()
    {
        GameState state = NewGame(1).State;
        foreach (SettlementState s in state.Settlements)
        {
            Assert.Equal(53, s.TotalPopulation());   // with 2 thinkers and a leader (stage 4F)
            Assert.Equal(2, s.PopulationView[Stratum("thinkers")]);
            Assert.Equal(10, s.PopulationView[Stratum("warriors")]);
        }
    }

    [Fact]
    public void GatherersBringFoodHomeAndEveryoneEats()
    {
        TurnRunner runner = NewGame(2);
        runner.RunTurn();
        foreach (SettlementState s in runner.State.Settlements)
        {
            Assert.True(s.FoodGathered > 0, $"{s.Name} gathered nothing");
            Assert.True(s.FoodNeeded > 0);
            Assert.True(s.FoodEaten <= s.FoodNeeded);
        }
    }

    [Fact]
    public void WellFedTribesGrow()
    {
        TurnRunner runner = NewGame(3);
        int before = runner.State.Settlements[0].TotalPopulation();
        runner.RunTurn();
        var fed = runner.State.Settlements.Where(s => s.FedPerMille == 1000).ToList();
        Assert.NotEmpty(fed);
        Assert.All(fed, s => Assert.True(s.TotalPopulation() > before, $"{s.Name} was fed but did not grow"));
    }

    [Fact]
    public void TribesWithoutFoodStarveAndDieOut()
    {
        TurnRunner runner = NewGame(4);
        SettlementState camp = runner.State.Settlements[0];
        CivilizationState civ = runner.State.Civilizations[0];

        // A permanent effect that stops all gathering in the camp: no work, no food.
        runner.State.Effects.Add(Content.Effect("damage"), camp.World, camp.Id.Value, -100, EffectInstance.Permanent);
        camp.Stock.Clear();
        for (int t = 0; t < 10 && camp.TotalPopulation() > 0; t++)
        {
            runner.RunTurn();
            Assert.True(camp.Health < 50 || camp.TotalPopulation() == 0);
        }

        Assert.Equal(0, camp.TotalPopulation());
        Assert.False(civ.IsAlive);
        Assert.Contains(runner.LastChronicle, l => l.Contains("died out", System.StringComparison.Ordinal));
    }

    [Fact]
    public void PeopleNeverAppearOrVanishThroughRounding()
    {
        TurnRunner runner = NewGame(5);
        SettlementState camp = runner.State.Settlements[0];
        for (int t = 0; t < 30; t++)
        {
            runner.RunTurn();
            int sum = 0;
            foreach (int p in camp.PopulationView)
            {
                Assert.True(p >= 0);
                sum += p;
            }

            Assert.Equal(camp.TotalPopulation(), sum);
        }
    }

    [Fact]
    public void StrataDriftTowardsTheirTargetShares()
    {
        TurnRunner runner = NewGame(6);
        for (int t = 0; t < 40; t++)
        {
            runner.RunTurn();
        }

        // The largest camp: the first one may have been driven out or merged by then (AI choices since stage 4b).
        SettlementState camp = runner.State.Settlements.OrderByDescending(s => s.TotalPopulation()).ThenBy(s => s.Id.Value).First();
        int total = camp.TotalPopulation();
        Assert.True(total > 0);
        int artisans = camp.PopulationView[Stratum("artisans")] * 100 / total;
        int warriors = camp.PopulationView[Stratum("warriors")] * 100 / total;
        Assert.InRange(artisans, 3, 7);
        Assert.InRange(warriors, 12, 18);
    }

    [Fact]
    public void TribesLevelOffInsteadOfExploding()
    {
        TurnRunner runner = NewGame(7);
        for (int t = 0; t < 200; t++)
        {
            runner.RunTurn();
        }

        foreach (SettlementState s in runner.State.Settlements.Where(s => s.IsActive))
        {
            // Two rings of land around a camp feed a few hundred people at most.
            Assert.InRange(s.TotalPopulation(), 1, 1000);
        }
    }

    [Fact]
    public void PopulationSurvivesSaveAndLoadAndStaysDeterministic()
    {
        TurnRunner runner = NewGame(8);
        for (int t = 0; t < 5; t++)
        {
            runner.RunTurn();
        }

        using var ms = new MemoryStream();
        SaveGameSerializer.Save(runner.State, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(GameStateHash.Compute(runner.State), GameStateHash.Compute(loaded));

        var twin = new TurnRunner(loaded);
        CoreSystems.Register(twin, Content);
        for (int t = 0; t < 10; t++)
        {
            runner.RunTurn();
            twin.RunTurn();
        }

        Assert.Equal(GameStateHash.Compute(runner.State), GameStateHash.Compute(twin.State));
    }

    [Fact]
    public void TurnLengthDoesNotChangeTheYearlyRates()
    {
        // 600 months as one turn or as two turns of 300 months: the same growth.
        long once = PopulationSystem.GrowthFactor(300, 600);
        long half = PopulationSystem.GrowthFactor(300, 300);
        long twice = IntMath.MulDiv(half, half, Spoilage.One);
        Assert.InRange(twice, once - 100, once + 100);
    }
}
