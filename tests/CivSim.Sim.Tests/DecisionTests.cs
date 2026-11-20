using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Sim.Ai;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Rules decided after the tests of 2026-11-19 (claude/decyzje-po-testach.md).</summary>
public sealed class DecisionTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void AnEraNeedsItsGatewayTechnology()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        civ.Era = 1;
        int gate = Content.GatewayOf(1);
        Assert.Equal("copper_working", Content.Techs[gate].Key);
        foreach (SimTech t in Content.Techs.Where(t => t.Era == 1 && !t.Basic && t.Id != gate).Take(Content.EraAdvanceTechs + 1))
        {
            civ.KnownTech[t.Id] = true;
        }

        g.AdvanceEra(civ, tell: false);
        Assert.Equal(1, civ.Era); // enough technologies, but no gateway
        civ.KnownTech[gate] = true;
        g.AdvanceEra(civ, tell: false);
        Assert.Equal(2, civ.Era);
    }

    [Fact]
    public void TheAiPaysNoTributeBeforeItLostPeople()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        a.Known[g.Settlements[b.Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(a.Id, b.Id)));
        Assert.Null(g.Apply(new ProposePeaceCommand(a.Id, b.Id, 5)));
        b.IsAi = true;
        SimAi.Think(g, b);
        Assert.True(g.AtWar(a.Id, b.Id)); // refused: no battle yet (E1)
        Assert.Null(g.Apply(new ProposePeaceCommand(a.Id, b.Id, 0)));
        SimAi.Think(g, b);
        Assert.False(g.AtWar(a.Id, b.Id)); // a plain peace is fine
    }

    [Fact]
    public void ARouteKeepsWhatTheSourceNeeds()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        int wood = Content.GoodId("wood");
        var t = new TradeRoute { Civ = civ.Id, From = home.Id, To = home.Id, Good = wood, Amount = 20 * SimContent.One, KeepAtSource = 50 * SimContent.One };
        Assert.Equal(50 * SimContent.One, t.KeepAtSource);
        Assert.NotNull(g.Apply(new RouteCommand(civ.Id, home.Id, home.Id, wood, 20 * SimContent.One, null, 50 * SimContent.One)));
    }

    [Fact]
    public void ADepositOfALaterEraShowsAsUnknownUntilThatEra()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        int iron = Content.World.Deposits.First(d => d.Key == "iron_ore").Id;
        int cell = home.Cells.First(c => c != home.Center);
        g.Map.Deposit.Set(cell, (ushort)iron);
        civ.Era = 1;
        Assert.True(g.DepositHidden(civ, cell));
        Assert.False(g.KnowsDeposit(civ, cell));
        civ.Era = Content.DepositEra[iron];
        Assert.False(g.DepositHidden(civ, cell));
        Assert.True(g.KnowsDeposit(civ, cell)); // learnt with the era, nobody had to look again (M6)
    }

    [Fact]
    public void AnEmptySettlementFallsToRuinAndSettlersTakeIt()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        int site = Enumerable.Range(0, g.Geo.CellCount).First(c => g.OwnerAt[c] < 0 && !Content.World.Biomes[g.Map.Biome[c]].IsWater
            && g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(home.Center)) == 9);
        Settlement camp = g.FoundSettlement(civ, site, 0);
        Assert.Equal(2, civ.Settlements.Count);
        for (int i = 0; i < (Content.RuinEmptySeconds + 3) * SimContent.TicksPerSecond; i++)
        {
            camp.People = 0;
            g.Tick();
        }

        Assert.True(camp.Abandoned);
        Assert.DoesNotContain(camp.Id, civ.Settlements);
        Assert.Equal(-1, g.OwnerAt[camp.Center]);

        // Settlers of the other people take it.
        Civ other = g.Civs[1];
        Settlement theirs = g.Settlements[other.Settlements[0]];
        theirs.People = 100;
        for (int k = 0; k < theirs.Stock.Length; k++)
        {
            theirs.Stock[k] = 500 * SimContent.One;
        }

        other.KnownTech[Content.TechId("migration")] = true;
        Assert.Null(g.Apply(new TrainCommand(other.Id, theirs.Id, Content.Unit("settlers").Id)));
        Unit settlers = g.Units[^1];
        for (int i = 0; i < 5000 && settlers.State == UnitState.Training; i++)
        {
            g.Tick();
        }

        g.Place(settlers, camp.Center);
        Assert.Null(g.Apply(new FoundCommand(other.Id, settlers.Id)));
        Assert.False(camp.Abandoned);
        Assert.Equal(other.Id, camp.Civ);
        Assert.Contains(camp.Id, other.Settlements);
    }

    [Fact]
    public void UpgradeGoodsComeByCaravanFirst()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        int site = Enumerable.Range(0, g.Geo.CellCount).First(c => g.OwnerAt[c] < 0 && !Content.World.Biomes[g.Map.Biome[c]].IsWater
            && g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(home.Center)) == 8 && g.FindPath(home.Center, c, new List<int>(), false, civ.Id));
        Settlement camp = g.FoundSettlement(civ, site, 50);
        Array.Clear(home.Stock);
        for (int k = 0; k < camp.Stock.Length; k++)
        {
            camp.Stock[k] = 400 * SimContent.One;
        }

        home.People = 45;
        civ.KnownTech[Content.TechId("agriculture")] = true;
        Assert.Null(g.Apply(new UpgradeSettlementCommand(civ.Id, home.Id)));
        Assert.True(home.UpgradeWaiting);
        Assert.Equal(0, home.Level);
        for (int i = 0; i < 3 * SimContent.TicksPerMinute && home.Level == 0; i++)
        {
            home.People = 45;
            g.Tick();
        }

        Assert.Equal(1, home.Level); // the caravans came and the camp became a village
    }

    [Fact]
    public void TheAiBringsAWorkshopTheGoodsItUses()
    {
        // AI goods (2026-11-20): goods lie in each settlement; a potter with no clay at home gets it from the other camp.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        int site = Enumerable.Range(0, g.Geo.CellCount).First(c => g.OwnerAt[c] < 0 && !Content.World.Biomes[g.Map.Biome[c]].IsWater
            && g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(home.Center)) == 8 && g.FindPath(home.Center, c, new List<int>(), false, civ.Id));
        Settlement camp = g.FoundSettlement(civ, site, 30);
        civ.KnownTech[Content.TechId("pottery")] = true;
        BuildingType potter = Content.Building("potter");
        int clay = Content.GoodId("clay");
        home.Stock[Content.GoodId("wood")] = 100 * SimContent.One;
        home.Stock[Content.GoodId("stone")] = 100 * SimContent.One;
        home.People = 40;
        int cell = home.Cells.First(c => g.CannotBuild(civ, c, potter, checkPrice: false) is null);
        Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, potter.Id)));
        Building b = g.Buildings[g.BuildingAt[cell]];
        for (int i = 0; i < 3000 && b.State is BuildingState.Delivery or BuildingState.Construction; i++)
        {
            g.Tick();
        }

        for (int i = 0; i < Content.EconomyEveryTicks * 2; i++)
        {
            g.Tick();
        }

        Assert.True(b.Workers > 0);
        home.Stock[clay] = 0;
        camp.Stock[clay] = 100 * SimContent.One;
        SimAi.Think(g, civ);
        Assert.Contains(civ.Units, id => g.Units[id].Target == home.Id && g.Units[id].Cargo is { } load && load[clay] > 0);
    }

    [Fact]
    public void APeopleFightsAtMostMaxWarsAtOnce()
    {
        // Design chapter 8 (2026-11-20 sprint): at most war.maxWars wars at once.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, Content.War.MaxWars + 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        for (int o = 1; o < g.Civs.Count; o++)
        {
            civ.Known[g.Settlements[g.Civs[o].Settlements[0]].Center] = true;
            g.Civs[o].Known[g.Settlements[civ.Settlements[0]].Center] = true;
        }

        for (int o = 1; o <= Content.War.MaxWars; o++)
        {
            Assert.Null(g.Apply(new DeclareWarCommand(0, o)));
        }

        Assert.Equal(Content.War.MaxWars, g.WarsOf(0));
        Assert.NotNull(g.Apply(new DeclareWarCommand(0, Content.War.MaxWars + 1)));
        Assert.NotNull(g.Apply(new DeclareWarCommand(Content.War.MaxWars + 1, 0))); // nor may others join in on us
    }
}
