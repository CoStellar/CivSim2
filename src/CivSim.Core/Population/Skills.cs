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
}
