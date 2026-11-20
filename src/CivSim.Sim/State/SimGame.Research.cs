using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Start researching a technology.</summary>
public sealed record ResearchCommand(int Civ, int Tech) : SimCommand(Civ);

/// <summary>Hire the thinker (<paramref name="Advisor"/> false) or advisor on offer.</summary>
public sealed record HireCommand(int Civ, bool Advisor) : SimCommand(Civ);

/// <summary>Let the advisor go.</summary>
public sealed record DismissAdvisorCommand(int Civ) : SimCommand(Civ);

/// <summary>A thinker or advisor: a named person with a rarity, like an item to look for.</summary>
public sealed class Person
{
    /// <summary>Name without the generation number.</summary>
    public string Name { get; init; } = "";

    /// <summary>Rarity id.</summary>
    public int Rarity { get; init; }

    /// <summary>1 for the founder, 2 for the successor (II)…</summary>
    public int Generation { get; set; } = 1;

    /// <summary>Tick hired (or succeeded).</summary>
    public long Since { get; set; }

    /// <summary>Advisor fields (indexes into the content's fields); empty for thinkers.</summary>
    public int[] Fields { get; init; } = [];

    /// <summary>Name with the generation, e.g. "Handalf III".</summary>
    public string FullName => Generation <= 1 ? Name : $"{Name} {Roman(Generation)}";

