using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Population;
using CivSim.Core.Research;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Population;

/// <summary>Learning skills (stage 4F, PR 2, ADR 0020): masters, apprentices, moving, loss, skilled work.</summary>
public class SkillLearningTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static int Skill(string key) => Content.Skills.Single(s => s.Key == key).Id;

    private const int Producers = 0;
    private const int Artisans = 1;

    private static (GameState State, HexCoord[] Land) Empty(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        state.RemoveSettlements(_ => true);
        WorldState world = state.Worlds[0];
        var land = new List<HexCoord>();
        for (int i = 0; i < world.Geometry.CellCount && land.Count < 400; i++)
        {
            if (!Content.Biomes[world.Map.Biome[i]].IsWater)
            {
                land.Add(world.Geometry.FromIndex(i));
            }
        }

        return (state, land.ToArray());
    }

    private static SettlementState Camp(GameState state, CivId civ, HexCoord cell, int producers, int artisans = 0)
    {
        SettlementState s = state.FoundSettlement(civ, state.Worlds[0].Id, cell, "Camp", Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Producers] = producers;
        s.Population[Artisans] = artisans;
        return s;
    }

    private static void Run(GameState state, params ITurnSystem[] systems)
    {
        var runner = new TurnRunner(state);
        foreach (ITurnSystem s in systems)
        {
            runner.AddSystem(s);
        }

        runner.RunTurn();
    }

    private static void Learn(GameState state, CivilizationState civ, params string[] keys)
    {
        foreach (string key in keys)
        {
            Technologies.Learn(Content, state, civ, Content.Technology(key).Id);
        }
    }

    [Fact]
    public void FirstMastersAppearWhereMostOfTheStratumLive()
    {
        (GameState state, HexCoord[] land) = Empty(61);
        CivilizationState civ = state.Civilizations[0];
        SettlementState small = Camp(state, civ.Id, land[0], 20);
        SettlementState big = Camp(state, civ.Id, land[20], 50);
        Learn(state, civ, "flint_knapping", "cooperative_hunting");

        Assert.Equal(Content.SkillRules.FirstMasters, big.SkilledView[Skill("hunter")]);
        Assert.Equal(0, small.SkilledView[Skill("hunter")]);
    }

    [Fact]
    public void MastersTakeApprenticesWhoLearnTheirYears()
    {
        (GameState state, HexCoord[] land) = Empty(62);
        CivilizationState civ = state.Civilizations[0];
        SettlementState camp = Camp(state, civ.Id, land[0], 100);
        Learn(state, civ, "flint_knapping", "cooperative_hunting");
        int hunter = Skill("hunter");
        Assert.Equal(2, camp.SkilledView[hunter]);

        // A Stone Age step (50 years) is longer than the 3 years of training: last step's apprentices are hunters now,
        // and each master takes up to 3 new ones, up to the 20% of the producers the skill can use.
        Run(state, new SkillSystem(Content));
        Assert.Equal(2, camp.SkilledView[hunter]);
        Assert.Equal(6, camp.TraineesView[hunter]);

        Run(state, new SkillSystem(Content));
        Assert.Equal(8, camp.SkilledView[hunter]);
        Assert.Equal(12, camp.TraineesView[hunter]);   // 20 can be used: 8 hunters + 12 learning

        // Apprentices work at half: 8 + 12 / 2 = 14 workers.
        Assert.Equal(14, Skills.Workers(Content, camp, hunter));
    }

    [Fact]
    public void ALostSkillComesBackOnlyFromATradePartner()
    {
        (GameState state, HexCoord[] land) = Empty(63);
        CivilizationState civ = state.Civilizations[0];
        CivilizationState partner = state.Civilizations[1];
        SettlementState camp = Camp(state, civ.Id, land[0], 60);
        SettlementState theirs = Camp(state, partner.Id, land[200], 60);
        Learn(state, civ, "flint_knapping", "cooperative_hunting");
        Learn(state, partner, "flint_knapping", "cooperative_hunting");
        theirs.Skilled[Skill("hunter")] = 10;
        camp.Skilled.Clear(); // the last hunters died

        Run(state, new SkillSystem(Content));
        Assert.Equal(0, camp.SkilledView[Skill("hunter")]);

        state.Treaties.Add(new CivSim.Core.Diplomacy.Treaty(civ.Id, partner.Id, CivSim.Core.Diplomacy.TreatyKind.Trade, civ.Id, 0));
        Run(state, new SkillSystem(Content));
        Assert.Equal(1, camp.SkilledView[Skill("hunter")]);
    }

    [Fact]
    public void SpareSkilledPeopleMoveToASettlementThatNeedsThem()
    {
        (GameState state, HexCoord[] land) = Empty(64);
        CivilizationState civ = state.Civilizations[0];
        WorldState world = state.Worlds[0];
        SettlementState rich = Camp(state, civ.Id, land[0], 100);
        HexCoord near = land.First(c => world.Geometry.Distance(c, land[0]) is > 2 and <= 6);
        SettlementState poor = Camp(state, civ.Id, near, 100);
        Learn(state, civ, "flint_knapping", "cooperative_hunting");
        int hunter = Skill("hunter");
        rich.Skilled.Clear();
        rich.Skilled[hunter] = 40;   // 20 can be used: 20 spare
        poor.Skilled.Clear();

        Run(state, new SkillSystem(Content));

        Assert.Equal(10, poor.SkilledView[hunter]); // half the spare, with their place among the producers
        Assert.Equal(110, poor.PopulationView[Producers]);
        Assert.Equal(90, rich.PopulationView[Producers]);
    }

    [Fact]
    public void SettlersTakeAtLeastOneOfEachSkill()
    {
        (GameState state, HexCoord[] land) = Empty(65);
        CivilizationState civ = state.Civilizations[0];
        SettlementState village = Camp(state, civ.Id, land[0], 100);
        village.Skilled[Skill("farmer")] = 3;
        var split = new SplitSettlementCommand(Content, village.Id, land[30], 10);
        split.Apply(state, civ.Id);
        CivSim.Core.Units.Unit settlers = state.Units.All[^1]; // stage 6 PR 3a: they walk as a unit first

        Assert.Equal(1, settlers.Skilled[Skill("farmer")]);
        Assert.Equal(2, village.SkilledView[Skill("farmer")]);
    }

    [Fact]
    public void OnlySkilledArtisansWorkTheirRecipe()
    {
        (GameState state, HexCoord[] land) = Empty(66);
        SettlementState s = Camp(state, state.Civilizations[0].Id, land[0], 20, artisans: 10);
        RecipeDefinition sew = Content.Recipes.Single(r => r.Key == "sew_hide_clothing");
        Assert.Equal(Skill("tanner"), Content.SkillLinks.ForRecipe(sew.Id));
        Assert.Equal(0, Demand.Runs(Content, s, sew, 12));

        s.Skilled[Skill("tanner")] = 2;
        s.Trainees[Skill("tanner")] = 2;   // at half: 3 workers of 10 artisans
        Assert.Equal(3L * 100 * 12 / (12L * sew.Years * sew.Workers), Demand.Runs(Content, s, sew, 12));
    }

    [Fact]
    public void ATechnologysGoodsComeInAsFarAsItsSkilledPeopleCoverTheWork()
    {
        (GameState state, HexCoord[] land) = Empty(67);
        SettlementState s = Camp(state, state.Civilizations[0].Id, land[0], 100);
        int hunter = Skill("hunter");
        Assert.Equal(0, Skills.CoveragePerMille(Content, s, hunter));
        s.Skilled[hunter] = 10;   // 20 can be used
        Assert.Equal(500, Skills.CoveragePerMille(Content, s, hunter));
        s.Skilled[hunter] = 30;
        Assert.Equal(1000, Skills.CoveragePerMille(Content, s, hunter));
    }
}
