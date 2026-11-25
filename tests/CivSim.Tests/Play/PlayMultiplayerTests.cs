using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Play;

/// <summary>A server for several people (multiplayer plan, MP2).</summary>
public sealed class PlayMultiplayerTests
{
    [Trait("Category", "Slow")]
    [Fact]
    public void APlayerWhoLostTheConnectionComesBackToTheSamePeople()
    {
        using PlayHost host = Dedicated();
        IConnection first = host.Connect();
        first.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        host.RunTicks(1);
        WelcomeMessage welcome = Take<WelcomeMessage>(first);
        Assert.Equal(32, welcome.ReturnKey.Length);
        Assert.False(host.Game.Civs[welcome.CivId].IsAi);

        first.Close();
        host.RunTicks(10);
        Assert.True(host.Game.Civs[welcome.CivId].IsAi); // the AI leads it while she is away

        IConnection stranger = host.Connect(); // someone else does not get her people
        stranger.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
        host.RunTicks(1);
        Assert.NotEqual(welcome.CivId, Take<WelcomeMessage>(stranger).CivId);

        IConnection back = host.Connect();
        back.Send(new HelloMessage(ProtocolInfo.Version, "Ala", ReturnKey: welcome.ReturnKey));
        host.RunTicks(1);
        WelcomeMessage again = Take<WelcomeMessage>(back);
        Assert.Equal(welcome.CivId, again.CivId);
        Assert.Equal(welcome.ReturnKey, again.ReturnKey);
        Assert.False(host.Game.Civs[welcome.CivId].IsAi);
    }

    [Fact]
    public void AnotherGameVersionAndAWrongPasswordAreTurnedAway()
    {
        using PlayHost host = Dedicated();
        host.Password = "otter";
        IConnection old = host.Connect();
        old.Send(new HelloMessage(ProtocolInfo.Version, "Ala", GameVersion: "26.10.1", Password: "otter"));
        IConnection none = host.Connect();
        none.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
        IConnection wrong = host.Connect();
        wrong.Send(new HelloMessage(ProtocolInfo.Version, "Ela", Password: "beaver"));
        IConnection right = host.Connect();
        right.Send(new HelloMessage(ProtocolInfo.Version, "Iza", Password: "otter"));
        host.RunTicks(1);

        Assert.Contains("26.10.1", Take<ConnectionRejectedMessage>(old).Reason, StringComparison.Ordinal);
        Assert.Equal("This game has a password.", Take<ConnectionRejectedMessage>(none).Reason);
        Assert.Equal("Wrong password.", Take<ConnectionRejectedMessage>(wrong).Reason);
        Assert.IsType<WelcomeMessage>(Next(right));
        Assert.False(old.IsOpen);
        Assert.Equal(1, host.Players);
    }

    [Fact]
    public void AMessageThatFailsOnTheServerDoesNotStopTheGame()
    {
        using PlayHost host = Dedicated();
        host.BeforeHandle = m =>
        {
            if (m is PlaySummaryRequestMessage)
            {
                throw new InvalidOperationException("a bug in the server");
            }
        };
        IConnection player = host.Connect();
        player.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        host.RunTicks(1);
        long ticks = host.Ticks;
        player.Send(new PlaySummaryRequestMessage());
        host.RunTicks(5);
        Assert.Equal(ticks + 5, host.Ticks);
        Assert.Equal(1, host.ClientFaults);
        Assert.Contains(Drain(player), m => m is CommandRejectedMessage r && r.Reason.Contains("could not carry out", StringComparison.Ordinal));
        Assert.True(player.IsOpen);

        player.Send(new PlaySummaryRequestMessage()); // a client that keeps failing is disconnected; the game goes on
        player.Send(new PlaySummaryRequestMessage());
        host.RunTicks(5);
        Assert.False(player.IsOpen);
        Assert.Equal(ticks + 10, host.Ticks);
        Assert.Null(host.Error);
    }

    [Fact]
    public void AFloodOfOrdersIsRefusedAndThenDisconnected()
    {
        using PlayHost host = Dedicated();
        IConnection player = host.Connect();
        player.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        host.RunTicks(1);
        for (int i = 0; i < 200; i++)
        {
            player.Send(new PlaySummaryRequestMessage());
        }

        host.RunTicks(1);
        List<IProtocolMessage> got = Drain(player);
        Assert.Equal(PlayHost.MessageBurst, got.Count(m => m is PlaySummaryMessage));
        Assert.Contains(got, m => m is CommandRejectedMessage r && r.Reason.StartsWith("Too many orders", StringComparison.Ordinal));
        Assert.True(player.IsOpen);

        for (int i = 0; i < PlayHost.FloodLimit; i++)
        {
            player.Send(new PlaySummaryRequestMessage());
        }

        host.RunTicks(1);
        Assert.False(player.IsOpen);
    }

