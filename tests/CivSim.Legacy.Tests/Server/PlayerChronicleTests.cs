using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Server;

/// <summary>
/// Playtest P-01 (2026-11-16): a player reads only the chronicle its people may know (CLAUDE.md rule 10), and leaders stay
/// out of the chronicle until the bronze age (ADR 0024, 0026).
/// </summary>
public class PlayerChronicleTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static List<string> Lines(InMemoryConnection connection)
    {
        var lines = new List<string>();
        while (connection.TryReceive(out IProtocolMessage? m))
        {
            if (m is GameViewMessage { Chronicle: { } chronicle })
            {
                lines.AddRange(chronicle.Select(e => e.Text));
            }
        }

        return lines;
    }

    [Fact]
    public void APlayerReadsNothingAboutAPeopleItHasNotMet()
    {
        GameServer server = GameServer.CreateNew(GameSettings.Test(5), Content);
        (InMemoryConnection player, InMemoryConnection playerEnd) = InMemoryConnection.CreatePair();
        (InMemoryConnection observer, InMemoryConnection observerEnd) = InMemoryConnection.CreatePair();
        server.Accept(playerEnd);
        server.Accept(observerEnd);
        player.Send(new HelloMessage(ProtocolInfo.Version, "Patryk", ClientRole.Player, 0));
        observer.Send(new HelloMessage(ProtocolInfo.Version, "Viewer", ClientRole.Observer));
        server.Pump();
        Lines(player);
        Lines(observer);
        CivilizationState us = server.State.Civilizations[0];
        Assert.Null(us.Relation(new CivId(1))); // the test needs a people we have not met

        server.AddChronicle("About us.", new ChronicleAbout(0));
        server.AddChronicle("About them.", new ChronicleAbout(1));
        server.AddChronicle("Us and them.", new ChronicleAbout(0, 1));
        server.AddChronicle("The whole world.");
        server.SendViews();

        List<string> seen = Lines(player);
        Assert.Contains("About us.", seen);
        Assert.Contains("The whole world.", seen);
        Assert.DoesNotContain("About them.", seen);
        Assert.DoesNotContain("Us and them.", seen); // it would tell us their name
        Assert.Contains("About them.", Lines(observer)); // the observer reads everything

        Contacts.Meet(Content, server.State, us, new CivId(1), ContactWay.Border);
        server.AddChronicle("About them, now met.", new ChronicleAbout(1));
        server.SendViews();
        Assert.Contains("About them, now met.", Lines(player));
    }

    [Fact]
    public void ASummaryKeepsOnlyThePeoplesTheReaderMet()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(5), Content, out _);
        string[] names = [.. state.Civilizations.Take(3).Select(c => c.Name)];
        var group = new ChronicleGroup("{0} groups set out", [
            new ChronicleGroupMember(0, 2, $"Settlers left {names[0]}."),
            new ChronicleGroupMember(1, 1, $"Settlers left {names[1]}."),
            new ChronicleGroupMember(2, 1, $"Settlers left {names[2]}."),
        ]);

        Assert.Equal($"4 groups set out: {names[0]} (2), {names[1]}, {names[2]}.", group.Text(state, _ => true));
        Assert.Equal($"3 groups set out: {names[0]} (2), {names[2]}.", group.Text(state, c => c != 1));
        Assert.Equal($"Settlers left {names[1]}.", group.Text(state, c => c == 1)); // one line left: its own text
        Assert.Null(group.Text(state, _ => false));
    }

    [Fact]
    public void LeadersStayOutOfTheChronicleUntilTheBronzeAge()
    {
        GameContent frozen = CivSim.Data.ContentLoader.Load().WithTime(TestContent.EveryTick); // TestContent is thawed
        Assert.False(frozen.Leaders.ChronicleEnabled);
        Assert.True(Content.Leaders.ChronicleEnabled);
        Assert.Equal(0, LeaderLines(frozen));
        Assert.True(LeaderLines(Content) > 0); // the leaders still die and follow one another
    }

    private static int LeaderLines(GameContent content)
    {
        GameServer server = GameServer.CreateNew(GameSettings.Test(5), content);
        int lines = 0;
        for (int t = 0; t < 600; t++)
        {
            server.AdvanceTurn(sendViews: false);
            lines += server.Runner.LastChronicle.Count(l => l.Contains("leads them now", System.StringComparison.Ordinal)
                || l.StartsWith("New leaders", System.StringComparison.Ordinal));
        }

        return lines;
    }
}
