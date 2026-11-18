using System;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

public sealed partial class SimGame
{
    /// <summary>
    /// Recomputes, for every settlement of a people, its storage, housing, jobs and the net rate of every good. Goods lie in
    /// the settlement where they were made (decision of 2026-11-17): every finished building adds a little room, a storehouse
    /// a lot. Runs only when something changed: a building finished or stopped, people came or went, a store ran empty or
    /// full, or filled again (docs/wydajnosc.md: rates, not items).
    /// </summary>
    public void Recompute(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int goods = Content.Goods.Count;
        Array.Clear(civ.ExperienceRate);
        Array.Clear(civ.MadeRate);
        int people = 0;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            SettlementLevel level = Content.Levels[s.Level];
            s.Housing = level.Housing;
            Array.Clear(s.Rate);
            Array.Fill(s.Capacity, level.Storage);
            s.Idle = s.People;
            s.HealPerMinute = 0;
            s.GrowthBonus = 0;
            s.EpidemicResist = 0;
            foreach (WorkKind w in Content.Works)
            {
                if (s.Aqueduct && w.GrowthBonus > 0)
                {
                    s.GrowthBonus += w.GrowthBonus;
                    s.EpidemicResist += w.ResistEpidemicPercent;
                }
            }
            s.WorkPermille = WorkPermille(s);
            people += s.People;
        }

