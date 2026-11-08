using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>
/// AI fixes after stage 4g (100 games): wars for loot and for land that the AI never declared, and alliances that hardly
/// ever formed.
/// </summary>
public class GreedAndAllianceTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Warriors => Content.Strata.Single(x => x.Key == "warriors").Id;

    // Two peoples who met: the first, strong, reviews its relations now; the second has a settlement of `level` a few
    // cells from the first one's camp, seen now, with few warriors and full stores. `fedPercent`: how well the first is fed.
    private static (GameState State, CivilizationState A, CivilizationState B, SettlementState Target) Neighbours(ulong seed, string level, int fedPercent)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        Contacts.Meet(Content, state, a, b.Id, ContactWay.Sighted);
        Contacts.Meet(Content, state, b, a.Id, ContactWay.Sighted);
        int grain = Content.Good("grain").Id;
        SettlementState home = state.Settlements.First(x => x.Owner == a.Id && x.IsActive);
        foreach (SettlementState s in state.Settlements.Where(x => x.Owner == a.Id && x.IsActive))
        {
            s.Stock.Clear();
            s.FoodNeeded = 1000;
            s.FoodEaten = fedPercent * 10;
        }

        home.Population[Warriors] = 60;
        home.Stock[Content.Good("stone_tools").Id] = Amount.FromUnits(50);
        home.Stock[grain] = Amount.FromUnits(20);
        Assert.True(FreeLandNear(state, home.Cell, 4, out HexCoord at));
        SettlementState target = state.FoundSettlement(b.Id, state.Worlds[0].Id, at, "Rich", Content.SettlementLevel(level).Id, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        target.Population[Content.Strata[0].Id] = 40;
        target.Population[Warriors] = 4;
        target.Stock[grain] = Amount.FromUnits(800);

        // Only the first people decides: it was alarmed, so its diplomat reviews its relations at once.
        foreach (CivilizationState c in state.Civilizations)
        {
            c.StepMonths = 0;
            Array.Fill(c.Traits, 500);
        }

        a.Traits[(int)Trait.Caution] = 1000; // takes the best step outright
        a.Relation(b.Id)!.Sympathy = -3000; // not friends: no trade agreement to choose instead
        a.Alarms |= CivSim.Core.Simulation.Alarm.WarDeclared;
        a.StepMonths = 12;
        Array.Fill(a.SeenYear, (int)(state.Month / 12) + 1);
        a.StepStartMonth = state.Month;
        return (state, a, b, target);
    }

    private static List<DeclareWarCommand> Wars(GameState state, CivilizationState civ, out AiCouncil council)
    {
        council = new AiCouncil(Content);
        var queue = new CommandQueue();
        council.IssueCommands(state, queue);
        var commands = new List<CommandEnvelope>();
        queue.DrainSorted(state, state.Turn, commands);
        return commands.Where(c => c.Civ == civ.Id).Select(c => c.Command).OfType<DeclareWarCommand>().ToList();
    }

    // The decisions of `civ`, for a failed assertion.
    private static string Explain(AiCouncil council, CivilizationState civ) => string.Join('\n', council.Log.Entries.Where(d => d.Civ == civ.Id)
        .Select(d => $"{d.Action} {d.Target} {d.Score} {Decision.Describe(d.First)}, {Decision.Describe(d.Second)}, {Decision.Describe(d.Third)}"));

    [Fact]
    public void AHungryPeopleRaidsTheFullStoresOfAWeakNeighbour()
    {
        (GameState state, CivilizationState a, CivilizationState b, _) = Neighbours(51, "camp", fedPercent: 50);
        var all = Wars(state, a, out AiCouncil council);
        Assert.True(all.Count == 1, Explain(council, a));
        DeclareWarCommand war = all[0];
        Assert.Equal(b.Id, war.Target);
        Assert.Equal(WarGoal.Loot, war.Goal);
        Decision why = council.Log.Entries.Single(d => d.Action == AiAction.DeclareWar);
        Assert.Contains(new[] { why.First, why.Second, why.Third }, f => f.Id is FactorId.Fed or FactorId.Loot or FactorId.Strength);

        // Well fed, the same people leaves the camp alone: no cause, and no need.
        (state, a, _, _) = Neighbours(51, "camp", fedPercent: 100);
        Assert.Empty(Wars(state, a, out _));
    }

    [Fact]
    public void APeopleThatWantsLandGoesToWarForAVillageItsArmyWouldTake()
    {
        (GameState state, CivilizationState a, CivilizationState b, SettlementState village) = Neighbours(52, "village", fedPercent: 100);
        Expand(a);
        var all = Wars(state, a, out AiCouncil council);
        Assert.True(all.Count == 1, Explain(council, a));
        DeclareWarCommand war = all[0];
        Assert.Equal(b.Id, war.Target);
        Assert.Equal(WarGoal.Land, war.Goal);
        Assert.Equal(village.Id.Value, war.GoalSettlement);

        // Unseen, the village is not a target (no hidden knowledge).
        (state, a, _, village) = Neighbours(52, "village", fedPercent: 100);
        Expand(a);
        a.SeenYear[state.Worlds[0].Geometry.Index(village.Cell)] = 0;
        Assert.Empty(Wars(state, a, out _));
    }

    // A people whose strategist wants land: expansion weight 2, the other goals neutral.
    private static void Expand(CivilizationState civ)
    {
        civ.GoalWeights = Enumerable.Repeat(1000, Character.GoalCount).ToArray();
        civ.GoalWeights[(int)Goal.Expansion] = 2000;
    }

    [Fact]
    public void ACommonEnemyBringsPartnersIntoAnAlliance()
    {
        Assert.False(AcceptsAlliance(commonEnemy: false));
        Assert.True(AcceptsAlliance(commonEnemy: true));
    }

    // The second people proposes an alliance to the first, with whom it has a pact; a third people attacked the first or not.
    private static bool AcceptsAlliance(bool commonEnemy)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(53), Content, out _);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        CivilizationState c = state.Civilizations[2];
        foreach (CivilizationState x in state.Civilizations)
        {
            x.StepMonths = 0;
            Array.Fill(x.Traits, 500);
            Array.Fill(x.SeenYear, 0);
        }

        foreach ((CivilizationState x, CivilizationState y) in new[] { (a, b), (b, a), (a, c), (c, a) })
        {
            Contacts.Meet(Content, state, x, y.Id, ContactWay.Sighted);
        }

        Relation toB = a.Relation(b.Id)!;
        toB.Sympathy = 0;
        toB.Trust = 1000;
        Treaties.Sign(Content, state, a.Id, b.Id, TreatyKind.NonAggression);
        if (commonEnemy)
        {
            Contacts.RecordAttack(Content, state, a.Id, c.Id);
        }

        new ProposeTreatyCommand(Content, a.Id, TreatyKind.DefensiveAlliance).Apply(state, b.Id);
        var council = new AiCouncil(Content);
        council.IssueCommands(state, new CommandQueue());
        Decision answer = council.Log.Entries.Single(d => d.Civ == a.Id && d.Action is AiAction.AcceptTreaty or AiAction.DeclineTreaty);
        return answer.Action == AiAction.AcceptTreaty;
    }

    // A land cell `distance` cells from `center` with no settlement on it.
    private static bool FreeLandNear(GameState state, HexCoord center, int distance, out HexCoord cell)
    {
        WorldState earth = state.Worlds[0];
        var ring = new HexCoord[HexGlobe.RangeSize(distance)];
        int n = earth.Geometry.Range(center, distance, ring);
        for (int k = n - 1; k >= 0; k--)
        {
            HexCoord at = ring[k];
            if (!Content.Biomes[earth.Map.Biome[earth.Geometry.Index(at)]].IsWater && !state.Settlements.Any(x => x.Cell == at))
            {
                cell = at;
                return true;
            }
        }

        cell = center;
        return false;
    }
}
