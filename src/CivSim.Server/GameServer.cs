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
    private readonly ObserverViewBuilder? _observerView;
    private long _lastTurnTicks;
    private int _nextPlayerId = 1;

    /// <summary>Creates a server for a new game with an empty map (tests, benchmarks).</summary>
    public GameServer(GameSettings settings)
        : this(GameState.CreateNew(settings))
    {
    }

    /// <summary>Creates a server for an existing (e.g. loaded) game.</summary>
    /// <summary>Ticks in a real second at speed x1 (data/time.json; 10 without content).</summary>
    public int TicksPerSecond => _content?.Time.TicksPerSecond ?? 10;

    public GameServer(GameState state, GameContent? content = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        Runner = new TurnRunner(state);
        _content = content;
        if (content is not null)
        {
            CoreSystems.Register(Runner, content);
            _observerView = new ObserverViewBuilder(content);
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

    /// <summary>Duration of the last turn in microseconds.</summary>
    public long LastTurnMicroseconds => (long)Stopwatch.GetElapsedTime(0, _lastTurnTicks).TotalMicroseconds;

    /// <summary>Pauses the simulation from the server side (turn limit of the start menu) and says why in the chronicle.</summary>
    public void Pause(string reason)
    {
        Speed = SimulationSpeed.Paused;
        AddChronicle(reason);
    }

    /// <summary>True when chronicle lines wait to be sent (for example after a speed change while paused).</summary>
    public bool HasUnsentChronicle => _chronicle.Count > 0 && _clients.Exists(c => c.Player is not null);

    /// <summary>Chronicle lines kept for clients that join later, and at most waiting for a client that fell behind.</summary>
    public const int ChronicleHistory = 500;

    /// <summary>
    /// A client with more unread messages than this gets no new view until it catches up; the map chunks and chronicle lines
    /// it missed are kept for it and sent once, with the latest view. Without this a window that reads slower than the
    /// simulation runs (maximum speed, a minimized window) piled up gigabytes of old views.
    /// </summary>
    public const int MaxBacklog = 256;

    /// <summary>True when a client that fell behind has caught up and waits for the view it missed.</summary>
    public bool HasDeferredViews => _clients.Exists(c => c.Deferred && c.Connection.IsOpen && c.Connection.Backlog <= MaxBacklog);

    /// <summary>Most unread messages any open client has (long-run statistics).</summary>
    public int MaxClientBacklog
    {
        get
        {
            int max = 0;
            foreach (Client c in _clients)
            {
                max = c.Connection.IsOpen ? Math.Max(max, c.Connection.Backlog) : max;
            }

            return max;
        }
    }

    /// <summary>Clients that fell behind and wait for the view they missed (long-run statistics).</summary>
    public int ClientsBehind => _clients.FindAll(c => c.Deferred && c.Connection.IsOpen).Count;

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
        _clients.Add(new Client(connection, _pendingChunks));
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
            case NameSuggestionsRequestMessage names:
                HandleSuggestions(client, names);
                break;
            case WatchCivilizationMessage watch:
                if (client.IsObserver)
                {
                    client.Watched = watch.CivId < 0 || State.FindCivilization(new CivId(watch.CivId)) is null ? null : new CivId(watch.CivId);
                    SendObserverDetails(client);
                }
                else
                {
                    client.Connection.Send(new ConnectionRejectedMessage("Only observers can watch another civilization."));
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
            client.ChronicleSeen = _chronicle.Count;
            SendAllChunks(client);
            SendObserverDetails(client);
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
        client.ChronicleSeen = _chronicle.Count;
        SendAllChunks(client);
    }

    // What the player's civilization knows (map memory, stage 3g); without content, the cells around its start.
    private bool[] KnownCells(CivilizationState civ)
    {
        if (_content is not null && civ.SeenYear.Length > 0)
        {
            return Core.Perception.MapMemory.KnownCells(_content, State, civ);
        }

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
    private GameViewMessage BuildView(Client client, GameSnapshot snapshot, bool joining = false)
    {
        GameViewMessage view = ViewBuilder.Build(
            snapshot, client.Civ ?? new CivId(WelcomeMessage.NoCiv), client.IsObserver ? null : client.Known ?? [], State.Worlds[0].Width);
        long micros = (long)Stopwatch.GetElapsedTime(0, _lastTurnTicks).TotalMicroseconds;
        if (client.IsObserver && _observerView is not null)
        {
            view = view with { Civilizations = _observerView.Civilizations(State, view.Civilizations) };
        }

        // A joining client gets the kept history; later views carry the lines collected for this client since its last view.
        ChronicleEntry[] lines = joining ? _history.ToArray() : client.Unsent.ToArray();
        return view with { TurnMicroseconds = micros, Speed = Speed, Chronicle = lines };
    }

    private void HandleSubmit(Client client, SubmitCommandMessage submit)
    {
        // Observers may rename any civilization or settlement (GDD 5.6). Names change nothing in the simulation, so
        // the server applies the command at once, between turns, instead of waiting for the next turn.
        if (client.IsObserver && submit.Command is RenameCivilizationDto or RenameSettlementDto && client.Player is not null)
        {
            RenameNow(client, submit);
            return;
        }

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

    private void RenameNow(Client client, SubmitCommandMessage submit)
    {
        var target = new CivId(submit.CivId);
        if (submit.Command is RenameSettlementDto rs && State.FindSettlement(new SettlementId(rs.SettlementId)) is { } settlement)
        {
            target = settlement.Owner;
        }

        ICommand? command = CommandMapper.ToCore(submit.Command, _content);
        string before = submit.Command is RenameSettlementDto r2 ? State.FindSettlement(new SettlementId(r2.SettlementId))?.Name ?? string.Empty : State.FindCivilization(target)?.Name ?? string.Empty;
        CommandResult check = command is null ? CommandResult.Fail("Unknown command.") : command.Validate(State, target);
        if (!check.Success)
        {
            client.Connection.Send(new CommandRejectedMessage(submit.ClientSequence, check.Reason ?? "Refused."));
            return;
        }

        command!.Apply(State, target);
        string after = submit.Command is RenameSettlementDto r3 ? State.FindSettlement(new SettlementId(r3.SettlementId))!.Name : State.FindCivilization(target)!.Name;
        AddChronicle($"{before} was renamed {after}.");
        Runner.Snapshots.Publish(GameSnapshot.From(State));
        SendViews();
    }

    private void HandleSuggestions(Client client, NameSuggestionsRequestMessage request)
    {
        if (_content is null)
        {
            return;
        }

        CivilizationState? civ = request.Target == NameTarget.Civilization
            ? State.FindCivilization(new CivId(request.Id))
            : State.FindSettlement(new SettlementId(request.Id)) is { } s ? State.FindCivilization(s.Owner) : null;
        if (civ is null || (!client.IsObserver && client.Civ != civ.Id))
        {
            client.Connection.Send(new ConnectionRejectedMessage("No names for that."));
            return;
        }

        List<string> names = Core.Naming.Names.Suggest(_content.Names, State, civ, request.Target == NameTarget.Civilization, Math.Clamp(request.Count, 1, 24), request.Page);
        client.Connection.Send(new NameSuggestionsMessage(request.Target, request.Id, request.Page, names));
    }

    /// <summary>
    /// Sends every client its view, the chronicle lines since the last send and the map chunks that changed.
    /// </summary>
    public void SendViews()
    {
        GameSnapshot snapshot = Runner.Snapshots.Latest ?? GameSnapshot.From(State);

        // Every client collects what changed since its last view: chunks and chronicle lines (a client that just joined
        // got the history, which held the lines waiting then, so only later lines are new to it).
        foreach (Client client in _clients)
        {
            if (client.Player is null)
            {
                continue;
            }

            client.Collect(_pendingChunks, _chronicle, client.JustJoined ? Math.Min(client.ChronicleSeen, _chronicle.Count) : 0);
            client.JustJoined = false;
        }

        foreach (bool[] world in _pendingChunks)
        {
            Array.Clear(world);
        }

        _chronicle.Clear();
        foreach (Client client in _clients)
        {
            if (client.Player is null || !client.Connection.IsOpen)
            {
                continue;
            }

            // Behind: no new view until it reads what it has (the latest view goes out once it catches up).
            if (client.Connection.Backlog > MaxBacklog)
            {
                client.Deferred = true;
                continue;
            }

            client.Deferred = false;

            // The fog of war follows the civilization's memory: newly known (or forgotten) cells go out with their chunks.
            if (!client.IsObserver && client.Civ is CivId civId && State.FindCivilization(civId) is { } civ && client.Known is not null)
            {
                bool[] known = KnownCells(civ);
                WorldState earth = State.Worlds[0];
                for (int i = 0; i < known.Length && i < client.Known.Length; i++)
                {
                    if (known[i] != client.Known[i])
                    {
                        client.Chunks[0][earth.Map.Chunks.ChunkOfIndex(i)] = true;
                    }
                }

                client.Known = known;
            }

            client.Connection.Send(BuildView(client, snapshot));
            client.Unsent.Clear();
            for (int w = 0; w < client.Chunks.Length; w++)
            {
                for (int chunk = 0; chunk < client.Chunks[w].Length; chunk++)
                {
                    if (client.Chunks[w][chunk])
                    {
                        SendChunk(client, State.Worlds[w], chunk);
                        client.Chunks[w][chunk] = false;
                    }
                }
            }

            SendObserverDetails(client);
        }
    }

    // Observers also get the developer layers and, when they watch a civilization, its fog (stage 5).
    private void SendObserverDetails(Client client)
    {
        if (!client.IsObserver || _observerView is null)
        {
            return;
        }

        client.Connection.Send(_observerView.Build(State));
        if (client.Watched is { } civ)
        {
            if (_observerView.Fog(State, civ) is { } fog)
            {
                client.Connection.Send(fog);
            }

            if (_observerView.Details(State, civ) is { } details)
            {
                client.Connection.Send(details);
            }
        }
    }

    private void Disconnect(Client client)
    {
        // The AI takes over a disconnected civilization (GDD 2.1).
        if (client.Civ is { } civ && State.FindCivilization(civ) is { } state)
        {
            state.Controller = PlayerId.Ai;
        }
    }

    private sealed class Client(IConnection connection, bool[][] chunkShape)
    {
        private readonly Dictionary<long, long> _clientSequences = [];

        // Map chunks changed since this client's last view, per world.
        public bool[][] Chunks { get; } = Array.ConvertAll(chunkShape, w => new bool[w.Length]);

        // Chronicle lines since this client's last view (the newest ChronicleHistory lines when it fell behind).
        public List<ChronicleEntry> Unsent { get; } = [];

        // It fell behind and missed a view.
        public bool Deferred { get; set; }

        // Takes the changed chunks and the new chronicle lines (from index `from`).
        public void Collect(bool[][] changed, List<ChronicleEntry> lines, int from)
        {
            for (int w = 0; w < changed.Length && w < Chunks.Length; w++)
            {
                for (int c = 0; c < changed[w].Length; c++)
                {
                    Chunks[w][c] |= changed[w][c];
                }
            }

            for (int k = from; k < lines.Count; k++)
            {
                Unsent.Add(lines[k]);
            }

            if (Unsent.Count > ChronicleHistory)
            {
                Unsent.RemoveRange(0, Unsent.Count - ChronicleHistory);
            }
        }

        public IConnection Connection { get; } = connection;

        public PlayerId? Player { get; set; }

        public CivId? Civ { get; set; }

        public long ReadyForTurn { get; set; } = -1;

        public bool IsObserver { get; set; }

        public bool[]? Known { get; set; }

        // Civilization whose fog an observer looks at, if any.
        public CivId? Watched { get; set; }

        // Got the whole history on joining, so the lines still waiting to be sent are not new to it.
        public bool JustJoined { get; set; }

        // Lines of the pending chronicle that were already in the history sent on joining.
        public int ChronicleSeen { get; set; }

        public void RememberSequence(long serverSequence, long clientSequence) => _clientSequences[serverSequence] = clientSequence;

        public long SequenceOf(long serverSequence) => _clientSequences.GetValueOrDefault(serverSequence, -1);

        public void ForgetSequences() => _clientSequences.Clear();
    }
}
