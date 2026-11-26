using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CivSim.Protocol.Transport;

namespace CivSim.Relay;

/// <summary>
/// The relay (multiplayer plan, chapter 4, MP6). A game server opens a room and gets a code; a player types the relay's address and
/// the code; the relay asks the game server to take the player and from then on only copies bytes both ways. It never decodes the
/// game, keeps no addresses in its log, and limits rooms, players, connections per address, bytes per second and wrong tries.
/// </summary>
public sealed class RelayServer : IDisposable
{
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PongWithin = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan JoinWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TriesWindow = TimeSpan.FromMinutes(10);
    private const int MaxWrongPasswords = 5;
    private const int MaxWrongCodes = 20;

    private readonly RelayConfig _config;
    private readonly TextWriter _log;
    private readonly Func<DateTime> _now;
    private readonly RelayPasswords _passwords;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, Room> _rooms = [];
    private readonly Dictionary<string, (string Secret, DateTime Until)> _reserved = [];
    private readonly Dictionary<IPAddress, int> _perAddress = [];
    private readonly Dictionary<IPAddress, (int Passwords, int Codes, DateTime Since)> _wrong = [];
    private readonly List<Pipe> _pipes = [];
    private readonly Task _accepting;
    private readonly Task _sweeping;
    private X509Certificate2? _certificate;
    private DateTime _certificateWritten;
    private int _connections;
    private long _bytes;
    private long _opened;

    /// <summary>Starts listening (port 0 takes any free port; <see cref="Port"/> says which). <paramref name="now"/> is the clock (tests).</summary>
    public RelayServer(RelayConfig config, TextWriter log, Func<DateTime>? now = null, IPAddress? address = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(log);
        _config = config;
        _log = log;
        _now = now ?? (() => DateTime.UtcNow);
        _passwords = new RelayPasswords(config.PasswordsFile, config.GraceDays, _now, Log);
        LoadCertificate();
        _listener = new TcpListener(address ?? IPAddress.Any, config.Port);
        _listener.Start(64);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Log(string.Create(CultureInfo.InvariantCulture,
            $"CivSim relay on port {Port}: up to {config.MaxRooms} games, {config.MaxPlayersPerRoom} players each, {config.RoomKBps} KB/s per game; " +
            $"{(_certificate is null ? "no TLS (players connect by IP address)" : "TLS on")}; " +
            $"{(_passwords.Open ? "anyone may open games (no passwords file)" : $"games open with a password ({_passwords.Working} work today)")}."));
        _accepting = Task.Run(AcceptLoop);
        _sweeping = Task.Run(SweepLoop);
    }

    /// <summary>The port the relay listens on.</summary>
    public int Port { get; }

    /// <summary>Games open now.</summary>
    public int Rooms
    {
        get
        {
            lock (_rooms)
            {
                return _rooms.Count;
            }
        }
    }

    /// <summary>Players passed through now.</summary>
    public int Players
    {
        get
        {
            lock (_pipes)
            {
                return _pipes.Count;
            }
        }
    }

    /// <summary>Bytes passed since the start, both ways.</summary>
    public long Bytes => Interlocked.Read(ref _bytes);

    /// <summary>Stops listening and closes every connection.</summary>
    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            Task.WaitAll([_accepting, _sweeping], TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        lock (_rooms)
        {
            foreach (Room room in _rooms.Values)
            {
                room.Close();
            }

            _rooms.Clear();
        }

        lock (_pipes)
        {
            foreach (Pipe pipe in _pipes)
            {
                pipe.Stop.Cancel();
            }
        }

        _stop.Dispose();
    }

