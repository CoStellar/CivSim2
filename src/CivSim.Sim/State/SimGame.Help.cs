using System;
using System.Linq;
using System.Collections.Generic;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// Help for a new player (decisions of Patryk after the tests of 2026-11-20, part E): alerts that say what to do, what a technology
/// opened, what an event does, the food balance of a settlement, and the steps of the tutorial. Only words: no rule changes here.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>The tab of the building bar a building is in (the same groups as the client's tabs).</summary>
    public static string TabOf(BuildingType t)
    {
        ArgumentNullException.ThrowIfNull(t);
        string g = t.Groups.Length > 0 ? t.Groups[0] : "";
        return g switch
        {
            "food" or "farm" => "Food",
            "tools" or "mine" or "craft" => "Production",
            "house" or "store" or "road" => "Homes & stores",
            "fort" or "army" => "Military",
            _ => "Society",
        };
    }

    // "Gatherers' hut (Food)" for a building key, or the key when unknown.
    private string Where(string key)
    {
        BuildingType? t = Content.Buildings.FirstOrDefault(b => b.Key == key);
        return t is null ? key : $"{t.Name} ({TabOf(t)})";
    }

    // The food maker a settlement can build now: the first known one with workers whose terrain fits somewhere in its land.
    private BuildingType? FoodMakerFor(Civ civ, Settlement s)
    {
        foreach (BuildingType t in Content.Buildings)
        {
            if (t.Id <= 0 || t.Workers <= 0 || t.Produces[Content.Food] <= 0 || !Knows(civ, t))
            {
                continue;
            }

            foreach (int cell in s.Cells)
            {
                if (CannotBuild(civ, cell, t, checkPrice: false) is null)
                {
                    return t;
                }
            }
        }

        return null;
    }

    // Jobs of working food buildings in a settlement that have nobody (tests 2026-11-24, P6).
    private int EmptyFoodJobs(Civ civ, Settlement s)
    {
        int empty = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            BuildingType t = Content.Buildings[b.Type];
            if (b.Settlement == s.Id && b.State == BuildingState.Active && t.Produces[Content.Food] > 0 && b.Workers < t.Workers)
            {
                empty += t.Workers - b.Workers;
            }
        }

        return empty;
    }

    // P6 (tests 2026-11-24): food buildings stand half empty because every worker is elsewhere, so another hut would stand
    // empty too; the advice is to free workers instead. Null when that is not the case.
    private string? FreeWorkersAdvice(Civ civ, Settlement s)
    {
        int empty = s.Idle == 0 ? EmptyFoodJobs(civ, s) : 0;
        return empty > 0
            ? $"{empty} food job{(empty == 1 ? "" : "s")} stand{(empty == 1 ? "s" : "")} empty and nobody is idle: give the food buildings priority, or stop a woodcutter, quarry or workshop (Stop)."
            : null;
    }

    /// <summary>E1: what to do about a hungry settlement, from why it is hungry.</summary>
    public string HungerAdvice(Civ civ, Settlement s)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(s);
        BuildingType? maker = FoodMakerFor(civ, s);
        string build = maker is null ? "bring food by caravan from another settlement" : $"build more {maker.Name}s ({TabOf(maker)})";
        if (s.Idle >= 3)
        {
            return $"{s.Idle} people have no work: {build}.";
        }

        if (FreeWorkersAdvice(civ, s) is { } free)
        {
            return free;
        }

        if (IsWinter)
        {
            return $"It is winter, so work is slower: {build}, and keep more food for the next winter.";
        }

        return maker is null ? $"Too few work at food: {build}." : $"Too few work at food: {build}, or move workers to food (priority).";
    }

    /// <summary>E3: the food balance of a settlement a minute, and how long its food lasts (empty when nothing to say).</summary>
    public string FoodBalance(Civ civ, Settlement s)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(s);
        long eaten = s.Eating;
        long made = s.Rate[Content.Food] + eaten;
        string text = $"Food a minute: {made / SimContent.One} made, {eaten / SimContent.One} eaten";
        if (made >= eaten)
        {
            return text + $" (+{(made - eaten) / SimContent.One}).";
        }

        long shortBy = eaten - made;
        BuildingType? maker = FoodMakerFor(civ, s);
        long each = maker is null ? 0 : maker.Produces[Content.Food] * s.WorkPermille / 1000;
        long workers = each > 0 ? (shortBy + each - 1) / each : 0;
        long lasts = s.Stock[Content.Food] * 60 / shortBy;
        text += $"; short by {(shortBy + SimContent.One - 1) / SimContent.One}";
        if (workers > 0)
        {
            text += $", about {workers} more food worker{(workers == 1 ? "" : "s")} ({maker!.Name})";
        }

        text += $". The food lasts {lasts / 60}:{lasts % 60:00}.";
        if (FreeWorkersAdvice(civ, s) is { } free)
        {
            text += " " + free;
        }

        // Variant C (2026-11-22): why the settlement does not grow although homes are free.
        return s.GrowthStopped && s.People < s.Housing
            ? text + $" It does not grow while its food falls and less than {Content.GrowthStopRations} rations are left."
            : text;
    }

    /// <summary>E5: what a technology opens, and where to find it.</summary>
    public string Opened(int tech)
    {
        var buildings = new List<string>();
        foreach (BuildingType b in Content.Buildings)
        {
            if (b.Id > 0 && b.Tech == tech)
            {
                buildings.Add($"{b.Name} ({TabOf(b)})");
            }
        }

        var units = new List<string>();
        foreach (UnitType u in Content.Units)
        {
            if (u.Tech == tech && !u.System)
            {
                units.Add(u.Name);
            }
        }

        var parts = new List<string>();
        if (buildings.Count > 0)
        {
            parts.Add("new buildings: " + string.Join(", ", buildings));
        }

        if (units.Count > 0)
        {
            parts.Add("new units (train them in a settlement's panel): " + string.Join(", ", units));
        }

        if (Content.Techs[tech].Gives is { Length: > 0 } gives && buildings.Count == 0 && units.Count == 0)
        {
            parts.Add(gives);
        }

        return parts.Count == 0 ? "" : " " + string.Join("; ", parts) + ".";
    }

    /// <summary>E15: what an event does and for how long.</summary>
    public string EventInfo(SimEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var parts = new List<string>();
        if (e.ProductionPercent != 100 && e.ProductionPercent > 0)
        {
            var goods = new List<string>();
            for (int g = 0; g < e.Goods.Length; g++)
            {
                if (e.Goods[g])
                {
                    goods.Add(Content.Goods[g].Name.ToLowerInvariant());
                }
            }

            parts.Add($"{string.Join(" and ", goods)} made at {e.ProductionPercent}%");
        }

        if (e.EatPercent != 100 && e.EatPercent > 0)
        {
            parts.Add($"people eat {e.EatPercent}% of their food");
        }

        if (e.LosePercentPerMinute > 0)
        {
            parts.Add($"{e.LosePercentPerMinute}% of the people die a minute");
        }

        if (e.DestroyBuilding)
        {
            parts.Add("a building burns down");
        }

        return $"{e.Name}: {string.Join(", ", parts)}, for {e.Ticks / SimContent.TicksPerSecond} s.";
    }

    /// <summary>E17: a good the people knows: it has some, makes some, or a building it knows makes it.</summary>
    public bool KnowsGood(Civ civ, int good)
    {
        if (civ.Stock[good] > 0 || civ.Made[good] > 0)
        {
            return true;
        }

        foreach (BuildingType b in Content.Buildings)
        {
            if (b.Id > 0 && b.Produces[good] > 0 && Knows(civ, b))
            {
                return true;
            }
        }

        return false;
    }

    // C1: a palisade stands on any hex of our land.
    private bool HasPalisade(Civ civ)
    {
        ushort bit = PalisadeBit;
        if (bit == 0)
        {
            return false;
        }

        for (int i = 0; i < WorksAt.Length; i++)
        {
            if ((WorksAt[i] & bit) != 0 && OwnerAt[i] == civ.Id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One step of the tutorial: what to do, whether it is done, and the building to highlight in the bar (or "").</summary>
    public sealed record TutorialStep(string Text, bool Done, string Highlight);

    /// <summary>
    /// E6 (Patryk, 2026-11-20): the steps of the tutorial, worked out from the game itself, so the progress is always known (also
    /// with the tutorial off, and after loading a save). The order works: food first, then wood, then homes, then the world.
    /// </summary>
    public List<TutorialStep> Tutorial(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int Count(string key)
        {
            int n = 0;
            foreach (int id in civ.Buildings)
            {
                Building b = Buildings[id];
                n += Content.Buildings[b.Type].Key == key && b.State != BuildingState.Removed ? 1 : 0;
            }

            return n;
        }

        bool Known(string key) => Content.Techs.FirstOrDefault(t => t.Key == key) is { } t && civ.KnownTech[t.Id];
        bool Trained(string key) => Units.Exists(u => u.Civ == civ.Id && Content.Units[u.Kind].Key == key);
        int scouts = Content.Units.ToList().FindIndex(u => u.Key == "scouts");
        string scoutsTech = scouts >= 0 ? Content.Techs[Content.Units[scouts].Tech].Name : "Scouting";
        return
        [
            new($"Build a {Where("gatherers_hut")}: food first.", Count("gatherers_hut") >= 1, "gatherers_hut"),
            new($"Build a second {Where("gatherers_hut")}: one hut feeds few people.", Count("gatherers_hut") >= 2, "gatherers_hut"),
            new($"Build a {Where("woodcutter")}: wood for everything else.", Count("woodcutter") >= 1, "woodcutter"),
            new("Research Dwellings (Research, key 2): homes.", Known("dwellings"), ""),
            new($"Build {Where("hut")}: people grow only with free homes and food.", Count("hut") >= 1, "hut"),
            new($"Research {scoutsTech} (Research, key 2).", Known("scouting"), ""),
            new("Train scouts in the settlement's panel and send them out (right click on the map).", Trained("scouts"), ""),

            // C1 (tests 2026-11-23): other peoples may come to fight after the first minutes; a few warriors or a palisade help.
            new($"Prepare a defence before minute {Content.AiPeacefulStartMinutes}: research Flint weapons and train warriors, or research Palisade and draw one.",
                Known("flint_weapons") && Trained("warriors") || Known("palisade") && HasPalisade(civ), ""),
        ];
    }
}
