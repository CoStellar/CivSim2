using System;
using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Army limit and veterans of each kind (Patryk, 2026-11-20, questions 9 and 11).</summary>
public sealed class VeteranTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static SimGame Game(out Civ civ, out Settlement home)
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        civ = g.Civs[0];
        home = g.Settlements[civ.Settlements[0]];
        home.People = 300;
        for (int i = 0; i < home.Stock.Length; i++)
        {
            home.Stock[i] = 900 * SimContent.One;
        }

        civ.KnownTech[Content.Unit("warriors").Tech] = true;
        return g;
    }

    [Fact]
    public void TheArmyLimitComesFromSettlementsAndLaws()
    {
        SimGame g = Game(out Civ civ, out Settlement home);
        Assert.Equal(Content.ArmyLimit.PerLevel[home.Level], g.ArmyLimit(civ));
        int warriors = Content.Unit("warriors").Id;
        for (int i = 0; i < g.ArmyLimit(civ); i++)
        {
            Assert.Null(g.Apply(new TrainCommand(0, home.Id, warriors)));
        }

        Assert.NotNull(g.Apply(new TrainCommand(0, home.Id, warriors)));
        civ.KnownTech[Content.TechId("code_of_laws")] = true;
        Assert.Null(g.Apply(new TrainCommand(0, home.Id, warriors)));
    }

    [Fact]
    public void KillsRaiseTheKindAndTheChoiceHelpsAllItsUnits()
    {
        SimGame g = Game(out Civ civ, out Settlement home);
        civ.KnownTech[Content.TechId("code_of_laws")] = true;
        int warriors = Content.Unit("warriors").Id;
        Assert.Null(g.Apply(new TrainCommand(0, home.Id, warriors)));
        Unit a = g.Units[^1];
        for (int i = 0; i < 5000 && a.State == UnitState.Training; i++)
        {
            g.Tick();
        }

        long before = g.StrikeDamage(a, a, 10, false);
        civ.KindKills[warriors] = Content.Veterans.Levels[0] - 1;
        Assert.NotNull(g.Apply(new ChooseVeteranCommand(0, warriors, 0))); // nothing to choose yet
        civ.KindKills[warriors] = Content.Veterans.Levels[0];
        civ.KindLevel[warriors] = 1;
        civ.KindPending[warriors] = 1;
        int hardened = Array.FindIndex(Content.Veterans.Options, o => o.Key == "hardened");
        Assert.Null(g.Apply(new ChooseVeteranCommand(0, warriors, hardened)));
        Assert.Equal(0, civ.KindPending[warriors]);
        long after = g.StrikeDamage(a, a, 10, false);
        Assert.True(after > before, $"{before} -> {after}");
    }
}
