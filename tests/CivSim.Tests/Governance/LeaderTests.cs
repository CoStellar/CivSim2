using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Content;
using CivSim.Core.Governance;
using CivSim.Core.Random;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Governance;

/// <summary>Leaders: names, traits in the character, death and succession by government (GDD 7.5; stage 4f PR 2).</summary>
public class LeaderTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Gov(string key) => Content.Governments.Single(g => g.Key == key).Id;

    private static int Trait(string key) => Content.Leaders.Traits.Single(t => t.Key == key).Id;

    [Fact]
    public void EveryPeopleStartsWithALeaderNamedInItsLanguage()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(11), Content, out _);
        foreach (CivilizationState c in state.Civilizations)
        {
            Assert.NotEmpty(c.LeaderName);
            Assert.InRange(Leaders.Age(c, state.Month), Content.Leaders.StartAge.Min, Content.Leaders.StartAge.Max);
            Assert.InRange(c.LeaderTraits.Length, Content.Leaders.TraitCount.Min, Content.Leaders.TraitCount.Max);
            Assert.Equal(c.LeaderTraits.Length, c.LeaderTraits.Distinct().Count());
            Assert.Equal(1, c.Leaders);
        }

        CivilizationState first = state.Civilizations[0];
        Assert.StartsWith(Content.Governments[first.Government].LeaderTitle + " " + first.LeaderName + ", ", Leaders.Describe(Content, state, first), System.StringComparison.Ordinal);

        // The same seed gives the same leaders.
        GameState again = GameState.CreateNew(GameSettings.Test(11), Content, out _);
        Assert.Equal(state.Civilizations.Select(c => c.LeaderName), again.Civilizations.Select(c => c.LeaderName));
    }

    [Fact]
    public void TheLeadersTraitsShiftTheCharacterWhileTheyRule()
    {
        var civ = new CivilizationState(new CivId(0), "Karrut", PlayerId.Ai) { Traits = new int[Character.TraitCount] };
        civ.Traits[(int)Core.Ai.Trait.Aggression] = 500;
        civ.Traits[(int)Core.Ai.Trait.Openness] = 100;
        civ.LeaderTraits = [Trait("conqueror"), Trait("paranoid")];
        civ.LeaderShift = Leaders.ShiftOf(Content.Leaders, civ.LeaderTraits);
        Assert.Equal(700, civ.Trait(Core.Ai.Trait.Aggression));
        Assert.Equal(0, civ.Trait(Core.Ai.Trait.Openness)); // kept within 0..1000
        Assert.Equal(200, civ.Trait(Core.Ai.Trait.Vengefulness));
        Assert.Equal(500, civ.OwnTrait(Core.Ai.Trait.Aggression));
        Assert.Equal(1200, Character.Multiplier(civ, Goal.Conquest));
    }

    [Fact]
    public void OldLeadersDieAndAreFollowed()
    {
        Assert.Equal(10, Content.Leaders.DeathPerMille(20));
        Assert.Equal(15, Content.Leaders.DeathPerMille(30));
        Assert.Equal(500, Content.Leaders.DeathPerMille(95));

        GameState state = GameState.CreateNew(GameSettings.Test(12), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Gov("elder_council");
        string name = civ.LeaderName;
        civ.LeaderBornMonth = state.Month - (90 * 12);
        var lines = new List<string>();
        Leaders.Live(Content, state, civ, state.Month, 600, new DeterministicRandom(5), lines.Add);
        Assert.NotEqual(name, civ.LeaderName);
        Assert.True(civ.Leaders >= 2);
        Assert.StartsWith($"Elder {name} of Council of {civ.Name} died at ", lines[0], System.StringComparison.Ordinal);
        Assert.InRange(Leaders.Age(civ, civ.LeaderSinceMonth), 40, 60); // a council picks an elder
        Assert.True(civ.CrisisUntilMonth == 0, "a council has no succession crisis");
    }

    [Fact]
    public void AChiefsDeathBringsACrisisAndACouncilPicksAFittingLeader()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(13), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Gov("chiefdom");
        civ.LeaderBornMonth = state.Month - (95 * 12);
        var lines = new List<string>();
        var random = new DeterministicRandom(9);
        while (civ.Leaders < 2)
        {
            Leaders.Live(Content, state, civ, state.Month, 12, random, lines.Add);
        }

        Assert.True(civ.CrisisUntilMonth > state.Month, "a chief's death brings a struggle for power");
        Assert.EndsWith("after a struggle for power.", lines.Single(), System.StringComparison.Ordinal);

        // A council weighs candidates: a curious people gets scholars more often than chance.
        civ.Government = Gov("elder_council");
        civ.Traits = Enumerable.Repeat(500, Character.TraitCount).ToArray();
        civ.Traits[(int)Core.Ai.Trait.Curiosity] = 1000;
        int scholars = 0;
        int successions = 0;
        for (int k = 0; k < 200; k++)
        {
            civ.LeaderBornMonth = state.Month - (100 * 12);
            long before = civ.Leaders;
            Leaders.Live(Content, state, civ, state.Month, 12, random, null);
            if (civ.Leaders > before)
            {
                successions++;
                scholars += civ.LeaderTraits.Contains(Trait("scholar")) ? 1 : 0;
            }
        }

        // By chance a leader has the scholar trait in about 42% of cases; a council of 3 candidates finds one in about 80%.
        Assert.True(scholars * 100 > successions * 65, $"scholars {scholars} of {successions}");
    }

    [Fact]
    public void AnElectedLeaderRulesForATerm()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(14), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Gov("city_republic");
        civ.LeaderBornMonth = state.Month - (20 * 12); // young: unlikely to die
        civ.LeaderSinceMonth = state.Month;
        var lines = new List<string>();
        Leaders.Live(Content, state, civ, state.Month, 120, new DeterministicRandom(1), lines.Add);
        Assert.Contains(lines, l => l.StartsWith("The term of Consul ", System.StringComparison.Ordinal));
    }

    [Fact]
    public void LeadersSurviveASave()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(15), Content, out _);
        state.Civilizations[0].CrisisUntilMonth = 77;
        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(Core.Hashing.GameStateHash.Compute(state), Core.Hashing.GameStateHash.Compute(loaded));
        Assert.Equal(state.Civilizations[0].Trait(Core.Ai.Trait.Aggression), loaded.Civilizations[0].Trait(Core.Ai.Trait.Aggression));
    }

    [Fact]
    public void BadLeaderDataIsRejected()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim-leaders-" + System.Environment.ProcessId + "-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "leaders.json");
            File.WriteAllText(path, File.ReadAllText(path)
                .Replace("[50, 35], [60, 60]", "[60, 60], [50, 35]", System.StringComparison.Ordinal)
                .Replace("\"traitCount\": [2, 3]", "\"traitCount\": [2, 9]", System.StringComparison.Ordinal));
            string govs = Path.Combine(dir, "governments.json");
            File.WriteAllText(govs, File.ReadAllText(govs).Replace("\"succession\": \"strongest\"", "\"succession\": \"lottery\"", System.StringComparison.Ordinal));
            ContentException e = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(e.Errors, x => x.Contains("ages rising", System.StringComparison.Ordinal));
            Assert.Contains(e.Errors, x => x.Contains("more traits than there are", System.StringComparison.Ordinal));
            Assert.Contains(e.Errors, x => x.Contains("unknown succession 'lottery'", System.StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
