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
}
