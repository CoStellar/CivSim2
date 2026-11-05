using System;
using System.Collections.Generic;
using System.Diagnostics;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;

namespace CivSim.Server;

/// <summary>
/// The authoritative game server. It owns the game state, is the only thing that runs the simulation,
/// checks every command and sends each player a filtered view. In single player it runs in the same
/// process as the client, connected by <see cref="InMemoryConnection"/>.
/// </summary>
public sealed class GameServer
{
    /// <summary>Start cells within this radius are known to a player until fog of war arrives (Stage 3).</summary>
    public const int StartKnowledgeRadius = 4;

    private readonly List<Client> _clients = [];
    private readonly GameContent? _content;
    private readonly List<ChronicleEntry> _chronicle = [];
    private readonly List<ChronicleEntry> _history = [];
    private readonly bool[][] _pendingChunks;
    private long _lastTurnTicks;
    private int _nextPlayerId = 1;

    /// <summary>Creates a server for a new game with an empty map (tests, benchmarks).</summary>
    public GameServer(GameSettings settings)
        : this(GameState.CreateNew(settings))
    {
    }

    /// <summary>Creates a server for an existing (e.g. loaded) game.</summary>
    public GameServer(GameState state, GameContent? content = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        Runner = new TurnRunner(state);
        _content = content;
        if (content is not null)
        {
            CoreSystems.Register(Runner, content);
        }
        _pendingChunks = new bool[state.Worlds.Count][];
        for (int w = 0; w < state.Worlds.Count; w++)
        {
            _pendingChunks[w] = new bool[state.Worlds[w].Map.Chunks.Count];
        }
    }

    /// <summary>Creates a new game and generates its world (GDD 3).</summary>
    public static GameServer CreateNew(GameSettings settings, GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        GameState state = GameState.CreateNew(settings, content, out WorldGenReport report);
        var server = new GameServer(state, content);
        server.AddChronicle(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"World generated from seed {settings.Seed}: {settings.MapWidth}x{settings.MapHeight}, {settings.WorldType}, {report.WaterPercent}% water, {report.Rivers} rivers, {report.Lakes} lakes."));
        server.AddChronicle(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{report.Starts.Count} tribes start at least {report.MinStartDistance} cells apart (fairness {report.StartFairnessPercent}%)."));
        return server;
    }

    /// <summary>Speed chosen by an observer. The host uses it to pace turns.</summary>
    public SimulationSpeed Speed { get; private set; } = SimulationSpeed.Paused;

    /// <summary>True when chronicle lines wait to be sent (for example after a speed change while paused).</summary>
    public bool HasUnsentChronicle => _chronicle.Count > 0 && _clients.Exists(c => c.Player is not null);

    /// <summary>Chronicle lines kept for clients that join later.</summary>
    public const int ChronicleHistory = 500;

    /// <summary>Number of connected observers.</summary>
    public int ConnectedObservers => _clients.FindAll(c => c.IsObserver).Count;

    /// <summary>The turn runner (systems and AI register here).</summary>
    public TurnRunner Runner { get; }

    /// <summary>The authoritative state. Never hand this to a client.</summary>
    public GameState State => Runner.State;

    /// <summary>Number of connected players.</summary>
    public int ConnectedPlayers => _clients.Count;

    /// <summary>Accepts a new connection. The player is welcomed once its Hello message is processed.</summary>
    public void Accept(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        _clients.Add(new Client(connection));
    }

