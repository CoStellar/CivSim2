using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Episodes;

/// <summary>Supply and peace after the tuning of the war (stage 4e PR 6).</summary>
public class WarTuningTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Food => Content.Goods.Single(g => g.Key == "wild_plants").Id;

    [Fact]
    public void CarriersComeFromTheSettlementWithTheMostFood()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(101, 10);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 75);
        SettlementState poor = ArmyTests.Village(state, a, start, 2, 100, 0, 1);
        SettlementState rich = ArmyTests.Village(state, a, start, 4, 100, 0, 300);
        SettlementState target = ArmyTests.Village(state, b, start, 10, 40, 3);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 50).ApplyWithChronicle(state, a.Id);
        TurnRunner runner = ArmyTests.Runner(state);
        ArmyTests.FirstStep(runner);
        Episode e = state.Episodes.All[0];
        Assert.Equal(rich.Id.Value, e.ConvoyFrom);
        Assert.True(poor.Stock[Food] == Amount.FromUnits(1), "the nearly empty settlement keeps its crumbs");
    }

    [Fact]
    public void WarriorsGatheringAtHomeEatFromItsStores()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(102, 10);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 60, 300);
        SettlementState other = ArmyTests.Village(state, a, start, 6, 100, 30, 100);
        SettlementState target = ArmyTests.Village(state, b, start, 10, 40, 3);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 30, [new CampaignMuster(other.Id, 20)]).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        Assert.Equal(CampaignPhase.Muster, e.Phase);
        Amount packed = Campaigns.FoodIn(Content, e.Food);
        Amount stores = home.Stock[Food];
        TurnRunner runner = ArmyTests.Runner(state);
        ArmyTests.FirstStep(runner);
        Assert.Equal(CampaignPhase.Muster, e.Phase);
        Assert.Equal(packed, Campaigns.FoodIn(Content, e.Food));
        Assert.True(home.Stock[Food] < stores, "home fed the gathering warriors");
    }

    [Fact]
    public void TheCommanderSendsNoArmyTheLandCannotFeed()
    {
        // Within the carriers' reach the whole way, but the settlements hold food for a month or two of the campaign.
        Assert.Single(Launches(food: 300));
        Assert.Empty(Launches(food: 12));
    }

    [Fact]
    public void ASideThatIsLosingBadlyBuysPeace()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(103, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        ArmyTests.Village(state, a, start, 0, 200, 20, 300);
        ArmyTests.Village(state, b, start, 6, 200, 20, 300);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Revenge, -1);
        Treaty war = state.Treaties.Find(a.Id, b.Id, TreatyKind.War)!;
        war.Score = war.A == a.Id ? 80 : -80; // a is far ahead
        war.WearinessA = 70;
        war.WearinessB = 70;
        var queue = new CommandQueue();
        new DiplomatAdvisor(Content).IssueCommands(state, queue);
        var commands = new List<CommandEnvelope>();
        queue.DrainSorted(state, state.Turn, commands);
        ProposeTreatyCommand? peace = commands.Select(c => c.Command).OfType<ProposeTreatyCommand>().FirstOrDefault(p => p.Kind == TreatyKind.Peace);
        Assert.NotNull(peace);
        Assert.Equal(PeaceTermsKind.Ransom, peace!.Terms.Kind);
        Assert.Equal(b.Id, peace.Terms.Giver);
    }

    [Fact]
    public void ADeclinedPeaceMayBeProposedAgainAfterAYear()
    {
        Assert.Equal(1, Content.Treaties.RetryYearsFor(TreatyKind.Peace));
        Assert.Equal(Content.Treaties.RetryYears, Content.Treaties.RetryYearsFor(TreatyKind.Trade));
    }

    private static List<LaunchCampaignCommand> Launches(int food)
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(104, 6);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        ArmyTests.Village(state, a, start, 0, 200, 75, food);
        ArmyTests.Village(state, b, start, 6, 40, 3, 100);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Alarms = Alarm.WarDeclared;
        }

        var queue = new CommandQueue();
        new CommanderAdvisor(Content).IssueCommands(state, queue);
        var commands = new List<CommandEnvelope>();
        queue.DrainSorted(state, state.Turn, commands);
        return commands.Select(c => c.Command).OfType<LaunchCampaignCommand>().ToList();
    }
}
