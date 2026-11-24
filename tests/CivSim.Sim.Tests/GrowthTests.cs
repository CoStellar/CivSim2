using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Variant C (Patryk, 2026-11-22): no growth while the food falls and less than two rations are left.</summary>
public sealed class GrowthTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void FallingFoodWithLessThanTwoRationsStopsGrowth()
    {
        (SimGame g, Settlement s) = NewCamp();
        s.Eating = 30 * SimContent.One; // a ration of 30
        s.Rate[Content.Food] = -5 * SimContent.One;
        s.Stock[Content.Food] = 59 * SimContent.One;
        Assert.Equal(2, Content.GrowthStopRations);
        Assert.True(g.FoodStopsGrowth(s));

        s.Stock[Content.Food] = 60 * SimContent.One; // two rations left: it still grows
        Assert.False(g.FoodStopsGrowth(s));
    }

    [Fact]
    public void ASettlementWhoseFoodGrowsKeepsGrowingWithALowStore()
    {
        (SimGame g, Settlement s) = NewCamp();
        s.Eating = 30 * SimContent.One;
        s.Rate[Content.Food] = 1 * SimContent.One;
        s.Stock[Content.Food] = 5 * SimContent.One;
        Assert.False(g.FoodStopsGrowth(s));
    }

    [Fact]
    public void ThePanelSaysWhyTheSettlementDoesNotGrow()
    {
        (SimGame g, Settlement s) = NewCamp();
        s.People = 10;
        s.Housing = 25;
        s.Eating = 30 * SimContent.One;
        s.Rate[Content.Food] = -5 * SimContent.One;
        s.Stock[Content.Food] = 40 * SimContent.One;
        s.GrowthStopped = true;

        Assert.Contains("does not grow", g.FoodBalance(g.Civs[0], s), System.StringComparison.Ordinal);
    }

    [Fact]
    public void AStarvingCampStopsGrowingInAGame()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Settlement s = g.Settlements[g.Civs[0].Settlements[0]];
        s.Gathering = false; // nobody finds food: the store only falls
        s.People = 10; // homes are free
        s.Stock[Content.Food] = 15 * SimContent.One; // under two rations of ten people
        g.Civs[0].Dirty = true;
        for (int i = 0; i < SimContent.TicksPerMinute && !s.GrowthStopped; i++)
        {
            g.Tick();
        }

        Assert.True(s.GrowthStopped);
        int people = s.People;
        for (int i = 0; i < 2 * SimContent.TicksPerMinute; i++)
        {
            g.Tick();
            Assert.True(s.People <= people, "a settlement with falling food and few rations grew");
        }
    }

    [Fact]
    public void EmptyFoodJobsWithNobodyIdleAskToFreeWorkersNotToBuildMore()
    {
        // P6 (tests 2026-11-24): another hut would stand empty too; the panel says to free workers instead.
        (SimGame g, Settlement s) = NewCamp();
        Civ civ = g.Civs[0];
        BuildingType hut = Content.Building("gatherers_hut");
        int cell = System.Linq.Enumerable.First(s.Cells, c => c != s.Center && g.FreeSlots(c) == 15);
        var b = new Building { Id = g.Buildings.Count, Civ = civ.Id, Type = hut.Id, Cell = cell, Settlement = s.Id, State = BuildingState.Active, Workers = 0 };
        g.Buildings.Add(b);
        civ.Buildings.Add(b.Id);
        g.PlaceForTest(b);
        s.Eating = 30 * SimContent.One;
        s.Rate[Content.Food] = -5 * SimContent.One;
        s.Stock[Content.Food] = 40 * SimContent.One;

        s.Idle = 0;
        Assert.Contains("nobody is idle", g.FoodBalance(civ, s), System.StringComparison.Ordinal);
        Assert.Contains("nobody is idle", g.HungerAdvice(civ, s), System.StringComparison.Ordinal);

        s.Idle = 5; // people without work: building more is right again
        Assert.DoesNotContain("nobody is idle", g.FoodBalance(civ, s), System.StringComparison.Ordinal);
    }

    private static (SimGame G, Settlement S) NewCamp()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        return (g, g.Settlements[g.Civs[0].Settlements[0]]);
    }
}
