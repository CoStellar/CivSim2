using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

public sealed partial class SimGame
{
    /// <summary>
    /// Sum of the effects of the technologies a people knows that aim at <paramref name="key"/> (a building, unit or event
    /// key), at one of its <paramref name="groups"/>, or at everything ("*"). Read when something is recomputed, never per tick.
    /// </summary>
    public int Bonus(Civ civ, EffectKind kind, string key, string[]? groups = null)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int sum = 0;
        foreach (SimTech t in Content.Techs)
        {
            if (!civ.KnownTech[t.Id] || t.Effects.Length == 0)
            {
                continue;
            }

            foreach (TechEffect e in t.Effects)
            {
                if (e.Kind == kind && (e.Target == "*" || e.Target == key || (groups is not null && Array.IndexOf(groups, e.Target) >= 0)))
                {
                    sum += e.Value;
                }
            }
        }

        return sum;
    }

    /// <summary>A share of an event's harm after the people's resistances, percent (100 = full harm).</summary>
    public int Harm(Civ civ, string eventKey) => Math.Max(0, 100 - Bonus(civ, EffectKind.Resist, eventKey));
}
