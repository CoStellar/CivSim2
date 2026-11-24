using System;
using System.Linq;
using System.Collections.Generic;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>What needs the player's reaction in a settlement.</summary>
public enum AlertKind
{
    /// <summary>The settlement is besieged.</summary>
    Attacked = 0,

    /// <summary>Soldiers of a people at war with us are close.</summary>
    EnemyNear = 1,

    /// <summary>People go hungry: food must be brought in or made.</summary>
    Hungry = 2,

    /// <summary>A building stands still for lack of a good: it must be brought in.</summary>
    NeedsGood = 3,

    /// <summary>No homes left: the settlement stopped growing.</summary>
    NoRoom = 4,

    /// <summary>A good is full: its makers stopped.</summary>
    StoreFull = 5,

    /// <summary>A construction stopped: its materials were lost on the way.</summary>
    MissingMaterials = 6,

    /// <summary>Winter comes soon (or is here) and the settlement has less than one set of clothing per person.</summary>
    Clothing = 7,

    /// <summary>The settlement will not have the food for its units' next ration (or did not have it at the last one).</summary>
    Rations = 8,

    /// <summary>11B (2026-11-20): the settlement eats more than it makes and its food runs out within alerts.foodLowMinutes.</summary>
    FoodLow = 9,

    /// <summary>E2 (2026-11-20): nothing is being researched though something could be (the client opens the research window).</summary>
    NoResearch = 10,

    /// <summary>P3 (Patryk, 2026-11-23): units of a people we have not met were seen by our settlement, and they have seen it.</summary>
    Strangers = 11,
}

/// <summary>One alert. <see cref="Good"/> is −1 when it is not about a good; <see cref="Cell"/> is where to look.</summary>
public sealed record Alert(AlertKind Kind, int Settlement, int Cell, int Good, string Text);

