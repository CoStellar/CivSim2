using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CivSim.Core.Content;
using CivSim.Core.State;
using CivSim.Data;
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
            List<IProtocolMessage> later = WaitFor(viewer, l =>
                l.Exists(m => m is GameViewMessage v && v.Chronicle is { } c && c.Any(e => e.Text.StartsWith("Turn limit 4", StringComparison.Ordinal)))
                && l.Exists(m => m is GameViewMessage { Turn: 4, Speed: SimulationSpeed.Paused }));
            Assert.DoesNotContain(later, m => m is GameViewMessage { Turn: > 4 });
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
    public void AViewerThatFallsBehindGetsNoPileOfViewsButCatchesUp()
    {
        // A window that stops reading (minimized, too slow at full speed) used to collect every view in memory: 37 GB after
        // a few hours. Now the server waits until it reads, and then sends the latest view with what it missed.
        GameContent content = ContentLoader.Load();
        GameServer server = GameServer.CreateNew(GameSettings.Test(5), content);
        (InMemoryConnection viewer, InMemoryConnection end) = InMemoryConnection.CreatePair();
        server.Accept(end);
        viewer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
        server.Pump();
        for (int t = 0; t < 120; t++)
        {
            server.AddChronicle($"Line {t}");
            server.AdvanceTurn();
        }

        // At most the limit plus one round of messages (a view, the changed chunks, the layers) waits.
        Assert.InRange(end.Backlog, GameServer.MaxBacklog, GameServer.MaxBacklog + 40);
        Assert.False(server.HasDeferredViews);
        var waiting = new List<IProtocolMessage>();
        while (viewer.TryReceive(out IProtocolMessage? m))
        {
            waiting.Add(m!);
        }

        long last = server.State.Turn;
        Assert.DoesNotContain(waiting, m => m is GameViewMessage v && v.Turn == last);

        // Caught up: the latest view comes with the chronicle lines it missed (the newest ones).
        Assert.True(server.HasDeferredViews);
        server.SendViews();
        var later = new List<IProtocolMessage>();
        while (viewer.TryReceive(out IProtocolMessage? m))
        {
            later.Add(m!);
        }

        GameViewMessage view = later.OfType<GameViewMessage>().Single();
        Assert.Equal(last, view.Turn);
        Assert.Contains(view.Chronicle!, e => e.Text == "Line 119");
        Assert.Contains(later, m => m is MapChunkMessage);
        Assert.False(server.HasDeferredViews);
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

    [Fact]
    public void ObserverRenamesAtOnceAndGetsNamesOfTheSameLanguage()
    {
        using LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 3));
        IConnection viewer = host.Connect();
        viewer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
        GameViewMessage first = (GameViewMessage)WaitFor(viewer, l => l.Exists(m => m is GameViewMessage)).Find(m => m is GameViewMessage)!;
        SettlementView camp = first.Settlements![0];

        viewer.Send(new NameSuggestionsRequestMessage(NameTarget.Settlement, camp.Id, 0, 5));
        NameSuggestionsMessage names = (NameSuggestionsMessage)WaitFor(viewer, l => l.Exists(m => m is NameSuggestionsMessage)).Find(m => m is NameSuggestionsMessage)!;
        Assert.Equal(5, names.Names.Count);
        Assert.DoesNotContain(camp.Name, names.Names);

        viewer.Send(new SubmitCommandMessage(camp.Owner, 1, new RenameSettlementDto(camp.Id, names.Names[2])));
        viewer.Send(new SubmitCommandMessage(1, 2, new RenameCivilizationDto("Riverfolk")));
        List<IProtocolMessage> after = WaitFor(viewer, l => l.Exists(m => m is GameViewMessage v && v.Civilizations.Any(c => c.Name == "Riverfolk")));
        GameViewMessage view = (GameViewMessage)after.FindLast(m => m is GameViewMessage)!;
        Assert.Equal(0, view.Turn); // paused: renamed without running a turn
        Assert.Equal(names.Names[2], view.Settlements!.First(s => s.Id == camp.Id).Name);
        Assert.Contains(view.Chronicle!, e => e.Text.EndsWith("was renamed Riverfolk.", StringComparison.Ordinal));

        viewer.Send(new SubmitCommandMessage(2, 3, new RenameCivilizationDto("Riverfolk")));
        Assert.Contains(WaitFor(viewer, l => l.Exists(m => m is CommandRejectedMessage)), m => m is CommandRejectedMessage { ClientSequence: 3 });
    }

    [Fact]
    public void PlayerGetsNamesOnlyForItsOwnCivilization()
    {
        using LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 3));
        IConnection player = host.Connect();
        player.Send(new HelloMessage(ProtocolInfo.Version, "Patryk"));
        WelcomeMessage welcome = (WelcomeMessage)WaitFor(player, l => l.Exists(m => m is WelcomeMessage)).Find(m => m is WelcomeMessage)!;
        player.Send(new NameSuggestionsRequestMessage(NameTarget.Civilization, welcome.CivId, 0, 4));
        Assert.Equal(4, ((NameSuggestionsMessage)WaitFor(player, l => l.Exists(m => m is NameSuggestionsMessage)).Find(m => m is NameSuggestionsMessage)!).Names.Count);
        player.Send(new NameSuggestionsRequestMessage(NameTarget.Civilization, welcome.CivId + 1, 0, 4));
        Assert.Contains(WaitFor(player, l => l.Exists(m => m is ConnectionRejectedMessage)), m => m is ConnectionRejectedMessage);
    }
}