        // Storage and housing first (every finished building), then jobs: priority buildings first, the rest in build order.
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            if (b.State is BuildingState.Active or BuildingState.Stopped)
            {
                BuildingType t = Content.Buildings[b.Type];
                Settlement s = Settlements[b.Settlement];
                s.Housing += t.Housing + Bonus(civ, EffectKind.Housing, t.Key, t.Groups);
                long room = t.Storage > 0 ? t.Storage : Content.BuildingStorage;
                room += room * Bonus(civ, EffectKind.Storage, t.Key, t.Groups) / 100;
                for (int g = 0; g < goods; g++)
                {
                    s.Capacity[g] += room;
                }
            }
        }

        // Jobs first: priority buildings, then the rest in build order.
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
                b.Starved = false;
                b.Full = false;
                if (b.State != BuildingState.Active || t.Workers == 0)
                {
                    continue;
                }

                Settlement s = Settlements[b.Settlement];
                b.Workers = Math.Min(t.Workers, s.Idle);
                s.Idle -= b.Workers;
                if (b.Workers == 0)
                {
                    continue;
                }

                // Asleep while an input is out or every output is full (woken when the stores change, see EconomyStep).
                bool anyOut = false;
                bool allFull = true;
                for (int g = 0; g < goods; g++)
                {
                    b.Starved |= t.Consumes[g] > 0 && (s.EmptyMask & (1L << g)) != 0;
                    if (t.Produces[g] > 0)
                    {
                        anyOut = true;
                        allFull &= (s.FullMask & (1L << g)) != 0;
                    }
                }

                b.Full = anyOut && allFull;
                civ.ExperienceRate[t.Tech] += b.Workers;
            }
        }

        // Then what they make: raised by technologies and support buildings, slowed by winter, helped by clothing.
        GatherSupport(civ);
        bool winter = IsWinter;
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            BuildingType t = Content.Buildings[b.Type];
            if (b.Workers == 0 || b.Starved || b.Full)
            {
                continue;
            }

            Settlement s = Settlements[b.Settlement];
            long boost = 100 + Bonus(civ, EffectKind.Production, t.Key, t.Groups) + Support(s, t) + WaterBonus(b.Cell, t);
            long work = s.WorkPermille * (winter ? t.WinterPercent : 100) / 100;
            for (int g = 0; g < goods; g++)
            {
                long made = t.Produces[g] * b.Workers * b.YieldPercent / 100 * boost / 100 * EventProduction(civ, g) / 100 * work / 1000;
                s.Rate[g] += made - (t.Consumes[g] * b.Workers);
                civ.MadeRate[g] += made;
            }
        }

        int eat = civ.Event >= 0 ? 100 + ((Content.Events[civ.Event].EatPercent - 100) * Harm(civ, Content.Events[civ.Event].Key) / 100) : 100;
        int idleAll = 0;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            idleAll += s.Idle;
            if (s.Gathering && s.Idle > 0)
            {
                int[] rich = Richness(s);
                long work = s.WorkPermille * (IsWinter ? 50 : 100) / 100; // gathering suffers in winter like the gatherers' huts
                for (int g = 0; g < goods; g++)
                {
                    long got = s.Idle * Content.ForagePerMinute[g] * rich[g] / 100 * EventProduction(civ, g) / 100 * work / 1000;
                    s.Rate[g] += got;
                    civ.MadeRate[g] += got;
                }
            }

            s.Rate[Content.Food] -= s.People * Content.FoodPerPersonPerMinute * eat / 100;
            if (Content.Clothing >= 0 && s.Stock[Content.Clothing] >= SimContent.One)
            {
                s.Rate[Content.Clothing] -= s.People * Content.ClothingWearPerPersonPerMinute;
            }
        }

        // Soldiers in the field and the advisor are fed from the first settlement (the capital).
        long soldiers = 0;
        foreach (int uid in civ.Units)
        {
            Unit u = Units[uid];
            if (u.State != UnitState.Gone && Content.Units[u.Kind].Army)
            {
                soldiers += u.People;
            }
        }

        if (civ.Settlements.Count > 0)
        {
            Settlement capital = Settlements[civ.Settlements[0]];
            capital.Rate[Content.Food] -= (soldiers * Content.War.SoldierFoodPerMinute) + (civ.Advisor is null ? 0 : Content.AdvisorUpkeep);
        }

        civ.ExperienceRate[0] += idleAll / 2; // gatherers around the settlements: a little experience in the oldest craft
        civ.People = people;
        civ.Dirty = false;
        SyncTotals(civ);
    }

    /// <summary>
    /// Richness of a settlement's land for gatherers, percent per good: the mean over its land hexes of what each hex gives
    /// (food and wood by biome, stone by relief, more on deposits and by rivers). Worked out again only when its land changes.
    /// </summary>
    public int[] Richness(Settlement s)
    {
        ArgumentNullException.ThrowIfNull(s);
        if (s.ForageCells == s.Cells.Count && s.ForageRichness.Length == Content.Goods.Count)
        {
            return s.ForageRichness;
        }

        int goods = Content.Goods.Count;
        var sum = new long[goods];
        int land = 0;
        int wood = Content.Goods.Count > 1 ? GoodId("wood") : -1;
        int stone = GoodId("stone");
        foreach (int cell in s.Cells)
        {
            int biome = Map.Biome[cell];
            if (Content.World.Biomes[biome].IsWater)
            {
                continue;
            }

            land++;
            sum[Content.Food] += Content.ForageBiome[biome * 2];
            if (wood >= 0)
            {
                sum[wood] += Content.ForageBiome[(biome * 2) + 1];
            }

            if (stone >= 0)
            {
                sum[stone] += Content.ForageRelief[Map.Relief[cell]];
            }

            int dep = Map.DepositDepth[cell] == 0 ? Map.Deposit[cell] : 0;
            if (dep > 0)
            {
                for (int g = 0; g < goods; g++)
                {
                    sum[g] += Content.ForageDeposit[(dep * goods) + g];
                }
            }

            if (Content.ForageRiverFood > 0 && ByRiver(cell))
            {
                sum[Content.Food] += Content.ForageRiverFood;
            }
        }

        var rich = new int[goods];
        for (int g = 0; g < goods; g++)
        {
            rich[g] = land == 0 ? 0 : (int)Math.Min(200, sum[g] / land);
        }

        s.ForageRichness = rich;
        s.ForageCells = s.Cells.Count;
        return rich;
    }

    private bool ByRiver(int cell)
    {
        HexCoord c = Geo.FromIndex(cell);
        for (int d = 0; d < 6; d++)
        {
            if (Map.River[Geo.EdgeIndex(Geo.Edge(c, (HexDirection)d))] > 0)
            {
                return true;
            }
        }

        return false;
    }

    private int GoodId(string key)
    {
        for (int g = 0; g < Content.Goods.Count; g++)
        {
            if (Content.Goods[g].Key == key)
            {
                return g;
            }
        }

        return -1;
    }

    /// <summary>Refreshes the people's totals (sum of its settlements) shown on the resource bar and used by the AI.</summary>
    public void SyncTotals(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        Array.Clear(civ.Stock);
        Array.Clear(civ.Capacity);
        Array.Clear(civ.Rate);
        bool hungry = false;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            hungry |= s.Hungry;
            for (int g = 0; g < civ.Stock.Length; g++)
            {
                civ.Stock[g] += s.Stock[g];
                civ.Capacity[g] += s.Capacity[g];
                civ.Rate[g] += s.Rate[g];
            }
        }

        civ.Hungry = hungry;
        civ.AnyStarved = false;
        foreach (int id in civ.Buildings)
        {
            civ.AnyStarved |= Buildings[id].Starved;
        }
    }

    // Adds each settlement's rates for the ticks since the last step. A store never goes below zero or above its room; when a
    // store runs empty, fills up or recovers, the people is recomputed (that is how sleeping buildings wake).
    private void EconomyStep(Civ civ)
    {
        if (civ.Dirty)
        {
            Recompute(civ);
        }

        for (int t = 0; t < civ.ExperienceRate.Length; t++)
        {
            civ.Experience[t] += (long)civ.ExperienceRate[t] * Content.EconomyEveryTicks;
        }

        for (int g = 0; g < civ.MadeRate.Length; g++)
        {
            civ.Made[g] += civ.MadeRate[g] * Content.EconomyEveryTicks;
        }

        bool changed = false;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            long empty = 0;
            long full = 0;
            for (int g = 0; g < s.Stock.Length; g++)
            {
                long delta = (s.Rate[g] * Content.EconomyEveryTicks) + s.Remainder[g];
                long whole = delta / SimContent.TicksPerMinute;
                s.Remainder[g] = delta - (whole * SimContent.TicksPerMinute);
                long stock = s.Stock[g] + whole;
                if (stock > s.Capacity[g])
                {
                    stock = Math.Max(s.Stock[g], s.Capacity[g]);
                    s.Remainder[g] = 0;
                }

                if (stock <= 0)
                {
                    stock = 0;
                    s.Remainder[g] = 0;
                }

                s.Stock[g] = stock;
                if (stock < SimContent.One)
                {
                    empty |= 1L << g;
                }

                if (s.Capacity[g] > 0 && stock * 100 >= s.Capacity[g] * 98)
                {
                    full |= 1L << g;
                }
            }

            bool hungry = (empty & (1L << Content.Food)) != 0 && s.Rate[Content.Food] < 0;
            if (hungry && !s.Hungry)
            {
                News.Add((civ.Id, $"{s.Name} has run out of food: people are starving."));
            }

            s.Hungry = hungry;
            if (empty != s.EmptyMask || full != s.FullMask || WorkChanged(s))
            {
                s.EmptyMask = empty;
                s.FullMask = full;
                changed = true;
            }
        }

        LogisticsStep(civ);
        if (changed)
        {
            civ.Dirty = true;
        }

        SyncTotals(civ);
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
            int sick = civ.Event >= 0 ? Content.Events[civ.Event].LosePercentPerMinute * Harm(civ, Content.Events[civ.Event].Key) / 100 : 0;
            if (sick > 0 && Content.Events[civ.Event].Key == "epidemic")
            {
                sick = sick * Math.Max(0, 100 - s.EpidemicResist) / 100;
            }
            if (sick > 0)
            {
                perMinute = -Math.Max(SimContent.One, s.People * sick);
                s.GrowthRemainder = Math.Min(0, s.GrowthRemainder);
            }
            else if (s.Hungry)
            {
                perMinute = -Math.Max(SimContent.One, s.People * Content.StarvePercentPerMinute);
                s.GrowthRemainder = Math.Min(0, s.GrowthRemainder);
            }
            else if (s.People < s.Housing)
            {
                perMinute = Math.Max(Content.MinGrowthPerMinute * SimContent.One, s.People * (Content.GrowthPercentPerMinute + Bonus(civ, EffectKind.Growth, "*") + s.GrowthBonus));
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
