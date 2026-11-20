using System;
using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Supplies of the army in the field (Patryk, 2026-11-20, question 1).</summary>
public sealed class SupplyTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static (SimGame G, Unit U, Settlement Home) Army(int people)
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        home.People = 200;
        for (int i = 0; i < home.Stock.Length; i++)
        {
            home.Stock[i] = 900 * SimContent.One;
        }

        UnitType t = Content.Unit("warriors");
        civ.KnownTech[t.Tech] = true;
        Assert.Null(g.Apply(new TrainCommand(0, home.Id, t.Id, people)));
        Unit u = g.Units[^1];
        for (int i = 0; i < 5000 && u.State == UnitState.Training; i++)
        {
            g.Tick();
        }

        return (g, u, home);
    }

    // Open land far from every people's land.
    private static int Wild(SimGame g) => Enumerable.Range(0, g.Geo.CellCount).First(c => !Content.World.Biomes[g.Map.Biome[c]].IsWater
        && g.Settlements.All(s => g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(s.Center)) > 12));

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    [Fact]
    public void FoodForAUnitWithNoWayToItIsRefused()
    {
        // A2 (tests 2026-11-20): the caravan searched the whole map up to 7 times, went home, and the AI sent the next one.
        (SimGame g, Unit u, Settlement home) = Army(10);
        int island = Enumerable.Range(0, g.Geo.CellCount).First(c => !Content.World.Biomes[g.Map.Biome[c]].IsWater
            && g.Settlements.All(s => g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(s.Center)) > 12)
            && !g.FindPath(home.Center, c, new System.Collections.Generic.List<int>(), false, 0));
        g.Place(u, island);
        u.Provisions = 0;
        int carriers = g.Units.Count;
        string? why = g.Apply(new SupplyUnitCommand(0, home.Id, u.Id, 50 * SimContent.One));
        Assert.NotNull(why);
        Assert.StartsWith("No way to our", why, StringComparison.Ordinal);
        Assert.Equal(carriers, g.Units.Count); // no caravan left
        Assert.Equal(g.Ticks, u.SupplyNoWayTick);
    }

    [Fact]
    public void FoodForAUnitWithFullPacksIsRefused()
    {
        (SimGame g, Unit u, Settlement home) = Army(10);
        g.Place(u, Wild(g));
        Assert.Equal("Our warriors have full packs.", g.Apply(new SupplyUnitCommand(0, home.Id, u.Id, 50 * SimContent.One)));
    }

    [Fact]
    public void ANewUnitLeavesWithFullPacks()
    {
        (SimGame g, Unit u, _) = Army(10);
        Assert.Equal(g.ProvisionsMax(u), u.Provisions);
        Assert.Equal(10L * Content.FoodPerPersonPerMinute * Content.Supply.CarryMinutes, g.ProvisionsMax(u));
        Assert.True(g.InSupply(u));
    }

    [Fact]
    public void InTheFieldTheUnitEatsItsPacksAndThenGoesHungry()
    {
        (SimGame g, Unit u, _) = Army(30);
        g.Place(u, Wild(g));
        Assert.False(g.InSupply(u));
        long packs = u.Provisions;
        Run(g, Content.Rations.EveryTicks + 5);
        Assert.True(u.Provisions < packs, "it ate from its packs");
        Assert.Equal(0, u.Hunger);
        Run(g, (Content.Supply.CarryMinutes + 2) * Content.Rations.EveryTicks);
        Assert.Equal(0, u.Provisions);
        Assert.True(u.Hunger > 0 || u.People < 30);
    }

    [Fact]
    public void ASmallUnitLivesOffTheLandLonger()
    {
        (SimGame g, Unit small, _) = Army(5);
        Unit big = g.Units[0];
        int cell = Wild(g);
        Assert.True(g.LandFood(small) > 0 || Content.World.Biomes[g.Map.Biome[cell]].IsWater);
        g.Place(small, cell);
        long land = g.LandFood(small);
        long eat = 5L * Content.FoodPerPersonPerMinute * Content.Rations.EveryTicks / SimContent.TicksPerMinute;
        Assert.True(land <= eat * 2);
        Assert.NotNull(big);
    }

    [Fact]
    public void AFoodCaravanFillsThePacksInTheField()
    {
        (SimGame g, Unit u, Settlement home) = Army(10);
        int cell = Enumerable.Range(0, g.Geo.CellCount).First(c => !Content.World.Biomes[g.Map.Biome[c]].IsWater && g.OwnerAt[c] < 0
            && g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(home.Center)) == 8 && g.FindPath(home.Center, c, new System.Collections.Generic.List<int>(), false, 0));
        g.Place(u, cell);
        u.Provisions = 0;
        Assert.Null(g.Apply(new SupplyUnitCommand(0, home.Id, u.Id, 30 * SimContent.One)));
        for (int i = 0; i < 3 * SimContent.TicksPerMinute && u.Provisions == 0; i++)
        {
            g.Tick();
        }

        Assert.True(u.Provisions > 0, "the caravan reached the unit");
    }
}
