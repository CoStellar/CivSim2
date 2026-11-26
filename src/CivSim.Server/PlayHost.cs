using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CivSim.Core.Map;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Server;

/// <summary>Options of a new game of the rebuilt simulation (ADR 0028).</summary>
public sealed record PlayOptions(ulong Seed, int LegacyWidth = 98, int LegacyHeight = 60, int Civilizations = 4)
{
    /// <summary>Civilizations choose where they start (the player too); false starts at once on the generator's places.</summary>
    public bool StartPhase { get; init; } = true;

    /// <summary>Test scenario to start from (file name in data/sim/scenarios without .json; null = a normal game).</summary>
    public string? Scenario { get; init; }

    /// <summary>What wins the game: 0 last standing, 1 first to the Middle Ages, 2 best score after 2 hours (protocol only, no core types).</summary>
    public int Goal { get; init; } = 1;

    /// <summary>World type: 0 continents, 1 one big continent, 2 islands.</summary>
    public int WorldType { get; init; }

    /// <summary>Share of the planet under water, percent.</summary>
    public int WaterPercent { get; init; } = 60;

    /// <summary>A save to load (the other options then come from the save).</summary>
    public string? LoadPath { get; init; }

    /// <summary>E6 (2026-11-20): the tutorial's task list shows on screen (its progress is kept either way).</summary>
    public bool Tutorial { get; init; }

    /// <summary>Ores (2026-11-24): true fair (copper, tin and iron near every start), false realistic (where the world put them).</summary>
    public bool FairOres { get; init; } = true;

    /// <summary>Advanced world settings (PR W1, 2026-11-25); null = a normal world.</summary>
    public PlayWorld? World { get; init; }

    /// <summary>
    /// A server without a window (multiplayer, MP1): every civilization is led by the AI until a player joins, the start is chosen
    /// with the time limit, and the game runs at normal speed from the start.
    /// </summary>
    public bool Dedicated { get; init; }

    /// <summary>Places for players in a multiplayer game (0 = any free people); the AI leads the rest (MP3).</summary>
    public int HumanSeats { get; init; }

    /// <summary>Speed when the game begins: 1 = x1, 2 = x2; 0 = paused (alone) or x1 (server without a window) (MP3).</summary>
    public int StartSpeed { get; init; }
}

/// <summary>
/// Runs a game of the new simulation on its own thread and talks to clients through the protocol only (the client never sees
/// <c>CivSim.Sim</c>). Ten ticks a second at ×1. The player leads civilization 0.
/// </summary>
public sealed partial class PlayHost : IDisposable
{
    private static readonly TimeSpan ViewInterval = TimeSpan.FromMilliseconds(100);
    private readonly ConcurrentQueue<Pending> _pending = new();
    private readonly List<PlayClient> _clients = [];
    private readonly Thread _thread;
    private readonly SimGame _game;
    private readonly List<int> _dirty = [];
    private readonly List<(int Civ, string Text)> _news = []; // Civ < 0: news for everyone
    private volatile bool _stopping;
    private volatile string? _error;
    private SimulationSpeed _speed = SimulationSpeed.Paused;
    private PlayOptions _options;
    private readonly List<SavedCommand> _log = [];
    private SavedGame? _replay;
    private long _lastAutosave;

    private PlayHost(SimGame game, PlayOptions options)
    {
        _game = game;
        _options = options with { LoadPath = null };
        _thread = new Thread(Run) { IsBackground = true, Name = "CivSim play" };
    }

    /// <summary>The error that stopped the game thread, if any.</summary>
    public string? Error => _error;

    /// <summary>Creates the game and starts its thread (paused).</summary>
    public static PlayHost Start(PlayOptions options, string? dataDirectory = null)
    {
        PlayHost host = Create(options, dataDirectory);
        host._thread.Start();
        return host;
    }

    /// <summary>
    /// Creates the game without starting its thread: tests and the test bot drive it with <see cref="RunTicks"/>, with no clock
    /// (tests 2026-11-20: a public way into the server without a thread).
    /// </summary>
    public static PlayHost Create(PlayOptions options, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        SimContent content = SimContent.Load(dataDirectory);
        SavedGame? save = null;
        if (options.LoadPath is { Length: > 0 } load)
        {
            // A save: the same game again; its commands are played again on the game thread before anything else.
            save = PlaySaves.Read(load);

            // A save loaded into a lobby (MP5) keeps the places and the speed the host chose now; the world is the saved one.
            options = save.Options with
            {
                Dedicated = save.Options.Dedicated || options.Dedicated,
                HumanSeats = options.HumanSeats > 0 ? options.HumanSeats : save.Options.HumanSeats,
                StartSpeed = options.StartSpeed > 0 ? options.StartSpeed : save.Options.StartSpeed,
            };
        }

        SimGame game;
        if (options.Scenario is { Length: > 0 } key)
        {
            // A test scenario: the game starts at the moment of development it describes.
            string path = System.IO.Path.Combine(SimScenario.Folder(dataDirectory), key + ".json");
            game = SimGame.FromScenario(content, SimScenario.Load(path));
        }
        else
        {
            game = SimGame.Create(content, new SimSettings(options.Seed, options.LegacyWidth, options.LegacyHeight, options.Civilizations)
            {
                StartPhase = options.StartPhase,
                Goal = (GameGoal)Math.Clamp(options.Goal, 0, 2),
                WorldType = (CivSim.Core.WorldGen.WorldType)Math.Clamp(options.WorldType, 0, 2),
                WaterPercent = Math.Clamp(options.WaterPercent, 30, 80),
                FairOres = options.FairOres,
            }.WithWorld(options.World, content));
        }

        if (!options.Dedicated)
        {
            game.Civs[0].IsAi = false; // the player leads the first people; the AI plays the others
            game.PickWaitsForPlayers = true; // E13 (2026-11-20): alone, the player chooses the start without a time limit
        }

        game.RestartPickClock();
        return new PlayHost(game, options)
        {
            _replay = save,
            _speed = options.StartSpeed is 1 or 2 ? (SimulationSpeed)options.StartSpeed : options.Dedicated ? SimulationSpeed.X1 : SimulationSpeed.Paused,
            _startPlayers = [.. game.Civs.Where(c => !c.IsAi).Select(c => c.Id)],
        };
    }

    /// <summary>The game itself, for tests in this solution only.</summary>
    internal SimGame Game => _game;

    /// <summary>Least time between two full views of a client (tests driving RunTicks set it to zero).</summary>
    internal TimeSpan FullViewEvery { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Builds the view of every civilization (what an observer would get), for determinism tests.</summary>
    internal void ViewEveryPeople()
    {
        for (int c = 0; c < _game.Civs.Count; c++)
        {
            _ = View(c);
        }
    }

    /// <summary>Clock of the full views (Stopwatch ticks); the network measurement drives it with the game's ticks.</summary>
    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>Time spent on the game's ticks, in Stopwatch ticks (for the server's measurements; any thread).</summary>
    public long SimTime => Interlocked.Read(ref _simTime);

    /// <summary>Time spent on the players' views, in Stopwatch ticks (any thread).</summary>
    public long ViewTime => Interlocked.Read(ref _viewTime);

    /// <summary>Connected clients that lead a civilization (any thread).</summary>
    public int Players => Volatile.Read(ref _players);

    /// <summary>Rounds of views not sent to a client because its connection was congested (any thread).</summary>
    public long SkippedViews => Interlocked.Read(ref _skippedViews);

    /// <summary>
    /// Password of the game (multiplayer, MP2): a player must give it in Hello. Empty = none. Set it before players join; it is not
    /// kept in saves.
    /// </summary>
    public string Password { get; set; } = "";

    /// <summary>
    /// Holds the game still until <paramref name="players"/> players have joined, for at most <paramref name="wait"/>: when a lobby
    /// starts the game, its players join a moment later and the AI should not choose their start meanwhile (MP3). Any thread.
    /// </summary>
    public void ExpectPlayers(int players, TimeSpan wait)
    {
        Interlocked.Exchange(ref _expectUntil, Stopwatch.GetTimestamp() + (long)(wait.TotalSeconds * Stopwatch.Frequency));
        Volatile.Write(ref _expect, players);
    }

    private int _expect;
    private long _expectUntil;

    /// <summary>Messages of clients that failed on the server (the message was dropped, the game went on; any thread).</summary>
    public long ClientFaults => Interlocked.Read(ref _clientFaults);

    /// <summary>Orders and requests a player may send in a second, on average (multiplayer plan, chapter 9).</summary>
    public const int MessagesPerSecond = 30;

    /// <summary>Orders and requests a player may send at once (clicking fast, a drawn path).</summary>
    public const int MessageBurst = 90;

    /// <summary>Messages refused for coming too fast after which the server disconnects the client.</summary>
    public const int FloodLimit = 600;

    /// <summary>Messages that failed on the server after which it disconnects the client.</summary>
    public const int FaultLimit = 3;

    private long _clientFaults;
    private long _steps;
    private int[] _startPlayers = [];
    private int _format = 2;
    private readonly Dictionary<string, int> _returnKeys = new(StringComparer.Ordinal);
    private long _simTime;
    private long _viewTime;
    private long _skippedViews;
    private int _players;

    /// <summary>Hash of the whole game state (all civilizations).</summary>
    public ulong StateHash => _game.StateHash();

    /// <summary>Ticks played.</summary>
    public long Ticks => _game.Ticks;

    /// <summary>True while civilizations choose their start.</summary>
    public bool Picking => _game.Picking;

    /// <summary>
    /// Without the thread: loads the save (once), then plays <paramref name="ticks"/> ticks, reading the clients' messages before each
    /// and sending views every <paramref name="viewEvery"/> ticks (0 = never). The speed asked for by clients does not matter here.
    /// </summary>
    public void RunTicks(int ticks, int viewEvery = 1)
    {
        if (_thread.IsAlive)
        {
            throw new InvalidOperationException("The host runs on its own thread.");
        }

        LoadSave();
        for (int t = 0; t < ticks; t++)
        {
            Pump();
            Advance();
            if (viewEvery > 0 && t % viewEvery == 0)
            {
                SendViews();
            }
        }

        Pump();
    }

    /// <summary>Without the thread: reads the clients' messages and sends them views now.</summary>
    public void ViewNow()
    {
        LoadSave();
        Pump();
        SendViews();
    }

    private void LoadSave()
    {
        if (_replay is { } replay)
        {
            _replay = null;
            Replay(replay);
        }
    }

    private void Pump()
    {
        while (_pending.TryDequeue(out Pending c))
        {
            _clients.Add(new PlayClient(c.Connection, _game.Geo.CellCount) { Place = c.Civ, Name = c.Name, SuffixLength = c.SuffixLength, Host = c.Host });
        }

        foreach (PlayClient client in _clients)
        {
            Receive(client);
        }

        // A player who leaves hands the civilization back to the AI (abuse tests 2026-11-24: it stood still for ever); the save
        // records it, so a loaded game is the same game (MP2).
        foreach (PlayClient gone in _clients)
        {
            if (!gone.Connection.IsOpen && gone.Civ >= 0 && gone.Civ < _game.Civs.Count && !_game.Civs[gone.Civ].Dead)
            {
                SetLeader(gone.Civ, player: false);
                if (_clients.Exists(c => c != gone && c.Civ >= 0 && c.Connection.IsOpen))
                {
                    Announce($"{gone.Name} left; the AI leads {_game.Civs[gone.Civ].Name} until a player takes it.");
                }

                _playersDirty = true;
            }
        }

        _clients.RemoveAll(c => !c.Connection.IsOpen);
        int players = _clients.Count(c => c.Civ >= 0);
        Volatile.Write(ref _players, players);
        if (players > 1 && _speed > SimulationSpeed.X2)
        {
            _speed = SimulationSpeed.X2; // with other players: x1 or x2 (multiplayer plan, chapter 11)
        }

        Social();
        Spectators();
    }

