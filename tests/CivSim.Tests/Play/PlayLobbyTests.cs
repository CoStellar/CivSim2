using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Play;

/// <summary>The lobby of a multiplayer game (multiplayer plan, MP3).</summary>
public sealed class PlayLobbyTests
{
    [Trait("Category", "Slow")]
    [Fact]
    public void PlayersMeetChatGetReadyAndTheHostStartsTheGame()
    {
        using PlayLobby lobby = Lobby(seats: 3);
        IConnection ala = Join(lobby, "Ala");
        IConnection ola = Join(lobby, "Ola");
        IConnection ala2 = Join(lobby, "ala");
        LobbyStateMessage seen = Last(ala2);
        Assert.Equal(["Ala", "Ola"], seen.Seats.Take(2).Select(s => s.Name));
        Assert.StartsWith("ala ", seen.Seats[2].Name, StringComparison.Ordinal); // R4-4: a nickname already here gets a suffix
        Assert.True(seen.Seats[0].Host);
        Assert.Equal(2, seen.You);
        Assert.Contains(seen.Settings, l => l.StartsWith("Peoples: 4, places for players: 3", StringComparison.Ordinal));

        ola.Send(new LobbyChatMessage("hello [b]all[/b]\n"));
        lobby.Pump();
        Assert.Contains("Ola: hello [b]all[/b]", Last(ala).Chat); // plain text: the window never reads markup

        Assert.False(Last(ala).CanStart); // Ola and the second Ala are not ready
        ola.Send(new LobbyStartMessage());
        ala.Send(new LobbyStartMessage());
        lobby.Pump();
        Assert.Contains(Drain(ola), m => m is CommandRejectedMessage r && r.Reason == "Only the host starts the game.");
        Assert.Contains(Drain(ala), m => m is CommandRejectedMessage r && r.Reason == "Not every player is ready.");

        ola.Send(new LobbyReadyMessage(true));
        ala2.Send(new LobbyReadyMessage(true));
        lobby.Pump();
        Assert.True(Last(ala).CanStart);
        ala.Send(new LobbyStartMessage());
        lobby.Pump();
        PlayHost game = Assert.IsType<PlayHost>(lobby.Game);
        foreach (IConnection c in new[] { ala, ola, ala2 })
        {
            Assert.Contains(Drain(c), m => m is GameStartingMessage);
            c.Send(new HelloMessage(ProtocolInfo.Version, "again"));
        }

        game.RunTicks(1);
        Assert.Equal(0, Drain(ala).OfType<WelcomeMessage>().Single().CivId); // place n leads people n
        Assert.Equal(1, Drain(ola).OfType<WelcomeMessage>().Single().CivId);
        Assert.Equal(2, Drain(ala2).OfType<WelcomeMessage>().Single().CivId);
        Assert.Equal(3, game.Players);
        Assert.True(game.Game.Civs[3].IsAi);

        IConnection late = lobby.Connect(); // after the start a newcomer goes to the game, up to the places for players
        late.Send(new HelloMessage(ProtocolInfo.Version, "Iza"));
        game.RunTicks(1);
        Assert.Equal("Every place for players is taken.", Drain(late).OfType<ConnectionRejectedMessage>().Single().Reason);
    }

    [Fact]
    public void TheLobbyTurnsAwayAWrongPasswordAndAFullGameAndPassesOnTheHost()
    {
        using PlayLobby lobby = PlayLobby.Create(new PlayOptions(4, 40, 24, 3) { StartPhase = false, HumanSeats = 2 }, new LobbyConfig { Password = "otter" });
        IConnection wrong = lobby.Connect();
        wrong.Send(new HelloMessage(ProtocolInfo.Version, "Ela", Password: "beaver"));
        IConnection ala = Join(lobby, "Ala", "otter");
        IConnection ola = Join(lobby, "Ola", "otter");
        IConnection third = Join(lobby, "Iza", "otter");
        Assert.Equal("Wrong password.", Drain(wrong).OfType<ConnectionRejectedMessage>().Single().Reason);
        Assert.Equal("The game is full.", Drain(third).OfType<ConnectionRejectedMessage>().Single().Reason);

        ala.Close(); // the host leaves: the player there longest is the host now
        lobby.Pump();
        LobbyStateMessage state = Last(ola);
        Assert.Equal("Ola", Assert.Single(state.Seats).Name);
        Assert.True(state.Seats[0].Host);
        Assert.True(state.CanStart);
        Assert.Contains("Ala left.", state.Chat);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void TwoPlayersOverTheNetworkMeetInTheLobbyAndPlay()
    {
        using PlayLobby lobby = Lobby(seats: 2);
        using var listener = new PlayListener(lobby.Accept, 0, System.Net.IPAddress.Loopback);
        using TcpConnection ala = TcpConnection.Connect("127.0.0.1", listener.Port, TimeSpan.FromSeconds(5));
        using TcpConnection ola = TcpConnection.Connect("127.0.0.1", listener.Port, TimeSpan.FromSeconds(5));
        ala.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        WaitFor(lobby, () => lobby.Players == 1);
        ola.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
        ola.Send(new LobbyReadyMessage(true));
        WaitFor(lobby, () => lobby.Players == 2);
        ala.Send(new LobbyStartMessage());
        WaitFor(lobby, () => lobby.Game is not null);
        PlayHost game = lobby.Game!;
        foreach (TcpConnection c in new[] { ala, ola })
        {
            Protocol.TcpConnectionTests.WaitUntil(() => c.TryReceive(out IProtocolMessage? m) && m is GameStartingMessage);
            c.Send(new HelloMessage(ProtocolInfo.Version, "again"));
        }

        Protocol.TcpConnectionTests.WaitUntil(() =>
        {
            game.RunTicks(1);
            return game.Players == 2;
        });
    }

    private static PlayLobby Lobby(int seats) =>
        PlayLobby.Create(new PlayOptions(4, 40, 24, 4) { StartPhase = false, HumanSeats = seats }, new LobbyConfig { GameName = "Test" });

    private static IConnection Join(PlayLobby lobby, string name, string password = "")
    {
        IConnection c = lobby.Connect();
        c.Send(new HelloMessage(ProtocolInfo.Version, name, Password: password));
        lobby.Pump();
        return c;
    }

    private static void WaitFor(PlayLobby lobby, Func<bool> done) => Protocol.TcpConnectionTests.WaitUntil(() =>
    {
        lobby.Pump();
        return done();
    });

    // The newest lobby this connection got, also from messages read before (the lobby sends it only after a change).
    private static LobbyStateMessage Last(IConnection c)
    {
        Drain(c);
        lock (Seen)
        {
            return Seen[c];
        }
    }

    private static readonly Dictionary<IConnection, LobbyStateMessage> Seen = new(ReferenceEqualityComparer.Instance);

    private static List<IProtocolMessage> Drain(IConnection c)
    {
        var all = new List<IProtocolMessage>();
        while (c.TryReceive(out IProtocolMessage? m))
        {
            all.Add(m!);
            if (m is LobbyStateMessage state)
            {
                lock (Seen)
                {
                    Seen[c] = state;
                }
            }
        }

        return all;
    }
}
