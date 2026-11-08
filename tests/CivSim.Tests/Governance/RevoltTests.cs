using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Governance;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Naming;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Governance;

/// <summary>Revolts: chance, rebels, secession as a new civilization, coup (GDD 7.3; stage 4f PR 4).</summary>
public class RevoltTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Gov(string key) => Content.Governments.Single(g => g.Key == key).Id;

    private static int Stratum(string key) => Content.Strata.Single(s => s.Key == key).Id;

    private static SettlementState Found(GameState state, CivilizationState civ, int dCol, int dRow, int people, int loyalty)
    {
        HexCoord at = state.Worlds[0].Geometry.Wrap(civ.StartCell.Col + dCol, civ.StartCell.Row + dRow);
        SettlementState s = state.FoundSettlement(civ.Id, WorldId.Earth, at, "Rebel" + state.NextSettlementId, Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Stratum("producers")] = people;
        s.Loyalty = loyalty;
        return s;
    }

    [Fact]
    public void RevoltsBreakOutOnlyBelowTheThresholdAndNotInTheCalm()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(31), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.StepMonths = 600;
        civ.Stability = Content.Stability.RevoltBelow;
        Assert.Equal(0, Revolts.ChancePerMille(Content, state, civ));

        // 2% a year over a 50-year step: 1 - 0.98^50, about 64%.
        civ.Stability = Content.Stability.RevoltBelow - 1;
        Assert.InRange(Revolts.ChancePerMille(Content, state, civ), 630, 640);
        civ.StepMonths = 1; // a war month: one year's chance
        Assert.Equal(Content.Stability.RevoltPerMillePerYear, Revolts.ChancePerMille(Content, state, civ));

        civ.RevoltCalmUntilMonth = state.Month + 1;
        Assert.Equal(0, Revolts.ChancePerMille(Content, state, civ));
    }

    [Fact]
    public void TheRebelsAreTheLargestGroupOfDistantDisloyalSettlements()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(32), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        state.Settlements.First(s => s.Owner == civ.Id).Loyalty = 0; // the capital never rebels
        SettlementState loyal = Found(state, civ, 2, 0, 500, 40);
        SettlementState a = Found(state, civ, -3, 0, 150, 2);
        SettlementState b = Found(state, civ, -5, 0, 150, 1);
        SettlementState lone = Found(state, civ, 0, 14, 250, 0);
        var rebels = Revolts.Rebels(Content, state, civ)!;
        Assert.Equal([a.Id, b.Id], rebels.Select(s => s.Id)); // 300 people together beat 250 alone
        Assert.DoesNotContain(loyal, rebels);

        // Too few people: a coup instead of a secession.
        a.Population.Clear();
        b.Population.Clear();
        lone.Population.Clear();
        Assert.Null(Revolts.Rebels(Content, state, civ));
    }

    [Fact]
    public void FarRebelsDriftAwayEvenFromACalmState()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(34), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.StepMonths = 600;
        civ.Stability = 80; // calm: no revolt
        Found(state, civ, 0, 14, 300, 0);
        int before = state.Civilizations.Count;

        civ.RevoltCalmUntilMonth = state.Month + 1;
        Assert.Null(Revolts.Drift(Content, state, civ, new DeterministicRandom(1))); // not in the calm after a revolt
        civ.RevoltCalmUntilMonth = 0;

        string? line = null;
        for (ulong seed = 1; seed < 200 && line is null; seed++)
        {
            Assert.Null(Revolts.Check(Content, state, civ, new DeterministicRandom(seed)));
            line = Revolts.Drift(Content, state, civ, new DeterministicRandom(seed));
        }

        Assert.NotNull(line);
        Assert.Contains("broke away", line, System.StringComparison.Ordinal);
        Assert.Equal(before + 1, state.Civilizations.Count);
        Assert.True(state.Month < civ.RevoltCalmUntilMonth);
    }

    [Fact]
    public void RebelsBreakAwayAsANewCivilization()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(33), Content, out _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        for (int i = 0; i < 3; i++)
        {
            runner.RunTurn();
        }

        CivilizationState parent = state.Civilizations[0];
        parent.Government = Gov("chiefdom");
        SettlementState a = Found(state, parent, -3, 0, 150, 2);
        SettlementState b = Found(state, parent, -5, 0, 160, 1);
        new Core.Population.Influence(Content).Recompute(state); // their land is the parent's until they break away
        Assert.Equal(parent.Id.Value, state.Worlds[0].Map.Owner[state.Worlds[0].Geometry.Index(b.Cell)]);
        int before = state.Civilizations.Count;
        string line = Revolts.Revolt(Content, state, parent, new DeterministicRandom(7));

        Assert.Equal(before + 1, state.Civilizations.Count);
        CivilizationState child = state.Civilizations[^1];
        Assert.Equal(before, child.Id.Value);
        Assert.Equal(parent.Id.Value, child.ParentCiv);
        Assert.Equal(child.Id, a.Owner);
        Assert.Equal(child.Id, b.Owner);
        Assert.Equal(b.Id.Value, child.Capital); // the larger one
        Assert.NotEqual(parent.Government, child.Government); // a splinter of other views
        Assert.NotEqual(parent.Name, child.Name);
        Assert.StartsWith(parent.Name[..2], child.Name, System.StringComparison.Ordinal); // a related name
        Assert.NotEqual(Names.Language(Content.Names, parent).PlaceName(5), Names.Language(Content.Names, child).PlaceName(5));
        Assert.Equal(parent.KnownTechnologies, child.KnownTechnologies);
        Assert.NotEmpty(child.LeaderName);
        Assert.Equal(child.Id.Value, state.Worlds[0].Map.Owner[state.Worlds[0].Geometry.Index(b.Cell)]);
        Assert.Equal($"{a.Name} and 1 other settlement broke away from the Chiefdom of {parent.Name} and founded the {Governments.Title(Content, child)}, led by {Leaders.Styled(Content, child)}.", line);

        // The parent remembers, and it is a reason for war; there is no war yet.
        Assert.True(Treaties.HasGrievance(state, parent.Id, child.Id));
        Assert.True(child.Relation(parent.Id)!.HasMet);
        Assert.False(state.Treaties.AtWar(parent.Id, child.Id));
        Assert.True(state.Month < parent.RevoltCalmUntilMonth);

        // The game goes on with the new people, and a save keeps it.
        for (int i = 0; i < 5; i++)
        {
            runner.RunTurn();
        }

        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
        Assert.Equal(Names.Language(Content.Names, child).PlaceName(9), Names.Language(Content.Names, loaded.Civilizations[^1]).PlaceName(9));
    }

    [Fact]
    public void WithoutRebelsARevoltIsACoup()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(34), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Gov("elder_council");
        string leader = civ.LeaderName;
        string line = Revolts.Revolt(Content, state, civ, new DeterministicRandom(3));
        Assert.Equal(4, state.Civilizations.Count);
        Assert.Equal(Gov("chiefdom"), civ.Government);
        Assert.NotEqual(leader, civ.LeaderName);
        Assert.True(Stability.InAnarchy(civ, state.Month));
        Assert.Equal($"A coup in the Council of {civ.Name}: {Leaders.Styled(Content, civ)} seized power; it became the Chiefdom of {civ.Name}; 2 years of anarchy follow.", line);
    }

    [Fact]
    public void AFullWorldHasCoupsInsteadOfSecessions()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim-revolts-" + System.Environment.ProcessId + "-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "stability.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"maxCivilizations\": 64", "\"maxCivilizations\": 4", System.StringComparison.Ordinal));
            GameContent content = ContentLoader.Load(dir);
            GameState state = GameState.CreateNew(GameSettings.Test(35), content, out _);
            CivilizationState civ = state.Civilizations[0];
            Found(state, civ, -3, 0, 300, 0);
            Assert.StartsWith("A coup in the ", Revolts.Revolt(content, state, civ, new DeterministicRandom(1)), System.StringComparison.Ordinal);
            Assert.Equal(4, state.Civilizations.Count);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
