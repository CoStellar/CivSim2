using System;
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
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
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
        Assert.True(b.Porters > 0);
        Unit porters = g.Units.Last(u => u.Kind == Content.Porters && u.Target == b.Id);
        Assert.True(s.People < people, "porters leave their work");
        while (b.State == BuildingState.Delivery)
        {
            g.Tick();
        }

        Assert.Equal(BuildingState.Construction, b.State);
        Assert.True(b.FinishTick <= g.Ticks + hut.BuildTicks);
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
    public void KilledPortersLoseTheLoadAndTheConstructionWaitsForTheMissingMaterials()
    {
        SimGame g = NewGame();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        Settlement sa = g.Settlements[a.Settlements[0]];
        Settlement sb = g.Settlements[b.Settlements[0]];
        sb.People = 60;
        BuildingType hut = Content.Building("hut");
        int cell = sa.Cells.Where(x => g.CannotBuild(a, x, hut) is null).OrderByDescending(x => g.Geo.Distance(g.Geo.FromIndex(x), g.Geo.FromIndex(sa.Center))).First();
        Assert.Null(g.Apply(new TrainCommand(b.Id, sb.Id, Content.Unit("warriors").Id)));
        for (int i = 0; i <= Content.Unit("warriors").TrainTicks + 1; i++)
        {
            g.Tick();
        }

        Unit w = g.Units.Last(u => u.Civ == b.Id);
        g.Apply(new BuildCommand(a.Id, cell, hut.Id));
        Building site = g.Buildings[g.BuildingAt[cell]];
        Unit porters = g.Units.Last(u => u.Kind == Content.Porters && u.Target == site.Id);
        porters.People = 1; // one porter, so the warriors kill them before they reach the site
        g.Place(w, porters.Cell);
        long[] before = (long[])sb.Stock.Clone();
        g.Apply(new AttackCommand(b.Id, w.Id, porters.Cell));
        for (int i = 0; i < 100 && porters.State != UnitState.Gone; i++)
        {
            g.Tick();
        }

        Assert.Equal(UnitState.Gone, porters.State);
        Assert.True(g.Missing(site).Sum() > 0, "the lost materials are missing at the site");
        w.Stance = Stance.Passive; // the raiders stand by and let the next porters pass
        Assert.Equal(0, site.Porters);
        int wood = Content.GoodId("wood");
        Assert.True(sb.Stock[wood] <= before[wood] + SimContent.One, "nobody takes the load");

        // Nothing is sent again by itself.
        for (int i = 0; i < 300; i++)
        {
            g.Tick();
        }

        Assert.Equal(0, site.Porters);
        Assert.Equal(BuildingState.Delivery, site.State);

        // The owner sends the missing materials and the building gets finished.
        Assert.Null(g.Apply(new BringMaterialsCommand(a.Id, site.Id)));
        Assert.True(site.Porters > 0);
        for (int i = 0; i < 3000 && site.State != BuildingState.Active; i++)
        {
            g.Tick();
        }

        Assert.Equal(BuildingState.Active, site.State);
    }

    [Fact]
    public void WorkGoesOnlyAsFarAsTheDeliveredMaterialsAllow()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        Array.Fill(civ.KnownTech, true);
        BuildingType t = Content.Buildings.First(x => x.Price.Count(p => p > 0) >= 2 && s.Cells.Any(c => g.CannotBuild(civ, c, x) is null));
        int cell = s.Cells.Where(c => g.CannotBuild(civ, c, t) is null).OrderByDescending(c => g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(s.Center))).First();
        Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, t.Id)));
        Building b = g.Buildings[g.BuildingAt[cell]];
        Assert.True(b.Porters >= 2, "one group of porters per good");

        // Kill one group: the work stops part-way and waits.
        Unit lost = g.Units.Last(u => u.Kind == Content.Porters && u.Target == b.Id);
        g.Destroyed(lost, g.Civs[1].Id);
        for (int i = 0; i < 3000 && !(b.State == BuildingState.Delivery && b.Porters == 0 && b.WorkCap > 0); i++)
        {
            g.Tick();
        }

        for (int i = 0; i < t.BuildTicks + 10; i++)
        {
            g.Tick();
        }

        Assert.Equal(BuildingState.Delivery, b.State);
        Assert.True(g.WorkDone(b) > 0 && g.WorkDone(b) < t.BuildTicks, $"done {g.WorkDone(b)} of {t.BuildTicks}");
    }

    [Fact]
    public void EveryEraHasEnoughTechnologiesBeyondTheBasicsAndItsUpgrades()
    {
        Assert.True(Content.Techs.Count(t => t.Upgrade) >= 10);
        foreach (SimEra era in Content.Eras)
        {
            int counted = Content.Techs.Count(t => !t.Upgrade && !t.Basic && t.Era == era.Id);
            Assert.True(counted >= Content.EraAdvanceTechs + 2, $"{era.Name} has {counted} technologies that count");
        }
    }

    [Fact]
    public void BasicsDoNotCountTowardTheNextEra()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Assert.Equal(1, g.KnownIn(civ, 0)); // the six basics do not count, Flint weapons does
        g.AdvanceEra(civ, tell: false);
        Assert.Equal(0, civ.Era);
    }

    [Fact]
    public void ATechnologyCanAskForAnotherKnownFirstAndForGoodsMade()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Array.Fill(civ.KnownTech, true);
        civ.Era = 1;
        int barter = Content.TechId("barter");
        int currency = Content.TechId("currency");
        civ.KnownTech[barter] = false;
        civ.KnownTech[currency] = false;
        civ.Era = 2;
        Assert.Contains("Barter first", g.CannotResearch(civ, currency));
        civ.Experience[Content.TechId("pottery")] = 1000 * SimContent.TicksPerMinute;
        Assert.Contains("pottery made", g.CannotResearch(civ, barter));
        civ.Made[Content.GoodId("pottery")] = 30 * SimContent.One * SimContent.TicksPerMinute;
        Assert.Null(g.CannotResearch(civ, barter));
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
        civ.KnownTech[Content.TechId("bronze_age_arms")] = true;
        Assert.True(g.Power(u) > before);
    }

    [Fact]
    public void ANewEraOpensAfterEnoughTechnologiesOfTheOneBefore()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        int agriculture = Content.TechId("agriculture");
        Assert.Contains("Opens in the", g.CannotResearch(civ, agriculture));
        foreach (SimTech t in Content.Techs.Where(t => t.Era == 0 && !t.Basic).Take(Content.EraAdvanceTechs))
        {
            civ.KnownTech[t.Id] = true;
        }

        g.AdvanceEra(civ, tell: false);
        Assert.True(civ.Era >= 1, "knowing five stone-age technologies opens the neolithic");
        Assert.DoesNotContain("Opens in the", g.CannotResearch(civ, agriculture) ?? "");
    }

    [Fact]
    public void AlertsTellWhatNeedsAReaction()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        for (int i = 0; i < 2 * SimContent.TicksPerSecond; i++)
        {
            g.Tick();
        }

        s.People = s.Housing + 40;
        s.Gathering = false;
        civ.Dirty = true;
        for (int i = 0; i < SimContent.TicksPerMinute + 1; i++)
        {
            s.Stock[Content.Food] = 0; // through a ration: the people go hungry
            g.Tick();
        }

        System.Collections.Generic.List<Alert> alerts = g.Alerts(civ);
        Assert.True(alerts.Count > 0, $"people {s.People}, homes {s.Housing}, hungry {s.Hungry}, food {s.Stock[Content.Food]}");
        Assert.Contains(alerts, a => a.Kind == AlertKind.Hungry && a.Settlement == s.Id);
    }
}
