using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Ai;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>AI food of big peoples (2026-11-22): a winter margin, several food sites, caravans sized to the settlement.</summary>
public sealed class AiFoodTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void FoodMadeMustCoverEatingPlusTheWinterMargin()
    {
        (SimGame g, Civ civ) = NewGame();
        long eating = 4000 * SimContent.One;
        Assert.False(g.IsWinter);
        civ.MadeRate[Content.Food] = eating * 110 / 100; // a fixed +3 a minute was enough before; 10% is not
        Assert.True(SimAi.ShortOfFood(g, civ, eating));
        civ.MadeRate[Content.Food] = eating * (100 + Content.AiFoodMarginPercent) / 100;
        Assert.False(SimAi.ShortOfFood(g, civ, eating));
    }

    [Fact]
    public void InWinterTheMarginIsNotAsked()
    {
        (SimGame g, Civ civ) = NewGame();
        while (!g.IsWinter)
        {
            g.Tick();
        }

        civ.MadeRate[Content.Food] = 0; // winter food is low anyway: the summer decides
        Assert.False(SimAi.ShortOfFood(g, civ, 4000 * SimContent.One));
    }

    [Fact]
    public void ALeanPeopleDoesNotUpgradeItsSettlements()
    {
        (SimGame g, Civ civ) = NewGame();
        Settlement home = g.Settlements[civ.Settlements[0]];
        civ.Era = 1;
        foreach (SimTech t in Content.Techs)
        {
            civ.KnownTech[t.Id] = true;
        }

        for (int i = 0; i < civ.Stock.Length; i++)
        {
            civ.Stock[i] = 100_000 * SimContent.One;
            home.Stock[i] = 100_000 * SimContent.One;
        }

        home.People = Math.Max(home.Housing, 60);
        home.Eating = home.People * SimContent.One;
        civ.MadeRate[Content.Food] = home.Eating; // made = eaten: nothing left for the winter
        Assert.True(SimAi.Lean(g, civ));
        Assert.False(SimAi.Upgrade(g, civ));
        civ.MadeRate[Content.Food] = home.Eating * 2; // the same people with food to spare upgrades
        Assert.False(SimAi.Lean(g, civ));
        Assert.True(SimAi.Upgrade(g, civ));
    }

    [Fact]
    public void AHungrySettlementGetsTwoMinutesOfItsEatingByCaravan()
    {
        (SimGame g, Civ civ) = NewGame();
        Settlement home = g.Settlements[civ.Settlements[0]];
        Settlement camp = g.FoundSettlement(civ, FreeSite(g, home), 10, new long[Content.Goods.Count]);
        home.Stock[Content.Food] = 2000 * SimContent.One;
        home.Eating = 100 * SimContent.One;
        camp.Stock[Content.Food] = 0;
        camp.Eating = 300 * SimContent.One;
        camp.Hungry = true;

        SimAi.Supply(g, civ);

        Unit caravan = g.Units.Last(u => u.Civ == civ.Id && u.Cargo is not null && u.Target == camp.Id);
        Assert.Equal(600 * SimContent.One, caravan.Cargo![Content.Food]); // was a fixed 40
        Assert.Equal(1400 * SimContent.One, home.Stock[Content.Food]);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void TheRichSettlementKeepsTwoMinutesOfItsOwnEating()
    {
        (SimGame g, Civ civ) = NewGame();
        Settlement home = g.Settlements[civ.Settlements[0]];
        Settlement camp = g.FoundSettlement(civ, FreeSite(g, home), 10, new long[Content.Goods.Count]);
        home.Stock[Content.Food] = 500 * SimContent.One;
        home.Eating = 200 * SimContent.One;
        camp.Stock[Content.Food] = 0;
        camp.Eating = 300 * SimContent.One;
        camp.Hungry = true;

        SimAi.Supply(g, civ);

        Unit caravan = g.Units.Last(u => u.Civ == civ.Id && u.Cargo is not null && u.Target == camp.Id);
        Assert.Equal(100 * SimContent.One, caravan.Cargo![Content.Food]);
    }

    [Fact]
    public void AWorkshopShortOfItsInputGetsAStandingRouteFromTheSettlementThatHasIt()
    {
        // Known issue 13 (2026-11-23): steady routes instead of single caravans for a workshop's inputs.
        (SimGame g, Civ civ) = NewGame();
        Settlement home = g.Settlements[civ.Settlements[0]];
        Settlement camp = g.FoundSettlement(civ, FreeSite(g, home), 20, new long[Content.Goods.Count]);
        BuildingType potter = Content.Building("potter");
        int cell = camp.Cells.First(c => c != camp.Center && g.FreeSlots(c) == 15);
        var b = new Building { Id = g.Buildings.Count, Civ = civ.Id, Type = potter.Id, Cell = cell, Settlement = camp.Id, State = BuildingState.Active, Workers = potter.Workers };
        g.Buildings.Add(b);
        civ.Buildings.Add(b.Id);
        g.PlaceForTest(b);
        int clay = Content.GoodId("clay");
        home.Stock[clay] = 300 * SimContent.One;
        camp.Stock[clay] = 0;

        SimAi.Supply(g, civ);

        TradeRoute route = Assert.Single(g.Routes, r => r.Civ == civ.Id && !r.Ended && r.Good == clay);
        Assert.Equal(home.Id, route.From);
        Assert.Equal(camp.Id, route.To);
        long use = potter.Consumes[clay] * potter.Workers;
        Assert.Equal(use * Content.AiInputRouteTripMinutes, route.Amount);
        Assert.Equal(use * Content.AiInputRouteTargetMinutes, route.UntilTargetHas);

        // Nothing to spare at the source any more: the AI ends the route, so a better source can take over.
        home.Stock[clay] = 0;
        SimAi.Supply(g, civ);
        Assert.True(route.Ended);
    }

    [Fact]
    public void ABigStoreDoesNotStopTheNextMakerWhileTheShortfallLasts()
    {
        // 2026-11-23: the next charcoal burner goes up until the store lasts ai.inputMakerStoreMinutes of the shortfall.
        BuildingType smith = Content.Building("bronze_smith");
        int charcoal = Content.GoodId("charcoal");
        for (ulong seed = 1; seed < 30; seed++)
        {
            SimGame g = SimGame.Create(Content, new SimSettings(seed, 66, 40, 2) { Ai = false }).Basics();
            Civ civ = g.Civs[0];
            civ.KnownTech[Content.Building("charcoal_burner").Tech] = true;
            civ.KnownTech[smith.Tech] = true;
            Settlement home = g.Settlements[civ.Settlements[0]];
            Array.Fill(home.Stock, 500 * SimContent.One);
            for (int k = 0; k < 4; k++)
            {
                int cell = home.Cells.First(c => c != home.Center && g.FreeSlots(c) == 15);
                var b = new Building { Id = g.Buildings.Count, Civ = civ.Id, Type = smith.Id, Cell = cell, Settlement = home.Id, State = BuildingState.Active };
                g.Buildings.Add(b);
                civ.Buildings.Add(b.Id);
                g.PlaceForTest(b);
            }

            g.SyncForTest(civ);
            long shortfall = 4 * smith.Consumes[charcoal] * smith.Workers; // nobody makes charcoal yet
            civ.Stock[charcoal] = 0;
            if (SimAi.InputMaker(g, civ, smith) != "charcoal_burner")
            {
                continue; // no place for a burner on this map
            }

            civ.Stock[charcoal] = shortfall * (Content.AiInputMakerStoreMinutes - 1); // far above the old limit of 20
            Assert.True(civ.Stock[charcoal] > 20 * SimContent.One);
            Assert.Equal("charcoal_burner", SimAi.InputMaker(g, civ, smith));
            civ.Stock[charcoal] = shortfall * Content.AiInputMakerStoreMinutes;
            Assert.Null(SimAi.InputMaker(g, civ, smith));
            return;
        }

        Assert.Fail("no map with a place for a charcoal burner");
    }

    private static (SimGame G, Civ Civ) NewGame()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        return (g, g.Civs[0]);
    }

    // A hex near home where a settlement may stand and that home reaches on foot.
    private static int FreeSite(SimGame g, Settlement home)
    {
        HexCoord c = g.Geo.FromIndex(home.Center);
        for (int r = Content.FoundMinDistance + 1; r < 14; r++)
        {
            var ring = new HexCoord[HexGlobe.RingSize(r)];
            int n = g.Geo.Ring(c, r, ring);
            for (int k = 0; k < n; k++)
            {
                int i = g.Geo.Index(ring[k]);
                if (g.CannotFound(i) is null && g.FindPath(home.Center, i, new List<int>()))
                {
                    return i;
                }
            }
        }

        throw new Xunit.Sdk.XunitException("no free site near home");
    }
}
