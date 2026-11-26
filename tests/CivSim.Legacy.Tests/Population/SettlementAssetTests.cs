using System.Linq;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Population;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Population;

/// <summary>Settlements as property (GDD 5.5) and migration with conquest (GDD 5.4, simplified).</summary>
public class SettlementAssetTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static GameState NewState(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out WorldGenReport _);

    private static int Good(string key) => Content.Good(key).Id;

    [Fact]
    public void SettlementDataLoads()
    {
        SettlementLevelDefinition camp = Content.SettlementLevel("camp");
        Assert.True(camp.AbandonedYears > 0 && camp.RuinsUntilYears > camp.AbandonedYears);
        Assert.True(Content.SettlementLevel("city").RuinsUntilYears > camp.RuinsUntilYears);
        Assert.Equal(20, Content.Assets.ClaimYears); // years of action since stage 6 PR 2 (2 minutes at x1)
        Assert.Equal(Amount.FromUnits(10), Content.Assets.CarryPerPerson);
    }

    [Fact]
    public void CarryingIsLimitedAndRaisedByEffects()
    {
        GameState state = NewState(1);
        CivId civ = state.Civilizations[0].Id;
        Assert.Equal(Amount.FromUnits(100), SettlementAssets.CarryCapacity(Content, state, civ, 10));
        state.Effects.Add(Content.Effect("carrying"), WorldId.Earth, civ.Value, 50, state.Month + 1200);
        Assert.Equal(Amount.FromUnits(150), SettlementAssets.CarryCapacity(Content, state, civ, 10));
    }

    [Fact]
    public void AMovingCampLeavesWhatItCannotCarryAtItsOldSite()
    {
        GameState state = NewState(2);
        SettlementState camp = state.Settlements[0];
        HexCoord home = camp.Cell;
        camp.Population.Clear();
        camp.Population[0] = 5; // carry 50 units
        camp.Stock[Good("stone")] = Amount.FromUnits(200);
        HexCoord target = state.Worlds[0].Geometry.Wrap(home.Col + 1, home.Row);

        SettlementState? site = SettlementAssets.Relocate(Content, state, camp, target);

        Assert.NotNull(site);
        Assert.Equal(home, site!.Cell);
        Assert.Equal(SettlementStatus.Abandoned, site.Status);
        Assert.Equal(camp.Owner.Value, site.ClaimCiv);
        Assert.Equal(Amount.FromUnits(50), camp.TotalStock());
        Assert.Equal(camp.TotalStock() + site.TotalStock(), Amount.FromUnits(200) + Content.NewTribeStock.Aggregate(Amount.Zero, (a, q) => a + q.Amount));
    }

    [Fact]
    public void EmptySettlementsDecayIntoRuinsAndDisappear()
    {
        GameState state = NewState(3);
        var runner = new TurnRunner(state);
        runner.AddSystem(new AbandonmentSystem(Content));
        runner.AddSystem(new DecaySystem(Content));
        runner.AddSystem(new CalendarSystem(Content));
        SettlementState camp = state.Settlements[0];
        camp.Population.Clear();
        SettlementLevelDefinition level = Content.SettlementLevels[camp.Level];

        runner.RunTurn();
        Assert.Equal(SettlementStatus.Abandoned, camp.Status);
        Assert.True(camp.HasClaim(state.ActionMonth)); // claims count in months of action

        while ((state.Month - camp.AbandonedMonth) / 12 < level.AbandonedYears)
        {
            runner.RunTurn();
        }

        runner.RunTurn();
        Assert.Equal(SettlementStatus.Ruins, camp.Status);
        while ((state.Month - camp.AbandonedMonth) / 12 < level.RuinsUntilYears)
        {
            runner.RunTurn();
        }

        runner.RunTurn();
        Assert.Null(state.FindSettlement(camp.Id));
    }

    [Fact]
    public void AnotherTribeCanTakeOverAndPlunderButTheClaimStays()
    {
        GameState state = NewState(4);
        SettlementState empty = state.Settlements[0];
        SettlementState other = state.Settlements[1];
        empty.Stock[Good("flint")] = Amount.FromUnits(30);
        SettlementAssets.Abandon(Content, state, empty);
        other.Cell = state.Worlds[0].Geometry.Wrap(empty.Cell.Col + 2, empty.Cell.Row);
        state.Worlds[0].Map.Owner.Set(state.Worlds[0].Geometry.Index(empty.Cell), WorldMap.NoOwner);

        Assert.False(new PlunderSettlementCommand(Content, other.Id, other.Id).Validate(state, other.Owner).Success);
        var plunder = new PlunderSettlementCommand(Content, other.Id, empty.Id);
        Assert.True(plunder.Validate(state, other.Owner).Success, plunder.Validate(state, other.Owner).Reason);
        Amount before = other.StockView[Good("flint")];
        plunder.Apply(state, other.Owner);
        Assert.True(other.StockView[Good("flint")] > before);

        var occupy = new OccupySettlementCommand(Content, other.Id, empty.Id, 100);
        Assert.True(occupy.Validate(state, other.Owner).Success, occupy.Validate(state, other.Owner).Reason);
        string? line = occupy.ApplyWithChronicle(state, other.Owner);

        Assert.True(empty.IsActive);
        Assert.Equal(other.Owner, empty.Owner);
        Assert.Equal(SettlementStatus.Abandoned, other.Status);
        Assert.True(empty.HasClaim(state.ActionMonth));
        Assert.Contains("claimed by", line, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ConquestExpelsTheWeakerCampOrFails()
    {
        int wins = 0;
        for (ulong seed = 10; seed < 20; seed++)
        {
            GameState state = NewState(seed);
            SettlementState attacker = state.Settlements[0];
            SettlementState defender = state.Settlements[1];
            attacker.Population[2] = 200;
            HexCoord where = defender.Cell = state.Worlds[0].Geometry.Wrap(attacker.Cell.Col + 3, attacker.Cell.Row);
            int defenders = defender.TotalPopulation();

            var attack = new ConquerSettlementCommand(Content, attacker.Id, defender.Id);
            Assert.True(attack.Validate(state, attacker.Owner).Success, attack.Validate(state, attacker.Owner).Reason);
            attack.Apply(state, attacker.Owner);

            Assert.True(defender.TotalPopulation() < defenders);
            if (attacker.Cell == where)
            {
                wins++;
                Assert.NotEqual(where, defender.Cell);
            }
        }

        Assert.True(wins >= 7, $"a far stronger camp won only {wins} of 10 fights");
    }

    [Fact]
    public void ACivilizationWithNoPeopleLeftDiesOut()
    {
        GameState state = NewState(5);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        CivilizationState civ = state.Civilizations[0];
        foreach (SettlementState s in state.Settlements.Where(s => s.Owner == civ.Id))
        {
            s.Population.Clear();
        }

        long start = state.Month;
        runner.RunTurn();

        Assert.False(civ.IsAlive);
        Assert.Equal(start, civ.DiedOutMonth);
        Assert.Contains(runner.LastChronicle, l => l.Contains("died out", System.StringComparison.Ordinal));
        Assert.All(state.Settlements.Where(s => s.Owner == civ.Id), s => Assert.False(s.IsActive));
    }
}
