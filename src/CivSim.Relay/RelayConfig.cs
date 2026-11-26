using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CivSim.Protocol.Transport;

namespace CivSim.Relay;

/// <summary>
/// Settings of the relay (multiplayer plan, chapter 4): from a JSON file (<c>--config relay.json</c>), then environment variables
/// (<c>CIVSIM_RELAY_PORT</c>...), then the command line (<c>--port 7700</c>...); a later source wins. Names are the same everywhere:
/// <c>maxRooms</c> in the file, <c>CIVSIM_RELAY_MAXROOMS</c> in the environment, <c>--max-rooms</c> on the command line.
/// </summary>
public sealed class RelayConfig
{
    /// <summary>TCP port players and game servers connect to.</summary>
    public int Port { get; set; } = RelayWire.DefaultPort;

    /// <summary>Games open at once.</summary>
    public int MaxRooms { get; set; } = 32;

    /// <summary>Players in one game (and those still waiting to get in).</summary>
    public int MaxPlayersPerRoom { get; set; } = 16;

    /// <summary>Connections open at once, all together.</summary>
    public int MaxConnections { get; set; } = 400;

    /// <summary>Connections open at once from one address (a game server with its players' connections counts each one).</summary>
    public int MaxPerAddress { get; set; } = 24;

    /// <summary>Bytes per second passed for one game, both ways together, in KB (8 players take about 65 KB/s; the start of a game up to 1 MB).</summary>
    public int RoomKBps { get; set; } = 2048;

    /// <summary>A connection that passed nothing for this long is closed (the game sends views ten times a second).</summary>
    public int IdleMinutes { get; set; } = 5;

    /// <summary>Minutes a closed game's code stays kept for its server, so it gets the same code back after a lost connection.</summary>
    public int ReserveMinutes { get; set; } = 10;

    /// <summary>
    /// File with passwords to open games (one per line: <c>password</c> or <c>password 2026-12-31</c>, the last day it works); read
    /// again when it changes, without a restart. Empty: anyone may open games on this relay.
    /// </summary>
    public string PasswordsFile { get; set; } = "";

    /// <summary>Days a password still works after its last day, so nobody is cut off in the middle of a week of games.</summary>
    public int GraceDays { get; set; } = 7;

    /// <summary>TLS certificate (PEM, e.g. Let's Encrypt <c>fullchain.pem</c>); empty: no TLS (players then connect by IP address).</summary>
    public string CertificateFile { get; set; } = "";

    /// <summary>Private key of the certificate (PEM, e.g. <c>privkey.pem</c>).</summary>
    public string KeyFile { get; set; } = "";

    /// <summary>Minutes between lines with the relay's numbers in the log.</summary>
    public int StatsMinutes { get; set; } = 5;

    private static readonly (string Name, Action<RelayConfig, string> Set)[] Settings =
    [
        ("port", (c, v) => c.Port = Int(v, 1, 65535)),
        ("maxRooms", (c, v) => c.MaxRooms = Int(v, 1, 10000)),
        ("maxPlayersPerRoom", (c, v) => c.MaxPlayersPerRoom = Int(v, 1, 64)),
        ("maxConnections", (c, v) => c.MaxConnections = Int(v, 2, 100000)),
        ("maxPerAddress", (c, v) => c.MaxPerAddress = Int(v, 1, 10000)),
        ("roomKBps", (c, v) => c.RoomKBps = Int(v, 16, 1_000_000)),
        ("idleMinutes", (c, v) => c.IdleMinutes = Int(v, 1, 1440)),
        ("reserveMinutes", (c, v) => c.ReserveMinutes = Int(v, 0, 1440)),
        ("passwordsFile", (c, v) => c.PasswordsFile = v),
        ("graceDays", (c, v) => c.GraceDays = Int(v, 0, 365)),
        ("certificateFile", (c, v) => c.CertificateFile = v),
        ("keyFile", (c, v) => c.KeyFile = v),
        ("statsMinutes", (c, v) => c.StatsMinutes = Int(v, 1, 1440)),
    ];

    /// <summary>Help for the command line.</summary>
    public static string Usage => """
        CivSim relay: passes bytes between game servers and players; nobody opens a port (docs/multiplayer/vps.md).
          --config relay.json   settings from a file (see relay.example.json)
          --port 7700  --max-rooms 32  --max-players-per-room 16  --max-connections 400  --max-per-address 24
          --room-kbps 2048  --idle-minutes 5  --reserve-minutes 10  --stats-minutes 5
          --passwords-file passwords.txt  --grace-days 7     (no file: anyone may open games)
          --certificate-file fullchain.pem  --key-file privkey.pem     (TLS for players who type the relay's name)
        Every setting also comes from the environment: CIVSIM_RELAY_PORT, CIVSIM_RELAY_PASSWORDSFILE...
        """;

    /// <summary>Reads the settings; throws <see cref="ArgumentException"/> with a reason for a wrong one.</summary>
    public static RelayConfig Load(string[] args, IDictionary<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);
        var config = new RelayConfig();
        int file = Array.IndexOf(args, "--config");
        if (file >= 0)
        {
            if (file + 1 >= args.Length || !File.Exists(args[file + 1]))
            {
                throw new ArgumentException("--config needs an existing file.");
            }

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(args[file + 1]), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            foreach (JsonProperty p in doc.RootElement.EnumerateObject())
            {
                string value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.GetRawText();
                Set(config, p.Name, value, $"{p.Name} in {args[file + 1]}");
            }
        }

        foreach ((string name, _) in Settings)
        {
            if (environment.TryGetValue("CIVSIM_RELAY_" + name.ToUpperInvariant(), out string? value) && value is not null)
            {
                Set(config, name, value, "CIVSIM_RELAY_" + name.ToUpperInvariant());
            }
        }

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--config")
            {
                i++;
                continue;
            }

            string name = args[i].StartsWith("--", StringComparison.Ordinal) ? Camel(args[i][2..]) : "";
            if (name.Length == 0 || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Unknown argument {args[i]}.");
            }

            Set(config, name, args[++i], args[i - 1]);
        }

        if ((config.CertificateFile.Length == 0) != (config.KeyFile.Length == 0))
        {
            throw new ArgumentException("TLS needs both certificateFile and keyFile.");
        }

        return config;
    }

    private static void Set(RelayConfig config, string name, string value, string where)
    {
        foreach ((string n, Action<RelayConfig, string> set) in Settings)
        {
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    set(config, value.Trim());
                }
                catch (FormatException)
                {
                    throw new ArgumentException($"{where}: \"{value}\" is not a number in the allowed range.");
                }

                return;
            }
        }

        throw new ArgumentException($"{where}: unknown setting {name}.");
    }

    private static int Int(string text, int min, int max) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int v) && v >= min && v <= max ? v : throw new FormatException();

    // max-rooms → maxRooms
    private static string Camel(string dashed)
    {
        string[] parts = dashed.Split('-');
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i][1..];
            }
        }

        return string.Concat(parts);
    }
}
