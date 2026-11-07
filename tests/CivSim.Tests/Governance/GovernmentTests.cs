using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Governance;
using CivSim.Core.Map;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using CivSim.Tests.Episodes;
using Xunit;

namespace CivSim.Tests.Governance;

/// <summary>Forms of government: start, title, enactment, pace of the AI (GDD 7.2; stage 4f PR 1, ADR 0023).</summary>
public class GovernmentTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Id(string key) => Content.Governments.Single(g => g.Key == key).Id;

    [Fact]
    public void TwoGovernmentsOpenAtTheStartAndTwoWithBronze()
    {
        Assert.Equal(["elder_council", "chiefdom"], Content.Governments.Where(g => g.FromEra == 0).Select(g => g.Key));
        int bronze = Content.Eras.ToList().FindIndex(e => e.Key == "bronze_age");
        Assert.Equal(["despotism", "city_republic"], Content.Governments.Where(g => g.FromEra == bronze).Select(g => g.Key));
        Assert.Equal(120, Content.Governments[Id("chiefdom")].PercentFor(Goal.Conquest));
        Assert.Equal(100, Content.Governments[Id("chiefdom")].PercentFor(Goal.Science));
    }

    [Fact]
    public void APeopleStartsWithTheGovernmentItsCharacterLeansTo()
    {
        var civ = new CivilizationState(new CivId(0), "Karrut", PlayerId.Ai) { Traits = new int[Character.TraitCount] };
        civ.Traits[(int)Trait.Aggression] = 900;
        civ.Traits[(int)Trait.Expansiveness] = 800;
        Assert.Equal(Id("chiefdom"), Governments.Leaning(Content, civ, 0));
        civ.Traits[(int)Trait.Aggression] = 100;
        civ.Traits[(int)Trait.Caution] = 900;
        civ.Traits[(int)Trait.Openness] = 700;
        Assert.Equal(Id("elder_council"), Governments.Leaning(Content, civ, 0));

        // A new game: every people gets one, and both kinds occur.
        GameState state = GameState.CreateNew(GameSettings.Test(7) with { CivilizationCount = 12 }, Content, out _);
        Assert.All(state.Civilizations, c => Assert.Equal(Governments.Leaning(Content, c, 0), c.Government));
        Assert.Equal(2, state.Civilizations.Select(c => c.Government).Distinct().Count());
    }

    [Fact]
    public void TheTitleCarriesThePeoplesName()
    {
        var civ = new CivilizationState(new CivId(0), "Karrut", PlayerId.Ai) { Government = Id("chiefdom") };
        Assert.Equal("Chiefdom of Karrut", Governments.Title(Content, civ));
        civ.Government = Id("despotism");
        Assert.Equal("Kingdom of Karrut", Governments.Title(Content, civ));
        Assert.Equal("Karrut", Governments.Title(new GameContent([], []), civ));
    }

    [Fact]
    public void ANewGovernmentTakesEffectAfterTheEnactmentDelay()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(8), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Id("elder_council");
        var change = new ChangeGovernmentCommand(Content, Id("chiefdom"));
        Assert.True(change.Validate(state, civ.Id).Success);
        Assert.Contains("began to change its government to chiefdom", change.ApplyWithChronicle(state, civ.Id), System.StringComparison.Ordinal);
        Assert.Equal(state.Month + 48, civ.PendingGovernmentMonth);
        Assert.False(change.Validate(state, civ.Id).Success, "already under way");
        Assert.NotNull(Treaties.CannotDeclareWar(Content, state, civ.Id, state.Civilizations[1].Id, WarGoal.Loot, -1));

        var lines = new List<string>();
        Assert.Equal(0, Governments.EnactDue(Content, state, lines.Add));
        state.SetCountersForLoad(state.Month + 48, state.NextSettlementId, state.StepMonths);
        Assert.Equal(1, Governments.EnactDue(Content, state, lines.Add));
        Assert.Equal(Id("chiefdom"), civ.Government);
        Assert.Equal(CivilizationState.NoGovernment, civ.PendingGovernment);
        Assert.EndsWith($"became the Chiefdom of {civ.Name}.", lines.Single(), System.StringComparison.Ordinal);

        // A government with no delay changes at once; one of a later era is closed.
        Assert.False(new ChangeGovernmentCommand(Content, Id("despotism")).Validate(state, civ.Id).Success);
        Assert.False(new ChangeGovernmentCommand(Content, Id("chiefdom")).Validate(state, civ.Id).Success, "already ours");
    }

    [Fact]
    public void ArmiesOfADeclaredWarWaitForTheGovernment()
    {
        (GameState state, HexCoord start) = ArmyTests.Strip(110, 6);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        a.Government = Id("elder_council");
        SettlementState home = ArmyTests.Village(state, a, start, 0, 200, 75, 300);
        SettlementState target = ArmyTests.Village(state, b, start, 6, 200, 75, 300);
        string? line = new DeclareWarCommand(Content, b.Id, WarGoal.Loot).ApplyWithChronicle(state, a.Id);
        Assert.EndsWith("Its warriors will be ready in 4 years.", line, System.StringComparison.Ordinal);
        Treaty war = state.Treaties.Find(a.Id, b.Id, TreatyKind.War)!;
        Assert.Equal(state.Month + 48, war.ReadyMonth);
        Assert.Equal(war.ReadyMonth, war.QuietSince);
        Assert.Equal("Our people are not ready for war yet.", Campaigns.CannotLaunch(Content, state, a.Id, home.Id.Value, target.Id.Value, 20));
        Assert.Null(Campaigns.CannotLaunch(Content, state, b.Id, target.Id.Value, home.Id.Value, 20)); // the defender may answer at once
        Assert.True(war.ReadyFor(a.Id, war.ReadyMonth));
    }

    [Fact]
    public void TheGovernmentShiftsCautionAndPace()
    {
        var civ = new CivilizationState(new CivId(0), "Karrut", PlayerId.Ai) { Traits = new int[Character.TraitCount] };
        civ.Traits[(int)Trait.Caution] = 900;
        civ.Government = Id("elder_council");
        Assert.Equal(1000, Governments.Caution(Content, civ));
        Assert.Equal(150, Governments.ReviewPercent(Content, civ));
        civ.Government = Id("despotism");
        Assert.Equal(700, Governments.Caution(Content, civ));
        Assert.Equal(1200, Governments.Weight(Content, civ, Goal.Conquest, 1000));
    }

    [Fact]
    public void GovernmentsSurviveASave()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(9), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Id("elder_council");
        Governments.Change(Content, state, civ.Id, Id("chiefdom"));
        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(Core.Hashing.GameStateHash.Compute(state), Core.Hashing.GameStateHash.Compute(loaded));
        Assert.Equal(Id("chiefdom"), loaded.Civilizations[0].PendingGovernment);
    }

    [Fact]
    public void BadGovernmentsAreRejected()
    {
        ContentException e = Assert.Throws<ContentException>(() => WithData(
            "\"title\": \"Chiefdom of {people}\"", "\"title\": \"Chiefdom\"",
            "\"fromEra\": \"bronze_age\"", "\"fromEra\": \"golden_age\"",
            "\"leaning\": {\"aggression\": 1, \"expansiveness\": 1}", "\"leaning\": {\"anger\": 1}"));
        Assert.Contains(e.Errors, x => x.Contains("title: must contain {people}", System.StringComparison.Ordinal));
        Assert.Contains(e.Errors, x => x.Contains("unknown era 'golden_age'", System.StringComparison.Ordinal));
        Assert.Contains(e.Errors, x => x.Contains("unknown name 'anger'", System.StringComparison.Ordinal));
    }

    // The game data with governments.json changed by pairs of (old, new) text.
    private static GameContent WithData(params string[] replacements)
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim-governments-" + System.Environment.ProcessId + "-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "governments.json");
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
