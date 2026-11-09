using System;
using System.Collections.Generic;
using System.Text;
using CivSim.Core.Random;

namespace CivSim.Core.Naming;

/// <summary>Rules of the peoples' languages, from <c>data/names.json</c> (GDD 5.6).</summary>
public sealed record NameRules
{
    /// <summary>Hard and soft consonants (also clusters).</summary>
    public (IReadOnlyList<string> Hard, IReadOnlyList<string> Soft) Consonants { get; init; } =
        (["k", "t", "p", "g", "d", "b", "r", "z", "kh", "dr", "tr", "kr"], ["l", "m", "n", "s", "v", "w", "y", "f", "h", "sh", "th", "r"]);

    /// <summary>Hard and soft vowels.</summary>
    public (IReadOnlyList<string> Hard, IReadOnlyList<string> Soft) Vowels { get; init; } = (["a", "u", "o", "e", "i"], ["a", "e", "i", "o", "ae", "ia", "ei"]);

    /// <summary>Hard and soft syllable codas.</summary>
    public (IReadOnlyList<string> Hard, IReadOnlyList<string> Soft) Codas { get; init; } = (["k", "t", "r", "rk", "n", "g"], ["n", "l", "s", "m", "r", "th"]);

    /// <summary>Hard and soft endings of peoples' names.</summary>
    public (IReadOnlyList<string> Hard, IReadOnlyList<string> Soft) PeopleEndings { get; init; } = (["ut", "ak", "or", "ek"], ["i", "awi", "ae", "ia"]);

    /// <summary>Hard and soft endings of place names.</summary>
    public (IReadOnlyList<string> Hard, IReadOnlyList<string> Soft) PlaceEndings { get; init; } = (["ok", "an", "du", "rak"], ["ae", "en", "is", "wen"]);

    /// <summary>Syllable patterns (C consonant, V vowel, K coda) with weights.</summary>
    public IReadOnlyList<(string Pattern, int Weight)> Patterns { get; init; } = [("CV", 40), ("CVK", 25), ("V", 8), ("VK", 7), ("CVV", 10), ("CVCV", 10)];

    /// <summary>Consonants a language keeps, smallest and largest.</summary>
    public (int Min, int Max) LanguageConsonants { get; init; } = (7, 11);

    /// <summary>Vowels a language keeps.</summary>
    public (int Min, int Max) LanguageVowels { get; init; } = (3, 5);

    /// <summary>Codas a language keeps.</summary>
    public (int Min, int Max) LanguageCodas { get; init; } = (2, 5);

    /// <summary>People endings a language keeps.</summary>
    public (int Min, int Max) LanguagePeopleEndings { get; init; } = (1, 3);

    /// <summary>Place endings a language keeps.</summary>
    public (int Min, int Max) LanguagePlaceEndings { get; init; } = (2, 4);

    /// <summary>How often a language doubles a single consonant between vowels, percent.</summary>
    public (int Min, int Max) DoubledPercent { get; init; } = (0, 25);

    /// <summary>Chance that a people's name gets one of its endings, percent.</summary>
    public int PeopleEndingPercent { get; init; } = 70;

    /// <summary>Chance that a place name gets one of its endings, percent.</summary>
    public int PlaceEndingPercent { get; init; } = 55;

    /// <summary>Syllables of a people's name.</summary>
    public (int Min, int Max) PeopleSyllables { get; init; } = (2, 3);

    /// <summary>Syllables of a place name.</summary>
    public (int Min, int Max) PlaceSyllables { get; init; } = (1, 3);

    /// <summary>Letters of any name.</summary>
    public (int Min, int Max) Length { get; init; } = (3, 11);

    /// <summary>What a daughter language changes (secession, stage 4f): consonants, vowels, endings swapped.</summary>
    public (int Consonants, int Vowels, int Endings) Mutation { get; init; } = (2, 1, 1);
}

