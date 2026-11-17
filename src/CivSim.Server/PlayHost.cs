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
            buildings.Add(new PlayBuildingInfo(t.Id, t.Key, t.Name, t.Workers, t.Price, t.BuildTicks / SimContent.TicksPerSecond, t.Produces, t.Consumes, t.Housing, t.Storage, t.Id == 0 ? "" : _game.Needs(t)));
        }

        var levels = new List<PlayLevelInfo>();
        foreach (SettlementLevel l in content.Levels)
        {
            levels.Add(new PlayLevelInfo(l.Key, l.Name, l.Radius, l.Housing, l.UpgradePrice));
        }

        client.Connection.Send(new PlayContentMessage(goods, buildings, levels, _game.Geo.Width, _game.Geo.Height, "Earth"));
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

            bool knownChanged = UpdateKnown(client);
            if (!client.SentChunks || knownChanged)
            {
                for (int chunk = 0; chunk < _game.Map.Chunks.Count; chunk++)
                {
                    SendChunk(client, chunk);
                }

                client.SentChunks = true;
            }
            else
            {
                foreach (int chunk in _dirty)
                {
                    SendChunk(client, chunk);
                }
            }

            client.Connection.Send(View(client.Civ));
            client.ViewSoon = false;
        }

        _news.Clear();
    }

    private void SendChunk(PlayClient client, int chunk)
    {
        MapChunkMessage? m = MapViewBuilder.Build(_game.Map, 0, chunk, _game.Ticks, client.Known);
        if (m is not null)
        {
            client.Connection.Send(m);
        }
    }

    // The player knows the land a few hexes beyond its settlements (scouts widen it in stage C).
    private bool UpdateKnown(PlayClient client)
    {
        Civ civ = _game.Civs[client.Civ];
        int stamp = 0;
        foreach (int sid in civ.Settlements)
        {
            stamp = (stamp * 31) + sid + (_game.Settlements[sid].Level * 7919);
        }

        if (stamp == client.KnownStamp && client.SentChunks)
        {
            return false;
        }

        client.KnownStamp = stamp;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = _game.Settlements[sid];
            int radius = _game.Content.Levels[s.Level].Radius + 8;
            Span<HexCoord> area = new HexCoord[HexGlobe.RangeSize(radius)];
            int n = _game.Geo.Range(_game.Geo.FromIndex(s.Center), radius, area);
            for (int k = 0; k < n; k++)
            {
                client.Known[_game.Geo.Index(area[k])] = true;
            }
        }

        return true;
    }

    private PlayViewMessage View(int civId)
    {
        Civ civ = _game.Civs[civId];
        if (civ.Dirty)
        {
            _game.Recompute(civ);
        }

        var settlements = new List<PlaySettlementView>();
        foreach (Settlement s in _game.Settlements)
        {
            if (s.Abandoned)
            {
                continue;
            }

            HexCoord h = _game.Geo.FromIndex(s.Center);
            settlements.Add(new PlaySettlementView(s.Id, s.Civ, s.Name, h.Col, h.Row, s.Level, s.People, s.Housing, s.Idle));
        }

        var buildings = new List<PlayBuildingView>();
        foreach (Building b in _game.Buildings)
        {
            if (b.State == BuildingState.Removed)
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

        return new PlayViewMessage(_game.Ticks, _speed, civId, (long[])civ.Stock.Clone(), (long[])civ.Capacity.Clone(), (long[])civ.Rate.Clone(),
            civ.People, civ.Hungry, settlements, buildings, [.. _news]);
    }

    private sealed class PlayClient(IConnection connection, int cells)
    {
        public IConnection Connection { get; } = connection;

        public int Civ { get; set; } = -1;

        public bool[] Known { get; } = new bool[cells];

        public int KnownStamp { get; set; } = -1;

        public bool SentChunks { get; set; }

        public bool ViewSoon { get; set; }
    }
}
