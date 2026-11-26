using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>
/// The pace report (stage 6 PR T, ADR 0026 rule 2): for every civilization of a run, the minute of the game (at x1) of each milestone,
/// its people after 5 minutes and its settlements after 15. One row per civilization; a batch puts the rows of all its seeds in one file.
/// </summary>
public sealed class MilestoneCsv
{
    /// <summary>Column names; minutes are empty when the milestone was not reached.</summary>
    public const string Header =
        "seed,civ,name,from_start,first_technology,first_village,era_1,five_settlements,first_contact,first_war," +
        "people_5min,settlements_15min,alive_end";

    private readonly ulong _seed;
    private readonly MilestoneTracker _tracker;
    private readonly int _perMinute;
    private readonly List<double[]> _minutes = [];
    private readonly List<long> _people5 = [];
    private readonly List<int> _settlements15 = [];
    private readonly HashSet<int> _fromStart = [];

    /// <summary>Starts watching a new game of <paramref name="content"/>.</summary>
    public MilestoneCsv(GameContent content, GameState state, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(state);
        _seed = seed;
        _tracker = new MilestoneTracker(content);
        _perMinute = _tracker.TicksPerMinute;
        _tracker.Prime(state);
        foreach (CivilizationState c in state.Civilizations)
        {
            _fromStart.Add(c.Id.Value);
        }
    }

    /// <summary>Checks the state after a tick (milestones once a second of the game, counts at minutes 5 and 15).</summary>
    public void AfterTick(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Turn % Math.Max(1, _perMinute / 60) == 0)
        {
            foreach (MilestoneReached m in _tracker.Check(state))
            {
                if (m.Kind == Milestone.NewEra && state.FindCivilization(m.Civ)?.Era != 1)
                {
                    continue;
                }

                double[] row = Row(m.Civ.Value);
                if (row[(int)m.Kind] < 0)
                {
                    row[(int)m.Kind] = (double)m.Tick / _perMinute;
                }
            }
        }

        if (state.Turn == 5L * _perMinute || state.Turn == 15L * _perMinute)
        {
            foreach (CivilizationState c in state.Civilizations)
            {
                Row(c.Id.Value);
                long people = 0;
                int settlements = 0;
                foreach (SettlementState s in state.Settlements)
                {
                    if (s.Owner == c.Id && s.IsActive)
                    {
                        settlements++;
                        foreach (int p in s.Population)
                        {
                            people += p;
                        }
                    }
                }

                if (state.Turn == 5L * _perMinute)
                {
                    _people5[c.Id.Value] = people;
                }
                else
                {
                    _settlements15[c.Id.Value] = settlements;
                }
            }
        }
    }

    /// <summary>The rows of this game, one per civilization in id order.</summary>
    public IReadOnlyList<string> Rows(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var rows = new List<string>();
        foreach (CivilizationState c in state.Civilizations)
        {
            double[] m = Row(c.Id.Value);
            rows.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"{_seed},{c.Id.Value},{c.Name},{(_fromStart.Contains(c.Id.Value) ? 1 : 0)},{Minute(m[0])},{Minute(m[1])},{Minute(m[2])},{Minute(m[3])},{Minute(m[4])},{Minute(m[5])}," +
                $"{Count(_people5[c.Id.Value])},{Count(_settlements15[c.Id.Value])},{(c.IsAlive ? 1 : 0)}"));
        }

        return rows;
    }

    private double[] Row(int civ)
    {
        while (_minutes.Count <= civ)
        {
            _minutes.Add([-1, -1, -1, -1, -1, -1]);
            _people5.Add(-1);
            _settlements15.Add(-1);
        }

        return _minutes[civ];
    }

    private static string Minute(double m) => m < 0 ? string.Empty : m.ToString("F1", CultureInfo.InvariantCulture);

    private static string Count(long n) => n < 0 ? string.Empty : n.ToString(CultureInfo.InvariantCulture);
}
