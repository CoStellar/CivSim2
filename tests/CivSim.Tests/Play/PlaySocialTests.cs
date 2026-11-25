using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Play;

/// <summary>A game with other players (multiplayer plan, MP4): players and ping, chat, the speed vote, the pause.</summary>
public sealed class PlaySocialTests
{
    [Fact]
    public void PlayersSeeEachOtherWithPingAndARepeatedNicknameGetsASuffix()
    {
        long now = Stopwatch.GetTimestamp();
        (PlayHost host, IConnection ala, IConnection ola) = Two("Ala", "ala", () => now);
        using (host)
        {
            now += 3 * Stopwatch.Frequency; // the next ping and list of players
            host.RunTicks(1);
            foreach (PlayPingMessage ping in Drain(ola).OfType<PlayPingMessage>())
            {
                ola.Send(ping); // the client echoes the server's ping
            }

            now += Stopwatch.Frequency / 20; // 50 ms there and back
            host.RunTicks(1);
            now += Stopwatch.Frequency;
            host.RunTicks(1);
            PlayPlayersMessage seen = Drain(ala).OfType<PlayPlayersMessage>().Last();
            Assert.Equal(2, seen.Players.Count);
            Assert.Equal("Ala", seen.Players[0].Name);
            Assert.StartsWith("ala ", seen.Players[1].Name, StringComparison.Ordinal);
            Assert.Equal(seen.Players[1].Name.Length - 3, seen.Players[1].SuffixLength);
            Assert.Equal(PlayHost.PausesPerPlayer, seen.Players[0].PausesLeft);
            Assert.Equal(0, seen.You);
            Assert.Equal(50, seen.Players[1].PingMs);
        }
    }

    [Fact]
    public void ChatGoesToEveryoneAsPlainTextAndToAlliesOnlyWithAnAlly()
    {
        (PlayHost host, IConnection ala, IConnection ola) = Two("Ala", "Ola");
        using (host)
        {
            ala.Send(new PlayChatMessage(false, "Hello\u0007 [b]there[/b]\n"));
            ola.Send(new PlayChatMessage(true, "only for friends"));
            host.RunTicks(1);
            List<IProtocolMessage> olaGot = Drain(ola);
            PlayChatLineMessage line = olaGot.OfType<PlayChatLineMessage>().Single(l => l.From == "Ala");
            Assert.Contains(olaGot.OfType<CommandRejectedMessage>(), r => r.Reason.StartsWith("No ally", StringComparison.Ordinal));
            Assert.Equal("Hello [b]there[/b]", line.Text); // control characters gone; the window shows the rest as text, not markup
            Assert.False(line.Allies);
            Assert.DoesNotContain(Drain(ala).OfType<PlayChatLineMessage>(), l => l.Text == "only for friends");
        }
    }

    [Fact]
    public void AChangeOfSpeedPassesOnlyWhenMoreThanHalfSayYes()
    {
        long now = Stopwatch.GetTimestamp();
        (PlayHost host, IConnection ala, IConnection ola) = Two("Ala", "Ola", () => now);
        using (host)
        {
            Assert.Equal(SimulationSpeed.X1, host.Speed);
            ala.Send(new SetSpeedMessage(SimulationSpeed.X2));
            host.RunTicks(1);
            PlayVoteInfo vote = Drain(ola).OfType<PlayPlayersMessage>().Last().Vote!;
            Assert.Equal(1, vote.Yes);
            Assert.Equal(2, vote.Eligible);
            Assert.True(vote.CanVote);
            Assert.Equal(SimulationSpeed.X1, host.Speed); // one of two is not more than half

            ola.Send(new PlayVoteMessage(vote.Id, true));
            host.RunTicks(1);
            Assert.Equal(SimulationSpeed.X2, host.Speed);
            Assert.Contains(Drain(ala).OfType<PlayChatLineMessage>(), l => l.From.Length == 0 && l.Text.StartsWith("Speed x2", StringComparison.Ordinal));

            ola.Send(new SetSpeedMessage(SimulationSpeed.X1));
            host.RunTicks(1);
            now += 21 * Stopwatch.Frequency; // nobody else votes: no vote counts as no
            host.RunTicks(1);
            Assert.Equal(SimulationSpeed.X2, host.Speed);

            ola.Send(new SetSpeedMessage(SimulationSpeed.X1)); // again too soon
            host.RunTicks(1);
            Assert.Contains(Drain(ola).OfType<CommandRejectedMessage>(), r => r.Reason.StartsWith("You may propose", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void EveryoneSeesWhoPausedAndThePauseEndsAtTheSpeedBefore()
    {
        (PlayHost host, IConnection ala, IConnection ola) = Two("Ala", "Ola");
        using (host)
        {
            ala.Send(new SetSpeedMessage(SimulationSpeed.Paused));
            host.RunTicks(1);
            PlayPlayersMessage seen = Drain(ola).OfType<PlayPlayersMessage>().Last();
            Assert.Equal("Ala", seen.PausedBy);
            Assert.Equal(PlayHost.PausesPerPlayer - 1, seen.Players[0].PausesLeft);
            Assert.Equal(SimulationSpeed.Paused, host.Speed);

            ala.Send(new SetSpeedMessage(SimulationSpeed.X2)); // ending a pause is not a way round the vote
            host.RunTicks(1);
            Assert.Equal(SimulationSpeed.X1, host.Speed);
            Assert.Contains(Drain(ola).OfType<PlayChatLineMessage>(), l => l.Text == "Ala ended the pause.");
        }
    }

    private static (PlayHost Host, IConnection A, IConnection B) Two(string a, string b, Func<long>? clock = null)
    {
        PlayHost host = PlayHost.Create(new PlayOptions(4, 40, 24, 3) { StartPhase = false, Dedicated = true, HumanSeats = 2, StartSpeed = 1 });
        host.AutosaveOn = false;
        if (clock is not null)
        {
            host.Clock = clock;
        }

        IConnection first = host.Connect();
        first.Send(new HelloMessage(ProtocolInfo.Version, a));
        host.RunTicks(0);
        IConnection second = host.Connect();
        second.Send(new HelloMessage(ProtocolInfo.Version, b));
        host.RunTicks(1);
        Drain(first);
        Drain(second);
        return (host, first, second);
    }

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
