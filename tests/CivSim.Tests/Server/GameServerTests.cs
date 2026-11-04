using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Hashing;
using CivSim.Core.State;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Server;

public class GameServerTests
{
    private static List<IProtocolMessage> Drain(IConnection connection)
    {
        var list = new List<IProtocolMessage>();
        while (connection.TryReceive(out IProtocolMessage? m))
        {
            list.Add(m!);
        }

        return list;
    }

    private static (GameServer Server, InMemoryConnection Client) Connect(ulong seed = 1)
    {
        var server = new GameServer(GameSettings.Test(seed));
        (InMemoryConnection client, InMemoryConnection serverEnd) = InMemoryConnection.CreatePair();
        server.Accept(serverEnd);
        client.Send(new HelloMessage(ProtocolInfo.Version, "Patryk"));
        server.Pump();
        return (server, client);
    }

    [Fact]
    public void HelloGetsWelcomeAndInitialView()
    {
        (GameServer server, InMemoryConnection client) = Connect();
        List<IProtocolMessage> messages = Drain(client);

        WelcomeMessage welcome = Assert.IsType<WelcomeMessage>(messages[0]);
        Assert.Equal(1, welcome.PlayerId);
        Assert.Equal(0, welcome.CivId);
        GameViewMessage view = Assert.IsType<GameViewMessage>(messages[1]);
        Assert.Equal(0, view.Turn);
        Assert.Equal(4, view.Civilizations.Count);
        Assert.Equal(new PlayerId(1), server.State.Civilizations[0].Controller);
    }

    [Fact]
    public void WrongProtocolVersionIsRefused()
    {
        var server = new GameServer(GameSettings.Test(1));
        (InMemoryConnection client, InMemoryConnection serverEnd) = InMemoryConnection.CreatePair();
        server.Accept(serverEnd);
        client.Send(new HelloMessage(ProtocolInfo.Version + 1, "Old client"));
        server.Pump();

        Assert.IsType<ConnectionRejectedMessage>(Drain(client)[0]);
        Assert.False(client.IsOpen);
    }

    [Fact]
    public void TurnRunsWhenThePlayerEndsIt()
    {
        (GameServer server, InMemoryConnection client) = Connect();
        Drain(client);

        Assert.False(server.Pump());
        client.Send(new EndTurnMessage(0));
        Assert.True(server.Pump());

        Assert.Equal(1, server.State.Turn);
        List<IProtocolMessage> after = Drain(client);
        GameViewMessage view = Assert.IsType<GameViewMessage>(after[0]);
        Assert.Equal(1, view.Turn);

        // A new map starts with every chunk changed, so the first turn resends what the player knows.
        Assert.All(after.GetRange(1, after.Count - 1), m => Assert.IsType<MapChunkMessage>(m));
    }

    [Fact]
    public void CommandsForOtherCivilizationsOrUnknownCommandsAreRejected()
    {
        (GameServer server, InMemoryConnection client) = Connect();
        Drain(client);

        client.Send(new SubmitCommandMessage(3, 10, new RenameCivilizationDto("Stolen")));
        client.Send(new SubmitCommandMessage(0, 11, null!));
        server.Pump();

        List<IProtocolMessage> replies = Drain(client);
        Assert.Equal(10, Assert.IsType<CommandRejectedMessage>(replies[0]).ClientSequence);
        Assert.Equal(11, Assert.IsType<CommandRejectedMessage>(replies[1]).ClientSequence);
        Assert.Equal(0, server.Runner.Commands.Count);
    }

    [Fact]
    public void RuleViolationsComeBackAfterTheTurnWithTheClientSequence()
    {
        (GameServer server, InMemoryConnection client) = Connect();
        Drain(client);

        client.Send(new SubmitCommandMessage(0, 42, new RenameCivilizationDto("")));
        client.Send(new EndTurnMessage(0));
        server.Pump();

        List<IProtocolMessage> replies = Drain(client);
        Assert.Equal(42, Assert.IsType<CommandRejectedMessage>(replies[0]).ClientSequence);
        Assert.IsType<GameViewMessage>(replies[1]);
    }

    [Fact]
    public void DisconnectedCivilizationGoesBackToTheAi()
    {
        (GameServer server, InMemoryConnection client) = Connect();
        client.Close();
        server.Pump();

        Assert.Equal(0, server.ConnectedPlayers);
        Assert.Equal(PlayerId.Ai, server.State.Civilizations[0].Controller);
    }

    [Fact]
    public void CommandThroughProtocolGivesTheSameStateAsDirectCommand()
    {
        // Through the protocol.
        (GameServer server, InMemoryConnection client) = Connect(seed: 77);
        client.Send(new SubmitCommandMessage(0, 1, new RenameCivilizationDto("Rome")));
        client.Send(new EndTurnMessage(0));
        server.Pump();
        for (long t = 1; t <= 20; t++)
        {
            client.Send(new EndTurnMessage(t));
            server.Pump();
        }

        // Directly on the core.
        var direct = new Core.Simulation.TurnRunner(GameState.CreateNew(GameSettings.Test(77)));
        direct.Commands.Enqueue(new PlayerId(1), new CivId(0), new RenameCivilizationCommand("Rome"));
        for (int t = 0; t < 21; t++)
        {
            direct.RunTurn();
        }

        Assert.Equal(21, server.State.Turn);
        Assert.Equal(GameStateHash.Compute(direct.State), GameStateHash.Compute(server.State));
    }
}
