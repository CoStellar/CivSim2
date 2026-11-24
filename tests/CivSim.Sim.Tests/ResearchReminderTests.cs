using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Patryk, 2026-11-24: "nothing is being researched" is a reminder a while after a research ends, not a fixture.</summary>
public sealed class ResearchReminderTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void TheReminderWaitsAfterTheLastResearch()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        civ.ResearchEnd = g.Ticks;
        Assert.DoesNotContain(g.Alerts(civ), a => a.Kind == AlertKind.NoResearch);
        for (int i = 0; i <= Content.NoResearchSeconds * SimContent.TicksPerSecond; i++)
        {
            g.Tick();
        }

        Assert.Contains(g.Alerts(civ), a => a.Kind == AlertKind.NoResearch);
    }
}
