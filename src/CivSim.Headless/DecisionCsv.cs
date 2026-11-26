using System;
using System.Globalization;
using System.IO;
using System.Text;
using CivSim.Core.Ai.Utility;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>
/// Writes AI decisions with their three main reasons ("Why?", GDD 8.7) as CSV rows: one row per command an advisor
/// issued, so a whole game can be read in a spreadsheet.
/// </summary>
public static class DecisionCsv
{
    /// <summary>Column names.</summary>
    public const string Header = "turn,civ,advisor,action,settlement,target,score,why1,why2,why3";

    /// <summary>Writes every decision of <paramref name="log"/>.</summary>
    public static void Write(TextWriter writer, DecisionLog log, GameState state)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(state);
        var line = new StringBuilder();
        foreach (Decision d in log.Entries)
        {
            line.Clear();
            string civ = state.FindCivilization(d.Civ)?.Name ?? d.Civ.Value.ToString(CultureInfo.InvariantCulture);
            line.Append(CultureInfo.InvariantCulture, $"{d.Turn},{civ},{d.Advisor},{d.Action},{d.Subject},{d.Target},{d.Score}");
            line.Append(',').Append(Reason(d.First)).Append(',').Append(Reason(d.Second)).Append(',').Append(Reason(d.Third));
            writer.WriteLine(line.ToString());
        }
    }

    private static string Reason(Factor factor) => factor.Id == FactorId.None ? string.Empty : Decision.Describe(factor);
}
