using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CivSim.Protocol.Transport;

namespace CivSim.Server;

/// <summary>
/// Lets players join a game through a relay (multiplayer plan, chapter 4, MP6): opens a room on the relay, gets its code and, for each
/// player the relay announces, opens a connection to the relay and hands it to the game as a <see cref="TcpConnection"/>. Nobody opens
/// a port: the game server and the players only connect out to the relay, and the players never learn the host's address.
/// </summary>
/// <remarks>
/// When the line to the relay breaks, the room is opened again every few seconds with the same code (the relay keeps it for the
/// room's secret for a while), so the players' code stays valid. Players already in the game through the relay lose their connections
/// only if the relay itself went down; the AI leads their civilizations until they come back (MP2).
/// </remarks>
public sealed class RelayHost : IDisposable
{
    /// <summary>Time between tries to open the room again after the line to the relay broke.</summary>
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(5);

    private readonly Action<IConnection> _accept;
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly TimeSpan _timeout;
    private readonly Thread _thread;
    private readonly List<TcpConnection> _connections = [];
    private readonly int _maxConnections;
    private volatile bool _stopping;
    private volatile string? _code;
    private volatile string? _problem;
    private string _secret = "";
    private Socket? _control;
    private Stream? _controlStream;

    /// <summary>
    /// Opens a room on the relay at <paramref name="host"/>:<paramref name="port"/> with the password to open games (empty when the
    /// relay needs none). Throws <see cref="RelayException"/> with the relay's reason when it cannot (wrong password, relay full...).
    /// </summary>
    public RelayHost(Action<IConnection> accept, string host, int port, string password, TimeSpan timeout,
        int maxConnections = PlayListener.DefaultMaxConnections)
    {
        ArgumentNullException.ThrowIfNull(accept);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _accept = accept;
        _host = host.Trim();
        _port = port;
        _password = password ?? "";
        _timeout = timeout;
        _maxConnections = maxConnections;
        OpenRoom();
        _thread = new Thread(ControlLoop) { IsBackground = true, Name = "CivSim relay" };
        _thread.Start();
    }

    /// <summary>The room code players type (e.g. <c>K7M-4QD</c>).</summary>
    public string Code => RelayWire.ShowCode(_code ?? "");

    /// <summary>The relay's address as players type it.</summary>
    public string Address => _port == RelayWire.DefaultPort ? _host : $"{_host}:{_port}";

    /// <summary>Why the room is closed now (null while it is open), e.g. "Lost the relay; trying again".</summary>
    public string? Problem => _problem;

    /// <summary>The open connections of players who came through the relay (a copy), for the server's measurements.</summary>
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

    /// <summary>Closes the room and every connection that came through it.</summary>
    public void Dispose()
    {
        _stopping = true;
        CloseControl();
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

    private void OpenRoom()
    {
        (Socket socket, Stream stream) = RelayWire.Connect(_host, _port, _timeout, RelayWire.Open, _password, _code ?? "", _secret, Protocol.ProtocolInfo.GameVersion);
        try
        {
            string[] answer = RelayWire.ReadAnswer(stream);
            if (answer.Length < 2 || RelayWire.NormalizeCode(answer[0]) is not { } code)
            {
                throw new RelayException("The relay's answer could not be read.");
            }

            socket.ReceiveTimeout = 0;
            socket.SendTimeout = (int)RelayWire.GreetingTimeout.TotalMilliseconds;
            _code = code;
            _secret = answer[1];
            _control = socket;
            _controlStream = stream;
            _problem = null;
        }
        catch
        {
            stream.Dispose();
            socket.Dispose();
            throw;
        }
    }

    private void ControlLoop()
    {
        while (!_stopping)
        {
            Stream? stream = _controlStream;
            if (stream is null)
            {
                Thread.Sleep(RetryEvery);
                if (_stopping)
                {
                    return;
                }

                try
                {
                    OpenRoom();
                }
                catch (RelayException e)
                {
                    _problem = "The relay cannot be reached; trying again. " + e.Message;
                }

                continue;
            }

            try
            {
                (byte kind, uint value) = RelayWire.ReadControl(stream);
                if (kind == RelayWire.Ping)
                {
                    lock (stream)
                    {
                        RelayWire.WriteControl(stream, RelayWire.Pong, value);
                    }
                }
                else if (kind == RelayWire.Incoming)
                {
                    uint ticket = value;
                    _ = Task.Run(() => TakePlayer(ticket));
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                if (_stopping)
                {
                    return;
                }

                CloseControl();
                _problem = "Lost the relay; trying again.";
            }
        }
    }

    // A player is waiting on the relay: a new connection to the relay takes them into the game.
    private void TakePlayer(uint ticket)
    {
        lock (_connections)
        {
            _connections.RemoveAll(c => !c.IsOpen);
            if (_connections.Count >= _maxConnections)
            {
                return; // the relay tells the player the game did not answer
            }
        }

        string code = _code ?? "";
        Socket? socket = null;
        Stream? stream = null;
        try
        {
            (socket, stream) = RelayWire.Connect(_host, _port, _timeout, RelayWire.Accept, code, _secret, ticket.ToString(System.Globalization.CultureInfo.InvariantCulture));
            RelayWire.ReadAnswer(stream);
            socket.ReceiveTimeout = 0;
            socket.SendTimeout = 0;
            var connection = new TcpConnection(socket, stream, TcpConnection.MaxClientFrame, "relay");
            lock (_connections)
            {
                if (_stopping)
                {
                    connection.Dispose();
                    return;
                }

                _connections.Add(connection);
                _accept(connection);
            }
        }
        catch (RelayException)
        {
            stream?.Dispose();
            socket?.Dispose(); // the player went away meanwhile, or the room closed
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            stream?.Dispose();
            socket?.Dispose();
        }
    }

    private void CloseControl()
    {
        Stream? stream = Interlocked.Exchange(ref _controlStream, null);
        Socket? socket = Interlocked.Exchange(ref _control, null);
        try
        {
            stream?.Dispose();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
        }

        socket?.Dispose();
    }
}
