using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Map;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Episodes;

/// <summary>The commander: the battle it expects, road and food, and its look every month of a campaign (stage 4e PR 5).</summary>
public class CommanderTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static BattleRules Rules => Content.War.Battle;

    [Fact]
    public void TheExpectedBattleNeedsNoDrawAndMatchesRealBattles()
    {
        ExpectedBattle strong = Battles.Expect(Rules, 50, 5000, 100, 8, 400);
        ExpectedBattle weak = Battles.Expect(Rules, 8, 400, 100, 50, 5000);
        Assert.Equal(100, strong.ChancePercent);
        Assert.Equal(0, weak.ChancePercent);
        Assert.True(strong.LossPercent < weak.LossPercent);
        Assert.Equal(strong, Battles.Expect(Rules, 50, 5000, 100, 8, 400));

        // Close battles: the expected chance is near the share real battles (with drawn luck) the attackers win.
        foreach ((int men, long attack, int defenders, long defence) in new[] { (30, 2100L, 30, 2100L), (30, 2600L, 30, 2100L), (30, 2100L, 30, 2600L) })
        {
            int wins = 0;
            for (ulong seed = 1; seed <= 300; seed++)
            {
                wins += Battles.Fight(Rules, new DeterministicRandom(seed), men, attack, 100, defenders, defence).AttackersWon ? 1 : 0;
            }

            int expected = Battles.Expect(Rules, men, attack, 100, defenders, defence).ChancePercent;
            Assert.InRange(expected, (wins / 3) - 25, (wins / 3) + 25);
        }
    }

    [Fact]
    public void TheCommanderSendsAnArmyOnlyAgainstWhatItExpectsToBeat()
    {
        Assert.Single(Launches(strongTarget: false, distance: 6));
        Assert.Empty(Launches(strongTarget: true, distance: 6));
    }

    [Fact]
    public void TheCommanderSendsNoArmyFartherThanItsFoodLasts()
    {
        // 6 cells: carriers reach the whole way. 18 cells: 6 cells beyond the reach of carriers, two months there and
        // two back, more than the warriors carry.
        Assert.Single(Launches(strongTarget: false, distance: 6));
        Assert.Empty(Launches(strongTarget: false, distance: 18));
    }

    [Fact]
    public void TheCommanderCallsAnArmyHomeWhenTheFoodWillNotBringItBack()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(95, 18);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 75, 300);
        SettlementState target = ArmyTests.Village(state, b, start, 18, 40, 3);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 50).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        e.Phase = CampaignPhase.March;
        e.Cell = state.Worlds[0].Geometry.Index(new HexCoord(start.Col, start.Row + 14));
        System.Array.Clear(e.Food);
        e.Food[Content.Good("wild_plants").Id] = Campaigns.FoodFor(Content, e.Warriors, 1);

        List<CommandEnvelope> commands = Decide(state);
        Assert.Contains(commands, c => c.Command is RecallCampaignCommand r && r.Episode == e.Id);

        // Fed for the way there and back (two months to the target, two back to the reach of carriers), the army marches on.
        e.Food[Content.Good("wild_plants").Id] = Campaigns.FoodFor(Content, e.Warriors, 4);
        Assert.DoesNotContain(Decide(state), c => c.Command is RecallCampaignCommand);
    }

    [Fact]
    public void ArmiesStandBeforeTheirTargetAMonthBeforeTheyAttack()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(96, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 75, 300);
        SettlementState target = ArmyTests.Village(state, b, start, 3, 40, 3);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 50).ApplyWithChronicle(state, a.Id);
        TurnRunner runner = ArmyTests.Runner(state);
        ArmyTests.FirstStep(runner);
        Episode e = state.Episodes.All[0];
        Assert.Equal(1, state.Worlds[0].Geometry.Distance(state.Worlds[0].Geometry.FromIndex(e.Cell), target.Cell));
        Assert.Equal(0, e.Battles);
        runner.RunTurn();
        Assert.Equal(1, e.Battles);
    }

    [Fact]
    public void OnlyTheOwnerCallsBackACampaignOnTheMarch()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(97, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 75, 300);
        SettlementState target = ArmyTests.Village(state, b, start, 6, 40, 3);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 50).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        var recall = new RecallCampaignCommand(e.Id);
        Assert.False(recall.Validate(state, b.Id).Success);
        Assert.True(recall.Validate(state, a.Id).Success);
        Assert.Contains("called its warriors back", recall.ApplyWithChronicle(state, a.Id), System.StringComparison.Ordinal);
        Assert.Equal(CampaignPhase.Return, e.Phase);
        Assert.False(recall.Validate(state, a.Id).Success, "already going home");
    }

    // The campaigns the commander of the first people would launch against a village `distance` cells away.
    private static List<LaunchCampaignCommand> Launches(bool strongTarget, int distance)
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(94, distance);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        ArmyTests.Village(state, a, start, 0, 200, 75, 300);
        ArmyTests.Village(state, b, start, distance, 40, strongTarget ? 120 : 3, 100);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        return Decide(state).Select(c => c.Command).OfType<LaunchCampaignCommand>().ToList();
    }

    // What the commanders decide now (as at the start of a war).
    private static List<CommandEnvelope> Decide(GameState state)
    {
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Alarms = Alarm.WarDeclared;
        }

        var queue = new CommandQueue();
        new CommanderAdvisor(Content).IssueCommands(state, queue);
        var commands = new List<CommandEnvelope>();
        queue.DrainSorted(state, state.Turn, commands);
        return commands;
    }
}
