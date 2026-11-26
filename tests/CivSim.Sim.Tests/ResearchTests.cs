using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class ResearchTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static SimGame NewGame() => SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();

    [Fact]
    public void ATechnologyNeedsPriceAndTime()
    {
        // Tests 2026-11-23 (Patryk): experience is no longer a brake; the price and the time are.
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        int tools = Content.TechId("stone_tools");
        Assert.Null(g.Apply(new ResearchCommand(civ.Id, tools)));
        Assert.NotNull(g.Apply(new ResearchCommand(civ.Id, Content.TechId("fishing"))));
        long end = civ.ResearchEnd;
        while (g.Ticks < end)
        {
            g.Tick();
        }

        Assert.True(civ.KnownTech[tools]);
        Assert.Equal(-1, civ.Researching);
    }

    [Fact]
    public void BuildingsAndVillagesWaitForTheirTechnology()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        Assert.Contains("Stone tools", g.CannotBuild(civ, s.Cells[1], Content.Building("quarry"), checkPrice: false), System.StringComparison.Ordinal);
        for (int i = 0; i < civ.Stock.Length; i++)
        {
            g.Settlements[civ.Settlements[0]].Stock[i] = 500 * SimContent.One;
        }

        Assert.Contains("Agriculture", g.Apply(new UpgradeSettlementCommand(civ.Id, s.Id)), System.StringComparison.Ordinal);
    }

    [Fact]
    public void WorkersGiveExperienceInTheirCraft()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        for (int i = 0; i < SimContent.TicksPerMinute; i++)
        {
            g.Tick();
        }

        Assert.True(civ.Experience[Content.TechId("foraging")] > 0);
    }

    [Fact]
    public void ABetterThinkerMakesResearchCheaperAndSuccessorsLoseTheEdge()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        int tools = Content.TechId("stone_tools");
        long common = g.ResearchPrice(civ, tools)[Content.Food];
        civ.Thinker = new Person { Name = "Handalf", Rarity = Content.Rarities.Count - 1 };
        long legendary = g.ResearchPrice(civ, tools)[Content.Food];
        civ.Thinker.Generation = 3;
        long third = g.ResearchPrice(civ, tools)[Content.Food];
        Assert.True(legendary < common);
        Assert.True(third > legendary && third < common);
        Assert.Equal("Handalf III", civ.Thinker.FullName);
    }

    [Fact]
    public void CandidatesShowUpAndCanBeHired()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        for (int i = 0; i < civ.Stock.Length; i++)
        {
            g.Settlements[civ.Settlements[0]].Stock[i] = 900 * SimContent.One; // E17: thinkers ask only for goods the civilization knows
        }

        for (int t = 0; t < 30 * SimContent.TicksPerMinute && civ.ThinkerOffer is null; t++)
        {
            g.Tick();
        }

        Assert.NotNull(civ.ThinkerOffer);
        for (int i = 0; i < civ.Stock.Length; i++)
        {
            g.Settlements[civ.Settlements[0]].Stock[i] = 900 * SimContent.One;
        }

        Person offer = civ.ThinkerOffer;
        Assert.Null(g.Apply(new HireCommand(civ.Id, false)));
        Assert.Same(offer, civ.Thinker);
    }
}
