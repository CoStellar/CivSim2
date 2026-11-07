using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Diplomacy;

/// <summary>War goals, score, weariness and peace terms (GDD 9.3; stage 4e PR 1, ADR 0021).</summary>
public class WarTests
{
    private static readonly GameContent Content = ContentLoader.Load();

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

        return (state, land.ToArray());
    }

    private static SettlementState Village(GameState state, CivilizationState civ, HexCoord cell, int people)
    {
        SettlementState s = state.FoundSettlement(civ.Id, state.Worlds[0].Id, cell, civ.Name + " Village", Content.SettlementLevel("village").Id, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Content.Strata[0].Id] = people;
        return s;
    }

    private static string? Run(GameState state, CivId civ, ICommand command)
    {
        CommandResult result = command.Validate(state, civ);
        Assert.True(result.Success, result.Reason);
        if (command is IChronicledCommand c)
        {
            return c.ApplyWithChronicle(state, civ);
        }

        command.Apply(state, civ);
        return null;
    }

    private static int Good(string key) => Content.Goods.ToList().FindIndex(g => g.Key == key);

    [Fact]
    public void TheRulesComeFromTheData()
    {
        WarRules rules = Content.War;
        Assert.Equal(3, rules.Goals.Count);
        Assert.Equal(100, rules.Goals[(int)WarGoal.Loot].WitnessPercent);
        Assert.Equal(0, rules.Goals[(int)WarGoal.Revenge].WitnessPercent);
        Assert.Equal(60, rules.PeaceAbove);
        Assert.Equal(50, rules.RansomPercentPerScorePoint);
        Assert.Contains(Content.Effects, e => e.Key == "war_weariness" && e.Stat == EffectStat.Mood);
    }

    [Fact]
    public void AWarNeedsAGoalItCanName()
    {
        (GameState state, HexCoord[] land) = Empty(51);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState theirs = Village(state, b, land[10], 60);

        Assert.False(new DeclareWarCommand(Content, b.Id, WarGoal.Revenge).Validate(state, a.Id).Success, "nothing to avenge");
        Assert.False(new DeclareWarCommand(Content, b.Id, WarGoal.Land, -1).Validate(state, a.Id).Success, "no settlement named");

        CivSim.Core.Perception.MapMemory.See(Content, state);
        bool known = CivSim.Core.Perception.MapMemory.Knows(Content, state, a, state.Worlds[0].Geometry.Index(theirs.Cell));
        Assert.Equal(known, new DeclareWarCommand(Content, b.Id, WarGoal.Land, theirs.Id.Value).Validate(state, a.Id).Success);
        Assert.True(new DeclareWarCommand(Content, b.Id, WarGoal.Loot).Validate(state, a.Id).Success);
    }

    [Fact]
    public void LootIsRememberedByEveryoneRevengeOnlyByTheVictim()
    {
        (GameState state, _) = Empty(52);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        CivilizationState witness = state.Civilizations[2];
        string? line = Run(state, a.Id, new DeclareWarCommand(Content, b.Id, WarGoal.Loot));
        Assert.EndsWith(" for loot.", line, System.StringComparison.Ordinal);
        Assert.Contains(b.Relation(a.Id)!.Memories, m => m.Kind == RelationEvent.Aggression);
        Assert.Contains(witness.Relation(a.Id)!.Memories, m => m.Kind == RelationEvent.Aggression);

        // Revenge: b now has a grievance against a; witnesses do not hold it against b.
        Treaties.Sign(Content, state, a.Id, b.Id, TreatyKind.Peace);
        state.Treaties.Remove(a.Id, b.Id, TreatyKind.Peace);
        Run(state, b.Id, new DeclareWarCommand(Content, a.Id, WarGoal.Revenge));
        Assert.DoesNotContain(witness.Relation(b.Id)!.Memories, m => m.Kind == RelationEvent.Aggression);
    }

    [Fact]
    public void FightsMoveTheScoreAndWearinessGrowsWithYearsAndTheDead()
    {
        (GameState state, HexCoord[] land) = Empty(53);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        Village(state, a, land[0], 200);
        Village(state, b, land[40], 100);
        Run(state, a.Id, new DeclareWarCommand(Content, b.Id, WarGoal.Loot));
        Treaty war = state.Treaties.Find(a.Id, b.Id, TreatyKind.War)!;
        Assert.Equal(200, a.Id == war.A ? war.PeopleA : war.PeopleB);

        Wars.RecordFight(Content, state, a.Id, b.Id, settlementTaken: true, winnerDead: 4, loserDead: 10);
        Assert.Equal(Content.War.ScorePerWin + Content.War.ScorePerSettlement, Wars.ScoreFor(war, a.Id));
        Assert.Equal(-Wars.ScoreFor(war, a.Id), Wars.ScoreFor(war, b.Id));

        // 20 years and 10 of 100 people dead: 20 × 1 + 10 × 3 = 50 points for b; a lost 2% (2 × 3 + 20 = 26).
        long month = war.SinceMonth + (20 * 12);
        Assert.Equal(50, Wars.ComputeWeariness(Content.War, war, b.Id, month));
        Assert.Equal(26, Wars.ComputeWeariness(Content.War, war, a.Id, month));
    }

    [Fact]
    public void WearinessLowersTheMoodOfTheWholeCivilization()
    {
        (GameState state, HexCoord[] land) = Empty(54);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        Village(state, b, land[40], 100);
        Run(state, a.Id, new DeclareWarCommand(Content, b.Id, WarGoal.Loot));
        Wars.RecordFight(Content, state, a.Id, b.Id, settlementTaken: false, winnerDead: 0, loserDead: 20);

        var runner = new TurnRunner(state);
        runner.AddSystem(new WarSystem(Content));
        runner.RunTurn();
        int w = Wars.Weariness(state, b.Id);
        Assert.True(w >= 60, $"weariness {w}");
        int mood = state.Effects.Total(Content.Effects, EffectStat.Mood, EffectTarget.Civilization, WorldId.Earth, b.Id.Value);
        Assert.Equal(-(w * Content.War.MoodAtFullWeariness / 100), mood);
    }

    [Fact]
    public void TheWinnerTakesTheSettlementTheWarWasAbout()
    {
        (GameState state, HexCoord[] land) = Empty(55);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        Village(state, a, land[0], 100);
        SettlementState disputed = Village(state, b, land[40], 80);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Land, disputed.Id.Value);
        Assert.Equal(PeaceTermsKind.White, Wars.TermsFor(Content, state, a.Id, b.Id).Kind);

        Wars.RecordFight(Content, state, a.Id, b.Id, settlementTaken: true, winnerDead: 1, loserDead: 5);
        PeaceTerms terms = Wars.TermsFor(Content, state, a.Id, b.Id);
        Assert.Equal(new PeaceTerms(PeaceTermsKind.Cede, b.Id, disputed.Id.Value, 0), terms);
        Assert.Equal(terms, Wars.TermsFor(Content, state, b.Id, a.Id)); // the loser offers the same
        Assert.True(Wars.TermsWorth(Content, terms, a.Id) > 0);
        Assert.True(Wars.TermsWorth(Content, terms, b.Id) < 0);

        Run(state, a.Id, new ProposeTreatyCommand(Content, b.Id, TreatyKind.Peace, terms));
        string? line = Run(state, b.Id, new AnswerProposalCommand(Content, a.Id, TreatyKind.Peace, Accept: true));
        Assert.EndsWith($"handed over {disputed.Name}.", line, System.StringComparison.Ordinal);
        Assert.Equal(a.Id, disputed.Owner);
        Assert.Equal(80, disputed.TotalPopulation());
        Assert.Equal(terms, state.Treaties.Find(a.Id, b.Id, TreatyKind.Peace)!.Terms);
    }

    [Fact]
    public void ALootWarEndsWithARansom()
    {
        (GameState state, HexCoord[] land) = Empty(56);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState mine = Village(state, a, land[0], 100);
        SettlementState theirs = Village(state, b, land[40], 80);
        int grain = Good("grain");
        theirs.Stock[grain] = Amount.FromUnits(100);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        for (int k = 0; k < 4; k++)
        {
            Wars.RecordFight(Content, state, a.Id, b.Id, settlementTaken: false, winnerDead: 0, loserDead: 1);
        }

        // Score 40: (40 − 20 + 1) × 0.5 = 10% of every store.
        PeaceTerms terms = Wars.TermsFor(Content, state, b.Id, a.Id);
        Assert.Equal(new PeaceTerms(PeaceTermsKind.Ransom, b.Id, -1, 10), terms);
        Treaties.Sign(Content, state, b.Id, a.Id, TreatyKind.Peace, terms);
        Assert.Equal(Amount.FromUnits(90), theirs.Stock[grain]);
        Assert.Equal(Amount.FromUnits(10), mine.Stock[grain]);
    }

    [Fact]
    public void TermsThatCannotBeKeptAreRefused()
    {
        (GameState state, HexCoord[] land) = Empty(57);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState mine = Village(state, a, land[0], 100);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        var notTheirs = new PeaceTerms(PeaceTermsKind.Cede, b.Id, mine.Id.Value, 0);
        Assert.False(new ProposeTreatyCommand(Content, b.Id, TreatyKind.Peace, notTheirs).Validate(state, a.Id).Success);
        var tooMuch = new PeaceTerms(PeaceTermsKind.Ransom, b.Id, -1, Content.War.MaxRansomPercent + 1);
        Assert.False(new ProposeTreatyCommand(Content, b.Id, TreatyKind.Peace, tooMuch).Validate(state, a.Id).Success);
        Assert.False(new ProposeTreatyCommand(Content, b.Id, TreatyKind.Trade, new PeaceTerms(PeaceTermsKind.Ransom, b.Id, -1, 5)).Validate(state, a.Id).Success);
    }

    [Fact]
    public void WarsAndTermsSurviveASave()
    {
        (GameState state, HexCoord[] land) = Empty(58);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        Village(state, a, land[0], 100);
        SettlementState theirs = Village(state, b, land[40], 80);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Land, theirs.Id.Value);
        Wars.RecordFight(Content, state, a.Id, b.Id, settlementTaken: true, winnerDead: 2, loserDead: 7);
        state.Treaties.Propose(new TreatyProposal(a.Id, b.Id, TreatyKind.Peace, state.Month, ProposalStatus.Pending, Wars.TermsFor(Content, state, a.Id, b.Id)));

        using var stream = new MemoryStream();
        SaveGameSerializer.Save(state, stream);
        stream.Position = 0;
        GameState loaded = SaveGameSerializer.Load(stream);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
        Treaty war = loaded.Treaties.Find(a.Id, b.Id, TreatyKind.War)!;
        Assert.Equal(WarGoal.Land, war.Goal);
        Assert.Equal(theirs.Id.Value, war.GoalSettlement);
        Assert.Equal(PeaceTermsKind.Cede, loaded.Treaties.Proposals.Single().Terms.Kind);
    }

    [Fact]
    public void BrokenWarDataIsReported()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-war-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "war.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("    {\"goal\": \"revenge\", \"name\": \"Revenge\", \"victimPercent\": 50, \"witnessPercent\": 0}\n", string.Empty, System.StringComparison.Ordinal)
                .Replace("\"witnessPercent\": 100},", "\"witnessPercent\": 100}", System.StringComparison.Ordinal));
            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("missing goal 'revenge'", System.StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
