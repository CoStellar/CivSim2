using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CivSim.Protocol.Transport;

namespace CivSim.Server;

/// <summary>
/// Lets players join a <see cref="PlayHost"/> over the network (multiplayer plan, MP1): listens on a TCP port and hands every
/// connection to the host as a <see cref="TcpConnection"/>. Limits the number of connections in all and from one address, so a
/// stranger cannot fill the server with idle sockets (multiplayer plan, chapter 9).
/// </summary>
public sealed class PlayListener : IDisposable
{
    /// <summary>Connections open at once, players and anyone still saying who they are.</summary>
    public const int DefaultMaxConnections = 16;

    /// <summary>Connections open at once from one address (a few players behind one home router).</summary>
    public const int DefaultMaxPerAddress = 8;

    private readonly Action<IConnection> _accept;
    private readonly TcpListener _listener;
    private readonly Thread _thread;
    private readonly List<TcpConnection> _connections = [];
    private readonly int _maxConnections;
    private readonly int _maxPerAddress;
    private volatile bool _stopping;
    private long _refused;

    /// <summary>Starts listening. Port 0 takes any free port (tests); <see cref="Port"/> says which.</summary>
    public PlayListener(PlayHost host, int port = TcpConnection.DefaultPort, IPAddress? address = null,
        int maxConnections = DefaultMaxConnections, int maxPerAddress = DefaultMaxPerAddress)
        : this(Into(host), port, address, maxConnections, maxPerAddress)
    {
    }

    private static Action<IConnection> Into(PlayHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return c => host.Accept(c);
    }

    /// <summary>Starts listening and hands every connection to <paramref name="accept"/> (a lobby, then its game).</summary>
    public PlayListener(Action<IConnection> accept, int port = TcpConnection.DefaultPort, IPAddress? address = null,
        int maxConnections = DefaultMaxConnections, int maxPerAddress = DefaultMaxPerAddress)
    {
        ArgumentNullException.ThrowIfNull(accept);
        _accept = accept;
        _maxConnections = maxConnections;
        _maxPerAddress = maxPerAddress;
        _listener = new TcpListener(address ?? IPAddress.Any, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "CivSim listen" };
        _thread.Start();
    }

    /// <summary>The port players connect to.</summary>
    public int Port { get; }

    /// <summary>Connections turned away because of the limits.</summary>
    public long Refused => Interlocked.Read(ref _refused);

    /// <summary>The open connections (a copy), for the server's measurements.</summary>
    public IReadOnlyList<TcpConnection> Connections
    {
        get
        {
            lock (_connections)
            {
                _connections.RemoveAll(c => !c.IsOpen);
                return [.. _connections];
            }
        }
    }

    /// <summary>Stops listening and closes every connection.</summary>
    public void Dispose()
    {
        _stopping = true;
        _listener.Stop();
        _thread.Join(TimeSpan.FromSeconds(2));
        lock (_connections)
        {
            foreach (TcpConnection c in _connections)
            {
                c.Dispose();
            }

            _connections.Clear();
        }
    }

    private void AcceptLoop()
    {
        while (!_stopping)
        {
            Socket socket;
            try
            {
                socket = _listener.AcceptSocket();
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                if (_stopping)
                {
                    return;
                }

                continue;
            }

            IPAddress? from = (socket.RemoteEndPoint as IPEndPoint)?.Address;
            lock (_connections)
            {
                _connections.RemoveAll(c => !c.IsOpen);
                int same = 0;
                foreach (TcpConnection c in _connections)
                {
                    if (from is not null && from.Equals(c.RemoteIp))
                    {
                        same++;
                    }
                }

                if (_connections.Count >= _maxConnections || same >= _maxPerAddress)
                {
                    Interlocked.Increment(ref _refused);
                    socket.Close();
                    continue;
                }

                var connection = new TcpConnection(socket, TcpConnection.MaxClientFrame);
                _connections.Add(connection);
                _accept(connection);
            }
        }
    }
}
