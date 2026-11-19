using System;
using System.Collections.Generic;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;

namespace CivSim.Game.Net;

/// <summary>
/// Everything the client knows, built from server messages only. Call <see cref="Pump"/> once per frame on the
/// main thread; the simulation runs on the server's own thread.
/// </summary>
public sealed class ClientSession
{
    /// <summary>Turn times kept for the chart.</summary>
    public const int HistoryLength = 300;

    private readonly IConnection _connection;
    private readonly List<ChronicleEntry> _chronicle = [];

    /// <summary>Chronicle lines kept (the newest; constant memory however long the game runs).</summary>
    public const int ChronicleKept = 2000;
    private readonly Queue<double> _turnMs = new();
    private long _lastTurn;
    private long _sequence;

    /// <summary>Connects and says hello in the given role; a player names the civilization it leads (or any free one).</summary>
    public ClientSession(IConnection connection, ClientRole role, string name, int civ = HelloMessage.AnyCiv)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Role = role;
        _connection.Send(new HelloMessage(ProtocolInfo.Version, name, role, civ));
    }

    /// <summary>Observer (sees everything) or player (leads one civilization through its fog).</summary>
    public ClientRole Role { get; }

    /// <summary>True when this client leads a civilization (stage 6 PR P2).</summary>
    public bool IsPlayer => Role == ClientRole.Player;

    /// <summary>Set after Welcome.</summary>
    public int PlayerId { get; private set; } = -1;

    /// <summary>Civilization led by this client, or <see cref="WelcomeMessage.NoCiv"/> for observers.</summary>
    public int CivId { get; private set; } = WelcomeMessage.NoCiv;

    /// <summary>Content (biomes, deposits) for drawing; null until received.</summary>
    public ContentMessage? Content { get; private set; }

    /// <summary>Latest view.</summary>
    public GameViewMessage? View { get; private set; }

    /// <summary>Known worlds by id.</summary>
    public Dictionary<int, ClientWorld> Worlds { get; } = [];

    /// <summary>All chronicle lines received.</summary>
    public IReadOnlyList<ChronicleEntry> Chronicle => _chronicle;

    /// <summary>Recent turn times in milliseconds, oldest first.</summary>
    public IReadOnlyCollection<double> TurnMilliseconds => _turnMs;

    /// <summary>Latest observer layers (journeys, fields, events, relations); null for players.</summary>
    public ObserverDetailsMessage? Details { get; private set; }

    /// <summary>Fog of the watched civilization per world id (observer only), or empty when none is watched.</summary>
    public Dictionary<int, FogMessage> Fog { get; } = [];

    /// <summary>Civilization the observer follows (fog and details), or <see cref="WelcomeMessage.NoCiv"/>.</summary>
    public int WatchedCiv { get; private set; } = WelcomeMessage.NoCiv;

    /// <summary>Details of the followed civilization; null when none is followed or nothing arrived yet.</summary>
    public CivilizationDetailsMessage? CivDetails { get; private set; }

    /// <summary>People, settlements and technologies of every civilization over time (observers).</summary>
    public CivHistory History { get; } = new();

    /// <summary>Counts fog updates, so the map knows when to recolour.</summary>
    public int FogVersion { get; private set; }

    /// <summary>Last refusal or error from the server.</summary>
    public string? LastError { get; private set; }

    /// <summary>Raised when new chronicle lines arrive.</summary>
    public event Action<IReadOnlyList<ChronicleEntry>>? ChronicleAdded;

    /// <summary>Sends a speed change (observers only).</summary>
    public void SetSpeed(SimulationSpeed speed) => _connection.Send(new SetSpeedMessage(speed));

    /// <summary>Asks the server for the fog of <paramref name="civ"/> (observers only); NoCiv stops it.</summary>
    public void Watch(int civ)
    {
        WatchedCiv = civ;
        CivDetails = null;
        Fog.Clear();
        FogVersion++;
        _connection.Send(new WatchCivilizationMessage(civ));
    }

    /// <summary>Raised when name suggestions arrive (GDD 5.6).</summary>
    public event Action<NameSuggestionsMessage>? NamesReceived;

    /// <summary>Raised when the server refuses a command (text of the reason).</summary>
    public event Action<string>? CommandRefused;

    /// <summary>Asks for other names in the same language for a civilization or a settlement.</summary>
    public void RequestNames(NameTarget target, int id, int page, int count = 8) =>
        _connection.Send(new NameSuggestionsRequestMessage(target, id, page, count));

    /// <summary>Renames a civilization (observers: any; applied at once between turns).</summary>
    public void RenameCivilization(int civ, string name) =>
        _connection.Send(new SubmitCommandMessage(civ, ++_sequence, new RenameCivilizationDto(name)));

    /// <summary>Renames a settlement of <paramref name="owner"/>.</summary>
    public void RenameSettlement(int owner, int settlement, string name) =>
        _connection.Send(new SubmitCommandMessage(owner, ++_sequence, new RenameSettlementDto(settlement, name)));

    /// <summary>Sends a command for the civilization this player leads (stage 6 PR P2); the server checks it and may refuse it.</summary>
    public void Command(ICommandDto command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (CivId >= 0)
        {
            _connection.Send(new SubmitCommandMessage(CivId, ++_sequence, command));
        }
    }

    /// <summary>Takes a lever (1–5) or gives it back to the AI.</summary>
    public void SetLever(int lever, bool take) => Command(new SetLeverDto(lever, take));

    /// <summary>Rules of the new game (ADR 0028), null in the legacy viewer.</summary>
    public PlayContentMessage? PlayContent { get; private set; }

    /// <summary>Latest view of the new game.</summary>
    public PlayViewMessage? PlayView { get; private set; }

    /// <summary>Hexes where a building type can go, as answered by the server.</summary>
    public event Action<PlacesMessage>? PlacesReceived;

    /// <summary>News of the new game as they come.</summary>
    public event Action<IReadOnlyList<string>>? PlayNews;

    /// <summary>Sends a command of the new game.</summary>
    public void Play(PlayCommandKind kind, int a, int col = 0, int row = 0, bool flag = false, int b = 0, int c = 0, int[]? cells = null, int d = 0) =>
        _connection.Send(new PlayCommandMessage(++_sequence, kind, a, col, row, flag, b, c, cells, d));

    private readonly SortedDictionary<int, PlayBuildingView> _buildings = [];
    private readonly SortedDictionary<int, PlaySettlementView> _settlements = [];
    private readonly SortedDictionary<int, PlayUnitView> _units = [];

    // Objects come as changes (fog of war work, 2026-11-18): keep them here and put them into each view.
    private void Merge(PlayObjectsMessage m)
    {
        foreach (int id in m.GoneBuildings)
        {
            _buildings.Remove(id);
        }

        foreach (int id in m.GoneSettlements)
        {
            _settlements.Remove(id);
        }

        foreach (int id in m.GoneUnits)
        {
            _units.Remove(id);
        }

        foreach (PlayBuildingView b in m.Buildings)
        {
            _buildings[b.Id] = b;
        }

        foreach (PlaySettlementView s in m.Settlements)
        {
            _settlements[s.Id] = s;
        }

        foreach (PlayUnitView u in m.Units)
        {
            _units[u.Id] = u;
        }
    }

    private PlayViewMessage Merged(PlayViewMessage v, IReadOnlyList<PlayTechState> techs) => v with
    {
        Buildings = [.. _buildings.Values],
        Settlements = [.. _settlements.Values],
        Units = [.. _units.Values],
        Techs = techs,
        News = v.News,
    };

    /// <summary>Latest summary of the new game.</summary>
    public PlaySummaryMessage? Summary { get; private set; }

    /// <summary>Asks for the summary (history and start quality).</summary>
    public void AskSummary() => _connection.Send(new PlaySummaryRequestMessage());

    /// <summary>Asks where a building type can be built.</summary>
    public void AskPlaces(int type) => _connection.Send(new PlacesRequestMessage(type));

    /// <summary>Closes the connection (back to the start menu).</summary>
    public void Close() => _connection.Close();

    /// <summary>
    /// Handles waiting messages; stops after <paramref name="budget"/> messages or <paramref name="milliseconds"/>
    /// (after at least one message) so a big update never freezes a frame.
    /// </summary>
    public int Pump(int budget = 400, double milliseconds = 4)
    {
        int handled = 0;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (handled < budget && (handled == 0 || System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds < milliseconds)
            && _connection.TryReceive(out IProtocolMessage? message))
        {
            Handle(message!);
            handled++;
        }

        return handled;
    }

    private void Handle(IProtocolMessage message)
    {
        switch (message)
        {
            case WelcomeMessage w:
                PlayerId = w.PlayerId;
                CivId = w.CivId;
                break;
            case ContentMessage c:
                Content = c;
                break;
            case GameViewMessage v:
                bool first = View is null;
                long previous = _lastTurn;
                _lastTurn = v.Turn;
                View = v;
                foreach (WorldView w in v.Worlds)
                {
                    if (!Worlds.ContainsKey(w.Id))
                    {
                        Worlds[w.Id] = new ClientWorld(w.Id, w.Name, w.Width, w.Height);
                    }
                }

                if (first || v.Turn > previous)
                {
                    History.Add(v);
                }

                if (!first && v.Turn > previous)
                {
                    _turnMs.Enqueue(v.TurnMicroseconds / 1000.0);
                    while (_turnMs.Count > HistoryLength)
                    {
                        _turnMs.Dequeue();
                    }
                }

                if (v.Chronicle is { Count: > 0 } lines)
                {
                    _chronicle.AddRange(lines);
                    if (_chronicle.Count > ChronicleKept)
                    {
                        _chronicle.RemoveRange(0, _chronicle.Count - ChronicleKept);
                    }

                    ChronicleAdded?.Invoke(lines);
                }

                break;
            case MapChunkMessage chunk:
                if (Worlds.TryGetValue(chunk.WorldId, out ClientWorld? world))
                {
                    world.Apply(chunk);
                }

                break;
            case PlayCellsMessage cells:
                if (Worlds.TryGetValue(0, out ClientWorld? earth))
                {
                    earth.Apply(cells);
                }

                break;
            case ObserverDetailsMessage details:
                Details = details;
                break;
            case CivilizationDetailsMessage civ:
                if (civ.CivId == WatchedCiv)
                {
                    CivDetails = civ;
                }

                break;
            case FogMessage fog:
                if (fog.CivId == WatchedCiv)
                {
                    Fog[fog.WorldId] = fog;
                    FogVersion++;
                }

                break;
            case ConnectionRejectedMessage rejected:
                LastError = rejected.Reason;
                break;
            case CommandRejectedMessage rejected:
                LastError = rejected.Reason;
                CommandRefused?.Invoke(rejected.Reason);
                break;
            case NameSuggestionsMessage names:
                NamesReceived?.Invoke(names);
                break;
            case PlayContentMessage pc:
                PlayContent = pc;
                if (!Worlds.ContainsKey(0))
                {
                    Worlds[0] = new ClientWorld(0, pc.WorldName, pc.MapWidth, pc.MapHeight);
                }

                break;
            case PlayObjectsMessage objects:
                Merge(objects);
                if (PlayView is { } last)
                {
                    PlayView = Merged(last, last.Techs);
                }

                break;
            case PlayViewMessage pv:
                PlayView = pv.ObjectsApart ? Merged(pv, pv.TechsSame && PlayView is { } prev ? prev.Techs : pv.Techs) : pv;
                if (pv.News is { Count: > 0 } news)
                {
                    PlayNews?.Invoke(news);
                }

                break;
            case PlaySummaryMessage summary:
                Summary = summary;
                break;
            case PlacesMessage places:
                PlacesReceived?.Invoke(places);
                break;
        }
    }
}
