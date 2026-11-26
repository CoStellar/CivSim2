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

    /// <summary>
    /// Connects and says hello in the given role; a player names the civilization it leads (or any free one). Without
    /// <paramref name="hello"/> the hello was said already (the lobby screen of a game on another computer, MP3).
    /// </summary>
    public ClientSession(IConnection connection, ClientRole role, string name, int civ = HelloMessage.AnyCiv, string password = "", string returnKey = "", bool hello = true)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        Role = role;
        if (hello)
        {
            _connection.Send(new HelloMessage(ProtocolInfo.Version, name, role, civ, Password: password, ReturnKey: returnKey));
        }
    }

    /// <summary>Why the server turned this client away, or null.</summary>
    public string? Rejected { get; private set; }

    /// <summary>Why the connection closed, when the other side said so or the network did (a game on another computer).</summary>
    public string? CloseReason => _connection is TcpConnection t ? t.CloseReason : null;

    /// <summary>False once the server closed the connection or it was lost (a game on another computer).</summary>
    public bool IsOpen => _connection.IsOpen;

    /// <summary>The key to lead the same people again after a lost connection (MP2); empty until Welcome.</summary>
    public string ReturnKey { get; private set; } = "";

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

    /// <summary>The players of a game with others, the vote and the pause (MP4); null in a game alone.</summary>
    public PlayPlayersMessage? Players { get; private set; }

    /// <summary>Lines of chat received (the newest <see cref="ChatKept"/>).</summary>
    public IReadOnlyList<PlayChatLineMessage> ChatLines => _chatLines;

    /// <summary>Lines of chat kept.</summary>
    public const int ChatKept = 100;

    private readonly List<PlayChatLineMessage> _chatLines = [];

    /// <summary>Raised when a line of chat arrives.</summary>
    public event Action<PlayChatLineMessage>? ChatReceived;

    /// <summary>Says something to everyone, or only to our allies.</summary>
    public void Chat(string text, bool allies) => _connection.Send(new PlayChatMessage(allies, text));

    /// <summary>Yes or no in the vote going on.</summary>
    public void Vote(int id, bool yes) => _connection.Send(new PlayVoteMessage(id, yes));

    /// <summary>A candidate asks to lead this civilization; the players vote.</summary>
    public void AskToLead(int civ) => _connection.Send(new PlayJoinMessage(civ));

    /// <summary>A candidate watches the whole map instead, and cannot lead a civilization in this game any more.</summary>
    public void WatchInstead() => _connection.Send(new PlayWatchMessage());

    /// <summary>Removes a player (the host at once; another player starts a vote).</summary>
    public void Kick(string name) => _connection.Send(new PlayKickMessage(name));

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

    /// <summary>Raised with the server's answer to a save.</summary>
    public event Action<SaveGameResultMessage>? Saved;

    /// <summary>Asks the server to save the game under a name.</summary>
    public void SaveGame(string name) => _connection.Send(new SaveGameRequestMessage(name));

    /// <summary>Turns the tutorial's task list on or off for this game.</summary>
    public void SetTutorial(bool on) => _connection.Send(new SetTutorialMessage(on));

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

    /// <summary>A command of the new game was sent (the sound plays its effect).</summary>
    public event Action<PlayCommandKind>? PlaySent;

    /// <summary>Sends a command of the new game.</summary>
    public void Play(PlayCommandKind kind, int a, int col = 0, int row = 0, bool flag = false, int b = 0, int c = 0, int[]? cells = null, int d = 0)
    {
        _connection.Send(new PlayCommandMessage(++_sequence, kind, a, col, row, flag, b, c, cells, d));
        PlaySent?.Invoke(kind);
    }

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
                ReturnKey = w.ReturnKey ?? "";
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
                Rejected = rejected.Reason;
                break;
            case CommandRejectedMessage rejected:
                LastError = rejected.Reason;
                CommandRefused?.Invoke(rejected.Reason);
                break;
            case PlayPingMessage ping:
                _connection.Send(ping); // echoed at once: the server measures the time there and back
                break;
            case PlayPlayersMessage players:
                Players = players.Players.Count > 1 ? players : null;
                break;
            case PlayChatLineMessage line:
                _chatLines.Add(line);
                if (_chatLines.Count > ChatKept)
                {
                    _chatLines.RemoveAt(0);
                }

                ChatReceived?.Invoke(line);
                break;
            case SaveGameResultMessage saved:
                Saved?.Invoke(saved);
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
