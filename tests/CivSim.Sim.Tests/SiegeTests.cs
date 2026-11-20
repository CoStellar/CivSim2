using System;
using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Sieges and garrisons (Patryk, 2026-11-20, questions 3, 5, 7, 8).</summary>
public sealed class SiegeTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static SimGame War()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        g.Civs[0].Known[g.Settlements[g.Civs[1].Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(0, 1)));
        return g;
    }

    private static Unit Soldiers(SimGame g, int civ, string kind, int cell, int people)
    {
        Settlement home = g.Settlements[g.Civs[civ].Settlements[0]];
        for (int i = 0; i < home.Stock.Length; i++)
        {
            home.Stock[i] = 900 * SimContent.One;
        }

        home.People = 200;
        UnitType t = Content.Unit(kind);
        foreach (int tech in t.AlsoTechs.Append(t.Tech))
        {
            if (tech >= 0)
            {
                g.Civs[civ].KnownTech[tech] = true;
            }
        }

        Assert.Null(g.Apply(new TrainCommand(civ, home.Id, t.Id, people)));
        Unit u = g.Units[^1];
        for (int i = 0; i < 5000 && u.State == UnitState.Training; i++)
        {
            g.Tick();
        }

        g.Place(u, cell);
        return u;
    }

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    // A land hex at the given distance from a settlement's centre, outside every settlement.
    private static int Near(SimGame g, Settlement s, int distance) => Enumerable.Range(0, g.Geo.CellCount).First(c =>
        !Content.World.Biomes[g.Map.Biome[c]].IsWater && g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(s.Center)) == distance);

    [Fact]
    public void AnEnemyArmyNearBlockadesTheSettlement()
    {
        SimGame g = War();
        Settlement target = g.Settlements[g.Civs[1].Settlements[0]];
        target.People = 60;
        Unit u = Soldiers(g, 0, "warriors", Near(g, target, Content.Siege.BlockadeRange), 10);
        int before = g.WorkPermille(target);
        Run(g, 2 * SimContent.TicksPerSecond);
        Assert.True(target.Besieged);
        Assert.Equal(before * Content.Siege.BlockadeWorkPercent / 100 / Content.WorkStepPermille * Content.WorkStepPermille, g.WorkPermille(target));
        Assert.NotNull(g.Apply(new TrainCommand(1, target.Id, Content.Unit("scouts").Id)));

        // The army leaves: the blockade lifts after siege.liftSeconds.
        g.Place(u, g.Settlements[g.Civs[0].Settlements[0]].Center);
        Run(g, (Content.Siege.LiftSeconds + 2) * SimContent.TicksPerSecond);
        Assert.False(target.Besieged);
    }

    [Fact]
    public void BlowsFallOnTheGuardFirstThenOnTheFightersAndTheSettlementFalls()
    {
        SimGame g = War();
        Settlement target = g.Settlements[g.Civs[1].Settlements[0]];
        target.People = 100;
        target.Guard = 10;
        Unit u = Soldiers(g, 0, "warriors", Near(g, target, 1), 30);
        Assert.Null(g.Apply(new AttackCommand(0, u.Id, target.Center)));
        int guardGoneAt = -1;
        for (int i = 0; i < 3 * SimContent.TicksPerMinute && target.FallenTo < 0 && u.People > 0; i++)
        {
            g.Tick();
            if (target.Guard == 10 && target.People < 100)
            {
                Assert.Fail("residents died while the guard stood");
            }

            if (guardGoneAt < 0 && target.Guard == 0)
            {
                guardGoneAt = i;
            }
        }

        Assert.Equal(0, target.FallenTo);
        Assert.Equal(1, target.Civ); // the taker has yet to choose
        Assert.True(target.People >= 100 - (100 * Content.War.DefenderPercent / 100), $"people {target.People}");
        Assert.True(target.People >= 100 * Content.Siege.ResidentsFloorPercent / 100);
        int people = target.People;
        Run(g, 10 * SimContent.TicksPerSecond);
        Assert.Equal(people, target.People); // defenceless: nobody dies any more

        // Waiting too long takes it.
        Run(g, (Content.Siege.ChoiceSeconds + 1) * SimContent.TicksPerSecond);
        Assert.Equal(0, target.Civ);
    }

    [Fact]
    public void ASackedSettlementLosesHalfItsGoodsAndStaysTheirs()
    {
        SimGame g = War();
        Settlement target = g.Settlements[g.Civs[1].Settlements[0]];
        Settlement home = g.Settlements[g.Civs[0].Settlements[0]];
        int wood = Content.GoodId("wood");
        target.FallenTo = 0;
        target.Stock[wood] = 100 * SimContent.One;
        long homeWood = home.Stock[wood];
        Assert.NotNull(g.Apply(new TakeSettlementCommand(1, target.Id, TakeChoice.Sack)));
        Assert.Null(g.Apply(new TakeSettlementCommand(0, target.Id, TakeChoice.Sack)));
        Assert.Equal(1, target.Civ);
        Assert.Equal(50 * SimContent.One, target.Stock[wood]);
        Assert.Equal(homeWood + (50 * SimContent.One), home.Stock[wood]);
        Assert.True(target.SackedUntil > g.Ticks);
    }

    [Fact]
    public void ARazedSettlementIsARuinAndItsPeopleFlee()
    {
        SimGame g = War();
        Civ loser = g.Civs[1];
        Settlement target = g.Settlements[loser.Settlements[0]];
        int site = Enumerable.Range(0, g.Geo.CellCount).First(c => g.OwnerAt[c] < 0 && !Content.World.Biomes[g.Map.Biome[c]].IsWater
            && g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(target.Center)) == 9);
        Settlement refuge = g.FoundSettlement(loser, site, 10);
        target.People = 50;
        target.FallenTo = 0;
        Assert.Null(g.Apply(new TakeSettlementCommand(0, target.Id, TakeChoice.Raze)));
        Assert.True(target.Abandoned);
        Assert.Equal(60, refuge.People);
        Assert.False(loser.Dead);
    }

    [Fact]
    public void WallsGiveAGuardThatGrowsBackInPeace()
    {
        SimGame g = War();
        Settlement s = g.Settlements[g.Civs[1].Settlements[0]];
        int max = g.GuardMax(s);
        Assert.Equal(0, max); // no palisade, wall or castle yet
        BuildingType castle = Content.Building("castle");
        Assert.Equal(40, castle.Guard);
    }
}
