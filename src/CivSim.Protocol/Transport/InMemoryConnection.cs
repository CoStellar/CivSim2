using System.Collections.Concurrent;
using CivSim.Protocol.Messages;

namespace CivSim.Protocol.Transport;

/// <summary>
/// In-process connection used in single player. Messages still go through <see cref="ProtocolSerializer"/>,
/// so single player exercises exactly the same wire format as a future network game.
/// </summary>
public sealed class InMemoryConnection : IConnection
{
    private readonly ConcurrentQueue<byte[]> _inbox;
    private readonly ConcurrentQueue<byte[]> _outbox;
    private readonly SharedState _shared;

    private InMemoryConnection(ConcurrentQueue<byte[]> inbox, ConcurrentQueue<byte[]> outbox, SharedState shared)
    {
        _inbox = inbox;
        _outbox = outbox;
        _shared = shared;
    }

    /// <inheritdoc />
    public bool IsOpen => !_shared.Closed;

    /// <summary>Creates a connected pair: give <c>Client</c> to the game and <c>Server</c> to the server.</summary>
    public static (InMemoryConnection Client, InMemoryConnection Server) CreatePair()
    {
        var toServer = new ConcurrentQueue<byte[]>();
        var toClient = new ConcurrentQueue<byte[]>();
        var shared = new SharedState();
        return (new InMemoryConnection(toClient, toServer, shared), new InMemoryConnection(toServer, toClient, shared));
    }

    /// <inheritdoc />
    public void Send(IProtocolMessage message)
    {
        if (_shared.Closed)
        {
            throw new System.InvalidOperationException("Connection is closed.");
        }

        _outbox.Enqueue(ProtocolSerializer.Serialize(message));
    }

    /// <inheritdoc />
    public bool TryReceive(out IProtocolMessage? message)
    {
        if (_inbox.TryDequeue(out byte[]? frame))
        {
            message = ProtocolSerializer.Deserialize(frame);
            return true;
        }

        message = null;
        return false;
    }

    /// <inheritdoc />
    public void Close() => _shared.Closed = true;

    private sealed class SharedState
    {
        public volatile bool Closed;
    }
}
