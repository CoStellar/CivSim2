using System;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;
using CivSim.Core.WorldGen;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>
/// A player's order holds (stage 6 PR P1; Patryk 2026-11-13: "a decision of the player is carried out to the end"): the AI does not
/// undo it, neither in the same tick nor later, while it holds.
/// </summary>
public class PlayerOrderTests
{
    private static readonly GameContent Content = TestContent.Load();
    private static readonly PlayerId Player = new(1);

    private static (GameState State, CivilizationState Civ, SettlementState Camp) NewGame(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out WorldGenReport _);
        CivilizationState civ = state.Civilizations[0];
        civ.Controller = Player;
        Array.Fill(civ.SeenYear, (int)(state.Month / 12) + 1);
        SettlementState camp = state.Settlements.First(s => s.Owner == civ.Id);
        camp.Population[0] += 100; // enough people to split
        camp.ScoutOut = true;
        return (state, civ, camp);
    }

    // A cell <paramref name="distance"/> or more cells from the camp where it may move to (found: where its settlers may found a camp).
    private static HexCoord FreeCell(GameState state, CivilizationState civ, SettlementState camp, int distance, bool found = false, HexCoord? not = null)
    {
        WorldState world = state.Worlds[0];
        for (int d = distance; d <= Content.Population.MaxMoveCells; d++)
        {
            var ring = new HexCoord[6 * d];
            int count = world.Geometry.Ring(camp.Cell, d, ring);
            foreach (HexCoord c in ring.AsSpan(0, count))
            {
                bool ok = found
                    ? UnitSystem.CanFound(Content, state, civ.Id, world, world.Geometry.Index(c)) && MapMemoryKnows(state, civ, world, c)
                    : new MoveSettlementCommand(Content, camp.Id, c).Validate(state, civ.Id).Success;
                if (ok && c != not)
                {
                    return c;
                }
            }
        }

        throw new InvalidOperationException("No free cell for the test.");
    }

    private static bool MapMemoryKnows(GameState state, CivilizationState civ, WorldState world, HexCoord c) =>
        CivSim.Core.Perception.MapMemory.Knows(Content, state, civ, world.Geometry.Index(c))
        && new SplitSettlementCommand(Content, state.Settlements.First(s => s.Owner == civ.Id).Id, c, 10).Validate(state, civ.Id).Success;

    [Fact]
    public void InTheSameTickThePlayersOrderWinsOverTheAis()
    {
        (GameState state, CivilizationState civ, SettlementState camp) = NewGame(61);
        HexCoord mine = FreeCell(state, civ, camp, 2);
        HexCoord theirs = FreeCell(state, civ, camp, 2, not: mine);
        var runner = new TurnRunner(state);
        runner.Commands.Enqueue(Player, civ.Id, new MoveSettlementCommand(Content, camp.Id, mine));
        runner.Commands.Enqueue(PlayerId.Ai, civ.Id, new MoveSettlementCommand(Content, camp.Id, theirs)); // decided in the same tick
        runner.RunTurn();

        Assert.Equal(mine, camp.Cell);
        Assert.Contains(runner.LastReport.Rejected, r => r.Reason == "A player's order holds.");
    }

    [Fact]
    public void TheOrderHoldsForItsYearsThenTheAiDecidesAgain()
    {
        (GameState state, CivilizationState civ, SettlementState camp) = NewGame(62);
        HexCoord mine = FreeCell(state, civ, camp, 2);
        var runner = new TurnRunner(state);
        runner.Commands.Enqueue(Player, civ.Id, new MoveSettlementCommand(Content, camp.Id, mine));
        runner.RunTurn();
        Assert.Equal(state.Turn - 1, camp.PlayerOrderTick);

        long hold = PlayerOrders.HoldTicks(Content);
        Assert.Equal(Content.Time.ActionTicks(12L * Content.Ai.PlayerOrderHoldYears), hold);
        var ai = new MoveSettlementCommand(Content, camp.Id, FreeCell(state, civ, camp, 2, not: mine));
        Assert.True(PlayerOrders.HeldAgainstAi(state, civ.Id, ai));
        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, new RenameSettlementCommand(camp.Id, "Ours"))); // only what undoes the order

        while (state.Turn < camp.PlayerOrderTick + hold)
        {
            runner.RunTurn();
        }

        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, ai));
    }

    [Fact]
    public void TheCampThePlayersSettlersFoundIsHeldAndIsNotMergedBack()
    {
        (GameState state, CivilizationState civ, SettlementState camp) = NewGame(63);
        HexCoord site = FreeCell(state, civ, camp, Content.Population.MinDistanceBetweenOwnCamps, found: true);
        var runner = new TurnRunner(state);
        runner.AddSystem(new UnitSystem(Content));
        int percent = Math.Max(1, (Content.Population.MergeBelowPeople - 5) * 100 / camp.TotalPopulation()); // a small group: below the merge limit
        runner.Commands.Enqueue(Player, civ.Id, new SplitSettlementCommand(Content, camp.Id, site, percent));
        runner.RunTurn();
        Unit settlers = state.Units.All.Single(u => u.Role == UnitRole.Settlers && u.Owner == civ.Id);
        Assert.True(settlers.ByPlayer);

        for (int t = 0; t < 400 && state.Units.All.Any(u => u.Role == UnitRole.Settlers && u.Owner == civ.Id && !u.Gone); t++)
        {
            runner.RunTurn();
        }

        SettlementState founded = state.Settlements.Single(s => s.Owner == civ.Id && s.Cell == site);
        Assert.True(founded.TotalPopulation() < Content.Population.MergeBelowPeople);
        Assert.True(founded.PlayerOrderTick >= 0);
        Assert.True(PlayerOrders.HeldAgainstAi(state, civ.Id, new AbandonSettlementCommand(Content, founded.Id, camp.Id)));
    }

    [Fact]
    public void AWorkGroupThePlayerSentHoldsItsHome()
    {
        (GameState state, CivilizationState civ, SettlementState camp) = NewGame(64);
        var runner = new TurnRunner(state);
        HexCoord cell = camp.Cell; // woodcutting on the camp's own cell when it gives wood
        WorldState world = state.Worlds[0];
        byte forest = (byte)Content.Biomes.ToList().FindIndex(b => b.Key == "deciduous_forest");
        world.Map.Biome.Set(world.Geometry.Index(cell), forest);
        runner.Commands.Enqueue(Player, civ.Id, new StartTaskCommand(Content, camp.Id, "woodcutting", cell));
        runner.RunTurn();
        Unit group = state.Units.All.Single(u => u.Role == UnitRole.Workers);
        Assert.True(group.ByPlayer);

        camp.PlayerOrderTick = -1000; // the hold of the order itself is over
        Assert.True(PlayerOrders.Held(Content, state, camp.Id)); // the group is still out
        group.Gone = true;
        state.Units.RemoveGone();
        Assert.False(PlayerOrders.Held(Content, state, camp.Id));
    }

    [Fact]
    public void ACampaignThePlayerDirectsIsNotRecalledAndNoPeaceIsMadeMeanwhile()
    {
        (GameState state, CivilizationState civ, SettlementState camp) = NewGame(65);
        CivId enemy = state.Civilizations[1].Id;
        Episode e = state.Episodes.Begin(new Episode { Kind = EpisodeKind.Campaign, Civ = civ.Id, Other = enemy, Home = camp.Id.Value });
        var recall = new RecallCampaignCommand(e.Id);
        var peace = new ProposeTreatyCommand(Content, enemy, TreatyKind.Peace);
        var answer = new AnswerProposalCommand(Content, enemy, TreatyKind.Peace, Accept: true);
        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, recall)); // a campaign of the AI: the commander may call it back
        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, peace));

        e.ByPlayer = true; // the player sent its army to attack (AttackCommand marks it)
        Assert.True(PlayerOrders.HeldAgainstAi(state, civ.Id, recall));
        Assert.True(PlayerOrders.HeldAgainstAi(state, civ.Id, peace));
        Assert.True(PlayerOrders.HeldAgainstAi(state, civ.Id, answer));
        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, answer with { Accept = false })); // refusing peace keeps the war going

        e.Ended = true;
        Assert.False(PlayerOrders.HeldAgainstAi(state, civ.Id, peace));
    }

    [Fact]
    public void InAWholeGameTheAiLeavesThePlayersNewCampAloneWhileTheOrderHolds()
    {
        (GameState state, CivilizationState civ, SettlementState camp) = NewGame(66);
        HexCoord site = FreeCell(state, civ, camp, Content.Population.MinDistanceBetweenOwnCamps, found: true);
        var runner = new TurnRunner(state);
        AiCouncil council = CoreSystems.Register(runner, Content); // the AI leads every lever of the player's civilization too
        int percent = Math.Max(1, (Content.Population.MergeBelowPeople - 5) * 100 / camp.TotalPopulation());
        runner.Commands.Enqueue(Player, civ.Id, new SplitSettlementCommand(Content, camp.Id, site, percent));
        SettlementState? founded = null;
        for (int t = 0; t < 600 && founded is null; t++)
        {
            runner.RunTurn();
            founded = state.Settlements.FirstOrDefault(s => s.Owner == civ.Id && s.Cell == site && s.IsActive);
        }

        Assert.NotNull(founded);
        long until = founded!.PlayerOrderTick + PlayerOrders.HoldTicks(Content);
        while (state.Turn < until - 1)
        {
            runner.RunTurn();
            // No decision on where the camp's people live (lever 1; other decisions use Subject for other ids, and work orders that are
            // no lever, such as artisans' recipes, stay the AI's).
            var wrong = council.Log.Entries.Where(d => d.Civ == civ.Id && d.Subject == founded.Id.Value && Levers.Of(d.Action) == Lever.WhereWeLive).ToList();
            Assert.True(wrong.Count == 0, $"tick {state.Turn}: {string.Join("; ", wrong)}");
            Assert.True(founded.IsActive && founded.Cell == site, $"tick {state.Turn}: the camp was moved or merged");
        }
    }
}
