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
public sealed record PlayOptions(ulong Seed, int LegacyWidth = 98, int LegacyHeight = 60, int Civilizations = 4);

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
        SimGame game = SimGame.Create(content, new SimSettings(options.Seed, options.LegacyWidth, options.LegacyHeight, options.Civilizations));
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
                    client.Civ = 0;
                    client.Connection.Send(new WelcomeMessage(ProtocolInfo.Version, 0, client.Civ));
                    SendContent(client);
                    break;
                case SetSpeedMessage speed:
                    _speed = speed.Speed;
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
                case PlacesRequestMessage places when client.Civ >= 0:
                    client.Connection.Send(Places(client.Civ, places.Type));
                    break;
                default:
                    break;
            }
        }
    }

    private SimCommand ToCommand(int civ, PlayCommandMessage m) => m.Kind switch
    {
        PlayCommandKind.Build => new BuildCommand(civ, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row)), m.A),
        PlayCommandKind.PauseConstruction => new PauseConstructionCommand(civ, m.A, m.Flag),
        PlayCommandKind.Stop => new StopBuildingCommand(civ, m.A, m.Flag),
        PlayCommandKind.Priority => new PriorityCommand(civ, m.A, m.Flag),
        PlayCommandKind.Demolish => new DemolishCommand(civ, m.A),
        PlayCommandKind.Train => new TrainCommand(civ, m.A, m.B),
        PlayCommandKind.Move => new MoveCommand(civ, m.A, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row)), m.Flag),
        PlayCommandKind.Found => new FoundCommand(civ, m.A),
        PlayCommandKind.Research => new ResearchCommand(civ, m.A),
        PlayCommandKind.Hire => new HireCommand(civ, m.Flag),
        PlayCommandKind.Dismiss => new DismissAdvisorCommand(civ),
        PlayCommandKind.DeclareWar => new DeclareWarCommand(civ, m.A),
        PlayCommandKind.Attack => new AttackCommand(civ, m.A, _game.Geo.Index(_game.Geo.Wrap(m.Col, m.Row))),
        PlayCommandKind.ProposePeace => new ProposePeaceCommand(civ, m.A, m.B),
        PlayCommandKind.AnswerPeace => new AnswerPeaceCommand(civ, m.A, m.Flag),
        PlayCommandKind.Disband => new DisbandCommand(civ, m.A),
        _ => new UpgradeSettlementCommand(civ, m.A),
    };

    private PlacesMessage Places(int civ, int type)
    {
        var cells = new List<int>();
        if (type > 0 && type < _game.Content.Buildings.Count)
        {
            BuildingType t = _game.Content.Buildings[type];
            Civ c = _game.Civs[civ];
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
            buildings.Add(new PlayBuildingInfo(t.Id, t.Key, t.Name, t.Workers, t.Price, t.BuildTicks / SimContent.TicksPerSecond, t.Produces, t.Consumes, t.Housing, t.Storage, t.Id == 0 ? "" : _game.Needs(t), t.Tech));
        }

        var levels = new List<PlayLevelInfo>();
        foreach (SettlementLevel l in content.Levels)
        {
            levels.Add(new PlayLevelInfo(l.Key, l.Name, l.Radius, l.Housing, l.UpgradePrice, l.Tech));
        }

        var units = new List<PlayUnitInfo>();
        foreach (UnitType u in content.Units)
        {
            units.Add(new PlayUnitInfo(u.Id, u.Key, u.Name, u.People, u.Price, u.TrainTicks / SimContent.TicksPerSecond, u.HiddenTicksPerHex > 0, u.Founds, u.Tech, u.Army, u.Strength, u.Range));
        }

        var techs = new List<PlayTechInfo>();
        foreach (SimTech t in content.Techs)
        {
            var needs = new List<string>();
            foreach ((int req, long ticks) in t.Experience)
            {
                needs.Add($"{content.Techs[req].Name} {ticks / SimContent.TicksPerMinute}");
            }

            techs.Add(new PlayTechInfo(t.Id, t.Key, t.Name, t.Era, string.Join(", ", needs), t.ResearchTicks / SimContent.TicksPerSecond));
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
            if (!client.SentChunks)
            {
                for (int chunk = 0; chunk < _game.Map.Chunks.Count; chunk++)
                {
                    SendChunk(client, chunk);
                }

                client.SentChunks = true;
                civ.NewlyKnown.Clear();
            }
            else
            {
                foreach (int chunk in _dirty)
                {
                    civ.NewlyKnown.Remove(chunk);
                    SendChunk(client, chunk);
                }

                foreach (int chunk in civ.NewlyKnown)
                {
                    SendChunk(client, chunk);
                }

                civ.NewlyKnown.Clear();
            }

            client.Connection.Send(View(client.Civ));
            client.ViewSoon = false;
        }

        _news.Clear();
    }

    private void SendChunk(PlayClient client, int chunk)
    {
        MapChunkMessage? m = MapViewBuilder.Build(_game.Map, 0, chunk, _game.Ticks, _game.Civs[client.Civ].Known);
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
            if (s.Abandoned || !known[s.Center])
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(s.Center);
            settlements.Add(new PlaySettlementView(s.Id, s.Civ, s.Name, h.Col, h.Row, s.Level, s.People, s.Housing, s.Idle, s.Besieged, _game.Defense(s)));
        }

        var buildings = new List<PlayBuildingView>();
        foreach (Building b in _game.Buildings)
        {
            if (b.State == BuildingState.Removed || !known[b.Cell])
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(b.Cell);
            BuildingType t = _game.Content.Buildings[b.Type];
            int progress = 1000;
            if (b.State == BuildingState.Construction)
            {
                progress = 1000 - (int)Math.Clamp((b.FinishTick - _game.Ticks) * 1000 / Math.Max(1, t.BuildTicks), 0, 1000);
            }
            else if (b.State == BuildingState.ConstructionPaused)
            {
                progress = 1000 - (b.RemainingTicks * 1000 / Math.Max(1, t.BuildTicks));
            }

            buildings.Add(new PlayBuildingView(b.Id, b.Type, b.Civ, h.Col, h.Row, (int)b.State, b.Workers, b.Starved, b.Priority, progress, b.YieldPercent));
        }

        var units = new List<PlayUnitView>();
        foreach (Unit u in _game.Units)
        {
            if (u.State == UnitState.Gone || (u.Civ != civId && !_game.Sees(civ, u)))
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(u.Cell);
            HexCoord to = u.State == UnitState.Moving && u.Path.Count > 0 ? _game.Geo.FromIndex(u.Path[^1]) : h;
            units.Add(new PlayUnitView(u.Id, u.Kind, u.Civ, h.Col, h.Row, u.People, (int)u.State, u.Hidden, to.Col, to.Row, u.Fighting));
        }

        return new PlayViewMessage(_game.Ticks, _speed, civId, (long[])civ.Stock.Clone(), (long[])civ.Capacity.Clone(), (long[])civ.Rate.Clone(),
            civ.People, civ.Hungry, settlements, buildings, [.. _news], units, _game.EraOf(civ), civ.Researching, ResearchPermille(civ), TechStates(civ),
            Person(civ.Thinker, false), Person(civ.ThinkerOffer, false), Person(civ.Advisor, true), Person(civ.AdvisorOffer, true), _game.Advice(civ),
            Peoples(civ), civ.Dead);
    }

    private List<PlayPeopleView> Peoples(Civ civ)
    {
        var list = new List<PlayPeopleView>();
        foreach (Civ other in _game.Civs)
        {
            if (other.Id == civ.Id || !_game.Met(civ, other) && !_game.AtWar(civ.Id, other.Id))
            {
                continue;
            }

            PeaceOffer? offer = _game.PeaceOffers.Find(o => o.From == other.Id && o.To == civ.Id);
            list.Add(new PlayPeopleView(
                other.Id, other.Name, _game.AtWar(civ.Id, other.Id), _game.Score(civ.Id, other.Id), _game.Score(other.Id, civ.Id),
                _game.SuggestedTribute(civ.Id, other.Id), other.Reputation, offer?.TributePercent, other.Dead));
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

        public bool SentChunks { get; set; }

        public bool ViewSoon { get; set; }
    }
}
