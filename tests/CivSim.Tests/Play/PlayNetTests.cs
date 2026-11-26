using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using CivSim.Core.Map;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using CivSim.Sim.State;
using CivSim.Tests.Protocol;
using Xunit;

namespace CivSim.Tests.Play;

/// <summary>The game over the network (multiplayer plan, MP1).</summary>
public sealed class PlayNetTests
{
    // Two players over TCP play exactly the game two players in memory play, and get exactly the same messages.
    [Trait("Category", "Slow")]
    [Fact]
    public void TwoPlayersOverTheNetworkPlayTheSameGameAsInMemory()
    {
        (ulong hash, string[] seen) memory = Play(network: false);
        (ulong hash, string[] seen) network = Play(network: true);
        Assert.Equal(memory.hash, network.hash);
        Assert.Equal(memory.seen, network.seen);
    }

    [Fact]
    public void ADedicatedServerLetsTheAiLeadEveryPeopleUntilAPlayerJoins()
    {
        using PlayHost host = PlayHost.Create(new PlayOptions(4, 40, 24, 3) { StartPhase = false, Dedicated = true });
        host.AutosaveOn = false;
        Assert.All(host.Game.Civs, c => Assert.True(c.IsAi));
        using var listener = new PlayListener(host, 0, IPAddress.Loopback);
        using TcpConnection player = TcpConnection.Connect("127.0.0.1", listener.Port, TimeSpan.FromSeconds(5));
        player.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        TcpConnectionTests.WaitUntil(() =>
        {
            host.ViewNow();
            return host.Players == 1;
        });
        WelcomeMessage welcome = Assert.IsType<WelcomeMessage>(TcpConnectionTests.Next(player));
        Assert.False(host.Game.Civs[welcome.CivId].IsAi);

        player.Close(); // leaving hands the people back to the AI
        TcpConnectionTests.WaitUntil(() =>
        {
            host.ViewNow();
            return host.Players == 0;
        });
        Assert.True(host.Game.Civs[welcome.CivId].IsAi);
    }

    [Fact]
    public void ACongestedPlayerGetsNoViewsUntilItCatchesUpAndThenGetsWhatItMissed()
    {
        using PlayHost host = PlayHost.Create(new PlayOptions(4, 40, 24, 2) { StartPhase = false });
        host.AutosaveOn = false;
        host.FullViewEvery = TimeSpan.Zero;
        var slow = new SlowConnection(host);
        slow.Client.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        host.RunTicks(1);
        while (slow.Client.TryReceive(out _))
        {
        }

        slow.Congested = true;
        host.RunTicks(50);
        Assert.False(slow.Client.TryReceive(out _));
        Assert.True(host.SkippedViews >= 50);

        slow.Congested = false;
        host.RunTicks(1);
        PlayViewMessage? view = null;
        while (slow.Client.TryReceive(out IProtocolMessage? m))
        {
            view = m as PlayViewMessage ?? view;
        }

        Assert.NotNull(view);
        Assert.Equal(host.Ticks, view!.Tick);
    }

