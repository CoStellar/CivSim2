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
