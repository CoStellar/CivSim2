using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Ai;

// The AI's economy: what to build, research, thinkers, settlement upgrades and rebuilding (split from SimAi.cs, 2026-11-22).
public static partial class SimAi
{
    private static bool Economy(SimGame g, Civ civ)
    {
        // N26 (2026-11-19): the teacher of what research waits for jumps the queue of building sites (one more site allowed).
        int under = Under(g, civ);
        if (under < 2 + civ.Settlements.Count && Teacher(g, civ, missingOnly: true) is string first && (TryBuild(g, civ, first, keepSaving: false) || PriceMaker(g, civ, first)))
        {
            g.Apply(new PriorityCommand(civ.Id, civ.Buildings[^1], true));
            return true;
        }

        if (under >= 1 + civ.Settlements.Count)
        {
            return false;
        }

        // A hungry settlement gets its own food maker first (new camps eat more than their gatherers find).
        if (HungryCamp(g, civ))
        {
            return true;
        }

        long eating = Eating(g, civ);

        // 2026-11-22 (long runs: big peoples starved after 90 minutes): the summer surplus has to cover the winter, so outside
        // winter food made must exceed eating by ai.foodMarginPercent; a fixed +3 a minute meant nothing to thousands of
        // people. Big peoples may build several food makers at once (one more per ai.foodSitesPerPeople people).
        long food = NetFood(g, civ);
        bool lean = ShortOfFood(g, civ, eating);
        int sites = 1 + (civ.People / Math.Max(1, g.Content.AiFoodSitesPerPeople));
        if ((food < 3 * SimContent.One || civ.Hungry || lean) && Building(g, civ, FoodBuildings, sites) < sites && (civ.Hungry || Count(g, civ, FoodBuildings) <= civ.People / 5))
        {
            // Hungry: what feeds fastest and needs no tools comes first.
            foreach (string k in civ.Hungry ? ["gatherers_hut", "hunters_lodge", "fishing_hut", "farm", "pasture"] : FoodBuildings)
            {
                if (TryBuild(g, civ, k))
                {
                    return true;
                }
            }
        }

        int idle = 0;
        bool crowded = false;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            idle += s.Idle;
            int most = g.Content.Levels[s.Level].MaxHousing;
            crowded |= s.People >= s.Housing - 4 && (most <= 0 || s.Housing < most); // 9B: no homes past the level's limit
        }

        // 16B (Patryk, 2026-11-20): new homes only with food on hand for homesFoodMinutes of eating; without it more people
        // only starve the next winter. 2026-11-22: and only while the food made covers the winter too (not in winter, not
        // short of the margin above): the store alone let big peoples outgrow their fields.
        bool fed = civ.Stock[g.Content.Food] >= eating * g.Content.AiHomesFoodMinutes && !g.IsWinter && !lean;
        if (crowded && fed && (TryBuild(g, civ, "stone_house") || TryBuild(g, civ, "hut")))
        {
            return true;
        }

        for (int i = 0; i < civ.Stock.Length; i++)
        {
            if (civ.Stock[i] >= civ.Capacity[i] - SimContent.One && civ.Rate[i] > 0)
            {
                if (TryBuild(g, civ, "great_storehouse") || TryBuild(g, civ, "storehouse"))
                {
                    return true;
                }
            }
        }

        // What teaches the experience the next technology lacks comes first, even without idle hands: it gets workers first.
        string? learn = Teacher(g, civ, missingOnly: true) ?? Teacher(g, civ);
        if (learn is not null && !Has(g, civ, learn) && (TryBuild(g, civ, learn, keepSaving: false) || PriceMaker(g, civ, learn)))
        {
            g.Apply(new PriorityCommand(civ.Id, civ.Buildings[^1], true));
            return true;
        }

        // The teacher stands already: it gets workers first.
        if (learn is not null)
        {
            foreach (int id in civ.Buildings)
            {
                Building b = g.Buildings[id];
                if (!b.Priority && b.State == BuildingState.Active && g.Content.Buildings[b.Type].Key == learn)
                {
                    g.Apply(new PriorityCommand(civ.Id, id, true));
                    break;
                }
            }
        }

