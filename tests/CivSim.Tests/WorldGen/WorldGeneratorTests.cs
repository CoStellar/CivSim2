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
        return WorldValidator.Validate(Content, state.Worlds[0].Map, report, WorldGenOptions.From(s));
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

    private const ulong GoldenWorldHash = 2205344670942499559UL;

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

    private static GameSettings Medium => new(21, 128, 80, 8);

    private static int CountBiomes(WorldGenReport r, params string[] keys)
    {
        int n = 0;
        foreach (string k in keys)
        {
            n += r.BiomeCounts[Content.Biome(k).Id];
        }

        return n;
    }

    private static int CountRelief(GameState state, Relief relief)
    {
        int n = 0;
        foreach (byte v in state.Worlds[0].Map.Relief.Values)
        {
            n += v == (byte)relief ? 1 : 0;
        }

        return n;
    }

    private static int Sum(int[] counts)
    {
        int n = 0;
        foreach (int c in counts)
        {
            n += c;
        }

        return n;
    }

    [Theory]
    [InlineData(WorldClimate.Cold, WorldMoisture.Dry, WorldRelief.Flat, WorldResources.Scarce, WorldInland.Dry)]
    [InlineData(WorldClimate.Hot, WorldMoisture.Wet, WorldRelief.Mountainous, WorldResources.Rich, WorldInland.Humid)]
    [InlineData(WorldClimate.Cold, WorldMoisture.Wet, WorldRelief.Normal, WorldResources.Rich, WorldInland.Normal)]
    [InlineData(WorldClimate.Hot, WorldMoisture.Dry, WorldRelief.Mountainous, WorldResources.Normal, WorldInland.Dry)]
    [InlineData(WorldClimate.Temperate, WorldMoisture.Dry, WorldRelief.Flat, WorldResources.Normal, WorldInland.Humid)]
    public void EveryWorldSettingIsValid(WorldClimate climate, WorldMoisture moisture, WorldRelief relief, WorldResources resources, WorldInland inland)
    {
        GameSettings settings = Medium with { Climate = climate, Moisture = moisture, Relief = relief, Resources = resources, Inland = inland };
        (GameState state, WorldGenReport report) = Generate(settings);
        Assert.Empty(Validate(state, report));
    }

    [Fact]
    public void ColdWorldsHaveMoreIceAndTundraThanHotOnes()
    {
        WorldGenReport cold = Generate(Medium with { Climate = WorldClimate.Cold }).Report;
        WorldGenReport hot = Generate(Medium with { Climate = WorldClimate.Hot }).Report;
        Assert.True(CountBiomes(cold, "ice", "tundra") > CountBiomes(hot, "ice", "tundra") * 3 / 2);
        Assert.True(CountBiomes(hot, "jungle", "savanna", "desert") > CountBiomes(cold, "jungle", "savanna", "desert") * 3 / 2);
    }

    [Fact]
    public void WetWorldsHaveMoreRiversAndForestThanDryOnes()
    {
        WorldGenReport dry = Generate(Medium with { Moisture = WorldMoisture.Dry }).Report;
        WorldGenReport wet = Generate(Medium with { Moisture = WorldMoisture.Wet }).Report;
        Assert.True(wet.RiverEdges > dry.RiverEdges * 3 / 2, $"wet {wet.RiverEdges}, dry {dry.RiverEdges}");
        Assert.True(CountBiomes(dry, "desert", "steppe") > CountBiomes(wet, "desert", "steppe"));
    }

    [Fact]
    public void DryInlandWorldsHaveLessHumidityThanHumidOnes()
    {
        long dry = TotalHumidity(Generate(Medium with { Inland = WorldInland.Dry }).State);
        long normal = TotalHumidity(Generate(Medium).State);
        long humid = TotalHumidity(Generate(Medium with { Inland = WorldInland.Humid }).State);
        Assert.True(dry < normal && normal < humid, $"{dry} / {normal} / {humid}");
    }

    private static long TotalHumidity(GameState state)
    {
        long sum = 0;
        foreach (byte v in state.Worlds[0].Map.Humidity.Values)
        {
            sum += v;
        }

        return sum;
    }

    [Fact]
    public void MountainousWorldsHaveMoreMountains()
    {
        int flat = CountRelief(Generate(Medium with { Relief = WorldRelief.Flat }).State, Relief.Mountains);
        int normal = CountRelief(Generate(Medium).State, Relief.Mountains);
        int high = CountRelief(Generate(Medium with { Relief = WorldRelief.Mountainous }).State, Relief.Mountains);
        Assert.True(flat < normal && normal < high, $"{flat} / {normal} / {high}");
    }

    [Fact]
    public void RichWorldsHaveMoreDeposits()
    {
        int scarce = Sum(Generate(Medium with { Resources = WorldResources.Scarce }).Report.DepositCounts);
        int rich = Sum(Generate(Medium with { Resources = WorldResources.Rich }).Report.DepositCounts);
        Assert.True(rich > scarce * 2, $"scarce {scarce}, rich {rich}");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(20)]
    public void CivilizationCountIsIndependentOfMapSize(int civs)
    {
        (GameState state, WorldGenReport report) = Generate(Medium with { CivilizationCount = civs });
        Assert.Equal(civs, state.Civilizations.Count);
        Assert.Empty(Validate(state, report));
    }

    [Fact]
    public void WorldSettingsSurviveSaveAndLoad()
    {
        GameSettings settings = GameSettings.Test(8) with
        {
            Climate = WorldClimate.Hot,
            Moisture = WorldMoisture.Wet,
            Relief = WorldRelief.Mountainous,
            Resources = WorldResources.Scarce,
            Inland = WorldInland.Humid,
        };
        GameState state = Generate(settings).State;
        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(state.Settings, loaded.Settings);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
    }
}
