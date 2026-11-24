using System.IO;
using System.Linq;
using System.Text.Json;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Content;

/// <summary>
/// The game's sound (<c>audio/sounds.json</c>, read by the client's AudioManager): every file it names is there, every era
/// has music and every biome an ambient layer.
/// </summary>
public sealed class AudioFilesTests
{
    // audio/ is not copied next to the tests: look for it above the test's folder (the repository root).
    private static readonly string AudioDirectory = FindAudio();

    private static string FindAudio()
    {
        for (DirectoryInfo? dir = new(System.AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "audio");
            if (File.Exists(Path.Combine(candidate, "sounds.json")))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("audio/sounds.json not found above " + System.AppContext.BaseDirectory);
    }

    private static JsonElement Manifest()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AudioDirectory, "sounds.json")));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void EveryNamedFileExists()
    {
        JsonElement m = Manifest();
        Assert.True(File.Exists(Path.Combine(AudioDirectory, "instruments.sf2")), "the instruments of the music");
        foreach (JsonProperty list in m.GetProperty("music").EnumerateObject())
        {
            Assert.NotEmpty(list.Value.EnumerateArray());
            foreach (JsonElement file in list.Value.EnumerateArray())
            {
                Assert.True(File.Exists(Path.Combine(AudioDirectory, "music", file.GetString() + ".mid")), $"music {file}");
            }
        }

        foreach (JsonProperty layer in m.GetProperty("ambient").EnumerateObject())
        {
            Assert.True(File.Exists(Path.Combine(AudioDirectory, "ambient", layer.Value.GetString() + ".ogg")), $"ambient {layer.Name}");
        }

        foreach (JsonProperty sound in m.GetProperty("sounds").EnumerateObject())
        {
            string file = sound.Value.GetProperty("file").GetString()!;
            Assert.True(File.Exists(Path.Combine(AudioDirectory, "sfx", file + ".ogg")), $"sound {sound.Name}");
        }
    }

    // The terrains of the music (Patryk, 2026-11-24); the coast is chosen when water fills the screen.
    private static readonly string[] Regions = ["mountains", "forest", "steppe", "desert", "wetland", "coast"];

    [Fact]
    public void EveryEraHasMusicAndEveryBiomeAnAmbientLayer()
    {
        JsonElement m = Manifest();
        using JsonDocument game = JsonDocument.Parse(File.ReadAllText(Path.Combine(ContentLoader.DefaultDirectory, "sim", "game.json")));

        // The client names an era's playlist after the era's name in snake case ("Stone Age" -> stone_age).
        foreach (JsonElement era in game.RootElement.GetProperty("eras").EnumerateArray())
        {
            string key = era.GetProperty("name").GetString()!.Trim().ToLowerInvariant().Replace(' ', '_');
            foreach (string region in Regions)
            {
                // Three pieces of about five minutes for each terrain in each era.
                for (int k = 1; k <= 3; k++)
                {
                    Assert.True(File.Exists(Path.Combine(AudioDirectory, "music", $"{key}_{region}_{k}.mid")), $"no music {key}_{region}_{k}");
                }
            }
        }

        using JsonDocument biomes = JsonDocument.Parse(File.ReadAllText(Path.Combine(ContentLoader.DefaultDirectory, "biomes.json")));
        string[] layers = [.. m.GetProperty("ambient").EnumerateObject().Select(p => p.Name)];
        foreach (JsonElement biome in biomes.RootElement.GetProperty("biomes").EnumerateArray())
        {
            string key = biome.GetProperty("key").GetString()!;
            Assert.True(m.GetProperty("biomeAmbient").TryGetProperty(key, out JsonElement layer), $"no ambient for biome {key}");
            Assert.Contains(layer.GetString(), layers);
            if (biome.GetProperty("kind").GetString() == "land")
            {
                Assert.True(m.GetProperty("biomeRegion").TryGetProperty(key, out JsonElement region), $"no music terrain for biome {key}");
                Assert.Contains(region.GetString(), Regions);
            }
        }
    }
}
