using System.Collections.Generic;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;

namespace CivSim.Game.Net;

/// <summary>
/// A connection that first hands out messages already read from it (the lobby screen read the Welcome of a game in progress),
/// then reads on (MP3).
/// </summary>
public sealed class BufferedConnection(IConnection inner, IEnumerable<IProtocolMessage> first) : IConnection
{
    private readonly Queue<IProtocolMessage> _first = new(first);

    /// <inheritdoc />
    public bool IsOpen => inner.IsOpen || _first.Count > 0;

    /// <inheritdoc />
    public int Backlog => inner.Backlog;

    /// <inheritdoc />
    public bool Congested => inner.Congested;

    /// <inheritdoc />
    public void Send(IProtocolMessage message) => inner.Send(message);

    /// <inheritdoc />
    public bool TryReceive(out IProtocolMessage? message)
    {
        if (_first.TryDequeue(out IProtocolMessage? m))
        {
            message = m;
            return true;
        }

        return inner.TryReceive(out message);
    }

    /// <inheritdoc />
    public void Close() => inner.Close();
}