    [Fact]
    public void WithTwoPlayersDebugKeysAndFastSpeedsAreRefused()
    {
        using PlayHost host = PlayHost.Create(new PlayOptions(4, 40, 24, 3) { StartPhase = false });
        host.AutosaveOn = false;
        IConnection a = host.Connect();
        a.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        IConnection b = host.Connect();
        b.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
        host.RunTicks(1);
        Drain(a);
        a.Send(new PlayCommandMessage(7, PlayCommandKind.Debug, 0, 0, 0, false));
        a.Send(new SetSpeedMessage(SimulationSpeed.X4));
        host.RunTicks(1);
        List<string> reasons = [.. Drain(a).OfType<CommandRejectedMessage>().Select(r => r.Reason)];
        Assert.Contains("Debug keys work only in a game with one player.", reasons);
        Assert.Contains("With other players the game runs at x1 or x2.", reasons);
    }

    // Joining during the start phase, playing, leaving and joining again: the save plays all of it again at the same steps.
    [Trait("Category", "Slow")]
    [Fact]
    public void AGameWherePlayersJoinAndLeaveLoadsAsTheSameGame()
    {
        string name = "test joins " + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string path = PlaySaves.PathOf(name);
        try
        {
            ulong saved;
            using (PlayHost host = PlayHost.Create(new PlayOptions(6, 66, 40, 4) { StartPhase = true, Dedicated = true }))
            {
                host.AutosaveOn = false;
                host.RunTicks(13, viewEvery: 0); // the start phase: ticks stand still, steps go on
                IConnection a = host.Connect();
                a.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
                host.RunTicks(1, viewEvery: 0);
                while (host.Picking)
                {
                    host.RunTicks(1, viewEvery: 0);
                }

                host.RunTicks(600, viewEvery: 0);
                // Ola joins the running game as a candidate (MP4b), asks for a civilization and Ala takes her in.
                IConnection b = host.Connect();
                b.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
                host.RunTicks(1, viewEvery: 0);
                b.Send(new PlayJoinMessage(Drain(b).OfType<PlayPlayersMessage>().Last().Takeable![0].Civ));
                host.RunTicks(1, viewEvery: 0);
                a.Send(new PlayVoteMessage(Drain(a).OfType<PlayPlayersMessage>().Last().Vote!.Id, true));
                host.RunTicks(448, viewEvery: 0);
                a.Close();
                host.RunTicks(300, viewEvery: 0);
                b.Send(new SaveGameRequestMessage(name));
                host.RunTicks(0);
                saved = host.StateHash;
            }

            SavedGame save = PlaySaves.Read(path);
            Assert.Equal(2, save.Format);
            Assert.Empty(save.Players); // nobody led a people at the start of a server without a window
            Assert.Equal(3, save.Commands.Count(c => c.Leader != SavedCommand.Command));
            using PlayHost loaded = PlayHost.Create(new PlayOptions(0) { LoadPath = path });
            loaded.AutosaveOn = false;
            loaded.RunTicks(0);
            Assert.Equal(saved, loaded.StateHash);
            Assert.All(loaded.Game.Civs, c => Assert.True(c.IsAi)); // the players are not back yet
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The same game played without anyone joining is a different game: joining matters, so the test above checks something.
    [Trait("Category", "Slow")]
    [Fact]
    public void JoiningChangesTheGame()
    {
        ulong Play(bool join)
        {
            using PlayHost host = PlayHost.Create(new PlayOptions(6, 66, 40, 4) { StartPhase = false, Dedicated = true });
            host.AutosaveOn = false;
            if (join)
            {
                host.Connect().Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
            }

            host.RunTicks(900, viewEvery: 0);
            return host.StateHash;
        }

        Assert.NotEqual(Play(false), Play(true));
    }

    private static PlayHost Dedicated()
    {
        PlayHost host = PlayHost.Create(new PlayOptions(4, 40, 24, 3) { StartPhase = false, Dedicated = true });
        host.AutosaveOn = false;
        return host;
    }

    private static T Take<T>(IConnection c) => Drain(c).OfType<T>().First();

    private static IProtocolMessage Next(IConnection c) => c.TryReceive(out IProtocolMessage? m) ? m! : throw new InvalidOperationException("nothing came");

    private static List<IProtocolMessage> Drain(IConnection c)
    {
        var all = new List<IProtocolMessage>();
        while (c.TryReceive(out IProtocolMessage? m))
        {
            all.Add(m!);
        }

        return all;
    }
}
