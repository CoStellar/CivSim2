using System;
using System.IO;
using CivSim.Core.Content;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Data;

public class ContentLoaderTests
{
    [Fact]
    public void DefaultDataLoadsWithoutErrors()
    {
        GameContent content = TestContent.Load();
        Assert.Equal(14, content.Biomes.Count);
        Assert.Equal(22, content.Deposits.Count);
        for (int i = 0; i < content.Biomes.Count; i++)
        {
            Assert.Equal(i, content.Biomes[i].Id);
        }

        Assert.Equal(BiomeKind.Lake, content.WaterBiome(BiomeKind.Lake).Kind);
        Assert.Equal("game", content.Deposit(1).Key);
    }

    [Fact]
    public void BrokenDataReportsEveryError()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-content-" + Environment.ProcessId + "-" + Environment.CurrentManagedThreadId);
        Directory.CreateDirectory(dir);
        try
        {
            string biomes = File.ReadAllText(Path.Combine(ContentLoader.DefaultDirectory, "biomes.json"))
                .Replace("\"key\": \"lake\"", "\"key\": \"coast\"", StringComparison.Ordinal)
                .Replace("\"#7ba154\"", "\"green\"", StringComparison.Ordinal);
            string deposits = File.ReadAllText(Path.Combine(ContentLoader.DefaultDirectory, "resources.json"))
                .Replace("\"biomes\": [\"coast\", \"lake\", \"shallow_sea\"]", "\"biomes\": [\"moon\"]", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(dir, "biomes.json"), biomes);
            File.WriteAllText(Path.Combine(dir, "resources.json"), deposits);

            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("coast", StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("green", StringComparison.Ordinal) || e.Contains("color", StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("moon", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
