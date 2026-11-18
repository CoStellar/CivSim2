using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <summary>Peoples choose where they start (the player too); false starts at once on the generator's places.</summary>
    public bool StartPhase { get; init; } = true;

    /// <summary>What wins the game: 0 last standing, 1 first to the Middle Ages, 2 best score after 2 hours (protocol only, no core types).</summary>
    public int Goal { get; init; } = 1;
}

/// <summary>
/// Runs a game of the new simulation on its own thread and talks to clients through the protocol only (the client never sees
/// <c>CivSim.Sim</c>). Ten ticks a second at ×1. The player leads civilization 0.
/// </summary>
public sealed class PlayHost : IDisposable
{
    private static readonly TimeSpan ViewInterval = TimeSpan.FromMilliseconds(100);
    private readonly ConcurrentQueue<IConnection> _pending = new();
    private readonly List<PlayClient> _clients = [];
    private readonly Thread _thread;
    private readonly SimGame _game;
    private readonly List<int> _dirty = [];
    private readonly List<string> _news = [];
    private volatile bool _stopping;
    private volatile string? _error;
    private SimulationSpeed _speed = SimulationSpeed.Paused;

    private PlayHost(SimGame game)
    {
        _game = game;
        _thread = new Thread(Run) { IsBackground = true, Name = "CivSim play" };
    }

    /// <summary>The error that stopped the game thread, if any.</summary>
    public string? Error => _error;

    /// <summary>Creates the game and starts its thread (paused).</summary>
    public static PlayHost Start(PlayOptions options, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        SimContent content = SimContent.Load(dataDirectory);
        SimGame game = SimGame.Create(content, new SimSettings(options.Seed, options.LegacyWidth, options.LegacyHeight, options.Civilizations)
        {
            StartPhase = options.StartPhase,
            Goal = (GameGoal)Math.Clamp(options.Goal, 0, 2),
        });
        game.Civs[0].IsAi = false; // the player leads the first people; the AI plays the others
        var host = new PlayHost(game);
        host._thread.Start();
        return host;
    }

    /// <summary>Opens an in-memory connection for a local client.</summary>
    public IConnection Connect()
    {
        (InMemoryConnection client, InMemoryConnection server) = InMemoryConnection.CreatePair();
        _pending.Enqueue(server);
        return client;
    }

