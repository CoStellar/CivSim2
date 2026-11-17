using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

public sealed partial class SimGame
{
    /// <summary>
    /// Recomputes storage, housing, jobs and the net rate of every good for one civilization. Runs only when something
    /// changed (a building finished or stopped, people came or went, an input ran out or came back).
    /// </summary>
    public void Recompute(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int goods = Content.Goods.Count;
        Array.Clear(civ.Rate);
        Array.Clear(civ.Capacity);
        int people = 0;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            if (s.Abandoned)
            {
                continue;
            }

            SettlementLevel level = Content.Levels[s.Level];
            s.Housing = level.Housing;
            for (int g = 0; g < goods; g++)
            {
                civ.Capacity[g] += level.Storage;
            }

            s.Idle = s.People;
            people += s.People;
        }

        // Priority buildings take workers first, then the rest in the order they were built.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (int id in civ.Buildings)
            {
                Building b = Buildings[id];
                if (b.Priority != (pass == 0))
                {
                    continue;
                }

                BuildingType t = Content.Buildings[b.Type];
                b.Workers = 0;
                if (b.State != BuildingState.Active)
                {
                    continue;
                }

                Settlement s = Settlements[b.Settlement];
                s.Housing += t.Housing;
                for (int g = 0; g < goods; g++)
                {
                    civ.Capacity[g] += t.Storage;
                }

                if (t.Workers == 0)
                {
                    continue;
                }

                b.Workers = Math.Min(t.Workers, s.Idle);
                s.Idle -= b.Workers;
                if (b.Starved || b.Workers == 0)
                {
                    continue;
                }

                for (int g = 0; g < goods; g++)
                {
                    civ.Rate[g] += (t.Produces[g] * b.Workers * b.YieldPercent / 100) - (t.Consumes[g] * b.Workers);
                }
            }
        }

        int idle = 0;
        foreach (int sid in civ.Settlements)
        {
            idle += Settlements[sid].Abandoned ? 0 : Settlements[sid].Idle;
        }

        civ.Rate[Content.Food] += (idle * Content.IdleForagePerMinute) - (people * Content.FoodPerPersonPerMinute);
        civ.People = people;
        civ.Dirty = false;
    }

    // Adds the rates for the ticks since the last step. Excess over storage is lost; a store that runs dry stops its
    // consumers (they sleep until the good comes back), and dry food makes the civilization hungry.
    private void EconomyStep(Civ civ)
    {
        if (civ.Dirty)
        {
            Recompute(civ);
        }

        bool starve = false;
        for (int g = 0; g < civ.Stock.Length; g++)
        {
            long delta = (civ.Rate[g] * Content.EconomyEveryTicks) + civ.Remainder[g];
            long whole = delta / SimContent.TicksPerMinute;
            civ.Remainder[g] = delta - (whole * SimContent.TicksPerMinute);
            long stock = civ.Stock[g] + whole;
            if (stock > civ.Capacity[g])
            {
                stock = Math.Max(civ.Stock[g], civ.Capacity[g]);
            }

            if (stock < 0)
            {
                stock = 0;
                civ.Remainder[g] = 0;
                if (g == Content.Food)
                {
                    if (!civ.Hungry)
                    {
                        News.Add((civ.Id, "Food has run out: people are starving."));
                    }

                    civ.Hungry = true;
                }
                else
                {
                    starve |= StarveConsumersOf(civ, g);
                }
            }
            else if (g == Content.Food && stock > 0)
            {
                civ.Hungry = false;
            }

            civ.Stock[g] = stock;
        }

        if (civ.AnyStarved && !starve)
        {
            WakeStarved(civ);
        }

        if (starve)
        {
            civ.Dirty = true;
        }
    }

    private bool StarveConsumersOf(Civ civ, int good)
    {
        bool any = false;
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            if (!b.Starved && b.State == BuildingState.Active && Content.Buildings[b.Type].Consumes[good] > 0)
            {
                b.Starved = true;
                any = true;
            }
        }

        civ.AnyStarved |= any;
        return any;
    }

    // A starved building wakes when each of its inputs has at least a minute of its own use in store.
    private void WakeStarved(Civ civ)
    {
        bool still = false;
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            if (!b.Starved)
            {
                continue;
            }

            BuildingType t = Content.Buildings[b.Type];
            bool ok = true;
            for (int g = 0; g < t.Consumes.Length && ok; g++)
            {
                ok = civ.Stock[g] >= t.Consumes[g] * Math.Max(1, t.Workers);
            }

            if (ok)
            {
                b.Starved = false;
                civ.Dirty = true;
            }
            else
            {
                still = true;
            }
        }

        civ.AnyStarved = still;
    }

    // People grow toward the housing while there is food and die off while there is none.
    private void PopulationStep(Civ civ)
    {
        bool changed = false;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            if (s.Abandoned)
            {
                continue;
            }

            long perMinute; // hundredths of a person
            if (civ.Hungry)
            {
                perMinute = -Math.Max(SimContent.One, s.People * Content.StarvePercentPerMinute);
                s.GrowthRemainder = Math.Min(0, s.GrowthRemainder);
            }
            else if (s.People < s.Housing)
            {
                perMinute = Math.Max(Content.MinGrowthPerMinute * SimContent.One, s.People * Content.GrowthPercentPerMinute);
            }
            else
            {
                s.GrowthRemainder = 0;
                continue;
            }

            // The remainder is kept in hundredths × ticks so that slow growth is not lost to rounding.
            long delta = s.GrowthRemainder + (perMinute * Content.PopulationEveryTicks);
            long unit = SimContent.One * SimContent.TicksPerMinute;
            int whole = (int)(delta / unit);
            s.GrowthRemainder = delta - (whole * unit);
            if (whole != 0)
            {
                s.People = Math.Clamp(s.People + whole, 0, Math.Max(s.People, s.Housing));
                changed = true;
            }
        }

        if (changed)
        {
            civ.Dirty = true;
        }
    }
}
