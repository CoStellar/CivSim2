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
        yield return [new GameViewMessage(4, -1, [], [], 1234, SimulationSpeed.X5, [new ChronicleEntry(4, "Speed set to X5.")])];
        yield return [new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer)];
        yield return [new SetSpeedMessage(SimulationSpeed.Max)];
        yield return [new ContentMessage([new BiomeInfo(0, "deep_ocean", "Deep Ocean", true, 0x0a2647)], [new DepositInfo(1, "game", "Game", 0x8a562d)], ["None"], ["Flat"])];
        yield return [new MapChunkMessage(0, 2, 5, 32, 0, 2, 1, [2, 0], [120, 0], [-3, 0], [50, 0], [12, 0], [1, 0], [0, 0], [70, 0], [3, 0], [500, 0], [0, 0], [-1, -1], [1, 0, 2, 0, 0, 0])];
    }

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void EveryMessageRoundTrips(IProtocolMessage message)
    {
        byte[] frame = ProtocolSerializer.Serialize(message);
        IProtocolMessage back = ProtocolSerializer.Deserialize(frame);

        Assert.Equal(message.GetType(), back.GetType());

        // Records holding lists or arrays compare by reference, so compare their encoded bytes instead.
        Assert.Equal(frame, ProtocolSerializer.Serialize(back));
        if (message is GameViewMessage or ContentMessage or MapChunkMessage)
        {
            return;
        }

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
        // Compatibility guard: version 2 frames must keep encoding the same way.
        // Version 2 added the client role to Hello (observer mode) and the map, content and speed messages.
        byte[] frame = ProtocolSerializer.Serialize(new HelloMessage(1, "A"));
        Assert.Equal("02000000" + "920093" + "01A14100", Convert.ToHexString(frame));
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
