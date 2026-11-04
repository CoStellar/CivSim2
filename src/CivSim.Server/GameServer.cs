using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Simulation;
using CivSim.Core.State;
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
    private readonly List<Client> _clients = [];
    private int _nextPlayerId = 1;

    /// <summary>Creates a server for a new game.</summary>
    public GameServer(GameSettings settings)
        : this(GameState.CreateNew(settings))
    {
    }

    /// <summary>Creates a server for an existing (e.g. loaded) game.</summary>
    public GameServer(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Runner = new TurnRunner(state);
    }

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

    /// <summary>Runs one turn now (observer mode, headless, or a turn timer) and sends views to all players.</summary>
    public TurnReport AdvanceTurn()
    {
        TurnReport report = Runner.RunTurn();
        foreach (RejectedCommand rejected in report.Rejected)
        {
            Client? owner = _clients.Find(c => c.Player == rejected.Envelope.Player);
            owner?.Connection.Send(new CommandRejectedMessage(owner.SequenceOf(rejected.Envelope.Sequence), rejected.Reason));
        }

        foreach (Client client in _clients)
        {
            client.ForgetSequences();
        }

        SendViews();
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
        client.Connection.Send(ViewBuilder.Build(Runner.Snapshots.Latest ?? GameSnapshot.From(State), civ.Id));
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

        ICommand? command = CommandMapper.ToCore(submit.Command);
        if (command is null)
        {
            client.Connection.Send(new CommandRejectedMessage(submit.ClientSequence, "Unknown command."));
            return;
        }

        CommandEnvelope envelope = Runner.Commands.Enqueue(player, civ, command);
        client.RememberSequence(envelope.Sequence, submit.ClientSequence);
    }

    private void SendViews()
    {
        GameSnapshot snapshot = Runner.Snapshots.Latest ?? GameSnapshot.From(State);
        foreach (Client client in _clients)
        {
            if (client.Civ is { } civ && client.Connection.IsOpen)
            {
                client.Connection.Send(ViewBuilder.Build(snapshot, civ));
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

    private sealed class Client(IConnection connection)
    {
        private readonly Dictionary<long, long> _clientSequences = [];

        public IConnection Connection { get; } = connection;

        public PlayerId? Player { get; set; }

        public CivId? Civ { get; set; }

        public long ReadyForTurn { get; set; } = -1;

        public void RememberSequence(long serverSequence, long clientSequence) => _clientSequences[serverSequence] = clientSequence;

        public long SequenceOf(long serverSequence) => _clientSequences.GetValueOrDefault(serverSequence, -1);

        public void ForgetSequences() => _clientSequences.Clear();
    }
}
