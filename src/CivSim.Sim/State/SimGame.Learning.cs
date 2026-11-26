using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Set the branch of research one worker of a school, academy or university speeds.</summary>
public sealed record SetFocusCommand(int Civ, int Building, int Worker, string Category) : SimCommand(Civ);

/// <summary>
/// Learning and support (decisions of 2026-11-18): buildings that make nothing work only with their workers. Every worker of a
/// school, academy or university speeds one branch of research the player chooses (a school has three); the speed-ups of a
/// branch add up, at most +50%. Builders' yards make building in their settlement faster, training halls the training of their
/// kind of unit.
/// </summary>
public sealed partial class SimGame
{

    private static readonly string[] FocusOrder = ["science", "industry", "food", "building", "military", "trade", "governance"];

    private static string[] DefaultFocus(int workers)
    {
        var f = new string[workers];
        for (int k = 0; k < workers; k++)
        {
            f[k] = FocusOrder[k % FocusOrder.Length];
        }

        return f;
    }

    /// <summary>How much faster research of a branch goes for the people, percent (schools, academies, universities).</summary>
    public int ResearchSpeed(Civ civ, string category)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int pct = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            BuildingType t = Content.Buildings[b.Type];
            if (t.ResearchPercent <= 0 || b.State != BuildingState.Active)
            {
                continue;
            }

            for (int k = 0; k < Math.Min(b.Workers, b.Focus.Length); k++)
            {
                if (b.Focus[k] == category)
                {
                    pct += t.ResearchPercent;
                }
            }
        }

        return Math.Min(Content.MaxResearchSupportPercent, pct);
    }

    /// <summary>Ticks a research of the technology takes the people now.</summary>
    public int ResearchTicks(Civ civ, int tech)
    {
        ArgumentNullException.ThrowIfNull(civ);
        SimTech t = Content.Techs[tech];
        return Math.Max(1, t.ResearchTicks * Content.Pace.Seconds(t.Era) / (100 + ResearchSpeed(civ, t.Category)));
    }

    // What the working support buildings of a settlement add of one kind (builders' yards: building speed), by jobs filled.
    private int SupportOf(Civ civ, int sid, Func<BuildingType, int> of)
    {
        int pct = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            BuildingType t = Content.Buildings[b.Type];
            int v = of(t);
            if (v > 0 && b.Settlement == sid && b.State == BuildingState.Active)
            {
                pct += v * b.Workers / Math.Max(1, t.Workers);
            }
        }

        return pct;
    }

    // Buildings whose training speed-up is for the units that need them (siege workshops, shipyards), not for all.
    private bool TrainsOwnKind(BuildingType bt)
    {
        foreach (UnitType u in Content.Units)
        {
            if (u.NeedsBuilding == bt.Key)
            {
                return true;
            }
        }

        return false;
    }

    private string? SetFocus(Civ civ, int id, int worker, string category)
    {
        if ((uint)id >= (uint)Buildings.Count || Buildings[id].Civ != civ.Id)
        {
            return "Not our building.";
        }

        Building b = Buildings[id];
        if (worker < 0 || worker >= b.Focus.Length)
        {
            return "This building does not speed research.";
        }

        if (Array.IndexOf(Content.TechCategories, category) < 0 || category == SimContent.BasicsCategory)
        {
            return "No such branch of research.";
        }

        b.Focus[worker] = category;
        return null;
    }
}
