using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>Moments that show a civilization's rise (ADR 0026 rules 2 and 12, stage 6 PR P2b).</summary>
public enum Milestone
{
    /// <summary>Its first technology.</summary>
    FirstTechnology = 0,

    /// <summary>Its first settlement above the camp level (a village).</summary>
    FirstVillage = 1,

    /// <summary>It entered a new era (each era once).</summary>
    NewEra = 2,

    /// <summary>Five active settlements at the same time.</summary>
    FiveSettlements = 3,

    /// <summary>It met another civilization.</summary>
    FirstContact = 4,

    /// <summary>Its first war.</summary>
    FirstWar = 5,
}

/// <summary>A milestone reached: who, what, when (tick) and a line for the chronicle.</summary>
public readonly record struct MilestoneReached(CivId Civ, Milestone Kind, long Tick, string Text);

/// <summary>
/// Watches the state for milestones (stage 6 PR P2b). It only reads the state, so it changes nothing in the simulation or its hash;
/// it lives beside it (the server writes the lines into the chronicle with the minute of the game, the headless tool will report the
/// minutes). <see cref="Prime"/> marks what a loaded game has already reached, so a load does not announce it again.
/// </summary>
public sealed class MilestoneTracker
{
    private readonly GameContent _content;
    private readonly List<int> _reached = []; // bit per one-time milestone, per civilization id
    private readonly List<int> _era = [];
    private readonly List<MilestoneReached> _found = [];

    /// <summary>Creates a tracker for games of <paramref name="content"/>.</summary>
    public MilestoneTracker(GameContent content) => _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>Ticks in one minute of the game at x1 (10 ticks a second).</summary>
    public int TicksPerMinute => Math.Max(1, _content.Time.TicksPerSecond * 60);

    /// <summary>Marks everything <paramref name="state"/> has already reached, without reporting it.</summary>
    public void Prime(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Check(state);
        _found.Clear();
    }

    /// <summary>The milestones reached since the last call (in civilization id order, then by kind); the list is reused.</summary>
    public IReadOnlyList<MilestoneReached> Check(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _found.Clear();
        foreach (CivilizationState civ in state.Civilizations)
        {
            int id = civ.Id.Value;
            while (_reached.Count <= id)
            {
                _reached.Add(0);
                _era.Add(0);
            }

            if (!civ.IsAlive)
            {
                continue;
            }

            if (Missing(id, Milestone.FirstTechnology) && FirstKnown(civ) is int tech)
            {
                string name = tech < _content.Technologies.Count ? _content.Technologies[tech].Name : "a technology";
                Add(state, civ, Milestone.FirstTechnology, $"learned their first technology: {name}");
            }

            int active = 0;
            bool village = false;
            foreach (SettlementState s in state.Settlements)
            {
                if (s.Owner == civ.Id && s.IsActive)
                {
                    active++;
                    village |= s.Level > 0;
                }
            }

            if (Missing(id, Milestone.FirstVillage) && village)
            {
                Add(state, civ, Milestone.FirstVillage, "built their first village");
            }

            if (Missing(id, Milestone.FiveSettlements) && active >= 5)
            {
                Add(state, civ, Milestone.FiveSettlements, "live in five settlements");
            }

            if (Missing(id, Milestone.FirstContact) && civ.Relations.Exists(r => r.HasMet))
            {
                Add(state, civ, Milestone.FirstContact, "met another people");
            }

            if (Missing(id, Milestone.FirstWar) && state.Treaties.AtWarWithAnyone(civ.Id))
            {
                Add(state, civ, Milestone.FirstWar, "went to war for the first time");
            }

            if (civ.Era > _era[id])
            {
                _era[id] = civ.Era;
                string era = civ.Era < _content.Eras.Count ? _content.Eras[civ.Era].Name : $"era {civ.Era + 1}";
                _found.Add(new MilestoneReached(civ.Id, Milestone.NewEra, state.Turn, Line(state, civ, $"entered the {era}")));
            }
        }

        return _found;
    }

    private bool Missing(int civ, Milestone m) => (_reached[civ] & (1 << (int)m)) == 0;

    private void Add(GameState state, CivilizationState civ, Milestone m, string what)
    {
        _reached[civ.Id.Value] |= 1 << (int)m;
        _found.Add(new MilestoneReached(civ.Id, m, state.Turn, Line(state, civ, what)));
    }

    private string Line(GameState state, CivilizationState civ, string what) =>
        $"Minute {(state.Turn / TicksPerMinute) + 1}: the {civ.Name} {what}.";

    private static int? FirstKnown(CivilizationState civ)
    {
        for (int t = 0; t < civ.KnownTechnologies.Length; t++)
        {
            if (civ.KnownTechnologies[t])
            {
                return t;
            }
        }

        return null;
    }
}