/// <summary>
/// One people's invented language (GDD 5.6): the sounds and endings it likes. Made only from a seed and the rules,
/// so it needs no storage and never uses the simulation's random streams; every name is a pure function of the
/// language and a number (the same seed always gives the same names).
/// </summary>
public sealed class NameLanguage
{
    private readonly NameRules _rules;
    private readonly List<string> _consonants;
    private readonly List<string> _vowels;
    private readonly List<string> _codas;
    private readonly List<string> _peopleEndings;
    private readonly List<string> _placeEndings;
    private readonly int[] _patternWeights;
    private readonly int _doubledPercent;

    private NameLanguage(NameRules rules, ulong seed, List<string> consonants, List<string> vowels, List<string> codas,
        List<string> peopleEndings, List<string> placeEndings, int[] patternWeights, int doubledPercent, int hardnessPercent)
    {
        _rules = rules;
        Seed = seed;
        _consonants = consonants;
        _vowels = vowels;
        _codas = codas;
        _peopleEndings = peopleEndings;
        _placeEndings = placeEndings;
        _patternWeights = patternWeights;
        _doubledPercent = doubledPercent;
        HardnessPercent = hardnessPercent;
    }

    /// <summary>The seed the language was made from.</summary>
    public ulong Seed { get; }

    /// <summary>0 = soft sounds only, 100 = hard sounds only.</summary>
    public int HardnessPercent { get; }

    /// <summary>The consonants the language uses (for tests and the docs).</summary>
    public IReadOnlyList<string> ConsonantSet => _consonants;

    /// <summary>The vowels the language uses.</summary>
    public IReadOnlyList<string> VowelSet => _vowels;

    /// <summary>Makes the language of <paramref name="seed"/>.</summary>
    public static NameLanguage Create(NameRules rules, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ulong s = seed;
        int hardness = Roll(ref s, 101);
        List<string> consonants = Pick(ref s, rules.Consonants, hardness, Between(ref s, rules.LanguageConsonants));
        List<string> vowels = Pick(ref s, rules.Vowels, hardness, Between(ref s, rules.LanguageVowels));
        KeepOneLong(vowels);
        List<string> codas = Pick(ref s, rules.Codas, hardness, Between(ref s, rules.LanguageCodas));
        List<string> people = Pick(ref s, rules.PeopleEndings, hardness, Between(ref s, rules.LanguagePeopleEndings));
        List<string> places = Pick(ref s, rules.PlaceEndings, hardness, Between(ref s, rules.LanguagePlaceEndings));
        var weights = new int[rules.Patterns.Count];
        for (int k = 0; k < weights.Length; k++)
        {
            // Each language leans on some patterns: weight × 50–150%.
            weights[k] = rules.Patterns[k].Weight * (50 + Roll(ref s, 101)) / 100;
        }

        int doubled = Between(ref s, rules.DoubledPercent);
        return new NameLanguage(rules, seed, consonants, vowels, codas, people, places, weights, doubled, hardness);
    }

    /// <summary>
    /// A daughter language (secession, stage 4f): the same language with a few consonants, vowels and endings swapped
    /// for others of the same hardness, so its names sound related to the parent's.
    /// </summary>
    public NameLanguage Mutate(ulong seed)
    {
        ulong s = SplitMix64.Combine(Seed, seed);
        var consonants = new List<string>(_consonants);
        var vowels = new List<string>(_vowels);
        var people = new List<string>(_peopleEndings);
        var places = new List<string>(_placeEndings);
        Swap(ref s, consonants, _rules.Consonants, HardnessPercent, _rules.Mutation.Consonants);
        Swap(ref s, vowels, _rules.Vowels, HardnessPercent, _rules.Mutation.Vowels);
        Swap(ref s, people, _rules.PeopleEndings, HardnessPercent, _rules.Mutation.Endings);
        Swap(ref s, places, _rules.PlaceEndings, HardnessPercent, _rules.Mutation.Endings);
        return new NameLanguage(_rules, s, consonants, vowels, [.. _codas], people, places, [.. _patternWeights], _doubledPercent, HardnessPercent);
    }

