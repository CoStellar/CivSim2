using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Events;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Research;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Diplomacy;

/// <summary>Treaties, war, the diplomat and trade between civilizations (GDD 9.1-9.2; stage 4d).</summary>
public class TreatyTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    // A fresh test world without the starting camps; its land cells; civilizations by index.
    private static (GameState State, HexCoord[] Land) Empty(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        state.RemoveSettlements(_ => true);
        WorldState world = state.Worlds[0];
        var land = new List<HexCoord>();
        for (int i = 0; i < world.Geometry.CellCount; i++)
        {
            if (!Content.Biomes[world.Map.Biome[i]].IsWater)
            {
                land.Add(world.Geometry.FromIndex(i));
            }
        }

        return (state, land.ToArray());
    }

    private static SettlementState Camp(GameState state, CivilizationState civ, HexCoord cell, int people)
    {
        SettlementState s = state.FoundSettlement(civ.Id, state.Worlds[0].Id, cell, civ.Name + " Camp", Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Content.Strata[0].Id] = people;
        return s;
    }

    private static void MeetAll(GameState state)
    {
        foreach (CivilizationState a in state.Civilizations)
        {
            foreach (CivilizationState b in state.Civilizations)
            {
                if (a != b)
                {
                    Contacts.Meet(Content, state, a, b.Id, ContactWay.Sighted);
                }
            }
        }
    }

    private static CivilizationState Civ(GameState state, int index) => state.Civilizations[index];

    private static string? Run(GameState state, CivId civ, ICommand command)
    {
        CommandResult result = command.Validate(state, civ);
        Assert.True(result.Success, result.Reason);
        return command is IChronicledCommand c ? c.ApplyWithChronicle(state, civ) : Apply(state, civ, command);
    }

    private static string? Apply(GameState state, CivId civ, ICommand command)
    {
        command.Apply(state, civ);
        return null;
    }

    private static int Good(string key) => Content.Goods.ToList().FindIndex(g => g.Key == key);

    [Fact]
    public void DiplomacyNeedsRealContactOnBothSides()
    {
        (GameState state, _) = Empty(41);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        var propose = new ProposeTreatyCommand(Content, b.Id, TreatyKind.Trade);
        Assert.False(propose.Validate(state, a.Id).Success);

        Contacts.Meet(Content, state, a, b.Id, ContactWay.Sighted);
        Assert.False(propose.Validate(state, a.Id).Success, "they have not met us yet");

        Contacts.Meet(Content, state, b, a.Id, ContactWay.Sighted);
        Assert.True(propose.Validate(state, a.Id).Success);
        Assert.False(new ProposeTreatyCommand(Content, b.Id, TreatyKind.War).Validate(state, a.Id).Success, "war is declared, not proposed");
    }

    [Fact]
    public void TheOtherSideAcceptsOrDeclinesAndADeclineIsRemembered()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        Run(state, a.Id, new ProposeTreatyCommand(Content, b.Id, TreatyKind.Trade));
        Assert.Equal(ProposalStatus.Pending, Assert.Single(state.Treaties.Proposals).Status);
        Assert.False(new ProposeTreatyCommand(Content, b.Id, TreatyKind.Trade).Validate(state, a.Id).Success, "already waiting");
        Assert.False(new ProposeTreatyCommand(Content, a.Id, TreatyKind.Trade).Validate(state, b.Id).Success, "answer instead");

        string? line = Run(state, b.Id, new AnswerProposalCommand(Content, a.Id, TreatyKind.Trade, Accept: true));
        Assert.True(state.Treaties.Has(a.Id, b.Id, TreatyKind.Trade));
        Assert.Empty(state.Treaties.Proposals);
        Assert.Equal($"{a.Name} and {b.Name} agreed to trade.", line);

        Run(state, a.Id, new ProposeTreatyCommand(Content, b.Id, TreatyKind.NonAggression));
        Assert.Null(Run(state, b.Id, new AnswerProposalCommand(Content, a.Id, TreatyKind.NonAggression, Accept: false)));
        Assert.False(state.Treaties.Has(a.Id, b.Id, TreatyKind.NonAggression));
        Assert.Equal(ProposalStatus.Declined, Assert.Single(state.Treaties.Proposals).Status);
        Assert.False(new ProposeTreatyCommand(Content, b.Id, TreatyKind.NonAggression).Validate(state, a.Id).Success, "declined not long ago");
        Assert.False(new AnswerProposalCommand(Content, a.Id, TreatyKind.NonAggression, true).Validate(state, b.Id).Success, "nothing waits");
    }

    [Fact]
    public void AnUnansweredProposalLapsesAndARefusalIsForgottenAfterSomeYears()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        Run(state, a.Id, new ProposeTreatyCommand(Content, b.Id, TreatyKind.Trade));
        var runner = new TurnRunner(state);
        runner.AddSystem(new DiplomacySystem(Content));
        runner.AddSystem(new CalendarSystem(Content));

        runner.RunTurn();
        Assert.Equal(ProposalStatus.Pending, state.Treaties.Proposals[0].Status);
        runner.RunTurn();
        Assert.Equal(ProposalStatus.Declined, state.Treaties.Proposals[0].Status);

        while (state.Treaties.Proposals.Count > 0 && state.Month < 12L * 1000)
        {
            runner.RunTurn();
        }

        Assert.Empty(state.Treaties.Proposals);
        Assert.True(new ProposeTreatyCommand(Content, b.Id, TreatyKind.Trade).Validate(state, a.Id).Success);
    }

    [Fact]
    public void AnAllianceNeedsAPactFirst()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        Assert.False(new ProposeTreatyCommand(Content, b.Id, TreatyKind.DefensiveAlliance).Validate(state, a.Id).Success);
        Treaties.Sign(Content, state, a.Id, b.Id, TreatyKind.NonAggression);
        Assert.True(new ProposeTreatyCommand(Content, b.Id, TreatyKind.DefensiveAlliance).Validate(state, a.Id).Success);
    }

    [Fact]
    public void WarEndsTreatiesAndBreakingAPactCostsTrustWithTheVictimAndWitnesses()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        CivilizationState witness = Civ(state, 2);
        Treaties.Sign(Content, state, a.Id, b.Id, TreatyKind.Trade);
        Treaties.Sign(Content, state, a.Id, b.Id, TreatyKind.NonAggression);
        int trustBefore = b.Relation(a.Id)!.Trust;

        string? line = Run(state, a.Id, new DeclareWarCommand(Content, b.Id));

        Assert.Equal($"{a.Name} declared war on {b.Name}, breaking their treaty.", line);
        Assert.True(state.Treaties.AtWar(a.Id, b.Id));
        Assert.False(state.Treaties.Has(a.Id, b.Id, TreatyKind.Trade));
        Assert.False(state.Treaties.Has(a.Id, b.Id, TreatyKind.NonAggression));
        RelationEventDefinition broken = Content.Relations.Events[(int)RelationEvent.BrokenTreaty];
        Assert.Equal(trustBefore + broken.Trust, b.Relation(a.Id)!.Trust);
        RelationMemory seen = Assert.Single(witness.Relation(a.Id)!.Memories, m => m.Kind == RelationEvent.BrokenTreaty);
        Assert.Equal(broken.Trust * Content.Treaties.WitnessPercent / 100, seen.Trust);
        Assert.DoesNotContain(a.Relation(b.Id)!.Memories, m => m.Kind == RelationEvent.BrokenTreaty);
        Assert.False(new DeclareWarCommand(Content, b.Id).Validate(state, a.Id).Success, "already at war");
    }

    [Fact]
    public void AWarWithoutTreatiesBreaksNothing()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        Treaties.Sign(Content, state, a.Id, b.Id, TreatyKind.Trade);
        Assert.Equal($"{a.Name} declared war on {b.Name}.", Run(state, a.Id, new DeclareWarCommand(Content, b.Id)));
        Assert.DoesNotContain(b.Relation(a.Id)!.Memories, m => m.Kind == RelationEvent.BrokenTreaty);
    }

    [Fact]
    public void PeaceEndsTheWarWithATruceThatRunsOut()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        Run(state, a.Id, new DeclareWarCommand(Content, b.Id));
        Assert.False(new ProposeTreatyCommand(Content, b.Id, TreatyKind.Trade).Validate(state, a.Id).Success, "make peace first");
        Run(state, b.Id, new ProposeTreatyCommand(Content, a.Id, TreatyKind.Peace));
        string? line = Run(state, a.Id, new AnswerProposalCommand(Content, b.Id, TreatyKind.Peace, Accept: true));

        Assert.StartsWith($"{b.Name} and {a.Name} made peace after", line, System.StringComparison.Ordinal);
        Assert.False(state.Treaties.AtWar(a.Id, b.Id));
        Treaty truce = state.Treaties.Find(a.Id, b.Id, TreatyKind.Peace)!;
        Assert.Equal(state.Month + (Content.Treaties.Kinds[(int)TreatyKind.Peace].Years * 12L), truce.UntilMonth);
        Assert.True(state.Treaties.ForbidsAttack(a.Id, b.Id));

        var runner = new TurnRunner(state);
        runner.AddSystem(new DiplomacySystem(Content));
        runner.AddSystem(new CalendarSystem(Content));
        while (state.Month < truce.UntilMonth)
        {
            runner.RunTurn();
        }

        runner.RunTurn();
        Assert.False(state.Treaties.Has(a.Id, b.Id, TreatyKind.Peace));
    }

    [Fact]
    public void ALoyalAllyJoinsADefensiveWar()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState aggressor = Civ(state, 0);
        CivilizationState victim = Civ(state, 1);
        CivilizationState ally = Civ(state, 2);
        ally.Traits = Enumerable.Repeat(500, Character.TraitCount).ToArray();
        ally.Traits[(int)Trait.Loyalty] = 1000;
        Treaties.Sign(Content, state, victim.Id, ally.Id, TreatyKind.NonAggression);
        Treaties.Sign(Content, state, victim.Id, ally.Id, TreatyKind.DefensiveAlliance);

        string? line = Run(state, aggressor.Id, new DeclareWarCommand(Content, victim.Id));

        Assert.Contains($"{ally.Name} joined the war at the side of its ally.", line, System.StringComparison.Ordinal);
        Assert.True(state.Treaties.AtWar(ally.Id, aggressor.Id));
        Assert.Contains(victim.Relation(ally.Id)!.Memories, m => m.Kind == RelationEvent.WarHelp);
    }

    [Fact]
    public void AnAllyThatStaysOutBreaksTheAlliance()
    {
        GameContent content = WithData("\"allyJoinBasePerMille\": 300", "\"allyJoinBasePerMille\": 0");
        GameState state = GameState.CreateNew(GameSettings.Test(41), content, out _);
        foreach (CivilizationState x in state.Civilizations)
        {
            foreach (CivilizationState y in state.Civilizations)
            {
                if (x != y)
                {
                    Contacts.Meet(content, state, x, y.Id, ContactWay.Sighted);
                }
            }
        }

        CivilizationState aggressor = Civ(state, 0);
        CivilizationState victim = Civ(state, 1);
        CivilizationState ally = Civ(state, 2);
        ally.Traits = new int[Character.TraitCount];
        Treaties.Sign(content, state, victim.Id, ally.Id, TreatyKind.NonAggression);
        Treaties.Sign(content, state, victim.Id, ally.Id, TreatyKind.DefensiveAlliance);

        string line = Treaties.DeclareWar(content, state, aggressor.Id, victim.Id);

        Assert.Contains($"{ally.Name} let its ally down.", line, System.StringComparison.Ordinal);
        Assert.False(state.Treaties.AtWar(ally.Id, aggressor.Id));
        Assert.False(state.Treaties.Has(victim.Id, ally.Id, TreatyKind.DefensiveAlliance));
        Assert.Contains(victim.Relation(ally.Id)!.Memories, m => m.Kind == RelationEvent.BrokenTreaty);
    }

    [Fact]
    public void APactForbidsRaidsUntilWarIsDeclared()
    {
        (GameState state, HexCoord[] land) = Empty(42);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        HexGlobe geo = state.Worlds[0].Geometry;
        HexCoord here = land[land.Length / 2];
        HexCoord there = land.First(c => geo.Distance(here, c) == 2);
        SettlementState raiders = Camp(state, a, here, 100);
        raiders.Population[Content.Strata.First(s => s.Fights).Id] = 50;
        SettlementState victims = Camp(state, b, there, 20);
        var raid = new ConquerSettlementCommand(Content, raiders.Id, victims.Id);
        Assert.True(raid.Validate(state, a.Id).Success);

        Treaties.Sign(Content, state, a.Id, b.Id, TreatyKind.NonAggression);
        Assert.False(raid.Validate(state, a.Id).Success);

        Treaties.DeclareWar(Content, state, a.Id, b.Id);
        Assert.True(raid.Validate(state, a.Id).Success);
    }

    // Two adjacent gateways: one rich in food without tools, the other rich in tools without food.
    private static (GameState State, SettlementState Food, SettlementState Tools) Partners()
    {
        (GameState state, HexCoord[] land) = Empty(43);
        MeetAll(state);
        HexGlobe geo = state.Worlds[0].Geometry;
        HexCoord here = land[land.Length / 2];
        HexCoord there = land.First(c => geo.Distance(here, c) == 1);
        SettlementState food = Camp(state, Civ(state, 0), here, 100);
        SettlementState tools = Camp(state, Civ(state, 1), there, 100);
        food.Add(Good("meat"), Amount.FromUnits(20_000));
        tools.Add(Good("stone_tools"), Amount.FromUnits(2_000));
        Treaties.Sign(Content, state, Civ(state, 0).Id, Civ(state, 1).Id, TreatyKind.Trade);
        return (state, food, tools);
    }

    [Fact]
    public void TradePartnersBarterWhatEachLacks()
    {
        (GameState state, SettlementState food, SettlementState tools) = Partners();
        var trade = new ForeignTradeSystem(Content);
        var runner = new TurnRunner(state);
        runner.AddSystem(trade);

        runner.RunTurn();

        Assert.True(tools.StockView[Good("meat")].Hundredths > 0, "food went to the hungry side");
        Assert.True(food.StockView[Good("stone_tools")].Hundredths > 0, "tools went to the side without them");
        Treaty t = state.Treaties.Find(food.Owner, tools.Owner, TreatyKind.Trade)!;
        Assert.True(t.LastWorth > 0);
        Assert.Equal(t.LastWorth, trade.LastWorth);
        Assert.Equal(0, t.LastFlowMonth);
        Assert.True(state.FindCivilization(food.Owner)!.Relation(tools.Owner)!.FindOngoing(RelationEvent.ActiveTrade) >= 0);
        Assert.True(state.FindCivilization(tools.Owner)!.Relation(food.Owner)!.FindOngoing(RelationEvent.ActiveTrade) >= 0);
    }

    [Fact]
    public void NoTradeWithoutAnAgreementOrBeyondReach()
    {
        (GameState state, SettlementState food, SettlementState tools) = Partners();
        state.Treaties.Remove(food.Owner, tools.Owner, TreatyKind.Trade);
        var runner = new TurnRunner(state);
        runner.AddSystem(new ForeignTradeSystem(Content));
        runner.RunTurn();
        Assert.Equal(0, tools.StockView[Good("meat")].Hundredths);

        Treaties.Sign(Content, state, food.Owner, tools.Owner, TreatyKind.Trade);
        HexGlobe geo = state.Worlds[0].Geometry;
        tools.Cell = geo.Wrap(food.Cell.Col + Content.Treaties.ReachCells + 4, food.Cell.Row);
        runner.RunTurn();
        Assert.Equal(0, tools.StockView[Good("meat")].Hundredths);
    }

    [Fact]
    public void TradePartnersSpreadTechnologiesHoweverFarApart()
    {
        (GameState state, SettlementState food, SettlementState tools) = Partners();
        HexGlobe geo = state.Worlds[0].Geometry;
        tools.Cell = geo.Wrap(food.Cell.Col + 30, food.Cell.Row);
        CivFacts[] before = CivFacts.Measure(Content, state);
        Assert.DoesNotContain(1, before[0].Neighbours);

        state.Treaties.Find(food.Owner, tools.Owner, TreatyKind.Trade)!.LastFlowMonth = state.Month;
        CivFacts[] after = CivFacts.Measure(Content, state);
        Assert.Contains(1, after[0].Neighbours);
        Assert.Contains(0, after[1].Neighbours);
    }

    [Fact]
    public void MerchantsCarryEpidemicsToTradePartners()
    {
        (GameState state, SettlementState food, SettlementState tools) = Partners();
        food.Population[0] = 200;
        Treaty t = state.Treaties.Find(food.Owner, tools.Owner, TreatyKind.Trade)!;
        t.GatewayA = (t.A == food.Owner ? food : tools).Id.Value;
        t.GatewayB = (t.B == food.Owner ? food : tools).Id.Value;
        t.LastFlowMonth = state.Month;
        EventDefinition epidemic = Content.Events.First(e => e.Key == "epidemic") with
        {
            TimesPer10000Years = 10_000,
            Conditions = [new EventCondition(EventConditionKind.PeopleAtLeast, 101, [], 100, true)],
            SpreadPercent = 100,
        };
        var runner = new TurnRunner(state);
        runner.AddSystem(new EventSystem(Content, [epidemic]));

        runner.RunTurn();

        int depopulation = Content.Effect("depopulation").Id;
        Assert.Contains(state.Effects.All, e => e.DefinitionId == depopulation && e.TargetId == tools.Id.Value);
        Assert.Contains(runner.LastChronicle, l => l.StartsWith("Merchants carried the epidemic", System.StringComparison.Ordinal));
    }

    [Fact]
    public void CivilizationsAtWarWithTheSameEnemyRememberIt()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        CivilizationState enemy = Civ(state, 2);
        Treaties.DeclareWar(Content, state, enemy.Id, a.Id);
        Treaties.DeclareWar(Content, state, enemy.Id, b.Id);
        var runner = new TurnRunner(state);
        runner.AddSystem(new DiplomacySystem(Content));
        runner.RunTurn();
        Assert.True(a.Relation(b.Id)!.FindOngoing(RelationEvent.CommonEnemy) >= 0);
        Assert.Equal(-1, a.Relation(enemy.Id)!.FindOngoing(RelationEvent.CommonEnemy));

        Treaties.Sign(Content, state, enemy.Id, a.Id, TreatyKind.Peace);
        runner.RunTurn();
        Assert.Equal(-1, a.Relation(b.Id)!.FindOngoing(RelationEvent.CommonEnemy));
        Assert.Contains(a.Relation(b.Id)!.Memories, m => m.Kind == RelationEvent.CommonEnemy);
    }

    // Two friendly neighbours of equal strength, the first one cautious: it wants a non-aggression pact.
    private static (GameState State, CivilizationState A, CivilizationState B) Neighbours()
    {
        (GameState state, HexCoord[] land) = Empty(44);
        MeetAll(state);
        HexGlobe geo = state.Worlds[0].Geometry;
        HexCoord here = land[land.Length / 2];
        HexCoord there = land.First(c => geo.Distance(here, c) == 4);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        Camp(state, a, here, 100);
        Camp(state, b, there, 100);
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Traits = Enumerable.Repeat(500, Character.TraitCount).ToArray();
            c.Traits[(int)Trait.Caution] = 1000;
            c.Traits[(int)Trait.Tradefulness] = 0;
        }

        foreach ((CivilizationState x, CivilizationState y) in new[] { (a, b), (b, a) })
        {
            Relation r = x.Relation(y.Id)!;
            r.Opening = 4000;
            r.Recount(state.Month);
        }

        return (state, a, b);
    }

    private static List<CommandEnvelope> Decide(GameState state, Advisor advisor)
    {
        var queue = new CommandQueue();
        advisor.IssueCommands(state, queue);
        var list = new List<CommandEnvelope>();
        queue.DrainSorted(state, state.Turn, list);
        return list;
    }

    [Fact]
    public void TheDiplomatProposesAPactAndTheOtherSideWeighsItTheSameWay()
    {
        (GameState state, CivilizationState a, CivilizationState b) = Neighbours();
        var diplomat = new DiplomatAdvisor(Content) { Log = new Core.Ai.Utility.DecisionLog() };

        List<CommandEnvelope> first = Decide(state, diplomat);
        CommandEnvelope proposal = Assert.Single(first, e => e.Civ == a.Id && e.Command is ProposeTreatyCommand);
        Assert.Equal(TreatyKind.NonAggression, ((ProposeTreatyCommand)proposal.Command).Kind);
        Assert.Contains(diplomat.Log.Entries, d => d.Civ == a.Id && d.Action == Core.Ai.Utility.AiAction.ProposeTreaty && d.Target == b.Id.Value);
        foreach (CommandEnvelope e in first.Where(e => e.Command.Validate(state, e.Civ).Success))
        {
            e.Command.Apply(state, e.Civ);
        }

        List<CommandEnvelope> second = Decide(state, diplomat);
        CommandEnvelope answer = Assert.Single(second, e => e.Command is AnswerProposalCommand x && x.From == a.Id);
        Assert.True(((AnswerProposalCommand)answer.Command).Accept);
        Assert.Equal(b.Id, answer.Civ);
    }

    [Fact]
    public void AFriendlessDiplomatProposesNothing()
    {
        (GameState state, CivilizationState a, CivilizationState b) = Neighbours();
        foreach ((CivilizationState x, CivilizationState y) in new[] { (a, b), (b, a) })
        {
            Relation r = x.Relation(y.Id)!;
            r.Opening = -6000;
            r.Recount(state.Month);
        }

        Assert.DoesNotContain(Decide(state, new DiplomatAdvisor(Content)), e => e.Command is ProposeTreatyCommand);
    }

    [Fact]
    public void AStrongHostileNeighbourWithACauseDeclaresWar()
    {
        (GameState state, CivilizationState a, CivilizationState b) = Neighbours();
        SettlementState strong = state.Settlements.First(s => s.Owner == a.Id);
        strong.Population[Content.Strata.First(s => s.Fights).Id] = 400;
        a.Traits[(int)Trait.Aggression] = 1000;
        Relation r = a.Relation(b.Id)!;
        r.Opening = -6000;
        Contacts.RecordAttack(Content, state, a.Id, b.Id);
        r.Recount(state.Month);

        CommandEnvelope war = Assert.Single(Decide(state, new DiplomatAdvisor(Content)), e => e.Civ == a.Id);
        Assert.IsType<DeclareWarCommand>(war.Command);
    }

    [Fact]
    public void TreatiesAndProposalsSurviveASave()
    {
        (GameState state, _) = Empty(41);
        MeetAll(state);
        CivilizationState a = Civ(state, 0);
        CivilizationState b = Civ(state, 1);
        CivilizationState c = Civ(state, 2);
        Treaties.Sign(Content, state, a.Id, b.Id, TreatyKind.Trade);
        Treaty t = state.Treaties.Find(a.Id, b.Id, TreatyKind.Trade)!;
        t.GatewayA = 3;
        t.LastFlowMonth = 0;
        t.TotalWorth = 1234;
        Treaties.DeclareWar(Content, state, c.Id, a.Id);
        state.Treaties.Propose(new TreatyProposal(c.Id, a.Id, TreatyKind.Peace, 0, ProposalStatus.Pending));

        using var stream = new MemoryStream();
        SaveGameSerializer.Save(state, stream);
        stream.Position = 0;
        GameState loaded = SaveGameSerializer.Load(stream);

        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
        Assert.Equal(1234, loaded.Treaties.Find(a.Id, b.Id, TreatyKind.Trade)!.TotalWorth);
        Assert.True(loaded.Treaties.AtWar(a.Id, c.Id));
        Assert.Single(loaded.Treaties.Proposals);
    }

    [Fact]
    public void TheTreatyBookStaysSortedWhateverTheOrderOfSigning()
    {
        var book = new TreatyBook();
        book.Add(new Treaty(new CivId(3), new CivId(1), TreatyKind.Trade, new CivId(3), 0));
        book.Add(new Treaty(new CivId(0), new CivId(2), TreatyKind.NonAggression, new CivId(0), 0));
        book.Add(new Treaty(new CivId(1), new CivId(3), TreatyKind.War, new CivId(1), 0));
        book.Add(new Treaty(new CivId(2), new CivId(0), TreatyKind.NonAggression, new CivId(2), 5));
        Assert.Equal([(0, 2, TreatyKind.NonAggression), (1, 3, TreatyKind.War), (1, 3, TreatyKind.Trade)],
            book.All.Select(t => (t.A.Value, t.B.Value, t.Kind)));
        Assert.Equal(0, book.Find(new CivId(2), new CivId(0), TreatyKind.NonAggression)!.SinceMonth);
        Assert.True(book.Remove(new CivId(3), new CivId(1), TreatyKind.War));
        Assert.False(book.AtWar(new CivId(1), new CivId(3)));
    }

    [Fact]
    public void DiplomacyDataLoads()
    {
        Assert.Equal(1500, Content.Treaties.DeclareWarAbove); // "1.5" in the file
        Assert.Equal(1600, Content.Treaties.ReachCells * 100);
        Assert.Equal(2000, Content.Treaties.Opinion.Evaluate(5000)); // +50 points: factor 2
        Assert.Equal(0, Content.Treaties.Opinion.Evaluate(-5000));
        Assert.True(Content.Treaties.Kinds[(int)TreatyKind.NonAggression].BindsAgainstWar);
        Assert.False(Content.Treaties.Kinds[(int)TreatyKind.Trade].BindsAgainstWar);
        Assert.Equal(-1, Content.Treaties.Kinds[(int)TreatyKind.Trade].RequiresTechnology);
    }

    [Fact]
    public void BrokenDiplomacyDataIsRejected()
    {
        ContentException e = Assert.Throws<ContentException>(() => WithData(
            "{\"kind\": \"trade\"},", string.Empty,
            "\"years\": 100, ", string.Empty,
            "\"x0\": 120, \"f0\": \"0\", \"x1\": 250", "\"x0\": 250, \"f0\": \"0\", \"x1\": 120"));
        Assert.Contains(e.Errors, x => x.Contains("missing kind 'trade'", System.StringComparison.Ordinal));
        Assert.Contains(e.Errors, x => x.Contains("peace needs years", System.StringComparison.Ordinal));
        Assert.Contains(e.Errors, x => x.Contains("curves.advantage: x1 must be greater than x0", System.StringComparison.Ordinal));
    }

    // The game data with diplomacy.json changed by pairs of (old, new) text.
    private static GameContent WithData(params string[] replacements)
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim-diplomacy-" + System.Environment.ProcessId + "-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "diplomacy.json");
            string text = File.ReadAllText(path);
            for (int k = 0; k + 1 < replacements.Length; k += 2)
            {
                Assert.Contains(replacements[k], text, System.StringComparison.Ordinal);
                text = text.Replace(replacements[k], replacements[k + 1], System.StringComparison.Ordinal);
            }

            File.WriteAllText(path, text);
            return ContentLoader.Load(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