    private void Log(string line) => _log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{_now():yyyy-MM-dd HH:mm:ss}] {line}"));

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            Socket socket;
            try
            {
                socket = await _listener.AcceptSocketAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or OperationCanceledException or InvalidOperationException)
            {
                if (_stop.IsCancellationRequested)
                {
                    return;
                }

                continue;
            }

            IPAddress address = (socket.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
            lock (_perAddress)
            {
                int same = _perAddress.GetValueOrDefault(address);
                if (_connections >= _config.MaxConnections || same >= _config.MaxPerAddress)
                {
                    socket.Dispose();
                    continue;
                }

                _perAddress[address] = same + 1;
                _connections++;
            }

            _ = Task.Run(() => Serve(socket, address));
        }
    }

    private void Gone(IPAddress address)
    {
        lock (_perAddress)
        {
            _connections--;
            if (_perAddress.TryGetValue(address, out int n) && n > 1)
            {
                _perAddress[address] = n - 1;
            }
            else
            {
                _perAddress.Remove(address);
            }
        }
    }

    // One connection: TLS when it starts with a TLS handshake, then the greeting, then what it asked for.
    private async Task Serve(Socket socket, IPAddress address)
    {
        Stream? stream = null;
        bool kept = false; // a waiting player handed to a pipe, or a game server's connection for a player
        try
        {
            socket.NoDelay = true;
            socket.ReceiveTimeout = (int)RelayWire.GreetingTimeout.TotalMilliseconds;
            socket.SendTimeout = (int)RelayWire.GreetingTimeout.TotalMilliseconds;
            byte[] first = new byte[1];
            using (var greeting = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
            {
                greeting.CancelAfter(RelayWire.GreetingTimeout);
                if (await socket.ReceiveAsync(first, SocketFlags.Peek, greeting.Token).ConfigureAwait(false) != 1)
                {
                    return;
                }

                stream = new NetworkStream(socket, ownsSocket: false);
                if (first[0] == RelayWire.TlsFirstByte)
                {
                    X509Certificate2? certificate = Certificate();
                    if (certificate is null)
                    {
                        return; // a player typed the relay's name, but this relay has no certificate
                    }

                    var tls = new SslStream(stream, leaveInnerStreamOpen: false);
                    stream = tls;
                    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, greeting.Token).ConfigureAwait(false);
                }
            }

            (byte kind, string[] texts) = RelayWire.ReadGreeting(stream);
            kept = kind switch
            {
                RelayWire.Open => await OpenRoom(socket, stream, address, texts).ConfigureAwait(false),
                RelayWire.Join => await JoinRoom(socket, stream, address, texts).ConfigureAwait(false),
                RelayWire.Accept => AcceptPlayer(socket, stream, texts),
                _ => Refuse(stream, "Unknown greeting."),
            };
        }
        catch (RelayException e)
        {
            Refuse(stream, e.Message);
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException or System.Security.Authentication.AuthenticationException)
        {
        }
        finally
        {
            if (!kept)
            {
                Shut(socket, stream);
                Gone(address);
            }
        }
    }

    private static bool Refuse(Stream? stream, string reason)
    {
        if (stream is not null)
        {
            try
            {
                RelayWire.WriteAnswer(stream, false, reason);
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or NotSupportedException)
            {
            }
        }

        return false;
    }

    private static void Shut(Socket socket, Stream? stream)
    {
        try
        {
            stream?.Dispose();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
        }

        socket.Dispose();
    }

    // Too many wrong passwords or codes from one address: refused for a while (no guessing passwords or codes).
    private bool TooManyTries(IPAddress address, bool password)
    {
        lock (_wrong)
        {
            if (!_wrong.TryGetValue(address, out var w) || _now() - w.Since > TriesWindow)
            {
                return false;
            }

            return password ? w.Passwords >= MaxWrongPasswords : w.Codes >= MaxWrongCodes;
        }
    }

    private void Wrong(IPAddress address, bool password)
    {
        lock (_wrong)
        {
            if (!_wrong.TryGetValue(address, out var w) || _now() - w.Since > TriesWindow)
            {
                w = (0, 0, _now());
            }

            _wrong[address] = password ? (w.Passwords + 1, w.Codes, w.Since) : (w.Passwords, w.Codes + 1, w.Since);
        }
    }

    // A game server opens a room: password, then a code (the same as before for its secret), then the control line until it goes.
    private async Task<bool> OpenRoom(Socket socket, Stream stream, IPAddress address, string[] texts)
    {
        string password = texts.Length > 0 ? texts[0] : "";
        string? wanted = texts.Length > 1 ? RelayWire.NormalizeCode(texts[1]) : null;
        string oldSecret = texts.Length > 2 ? texts[2] : "";
        string version = texts.Length > 3 ? texts[3] : "?";
        if (TooManyTries(address, password: true))
        {
            return Refuse(stream, "Too many wrong passwords; try again in 10 minutes.");
        }

        if (!_passwords.Check(password))
        {
            Wrong(address, password: true);
            await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token).ConfigureAwait(false);
            return Refuse(stream, password.Length == 0 ? "This relay needs a password to open games." : "Wrong password to open games on this relay.");
        }

        Room room;
        lock (_rooms)
        {
            DateTime now = _now();
            foreach (string gone in new List<string>(_reserved.Keys))
            {
                if (_reserved[gone].Until < now)
                {
                    _reserved.Remove(gone);
                }
            }

            string code;
            bool again = false;
            if (wanted is not null && _reserved.TryGetValue(wanted, out var kept) && SameSecret(kept.Secret, oldSecret))
            {
                code = wanted;
                again = true;
                _reserved.Remove(wanted);
            }
            else if (wanted is not null && _rooms.TryGetValue(wanted, out Room? stale) && SameSecret(stale.Secret, oldSecret))
            {
                code = wanted; // its server lost the line before the relay noticed
                again = true;
                _rooms.Remove(wanted);
                stale.Close();
            }
            else
            {
                if (_rooms.Count >= _config.MaxRooms)
                {
                    return Refuse(stream, $"The relay is full: {_config.MaxRooms} games at once. Try again later.");
                }

                do
                {
                    code = NewCode();
                }
                while (_rooms.ContainsKey(code) || _reserved.ContainsKey(code));
            }

            room = new Room(code, again ? oldSecret : Convert.ToHexString(RandomNumberGenerator.GetBytes(16)), socket, stream, _config.RoomKBps * 1024L, now);
            _rooms[code] = room;
            _opened++;
            Log($"Game {RelayWire.ShowCode(code)} {(again ? "opened again" : "opened")} (game version {Clean(version)}); {_rooms.Count} games.");
        }

        RelayWire.WriteAnswer(stream, true, room.Code, room.Secret);
        socket.ReceiveTimeout = 0;
        await Control(room).ConfigureAwait(false);
        lock (_rooms)
        {
            if (_rooms.TryGetValue(room.Code, out Room? same) && same == room)
            {
                _rooms.Remove(room.Code);
                if (_config.ReserveMinutes > 0)
                {
                    _reserved[room.Code] = (room.Secret, _now().AddMinutes(_config.ReserveMinutes));
                }

                Log($"Game {RelayWire.ShowCode(room.Code)} closed; {_rooms.Count} games.");
            }
        }

        room.Close();
        return false;
    }

    private static bool SameSecret(string a, string b) =>
        a.Length > 0 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string NewCode()
    {
        Span<char> code = stackalloc char[RelayWire.CodeLength];
        for (int i = 0; i < code.Length; i++)
        {
            code[i] = RelayWire.CodeLetters[RandomNumberGenerator.GetInt32(RelayWire.CodeLetters.Length)];
        }

        return new string(code);
    }

    private static string Clean(string text)
    {
        var b = new StringBuilder();
        foreach (char c in text)
        {
            if (b.Length < 32 && (char.IsLetterOrDigit(c) || c is '.' or '-'))
            {
                b.Append(c);
            }
        }

        return b.Length > 0 ? b.ToString() : "?";
    }

    // The room's control line: pings out, pongs in; the room closes when the line breaks or stays silent.
    private async Task Control(Room room)
    {
        Task reading = Task.Run(() =>
        {
            try
            {
                while (!room.Closed.IsCancellationRequested)
                {
                    (byte kind, _) = RelayWire.ReadControl(room.Stream);
                    if (kind == RelayWire.Pong)
                    {
                        room.Heard = _now();
                    }
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
            }
        });
        try
        {
            while (!room.Closed.IsCancellationRequested && !reading.IsCompleted)
            {
                await Task.WhenAny(reading, Task.Delay(PingEvery, room.Closed.Token)).ConfigureAwait(false);
                if (reading.IsCompleted || _now() - room.Heard > PongWithin || !room.Send(RelayWire.Ping, 0))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        room.Close();
        await reading.ConfigureAwait(false);
    }

    // A player joins: the game server is asked to take them; once it does, the bytes flow.
    private async Task<bool> JoinRoom(Socket socket, Stream stream, IPAddress address, string[] texts)
    {
        string? code = texts.Length > 0 ? RelayWire.NormalizeCode(texts[0]) : null;
        if (TooManyTries(address, password: false))
        {
            return Refuse(stream, "Too many wrong codes; try again in 10 minutes.");
        }

        Room? room = null;
        lock (_rooms)
        {
            if (code is not null)
            {
                _rooms.TryGetValue(code, out room);
            }
        }

        if (room is null)
        {
            Wrong(address, password: false);
            return Refuse(stream, $"No game with the code {(code is null ? (texts.Length > 0 ? Clean(texts[0]) : "?") : RelayWire.ShowCode(code))} on this relay. Check the code with the host.");
        }

        if (room.Players + room.WaitingCount >= _config.MaxPlayersPerRoom)
        {
            return Refuse(stream, "This game is full on the relay.");
        }

        (uint ticket, TaskCompletionSource<(Socket, Stream)> taken) = room.Wait();
        if (!room.Send(RelayWire.Incoming, ticket))
        {
            room.Forget(ticket);
            return Refuse(stream, "The game is not answering. Is it still running?");
        }

        Task done = await Task.WhenAny(taken.Task, Task.Delay(JoinWait, _stop.Token)).ConfigureAwait(false);
        room.Forget(ticket);
        if (done != taken.Task && !taken.TrySetCanceled())
        {
            done = taken.Task; // taken at the last moment
        }

        if (done != taken.Task || taken.Task.IsCanceled)
        {
            return Refuse(stream, "The game did not take you in time. Is it still running?");
        }

        (Socket hostSocket, Stream hostStream) = taken.Task.Result;
        try
        {
            RelayWire.WriteAnswer(stream, true, "OK");
            RelayWire.WriteAnswer(hostStream, true, "OK");
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
        {
            Shut(hostSocket, hostStream);
            Gone((hostSocket.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None);
            return false;
        }

        socket.ReceiveTimeout = 0;
        socket.SendTimeout = 0;
        hostSocket.ReceiveTimeout = 0;
        hostSocket.SendTimeout = 0;
        var pipe = new Pipe(room, socket, stream, address, hostSocket, hostStream, (hostSocket.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None, _now());
        lock (_pipes)
        {
            _pipes.Add(pipe);
        }

        Interlocked.Increment(ref room.PlayerCount);
        _ = Task.Run(() => Run(pipe));
        return true;
    }

    // The game server's connection for a waiting player.
    private bool AcceptPlayer(Socket socket, Stream stream, string[] texts)
    {
        string? code = texts.Length > 0 ? RelayWire.NormalizeCode(texts[0]) : null;
        Room? room = null;
        lock (_rooms)
        {
            if (code is not null)
            {
                _rooms.TryGetValue(code, out room);
            }
        }

        if (room is null || texts.Length < 3 || !SameSecret(room.Secret, texts[1])
            || !uint.TryParse(texts[2], NumberStyles.None, CultureInfo.InvariantCulture, out uint ticket))
        {
            return Refuse(stream, "No such game on this relay.");
        }

        if (!room.Take(ticket, socket, stream))
        {
            return Refuse(stream, "That player is not waiting any more.");
        }

        return true; // the player's side answers both and starts the pipe
    }

    private async Task Run(Pipe pipe)
    {
        try
        {
            await Task.WhenAll(Copy(pipe, pipe.PlayerStream, pipe.HostStream), Copy(pipe, pipe.HostStream, pipe.PlayerStream)).ConfigureAwait(false);
        }
        finally
        {
            Shut(pipe.PlayerSocket, pipe.PlayerStream);
            Shut(pipe.HostSocket, pipe.HostStream);
            Gone(pipe.PlayerAddress);
            Gone(pipe.HostAddress);
            Interlocked.Decrement(ref pipe.Room.PlayerCount);
            lock (_pipes)
            {
                _pipes.Remove(pipe);
            }

            pipe.Stop.Dispose();
        }
    }

    private async Task Copy(Pipe pipe, Stream from, Stream to)
    {
        byte[] buffer = new byte[16 * 1024];
        CancellationToken stop = pipe.Stop.Token;
        try
        {
            while (true)
            {
                int n = await from.ReadAsync(buffer, stop).ConfigureAwait(false);
                if (n <= 0)
                {
                    break;
                }

                pipe.Heard = _now();
                await pipe.Room.Limit.Take(n, stop).ConfigureAwait(false);
                await to.WriteAsync(buffer.AsMemory(0, n), stop).ConfigureAwait(false);
                Interlocked.Add(ref _bytes, n);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
        }
        finally
        {
            try
            {
                pipe.Stop.Cancel(); // one side gone: close both
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    // Once every 30 s: closes silent connections, reloads a renewed certificate, and now and then logs the numbers.
    private async Task SweepLoop()
    {
        var stats = Stopwatch.StartNew();
        long lastBytes = 0;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), _stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            DateTime now = _now();
            lock (_pipes)
            {
                foreach (Pipe pipe in _pipes)
                {
                    if (now - pipe.Heard > TimeSpan.FromMinutes(_config.IdleMinutes))
                    {
                        pipe.Stop.Cancel();
                    }
                }
            }

            Certificate();
            if (stats.Elapsed >= TimeSpan.FromMinutes(_config.StatsMinutes))
            {
                long bytes = Bytes;
                Log(string.Create(CultureInfo.InvariantCulture,
                    $"{Rooms} games, {Players} players, {(bytes - lastBytes) / stats.Elapsed.TotalSeconds / 1024:F1} KB/s passed; {Environment.WorkingSet / (1024 * 1024)} MB."));
                lastBytes = bytes;
                stats.Restart();
            }
        }
    }

    private X509Certificate2? Certificate()
    {
        if (_config.CertificateFile.Length > 0)
        {
            try
            {
                if (File.GetLastWriteTimeUtc(_config.CertificateFile) != _certificateWritten)
                {
                    LoadCertificate();
                }
            }
            catch (IOException)
            {
            }
        }

        return Volatile.Read(ref _certificate);
    }

    private void LoadCertificate()
    {
        if (_config.CertificateFile.Length == 0)
        {
            return;
        }

        try
        {
            DateTime written = File.GetLastWriteTimeUtc(_config.CertificateFile);
            using X509Certificate2 pem = X509Certificate2.CreateFromPemFile(_config.CertificateFile, _config.KeyFile);
            X509Certificate2 usable = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null); // a key SslStream can use on every system
            X509Certificate2? old = Interlocked.Exchange(ref _certificate, usable);
            _certificateWritten = written;
            if (old is not null)
            {
                Log($"Certificate renewed (valid until {usable.NotAfter.ToUniversalTime():yyyy-MM-dd}).");
            }
        }
        catch (Exception e) when (e is IOException or CryptographicException or UnauthorizedAccessException)
        {
            Log("The TLS certificate could not be read: " + e.Message);
        }
    }

    private sealed class Room(string code, string secret, Socket socket, Stream stream, long bytesPerSecond, DateTime now)
    {
        private readonly Dictionary<uint, TaskCompletionSource<(Socket, Stream)>> _waiting = [];
        private uint _next = 1;

        public int PlayerCount;

        public string Code { get; } = code;

        public string Secret { get; } = secret;

        public Stream Stream { get; } = stream;

        public CancellationTokenSource Closed { get; } = new();

        public RateLimit Limit { get; } = new(bytesPerSecond);

        public DateTime Heard { get; set; } = now;

        public int Players => Volatile.Read(ref PlayerCount);

        public int WaitingCount
        {
            get
            {
                lock (_waiting)
                {
                    return _waiting.Count;
                }
            }
        }

        public (uint, TaskCompletionSource<(Socket, Stream)>) Wait()
        {
            lock (_waiting)
            {
                uint ticket = _next++;
                var taken = new TaskCompletionSource<(Socket, Stream)>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting[ticket] = taken;
                return (ticket, taken);
            }
        }

        public void Forget(uint ticket)
        {
            lock (_waiting)
            {
                _waiting.Remove(ticket);
            }
        }

        public bool Take(uint ticket, Socket hostSocket, Stream hostStream)
        {
            TaskCompletionSource<(Socket, Stream)>? taken;
            lock (_waiting)
            {
                if (!_waiting.Remove(ticket, out taken))
                {
                    return false;
                }
            }

            return taken.TrySetResult((hostSocket, hostStream));
        }

        public bool Send(byte kind, uint value)
        {
            try
            {
                lock (Stream)
                {
                    RelayWire.WriteControl(Stream, kind, value);
                }

                return true;
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or NotSupportedException)
            {
                return false;
            }
        }

        public void Close()
        {
            try
            {
                Closed.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            Shut(socket, Stream);
        }
    }

    private sealed class Pipe(Room room, Socket playerSocket, Stream playerStream, IPAddress playerAddress, Socket hostSocket, Stream hostStream, IPAddress hostAddress, DateTime now)
    {
        public Room Room { get; } = room;

        public Socket PlayerSocket { get; } = playerSocket;

        public Stream PlayerStream { get; } = playerStream;

        public IPAddress PlayerAddress { get; } = playerAddress;

        public Socket HostSocket { get; } = hostSocket;

        public Stream HostStream { get; } = hostStream;

        public IPAddress HostAddress { get; } = hostAddress;

        public CancellationTokenSource Stop { get; } = new();

        public DateTime Heard { get; set; } = now;
    }

    // Bytes per second for one game (a bucket that fills over one second).
    private sealed class RateLimit(long bytesPerSecond)
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _tokens = bytesPerSecond;
        private double _at;

        public async Task Take(int bytes, CancellationToken stop)
        {
            while (true)
            {
                double wait;
                lock (this)
                {
                    double now = _clock.Elapsed.TotalSeconds;
                    _tokens = Math.Min(bytesPerSecond, _tokens + ((now - _at) * bytesPerSecond));
                    _at = now;
                    if (_tokens >= bytes || _tokens >= bytesPerSecond)
                    {
                        _tokens -= bytes;
                        return;
                    }

                    wait = (bytes - _tokens) / bytesPerSecond;
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0.001, wait)), stop).ConfigureAwait(false);
            }
        }
    }
}