    private static string Roman(int n)
    {
        string[] r = ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII", "XIII", "XIV", "XV", "XVI", "XVII", "XVIII", "XIX", "XX"];
        return n < r.Length ? r[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

public sealed partial class SimGame
{
    /// <summary>The thinker's edge for cost or experience (percent; 100 = no edge), weakened by each generation.</summary>
    public int Edge(Person? thinker, bool cost)
    {
        if (thinker is null)
        {
            return 100;
        }

        Rarity r = Content.Rarities[thinker.Rarity];
        long delta = (cost ? r.CostPercent : r.ExperiencePercent) - 100;
        for (int g = 1; g < thinker.Generation; g++)
        {
            delta = delta * (100 - Content.GenerationLossPercent) / 100;
        }

        return (int)(100 + delta);
    }

    /// <summary>Price of a technology for a people now (hundredths per good).</summary>
    public long[] ResearchPrice(Civ civ, int tech)
    {
        ArgumentNullException.ThrowIfNull(civ);
        SimTech t = Content.Techs[tech];
        int discount = Math.Min(Content.MaxNeighbourDiscountPercent, Content.NeighbourDiscountPercent * NeighboursKnowing(civ, tech))
            + (Content.TradeResearchDiscount * Partners(civ.Id));
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            if (b.State == BuildingState.Active && b.Workers > 0)
            {
                discount += Content.Buildings[b.Type].ResearchDiscountPercent;
            }
        }

        discount += Bonus(civ, EffectKind.Research, "*");
        discount = Math.Min(60, discount);
        int edge = Edge(civ.Thinker, cost: true);
        var price = new long[t.Price.Length];
        for (int g = 0; g < price.Length; g++)
        {
            price[g] = t.Price[g] * edge / 100 * (100 - discount) / 100 * Content.Pace.Price(t.Era) / 100;
        }

        return price;
    }

    /// <summary>Why a people cannot start researching <paramref name="tech"/> (null = it can, price aside).</summary>
    public string? CannotResearch(Civ civ, int tech)
    {
        ArgumentNullException.ThrowIfNull(civ);
        if ((uint)tech >= (uint)Content.Techs.Count)
        {
            return "Unknown technology.";
        }

        if (civ.KnownTech[tech])
        {
            return "Already known.";
        }

        if (civ.Researching >= 0)
        {
            return $"Already researching {Content.Techs[civ.Researching].Name}.";
        }

        int era = Content.Techs[tech].Era;
        if (era > civ.Era)
        {
            int prev = Math.Max(0, era - 1);
            return civ.Era < prev
                ? $"Our people must first reach the {Content.Eras[prev].Name}."
                : $"Opens in the {Content.Eras[era].Name}: know {Content.EraAdvanceTechs} technologies of the {Content.Eras[civ.Era].Name}, basics not counted (we know {KnownIn(civ, civ.Era)})"
                    + (Content.GatewayOf(civ.Era) is int gate and >= 0 ? $", among them the gateway {Content.Techs[gate].Name}{(civ.KnownTech[gate] ? " (known)" : "")}" : "")
                    + (EntryMissing(civ, era) is string made ? $", and make {made} first." : ".");
        }

        foreach (int req in Content.Techs[tech].Requires)
        {
            if (!civ.KnownTech[req])
            {
                return $"Needs {Content.Techs[req].Name} first.";
            }
        }

        int edge = Edge(civ.Thinker, cost: false) * Content.Pace.Experience(era) / 100;
        foreach ((int good, long amount) in Content.Techs[tech].Made)
        {
            long made = civ.Made[good] / SimContent.TicksPerMinute;
            long needed = amount * edge / 100;
            if (made < needed)
            {
                return $"Needs more {Content.Goods[good].Name.ToLowerInvariant()} made: {made / SimContent.One} of {needed / SimContent.One}.";
            }
        }

        foreach ((int req, long need) in Content.Techs[tech].Experience)
        {
            if (!civ.KnownTech[req])
            {
                return $"Needs {Content.Techs[req].Name} first.";
            }

            long have = civ.Experience[req];
            long needed = need * edge / 100;
            if (have < needed)
            {
                return $"Needs more experience in {Content.Techs[req].Name}: {have / SimContent.TicksPerMinute} of {needed / SimContent.TicksPerMinute} points (1 point = 1 worker for 1 minute{ExperienceFrom(req)}).";
            }
        }

        return null;
    }

    /// <summary>A12 (tests 2026-11-20): where the experience of a technology comes from, as ", in a farm or a pasture" ("" when nowhere).</summary>
    public string ExperienceFrom(int tech)
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (BuildingType b in Content.Buildings)
        {
            if (b.Tech == tech && b.Workers > 0 && !names.Contains("a " + b.Name.ToLowerInvariant()))
            {
                names.Add("a " + b.Name.ToLowerInvariant());
            }
        }

        string where = names.Count == 0 ? "" : $"working in {string.Join(" or ", names)}";
        if (tech == 0)
        {
            where = where.Length == 0 ? "people gathering around our settlements" : $"{where}, or people gathering around our settlements";
        }

        return where.Length == 0 ? "" : $"; it comes from {where}";
    }

    /// <summary>Number of peoples this one has met (seen a settlement of) that know <paramref name="tech"/>.</summary>
    public int NeighboursKnowing(Civ civ, int tech)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int n = 0;
        foreach (Civ other in Civs)
        {
            if (other.Id != civ.Id && !other.Dead && other.KnownTech[tech] && Met(civ, other))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>True when <paramref name="civ"/> has seen a settlement of <paramref name="other"/>.</summary>
    public bool Met(Civ civ, Civ other)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(other);
        foreach (int sid in other.Settlements)
        {
            if (civ.Known[Settlements[sid].Center])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The era of a people: a level reached by knowing enough technologies of the era before.</summary>
    public int EraOf(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.Era;
    }

    /// <summary>How many technologies of an era a people knows.</summary>
    public int KnownIn(Civ civ, int era)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int n = 0;
        for (int t = 0; t < Content.Techs.Count; t++)
        {
            if (civ.KnownTech[t] && Content.Techs[t].Era == era && !Content.Techs[t].Basic)
            {
                n++;
            }
        }

        return n;
    }

    // Raises the era while the people knows enough technologies of its current one.
    /// <summary>7A (2026-11-20): the goods still to make before entering <paramref name="era"/>, as "bronze 2 of 5" (null = none).</summary>
    public string? EntryMissing(Civ civ, int era)
    {
        ArgumentNullException.ThrowIfNull(civ);
        if ((uint)era >= (uint)Content.EraEntryMade.Length)
        {
            return null;
        }

        long[] need = Content.EraEntryMade[era];
        var parts = new System.Collections.Generic.List<string>();
        for (int g = 0; g < need.Length; g++)
        {
            long made = civ.Made[g] / SimContent.TicksPerMinute;
            if (need[g] > 0 && made < need[g])
            {
                parts.Add($"{Content.Goods[g].Name.ToLowerInvariant()} {made / SimContent.One} of {need[g] / SimContent.One}");
            }
        }

        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    internal void AdvanceEra(Civ civ, bool tell)
    {
        while (civ.Era + 1 < Content.Eras.Count && KnownIn(civ, civ.Era) >= Content.EraAdvanceTechs
            && (Content.GatewayOf(civ.Era) < 0 || civ.KnownTech[Content.GatewayOf(civ.Era)]) && EntryMissing(civ, civ.Era + 1) is null)
        {
            civ.Era++;
            if (tell)
            {
                News.Add((civ.Id, $"Our people enter the {Content.Eras[civ.Era].Name}: its technologies can now be researched."));
            }
        }
    }

    private string? Research(Civ civ, int tech)
    {
        string? why = CannotResearch(civ, tech);
        if (why is not null)
        {
            return why;
        }

        long[] price = ResearchPrice(civ, tech);
        if (!CivCanAfford(civ, price))
        {
            return $"{Content.Techs[tech].Name} costs {PriceText(price)}.";
        }

        CivPay(civ, price);
        civ.Researching = tech;
        civ.ResearchEnd = Ticks + ResearchTicks(civ, tech);
        _schedule.Enqueue((DoneResearch, civ.Id), (civ.ResearchEnd, DoneResearch, civ.Id));
        return null;
    }

    private void FinishResearch(Civ civ, long due)
    {
        if (civ.Researching < 0 || civ.ResearchEnd != due)
        {
            return;
        }

        int tech = civ.Researching;
        civ.KnownTech[tech] = true;
        civ.Researching = -1;
        News.Add((civ.Id, $"We have learned {Content.Techs[tech].Name}."));
        UpgradeUnits(civ, tech);
        AdvanceEra(civ, tell: true);

        civ.Dirty = true;
    }

    private string? Hire(Civ civ, bool advisor)
    {
        Person? offer = advisor ? civ.AdvisorOffer : civ.ThinkerOffer;
        if (offer is null)
        {
            return advisor ? "No advisor is offering service." : "No thinker is offering service.";
        }

        long[] price = HirePrice(offer, advisor);
        if (!CivCanAfford(civ, price))
        {
            return $"{offer.Name} asks for {PriceText(price)}.";
        }

        CivPay(civ, price);
        offer.Since = Ticks;
        if (advisor)
        {
            civ.Advisor = offer;
            civ.AdvisorOffer = null;
            civ.Dirty = true;
        }
        else
        {
            civ.Thinker = offer;
            civ.ThinkerOffer = null;
        }

        News.Add((civ.Id, $"{offer.Name} ({Content.Rarities[offer.Rarity].Name.ToLowerInvariant()}) joins us as {(advisor ? "advisor" : "thinker")}."));
        return null;
    }

    /// <summary>Price of hiring a person on offer.</summary>
    public long[] HirePrice(Person p, bool advisor)
    {
        ArgumentNullException.ThrowIfNull(p);
        long[] basePrice = advisor ? Content.AdvisorPrice : Content.ThinkerPrice;
        var price = new long[basePrice.Length];
        for (int g = 0; g < price.Length; g++)
        {
            price[g] = basePrice[g] * Content.Rarities[p.Rarity].PriceTimes;
        }

        return price;
    }

    // Every few seconds: thinkers retire to a successor, new candidates show up.
    private void PeopleOfNoteStep(Civ civ)
    {
        if (civ.Thinker is { } t && Ticks - t.Since >= Content.ThinkerRetireTicks)
        {
            string old = t.FullName;
            t.Generation++;
            t.Since = Ticks;
            News.Add((civ.Id, $"{old} retires; {t.FullName} carries on the teaching."));
        }

        if (Ticks >= civ.NextThinkerOffer)
        {
            civ.ThinkerOffer = NewPerson(civ, advisor: false);
            civ.NextThinkerOffer = Ticks + Content.ThinkerOfferTicks;
            News.Add((civ.Id, $"A {Content.Rarities[civ.ThinkerOffer.Rarity].Name.ToLowerInvariant()} thinker, {civ.ThinkerOffer.Name}, offers to teach us."));
        }

        if (Ticks >= civ.NextAdvisorOffer)
        {
            civ.AdvisorOffer = NewPerson(civ, advisor: true);
            civ.NextAdvisorOffer = Ticks + Content.AdvisorOfferTicks;
            News.Add((civ.Id, $"{civ.AdvisorOffer.Name} offers advice on {FieldsText(civ.AdvisorOffer)}."));
        }
    }

    /// <summary>The fields of an advisor as text.</summary>
    public string FieldsText(Person p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var names = new List<string>();
        foreach (int f in p.Fields)
        {
            names.Add(Content.AdvisorFields[f]);
        }

        return string.Join(" and ", names);
    }

    private Person NewPerson(Civ civ, bool advisor)
    {
        int total = 0;
        foreach (Rarity r in Content.Rarities)
        {
            total += r.Weight;
        }

        // Philosophy and the like make rare people more likely: the roll leans toward the end of the table.
        int luck = Bonus(civ, EffectKind.Luck, "*");
        int roll = Math.Min(total - 1, _events.NextInt(total) + (_events.NextInt(total) * luck / 100 / 2));
        int rarity = 0;
        for (; rarity < Content.Rarities.Count - 1 && roll >= Content.Rarities[rarity].Weight; rarity++)
        {
            roll -= Content.Rarities[rarity].Weight;
        }

        int[] fields = [];
        if (advisor)
        {
            int f1 = _events.NextInt(Content.AdvisorFields.Count);
            int f2 = (f1 + 1 + _events.NextInt(Content.AdvisorFields.Count - 1)) % Content.AdvisorFields.Count;
            fields = rarity >= 2 ? [f1, f2] : [f1];
        }

        string name = civ.Language?.PeopleName((ulong)(1000 + civ.NamesUsed++)) ?? $"Sage {civ.NamesUsed++}";
        return new Person { Name = name, Rarity = rarity, Fields = fields };
    }

    /// <summary>
    /// One piece of advice from the advisor, only in its fields (null when there is no advisor or nothing to say).
    /// Read by the server for the player's view; changes nothing.
    /// </summary>
    public string? Advice(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        if (civ.Advisor is not { } a)
        {
            return null;
        }

        bool Has(string field) => Array.Exists(a.Fields, f => Content.AdvisorFields[f] == field);
        if (civ.Event >= 0 && Has(Content.Events[civ.Event].Field))
        {
            return Content.Events[civ.Event].Advice;
        }

        if (Has("economy"))
        {
            if (civ.Rate[Content.Food] < 0)
            {
                return "Food is running down: build gatherers, hunters, fishers or farms.";
            }

            for (int g = 0; g < civ.Stock.Length; g++)
            {
                if (civ.Stock[g] >= civ.Capacity[g] && civ.Rate[g] > 0)
                {
                    return $"The store of {Content.Goods[g].Name.ToLowerInvariant()} is full: build a storehouse or use it.";
                }
            }

            if (civ.AnyStarved)
            {
                return "Some workshops wait for goods: produce what they use or stop them.";
            }
        }

        if (Has("growth"))
        {
            foreach (int sid in civ.Settlements)
            {
                Settlement s = Settlements[sid];
                if (!s.Abandoned && s.People >= s.Housing)
                {
                    return $"{s.Name} has no room to grow: build houses there.";
                }

                if (!s.Abandoned && s.Idle >= 6)
                {
                    return $"{s.Idle} people in {s.Name} have no work: build workplaces.";
                }
            }
        }

        if (Has("research"))
        {
            if (civ.Researching < 0)
            {
                for (int t = 0; t < Content.Techs.Count; t++)
                {
                    if (CannotResearch(civ, t) is null)
                    {
                        return $"We could research {Content.Techs[t].Name} now.";
                    }
                }

                return "Work in our oldest crafts to gain the experience new ideas need.";
            }
        }

        if (Has("expansion") && civ.Settlements.Count < 3 && civ.People >= 25)
        {
            return "We are many: train settlers and found a new camp on good land.";
        }

        return null;
    }
}
