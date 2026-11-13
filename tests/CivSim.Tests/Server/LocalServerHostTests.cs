using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using CivSim.Protocol;
using CivSim.Protocol.Hosting;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Server;

public class LocalServerHostTests
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
    public void TheHostWritesAReplayThatPlaysTheSameGame()
    {
        // T11: with ReplayPath the host records the game and writes the replay when it stops.
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"civsim-replay-{Environment.ProcessId}-{Environment.CurrentManagedThreadId}.civreplay");
        long turn;
        ulong hash;
        LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 3) with { ReplayPath = path });
        try
        {
            IConnection viewer = host.Connect();
            viewer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
            viewer.Send(new SetSpeedMessage(SimulationSpeed.Max));
            WaitFor(viewer, l => l.Exists(m => m is GameViewMessage v && v.Turn >= 650)); // past the first checkpoint (tick 600), so the replay compares a hash
        }
        finally
        {
            host.Dispose();
        }

        try
        {
            Assert.Null(host.Error);
            var result = CivSim.Server.Replays.ReplayPlayer.PlayFile(path, CivSim.Data.ContentLoader.Load()); // the content the host played with (not TestContent: its pulse in every tick drifts at the first checkpoint, tick 600)
            turn = result.State.Turn;
            hash = Core.Hashing.GameStateHash.Compute(result.State);
            Assert.True(result.Matches, result.Divergence?.ToString());
            Assert.True(turn >= 650, $"replayed to tick {turn}");
            Assert.NotEqual(0UL, hash);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void ObserverGetsContentTheWholeMapAndControlsSpeed()
    {
        using LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 3));
        IConnection viewer = host.Connect();
        viewer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));

        // 64×40 map = 2×2 chunks.
        List<IProtocolMessage> first = WaitFor(viewer, l => l.FindAll(m => m is MapChunkMessage).Count >= 4);
        WelcomeMessage welcome = Assert.IsType<WelcomeMessage>(first[0]);
        Assert.Equal(WelcomeMessage.NoCiv, welcome.CivId);
        ContentMessage content = Assert.IsType<ContentMessage>(first[1]);
        Assert.Equal(14, content.Biomes.Count);
        GameViewMessage view = Assert.IsType<GameViewMessage>(first[2]);
        Assert.Equal(0, view.Turn);
        Assert.Contains(view.Chronicle!, e => e.Text.StartsWith("World generated", StringComparison.Ordinal));
        foreach (MapChunkMessage chunk in first.FindAll(m => m is MapChunkMessage).ConvertAll(m => (MapChunkMessage)m))
        {
            Assert.All(chunk.Visibility, v => Assert.Equal(MapChunkMessage.Visible, v));
        }

        // Paused: no turns. Max speed: turns run on the simulation thread.
        Thread.Sleep(100);
        Assert.Equal(0, host.Turn);
        viewer.Send(new SetSpeedMessage(SimulationSpeed.Max));
        List<IProtocolMessage> later = WaitFor(viewer, l => l.Exists(m => m is GameViewMessage v && v.Turn >= 20));
        Assert.Contains(later, m => m is GameViewMessage v && v.Speed == SimulationSpeed.Max);

        viewer.Send(new SetSpeedMessage(SimulationSpeed.Paused));
        WaitFor(viewer, l => l.Exists(m => m is GameViewMessage v && v.Speed == SimulationSpeed.Paused));
        long stopped = host.Turn;
        Thread.Sleep(200);
        Assert.Equal(stopped, host.Turn);
        Assert.Null(host.Error);
    }

    [Fact]
    public void PlayerOnlyReceivesCellsItKnows()
    {
        using LocalServerHost host = LocalServerHost.Start(LocalGameOptions.ForSize("test", 3));
        IConnection player = host.Connect();
        player.Send(new HelloMessage(ProtocolInfo.Version, "Patryk"));
        List<IProtocolMessage> messages = WaitFor(player, l => l.Exists(m => m is MapChunkMessage));
        Thread.Sleep(100);
        while (player.TryReceive(out IProtocolMessage? m))
        {
            messages.Add(m!);
        }

        int known = 0;
        int total = 0;
        foreach (IProtocolMessage m in messages)
        {
            if (m is MapChunkMessage chunk)
            {
                for (int k = 0; k < chunk.Visibility.Length; k++)
                {
                    total++;
                    if (chunk.Visibility[k] == MapChunkMessage.Unknown)
                    {
                        Assert.Equal(0, chunk.Biome[k]);
                        Assert.Equal(0, chunk.Deposit[k]);
                    }
                    else
                    {
                        known++;
                    }
                }
            }
        }

        // Map memory at the start: sight 6 around the starting camp = 127 cells (stage 3g).
        Assert.Equal(127, known);
        Assert.True(total > known);

        // A player who plays alone sets the speed (stage 6 PR P, ADR 0024: pause and speeds in the solo game); with a second player
        // neither may (the host sets it in a game of several, stage 9).
        player.Send(new SetSpeedMessage(SimulationSpeed.Max));
        Assert.Contains(WaitFor(player, l => l.Exists(m => m is GameViewMessage { Speed: SimulationSpeed.Max })), m => m is GameViewMessage);
        IConnection second = host.Connect();
        second.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
        WaitFor(second, l => l.Exists(m => m is WelcomeMessage));
        player.Send(new SetSpeedMessage(SimulationSpeed.X1));
        Assert.Contains(WaitFor(player, l => l.Exists(m => m is ConnectionRejectedMessage)), m => m is ConnectionRejectedMessage);
    }

    [Fact]
    public void UnknownSettingWordIsRefused()
    {
        Assert.Throws<ArgumentException>(() => LocalServerHost.Start(LocalGameOptions.ForSize("test", 1) with { Planet = "lava" }));
    }
}
