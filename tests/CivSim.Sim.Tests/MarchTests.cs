using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Known issue 15 (2026-11-23): soldiers marching on one goal share one search of the way.</summary>
public sealed class MarchTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void SoldiersSentTogetherShareTheWay()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        for (ulong seed = 2; !g.Reachable(g.Civs[0], g.Civs[1]) && seed < 40; seed++)
        {
            g = SimGame.Create(Content, new SimSettings(seed, 66, 40, 2) { Ai = false }).Basics();
        }

        Civ a = g.Civs[0];
        Settlement home = g.Settlements[a.Settlements[0]];
        Settlement target = g.Settlements[g.Civs[1].Settlements[0]];
        a.Known[target.Center] = true;
        a.SeenSettlements[target.Center] = new SettlementMemory(target.Id, target.Civ, target.Name, target.Level, target.Center); // our scouts saw it
        for (int i = 0; i < home.Stock.Length; i++)
        {
            home.Stock[i] = 500 * SimContent.One;
        }

        home.People = 120;
        home.Level = 3; // the army limit of a city
        int warriors = Content.Unit("warriors").Id;
        a.KnownTech[Content.Units[warriors].Tech] = true;
        int before = g.Units.Count;
        for (int k = 0; k < 4; k++)
        {
            Assert.Null(g.Apply(new TrainCommand(a.Id, home.Id, warriors, 0)));
        }

        Unit[] army = [.. g.Units.Skip(before).Where(x => x.Civ == a.Id && Content.Units[x.Kind].Army)];
        for (int i = 0; i < 5000 && army.Any(u => u.State == UnitState.Training); i++)
        {
            g.Tick();
        }

        HexCoord c = g.Geo.FromIndex(home.Center);
        for (int k = 0; k < army.Length; k++)
        {
            g.Place(army[k], g.Geo.Index(g.Geo.Neighbor(c, (HexDirection)k)));
        }

        int shared = g.SharedWays;
        foreach (Unit u in army)
        {
            Assert.Null(g.Apply(new AttackCommand(a.Id, u.Id, target.Center)));
        }

        // Each soldier checks the way when sent and walks it: one long search for the first, the rest share it.
        Assert.True(g.SharedWays - shared >= (2 * army.Length) - 2, $"shared {g.SharedWays - shared}");
        Assert.All(army, u => Assert.Equal(UnitState.Moving, u.State));
        Assert.All(army, u => Assert.True(g.Geo.Distance(g.Geo.FromIndex(u.Path[^1]), g.Geo.FromIndex(target.Center)) <= Content.Units[u.Kind].Range));
    }
}
