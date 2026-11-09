using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CivSim.Protocol;
using CivSim.Protocol.Hosting;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Server;

public class LongRunTests
{
    [Theory]
    [InlineData("war", "The Arvi declared war on the Belo.")]
    [InlineData("peace", "The Arvi and the Belo made peace after 3 years of war.")]
    [InlineData("treaty", "The Arvi and the Belo agreed to trade.")]
    [InlineData("secession", "Kora broke away from the Arvi.")]
    [InlineData("coup", "A coup in the Arvi: Ana seized power.")]
    [InlineData("died_out", "The Arvi has died out.")]
    [InlineData("era", "The Arvi entered the Neolithic.")]
    [InlineData("settlement", "13 people left Kora and founded Raesh.")]
    [InlineData("unrest", "Unrest spread in the Chiefdom of Arvi.")]
    [InlineData("epidemic", "An epidemic broke out in Kora.")]
    [InlineData("nature", "A drought dried the land around Kora.")]
    [InlineData("nature", "Drought in 3 settlements: Arvi (2), Belo.")]
    [InlineData("epidemic", "Epidemic in 4 settlements: Arvi.")]
    [InlineData("settlement", "2 new settlements were founded: Arvi, Belo.")]
    [InlineData("nature", "The river flooded Kora (Arvi); the fields are lost.")]
    [InlineData("other", "World generated.")]
    public void EventsGetAKind(string kind, string line) => Assert.Equal(kind, LongRunRecorder.KindOf(line));

    [Fact]
    public void LongRunWritesItsFilesAndEndsAtTheTimeLimit()
    {
        string folder = Path.Combine(Path.GetTempPath(), "civsim_longrun_" + Environment.ProcessId + "_" + Stopwatch.GetTimestamp());
        try
        {
            LocalGameOptions options = LocalGameOptions.ForSize("test", 5) with
            {
                LongRunFolder = folder,
                LongRunSeconds = 3,
                LongRunSampleSeconds = 1,

                // Only the time limit ends this run: on a fast machine a small world can pass 10 000 quiet years in 3 s.
                LongRunStillYears = 0,
            };
            using (LocalServerHost host = LocalServerHost.Start(options))
            {
                IConnection viewer = host.Connect();
                viewer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
                viewer.Send(new SetSpeedMessage(SimulationSpeed.Max));
                var clock = Stopwatch.StartNew();
                while (!host.LongRunFinished)
                {
                    Assert.True(clock.Elapsed < TimeSpan.FromSeconds(60), "the long run did not end");
                    while (viewer.TryReceive(out _))
                    {
                    }

                    host.ReportFrame(2, 16);
                    host.ReportEngine(50, 20, 1000, 100);
                    Thread.Sleep(10);
                }

                Assert.Null(host.Error);
                Assert.Equal(Path.GetFullPath(folder), host.LongRunFolder);
            }

            string[] stats = File.ReadAllLines(Path.Combine(folder, "stats.csv"));
            Assert.Equal(LongRunRecorder.StatsHeader, stats[0]);
            Assert.True(stats.Length >= 3, $"only {stats.Length} lines in stats.csv");
            int columns = stats[0].Split(',').Length;
            Assert.All(stats, l => Assert.Equal(columns, l.Split(',').Length));
            string[] head = stats[0].Split(',');
            string[] last = stats[^1].Split(',');
            Assert.True(long.Parse(last[Array.IndexOf(head, "turn")], System.Globalization.CultureInfo.InvariantCulture) > 0);
            Assert.True(long.Parse(last[Array.IndexOf(head, "working_set_mb")], System.Globalization.CultureInfo.InvariantCulture) > 0);
            Assert.Equal("50", last[Array.IndexOf(head, "godot_static_mb")]);

            string[] turns = File.ReadAllLines(Path.Combine(folder, "turns.csv"));
            Assert.StartsWith("turn,year,elapsed_s,turn_ms,world_ms", turns[0], StringComparison.Ordinal);
            Assert.True(turns.Length > 2);
            Assert.True(File.ReadAllLines(Path.Combine(folder, "events.csv")).Length >= 1);
            string log = File.ReadAllText(Path.Combine(folder, "log.txt"));
            Assert.Contains("start: seed 5", log, StringComparison.Ordinal);
            Assert.Contains("end: time limit", log, StringComparison.Ordinal);
            string summary = File.ReadAllText(Path.Combine(folder, "summary.txt"));
            Assert.Contains("ended:            time limit", summary, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void ALongRunEndsWhenTheWorldStandsStill()
    {
        string folder = Path.Combine(Path.GetTempPath(), "civsim_longrun_still_" + Environment.ProcessId + "_" + Stopwatch.GetTimestamp());
        try
        {
            LocalGameOptions options = LocalGameOptions.ForSize("test", 5) with
            {
                LongRunFolder = folder,
                LongRunSeconds = 3600,
                LongRunSampleSeconds = 1,
                LongRunStillYears = 1, // the first quiet step ends it
            };
            using (LocalServerHost host = LocalServerHost.Start(options))
            {
                IConnection viewer = host.Connect();
                viewer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
                viewer.Send(new SetSpeedMessage(SimulationSpeed.Max));
                var clock = Stopwatch.StartNew();
                while (!host.LongRunFinished)
                {
                    Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "the long run did not end");
                    while (viewer.TryReceive(out _))
                    {
                    }

                    Thread.Sleep(10);
                }
            }

            string summary = File.ReadAllText(Path.Combine(folder, "summary.txt"));
            Assert.Contains("ended:            the world stood still", summary, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void ClosingTheGameEarlyStillWritesTheSummary()
    {
        string folder = Path.Combine(Path.GetTempPath(), "civsim_longrun_closed_" + Environment.ProcessId + "_" + Stopwatch.GetTimestamp());
        try
        {
            using (LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 6) with { LongRunFolder = folder, LongRunSeconds = 3600 }))
            {
                Thread.Sleep(200);
                Assert.False(host.LongRunFinished);
            }

            Assert.Contains("closed before the time limit", File.ReadAllText(Path.Combine(folder, "summary.txt")), StringComparison.Ordinal);
            Assert.Equal(2, File.ReadAllLines(Path.Combine(folder, "stats.csv")).Length);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void WithoutALongRunNothingIsRecorded()
    {
        using LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 7));
        Assert.Null(host.LongRunFolder);
        host.ReportFrame(1, 16);
        host.LogLongRun("ignored");
        Assert.False(host.LongRunFinished);
    }
}