    /// <summary>
    /// The name of a daughter people (secession, stage 4f): the parent's name with its last sounds replaced by an
    /// ending of this (daughter) language, e.g. Trugaar → Trugeth, so the two names are visibly related.
    /// </summary>
    public string DaughterPeopleName(string parentName, ulong number)
    {
        ArgumentNullException.ThrowIfNull(parentName);
        ulong s = SplitMix64.Combine(Seed, number + 0x5EC5);
        string root = parentName.ToLowerInvariant();
        int keep = Math.Max(2, root.Length - 1 - Roll(ref s, 2));
        while (keep > 2 && IsVowel(root[keep - 1]))
        {
            keep--;
        }

        root = root[..keep];
        var endings = new List<string>(_peopleEndings);
        endings.AddRange(_vowels);
        int start = Roll(ref s, endings.Count);
        for (int k = 0; k < endings.Count; k++)
        {
            // The first ending that does not give back the parent's own name.
            var sb = new StringBuilder(root);
            Join(sb, endings[(start + k) % endings.Count]);
            string word = Clean(sb.ToString());
            word = char.ToUpperInvariant(word[0]) + word[1..];
            if (!string.Equals(word, parentName, StringComparison.OrdinalIgnoreCase))
            {
                return word;
            }
        }

        return parentName + "i";
    }

    /// <summary>The <paramref name="number"/>-th name for a people in this language.</summary>
    public string PeopleName(ulong number) => Word(number * 2, _rules.PeopleSyllables, _peopleEndings, _rules.PeopleEndingPercent);

    /// <summary>The <paramref name="number"/>-th place name in this language.</summary>
    public string PlaceName(ulong number) => Word((number * 2) + 1, _rules.PlaceSyllables, _placeEndings, _rules.PlaceEndingPercent);

    private string Word(ulong number, (int Min, int Max) syllables, List<string> endings, int endingPercent)
    {
        // A name that comes out too short or too long is rolled again from the next sub-seed (still deterministic).
        for (ulong attempt = 0; attempt < 32; attempt++)
        {
            ulong s = SplitMix64.Combine(SplitMix64.Combine(Seed, number), attempt);
            var sb = new StringBuilder();
            int count = Between(ref s, syllables);
            for (int k = 0; k < count; k++)
            {
                AppendSyllable(ref s, sb, first: k == 0);
            }

            if (endings.Count > 0 && Roll(ref s, 100) < endingPercent)
            {
                string ending = endings[Roll(ref s, endings.Count)];
                Join(sb, ending);
            }

            string word = Clean(sb.ToString());
            if (word.Length >= _rules.Length.Min && word.Length <= _rules.Length.Max)
            {
                return char.ToUpperInvariant(word[0]) + word[1..];
            }
        }

        return "Ana";
    }

    private void AppendSyllable(ref ulong s, StringBuilder sb, bool first)
    {
        string pattern = _rules.Patterns[WeightedIndex(ref s, _patternWeights)].Pattern;

        // A syllable that starts with a vowel only begins a word; inside a word it gets a consonant first, so vowels
        // never pile up across syllables ("Ougtreqais").
        if (!first && pattern.Length > 0 && pattern[0] == 'V')
        {
            pattern = "C" + pattern;
        }

        foreach (char p in pattern)
        {
            switch (p)
            {
                case 'C':
                    string c = _consonants[Roll(ref s, _consonants.Count)];
                    if (sb.Length == 0 && c.Length == 2 && (c[0] == c[1] || c[0] is 'l' or 'n' or 'm' or 's' && c[1] is not 'h' and not 'y'))
                    {
                        c = c[..1]; // no word starts with "ll", "ss" or "lm"
                    }

                    if (!first && c.Length == 1 && sb.Length > 0 && IsVowel(sb[^1]) && Roll(ref s, 100) < _doubledPercent)
                    {
                        c += c;
                    }

                    sb.Append(c);
                    break;
                case 'V':
                    AppendVowel(ref s, sb);
                    break;
                case 'K':
                    if (_codas.Count > 0)
                    {
                        sb.Append(_codas[Roll(ref s, _codas.Count)]);
                    }

                    break;
            }
        }
    }

