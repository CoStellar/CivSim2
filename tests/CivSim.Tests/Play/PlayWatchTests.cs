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
/// The full view of observers (Patryk, 2026-11-26): the table of civilizations and the intentions log only for spectators, their own
/// chat channel, every unit's route on the whole map, and watching through one civilization's eyes.
/// </summary>
public sealed class PlayWatchTests
{
    [Fact]
    public void AnObserverGetsTheTableAndTheIntentionsOfPlayersButThePlayersDoNot()
    {
        long now = Stopwatch.GetTimestamp();
        (PlayHost host, IConnection ala, IConnection ola, IConnection eye) = Three(() => now);
        using (host)
        {
            // Orders the game takes: research (the first technology it accepts) and a building at home.
            for (int t = 0; t < host.Game.Content.Techs.Count; t++)
            {
                ala.Send(new PlayCommandMessage(t + 1, PlayCommandKind.Research, t, 0, 0, false));
            }

            host.RunTicks(1);
            host.RunTicks(host.Game.Content.Multiplayer.IntentSeconds * 10); // the orders are summed up
            now += Stopwatch.Frequency * 3; // and sent with the next table
            host.RunTicks(1);
            List<IProtocolMessage> got = Drain(eye);
            PlayOverviewMessage overview = got.OfType<PlayOverviewMessage>().Last();
            Assert.Equal(host.Game.Civs.Count, overview.Civs.Count);
            PlayOverviewRow first = overview.Civs[0];
            Assert.Equal("Ala", first.Player);
            Assert.Null(overview.Civs[2].Player); // the AI leads it
            Assert.Equal(host.Game.Civs[0].People, first.People);
            Assert.True(first.CapitalCol >= 0);
            List<PlayIntentLine> intents = [.. got.OfType<PlayOverviewMessage>().SelectMany(o => o.Intents)];
            Assert.Contains(intents, l => l.Civ == 0 && l.Player && l.Text.Contains("researches", StringComparison.Ordinal));
            Assert.Empty(Drain(ala).OfType<PlayOverviewMessage>()); // players never see what the others intend
            Assert.Empty(Drain(ola).OfType<PlayOverviewMessage>());
            Assert.True(host.Game.AiLogOn); // the AI writes its decisions while someone watches
        }
    }

    [Fact]
    public void ObserversWriteOnlyToSpectatorsAndReadEveryone()
    {
        (PlayHost host, IConnection ala, IConnection ola, IConnection eye) = Three();
        using (host)
        {
            eye.Send(new PlayChatMessage(false, "they plan to attack you"));
            ala.Send(new PlayChatMessage(false, "hello"));
            host.RunTicks(1);
            Assert.DoesNotContain(Drain(ola).OfType<PlayChatLineMessage>(), l => l.From == "Eye");
            List<PlayChatLineMessage> seen = [.. Drain(eye).OfType<PlayChatLineMessage>()];
            Assert.Contains(seen, l => l.From == "Eye" && l.Spectators);
            Assert.Contains(seen, l => l.From == "Ala" && !l.Spectators);
        }
    }

    [Fact]
    public void AnObserverWatchesThroughTheEyesOfOneCivilizationAndBackOverTheWholeMap()
    {
        (PlayHost host, IConnection _, IConnection _, IConnection eye) = Three();
        using (host)
        {
            eye.Send(new PlayEyeMessage(2));
            host.RunTicks(2);
            List<IProtocolMessage> got = Drain(eye);
            PlayViewMessage view = got.OfType<PlayViewMessage>().Last();
            Assert.Equal(2, view.Civ);
            Assert.True(view.Spectator);
            Assert.NotNull(view.Settlements.Concat(got.OfType<PlayObjectsMessage>().SelectMany(o => o.Settlements)).FirstOrDefault(s => s.Civ == 2)?.Stock); // its own store, as its player sees it
            Assert.Contains(got.OfType<MapChunkMessage>(), m => m.Visibility.Any(x => x == MapChunkMessage.Unknown)); // its fog again

            eye.Send(new PlayEyeMessage(-1));
            host.RunTicks(2);
            Assert.Equal(-1, Drain(eye).OfType<PlayViewMessage>().Last().Civ);

            // A player cannot look through other eyes.
            Assert.False(host.Game.Civs[2].Dead);
        }
    }

    private static (PlayHost Host, IConnection A, IConnection B, IConnection Eye) Three(Func<long>? clock = null)
    {
        PlayHost host = PlayHost.Create(new PlayOptions(4, 40, 24, 3) { StartPhase = false, Dedicated = true, HumanSeats = 2, StartSpeed = 1 });
        host.AutosaveOn = false;
        if (clock is not null)
        {
            host.Clock = clock;
        }

        (IConnection a, IConnection sa) = InMemoryConnection.CreatePair();
        (IConnection b, IConnection sb) = InMemoryConnection.CreatePair();
        host.Accept(sa, 0, "Ala", 0, host: true);
        host.Accept(sb, 1, "Ola");
        a.Send(new HelloMessage(ProtocolInfo.Version, "Ala"));
        b.Send(new HelloMessage(ProtocolInfo.Version, "Ola"));
        host.RunTicks(3);
        IConnection eye = host.Connect();
        eye.Send(new HelloMessage(ProtocolInfo.Version, "Eye", ClientRole.Observer));
        host.RunTicks(2);
        Drain(a);
        Drain(b);
        Drain(eye);
        return (host, a, b, eye);
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
