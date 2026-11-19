using System;
using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class EconomyTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static SimGame NewGame(ulong seed = 1)
    {
        SimGame g = SimGame.Create(Content, new SimSettings(seed, 66, 40, 2) { Ai = false }).Basics();
        foreach (Civ civ in g.Civs)
        {
            civ.KnownTech[Content.TechId("stone_tools")] = true;
            civ.KnownTech[Content.TechId("agriculture")] = true;
        }

        return g;
    }

    private static int FreeCell(SimGame g, Civ civ, BuildingType t) =>
        g.Settlements[civ.Settlements[0]].Cells.First(c => g.CannotBuild(civ, c, t, checkPrice: false) is null);

    // Runs until a building is past the porters' walk and its construction (or a limit).
    private static void Finish(SimGame g, Building b, int limit = 3000)
    {
        for (int i = 0; i < limit && b.State is BuildingState.Delivery or BuildingState.Construction; i++)
        {
            g.Tick();
        }
    }

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    [Fact]
    public void ANewGameGivesEveryPeopleACampWithLand()
    {
        SimGame g = NewGame();
        Assert.Equal(2, g.Civs.Count);
        foreach (Civ civ in g.Civs)
        {
            Settlement s = g.Settlements[civ.Settlements[0]];
            Assert.Equal(Content.StartPeople, s.People);
            Assert.True(s.Cells.Count >= 10, $"camp has {s.Cells.Count} hexes");
            Assert.All(s.Cells, c => Assert.Equal(civ.Id, g.OwnerAt[c]));
        }
    }

    [Fact]
    public void AHutOfGatherersFeedsPeopleOnceItIsBuilt()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        BuildingType hut = Content.Building("gatherers_hut");
        g.Recompute(civ);
        long before = civ.Rate[Content.Food];
        int cell = FreeCell(g, civ, hut);
        Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, hut.Id)));
        Assert.Equal(Content.StartStock[Content.GoodId("wood")] - hut.Price[Content.GoodId("wood")], civ.Stock[Content.GoodId("wood")]);
        Building b = g.Buildings[g.BuildingAt[cell]];
        Finish(g, b);
        Run(g, Content.EconomyEveryTicks);
        Assert.Equal(BuildingState.Active, b.State);
        Assert.Equal(hut.Workers, b.Workers);
        Assert.True(civ.Rate[Content.Food] > before, $"food rate {before} -> {civ.Rate[Content.Food]}");
    }

    [Fact]
    public void BuildingNeedsOwnLandFreeHexAndGoods()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        BuildingType hut = Content.Building("hut");
        Settlement s = g.Settlements[civ.Settlements[0]];
        Assert.NotNull(g.Apply(new BuildCommand(civ.Id, s.Center, hut.Id)));
        Assert.NotNull(g.Apply(new BuildCommand(civ.Id, g.Settlements[g.Civs[1].Settlements[0]].Cells[1], hut.Id)));
        Array.Clear(g.Settlements[civ.Settlements[0]].Stock);
        int cell = FreeCell(g, civ, hut);
        Assert.Contains("Not enough goods", g.Apply(new BuildCommand(civ.Id, cell, hut.Id)), StringComparison.Ordinal);
    }

    [Fact]
    public void APausedConstructionWaitsAndCostsMoreToResume()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        BuildingType hut = Content.Building("hut");
        int cell = FreeCell(g, civ, hut);
        g.Apply(new BuildCommand(civ.Id, cell, hut.Id));
        int id = g.BuildingAt[cell];
        while (g.Buildings[id].State == BuildingState.Delivery)
        {
            g.Tick();
        }

        Run(g, 3);
        Assert.Null(g.Apply(new PauseConstructionCommand(civ.Id, id, true)));
        Run(g, hut.BuildTicks * 2);
        Assert.Equal(BuildingState.ConstructionPaused, g.Buildings[id].State);
        long wood = civ.Stock[Content.GoodId("wood")];
        Assert.Null(g.Apply(new PauseConstructionCommand(civ.Id, id, false)));
        Assert.Equal(wood - (hut.Price[Content.GoodId("wood")] * Content.ResumeCostPercent / 100), civ.Stock[Content.GoodId("wood")]);
        Run(g, hut.BuildTicks);
        Assert.Equal(BuildingState.Active, g.Buildings[id].State);
    }

    [Fact]
    public void AToolmakerWithoutStoneSleepsAndWakesWhenStoneComes()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        BuildingType tm = Content.Building("toolmaker");
        int stone = Content.GoodId("stone");
        g.Settlements[civ.Settlements[0]].Stock[stone] = 100 * SimContent.One;
        int cell = FreeCell(g, civ, tm);
        Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, tm.Id)));
        Building b = g.Buildings[g.BuildingAt[cell]];
        Finish(g, b);
        g.Settlements[civ.Settlements[0]].Stock[stone] = 1 * SimContent.One;
        g.Settlements[civ.Settlements[0]].Stock[Content.GoodId("wood")] = 90 * SimContent.One;
        Run(g, SimContent.TicksPerMinute / 2);
        Assert.True(b.Starved);
        Assert.True(g.Settlements[civ.Settlements[0]].Stock[stone] < SimContent.One);
        g.Settlements[civ.Settlements[0]].Stock[stone] = 50 * SimContent.One;
        Run(g, Content.EconomyEveryTicks * 2);
        Assert.False(b.Starved);
        Assert.True(civ.Rate[Content.GoodId("tools")] > 0);
    }

    [Fact]
    public void PeopleGrowToTheHousingWhenFedAndDieOffWhenHungry()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        g.Settlements[civ.Settlements[0]].Stock[Content.Food] = 300 * SimContent.One;
        Run(g, SimContent.TicksPerMinute);
        Assert.True(s.People > Content.StartPeople, $"people {s.People}");
        Assert.True(s.People <= s.Housing);
        // No food and nobody gathering: the next ration leaves them hungry, the one after kills some (rations, 2026-11-19).
        s.Gathering = false;
        civ.Dirty = true;
        int before = s.People;
        for (int i = 0; i < 3 * SimContent.TicksPerMinute; i++)
        {
            s.Stock[Content.Food] = 0;
            g.Tick();
        }

        Assert.True(civ.Hungry);
        Assert.True(s.People < before, $"people {before} -> {s.People}");
    }

    [Fact]
    public void UpgradingACampWidensItsLand()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        int before = s.Cells.Count;
        Assert.NotNull(g.Apply(new UpgradeSettlementCommand(civ.Id, s.Id)));
        for (int i = 0; i < civ.Stock.Length; i++)
        {
            g.Settlements[civ.Settlements[0]].Stock[i] = 200 * SimContent.One;
        }

        Assert.Null(g.Apply(new UpgradeSettlementCommand(civ.Id, s.Id)));
        Assert.Equal(1, s.Level);
        Assert.True(s.Cells.Count > before);
    }

    [Fact]
    public void TheSameSeedAndCommandsGiveTheSameGame()
    {
        ulong Play()
        {
            SimGame g = NewGame(9);
            foreach (Civ civ in g.Civs)
            {
                BuildingType hut = Content.Building("gatherers_hut");
                g.Apply(new BuildCommand(civ.Id, FreeCell(g, civ, hut), hut.Id));
            }

            Run(g, 3 * SimContent.TicksPerMinute);
            return g.StateHash();
        }

        Assert.Equal(Play(), Play());
    }
}
