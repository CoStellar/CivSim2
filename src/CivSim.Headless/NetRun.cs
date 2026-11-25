using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using CivSim.Sim.Content;

namespace CivSim.Headless;

/// <summary>
/// The game over the network without a window (multiplayer plan, MP1):
/// <list type="bullet">
/// <item><c>--serve</c>: a server; players join with the game or with <c>--join</c>.</item>
/// <item><c>--join host:port</c>: a test client that joins, reads what the server sends and counts it.</item>
/// <item><c>--net-bench</c>: a server and bots on this computer over real TCP, played as fast as it goes; traffic and server time
/// per minute of the game.</item>
/// </list>
/// </summary>
public static class NetRun
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Usage of the three commands.</summary>
    public const string Usage = """
        --serve [--port 7777] [--seed N] [--civs 4] [--size 98x60] [--no-start-phase] [--stats-seconds 30] [--minutes N]
            A server without a window. Every people is led by the AI until a player joins; the game runs at x1.
            Stops after N minutes (default: never, Ctrl+C).
        --join host[:port] [--name Bot] [--seconds 60]
            A test client: joins, reads the views and prints what came. Exit code 0 when the server let it in.
        --net-bench [--civs 8] [--clients 8] [--minutes 45] [--seed 1] [--size 98x60] [--csv file]
            A server and bots over TCP on this computer, as fast as it goes; the AI plays for the bots, so their views grow
            as a player's would. Prints traffic per player and server time per minute of the game.
        """;

    /// <summary>Runs <c>--serve</c>.</summary>
    public static int Serve(string[] args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryWorld(args, 4, out PlayOptions? options, out string? error))
        {
            output.WriteLine(error);
            output.WriteLine(Usage);
            return 2;
        }

        int port = Int(args, "--port", TcpConnection.DefaultPort);
        int statsSeconds = Math.Max(1, Int(args, "--stats-seconds", 30));
        int minutes = Int(args, "--minutes", 0);
        using PlayHost host = PlayHost.Start(options! with { Dedicated = true });
        using var listener = new PlayListener(host, port);
        output.WriteLine(string.Create(Inv, $"CivSim {ProtocolInfo.GameVersion} server (protocol {ProtocolInfo.Version}): port {listener.Port}, seed {options!.Seed}, {options.Civilizations} peoples, map {options.LegacyWidth}x{options.LegacyHeight}"));
        output.WriteLine("Players join with the game or with: dotnet run --project src/CivSim.Headless -c Release -- --join <address>:" + listener.Port.ToString(Inv));
        using var stop = new ManualResetEventSlim();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Set();
        };

        var started = Stopwatch.StartNew();
        var last = new Dictionary<TcpConnection, long>();
        long lastSim = 0, lastView = 0;
        double lastAt = 0;
        while (!stop.Wait(TimeSpan.FromSeconds(statsSeconds)))
        {
            if (host.Error is { } failed)
            {
                output.WriteLine("The game stopped: " + failed);
                return 1;
            }

            double now = started.Elapsed.TotalSeconds;
            double span = now - lastAt;
            IReadOnlyList<TcpConnection> connections = listener.Connections;
            string traffic = string.Join(", ", connections.Select(c =>
            {
                long before = last.TryGetValue(c, out long b) ? b : 0;
                last[c] = c.BytesSent;
                return string.Create(Inv, $"{c.RemoteAddress} {(c.BytesSent - before) / span / 1024:F1} KB/s");
            }));
            double sim = (host.SimTime - lastSim) * 100.0 / Stopwatch.Frequency / span;
            double view = (host.ViewTime - lastView) * 100.0 / Stopwatch.Frequency / span;
            (lastSim, lastView, lastAt) = (host.SimTime, host.ViewTime, now);
            output.WriteLine(string.Create(Inv,
                $"[{TimeSpan.FromSeconds(now):hh\\:mm\\:ss}] game {Minutes(host.Ticks)}, players {host.Players}, connections {connections.Count}, refused {listener.Refused}; " +
                $"server: game {sim:F1}% + views {view:F1}% of one core, {Environment.WorkingSet / (1024 * 1024)} MB; {(traffic.Length == 0 ? "nobody connected" : traffic)}"));
            if (minutes > 0 && started.Elapsed.TotalMinutes >= minutes)
            {
                break;
            }
        }

        output.WriteLine("Server stopped.");
        return 0;
    }

    /// <summary>Runs <c>--join</c>.</summary>
    public static int Join(string[] args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        string target = Text(args, "--join") ?? "";
        if (!TryAddress(target, out string host, out int port))
        {
            output.WriteLine("--join needs an address: host or host:port.");
            return 2;
        }

        string name = Text(args, "--name") ?? "Bot";
        int seconds = Math.Max(1, Int(args, "--seconds", 60));
        TcpConnection connection;
        try
        {
            connection = TcpConnection.Connect(host, port, TimeSpan.FromSeconds(10));
        }
        catch (SocketException ex)
        {
            output.WriteLine($"Could not connect to {host}:{port}: {ex.Message}");
            return 1;
        }

        using (connection)
        {
            connection.Send(new HelloMessage(ProtocolInfo.Version, name));
            var watch = Stopwatch.StartNew();
            var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
            bool welcomed = false;
            long tick = -1;
            double nextReport = 10;
            while (connection.IsOpen && watch.Elapsed.TotalSeconds < seconds)
            {
                while (connection.TryReceive(out IProtocolMessage? m) && m is not null)
                {
                    string kind = m.GetType().Name;
                    kinds[kind] = kinds.GetValueOrDefault(kind) + 1;
                    switch (m)
                    {
                        case WelcomeMessage w:
                            welcomed = true;
                            output.WriteLine(string.Create(Inv, $"Joined {host}:{port} as {name}: people {w.CivId}."));
                            break;
                        case ConnectionRejectedMessage r:
                            output.WriteLine("The server turned us away: " + r.Reason);
                            return 1;
                        case PlayViewMessage v:
                            tick = v.Tick;
                            break;
                        default:
                            break;
                    }
                }

                if (watch.Elapsed.TotalSeconds >= nextReport)
                {
                    nextReport += 10;
                    output.WriteLine(string.Create(Inv, $"[{watch.Elapsed.TotalSeconds:F0} s] game {(tick < 0 ? "-" : Minutes(tick))}, received {connection.BytesReceived / 1024.0:F0} KB ({connection.BytesReceived / 1024.0 / watch.Elapsed.TotalSeconds:F1} KB/s)"));
                }

                Thread.Sleep(20);
            }

            if (connection.CloseReason is { } why)
            {
                output.WriteLine("Connection closed: " + why);
            }

            output.WriteLine(string.Create(Inv, $"Received {connection.BytesReceived / 1024.0:F0} KB in {watch.Elapsed.TotalSeconds:F0} s: ") +
                string.Join(", ", kinds.Select(k => string.Create(Inv, $"{k.Key} {k.Value}"))));
            return welcomed ? 0 : 1;
        }
    }

    /// <summary>Runs <c>--net-bench</c>.</summary>
    public static int Bench(string[] args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        if (!TryWorld(args, 8, out PlayOptions? options, out string? error))
        {
            output.WriteLine(error);
            output.WriteLine(Usage);
            return 2;
        }

        int clients = Math.Clamp(Int(args, "--clients", 8), 1, options!.Civilizations);
        int minutes = Math.Max(1, Int(args, "--minutes", 45));
        string? csv = Text(args, "--csv");
        using PlayHost host = PlayHost.Create(options with { Dedicated = true });
        host.AutosaveOn = false;
        host.Clock = () => host.Ticks * Stopwatch.Frequency / SimContent.TicksPerSecond; // full views four times a second of the game
        using var listener = new PlayListener(host, 0, IPAddress.Loopback, maxConnections: 64, maxPerAddress: 64);
        var bots = new List<Bot>();
        for (int i = 0; i < clients; i++)
        {
            var bot = new Bot(TcpConnection.Connect("127.0.0.1", listener.Port, TimeSpan.FromSeconds(10)));
            bot.Connection.Send(new HelloMessage(ProtocolInfo.Version, "Bot " + (i + 1).ToString(Inv)));
            bots.Add(bot);
        }

        var wait = Stopwatch.StartNew();
        while (bots.Exists(b => b.Civ < 0) && wait.Elapsed.TotalSeconds < 30)
        {
            host.ViewNow();
            Thread.Sleep(5);
        }

        if (bots.Exists(b => b.Civ < 0))
        {
            output.WriteLine("Not every bot was let in.");
            return 1;
        }

        foreach (Bot b in bots)
        {
            host.Game.Civs[b.Civ].IsAi = true; // the AI plays for the bot: its view grows like a player's
        }

        output.WriteLine(string.Create(Inv, $"Net bench: seed {options.Seed}, map {options.LegacyWidth}x{options.LegacyHeight}, {options.Civilizations} peoples, {clients} players over TCP, {minutes} minutes of the game"));
        output.WriteLine("minute | KB/s per player (avg / max) | unpacked KB/s | server ms per second of the game (game + views) | worst tick ms | skipped | MB");
        var rows = new List<string> { "minute,kb_per_s_avg,kb_per_s_max,raw_kb_per_s_avg,server_ms_per_game_s,sim_ms_per_game_s,view_ms_per_game_s,worst_tick_ms,skipped_views,working_set_mb,first_minute_kb_avg" };
        long[] lastSent = new long[clients];
        long[] lastRaw = new long[clients];
        long lastSim = 0, lastView = 0;
        double firstMinuteKb = 0;
        var wall = Stopwatch.StartNew();
        for (int minute = 1; minute <= minutes; minute++)
        {
            List<TcpConnection> server = [.. listener.Connections];
            double worst = 0;
            for (int t = 0; t < 60 * SimContent.TicksPerSecond; t++)
            {
                long before = Stopwatch.GetTimestamp();
                host.RunTicks(1, viewEvery: 1);
                worst = Math.Max(worst, Stopwatch.GetElapsedTime(before).TotalMilliseconds);
                while (server.Exists(c => c.Backlog > TcpConnection.CongestedFrames / 2))
                {
                    Thread.Yield(); // the bench runs many times faster than the game: let the bots read, as they would at x1
                }
            }

            WaitForDrain(listener);
            double[] kbs = new double[server.Count];
            double rawSum = 0;
            for (int i = 0; i < server.Count && i < clients; i++)
            {
                kbs[i] = (server[i].BytesSent - lastSent[i]) / 60.0 / 1024;
                rawSum += (server[i].RawBytesSent - lastRaw[i]) / 60.0 / 1024;
                lastSent[i] = server[i].BytesSent;
                lastRaw[i] = server[i].RawBytesSent;
            }

            double sim = (host.SimTime - lastSim) * 1000.0 / Stopwatch.Frequency / 60;
            double view = (host.ViewTime - lastView) * 1000.0 / Stopwatch.Frequency / 60;
            (lastSim, lastView) = (host.SimTime, host.ViewTime);
            if (minute == 1)
            {
                firstMinuteKb = kbs.Average() * 60;
            }

            long mb = Environment.WorkingSet / (1024 * 1024);
            rows.Add(string.Create(Inv, $"{minute},{kbs.Average():F2},{kbs.Max():F2},{rawSum / Math.Max(1, server.Count):F2},{sim + view:F1},{sim:F1},{view:F1},{worst:F1},{host.SkippedViews},{mb},{firstMinuteKb:F0}"));
            if (minute == 1 || minute % 5 == 0 || minute == minutes)
            {
                output.WriteLine(string.Create(Inv, $"{minute,6} | {kbs.Average(),8:F1} / {kbs.Max(),6:F1} | {rawSum / Math.Max(1, server.Count),13:F1} | {sim + view,7:F0} ({sim:F0} + {view:F0}) | {worst,6:F1} | {host.SkippedViews,7} | {mb}"));
            }

            if (host.Error is { } failed)
            {
                output.WriteLine("The game stopped: " + failed);
                return 1;
            }
        }

        output.WriteLine(string.Create(Inv, $"First minute (the whole map at the start): {firstMinuteKb:F0} KB per player. Wall time {wall.Elapsed.TotalSeconds:F0} s."));
        output.WriteLine(string.Create(Inv, $"Bots received: {string.Join(", ", bots.Select(b => $"{b.Connection.BytesReceived / 1024 / 1024.0:F1} MB"))}; closed: {bots.Count(b => !b.Connection.IsOpen)}."));
        if (csv is not null)
        {
            File.WriteAllLines(csv, rows);
            output.WriteLine("CSV: " + csv);
        }

        foreach (Bot b in bots)
        {
            b.Dispose();
        }

        return 0;
    }

    private static void WaitForDrain(PlayListener listener)
    {
        var wait = Stopwatch.StartNew();
        while (listener.Connections.Any(c => c.Backlog > 0) && wait.Elapsed.TotalSeconds < 10)
        {
            Thread.Sleep(1);
        }
    }

    private static string Minutes(long ticks) => string.Create(Inv, $"{ticks / SimContent.TicksPerSecond / 60}:{ticks / SimContent.TicksPerSecond % 60:00}");

    private static bool TryWorld(string[] args, int civs, out PlayOptions? options, out string? error)
    {
        options = null;
        error = null;
        int width = 98, height = 60;
        if (Text(args, "--size") is { } size)
        {
            string[] p = size.Split('x');
            if (p.Length != 2 || !int.TryParse(p[0], NumberStyles.None, Inv, out width) || !int.TryParse(p[1], NumberStyles.None, Inv, out height)
                || width < 20 || height < 12 || width > 400 || height > 240)
            {
                error = "--size must be WIDTHxHEIGHT, e.g. 98x60.";
                return false;
            }
        }

        int count = Int(args, "--civs", civs);
        if (count is < 2 or > 16)
        {
            error = "--civs must be 2 to 16.";
            return false;
        }

        ulong seed = ulong.TryParse(Text(args, "--seed"), NumberStyles.None, Inv, out ulong s) ? s : 1;
        options = new PlayOptions(seed, width, height, count) { StartPhase = !args.Contains("--no-start-phase") };
        return true;
    }

    private static bool TryAddress(string text, out string host, out int port)
    {
        host = text;
        port = TcpConnection.DefaultPort;
        int colon = text.LastIndexOf(':');
        if (colon > 0 && !text.EndsWith(']') && text.IndexOf(':') == colon)
        {
            host = text[..colon];
            if (!int.TryParse(text[(colon + 1)..], NumberStyles.None, Inv, out port) || port is < 1 or > 65535)
            {
                return false;
            }
        }

        return host.Length > 0;
    }

    private static string? Text(string[] args, string key)
    {
        int i = Array.IndexOf(args, key);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Int(string[] args, string key, int fallback) =>
        int.TryParse(Text(args, key), NumberStyles.AllowLeadingSign, Inv, out int v) ? v : fallback;

    // A client that reads everything on its own thread, as the game's window would.
    private sealed class Bot : IDisposable
    {
        private readonly Thread _thread;
        private volatile bool _stop;
        private volatile int _civ = -1;

        public Bot(TcpConnection connection)
        {
            Connection = connection;
            _thread = new Thread(Read) { IsBackground = true, Name = "CivSim bot" };
            _thread.Start();
        }

        public TcpConnection Connection { get; }

        public int Civ => _civ;

        public void Dispose()
        {
            _stop = true;
            _thread.Join(TimeSpan.FromSeconds(2));
            Connection.Dispose();
        }

        private void Read()
        {
            while (!_stop && Connection.IsOpen)
            {
                bool any = false;
                while (Connection.TryReceive(out IProtocolMessage? m))
                {
                    any = true;
                    if (m is WelcomeMessage w)
                    {
                        _civ = w.CivId;
                    }
                }

                if (!any)
                {
                    Thread.Sleep(1);
                }
            }
        }
    }
}
