using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CivSim.Core.Content;

namespace CivSim.Sim.Content;

/// <summary>A good the player stores (food, wood, stone…). Ids are list indexes.</summary>
public sealed record SimGood(int Id, string Key, string Name);

/// <summary>A level of a settlement: territory radius, housing and storage it gives, price of reaching it.</summary>
public sealed record SettlementLevel(int Id, string Key, string Name, int Radius, int Housing, long Storage, long[] UpgradePrice)
{
    /// <summary>Technology needed to reach this level (−1 = none).</summary>
    public int Tech { get; init; } = -1;
}

/// <summary>An era (Stone Age … Middle Ages).</summary>
public sealed record SimEra(int Id, string Key, string Name);

/// <summary>A technology: experience needed in earlier technologies (worker-ticks), price (hundredths) and time (ticks).</summary>
public sealed record SimTech(int Id, string Key, string Name, int Era, bool KnownAtStart, (int Tech, long WorkerTicks)[] Experience, long[] Price, int ResearchTicks);

/// <summary>A rarity of thinkers and advisors.</summary>
public sealed record Rarity(int Id, string Key, string Name, int Weight, int CostPercent, int ExperiencePercent, int PriceTimes);

/// <summary>A kind of unit (settlers, scouts; armies in stage E). Price in hundredths.</summary>
public sealed record UnitType(int Id, string Key, string Name, int People, long[] Price, int TrainTicks, int TicksPerHex, int HiddenTicksPerHex, int Sight, bool Founds)
{
    /// <summary>Technology that allows it.</summary>
    public int Tech { get; init; }

    /// <summary>A fighting unit.</summary>
    public bool Army { get; init; }

    /// <summary>Called-up workers: free, weak, go back to work when disbanded.</summary>
    public bool Levy { get; init; }

    /// <summary>Strength per person (warriors = 10).</summary>
    public int Strength { get; init; }

    /// <summary>Reach in hexes (1 = next hex).</summary>
    public int Range { get; init; } = 1;

    /// <summary>Biomes where it fights better.</summary>
    public bool[] BonusBiomes { get; init; } = [];

    /// <summary>Reliefs where it fights better.</summary>
    public bool[] BonusReliefs { get; init; } = [];

    /// <summary>Bonus on its terrain, percent.</summary>
    public int BonusPercent { get; init; }

    /// <summary>Technologies that make it stronger, with the percent.</summary>
    public (int Tech, int Percent)[] Upgrades { get; init; } = [];
}

/// <summary>Rules of fighting.</summary>
public sealed record WarRules(int CombatEveryTicks, int LowLuckPercent, int HighLuckPercent, int DefenderPercent, int DefenderStrength, int CaptureBelowPeople, long SoldierFoodPerMinute, int SurpriseReputation);

/// <summary>A building type from <c>data/sim/game.json</c>. Amounts in hundredths; rates per worker per minute.</summary>
public sealed class BuildingType
{
    /// <summary>Index in <see cref="SimContent.Buildings"/>, from 1 (0 means no building).</summary>
    public int Id { get; init; }

