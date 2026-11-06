using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Naming;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Naming;

/// <summary>Peoples' languages and names (GDD 5.6).</summary>
public class NameTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static GameState NewGame(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out WorldGenReport _);

    [Fact]
    public void TheSameSeedGivesTheSameLanguageAndNames()
    {
        NameLanguage a = NameLanguage.Create(Content.Names, Names.LanguageSeed(5, 2));
        NameLanguage b = NameLanguage.Create(Content.Names, Names.LanguageSeed(5, 2));
        Assert.Equal(a.PeopleName(0), b.PeopleName(0));
        Assert.Equal(Enumerable.Range(0, 20).Select(i => a.PlaceName((ulong)i)), Enumerable.Range(0, 20).Select(i => b.PlaceName((ulong)i)));
        Assert.Equal(a.ConsonantSet, b.ConsonantSet);
    }

    [Fact]
    public void NamesAreWordsOfLatinLettersWithinTheLengthLimits()
    {
        for (int civ = 0; civ < 32; civ++)
        {
            NameLanguage language = NameLanguage.Create(Content.Names, Names.LanguageSeed(11, civ));
            for (ulong n = 0; n < 40; n++)
            {
                foreach (string name in new[] { language.PeopleName(n), language.PlaceName(n) })
                {
                    Assert.InRange(name.Length, Content.Names.Length.Min, Content.Names.Length.Max);
                    Assert.True(char.IsUpper(name[0]), name);
                    Assert.All(name[1..], c => Assert.InRange(c, 'a', 'z'));
                    string lower = name.ToLowerInvariant();
                    for (int i = 2; i < lower.Length; i++)
                    {
                        Assert.False(lower[i] == lower[i - 1] && lower[i] == lower[i - 2], $"three same letters in {name}");
                    }
                }
            }
        }
    }

    [Fact]
    public void LanguagesDifferBetweenPeoples()
    {
        var hardness = new HashSet<int>();
        var people = new HashSet<string>();
        for (int civ = 0; civ < 12; civ++)
        {
            NameLanguage language = NameLanguage.Create(Content.Names, Names.LanguageSeed(3, civ));
            hardness.Add(language.HardnessPercent);
            people.Add(language.PeopleName(0));
        }

        Assert.True(hardness.Count >= 8);
        Assert.Equal(12, people.Count);
    }

    [Fact]
    public void ANewGameNamesEveryPeopleAndCampFromItsLanguage()
    {
        GameState state = NewGame(4);
        var all = new List<string>();
        foreach (CivilizationState civ in state.Civilizations)
        {
            Assert.DoesNotContain("Civilization", civ.Name, StringComparison.Ordinal);
            Assert.True(civ.NamesIssued >= 2, "the people's name and its first camp");
            all.Add(civ.Name);
        }

        foreach (SettlementState s in state.Settlements)
        {
            Assert.DoesNotContain("Camp", s.Name, StringComparison.Ordinal);
            all.Add(s.Name);
        }

        Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(NewGame(4)));
    }

    [Fact]
    public void SuggestionsAreFreeNamesOfTheSameLanguageAndChangeNothing()
    {
        GameState state = NewGame(6);
        CivilizationState civ = state.Civilizations[1];
        ulong hash = GameStateHash.Compute(state);
        List<string> first = Names.Suggest(Content.Names, state, civ, people: false, count: 6, page: 0);
        List<string> more = Names.Suggest(Content.Names, state, civ, people: false, count: 6, page: 1);
        Assert.Equal(6, first.Count);
        Assert.Equal(first, Names.Suggest(Content.Names, state, civ, people: false, count: 6, page: 0));
        Assert.Empty(first.Intersect(more));
        Assert.DoesNotContain(state.Settlements[1].Name, first);
        Assert.Equal(hash, GameStateHash.Compute(state));
        Assert.Equal(6, Names.Suggest(Content.Names, state, civ, people: true, count: 6, page: 0).Count);
    }

    [Fact]
    public void RenamingRefusesTakenNamesAndAcceptsFreeOnes()
    {
        GameState state = NewGame(8);
        SettlementState mine = state.Settlements.First(s => s.Owner == state.Civilizations[0].Id);
        SettlementState other = state.Settlements.First(s => s.Owner == state.Civilizations[1].Id);
        CivId owner = mine.Owner;

        Assert.False(new RenameSettlementCommand(mine.Id, other.Name).Validate(state, owner).Success);
        Assert.False(new RenameSettlementCommand(mine.Id, state.Civilizations[2].Name.ToUpperInvariant()).Validate(state, owner).Success);
        Assert.False(new RenameSettlementCommand(other.Id, "Anywhere").Validate(state, owner).Success);
        Assert.False(new RenameCivilizationCommand(state.Civilizations[1].Name).Validate(state, owner).Success);

        var rename = new RenameSettlementCommand(mine.Id, "  Harbour  ");
        Assert.True(rename.Validate(state, owner).Success);
        rename.Apply(state, owner);
        Assert.Equal("Harbour", mine.Name);
        Assert.True(new RenameCivilizationCommand(state.Civilizations[0].Name).Validate(state, owner).Success, "keeping one's own name is allowed");
    }

    [Fact]
    public void ADaughterPeopleSoundsRelated()
    {
        NameLanguage parent = NameLanguage.Create(Content.Names, Names.LanguageSeed(9, 0));
        string name = parent.PeopleName(0);
        NameLanguage daughter = parent.Mutate(1);
        string child = daughter.DaughterPeopleName(name, 0);
        Assert.NotEqual(name, child, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(name[..2], child[..2]);
        Assert.Equal(parent.HardnessPercent, daughter.HardnessPercent);
        Assert.NotEqual(parent.PlaceName(3), daughter.PlaceName(3));
    }

    [Fact]
    public void NamesDataMustUseLowerCaseLatinLetters()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-names-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            string names = File.ReadAllText(Path.Combine(ContentLoader.DefaultDirectory, "names.json"))
                .Replace("\"ut\", \"ak\"", "\"út\", \"ak\"", StringComparison.Ordinal)
                .Replace("\"CV\": 45", "\"CX\": 45", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(dir, "names.json"), names);
            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("'út'", StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("CX", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
