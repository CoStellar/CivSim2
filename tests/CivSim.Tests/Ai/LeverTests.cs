using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>The five levers of the helmsman (ADR 0027, stage 6 PR P): the AI decides every lever a player has not taken.</summary>
public class LeverTests
{
    private static readonly GameContent Content = TestContent.Load();

    private const int Ticks = 240;

    // A whole game with every core system and the AI council; the first civilization led by a player holding <paramref name="levers"/>.
    private static (GameState State, List<Decision> Decisions) Play(ulong seed, bool player, Lever levers)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out WorldGenReport _);
        if (player)
        {
            state.Civilizations[0].Controller = new PlayerId(1);
            state.Civilizations[0].PlayerLevers = levers;
        }

        var runner = new TurnRunner(state);
        AiCouncil council = CoreSystems.Register(runner, Content);
        var decisions = new List<Decision>();
        for (int t = 0; t < Ticks; t++)
        {
            runner.RunTurn();
            decisions.AddRange(council.Log.Entries);
        }

        return (state, decisions);
    }

    [Fact]
    public void EveryAiActionHasItsLever()
    {
        foreach (AiAction action in Enum.GetValues<AiAction>())
        {
            Lever lever = Levers.Of(action);
            Assert.True(action == AiAction.SetRecipe ? lever == Lever.None : Levers.Number(lever) is >= 1 and <= 5, $"{action} → {lever}");
        }

        Assert.Equal(Lever.WhereWeLive, Levers.Of(AiAction.SendSettlers));
        Assert.Equal(Lever.WhereWeHead, Levers.Of(AiAction.SetResearch));
        Assert.Equal(Lever.WithWhom, Levers.Of(AiAction.DeclareWar));
        Assert.Equal(Lever.Cards, Levers.Of(AiAction.ChangeGovernment));
    }

    [Fact]
    public void APlayerWhoTakesNoLeverGetsTheGameTheAiWouldPlay()
    {
        // Test of the helmsman, point 5 (GDD 1.2): a game without clicking is the same game.
        (GameState ai, _) = Play(51, player: false, Lever.None);
        (GameState led, List<Decision> decisions) = Play(51, player: true, Lever.None);
        Assert.Equal(GameStateHash.Compute(ai), GameStateHash.Compute(led));
        Assert.Contains(decisions, d => d.Civ == led.Civilizations[0].Id); // the AI does decide for the player's civilization
    }

    [Theory]
    [InlineData(Lever.WhereWeLive, Lever.WhereWeHead)]
    [InlineData(Lever.WhereWeHead, Lever.WhereWeLive)]
    public void TheAiKeepsOffTheLeverThePlayerHoldsAndDecidesTheOthers(Lever held, Lever free)
    {
        (_, List<Decision> none) = Play(52, player: true, Lever.None);
        (GameState state, List<Decision> decisions) = Play(52, player: true, held);
        CivId first = state.Civilizations[0].Id;

        Assert.Contains(none, d => d.Civ == first && Levers.Of(d.Action) == held); // the AI would decide it
        Assert.DoesNotContain(decisions, d => d.Civ == first && Levers.Of(d.Action) == held);
        Assert.Contains(decisions, d => d.Civ == first && Levers.Of(d.Action) == free);
        Assert.Contains(decisions, d => d.Civ != first && Levers.Of(d.Action) == held); // other civilizations go on as before
    }

    [Fact]
    public void TheLeverCommandIsForPlayersOnly()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(53), Content, out WorldGenReport _);
        CivilizationState civ = state.Civilizations[0];
        var take = new SetLeverCommand(Lever.OnTheLand, PlayerTakes: true);
        Assert.False(take.Validate(state, civ.Id).Success); // an AI civilization keeps all its levers
        civ.Controller = new PlayerId(1);
        Assert.True(take.Validate(state, civ.Id).Success);
        Assert.False(new SetLeverCommand(Lever.None, true).Validate(state, civ.Id).Success);
        take.Apply(state, civ.Id);
        new SetLeverCommand(Lever.Cards, true).Apply(state, civ.Id);
        Assert.Equal(Lever.OnTheLand | Lever.Cards, civ.PlayerLevers);
        Assert.False(Levers.AiLeads(civ, Lever.Cards));
        Assert.True(Levers.AiLeads(civ, Lever.WithWhom));
        new SetLeverCommand(Lever.Cards, false).Apply(state, civ.Id);
        Assert.Equal(Lever.OnTheLand, civ.PlayerLevers);

        // An AI civilization ignores levers left in its state (a player who left).
        civ.Controller = PlayerId.Ai;
        Assert.True(Levers.AiLeads(civ, Lever.OnTheLand));
    }

    [Fact]
    public void LeversAreSavedAndHashed()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(54), Content, out WorldGenReport _);
        ulong before = GameStateHash.Compute(state);
        state.Civilizations[0].PlayerLevers = Lever.WithWhom;
        Assert.NotEqual(before, GameStateHash.Compute(state));
        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(Lever.WithWhom, loaded.Civilizations[0].PlayerLevers);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
    }

    [Fact]
    public void APlayerChoosesACivilizationAndSeesWhatTheAiDidForIt()
    {
        var server = GameServer.CreateNew(GameSettings.Test(55), Content);
        (InMemoryConnection client, InMemoryConnection serverSide) = InMemoryConnection.CreatePair();
        server.Accept(serverSide);
        client.Send(new HelloMessage(ProtocolInfo.Version, "Patryk", ClientRole.Player, CivId: 2));
        server.Pump();
        List<IProtocolMessage> got = Drain(client);
        WelcomeMessage welcome = Assert.Single(got.OfType<WelcomeMessage>());
        Assert.Equal(2, welcome.CivId);

        // Someone else asking for the same civilization is refused.
        (InMemoryConnection other, InMemoryConnection otherSide) = InMemoryConnection.CreatePair();
        server.Accept(otherSide);
        other.Send(new HelloMessage(ProtocolInfo.Version, "Ola", ClientRole.Player, CivId: 2));
        server.Pump();
        Assert.Contains(Drain(other), m => m is ConnectionRejectedMessage);

        // The player takes lever 2; the view shows it, and what the AI did on the others.
        client.Send(new SubmitCommandMessage(2, 1, new SetLeverDto(LeverFlags.WhereWeHead, Take: true)));
        server.Pump();
        GameViewMessage? last = null;
        for (int t = 0; t < 120; t++)
        {
            server.AdvanceTurn();
            last = Drain(client).OfType<GameViewMessage>().LastOrDefault() ?? last;
        }

        Assert.NotNull(last);
        Assert.Equal(LeverFlags.WhereWeHead, last!.PlayerLevers);
        Assert.NotNull(last.AiDecisions);
        Assert.NotEmpty(last.AiDecisions!);
        Assert.DoesNotContain(last.AiDecisions!, d => d.Lever == 2 && d.Turn > 2); // from the tick after the lever was taken
        Assert.All(last.AiDecisions!, d => Assert.False(string.IsNullOrWhiteSpace(d.Text)));
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
