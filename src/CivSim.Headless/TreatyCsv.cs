using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CivSim.Core.Diplomacy;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>Writes the treaties and wars in force (GDD 9.1; stage 4d) as CSV, one row per sample.</summary>
public static class TreatyCsv
{
    /// <summary>Column names.</summary>
    public const string Header = "turn,year,wars,truces,trade,non_aggression,defensive_alliance,pending,declined,trade_worth_last_turn";

    /// <summary>Writes one row with the counts of the moment.</summary>
    public static void Write(TextWriter writer, GameState state)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(state);
        var count = new int[5];
        long worth = 0;
        foreach (Treaty t in state.Treaties.All)
        {
            count[(int)t.Kind]++;
            worth += t.Kind == TreatyKind.Trade ? t.LastWorth : 0;
        }

        int pending = 0;
        int declined = 0;
        foreach (TreatyProposal p in state.Treaties.Proposals)
        {
            pending += p.Status == ProposalStatus.Pending ? 1 : 0;
            declined += p.Status == ProposalStatus.Declined ? 1 : 0;
        }

        writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{state.Turn},{state.Month / 12},{count[0]},{count[1]},{count[2]},{count[3]},{count[4]},{pending},{declined},{worth / 100}"));
    }
}

/// <summary>Counts diplomatic events of a run from the chronicle, and what is in force at the end (stage 4d).</summary>
public sealed class TreatyTally
{
    /// <summary>Treaties signed by kind (index = <see cref="TreatyKind"/>; peace = wars ended).</summary>
    public int[] Signed { get; } = new int[5];

    /// <summary>Treaties and wars in force at the end, by kind.</summary>
    public int[] InForce { get; } = new int[5];

    /// <summary>Wars declared (allies joining not counted).</summary>
    public int Wars { get; private set; }

    /// <summary>Declarations of war that broke a treaty.</summary>
    public int Broken { get; private set; }

    /// <summary>Allies that joined a defensive war.</summary>
    public int AlliesJoined { get; private set; }

    /// <summary>Allies that let their ally down.</summary>
    public int AlliesFailed { get; private set; }

    /// <summary>Epidemics merchants carried to a trade partner.</summary>
    public int Epidemics { get; private set; }

    /// <summary>Worth of all goods traded between civilizations, hundredths (sum over the treaties at the end).</summary>
    public long TotalWorth { get; private set; }

    /// <summary>Counts the lines of one turn's chronicle.</summary>
    public void Count(IReadOnlyList<string> chronicle)
    {
        ArgumentNullException.ThrowIfNull(chronicle);
        foreach (string line in chronicle)
        {
            Signed[2] += line.EndsWith(" agreed to trade.", StringComparison.Ordinal) ? 1 : 0;
            Signed[3] += line.EndsWith(" swore not to attack each other.", StringComparison.Ordinal) ? 1 : 0;
            Signed[4] += line.EndsWith(" formed a defensive alliance.", StringComparison.Ordinal) ? 1 : 0;
            Signed[1] += line.Contains(" made peace after ", StringComparison.Ordinal) ? 1 : 0;
            Epidemics += line.StartsWith("Merchants carried the ", StringComparison.Ordinal) ? 1 : 0;
            if (line.Contains(" declared war on ", StringComparison.Ordinal))
            {
                Wars++;
                Broken += line.Contains(", breaking their treaty", StringComparison.Ordinal) ? 1 : 0;
                AlliesJoined += Occurrences(line, " joined the war at the side of its ally.");
                AlliesFailed += Occurrences(line, " let its ally down.");
            }
        }
    }

    /// <summary>Records what is in force at the end and returns this tally.</summary>
    public TreatyTally Finish(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (Treaty t in state.Treaties.All)
        {
            InForce[(int)t.Kind]++;
            TotalWorth += t.TotalWorth;
        }

        return this;
    }

    private static int Occurrences(string text, string part)
    {
        int n = 0;
        for (int at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            n++;
        }

        return n;
    }
}