    /// <summary>
    /// Processes all waiting messages. Runs a turn when every welcomed player has ended the current turn.
    /// Returns true when a turn was run.
    /// </summary>
    public bool Pump()
    {
        for (int i = _clients.Count - 1; i >= 0; i--)
        {
            Client client = _clients[i];
            if (!client.Connection.IsOpen)
            {
                Disconnect(client);
                _clients.RemoveAt(i);
                continue;
            }

            while (client.Connection.TryReceive(out IProtocolMessage? message))
            {
                Handle(client, message!);
            }
        }

        bool anyPlayer = _clients.Exists(c => c.Civ is not null);
        if (anyPlayer && _clients.TrueForAll(c => c.Civ is null || c.ReadyForTurn == State.Turn))
        {
            AdvanceTurn();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Runs one turn now (observer mode, headless, or a turn timer). With <paramref name="sendViews"/> false the
    /// changed chunks are remembered and sent by the next <see cref="SendViews"/> (used at maximum speed).
    /// </summary>
    public TurnReport AdvanceTurn(bool sendViews = true)
    {
        TurnReport report = Runner.RunTurn();
        _lastTurnTicks = report.TotalTicks;
        foreach (string line in Runner.LastChronicle)
        {
            AddChronicle(line);
        }

        GameSnapshot? snapshot = Runner.Snapshots.Latest;
        if (snapshot is not null)
        {
            for (int w = 0; w < snapshot.Worlds.Length && w < _pendingChunks.Length; w++)
            {
                foreach (int chunk in snapshot.Worlds[w].ChangedChunks)
                {
                    _pendingChunks[w][chunk] = true;
                }
            }
        }

        foreach (RejectedCommand rejected in report.Rejected)
        {
            Client? owner = _clients.Find(c => c.Player == rejected.Envelope.Player);
            owner?.Connection.Send(new CommandRejectedMessage(owner.SequenceOf(rejected.Envelope.Sequence), rejected.Reason));
        }

        foreach (Client client in _clients)
        {
            client.ForgetSequences();
        }

        if (sendViews)
        {
            SendViews();
        }

        return report;
    }

    private void Handle(Client client, IProtocolMessage message)
    {
        switch (message)
        {
            case HelloMessage hello:
                HandleHello(client, hello);
                break;
            case SubmitCommandMessage submit:
                HandleSubmit(client, submit);
                break;
            case SetSpeedMessage speed:
                if (client.IsObserver)
                {
                    if (speed.Speed != Speed)
                    {
                        Speed = speed.Speed;
                        AddChronicle($"Speed set to {Speed}.");
                    }
                }
                else
                {
                    client.Connection.Send(new ConnectionRejectedMessage("Only observers can change the speed."));
                }

                break;
            case EndTurnMessage end:
                if (client.Civ is not null && end.Turn == State.Turn)
                {
                    client.ReadyForTurn = State.Turn;
                }

                break;
            default:
                client.Connection.Send(new ConnectionRejectedMessage($"Unexpected message {message.GetType().Name}."));
                break;
        }
    }

    private void HandleHello(Client client, HelloMessage hello)
    {
        if (client.Player is not null)
        {
            return;
        }

        if (hello.ProtocolVersion != ProtocolInfo.Version)
        {
            client.Connection.Send(new ConnectionRejectedMessage($"Server speaks protocol {ProtocolInfo.Version}."));
            client.Connection.Close();
            return;
        }

        if (hello.Role == ClientRole.Observer)
        {
            client.Player = new PlayerId(_nextPlayerId++);
            client.IsObserver = true;
            client.Connection.Send(new WelcomeMessage(ProtocolInfo.Version, client.Player.Value.Value, WelcomeMessage.NoCiv));
            SendContent(client);
            client.Connection.Send(BuildView(client, Runner.Snapshots.Latest ?? GameSnapshot.From(State), joining: true));
            client.JustJoined = true;
            SendAllChunks(client);
            return;
        }

        CivilizationState? civ = null;
        foreach (CivilizationState c in State.Civilizations)
        {
            if (c.IsAlive && c.Controller == PlayerId.Ai)
            {
                civ = c;
                break;
            }
        }

        if (civ is null)
        {
            client.Connection.Send(new ConnectionRejectedMessage("No free civilization."));
            client.Connection.Close();
            return;
        }

        client.Player = new PlayerId(_nextPlayerId++);
        client.Civ = civ.Id;
        civ.Controller = client.Player.Value;
        client.ReadyForTurn = -1;
        client.Connection.Send(new WelcomeMessage(ProtocolInfo.Version, client.Player.Value.Value, civ.Id.Value));
        client.Known = KnownCells(civ);
        SendContent(client);
        client.Connection.Send(BuildView(client, Runner.Snapshots.Latest ?? GameSnapshot.From(State), joining: true));
        client.JustJoined = true;
        SendAllChunks(client);
    }

    // Until perception (Stage 3), a player knows the cells around its start.
    private bool[] KnownCells(CivilizationState civ)
    {
        WorldState earth = State.Worlds[0];
        var known = new bool[earth.Geometry.CellCount];
        int radius = Math.Min(StartKnowledgeRadius, earth.Geometry.MaxUniqueRadius);
        var area = new HexCoord[HexGlobe.RangeSize(radius)];
        int count = earth.Geometry.Range(civ.StartCell, radius, area);
        for (int k = 0; k < count; k++)
        {
            known[earth.Geometry.Index(area[k])] = true;
        }

        return known;
    }

    private void SendContent(Client client)
    {
        if (_content is null)
        {
            return;
        }

        var biomes = new List<BiomeInfo>(_content.Biomes.Count);
        foreach (BiomeDefinition b in _content.Biomes)
        {
            biomes.Add(new BiomeInfo(b.Id, b.Key, b.Name, b.IsWater, b.Color));
        }

        var deposits = new List<DepositInfo>(_content.Deposits.Count);
        foreach (DepositDefinition d in _content.Deposits)
        {
            deposits.Add(new DepositInfo(d.Id, d.Key, d.Name, d.Color));
        }

        var goods = new List<GoodInfo>(_content.Goods.Count);
        foreach (GoodDefinition g in _content.Goods)
        {
            goods.Add(new GoodInfo(g.Id, g.Key, g.Name, g.IsFood, g.Color));
        }

        var levels = new List<string>(_content.SettlementLevels.Count);
        foreach (SettlementLevelDefinition l in _content.SettlementLevels)
        {
            levels.Add(l.Name);
        }

        var strata = new List<string>(_content.Strata.Count);
        foreach (StratumDefinition s in _content.Strata)
        {
            strata.Add(s.Name);
        }

        client.Connection.Send(new ContentMessage(biomes, deposits, Enum.GetNames<TerrainFeature>(), Enum.GetNames<Relief>(), goods, levels, strata));
    }

    private void SendAllChunks(Client client)
    {
        foreach (WorldState world in State.Worlds)
        {
            for (int chunk = 0; chunk < world.Map.Chunks.Count; chunk++)
            {
                SendChunk(client, world, chunk);
            }
        }
    }

    private void SendChunk(Client client, WorldState world, int chunk)
    {
        // Players only see Earth until space travel.
        if (!client.IsObserver && world.Id != WorldId.Earth)
        {
            return;
        }

        MapChunkMessage? message = MapViewBuilder.Build(world, chunk, State.Turn, client.IsObserver ? null : client.Known);
        if (message is not null)
        {
            client.Connection.Send(message);
        }
    }

    /// <summary>Adds a line to the chronicle; it is sent with the next views.</summary>
    public void AddChronicle(string text)
    {
        var entry = new ChronicleEntry(State.Turn, text);
        _chronicle.Add(entry);
        _history.Add(entry);
        if (_history.Count > ChronicleHistory)
        {
            _history.RemoveRange(0, _history.Count - ChronicleHistory);
        }
    }

    // A joining client gets the whole kept history; later views carry only the new lines.
    private GameViewMessage BuildView(Client client, GameSnapshot snapshot, bool joining = false, bool skipChronicle = false)
    {
        GameViewMessage view = ViewBuilder.Build(
            snapshot, client.Civ ?? new CivId(WelcomeMessage.NoCiv), client.IsObserver ? null : client.Known ?? [], State.Worlds[0].Width);
        long micros = (long)Stopwatch.GetElapsedTime(0, _lastTurnTicks).TotalMicroseconds;
        return view with { TurnMicroseconds = micros, Speed = Speed, Chronicle = joining ? _history.ToArray() : skipChronicle ? [] : _chronicle.ToArray() };
    }

    private void HandleSubmit(Client client, SubmitCommandMessage submit)
    {
        if (client.Player is not { } player || client.Civ is not { } civ)
        {
            client.Connection.Send(new CommandRejectedMessage(submit.ClientSequence, "Say hello first."));
            return;
        }

        if (submit.CivId != civ.Value)
        {
            client.Connection.Send(new CommandRejectedMessage(submit.ClientSequence, "You do not control that civilization."));
            return;
        }

        ICommand? command = CommandMapper.ToCore(submit.Command, _content);
        if (command is null)
        {
            client.Connection.Send(new CommandRejectedMessage(submit.ClientSequence, "Unknown command."));
            return;
        }

        CommandEnvelope envelope = Runner.Commands.Enqueue(player, civ, command);
        client.RememberSequence(envelope.Sequence, submit.ClientSequence);
    }

    /// <summary>
    /// Sends every client its view, the chronicle lines since the last send and the map chunks that changed.
    /// </summary>
    public void SendViews()
    {
        GameSnapshot snapshot = Runner.Snapshots.Latest ?? GameSnapshot.From(State);
        foreach (Client client in _clients)
        {
            if (client.Player is null || !client.Connection.IsOpen)
            {
                continue;
            }

            client.Connection.Send(BuildView(client, snapshot, skipChronicle: client.JustJoined));
            client.JustJoined = false;
            for (int w = 0; w < _pendingChunks.Length; w++)
            {
                for (int chunk = 0; chunk < _pendingChunks[w].Length; chunk++)
                {
                    if (_pendingChunks[w][chunk])
                    {
                        SendChunk(client, State.Worlds[w], chunk);
                    }
                }
            }
        }

        foreach (bool[] world in _pendingChunks)
        {
            Array.Clear(world);
        }

        _chronicle.Clear();
    }

    private void Disconnect(Client client)
    {
        // The AI takes over a disconnected civilization (GDD 2.1).
        if (client.Civ is { } civ && State.FindCivilization(civ) is { } state)
        {
            state.Controller = PlayerId.Ai;
        }
    }

    private sealed class Client(IConnection connection)
    {
        private readonly Dictionary<long, long> _clientSequences = [];

        public IConnection Connection { get; } = connection;

        public PlayerId? Player { get; set; }

        public CivId? Civ { get; set; }

        public long ReadyForTurn { get; set; } = -1;

        public bool IsObserver { get; set; }

        public bool[]? Known { get; set; }

        // Got the whole history on joining, so the lines still waiting to be sent are not new to it.
        public bool JustJoined { get; set; }

        public void RememberSequence(long serverSequence, long clientSequence) => _clientSequences[serverSequence] = clientSequence;

        public long SequenceOf(long serverSequence) => _clientSequences.GetValueOrDefault(serverSequence, -1);

        public void ForgetSequences() => _clientSequences.Clear();
    }
}
