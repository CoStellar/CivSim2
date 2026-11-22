using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// Winter, clothing and care (decisions of 2026-11-18). Winter comes every cycle of the game (no years: playability first); in
/// winter every worker works at half, and gathering and fields lose half again. Every set of clothing a worker has adds 15%:
/// outside winter only one set counts (at most 115%), in winter more sets count, up to 110%. All of it is worked out per
/// settlement (clothing in store / people), rounded to steps of 2.5%, and the rates are worked out again only when a step or
/// the season changes (docs/wydajnosc.md: rates, not items). Herbalists heal wounded soldiers in their settlement's land.
/// </summary>
/// <summary>The people of a settlement gather food, wood and stone around it while they have no job, or stop.</summary>
public sealed record GatherCommand(int Civ, int Settlement, bool On) : SimCommand(Civ);

public sealed partial class SimGame
{
    private string? Gather(Civ civ, int sid, bool on)
    {
        if ((uint)sid >= (uint)Settlements.Count || Settlements[sid].Civ != civ.Id)
        {
            return "Not our settlement.";
        }

        Settlements[sid].Gathering = on;
        civ.Dirty = true;
        return null;
    }

    /// <summary>True while it is winter.</summary>
    public bool IsWinter => Content.SeasonCycleTicks > 0 && Ticks % Content.SeasonCycleTicks >= Content.SeasonCycleTicks - Content.WinterTicks;

    /// <summary>Ticks until the season changes (winter comes or ends).</summary>
    public long TicksToSeasonChange
    {
        get
        {
            if (Content.SeasonCycleTicks <= 0)
            {
                return long.MaxValue;
            }

            long inCycle = Ticks % Content.SeasonCycleTicks;
            long winterFrom = Content.SeasonCycleTicks - Content.WinterTicks;
            return inCycle < winterFrom ? winterFrom - inCycle : Content.SeasonCycleTicks - inCycle;
        }
    }

    /// <summary>
    /// What a worker of the settlement does now, per mille of a plain worker: winter and clothing. Worked out from the clothing in
    /// store and the people, rounded down to steps of 2.5%.
    /// </summary>
    public int WorkPermille(Settlement s)
    {
        ArgumentNullException.ThrowIfNull(s);
        long sets = 0; // sets per person, per mille
        if (Content.Clothing >= 0 && s.People > 0)
        {
            sets = s.Stock[Content.Clothing] * 1000 / ((long)s.People * SimContent.One);
        }

        long bonus = Content.ClothingBonusPercent * 10L;

        // Warm (stone) homes: the share of people living in them loses only 35% in winter instead of 50%.
        long warm = s.People == 0 ? 0 : Math.Min(1000, (long)s.WarmHomes * 1000 / s.People);
        long winterBase = (Content.WinterWorkPercent * 10L) + ((1000 - (Content.WinterWorkPercent * 10L)) * 3 / 10 * warm / 1000);
        long work = IsWinter
            ? Math.Min(Content.ClothingWinterMaxPercent * 10L, winterBase + (bonus * sets / 1000))
            : 1000 + (bonus * Math.Min(sets, 1000) / 1000);
        work = Math.Max(work, 0);
        if (s.Besieged)
        {
            work = work * Content.Siege.BlockadeWorkPercent / 100; // siege: a blockaded settlement works less
        }

        return (int)(work / Content.WorkStepPermille * Content.WorkStepPermille);
    }

    // Tells the peoples about the seasons and makes them work the rates out again when the season changes.
    private void SeasonStep()
    {
        if (Content.SeasonCycleTicks <= 0)
        {
            return;
        }

        long left = TicksToSeasonChange;
        bool winter = IsWinter;
        if (!winter && left == Content.WinterWarnTicks)
        {
            foreach (Civ c in Civs)
            {
                if (!c.Dead)
                {
                    News.Add((c.Id, $"Winter comes in {Content.WinterWarnTicks / SimContent.TicksPerSecond} seconds: workers will work at half, gatherers and fields at a quarter. Clothing and food stores help."));
                }
            }
        }

        if (winter != _wasWinter)
        {
            _wasWinter = winter;
            foreach (Civ c in Civs)
            {
                if (c.Dead)
                {
                    continue;
                }

                c.Dirty = true;
                News.Add((c.Id, winter ? "Winter has come: work is slow, hunters and fishers are worth more now." : "Winter is over: work goes at its full pace again."));
            }
        }
    }

    private bool _wasWinter;

    // Clothing in store changes the work of a settlement by steps; when the step moves, the rates are worked out again.
    private bool WorkChanged(Settlement s) => WorkPermille(s) != s.WorkPermille;

    // Support buildings (herbalists, masons, mills, guild halls) raise the buildings of a group in their settlement while their
    // workers work; the raise grows with the share of the jobs filled. Gathered once per recompute (a short list), read by the
    // production pass.
    private readonly System.Collections.Generic.List<(int Settlement, string Group, int Percent)> _support = [];

    private void GatherSupport(Civ civ)
    {
        _support.Clear();
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            if (b.Workers == 0 || b.State != BuildingState.Active)
            {
                continue;
            }

            BuildingType st = Content.Buildings[b.Type];
            if (st.SupportPercent > 0)
            {
                _support.Add((b.Settlement, st.SupportGroup, st.SupportPercent * b.Workers / Math.Max(1, st.Workers)));
            }

            Settlement home = Settlements[b.Settlement];
            if (st.HealsPerMinute > 0)
            {
                home.HealPerMinute += st.HealsPerMinute * b.Workers / Math.Max(1, st.Workers);
            }

            // Bathhouses: milder epidemics and faster growth in their settlement, by the share of their jobs filled.
            home.EpidemicResist = Math.Min(90, home.EpidemicResist + (st.ResistEpidemicPercent * b.Workers / Math.Max(1, st.Workers)));
            home.GrowthBonus += st.GrowthBonus * b.Workers / Math.Max(1, st.Workers);
        }
    }

    private int Support(Settlement s, BuildingType t)
    {
        int pct = 0;
        foreach ((int sid, string group, int percent) in _support)
        {
            if (sid == s.Id && Array.IndexOf(t.Groups, group) >= 0)
            {
                pct += percent;
            }
        }

        return pct;
    }

    // Wounded soldiers standing in the land of a settlement with working herbalists come back to their unit.
    private void HealStep()
    {
        foreach (Unit u in ActiveUnits())
        {
            if (u.Injured <= 0 || u.Fighting)
            {
                continue;
            }

            int sid = SettlementAt[u.Cell];
            if (sid < 0 || Settlements[sid].Civ != u.Civ || Settlements[sid].HealPerMinute <= 0)
            {
                continue;
            }

            u.HealProgress += Settlements[sid].HealPerMinute * SimContent.One / 60;
            while (u.HealProgress >= SimContent.One && u.Injured > 0)
            {
                u.HealProgress -= SimContent.One;
                u.Injured--;
                u.People++;
            }
        }
    }
}
