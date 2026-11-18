using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class DiplomacyTests
{
    private static readonly SimContent Content = SimContent.Load();

    // Three peoples that have all met; people 1 is led by the AI so that it answers.
    private static SimGame Met()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 3) { Ai = false }).Basics();
        foreach (Civ a in g.Civs)
        {
            a.KnownTech[Content.TechId("barter")] = true;
            foreach (Civ b in g.Civs)
            {
                a.Known[g.Settlements[b.Settlements[0]].Center] = true;
            }
        }

        return g;
    }

    [Fact]
    public void ATradeAgreementAllowsBarterAndMakesResearchCheaper()
    {
        SimGame g = Met();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        int wood = Content.GoodId("wood");
        int food = Content.Food;
        int tools = Content.TechId("stone_tools");
        Assert.NotNull(g.Apply(new BarterCommand(a.Id, b.Id, food, 10 * SimContent.One, wood, 5 * SimContent.One)));
        long before = g.ResearchPrice(a, tools)[food];
        Assert.Null(g.Apply(new ProposeTreatyCommand(a.Id, b.Id, TreatyKind.Trade)));
        Assert.Null(g.Apply(new AnswerTreatyCommand(b.Id, a.Id, TreatyKind.Trade, true)));
        Assert.True(g.Has(a.Id, b.Id, TreatyKind.Trade));
        Assert.True(g.ResearchPrice(a, tools)[food] < before);

        b.IsAi = true;
        if (!g.Reachable(a, b))
        {
            Assert.Contains("No way", g.Apply(new BarterCommand(a.Id, b.Id, food, 20 * SimContent.One, wood, 10 * SimContent.One)), System.StringComparison.Ordinal);
            return;
        }

        long aWood = a.Stock[wood];
        Assert.Null(g.Apply(new BarterCommand(a.Id, b.Id, food, 20 * SimContent.One, wood, 10 * SimContent.One)));
        Assert.Equal(aWood, a.Stock[wood]); // the wood is on its way in their caravan
        Assert.Contains(g.Units, u => u.Civ == b.Id && u.Job == CarrierJob.ToSettlement);
        for (int i = 0; i < 6000 && !g.Units.TrueForAll(u => u.State == UnitState.Gone); i++)
        {
            g.Tick();
        }

        Assert.True(g.Settlements[a.Settlements[0]].Stock[wood] >= aWood + (10 * SimContent.One) - (5 * SimContent.One), "the caravan brought the wood");
        Assert.NotNull(g.Apply(new BarterCommand(a.Id, b.Id, food, 1 * SimContent.One, wood, 20 * SimContent.One)));
    }

    [Fact]
    public void AnAllyJoinsAWarDeclaredOnItsPartner()
    {
        SimGame g = Met();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        Civ c = g.Civs[2];
        Assert.NotNull(g.Apply(new ProposeTreatyCommand(b.Id, c.Id, TreatyKind.Alliance)));
        g.Apply(new ProposeTreatyCommand(b.Id, c.Id, TreatyKind.Trade));
        g.Apply(new AnswerTreatyCommand(c.Id, b.Id, TreatyKind.Trade, true));
        for (int i = 0; i <= Content.OfferCooldownTicks; i++)
        {
            g.Tick(); // one offer to the same people per cooldown
        }

        Assert.Null(g.Apply(new ProposeTreatyCommand(b.Id, c.Id, TreatyKind.Alliance)));
        g.Apply(new AnswerTreatyCommand(c.Id, b.Id, TreatyKind.Alliance, true));
        Assert.Null(g.Apply(new DeclareWarCommand(a.Id, b.Id)));
        Assert.True(g.AtWar(a.Id, c.Id));
    }
}

public sealed class GoalTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void TheFirstPeopleInTheMiddleAgesWins()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false, Goal = GameGoal.FirstToEra, GoalEra = 4 }).Basics();
        g.Civs[1].Era = 4; // eras are levels: reached by knowing enough technologies of the era before
        for (int i = 0; i < SimContent.TicksPerMinute; i++)
        {
            g.Tick();
        }

        Assert.Equal(1, g.Winner);
    }
}
