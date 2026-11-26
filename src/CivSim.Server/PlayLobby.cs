using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Sim.Content;

namespace CivSim.Server;

/// <summary>What the host chose for a multiplayer game beyond the game's own options (MP3).</summary>
public sealed record LobbyConfig
{
    /// <summary>Name players see in the lobby.</summary>
    public string GameName { get; init; } = "CivSim game";

    /// <summary>Password players must give (empty = none). Not kept in saves.</summary>
    public string Password { get; init; } = "";
}

/// <summary>
/// The lobby of a multiplayer game (multiplayer plan, MP3): players join, see the settings (read-only), chat and say they are
/// ready; the host (the first player in, then the one there longest) starts the game. Then the lobby creates the
/// <see cref="PlayHost"/>, hands it every connection with its place (place n leads people n), and passes on later connections.
/// </summary>
public sealed class PlayLobby : IDisposable
{
    /// <summary>Lines of chat the lobby keeps.</summary>
    public const int ChatLines = 50;

    /// <summary>Longest nickname.</summary>
    public const int MaxNameLength = 24;

    /// <summary>Time a connection has to say who it is.</summary>
    public static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(15);

    private readonly PlayOptions _options;
    private readonly LobbyConfig _config;
    private readonly string? _dataDirectory;
    private readonly bool _threaded;
    private readonly ConcurrentQueue<IConnection> _pending = new();
    private readonly List<Member> _members = [];
    private readonly List<string> _chat = [];
    private readonly string[] _suffixes;
    private readonly string[] _settings;
    private readonly object _gate = new();
    private Thread? _thread;
    private PlayHost? _game;
    private volatile bool _stopping;
    private bool _dirty;

    private PlayLobby(PlayOptions options, LobbyConfig config, string? dataDirectory, bool threaded)
    {
        if (options.LoadPath is { Length: > 0 } load)
        {
            // A saved game (MP5): its world and its players' places; the host chose the places for players and the speed now.
            _save = PlaySaves.Read(load);
            options = _save.Options with
            {
                LoadPath = load,
                HumanSeats = options.HumanSeats > 0 ? Math.Max(options.HumanSeats, _save.Seats.Count) : Math.Max(_save.Options.HumanSeats, _save.Seats.Count),
                StartSpeed = options.StartSpeed > 0 ? options.StartSpeed : _save.Options.StartSpeed,
            };
            _places = _save.Seats.Count > 0 || _save.Options.Dedicated
                ? [.. _save.Seats]
                : [new SavedSeat(_save.Players.Length > 0 ? _save.Players[0] : 0, "", "", "", true)]; // saved alone: the host leads it again
        }

        _options = options with { Dedicated = true, HumanSeats = Math.Clamp(options.HumanSeats, 1, options.Civilizations) };
        _config = config;
        _dataDirectory = dataDirectory;
        _threaded = threaded;
        _suffixes = SimContent.Load(dataDirectory).NicknameSuffixes;
        _settings = Describe(_options, config);
        if (_save is not null)
        {
            _settings = [$"Saved game: {_save.Name}, minute {_save.Tick / SimContent.TicksPerMinute}, {_save.Era}", .. _settings];
            if (_save.Seats.Count > 0)
            {
                _settings = [.. _settings, "Places from the save: " + string.Join(", ", _save.Seats.Select(s => $"{s.Name} leads {s.CivName}"))];
            }
        }
    }

    // A saved game loaded into the lobby, and the players' places in it (MP5).
    private readonly SavedGame? _save;
    private readonly List<SavedSeat> _places = [];

