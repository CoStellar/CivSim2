using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

public sealed class ResearchTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static SimGame NewGame() => SimGame.Create(Content, new SimSettings(1, 66, 40, 2));

    [Fact]
    public void ATechnologyNeedsExperiencePriceAndTime()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        int tools = Content.TechId("stone_tools");
        Assert.Contains("experience", g.Apply(new ResearchCommand(civ.Id, tools)), System.StringComparison.Ordinal);
        civ.Experience[Content.TechId("foraging")] = 1000 * SimContent.TicksPerMinute;
        Assert.Null(g.Apply(new ResearchCommand(civ.Id, tools)));
        Assert.NotNull(g.Apply(new ResearchCommand(civ.Id, Content.TechId("fishing"))));
        for (int i = 0; i <= Content.Techs[tools].ResearchTicks; i++)
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
            civ.Stock[i] = 500 * SimContent.One;
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
        while (civ.ThinkerOffer is null)
        {
            g.Tick();
        }

        for (int i = 0; i < civ.Stock.Length; i++)
        {
            civ.Stock[i] = 900 * SimContent.One;
        }

        Person offer = civ.ThinkerOffer;
        Assert.Null(g.Apply(new HireCommand(civ.Id, false)));
        Assert.Same(offer, civ.Thinker);
    }
}
