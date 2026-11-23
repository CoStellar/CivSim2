using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using MessagePack;
using Xunit;

namespace CivSim.Tests.Protocol;

/// <summary>
/// Every message type of the protocol, the new game's included (2026-11-22): registered in the union and round-tripping
/// to the same bytes. The hand-written cases in <see cref="ProtocolSerializerTests"/> cover only the legacy messages.
/// </summary>
public sealed class EveryMessageTests
{
    private static readonly Type[] Messages = [.. typeof(IProtocolMessage).Assembly.GetTypes()
        .Where(t => typeof(IProtocolMessage).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
        .OrderBy(t => t.FullName, StringComparer.Ordinal)];

    [Fact]
    public void EveryMessageIsRegisteredInTheUnion()
    {
        var registered = typeof(IProtocolMessage).GetCustomAttributes<UnionAttribute>().Select(u => u.SubType).ToHashSet();

        Assert.NotEmpty(Messages);
        Assert.Empty(Messages.Where(t => !registered.Contains(t)).Select(t => t.Name));
    }

    [Fact]
    public void EveryMessageRoundTripsToTheSameBytes()
    {
        foreach (Type t in Messages)
        {
            // An empty instance (nulls and zeros) is enough to see that every member has a key and comes back.
            var message = (IProtocolMessage)RuntimeHelpers.GetUninitializedObject(t);
            byte[] frame = ProtocolSerializer.Serialize(message);
            IProtocolMessage back = ProtocolSerializer.Deserialize(frame);

            Assert.Equal(t, back.GetType());
            Assert.True(frame.AsSpan().SequenceEqual(ProtocolSerializer.Serialize(back)), $"{t.Name} changes on the way");
        }
    }
}
