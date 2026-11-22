using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Ai;

/// <summary>
/// The AI player (ADR 0028): it plays a people with the same commands as a person and sees only what its people has seen.
/// It thinks every <see cref="ThinkEveryTicks"/> ticks, staggered by people, and does at most a few things each time, so it
/// costs little (docs/wydajnosc.md). Its character (aggression, expansion) shows only in what it does.
/// </summary>
public static partial class SimAi
{
    /// <summary>Ticks between two thoughts of one AI people.</summary>
    public const int ThinkEveryTicks = 30;

    /// <summary>No AI starts a war before this tick (8 minutes): every people gets time to settle.</summary>

    private static readonly string[] FoodBuildings = ["farm", "fishing_hut", "gatherers_hut", "hunters_lodge", "pasture", "manor"];

    /// <summary>One thought: looks at the people's state and issues commands through <see cref="SimGame.Apply"/>.</summary>
    public static void Think(SimGame g, Civ civ)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(civ);
        if (civ.Dirty)
        {
            g.Recompute(civ);
        }

        int actions = 0;
        AnswerPeace(g, civ);
        Diplomacy(g, civ);
        Supply(g, civ);
        FieldSupply(g, civ);
        Research(g, civ);
        Hire(g, civ);
        bool lean = LeanTimes(g, civ); // winter close and too little food in store: food first, no settlers or soldiers
        actions += lean && WinterFood(g, civ) ? 1 : 0;
        actions += actions == 0 && Economy(g, civ) ? 1 : 0;
        actions += actions == 0 && Care(g, civ) ? 1 : 0;
        if (actions == 0)
        {
            actions += Upgrade(g, civ) ? 1 : 0;
        }

        if (actions == 0)
        {
            actions += Rebuild(g, civ) ? 1 : 0;
        }

        Works(g, civ);
        Scouts(g, civ);
        if (!lean)
        {
            Settlers(g, civ);
        }

        if (actions == 0 && !lean)
        {
            Army(g, civ);
        }