    private static (ulong Hash, string[] Seen) Play(bool network)
    {
        using PlayHost host = PlayHost.Create(new PlayOptions(5, 66, 40, 4) { StartPhase = true });
        host.AutosaveOn = false;
        host.FullViewEvery = TimeSpan.Zero; // views by ticks, not by the clock
        using PlayListener? listener = network ? new PlayListener(host, 0, IPAddress.Loopback) : null;
        var clients = new List<IConnection>();
        var digests = new List<IncrementalHash>();
        int sent = 0;
        for (int i = 0; i < 2; i++)
        {
            IConnection c = network ? TcpConnection.Connect("127.0.0.1", listener!.Port, TimeSpan.FromSeconds(5)) : host.Connect();
            clients.Add(c);
            digests.Add(IncrementalHash.CreateHash(HashAlgorithmName.SHA256));
            c.Send(new HelloMessage(ProtocolInfo.Version, "Player " + i));
            Deliver(listener, i + 1, ++sent);
            host.RunTicks(0);
        }

        SimGame g = host.Game;
        int guard = 0;
        int sequence = 0;
        while (g.Picking && guard++ < 20_000)
        {
            int civ = g.PickOrder[g.PickIndex];
            if (civ < clients.Count && !g.Civs[civ].IsAi)
            {
                HexCoord at = g.Geo.FromIndex(FreeStart(g));
                clients[civ].Send(new PlayCommandMessage(++sequence, PlayCommandKind.PickStart, 0, at.Col, at.Row, false));
                Deliver(listener, clients.Count, ++sent);
            }

            host.RunTicks(1, viewEvery: 1);
            Drain(listener, clients, digests);
        }

        Assert.False(g.Picking);
        // A few views at a time: more than TcpConnection.CongestedFrames waiting views make the server skip views for that player,
        // and a busy machine (CI on Windows, 2026-11-26) sends slower than 100 ticks run, so the messages differed.
        for (int round = 0; round < 200; round++)
        {
            host.RunTicks(3, viewEvery: 3);
            Drain(listener, clients, digests);
        }

        string[] seen = [.. digests.Select(d => Convert.ToHexString(d.GetHashAndReset()))];
        foreach (IConnection c in clients)
        {
            c.Close();
        }

        return (host.StateHash, seen);
    }

    // Over the network a message reaches the server a moment later: wait until the server has every message sent so far, so
    // each comes at the same tick as in memory.
    private static void Deliver(PlayListener? listener, int connections, int sent)
    {
        if (listener is not null)
        {
            TcpConnectionTests.WaitUntil(() =>
            {
                IReadOnlyList<TcpConnection> server = listener.Connections;
                return server.Count == connections && server.Sum(c => c.FramesReceived) == sent;
            });
        }
    }

    // Reads everything the server sent so far into the digests (over the network, after it has all arrived).
    private static void Drain(PlayListener? listener, List<IConnection> clients, List<IncrementalHash> digests)
    {
        if (listener is not null)
        {
            IReadOnlyList<TcpConnection> server = listener.Connections;
            TcpConnectionTests.WaitUntil(() => server.All(s => s.Backlog == 0)
                && clients.Select((c, i) => ((TcpConnection)c).FramesReceived == server[i].FramesSent).All(x => x));
        }

        for (int i = 0; i < clients.Count; i++)
        {
            while (clients[i].TryReceive(out IProtocolMessage? m))
            {
                if (m is PlayPingMessage or PlayPlayersMessage)
                {
                    continue; // these follow the clock (pings, the list of players once a second), not the game
                }

                digests[i].AppendData(ProtocolSerializer.Serialize(m is WelcomeMessage w ? w with { ReturnKey = "" } : m!)); // return keys are random
            }
        }
    }

    private static int FreeStart(SimGame g)
    {
        int best = -1;
        for (int i = 0; i < g.Geo.CellCount; i++)
        {
            if (g.CannotStart(i) is null && (best < 0 || g.Sites!.Seen[i] > g.Sites.Seen[best]))
            {
                best = i;
            }
        }

        return best;
    }

    // An in-memory connection whose congestion the test switches.
    private sealed class SlowConnection : IConnection
    {
        private readonly IConnection _server;

        public SlowConnection(PlayHost host)
        {
            (InMemoryConnection client, InMemoryConnection server) = InMemoryConnection.CreatePair();
            Client = client;
            _server = server;
            host.Accept(this);
        }

        public IConnection Client { get; }

        public bool Congested { get; set; }

        public bool IsOpen => _server.IsOpen;

        public int Backlog => _server.Backlog;

        public void Send(IProtocolMessage message) => _server.Send(message);

        public bool TryReceive(out IProtocolMessage? message) => _server.TryReceive(out message);

        public void Close() => _server.Close();
    }
}
