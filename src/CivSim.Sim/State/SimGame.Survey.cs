using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// What a people knows of the land (Patryk, 2026-11-19). On its own land it knows everything (its people live there).
/// Beyond it, seeing a hex shows only what lies on the surface (game, fish, wild horses). The rest is found by units that
/// survey, standing still: prospectors look at the rock within <c>survey.radius</c> hexes (stone, clay, flint, ores),
/// surveyors at the soil (fertility, wild grain, cattle, sheep). An ore is recognised from the era of the technology that
/// works it, even before that technology is known; a hex prospected in an earlier era must be looked at again.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>True when the people knows what deposit lies on the hex (if any).</summary>
    public bool KnowsDeposit(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int d = Map.Deposit[cell];
        if (d == 0 || !civ.Known[cell])
        {
            return false;
        }

        int kind = d < Content.DepositSurvey.Length ? Content.DepositSurvey[d] : 0;
        int era = d < Content.DepositEra.Length ? Content.DepositEra[d] : 0;
        bool own = OwnerAt[cell] == civ.Id;
        return kind switch
        {
            0 => true,
            2 => own || civ.SoilSeen[cell],
            _ => (own ? civ.Era : civ.RockSeen[cell] - 1) >= era,
        };
    }

    /// <summary>True when the people knows how fertile the hex is.</summary>
    public bool KnowsSoil(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.Known[cell] && (OwnerAt[cell] == civ.Id || civ.SoilSeen[cell]);
    }

    /// <summary>True when prospectors have looked at the hex in the people's present era (or it is our land).</summary>
    public bool RockLookedAt(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return OwnerAt[cell] == civ.Id || civ.RockSeen[cell] == civ.Era + 1;
    }

    /// <summary>Deposits a standing prospector or surveyor knows around it (for its panel): deposit id and hex.</summary>
    public List<(int Deposit, int Cell)> FoundAround(Unit u)
    {
        ArgumentNullException.ThrowIfNull(u);
        var list = new List<(int, int)>();
        int kind = Content.Units[u.Kind].Survey;
        if (kind == 0)
        {
            return list;
        }

        Civ civ = Civs[u.Civ];
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(Content.SurveyRadius)];
        int n = Geo.Range(Geo.FromIndex(u.Cell), Content.SurveyRadius, area);
        for (int k = 0; k < n; k++)
        {
            int cell = Geo.Index(area[k]);
            int d = Map.Deposit[cell];
            if (d != 0 && d < Content.DepositSurvey.Length && Content.DepositSurvey[d] == kind && KnowsDeposit(civ, cell))
            {
                list.Add((d, cell));
            }
        }

        return list;
    }

    // Once a second: every standing prospector and surveyor looks at the land around it.
    private void SurveyStep()
    {
        foreach (Civ civ in Civs)
        {
            if (civ.Dead)
            {
                continue;
            }

            foreach (int uid in civ.Units)
            {
                Unit u = Units[uid];
                int kind = Content.Units[u.Kind].Survey;
                if (kind == 0 || u.State != UnitState.Idle || u.Aboard >= 0)
                {
                    continue;
                }

                int at = (u.Cell * 16) + civ.Era;
                if (u.SurveyedAt == at)
                {
                    continue;
                }

                u.SurveyedAt = at;
                Survey(civ, u, kind);
            }
        }
    }

    private void Survey(Civ civ, Unit u, int kind)
    {
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(Content.SurveyRadius)];
        int n = Geo.Range(Geo.FromIndex(u.Cell), Content.SurveyRadius, area);
        byte era = (byte)Math.Min(255, civ.Era + 1);
        var found = new List<string>();
        for (int k = 0; k < n; k++)
        {
            int cell = Geo.Index(area[k]);
            if (!civ.Known[cell])
            {
                continue; // they look only at what they see (their sight is the survey radius)
            }

            bool before = KnowsDeposit(civ, cell);
            if (kind == 1)
            {
                if (civ.RockSeen[cell] >= era)
                {
                    continue;
                }

                civ.RockSeen[cell] = era;
            }
            else
            {
                if (civ.SoilSeen[cell])
                {
                    continue;
                }

                civ.SoilSeen[cell] = true;
            }

            civ.NewlyKnown.Add(Map.Chunks.ChunkOfIndex(cell));
            int d = Map.Deposit[cell];
            if (!before && KnowsDeposit(civ, cell))
            {
                string name = Content.World.Deposits[d - 1].Name.ToLowerInvariant();
                if (!found.Contains(name))
                {
                    found.Add(name);
                }
            }
        }

        if (found.Count > 0)
        {
            News.Add((civ.Id, $"Our {Content.Units[u.Kind].Name.ToLowerInvariant()} found {string.Join(", ", found)}."));
        }
    }

    /// <summary>True when the hex holds a deposit some mine works (a building on it blocks that mine; M2, 2026-11-19).</summary>
    public bool Mineable(int cell)
    {
        int d = Map.Deposit[cell];
        if (d == 0)
        {
            return false;
        }

        foreach (BuildingType b in Content.Buildings)
        {
            if (b.OrDeposit == d)
            {
                return true;
            }
        }

        return false;
    }
}
