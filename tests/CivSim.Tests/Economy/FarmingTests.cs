using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Research;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Economy;

/// <summary>Settling down and farming (stage 3f, GDD 4.4 and 5.3).</summary>
public class FarmingTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Village => Content.SettlementLevel("village").Id;

    private static GameState NewGame(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out _);

    private static void Teach(GameState state, CivilizationState civ, params string[] keys)
    {
        foreach (string key in keys)
        {
            Technologies.Learn(Content, state, civ, Content.Technology(key).Id);
        }
    }

    [Fact]
    public void ACampSettlesDownOnlyWithAgricultureAndEnoughPeople()
    {
        GameState state = NewGame(51);
        SettlementState camp = state.Settlements[0];
        CivilizationState civ = state.FindCivilization(camp.Owner)!;
        var settle = new SettleCommand(Content, camp.Id);
        Assert.Equal(Village, SettleCommand.VillageLevel(Content, camp.Level));
        Assert.Equal("Settling down needs Agriculture.", settle.Validate(state, civ.Id).Reason);

        Teach(state, civ, "fire", "flint_knapping", "agriculture");
        camp.Population[0] = 10;
        Assert.False(settle.Validate(state, civ.Id).Success);       // too few people

        camp.Population[0] = 80;
        Assert.True(settle.Validate(state, civ.Id).Success);
        settle.Apply(state, civ.Id);
        Assert.Equal(Village, camp.Level);
        Assert.False(new MoveSettlementCommand(Content, camp.Id, state.Settlements[1].Cell).Validate(state, civ.Id).Success);
    }

    [Fact]
    public void VillagersFarmTheBestLandAndTheRestGather()
    {
        GameState state = NewGame(52);
        SettlementState village = state.Settlements[0];
        village.Level = Village;
        village.Population[0] = 300;
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        runner.RunTurn();

        // Every farmable cell of the zone takes at most farmersPerCell farmers.
        Assert.InRange(village.Farmers, 1, 300);
        Assert.True(village.StockView[Content.Good("grain").Id] > Amount.Zero || village.FoodEaten > 0);

        // A camp next door does not farm.
        Assert.Equal(0, state.Settlements[1].Farmers);
    }

    [Fact]
    public void FarmedLandWearsOutAndRecoversWhenLeft()
    {
        GameState state = NewGame(53);
        WorldMap map = state.Worlds[0].Map;
        SettlementState village = state.Settlements[0];
        village.Level = Village;
        village.Population[0] = 2000;   // more than the zone can take: every farmable cell is worked in full
        var farming = new FarmingSystem(Content);
        var runner = new TurnRunner(state);
        runner.AddSystem(new CivSim.Core.Population.InfluenceSystem(Content, TurnPhase.World));
        runner.AddSystem(farming);
        for (int t = 0; t < 6; t++)
        {
            runner.RunTurn();
        }

        int worn = 0;
        for (int i = 0; i < map.Fertility.Length; i++)
        {
            Assert.True(map.Fertility[i] <= map.NaturalFertility[i]);
            worn += map.Fertility[i] < map.NaturalFertility[i] ? 1 : 0;
        }

        Assert.True(worn > 0, "farming wears the land out");

        // Nobody farms any more: the land creeps back to its natural fertility.
        village.Population.Clear();
        int before = 0;
        for (int i = 0; i < map.Fertility.Length; i++)
        {
            before += map.NaturalFertility[i] - map.Fertility[i];
        }

        for (int t = 0; t < 10; t++)
        {
            runner.RunTurn();
        }

        int after = 0;
        for (int i = 0; i < map.Fertility.Length; i++)
        {
            after += map.NaturalFertility[i] - map.Fertility[i];
        }

        Assert.True(after < before, $"{after} vs {before}");
    }

    [Fact]
    public void GrainDependsOnFertilityAndSeason()
    {
        GameState state = NewGame(54);
        WorldMap map = state.Worlds[0].Map;
        var farming = new FarmingSystem(Content);
        int best = -1;
        for (int i = 0; i < map.Fertility.Length; i++)
        {
            if (!Content.Biomes[map.Biome[i]].IsWater && map.GrowingSeason[i] >= 60 && map.Fertility[i] >= 50)
            {
                best = i;
                break;
            }
        }

        Assert.True(best >= 0);
        long rich = farming.GrainPerFarmer(map, best);
        Assert.True(rich >= Content.Population.GrainPerFarmerPerYear.Hundredths, $"{rich}");
        map.Fertility.Set(best, (byte)(Content.Population.MinFarmFertility - 1));
        Assert.Equal(0, farming.GrainPerFarmer(map, best));
    }
}
