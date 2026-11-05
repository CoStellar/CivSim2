using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.WorldGen;

/// <summary>The sea level follows the ice (stage 1c-C).</summary>
public class SeaLevelTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    [Fact]
    public void TheSeaFallsWithTheIce()
    {
        ClimateCycleRules rules = Content.ClimateCycles;
        Assert.Equal(100, SeaLevel.Target(rules, 0));
        Assert.Equal(97, SeaLevel.Target(rules, 1_000_000)); // 120 m at 40 m per step
        Assert.Equal(99, SeaLevel.Target(rules, 300_000));
    }

    [Fact]
    public void ShelvesFallDryAndFloodAgain()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(21), Content, out _);
        WorldState world = state.Worlds[0];
        WorldMap map = world.Map;
        bool[] waterAtStart = Water(map);

        var lines = SeaLevel.Apply(Content, state, world, 97);
        Assert.NotEmpty(lines);
        int exposed = 0;
        for (int i = 0; i < map.Biome.Length; i++)
        {
            bool water = Content.Biomes[map.Biome[i]].IsWater;
            Assert.False(!waterAtStart[i] && water, "the sea never rises above its starting level");
            if (waterAtStart[i] && !water)
            {
                exposed++;
                Assert.True(map.Elevation[i] >= 97);
            }
        }

        Assert.True(exposed > map.Biome.Length / 100, $"{exposed}");

        SeaLevel.Apply(Content, state, world, 100);
        Assert.Equal(waterAtStart, Water(map));
        Assert.Equal(100, world.Climate.SeaLevel);
        AssertRiversOnlyBetweenLand(world);
    }

    [Fact]
    public void PeopleLeaveFloodedLandAndTheRuinsStayUnderTheSea()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(22), Content, out _);
        WorldState world = state.Worlds[0];
        WorldMap map = world.Map;
        SeaLevel.Apply(Content, state, world, 97);

        // A camp on the dry sea floor, with more goods than its people can carry.
        int shelf = -1;
        for (int i = 0; i < map.Biome.Length && shelf < 0; i++)
        {
            shelf = !Content.Biomes[map.Biome[i]].IsWater && map.Elevation[i] < 100 ? i : -1;
        }

        Assert.True(shelf >= 0);
        CivilizationState civ = state.Civilizations[0];
        SettlementState camp = state.FoundSettlement(civ.Id, world.Id, world.Geometry.FromIndex(shelf), "Shore Camp", Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count);
        camp.Population[0] = 20;
        camp.Stock[0] = Amount.FromUnits(100_000);

        var lines = SeaLevel.Apply(Content, state, world, 100);
        Assert.Contains(lines, l => l.Contains("Shore Camp", System.StringComparison.Ordinal));
        Assert.False(Content.Biomes[map.Biome[world.Geometry.Index(camp.Cell)]].IsWater);
        Assert.Equal(20, camp.TotalPopulation());

        SettlementState ruins = Assert.Single(state.Settlements, s => s.Cell == world.Geometry.FromIndex(shelf) && !s.IsActive);
        Assert.Equal(SettlementStatus.Ruins, ruins.Status);
        // The refugees cannot plunder or move back into their drowned camp.
        Assert.Equal("The ruins lie under the sea.", new PlunderSettlementCommand(Content, camp.Id, ruins.Id).Validate(state, civ.Id).Reason);
        Assert.Equal("The ruins lie under the sea.", new OccupySettlementCommand(Content, camp.Id, ruins.Id, 100).Validate(state, civ.Id).Reason);
    }

    private static bool[] Water(WorldMap map)
    {
        var water = new bool[map.Biome.Length];
        for (int i = 0; i < water.Length; i++)
        {
            water[i] = Content.Biomes[map.Biome[i]].IsWater;
        }

        return water;
    }

    private static void AssertRiversOnlyBetweenLand(WorldState world)
    {
        HexGlobe geo = world.Geometry;
        for (int i = 0; i < geo.CellCount; i++)
        {
            for (int d = 0; d < 6; d++)
            {
                HexEdge edge = geo.Edge(geo.FromIndex(i), (HexDirection)d);
                if (world.Map.River[geo.EdgeIndex(edge)] == 0)
                {
                    continue;
                }

                (HexCoord a, HexCoord b) = geo.EdgeCells(edge);
                Assert.False(Content.Biomes[world.Map.Biome[geo.Index(a)]].IsWater);
                Assert.False(Content.Biomes[world.Map.Biome[geo.Index(b)]].IsWater);
            }
        }
    }
}
