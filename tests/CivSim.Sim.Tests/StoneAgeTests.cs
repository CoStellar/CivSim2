using System;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Winter, clothing, gathering, carriers, rivers and care of the Stone Age (decisions of 2026-11-18).</summary>
public sealed class StoneAgeTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static void Run(SimGame g, long ticks)
    {
        for (long i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    private static SimGame NewGame() => SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();

    // A second camp of the people, founded by settlers put straight on free land a little away.
    private static Settlement SecondCamp(SimGame g, Civ civ)
    {
        Settlement s = g.Settlements[civ.Settlements[0]];
        Assert.Null(g.Apply(new TrainCommand(civ.Id, s.Id, Content.Unit("settlers").Id)));
        Run(g, Content.Unit("settlers").TrainTicks + 1);
        Unit u = g.Units[^1];
        HexCoord c = g.Geo.FromIndex(s.Center);
        for (int r = Content.FoundMinDistance + 1; r < Content.FoundMinDistance + 9; r++)
        {
            var ring = new HexCoord[HexGlobe.RingSize(r)];
            int n = g.Geo.Ring(c, r, ring);
            for (int k = 0; k < n; k++)
            {
                int i = g.Geo.Index(ring[k]);
                if (g.CannotFound(i) is null)
                {
                    g.Place(u, i);
                    Assert.Null(g.Apply(new FoundCommand(civ.Id, u.Id)));
                    return g.Settlements[civ.Settlements[^1]];
                }
            }
        }

        throw new InvalidOperationException("no free land");
    }

    private static void RunToWinter(SimGame g)
    {
        Run(g, g.TicksToSeasonChange + 1);
        Assert.True(g.IsWinter);
    }

    [Fact]
    public void WinterHalvesWorkAndClothingHelpsUpToTheCaps()
    {
        SimGame g = NewGame();
        Settlement s = g.Settlements[g.Civs[0].Settlements[0]];
        s.People = 20;
        int clothing = Content.GoodId("clothing");
        s.Stock[clothing] = 0;
        Assert.Equal(1000, g.WorkPermille(s));
        s.Stock[clothing] = 20 * SimContent.One; // one set each: +15%
        Assert.Equal(1150, g.WorkPermille(s));
        s.Stock[clothing] = 60 * SimContent.One; // outside winter only one set counts
        Assert.Equal(1150, g.WorkPermille(s));

        RunToWinter(g);
        s.People = 20;
        s.Stock[clothing] = 0;
        Assert.Equal(500, g.WorkPermille(s));
        s.Stock[clothing] = 20 * SimContent.One;
        Assert.Equal(650, g.WorkPermille(s));
        s.Stock[clothing] = 80 * SimContent.One; // four sets each reach the winter cap
        Assert.Equal(1100, g.WorkPermille(s));
        s.Stock[clothing] = 200 * SimContent.One;
        Assert.Equal(1100, g.WorkPermille(s));
    }

    [Fact]
    public void ClothingWearsOut()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        int clothing = Content.GoodId("clothing");
        s.Stock[clothing] = 30 * SimContent.One;
        civ.Dirty = true;
        Assert.False(g.IsWinter);
        Run(g, SimContent.TicksPerMinute);
        Assert.Equal(30 * SimContent.One, s.Stock[clothing]); // clothing.wearOnlyInWinter: no wear outside winter
        while (!g.IsWinter)
        {
            Run(g, SimContent.TicksPerSecond);
        }
        long atWinter = s.Stock[clothing];
        Run(g, SimContent.TicksPerMinute);
        Assert.True(s.Stock[clothing] < atWinter, "clothing wears out in winter");
    }

    [Fact]
    public void GatherersPickUpWhatTheirLandGives()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        int[] rich = g.Richness(s);
        Assert.True(rich[Content.Food] > 0, "every land gives some food");
        g.Recompute(civ);
        long withGathering = s.Rate[Content.Food];
        s.Gathering = false; // a test lever: the player cannot turn gathering off
        g.Recompute(civ);
        Assert.True(s.Rate[Content.Food] < withGathering, "without gathering the people only eat");
        int wood = Content.GoodId("wood");
        Assert.True(s.Rate[wood] <= 0);
    }

    [Fact]
    public void PortersCarryInTheirHandsUntilBaskets()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        civ.KnownTech[Content.TechId("carrying")] = false;
        Assert.Equal(0, g.Bonus(civ, EffectKind.Cargo, "porters", Content.Units[Content.Porters].Groups));
        civ.KnownTech[Content.TechId("carrying")] = true;
        Assert.Equal(15, g.Bonus(civ, EffectKind.Cargo, "porters", Content.Units[Content.Porters].Groups));
    }

    [Fact]
    public void GoodsGoWithPortersUntilThePeopleHasCaravansAndHorses()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement a = g.Settlements[civ.Settlements[0]];
        a.People = 60;
        Array.Fill(a.Stock, 300 * SimContent.One);
        Settlement b = SecondCamp(g, civ);
        a.Stock[Content.GoodId("horses")] = 0;
        Assert.Null(g.Apply(new SendCaravanCommand(civ.Id, a.Id, b.Id, Content.Food, 20 * SimContent.One)));
        Assert.Equal(Content.Porters, g.Units[^1].Kind);

        civ.KnownTech[Content.TechId("the_wheel")] = true;
        civ.KnownTech[Content.TechId("horseback_riding")] = true;
        Assert.Null(g.Apply(new SendCaravanCommand(civ.Id, a.Id, b.Id, Content.Food, 20 * SimContent.One)));
        Assert.Equal(Content.Porters, g.Units[^1].Kind); // no horses yet
        a.Stock[Content.GoodId("horses")] = 4 * SimContent.One;
        Assert.Null(g.Apply(new SendCaravanCommand(civ.Id, a.Id, b.Id, Content.Food, 20 * SimContent.One)));
        Assert.Equal(Content.Caravans, g.Units[^1].Kind);
        Assert.Equal(2 * SimContent.One, a.Stock[Content.GoodId("horses")]);
    }

    [Fact]
    public void RiversStopWalkersUntilARaftLanding()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];

        // A river edge between two land hexes somewhere on the map.
        int from = -1;
        int dir = -1;
        for (int i = 0; i < g.Geo.CellCount && from < 0; i++)
        {
            if (Content.World.Biomes[g.Map.Biome[i]].IsWater)
            {
                continue;
            }

            for (int d = 0; d < 6; d++)
            {
                HexCoord c = g.Geo.FromIndex(i);
                int n = g.Geo.Index(g.Geo.Neighbor(c, (HexDirection)d));
                if (g.Map.River[g.Geo.EdgeIndex(g.Geo.Edge(c, (HexDirection)d))] > 0 && !Content.World.Biomes[g.Map.Biome[n]].IsWater)
                {
                    from = i;
                    dir = d;
                    break;
                }
            }
        }

        Assert.True(from >= 0, "the map has a river");
        Assert.False(g.Crossable(civ.Id, from, dir));
        Assert.True(g.Crossable(-1, from, dir));
        civ.Era = Content.RiversFreeEra;
        Assert.True(g.Crossable(civ.Id, from, dir));
        civ.Era = 0;

        // A raft landing of our own on the bank lets us cross there.
        BuildingType landing = Content.Building("raft_landing");
        var b = new Building { Id = g.Buildings.Count, Civ = civ.Id, Type = landing.Id, Cell = from, Settlement = s.Id, State = BuildingState.Active };
        g.Buildings.Add(b);
        g.PlaceForTest(b);
        Assert.True(g.Crossable(civ.Id, from, dir));
    }

    [Fact]
    public void HuntersBecomeArchersWithArchery()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        s.People = 60;
        Array.Fill(s.Stock, 500 * SimContent.One);
        civ.KnownTech[Content.TechId("hunting")] = true;
        Assert.Null(g.Apply(new TrainCommand(civ.Id, s.Id, Content.Unit("hunters").Id)));
        Unit u = g.Units[^1];
        Assert.Equal(Content.Unit("hunters").Id, u.Kind);
        Assert.Equal(u.Kind, g.Upgraded(civ, u.Kind)); // not yet
        civ.KnownTech[Content.TechId("archery")] = true;
        Assert.Equal(Content.Unit("archers").Id, g.Upgraded(civ, Content.Unit("hunters").Id));
    }

    [Fact]
    public void HerbalistsHealTheWounded()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        s.People = 60;
        Array.Fill(s.Stock, 500 * SimContent.One);
        civ.KnownTech[Content.TechId("herbal_lore")] = true;
        BuildingType herbalist = Content.Building("herbalist");
        int cell = s.Cells.First(c => g.CannotBuild(civ, c, herbalist) is null);
        Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, herbalist.Id)));
        Run(g, herbalist.BuildTicks + 1200);
        Assert.Null(g.Apply(new TrainCommand(civ.Id, s.Id, Content.Unit("warriors").Id)));
        Run(g, Content.Unit("warriors").TrainTicks + 2);
        Unit u = g.Units.Last(x => x.Kind == Content.Unit("warriors").Id);
        u.People -= 3;
        u.Injured = 3;
        civ.Dirty = true;
        Run(g, 2 * SimContent.TicksPerMinute);
        Assert.True(u.Injured < 3, $"herbalists heal: {u.Injured} still wounded, heal {s.HealPerMinute}/min");
    }

    [Fact]
    public void AHexHoldsFourPlacesOfSmallBuildingsOrOneWholeHexBuilding()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        Array.Fill(s.Stock, 900 * SimContent.One);
        s.People = 60;
        BuildingType hut = Content.Building("hut");
        Assert.Equal(1, hut.Size);
        int cell = s.Cells.First(c => g.CannotBuild(civ, c, hut) is null && g.FreeSlots(c) == 15);
        for (int k = 0; k < 4; k++)
        {
            Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, hut.Id)));
        }

        Assert.Equal(4, g.BuildingsOn(cell).Count);
        Assert.Contains("4 of 4 places", g.CannotBuild(civ, cell, hut));

        BuildingType storehouse = Content.Building("storehouse");
        int other = s.Cells.First(c => c != cell && g.CannotBuild(civ, c, hut) is null && g.FreeSlots(c) == 15);
        Assert.Null(g.Apply(new BuildCommand(civ.Id, other, storehouse.Id)));
        Assert.Null(g.Apply(new BuildCommand(civ.Id, other, storehouse.Id)));
        Assert.NotNull(g.CannotBuild(civ, other, hut)); // two storehouses fill the hex

        int third = s.Cells.First(c => c != cell && c != other && g.CannotBuild(civ, c, hut) is null && g.FreeSlots(c) == 15);
        Assert.Null(g.Apply(new BuildCommand(civ.Id, third, hut.Id)));
        BuildingType quarry = Content.Building("quarry");
        Assert.Equal(4, quarry.Size);
        civ.KnownTech[quarry.Tech] = true;
        Assert.Contains("whole hex", g.CannotBuild(civ, third, quarry));
    }
}