public sealed partial class SimGame
{
    /// <summary>What needs a people's reaction right now, most urgent first. Worked out on request (for the view), not stored.</summary>
    public List<Alert> Alerts(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        var list = new List<Alert>();
        foreach (int sid in civ.Settlements)
        {
            Settlement s = Settlements[sid];
            if (s.Besieged)
            {
                list.Add(new Alert(AlertKind.Attacked, s.Id, s.Center, -1, $"{s.Name} is under attack."));
            }

            if (EnemyNear(civ, s))
            {
                list.Add(new Alert(AlertKind.EnemyNear, s.Id, s.Center, -1, $"Enemy soldiers near {s.Name}."));
            }

            if (StrangersNear(civ, s))
            {
                // P3: only that they are there and saw us; not who they are nor where they came from.
                list.Add(new Alert(AlertKind.Strangers, s.Id, s.Center, -1, $"Strangers were seen near {s.Name}, and they have seen us. Who they are and where they come from, we do not know."));
            }

            if (Content.Clothing >= 0 && (IsWinter || TicksToSeasonChange <= SimContent.TicksPerMinute) && s.People > 0
                && s.Stock[Content.Clothing] < (long)s.People * SimContent.One)
            {
                // E1 (2026-11-20): what winter does and what helps.
                string when = IsWinter ? "It is winter" : $"Winter comes in {TicksToSeasonChange / SimContent.TicksPerSecond} s";
                BuildingType? tailor = Content.Buildings.FirstOrDefault(b => b.Id > 0 && b.Produces[Content.Clothing] > 0);
                string help = tailor is null ? "" : Knows(civ, tailor) ? $" Build a {Where(tailor.Key)}." : $" Research {Content.Techs[tailor.Tech].Name} for clothing.";
                list.Add(new Alert(AlertKind.Clothing, s.Id, s.Center, Content.Clothing,
                    $"{when}: workers work at {Content.WinterWorkPercent}% without clothing; {s.Name} has {s.Stock[Content.Clothing] / SimContent.One} sets for {s.People} people. Keep food in store.{help}"));
            }

            // The next ration: what it will have by then (its makers keep making) against what everyone eats.
            (_, _, long ration) = RationOf(s);
            long secs = TicksToRation(civ) / SimContent.TicksPerSecond;
            long then = s.Stock[Content.Food] + ((s.Rate[Content.Food] + s.Eating) * secs / 60);
            if (s.Hungry || s.RationShort > 0)
            {
                list.Add(new Alert(AlertKind.Hungry, s.Id, s.Center, Content.Food, (s.HungerStreak >= 2
                    ? $"{s.Name} starves: {s.HungryPeople} people" + (s.RationShort > 0 ? $" and {s.RationShort} units" : "") + $" without food, some die at every ration (next in {secs} s). "
                    : $"{s.Name} goes hungry: {s.HungryPeople} people" + (s.RationShort > 0 ? $" and {s.RationShort} units" : "") + $" got no ration. Missing the next one in {secs} s kills some. ")
                    + HungerAdvice(civ, s)));
            }
            else if (s.Rate[Content.Food] < 0 && s.Stock[Content.Food] * SimContent.TicksPerSecond * 60 / -s.Rate[Content.Food] <= (long)Content.FoodLowMinutes * SimContent.TicksPerMinute)
            {
                // 11B (Patryk, 2026-11-20): an early warning while there is still time, with what to do.
                long left = s.Stock[Content.Food] * 60 / -s.Rate[Content.Food];
                list.Add(new Alert(AlertKind.FoodLow, s.Id, s.Center, Content.Food,
                    $"Food in {s.Name} runs out in {left / 60}:{left % 60:00}: build a Gatherers' hut (Food) or put more people to food."));
            }
            else if (ration > 0 && then < ration)
            {
                list.Add(new Alert(AlertKind.Rations, s.Id, s.Center, Content.Food,
                    $"{s.Name} will lack food for the next ration in {secs} s: {then / SimContent.One} of {ration / SimContent.One} needed. The idle go hungry first. {HungerAdvice(civ, s)}"));
            }

            long reported = 0;
            foreach (int id in civ.Buildings)
            {
                Building b = Buildings[id];
                if (b.Settlement != s.Id)
                {
                    continue;
                }

                if (b.State == BuildingState.Delivery && b.Porters == 0 && b.WorkCap < Content.Buildings[b.Type].BuildTicks)
                {
                    list.Add(new Alert(AlertKind.MissingMaterials, s.Id, b.Cell, -1, $"{Content.Buildings[b.Type].Name} in {s.Name} lacks materials: {CargoText(Missing(b))}."));
                }

                if (b.State == BuildingState.Active && b.Starved)
                {
                    long[] needs = Content.Buildings[b.Type].Consumes;
                    for (int g = 0; g < needs.Length; g++)
                    {
                        long bit = 1L << g;
                        if (needs[g] > 0 && (s.EmptyMask & bit) != 0 && (reported & bit) == 0 && g != Content.Food)
                        {
                            reported |= bit;
                            list.Add(new Alert(AlertKind.NeedsGood, s.Id, s.Center, g, $"{s.Name} needs {GoodText(g)}: its {Content.Buildings[b.Type].Name.ToLowerInvariant()} stands still."));
                        }
                    }
                }
            }

            if (s.People > s.Housing)
            {
                list.Add(new Alert(AlertKind.NoRoom, s.Id, s.Center, -1,
                    $"{s.Name}: {s.People - s.Housing} people have no home and do not work: {HomesFix(civ, s)}."));
            }
            else if (s.People >= s.Housing && !s.Hungry)
            {
                list.Add(new Alert(AlertKind.NoRoom, s.Id, s.Center, -1, $"{s.Name} has no homes left and stopped growing: {HomesFix(civ, s)}."));
            }

            for (int g = 0; g < Content.Goods.Count; g++)
            {
                if ((s.FullMask & (1L << g)) != 0 && s.Rate[g] >= 0 && MadeIn(civ, s, g))
                {
                    list.Add(new Alert(AlertKind.StoreFull, s.Id, s.Center, g, $"{s.Name}: {GoodText(g)} is full, its makers stopped. Build a storehouse or send it away."));
                }
            }
        }

        // E2: nothing is being researched though something could be.
        if (civ.Researching < 0 && civ.Settlements.Count > 0 && !Picking)
        {
            foreach (SimTech t in Content.Techs)
            {
                if (CannotResearch(civ, t.Id) is null)
                {
                    list.Add(new Alert(AlertKind.NoResearch, civ.Settlements[0], Settlements[civ.Settlements[0]].Center, -1,
                        $"Nothing is being researched: {t.Name} and maybe more can be. Click to choose (Research, key 2)."));
                    break;
                }
            }
        }

        list.Sort((a, b) => a.Kind.CompareTo(b.Kind));
        return list;
    }

