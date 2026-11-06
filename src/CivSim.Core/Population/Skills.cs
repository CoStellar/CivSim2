using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.State;

namespace CivSim.Core.Population;

/// <summary>
/// Keeps the skill counts of a settlement within its people (stage 4F, ADR 0020): the skilled people and apprentices
/// of a stratum are never more than the stratum's people. Deaths, flight and splits take people from a stratum; this
/// takes the same share from its skills.
/// </summary>
public static class Skills
{
    /// <summary>
    /// Lowers the skilled people and apprentices of every stratum of <paramref name="s"/> to at most the stratum's
    /// people, in proportion (rounded up for the skills, so a skill is lost only when its last holder goes); apprentices
    /// go first. Deterministic, no allocation.
    /// </summary>
    public static void Trim(IReadOnlyList<SkillDefinition> skills, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(s);
        Span<int> skilled = s.Skilled;
        Span<int> trainees = s.Trainees;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int stratum = 0; stratum < people.Length; stratum++)
        {
            long trained = 0;
            long learning = 0;
            for (int k = 0; k < skilled.Length && k < skills.Count; k++)
            {
                if (skills[k].StratumId == stratum)
                {
                    trained += skilled[k];
                    learning += trainees[k];
                }
            }

            long room = people[stratum];
            if (trained + learning <= room)
            {
                continue;
            }

            // Apprentices give way first, then the skilled, each skill keeping its share.
            long keepLearning = Math.Max(0, room - trained);
            long keepTrained = Math.Min(trained, room);
            for (int k = 0; k < skilled.Length && k < skills.Count; k++)
            {
                if (skills[k].StratumId != stratum)
                {
                    continue;
                }

                trainees[k] = learning == 0 ? 0 : (int)(trainees[k] * keepLearning / learning);
                skilled[k] = trained == 0 ? 0 : (int)((skilled[k] * keepTrained + trained - 1) / trained);
            }

            // Rounding up may leave a few too many skilled: take them from the biggest skill, lowest id first.
            long over = -room;
            for (int k = 0; k < skilled.Length && k < skills.Count; k++)
            {
                over += skills[k].StratumId == stratum ? skilled[k] + trainees[k] : 0;
            }

            while (over > 0)
            {
                int biggest = -1;
                for (int k = 0; k < skilled.Length && k < skills.Count; k++)
                {
                    if (skills[k].StratumId == stratum && (biggest < 0 || skilled[k] > skilled[biggest]))
                    {
                        biggest = k;
                    }
                }

                int take = (int)Math.Min(over, skilled[biggest]);
                skilled[biggest] -= take;
                over -= take;
            }
        }
    }

    /// <summary>
    /// Work of the people of skill <paramref name="skill"/> in <paramref name="s"/>, in workers: the skilled plus the
    /// apprentices at their share (rounded down), never more than the skill's stratum.
    /// </summary>
    public static int Workers(GameContent content, SettlementState s, int skill)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        if (skill < 0 || skill >= s.SkilledView.Length || skill >= content.Skills.Count)
        {
            return 0;
        }

        long work = s.SkilledView[skill] + ((long)s.TraineesView[skill] * content.SkillRules.ApprenticeWorkPercent / 100);
        int stratum = content.Skills[skill].StratumId;
        int people = stratum >= 0 && stratum < s.PopulationView.Length ? s.PopulationView[stratum] : 0;
        return (int)Math.Min(work, people);
    }

    /// <summary>People of the skill's stratum the skill can use in <paramref name="s"/> at full work (at least 1).</summary>
    public static int Needed(GameContent content, SettlementState s, int skill)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        SkillDefinition k = content.Skills[skill];
        int people = k.StratumId >= 0 && k.StratumId < s.PopulationView.Length ? s.PopulationView[k.StratumId] : 0;
        if (content.SkillLinks.ForRecipe(s.RecipeId) == skill)
        {
            return Math.Max(1, people); // the settlement's recipe wants all its artisans
        }

        return Math.Max(1, (int)((long)people * k.JobsPercent / 100));
    }

    /// <summary>
    /// Share of the goods of the skill's technology that the settlement gathers, per mille: its workers against the
    /// people the skill can use there (1000 = all of them).
    /// </summary>
    public static int CoveragePerMille(GameContent content, SettlementState s, int skill) =>
        (int)Math.Min(1000L, Workers(content, s, skill) * 1000L / Needed(content, s, skill));

    /// <summary>
    /// The first masters of <paramref name="skill"/> for a civilization that just learned its technology: in its
    /// settlement on Earth with most people of the skill's stratum (ties: lowest id), as many as the free people of
    /// the stratum allow.
    /// </summary>
    public static SettlementState? Seed(GameContent content, GameState state, CivId civ, int skill, int masters)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        int stratum = content.Skills[skill].StratumId;
        SettlementState? best = null;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.Owner == civ && s.IsActive && s.World == WorldId.Earth && skill < s.SkilledView.Length && stratum < s.PopulationView.Length
                && (best is null || s.PopulationView[stratum] > best.PopulationView[stratum]))
            {
                best = s;
            }
        }

        if (best is null)
        {
            return null;
        }

        // Without free people of the stratum, the masters come from the biggest other skill of it (they retrain).
        int add = Math.Min(masters, best.PopulationView[stratum]);
        int missing = add - Free(content, best, stratum);
        while (missing > 0)
        {
            int from = -1;
            for (int k = 0; k < best.SkilledView.Length && k < content.Skills.Count; k++)
            {
                if (k != skill && content.Skills[k].StratumId == stratum && best.SkilledView[k] > 0
                    && (from < 0 || best.SkilledView[k] > best.SkilledView[from]))
                {
                    from = k;
                }
            }

            if (from < 0)
            {
                add -= missing; // only apprentices of this very skill are left: no room
                break;
            }

            int take = Math.Min(missing, best.SkilledView[from]);
            best.Skilled[from] -= take;
            missing -= take;
        }

        if (add <= 0)
        {
            return null;
        }

        best.Skilled[skill] += add;
        return best;
    }

    /// <summary>People of the stratum in <paramref name="s"/> who neither know nor learn any skill.</summary>
    public static int Free(GameContent content, SettlementState s, int stratum)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        if (stratum < 0 || stratum >= s.PopulationView.Length)
        {
            return 0;
        }

        long used = 0;
        for (int k = 0; k < s.SkilledView.Length && k < content.Skills.Count; k++)
        {
            used += content.Skills[k].StratumId == stratum ? s.SkilledView[k] + s.TraineesView[k] : 0;
        }

        return (int)Math.Max(0, s.PopulationView[stratum] - used);
    }

    /// <summary>
    /// Moves <paramref name="percent"/> of the skilled people and apprentices of <paramref name="from"/> to
    /// <paramref name="to"/> (rounded down, as a split takes its people); 100 moves them all.
    /// </summary>
    public static void Move(SettlementState from, SettlementState to, int percent)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        Span<int> skilled = from.Skilled;
        Span<int> trainees = from.Trainees;
        for (int k = 0; k < skilled.Length && k < to.SkilledView.Length; k++)
        {
            int ks = percent >= 100 ? skilled[k] : (int)((long)skilled[k] * percent / 100);
            int kt = percent >= 100 ? trainees[k] : (int)((long)trainees[k] * percent / 100);
            skilled[k] -= ks;
            trainees[k] -= kt;
            to.Skilled[k] += ks;
            to.Trainees[k] += kt;
        }
    }

    /// <summary>Forgets every skill of <paramref name="s"/> (its people are gone).</summary>
    public static void Clear(SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        s.Skilled.Clear();
        s.Trainees.Clear();
    }
}
