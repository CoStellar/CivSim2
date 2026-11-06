using System;
using CivSim.Core.Content;
using CivSim.Core.Perception;
using CivSim.Core.Research;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>What technologies are worth to the scholar, and what a civilization sees now (AI rework, PR B).</summary>
public class AiValueTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static (GameState State, CivilizationState Civ) NewCiv()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(9), Content, out WorldGenReport _);
        CivilizationState civ = state.Civilizations[0];
        Technologies.EnsureSize(Content, civ);
        return (state, civ);
    }

    // A zone of 30 land cells and 100 people with nothing special in it.
    private static CivFacts Plain() => new()
    {
        People = 100,
        Goods = new long[Content.Goods.Count],
        DepositCells = new int[Content.Deposits.Count],
        BiomeCells = new int[Content.Biomes.Count],
        LandCells = 30,
    };

    [Fact]
    public void FishingIsWorthMoreOnTheCoast()
    {
        (GameState state, CivilizationState civ) = NewCiv();
        var scholar = new ScholarAdvisor(Content);
        int fishing = Content.Technology("fishing").Id;
        CivFacts coast = Plain();
        coast.BiomeCells[Content.Biome("coast").Id] = 12;

        long inland = scholar.Worths(state, civ, Plain(), 0)[fishing];
        long shore = scholar.Worths(state, civ, coast, 0)[fishing];

        Assert.True(shore > inland * 3, $"coast {shore}, inland {inland}");
    }

    [Fact]
    public void FireIsWorthMoreInTheCold()
    {
        (GameState state, CivilizationState civ) = NewCiv();
        var scholar = new ScholarAdvisor(Content);
        int fire = Content.Technology("fire").Id;
        CivFacts warm = Plain();
        warm.ColdestWinter = 10;
        CivFacts cold = Plain();
        cold.ColdestWinter = -20;

        Assert.True(scholar.Worths(state, civ, cold, 0)[fire] > scholar.Worths(state, civ, warm, 0)[fire]);
    }

    [Fact]
    public void FarmingIsWorthLessOnBarrenLand()
    {
        (GameState state, CivilizationState civ) = NewCiv();
        var scholar = new ScholarAdvisor(Content);
        int agriculture = Content.Technology("agriculture").Id;
        CivFacts fertile = Plain();
        fertile.FarmYield = 30 * 150; // the best land everywhere
        CivFacts barren = Plain();

        Assert.True(scholar.Worths(state, civ, fertile, 0)[agriculture] > scholar.Worths(state, civ, barren, 0)[agriculture]);
    }

    [Fact]
    public void CuriosityEasesTheFearOfCost()
    {
        var scholar = new ScholarAdvisor(Content);

        Assert.Equal(1000, scholar.CostFactor(100, 100, 0));
        Assert.Equal(250, scholar.CostFactor(100, 200, 0));    // twice as dear: a quarter
        Assert.Equal(375, scholar.CostFactor(100, 200, 500));
        Assert.Equal(500, scholar.CostFactor(100, 200, 1000)); // full curiosity: a half
    }

    [Fact]
    public void ACivilizationSeesNowOnlyWhatItSawInItsLastStep()
    {
        (GameState state, CivilizationState civ) = NewCiv();
        civ.StepStartMonth = 600; // its step began in year 50
        Array.Clear(civ.SeenYear);
        civ.SeenYear[5] = 51;     // seen in year 50 (years are stored plus one)
        civ.SeenYear[6] = 50;     // seen in year 49, before the step

        Assert.True(MapMemory.SeesNow(state, civ, 5));
        Assert.False(MapMemory.SeesNow(state, civ, 6));
        Assert.False(MapMemory.SeesNow(state, civ, 7)); // never seen
    }
}