        War(g, civ);
        if (!lean && actions == 0)
        {
            Surveys(g, civ); // last: settlers and soldiers come first
        }
    }

    private static int Key(SimGame g, string key)
    {
        foreach (BuildingType b in g.Content.Buildings)
        {
            if (b.Key == key)
            {
                return b.Id;
            }
        }

        return -1;
    }

    // The price can be paid and leaves what is saved for the next technology (unless saving is ignored: food buildings).
    private static bool Affordable(Civ civ, long[] price, int keepPercent = 0, bool keepSaving = true)
    {
        long[]? saving = keepSaving ? civ.AiSaving : null;
        for (int i = 0; i < price.Length; i++)
        {
            long saved = saving is not null && price[i] > 0 ? saving[i] : 0;
            if (civ.Stock[i] - saved < price[i] * (100 + keepPercent) / 100)
            {
                return false;
            }
        }

        return true;
    }

    // Builds the building on the best free hex of any own settlement: a bonus deposit first, then the hex nearest the centre.
    private static bool TryBuild(SimGame g, Civ civ, string key, bool keepSaving = true)
    {
        int id = Key(g, key);
        if (id < 0)
        {
            return false;
        }

        BuildingType t = g.Content.Buildings[id];
        if (!SimGame.Knows(civ, t) || !Affordable(civ, t.Price, keepSaving: keepSaving && Array.IndexOf(FoodBuildings, t.Key) < 0))
        {
            return false;
        }

        // N24 (2026-11-19): one of its kind still being built comes first (no twins under construction), and a working
        // building goes only to a settlement with free hands (endless empty woodcutters).
        int idleAnywhere = 0;
        foreach (int sid in civ.Settlements)
        {
            idleAnywhere += g.Settlements[sid].Idle;
        }

        if (t.Workers > 0)
        {
            // A1 (2026-11-19 tests): a new workplace only when free jobs are fewer than idle hands; teachers and makers of
            // what research lacks (keepSaving off) and food for the hungry may still go first.
            int freeJobs = 0;
            foreach (int bid in civ.Buildings)
            {
                Building b = g.Buildings[bid];
                if (b.Type == id && b.State is BuildingState.Delivery or BuildingState.Construction)
                {
                    return false;
                }

                int jobs = g.Content.Buildings[b.Type].Workers;
                freeJobs += b.State is BuildingState.Active or BuildingState.Delivery or BuildingState.Construction ? Math.Max(0, jobs - b.Workers) : 0;
            }

            bool urgent = !keepSaving || (civ.Hungry && Array.IndexOf(FoodBuildings, t.Key) >= 0);
            if (!urgent && freeJobs >= idleAnywhere)
            {
                return false;
            }

            if (civ.People < g.Content.AiSmallPeople && freeJobs > 0)
            {
                return false; // 17B (Patryk, 2026-11-20): a small people fills the jobs it has first (8 huts for 9 people)
            }
        }

        int best = -1;
        int bestScore = int.MinValue;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (!SimGame.CanAfford(s.Stock, t.Price, 100))
            {
                continue; // goods lie in each settlement: build where the materials are
            }

            if (t.Workers > 0 && s.Idle == 0 && idleAnywhere > 0 && keepSaving)
            {
                continue; // nobody here would work in it (teachers go first anyway: their priority takes workers)
            }

            foreach (int cell in s.Cells)
            {
                if (g.CannotBuild(civ, cell, t, checkPrice: false) is not null)
                {
                    continue;
                }

                // M2 (2026-11-19): a known deposit is kept for its mine; only buildings that work it go there.
                if (t.OrDeposit == 0 && t.BonusDeposit != g.Map.Deposit[cell] && g.Map.Deposit[cell] != 0 && g.KnowsDeposit(civ, cell) && g.Mineable(cell))
                {
                    continue;
                }

                // Where hands are free: a working building goes where its workers live.
                int score = -g.Geo.Distance(g.Geo.FromIndex(cell), g.Geo.FromIndex(s.Center)) + (Math.Min(s.Idle, t.Workers) * 10) + InputsAtHand(s, t);
                if (t.BonusDeposit != 0 && g.Map.Deposit[cell] == t.BonusDeposit && g.KnowsDeposit(civ, cell))
                {
                    score += 20 * g.DepositSizePercent(cell) / 100;
                }

                if (t.OrDeposit != 0 && g.Map.Deposit[cell] == t.OrDeposit)
                {
                    score += g.DepositSizePercent(cell) / 5; // 2026-11-22: the larger deposit first (large +30, small +10)
                }

                if (t.BonusReliefs.Length > 0 && t.BonusReliefs[g.Map.Relief[cell]])
                {
                    score += 15;
                }

                if (score > bestScore)
                {
                    best = cell;
                    bestScore = score;
                }
            }
        }

        if (best < 0 && !keepSaving)
        {
            Fund(g, civ, t);
        }

        return best >= 0 && g.Apply(new BuildCommand(civ.Id, best, id)) is null;
    }

    // AI goods (2026-11-20): a building research waits for can stand only in a settlement that lacks its price (the copper
    // deposit lay in a young camp with no tools): caravans from the richest settlements bring what is missing, and the next
    // thought builds it.
    private static void Fund(SimGame g, Civ civ, BuildingType t)
    {
        if (civ.Settlements.Count < 2 || !SimGame.CanAfford(civ.Stock, t.Price, 100))
        {
            return;
        }

        Settlement? site = null;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            foreach (int cell in s.Cells)
            {
                if (g.CannotBuild(civ, cell, t, checkPrice: false) is null)
                {
                    site = s;
                    break;
                }
            }

            if (site is not null)
            {
                break;
            }
        }

        if (site is null)
        {
            return;
        }

        for (int i = 0; i < t.Price.Length; i++)
        {
            long missing = t.Price[i] - site.Stock[i];
            if (missing <= 0 || Carrying(g, civ, site.Id, i))
            {
                continue;
            }

            Settlement? rich = null;
            foreach (int o in civ.Settlements)
            {
                Settlement r = g.Settlements[o];
                if (r != site && r.Stock[i] >= missing && (rich is null || r.Stock[i] > rich.Stock[i]))
                {
                    rich = r;
                }
            }

            if (rich is not null)
            {
                g.Apply(new SendCaravanCommand(civ.Id, rich.Id, site.Id, i, missing + SimContent.One));
            }
        }
    }

    private static int Under(SimGame g, Civ civ)
    {
        int n = 0;
        foreach (int id in civ.Buildings)
        {
            if (g.Buildings[id].State is BuildingState.Delivery or BuildingState.Construction or BuildingState.ConstructionPaused)
            {
                n++;
            }
        }

        return n;
    }

    // True when the people has a building of this kind (built or being built).
    private static bool Has(SimGame g, Civ civ, string key)
    {
        foreach (int id in civ.Buildings)
        {
            if (g.Content.Buildings[g.Buildings[id].Type].Key == key)
            {
                return true;
            }
        }

        return false;
    }

    private static int Count(SimGame g, Civ civ, string[] keys)
    {
        int n = 0;
        foreach (int id in civ.Buildings)
        {
            n += Array.IndexOf(keys, g.Content.Buildings[g.Buildings[id].Type].Key) >= 0 ? 1 : 0;
        }

        return n;
    }

    // Buildings of these kinds being built, counted up to `upTo`.
    private static int Building(SimGame g, Civ civ, string[] keys, int upTo)
    {
        int n = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            if (b.State is BuildingState.Delivery or BuildingState.Construction or BuildingState.ConstructionPaused && Array.IndexOf(keys, g.Content.Buildings[b.Type].Key) >= 0 && ++n >= upTo)
            {
                break;
            }
        }

        return n;
    }

    // True when a building of one of these kinds is being built.
    private static bool Building(SimGame g, Civ civ, string[] keys)
    {
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            if (b.State is BuildingState.Delivery or BuildingState.Construction or BuildingState.ConstructionPaused && Array.IndexOf(keys, g.Content.Buildings[b.Type].Key) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int Own(SimGame g, Civ civ, Func<UnitType, bool> kind, bool includeTraining = true)
    {
        int n = 0;
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (kind(g.Content.Units[u.Kind]) && (includeTraining || u.State != UnitState.Training))
            {
                n++;
            }
        }

        return n;
    }

    private static ulong Mix(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        ulong x = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    private static int Hexes(SimGame g, int a, int b) => g.Geo.Distance(g.Geo.FromIndex(a), g.Geo.FromIndex(b));
}
