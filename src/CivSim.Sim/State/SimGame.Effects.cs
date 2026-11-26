using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

public sealed partial class SimGame
{
    /// <summary>
    /// Sum of the effects of the technologies a civilization knows that aim at <paramref name="key"/> (a building, unit or event
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

    /// <summary>A share of an event's harm after the civilization's resistances, percent (100 = full harm).</summary>
    /// <summary>True when the civilization knows <paramref name="tech"/> and every technology in <paramref name="also"/>.</summary>
    public static bool Knows(Civ civ, int tech, int[] also)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(also);
        if (tech >= 0 && !civ.KnownTech[tech])
        {
            return false;
        }

        foreach (int t in also)
        {
            if (!civ.KnownTech[t])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The technologies something needs, for the player: "Masonry and Code of laws".</summary>
    public string NeedText(int tech, int[] also)
    {
        ArgumentNullException.ThrowIfNull(also);
        var names = new System.Collections.Generic.List<string>();
        if (tech >= 0)
        {
            names.Add(Content.Techs[tech].Name);
        }

        foreach (int t in also)
        {
            names.Add(Content.Techs[t].Name);
        }

        return string.Join(" and ", names);
    }

    /// <summary>True when the people may build this kind of building.</summary>
    public static bool Knows(Civ civ, BuildingType type) => Knows(civ, type?.Tech ?? -1, type?.AlsoTechs ?? []);

    /// <summary>True when the people may train or send this kind of unit.</summary>
    public static bool Knows(Civ civ, UnitType type) => Knows(civ, type?.Tech ?? -1, type?.AlsoTechs ?? []);

    public int Harm(Civ civ, string eventKey) => Math.Max(0, 100 - Bonus(civ, EffectKind.Resist, eventKey));
}