    /// <summary>Stops the game thread.</summary>
    public void Dispose()
    {
        _stopping = true;
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private void Run()
    {
        try
        {
            long lastTick = Stopwatch.GetTimestamp();
            long lastView = 0;
            while (!_stopping)
            {
                while (_pending.TryDequeue(out IConnection? c))
                {
                    _clients.Add(new PlayClient(c, _game.Geo.CellCount));
                }

                foreach (PlayClient client in _clients)
                {
                    Receive(client);
                }

                _clients.RemoveAll(c => !c.Connection.IsOpen);
                int ticksPerSecond = (int)_speed * SimContent.TicksPerSecond;
                if (_speed == SimulationSpeed.Max)
                {
                    ticksPerSecond = 400;
                }

                if (ticksPerSecond > 0)
                {
                    double wait = 1.0 / ticksPerSecond;
                    int ran = 0;
                    while (Stopwatch.GetElapsedTime(lastTick).TotalSeconds >= wait && ran < 50)
                    {
                        _game.Tick();
                        foreach ((int civ, string text) in _game.News)
                        {
                            if (civ < 0 || civ == 0)
                            {
                                _news.Add(text);
                            }
                        }

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
        while (client.Connection.TryReceive(out IProtocolMessage? message))
        {
            switch (message)
            {
                case HelloMessage:
                    // Each player leads the next free people; the AI hands it over (multiplayer from the start, ADR 0028).
                    client.Civ = NextFreeCiv();
                    if (client.Civ < 0)
                    {
                        client.Connection.Send(new ConnectionRejectedMessage("Every people already has a player."));
                        break;
                    }

                    _game.Civs[client.Civ].IsAi = false;
                    client.Connection.Send(new WelcomeMessage(ProtocolInfo.Version, client.Civ, client.Civ));
                    SendContent(client);
                    break;
                case SetSpeedMessage speed:
                    string? refused = SetSpeed(client, speed.Speed);
                    if (refused is not null)
                    {
                        client.Connection.Send(new CommandRejectedMessage(0, refused));
                    }

                    break;
                case PlayCommandMessage command when client.Civ >= 0:
                    string? why = _game.Apply(ToCommand(client.Civ, command));
                    if (why is not null)
                    {
                        client.Connection.Send(new CommandRejectedMessage(command.Sequence, why));
                    }
                    else
                    {
                        client.ViewSoon = true;
                    }

                    break;
                case PlaySummaryRequestMessage when client.Civ >= 0:
                    client.Connection.Send(Summary(_game.Civs[client.Civ]));
                    break;
                case PlacesRequestMessage places when client.Civ >= 0:
                    client.Connection.Send(Places(client.Civ, places.Type));
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Pauses each player may start in a game with several players (decision of 2026-11-17).</summary>
    public const int PausesPerPlayer = 3;

    /// <summary>A pause is protected from other players for this long.</summary>
    public static readonly TimeSpan PauseProtection = TimeSpan.FromSeconds(30);

    private PlayClient? _pausedBy;
    private long _pausedAt;

    private int NextFreeCiv()
    {
        for (int c = 0; c < _game.Civs.Count; c++)
        {
            if (!_game.Civs[c].Dead && !_clients.Exists(x => x.Civ == c))
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

        if (speed == SimulationSpeed.Paused)
        {
            if (_speed == SimulationSpeed.Paused)
            {
                return null;
            }

            if (client.PausesUsed >= PausesPerPlayer)
            {
                return $"You have used your {PausesPerPlayer} pauses.";
            }

            client.PausesUsed++;
            _pausedBy = client;
            _pausedAt = Stopwatch.GetTimestamp();
            _speed = speed;
            return null;
        }

        if (_speed == SimulationSpeed.Paused && _pausedBy is not null && _pausedBy != client && Stopwatch.GetElapsedTime(_pausedAt) < PauseProtection)
        {
            return $"The pause is protected for {(int)(PauseProtection - Stopwatch.GetElapsedTime(_pausedAt)).TotalSeconds} s more.";
        }

        _pausedBy = null;
        _speed = speed;
        return null;
    }

    private SimCommand ToCommand(int civ, PlayCommandMessage m) => m.Kind switch
    {
        PlayCommandKind.Build => new BuildCommand(civ, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row)), m.A),
        PlayCommandKind.PauseConstruction => new PauseConstructionCommand(civ, m.A, m.Flag),
        PlayCommandKind.Stop => new StopBuildingCommand(civ, m.A, m.Flag),
        PlayCommandKind.Priority => new PriorityCommand(civ, m.A, m.Flag),
        PlayCommandKind.Demolish => new DemolishCommand(civ, m.A),
        PlayCommandKind.Train => new TrainCommand(civ, m.A, m.B),
        PlayCommandKind.Move => new MoveCommand(civ, m.A, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row)), m.Flag, Cells(m.Cells)),
        PlayCommandKind.Patrol => new PatrolCommand(civ, m.A, Cells(m.Cells) ?? []),
        PlayCommandKind.Found => new FoundCommand(civ, m.A),
        PlayCommandKind.Research => new ResearchCommand(civ, m.A),
        PlayCommandKind.Hire => new HireCommand(civ, m.Flag),
        PlayCommandKind.Dismiss => new DismissAdvisorCommand(civ),
        PlayCommandKind.DeclareWar => new DeclareWarCommand(civ, m.A),
        PlayCommandKind.Attack => new AttackCommand(civ, m.A, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row))),
        PlayCommandKind.ProposePeace => new ProposePeaceCommand(civ, m.A, m.B),
        PlayCommandKind.AnswerPeace => new AnswerPeaceCommand(civ, m.A, m.Flag),
        PlayCommandKind.Disband => new DisbandCommand(civ, m.A),
        PlayCommandKind.ProposeTreaty => new ProposeTreatyCommand(civ, m.A, (TreatyKind)m.B),
        PlayCommandKind.AnswerTreaty => new AnswerTreatyCommand(civ, m.A, (TreatyKind)m.B, m.Flag),
        PlayCommandKind.CancelTreaty => new CancelTreatyCommand(civ, m.A, (TreatyKind)m.B),
        PlayCommandKind.Barter => new BarterCommand(civ, m.A, m.B, m.Col * SimContent.One, m.Row, m.C * SimContent.One),
        PlayCommandKind.AnswerBarter => new AnswerBarterCommand(civ, m.A, m.Flag),
        PlayCommandKind.SendCaravan => new SendCaravanCommand(civ, m.A, m.B, m.Col, m.C * SimContent.One),
        PlayCommandKind.Route => new RouteCommand(civ, m.A, m.B, m.Col, m.C * SimContent.One, Cells(m.Cells)),
        PlayCommandKind.EndRoute => new EndRouteCommand(civ, m.A),
        PlayCommandKind.BringMaterials => new BringMaterialsCommand(civ, m.A),
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

            foreach (int sid in c.Settlements)
            {
                foreach (int cell in _game.Settlements[sid].Cells)
                {
                    if (_game.CannotBuild(c, cell, t, checkPrice: false) is null)
                    {
                        HexCoord h = _game.Geo.FromIndex(cell);
                        cells.Add(h.Col);
                        cells.Add(h.Row);
                    }
                }
            }
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
            buildings.Add(new PlayBuildingInfo(t.Id, t.Key, t.Name, t.Workers, t.Price, t.BuildTicks / SimContent.TicksPerSecond, t.Produces, t.Consumes, t.Housing, t.Storage, t.Id == 0 ? "" : _game.Needs(t), t.Tech, t.Groups));
        }

        var levels = new List<PlayLevelInfo>();
        foreach (SettlementLevel l in content.Levels)
        {
            levels.Add(new PlayLevelInfo(l.Key, l.Name, l.Radius, l.Housing, l.UpgradePrice, l.Tech));
        }

        var units = new List<PlayUnitInfo>();
        foreach (UnitType u in content.Units)
        {
            units.Add(new PlayUnitInfo(u.Id, u.Key, u.Name, u.People, u.Price, u.TrainTicks / SimContent.TicksPerSecond, u.HiddenTicksPerHex > 0, u.Founds, u.Tech, u.Army, u.Strength, u.Range, u.System, u.NeedsBuilding));
        }

        var techs = new List<PlayTechInfo>();
        foreach (SimTech t in content.Techs)
        {
            var needs = new List<string>();
            foreach ((int req, long ticks) in t.Experience)
            {
                needs.Add($"{content.Techs[req].Name} {ticks / SimContent.TicksPerMinute}");
            }

            techs.Add(new PlayTechInfo(t.Id, t.Key, t.Name, t.Era, string.Join(", ", needs), t.ResearchTicks / SimContent.TicksPerSecond, t.Gives, t.Upgrade, t.Category));
        }

        var eras = new List<string>();
        foreach (SimEra e in content.Eras)
        {
            eras.Add(e.Name);
        }

        client.Connection.Send(new PlayContentMessage(goods, buildings, levels, _game.Geo.Width, _game.Geo.Height, "Earth", units, techs, eras));
        client.SentChunks = false;
    }

    private void SendViews()
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
            if (client.Civ < 0)
            {
                continue;
            }

            Civ civ = _game.Civs[client.Civ];
            if (!client.SentChunks || client.SawPicking != _game.Picking)
            {
                // The whole map again: at the start, and when the start phase ends (deposits and soil appear, the fog comes).
                System.Array.Clear(client.SentTerrain);
                for (int chunk = 0; chunk < _game.Map.Chunks.Count; chunk++)
                {
                    SendChunk(client, chunk, always: client.SentChunks);
                }

                client.SentChunks = true;
                client.SawPicking = _game.Picking;
                civ.NewlyKnown.Clear();
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

                civ.NewlyKnown.Clear();
                SendChanges(client, civ);
            }

            client.Connection.Send(View(client.Civ));
            client.ViewSoon = false;
        }

        _news.Clear();
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

    // Only what changed against what the client has: terrain once per newly explored chunk, then fog and owner per hex.
    private void SendChanges(PlayClient client, Civ civ)
    {
        _cellList.Clear();
        _visList.Clear();
        _ownerList.Clear();
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
                    if (civ.Known[i] && !client.SentTerrain[i])
                    {
                        terrain = true;
                        break;
                    }
                }
            }