        // N22 (2026-11-19, era pacing): a good the saved-for technology lacks, with nobody making it, gets its maker even
        // without idle hands (like the teacher above).
        if (civ.AiSaving is { } save)
        {
            for (int i = 0; i < save.Length; i++)
            {
                if (save[i] <= 0 || civ.Stock[i] >= save[i] || i == g.Content.Food)
                {
                    continue;
                }

                string[] makers = Makers(g, g.Content.Goods[i].Key);
                if (makers.Length > 0 && Count(g, civ, makers) == 0)
                {
                    foreach (string k in makers)
                    {
                        if (TryBuild(g, civ, k, keepSaving: false))
                        {
                            g.Apply(new PriorityCommand(civ.Id, civ.Buildings[^1], true));
                            return true;
                        }
                    }
                }
            }
        }

        // Late eras (2026-11-21): a workshop whose input nobody makes (a bronze smith with no charcoal) gets its maker.
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            if (b.State == BuildingState.Active && InputMaker(g, civ, g.Content.Buildings[b.Type]) is string maker && TryBuild(g, civ, maker, keepSaving: false))
            {
                return true;
            }
        }

        if (idle < 3)
        {
            return false;
        }

        // Work for the idle: the good we have least of.
        string[] order = Makers(g, Wanted(g, civ) ?? Lowest(g, civ));
        if (order.Length == 0)
        {
            order = ["gatherers_hut", "hunters_lodge"];
        }

        foreach (string k in order)
        {
            if (TryBuild(g, civ, k))
            {
                return true;
            }
        }

        foreach (string k in new[] { "school", "academy", "farm", "pasture", "woodcutter", "quarry", "mason", "clay_pit", "toolmaker", "copper_mine", "tin_mine", "bronze_smith", "iron_mine", "blacksmith", "potter", "tailor", "herbalist", "gatherers_hut", "hunters_lodge" })
        {
            if (TryBuild(g, civ, k))
            {
                return true;
            }
        }

        return false;
    }

    // Buildings that make a good, best first ([] = none the AI builds for it).
    private static string[] Makers(SimGame g, string good) => good switch
    {
        "wood" => ["woodcutter", "gatherers_hut"],
        "stone" => ["quarry"],
        "clay" => ["clay_pit"],
        "tools" => ["toolmaker", "blacksmith", "steelworks"],
        "copper" => ["copper_mine"],
        "tin" => ["tin_mine"],
        "bronze" => ["bronze_smith"],
        "iron" => ["iron_mine"],
        "hides" => ["hunters_lodge"],
        "pottery" => ["potter"],
        "charcoal" => ["charcoal_burner"],
        "bricks" => ["brickworks"],
        "planks" => ["sawmill"],
        "mortar" => ["lime_kiln"],
        "wool" => ["sheepfold"],
        "cloth" => ["cloth_weaver"],
        "textiles" => ["weaver"],
        _ => [],
    };

    // Eating of the whole people a minute: residents, units and the advisor.
    internal static long Eating(SimGame g, Civ civ)
    {
        long eating = 0;
        foreach (int sid in civ.Settlements)
        {
            eating += g.Settlements[sid].Eating;
        }

        return eating;
    }

    // Outside winter: food made a minute falls short of eating plus ai.foodMarginPercent (the winter would eat the store).
    internal static bool ShortOfFood(SimGame g, Civ civ, long eating) =>
        !g.IsWinter && civ.MadeRate[g.Content.Food] * 100 < eating * (100 + g.Content.AiFoodMarginPercent);

    // More housing (a settlement level, better homes) waits while the food made does not cover the winter (2026-11-22).
    internal static bool Lean(SimGame g, Civ civ) => civ.Hungry || ShortOfFood(g, civ, Eating(g, civ));

    // A building with jobs whose workers give the experience the nearest technology still lacks.
    // N25 (2026-11-19, gateways): the unknown technologies on the way to the era's gateway: the gateway, what it requires and the
    // technologies it asks experience in, two steps back.
    private static bool[] GatewayWay(SimGame g, Civ civ)
    {
        var way = new bool[g.Content.Techs.Count];
        int gate = g.Content.GatewayOf(civ.Era);
        if (gate < 0 || civ.KnownTech[gate])
        {
            return way;
        }

        Mark(gate, 3);

        // Late eras (2026-11-21): the technologies of the makers of what the next era asks to have made (bricks) are on the way too.
        int next = civ.Era + 1;
        if (next < g.Content.EraEntryMade.Length)
        {
            long[] need = g.Content.EraEntryMade[next];
            for (int good = 0; good < need.Length; good++)
            {
                if (need[good] <= 0)
                {
                    continue;
                }

                foreach (BuildingType b in g.Content.Buildings)
                {
                    if (b.Id > 0 && b.Produces[good] > 0 && b.Tech >= 0)
                    {
                        Mark(b.Tech, 3);
                    }
                }
            }
        }

        return way;

        void Mark(int tech, int depth)
        {
            if (civ.KnownTech[tech] || way[tech] || depth == 0)
            {
                return;
            }

            way[tech] = true;
            SimTech t = g.Content.Techs[tech];
            foreach (int req in t.Requires)
            {
                Mark(req, depth - 1);
            }

            foreach ((int exp, long _) in t.Experience)
            {
                Mark(exp, depth - 1);
            }
        }
    }

    // The buildable maker of an input the building lacks (none in store and none made), or null.
    private static string? InputMaker(SimGame g, Civ civ, BuildingType b)
    {
        for (int i = 0; i < b.Consumes.Length; i++)
        {
            if (b.Consumes[i] <= 0 || civ.Stock[i] >= 5 * SimContent.One || civ.Rate[i] > 0)
            {
                continue;
            }

            foreach (BuildingType m in g.Content.Buildings)
            {
                // Late eras (2026-11-21): one charcoal burner does not feed every smith; another maker goes up while the good
                // runs out and none is being built.
                if (m.Id > 0 && m.Produces[i] > 0 && SimGame.Knows(civ, m) && !Building(g, civ, [m.Key]) && Buildable(g, civ, m))
                {
                    return m.Key;
                }
            }
        }

        return null;
    }

    internal static string? Teacher(SimGame g, Civ civ, bool missingOnly = false)
    {
        // 7A (2026-11-20): goods the next era asks to have made (iron age: bronze) come first once its gateway is known.
        int nextEra = civ.Era + 1;
        int gate0 = g.Content.GatewayOf(civ.Era);
        if (nextEra < g.Content.EraEntryMade.Length && (gate0 < 0 || civ.KnownTech[gate0]))
        {
            long[] need = g.Content.EraEntryMade[nextEra];
            for (int good = 0; good < need.Length; good++)
            {
                if (need[good] <= 0 || civ.Made[good] / SimContent.TicksPerMinute >= need[good])
                {
                    continue;
                }

                foreach (BuildingType b in g.Content.Buildings)
                {
                    if (b.Id <= 0 || b.Produces[good] <= 0 || !SimGame.Knows(civ, b))
                    {
                        continue;
                    }

                    if (!Has(g, civ, b.Key))
                    {
                        if (Buildable(g, civ, b))
                        {
                            return b.Key;
                        }

                        continue;
                    }

                    // The maker stands: what it lacks (bronze waited for tin) is the next thing to build.
                    if (InputMaker(g, civ, b) is string input)
                    {
                        return input;
                    }
                }
            }
        }

        // The gateway's way first, then the rest in the order of the tree.
        bool[] way = GatewayWay(g, civ);
        var order = new List<SimTech>(g.Content.Techs.Count);
        foreach (SimTech x in g.Content.Techs)
        {
            if (way[x.Id])
            {
                order.Add(x);
            }
        }

        foreach (SimTech x in g.Content.Techs)
        {
            if (!way[x.Id])
            {
                order.Add(x);
            }
        }

        foreach (SimTech t in order)
        {
            if (civ.KnownTech[t.Id])
            {
                continue;
            }

            // Goods to make: the first building that makes the missing good.
            foreach ((int good, long amount) in t.Made)
            {
                if (civ.Made[good] / SimContent.TicksPerMinute >= amount)
                {
                    continue;
                }

                foreach (BuildingType b in g.Content.Buildings)
                {
                    // AI goods (2026-11-20): only a maker that can stand somewhere (a mine needs its deposit in our land);
                    // otherwise the next technology gets its turn and settlers look for the deposit (WantedDeposits).
                    if (b.Id > 0 && b.Produces[good] > 0 && SimGame.Knows(civ, b) && !(missingOnly && Has(g, civ, b.Key)) && (Has(g, civ, b.Key) || Buildable(g, civ, b)))
                    {
                        return b.Key;
                    }
                }
            }

            foreach ((int req, long need) in t.Experience)
            {
                if (!civ.KnownTech[req] || civ.Experience[req] >= need)
                {
                    continue;
                }

                foreach (BuildingType b in g.Content.Buildings)
                {
                    // N23 (2026-11-19): a teacher that lacks its inputs (a toolmaker with no stone) asks for their maker first.
                    if (b.Id > 0 && b.Tech == req && b.Workers > 0 && b.Consumes.Length > 0 && !NoInputsMissing(civ, b) && SimGame.Knows(civ, b))
                    {
                        for (int i = 0; i < b.Consumes.Length; i++)
                        {
                            if (b.Consumes[i] > 0 && civ.Rate[i] <= 0 && civ.Stock[i] < 20 * SimContent.One)
                            {
                                foreach (string k in Makers(g, g.Content.Goods[i].Key))
                                {
                                    int id = Key(g, k);
                                    if (id >= 0 && SimGame.Knows(civ, g.Content.Buildings[id]) && !Has(g, civ, k))
                                    {
                                        return k;
                                    }
                                }
                            }
                        }
                    }

                    if (b.Id > 0 && b.Tech == req && b.Workers > 0 && (b.Consumes.Length == 0 || NoInputsMissing(civ, b)))
                    {
                        if (Buildable(g, civ, b) && !(missingOnly && Has(g, civ, b.Key)))
                        {
                            return b.Key;
                        }
                    }
                }
            }
        }

        return null;
    }

    // AI goods (2026-11-20): a teacher whose price asks for a good nobody makes (a farm needs tools, no toolmaker stands)
    // gets that good's maker built first; otherwise neither ever came.
    private static bool PriceMaker(SimGame g, Civ civ, string key)
    {
        int id = Key(g, key);
        if (id < 0)
        {
            return false;
        }

        long[] price = g.Content.Buildings[id].Price;
        for (int i = 0; i < price.Length; i++)
        {
            if (price[i] <= 0 || civ.Stock[i] >= price[i])
            {
                continue;
            }

            string[] makers = Makers(g, g.Content.Goods[i].Key);
            if (makers.Length == 0 || Count(g, civ, makers) > 0)
            {
                continue;
            }

            foreach (string k in makers)
            {
                if (TryBuild(g, civ, k, keepSaving: false))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // AI goods (2026-11-20): a building that uses goods goes where they lie or are made: up to 20 for each input.
    private static int InputsAtHand(Settlement s, BuildingType t)
    {
        int score = 0;
        for (int i = 0; i < t.Consumes.Length; i++)
        {
            if (t.Consumes[i] > 0)
            {
                score += (int)Math.Min(20, (s.Stock[i] / SimContent.One / 2) + (s.Rate[i] > 0 ? 10 : 0));
            }
        }

        return score;
    }

    // True when the building may stand on some hex of our settlements (its deposit, water and a free place).
    // Tick spikes (2026-11-20): Buildable looks at every hex of every settlement; one thought asks for the same building many
    // times (the teacher is sought up to three times), so the answers are kept for the thought.
    // Per thread (games may run side by side in tests and tools) and per game.
    [ThreadStatic]
    private static SimGame? _buildableGame;

    [ThreadStatic]
    private static long _buildableTick;

    [ThreadStatic]
    private static int _buildableCiv;

    [ThreadStatic]
    private static sbyte[]? _buildable;

    private static bool Buildable(SimGame g, Civ civ, BuildingType b)
    {
        if (_buildable is null || _buildableGame != g || _buildableTick != g.Ticks || _buildableCiv != civ.Id || _buildable.Length != g.Content.Buildings.Count)
        {
            _buildableGame = g;
            _buildableTick = g.Ticks;
            _buildableCiv = civ.Id;
            if (_buildable is null || _buildable.Length != g.Content.Buildings.Count)
            {
                _buildable = new sbyte[g.Content.Buildings.Count];
            }

            Array.Fill(_buildable, (sbyte)-1);
        }

        if (_buildable[b.Id] < 0)
        {
            _buildable[b.Id] = (sbyte)(BuildableNow(g, civ, b) ? 1 : 0);
        }

        return _buildable[b.Id] == 1;
    }

    private static bool BuildableNow(SimGame g, Civ civ, BuildingType b)
    {
        foreach (int sid in civ.Settlements)
        {
            foreach (int cell in g.Settlements[sid].Cells)
            {
                if (g.CannotBuild(civ, cell, b, checkPrice: false) is null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // AI goods (2026-11-20): deposits the way to the era's gateway needs a mine on and none of our settlements has (bronze
    // waited for copper outside the camp's land). Settlers look for them first.
    private static bool[] WantedDeposits(SimGame g, Civ civ)
    {
        var want = new bool[g.Content.DepositSurvey.Length];
        bool[] way = GatewayWay(g, civ);
        foreach (SimTech t in g.Content.Techs)
        {
            if (!way[t.Id] || civ.KnownTech[t.Id])
            {
                continue;
            }

            foreach ((int good, long amount) in t.Made)
            {
                if (civ.Made[good] / SimContent.TicksPerMinute >= amount)
                {
                    continue;
                }

                foreach (BuildingType b in g.Content.Buildings)
                {
                    if (b.Id > 0 && b.Produces[good] > 0 && b.OrDeposit > 0 && b.OrDeposit < want.Length
                        && (SimGame.Knows(civ, b) || (b.Tech >= 0 && way[b.Tech])) && !Has(g, civ, b.Key) && !Buildable(g, civ, b))
                    {
                        want[b.OrDeposit] = true;
                    }
                }
            }
        }

        // 7A: the inputs of what the next era asks to have made (bronze: copper and tin), when only a mine makes them.
        if (civ.Era + 1 < g.Content.EraEntryMade.Length)
        {
            long[] need = g.Content.EraEntryMade[civ.Era + 1];
            for (int good = 0; good < need.Length; good++)
            {
                if (need[good] <= 0 || civ.Made[good] / SimContent.TicksPerMinute >= need[good])
                {
                    continue;
                }

                foreach (BuildingType maker in g.Content.Buildings)
                {
                    if (maker.Id <= 0 || maker.Produces[good] <= 0)
                    {
                        continue;
                    }

                    for (int i = 0; i < maker.Consumes.Length; i++)
                    {
                        if (maker.Consumes[i] <= 0 || civ.Rate[i] > 0)
                        {
                            continue;
                        }

                        foreach (BuildingType mine in g.Content.Buildings)
                        {
                            if (mine.Id > 0 && mine.Produces[i] > 0 && mine.OrDeposit > 0 && mine.OrDeposit < want.Length && SimGame.Knows(civ, mine)
                                && !Has(g, civ, mine.Key) && !Buildable(g, civ, mine))
                            {
                                want[mine.OrDeposit] = true;
                            }
                        }
                    }
                }
            }
        }

        // 7A (Patryk, 2026-11-20): experience that only a mine gives (iron working: an iron mine) for the technologies of our era.
        // Without such a mine in our land, settlers go to the deposit ("a settlement by the iron ore").
        foreach (SimTech t in g.Content.Techs)
        {
            if (civ.KnownTech[t.Id] || t.Era != civ.Era)
            {
                continue;
            }

            foreach ((int req, long need) in t.Experience)
            {
                if (!civ.KnownTech[req] || civ.Experience[req] >= need)
                {
                    continue;
                }

                foreach (BuildingType b in g.Content.Buildings)
                {
                    if (b.Id > 0 && b.Tech == req && b.Workers > 0 && b.OrDeposit > 0 && b.OrDeposit < want.Length && SimGame.Knows(civ, b)
                        && !Has(g, civ, b.Key) && !Buildable(g, civ, b))
                    {
                        want[b.OrDeposit] = true;
                    }
                }
            }
        }

        return want;
    }

    private static bool NoInputsMissing(Civ civ, BuildingType b)
    {
        for (int i = 0; i < b.Consumes.Length; i++)
        {
            if (b.Consumes[i] > 0 && civ.Rate[i] <= 0 && civ.Stock[i] < 20 * SimContent.One)
            {
                return false;
            }
        }

        return true;
    }

    // A good the next technology's price lacks.
    private static string? Wanted(SimGame g, Civ civ)
    {
        foreach (SimTech t in g.Content.Techs)
        {
            if (g.CannotResearch(civ, t.Id) is not null)
            {
                continue;
            }

            long[] price = g.ResearchPrice(civ, t.Id);
            for (int i = 0; i < price.Length; i++)
            {
                if (civ.Stock[i] < price[i] && i != g.Content.Food)
                {
                    return g.Content.Goods[i].Key;
                }
            }
        }

        return null;
    }

    // The known good (one we can make) whose stock is lowest.
    private static string Lowest(SimGame g, Civ civ)
    {
        string[] makeable = ["wood", "stone", "clay", "tools", "copper", "tin", "bronze", "iron", "charcoal", "bricks", "planks", "mortar", "cloth"];
        string best = "";
        long bestStock = long.MaxValue;
        foreach (string k in makeable)
        {
            int good = g.Content.GoodId(k);
            bool can = false;
            foreach (BuildingType b in g.Content.Buildings)
            {
                can |= b.Id > 0 && b.Produces[good] > 0 && SimGame.Knows(civ, b);
            }

            if (can && civ.Stock[good] < bestStock)
            {
                best = k;
                bestStock = civ.Stock[good];
            }
        }

        return best;
    }

    // N21 (2026-11-19, era pacing): with nothing affordable, the AI saves the price of the technology it wants most, so
    // buildings and soldiers do not eat it up; food buildings still go first.
    private static void Research(SimGame g, Civ civ)
    {
        if (civ.Researching >= 0)
        {
            civ.AiSaving = null;
            return;
        }

        bool[] way = GatewayWay(g, civ);
        int pick = -1;
        int want = -1;
        int wantScore = int.MinValue;
        int bestScore = int.MinValue;
        foreach (SimTech t in g.Content.Techs)
        {
            if (g.CannotResearch(civ, t.Id) is not null)
            {
                continue;
            }

            // Warlike peoples like the technologies of arms, builders the others; earlier eras first, the cheap basics before all.
            int score = (-t.Era * 10) + (t.Basic ? 30 : 0) + (way[t.Id] ? 25 : 0); // the gateway and its way open the next era (M1, N25)
            foreach (UnitType u in g.Content.Units)
            {
                if (u.Army && u.Tech == t.Id)
                {
                    score += (civ.Aggression - 50) / 5;
                }
            }

            long[] price = g.ResearchPrice(civ, t.Id);
            if (score > bestScore && Affordable(civ, price, keepSaving: false))
            {
                pick = t.Id;
                bestScore = score;
            }

            // Wanted most: the best score, then the cheapest.
            long total = 0;
            foreach (long x in price)
            {
                total += x;
            }

            score -= (int)Math.Min(1000, total / SimContent.One / 20);
            if (score > wantScore)
            {
                want = t.Id;
                wantScore = score;
            }
        }

        if (pick >= 0)
        {
            civ.AiSaving = null;
            g.Apply(new ResearchCommand(civ.Id, pick));
        }
        else
        {
            civ.AiSaving = want >= 0 ? g.ResearchPrice(civ, want) : null;
        }
    }

    private static void Hire(SimGame g, Civ civ)
    {
        if (civ.ThinkerOffer is { } p && civ.Thinker is { } cur && p.Rarity > cur.Rarity && Affordable(civ, g.HirePrice(p, false), 50))
        {
            g.Apply(new HireCommand(civ.Id, false));
        }
    }

    internal static bool Upgrade(SimGame g, Civ civ)
    {
        if (Lean(g, civ))
        {
            return false;
        }

        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            bool waited = s.UpgradeFailedTick < 0 || g.Ticks - s.UpgradeFailedTick >= g.Content.UpgradeCaravans.AiRetryTicks; // A1: not again at once
            if (waited && !s.UpgradeWaiting && s.Level + 1 < g.Content.Levels.Count && s.People >= s.Housing - 2 && Affordable(civ, g.Content.Levels[s.Level + 1].UpgradePrice, 20)
                && g.Apply(new UpgradeSettlementCommand(civ.Id, sid)) is null)
            {
                return true;
            }
        }

        return false;
    }

    // A building into its better kind (huts into stone houses, toolmaker into blacksmith...), one at a time, when the rest of
    // the price leaves a fifth of the stores; houses first when homes run short.
    private static bool Rebuild(SimGame g, Civ civ)
    {
        if (Under(g, civ) >= 1 + civ.Settlements.Count)
        {
            return false;
        }

        bool lean = Lean(g, civ);
        foreach (bool homes in new[] { true, false })
        {
            if (homes && lean)
            {
                continue;
            }

            foreach (int id in civ.Buildings.ToArray())
            {
                Building b = g.Buildings[id];
                BuildingType t = g.Content.Buildings[b.Type];
                if (t.UpgradesTo <= 0 || (t.Housing > 0) != homes)
                {
                    continue;
                }

                Settlement s = g.Settlements[b.Settlement];
                if (homes && s.People < s.Housing - 4)
                {
                    continue;
                }

                if (g.CannotUpgrade(civ, b, out BuildingType? to) is null && Affordable(civ, g.UpgradeRest(t, to!), 20)
                    && g.Apply(new UpgradeBuildingCommand(civ.Id, b.Id)) is null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Food a people makes a minute after everyone's rations (residents and units; the rate shows the rations per minute).
    private static long NetFood(SimGame g, Civ civ) => civ.Rate[g.Content.Food];
}