    // After a vowel only a different single vowel may follow (a diphthong like "ai"), never a third one.
    private void AppendVowel(ref ulong s, StringBuilder sb)
    {
        int tail = 0;
        for (int i = sb.Length - 1; i >= 0 && IsVowel(sb[i]); i--)
        {
            tail++;
        }

        if (tail == 0)
        {
            sb.Append(_vowels[Roll(ref s, _vowels.Count)]);
            return;
        }

        if (tail >= 2)
        {
            return;
        }

        char last = sb[^1];
        var singles = _vowels.FindAll(v => v.Length == 1 && v[0] != last);
        if (singles.Count > 0)
        {
            sb.Append(singles[Roll(ref s, singles.Count)]);
        }
    }

    // Adds a part; when two vowels would pile up into three or more, the first vowel of the part is dropped.
    private static void Join(StringBuilder sb, string part)
    {
        int tail = 0;
        for (int i = sb.Length - 1; i >= 0 && IsVowel(sb[i]); i--)
        {
            tail++;
        }

        int start = 0;
        while (start < part.Length - 1 && IsVowel(part[start]) && tail + start >= 2)
        {
            start++;
        }

        sb.Append(part, start, part.Length - start);
    }

    // No letter three times in a row, no more than two consonants after a doubled one.
    private static string Clean(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (char ch in word)
        {
            if (sb.Length >= 2 && sb[^1] == ch && sb[^2] == ch)
            {
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    // A language keeps at most one two-letter vowel (diphthong), so its words do not drown in vowels.
    private static void KeepOneLong(List<string> vowels)
    {
        bool seen = false;
        for (int i = vowels.Count - 1; i >= 0; i--)
        {
            if (vowels[i].Length > 1)
            {
                if (seen && vowels.Count > 2)
                {
                    vowels.RemoveAt(i);
                }

                seen = true;
            }
        }

        if (!vowels.Exists(v => v.Length == 1))
        {
            vowels.Add("a");
        }
    }

    private static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static List<string> Pick(ref ulong s, (IReadOnlyList<string> Hard, IReadOnlyList<string> Soft) pools, int hardness, int count)
    {
        var chosen = new List<string>(count);
        for (int tries = 0; chosen.Count < count && tries < count * 20; tries++)
        {
            IReadOnlyList<string> pool = Roll(ref s, 100) < hardness ? pools.Hard : pools.Soft;
            if (pool.Count == 0)
            {
                continue;
            }

            string item = pool[Roll(ref s, pool.Count)];
            if (!chosen.Contains(item))
            {
                chosen.Add(item);
            }
        }

        return chosen;
    }

    private static void Swap(ref ulong s, List<string> list, (IReadOnlyList<string> Hard, IReadOnlyList<string> Soft) pools, int hardness, int times)
    {
        for (int t = 0; t < times && list.Count > 0; t++)
        {
            List<string> fresh = Pick(ref s, pools, hardness, 1);
            if (fresh.Count == 1 && !list.Contains(fresh[0]))
            {
                list[Roll(ref s, list.Count)] = fresh[0];
            }
        }
    }

    private static int WeightedIndex(ref ulong s, int[] weights)
    {
        int total = 0;
        foreach (int w in weights)
        {
            total += Math.Max(0, w);
        }

        int r = Roll(ref s, Math.Max(1, total));
        for (int k = 0; k < weights.Length; k++)
        {
            r -= Math.Max(0, weights[k]);
            if (r < 0)
            {
                return k;
            }
        }

        return 0;
    }

    private static int Between(ref ulong s, (int Min, int Max) range) => range.Min + Roll(ref s, Math.Max(1, range.Max - range.Min + 1));

    private static int Roll(ref ulong s, int n) => n <= 1 ? 0 : (int)(SplitMix64.Next(ref s) % (ulong)n);
}
