using System;
using System.IO;
using System.Linq;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Content;

/// <summary>
/// The game manual (<c>data/sim/manual.md</c>, shown in the start menu and with F1): it is there, its chapters are the ones
/// the client opens by name, and its key tables keep the format the client draws.
/// </summary>
public sealed class ManualTests
{
    private static readonly string ManualPath = Path.Combine(ContentLoader.DefaultDirectory, "sim", "manual.md");

    [Fact]
    public void TheManualHasItsChapters()
    {
        string[] lines = File.ReadAllLines(ManualPath);
        string[] chapters = [.. lines.Where(l => l.StartsWith("# ", StringComparison.Ordinal)).Select(l => l[2..].Trim())];

        // "Controls" is opened by the help panel (H); the rest are what Patryk asked for: controls and how to progress.
        foreach (string needed in new[] { "The goal", "Controls", "First minutes", "Research and eras", "War and peace" })
        {
            Assert.Contains(needed, chapters);
        }

        Assert.Equal(chapters.Length, chapters.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryKeyRowHasKeysAndAMeaning()
    {
        foreach (string line in File.ReadAllLines(ManualPath).Where(l => l.StartsWith('|')))
        {
            Assert.StartsWith("| ", line);
            string[] cells = line[2..].Split(" | ", 2);
            Assert.True(cells.Length == 2 && cells[0].Trim().Length > 0 && cells[1].Trim().Length > 0, $"A key row needs '| keys | meaning': {line}");
        }
    }
}
