using System.Collections.Generic;
using System.IO;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Research;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Research;

/// <summary>Technologies, research, inspirations, diffusion and eras (stage 3f).</summary>
public class ResearchTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static int Tech(string key) => Content.Technology(key).Id;

    private static GameState NewGame(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out _);

    [Fact]
    public void TheTechnologyGraphLoads()
    {
        Assert.Equal(14, Content.Technologies.Count);
        Assert.Equal(Tech("agriculture"), Content.Eras[1].ThresholdTechnology);
        Assert.Equal([Tech("fire"), Tech("flint_knapping")], Content.Technology("agriculture").Requires);
        Assert.Equal(2, Content.Technology("weaving").RequiresAny.Count);
        foreach (RecipeDefinition recipe in Content.Recipes)
        {
            Assert.Contains(Content.Technologies, t => System.Linq.Enumerable.Contains(t.Recipes, recipe.Id));
        }
    }

    [Fact]
    public void RequirementsGateResearch()
    {
        CivilizationState civ = NewGame(41).Civilizations[0];
        Technologies.EnsureSize(Content, civ);
        Assert.True(Technologies.Available(Content, civ, Tech("fire")));
        Assert.False(Technologies.Available(Content, civ, Tech("agriculture")));
        Assert.False(Technologies.Available(Content, civ, Tech("weaving")));

        civ.KnownTechnologies[Tech("fire")] = true;
        civ.KnownTechnologies[Tech("flint_knapping")] = true;
        Assert.True(Technologies.Available(Content, civ, Tech("agriculture")));
        Assert.False(Technologies.Available(Content, civ, Tech("fire")));       // already known

        civ.KnownTechnologies[Tech("agriculture")] = true;
        Assert.True(Technologies.Available(Content, civ, Tech("weaving")));     // one of its alternatives is known
    }

    [Fact]
    public void InspirationsAndNeighboursMakeTechnologiesCheaper()
    {
        GameState state = NewGame(42);
        CivFacts[] facts = CivFacts.Measure(Content, state);
        int people = Tech("traditions");
        Assert.Equal(100, Technologies.CostPercent(Content, state, facts, 0, people));

        facts[0].People = 1000;                                           // traditions: 500 people → −20%
        Assert.Equal(80, Technologies.CostPercent(Content, state, facts, 0, people));

        // Two neighbours that know it take off 40% more; a third adds nothing (maxNeighbours 2).
        facts[0].Neighbours.AddRange([1, 2, 3]);
        foreach (int n in new[] { 1, 2, 3 })
        {
            Technologies.Learn(Content, state, state.Civilizations[n], people);
        }

        Assert.Equal(40, Technologies.CostPercent(Content, state, facts, 0, people));

        // Never below a quarter of the base cost.
        facts[0].Goods[Content.Good("hides").Id] = 1_000_000;
        facts[0].ColdestWinter = -30;
        facts[0].Deposits.Add(System.Linq.Enumerable.First(Content.Deposits, d => d.Key == "game").Id);
        int tanning = Tech("tanning");
        foreach (int n in new[] { 1, 2 })
        {
            Technologies.Learn(Content, state, state.Civilizations[n], tanning);
        }

        Assert.Equal(25, Technologies.CostPercent(Content, state, facts, 0, tanning));
    }

    [Fact]
    public void ResearchLearnsTechnologiesAndStartsTheirEffects()
    {
        GameState state = NewGame(43);
        CivilizationState civ = state.Civilizations[0];
        civ.Researching = Tech("fire");
        civ.ResearchProgress = Content.Technology("fire").Cost.Hundredths;
        var runner = new TurnRunner(state);
        runner.AddSystem(new ResearchSystem(Content));
        runner.RunTurn();

        Assert.True(civ.Knows(Tech("fire")));
        Assert.Equal(CivilizationState.NoResearch, civ.Researching);
        Assert.Contains(runner.LastChronicle, l => l.EndsWith("learned Fire.", System.StringComparison.Ordinal));
        Assert.Equal(4, state.Effects.Total(Content.Effects, EffectStat.ColdToleranceDegrees, EffectTarget.Civilization, WorldId.Earth, civ.Id.Value));
        Assert.Equal(10, state.Effects.Total(Content.Effects, EffectStat.OutputPercent, EffectTarget.Civilization, WorldId.Earth, civ.Id.Value));
        Assert.True(civ.LastScience > 0);
        Assert.True(civ.ResearchProgress >= civ.LastScience);          // the turn's points (and any inspiration saving) carry on
    }

    [Fact]
    public void AgricultureAndMostStoneAgeKnowledgeOpenTheNeolithic()
    {
        GameState state = NewGame(44);
        CivilizationState civ = state.Civilizations[0];
        foreach (string key in new[] { "fire", "flint_knapping", "agriculture" })
        {
            Technologies.Learn(Content, state, civ, Tech(key));
        }

        Assert.Equal(0, civ.Era);                                       // 2 of 6 Stone Age technologies are not 60%
        Technologies.Learn(Content, state, civ, Tech("cooperative_hunting"));
        List<string> lines = Technologies.Learn(Content, state, civ, Tech("tanning"));
        Assert.Equal(1, civ.Era);
        Assert.Contains(lines, l => l.EndsWith("entered the Neolithic.", System.StringComparison.Ordinal));
        Assert.Equal(300, WorldClock.RhythmMonths(Content, state, civ)); // its own rhythm follows its own era (ADR 0018)
        Assert.Equal(600, WorldClock.RhythmMonths(Content, state, state.Civilizations[1])); // the others keep theirs
    }

    [Fact]
    public void TheAiResearchesTheCheapestTechnology()
    {
        GameState state = NewGame(45);
        var queue = new CommandQueue();
        new ScholarAdvisor(Content).IssueCommands(state, queue);
        var commands = new List<CommandEnvelope>();
        queue.DrainSorted(state, 1, commands);
        Assert.Equal(state.Civilizations.Count, commands.Count);
        foreach (CommandEnvelope e in commands)
        {
            int t = Assert.IsType<SetResearchCommand>(e.Command).Technology;
            Assert.Contains(t, new[] { Tech("fire"), Tech("flint_knapping") });
            Assert.True(e.Command.Validate(state, e.Civ).Success);
        }

        Assert.False(new SetResearchCommand(Content, Tech("agriculture")).Validate(state, state.Civilizations[0].Id).Success);
    }

    [Fact]
    public void FishersWorkTheShore()
    {
        GameState state = NewGame(46);
        CivilizationState civ = state.Civilizations[0];
        var gathering = new GatheringSystem(Content, new YieldYears(0));
        TechBenefits before = gathering.Benefits(state, civ.Id);
        Assert.False(before.FromWater);

        Technologies.Learn(Content, state, civ, Tech("flint_knapping"));
        Technologies.Learn(Content, state, civ, Tech("fishing"));
        TechBenefits after = gathering.Benefits(state, civ.Id);
        Assert.True(after.FromWater);

        WorldMap map = state.Worlds[0].Map;
        int coast = Content.WaterBiome(BiomeKind.Coast).Id;
        int cell = System.Array.IndexOf(map.Biome.Values.ToArray(), (byte)coast);
        Assert.Equal(Amount.Zero, gathering.CellFoodPerYear(map, cell, false, before));
        Assert.True(gathering.CellFoodPerYear(map, cell, false, after) >= Amount.FromUnits(4));
    }

    [Fact]
    public void PotsKeepFoodLonger()
    {
        GameState state = NewGame(47);
        SettlementState camp = state.Settlements[0];
        SettlementState other = state.Settlements[1];
        camp.Stock[Content.Good("meat").Id] = Amount.FromUnits(100);
        other.Stock[Content.Good("meat").Id] = Amount.FromUnits(100);
        CivilizationState civ = state.FindCivilization(camp.Owner)!;
        foreach (string key in new[] { "fire", "flint_knapping", "agriculture", "pottery" })
        {
            Technologies.Learn(Content, state, civ, Tech(key));
        }

        var runner = new TurnRunner(state);
        runner.AddSystem(new StorageSystem(Content));
        runner.RunTurn();
        Assert.True(camp.StockView[Content.Good("meat").Id] > other.StockView[Content.Good("meat").Id]);
    }

    [Fact]
    public void ResearchIsDeterministicAndSurvivesSaving()
    {
        GameState a = Run(48, 30);
        GameState b = Run(48, 30);
        Assert.Equal(GameStateHash.Compute(a), GameStateHash.Compute(b));
        Assert.All(a.Civilizations, c => Assert.True(c.Researching >= 0 && c.ResearchProgress > 0));

        using var ms = new MemoryStream();
        SaveGameSerializer.Save(a, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(GameStateHash.Compute(a), GameStateHash.Compute(loaded));
        Assert.Equal(a.Civilizations[0].KnownCount, loaded.Civilizations[0].KnownCount);
    }

    private static GameState Run(ulong seed, int turns)
    {
        GameState state = NewGame(seed);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        for (int i = 0; i < turns; i++)
        {
            runner.RunTurn();
        }

        return state;
    }
}
