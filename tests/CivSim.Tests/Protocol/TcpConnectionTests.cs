using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using Xunit;

namespace CivSim.Tests.Protocol;

/// <summary>The network connection (multiplayer plan, MP1): frames, packing, limits, and what a stranger can do to it.</summary>
public sealed class TcpConnectionTests
{
    [Fact]
    public void MessagesGoBothWaysAndBigOnesArePacked()
    {
        (TcpConnection client, TcpConnection server) = Pair();
        using (client)
        using (server)
        {
            client.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
            HelloMessage hello = Assert.IsType<HelloMessage>(Next(server));
            Assert.Equal("Ala", hello.PlayerName);

            string big = string.Concat(System.Linq.Enumerable.Repeat("The steppe people build a granary. ", 3000));
            server.Send(new ConnectionRejectedMessage(big));
            ConnectionRejectedMessage back = Assert.IsType<ConnectionRejectedMessage>(Next(client));
            Assert.Equal(big, back.Reason);
            Assert.True(server.BytesSent * 10 < server.RawBytesSent, $"packed {server.BytesSent} of {server.RawBytesSent} bytes");
        }
    }

    [Fact]
    public void AFrameOverTheLimitClosesTheConnection()
    {
        (TcpConnection client, TcpConnection server) = Pair();
        using (client)
        using (server)
        {
            // A client may send the server at most 256 KB; random text does not pack, so the frame itself is over the limit.
            var random = new System.Random(3); // test data only, not game logic
            char[] text = new char[TcpConnection.MaxClientFrame + 1000];
            for (int i = 0; i < text.Length; i++)
            {
                text[i] = (char)random.Next(33, 126);
            }

            client.Send(new ConnectionRejectedMessage(new string(text)));
            WaitUntil(() => !server.IsOpen);
            Assert.Contains("limit", server.CloseReason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void APackedFrameThatUnpacksOverTheLimitClosesTheConnection()
    {
        (TcpConnection client, TcpConnection server) = Pair();
        using (client)
        using (server)
        {
            // A few kilobytes that would unpack into megabytes (a "zip bomb"): turned away before unpacking.
            client.Send(new ConnectionRejectedMessage(new string('a', 4 * TcpConnection.MaxClientFrame)));
            WaitUntil(() => !server.IsOpen);
            Assert.Contains("unpacks to", server.CloseReason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnotherProgramIsTurnedAway()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var raw = new TcpClient();
        raw.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var server = new TcpConnection(listener.AcceptSocket(), TcpConnection.MaxClientFrame);
        raw.GetStream().Write("GET / HTTP/1.1\r\n\r\n"u8);
        WaitUntil(() => !server.IsOpen);
        Assert.Contains("not a CivSim game", server.CloseReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherProtocolVersionIsTurnedAwayWithAReason()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var raw = new TcpClient();
        raw.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var server = new TcpConnection(listener.AcceptSocket(), TcpConnection.MaxClientFrame);
        byte[] hello = new byte[8];
        "CIVS"u8.CopyTo(hello);
        BinaryPrimitives.WriteInt32LittleEndian(hello.AsSpan(4), ProtocolInfo.Version - 1);
        raw.GetStream().Write(hello);
        WaitUntil(() => !server.IsOpen);
        Assert.Contains("protocol " + (ProtocolInfo.Version - 1), server.CloseReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageThatCannotBeReadClosesTheConnection()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var raw = new TcpClient();
        raw.Connect(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        using var server = new TcpConnection(listener.AcceptSocket(), TcpConnection.MaxClientFrame);
        byte[] bytes = new byte[8 + 4 + 12];
        "CIVS"u8.CopyTo(bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), ProtocolInfo.Version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 12);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), ProtocolInfo.Version);
        bytes.AsSpan(16).Fill(0xC1); // never used in MessagePack
        raw.GetStream().Write(bytes);
        WaitUntil(() => !server.IsOpen);
        Assert.Contains("could not be read", server.CloseReason, StringComparison.Ordinal);
        Assert.False(server.TryReceive(out _));
    }

    [Fact]
    public void ClosingStillDeliversWhatWasSentBefore()
    {
        (TcpConnection client, TcpConnection server) = Pair();
        using (client)
        using (server)
        {
            server.Send(new ConnectionRejectedMessage("Wrong password."));
            server.Close(); // the refusal with its reason must reach the player
            Assert.False(server.IsOpen);
            Assert.Equal("Wrong password.", Assert.IsType<ConnectionRejectedMessage>(Next(client)).Reason);
            WaitUntil(() => !client.IsOpen);
        }
    }

    [Fact]
    public void SendingAfterTheOtherSideLeftDoesNothing()
    {
        (TcpConnection client, TcpConnection server) = Pair();
        using (server)
        {
            client.Dispose();
            WaitUntil(() => !server.IsOpen);
            server.Send(new ConnectionRejectedMessage("bye")); // the server must not stop because a player left
            Assert.False(server.IsOpen);
        }
    }

    [Fact]
    public void ConnectingWhereNobodyListensFails()
    {
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        Assert.ThrowsAny<SocketException>(() => TcpConnection.Connect("127.0.0.1", port, TimeSpan.FromSeconds(5)));
    }

    internal static (TcpConnection Client, TcpConnection Server) Pair()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        TcpConnection client = TcpConnection.Connect("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, TimeSpan.FromSeconds(5));
        var server = new TcpConnection(listener.AcceptSocket(), TcpConnection.MaxClientFrame);
        return (client, server);
    }

    internal static IProtocolMessage Next(IConnection c)
    {
        IProtocolMessage? m = null;
        WaitUntil(() => c.TryReceive(out m));
        return m!;
    }

    internal static void WaitUntil(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "timed out");
            Thread.Sleep(1);
        }
    }
}