    /// <summary>Opens the lobby with its own thread.</summary>
    public static PlayLobby Start(PlayOptions options, LobbyConfig config, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);
        var lobby = new PlayLobby(options, config, dataDirectory, threaded: true);
        lobby._thread = new Thread(lobby.Run) { IsBackground = true, Name = "CivSim lobby" };
        lobby._thread.Start();
        return lobby;
    }

    /// <summary>Opens the lobby without a thread: tests call <see cref="Pump"/>, and the game it starts runs without a thread too.</summary>
    public static PlayLobby Create(PlayOptions options, LobbyConfig config, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(config);
        return new PlayLobby(options, config, dataDirectory, threaded: false);
    }

    /// <summary>The game, once the host started it.</summary>
    public PlayHost? Game
    {
        get
        {
            lock (_gate)
            {
                return _game;
            }
        }
    }

    /// <summary>The options the game will start with.</summary>
    public PlayOptions Options => _options;

    /// <summary>Players in the lobby (any thread; for the server's log).</summary>
    public int Players => Volatile.Read(ref _players);

    private int _players;

    /// <summary>Takes a connection (from the network or <see cref="Connect"/>); after the start it goes to the game. Any thread.</summary>
    public void Accept(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        lock (_gate)
        {
            if (_game is not null)
            {
                _game.Accept(connection);
                return;
            }

            _pending.Enqueue(connection);
        }
    }

    /// <summary>An in-memory connection for the host's own window.</summary>
    public IConnection Connect()
    {
        (InMemoryConnection client, InMemoryConnection server) = InMemoryConnection.CreatePair();
        Accept(server);
        return client;
    }

    /// <summary>Stops the lobby (and its game).</summary>
    public void Dispose()
    {
        _stopping = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
        lock (_gate)
        {
            _game?.Dispose();
            foreach (Member m in _members)
            {
                m.Connection.Close();
            }
        }
    }

    /// <summary>Reads the players' messages and sends the lobby to everyone if it changed (the thread does it every 20 ms).</summary>
    public void Pump()
    {
        if (Game is not null)
        {
            return;
        }

        while (_pending.TryDequeue(out IConnection? c))
        {
            _members.Add(new Member(c, Stopwatch.GetTimestamp()));
        }

        foreach (Member m in _members.ToList())
        {
            while (Game is null && m.Connection.IsOpen && m.Connection.TryReceive(out IProtocolMessage? message))
            {
                Handle(m, message!);
            }

            if (m.Name is null && Stopwatch.GetElapsedTime(m.Since) > HelloTimeout)
            {
                m.Connection.Close();
            }
        }

        if (Game is not null)
        {
            return;
        }

        foreach (Member gone in _members.Where(m => !m.Connection.IsOpen && m.Name is not null).ToList())
        {
            Say($"{gone.Name} left.");
        }

        int before = _members.Count;
        _members.RemoveAll(m => !m.Connection.IsOpen);
        _dirty |= _members.Count != before;
        Volatile.Write(ref _players, _members.Count(m => m.Name is not null));
        if (_dirty)
        {
            _dirty = false;
            SendState();
        }
    }

    private void Run()
    {
        while (!_stopping && Game is null)
        {
            Pump();
            Thread.Sleep(20);
        }
    }

    private Member? Host => _members.Find(m => m.Name is not null && m.Connection.IsOpen);

    private void Handle(Member m, IProtocolMessage message)
    {
        switch (message)
        {
            case HelloMessage hello when m.Name is null:
                Join(m, hello);
                break;
            case LobbyReadyMessage ready when m.Name is not null:
                m.Ready = ready.Ready;
                _dirty = true;
                break;
            case LobbyChatMessage chat when m.Name is not null:
                string text = Nicknames.Clean(chat.Text, LobbyChatMessage.MaxLength);
                if (text.Length > 0 && m.MayChat(Stopwatch.GetTimestamp()))
                {
                    Say($"{m.Name}: {text}");
                }

                break;
            case LobbyStartMessage when m.Name is not null:
                if (m != Host)
                {
                    m.Connection.Send(new CommandRejectedMessage(0, "Only the host starts the game."));
                }
                else if (!Everyone())
                {
                    m.Connection.Send(new CommandRejectedMessage(0, "Not every player is ready."));
                }
                else
                {
                    StartGame();
                }

                break;
            default:
                break;
        }
    }

    private void Join(Member m, HelloMessage hello)
    {
        string? refused = hello.ProtocolVersion != ProtocolInfo.Version
            ? $"This game speaks protocol {ProtocolInfo.Version}; the client speaks {hello.ProtocolVersion}."
            : hello.GameVersion != ProtocolInfo.GameVersion
                ? $"This game is version {ProtocolInfo.GameVersion}; yours is {(hello.GameVersion is { Length: > 0 } v ? v : "older")}. Both need the same version."
                : !PlayHost.SamePassword(hello.Password ?? "", _config.Password)
                    ? (hello.Password is { Length: > 0 } ? "Wrong password." : "This game has a password.")
                    : _members.Count(x => x.Name is not null) >= _options.HumanSeats
                        ? "The game is full."
                        : null;
        if (refused is not null)
        {
            m.Connection.Send(new ConnectionRejectedMessage(refused));
            m.Connection.Close();
            return;
        }

        (m.Name, m.SuffixLength) = Nicknames.Unique(hello.PlayerName, _members.Where(x => x.Name is not null).Select(x => x.Name!), _suffixes);
        Say($"{m.Name} joined.");
        if (_save is not null)
        {
            // Back on the place of the save: by the return key, then by the nickname; the host of the lobby takes the host's place (MP5).
            List<SavedSeat> free = [.. _places.Where(p => !_members.Exists(x => x != m && x.Place == p))];
            SavedSeat? place = free.Find(p => p.ReturnKey.Length > 0 && p.ReturnKey == hello.ReturnKey)
                ?? free.Find(p => p.Name.Length > 0 && string.Equals(p.Name, hello.PlayerName?.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? (m == Host ? free.Find(p => p.Host) : null);
            m.Place = place;
            Say(place is not null
                ? $"{m.Name} takes their place again: {(place.CivName.Length > 0 ? place.CivName : "the civilization of the save")}."
                : $"{m.Name} was not in the saved game: in it they ask the players for a civilization the AI leads.");
        }
    }

    private bool Everyone() => _members.Where(x => x.Name is not null && x != Host).All(x => x.Ready);

    private void Say(string line)
    {
        _chat.Add(line);
        if (_chat.Count > ChatLines)
        {
            _chat.RemoveRange(0, _chat.Count - ChatLines);
        }

        _dirty = true;
    }

    private void SendState()
    {
        Member? host = Host;
        List<Member> seated = [.. _members.Where(x => x.Name is not null)];
        LobbySeat[] seats = [.. seated.Select(x => new LobbySeat(x.Name!, x == host || x.Ready, x == host, x.SuffixLength))];
        bool everyone = Everyone();
        for (int i = 0; i < seated.Count; i++)
        {
            seated[i].Connection.Send(new LobbyStateMessage(_config.GameName, seats, _options.HumanSeats, _settings, [.. _chat], i, seated[i] == host && everyone));
        }
    }

    private void StartGame()
    {
        List<Member> seated = [.. _members.Where(x => x.Name is not null && x.Connection.IsOpen)];
        PlayHost game = _threaded ? PlayHost.Start(_options, _dataDirectory) : PlayHost.Create(_options, _dataDirectory);
        game.Password = _config.Password;
        game.ExpectPlayers(seated.Count, TimeSpan.FromSeconds(10));
        lock (_gate)
        {
            for (int i = 0; i < seated.Count; i++)
            {
                seated[i].Connection.Send(new GameStartingMessage());
                // Place n leads civilization n; a saved game puts everyone back on their place (MP5), the others take a free one.
                int civ = _save is null ? i : seated[i].Place?.Civ ?? -1;
                game.Accept(seated[i].Connection, civ: civ, name: seated[i].Name, suffixLength: seated[i].SuffixLength, host: seated[i] == Host);
            }

            while (_pending.TryDequeue(out IConnection? late))
            {
                game.Accept(late);
            }

            _members.Clear();
            _game = game;
        }
    }

    /// <summary>The settings as lines for the lobby (read-only for the players).</summary>
    public static string[] Describe(PlayOptions o, LobbyConfig config)
    {
        ArgumentNullException.ThrowIfNull(o);
        ArgumentNullException.ThrowIfNull(config);
        CultureInfo inv = CultureInfo.InvariantCulture;
        string world = o.WorldType switch { 1 => "pangaea", 2 => "archipelago", _ => "continents" };
        string goal = o.Goal switch { 0 => "the last civilization standing wins", 2 => "the best score after the time wins", _ => "the first civilization in the Middle Ages wins" };
        var lines = new List<string>
        {
            string.Create(inv, $"Civilizations: {o.Civilizations}, places for players: {o.HumanSeats} (the AI leads the others)"),
            string.Create(inv, $"Map: {o.LegacyWidth} x {o.LegacyHeight}, {world}, sea {o.WaterPercent}%, seed {o.Seed}"),
            "Goal: " + goal,
            o.StartSpeed == 2 ? "Speed: x2 (harder: less time for every decision)" : "Speed: x1",
            "Ores: " + (o.FairOres ? "fair (every start has its ores nearby)" : "as the world made them"),
            string.Create(inv, $"Pauses: {PlayHost.PausesPerPlayer} per player; choosing the start: 30 s each"),
            "Password: " + (config.Password.Length > 0 ? "yes" : "no"),
        };
        if (o.World is { IsDefault: false } w)
        {
            lines.Add($"World: planet {w.Planet}, rain {w.Moisture ?? "as the planet"}, inland {w.Inland}, relief {w.Relief ?? "as the planet"}, deposits {w.Resources}"
                + (w.HasNumbers ? ", own planet and star numbers" : ""));
        }

        return [.. lines];
    }

    private sealed class Member(IConnection connection, long since)
    {
        private double _tokens = 5;
        private long _refilled = since;

        public IConnection Connection { get; } = connection;

        public long Since { get; } = since;

        public string? Name { get; set; }

        public bool Ready { get; set; }

        // Its place in a saved game (MP5).
        public SavedSeat? Place { get; set; }

        // Characters the lobby added to a repeated nickname (with the space before them).
        public int SuffixLength { get; set; }

        // One line a second, five at once.
        public bool MayChat(long now)
        {
            _tokens = Math.Min(5, _tokens + ((now - _refilled) / (double)Stopwatch.Frequency));
            _refilled = now;
            if (_tokens < 1)
            {
                return false;
            }

            _tokens--;
            return true;
        }
    }
}
