using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class FogTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

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

            s.People = 60;
        }

        return g;
    }

    private static Unit Scouts(SimGame g, Civ civ)
    {
        Settlement home = g.Settlements[civ.Settlements[0]];
        Assert.Null(g.Apply(new TrainCommand(civ.Id, home.Id, Content.Unit("scouts").Id)));
        Run(g, Content.Unit("scouts").TrainTicks + 2);
        return g.Units.Last(u => u.Civ == civ.Id);
    }

    [Fact]
    public void OurOwnLandIsAlwaysSeen()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        Assert.All(s.Cells, c => Assert.True(g.Visible(civ, c)));
    }

    [Fact]
    public void WhatWeSawStaysAsAMemoryUntilWeLookAgain()
    {
        SimGame g = NewGame();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        Settlement theirs = g.Settlements[b.Settlements[0]];
        Unit scouts = Scouts(g, a);

        // Our scouts stand by their camp: we see it live.
        g.Place(scouts, theirs.Center);
        Assert.True(g.Visible(a, theirs.Center));
        Assert.Empty(a.SeenSettlements);

        // They go home: the camp becomes a memory, as it was.
        g.Place(scouts, g.Settlements[a.Settlements[0]].Center);
        Assert.False(g.Visible(a, theirs.Center));
        Assert.True(a.Known[theirs.Center]);
        Assert.Contains(theirs.Center, a.SeenSettlements.Keys);
        int level = a.SeenSettlements[theirs.Center].Level;

        // It grows while we do not look; the memory does not.
        theirs.Level++;
        Assert.Equal(level, a.SeenSettlements[theirs.Center].Level);

        // Looking again replaces the memory with what is there.
        g.Place(scouts, theirs.Center);
        Assert.DoesNotContain(theirs.Center, a.SeenSettlements.Keys);
    }

    [Fact]
    public void WeSeeOtherUnitsOnlyWhereWeLook()
    {
        SimGame g = NewGame();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        Unit ours = Scouts(g, a);
        Unit theirs = Scouts(g, b);
        g.Place(theirs, g.Settlements[b.Settlements[0]].Center);
        g.Place(ours, g.Settlements[a.Settlements[0]].Center);
        Assert.False(g.Sees(a, theirs));
        g.Place(ours, theirs.Cell);
        Assert.True(g.Sees(a, theirs));
    }

    [Fact]
    public void UnitsWalkExactlyTheDrawnHexes()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Unit u = Scouts(g, civ);

        // A drawn line: a few steps north-east, then south-east, over walkable land.
        var drawn = new List<int>();
        int cell = u.Cell;
        foreach (HexDirection d in new[] { HexDirection.NE, HexDirection.NE, HexDirection.SE, HexDirection.SE, HexDirection.S })
        {
            int next = g.Geo.Index(g.Geo.Neighbor(g.Geo.FromIndex(cell), d));
            if (g.FindPath(cell, next, new List<int>()))
            {
                drawn.Add(next);
                cell = next;
            }
        }

        Assert.True(drawn.Count >= 2, "the test needs some land");
        Assert.Null(g.Apply(new MoveCommand(civ.Id, u.Id, drawn[^1], false, [.. drawn])));
        Assert.Equal(drawn, u.Path);
        var walked = new List<int>();
        for (int i = 0; i < 2000 && u.State == UnitState.Moving; i++)
        {
            int before = u.Cell;
            g.Tick();
            if (u.Cell != before)
            {
                walked.Add(u.Cell);
            }
        }

        Assert.Equal(drawn, walked);
    }

    [Fact]
    public void ADrawnPathMustNotJump()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Unit u = Scouts(g, civ);
        HexCoord c = g.Geo.FromIndex(u.Cell);
        int near = g.Geo.Index(g.Geo.Neighbor(c, HexDirection.N));
        int far = g.Geo.Index(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.Neighbor(c, HexDirection.N), HexDirection.N), HexDirection.N));
        Assert.Contains("neighbouring", g.Apply(new MoveCommand(civ.Id, u.Id, far, false, [near, far])));
    }

    [Fact]
    public void APatrolWalksItsLoopEatsAtHomeAndGoesAgain()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Unit u = Scouts(g, civ);
        Settlement home = g.Settlements[civ.Settlements[0]];

        // A small loop a few hexes from the camp.
        var loop = new List<int>();
        HexCoord start = g.Geo.FromIndex(home.Center);
        for (int k = 0; k < 5; k++)
        {
            start = g.Geo.Neighbor(start, HexDirection.N);
        }

        HexCoord at = start;
        foreach (HexDirection d in new[] { HexDirection.NE, HexDirection.SE, HexDirection.S, HexDirection.SW, HexDirection.NW, HexDirection.N })
        {
            loop.Add(g.Geo.Index(at));
            at = g.Geo.Neighbor(at, d);
        }

        if (g.Apply(new PatrolCommand(civ.Id, u.Id, [.. loop])) is { } why)
        {
            Assert.Contains("walk", why); // water or mountains on this seed: nothing more to check
            return;
        }

        var stages = new HashSet<PatrolStage>();
        for (int i = 0; i < 6000; i++)
        {
            g.Tick();
            stages.Add(u.PatrolStage);
        }

        Assert.Contains(PatrolStage.OnLoop, stages);
        Assert.Contains(PatrolStage.ToSupply, stages);
        Assert.NotEqual(PatrolStage.None, u.PatrolStage);
    }
}
