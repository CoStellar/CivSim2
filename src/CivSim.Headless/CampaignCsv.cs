using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CivSim.Core.Episodes;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>
/// Writes every war campaign of a run (GDD 9.3; stage 4e PR 3) as CSV, one row when it ends: who, how many from how many
/// settlements, arms, food, hunger, carriers and losses. Reads the running episodes after every turn and keeps the last
/// look at each one; a campaign still out at the end of the run gets a row with an empty end year.
/// </summary>
public sealed class CampaignCsv(TextWriter writer)
{
    /// <summary>Column names.</summary>
    public const string Header = "id,civ,enemy,start_year,end_year,months,settlements,warriors,survivors,dead,arms_percent,morale_end,hungry_months,forays,convoys";

    private readonly TextWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly SortedDictionary<long, Row> _out = [];
    private readonly List<long> _gone = [];

    /// <summary>Writes the header.</summary>
    public void Start() => _writer.WriteLine(Header);

    /// <summary>Notes the campaigns out now and writes those that ended since the last call (call every turn).</summary>
    public void Seen(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (Row r in _out.Values)
        {
            r.Fresh = false;
        }

        foreach (Episode e in state.Episodes.All)
        {
            if (e.Kind == EpisodeKind.Campaign)
            {
                _out[e.Id] = new Row(e, state.Month) { Fresh = true };
            }
        }

        _gone.Clear();
        foreach ((long id, Row r) in _out)
        {
            if (!r.Fresh)
            {
                Write(r, state.Month);
                _gone.Add(id);
            }
        }

        foreach (long id in _gone)
        {
            _out.Remove(id);
        }
    }

    /// <summary>Writes the campaigns still out at the end of the run.</summary>
    public void Ongoing()
    {
        foreach (Row r in _out.Values)
        {
            Write(r, -1);
        }

        _out.Clear();
    }

    private void Write(Row r, long endMonth)
    {
        string end = endMonth >= 0 ? (endMonth / 12).ToString(CultureInfo.InvariantCulture) : string.Empty;
        long months = (endMonth >= 0 ? endMonth : r.LastMonth) - r.StartMonth;
        _writer.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{r.Id},{r.Civ},{r.Enemy},{r.StartMonth / 12},{end},{months},{r.Settlements},{r.Mustered},{r.Warriors},{r.Dead},{r.Arms},{r.Morale},{r.Hungry},{r.Forays},{r.Convoys}"));
    }

    private sealed class Row(Episode e, long month)
    {
        public long Id { get; } = e.Id;

        public int Civ { get; } = e.Civ.Value;

        public int Enemy { get; } = e.Other.Value;

        public long StartMonth { get; } = e.StartMonth;

        public long LastMonth { get; } = month;

        public int Settlements { get; } = e.Members.Length;

        public int Mustered { get; } = e.Mustered;

        public int Warriors { get; } = e.Warriors;

        public int Dead { get; } = e.Dead;

        public int Arms { get; } = e.ArmsPercent;

        public int Morale { get; } = e.Morale;

        public int Hungry { get; } = e.HungryMonths;

        public int Forays { get; } = e.Forays;

        public int Convoys { get; } = e.Convoys;

        public bool Fresh { get; set; }
    }
}
