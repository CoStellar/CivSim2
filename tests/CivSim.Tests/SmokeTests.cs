using System.IO;
using CivSim.Headless;
using CivSim.Protocol;
using Xunit;

namespace CivSim.Tests;

public class SmokeTests
{
    [Fact]
    public void ProtocolVersionIsPositive()
    {
        Assert.True(ProtocolInfo.Version >= 1);
    }

    [Fact]
    public void HeadlessRunnerExitsCleanly()
    {
        Assert.Equal(0, Program.Main(["--turns", "5", "--map-size", "test", "--quiet"]));
    }

    [Fact]
    public void HeadlessRejectsBadArguments()
    {
        Assert.Equal(2, Program.Main(["--turns", "zero"]));
        Assert.Equal(2, Program.Main(["--map-size", "galactic"]));
        Assert.Equal(2, Program.Main(["--what"]));
    }

    [Fact]
    public void HeadlessIsDeterministicAndWritesTheReport()
    {
        string path = Path.Combine(Path.GetTempPath(), $"civsim2-report-{System.Environment.ProcessId}.csv");
        try
        {
            var options = new HeadlessOptions(42, 50, MapSize.Test, path, Quiet: true);
            HeadlessResult a = HeadlessRun.Execute(options, TextWriter.Null);
            HeadlessResult b = HeadlessRun.Execute(options with { ReportPath = null }, TextWriter.Null);

            Assert.Equal(a.StateHash, b.StateHash);
            Assert.Equal(50, a.Turns);
            string[] lines = File.ReadAllLines(path);
            Assert.Equal(51, lines.Length);
            Assert.Equal(HeadlessRun.CsvHeader(), lines[0]);
            Assert.StartsWith("1,", lines[1]);
            Assert.Equal(14, lines[1].Split(',').Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
