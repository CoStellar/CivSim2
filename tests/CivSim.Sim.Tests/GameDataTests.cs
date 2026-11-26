using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using CivSim.Sim.Content;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>The check of data/sim/game.json the start menu runs (2026-11-22).</summary>
public sealed class GameDataTests
{
    [Fact]
    public void TheGameDataHasNoProblems()
    {
        Assert.Empty(SimContent.Check());
    }

    [Fact]
    public void AnUnknownKeyIsReportedWithItsPlace()
    {
        IReadOnlyList<string> errors = WithGameJson(root =>
        {
            root["ai"]!["homesFoodMinutse"] = 3;
            root["seasons"]!["cycleMinutez"] = 8;
        });

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("'ai.homesFoodMinutse'", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'seasons.cycleMinutez'", StringComparison.Ordinal));
    }

    [Fact]
    public void CommentsAreNotProblems()
    {
        Assert.Empty(WithGameJson(root => root["ai"]!["comment"] = "a note for people"));
    }

    [Fact]
    public void AnUnknownBuildingInTheArmyLimitIsReported()
    {
        IReadOnlyList<string> errors = WithGameJson(root => root["armyLimit"]!["perBuilding"]!["barracsk"] = 1);

        Assert.Contains(errors, e => e.Contains("'barracsk'", StringComparison.Ordinal));
    }

    [Fact]
    public void AFileThatDoesNotLoadIsAProblemNotACrash()
    {
        IReadOnlyList<string> errors = WithGameJson(root => root["goods"]!.AsArray().Add(new JsonObject { ["name"] = "No key" }));

        Assert.NotEmpty(errors);
    }

    // Copies the data folder, changes game.json and checks the copy.
    private static IReadOnlyList<string> WithGameJson(Action<JsonNode> change)
    {
        string source = CivSim.Data.ContentLoader.DefaultDirectory;
        string copy = Path.Combine(Path.GetTempPath(), $"civsim-data-{Environment.ProcessId}-{Interlocked.Increment(ref _copies)}");
        try
        {
            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string to = Path.Combine(copy, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }

            string game = Path.Combine(copy, "sim", "game.json");
            JsonNode root = JsonNode.Parse(File.ReadAllText(game))!;
            change(root);
            File.WriteAllText(game, root.ToJsonString());
            return SimContent.Check(copy);
        }
        finally
        {
            Directory.Delete(copy, recursive: true);
        }
    }

    private static int _copies;
}
