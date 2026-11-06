using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CivSim.Protocol;
using CivSim.Protocol.Hosting;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Server;

/// <summary>Stage 5: start menu support on the host and the observer's developer layers.</summary>
public class ObserverLayerTests
{
    private static List<IProtocolMessage> WaitFor(IConnection connection, Func<List<IProtocolMessage>, bool> done, int timeoutMs = 20000)
    {
        var all = new List<IProtocolMessage>();
        var clock = Stopwatch.StartNew();
        while (!done(all))
        {
            Assert.True(clock.ElapsedMilliseconds < timeoutMs, $"timed out after {all.Count} messages");
            while (connection.TryReceive(out IProtocolMessage? m))
            {
                all.Add(m!);
            }

            Thread.Sleep(5);
        }

        return all;
    }

    [Fact]
    public void SetupListsThePlanetPresets()
    {
        LocalSetupInfo info = LocalServerHost.Describe();
        Assert.Equal(["earth", "mars", "uranus"], info.Planets.Select(p => p.Key));
        PlanetPresetInfo earth = info.Planets[0];
        Assert.Equal(234, earth.AxialTiltTenths);
        Assert.Equal(1000, earth.DistanceThousandths);
        Assert.Equal(100, earth.YearHundredths);
        Assert.Equal(60, earth.WaterPercent);
        Assert.Equal("dry", info.Planets[1].Moisture);
        Assert.InRange(info.Planets[2].YearHundredths, 8350, 8450);
    }

    [Fact]
    public void PreviewDescribesThePlanetOrTheError()
    {
        PlanetPreview ok = LocalServerHost.Preview(LocalGameOptions.ForSize("test", 1) with { Planet = "mars" });
        Assert.Null(ok.Error);
        Assert.Contains(ok.Lines, l => l.StartsWith("orbit:", StringComparison.Ordinal) && l.Contains("1.524 AU", StringComparison.Ordinal));

        PlanetPreview bad = LocalServerHost.Preview(LocalGameOptions.ForSize("test", 1) with { WorldType = "donut" });
        Assert.NotNull(bad.Error);
        Assert.Empty(bad.Lines);
    }

    [Fact]
    public void TurnLimitPausesAndTheReportHasOneLinePerTurn()
    {
        string report = Path.Combine(Path.GetTempPath(), $"civsim-report-{Environment.ProcessId}.csv");
        using (LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 3) with { TurnLimit = 4, ReportPath = report }))
        {
            IConnection viewer = host.Connect();
            viewer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
            WaitFor(viewer, l => l.Exists(m => m is GameViewMessage));
            viewer.Send(new SetSpeedMessage(SimulationSpeed.Max));
            List<IProtocolMessage> later = WaitFor(viewer, l => l.Exists(m => m is GameViewMessage { Turn: 4, Speed: SimulationSpeed.Paused }));
            Assert.Contains(later, m => m is GameViewMessage v && v.Chronicle is { } c && c.Count > 0 && c[^1].Text.StartsWith("Turn limit 4", StringComparison.Ordinal));
            Thread.Sleep(100);
            Assert.Equal(4, host.Turn);
            Assert.Null(host.Error);
        }

        string[] lines = File.ReadAllLines(report);
        File.Delete(report);
        Assert.Equal("turn,year,turn_ms,people,settlements", lines[0]);
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("4,", lines[4], StringComparison.Ordinal);
    }

    [Fact]
    public void ObserverGetsLayersAndTheFogOfAWatchedCivilization()
    {
        using LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 3));
        IConnection viewer = host.Connect();
        viewer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
        WaitFor(viewer, l => l.Exists(m => m is ObserverDetailsMessage));
        viewer.Send(new SetSpeedMessage(SimulationSpeed.Max));
        WaitFor(viewer, l => l.Exists(m => m is GameViewMessage { Turn: >= 6 }));
        viewer.Send(new SetSpeedMessage(SimulationSpeed.Paused));
        viewer.Send(new WatchCivilizationMessage(0));
        List<IProtocolMessage> watched = WaitFor(viewer, l => l.Exists(m => m is FogMessage) && l.Exists(m => m is CivilizationDetailsMessage));
        FogMessage fog = (FogMessage)watched.Find(m => m is FogMessage)!;
        Assert.Equal(0, fog.CivId);
        Assert.Equal(66 * 40, fog.Cells.Length);
        Assert.Contains(fog.Cells, c => c == MapChunkMessage.Visible);
        Assert.Contains(fog.Cells, c => c == MapChunkMessage.Unknown);

        CivilizationDetailsMessage civ = (CivilizationDetailsMessage)watched.Find(m => m is CivilizationDetailsMessage)!;
        Assert.Equal(0, civ.CivId);
        Assert.Equal(civ.Strata.Count, civ.People.Count);
        Assert.Equal(civ.TraitNames.Count, civ.Traits.Count);
        Assert.True(civ.People.Sum() > 0);
        GameViewMessage view = (GameViewMessage)watched.FindLast(m => m is GameViewMessage)!;
        Assert.All(view.Civilizations, c => Assert.True(c.People >= 0 && c.Settlements >= 0 && c.Era.Length > 0));

        ObserverDetailsMessage details = (ObserverDetailsMessage)watched.FindLast(m => m is ObserverDetailsMessage)!;
        Assert.All(details.Relations, r => Assert.True(r.A < r.B));
        Assert.All(details.Fields, f => Assert.InRange(f.WorkedPercent, 1, 100));
        Assert.Null(host.Error);
    }

    [Fact]
    public void PlayerCannotWatchAnotherCivilization()
    {
        using LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 3));
        IConnection player = host.Connect();
        player.Send(new HelloMessage(ProtocolInfo.Version, "Patryk"));
        WaitFor(player, l => l.Exists(m => m is GameViewMessage));
        player.Send(new WatchCivilizationMessage(1));
        List<IProtocolMessage> answer = WaitFor(player, l => l.Exists(m => m is ConnectionRejectedMessage));
        Assert.DoesNotContain(answer, m => m is FogMessage or ObserverDetailsMessage or CivilizationDetailsMessage);
    }
}
