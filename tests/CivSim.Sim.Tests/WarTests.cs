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
            civ.Stock[i] = 500 * SimContent.One;
        }

        Settlement home = g.Settlements[civ.Settlements[0]];
        home.People = 60;
        Assert.Null(g.Apply(new TrainCommand(civ.Id, home.Id, Content.Unit(kind).Id)));
        Run(g, Content.Unit(kind).TrainTicks + 2);
        Unit u = g.Units[^1];
        HexCoord c = g.Geo.FromIndex(near);
        u.Cell = g.Geo.Index(g.Geo.Neighbor(c, HexDirection.N));
        return u;
    }

    [Fact]
    public void AttackingWithoutWarIsASurpriseThatCostsReputation()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false });
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
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false });
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
            SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false });
            Civ a = g.Civs[0];
            Civ b = g.Civs[1];
            Settlement target = g.Settlements[b.Settlements[0]];
            target.People = 25;
            if (walls)
            {
                b.KnownTech[Content.TechId("masonry")] = true;
                b.Stock[Content.GoodId("stone")] = 500 * SimContent.One;
                BuildingType w = Content.Building("stone_walls");
                int cell = target.Cells.Find(c => g.CannotBuild(b, c, w) is null);
                Assert.Null(g.Apply(new BuildCommand(b.Id, cell, w.Id)));
                Run(g, w.BuildTicks + 1);
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
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false });
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        b.Known[g.Settlements[a.Settlements[0]].Center] = true;
        a.Known[g.Settlements[b.Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(a.Id, b.Id)));
        Assert.Equal(0, a.Reputation);
        long wood = b.Stock[Content.GoodId("wood")];
        Assert.Null(g.Apply(new ProposePeaceCommand(a.Id, b.Id, 20)));
        Assert.Null(g.Apply(new AnswerPeaceCommand(b.Id, a.Id, true)));
        Assert.False(g.AtWar(a.Id, b.Id));
        Assert.Equal(wood - (wood * 20 / 100), b.Stock[Content.GoodId("wood")]);
    }
}

public sealed class AiTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void AiPeoplesGrowBuildResearchAndSpreadOnTheirOwn()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(5, 98, 60, 4));
        for (int i = 0; i < 10 * SimContent.TicksPerMinute; i++)
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
            SimGame g = SimGame.Create(Content, new SimSettings(5, 66, 40, 3));
            for (int i = 0; i < 4 * SimContent.TicksPerMinute; i++)
            {
                g.Tick();
            }

            return g.StateHash();
        }

        Assert.Equal(Play(), Play());
    }
}
