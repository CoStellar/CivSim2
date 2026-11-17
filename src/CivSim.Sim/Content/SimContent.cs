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
public sealed record SettlementLevel(int Id, string Key, string Name, int Radius, int Housing, long Storage, long[] UpgradePrice);

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

        var levels = new List<SettlementLevel>();
        foreach (JsonElement l in root.GetProperty("settlementLevels").EnumerateArray())
        {
            levels.Add(new SettlementLevel(
                levels.Count, l.GetProperty("key").GetString()!, l.GetProperty("name").GetString()!, l.GetProperty("radius").GetInt32(),
                l.GetProperty("housing").GetInt32(), Hundredths(l.GetProperty("storage")), Amounts(l.GetProperty("upgradePrice"))));
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
            });
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
