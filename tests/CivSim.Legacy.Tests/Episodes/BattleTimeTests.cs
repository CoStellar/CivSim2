using System.IO;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Episodes;

/// <summary>Battles fought over time (stage 6, decision of 2026-11-09): a round every <see cref="BattleRules.RoundTicks"/> ticks.</summary>
public class BattleTimeTests
{
    private static readonly GameContent Content = TestContent.Load();

    [Fact]
    public void RoundsOneByOneGiveTheSameBattleAsAllAtOnce()
    {
        BattleRules rules = Content.War.Battle;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            int attackers = 10 + (int)(seed * 7 % 60);
            int defenders = 10 + (int)(seed * 13 % 60);
            BattleResult whole = Battles.Fight(rules, new DeterministicRandom(seed), attackers, attackers * 100L, 100, defenders, defenders * 110L);

            var rng = new DeterministicRandom(seed);
            BattleState b = Battles.Begin(rules, attackers, attackers * 100L, 100, defenders, defenders * 110L);
            int lostA = 0;
            int lostD = 0;
            while (!b.Over)
            {
                (int a, int d) = Battles.Round(rules, rng, ref b);
                lostA += a;
                lostD += d;
            }

            Assert.Equal(whole, b.Result);
            Assert.Equal((whole.AttackersLost, whole.DefendersLost), (lostA, lostD)); // the rounds add up to the battle
        }
    }

    [Fact]
    public void ABattleTakesARoundEverySecondAndCannotBeCalledOff()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(111, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 60, 400);
        SettlementState target = ArmyTests.Village(state, b, start, 4, 40, 40, 200);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 50).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        TurnRunner runner = ArmyTests.Runner(state);
        for (int t = 0; t < 200 && e.Battles == 0; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(1, e.Battles);
        Assert.Equal(CampaignPhase.Battle, e.Phase); // the first round was fought, more are to come
        Assert.Equal(1, e.Fight.Rounds);
        Assert.Equal(state.Turn - 1 + Content.War.Battle.RoundTicks, e.RoundTick); // the tick counter moves on after the systems
        Assert.Equal(e.Fight.MenA, e.Warriors); // the dead of the round fell at once
        Assert.False(new RecallCampaignCommand(e.Id).Validate(state, a.Id).Success, "nobody calls a fighting army back");
        Assert.False(new ReturnUnitCommand(Content, e.Army).Validate(state, a.Id).Success);

        // The next round comes RoundTicks ticks later, not earlier.
        long first = state.Turn;
        while (e.Phase == CampaignPhase.Battle && e.Fight.Rounds == 1)
        {
            runner.RunTurn();
        }

        Assert.Equal(first + Content.War.Battle.RoundTicks, state.Turn);
        Assert.True(e.Fight.Rounds == 2 || e.Phase != CampaignPhase.Battle);

        for (int t = 0; t < 200 && e.Phase == CampaignPhase.Battle; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(CampaignPhase.Return, e.Phase);
        Assert.True(e.Fight.Over);
        Assert.Equal(e.Fight.LostA, e.FightLost); // the army shrank round by round by its dead
        Assert.InRange(e.Fight.Rounds, 2, Content.War.Battle.Rounds);
    }

    [Fact]
    public void PeaceInTheMiddleOfABattleEndsIt()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(112, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 60, 400);
        SettlementState target = ArmyTests.Village(state, b, start, 4, 40, 40, 200);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 50).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        TurnRunner runner = ArmyTests.Runner(state);
        for (int t = 0; t < 200 && e.Battles == 0; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(CampaignPhase.Battle, e.Phase);
        state.Treaties.Remove(a.Id, b.Id, TreatyKind.War);
        for (int t = 0; t <= Content.War.Battle.RoundTicks; t++)
        {
            runner.RunTurn();
        }

        Assert.NotEqual(CampaignPhase.Battle, e.Phase);
        Assert.False(e.Fleeing, "they leave in order");
    }

    [Fact]
    public void ABattleUnderWaySurvivesASave()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(113, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 60, 400);
        SettlementState target = ArmyTests.Village(state, b, start, 4, 40, 40, 200);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 50).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        TurnRunner runner = ArmyTests.Runner(state);
        for (int t = 0; t < 200 && e.Battles == 0; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(CampaignPhase.Battle, e.Phase);
        using var stream = new MemoryStream();
        SaveGameSerializer.Save(state, stream);
        stream.Position = 0;
        GameState loaded = SaveGameSerializer.Load(stream);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
        // Not ArmyTests.Runner: it would clear the alarm the battle raised in the loaded game only.
        var other = new TurnRunner(loaded);
        other.AddSystem(new UnitSystem(Content));
        other.AddSystem(new CampaignSystem(Content));
        other.AddSystem(new WarSystem(Content));
        other.AddSystem(new CalendarSystem(Content));
        WorldClock.Begin(Content, loaded);
        for (int k = 0; k < 3 * Content.War.Battle.RoundTicks; k++)
        {
            runner.RunTurn();
            other.RunTurn();
        }

        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
    }
}
