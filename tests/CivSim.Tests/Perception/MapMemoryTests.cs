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
    private static readonly GameContent Content = ContentLoader.Load();

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
        for (int t = 0; t < 5; t++)
        {
            MapMemory.Scout(Content, state);
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

        var runner = new TurnRunner(state);
        runner.AddSystem(new JourneySystem(Content));
        runner.RunTurn();
        Assert.Equal(Content.Population.MaxMoveCells, geo.Distance(from, camp.Cell));
        runner.RunTurn();
        runner.RunTurn();
        Assert.Equal(target, camp.Cell);
        Assert.Equal(SettlementState.NoJourney, camp.JourneyTarget);
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
