using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Decisions of Patryk after the tests of 2026-11-23: P1–P5, the idle people, and the fixes of the report.</summary>
public sealed class Tests20261123Tests
{
    private static readonly SimContent Content = SimContent.Load();

    private static SimGame NewGame() => SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();

    // Both civilizations have seen each other's capital: they have met.
    private static SimGame Met()
    {
        SimGame g = NewGame();
        foreach (Civ a in g.Civs)
        {
            foreach (Civ b in g.Civs)
            {
                a.Known[g.Settlements[b.Settlements[0]].Center] = true;
            }
        }

        return g;
    }

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    [Fact]
    public void ABadNameHealsInPeaceButNotForTheAggressor()
    {
        // P2: reputation is renewable: war.reputationRegenPerMinute a minute, up to 0, unless the people started a war going on.
        SimGame g = Met();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        a.Reputation = -100;
        b.Reputation = -100;
        Run(g, SimContent.TicksPerMinute * 2);
        Assert.Equal(-100 + (2 * Content.War.ReputationRegenPerMinute), a.Reputation);
        Assert.Equal(-100 + (2 * Content.War.ReputationRegenPerMinute), b.Reputation);

        Assert.Null(g.Apply(new DeclareWarCommand(a.Id, b.Id)));
        int before = a.Reputation;
        int victim = b.Reputation;
        Run(g, SimContent.TicksPerMinute);
        Assert.Equal(before, a.Reputation); // the aggressor of a war going on does not heal
        Assert.Equal(victim + Content.War.ReputationRegenPerMinute, b.Reputation);

        b.Reputation = -1;
        Run(g, SimContent.TicksPerMinute);
        Assert.Equal(0, b.Reputation); // never above 0
    }