    // True when a building of the settlement makes this good.
    private bool MadeIn(Civ civ, Settlement s, int good)
    {
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            if (b.Settlement == s.Id && b.State == BuildingState.Active && Content.Buildings[b.Type].Produces[good] > 0)
            {
                return true;
            }
        }

        return false;
    }

    // P3 (Patryk, 2026-11-23): a unit of a people we have not met, seen by us near the settlement, whose people has seen the
    // settlement (its scouts looked at it).
    private bool StrangersNear(Civ civ, Settlement s)
    {
        int reach = Content.Levels[s.Level].Radius + Content.SettlementSightBeyond;
        var centre = Geo.FromIndex(s.Center);
        foreach (Civ other in Civs)
        {
            if (other.Id == civ.Id || other.Dead || AtWar(civ.Id, other.Id) || Met(civ, other) || !other.Known[s.Center])
            {
                continue;
            }

            foreach (int uid in other.Units)
            {
                Unit u = Units[uid];
                if (u.State is not (UnitState.Gone or UnitState.Training) && u.Aboard < 0 && Sees(civ, u) && Geo.Distance(centre, Geo.FromIndex(u.Cell)) <= reach)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Soldiers of a people at war with us within a few hexes of the settlement's land.
    private bool EnemyNear(Civ civ, Settlement s)
    {
        int reach = Content.Levels[s.Level].Radius + 3;
        var centre = Geo.FromIndex(s.Center);
        foreach (Civ other in Civs)
        {
            if (other.Id == civ.Id || other.Dead || !AtWar(civ.Id, other.Id))
            {
                continue;
            }

            foreach (int uid in other.Units)
            {
                Unit u = Units[uid];
                if (Content.Units[u.Kind].Army && Sees(civ, u) && Geo.Distance(centre, Geo.FromIndex(u.Cell)) <= reach)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // What gives a settlement more homes: huts, or at its level's limit the upgrade and what it still lacks (G4, tests 2026-11-24).
    private string HomesFix(Civ civ, Settlement s)
    {
        BuildingType? hut = Content.Buildings.FirstOrDefault(b => b.Key == "hut");
        if (Content.Levels[s.Level].MaxHousing <= 0 || s.Housing < Content.Levels[s.Level].MaxHousing || s.Level + 1 >= Content.Levels.Count)
        {
            return hut is not null && Knows(civ, hut) ? $"build {Where("hut")}" : hut is not null ? $"research {Content.Techs[hut.Tech].Name} for huts" : "build homes";
        }

        SettlementLevel next = Content.Levels[s.Level + 1];
        string name = next.Name.ToLowerInvariant();
        if (!Knows(civ, next.Tech, next.AlsoTechs))
        {
            return $"upgrade it to a {name}, which needs {NeedText(next.Tech, next.AlsoTechs)}";
        }

        if (s.People < next.MinPeople)
        {
            return $"upgrade it to a {name} at {next.MinPeople} people";
        }

        var all = new long[next.UpgradePrice.Length];
        foreach (int own in civ.Settlements)
        {
            for (int g = 0; g < all.Length; g++)
            {
                all[g] += Settlements[own].Stock[g];
            }
        }

        return CanAfford(all, next.UpgradePrice, 100)
            ? $"upgrade it to a {name} (its panel)"
            : $"upgrade it to a {name}: it costs {PriceText(next.UpgradePrice)}, we have only {ShortText(all, next.UpgradePrice)}";
    }
}
