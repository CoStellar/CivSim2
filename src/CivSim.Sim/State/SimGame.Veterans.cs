using CivSim.Core.Map;
using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Pick option <paramref name="Option"/> for a level our units of <paramref name="Kind"/> reached (veterans).</summary>
public sealed record ChooseVeteranCommand(int Civ, int Kind, int Option) : SimCommand(Civ);

/// <summary>
/// Army limit and veterans (Patryk, 2026-11-20, questions 9 and 11; data: armyLimit, veterans). A people may have as many army
/// units as its settlements, barracks, castles and ways of government allow. Experience belongs to a kind of unit: the enemies
/// its units kill raise the kind's level, and at each level the people picks one bonus for all units of that kind.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Army units a people may have.</summary>
    public int ArmyLimit(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArmyLimitRules r = Content.ArmyLimit;
        int limit = 0;
        foreach (int sid in civ.Settlements)
        {
            int level = Settlements[sid].Level;
            limit += r.PerLevel.Length == 0 ? 0 : r.PerLevel[Math.Min(level, r.PerLevel.Length - 1)];
        }

        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            if (b.State != BuildingState.Active)
            {
                continue;
            }

            foreach ((int building, int add) in r.PerBuilding)
            {
                limit += b.Type == building ? add : 0;
            }
        }

        foreach ((int tech, int add) in r.PerTech)
        {
            limit += civ.KnownTech[tech] ? add : 0;
        }

        return limit;
    }

    /// <summary>Army units a people has, those in training included.</summary>
    public int ArmyUnits(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int n = 0;
        foreach (int id in civ.Units)
        {
            Unit u = Units[id];
            n += u.State != UnitState.Gone && Content.Units[u.Kind].Army && !Content.Units[u.Kind].Levy ? 1 : 0;
        }

        return n;
    }

    /// <summary>The kills a kind of unit needs for its next level (0 = all levels reached).</summary>
    public int NextLevelKills(Civ civ, int kind)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int[] levels = Content.Veterans.Levels;
        return civ.KindLevel[kind] < levels.Length ? levels[civ.KindLevel[kind]] : 0;
    }

    private int VetSum(Unit u, Func<VeteranOption, int> of)
    {
        VeteranOption[] options = Content.Veterans.Options;
        if (options.Length == 0)
        {
            return 0;
        }

        Civ civ = Civs[u.Civ];
        int sum = 0;
        for (int o = 0; o < options.Length; o++)
        {
            sum += civ.KindPicks[(u.Kind * options.Length) + o] * of(options[o]);
        }

        return sum;
    }

    // Percent more damage the unit's kind has earned.
    private int VetStrength(Unit u) => VetSum(u, o => o.StrengthPercent);

    // Percent of damage taken (negative: less) the unit's kind has earned.
    private int VetTaken(Unit u) => Math.Max(-90, VetSum(u, o => o.TakenPercent));

    // Percent faster walking the unit's kind has earned.
    private int VetSpeed(Unit u) => VetSum(u, o => o.SpeedPercent);

    // Kills of units of a kind raise its level; each level waits for a choice (the AI chooses at once).
    // 14C (Patryk, 2026-11-20): the killer's kind and every other kind of its people fighting near the fallen get the kills, once each.
    private void CreditNear(int civId, int kind, int cell, int kills)
    {
        Credit(civId, kind, kills);
        int r = Content.Veterans.ShareNearHexes;
        if (r <= 0 || civId < 0)
        {
            return;
        }

        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(r)];
        Span<bool> done = stackalloc bool[Content.Units.Count];
        if ((uint)kind < (uint)done.Length)
        {
            done[kind] = true;
        }

        int n = Geo.Range(Geo.FromIndex(cell), r, area);
        for (int k = 0; k < n; k++)
        {
            foreach (int id in UnitsAt(Geo.Index(area[k])))
            {
                Unit o = Units[id];
                if (o.Civ == civId && o.Fighting && o.State != UnitState.Gone && !done[o.Kind])
                {
                    done[o.Kind] = true;
                    Credit(civId, o.Kind, kills);
                }
            }
        }
    }

    private void Credit(int civId, int kind, int kills)
    {
        if (civId < 0 || kind < 0 || kills <= 0 || Content.Veterans.Levels.Length == 0 || !Content.Units[kind].Army)
        {
            return;
        }

        Civ civ = Civs[civId];
        civ.KindKills[kind] += kills;
        int[] levels = Content.Veterans.Levels;
        while (civ.KindLevel[kind] < levels.Length && civ.KindKills[kind] >= levels[civ.KindLevel[kind]])
        {
            civ.KindLevel[kind]++;
            civ.KindPending[kind]++;
            if (civ.IsAi)
            {
                Pick(civ, kind, Ai.SimAi.VeteranChoiceFor(this, civ, kind));
            }
            else
            {
                News.Add((civ.Id, $"Our {Content.Units[kind].Name.ToLowerInvariant()} reached level {civ.KindLevel[kind]}: choose what they learn (select one of them)."));
            }
        }
    }

    private string? ChooseVeteran(Civ civ, int kind, int option)
    {
        if ((uint)kind >= (uint)Content.Units.Count || (uint)option >= (uint)Content.Veterans.Options.Length)
        {
            return "Unknown kind or choice.";
        }

        if (civ.KindPending[kind] <= 0)
        {
            return $"Our {Content.Units[kind].Name.ToLowerInvariant()} have no level waiting for a choice.";
        }

        Pick(civ, kind, option);
        return null;
    }

    private void Pick(Civ civ, int kind, int option)
    {
        civ.KindPending[kind]--;
        civ.KindPicks[(kind * Content.Veterans.Options.Length) + option]++;
        News.Add((civ.Id, $"Our {Content.Units[kind].Name.ToLowerInvariant()} learned: {Content.Veterans.Options[option].Name} ({Content.Veterans.Options[option].Text})."));
    }
}
