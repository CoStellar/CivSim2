using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class DiagTests
{
    [Fact]
    public void Diag()
    {
        if (System.Environment.GetEnvironmentVariable("DIAG") is null)
        {
            return; // a long AI run printed for tuning: DIAG=1 dotnet test --filter Diag
        }

        SimContent content = SimContent.Load();
        SimGame g = SimGame.Create(content, new SimSettings(5, 98, 60, 4));
        var lines = new System.Collections.Generic.List<string>();
        for (int m = 1; m <= 30; m++)
        {
            for (int i = 0; i < SimContent.TicksPerMinute; i++)
            {
                g.Tick();
            }

            foreach (Civ c in g.Civs)
            {
                if (c.Dead)
                {
                    lines.Add($"m{m} civ{c.Id} dead");
                    continue;
                }

                var s = g.Settlements[c.Settlements[0]];
                lines.Add($"m{m} civ{c.Id} ag{c.Aggression} ex{c.Expansion} people {c.People} sets {c.Settlements.Count} housing {s.Housing} idle {s.Idle} food {c.Stock[0] / 100}({c.Rate[0] / 100}) wood {c.Stock[1] / 100}({c.Rate[1] / 100}) stone {c.Stock[2] / 100} b {c.Buildings.Count} tech {c.KnownTech.Count(k => k)} res {c.Researching} stoneR {c.Rate[2] / 100} agri [{g.CannotResearch(c, content.TechId("agriculture"))}] blds {string.Join(",", c.Buildings.Select(b => content.Buildings[g.Buildings[b].Type].Key))}");
            }
        }

        Assert.True(System.Environment.GetEnvironmentVariable("DIAG") is null, string.Join("\n", lines));
    }
}
