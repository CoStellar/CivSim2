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
    private readonly Queue<double> _turnMs = new();
    private long _lastTurn;
    private long _sequence;

    /// <summary>Connects and says hello in the given role.</summary>
    public ClientSession(IConnection connection, ClientRole role, string name)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _connection.Send(new HelloMessage(ProtocolInfo.Version, name, role));
    }

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
                    ChronicleAdded?.Invoke(lines);
                }

                break;
            case MapChunkMessage chunk:
                if (Worlds.TryGetValue(chunk.WorldId, out ClientWorld? world))
                {
                    world.Apply(chunk);
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
        }
    }
}
