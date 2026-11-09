using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>Character, the strategist and its effects on the other advisors (GDD 7.6, 8.1, 8.4; stage 4b).</summary>
public class StrategyTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static (TurnRunner Runner, AiCouncil Council) NewGame(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        var runner = new TurnRunner(state);
        return (runner, CoreSystems.Register(runner, Content));
    }

    [Fact]
    public void EveryCivilizationGetsADifferentCharacterFromTheSeed()
    {
        GameState a = GameState.CreateNew(GameSettings.Test(5));
        GameState b = GameState.CreateNew(GameSettings.Test(5));
        foreach (CivilizationState civ in a.Civilizations)
        {
            Assert.Equal(Character.TraitCount, civ.Traits.Length);
            Assert.All(civ.Traits, t => Assert.InRange(t, 0, 1000));
            Assert.Equal(civ.Traits, b.FindCivilization(civ.Id)!.Traits);
        }

        Assert.True(a.Civilizations.Select(c => c.Trait(Trait.Aggression)).Distinct().Count() > 1, "all civilizations alike");
        Assert.NotEqual(a.Civilizations[0].Traits, GameState.CreateNew(GameSettings.Test(6)).Civilizations[0].Traits);
    }

    [Fact]
    public void CharacterMultipliesBetweenHalfAndOneAndAHalf()
    {
        var civ = new CivilizationState(new CivId(0), "Test", PlayerId.Ai) { Traits = new int[Character.TraitCount] };
        Assert.Equal(500, Character.Multiplier(civ, Goal.Expansion));
        civ.Traits[(int)Trait.Expansiveness] = 1000;
        Assert.Equal(1500, Character.Multiplier(civ, Goal.Expansion));
        Assert.Equal(500, Character.Multiplier(civ, Goal.Conquest));
        Assert.Equal(Character.Average, new CivilizationState(new CivId(1), "Untraited", PlayerId.Ai).Trait(Trait.Caution));
    }

    [Fact]
    public void TheStrategistReviewsEveryCivilizationAtOnceThenSpreadsTheReviews()
    {
        (TurnRunner runner, AiCouncil council) = NewGame(31);
        runner.RunTurn();
        Decision[] strategies = council.Log.Entries.Where(d => d.Action == AiAction.SetStrategy).ToArray();
        Assert.Equal(runner.State.Civilizations.Count, strategies.Length);
        Assert.All(strategies, d => Assert.Equal(AdvisorKind.Strategist, d.Advisor));
        foreach (CivilizationState civ in runner.State.Civilizations)
        {
            Assert.Equal(Character.GoalCount, civ.GoalWeights.Length);
            Assert.Equal(civ.Era, civ.StrategyEra);
        }

        Assert.True(runner.State.Civilizations.Select(c => c.StrategyReviewMonth).Distinct().Count() > 1, "reviews not spread");

        // No civilization is reviewed again before its month comes.
        runner.RunTurn();
        Assert.DoesNotContain(council.Log.Entries, d => d.Action == AiAction.SetStrategy);
    }

    [Fact]
    public void TheExpansionTargetIsKnownFreeLand()
    {
        (TurnRunner runner, _) = NewGame(31);
        for (int t = 0; t < 40; t++)
        {
            runner.RunTurn();
        }

        GameState state = runner.State;
        WorldState earth = state.Worlds[0];
        int targets = 0;
        foreach (CivilizationState civ in state.Civilizations.Where(c => c.IsAlive && c.ExpansionTarget >= 0))
        {
            targets++;
            int cell = civ.ExpansionTarget;
            Assert.False(Content.Biomes[earth.Map.Biome[cell]].IsWater);
            Assert.True(Core.Perception.MapMemory.Knows(Content, state, civ, cell));
        }

        Assert.True(targets > 0, "no strategist found free land");
    }

    [Fact]
    public void HungerRaisesProsperityAndSecurity()
    {
        (TurnRunner runner, AiCouncil council) = NewGame(31);
        GameState state = runner.State;
        CivilizationState civ = state.Civilizations[0];
        foreach (SettlementState s in state.Settlements.Where(s => s.Owner == civ.Id))
        {
            s.FoodNeeded = 1000;
            s.FoodEaten = 500;
        }

        var queue = new CommandQueue();
        council.IssueCommands(state, queue);
        runner.RunTurn();
        // The government leans the weights its way (stage 4f).
        Assert.Equal(Core.Governance.Governments.Weight(Content, civ, Goal.Prosperity, Content.Ai.HungerWeightMax), civ.GoalWeight(Goal.Prosperity));
        Assert.Equal(Core.Governance.Governments.Weight(Content, civ, Goal.Security, Content.Ai.HungerWeightMax), civ.GoalWeight(Goal.Security));
        Assert.Equal(Core.Governance.Governments.Weight(Content, civ, Goal.Conquest, 1000), civ.GoalWeight(Goal.Conquest));
    }

    [Fact]
    public void StrategyCommandsAreChecked()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(3), Content, out _);
        CivId civ = state.Civilizations[0].Id;
        Assert.False(new SetStrategyCommand(Content, new int[2], -1, 0, 0).Validate(state, civ).Success);
        Assert.False(new SetStrategyCommand(Content, [1000, 1000, 1000, 1000, 1000, 99_999], -1, 0, 0).Validate(state, civ).Success);
        Assert.False(new SetStrategyCommand(Content, [1000, 1000, 1000, 1000, 1000, 1000], state.Worlds[0].Geometry.CellCount, 0, 0).Validate(state, civ).Success);
        Assert.True(new SetStrategyCommand(Content, [1000, 1000, 1000, 1000, 1000, 1000], 5, 0, 0).Validate(state, civ).Success);
    }

    [Fact]
    public void ArtisansNoLongerSwitchRecipesEveryTurn()
    {
        (TurnRunner runner, AiCouncil council) = NewGame(7);
        int switches = 0;
        int crafting = 0;
        for (int t = 0; t < 400; t++)
        {
            runner.RunTurn();
            switches += council.Log.Entries.Count(d => d.Action == AiAction.SetRecipe);
            crafting += runner.State.Settlements.Count(s => s.IsActive && s.RecipeId >= 0);
        }

        Assert.True(crafting > 0, "nobody crafted");
        Assert.True(switches * 10 < crafting, $"{switches} switches over {crafting} crafting settlement-turns");
    }

    [Fact]
    public void CharacterAndStrategySurviveSaving()
    {
        (TurnRunner runner, _) = NewGame(11);
        for (int t = 0; t < 20; t++)
        {
            runner.RunTurn();
        }

        using var stream = new System.IO.MemoryStream();
        SaveGameSerializer.Save(runner.State, stream);
        stream.Position = 0;
        GameState loaded = SaveGameSerializer.Load(stream);
        foreach (CivilizationState civ in runner.State.Civilizations)
        {
            CivilizationState copy = loaded.FindCivilization(civ.Id)!;
            Assert.Equal(civ.Traits, copy.Traits);
            Assert.Equal(civ.GoalWeights, copy.GoalWeights);
            Assert.Equal(civ.ExpansionTarget, copy.ExpansionTarget);
            Assert.Equal(civ.StrategyReviewMonth, copy.StrategyReviewMonth);
            Assert.Equal(civ.StrategyEra, copy.StrategyEra);
        }

        Assert.Equal(Core.Hashing.GameStateHash.Compute(runner.State), Core.Hashing.GameStateHash.Compute(loaded));
    }

    [Fact]
    public void PlacesTowardTheTargetAreBoosted()
    {
        (TurnRunner runner, AiCouncil council) = NewGame(31);
        bool seen = false;
        for (int t = 0; t < 150 && !seen; t++)
        {
            runner.RunTurn();
            seen = council.Log.Entries.Any(d => d.Factors().Any(f => f.Id == FactorId.Direction && f.PerMille == 1000 + (Content.Ai.DirectionBonusPercent * 10)));
        }

        Assert.True(seen, "no move was drawn toward an expansion target");
    }
}
