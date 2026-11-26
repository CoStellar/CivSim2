using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Patryk, 2026-11-24: "nothing is being researched" is a reminder a while after a research ends, not a fixture.</summary>
public sealed class ResearchReminderTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Trait("Category", "Slow")]
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

/// <summary>Abuse tests 2026-11-24: a treaty of an unknown kind (7) was kept as an alliance without the trade agreement first.</summary>
public sealed class TreatyKindTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void AnUnknownTreatyIsRefused()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Assert.Equal("No such treaty.", g.Apply(new ProposeTreatyCommand(0, 1, (TreatyKind)7)));
        Assert.Equal("No such treaty.", g.Apply(new AnswerTreatyCommand(0, 1, (TreatyKind)7, true)));
        Assert.Equal("No such treaty.", g.Apply(new CancelTreatyCommand(0, 1, (TreatyKind)7)));
    }
}

/// <summary>Abuse tests 2026-11-24: a caravan from a settlement of one person made a new person when it came home.</summary>
public sealed class CarrierPeopleTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Trait("Category", "Slow")]
    [Fact]
    public void CarriersASettlementDidNotHaveGoBackToNobody()
    {
        SimGame g = SimGame.FromScenario(Content, SimScenario.Load(System.IO.Path.Combine(SimScenario.Folder(), "02_bronze_age_empire.json")));
        Civ civ = g.Civs[0];
        Settlement a = g.Settlements[civ.Settlements[0]];
        Settlement b = g.Settlements[civ.Settlements[1]];
        int wood = Content.GoodId("wood");
        a.People = 2;
        a.Stock[wood] = 300 * SimContent.One;
        Assert.Null(g.Apply(new SendCaravanCommand(civ.Id, a.Id, b.Id, wood, 300 * SimContent.One)));
        Unit c = g.Units[civ.Units[^1]];
        Assert.Equal(1, a.People);
        Assert.Equal(c.People - 1, c.Lent);
        Assert.Null(g.Apply(new DisbandCommand(civ.Id, c.Id)));
        Assert.Equal(2, a.People);
        a.People = 1;
        Assert.Contains("Nobody", g.Apply(new SendCaravanCommand(civ.Id, a.Id, b.Id, wood, 10 * SimContent.One)));
    }
}
