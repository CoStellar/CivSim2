using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Ores mode (Patryk, 2026-11-24): fair puts the metals near every start, realistic leaves the world as it was made.</summary>
public sealed class OreModeTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static int Nearest(SimGame g, int center, string key)
    {
        int dep = Content.World.Deposits.First(d => d.Key == key).Id;
        int best = int.MaxValue;
        for (int cell = 0; cell < g.Geo.CellCount; cell++)
        {
            if (g.Map.Deposit[cell] == dep)
            {
                best = System.Math.Min(best, g.Geo.Distance(g.Geo.FromIndex(center), g.Geo.FromIndex(cell)));
            }
        }

        return best;
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void FairOresPutTinAndIronNearEveryStart()
    {
        for (ulong seed = 1; seed <= 3; seed++)
        {
            SimGame g = SimGame.Create(Content, new SimSettings(seed, 98, 60, 4) { Ai = false });
            foreach (Civ civ in g.Civs)
            {
                int center = g.Settlements[civ.Settlements[0]].Center;
                Assert.True(Nearest(g, center, "copper") <= 7, $"seed {seed} civ {civ.Id} copper");
                Assert.True(Nearest(g, center, "tin") <= 8, $"seed {seed} civ {civ.Id} tin");
                Assert.True(Nearest(g, center, "iron_ore") <= 12, $"seed {seed} civ {civ.Id} iron");
            }
        }
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void RealisticOresAddNothingToTheWorld()
    {
        SimGame fair = SimGame.Create(Content, new SimSettings(2, 98, 60, 4) { Ai = false });
        SimGame real = SimGame.Create(Content, new SimSettings(2, 98, 60, 4) { Ai = false, FairOres = false });
        int Count(SimGame g) => Enumerable.Range(0, g.Geo.CellCount).Count(c => g.Map.Deposit[c] != 0);
        Assert.True(Count(real) < Count(fair), $"realistic {Count(real)}, fair {Count(fair)}");
    }
}
