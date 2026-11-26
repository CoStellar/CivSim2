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

/// <summary>
/// Who leads what in a game with other players (multiplayer plan, chapter 19; MP4b): candidates and the vote on them, observers of
/// the whole map, players whose civilization fell, removing a player and its return.
/// </summary>
public sealed class PlaySeatsTests
{
    [Fact]
    public void ANewcomerInARunningGameWaitsWithoutTheMapUntilThePlayersTakeItIn()
    {
        long now = Stopwatch.GetTimestamp();
        (PlayHost host, IConnection ala, IConnection ola) = Two(() => now);
        using (host)
        {
            IConnection ewa = host.Connect();
            ewa.Send(new HelloMessage(ProtocolInfo.Version, "Ewa"));
            host.RunTicks(1);
            List<IProtocolMessage> got = Drain(ewa);
            Assert.Equal(WelcomeMessage.NoCiv, got.OfType<WelcomeMessage>().Single().CivId);
            Assert.Empty(got.OfType<MapChunkMessage>());
            Assert.Empty(got.OfType<PlayViewMessage>());
            PlayPlayersMessage list = got.OfType<PlayPlayersMessage>().Last();
            Assert.Equal(PlayPlayerInfo.Candidate, list.Players[list.You].Role);
            Assert.NotNull(list.Takeable);
            Assert.Equal(host.Game.Civs.Count - 2, list.Takeable!.Count); // every civilization but the two led by players
            Assert.True(list.Takeable.Count > 0);

            int civ = list.Takeable[0].Civ;
            ewa.Send(new PlayJoinMessage(civ));
            host.RunTicks(1);
            PlayVoteInfo vote = Drain(ala).OfType<PlayPlayersMessage>().Last().Vote!;
            Assert.Equal(PlayVoteInfo.Join, vote.Kind);
            Assert.Equal("Ewa", vote.By);
            Assert.Equal(host.Game.Civs[civ].Name, vote.About);
            Assert.Equal(2, vote.Eligible); // nobody has played 3 minutes yet: every player votes
            Assert.True(vote.CanVote);

            ala.Send(new PlayVoteMessage(vote.Id, true));
            ola.Send(new PlayVoteMessage(vote.Id, true));
            host.RunTicks(2);
            got = Drain(ewa);
            Assert.Equal(civ, got.OfType<WelcomeMessage>().Single().CivId);
            Assert.NotEmpty(got.OfType<MapChunkMessage>());
            Assert.False(host.Game.Civs[civ].IsAi);
        }
    }

    [Fact]
    public void APlayerVotesOnPlayersOnlyAfterThreeMinutesAndATieKeepsTheCandidateOut()
    {
        long now = Stopwatch.GetTimestamp();
        (PlayHost host, IConnection ala, IConnection ola) = Two(() => now);
        using (host)
        {
            now += Stopwatch.Frequency * 200; // both play over 3 minutes
            IConnection ewa = host.Connect();
            ewa.Send(new HelloMessage(ProtocolInfo.Version, "Ewa"));
            host.RunTicks(1);
            int civ = Drain(ewa).OfType<PlayPlayersMessage>().Last().Takeable![0].Civ;
            ewa.Send(new PlayJoinMessage(civ));
            host.RunTicks(1);
            PlayVoteInfo vote = Drain(ala).OfType<PlayPlayersMessage>().Last().Vote!;
            Assert.Equal(2, vote.Eligible);
            ala.Send(new PlayVoteMessage(vote.Id, true));
            ola.Send(new PlayVoteMessage(vote.Id, false));
            host.RunTicks(2);
            Assert.Empty(Drain(ewa).OfType<WelcomeMessage>());
            Assert.True(host.Game.Civs[civ].IsAi); // 1 of 2: not more than half
        }
    }

    [Fact]
    public void AnObserverSeesTheWholeMapAndCannotAskForACivilization()
    {
        (PlayHost host, IConnection ala, IConnection _) = Two();
        using (host)
        {
            IConnection eye = host.Connect();
            eye.Send(new HelloMessage(ProtocolInfo.Version, "Eye", ClientRole.Observer));
            host.RunTicks(2);
            List<IProtocolMessage> got = Drain(eye);
            Assert.NotEmpty(got.OfType<MapChunkMessage>());
            PlayViewMessage view = got.OfType<PlayViewMessage>().Last();
            Assert.True(view.Spectator);
            Assert.False(view.Lost);
            Assert.Equal(-1, view.Civ);
            Assert.True(view.Settlements.Count > 0 || got.OfType<PlayObjectsMessage>().Any(o => o.Settlements.Count > 0)); // everyone's settlements

            eye.Send(new PlayJoinMessage(2));
            host.RunTicks(1);
            Assert.True(host.Game.Civs[2].IsAi);
            Assert.Null(Drain(ala).OfType<PlayPlayersMessage>().Last().Vote);
        }
    }

