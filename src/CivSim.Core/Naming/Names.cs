using System;
using System.Collections.Generic;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Naming;

/// <summary>
/// Names of peoples and settlements from their languages (GDD 5.6). Every name comes from the civilization's
/// language seed and its running name counter, never from the simulation's random streams, so names change nothing
/// else in the game. A name already taken by a civilization or a settlement is skipped.
/// </summary>
public static class Names
{
    // Suggestions for renaming come from numbers far above any counter, so offering them changes no state.
    private const ulong SuggestionBase = 1UL << 40;

    /// <summary>The language seed of civilization <paramref name="civ"/> in a world made from <paramref name="worldSeed"/>.</summary>
    public static ulong LanguageSeed(ulong worldSeed, int civ) => SplitMix64.Combine(SplitMix64.Combine(worldSeed, 0x4E414D4553UL), (ulong)civ);

    /// <summary>The language of a civilization.</summary>
    public static NameLanguage Language(NameRules rules, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        NameLanguage language = NameLanguage.Create(rules, civ.LanguageSeed);
        foreach (ulong mutation in civ.LanguageMutations)
        {
            language = language.Mutate(mutation);
        }

        return language;
    }

    /// <summary>Gives every civilization a people's name from its own language (new game).</summary>
    public static void NamePeoples(NameRules rules, GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (CivilizationState civ in state.Civilizations)
        {
            civ.Name = Next(rules, state, civ, people: true);
        }
    }

    /// <summary>
    /// The people's name of <paramref name="daughter"/>, which broke away from a people called <paramref name="parentName"/>
    /// (stage 4f PR 4): the parent's name with an ending of the daughter language, the first that is free.
    /// </summary>
    public static string DaughterPeople(NameRules rules, GameState state, CivilizationState daughter, string parentName)
    {
        ArgumentNullException.ThrowIfNull(state);
        NameLanguage language = Language(rules, daughter);
        HashSet<string> used = Used(state);
        for (ulong number = 0; number < 50; number++)
        {
            string name = language.DaughterPeopleName(parentName, number);
            if (!used.Contains(name))
            {
                return name;
            }
        }

        return Next(rules, state, daughter, people: true);
    }

    /// <summary>The next free place name in the language of <paramref name="civ"/> (advances its counter).</summary>
    public static string NextPlace(NameRules rules, GameState state, CivilizationState civ) => Next(rules, state, civ, people: false);

    /// <summary>
    /// Other names in the same language for renaming: <paramref name="count"/> free names, page <paramref name="page"/>
    /// (0, 1, 2... for "more"). Reads the state only.
    /// </summary>
    public static List<string> Suggest(NameRules rules, GameState state, CivilizationState civ, bool people, int count, int page)
    {
        ArgumentNullException.ThrowIfNull(state);
        NameLanguage language = Language(rules, civ);
        HashSet<string> used = Used(state);
        var list = new List<string>(count);
        ulong number = SuggestionBase + ((ulong)Math.Max(0, page) * (ulong)Math.Max(1, count) * 4);
        for (int tries = 0; list.Count < count && tries < count * 40; tries++, number++)
        {
            string name = people ? language.PeopleName(number) : language.PlaceName(number);
            if (!used.Contains(name) && !list.Contains(name))
            {
                list.Add(name);
            }
        }

        return list;
    }

    /// <summary>Every name in use (civilizations and settlements), compared without case.</summary>
    public static HashSet<string> Used(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CivilizationState c in state.Civilizations)
        {
            used.Add(c.Name);
        }

        foreach (SettlementState s in state.Settlements)
        {
            used.Add(s.Name);
        }

        return used;
    }

    private static string Next(NameRules rules, GameState state, CivilizationState civ, bool people)
    {
        NameLanguage language = Language(rules, civ);
        HashSet<string> used = Used(state);
        for (int tries = 0; tries < 200; tries++)
        {
            long number = civ.NamesIssued++;
            string name = people ? language.PeopleName((ulong)number) : language.PlaceName((ulong)number);
            if (!used.Contains(name))
            {
                return name;
            }
        }

        return $"{civ.Name} {civ.NamesIssued}";
    }
}
