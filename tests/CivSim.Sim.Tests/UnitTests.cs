using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class UnitTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static SimGame NewGame() => SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false });

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    private static Unit TrainReady(SimGame g, Civ civ, string kind)
    {
        Settlement s = g.Settlements[civ.Settlements[0]];
        UnitType t = Content.Unit(kind);
        Assert.Null(g.Apply(new TrainCommand(civ.Id, s.Id, t.Id)));
        Run(g, t.TrainTicks + 1);
        Unit u = g.Units[^1];
        Assert.Equal(UnitState.Idle, u.State);
        return u;
    }

    // A land hex at least `distance` from the start that nobody owns, found by walking outward.
    private static int FreeLandAway(SimGame g, Civ civ, int distance)
    {
        Settlement s = g.Settlements[civ.Settlements[0]];
        HexCoord c = g.Geo.FromIndex(s.Center);
        for (int r = distance; r < distance + 8; r++)
        {
            var ring = new HexCoord[HexGlobe.RingSize(r)];
            int n = g.Geo.Ring(c, r, ring);
            for (int k = 0; k < n; k++)
            {
                int i = g.Geo.Index(ring[k]);
                if (g.CannotFound(i) is null && g.FindPath(s.Center, i, new System.Collections.Generic.List<int>()))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    [Fact]
    public void TrainingTakesPeopleAndGoodsAndTime()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        int people = s.People;
        UnitType scouts = Content.Unit("scouts");
        Assert.Null(g.Apply(new TrainCommand(civ.Id, s.Id, scouts.Id)));
        Assert.Equal(people - scouts.People, s.People);
        Assert.Equal(UnitState.Training, g.Units[0].State);
        Run(g, scouts.TrainTicks + 1);
        Assert.Equal(UnitState.Idle, g.Units[0].State);
    }

    [Fact]
    public void ScoutsWalkAndUncoverTheMap()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Unit u = TrainReady(g, civ, "scouts");
        int known = civ.Known.Count(k => k);
        int target = FreeLandAway(g, civ, 10);
        Assert.True(target >= 0);
        Assert.Null(g.Apply(new MoveCommand(civ.Id, u.Id, target, false)));
        Run(g, 2000);
        Assert.Equal(target, u.Cell);
        Assert.Equal(UnitState.Idle, u.State);
        Assert.True(civ.Known.Count(k => k) > known);
    }

    [Fact]
    public void HiddenScoutsWalkSlower()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Unit u = TrainReady(g, civ, "scouts");
        int target = FreeLandAway(g, civ, 8);
        g.Apply(new MoveCommand(civ.Id, u.Id, target, true));
        long start = g.Ticks;
        while (u.State == UnitState.Moving)
        {
            g.Tick();
        }

        long hidden = g.Ticks - start;
        g.Apply(new MoveCommand(civ.Id, u.Id, g.Settlements[civ.Settlements[0]].Center, false));
        start = g.Ticks;
        while (u.State == UnitState.Moving)
        {
            g.Tick();
        }

        Assert.True(hidden > g.Ticks - start, $"hidden {hidden}, open {g.Ticks - start}");
    }

    [Fact]
    public void SettlersFoundACampAwayFromOthers()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        g.Settlements[civ.Settlements[0]].Stock[Content.Food] = 200 * SimContent.One;
        g.Settlements[civ.Settlements[0]].Stock[Content.GoodId("wood")] = 200 * SimContent.One;
        Unit u = TrainReady(g, civ, "settlers");
        Assert.NotNull(g.Apply(new FoundCommand(civ.Id, u.Id)));
        int target = FreeLandAway(g, civ, Content.FoundMinDistance + 1);
        Assert.Null(g.Apply(new MoveCommand(civ.Id, u.Id, target, false)));
        Run(g, 3000);
        Assert.Null(g.Apply(new FoundCommand(civ.Id, u.Id)));
        Assert.Equal(2, civ.Settlements.Count);
        Settlement camp = g.Settlements[civ.Settlements[1]];
        Assert.Equal(target, camp.Center);
        Assert.Equal(Content.Unit("settlers").People, camp.People);
        Assert.Equal(UnitState.Gone, u.State);
    }
}