    /// <summary>Data key.</summary>
    public string Key { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Jobs.</summary>
    public int Workers { get; init; }

    /// <summary>Price per good, hundredths.</summary>
    public long[] Price { get; init; } = [];

    /// <summary>Construction time in ticks.</summary>
    public int BuildTicks { get; init; }

    /// <summary>Allowed biomes (empty = any land).</summary>
    public bool[] Biomes { get; init; } = [];

    /// <summary>Allowed reliefs (empty = any).</summary>
    public bool[] Reliefs { get; init; } = [];

    /// <summary>Needs water next to the hex (sea, lake or a river edge).</summary>
    public bool NeedsWater { get; init; }

    /// <summary>A surface deposit that also allows the building where terrain does not (0 = none).</summary>
    public int OrDeposit { get; init; }

    /// <summary>Production per worker per minute, hundredths.</summary>
    public long[] Produces { get; init; } = [];

    /// <summary>Consumption per worker per minute, hundredths.</summary>
    public long[] Consumes { get; init; } = [];

    /// <summary>Deposit on the hex that raises production (0 = none).</summary>
    public int BonusDeposit { get; init; }

    /// <summary>Extra production in percent with the bonus deposit.</summary>
    public int BonusPercent { get; init; }

    /// <summary>Housing added.</summary>
    public int Housing { get; init; }

    /// <summary>Storage per good added, hundredths.</summary>
    public long Storage { get; init; }

    /// <summary>True when the building has inputs.</summary>
    public bool HasInputs { get; init; }

    /// <summary>Technology that allows it; its workers give experience in that technology.</summary>
    public int Tech { get; init; }

    /// <summary>Least fertility of the hex (0 = any).</summary>
    public int MinFertility { get; init; }

    /// <summary>Research is this much cheaper while the building works (percent).</summary>
    public int ResearchDiscountPercent { get; init; }

    /// <summary>Reliefs that raise production.</summary>
    public bool[] BonusReliefs { get; init; } = [];

    /// <summary>Extra production in percent on those reliefs.</summary>
    public int BonusReliefPercent { get; init; }

    /// <summary>Defence of its settlement, percent (fortifications).</summary>
    public int DefensePercent { get; init; }

    /// <summary>Training time in its settlement, percent (barracks).</summary>
    public int TrainPercent { get; init; }
}

/// <summary>The rules of the new game, loaded from <c>data/sim/game.json</c> plus the world content of the legacy loader.</summary>
public sealed class SimContent
{
    /// <summary>Hundredths in one unit.</summary>
    public const long One = 100;

    /// <summary>Ticks per second.</summary>
    public const int TicksPerSecond = 10;

    /// <summary>Ticks per minute.</summary>
    public const int TicksPerMinute = 600;

    /// <summary>World content (biomes, deposits, climate) used by the world generator.</summary>
    public required GameContent World { get; init; }

    /// <summary>Hex density against the legacy map.</summary>
    public int Density { get; init; }

    /// <summary>Economy rhythm.</summary>
    public int EconomyEveryTicks { get; init; }

    /// <summary>Population rhythm.</summary>
    public int PopulationEveryTicks { get; init; }

    /// <summary>Goods.</summary>
    public required IReadOnlyList<SimGood> Goods { get; init; }

    /// <summary>Settlement levels, from the camp up.</summary>
    public required IReadOnlyList<SettlementLevel> Levels { get; init; }

    /// <summary>Building types; index 0 is an empty placeholder so that ids start at 1.</summary>
    public required IReadOnlyList<BuildingType> Buildings { get; init; }

    /// <summary>People of a new tribe.</summary>
    public int StartPeople { get; init; }

    /// <summary>Stock of a new tribe, hundredths.</summary>
    public required long[] StartStock { get; init; }

    /// <summary>Food per person per minute, hundredths.</summary>
    public long FoodPerPersonPerMinute { get; init; }

    /// <summary>Forage per idle person per minute, hundredths.</summary>
    public long IdleForagePerMinute { get; init; }

    /// <summary>Growth percent of people per minute.</summary>
    public int GrowthPercentPerMinute { get; init; }

    /// <summary>Least growth per minute (people).</summary>
    public int MinGrowthPerMinute { get; init; }

    /// <summary>Starvation loss percent per minute.</summary>
    public int StarvePercentPerMinute { get; init; }

    /// <summary>Extra price, percent, for resuming a paused construction.</summary>
    public int ResumeCostPercent { get; init; }

    /// <summary>Id of food.</summary>
    public int Food { get; init; }

    /// <summary>Unit kinds.</summary>
    public required IReadOnlyList<UnitType> Units { get; init; }