            if (terrain)
            {
                SendChunk(client, chunk);
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
                    if (vis != client.SentVisibility[i] || owner != client.SentOwner[i])
                    {
                        client.SentVisibility[i] = vis;
                        client.SentOwner[i] = owner;
                        _cellList.Add(i);
                        _visList.Add(vis);
                        _ownerList.Add(owner);
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
            client.Connection.Send(new PlayCellsMessage([.. _cellList], [.. _visList], [.. _ownerList], [.. _chunkList]));
        }
    }

    private void SendChunk(PlayClient client, int chunk, bool always = false)
    {
        Civ civ = _game.Civs[client.Civ];
        MapChunkMessage? m = MapViewBuilder.Build(_game.Map, 0, chunk, _game.Ticks, civ.Known, hideResources: _game.Picking, always: always,
            visibilityOf: i => VisibilityFor(civ, i), ownerSeen: i => OwnerFor(civ, i));
        (int x0, int y0, int w, int h) = _game.Map.Chunks.Bounds(chunk);
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int i = _game.Geo.Index(new HexCoord(x0 + c, y0 + r));
                client.SentTerrain[i] = civ.Known[i];
                client.SentVisibility[i] = VisibilityFor(civ, i);
                client.SentOwner[i] = OwnerFor(civ, i);
            }
        }

        if (m is not null)
        {
            client.Connection.Send(m);
        }
    }

    private PlayViewMessage View(int civId)
    {
        Civ civ = _game.Civs[civId];
        if (civ.Dirty)
        {
            _game.Recompute(civ);
        }

        bool[] known = civ.Known;
        var settlements = new List<PlaySettlementView>();
        foreach (Settlement s in _game.Settlements)
        {
            // Our own always; others only while seen (what we saw before comes from memory below).
            if (s.Abandoned || (s.Civ != civId && !_game.Visible(civ, s.Center)))
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(s.Center);
            bool own = s.Civ == civId;
            settlements.Add(new PlaySettlementView(s.Id, s.Civ, s.Name, h.Col, h.Row, s.Level, s.People, s.Housing, s.Idle, s.Besieged, _game.Defense(s),
                own ? (long[])s.Stock.Clone() : null, own ? (long[])s.Capacity.Clone() : null, own ? (long[])s.Rate.Clone() : null));
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
            if (b.State == BuildingState.Removed || (b.Civ != civId && !_game.Visible(civ, b.Cell)))
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

            buildings.Add(new PlayBuildingView(b.Id, b.Type, b.Civ, h.Col, h.Row, (int)b.State, b.Workers, b.Starved, b.Priority, progress, b.YieldPercent, missing, b.Civ == civId ? b.Porters : 0));
        }

        if (!_game.Picking)
        {
            foreach (BuildingMemory m in _game.RememberedBuildings(civ))
            {
                if (m.Civ != civId)
                {
                    HexCoord h = _game.Geo.FromIndex(m.Cell);
                    buildings.Add(new PlayBuildingView(m.Building, m.Type, m.Civ, h.Col, h.Row, (int)m.State, 0, false, false, 1000, 100, Remembered: true));
                }
            }
        }

        _ = known;
        var units = new List<PlayUnitView>();
        foreach (Unit u in _game.Units)
        {
            if (u.State == UnitState.Gone || (u.Civ != civId && !_game.Sees(civ, u)))
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(u.Cell);
            HexCoord to = u.State == UnitState.Moving && u.Path.Count > 0 ? _game.Geo.FromIndex(u.Path[^1]) : h;
            bool mine = u.Civ == civId;
            units.Add(new PlayUnitView(u.Id, u.Kind, u.Civ, h.Col, h.Row, u.People, (int)u.State, u.Hidden, to.Col, to.Row, u.Fighting,
                u.Cargo is null ? null : _game.CargoText(u.Cargo),
                mine && u.State == UnitState.Moving ? Pairs(u.Path, u.PathIndex) : null,
                mine && u.Patrol.Count > 0 ? Pairs(u.Patrol, 0) : null,
                u.TargetUnit >= 0 || u.TargetSettlement >= 0,
                _game.Content.Units[u.Kind].System));
        }

        return new PlayViewMessage(_game.Ticks, _speed, civId, (long[])civ.Stock.Clone(), (long[])civ.Capacity.Clone(), (long[])civ.Rate.Clone(),
            civ.People, civ.Hungry, settlements, buildings, [.. _news], units, _game.EraOf(civ), civ.Researching, ResearchPermille(civ), TechStates(civ),
            Person(civ.Thinker, false), Person(civ.ThinkerOffer, false), Person(civ.Advisor, true), Person(civ.AdvisorOffer, true), _game.Advice(civ),
            Peoples(civ), civ.Dead, _game.Picking, _game.Picking && _game.PickOrder[_game.PickIndex] == civId,
            _game.Picking ? _game.PickTicksLeft / SimContent.TicksPerSecond : 0, TakenStarts(), _game.BlockRadius,
            _game.Picking ? _game.Civs[_game.PickOrder[_game.PickIndex]].Name : null,
            civ.Event >= 0 ? _game.Content.Events[civ.Event].Name : null, civ.Event >= 0 ? (int)((civ.EventEnd - _game.Ticks) / SimContent.TicksPerSecond) : 0,
            _game.GoalText, _game.Winner >= 0 ? _game.Civs[_game.Winner].Name : null, _game.ScoreOf(civ), RoutesOf(civId),
            AlertsOf(civ), _game.KnownIn(civ, civ.Era), civ.Era + 1 < _game.Content.Eras.Count ? _game.Content.EraAdvanceTechs : 0);
    }

    private List<PlayAlertView> AlertsOf(Civ civ)
    {
        var list = new List<PlayAlertView>();
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
        var peoples = new List<PlayHistoryView>();
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

            peoples.Add(new PlayHistoryView(c.Id, c.Name, rows));
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

        return new PlaySummaryMessage(peoples, mine, best, SiteScores.Criteria, criteria);
    }

    private List<PlayRouteView> RoutesOf(int civ)
    {
        var list = new List<PlayRouteView>();
        for (int i = 0; i < _game.Routes.Count; i++)
        {
            TradeRoute r = _game.Routes[i];
            if (r.Civ == civ && !r.Ended)
            {
                list.Add(new PlayRouteView(i, r.From, r.To, r.Good, (int)(r.Amount / SimContent.One), r.Caravan >= 0, r.Path.Count > 0 ? Pairs(r.Path, 0) : null));
            }
        }

        return list;
    }

    private int[] TakenStarts()
    {
        var list = new List<int>();
        foreach (int cell in _game.StartCell)
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

    private List<PlayPeopleView> Peoples(Civ civ)
    {
        var list = new List<PlayPeopleView>();
        foreach (Civ other in _game.Civs)
        {
            // Peoples we have seen, and peoples that have seen us and spoke to us (an offer) or fight us.
            bool known = _game.Met(civ, other) || _game.AtWar(civ.Id, other.Id)
                || _game.TreatyOffers.Exists(o => o.From == other.Id && o.To == civ.Id) || _game.PeaceOffers.Exists(o => o.From == other.Id && o.To == civ.Id);
            if (other.Id == civ.Id || !known)
            {
                continue;
            }

            PeaceOffer? offer = _game.PeaceOffers.Find(o => o.From == other.Id && o.To == civ.Id);
            list.Add(new PlayPeopleView(
                other.Id, other.Name, _game.AtWar(civ.Id, other.Id), _game.Score(civ.Id, other.Id), _game.Score(other.Id, civ.Id),
                _game.SuggestedTribute(civ.Id, other.Id), other.Reputation, offer?.TributePercent, other.Dead,
                _game.Has(civ.Id, other.Id, TreatyKind.Trade), _game.Has(civ.Id, other.Id, TreatyKind.Alliance),
                _game.TreatyOffers.Exists(o => o.From == other.Id && o.To == civ.Id && o.Kind == TreatyKind.Trade),
                _game.TreatyOffers.Exists(o => o.From == other.Id && o.To == civ.Id && o.Kind == TreatyKind.Alliance),
                _game.BarterOffers.Find(o => o.From == other.Id && o.To == civ.Id) is { } b
                    ? $"{b.GiveAmount / SimContent.One} {_game.Content.Goods[b.GiveGood].Name.ToLowerInvariant()} for {b.WantAmount / SimContent.One} {_game.Content.Goods[b.WantGood].Name.ToLowerInvariant()}"
                    : null));
        }

        return list;
    }

    private int ResearchPermille(Civ civ)
    {
        if (civ.Researching < 0)
        {
            return 0;
        }

        int total = Math.Max(1, _game.Content.Techs[civ.Researching].ResearchTicks);
        return 1000 - (int)Math.Clamp((civ.ResearchEnd - _game.Ticks) * 1000 / total, 0, 1000);
    }

    private List<PlayTechState> TechStates(Civ civ)
    {
        var list = new List<PlayTechState>();
        foreach (SimTech t in _game.Content.Techs)
        {
            list.Add(civ.KnownTech[t.Id] ? new PlayTechState(t.Id, true, [], null) : new PlayTechState(t.Id, false, _game.ResearchPrice(civ, t.Id), _game.CannotResearch(civ, t.Id)));
        }

        return list;
    }

    private PlayPersonView? Person(Sim.State.Person? p, bool advisor) => p is null ? null : new PlayPersonView(
        p.FullName, _game.Content.Rarities[p.Rarity].Name, advisor ? 100 : _game.Edge(p, cost: true), advisor ? 100 : _game.Edge(p, cost: false),
        advisor ? _game.FieldsText(p) : "", _game.HirePrice(p, advisor));

    private sealed class PlayClient(IConnection connection, int cells)
    {
        public IConnection Connection { get; } = connection;

        public int Civ { get; set; } = -1;

        public int Cells { get; } = cells;

        // What this client has been told, hex by hex: the server sends only changes against it.
        public byte[] SentVisibility { get; } = new byte[cells];

        public short[] SentOwner { get; } = Filled(cells);

        public bool[] SentTerrain { get; } = new bool[cells];

        private static short[] Filled(int n)
        {
            var a = new short[n];
            System.Array.Fill(a, (short)-1);
            return a;
        }

        public bool SentChunks { get; set; }

        public bool ViewSoon { get; set; }

        public bool SawPicking { get; set; }

        public int PausesUsed { get; set; }
    }
}
