using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using Xunit;

namespace CivSim.Tests.Map;

/// <summary>The spatial grid of settlements (stage 6 PR 3a, T2) finds exactly what comparing with every settlement finds.</summary>
public class SettlementGridTests
{
    private static readonly GameContent Content = TestContent.Load();

    [Theory]
    [InlineData(3UL, 66, 40)]
    [InlineData(4UL, 70, 42)] // a width and height that are not multiples of the bucket
    public void TheGridFindsWhatAScanFinds(ulong seed, int width, int height)
    {
        GameState state = GameState.CreateNew(new GameSettings(seed, width, height, 8), Content, out WorldGenReport _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        for (int t = 0; t < 30; t++)
        {
            runner.RunTurn();
        }

        // Many settlements all over the map, near the poles and the date line too.
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        var random = new System.Random(5);
        for (int k = 0; k < 120; k++)
        {
            var cell = new HexCoord(random.Next(geo.Width), random.Next(geo.Height));
            state.FoundSettlement(state.Civilizations[k % state.Civilizations.Count].Id, world.Id, cell, $"g{k}", 0, Content.Goods.Count);
        }

        foreach (int radius in new[] { 0, 1, 3, 7, 12, 20, 40 })
        {
            for (int q = 0; q < 60; q++)
            {
                var center = new HexCoord(random.Next(geo.Width), q % 6 == 0 ? 0 : q % 6 == 1 ? geo.Height - 1 : random.Next(geo.Height));
                int[] scan = [.. SettlementGrid.WithinByScan(state, world, center, radius)];
                int[] grid = [.. state.Grid.Within(world, center, radius)];
                Assert.Equal(scan, grid);
            }
        }
    }

    [Fact]
    public void TheGridFollowsSettlementsThatMoveOrGo()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(6), Content, out WorldGenReport _);
        WorldState world = state.Worlds[0];
        SettlementState s = state.Settlements[0];
        Assert.Contains(0, state.Grid.Within(world, s.Cell, 0));

        HexCoord far = world.Geometry.Wrap(s.Cell.Col + (world.Geometry.Width / 2), s.Cell.Row);
        HexCoord old = s.Cell;
        s.Cell = far;
        Assert.DoesNotContain(0, state.Grid.Within(world, old, 0));
        Assert.Contains(0, state.Grid.Within(world, far, 0));

        long id = s.Id.Value;
        state.RemoveSettlements(x => x.Id.Value == id);
        Assert.Equal(SettlementGrid.WithinByScan(state, world, far, 5), state.Grid.Within(world, far, 5).ToList());
    }
}