    /// <summary>Least distance between settlement centres.</summary>
    public int FoundMinDistance { get; init; }

    /// <summary>Eras.</summary>
    public required IReadOnlyList<SimEra> Eras { get; init; }

    /// <summary>Technologies.</summary>
    public required IReadOnlyList<SimTech> Techs { get; init; }

    /// <summary>Discount per known people that knows a technology, percent.</summary>
    public int NeighbourDiscountPercent { get; init; }

    /// <summary>Most discount from neighbours, percent.</summary>
    public int MaxNeighbourDiscountPercent { get; init; }

    /// <summary>Rarities of thinkers and advisors.</summary>
    public required IReadOnlyList<Rarity> Rarities { get; init; }

    /// <summary>Thinker rules.</summary>
    public int ThinkerRetireTicks { get; init; }

    /// <summary>Ticks between thinker candidates.</summary>
    public int ThinkerOfferTicks { get; init; }

    /// <summary>Edge lost per thinker generation, percent.</summary>
    public int GenerationLossPercent { get; init; }

    /// <summary>Base price of hiring a thinker.</summary>
    public required long[] ThinkerPrice { get; init; }

    /// <summary>Ticks between advisor candidates.</summary>
    public int AdvisorOfferTicks { get; init; }

    /// <summary>Base price of hiring an advisor.</summary>
    public required long[] AdvisorPrice { get; init; }

    /// <summary>Food an advisor eats per minute, hundredths.</summary>
    public long AdvisorUpkeep { get; init; }

    /// <summary>Rules of fighting.</summary>
    public required WarRules War { get; init; }

    /// <summary>The shared pool of events.</summary>
    public required IReadOnlyList<State.SimEvent> Events { get; init; }

    /// <summary>Ticks between two events of a people (at least).</summary>
    public int EventEveryTicks { get; init; }

    /// <summary>Random extra ticks between events.</summary>
    public int EventSpreadTicks { get; init; }

    /// <summary>Advisor fields.</summary>
    public required IReadOnlyList<string> AdvisorFields { get; init; }

    /// <summary>Finds a technology by key.</summary>
    public int TechId(string key)
    {
        foreach (SimTech t in Techs)
        {
            if (t.Key == key)
            {
                return t.Id;
            }
        }

        throw new KeyNotFoundException($"Unknown technology '{key}'.");
    }

    /// <summary>Walking cost of each hex in percent of a flat open hex.</summary>
    public required int[] WalkCost { get; init; }

    /// <summary>Finds a unit kind by key.</summary>
    public UnitType Unit(string key)
    {
        foreach (UnitType u in Units)
        {
            if (u.Key == key)
            {
                return u;
            }
        }

        throw new KeyNotFoundException($"Unknown unit '{key}'.");
    }

    /// <summary>Finds a good by key.</summary>
    public int GoodId(string key)
    {
        foreach (SimGood g in Goods)
        {
            if (g.Key == key)
            {
                return g.Id;
            }
        }

        throw new KeyNotFoundException($"Unknown good '{key}'.");
    }

    /// <summary>Finds a building type by key.</summary>
    public BuildingType Building(string key)
    {
        foreach (BuildingType b in Buildings)
        {
            if (b.Key == key)
            {
                return b;
            }
        }

        throw new KeyNotFoundException($"Unknown building '{key}'.");
    }

    /// <summary>Loads the rules from a data folder (default: the folder next to the program).</summary>
    public static SimContent Load(string? directory = null)
    {
        directory ??= CivSim.Data.ContentLoader.DefaultDirectory;
        GameContent world = CivSim.Data.ContentLoader.Load(directory);
        string path = Path.Combine(directory, "sim", "game.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        var goods = new List<SimGood>();
        foreach (JsonElement g in root.GetProperty("goods").EnumerateArray())
        {
            goods.Add(new SimGood(goods.Count, g.GetProperty("key").GetString()!, g.GetProperty("name").GetString()!));
        }

        int GoodOf(string key)
        {
            int i = goods.FindIndex(x => x.Key == key);
            return i >= 0 ? i : throw new InvalidDataException($"sim/game.json: unknown good '{key}'.");
        }

        long[] Amounts(JsonElement e)
        {
            var a = new long[goods.Count];
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in e.EnumerateObject())
                {
                    a[GoodOf(p.Name)] += Hundredths(p.Value);
                }
            }

            return a;
        }

