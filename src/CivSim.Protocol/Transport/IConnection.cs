using CivSim.Protocol.Messages;

namespace CivSim.Protocol.Transport;

/// <summary>
/// One end of a client–server connection. Single player uses <see cref="InMemoryConnection"/>; a network
/// transport (future update) implements the same interface.
/// </summary>
public interface IConnection
{
    /// <summary>False after either side closed the connection.</summary>
    bool IsOpen { get; }

    /// <summary>Sends a message to the other end. Thread-safe.</summary>
    void Send(IProtocolMessage message);

    /// <summary>Takes the next received message, if any. Thread-safe.</summary>
    bool TryReceive(out IProtocolMessage? message);

    /// <summary>
    /// Messages sent from this end that the other end has not taken yet. The server sends no new views to a client that
    /// falls behind, so a slow or hidden window never piles up updates in memory.
    /// </summary>
    int Backlog { get; }

    /// <summary>Closes both ends.</summary>
    void Close();
}
