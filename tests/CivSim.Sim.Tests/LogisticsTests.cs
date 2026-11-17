using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class LogisticsTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static SimGame NewGame()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false });
        foreach (Civ c in g.Civs)
        {
            Settlement s = g.Settlements[c.Settlements[0]];
            for (int i = 0; i < s.Stock.Length; i++)
            {
                s.Stock[i] = 500 * SimContent.One;
            }
        }

        return g;
    }

    [Fact]
    public void PortersCarryTheMaterialsAndBuildingStartsWhenTheyArrive()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        BuildingType hut = Content.Building("hut");
        // The free hex farthest from the centre: the porters have a walk.
        int cell = s.Cells.Where(c => g.CannotBuild(civ, c, hut) is null).OrderByDescending(c => g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(s.Center))).First();
        int people = s.People;
        Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, hut.Id)));
        Building b = g.Buildings[g.BuildingAt[cell]];
        Assert.Equal(BuildingState.Delivery, b.State);
        Unit porters = g.Units[b.Porters];
        Assert.Equal(Content.Porters, porters.Kind);
        Assert.True(s.People < people, "porters leave their work");
        while (b.State == BuildingState.Delivery)
        {
            g.Tick();
        }

        Assert.Equal(BuildingState.Construction, b.State);
        Assert.Equal(g.Ticks + hut.BuildTicks, b.FinishTick);
        while (porters.State != UnitState.Gone)
        {
            g.Tick();
        }

        Assert.Equal(people, s.People + 0); // back at work (no growth in so short a time beyond the housing)
    }

    [Fact]
    public void AStorehouseNearTheSiteShortensTheDelivery()
    {
        static long Delivery(bool store)
        {
            SimGame g = NewGame();
            Civ civ = g.Civs[0];
            Settlement s = g.Settlements[civ.Settlements[0]];
            BuildingType hut = Content.Building("hut");
            var far = s.Cells.Where(c => g.CannotBuild(civ, c, hut) is null).OrderByDescending(c => g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(s.Center))).Take(2).ToArray();
            if (store)
            {
                BuildingType sh = Content.Building("storehouse");
                g.Apply(new BuildCommand(civ.Id, far[1], sh.Id));
                Building st = g.Buildings[g.BuildingAt[far[1]]];
                while (st.State != BuildingState.Active)
                {
                    g.Tick();
                }
            }

            g.Apply(new BuildCommand(civ.Id, far[0], hut.Id));
            Building b = g.Buildings[g.BuildingAt[far[0]]];
            long start = g.Ticks;
            while (b.State == BuildingState.Delivery)
            {
                g.Tick();
            }

            return g.Ticks - start;
        }

        Assert.True(Delivery(true) < Delivery(false));
    }

    [Fact]
    public void ARouteKeepsCarryingGoodsBetweenOurSettlements()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        HexCoord c = g.Geo.FromIndex(home.Center);
        int site = -1;
        for (int r = Content.FoundMinDistance + 1; r < 14 && site < 0; r++)
        {
            var ring = new HexCoord[HexGlobe.RingSize(r)];
            int n = g.Geo.Ring(c, r, ring);
            for (int k = 0; k < n && site < 0; k++)
            {
                int i = g.Geo.Index(ring[k]);
                if (g.CannotFound(i) is null && g.FindPath(home.Center, i, new System.Collections.Generic.List<int>()))
                {
                    site = i;
                }
            }
        }

        Settlement camp = g.FoundSettlement(civ, site, 10, new long[Content.Goods.Count]);
        int stone = Content.GoodId("stone");
        Assert.Null(g.Apply(new RouteCommand(civ.Id, home.Id, camp.Id, stone, 20 * SimContent.One)));
        for (int i = 0; i < 3 * SimContent.TicksPerMinute; i++)
        {
            g.Tick();
        }

        Assert.True(camp.Stock[stone] >= 20 * SimContent.One, $"camp has {camp.Stock[stone] / 100} stone");
    }

    [Fact]
    public void WhoDestroysCarriersTakesTheirLoad()
    {
        SimGame g = NewGame();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        Settlement sa = g.Settlements[a.Settlements[0]];
        Settlement sb = g.Settlements[b.Settlements[0]];
        sb.People = 60;
        BuildingType hut = Content.Building("hut");
        int cell = sa.Cells.Where(x => g.CannotBuild(a, x, hut) is null).OrderByDescending(x => g.Geo.Distance(g.Geo.FromIndex(x), g.Geo.FromIndex(sa.Center))).First();
        g.Apply(new BuildCommand(a.Id, cell, hut.Id));
        Unit porters = g.Units[g.Buildings[g.BuildingAt[cell]].Porters];
        Assert.Null(g.Apply(new TrainCommand(b.Id, sb.Id, Content.Unit("warriors").Id)));
        for (int i = 0; i <= Content.Unit("warriors").TrainTicks + 1; i++)
        {
            g.Tick();
        }

        Unit w = g.Units.Last(u => u.Civ == b.Id);
        w.Cell = porters.Cell;
        long wood = sb.Stock[Content.GoodId("wood")];
        g.Apply(new AttackCommand(b.Id, w.Id, porters.Cell));
        for (int i = 0; i < 100 && porters.State != UnitState.Gone; i++)
        {
            g.Tick();
        }

        Assert.Equal(UnitState.Gone, porters.State);
        Assert.True(sb.Stock[Content.GoodId("wood")] > wood || sb.Stock[Content.GoodId("wood")] >= sb.Capacity[Content.GoodId("wood")], "the wood was seized");
    }

    [Fact]
    public void TheTreeHas45BaseTechnologiesAndEraUpgrades()
    {
        Assert.Equal(45, Content.Techs.Count(t => !t.Upgrade));
        Assert.True(Content.Techs.Count(t => t.Upgrade) >= 10);
        foreach (SimEra era in Content.Eras)
        {
            Assert.Equal(9, Content.Techs.Count(t => !t.Upgrade && t.Era == era.Id));
        }
    }

    [Fact]
    public void BronzeWeaponsMakeMeleeStronger()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        s.People = 60;
        g.Apply(new TrainCommand(civ.Id, s.Id, Content.Unit("warriors").Id));
        Unit u = g.Units[^1];
        long before = g.Power(u);
        civ.KnownTech[Content.TechId("bronze_weapons")] = true;
        Assert.True(g.Power(u) > before);
    }
}
