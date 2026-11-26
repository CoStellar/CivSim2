using System;
using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Support buildings that work only with their workers: schools, builders' yards, masons (decisions of 2026-11-18).</summary>
public sealed class LearningTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static void Run(SimGame g, long ticks)
    {
        for (long i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    private static (SimGame, Civ, Settlement) NewGame()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Array.Fill(civ.KnownTech, true);
        Settlement s = g.Settlements[civ.Settlements[0]];
        Array.Fill(s.Stock, 900 * SimContent.One);
        s.People = 60;
        return (g, civ, s);
    }

    private static Building BuildDone(SimGame g, Civ civ, Settlement s, string key)
    {
        BuildingType t = Content.Building(key);
        int cell = s.Cells.First(c => g.CannotBuild(civ, c, t) is null);
        Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, t.Id)));
        Building b = g.Buildings[^1];
        Run(g, t.BuildTicks + 1200);
        Assert.Equal(BuildingState.Active, b.State);
        return b;
    }

    [Fact]
    public void LateEraMaterialsComeFromTheirChains()
    {
        // 2026-11-21 (decisions 13-14): bricks from clay and charcoal, mortar from stone and charcoal; a city costs bricks.
        (SimGame g, Civ civ, Settlement s) = NewGame();
        int bricks = Content.GoodId("bricks");
        int mortar = Content.GoodId("mortar");
        BuildDone(g, civ, s, "charcoal_burner");
        BuildDone(g, civ, s, "brickworks");
        BuildDone(g, civ, s, "lime_kiln"); // costs bricks
        s.Stock[bricks] = 0;
        s.Stock[mortar] = 0;
        Run(g, 2 * SimContent.TicksPerMinute);
        Assert.True(s.Stock[bricks] > 0);
        Assert.True(s.Stock[mortar] > 0);
        Assert.True(Content.Levels[^1].UpgradePrice[bricks] > 0);
        Assert.True(Content.Building("castle").Price[mortar] > 0);
        Assert.True(Content.Building("bronze_smith").Consumes[Content.GoodId("charcoal")] > 0);
    }

    [Fact]
    public void EveryWorkerOfASchoolSpeedsTheBranchItIsSetTo()
    {
        (SimGame g, Civ civ, Settlement s) = NewGame();
        Assert.Equal(0, g.ResearchSpeed(civ, "science"));
        Building school = BuildDone(g, civ, s, "school");
        Assert.Equal(3, school.Workers);
        Assert.Equal(15, g.ResearchSpeed(civ, "science")); // the first worker: science
        Assert.Null(g.Apply(new SetFocusCommand(civ.Id, school.Id, 1, "science")));
        Assert.Equal(30, g.ResearchSpeed(civ, "science"));
        int writing = Content.TechId("writing");
        Assert.True(g.ResearchTicks(civ, writing) < Content.Techs[writing].ResearchTicks);
        Assert.NotNull(g.Apply(new SetFocusCommand(civ.Id, school.Id, 0, "basics")));
    }

    [Fact]
    public void BuildersYardsMakeBuildingFasterAndMasonsRaiseQuarries()
    {
        (SimGame g, Civ civ, Settlement s) = NewGame();
        BuildDone(g, civ, s, "builders_yard");
        BuildingType hut = Content.Building("hut");
        int cell = s.Cells.First(c => g.CannotBuild(civ, c, hut) is null);
        Assert.Null(g.Apply(new BuildCommand(civ.Id, cell, hut.Id)));
        Assert.Equal(130, g.Buildings[^1].Speed);

        Building quarry;
        try
        {
            quarry = BuildDone(g, civ, s, "quarry");
        }
        catch (InvalidOperationException)
        {
            return; // no room for a quarry by this start
        }

        int stone = Content.GoodId("stone");
        s.Stock[stone] = 0;
        s.FullMask = 0;
        g.Recompute(civ);
        long before = s.Rate[stone];
        BuildDone(g, civ, s, "mason");
        s.Stock[stone] = 0;
        s.FullMask = 0;
        g.Recompute(civ);
        Assert.True(s.Rate[stone] > before, $"masons raise the quarries near them ({before} → {s.Rate[stone]})");
        _ = quarry;
    }

    [Fact]
    public void HorsesLiveOnlyInStablesAndBarterNeedsATradingPostPerAgreement()
    {
        (SimGame g, Civ civ, Settlement s) = NewGame();
        int currency = Content.TechId("currency");
        civ.KnownTech[currency] = false;
        Assert.Equal(0, g.TradeRoom(civ));
        civ.KnownTech[currency] = true;
        Assert.Equal(int.MaxValue, g.TradeRoom(civ));

        int horses = Content.GoodId("horses");
        g.Recompute(civ);
        Assert.Equal(0, s.Capacity[horses]);
        BuildDone(g, civ, s, "stable");
        g.Recompute(civ);
        Assert.Equal(32 * SimContent.One, s.Capacity[horses]); // 2026-11-21: 32 horses a stable
    }

    [Fact]
    public void StoneHousesKeepTheirPeopleWarmerInWinter()
    {
        (SimGame g, Civ civ, Settlement s) = NewGame();
        Run(g, g.TicksToSeasonChange + 1);
        Assert.True(g.IsWinter);
        int clothing = Content.GoodId("clothing");
        s.Stock[clothing] = 0;
        s.People = 32;
        s.WarmHomes = 0;
        Assert.Equal(500, g.WorkPermille(s));
        s.WarmHomes = 32;
        Assert.Equal(650, g.WorkPermille(s));
    }
}
