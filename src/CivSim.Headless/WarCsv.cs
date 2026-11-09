using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CivSim.Core.Diplomacy;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>
/// Writes every war of a run (GDD 9.3; stage 4e) as CSV: a row when a war ends in peace (read from the truce, which keeps
/// the war's record) and, at the end of the run, a row for every war still going on.
/// </summary>
public sealed class WarCsv(TextWriter writer)
{
    /// <summary>Column names.</summary>
    public const string Header = "a,b,initiator,goal,goal_settlement,start_year,end_year,years,score_a,dead_a,dead_b,people_a,people_b,weariness_a,weariness_b,terms,giver,ransom_percent,campaigns,campaign_months,months";

    private readonly TextWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly HashSet<(int, int, long)> _written = [];
    private readonly Dictionary<(int, int), (long Since, int Initiator)> _wars = [];

    /// <summary>Writes the header.</summary>
    public void Start() => _writer.WriteLine(Header);

    /// <summary>Writes the wars that ended since the last call (call every turn: it also notes when wars began).</summary>
    public void Ended(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind == TreatyKind.Peace && t.PeopleA + t.PeopleB > 0 && _written.Add((t.A.Value, t.B.Value, t.SinceMonth)))
            {
                (long since, int initiator) = _wars.TryGetValue((t.A.Value, t.B.Value), out var w) ? w : (-1L, -1);
                Row(t, since, t.SinceMonth, initiator);
            }
        }

        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind == TreatyKind.War)
            {
                _wars[(t.A.Value, t.B.Value)] = (t.SinceMonth, t.Initiator.Value);
            }
        }
    }

    /// <summary>Writes the wars still going on at the end of the run.</summary>
    public void Ongoing(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind == TreatyKind.War)
            {
                Row(t, t.SinceMonth, -1, t.Initiator.Value);
            }
        }
    }

    private void Row(Treaty t, long startMonth, long endMonth, int initiator)
    {
        string goal = t.Goal.ToString().ToLowerInvariant();
        string terms = t.Kind == TreatyKind.War ? string.Empty : t.Terms.Kind.ToString().ToLowerInvariant();
        string giver = t.Kind == TreatyKind.War || t.Terms.Kind == PeaceTermsKind.White ? string.Empty : t.Terms.Giver.Value.ToString(CultureInfo.InvariantCulture);
        string start = startMonth >= 0 ? (startMonth / 12).ToString(CultureInfo.InvariantCulture) : string.Empty;
        string end = endMonth >= 0 ? (endMonth / 12).ToString(CultureInfo.InvariantCulture) : string.Empty;
        string years = startMonth >= 0 && endMonth >= 0 ? ((endMonth - startMonth) / 12).ToString(CultureInfo.InvariantCulture) : string.Empty;
        string months = startMonth >= 0 && endMonth >= 0 ? (endMonth - startMonth).ToString(CultureInfo.InvariantCulture) : string.Empty;
        _writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{t.A.Value},{t.B.Value},{initiator},{goal},{t.GoalSettlement},{start},{end},{years},{t.Score},{t.DeadA},{t.DeadB},{t.PeopleA},{t.PeopleB},{t.WearinessA},{t.WearinessB},{terms},{giver},{t.Terms.RansomPercent},{t.Campaigns},{t.CampaignMonths},{months}"));
    }
}
