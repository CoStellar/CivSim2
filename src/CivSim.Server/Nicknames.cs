using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CivSim.Server;

/// <summary>Players' nicknames in the lobby and the game (MP3, MP4): plain text, short, and never two alike.</summary>
public static class Nicknames
{
    /// <summary>Longest nickname.</summary>
    public const int MaxLength = 24;

    /// <summary>Text without control characters, trimmed and cut to <paramref name="max"/> characters.</summary>
    public static string Clean(string? text, int max)
    {
        var sb = new StringBuilder();
        foreach (char ch in text ?? "")
        {
            if (!char.IsControl(ch))
            {
                sb.Append(ch);
            }
        }

        string s = sb.ToString().Trim();
        return s.Length > max ? s[..max].TrimEnd() : s;
    }

    /// <summary>
    /// R4-4: a nickname already taken gets a suffix nobody has ("Ania the Late"). Returns the name and how many characters at its
    /// end the game added (with the space before them), so the screens can show them in another colour.
    /// </summary>
    public static (string Name, int SuffixLength) Unique(string? asked, IEnumerable<string> taken, IReadOnlyList<string> suffixes)
    {
        ArgumentNullException.ThrowIfNull(suffixes);
        string name = Clean(asked, MaxLength) is { Length: > 0 } n ? n : "Player";
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(name))
        {
            return (name, 0);
        }

        string[] free = [.. suffixes.Where(s => !used.Contains(name + " " + s))];
        string full = free.Length > 0
            ? name + " " + free[RandomNumberGenerator.GetInt32(free.Length)] // outside the game: no determinism needed
            : name + " " + (used.Count + 1).ToString(CultureInfo.InvariantCulture);
        return (full, full.Length - name.Length);
    }
}