    [Fact]
    public void APeaceThatStillRunsIsATruce()
    {
        SimGame g = Met();
        Assert.Null(g.Apply(new DeclareWarCommand(0, 1)));
        Assert.False(g.InTruce(0, 1));
        g.MakePeace(g.Civs[0], g.Civs[1], 0);
        Assert.True(g.InTruce(0, 1));
        Assert.True(g.InTruce(1, 0));
        Run(g, Content.Peace.Minutes[0] * SimContent.TicksPerMinute + 1);
        Assert.False(g.InTruce(0, 1));
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void TheAiKeepsThePeaceItMade()
    {
        // B1: no AI breaks a truce (the news says "breaking a truce" when anyone does).
        foreach (ulong seed in new ulong[] { 1, 2 })
        {
            SimGame g = SimGame.Create(Content, new SimSettings(seed, 66, 40, 4));
            int broken = 0;
            for (int t = 0; t < 40 * SimContent.TicksPerMinute; t++)
            {
                g.Tick();
                broken += g.News.Count(n => n.Text.Contains("breaking a truce", System.StringComparison.Ordinal));
            }

            Assert.Equal(0, broken);
        }
    }

    [Fact]
    public void APeopleWithNobodyLeftHasLost()
    {
        // P5: nobody in the settlements and no unit: the civilization is no more (its empty settlement falls to ruin).
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        home.People = 0;
        Run(g, SimContent.TicksPerSecond * 2);
        Assert.True(civ.Dead);
        Assert.Equal(0, civ.People);
        Assert.True(home.Abandoned);
    }

    [Fact]
    public void AUnitKeepsThePeopleAliveAndCanStartAgain()
    {
        // P5: units count as people: with its village empty the civilization lives on, and the unit can join the village.
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        civ.KnownTech[Content.Unit("scouts").Tech] = true;
        Assert.Null(g.Apply(new TrainCommand(civ.Id, home.Id, Content.Unit("scouts").Id)));
        Unit u = g.Units[^1];
        Run(g, 2000);
        Assert.Equal(UnitState.Idle, u.State);
        int people = u.People;
        home.People = 0;
        Run(g, SimContent.TicksPerSecond * 2);
        Assert.False(civ.Dead);
        Assert.Null(g.Apply(new DisbandCommand(civ.Id, u.Id)));
        Assert.Equal(people, home.People);
        Run(g, SimContent.TicksPerSecond * 2);
        Assert.False(civ.Dead);
    }

    [Fact]
    public void IdleGatherersAlwaysFindTheirFood()
    {
        // Patryk, 2026-11-23: a civilization left alone survives on gathering, in any season, but finds nothing more.
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        foreach (int id in civ.Buildings.ToArray())
        {
            g.Buildings[id].State = BuildingState.Removed;
            civ.Buildings.Remove(id);
        }

        civ.Dirty = true;
        long low = long.MaxValue;
        for (int t = 0; t < Content.SeasonCycleTicks + 10; t += 10)
        {
            Run(g, 10);
            g.Recompute(civ);
            low = System.Math.Min(low, s.Rate[Content.Food]);
        }

        Assert.True(low >= 0, $"food fell by {-low / (double)SimContent.One} a minute");
        Assert.True(s.People > 0);
    }

    [Fact]
    public void StrangersThatSawUsRaiseAnAlertThatSaysNoMore()
    {
        // P3: units of a civilization we have not met near our settlement, whose civilization has seen it: "strangers", not who nor where.
        SimGame g = NewGame();
        Civ us = g.Civs[0];
        Civ them = g.Civs[1];
        Settlement home = g.Settlements[us.Settlements[0]];
        Settlement theirs = g.Settlements[them.Settlements[0]];
        Assert.False(g.Met(us, them));
        them.KnownTech[Content.Unit("scouts").Tech] = true;
        Assert.Null(g.Apply(new TrainCommand(them.Id, theirs.Id, Content.Unit("scouts").Id)));
        Unit scout = g.Units[^1];
        Run(g, 2000);
        Assert.DoesNotContain(g.Alerts(us), a => a.Kind == AlertKind.Strangers);
        int near = g.Geo.Index(g.Geo.Neighbor(g.Geo.FromIndex(home.Center), HexDirection.N));
        scout.Hidden = false;
        g.Place(scout, near);
        Assert.True(them.Known[home.Center]);
        Alert alert = Assert.Single(g.Alerts(us), a => a.Kind == AlertKind.Strangers);
        Assert.DoesNotContain(them.Name, alert.Text, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ASettlementSeesTwoHexesBeyondItsLand()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        HexCoord c = g.Geo.FromIndex(s.Center);
        int reach = Content.Levels[s.Level].Radius + Content.SettlementSightBeyond;
        Assert.Equal(2, Content.SettlementSightBeyond);
        HexCoord edge = c;
        for (int k = 0; k < reach; k++)
        {
            edge = g.Geo.Neighbor(edge, HexDirection.N);
        }

        Assert.True(g.Visible(civ, g.Geo.Index(edge)));
    }

    [Fact]
    public void ARouteCannotKeepLessThanNothing()
    {
        // E2 (tests 2026-11-23).
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        int a = civ.Settlements[0];
        Settlement other = g.FoundSettlement(civ, g.Geo.Index(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.FromIndex(g.Settlements[a].Center), HexDirection.S), HexDirection.S), HexDirection.S), HexDirection.S), HexDirection.S)), 10);
        Assert.NotNull(g.Apply(new RouteCommand(civ.Id, a, other.Id, Content.Food, 10 * SimContent.One, null, -100 * SimContent.One)));
        Assert.NotNull(g.Apply(new RouteCommand(civ.Id, a, other.Id, Content.Food, 10 * SimContent.One, null, 0, -100 * SimContent.One)));
    }

    [Fact]
    public void AnUnknownStanceIsSaidSo()
    {
        // B8 (tests 2026-11-23).
        SimGame g = NewGame();
        Assert.Equal("Unknown stance.", g.Apply(new StanceCommand(0, 0, (Stance)77)));
    }
}
