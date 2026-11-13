using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Protocol;
using CivSim.Protocol.Client;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Protocol;

/// <summary>The orders the client offers to a player (stage 6 PR P2) become commands the server accepts.</summary>
public class HelmActionsTests
{
    private static readonly GameContent Content = TestContent.Load();

    [Fact]
    public void ASelectedCampOffersOrdersOfItsLeversAndUnitsTheirs()
    {
        (GameServer server, InMemoryConnection client) = Connect(81);
        List<IProtocolMessage> first = Drain(client);
        GameViewMessage view = first.OfType<GameViewMessage>().Last();
        ContentMessage content = first.OfType<ContentMessage>().Single();
        SettlementView camp = view.Settlements!.First(s => s.Owner == 0 && s.Status == 0);
        HelmSelection selection = HelmActions.Select(view, 0, camp.World, camp.Col, camp.Row);
        Assert.Equal(camp.Id, selection.Settlement!.Id);
        IReadOnlyList<HelmAction> actions = HelmActions.For(selection, content);
        Assert.Contains(actions, a => a.Key == "move" && a.Lever == 1 && a.Target == HelmTarget.Cell);
        Assert.Contains(actions, a => a.Key == "raid" && a.Lever == 4 && a.Target == HelmTarget.Settlement);
        Assert.Contains(actions, a => a.Key.StartsWith("task:", System.StringComparison.Ordinal) && a.Lever == 3);
        Assert.All(actions, a => Assert.InRange(a.Lever, 1, 5));

        // Another civilization's camp is not ours to order.
        SettlementView foreign = view.Settlements!.FirstOrDefault(s => s.Owner != 0) ?? camp;
        if (foreign.Owner != 0)
        {
            Assert.True(HelmActions.Select(view, 0, foreign.World, foreign.Col, foreign.Row).IsEmpty);
        }

        // Settlers: go, found, return; an army: attack too.
        var settlers = new UnitView(5, 0, 0, 0, 1, 1, 1, 1, 0, 6, 0) { Role = 0 };
        var army = new UnitView(6, 0, 0, 0, 1, 1, 1, 1, 0, 6, 0) { Role = 2 };
        Assert.Equal(["go", "found", "return"], HelmActions.For(new HelmSelection(null, settlers), null).Select(a => a.Key));
        Assert.Equal(["go", "attack", "return"], HelmActions.For(new HelmSelection(null, army), null).Select(a => a.Key));
        Assert.IsType<FoundCampDto>(HelmActions.Build(HelmActions.For(new HelmSelection(null, settlers), null)[1], new HelmSelection(null, settlers)));
        Assert.Null(HelmActions.Build(HelmActions.For(new HelmSelection(null, army), null)[1], new HelmSelection(null, army))); // no target yet
        System.GC.KeepAlive(server);
    }

    [Fact]
    public void AMoveFromTheOrderPanelIsAcceptedAndHeldAsThePlayers()
    {
        (GameServer server, InMemoryConnection client) = Connect(82);
        GameViewMessage view = Latest(client)!;
        SettlementView camp = view.Settlements!.First(s => s.Owner == 0 && s.Status == 0);
        HelmSelection selection = HelmActions.Select(view, 0, camp.World, camp.Col, camp.Row);
        HelmAction move = HelmActions.For(selection, null).Single(a => a.Key == "move");

        // The player clicks a cell a few steps away; the first one the server accepts.
        WorldState world = server.State.Worlds[0];
        MoveSettlementDto? sent = null;
        foreach ((int col, int row) in Cells(world, new CivSim.Core.Map.HexCoord(camp.Col, camp.Row), 5))
        {
            var dto = (MoveSettlementDto)HelmActions.Build(move, selection, col, row)!;
            if (CommandMapper.ToCore(dto, Content)!.Validate(server.State, new CivId(0)).Success)
            {
                sent = dto;
                break;
            }
        }

        Assert.NotNull(sent);
        client.Send(new SubmitCommandMessage(0, 1, sent!));
        server.Pump();
        server.AdvanceTurn();
        Assert.DoesNotContain(Drain(client), m => m is CommandRejectedMessage);
        SettlementState s = server.State.Settlements.First(x => x.Id.Value == camp.Id);
        Assert.True(s.PlayerOrderTick >= 0); // the order is the player's: the AI keeps its hands off this camp for a while
    }

    [Fact]
    public void LeversAndTheLastAiDecisionAreReadFromTheView()
    {
        var view = new GameViewMessage(10, 0, [], [], PlayerLevers: 0b00110, AiDecisions:
        [
            new AiDecisionView(3, 1, "moved the camp"),
            new AiDecisionView(7, 1, "sent scouts"),
            new AiDecisionView(5, 4, "proposed peace"),
        ]);
        Assert.False(HelmActions.Holds(view.PlayerLevers, 1));
        Assert.True(HelmActions.Holds(view.PlayerLevers, 2));
        Assert.True(HelmActions.Holds(view.PlayerLevers, 3));
        Assert.False(HelmActions.Holds(view.PlayerLevers, 6));
        Assert.Equal("sent scouts", HelmActions.LastAiDecision(view, 1)!.Text);
        Assert.Null(HelmActions.LastAiDecision(view, 2));
        Assert.Equal(5, HelmActions.LeverNames.Count);
    }

    private static (GameServer Server, InMemoryConnection Client) Connect(ulong seed)
    {
        var server = GameServer.CreateNew(GameSettings.Test(seed), Content);
        (InMemoryConnection client, InMemoryConnection serverSide) = InMemoryConnection.CreatePair();
        server.Accept(serverSide);
        client.Send(new HelloMessage(ProtocolInfo.Version, "Patryk", ClientRole.Player, CivId: 0));
        server.Pump();
        return (server, client);
    }

    private static IEnumerable<(int Col, int Row)> Cells(WorldState world, CivSim.Core.Map.HexCoord center, int max)
    {
        var ring = new CivSim.Core.Map.HexCoord[6 * max];
        for (int d = 1; d <= max; d++)
        {
            int n = world.Geometry.Ring(center, d, ring);
            for (int i = 0; i < n; i++)
            {
                yield return (ring[i].Col, ring[i].Row);
            }
        }
    }

    private static GameViewMessage? Latest(InMemoryConnection c) => Drain(c).OfType<GameViewMessage>().LastOrDefault();

    private static List<IProtocolMessage> Drain(InMemoryConnection c)
    {
        var list = new List<IProtocolMessage>();
        while (c.TryReceive(out IProtocolMessage? m))
        {
            list.Add(m!);
        }

        return list;
    }
}