    private void Advance()
    {
        long started = Stopwatch.GetTimestamp();
        Step();
        Interlocked.Add(ref _simTime, Stopwatch.GetTimestamp() - started);
        SlowForBattle();
        Autosave();
        foreach ((int civ, string text) in _game.News)
        {
            _news.Add((civ, text)); // each player gets the news of the whole world and of the own people (MP2)
        }
    }

    // One step of the game; steps count also while civilizations choose their start, when ticks stand still (saves, format 2).
    private void Step()
    {
        _game.Tick();
        _steps++;
    }

    // Who leads a civilization from now on; the save keeps it with the step, so a loaded game is the same game (MP2).
    private void SetLeader(int civ, bool player)
    {
        if (_game.Civs[civ].IsAi != player)
        {
            return;
        }

        Lead(civ, player);
        if (_format >= 2)
        {
            _log.Add(SavedCommand.LeaderChange(_game.Ticks, _steps, civ, player));
        }
    }

    private void Lead(int civ, bool player)
    {
        _game.Civs[civ].IsAi = !player;
        if (_game.Picking && _game.PickOrder[_game.PickIndex] == civ)
        {
            _game.RestartPickClock(); // a player who takes over the people choosing now gets the player's time (MP3)
        }

        // Alone, a player chooses the start without a time limit (E13); with other players everyone has the same limit (MP2).
        _game.PickWaitsForPlayers = !_options.Dedicated && _game.Civs.Count(c => !c.IsAi) <= 1;
    }

    /// <summary>Opens an in-memory connection for a local client.</summary>
    public IConnection Connect()
    {
        (InMemoryConnection client, InMemoryConnection server) = InMemoryConnection.CreatePair();
        _pending.Enqueue(new Pending(server, -1, null, 0, false));
        return client;
    }

