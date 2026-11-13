using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;
using CivSim.Core.WorldGen;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>
/// Player commands for the levers that had none (stage 6 PR P1, known issue 14): research, war, treaties, campaigns, government,
/// settling down; and their decisions hold against the AI.
/// </summary>
public class PlayerCommandTests
{
    private static readonly GameContent Content = TestContent.Load();
    private static readonly PlayerId Player = new(1);

    [Fact]
    public void EveryWireCommandOfTheLeversMapsToTheAisCommand()
    {
        Assert.IsType<CivSim.Core.Research.SetResearchCommand>(CommandMapper.ToCore(new SetResearchDto("fire"), Content));
        Assert.IsType<DeclareWarCommand>(CommandMapper.ToCore(new DeclareWarDto(1, Goal: 1), Content));
        Assert.IsType<ProposeTreatyCommand>(CommandMapper.ToCore(new ProposeTreatyDto(1, 2), Content));
        Assert.IsType<AnswerProposalCommand>(CommandMapper.ToCore(new AnswerProposalDto(1, 2, true), Content));
        Assert.IsType<CivSim.Core.Episodes.LaunchCampaignCommand>(CommandMapper.ToCore(new LaunchCampaignDto(0, 1, 10), Content));
        Assert.IsType<CivSim.Core.Governance.ChangeGovernmentCommand>(CommandMapper.ToCore(new ChangeGovernmentDto("chiefdom"), Content));
        Assert.IsType<CivSim.Core.Economy.SettleCommand>(CommandMapper.ToCore(new SettleDto(0), Content));

        // Kinds out of range are refused, never guessed.
        Assert.Null(CommandMapper.ToCore(new ProposeTreatyDto(1, 0), Content)); // war is declared, not proposed
        Assert.Null(CommandMapper.ToCore(new DeclareWarDto(1, Goal: 7), Content));
        Assert.False(CommandMapper.ToCore(new SetResearchDto("time_travel"), Content)!.Validate(
            GameState.CreateNew(GameSettings.Test(71), Content, out WorldGenReport _), new CivId(0)).Success);
    }

    [Fact]
    public void TheAiDoesNotUndoThePlayersDiplomacyWhileItHolds()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(72), Content, out WorldGenReport _);
        CivilizationState civ = state.Civilizations[0];
        CivId other = state.Civilizations[1].Id;
        civ.Controller = Player;
        civ.Relations.Add(new Relation(other) { Level = ContactLevel.Met });
        state.Civilizations[1].Relations.Add(new Relation(civ.Id) { Level = ContactLevel.Met });
        var peace = new ProposeTreatyCommand(Content, other, TreatyKind.Peace);
        var war = new DeclareWarCommand(Content, other, WarGoal.Loot);
        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, peace));

        // The player declared war (applied through the turn, so it is marked as the player's).
        var runner = new TurnRunner(state);
        runner.Commands.Enqueue(Player, civ.Id, war);
        runner.RunTurn();
        Assert.True(PlayerOrders.HeldAgainstAi(state, civ.Id, peace));
        Assert.True(PlayerOrders.HeldAgainstAi(state, civ.Id, new AnswerProposalCommand(Content, other, TreatyKind.Peace, true)));
        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, new ProposeTreatyCommand(Content, state.Civilizations[2].Id, TreatyKind.Trade)));

        // The AI's own war, by contrast, leaves the AI free.
        Relation r = civ.Relations.Single(x => x.Other == other);
        r.PlayerOrderTick = -1;
        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, peace));
    }

    [Fact]
    public void APlayersResearchStaysUntilLearnedAndTheViewShowsTheOptions()
    {
        var server = GameServer.CreateNew(GameSettings.Test(73), Content);
        (InMemoryConnection client, InMemoryConnection serverSide) = InMemoryConnection.CreatePair();
        server.Accept(serverSide);
        client.Send(new HelloMessage(ProtocolInfo.Version, "Patryk", ClientRole.Player, CivId: 0));
        server.Pump();
        GameViewMessage first = Drain(client).OfType<GameViewMessage>().Last();
        Assert.NotNull(first.Research);
        Assert.NotEmpty(first.Research!.Options);
        Assert.Equal(first.Research.Options.OrderBy(o => o.Cost).ThenBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key), first.Research.Options.Select(o => o.Key));

        // The player picks the most expensive option; the scholar of the AI (lever 2 not taken) does not change it.
        string pick = first.Research.Options[^1].Key;
        client.Send(new SubmitCommandMessage(0, 1, new SetResearchDto(pick)));
        server.Pump();
        int tech = Content.Technology(pick).Id;
        CivilizationState civ = server.State.Civilizations[0];
        for (int t = 0; t < 60 && !civ.Knows(tech); t++)
        {
            server.AdvanceTurn();
            Assert.True(civ.Researching == tech || civ.Knows(tech), $"tick {server.State.Turn}: research changed to {civ.Researching}");
        }

        GameViewMessage last = Drain(client).OfType<GameViewMessage>().Last();
        Assert.NotNull(last.Proposals);
    }

    [Fact]
    public void ScoutsSentSomewhereComeBackWithWhatTheySaw()
    {
        GameContent live = CivSim.Data.ContentLoader.Load();
        GameState state = GameState.CreateNew(GameSettings.Test(74), Content, out WorldGenReport _);
        CivilizationState civ = state.Civilizations[0];
        Array.Fill(civ.SeenYear, (int)(state.Month / 12) + 1);
        SettlementState home = state.Settlements.First(s => s.Owner == civ.Id);
        WorldState world = state.Worlds[0];
        Unit scouts = state.Units.Add(Content.Units.First(UnitRole.Scouts), civ.Id, world.Id, world.Geometry.Index(home.Cell), home.Id.Value,
            UnitOrder.Scout, Content.Strata.Count, Content.Skills.Count, Content.Goods.Count);
        scouts.People[0] = 3;
        home.ScoutOut = true;
        HexCoord there = Enumerable.Range(2, 6).SelectMany(d => Ring(world, home.Cell, d))
            .First(c => CivSim.Core.Perception.MapMemory.Walkable(Content, world.Map, world.Geometry.Index(c))
                && new MoveUnitCommand(Content, scouts.Id, c).Validate(state, civ.Id).Success);
        new MoveUnitCommand(Content, scouts.Id, there).Apply(state, civ.Id);
        var runner = new TurnRunner(state);
        runner.AddSystem(new UnitSystem(Content));
        bool reached = false;
        for (int t = 0; t < 400 && state.Units.All.Any(u => u.Id == scouts.Id && !u.Gone); t++)
        {
            runner.RunTurn();
            reached |= state.Units.All.Any(u => u.Id == scouts.Id && u.Cell == world.Geometry.Index(there));
        }

        Assert.True(reached);
        Assert.False(home.ScoutOut); // home again: the settlement sends its next party later
        GC.KeepAlive(live);
    }

    private static IEnumerable<HexCoord> Ring(WorldState world, HexCoord center, int d)
    {
        var ring = new HexCoord[6 * d];
        int n = world.Geometry.Ring(center, d, ring);
        return ring.Take(n);
    }

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