    [Fact]
    public void APlayerWhoseCivilizationFellWatchesTheWholeMapAndDoesNotVote()
    {
        long now = Stopwatch.GetTimestamp();
        (PlayHost host, IConnection ala, IConnection ola) = Two(() => now);
        using (host)
        {
            host.Game.Civs[1].Dead = true; // Ola's civilization is gone
            host.RunTicks(2);
            List<IProtocolMessage> got = Drain(ola);
            PlayViewMessage view = got.OfType<PlayViewMessage>().Last();
            Assert.True(view.Spectator);
            Assert.True(view.Lost);
            Assert.NotEmpty(got.OfType<MapChunkMessage>()); // the whole map again
            PlayPlayersMessage list = got.OfType<PlayPlayersMessage>().Last();
            Assert.Equal(PlayPlayerInfo.Fallen, list.Players[list.You].Role);

            ola.Send(new SetSpeedMessage(SimulationSpeed.Paused));
            host.RunTicks(1);
            Assert.Contains(Drain(ola).OfType<CommandRejectedMessage>(), r => r.Reason.Contains("gone", StringComparison.Ordinal));
            Assert.NotEqual(SimulationSpeed.Paused, host.Speed);
        }
    }

    [Fact]
    public void TheHostRemovesAPlayerWhoseCivilizationNobodyElseMayTakeAndAVoteBringsItBack()
    {
        long now = Stopwatch.GetTimestamp();
        (PlayHost host, IConnection ala, IConnection ola) = Two(() => now);
        using (host)
        {
            string olaKey = host.ReturnKeyOf(1);
            ala.Send(new PlayKickMessage("Ola"));
            host.RunTicks(1);
            Assert.Contains(Drain(ola).OfType<ConnectionRejectedMessage>(), r => r.Reason.Contains("removed", StringComparison.Ordinal));
            Assert.True(host.Game.Civs[1].IsAi);

            // A newcomer cannot take the removed player's civilization.
            IConnection ewa = host.Connect();
            ewa.Send(new HelloMessage(ProtocolInfo.Version, "Ewa"));
            host.RunTicks(1);
            Assert.DoesNotContain(Drain(ewa).OfType<PlayPlayersMessage>().Last().Takeable!, t => t.Civ == 1);
            ewa.Send(new PlayJoinMessage(1));
            host.RunTicks(1);
            Assert.Contains(Drain(ewa).OfType<CommandRejectedMessage>(), r => r.Reason.Contains("cannot be taken", StringComparison.Ordinal));

            // The removed player comes back with its key: a candidate for its own civilization only; Ala alone votes.
            IConnection back = host.Connect();
            back.Send(new HelloMessage(ProtocolInfo.Version, "Ola", ReturnKey: olaKey));
            host.RunTicks(1);
            Assert.Equal(WelcomeMessage.NoCiv, Drain(back).OfType<WelcomeMessage>().Single().CivId);
            PlayVoteInfo vote = Drain(ala).OfType<PlayPlayersMessage>().Last().Vote!;
            Assert.Equal(PlayVoteInfo.Join, vote.Kind);
            Assert.Equal(1, vote.Value);
            Assert.Equal(1, vote.Eligible);
            ala.Send(new PlayVoteMessage(vote.Id, true));
            host.RunTicks(2);
            Assert.Equal(1, Drain(back).OfType<WelcomeMessage>().Single().CivId);
            Assert.False(host.Game.Civs[1].IsAi);
        }
    }

    [Fact]
    public void APlayerProposesToRemoveAnotherAndMoreThanHalfMustAgree()
    {
        long now = Stopwatch.GetTimestamp();
        (PlayHost host, IConnection ala, IConnection ola) = Two(() => now, hostFirst: false);
        using (host)
        {
            ala.Send(new PlayKickMessage("Ola"));
            host.RunTicks(1);
            PlayVoteInfo vote = Drain(ola).OfType<PlayPlayersMessage>().Last().Vote!;
            Assert.Equal(PlayVoteInfo.Kick, vote.Kind);
            Assert.Equal("Ola", vote.About);
            Assert.True(vote.CanVote); // the one concerned votes too
            ola.Send(new PlayVoteMessage(vote.Id, false));
            host.RunTicks(2);
            Assert.Empty(Drain(ola).OfType<ConnectionRejectedMessage>()); // 1 of 2: Ola stays
            Assert.False(host.Game.Civs[1].IsAi);
        }
    }

    private static (PlayHost Host, IConnection A, IConnection B) Two(Func<long>? clock = null, bool hostFirst = true)
    {
        PlayHost host = PlayHost.Create(new PlayOptions(4, 40, 24, 3) { StartPhase = false, Dedicated = true, HumanSeats = 3, StartSpeed = 1 });
        host.AutosaveOn = false;
        if (clock is not null)
        {
            host.Clock = clock;
        }

        // Two players from the lobby (their places), the first one the host; then the game runs a while.
        (IConnection a, IConnection sa) = InMemoryConnection.CreatePair();
        (IConnection b, IConnection sb) = InMemoryConnection.CreatePair();
        host.Accept(sa, 0, "Ala", 0, host: hostFirst);
        host.Accept(sb, 1, "Ola");
        a.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        b.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
        host.RunTicks(3);
        Drain(a);
        Drain(b);
        return (host, a, b);
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
