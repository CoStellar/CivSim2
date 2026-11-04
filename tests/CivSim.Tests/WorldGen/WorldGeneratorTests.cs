using System.Collections.Generic;
using System.IO;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.WorldGen;

public class WorldGeneratorTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static (GameState State, WorldGenReport Report) Generate(GameSettings settings)
    {
        GameState state = GameState.CreateNew(settings, Content, out WorldGenReport report);
        return (state, report);
    }

    private static List<string> Validate(GameState state, WorldGenReport report)
    {
        GameSettings s = state.Settings;
        return WorldValidator.Validate(Content, state.Worlds[0].Map, report, new WorldGenOptions(s.WorldType, s.WaterPercent, s.CivilizationCount));
    }

    [Fact]
    public void SameSeedGivesTheSameWorld()
    {
        Assert.Equal(GameStateHash.Compute(Generate(GameSettings.Test(5)).State), GameStateHash.Compute(Generate(GameSettings.Test(5)).State));
    }

    [Fact]
    public void DifferentSeedsGiveDifferentWorlds()
    {
        Assert.NotEqual(GameStateHash.Compute(Generate(GameSettings.Test(5)).State), GameStateHash.Compute(Generate(GameSettings.Test(6)).State));
    }

    [Fact]
    public void GoldenWorldHashIsStable()
    {
        // If this changes on purpose (generator or data changed), update it and say why in the PR: old seeds give new maps.
        Assert.Equal(GoldenWorldHash, GameStateHash.Compute(Generate(GameSettings.Test(2026)).State));
    }

    private const ulong GoldenWorldHash = 18191642694635137060UL;

    [Theory]
    [InlineData(WorldType.Continents, 60)]
    [InlineData(WorldType.Pangea, 50)]
    [InlineData(WorldType.Archipelago, 70)]
    [InlineData(WorldType.Continents, 30)]
    public void EveryWorldTypeIsValid(WorldType type, int water)
    {
        var settings = new GameSettings(11, 128, 80, 8) { WorldType = type, WaterPercent = water };
        (GameState state, WorldGenReport report) = Generate(settings);
        Assert.Empty(Validate(state, report));
        Assert.InRange(report.WaterPercent, water - WorldValidator.WaterTolerance, water + WorldValidator.WaterTolerance);
    }

    [Fact]
    public void HundredSeedsOnTheTestMapAreValid()
    {
        var failures = new List<string>();
        for (ulong seed = 1; seed <= 100; seed++)
        {
            (GameState state, WorldGenReport report) = Generate(GameSettings.Test(seed));
            foreach (string p in Validate(state, report))
            {
                failures.Add($"seed {seed}: {p}");
            }

            if (report.MinStartDistance < 3)
            {
                failures.Add($"seed {seed}: starts only {report.MinStartDistance} apart");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void StandardMapHasRiversLakesDepositsAndFairStarts()
    {
        (GameState state, WorldGenReport report) = Generate(new GameSettings(1, 256, 160, 12));
        Assert.Empty(Validate(state, report));
        Assert.True(report.Rivers >= 20, $"only {report.Rivers} rivers");
        Assert.True(report.Lakes >= 1, "no lakes");
        Assert.True(report.MinStartDistance >= 10, $"starts {report.MinStartDistance} apart");
        Assert.True(report.StartFairnessPercent >= 50, $"fairness {report.StartFairnessPercent}%");

        int kinds = 0;
        for (int d = 1; d < report.DepositCounts.Length; d++)
        {
            kinds += report.DepositCounts[d] > 0 ? 1 : 0;
        }

        Assert.True(kinds >= 18, $"only {kinds} deposit kinds placed");

        int landBiomes = 0;
        foreach (BiomeDefinition b in Content.Biomes)
        {
            landBiomes += b.Kind == BiomeKind.Land && report.BiomeCounts[b.Id] > 0 ? 1 : 0;
        }

        Assert.True(landBiomes >= 9, $"only {landBiomes} land biomes");
    }

    [Fact]
    public void CivilizationsGetTheirStartCells()
    {
        (GameState state, WorldGenReport report) = Generate(GameSettings.Test(3));
        for (int i = 0; i < state.Civilizations.Count; i++)
        {
            Assert.Equal(report.Starts[i], state.Civilizations[i].StartCell);
        }
    }

    [Fact]
    public void GeneratedWorldSurvivesSaveAndLoad()
    {
        var settings = GameSettings.Test(9) with { WorldType = WorldType.Pangea, WaterPercent = 45 };
        GameState state = Generate(settings).State;
        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
        Assert.Equal(WorldType.Pangea, loaded.Settings.WorldType);
        Assert.Equal(45, loaded.Settings.WaterPercent);
        Assert.Equal(state.Civilizations[2].StartCell, loaded.Civilizations[2].StartCell);
    }

    [Fact]
    public void RiversOnlyRunBetweenLandCells()
    {
        (GameState state, _) = Generate(GameSettings.Test(4));
        WorldMap map = state.Worlds[0].Map;
        HexTorus geo = map.Geometry;
        int rivers = 0;
        for (int e = 0; e < geo.EdgeCount; e++)
        {
            if (map.River[e] == 0)
            {
                continue;
            }

            rivers++;
            (HexCoord a, HexCoord b) = geo.EdgeCells(new HexEdge(geo.FromIndex(e / 3), (HexDirection)(e % 3)));
            Assert.False(Content.Biomes[map.Biome[geo.Index(a)]].IsWater);
            Assert.False(Content.Biomes[map.Biome[geo.Index(b)]].IsWater);
        }

        Assert.True(rivers > 0);
    }
}