        int DepositOf(JsonElement b, string name)
        {
            if (!b.TryGetProperty(name, out JsonElement e) || string.IsNullOrEmpty(e.GetString()))
            {
                return 0;
            }

            string key = e.GetString()!;
            foreach (DepositDefinition d in world.Deposits)
            {
                if (d.Key == key)
                {
                    return d.Id;
                }
            }

            throw new InvalidDataException($"sim/game.json: unknown deposit '{key}'.");
        }

        var eras = new List<SimEra>();
        foreach (JsonElement e in root.GetProperty("eras").EnumerateArray())
        {
            eras.Add(new SimEra(eras.Count, e.GetProperty("key").GetString()!, e.GetProperty("name").GetString()!));
        }

        var techKeys = new List<string>();
        foreach (JsonElement t in root.GetProperty("technologies").EnumerateArray())
        {
            techKeys.Add(t.GetProperty("key").GetString()!);
        }

        int TechOf(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out JsonElement v))
            {
                return -1;
            }

            int i = techKeys.IndexOf(v.GetString()!);
            return i >= 0 ? i : throw new InvalidDataException($"sim/game.json: unknown technology '{v.GetString()}'.");
        }

        var techs = new List<SimTech>();
        foreach (JsonElement t in root.GetProperty("technologies").EnumerateArray())
        {
            var exp = new List<(int, long)>();
            if (t.TryGetProperty("experience", out JsonElement xe))
            {
                foreach (JsonProperty p in xe.EnumerateObject())
                {
                    int ti = techKeys.IndexOf(p.Name);
                    if (ti < 0)
                    {
                        throw new InvalidDataException($"sim/game.json: unknown technology '{p.Name}' in experience.");
                    }

                    exp.Add((ti, p.Value.GetInt64() * TicksPerMinute));
                }
            }

            string eraKey = t.GetProperty("era").GetString()!;
            int era = eras.FindIndex(x => x.Key == eraKey);
            techs.Add(new SimTech(
                techs.Count, techKeys[techs.Count], t.GetProperty("name").GetString()!, era >= 0 ? era : throw new InvalidDataException($"sim/game.json: unknown era '{eraKey}'."),
                t.TryGetProperty("known", out JsonElement k) && k.GetBoolean(), [.. exp],
                t.TryGetProperty("price", out JsonElement tp) ? Amounts(tp) : new long[goods.Count],
                t.TryGetProperty("seconds", out JsonElement ts) ? ts.GetInt32() * TicksPerSecond : 0));
        }

        var levels = new List<SettlementLevel>();
        foreach (JsonElement l in root.GetProperty("settlementLevels").EnumerateArray())
        {
            levels.Add(new SettlementLevel(
                levels.Count, l.GetProperty("key").GetString()!, l.GetProperty("name").GetString()!, l.GetProperty("radius").GetInt32(),
                l.GetProperty("housing").GetInt32(), Hundredths(l.GetProperty("storage")), Amounts(l.GetProperty("upgradePrice")))
            {
                Tech = TechOf(l, "tech"),
            });
        }

        static bool[] ReliefsOf(JsonElement e, string name)
        {
            var f = new bool[3];
            if (e.TryGetProperty(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[k.GetString() switch { "flat" => 0, "hills" => 1, "mountains" => 2, _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'.") }] = true;
                }
            }

            return f;
        }

        var buildings = new List<BuildingType> { new() { Id = 0, Key = "", Name = "" } };
        foreach (JsonElement b in root.GetProperty("buildings").EnumerateArray())
        {
            var biomes = new bool[world.Biomes.Count];
            if (b.TryGetProperty("biomes", out JsonElement be))
            {
                foreach (JsonElement k in be.EnumerateArray())
                {
                    biomes[world.Biome(k.GetString()!).Id] = true;
                }
            }

            var reliefs = new bool[3];
            if (b.TryGetProperty("relief", out JsonElement re))
            {
                foreach (JsonElement k in re.EnumerateArray())
                {
                    reliefs[k.GetString() switch
                    {
                        "flat" => 0,
                        "hills" => 1,
                        "mountains" => 2,
                        _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'."),
                    }] = true;
                }
            }

            long[] consumes = b.TryGetProperty("consumes", out JsonElement ce) ? Amounts(ce) : new long[goods.Count];
            bool hasInputs = false;
            foreach (long c in consumes)
            {
                hasInputs |= c > 0;
            }

            buildings.Add(new BuildingType
            {
                Id = buildings.Count,
                Key = b.GetProperty("key").GetString()!,
                Name = b.GetProperty("name").GetString()!,
                Workers = b.GetProperty("workers").GetInt32(),
                Price = Amounts(b.GetProperty("price")),
                BuildTicks = (int)(Hundredths(b.GetProperty("buildSeconds")) * TicksPerSecond / One),
                Biomes = biomes,
                Reliefs = reliefs,
                NeedsWater = b.TryGetProperty("needsWater", out JsonElement w) && w.GetBoolean(),
                OrDeposit = DepositOf(b, "orDeposit"),
                Produces = b.TryGetProperty("produces", out JsonElement pe) ? Amounts(pe) : new long[goods.Count],
                Consumes = consumes,
                HasInputs = hasInputs,
                BonusDeposit = DepositOf(b, "bonusDeposit"),
                BonusPercent = b.TryGetProperty("bonusPercent", out JsonElement bp) ? bp.GetInt32() : 0,
                Housing = b.TryGetProperty("housing", out JsonElement h) ? h.GetInt32() : 0,
                Storage = b.TryGetProperty("storage", out JsonElement s) ? Hundredths(s) : 0,
                Tech = Math.Max(0, TechOf(b, "tech")),
                MinFertility = b.TryGetProperty("minFertility", out JsonElement mf) ? mf.GetInt32() : 0,
                ResearchDiscountPercent = b.TryGetProperty("researchDiscountPercent", out JsonElement rd) ? rd.GetInt32() : 0,
                DefensePercent = b.TryGetProperty("defensePercent", out JsonElement dp) ? dp.GetInt32() : 0,
                BonusReliefs = ReliefsOf(b, "bonusRelief"),
                BonusReliefPercent = b.TryGetProperty("bonusReliefPercent", out JsonElement brp) ? brp.GetInt32() : 0,
                TrainPercent = b.TryGetProperty("trainPercent", out JsonElement tp2) ? tp2.GetInt32() : 100,
            });
        }

        bool[] Flags(JsonElement e, string name)
        {
            var f = new bool[world.Biomes.Count];
            if (e.TryGetProperty(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[world.Biome(k.GetString()!).Id] = true;
                }
            }

            return f;
        }

        bool[] Reliefs(JsonElement e, string name)
        {
            var f = new bool[3];
            if (e.TryGetProperty(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[k.GetString() switch { "flat" => 0, "hills" => 1, "mountains" => 2, _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'.") }] = true;
                }
            }

            return f;
        }

        (int, int)[] Upgrades(JsonElement e)
        {
            var list = new List<(int, int)>();
            if (e.TryGetProperty("upgrades", out JsonElement up))
            {
                foreach (JsonProperty p in up.EnumerateObject())
                {
                    int ti = techKeys.IndexOf(p.Name);
                    list.Add((ti >= 0 ? ti : throw new InvalidDataException($"sim/game.json: unknown technology '{p.Name}'."), p.Value.GetInt32()));
                }
            }

            return [.. list];
        }

        var units = new List<UnitType>();
        foreach (JsonElement u in root.GetProperty("units").EnumerateArray())
        {
            int perHex = u.GetProperty("ticksPerHex").GetInt32();
            units.Add(new UnitType(
                units.Count, u.GetProperty("key").GetString()!, u.GetProperty("name").GetString()!, u.GetProperty("people").GetInt32(), Amounts(u.GetProperty("price")),
                (int)(Hundredths(u.GetProperty("trainSeconds")) * TicksPerSecond / One), perHex,
                u.TryGetProperty("hiddenTicksPerHex", out JsonElement hp) ? hp.GetInt32() : 0, u.GetProperty("sight").GetInt32(),
                u.TryGetProperty("founds", out JsonElement f) && f.GetBoolean())
            {
                Tech = Math.Max(0, TechOf(u, "tech")),
                Army = u.TryGetProperty("army", out JsonElement ar) && ar.GetBoolean(),
                Levy = u.TryGetProperty("levy", out JsonElement lv) && lv.GetBoolean(),
                Strength = u.TryGetProperty("strength", out JsonElement st) ? st.GetInt32() : 0,
                Range = u.TryGetProperty("range", out JsonElement rg) ? rg.GetInt32() : 1,
                BonusBiomes = Flags(u, "bonusBiomes"),
                BonusReliefs = Reliefs(u, "bonusRelief"),
                BonusPercent = u.TryGetProperty("bonusPercent", out JsonElement bpc) ? bpc.GetInt32() : 0,
                Upgrades = Upgrades(u),
            });
        }

        JsonElement tc = root.GetProperty("terrainCost");
        var forest = new bool[world.Biomes.Count];
        foreach (JsonElement k in tc.GetProperty("forestBiomes").EnumerateArray())
        {
            forest[world.Biome(k.GetString()!).Id] = true;
        }

        // Walking cost per biome × relief, flattened: index = biome * 3 + relief; 0 = impassable (water).
        var walk = new int[world.Biomes.Count * 3];
        for (int b = 0; b < world.Biomes.Count; b++)
        {
            for (int r = 0; r < 3; r++)
            {
                int cost = world.Biomes[b].IsWater ? 0 : 100;
                if (cost > 0)
                {
                    cost = cost * (r == 1 ? tc.GetProperty("hills").GetInt32() : r == 2 ? tc.GetProperty("mountains").GetInt32() : 100) / 100;
                    cost = forest[b] ? cost * tc.GetProperty("forest").GetInt32() / 100 : cost;
                }

                walk[(b * 3) + r] = cost;
            }
        }

        JsonElement research = root.GetProperty("research");
        JsonElement thinkers = root.GetProperty("thinkers");
        JsonElement advisors = root.GetProperty("advisors");
        var rarities = new List<Rarity>();
        foreach (JsonElement r in thinkers.GetProperty("rarities").EnumerateArray())
        {
            rarities.Add(new Rarity(
                rarities.Count, r.GetProperty("key").GetString()!, r.GetProperty("name").GetString()!, r.GetProperty("weight").GetInt32(),
                r.GetProperty("costPercent").GetInt32(), r.GetProperty("experiencePercent").GetInt32(), r.GetProperty("priceTimes").GetInt32()));
        }

        var fields = new List<string>();
        foreach (JsonElement f in advisors.GetProperty("fields").EnumerateArray())
        {
            fields.Add(f.GetString()!);
        }

        JsonElement start = root.GetProperty("start");
        JsonElement food = root.GetProperty("food");
        JsonElement growth = root.GetProperty("growth");
        return new SimContent
        {
            World = world,
            Density = root.GetProperty("density").GetInt32(),
            EconomyEveryTicks = root.GetProperty("economyEveryTicks").GetInt32(),
            PopulationEveryTicks = root.GetProperty("populationEveryTicks").GetInt32(),
            Goods = goods,
            Levels = levels,
            Buildings = buildings,
            StartPeople = start.GetProperty("people").GetInt32(),
            StartStock = Amounts(start.GetProperty("stock")),
            FoodPerPersonPerMinute = Hundredths(food.GetProperty("foodPerPersonPerMinute")),
            IdleForagePerMinute = Hundredths(food.GetProperty("idleForagePerMinute")),
            GrowthPercentPerMinute = growth.GetProperty("growthPercentPerMinute").GetInt32(),
            MinGrowthPerMinute = growth.GetProperty("minGrowthPerMinute").GetInt32(),
            StarvePercentPerMinute = growth.GetProperty("starvePercentPerMinute").GetInt32(),
            ResumeCostPercent = root.GetProperty("resumeCostPercent").GetInt32(),
            Food = GoodOf("food"),
            Units = units,
            FoundMinDistance = root.GetProperty("foundSettlement").GetProperty("minDistance").GetInt32(),
            WalkCost = walk,
            Eras = eras,
            Techs = techs,
            NeighbourDiscountPercent = research.GetProperty("neighbourDiscountPercent").GetInt32(),
            MaxNeighbourDiscountPercent = research.GetProperty("maxNeighbourDiscountPercent").GetInt32(),
            Rarities = rarities,
            ThinkerRetireTicks = thinkers.GetProperty("retireMinutes").GetInt32() * TicksPerMinute,
            ThinkerOfferTicks = thinkers.GetProperty("offerMinutes").GetInt32() * TicksPerMinute,
            GenerationLossPercent = thinkers.GetProperty("generationLossPercent").GetInt32(),
            ThinkerPrice = Amounts(thinkers.GetProperty("hirePrice")),
            AdvisorOfferTicks = advisors.GetProperty("offerMinutes").GetInt32() * TicksPerMinute,
            AdvisorPrice = Amounts(advisors.GetProperty("hirePrice")),
            AdvisorUpkeep = Hundredths(advisors.GetProperty("upkeepFoodPerMinute")),
            AdvisorFields = fields,
            Events = State.SimGame.LoadEvents(root, goods),
            EventEveryTicks = root.TryGetProperty("events", out JsonElement evr) ? evr.GetProperty("everyMinutes").GetInt32() * TicksPerMinute : 0,
            EventSpreadTicks = root.TryGetProperty("events", out JsonElement evs) ? evs.GetProperty("spreadMinutes").GetInt32() * TicksPerMinute : 0,
            War = new WarRules(
                root.GetProperty("war").GetProperty("combatEveryTicks").GetInt32(), root.GetProperty("war").GetProperty("lowLuckPercent").GetInt32(),
                root.GetProperty("war").GetProperty("highLuckPercent").GetInt32(), root.GetProperty("war").GetProperty("defenderPercent").GetInt32(),
                root.GetProperty("war").GetProperty("defenderStrength").GetInt32(), root.GetProperty("war").GetProperty("captureBelowPeople").GetInt32(),
                Hundredths(root.GetProperty("war").GetProperty("soldierFoodPerMinute")), root.GetProperty("war").GetProperty("surpriseReputation").GetInt32()),
        };
    }

    // Decimal text → hundredths without floating point (the JSON number's raw text is parsed).
    private static long Hundredths(JsonElement e)
    {
        string text = e.GetRawText();
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return long.Parse(text, CultureInfo.InvariantCulture) * One;
        }

        string frac = (text[(dot + 1)..] + "00")[..2];
        long whole = long.Parse(text[..dot], CultureInfo.InvariantCulture);
        long f = long.Parse(frac, CultureInfo.InvariantCulture);
        return (whole * One) + (text.StartsWith('-') ? -f : f);
    }
}
