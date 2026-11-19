using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class WarTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    // Trains a unit and puts it next to a hex (tests skip the walk).
    private static Unit Soldiers(SimGame g, Civ civ, string kind, int near)
    {
        for (int i = 0; i < civ.Stock.Length; i++)
        {
            g.Settlements[civ.Settlements[0]].Stock[i] = 500 * SimContent.One;
        }

        Settlement home = g.Settlements[civ.Settlements[0]];
        home.People = 60;
        Assert.Null(g.Apply(new TrainCommand(civ.Id, home.Id, Content.Unit(kind).Id)));
        Run(g, Content.Unit(kind).TrainTicks + 2);
        Unit u = g.Units[^1];
        HexCoord c = g.Geo.FromIndex(near);
        g.Place(u, g.Geo.Index(g.Geo.Neighbor(c, HexDirection.N)));
        return u;
    }

    [Fact]
    public void AttackingWithoutWarIsASurpriseThatCostsReputation()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        Settlement target = g.Settlements[g.Civs[1].Settlements[0]];
        Unit u = Soldiers(g, a, "warriors", target.Center);
        Assert.Null(g.Apply(new AttackCommand(a.Id, u.Id, target.Center)));
        Assert.True(g.AtWar(0, 1));
        Assert.True(a.Reputation < 0);
    }

    [Fact]
    public void ASettlementWithFewPeopleLeftIsTaken()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        Settlement target = g.Settlements[b.Settlements[0]];
        Unit u = Soldiers(g, a, "warriors", target.Center);
        u.People = 40;
        g.Apply(new AttackCommand(a.Id, u.Id, target.Center));
        Run(g, 1200);
        Assert.Equal(a.Id, target.Civ);
        Assert.Equal(a.Id, g.OwnerAt[target.Cells[0]]);
        Assert.True(b.Dead);
    }

    [Fact]
    public void WallsHelpTheDefenders()
    {
        static int Lost(bool walls)
        {
            SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
            Civ a = g.Civs[0];
            Civ b = g.Civs[1];
            Settlement target = g.Settlements[b.Settlements[0]];
            target.People = 25;
            if (walls)
            {
                b.KnownTech[Content.TechId("castles")] = true;
                Settlement home = g.Settlements[b.Settlements[0]];
                foreach (string k in new[] { "stone", "wood", "iron" })
                {
                    home.Stock[Content.GoodId(k)] = 500 * SimContent.One;
                }

                BuildingType w = Content.Building("castle");
                int cell = target.Cells.Find(c => g.CannotBuild(b, c, w) is null);
                Assert.Null(g.Apply(new BuildCommand(b.Id, cell, w.Id)));
                Run(g, w.BuildTicks + 600);
            }

            Unit u = Soldiers(g, a, "warriors", target.Center);
            g.Apply(new AttackCommand(a.Id, u.Id, target.Center));
            Run(g, 100);
            return Content.Unit("warriors").People - u.People;
        }

        Assert.True(Lost(true) > Lost(false));
    }

    [Fact]
    public void PeaceWithTributeMovesGoodsAndEndsTheWar()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        b.Known[g.Settlements[a.Settlements[0]].Center] = true;
        a.Known[g.Settlements[b.Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(a.Id, b.Id)));
        Assert.Equal(Content.War.DeclareReputation, a.Reputation); // an open declaration costs a little (E1)
        long wood = b.Stock[Content.GoodId("wood")];
        Assert.Null(g.Apply(new ProposePeaceCommand(a.Id, b.Id, 20)));
        Assert.Null(g.Apply(new AnswerPeaceCommand(b.Id, a.Id, true)));
        Assert.False(g.AtWar(a.Id, b.Id));
        // The tribute leaves in a caravan (it turns back when no way on land leads to the taker).
        Assert.Equal(g.Reachable(b, a) ? wood - (wood * 20 / 100) : wood, b.Stock[Content.GoodId("wood")]);
    }
}

public sealed class AiTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void AiPeoplesGrowBuildResearchAndSpreadOnTheirOwn()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(5, 98, 60, 4)).Basics();
        for (int i = 0; i < 13 * SimContent.TicksPerMinute; i++) // research goes faster since 2026-11-19 and takes the early wood
        {
            g.Tick();
        }

        int buildings = 0;
        int techs = 0;
        int settlements = 0;
        foreach (Civ civ in g.Civs)
        {
            buildings += civ.Buildings.Count;
            settlements += civ.Settlements.Count;
            foreach (bool k in civ.KnownTech)
            {
                techs += k ? 1 : 0;
            }
        }

        Assert.True(buildings >= 4 * 6, $"buildings {buildings}");
        Assert.True(techs >= 4 * 3, $"techs {techs}");
        Assert.True(settlements > 4, $"settlements {settlements}");
    }

    [Fact]
    public void AiGamesAreDeterministic()
    {
        static ulong Play()
        {
            SimGame g = SimGame.Create(Content, new SimSettings(5, 66, 40, 3)).Basics();
            for (int i = 0; i < 4 * SimContent.TicksPerMinute; i++)
            {
                g.Tick();
            }

            return g.StateHash();
        }

        Assert.Equal(Play(), Play());
    }
}

public sealed class StartAndEventTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void PeoplesChooseTheirStartInTurnsAwayFromEachOther()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(2, 98, 60, 4) { StartPhase = true }).Basics();
        Assert.True(g.Picking);
        Assert.Empty(g.Settlements);
        Assert.NotNull(g.Sites);
        int guard = 0;
        while (g.Picking && guard++ < 10_000)
        {
            g.Tick();
        }

        Assert.False(g.Picking);
        Assert.Equal(4, g.Settlements.Count);
        for (int a = 0; a < 4; a++)
        {
            for (int b = a + 1; b < 4; b++)
            {
                Assert.True(g.Geo.Distance(g.Geo.FromIndex(g.StartCell[a]), g.Geo.FromIndex(g.StartCell[b])) >= g.BlockRadius);
            }

            (int mine, int best) = g.StartQuality(g.Civs[a]);
            Assert.True(mine >= best * (100 - 30) / 100, $"start {mine} of {best}");
        }

        // The shown circles hide the start: never centred on it, yet every hex too close to it lies inside.
        int moved = 0;
        for (int a = 0; a < 4; a++)
        {
            int d = g.Geo.Distance(g.Geo.FromIndex(g.StartCell[a]), g.Geo.FromIndex(g.BlockCenter[a]));
            Assert.True(d <= g.BlockFuzz);
            moved += d > 0 ? 1 : 0;
        }

        Assert.True(moved >= 3, $"only {moved} circles moved");
    }

    [Fact]
    public void EveryPeopleMeetsAnEventAndNoTwoShareOne()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(2, 66, 40, 3) { Ai = false }).Basics();
        bool any = false;
        for (int i = 0; i < 12 * SimContent.TicksPerMinute; i++)
        {
            g.Tick();
            var now = new System.Collections.Generic.HashSet<int>();
            foreach (Civ c in g.Civs)
            {
                if (c.Event >= 0)
                {
                    any = true;
                    Assert.True(now.Add(c.Event), "two peoples share an event");
                }
            }
        }

        Assert.True(any);
        Assert.True(g.Civs[0].History.Count >= 11);
    }
}