    /// <summary>
    /// Takes the server end of a connection from elsewhere (a player over the network); any thread. <paramref name="civ"/>: the people
    /// this player leads after Hello (a lobby's place), or −1 for the next free one.
    /// </summary>
    public void Accept(IConnection connection, int civ = -1, string? name = null, int suffixLength = 0, bool host = false)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _pending.Enqueue(new Pending(connection, civ, name, suffixLength, host));
    }

    // A connection on its way into the game: the place, the nickname and whether it is the host (from the lobby, MP3–MP4).
    private readonly record struct Pending(IConnection Connection, int Civ, string? Name, int SuffixLength, bool Host);

    /// <summary>Stops the game thread.</summary>
    public void Dispose()
    {
        _stopping = true;
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
        }
    }

    private void Run()
    {
        try
        {
            LoadSave();
            long lastTick = Stopwatch.GetTimestamp();
            long lastView = 0;
            while (!_stopping)
            {
                Pump();
                if (_players < Volatile.Read(ref _expect) && Stopwatch.GetTimestamp() < Interlocked.Read(ref _expectUntil))
                {
                    lastTick = Stopwatch.GetTimestamp(); // the lobby's players are still on their way
                    Thread.Sleep(2);
                    continue;
                }

                int ticksPerSecond = (int)_speed * SimContent.TicksPerSecond;
                if (_speed == SimulationSpeed.Max)
                {
                    ticksPerSecond = 400;
                }

                // Choosing the start goes in real time whatever the speed: 30 seconds are 30 seconds.
                if (_game.Picking)
                {
                    ticksPerSecond = SimContent.TicksPerSecond;
                }

                if (ticksPerSecond > 0)
                {
                    double wait = 1.0 / ticksPerSecond;
                    int ran = 0;
                    while (Stopwatch.GetElapsedTime(lastTick).TotalSeconds >= wait && ran < 50)
                    {
                        Advance();
                        lastTick += (long)(wait * Stopwatch.Frequency);
                        ran++;
                    }

                    if (ran == 50)
                    {
                        lastTick = Stopwatch.GetTimestamp();
                    }
                }
                else
                {
                    lastTick = Stopwatch.GetTimestamp();
                }

                if (Stopwatch.GetElapsedTime(lastView) >= ViewInterval)
                {
                    lastView = Stopwatch.GetTimestamp();
                    SendViews();
                }

                Thread.Sleep(2);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or NullReferenceException or KeyNotFoundException)
        {
            _error = ex.ToString();
        }
    }

    private void Receive(PlayClient client)
    {
        while (client.Connection.IsOpen && client.Connection.TryReceive(out IProtocolMessage? message))
        {
            if (message is not HelloMessage && !client.Allow(Clock()))
            {
                // Too many orders at once (multiplayer plan, chapter 9): refused; a client that keeps it up is disconnected.
                if (client.Flooded == 1 || client.Flooded % 100 == 0)
                {
                    client.Connection.Send(new CommandRejectedMessage(0, $"Too many orders: at most {MessagesPerSecond} a second."));
                }

                if (client.Flooded >= FloodLimit)
                {
                    Drop(client, "Too many orders: disconnected.");
                }

                continue;
            }

            try
            {
                Handle(client, message!);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException or NullReferenceException
                or KeyNotFoundException or FormatException or OverflowException)
            {
                // One player's bad message never stops the game for everyone (multiplayer plan, chapter 9).
                Interlocked.Increment(ref _clientFaults);
                client.Faults++;
                client.Connection.Send(new CommandRejectedMessage(message is PlayCommandMessage c ? c.Sequence : 0, "The server could not carry out this order."));
                if (client.Faults >= FaultLimit)
                {
                    Drop(client, "Too many orders the server could not carry out: disconnected.");
                }
            }
        }
    }

    private static void Drop(PlayClient client, string reason)
    {
        client.Connection.Send(new ConnectionRejectedMessage(reason));
        client.Connection.Close();
    }

    /// <summary>Called before each client message is handled (tests: a message that fails on the server).</summary>
    internal Action<IProtocolMessage>? BeforeHandle { get; set; }

    private void Handle(PlayClient client, IProtocolMessage message)
    {
        BeforeHandle?.Invoke(message);
        switch (message)
        {
            case HelloMessage hello:
                Hello(client, hello);
                break;
            case SetSpeedMessage speed when client.Civ >= 0:
                string? refused = SetSpeed(client, speed.Speed);
                if (refused is not null)
                {
                    client.Connection.Send(new CommandRejectedMessage(0, refused));
                }

                break;
            case PlayCommandMessage command when client.Civ >= 0:
                // E6 (2026-11-19 tests): debug keys of a test scenario only when one player plays (MP2: never with others).
                string? why = command.Kind == PlayCommandKind.Debug && (_options.Dedicated || _clients.Count(c => c.Civ >= 0) > 1)
                    ? "Debug keys work only in a game with one player."
                    : command.Kind == PlayCommandKind.Gather
                        ? "Gathering is always on: people without work gather around their settlement."
                        : _game.Apply(ToCommand(client.Civ, command));
                if (why is not null)
                {
                    client.Connection.Send(new CommandRejectedMessage(command.Sequence, why));
                }
                else
                {
                    client.ViewSoon = true;
                    _log.Add(SavedCommand.Of(_game.Ticks, _steps, client.Civ, command)); // the save plays it again at this step
                    TallyOrder(client.Civ, command); // a summary for observers (3a)
                }

                break;
            case SetTutorialMessage tutorial when client.Civ >= 0:
                _options = _options with { Tutorial = tutorial.On }; // kept in the next saves
                break;
            case SaveGameRequestMessage when client.Civ >= 0 && !client.Host && _clients.Count(c => c.Civ >= 0 && c.Connection.IsOpen) > 1:
                client.Connection.Send(new SaveGameResultMessage(false, "In a game with other players the host saves it."));
                break;
            case SaveGameRequestMessage save when client.Civ >= 0:
                client.Connection.Send(Save(save.Name));
                break;
            case PlaySummaryRequestMessage when client.Civ >= 0:
                client.Connection.Send(Summary(_game.Civs[client.Civ]));
                break;
            case PlacesRequestMessage places when client.Civ >= 0:
                client.Connection.Send(Places(client.Civ, places.Type));
                break;
            case PlayPingMessage ping when client.Role >= 0:
                Pong(client, ping);
                break;
            case PlayChatMessage chat when client.Role >= 0:
                Chat(client, chat);
                break;
            case PlayVoteMessage vote when client.Civ >= 0:
                Cast(client, vote);
                break;
            case PlayJoinMessage join when client.Role == PlayPlayerInfo.Candidate:
                Reject(client, AskToLead(client, join.Civ));
                break;
            case PlayWatchMessage when client.Role == PlayPlayerInfo.Candidate:
                Watch(client);
                break;
            case PlayKickMessage kick when client.Civ >= 0:
                Reject(client, Kick(client, kick.Name));
                break;
            case PlayEyeMessage eye when Spectating(client):
                Eye(client, eye.Civ);
                break;
            default:
                break;
        }
    }

    private void Hello(PlayClient client, HelloMessage hello)
    {
        // Abuse tests 2026-11-24: a second Hello took the next civilization (and left the first with nobody, AI off),
        // and a client of another protocol version was let in.
        if (client.Role >= 0)
        {
            return;
        }

        string? refused = hello.ProtocolVersion != ProtocolInfo.Version
            ? $"This game speaks protocol {ProtocolInfo.Version}; the client speaks {hello.ProtocolVersion}."
            : hello.GameVersion != ProtocolInfo.GameVersion
                ? $"This game is version {ProtocolInfo.GameVersion}; yours is {(hello.GameVersion is { Length: > 0 } v ? v : "older")}. Both need the same version."
                : !SamePassword(hello.Password ?? "", Password)
                    ? (hello.Password is { Length: > 0 } ? "Wrong password." : "This game has a password.")
                    : null;
        if (refused is not null)
        {
            Drop(client, refused);
            return;
        }

        // A removed player comes back only as a candidate for its old civilization, by a vote (R3-9).
        if (hello.ReturnKey is { Length: > 0 } removedKey && _removed.TryGetValue(removedKey, out int removedCiv))
        {
            client.Removed = removedKey;
            Wait(client, hello, PlayPlayerInfo.Candidate);
            Reject(client, AskToLead(client, removedCiv));
            return;
        }

        // Back after a lost connection: the same civilization again (MP2). Otherwise the next free civilization that no absent player
        // keeps; the AI hands it over (multiplayer from the start, ADR 0028).
        bool back = hello.ReturnKey is { Length: > 0 } k && _returnKeys.ContainsKey(k);

        // In a running game with other players a newcomer watches the whole map or waits as a candidate for a vote (chapter 19).
        bool othersPlay = _clients.Exists(c => c != client && c.Civ >= 0 && c.Connection.IsOpen);
        if (!back && hello.Role == ClientRole.Observer)
        {
            Wait(client, hello, PlayPlayerInfo.Observer);
            return;
        }

        if (!back && _options.HumanSeats > 0 && _clients.Count(c => c.Civ >= 0) >= _options.HumanSeats)
        {
            Drop(client, "Every place for players is taken."); // an observer still gets in (above)
            return;
        }

        if (!back && client.Place < 0 && othersPlay && !_game.Picking && _game.Ticks > 0)
        {
            Wait(client, hello, PlayPlayerInfo.Candidate);
            return;
        }

        int civ = hello.ReturnKey is { Length: > 0 } key && _returnKeys.TryGetValue(key, out int kept) && !_game.Civs[kept].Dead
            && !_clients.Exists(c => c.Civ == kept)
            ? kept
            : client.Place >= 0 && client.Place < _game.Civs.Count && !_game.Civs[client.Place].Dead && !_clients.Exists(c => c.Civ == client.Place)
                ? client.Place
                : NextFreeCiv();
        if (civ < 0)
        {
            Drop(client, "Every civilization already has a player.");
            return;
        }

        Name(client, hello);
        Seat(client, civ);
    }

    // The nickname, made unique among those in the game (players, candidates and observers).
    private void Name(PlayClient client, HelloMessage hello)
    {
        if (client.Name is null)
        {
            (client.Name, client.SuffixLength) = Nicknames.Unique(hello.PlayerName,
                _clients.Where(c => c != client && c.Name is not null && c.Role >= 0).Select(c => c.Name!), _game.Content.NicknameSuffixes);
        }
    }

    // The client leads the civilization from now on: the AI hands it over, the client gets its return key and the content.
    private void Seat(PlayClient client, int civ)
    {
        client.Civ = civ;
        _seatNames[civ] = client.Name ?? "Player";
        client.Role = PlayPlayerInfo.Player;
        client.Since = Clock();
        if (_clients.Exists(c => c != client && c.Role >= 0 && c.Connection.IsOpen))
        {
            Announce($"{client.Name} joined and leads {_game.Civs[civ].Name}.");
        }

        _playersDirty = true;
        string returnKey = _returnKeys.FirstOrDefault(k => k.Value == civ).Key
            ?? System.Security.Cryptography.RandomNumberGenerator.GetHexString(32, lowercase: true); // outside the game: no determinism needed
        _returnKeys[returnKey] = civ;
        SetLeader(civ, player: true);
        client.Connection.Send(new WelcomeMessage(ProtocolInfo.Version, civ, civ, returnKey));
        if (client.SentContent)
        {
            client.SentChunks = false; // a candidate taken in: its map comes now
        }
        else
        {
            SendContent(client);
            client.SentContent = true;
        }
    }

    // Compares hashes, so the time taken says nothing about the password (multiplayer plan, chapter 9).
    internal static bool SamePassword(string given, string password)
    {
        if (password.Length == 0)
        {
            return true;
        }

        byte[] a = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(given));
        byte[] b = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password));
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>Pauses each player may start in a game with several players (decision of 2026-11-17).</summary>
    public const int PausesPerPlayer = 3;

    /// <summary>A pause is protected from other players for this long.</summary>
    public static readonly TimeSpan PauseProtection = TimeSpan.FromSeconds(30);

    private PlayClient? _pausedBy;
    private long _pausedAt;
    private readonly Dictionary<int, int> _pausesUsed = [];

    private int NextFreeCiv()
    {
        for (int c = 0; c < _game.Civs.Count; c++)
        {
            if (!_game.Civs[c].Dead && !_clients.Exists(x => x.Civ == c) && !_returnKeys.ContainsValue(c) && !_locked.Contains(c))
            {
                return c;
            }
        }

        return -1;
    }

    // Alone a player pauses as often as he likes. With other players: three pauses each; another player may end a pause
    // only after it has lasted 30 seconds; the speed is the last one asked for.
    private string? SetSpeed(PlayClient client, SimulationSpeed speed)
    {
        int players = _clients.FindAll(c => c.Civ >= 0).Count;
        if (players <= 1)
        {
            _speed = speed;
            return null;
        }

        if (_game.Civs[client.Civ].Dead)
        {
            return "Our civilization is gone: the players still in the game choose the speed.";
        }

        if (speed > SimulationSpeed.X2)
        {
            return "With other players the game runs at x1 or x2."; // multiplayer plan, chapter 11
        }

        if (speed == SimulationSpeed.Paused)
        {
            if (_speed == SimulationSpeed.Paused)
            {
                return null;
            }

            // Counted per civilization, so leaving and joining again gives no new pauses (abuse tests 2026-11-24).
            int used = _pausesUsed.GetValueOrDefault(client.Civ);
            if (used >= PausesPerPlayer)
            {
                return $"You have used your {PausesPerPlayer} pauses.";
            }

            _pausesUsed[client.Civ] = used + 1;
            _pausedBy = client;
            _pausedAt = Stopwatch.GetTimestamp();
            _speedBeforePause = _speed;
            _speed = speed;
            _playersDirty = true;
            Announce($"{client.Name} paused the game ({PausesPerPlayer - used - 1} pauses left).");
            return null;
        }

        if (_speed == SimulationSpeed.Paused)
        {
            if (_pausedBy is not null && _pausedBy != client && Stopwatch.GetElapsedTime(_pausedAt) < PauseProtection)
            {
                return $"The pause is protected for {(int)(PauseProtection - Stopwatch.GetElapsedTime(_pausedAt)).TotalSeconds} s more.";
            }

            // The game goes on at the speed it had (MP4: another speed only by a vote).
            _pausedBy = null;
            _speed = _speedBeforePause is SimulationSpeed.X1 or SimulationSpeed.X2 ? _speedBeforePause : SimulationSpeed.X1;
            _playersDirty = true;
            Announce($"{client.Name} ended the pause.");
            return null;
        }

        return ProposeSpeed(client, speed);
    }

    private static PlayCombatInfo CombatInfo(CombatRules r)
    {
        var open = new List<int>();
        for (int b = 0; b < r.OpenBiomes.Length; b++)
        {
            if (r.OpenBiomes[b])
            {
                open.Add(b);
            }
        }

        return new PlayCombatInfo(r.TreeCover.Ranged, r.TreeCover.Melee, r.PalisadeCover.Ranged, r.PalisadeCover.Melee, r.WallCover.Ranged, r.WallCover.Melee,
            r.OpenPercent, [.. open], r.ReliefPercent, r.ReliefMaxPercent, r.ElevationStep, r.ElevationPercent, r.ElevationMaxPercent, r.RiverPercent, r.Flank);
    }

    private long _calmSince;

    // Single player: a battle of the player's civilization that starts after a calm slows time to ×1 (Patryk, 2026-11-19).
    /// <summary>Game minutes between automatic saves.</summary>
    public const int AutosaveMinutes = 5;

    private SaveGameResultMessage Save(string name)
    {
        try
        {
            Civ first = _game.Civs[0];
            if (string.IsNullOrWhiteSpace(name))
            {
                name = _options.Scenario is { Length: > 0 } sc
                    ? $"{sc} minute {_game.Ticks / SimContent.TicksPerMinute}"
                    : $"World {_options.Seed} minute {_game.Ticks / SimContent.TicksPerMinute}";
            }

            string path = PlaySaves.Write(new SavedGame
            {
                Name = name,
                SavedAt = DateTime.UtcNow,
                Options = _options,
                Format = _format,
                Players = _format >= 2 ? _startPlayers : [.. _game.Civs.Where(c => !c.IsAi).Select(c => c.Id)],
                Tick = _game.Ticks,
                Steps = _steps,
                Hash = _game.StateHash(),
                Era = _game.Content.Eras[first.Era].Name,
                Commands = [.. _log],
                Seats = SeatsNow(),
                Locked = [.. _locked.Order()],
                Removed = new Dictionary<string, int>(_removed, StringComparer.Ordinal),
            });
            return new SaveGameResultMessage(true, $"Saved: {System.IO.Path.GetFileNameWithoutExtension(path)}");
        }
        catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
        {
            return new SaveGameResultMessage(false, $"Could not save: {e.Message}");
        }
    }

    /// <summary>Saves every <see cref="AutosaveMinutes"/> game minutes (tests turn it off so they write no files).</summary>
    public bool AutosaveOn { get; set; } = true;

    private void Autosave()
    {
        if (!AutosaveOn || _game.Picking || _game.Ticks - _lastAutosave < AutosaveMinutes * SimContent.TicksPerMinute)
        {
            return;
        }

        _lastAutosave = _game.Ticks;
        Save(PlaySaves.AutoName);
    }

    // Plays a save's commands again at their steps (format 2) or ticks (format 1), then checks it is the same game.
    private void Replay(SavedGame save)
    {
        foreach (Civ c in _game.Civs)
        {
            c.IsAi = !save.Players.Contains(c.Id);
        }

        _game.PickWaitsForPlayers = !_options.Dedicated && _game.Civs.Count(c => !c.IsAi) <= 1;
        _game.RestartPickClock();
        _startPlayers = [.. save.Players];

        foreach (SavedCommand c in save.Commands)
        {
            if (save.Format >= 2)
            {
                while (_steps < c.Step)
                {
                    Step();
                }
            }
            else
            {
                // A13 (tests 2026-11-20): ticks stand still while civilizations choose their start, so every command of that phase has tick 0.
                // The phase is played again until it is this civilization's turn (a pick) or until everyone has chosen (anything else).
                bool pick = c.Kind == PlayCommandKind.PickStart;
                while (_game.Picking && (!pick || _game.PickOrder[_game.PickIndex] != c.Civ))
                {
                    Step();
                }

                while (_game.Ticks < c.Tick)
                {
                    Step();
                }
            }

            if (c.Leader != SavedCommand.Command)
            {
                Lead(c.Civ, c.Leader == SavedCommand.PlayerLeads);
            }
            else if (c.ToMessage().Kind != PlayCommandKind.Gather) // removed 2026-11-24: older saves may still hold it
            {
                _game.Apply(ToCommand(c.Civ, c.ToMessage()));
            }

            _log.Add(c);
        }

        while (save.Format >= 2 ? _steps < save.Steps : _game.Ticks < save.Tick)
        {
            Step();
        }

        _format = save.Format; // an older save stays in its format: its ticks cannot be turned into steps

        // The places of the players (MP5): their return keys work again; removed players stay removed (MP4b).
        foreach (SavedSeat seat in save.Seats)
        {
            if (seat.Civ >= 0 && seat.Civ < _game.Civs.Count && seat.ReturnKey.Length > 0)
            {
                _returnKeys[seat.ReturnKey] = seat.Civ;
                _seatNames[seat.Civ] = seat.Name;
            }
        }

        _locked.UnionWith(save.Locked.Where(c => c >= 0 && c < _game.Civs.Count));
        foreach ((string key, int civ) in save.Removed)
        {
            _removed[key] = civ;
        }

        // On a server without a window the saved players are not here yet: the AI leads their civilizations until they join again (MP2).
        // In a window the player joins at once (and loading into a lobby comes with MP5).
        foreach (Civ c in _game.Civs)
        {
            if (_options.Dedicated && !c.IsAi && !c.Dead)
            {
                SetLeader(c.Id, player: false);
            }
        }

        _game.News.Clear();
        _lastAutosave = _game.Ticks;
        _news.Add((-1, _game.StateHash() == save.Hash
            ? $"Loaded: {save.Name}."
            : $"Loaded: {save.Name}, but it was saved by another version of the game: the game goes on from a different state."));
    }

    private IEnumerable<string> NewsOf(int civ) => _news.Where(n => n.Civ < 0 || n.Civ == civ).Select(n => n.Text);

    private void SlowForBattle()
    {
        bool fighting = _game.LastFightTick(0) == _game.Ticks;
        if (!fighting)
        {
            return;
        }

        bool fresh = _game.Ticks - _calmSince > _game.Content.Combat.CalmSeconds * SimContent.TicksPerSecond;
        _calmSince = _game.Ticks;
        if (fresh && _speed > SimulationSpeed.X1 && _clients.FindAll(c => c.Civ >= 0).Count <= 1)
        {
            _speed = SimulationSpeed.X1;
            _news.Add((-1, "Battle! Time runs at ×1."));
        }
    }

    private SimCommand ToCommand(int civ, PlayCommandMessage m) => m.Kind switch
    {
        PlayCommandKind.Build => new BuildCommand(civ, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row)), m.A),
        PlayCommandKind.PauseConstruction => new PauseConstructionCommand(civ, m.A, m.Flag),
        PlayCommandKind.Stop => new StopBuildingCommand(civ, m.A, m.Flag),
        PlayCommandKind.Priority => new PriorityCommand(civ, m.A, m.Flag),
        PlayCommandKind.Demolish => new DemolishCommand(civ, m.A),
        PlayCommandKind.Train => new TrainCommand(civ, m.A, m.B, m.C, m.Flag),
        PlayCommandKind.Split => new SplitCommand(civ, m.A, m.B),
        PlayCommandKind.Refit => new RefitCommand(civ, m.A),
        PlayCommandKind.Stance => new StanceCommand(civ, m.A, (Stance)m.B),
        PlayCommandKind.Guard => new GuardCommand(civ, m.A, Cells(m.Cells) ?? []),
        PlayCommandKind.Formation => new FormationCommand(civ, m.Cells ?? [], _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row)), m.B),
        PlayCommandKind.Move => new MoveCommand(civ, m.A, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row)), m.Flag, Cells(m.Cells)),
        PlayCommandKind.Patrol => new PatrolCommand(civ, m.A, Cells(m.Cells) ?? []),
        PlayCommandKind.BuildRoad => new BuildRoadCommand(civ, Cells(m.Cells) ?? [], m.A),
        PlayCommandKind.Board => new BoardCommand(civ, m.A, m.B),
        PlayCommandKind.Land => new LandCommand(civ, m.A, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row))),
        PlayCommandKind.UpgradeBuilding => new UpgradeBuildingCommand(civ, m.A),
        PlayCommandKind.ReplaceBuilding => new ReplaceBuildingCommand(civ, m.A, m.B),
        PlayCommandKind.SearchSigns => new SearchSignsCommand(civ, m.A),
        PlayCommandKind.Debug => new DebugCommand(civ, (DebugAction)m.A),
        PlayCommandKind.ResumePatrol => new ResumePatrolCommand(civ, m.A),
        PlayCommandKind.SetFocus => new SetFocusCommand(civ, m.A, m.B, (uint)m.C < (uint)_game.Content.TechCategories.Length ? _game.Content.TechCategories[m.C] : ""),
        PlayCommandKind.BuildWork => new BuildWorkCommand(civ, Cells(m.Cells) ?? [], m.A),
        PlayCommandKind.RemoveWork => new RemoveWorkCommand(civ, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row)), m.A),
        PlayCommandKind.AttackWall => new AttackWallCommand(civ, m.A, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row))),
        PlayCommandKind.Found => new FoundCommand(civ, m.A),
        PlayCommandKind.Research => new ResearchCommand(civ, m.A),
        PlayCommandKind.Hire => new HireCommand(civ, m.Flag),
        PlayCommandKind.Dismiss => new DismissAdvisorCommand(civ),
        PlayCommandKind.DeclareWar => new DeclareWarCommand(civ, m.A),
        PlayCommandKind.Attack => new AttackCommand(civ, m.A, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row))),
        PlayCommandKind.ProposePeace => new ProposePeaceCommand(civ, m.A, PeaceTermsOf(m)),
        PlayCommandKind.AnswerPeace => new AnswerPeaceCommand(civ, m.A, m.Flag),
        PlayCommandKind.Disband => new DisbandCommand(civ, m.A),
        PlayCommandKind.ProposeTreaty => new ProposeTreatyCommand(civ, m.A, (TreatyKind)m.B),
        PlayCommandKind.AnswerTreaty => new AnswerTreatyCommand(civ, m.A, (TreatyKind)m.B, m.Flag),
        PlayCommandKind.CancelTreaty => new CancelTreatyCommand(civ, m.A, (TreatyKind)m.B),
        PlayCommandKind.Barter => new BarterCommand(civ, m.A, m.B, m.Col * SimContent.One, m.Row, m.C * SimContent.One),
        PlayCommandKind.AnswerBarter => new AnswerBarterCommand(civ, m.A, m.Flag),
        PlayCommandKind.SendCaravan => new SendCaravanCommand(civ, m.A, m.B, m.Col, m.C * SimContent.One),
        PlayCommandKind.Route => new RouteCommand(civ, m.A, m.B, m.Col, m.C * SimContent.One, Cells(m.Cells), m.Row * SimContent.One, m.D * SimContent.One),
        PlayCommandKind.EndRoute => new EndRouteCommand(civ, m.A),
        PlayCommandKind.BringMaterials => new BringMaterialsCommand(civ, m.A),
        PlayCommandKind.TakeSettlement => new TakeSettlementCommand(civ, m.A, (TakeChoice)m.B),
        PlayCommandKind.SupplyUnit => new SupplyUnitCommand(civ, m.A, m.B, (long)m.C * SimContent.One),
        PlayCommandKind.ChooseVeteran => new ChooseVeteranCommand(civ, m.A, m.B),
        PlayCommandKind.PickStart => new PickStartCommand(civ, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row))),
        _ => new UpgradeSettlementCommand(civ, m.A),
    };

    private PlacesMessage Places(int civ, int type)
    {
        var cells = new List<int>();
        if (type > 0 && type < _game.Content.Buildings.Count)
        {
            BuildingType t = _game.Content.Buildings[type];
            Civ c = _game.Civs[civ];
            if (Array.IndexOf(t.Groups, "road") >= 0)
            {
                // Roads: known free land up to 25 hexes from our settlements.
                var seen = new HashSet<int>();
                foreach (int sid in c.Settlements)
                {
                    var area = new HexCoord[HexGlobe.RangeSize(25)];
                    int n = _game.Geo.Range(_game.Geo.FromIndex(_game.Settlements[sid].Center), 25, area);
                    for (int k = 0; k < n; k++)
                    {
                        int cell = _game.Geo.Index(area[k]);
                        if (seen.Add(cell) && _game.CannotBuild(c, cell, t, checkPrice: false) is null)
                        {
                            cells.Add(area[k].Col);
                            cells.Add(area[k].Row);
                        }
                    }
                }

                return new PlacesMessage(type, [.. cells]);
            }

            var free = new List<int>();
            foreach (int sid in c.Settlements)
            {
                foreach (int cell in _game.Settlements[sid].Cells)
                {
                    if (_game.CannotBuild(c, cell, t, checkPrice: false) is null)
                    {
                        HexCoord h = _game.Geo.FromIndex(cell);
                        cells.Add(h.Col);
                        cells.Add(h.Row);
                        free.Add(_game.FreeSlots(cell)); // 2026-11-23 (Patryk): the places show as slots while placing
                    }
                }
            }

            return new PlacesMessage(type, [.. cells], [.. free]);
        }

        return new PlacesMessage(type, [.. cells]);
    }

    private void SendContent(PlayClient client)
    {
        SimContent content = _game.Content;
        var biomes = new List<BiomeInfo>();
        foreach (Core.Content.BiomeDefinition b in content.World.Biomes)
        {
            biomes.Add(new BiomeInfo(b.Id, b.Key, b.Name, b.IsWater, b.Color));
        }

        var deposits = new List<DepositInfo>();
        foreach (Core.Content.DepositDefinition d in content.World.Deposits)
        {
            deposits.Add(new DepositInfo(d.Id, d.Key, d.Name, d.Color));
        }

        client.Connection.Send(new ContentMessage(biomes, deposits, Enum.GetNames<Core.WorldGen.TerrainFeature>(), Enum.GetNames<Core.Content.Relief>(), [], [], [], [], []));
        var goods = new List<PlayGoodInfo>();
        foreach (SimGood g in content.Goods)
        {
            goods.Add(new PlayGoodInfo(g.Id, g.Key, g.Name));
        }

        var buildings = new List<PlayBuildingInfo>();
        foreach (BuildingType t in content.Buildings)
        {
            buildings.Add(new PlayBuildingInfo(t.Id, t.Key, t.Name, t.Workers, t.Price, t.BuildTicks / SimContent.TicksPerSecond, t.Produces, t.Consumes, t.Housing, t.Storage, t.Id == 0 ? "" : _game.Needs(t), t.Tech, t.Groups, t.Size, t.UpgradesTo, Carriers(content, t)));
        }

        var levels = new List<PlayLevelInfo>();
        foreach (SettlementLevel l in content.Levels)
        {
            levels.Add(new PlayLevelInfo(l.Key, l.Name, l.Radius, l.Housing, l.UpgradePrice, l.Tech));
        }

        var units = new List<PlayUnitInfo>();
        foreach (UnitType u in content.Units)
        {
            units.Add(new PlayUnitInfo(u.Id, u.Key, u.Name, u.People, u.Price, u.TrainTicks / SimContent.TicksPerSecond, u.HiddenTicksPerHex > 0, u.Founds, u.Tech, u.Army, u.Strength, u.Range, u.System, u.NeedsBuilding, u.Naval, u.Carries, u.Survey, u.MinPeople, u.MaxPeople, u.Groups));
        }

        var techs = new List<PlayTechInfo>();
        foreach (SimTech t in content.Techs)
        {
            var needs = new List<string>();
            foreach ((int req, long ticks) in t.Experience)
            {
                needs.Add($"{content.Techs[req].Name} {ticks * content.Pace.Experience(t.Era) / 100 / SimContent.TicksPerMinute}");
            }

            // The branches of the tree: experience in, and plain need of, earlier technologies.
            var from = new List<int>();
            foreach ((int req, long _) in t.Experience)
            {
                from.Add(req);
            }

            foreach (int req in t.Requires)
            {
                if (!from.Contains(req))
                {
                    from.Add(req);
                }
            }

            techs.Add(new PlayTechInfo(t.Id, t.Key, t.Name, t.Era, string.Join(", ", needs), t.ResearchTicks * content.Pace.Seconds(t.Era) / 100 / SimContent.TicksPerSecond, t.Gives, t.Upgrade, t.Category, Unlocks(content, t), [.. from],
                content.GatewayOf(t.Era) == t.Id && t.Era + 1 < content.Eras.Count ? content.Eras[t.Era + 1].Name : ""));
        }

        var eras = new List<string>();
        foreach (SimEra e in content.Eras)
        {
            eras.Add(e.Name);
        }

        var roads = new List<PlayRoadInfo>();
        foreach (RoadLevel r in content.RoadLevels)
        {
            roads.Add(new PlayRoadInfo(r.Level, r.Name, r.Price, r.TicksPerHex / SimContent.TicksPerSecond, r.Tech));
        }

        var works = new List<PlayWorkInfo>();
        foreach (WorkKind w in _game.Content.Works)
        {
            string text = w.Key switch
            {
                "ditch" => $"Drawn from a hex next to water, at most {w.FromWater} hexes. Farms on watered hexes make {w.FarmPercent}% more. Stays under buildings.",
                "palisade" => $"Enemies at war standing on it lose {w.WoundPercentPerSecond}% of their people a second.",
                "wall" => "Foreign walkers cannot pass until they break it; our own people go through.",
                "aqueduct" => $"Drawn from water, at most {w.FromWater} hexes: a settlement it reaches grows {w.GrowthBonus}% a minute faster, epidemics {w.ResistEpidemicPercent}% milder.",
                _ => "",
            };
            works.Add(new PlayWorkInfo(w.Id, w.Key, w.Name, w.Price, w.TicksPerHex / SimContent.TicksPerSecond, w.Tech, w.FromWater, text));
        }

        client.Connection.Send(new PlayContentMessage(goods, buildings, levels, _game.Geo.Width, _game.Geo.Height, "Earth", units, techs, eras, roads, works, _game.Content.TechCategories,
            _game.Content.Rations.HungryPercent, _game.Content.Rations.StarvingPercent, _game.Content.WeaponGoods, _game.Content.ArmourGoods,
            _game.Content.Combat.Weapon, _game.Content.Combat.Armour, CombatInfo(_game.Content.Combat),
            [.. _game.Content.Veterans.Options.Select(o => $"{o.Name}|{o.Text}")], _game.Content.Veterans.Levels, _game.Content.Peace.Minutes, _game.Settings.Seed));
        client.SentChunks = false;
    }

    private void SendViews()
    {
        long started = Stopwatch.GetTimestamp();
        SendViewsNow();
        Interlocked.Add(ref _viewTime, Stopwatch.GetTimestamp() - started);
    }

    private void SendViewsNow()
    {
        bool mapChanged = !_game.Map.Dirty.IsEmpty;
        _dirty.Clear();
        if (mapChanged)
        {
            _game.Map.Dirty.CopyTo(_dirty);
            _game.Map.Dirty.Clear();
        }

        foreach (PlayClient client in _clients)
        {
            // Observers and players whose civilization fell see the whole map (P6, R3-7); candidates see none (chapter 19).
            bool watching = client.Role == PlayPlayerInfo.Observer || (client.Civ >= 0 && _game.Civs[client.Civ].Dead);
            if (client.Civ < 0 && !watching)
            {
                continue;
            }

            if (watching != client.Watching)
            {
                client.Watching = watching;
                client.SentChunks = false; // the whole map from now on
                client.ViewSoon = true;
                client.Eye = -1;
                _playersDirty = true; // a fallen player shows as such
            }

            int eye = watching && client.Eye >= 0 && client.Eye < _game.Civs.Count ? client.Eye : -1;
            bool blank = false;
            if (eye != client.SentEye)
            {
                // Another pair of eyes: the whole map again, hexes this civilization does not know blank (observer full view).
                client.SentEye = eye;
                client.SentChunks = false;
                client.ViewSoon = true;
                blank = true;
            }

            if (client.Connection.Congested)
            {
                // A player over a slow network: no new views until the sent ones are out. The views are differences from what
                // the client has, so the next round brings everything it missed (multiplayer plan, MP1).
                Interlocked.Increment(ref _skippedViews);
                client.MissedChunks.UnionWith(_dirty); // changed map parts of this round come with the next view
                continue;
            }

            Civ civ = eye >= 0 ? _game.Civs[eye] : watching ? Watcher() : _game.Civs[client.Civ];
            if (!client.SentChunks || client.SawPicking != _game.Picking)
            {
                // The whole map again: at the start, and when the start phase ends (deposits and soil appear, the fog comes).
                System.Array.Clear(client.SentTerrain);
                for (int chunk = 0; chunk < _game.Map.Chunks.Count; chunk++)
                {
                    SendChunk(client, civ, chunk, always: client.SentChunks || blank);
                }

                client.SentChunks = true;
                client.SawPicking = _game.Picking;
                if (eye < 0)
                {
                    civ.NewlyKnown.Clear();
                }
            }
            else if (eye >= 0)
            {
                // Through another civilization's eyes: its newly known hexes belong to its own player, so every chunk is compared.
                _changed.Clear();
                for (int chunk = 0; chunk < _game.Map.Chunks.Count; chunk++)
                {
                    _changed.Add(chunk);
                }

                client.MissedChunks.Clear();
                SendChanges(client, civ);
            }
            else
            {
                _changed.Clear();
                foreach (int chunk in _dirty)
                {
                    _changed.Add(chunk);
                }

                foreach (int chunk in civ.NewlyKnown)
                {
                    _changed.Add(chunk);
                }

                _changed.UnionWith(client.MissedChunks);
                client.MissedChunks.Clear();

                civ.NewlyKnown.Clear();
                SendChanges(client, civ);
            }

            SendView(client, civ);
        }

        _news.Clear();
    }

    // Objects every round, as changes; the rest of the view four times a second (or at once after a command).
    private void SendView(PlayClient client, Civ civ)
    {
        client.News.AddRange(NewsOf(client.Civ));
        PlayViewMessage v = client.Watching ? View(client.SentEye, civ) with { Lost = client.Civ >= 0, Spectator = true } : View(client.Civ, civ);
        var buildings = new List<PlayBuildingView>();
        var settlements = new List<PlaySettlementView>();
        var units = new List<PlayUnitView>();
        int[] goneB = Diff(v.Buildings, b => b.Id, Sig, client.SentBuildings, buildings);
        int[] goneS = Diff(v.Settlements, s => s.Id, Sig, client.SentSettlements, settlements);
        int[] goneU = Diff(v.Units, u => u.Id, Sig, client.SentUnits, units);
        if (buildings.Count + settlements.Count + units.Count + goneB.Length + goneS.Length + goneU.Length > 0)
        {
            client.Connection.Send(new PlayObjectsMessage(buildings, goneB, settlements, goneS, units, goneU));
        }

        long now = Clock();
        if (!client.ViewSoon && _game.Ticks > 0 && Stopwatch.GetElapsedTime(client.LastFullView, now) < FullViewEvery && client.LastFullView != long.MinValue)
        {
            return;
        }

        int techs = TechSig(v.Techs);
        bool same = techs == client.SentTechs;
        client.SentTechs = techs;
        client.Connection.Send(v with
        {
            Buildings = [],
            Settlements = [],
            Units = [],
            News = [.. client.News],
            Techs = same ? [] : v.Techs,
            ObjectsApart = true,
            TechsSame = same,
        });
        client.News.Clear();
        client.LastFullView = now;
        client.ViewSoon = false;
    }

    // Adds to changed the views whose signature differs from what the client has; returns the ids it has no more.
    private static int[] Diff<T>(IReadOnlyList<T> now, Func<T, int> id, Func<T, int> sig, Dictionary<int, int> sent, List<T> changed)
    {
        var present = new HashSet<int>();
        foreach (T x in now)
        {
            int key = id(x);
            int s = sig(x);
            present.Add(key);
            if (!sent.TryGetValue(key, out int old) || old != s)
            {
                sent[key] = s;
                changed.Add(x);
            }
        }

        var gone = new List<int>();
        foreach (int key in sent.Keys)
        {
            if (!present.Contains(key))
            {
                gone.Add(key);
            }
        }

        foreach (int key in gone)
        {
            sent.Remove(key);
        }

        gone.Sort();
        return [.. gone];
    }

    private static int Sig(PlayBuildingView b) => HashCode.Combine(HashCode.Combine(b.Type, b.Civ, b.Col, b.Row, b.State, b.Workers, b.Starved, b.Priority),
        HashCode.Combine(b.ProgressPermille, b.YieldPercent, b.Missing, b.Porters, b.Remembered, b.Slots, HashCode.Combine(b.Focus is null ? 0 : string.Join(",", b.Focus).GetHashCode(StringComparison.Ordinal), b.UpgradeBlocked, b.UpgradeCost)));

    private static int Sig(PlaySettlementView s) => HashCode.Combine(HashCode.Combine(s.Civ, s.Name, s.Level, s.People, s.Housing, s.Idle, s.Besieged, HashCode.Combine(s.DefensePercent, s.Guard, s.FallenToUs, s.Fighters)),
        HashCode.Combine(Arr(s.Stock), Arr(s.Capacity), Arr(s.Rate), s.Remembered, s.Col, s.Row, HashCode.Combine(s.Gathering, Arr(s.Richness), s.WorkPermille, s.HealPerMinute, s.RationUnits, s.RationFood, s.RationShort, HashCode.Combine(s.RationDead, s.HungryPeople, s.HungryWorkers))));

    private static int Sig(PlayUnitView u) => HashCode.Combine(HashCode.Combine(u.Kind, u.Civ, u.Col, u.Row, u.People, u.State, u.Hidden, u.ToCol),
        HashCode.Combine(u.ToRow, u.Fighting, u.Cargo, Arr(u.Path), Arr(u.Patrol), u.Attack, u.Carrier, HashCode.Combine(u.Injured, u.Aboard, u.Hunger, u.Home, Arr(u.LastPatrol), Arr(u.Found), HashCode.Combine(u.Weapon, u.Armour, u.Stance, u.LastLoss, Arr(u.Guard), u.Size, Arr(u.Struck), u.Charging))));

    private static int TechSig(IReadOnlyList<PlayTechState> techs)
    {
        var h = new HashCode();
        foreach (PlayTechState t in techs)
        {
            h.Add(t.Known);
            h.Add(t.Blocked);
            h.Add(Arr(t.Price));
            h.Add(t.Seconds);
        }

        return h.ToHashCode();
    }

    private static int Arr(long[]? a)
    {
        if (a is null)
        {
            return 0;
        }

        var h = new HashCode();
        foreach (long x in a)
        {
            h.Add(x);
        }

        return h.ToHashCode();
    }

    private static int Arr(int[]? a)
    {
        if (a is null)
        {
            return 0;
        }

        var h = new HashCode();
        foreach (int x in a)
        {
            h.Add(x);
        }

        return h.ToHashCode();
    }

    private readonly SortedSet<int> _changed = [];
    private readonly List<int> _cellList = [];
    private readonly List<byte> _visList = [];
    private readonly List<short> _ownerList = [];
    private readonly List<int> _chunkList = [];

    // What the player should see of a hex now: unknown, remembered (grey) or seen; and its owner as known.
    private byte VisibilityFor(Civ civ, int cell) =>
        !civ.Known[cell] ? MapChunkMessage.Unknown : _game.Visible(civ, cell) ? MapChunkMessage.Visible : MapChunkMessage.Remembered;

    private short OwnerFor(Civ civ, int cell) => civ.Known[cell] ? _game.OwnerSeen(civ, cell) : WorldMap.NoOwner;

    private byte RoadFor(Civ civ, int cell) => civ.Known[cell] ? _game.RoadSeen(civ, cell) : (byte)0;

    private readonly List<byte> _roadList = [];

    private readonly List<ushort> _worksList = [];

    private ushort WorksFor(Civ civ, int cell) => civ.Known[cell] ? _game.WorksSeen(civ, cell) : (ushort)0;

    // Only what changed against what the client has: terrain once per newly explored chunk, then fog and owner per hex.
    private void SendChanges(PlayClient client, Civ civ)
    {
        _cellList.Clear();
        _visList.Clear();
        _ownerList.Clear();
        _roadList.Clear();
        _worksList.Clear();
        _chunkList.Clear();
        foreach (int chunk in _changed)
        {
            (int x0, int y0, int w, int h) = _game.Map.Chunks.Bounds(chunk);
            bool terrain = false;
            for (int r = 0; r < h && !terrain; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int i = _game.Geo.Index(new HexCoord(x0 + c, y0 + r));
                    // New ground, or something found in it (prospectors, surveyors, our land growing): the whole chunk again.
                    if ((civ.Known[i] && !client.SentTerrain[i]) || (civ.Known[i] && SurveyFor(civ, i) != client.SentSurvey[i]))
                    {
                        terrain = true;
                        break;
                    }
                }
            }

            if (terrain)
            {
                SendChunk(client, civ, chunk);
                continue;
            }

            bool any = false;
            for (int r = 0; r < h; r++)
            {
                for (int c = 0; c < w; c++)
                {
                    int i = _game.Geo.Index(new HexCoord(x0 + c, y0 + r));
                    byte vis = VisibilityFor(civ, i);
                    short owner = OwnerFor(civ, i);
                    byte road = RoadFor(civ, i);
                    ushort works = WorksFor(civ, i);
                    if (vis != client.SentVisibility[i] || owner != client.SentOwner[i] || road != client.SentRoad[i] || works != client.SentWorks[i])
                    {
                        client.SentVisibility[i] = vis;
                        client.SentOwner[i] = owner;
                        client.SentRoad[i] = road;
                        client.SentWorks[i] = works;
                        _cellList.Add(i);
                        _visList.Add(vis);
                        _ownerList.Add(owner);
                        _roadList.Add(road);
                        _worksList.Add(works);
                        any = true;
                    }
                }
            }

            if (any)
            {
                _chunkList.Add(chunk);
            }
        }

        if (_cellList.Count > 0)
        {
            client.Connection.Send(new PlayCellsMessage([.. _cellList], [.. _visList], [.. _ownerList], [.. _chunkList], [.. _roadList], [.. _worksList]));
        }
    }

    // Ore signs of the people as pairs (deposit, centre hex); null with none (2026-11-22).
    private int[]? SignsFor(Civ civ)
    {
        IReadOnlyList<OreSign> signs = _game.SignsOf(civ);
        if (signs.Count == 0)
        {
            return null;
        }

        var list = new int[signs.Count * 2];
        for (int k = 0; k < signs.Count; k++)
        {
            list[2 * k] = signs[k].Deposit;
            list[(2 * k) + 1] = signs[k].Center;
        }

        return list;
    }

    // What the civilization knows of a hex's ground: rock looked at in its era (1), soil known (2).
    private byte SurveyFor(Civ civ, int i) => (byte)((_game.RockLookedAt(civ, i) ? MapChunkMessage.RockLooked : 0) | (_game.KnowsSoil(civ, i) ? MapChunkMessage.SoilKnown : 0)
        | (_game.KnowsDeposit(civ, i) ? MapChunkMessage.DepositKnown : 0) | (_game.DepositHidden(civ, i) ? MapChunkMessage.DepositHidden : 0));

    private void SendChunk(PlayClient client, Civ civ, int chunk, bool always = false)
    {
        MapChunkMessage? m = MapViewBuilder.Build(_game.Map, 0, chunk, _game.Ticks, civ.Known, hideResources: _game.Picking, always: always,
            visibilityOf: i => VisibilityFor(civ, i), ownerSeen: i => OwnerFor(civ, i), roadSeen: i => RoadFor(civ, i), worksSeen: i => WorksFor(civ, i), treesOf: i => civ.Known[i] ? _game.Trees[i] : (byte)0,
            depositKnown: i => _game.KnowsDeposit(civ, i), surveyOf: i => SurveyFor(civ, i));
        (int x0, int y0, int w, int h) = _game.Map.Chunks.Bounds(chunk);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int i = _game.Geo.Index(new HexCoord(x0 + c, y0 + r));
                client.SentTerrain[i] = civ.Known[i];
                client.SentVisibility[i] = VisibilityFor(civ, i);
                client.SentOwner[i] = OwnerFor(civ, i);
                client.SentRoad[i] = RoadFor(civ, i);
                client.SentWorks[i] = WorksFor(civ, i);
                client.SentSurvey[i] = SurveyFor(civ, i);
            }
        }

        if (m is not null)
        {
            client.Connection.Send(m);
        }
    }

    private PlayViewMessage View(int civId) => View(civId, _game.Civs[civId]);

    // The view of a civilization; civId −1 with the watcher: the whole map, nothing of it ours.
    private PlayViewMessage View(int civId, Civ civ)
    {
        // A14/B1 (tests 2026-11-20): the view only reads. The simulation keeps a player's people recomputed itself
        // (SimGame.SettlePlayer); an AI people seen by an observer shows its numbers from its last thought.
        bool[] known = civ.Known;
        var settlements = new List<PlaySettlementView>();
        foreach (Settlement s in _game.Settlements)
        {
            // Our own always; others only while seen (what we saw before comes from memory below).
            if (s.Abandoned)
            {
                // G2 (2026-11-19): ruins are nobody's; seen like any land we know.
                if (known[s.Center] && _game.Visible(civ, s.Center))
                {
                    HexCoord rh = _game.Geo.FromIndex(s.Center);
                    settlements.Add(new PlaySettlementView(s.Id, -1, $"Ruins of {s.Name}", rh.Col, rh.Row, s.Level, 0, 0, 0, false, 0, null, null, null));
                }

                continue;
            }

            if (s.Civ != civId && !_game.Visible(civ, s.Center))
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(s.Center);
            bool own = s.Civ == civId;
            (int rUnits, int rPeople, long rFood) = own ? _game.RationOf(s) : (0, 0, 0);
            settlements.Add(new PlaySettlementView(s.Id, s.Civ, s.Name, h.Col, h.Row, s.Level, s.People, s.Housing, s.Idle, s.Besieged, _game.Defense(s),
                own ? (long[])s.Stock.Clone() : null, own ? (long[])s.Capacity.Clone() : null, own ? (long[])s.Rate.Clone() : null,
                Gathering: own && s.Gathering, Richness: own ? (int[])_game.Richness(s).Clone() : null, WorkPermille: own ? s.WorkPermille : 1000,
                HealPerMinute: own ? s.HealPerMinute : 0, RationUnits: rUnits, RationPeople: rPeople, RationFood: rFood,
                RationShort: own ? s.RationShort : 0, RationDead: own ? s.RationDead : 0,
                HungryPeople: own ? s.HungryPeople : 0, HungryWorkers: own ? s.HungryWorkers : 0,
                Guard: s.Guard, GuardMax: own ? _game.GuardMax(s) : 0, FallenToUs: civId >= 0 && s.FallenTo == civId, Fighters: s.Besieged ? s.Fighters : 0,
                FoodBalance: own ? _game.FoodBalance(civ, s) : null));
        }

        if (!_game.Picking)
        {
            foreach (SettlementMemory m in _game.RememberedSettlements(civ))
            {
                if (m.Civ != civId)
                {
                    HexCoord h = _game.Geo.FromIndex(m.Center);
                    settlements.Add(new PlaySettlementView(m.Settlement, m.Civ, m.Name, h.Col, h.Row, m.Level, -1, 0, 0, Remembered: true));
                }
            }
        }

        var buildings = new List<PlayBuildingView>();
        foreach (Building b in _game.Buildings)
        {
            bool ruined = b.State == BuildingState.Ruined;
            if (b.State == BuildingState.Removed || ((b.Civ != civId || ruined) && !_game.Visible(civ, b.Cell)))
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(b.Cell);
            BuildingType t = _game.Content.Buildings[b.Type];
            int progress = 1000;
            string? missing = null;
            if (b.State is BuildingState.Delivery or BuildingState.Construction or BuildingState.ConstructionPaused)
            {
                progress = (int)Math.Clamp(_game.WorkDone(b) * 1000L / Math.Max(1, t.BuildTicks), 0, 1000);
                if (b.Civ == civId)
                {
                    string text = _game.CargoText(_game.Missing(b));
                    missing = text == "nothing" ? null : text;
                }
            }

            // An upgrade in place: what it becomes, what it still costs and why it cannot be done now.
            string? upBlocked = null;
            string? upCost = null;
            if (b.Civ == civId && t.UpgradesTo > 0 && b.State is BuildingState.Active or BuildingState.Stopped)
            {
                upBlocked = _game.CannotUpgrade(civ, b, out BuildingType? to) ?? "";
                upCost = _game.CargoText(_game.UpgradeRest(t, to!));
            }

            buildings.Add(new PlayBuildingView(b.Id, b.Type, ruined ? -1 : b.Civ, h.Col, h.Row, (int)b.State, b.Workers, b.Starved, b.Priority, progress, b.YieldPercent, missing, b.Civ == civId ? b.Porters : 0, Slots: b.Slots, Focus: b.Civ == civId && b.Focus.Length > 0 ? (string[])b.Focus.Clone() : null,
                UpgradeBlocked: upBlocked, UpgradeCost: upCost));
        }

        if (!_game.Picking)
        {
            foreach (BuildingMemory m in _game.RememberedBuildings(civ))
            {
                if (m.Civ != civId)
                {
                    HexCoord h = _game.Geo.FromIndex(m.Cell);
                    buildings.Add(new PlayBuildingView(m.Building, m.Type, m.Civ, h.Col, h.Row, (int)m.State, 0, false, false, 1000, 100, Remembered: true, Slots: m.Slots));
                }
            }
        }

        _ = known;
        var units = new List<PlayUnitView>();
        foreach (Unit u in ActiveAndTraining())
        {
            if (u.State == UnitState.Gone || u.Aboard >= 0 || (u.Civ != civId && !_game.Sees(civ, u)))
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(u.Cell);
            HexCoord to = u.State == UnitState.Moving && u.Path.Count > 0 ? _game.Geo.FromIndex(u.Path[^1]) : h;
            bool mine = u.Civ == civId;
            bool route = mine || civId < 0; // the whole map shows where everyone goes (observer full view, intentions on the map)
            units.Add(new PlayUnitView(u.Id, u.Kind, u.Civ, h.Col, h.Row, u.People, (int)u.State, u.Hidden, to.Col, to.Row, u.Fighting,
                u.Cargo is null ? null : _game.CargoText(u.Cargo),
                route && u.State == UnitState.Moving ? KnownPairs(u.Path, u.PathIndex, civ.Known) : null,
                route && u.Patrol.Count > 0 ? Pairs(u.Patrol, 0) : null,
                u.TargetUnit >= 0 || u.TargetSettlement >= 0,
                _game.Content.Units[u.Kind].System, mine ? u.Injured : 0, mine && _game.Content.Units[u.Kind].CarriesArmies ? _game.Aboard(u) : 0,
                mine ? u.Hunger : 0, mine ? u.Home : -1, mine && u.Patrol.Count == 0 && u.LastPatrol.Count > 0 ? Pairs(u.LastPatrol, 0) : null,
                mine && _game.Content.Units[u.Kind].Survey > 0 ? Found(u) : null, u.Weapon, u.Armour, (int)u.Stance, u.LastLoss,
                mine && u.Guard.Count > 0 ? Pairs(u.Guard, 0) : null, Math.Max(u.Size, u.People), u.Struck.Count > 0 ? [.. u.Struck] : null,
                u.ChargeTick >= 0 && _game.Ticks - u.ChargeTick < SimContent.TicksPerSecond,
                mine ? u.Provisions : 0, mine ? _game.ProvisionsMax(u) : 0, !mine || !_game.Content.Units[u.Kind].Army || _game.InSupply(u)));
        }

        return new PlayViewMessage(_game.Ticks, _speed, civId, (long[])civ.Stock.Clone(), (long[])civ.Capacity.Clone(), (long[])civ.Rate.Clone(),
            civ.People, civ.Hungry, settlements, buildings, [.. NewsOf(civId)], units, _game.EraOf(civ), civ.Researching, ResearchPermille(civ), TechStates(civ),
            Person(civ.Thinker, false), Person(civ.ThinkerOffer, false), Person(civ.Advisor, true), Person(civ.AdvisorOffer, true), _game.Advice(civ),
            KnownCivs(civ), civ.Dead, _game.Picking, _game.Picking && _game.PickOrder[_game.PickIndex] == civId,
            _game.Picking ? _game.PickTicksLeft / SimContent.TicksPerSecond : 0, TakenStarts(), _game.ShownBlockRadius,
            _game.Picking ? _game.Civs[_game.PickOrder[_game.PickIndex]].Name : null,
            civ.Event >= 0 ? _game.Content.Events[civ.Event].Name : null, civ.Event >= 0 ? (int)((civ.EventEnd - _game.Ticks) / SimContent.TicksPerSecond) : 0,
            _game.GoalText, _game.Winner >= 0 ? _game.Civs[_game.Winner].Name : null, _game.ScoreOf(civ), RoutesOf(civId),
            AlertsOf(civ), _game.KnownIn(civ, civ.Era), civ.Era + 1 < _game.Content.Eras.Count ? _game.Content.EraAdvanceTechs : 0,
            Winter: _game.IsWinter, SeasonSeconds: _game.TicksToSeasonChange == long.MaxValue ? 0 : (int)(_game.TicksToSeasonChange / SimContent.TicksPerSecond),
            RationSeconds: (_game.TicksToRation(civ) + SimContent.TicksPerSecond - 1) / SimContent.TicksPerSecond, Debug: _game.DebugOn,
            ArmyUnits: _game.ArmyUnits(civ), ArmyLimit: _game.ArmyLimit(civ), KindKills: (int[])civ.KindKills.Clone(), KindLevel: (int[])civ.KindLevel.Clone(), KindPending: (int[])civ.KindPending.Clone(), KindPicks: (int[])civ.KindPicks.Clone(), Won: civId >= 0 && _game.Winner == civId,
            EventInfo: civ.Event >= 0 ? _game.EventInfo(_game.Content.Events[civ.Event]) : null,
            Tutorial: civ.Settlements.Count > 0 ? [.. _game.Tutorial(civ).Select(t => new PlayTutorialStep(t.Text, t.Done, t.Highlight))] : null,
            TutorialOn: _options.Tutorial, PickNoLimit: _game.PickWaitsForPlayers,
            PickMaxHexes: _game.Picking ? _game.Content.StartPhase.NeighbourMaxHexes + _game.Content.StartPhase.BlockFuzz : 0,
            Signs: SignsFor(civ),
            SignRadius: _game.Content.Lodes.SignRadius);
    }

    // E9 (2026-11-20): people the materials of a building take from home while porters carry them (as NewCarrier counts them).
    private static int Carriers(SimContent content, BuildingType t)
    {
        long total = 0;
        foreach (long p in t.Price)
        {
            total += p;
        }

        long each = Math.Max(1, content.Units[content.Porters].CargoPerPerson);
        return total <= 0 ? 0 : (int)((total + each - 1) / each);
    }

    // What a technology opens and changes, worked out from the data (so the player sees exactly what it does).
    private static string Unlocks(SimContent content, SimTech t)
    {
        var lines = new List<string>();
        // "with X": things that need this technology and another one as well.
        string With(int tech, int[] also)
        {
            var others = also.Append(tech).Where(o => o != t.Id && o >= 0).Select(o => content.Techs[o].Name).ToList();
            return others.Count == 0 ? "" : $" (with {string.Join(" and ", others)})";
        }

        bool Needs(int tech, int[] also) => tech == t.Id || also.Contains(t.Id);
        var buildings = content.Buildings.Where(b => b.Id != 0 && Needs(b.Tech, b.AlsoTechs)).Select(b => b.Name + With(b.Tech, b.AlsoTechs)).ToList();
        if (buildings.Count > 0)
        {
            lines.Add("Buildings: " + string.Join(", ", buildings));
        }

        var units = content.Units.Where(u => (!u.System || u.Key == "caravan" || u.Key == "trade_ship") && !u.Levy && Needs(u.Tech, u.AlsoTechs)).Select(u => u.Name + With(u.Tech, u.AlsoTechs)).ToList();
        if (units.Count > 0)
        {
            lines.Add("Units: " + string.Join(", ", units));
        }

        foreach (UnitType u in content.Units)
        {
            foreach ((int tech, int kind) in u.Becomes)
            {
                if (tech == t.Id)
                {
                    lines.Add($"{u.Name} become {content.Units[kind].Name.ToLowerInvariant()}");
                }
            }
        }

        foreach (SettlementLevel l in content.Levels)
        {
            if (l.Id > 0 && Needs(l.Tech, l.AlsoTechs))
            {
                lines.Add($"Settlements can become a {l.Name.ToLowerInvariant()}{With(l.Tech, l.AlsoTechs)}");
            }
        }

        if (t.Requires.Length > 0)
        {
            lines.Add("Needs first: " + string.Join(", ", t.Requires.Select(r => content.Techs[r].Name)));
        }

        if (t.Made.Length > 0)
        {
            lines.Add("Needs made: " + string.Join(", ", t.Made.Select(m => $"{m.Amount * content.Pace.Experience(t.Era) / 100 / SimContent.One} {content.Goods[m.Good].Name.ToLowerInvariant()}")));
        }

        if (t.Basic)
        {
            lines.Add("A basic step: does not count toward the next era");
        }

        foreach (RoadLevel r in content.RoadLevels)
        {
            if (r.Tech == t.Id)
            {
                lines.Add($"Roads: {r.Name.ToLowerInvariant()}");
            }
        }

        foreach (WorkKind w in content.Works)
        {
            if (w.Tech == t.Id)
            {
                lines.Add($"Works on hexes: {w.Name.ToLowerInvariant()}");
            }
        }

        if (content.BridgeTech == t.Id)
        {
            lines.Add("Roads bridge the rivers they cross");
        }

        foreach (TechEffect e in t.Effects)
        {
            string target = e.Target == "*" ? "all" : content.Buildings.FirstOrDefault(b => b.Key == e.Target)?.Name
                ?? content.Units.FirstOrDefault(u => u.Key == e.Target)?.Name ?? e.Target;
            lines.Add(e.Kind switch
            {
                EffectKind.Production => $"Production of {target} +{e.Value}%",
                EffectKind.Housing => $"Homes in {target} +{e.Value}",
                EffectKind.Storage => $"Room in {target} +{e.Value}%",
                EffectKind.Strength => $"Strength of {target} +{e.Value}%",
                EffectKind.Speed => $"Walking speed of {target} +{e.Value}%",
                EffectKind.Training => $"Training of {target} {e.Value}% shorter",
                EffectKind.Research => $"Research {e.Value}% cheaper",
                EffectKind.Growth => $"Growth +{e.Value}% a minute",
                EffectKind.Resist => $"{target.Replace('_', ' ')} {e.Value}% milder",
                EffectKind.Siege => $"Siege +{e.Value}%",
                EffectKind.Defense => $"Defence +{e.Value}%",
                EffectKind.Cargo => $"Each of {target.ToLowerInvariant()} carries {e.Value} more",
                EffectKind.Fertility => $"{target} also on land {e.Value} points less fertile",
                _ => $"Rarer thinkers and advisors (+{e.Value})",
            });
        }

        return string.Join("\n", lines);
    }

    // Units of every civilization that are not gone (the game's list keeps every unit ever made).
    private IEnumerable<Unit> ActiveAndTraining()
    {
        foreach (Civ c in _game.Civs)
        {
            foreach (int id in c.Units)
            {
                yield return _game.Units[id];
            }
        }
    }

    // Alerts change at most once a second of play: worked out then, not for every view.
    private readonly Dictionary<int, (long Tick, List<PlayAlertView> Alerts)> _alerts = [];

    private List<PlayAlertView> AlertsOf(Civ civ)
    {
        if (_alerts.TryGetValue(civ.Id, out (long Tick, List<PlayAlertView> Alerts) cached) && _game.Ticks - cached.Tick < SimContent.TicksPerSecond && _game.Ticks >= cached.Tick)
        {
            return cached.Alerts;
        }

        var list = new List<PlayAlertView>();
        _alerts[civ.Id] = (_game.Ticks, list);
        if (civ.Dead || _game.Picking)
        {
            return list;
        }

        foreach (Alert a in _game.Alerts(civ))
        {
            HexCoord h = _game.Geo.FromIndex(a.Cell);
            list.Add(new PlayAlertView((int)a.Kind, a.Settlement, h.Col, h.Row, a.Good, a.Text));
        }

        return list;
    }

    // Hex indices from a position on, as (col, row) pairs for the client.
    // A20 (tests 2026-11-20): a way is shown only as far as the land we know; its shape beyond would give away hidden terrain.
    private int[] KnownPairs(IReadOnlyList<int> cells, int from, bool[] known)
    {
        int end = from;
        while (end < cells.Count && known[cells[end]])
        {
            end++;
        }

        int n = end - from;
        var pairs = new int[n * 2];
        for (int k = 0; k < n; k++)
        {
            HexCoord h = _game.Geo.FromIndex(cells[from + k]);
            pairs[2 * k] = h.Col;
            pairs[(2 * k) + 1] = h.Row;
        }

        return pairs;
    }

    private int[] Pairs(IReadOnlyList<int> cells, int from)
    {
        int n = Math.Max(0, cells.Count - from);
        var pairs = new int[n * 2];
        for (int k = 0; k < n; k++)
        {
            HexCoord h = _game.Geo.FromIndex(cells[from + k]);
            pairs[2 * k] = h.Col;
            pairs[(2 * k) + 1] = h.Row;
        }

        return pairs;
    }

    // Hexes drawn by the player, as (col, row) pairs, to hex indices.
    private int[]? Cells(int[]? pairs)
    {
        if (pairs is null || pairs.Length < 2)
        {
            return null;
        }

        var cells = new int[pairs.Length / 2];
        for (int k = 0; k < cells.Length; k++)
        {
            cells[k] = _game.Geo.Index(_game.Geo.Wrap(pairs[2 * k], pairs[(2 * k) + 1]));
        }

        return cells;
    }

    private PlaySummaryMessage Summary(Civ civ)
    {
        var civs = new List<PlayHistoryView>();
        foreach (Civ c in _game.Civs)
        {
            if (c.Id != civ.Id && !_game.Met(civ, c))
            {
                continue;
            }

            var rows = new List<PlayHistoryRow>();
            foreach (HistorySample h in c.History)
            {
                rows.Add(new PlayHistoryRow(h.Minute, h.People, h.Settlements, h.Buildings, h.Techs, h.Soldiers, h.FoodPerMinute, h.Era, h.Event >= 0 ? _game.Content.Events[h.Event].Name : null));
            }

            civs.Add(new PlayHistoryView(c.Id, c.Name, rows));
        }

        (int mine, int best) = _game.StartQuality(civ);
        var criteria = new int[SiteScores.Criteria.Length];
        if (_game.Sites is { } sites && _game.StartCell.Length > civ.Id && _game.StartCell[civ.Id] >= 0)
        {
            for (int k = 0; k < criteria.Length; k++)
            {
                criteria[k] = sites.Values[k][_game.StartCell[civ.Id]];
            }
        }

        return new PlaySummaryMessage(civs, mine, best, SiteScores.Criteria, criteria);
    }

    private List<PlayRouteView> RoutesOf(int civ)
    {
        var list = new List<PlayRouteView>();
        for (int i = 0; i < _game.Routes.Count; i++)
        {
            TradeRoute r = _game.Routes[i];
            if ((r.Civ == civ || civ < 0) && !r.Ended) // the whole map: every civilization's routes (observer full view)
            {
                list.Add(new PlayRouteView(i, r.From, r.To, r.Good, (int)(r.Amount / SimContent.One), r.Caravan >= 0, r.Path.Count > 0 ? Pairs(r.Path, 0) : null));
            }
        }

        return list;
    }

    // What a prospector or surveyor knows around it: deposit id, column, row for each.
    private int[] Found(Unit u)
    {
        var list = new List<int>();
        foreach ((int d, int cell) in _game.FoundAround(u))
        {
            HexCoord h = _game.Geo.FromIndex(cell);
            list.Add(d);
            list.Add(h.Col);
            list.Add(h.Row);
        }

        return [.. list];
    }

    private int[] TakenStarts()
    {
        var list = new List<int>();
        foreach (int cell in _game.BlockCenter)
        {
            if (cell >= 0 && _game.Picking)
            {
                HexCoord h = _game.Geo.FromIndex(cell);
                list.Add(h.Col);
                list.Add(h.Row);
            }
        }

        return [.. list];
    }

    private List<PlayKnownCivView> KnownCivs(Civ civ)
    {
        var list = new List<PlayKnownCivView>();
        if (civ.Id < 0)
        {
            // The watcher (observers, fallen players): every civilization, without our relations to it.
            foreach (Civ other in _game.Civs)
            {
                list.Add(new PlayKnownCivView(other.Id, other.Name, false, 0, 0, 0, other.Reputation, null, other.Dead, false, false, false, false, null, 0, 0, null, null, null, 0, false, false, 0));
            }

            return list;
        }

        foreach (Civ other in _game.Civs)
        {
            // Civilizations we have seen, and civilizations that have seen us and spoke to us (an offer) or fight us.
            bool known = _game.Met(civ, other) || _game.AtWar(civ.Id, other.Id)
                || _game.TreatyOffers.Exists(o => o.From == other.Id && o.To == civ.Id) || _game.PeaceOffers.Exists(o => o.From == other.Id && o.To == civ.Id);
            if (other.Id == civ.Id || !known)
            {
                continue;
            }

            PeaceOffer? offer = _game.PeaceOffers.Find(o => o.From == other.Id && o.To == civ.Id);
            bool war = _game.AtWar(civ.Id, other.Id);
            list.Add(new PlayKnownCivView(
                other.Id, other.Name, war, _game.Score(civ.Id, other.Id), _game.Score(other.Id, civ.Id),
                war ? _game.Favour(civ.Id, other.Id) : 0, other.Reputation, offer is null ? null : _game.TermsText(offer.Terms, other.Id, civ.Id), other.Dead,
                _game.Has(civ.Id, other.Id, TreatyKind.Trade), _game.Has(civ.Id, other.Id, TreatyKind.Alliance),
                _game.TreatyOffers.Exists(o => o.From == other.Id && o.To == civ.Id && o.Kind == TreatyKind.Trade),
                _game.TreatyOffers.Exists(o => o.From == other.Id && o.To == civ.Id && o.Kind == TreatyKind.Alliance),
                _game.BarterOffers.Find(o => o.From == other.Id && o.To == civ.Id) is { } b
                    ? $"{b.GiveAmount / SimContent.One} {_game.Content.Goods[b.GiveGood].Name.ToLowerInvariant()} for {b.WantAmount / SimContent.One} {_game.Content.Goods[b.WantGood].Name.ToLowerInvariant()}"
                    : null,
                _game.Weariness(civ.Id, other.Id), _game.Weariness(other.Id, civ.Id),
                war ? TheirGoods(civ, other) : null,
                war ? [.. other.Settlements.Where(sid => _game.KnowsSettlement(civ, _game.Settlements[sid]) && _game.SettlementTermRefused(civ, _game.Settlements[sid]) is null)] : null,
                war ? Encode(_game.SuggestTerms(civ.Id, other.Id)) : null,
                _game.WarWorkLoss(civ), _game.Aggressor(civ.Id, other.Id), _game.Aggressor(other.Id, civ.Id),
                war ? _game.FairWorth(civ.Id, other.Id) / SimContent.One : 0));
        }

        return list;
    }

    // 1B (ADR 0030): the peace window shows how much of each good we know the other civilization has (whole units).
    private long[] TheirGoods(Civ civ, Civ other)
    {
        var goods = new long[_game.Content.Goods.Count];
        for (int g = 0; g < goods.Length; g++)
        {
            goods[g] = _game.KnowsGood(civ, g) ? other.Stock[g] / SimContent.One : -1;
        }

        return goods;
    }

    // Terms as [we give 0/1, length, settlement, good, amount, ...] in whole units.
    private static int[] Encode(PeaceTerms t)
    {
        var list = new List<int> { t.WeGive ? 1 : 0, t.Length, t.Settlement };
        foreach ((int good, long amount) in t.Goods)
        {
            list.Add(good);
            list.Add((int)Math.Min(int.MaxValue, amount / SimContent.One));
        }

        return [.. list];
    }

    // The terms of a peace command: Cells are pairs of good and whole amount, C the settlement plus one.
    private static PeaceTerms PeaceTermsOf(PlayCommandMessage m)
    {
        var goods = new List<(int, long)>();
        if (m.Cells is { } cells)
        {
            for (int k = 0; k + 1 < cells.Length; k += 2)
            {
                goods.Add((cells[k], (long)Math.Max(0, cells[k + 1]) * SimContent.One));
            }
        }

        return new PeaceTerms(m.Flag, m.B, m.C - 1, goods);
    }

    private int ResearchPermille(Civ civ)
    {
        if (civ.Researching < 0)
        {
            return 0;
        }

        SimTech r = _game.Content.Techs[civ.Researching];
        int total = Math.Max(1, r.ResearchTicks * _game.Content.Pace.Seconds(r.Era) / 100);
        return 1000 - (int)Math.Clamp((civ.ResearchEnd - _game.Ticks) * 1000 / total, 0, 1000);
    }

    private List<PlayTechState> TechStates(Civ civ)
    {
        var list = new List<PlayTechState>();
        foreach (SimTech t in _game.Content.Techs)
        {
            list.Add(civ.KnownTech[t.Id] ? new PlayTechState(t.Id, true, [], null) : new PlayTechState(t.Id, false, _game.ResearchPrice(civ, t.Id), _game.CannotResearch(civ, t.Id), _game.ResearchTicks(civ, t.Id) / SimContent.TicksPerSecond));
        }

        return list;
    }

    private PlayPersonView? Person(Sim.State.Person? p, bool advisor) => p is null ? null : new PlayPersonView(
        p.FullName, _game.Content.Rarities[p.Rarity].Name, advisor ? 100 : _game.Edge(p, cost: true), advisor ? 100 : _game.Edge(p, cost: false),
        advisor ? _game.FieldsText(p) : "", _game.HirePrice(p, advisor));

    private sealed class PlayClient(IConnection connection, int cells)
    {
        public IConnection Connection { get; } = connection;

        /// <summary>The nickname (from the lobby, or made unique at Hello); null until then.</summary>
        public string? Name { get; set; }

        /// <summary>Characters the game added to a repeated nickname.</summary>
        public int SuffixLength { get; set; }

        /// <summary>The player who opened the lobby.</summary>
        public bool Host { get; init; }

        /// <summary>Time a message takes there and back, measured with <see cref="PlayPingMessage"/>.</summary>
        public int PingMs { get; set; }

        private double _chatTokens = -1;
        private long _chatRefilled;

        /// <summary>True when one more line of chat fits the limit (<paramref name="perSecond"/>, at once <paramref name="burst"/>).</summary>
        public bool MayChat(long now, int perSecond, int burst)
        {
            _chatTokens = _chatTokens < 0 ? burst : Math.Min(burst, _chatTokens + ((now - _chatRefilled) * (double)perSecond / Stopwatch.Frequency));
            _chatRefilled = now;
            if (_chatTokens < 1)
            {
                return false;
            }

            _chatTokens--;
            return true;
        }

        public int Civ { get; set; } = -1;

        /// <summary>What this client is in the game (<see cref="PlayPlayerInfo"/> roles); −1 before Hello.</summary>
        public int Role { get; set; } = -1;

        /// <summary>When it began to lead its civilization (a vote on players needs a while in the game, R4-1).</summary>
        public long Since { get; set; }

        /// <summary>Its views show the whole map (an observer, or a player whose civilization fell).</summary>
        public bool Watching { get; set; }

        /// <summary>A spectator watching through the eyes of this civilization, read only (−1: the whole map; observer full view).</summary>
        public int Eye { get; set; } = -1;

        /// <summary>The civilization whose eyes the views of a spectator were built with (to notice a change).</summary>
        public int SentEye { get; set; } = -1;

        /// <summary>Lines of the intentions log this spectator has (their number in <c>_intentCount</c>).</summary>
        public long IntentsSent { get; set; }

        /// <summary>A removed player who came back: the return key it was removed with (it may ask only for its old civilization).</summary>
        public string? Removed { get; set; }

        public int Cells { get; } = cells;

        // What this client has been told, hex by hex: the server sends only changes against it.
        public byte[] SentVisibility { get; } = new byte[cells];

        public short[] SentOwner { get; } = Filled(cells);

        public bool[] SentTerrain { get; } = new bool[cells];

        public byte[] SentRoad { get; } = new byte[cells];

        public ushort[] SentWorks { get; } = new ushort[cells];

        public byte[] SentSurvey { get; } = new byte[cells];

        // Signatures of the objects this client has, by id: only changes are sent.
        public Dictionary<int, int> SentBuildings { get; } = [];

        public Dictionary<int, int> SentSettlements { get; } = [];

        public Dictionary<int, int> SentUnits { get; } = [];

        public int SentTechs { get; set; }

        public long LastFullView { get; set; } = long.MinValue;

        public List<string> News { get; } = [];

        private static short[] Filled(int n)
        {
            var a = new short[n];
            System.Array.Fill(a, (short)-1);
            return a;
        }

        public bool SentChunks { get; set; }

        /// <summary>The content went out (a candidate gets it at Hello, before it leads a civilization).</summary>
        public bool SentContent { get; set; }

        public bool ViewSoon { get; set; }

        public bool SawPicking { get; set; }

        /// <summary>Changed map parts of rounds skipped while the connection was congested.</summary>
        public HashSet<int> MissedChunks { get; } = [];

        /// <summary>The people this client leads after Hello (its place in the lobby), or −1 for the next free one.</summary>
        public int Place { get; init; } = -1;

        /// <summary>Messages of this client that failed on the server.</summary>
        public int Faults { get; set; }

        /// <summary>Messages refused for coming too fast.</summary>
        public int Flooded { get; private set; }

        private double _tokens = MessageBurst;
        private long _refilled = long.MinValue;

        /// <summary>True when one more message fits the limit (a bucket of <see cref="MessageBurst"/>, refilled at <see cref="MessagesPerSecond"/>).</summary>
        public bool Allow(long now)
        {
            if (_refilled != long.MinValue)
            {
                _tokens = Math.Min(MessageBurst, _tokens + ((now - _refilled) * (double)MessagesPerSecond / Stopwatch.Frequency));
            }

            _refilled = now;
            if (_tokens < 1)
            {
                Flooded++;
                return false;
            }

            _tokens--;
            return true;
        }
    }
}
