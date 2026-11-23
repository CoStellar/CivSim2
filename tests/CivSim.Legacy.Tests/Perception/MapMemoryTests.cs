using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Perception;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Perception;

/// <summary>Sight, scouts, map memory and journeys (stage 3g, GDD 8.2).</summary>
public class MapMemoryTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static GameState NewGame(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out _);

    private static int Known(GameState state, CivilizationState civ)
    {
        int n = 0;
        foreach (bool k in MapMemory.KnownCells(Content, state, civ))
        {
            n += k ? 1 : 0;
        }

        return n;
    }

    [Fact]
    public void CivilizationsStartKnowingTheirSurroundings()
    {
        GameState state = NewGame(61);
        CivilizationState civ = state.Civilizations[0];
        Assert.True(MapMemory.Knows(Content, state, civ, state.Worlds[0].Geometry.Index(civ.StartCell)));
        Assert.Equal(HexGlobe.RangeSize(Content.Perception.SightCells), Known(state, civ));
    }

    [Fact]
    public void OralMemoryFadesUnlessTheTribeComesBack()
    {
        GameState state = NewGame(62);
        CivilizationState civ = state.Civilizations[0];
        int start = state.Worlds[0].Geometry.Index(civ.StartCell);
        state.SetCountersForLoad((Content.Perception.OralMemoryYears + 1) * 12L, state.NextSettlementId);
        Assert.False(MapMemory.Knows(Content, state, civ, start));

        MapMemory.See(Content, state); // the camp is still there: seen again
        Assert.True(MapMemory.Knows(Content, state, civ, start));
    }

    [Fact]
    public void ScoutsWidenWhatACivilizationKnows()
    {
        GameState state = NewGame(63);
        CivilizationState civ = state.Civilizations[0];
        int before = Known(state, civ);

        // Stage 6 PR 3a: scouts are visible units that walk out and see the land next to their way.
        var runner = new CivSim.Core.Simulation.TurnRunner(state);
        runner.AddSystem(new CivSim.Core.Units.UnitSystem(Content));
        runner.RunTurn();
        Assert.Contains(state.Units.All, u => u.Role == CivSim.Core.Units.UnitRole.Scouts && u.Owner == civ.Id);
        for (int t = 0; t < 100; t++)
        {
            runner.RunTurn();
        }

        Assert.True(Known(state, civ) > before);
    }

    [Fact]
    public void JourneysGoOnlyToKnownLandAndWalkThereOverTurns()
    {
        GameState state = NewGame(64);
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        SettlementState camp = state.Settlements[0];
        CivilizationState civ = state.FindCivilization(camp.Owner)!;
        HexCoord from = camp.Cell;

        // A long strip of land to the south-east.
        HexCoord target = camp.Cell;
        var strip = new List<int>();
        for (int k = 0; k < 25; k++)
        {
            target = geo.Neighbor(target, HexDirection.SE);
            strip.Add(geo.Index(target));
        }

        int grass = Content.Biome("grassland").Id;
        foreach (int i in strip)
        {
            world.Map.Biome.Set(i, (byte)grass);
        }

        Assert.False(new JourneyCommand(Content, camp.Id, target).Validate(state, civ.Id).Success); // not known yet
        foreach (int i in strip)
        {
            civ.SeenYear[i] = 1;
        }

        var journey = new JourneyCommand(Content, camp.Id, target);
        Assert.True(journey.Validate(state, civ.Id).Success);
        journey.Apply(state, civ.Id);

        // Stage 6 PR 3a: the camp walks cell by cell in ticks, a flat cell in journeyTicksPerCell ticks or more.
        var runner = new TurnRunner(state);
        runner.AddSystem(new JourneySystem(Content));
        int ticks = 0;
        while (camp.Cell == from)
        {
            runner.RunTurn();
            ticks++;
        }

        Assert.True(ticks >= Content.Units.JourneyTicksPerCell, $"the first cell took {ticks} ticks");
        Assert.Equal(1, geo.Distance(from, camp.Cell));
        for (int t = 0; t < 25 * 10 * Content.Units.JourneyTicksPerCell && camp.JourneyTarget != SettlementState.NoJourney; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(target, camp.Cell);
        Assert.Equal(SettlementState.NoJourney, camp.JourneyTarget);
    }

    [Fact]
    public void AJourneyFindsItsWayOnceAndSkipsCampsInTheWay()
    {
        // Stage 6 PR 3b (known issue 10): the way is found once for the goal, remembered (also in a save) and walked camp
        // after camp; a cell someone camped on meanwhile is passed, not a reason to search again.
        GameState state = NewGame(66);
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        SettlementState camp = state.Settlements[0];
        CivilizationState civ = state.FindCivilization(camp.Owner)!;
        HexCoord target = camp.Cell;
        int grass = Content.Biome("grassland").Id;
        for (int k = 0; k < 8; k++)
        {
            target = geo.Neighbor(target, HexDirection.SE);
            world.Map.Biome.Set(geo.Index(target), (byte)grass);
            civ.SeenYear[geo.Index(target)] = 1;
        }

        new JourneyCommand(Content, camp.Id, target).Apply(state, civ.Id);
        var runner = new TurnRunner(state);
        runner.AddSystem(new JourneySystem(Content));
        runner.RunTurn();

        Assert.Equal(geo.Index(target), camp.JourneyPathGoal);
        int[] way = [.. camp.JourneyPath];
        Assert.Equal(8, way.Length);
        Assert.Equal(way[0], camp.JourneyNext);

        // Someone pitches on the third cell of the way: the camp walks past it to the fourth.
        SettlementState other = state.Settlements[1];
        other.Cell = geo.FromIndex(way[2]);
        HexCoord before = camp.Cell;
        for (int t = 0; t < 40 * Content.Units.JourneyTicksPerCell && geo.Distance(before, camp.Cell) < 4; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(way, camp.JourneyPath); // never searched again
        Assert.NotEqual(other.Cell, camp.Cell);
        Assert.Equal(way[3], geo.Index(camp.Cell));
        Assert.Equal(4, camp.JourneyPathAt);

        using var stream = new System.IO.MemoryStream();
        CivSim.Server.Saves.SaveGameSerializer.Save(state, stream);
        stream.Position = 0;
        GameState loaded = CivSim.Server.Saves.SaveGameSerializer.Load(stream);
        SettlementState again = loaded.FindSettlement(camp.Id)!;
        Assert.Equal(way, again.JourneyPath);
        Assert.Equal((4, camp.JourneyPathGoal), (again.JourneyPathAt, again.JourneyPathGoal));
    }

    [Fact]
    public void SeaIceCanBeWalked()
    {
        GameState state = NewGame(65);
        WorldMap map = state.Worlds[0].Map;
        int water = -1;
        for (int i = 0; i < map.Biome.Length && water < 0; i++)
        {
            water = map.Biome[i] == Content.WaterBiome(BiomeKind.DeepOcean).Id && map.Feature[i] == 0 ? i : -1;
        }

        Assert.False(MapMemory.Walkable(Content, map, water));
        map.Feature.Set(water, (byte)TerrainFeature.IceField);
        Assert.True(MapMemory.Walkable(Content, map, water));
    }
}
