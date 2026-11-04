using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using Xunit;

namespace CivSim.Tests.Protocol;

public class ProtocolSerializerTests
{
    public static IEnumerable<object[]> AllMessages()
    {
        yield return [new HelloMessage(ProtocolInfo.Version, "Patryk")];
        yield return [new WelcomeMessage(ProtocolInfo.Version, 1, 2)];
        yield return [new ConnectionRejectedMessage("Full")];
        yield return [new SubmitCommandMessage(2, 7, new RenameCivilizationDto("Rome"))];
        yield return [new CommandRejectedMessage(7, "Nope")];
        yield return [new EndTurnMessage(12)];
        yield return [new GameViewMessage(3, 1, [new WorldView(0, "Earth", 64, 40)], [new CivilizationView(1, "Rome", true)])];
    }

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void EveryMessageRoundTrips(IProtocolMessage message)
    {
        byte[] frame = ProtocolSerializer.Serialize(message);
        IProtocolMessage back = ProtocolSerializer.Deserialize(frame);

        Assert.Equal(message.GetType(), back.GetType());
        if (message is GameViewMessage view)
        {
            var v = (GameViewMessage)back;
            Assert.Equal(view.Turn, v.Turn);
            Assert.Equal(view.Worlds, v.Worlds);
            Assert.Equal(view.Civilizations, v.Civilizations);
        }
        else
        {
            Assert.Equal(message, back);
        }
    }

    [Fact]
    public void FrameStartsWithProtocolVersion()
    {
        byte[] frame = ProtocolSerializer.Serialize(new EndTurnMessage(1));
        Assert.Equal(ProtocolInfo.Version, BinaryPrimitives.ReadInt32LittleEndian(frame));
    }

    [Fact]
    public void WrongVersionIsRejected()
    {
        byte[] frame = ProtocolSerializer.Serialize(new EndTurnMessage(1));
        BinaryPrimitives.WriteInt32LittleEndian(frame, ProtocolInfo.Version + 1);
        Assert.Throws<ProtocolException>(() => ProtocolSerializer.Deserialize(frame));
    }

    [Fact]
    public void GarbageIsRejected()
    {
        Assert.Throws<ProtocolException>(() => ProtocolSerializer.Deserialize(new byte[] { 1, 0 }));
        byte[] bad = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(bad, ProtocolInfo.Version);
        bad[4] = 0xC1; // never-used MessagePack byte
        Assert.Throws<ProtocolException>(() => ProtocolSerializer.Deserialize(bad));
    }

    [Fact]
    public void WireFormatIsStable()
    {
        // Compatibility guard: version 1 frames must keep decoding the same way.
        byte[] frame = ProtocolSerializer.Serialize(new HelloMessage(1, "A"));
        Assert.Equal("01000000920092" + "01A141", Convert.ToHexString(frame));
    }

    [Fact]
    public void InMemoryConnectionDeliversBothWaysInOrder()
    {
        (InMemoryConnection client, InMemoryConnection server) = InMemoryConnection.CreatePair();
        client.Send(new EndTurnMessage(1));
        client.Send(new EndTurnMessage(2));
        server.Send(new ConnectionRejectedMessage("x"));

        Assert.True(server.TryReceive(out IProtocolMessage? a));
        Assert.True(server.TryReceive(out IProtocolMessage? b));
        Assert.False(server.TryReceive(out _));
        Assert.Equal(new EndTurnMessage(1), a);
        Assert.Equal(new EndTurnMessage(2), b);
        Assert.True(client.TryReceive(out IProtocolMessage? c));
        Assert.IsType<ConnectionRejectedMessage>(c);

        client.Close();
        Assert.False(server.IsOpen);
        Assert.Throws<InvalidOperationException>(() => server.Send(new EndTurnMessage(3)));
    }
}
