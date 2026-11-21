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
                    score += 20;
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

        long food = NetFood(g, civ);
        if ((food < 3 * SimContent.One || civ.Hungry) && !Building(g, civ, FoodBuildings) && (civ.Hungry || Count(g, civ, FoodBuildings) <= civ.People / 5))
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
        // only starve the next winter.
        long eating = 0;
        foreach (int sid in civ.Settlements)
        {
            eating += g.Settlements[sid].Eating;
        }

        bool fed = civ.Stock[g.Content.Food] >= eating * g.Content.AiHomesFoodMinutes;
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

                string[] makers = Makers(g.Content.Goods[i].Key);
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

        if (idle < 3)
        {
            return false;
        }

        // Work for the idle: the good we have least of.
        string[] order = Makers(Wanted(g, civ) ?? Lowest(g, civ));
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
    private static string[] Makers(string good) => good switch
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
        _ => [],
    };

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
                if (m.Id > 0 && m.Produces[i] > 0 && SimGame.Knows(civ, m) && !Has(g, civ, m.Key) && Buildable(g, civ, m))
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
                                foreach (string k in Makers(g.Content.Goods[i].Key))
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

            string[] makers = Makers(g.Content.Goods[i].Key);
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
        string[] makeable = ["wood", "stone", "clay", "tools", "copper", "tin", "bronze", "iron"];
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

    private static bool Upgrade(SimGame g, Civ civ)
    {
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

        foreach (bool homes in new[] { true, false })
        {
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

    // Hexes a scout's way search may look at: a target ~14 hexes off needs far fewer; one beyond a river or a sea fails fast.
    private const int ScoutSearchLimit = 1500;

    // 17B (Patryk, 2026-11-20): a people this small needs every person at home.
    private static bool Small(SimGame g, Civ civ) => civ.People < g.Content.AiSmallPeople;

    private static void Scouts(SimGame g, Civ civ)
    {
        UnitType scouts = g.Content.Unit("scouts");
        if (Own(g, civ, t => t.Key == "scouts") == 0 && g.Ticks < 30 * SimContent.TicksPerMinute && Affordable(civ, scouts.Price, 50) && !Small(g, civ)) // 17B: later, once 30 people
        {
            g.Apply(new TrainCommand(civ.Id, civ.Settlements[0], scouts.Id));
        }

        foreach (int id in civ.Units.ToArray())
        {
            Unit u = g.Units[id];
            if (u.Kind == scouts.Id && u.State == UnitState.Idle && g.Ticks >= civ.AiScoutWait)
            {
                // Walk to an unknown land hex some way off; the hex is picked from the AI's own random stream. Tick spikes
                // (2026-11-20): each refused try searched a way over the whole map (water, other continents), up to 12 a
                // thought; now only land hexes are tried, and one search a thought.
                int searches = 0;
                for (int tries = 0; tries < 12 && searches < 1; tries++)
                {
                    HexCoord c = g.Geo.FromIndex(u.Cell);
                    int dc = g.AiRandom.NextInt(-14, 15);
                    int dr = g.AiRandom.NextInt(-10, 11);
                    int cell = g.Geo.Index(g.Geo.Wrap(c.Col + dc, Math.Clamp(c.Row + dr, 0, g.Geo.Height - 1)));
                    if (civ.Known[cell] || g.Content.World.Biomes[g.Map.Biome[cell]].IsWater)
                    {
                        continue;
                    }

                    searches++;
                    if (g.Apply(new MoveCommand(civ.Id, u.Id, cell, false, null, ScoutSearchLimit)) is null)
                    {
                        searches = -1;
                        break;
                    }
                }

                if (searches > 0)
                {
                    civ.AiScoutWait = g.Ticks + (30 * SimContent.TicksPerSecond); // nothing reachable around: look again later
                }
            }
        }
    }

    // Prospectors and surveyors (2026-11-19): one at a time once the people has three settlements (prospectors from the
    // Neolithic, when ores can be found; surveyors before); it walks to ground around
    // our settlements that it has not looked at yet, stands there (surveys), then goes on. Same rules as for the player.
    private static void Surveys(SimGame g, Civ civ)
    {
        // Not before the people has spread a little: early on every person is needed at home.
        if (g.Ticks < 15 * SimContent.TicksPerMinute || civ.Settlements.Count < 3 || civ.Hungry)
        {
            return;
        }

        bool any = Own(g, civ, x => x.Survey > 0) > 0; // one at a time: prospectors once ores matter, else surveyors

        foreach (UnitType t in g.Content.Units)
        {
            if (t.Survey == 0 || !SimGame.Knows(civ, t))
            {
                continue;
            }

            bool wanted = t.Survey == 1 ? civ.Era >= 1 : civ.Era == 0;
            if (!any && wanted && !Small(g, civ) && Own(g, civ, x => x.Founds) == 0 && Affordable(civ, t.Price, 50)
                && Own(g, civ, _ => true) == Own(g, civ, _ => true, includeTraining: false))
            {
                g.Apply(new TrainCommand(civ.Id, civ.Settlements[0], t.Id));
            }

            foreach (int id in civ.Units.ToArray())
            {
                Unit u = g.Units[id];
                if (u.Kind != t.Id || u.State != UnitState.Idle || u.SurveyedAt != (u.Cell * 16) + civ.Era)
                {
                    continue; // still walking, or not yet looked around where it stands
                }

                // Targets come from a hash of the unit and the time, not from the AI's random stream, so surveys do not
                // change the AI's other choices.
                ulong z = ((ulong)g.Ticks * 0x9E3779B97F4A7C15UL) ^ ((ulong)(u.Id + 1) * 0xBF58476D1CE4E5B9UL);
                Settlement home = g.Settlements[civ.Settlements[(int)(Mix(ref z) % (ulong)civ.Settlements.Count)]];
                HexCoord c = g.Geo.FromIndex(home.Center);
                // 7A (2026-11-20): while a deposit we need is not found (iron 15-20 hexes away), prospectors look farther.
                bool far = t.Survey == 1 && Array.IndexOf(WantedDeposits(g, civ), true) >= 0;
                int wide = far ? 22 : 12;
                int tall = far ? 18 : 9;
                for (int tries = 0; tries < 16; tries++)
                {
                    int dc = (int)(Mix(ref z) % (ulong)((2 * wide) + 1)) - wide;
                    int dr = (int)(Mix(ref z) % (ulong)((2 * tall) + 1)) - tall;
                    int cell = g.Geo.Index(g.Geo.Wrap(c.Col + dc, Math.Clamp(c.Row + dr, 0, g.Geo.Height - 1)));
                    bool looked = t.Survey == 1 ? g.RockLookedAt(civ, cell) : g.KnowsSoil(civ, cell);
                    if (civ.Known[cell] && !looked && g.OwnerAt[cell] < 0 && !g.Content.World.Biomes[g.Map.Biome[cell]].IsWater
                        && g.Apply(new MoveCommand(civ.Id, u.Id, cell, false)) is null)
                    {
                        break;
                    }
                }
            }
        }
    }

    private static ulong Mix(ref ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        ulong x = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    // Food a people makes a minute after everyone's rations (residents and units; the rate shows the rations per minute).
    private static long NetFood(SimGame g, Civ civ) => civ.Rate[g.Content.Food];

    private static void Settlers(SimGame g, Civ civ)
    {
        UnitType settlers = g.Content.Unit("settlers");
        int cap = 2 + (civ.Expansion / 25) + g.EraOf(civ);
        if (DepositSite(g, civ, g.Settlements[civ.Settlements[0]].Center) >= 0)
        {
            cap++; // 7A (2026-11-20): one more camp for a deposit the way on needs (tin, iron) when one is known
        }

        if (Own(g, civ, t => t.Founds) == 0 && civ.Settlements.Count < cap && NetFood(g, civ) > 0)
        {
            foreach (int sid in civ.Settlements)
            {
                Settlement s = g.Settlements[sid];
                if (s.People >= 20 && Affordable(civ, settlers.Price, 30, keepSaving: false)) // spreading goes before saving
                {
                    g.Apply(new TrainCommand(civ.Id, sid, settlers.Id));
                    break;
                }
            }
        }

        foreach (int id in civ.Units.ToArray())
        {
            Unit u = g.Units[id];
            if (u.Kind != settlers.Id || u.State != UnitState.Idle)
            {
                continue;
            }

            if (g.CannotFound(u.Cell, civ) is null)
            {
                g.Apply(new FoundCommand(civ.Id, u.Id));
                continue;
            }

            int site = BestSite(g, civ, u.Cell);
            if (site >= 0)
            {
                g.Apply(new MoveCommand(civ.Id, u.Id, site, false));
            }
        }
    }

    // A known free land hex 6–12 hexes away where a camp may stand, with the most food-friendly land around: the biome and
    // water, plus what our prospectors and surveyors found within 2 hexes (known soil and deposits only, 2026-11-19).
    // 7A (2026-11-20): a free place to found a camp within 2 hexes of a known deposit the way on needs and none of our
    // settlements holds, the nearest to us within 25 hexes (−1 = none).
    private static int DepositSite(SimGame g, Civ civ, int from)
    {
        bool[] wanted = WantedDeposits(g, civ);
        if (Array.IndexOf(wanted, true) < 0)
        {
            return -1;
        }

        HexCoord c = g.Geo.FromIndex(from);
        Span<HexCoord> around = stackalloc HexCoord[HexGlobe.RangeSize(2)];
        var area = new HexCoord[HexGlobe.RangeSize(25)];
        int n = g.Geo.Range(c, 25, area);
        int best = -1;
        int bestD = int.MaxValue;
        for (int k = 0; k < n; k++)
        {
            int cell = g.Geo.Index(area[k]);
            int d = g.Map.Deposit[cell];
            if (d <= 0 || d >= wanted.Length || !wanted[d] || g.OwnerAt[cell] >= 0 || !g.KnowsDeposit(civ, cell))
            {
                continue;
            }

            int m = g.Geo.Range(area[k], 2, around);
            for (int j = 0; j < m; j++)
            {
                int site = g.Geo.Index(around[j]);
                int dist = g.Geo.Distance(c, around[j]);
                if (dist < bestD && civ.Known[site] && g.Map.Relief[site] < 2 && g.CannotFound(site, civ) is null)
                {
                    best = site;
                    bestD = dist;
                }
            }
        }

        return best;
    }

    private static int BestSite(SimGame g, Civ civ, int from)
    {
        if (DepositSite(g, civ, from) is int deposit and >= 0)
        {
            return deposit;
        }

        HexCoord c = g.Geo.FromIndex(from);
        int best = -1;
        int bestScore = int.MinValue;
        bool[] wanted = WantedDeposits(g, civ);
        var ring = new HexCoord[HexGlobe.RingSize(12)];
        for (int r = 6; r <= 12; r += 2)
        {
            int n = g.Geo.Ring(c, r, ring);
            for (int k = 0; k < n; k += 2)
            {
                int cell = g.Geo.Index(ring[k]);
                if (!civ.Known[cell] || g.CannotFound(cell, civ) is not null)
                {
                    continue;
                }

                int score = (g.Content.World.Biomes[g.Map.Biome[cell]].Food * 10) + (g.NextToWater(cell) ? 15 : 0) - r + KnownGround(g, civ, cell, wanted);
                if (score > bestScore)
                {
                    best = cell;
                    bestScore = score;
                }
            }
        }

        return best;
    }

    // What we know of the ground around a hex is worth: fertile soil, food in the soil and on it, stone, clay and ores.
    private static int KnownGround(SimGame g, Civ civ, int cell, bool[] wanted)
    {
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(2)];
        int n = g.Geo.Range(g.Geo.FromIndex(cell), 2, area);
        int score = 0;
        for (int k = 0; k < n; k++)
        {
            int c = g.Geo.Index(area[k]);
            if (g.KnowsSoil(civ, c))
            {
                score += g.Map.Fertility[c] / 40;
            }

            if (g.KnowsDeposit(civ, c))
            {
                int kind = g.Content.DepositSurvey[g.Map.Deposit[c]];
                score += kind == 1 ? 6 : 4; // rock (stone, clay, ores) or food (game, fish, grain, herds)
                score += g.Map.Deposit[c] < wanted.Length && wanted[g.Map.Deposit[c]] && g.Mineable(c) ? 60 : 0; // what the gateway waits for
            }
        }

        return score;
    }

    private static UnitType? BestSoldier(SimGame g, Civ civ)
    {
        UnitType? best = null;
        foreach (UnitType t in g.Content.Units)
        {
            if (t.Army && !t.Levy && !t.Naval && SimGame.Knows(civ, t) && (best is null || t.Strength > best.Strength))
            {
                best = t;
            }
        }

        return best;
    }

    private static void Army(SimGame g, Civ civ)
    {
        int soldiers = 0;
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            soldiers += g.Content.Units[u.Kind].Army ? u.People : 0;
        }

        int want = civ.People * (5 + (civ.Aggression / 5)) / 100;
        if (soldiers >= want || NetFood(g, civ) < 2 * SimContent.One || Small(g, civ))
        {
            return;
        }

        UnitType? t = BestSoldier(g, civ);
        if (t is null || !Affordable(civ, t.Price, 20))
        {
            return;
        }

        // Rations: the settlement that trains them feeds them, so the one with the most food to spare trains them, and only if
        // it can feed them as well as its own people.
        Settlement? best = null;
        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (s.People >= t.People + 15 && s.Rate[g.Content.Food] >= t.People * g.Content.FoodPerPersonPerMinute
                && (best is null || s.Rate[g.Content.Food] > best.Rate[g.Content.Food]))
            {
                best = s;
            }
        }

        if (best is not null)
        {
            g.Apply(new TrainCommand(civ.Id, best.Id, t.Id));
        }
    }

    private static void War(SimGame g, Civ civ)
    {
        long power = 0;
        var idle = new List<Unit>();
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (g.Content.Units[u.Kind].Army && u.State != UnitState.Training)
            {
                power += g.Power(u);
                if (u.TargetSettlement < 0 && u.TargetUnit < 0)
                {
                    idle.Add(u);
                }
            }
        }

        // Defend: enemies seen on our land are attacked first.
        foreach (Unit enemy in g.ActiveUnits())
        {
            if (enemy.Civ == civ.Id || !g.Content.Units[enemy.Kind].Army)
            {
                continue;
            }

            int owner = g.OwnerAt[enemy.Cell];
            if (owner == civ.Id && g.AtWar(civ.Id, enemy.Civ) && g.Sees(civ, enemy))
            {
                foreach (Unit u in idle)
                {
                    g.Apply(new AttackCommand(civ.Id, u.Id, enemy.Cell));
                }

                return;
            }
        }

        foreach (Civ other in g.Civs)
        {
            if (other.Id == civ.Id || other.Dead || !g.Met(civ, other))
            {
                continue;
            }

            bool atWar = g.AtWar(civ.Id, other.Id);
            if (!atWar && (civ.Aggression < 40 || idle.Count == 0 || g.Ticks < (long)g.Content.AiPeacefulStartMinutes * SimContent.TicksPerMinute
                || g.WarsOf(civ.Id) >= g.Content.War.MaxWars || g.WarsOf(other.Id) >= g.Content.War.MaxWars
                || g.Has(civ.Id, other.Id, TreatyKind.Alliance) || (g.Has(civ.Id, other.Id, TreatyKind.Trade) && civ.Aggression < 85)))
            {
                continue; // A3 (tests 2026-11-20): no war to start, so no search for a target
            }

            Settlement? target = atWar && (idle.Count == 0 || power <= 0) ? null : NearestKnownSettlement(g, civ, other);
            if (atWar)
            {
                // Peace when the war has turned against us or dragged on; the course of the war sets the terms.
                int mine = g.Score(civ.Id, other.Id);
                int theirs = g.Score(other.Id, civ.Id);
                if ((theirs > mine + 20 || g.AiRandom.NextInt(100) < 3) && !g.PeaceOffers.Exists(o => o.From == civ.Id && o.To == other.Id))
                {
                    bool early = g.Ticks - g.WarStart(civ.Id, other.Id) < g.Content.War.TributeAfterTicks; // 1C: no tribute yet
                    g.Apply(new ProposePeaceCommand(civ.Id, other.Id, early ? 0 : g.SuggestedTribute(civ.Id, other.Id)));
                }

                if (target is not null && idle.Count > 0 && power > 0)
                {
                    foreach (Unit u in idle)
                    {
                        g.Apply(new AttackCommand(civ.Id, u.Id, target.Center));
                    }
                }

                continue;
            }

            // Start a war only after the first minutes, when strong enough against the target's defenders and warlike enough;
            // allies are left alone, trade partners only by the most warlike.
            if (target is null)
            {
                continue;
            }

            // A9 (tests 2026-11-20): the defence as we see it now, or as we last saw it.
            (int people, int guard, int defensePct) = g.Visible(civ, target.Center)
                ? (target.People, target.Guard, g.Defense(target))
                : civ.SeenSettlements.TryGetValue(target.Center, out SettlementMemory? seen) ? (seen.People, seen.Guard, seen.DefensePercent) : (target.People, target.Guard, g.Defense(target));
            long defence = (((long)people * g.Content.War.DefenderPercent / 100 * g.Content.War.DefenderStrength) + ((long)guard * g.Content.Siege.GuardStrength)) * defensePct;
            int warlike = civ.Reputation < g.Content.War.BadReputation ? civ.Aggression / 20 : civ.Aggression / 10; // 6C: a bad name, half as often
            if (power > defence * 2 && g.AiRandom.NextInt(100) < warlike)
            {
                if (civ.Aggression < 75)
                {
                    g.Apply(new DeclareWarCommand(civ.Id, other.Id));
                }

                foreach (Unit u in idle)
                {
                    g.Apply(new AttackCommand(civ.Id, u.Id, target.Center));
                }

                return;
            }
        }
    }

    /// <summary>
    /// The bonus the AI picks for a level of a kind of unit (2026-11-20): archers and machines hit harder, cavalry marches faster,
    /// the infantry of the front alternates more damage and a shield wall.
    /// </summary>
    public static int VeteranChoiceFor(SimGame g, Civ civ, int kind)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(civ);
        VeteranOption[] options = g.Content.Veterans.Options;
        UnitType t = g.Content.Units[kind];
        string want = t.Ranged || t.SiegePercent > 0 ? "hardened"
            : Array.IndexOf(t.Groups, "cavalry") >= 0 ? (civ.KindLevel[kind] % 2 == 1 ? "forced_march" : "hardened")
            : civ.KindLevel[kind] % 2 == 1 ? "hardened" : "shield_wall";
        int pick = Array.FindIndex(options, o => o.Key == want);
        return pick >= 0 ? pick : 0;
    }

    /// <summary>
    /// What the AI does with a settlement that fell to it (2026-11-20): it takes one within siege.aiTakeWithinHexes (12) of its own, sacks one farther away (it could
    /// not hold it), and razes it only when it is the loser's last home.
    /// </summary>
    public static TakeChoice TakeChoiceFor(SimGame g, Civ civ, Settlement s)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(s);
        if (g.Civs[s.Civ].Settlements.Count <= 1 && civ.Aggression >= 75)
        {
            return TakeChoice.Raze;
        }

        int nearest = int.MaxValue;
        foreach (int sid in civ.Settlements)
        {
            nearest = Math.Min(nearest, g.Geo.Distance(g.Geo.FromIndex(g.Settlements[sid].Center), g.Geo.FromIndex(s.Center)));
        }

        return nearest <= g.Content.Siege.AiTakeWithinHexes ? TakeChoice.Take : TakeChoice.Sack; // 2B: 12 hexes (was 20)
    }

    // Tick spikes (2026-11-20): armies sent to a settlement beyond the sea searched a way twice a second for good. The AI
    // remembers for two minutes which settlements its armies can reach from home. A3 (tests 2026-11-20): the answer is kept per
    // target settlement and searched from our first settlement, not from the army's hex (it moves, so the old key never hit).
    private static bool WayTo(SimGame g, Civ civ, Settlement s)
    {
        int from = g.Settlements[civ.Settlements[0]].Center;
        long key = ((long)s.Id * g.Geo.CellCount) + from;
        if (civ.AiWayTo.TryGetValue(key, out (bool Way, long Tick) known) && g.Ticks - known.Tick < 2 * SimContent.TicksPerMinute)
        {
            return known.Way;
        }

        if (civ.AiWayTo.Count > 256)
        {
            civ.AiWayTo.Clear(); // answers about settlements long gone
        }

        bool way = g.FindPath(from, s.Center, WayScratch, false, civ.Id, 20_000);
        civ.AiWayTo[key] = (way, g.Ticks);
        return way;
    }

    [ThreadStatic]
    private static List<int>? _wayScratch;

    private static List<int> WayScratch => _wayScratch ??= [];

    // A9 (tests 2026-11-20): only settlements the people sees now or remembers having seen, not every hex it once uncovered.
    private static Settlement? NearestKnownSettlement(SimGame g, Civ civ, Civ other)
    {
        Settlement? best = null;
        int bestD = int.MaxValue;
        HexCoord home = g.Geo.FromIndex(g.Settlements[civ.Settlements[0]].Center);
        foreach (int sid in other.Settlements)
        {
            Settlement s = g.Settlements[sid];
            bool known = g.Visible(civ, s.Center) || (civ.SeenSettlements.TryGetValue(s.Center, out SettlementMemory? m) && m.Settlement == s.Id && m.Civ == other.Id);
            if (!known)
            {
                continue;
            }

            int d = g.Geo.Distance(home, g.Geo.FromIndex(s.Center));
            if (d < bestD && WayTo(g, civ, s))
            {
                best = s;
                bestD = d;
            }
        }

        return best;
    }

    // Answers treaty offers at once. Offers a treaty only when every condition of docs/sim holds (known long enough, close,
    // no recent fighting, good name, and something to gain); the cooldowns live in the treaty rules themselves.
    private static void Diplomacy(SimGame g, Civ civ)
    {
        foreach (TreatyOffer offer in g.TreatyOffers.ToArray())
        {
            if (offer.To != civ.Id)
            {
                continue;
            }

            Civ from = g.Civs[offer.From];
            bool accept = from.Reputation > g.Content.TreatyMinReputation && Interest(g, civ, from, offer.Kind) &&
                (offer.Kind == TreatyKind.Trade ? civ.Aggression < 85 : civ.Aggression < 70 && from.Reputation >= 0);
            g.Apply(new AnswerTreatyCommand(civ.Id, offer.From, offer.Kind, accept));
        }

        foreach (Civ other in g.Civs)
        {
            if (!g.TreatyConditions(civ, other) || g.Ticks < g.LastOffer(civ.Id, other.Id))
            {
                continue;
            }

            if (!g.Has(civ.Id, other.Id, TreatyKind.Trade) && civ.Aggression < 70 && Interest(g, civ, other, TreatyKind.Trade))
            {
                g.Apply(new ProposeTreatyCommand(civ.Id, other.Id, TreatyKind.Trade));
                return;
            }

            if (g.Has(civ.Id, other.Id, TreatyKind.Trade) && !g.Has(civ.Id, other.Id, TreatyKind.Alliance) && civ.Aggression < 50
                && other.Reputation >= 0 && Interest(g, civ, other, TreatyKind.Alliance))
            {
                g.Apply(new ProposeTreatyCommand(civ.Id, other.Id, TreatyKind.Alliance));
                return;
            }
        }
    }

    // Feeds a hungry settlement from the one with the most food, with a caravan.
    private static void Supply(SimGame g, Civ civ)
    {
        // Constructions whose porters were killed: the AI sends the missing materials again when it has them.
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            if (b.State == BuildingState.Delivery && b.Porters == 0)
            {
                g.Apply(new BringMaterialsCommand(civ.Id, b.Id));
            }
        }

        foreach (int sid in civ.Settlements)
        {
            Settlement s = g.Settlements[sid];
            if (!s.Hungry)
            {
                continue;
            }

            Settlement? rich = null;
            foreach (int o in civ.Settlements)
            {
                Settlement r = g.Settlements[o];
                if (r != s && r.Stock[g.Content.Food] > 60 * SimContent.One && (rich is null || r.Stock[g.Content.Food] > rich.Stock[g.Content.Food]))
                {
                    rich = r;
                }
            }

            if (rich is not null && !Carrying(g, civ, s.Id))
            {
                g.Apply(new SendCaravanCommand(civ.Id, rich.Id, s.Id, g.Content.Food, 40 * SimContent.One));
            }
        }

        Inputs(g, civ);
    }

    // AI goods (2026-11-20): goods lie in each settlement. A working building whose inputs run short at home gets them by
    // caravan from the settlement that has the most to spare: one caravan per good and target at a time, at most two new
    // caravans a thought.
    private static void Inputs(SimGame g, Civ civ)
    {
        if (civ.Settlements.Count < 2)
        {
            return;
        }

        const long Low = 10 * SimContent.One;
        const long Keep = 20 * SimContent.One;
        const long Trip = 30 * SimContent.One;
        int sent = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = g.Buildings[id];
            BuildingType t = g.Content.Buildings[b.Type];
            if (b.State != BuildingState.Active || t.Workers == 0 || b.Workers == 0)
            {
                continue;
            }

            Settlement s = g.Settlements[b.Settlement];
            for (int i = 0; i < t.Consumes.Length; i++)
            {
                if (t.Consumes[i] <= 0 || s.Stock[i] >= Low || Carrying(g, civ, s.Id, i))
                {
                    continue;
                }

                Settlement? rich = null;
                foreach (int o in civ.Settlements)
                {
                    Settlement r = g.Settlements[o];
                    if (r != s && !r.Hungry && r.Stock[i] > Keep + (5 * SimContent.One) && (rich is null || r.Stock[i] > rich.Stock[i]))
                    {
                        rich = r;
                    }
                }

                if (rich is null || g.Apply(new SendCaravanCommand(civ.Id, rich.Id, s.Id, i, Math.Min(Trip, rich.Stock[i] - Keep))) is not null)
                {
                    continue;
                }

                if (++sent >= 2)
                {
                    return;
                }
            }
        }
    }

    // Supply (2026-11-20): an army unit in the field with less than a minute and a half of provisions gets a food caravan from
    // home when home can spare it; a hungry one with no food coming walks home.
    private static void FieldSupply(SimGame g, Civ civ)
    {
        foreach (int id in civ.Units.ToArray())
        {
            Unit u = g.Units[id];
            if (!g.Content.Units[u.Kind].Army || u.State is UnitState.Gone or UnitState.Training || u.Aboard >= 0 || g.InSupply(u))
            {
                continue;
            }

            long perMinute = (long)u.People * g.Content.FoodPerPersonPerMinute;
            if (u.Provisions >= perMinute * 3 / 2)
            {
                continue;
            }

            if (u.SupplyNoWayTick >= 0 && g.Ticks - u.SupplyNoWayTick < g.Content.Supply.NoWayTicks)
            {
                continue; // A2 (tests 2026-11-20): no way to it a moment ago; no caravan and no march home for a while
            }

            bool coming = false;
            foreach (int cid in civ.Units)
            {
                coming |= g.Units[cid].Job == CarrierJob.ToUnit && g.Units[cid].Target == u.Id && g.Units[cid].State != UnitState.Gone;
            }

            if (coming)
            {
                continue;
            }

            long want = g.ProvisionsMax(u) - u.Provisions;
            if ((uint)u.Home < (uint)g.Settlements.Count && g.Settlements[u.Home].Civ == civ.Id && g.Settlements[u.Home].Stock[g.Content.Food] > (2 * want) + (40 * SimContent.One)
                && g.Apply(new SupplyUnitCommand(civ.Id, u.Home, u.Id, want)) is null)
            {
                continue;
            }

            if (u.Hunger > 0 && (uint)u.Home < (uint)g.Settlements.Count)
            {
                g.Apply(new MoveCommand(civ.Id, u.Id, g.Settlements[u.Home].Center, false));
            }
        }
    }

    private static bool Carrying(SimGame g, Civ civ, int to, int good)
    {
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (u.Job == CarrierJob.ToSettlement && u.Target == to && u.Cargo is { } load && load[good] > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Carrying(SimGame g, Civ civ, int to)
    {
        foreach (int id in civ.Units)
        {
            Unit u = g.Units[id];
            if (u.Job == CarrierJob.ToSettlement && u.Target == to)
            {
                return true;
            }
        }

        return false;
    }

    // What the AI gains: trade when the other makes a good it lacks, or knows a craft it does not (seen in their land);
    // an alliance when both fight the same enemy.
    private static bool Interest(SimGame g, Civ civ, Civ other, TreatyKind kind)
    {
        if (kind == TreatyKind.Alliance)
        {
            foreach (Civ third in g.Civs)
            {
                if (third.Id != civ.Id && third.Id != other.Id && g.AtWar(civ.Id, third.Id) && g.AtWar(other.Id, third.Id))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (int id in other.Buildings)
        {
            Building b = g.Buildings[id];
            if (!civ.Known[b.Cell] || b.State != BuildingState.Active)
            {
                continue;
            }

            BuildingType t = g.Content.Buildings[b.Type];
            if (!SimGame.Knows(civ, t))
            {
                return true;
            }

            for (int i = 0; i < t.Produces.Length; i++)
            {
                if (t.Produces[i] > 0 && civ.Rate[i] <= 0 && civ.Stock[i] < 20 * SimContent.One)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AnswerPeace(SimGame g, Civ civ)
    {
        foreach (PeaceOffer offer in g.PeaceOffers.ToArray())
        {
            if (offer.To != civ.Id)
            {
                continue;
            }

            // Accept terms no worse than the course of the war suggests, with some give from peaceful characters. E1
            // (2026-11-19): tribute only after real losses (tributeNeedsKills people killed by them).
            int fair = g.SuggestedTribute(offer.From, civ.Id);
            bool hurt = g.Score(offer.From, civ.Id) >= g.Content.War.TributeNeedsKills;
            bool accept = offer.TributePercent <= 0 ? offer.TributePercent <= fair + ((100 - civ.Aggression) / 10)
                : hurt && offer.TributePercent <= fair + ((100 - civ.Aggression) / 10);
            g.Apply(new AnswerPeaceCommand(civ.Id, offer.From, accept));
        }
    }
}
