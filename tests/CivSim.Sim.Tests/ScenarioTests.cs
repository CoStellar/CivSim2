using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Test scenarios (Patryk, 2026-11-19): every file in data/sim/scenarios sets up and runs.</summary>
public sealed class ScenarioTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void EveryScenarioSetsUpAsWrittenAndRuns()
    {
        var all = SimScenario.LoadAll(SimScenario.Folder());
        Assert.NotEmpty(all);
        foreach (SimScenario sc in all)
        {
            SimGame g = SimGame.FromScenario(Content, sc);
            Assert.True(g.DebugOn == sc.Debug, sc.Key);
            for (int i = 0; i < sc.Civs.Count; i++)
            {
                ScenarioPeople p = sc.Civs[i];
                Civ civ = g.Civs[i];
                int era = p.Era.Length == 0 ? 0 : g.Content.Eras.ToList().FindIndex(e => e.Key == p.Era);
                Assert.True(civ.Era >= era, $"{sc.Key} people {i}: era {civ.Era} < {era}");
                Assert.True(civ.Settlements.Count >= 1 + (p.Settlements > 0 ? 1 : 0), $"{sc.Key} people {i}: {civ.Settlements.Count} settlements");
                Settlement s = g.Settlements[civ.Settlements[0]];
                if (p.Level.Length > 0)
                {
                    Assert.Equal(p.Level, g.Content.Levels[s.Level].Key);
                }

                // Buildings stand where the land allows (a mine needs its deposit): never more than asked, most of them placed.
                int placed = 0;
                foreach ((string key, int count) in p.Buildings)
                {
                    int have = civ.Buildings.Count(b => g.Content.Buildings[g.Buildings[b].Type].Key == key && g.Buildings[b].State == BuildingState.Active);
                    Assert.True(have <= count, $"{sc.Key} people {i}: {have} of {count} {key}");
                    placed += have;
                }

                Assert.True(placed * 3 >= p.Buildings.Values.Sum() * 2, $"{sc.Key} people {i}: only {placed} buildings placed");

                int units = p.Units.Values.Sum();
                Assert.Equal(units, civ.Units.Count(u => g.Units[u].State != UnitState.Gone));
            }

            foreach (int[] w in sc.Wars)
            {
                Assert.True(g.AtWar(w[0], w[1]), $"{sc.Key}: war {w[0]}-{w[1]}");
            }

            for (int t = 0; t < 2 * SimContent.TicksPerMinute; t++)
            {
                g.Tick();
            }
        }
    }

    [Fact]
    public void DebugActionsWorkOnlyInScenarios()
    {
        SimScenario sc = SimScenario.LoadAll(SimScenario.Folder()).First();
        SimGame g = SimGame.FromScenario(Content, sc);
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        long food = s.Stock[Content.Food];
        Assert.Null(g.Apply(new DebugCommand(civ.Id, DebugAction.Goods)));
        Assert.Equal(food + (200 * SimContent.One), s.Stock[Content.Food]);
        int era = civ.Era;
        Assert.Null(g.Apply(new DebugCommand(civ.Id, DebugAction.NextEra)));
        Assert.Equal(era + 1, civ.Era);

        SimGame normal = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false });
        Assert.NotNull(normal.Apply(new DebugCommand(0, DebugAction.Goods)));
    }
}
