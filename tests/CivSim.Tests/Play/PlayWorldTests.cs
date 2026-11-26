using System;
using System.Collections.Generic;
using System.IO;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Play;

/// <summary>Advanced world settings of a new game (Patryk, 2026-11-25, PR W1).</summary>
public sealed class PlayWorldTests
{
    // The game's state hash leaves the generated map out, so the test hashes the map itself (and the game state).
    private static ulong HashOf(PlayWorld? world)
    {
        using PlayHost host = PlayHost.Create(new PlayOptions(7, 66, 40, 2) { StartPhase = false, World = world });
        host.AutosaveOn = false;
        CivSim.Core.Map.WorldMap map = host.Game.Map;
        ulong h = 14695981039346656037UL ^ host.StateHash;
        foreach (byte[] layer in new[] { map.Biome.Raw.ToArray(), map.Relief.Raw.ToArray(), map.Humidity.Raw.ToArray(), map.Elevation.Raw.ToArray() })
        {
            foreach (byte b in layer)
            {
                h = (h ^ b) * 1099511628211UL;
            }
        }

        foreach (sbyte t in map.Temperature.Raw)
        {
            h = (h ^ (byte)t) * 1099511628211UL;
        }

        return h;
    }

    [Fact]
    public void DefaultSettingsGiveTheSameWorldAsNone()
    {
        ulong none = HashOf(null);
        Assert.Equal(none, HashOf(new PlayWorld()));
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void ChangedSettingsGiveAnotherWorldTheSameEachTime()
    {
        var dry = new PlayWorld { Moisture = "dry", Relief = "mountainous", Resources = "rich", AxialTiltTenths = 300 };
        ulong a = HashOf(dry);
        Assert.Equal(a, HashOf(dry with { }));
        Assert.NotEqual(HashOf(null), a);
    }

    [Fact]
    public void BadSettingsAreExplained()
    {
        Assert.Contains("Unknown planet", PlayWorlds.Check(new PlayWorld { Planet = "pluto" }, null, out _), StringComparison.Ordinal);
        Assert.Contains("Relief", PlayWorlds.Check(new PlayWorld { Relief = "spiky" }, null, out _), StringComparison.Ordinal);
        Assert.Null(PlayWorlds.Check(new PlayWorld { Planet = "mars" }, null, out IReadOnlyList<string> _));
    }

    [Fact]
    public void EveryPresetIsListed()
    {
        IReadOnlyList<(string Key, string Name)> presets = PlayWorlds.Presets();
        Assert.Contains(presets, p => p.Key == "earth");
        Assert.True(presets.Count >= 3);
    }

    [Fact]
    public void ASaveKeepsTheWorldSettings()
    {
        var options = new PlayOptions(7) { World = new PlayWorld { Planet = "mars", Moisture = "wet", LightThousandths = 900 } };
        string path = Path.Combine(Path.GetTempPath(), $"civsim-world-{Environment.ProcessId}.civsave");
        try
        {
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new SavedGame { Options = options }));
            SavedGame back = PlaySaves.Read(path);
            Assert.Equal(options.World, back.Options.World);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
