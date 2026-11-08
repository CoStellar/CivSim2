using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Population;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Population;

public class MigrationTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static TurnRunner NewGame(ulong seed, GameSettings? settings = null)
    {
        GameState state = GameState.CreateNew(settings ?? GameSettings.Test(seed), Content, out WorldGenReport _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        return runner;
    }

    private static int CellOf(GameState state, SettlementState s) => state.Worlds[0].Geometry.Index(s.Cell);

    [Fact]
    public void EveryCampClaimsTheLandAroundIt()
    {
        GameState state = NewGame(1).State;
        WorldMap map = state.Worlds[0].Map;
        foreach (SettlementState s in state.Settlements)
        {
            int cell = CellOf(state, s);
            Assert.Equal(s.Id.Value + 1, map.Claim[cell]);
            Assert.Equal(s.Owner.Value, map.Owner[cell]);
            Assert.True(map.Influence[cell] > 0);
        }

        Assert.True(map.Claim.Values.ToArray().Count(c => c != 0) >= state.Settlements.Count * 7);
    }

    [Fact]
    public void InfluenceIsStrongestAtTheCampAndStopsAtWater()
    {
        GameState state = NewGame(2).State;
        WorldState world = state.Worlds[0];
        SettlementState camp = state.Settlements[0];
        int center = CellOf(state, camp);
        var ring = new HexCoord[6];
        world.Geometry.Ring(camp.Cell, 1, ring);
        foreach (HexCoord c in ring)
        {
            int i = world.Geometry.Index(c);
            Assert.True(world.Map.Influence[i] <= world.Map.Influence[center]);
        }

        for (int i = 0; i < world.Geometry.CellCount; i++)
        {
            if (Content.Biomes[world.Map.Biome[i]].IsWater)
            {
                Assert.Equal(0, world.Map.Claim[i]);
            }
        }
    }

    [Fact]
    public void ABandKeepsItsHomeGroundNextToAStrongerNeighbour()
    {
        GameState state = NewGame(2).State;
        WorldState world = state.Worlds[0];
        SettlementState band = state.Settlements[0];
        HexGlobe geo = world.Geometry;

        // A crowd of warriors of another people camps four cells away: its zone is far stronger everywhere around.
        HexCoord near = Enumerable.Range(0, geo.CellCount).Select(geo.FromIndex)
            .First(c => geo.Distance(c, band.Cell) == 4 && !Content.Biomes[world.Map.Biome[geo.Index(c)]].IsWater);
        CivId other = state.Civilizations.First(c => c.Id != band.Owner).Id;
        SettlementState crowd = state.FoundSettlement(other, world.Id, near, "Crowd", Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        crowd.Population[2] = 10_000;
        new Influence(Content).Recompute(state);

        // The band's own cell and its cheap neighbours stay its own (home ground); the rest goes to the crowd.
        Assert.Equal(band.Id.Value + 1, world.Map.Claim[geo.Index(band.Cell)]);
        var ring = new HexCoord[6];
        geo.Ring(band.Cell, 1, ring);
        var influence = new Influence(Content);
        foreach (HexCoord c in ring)
        {
            int i = geo.Index(c);
            int cost = influence.EnterCost(world.Map, i);
            if (cost >= 0 && cost <= Content.Population.InfluenceHomeCost)
            {
                Assert.Equal(band.Id.Value + 1, world.Map.Claim[i]);
            }
        }

        Assert.Contains(Enumerable.Range(0, geo.CellCount), i => world.Map.Claim[i] == crowd.Id.Value + 1);
    }

    [Fact]
    public void WarriorsWidenTheZone()
    {
        var influence = new Influence(Content);
        SettlementState camp = NewGame(3).State.Settlements[0];
        int before = influence.RangeOf(camp);
        camp.Population[2] += 100;
        Assert.True(influence.RangeOf(camp) > before);
        camp.Population[2] += 100_000;
        Assert.Equal(Content.Population.InfluenceMaxRange, influence.RangeOf(camp));
    }

    [Fact]
    public void GatheringEatsWildFoodAndUnusedLandGrowsBack()
    {
        TurnRunner runner = NewGame(4);
        GameState state = runner.State;
        WorldMap map = state.Worlds[0].Map;
        SettlementState camp = state.Settlements[0];
        camp.Population[0] = 2000; // far too many gatherers for the land
        runner.RunTurn();
        int cell = CellOf(state, camp);
        ushort eaten = map.WildFood[cell];
        Assert.True(eaten < WorldMap.FullWildFood, $"wild food {eaten}");

        // Everyone leaves: the land is no longer claimed and grows back.
        foreach (SettlementState s in state.Settlements)
        {
            s.Population.Clear();
        }

        new Influence(Content).Recompute(state);
        for (int t = 0; t < 8; t++)
        {
            runner.RunTurn();
        }

        Assert.True(map.WildFood[cell] > eaten, $"{map.WildFood[cell]} <= {eaten}");
    }

    [Fact]
    public void CampsMoveWithTheirPeopleAndWarehouse()
    {
        TurnRunner runner = NewGame(5);
        GameState state = runner.State;
        SettlementState camp = state.Settlements[0];
        WorldState world = state.Worlds[0];
        HexCoord target = FindLand(state, world, camp.Cell, 3, camp.Owner);
        int people = camp.TotalPopulation();

        var move = new MoveSettlementCommand(Content, camp.Id, target);
        Assert.True(move.Validate(state, camp.Owner).Success);
        move.Apply(state, camp.Owner);

        Assert.Equal(target, camp.Cell);
        Assert.Equal(people, camp.TotalPopulation());
    }

    [Fact]
    public void MovesAreCheckedOnTheServer()
    {
        GameState state = NewGame(6).State;
        SettlementState camp = state.Settlements[0];
        WorldState world = state.Worlds[0];
        CivId other = state.Settlements[1].Owner;

        Assert.False(new MoveSettlementCommand(Content, camp.Id, camp.Cell).Validate(state, other).Success);
        Assert.False(new MoveSettlementCommand(Content, camp.Id, FindWater(world)).Validate(state, camp.Owner).Success);
        HexCoord far = world.Geometry.Wrap(camp.Cell.Col + Content.Population.MaxMoveCells + 3, camp.Cell.Row);
        Assert.False(new MoveSettlementCommand(Content, camp.Id, far).Validate(state, camp.Owner).Success);
        Assert.False(new MoveSettlementCommand(Content, camp.Id, state.Settlements[1].Cell).Validate(state, camp.Owner).Success);
    }

    [Fact]
    public void SplitGroupsTakeTheirShareOfPeopleAndGoods()
    {
        GameState state = NewGame(7).State;
        SettlementState camp = state.Settlements[0];
        camp.Population.Clear();
        camp.Population[0] = 80;
        camp.Population[2] = 20;
        Core.Numerics.Amount meat = camp.StockView[Content.Good("meat").Id];
        HexCoord target = FindLand(state, state.Worlds[0], camp.Cell, Content.Population.MinDistanceBetweenOwnCamps + 1, camp.Owner);

        var split = new SplitSettlementCommand(Content, camp.Id, target, 40);
        Assert.True(split.Validate(state, camp.Owner).Success, split.Validate(state, camp.Owner).Reason);
        string? line = split.ApplyWithChronicle(state, camp.Owner);

        SettlementState child = state.Settlements[^1];
        Assert.Equal(camp.Owner, child.Owner);
        Assert.Equal(target, child.Cell);
        Assert.Equal(32, child.PopulationView[0]);
        Assert.Equal(8, child.PopulationView[2]);
        Assert.Equal(60, camp.TotalPopulation());
        Assert.Equal(meat, camp.StockView[Content.Good("meat").Id] + child.StockView[Content.Good("meat").Id]);
        Assert.Contains("founded", line, System.StringComparison.Ordinal);
    }

    [Fact]
    public void TribesSpreadOverTheLandOnTheirOwn()
    {
        TurnRunner runner = NewGame(8);
        for (int t = 0; t < 60; t++)
        {
            runner.RunTurn();
        }

        Assert.True(runner.State.Settlements.Count > runner.State.Civilizations.Count, "nobody split");
        Assert.Contains(runner.State.Civilizations, c => c.IsAlive);
    }

    [Fact]
    public void GamesWithTheTribeAiAreDeterministic()
    {
        static ulong Run()
        {
            TurnRunner runner = NewGame(2026);
            for (int t = 0; t < 80; t++)
            {
                runner.RunTurn();
            }

            return GameStateHash.Compute(runner.State);
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void TheAiLeavesPlayerCivilizationsAlone()
    {
        TurnRunner runner = NewGame(9);
        GameState state = runner.State;
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Controller = new PlayerId(5);
        }

        var queue = new CommandQueue();
        new GovernorAdvisor(Content).IssueCommands(state, queue);
        Assert.Equal(0, queue.Count);
    }

    private static HexCoord FindLand(GameState state, WorldState world, HexCoord from, int distance, CivId owner)
    {
        var ring = new HexCoord[HexGlobe.RingSize(distance)];
        world.Geometry.Ring(from, distance, ring);
        foreach (HexCoord c in ring)
        {
            int i = world.Geometry.Index(c);
            short cellOwner = world.Map.Owner[i];
            if (!Content.Biomes[world.Map.Biome[i]].IsWater && (cellOwner == WorldMap.NoOwner || cellOwner == owner.Value)
                && !state.Settlements.Any(s => s.Cell == c))
            {
                return c;
            }
        }

        throw new Xunit.Sdk.XunitException("No free land found.");
    }

    private static HexCoord FindWater(WorldState world)
    {
        for (int i = 0; i < world.Geometry.CellCount; i++)
        {
            if (Content.Biomes[world.Map.Biome[i]].IsWater)
            {
                return world.Geometry.FromIndex(i);
            }
        }

        throw new Xunit.Sdk.XunitException("No water found.");
    }
}
