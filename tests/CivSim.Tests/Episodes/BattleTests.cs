using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// <summary>Battles in rounds, terrain, conquest of villages and flight from camps (stage 4e PR 4).</summary>
public class BattleTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static BattleRules Rules => Content.War.Battle;

    private static int Warriors => Content.Strata.Single(s => s.Key == "warriors").Id;

    [Fact]
    public void BattleRulesAreInTheData()
    {
        Assert.Equal(6, Rules.Rounds);
        Assert.Equal((50, 150), Rules.RollPercent);
        Assert.Equal(25, Rules.HillsPercent);
        Assert.Contains(Rules.Biomes, b => Content.Biomes[b.Biome].Key == "deciduous_forest" && b.Percent == 25);
    }

    [Fact]
    public void AnEvenBattleEndsWhenOneSideBreaksAndCostsBothSides()
    {
        int attackerWins = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            BattleResult r = Battles.Fight(Rules, new DeterministicRandom(seed), 30, 30 * 70, 100, 30, 30 * 70);
            Assert.InRange(r.Rounds, 1, Rules.Rounds);
            Assert.True(r.AttackersLost > 0 && r.DefendersLost > 0);
            Assert.True(r.Broken is 1 or 2 || !r.AttackersWon);
            attackerWins += r.AttackersWon ? 1 : 0;
        }

        Assert.InRange(attackerWins, 50, 150); // about even
        Assert.Equal(
            Battles.Fight(Rules, new DeterministicRandom(9), 30, 2100, 100, 30, 2100),
            Battles.Fight(Rules, new DeterministicRandom(9), 30, 2100, 100, 30, 2100));
    }

    [Fact]
    public void AMuchStrongerSideWinsAndLosesLittle()
    {
        for (ulong seed = 1; seed <= 50; seed++)
        {
            BattleResult r = Battles.Fight(Rules, new DeterministicRandom(seed), 50, 50 * 100, 100, 8, 8 * 50);
            Assert.True(r.AttackersWon);
            Assert.True(r.AttackersLost <= 3, $"lost {r.AttackersLost}");
        }

        // Low morale breaks sooner: the same army at 45 morale breaks after its first losses.
        BattleResult shaken = Battles.Fight(Rules, new DeterministicRandom(3), 30, 2100, 45, 30, 2100);
        Assert.Equal(1, shaken.Broken);
    }

    [Fact]
    public void WhenNobodyBreaksTheDefendersHold()
    {
        BattleRules soft = Rules with { CasualtyPercent = 0 };
        BattleResult r = Battles.Fight(soft, new DeterministicRandom(5), 40, 4000, 100, 10, 500);
        Assert.False(r.AttackersWon);
        Assert.Equal(0, r.Broken);
        Assert.Equal(soft.Rounds, r.Rounds);
    }

    [Fact]
    public void HillsForestsAndRiversHelpTheDefenders()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(91), Content, out _);
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        int hills = Enumerable.Range(0, geo.CellCount).First(i => (Relief)world.Map.Relief[i] == Relief.Hills);
        Assert.True(Battles.TerrainPercent(Content, world, hills, -1) >= Rules.HillsPercent);

        // A cell with a river on one side: attacked over the river it is harder than from a dry side.
        for (int i = 0; i < geo.CellCount; i++)
        {
            HexCoord c = geo.FromIndex(i);
            int wet = -1, dry = -1;
            for (int d = 0; d < 6; d++)
            {
                int n = geo.Index(geo.Neighbor(c, (HexDirection)d));
                if (world.Map.River[geo.EdgeIndex(geo.Edge(c, (HexDirection)d))] != 0)
                {
                    wet = n;
                }
                else
                {
                    dry = n;
                }
            }

            if (wet >= 0 && dry >= 0)
            {
                Assert.Equal(Battles.TerrainPercent(Content, world, i, dry) + Rules.RiverPercent, Battles.TerrainPercent(Content, world, i, wet));
                return;
            }
        }

        throw new Xunit.Sdk.XunitException("No river found.");
    }

    [Fact]
    public void AWarForLandTakesTheVillageWithItsPeople()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(92, 8);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 75, 300);
        SettlementState target = ArmyTests.Village(state, b, start, 6, 40, 3);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Land, target.Id.Value);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 50).ApplyWithChronicle(state, a.Id);
        TurnRunner runner = ArmyTests.Runner(state);
        List<string> lines = ArmyTests.RunOut(runner, state);
        Assert.Contains(lines, l => l.Contains("took " + target.Name, System.StringComparison.Ordinal));
        Assert.Equal(a.Id, target.Owner);
        Assert.True(target.IsActive);
        Assert.True(target.TotalPopulation() > 20, "the people stay in the conquered village");
    }

    [Fact]
    public void ThePeopleOfABeatenCampFleeToTheirNearestSettlement()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(93, 10);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 75, 300);
        SettlementState camp = state.FoundSettlement(b.Id, state.Worlds[0].Id, new HexCoord(start.Col, start.Row + 6), "Camp", Content.SettlementLevel("camp").Id, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        camp.Population[Content.Strata[0].Id] = 30;
        camp.Population[Warriors] = 3;
        SettlementState refuge = ArmyTests.Village(state, b, start, 10, 50, 5);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, camp.Id, 50).ApplyWithChronicle(state, a.Id);
        TurnRunner runner = ArmyTests.Runner(state);
        List<string> lines = ArmyTests.RunOut(runner, state);
        Assert.Contains(lines, l => l.Contains("fled to " + refuge.Name, System.StringComparison.Ordinal));
        Assert.False(camp.IsActive);
        Assert.True(refuge.TotalPopulation() > 55, "the camp's people joined the refuge");
    }

    [Fact]
    public void BrokenBattleDataIsReported()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-battle-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "war.json");
            File.WriteAllText(path, File.ReadAllText(path)
                .Replace("{\"biome\": \"taiga\"", "{\"biome\": \"taigaa\"", System.StringComparison.Ordinal)
                .Replace("\"rollPercent\": [50, 150]", "\"rollPercent\": [150, 50]", System.StringComparison.Ordinal));
            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("unknown biome 'taigaa'", System.StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("rollPercent", System.StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
