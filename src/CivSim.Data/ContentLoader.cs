using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CivSim.Core.Content;
using CivSim.Core.Numerics;

namespace CivSim.Data;

/// <summary>
/// Loads and validates game content from the <c>data/</c> folder. Every problem is collected with its file and
/// position, so a broken data file gives a readable list instead of a crash deep inside the simulation.
/// </summary>
public static class ContentLoader
{
    /// <summary>Name of the data folder next to the executable.</summary>
    public const string DefaultFolderName = "data";

    /// <summary>The data folder copied next to the running program.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, DefaultFolderName);

    /// <summary>Loads content from <paramref name="directory"/>. Throws <see cref="ContentException"/> with all errors.</summary>
    public static GameContent Load(string? directory = null)
    {
        directory ??= DefaultDirectory;
        var errors = new List<string>();
        List<BiomeDefinition> biomes = LoadBiomes(Path.Combine(directory, "biomes.json"), errors);
        List<DepositDefinition> deposits = LoadDeposits(Path.Combine(directory, "resources.json"), biomes, errors);
        ValidateBiomeSet(biomes, errors);
        List<GoodDefinition> goods = LoadGoods(Path.Combine(directory, "goods.json"), errors);
        List<RecipeDefinition> recipes = LoadRecipes(Path.Combine(directory, "recipes.json"), goods, errors);
        (List<SettlementLevelDefinition> levels, int tribeLevel, List<GoodQuantity> tribeStock) =
            LoadSettlements(Path.Combine(directory, "settlements.json"), goods, errors);
        List<EraDefinition> eras = LoadEras(Path.Combine(directory, "eras.json"), errors);
        List<EffectDefinition> effects = LoadEffects(Path.Combine(directory, "effects.json"), errors);
        if (errors.Count > 0)
        {
            throw new ContentException(errors);
        }

        return new GameContent(biomes, deposits)
        {
            Goods = goods,
            Recipes = recipes,
            SettlementLevels = levels,
            Eras = eras,
            Effects = effects,
            NewTribeLevel = tribeLevel,
            NewTribeStock = tribeStock,
        };
    }

    private static List<GoodDefinition> LoadGoods(string path, List<string> errors)
    {
        var result = new List<GoodDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "goods", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: goods[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            int era = r.Int("era", 1, 10);
            GoodCategory category = r.Enum<GoodCategory>("category");
            Amount spoilage = r.Amount("spoilagePerYear", Amount.Zero, Amount.FromUnits(100));
            uint color = r.Color("color");
            result.Add(new GoodDefinition(i, key, name, era, category, (int)spoilage.Hundredths, color));
            i++;
        }

        if (result.Count == 0)
        {
            errors.Add($"{Path.GetFileName(path)}: at least one good is required.");
        }

        if (result.Count > 255)
        {
            errors.Add($"{Path.GetFileName(path)}: at most 255 goods are supported.");
        }

        return result;
    }

    private static List<RecipeDefinition> LoadRecipes(string path, List<GoodDefinition> goods, List<string> errors)
    {
        var result = new List<RecipeDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "recipes", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: recipes[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            List<GoodQuantity> inputs = r.Goods("inputs", goods);
            List<GoodQuantity> outputs = r.Goods("outputs", goods);
            int workers = r.Int("workers", 1, 1000);
            int years = r.Int("years", 1, 100);
            result.Add(new RecipeDefinition(i, key, name, inputs, outputs, workers, years));
            i++;
        }

        return result;
    }

    private static (List<SettlementLevelDefinition> Levels, int TribeLevel, List<GoodQuantity> TribeStock) LoadSettlements(
        string path, List<GoodDefinition> goods, List<string> errors)
    {
        var levels = new List<SettlementLevelDefinition>();
        var stock = new List<GoodQuantity>();
        int tribeLevel = 0;
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "levels", path, errors, out JsonElement array))
        {
            return (levels, tribeLevel, stock);
        }

        string file = Path.GetFileName(path);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{file}: levels[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            (int min, int max) = r.Range("population", 0, int.MaxValue);
            Amount storage = r.Amount("storage", Amount.FromUnits(1), Amount.FromUnits(1_000_000_000));
            bool mobile = r.Bool("mobile");
            if (levels.Count > 0 && (min < levels[^1].MinPopulation || storage < levels[^1].StorageCapacity))
            {
                errors.Add($"{file}: levels[{i}] must not be smaller than the level before it.");
            }

            levels.Add(new SettlementLevelDefinition(i, key, name, min, max, storage, mobile));
            i++;
        }

        if (levels.Count == 0)
        {
            errors.Add($"{file}: at least one level is required.");
        }

        if (doc.RootElement.TryGetProperty("newTribe", out JsonElement tribe) && tribe.ValueKind == JsonValueKind.Object)
        {
            var r = new Reader(tribe, $"{file}: newTribe", errors);
            string level = r.String("level");
            tribeLevel = levels.FindIndex(l => l.Key == level);
            if (tribeLevel < 0 && level.Length > 0)
            {
                errors.Add($"{file}: newTribe.level: unknown level '{level}'.");
            }

            stock = r.Goods("stock", goods);
        }
        else
        {
            errors.Add($"{file}: missing object 'newTribe'.");
        }

        return (levels, Math.Max(0, tribeLevel), stock);
    }

    private static List<EraDefinition> LoadEras(string path, List<string> errors)
    {
        var result = new List<EraDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "eras", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: eras[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            int months = r.Int("monthsPerTurn", 1, 1200);
            result.Add(new EraDefinition(i, key, name, months));
            i++;
        }

        if (result.Count == 0)
        {
            errors.Add($"{Path.GetFileName(path)}: at least one era is required.");
        }

        return result;
    }

    private static List<EffectDefinition> LoadEffects(string path, List<string> errors)
    {
        var result = new List<EffectDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "effects", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: effects[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            EffectTarget target = r.Enum<EffectTarget>("target");
            EffectStat stat = r.Enum<EffectStat>("stat");
            EffectStacking stacking = r.Enum<EffectStacking>("stacking");
            result.Add(new EffectDefinition(i, key, name, target, stat, stacking));
            i++;
        }

        return result;
    }

    private static List<BiomeDefinition> LoadBiomes(string path, List<string> errors)
    {
        var result = new List<BiomeDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "biomes", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{Path.GetFileName(path)}: biomes[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            BiomeKind kind = r.Enum<BiomeKind>("kind");
            (int tMin, int tMax) = kind == BiomeKind.Land ? r.Range("temperature", -30, 40) : (0, 0);
            (int hMin, int hMax) = kind == BiomeKind.Land ? r.Range("humidity", 0, 100) : (0, 0);
            int move = r.Int("movementCost", 1, 10);
            int fert = r.Int("baseFertility", 0, 100);
            int food = r.Int("food", 0, 10);
            uint color = r.Color("color");
            result.Add(new BiomeDefinition(i, key, name, kind, tMin, tMax, hMin, hMax, move, fert, food, color));
            i++;
        }

        if (result.Count > byte.MaxValue)
        {
            errors.Add($"{Path.GetFileName(path)}: at most 255 biomes are supported.");
        }

        return result;
    }

    private static List<DepositDefinition> LoadDeposits(string path, List<BiomeDefinition> biomes, List<string> errors)
    {
        var result = new List<DepositDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "deposits", path, errors, out JsonElement array))
        {
            return result;
        }

        var biomeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (BiomeDefinition b in biomes)
        {
            biomeKeys.Add(b.Key);
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{Path.GetFileName(path)}: deposits[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            List<string> inBiomes = r.StringList("biomes");
            foreach (string b in inBiomes)
            {
                if (!biomeKeys.Contains(b))
                {
                    errors.Add($"{at}.biomes: unknown biome '{b}'.");
                }
            }

            var relief = new List<Relief>();
            foreach (string s in r.StringList("relief"))
            {
                if (TryRelief(s, out Relief rel))
                {
                    relief.Add(rel);
                }
                else
                {
                    errors.Add($"{at}.relief: '{s}' must be flat, hills or mountains.");
                }
            }

            int per = r.Int("perThousandCells", 1, 1000);
            (int rMin, int rMax) = r.Range("richness", 1, 1_000_000);
            int depth = r.Int("depth", 0, 2);
            bool renewable = r.Bool("renewable");
            uint color = r.Color("color");
            result.Add(new DepositDefinition(i + 1, key, name, inBiomes, relief, per, rMin, rMax, depth, renewable, color));
            i++;
        }

        return result;
    }

    private static void ValidateBiomeSet(List<BiomeDefinition> biomes, List<string> errors)
    {
        foreach (BiomeKind kind in new[] { BiomeKind.DeepOcean, BiomeKind.ShallowSea, BiomeKind.Coast, BiomeKind.Lake })
        {
            int n = biomes.FindAll(b => b.Kind == kind).Count;
            if (n != 1)
            {
                errors.Add($"biomes.json: exactly one biome of kind '{ToSnake(kind.ToString())}' is required, found {n}.");
            }
        }

        if (!biomes.Exists(b => b.Key == "grassland" && b.Kind == BiomeKind.Land))
        {
            errors.Add("biomes.json: a land biome 'grassland' is required (fallback for cells no rule matches).");
        }
    }

    private static JsonDocument? Open(string path, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{path}: file not found.");
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            errors.Add($"{Path.GetFileName(path)}: invalid JSON at line {ex.LineNumber + 1}: {ex.Message}");
            return null;
        }
    }

    private static bool TryArray(JsonElement root, string name, string path, List<string> errors, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        errors.Add($"{Path.GetFileName(path)}: missing array '{name}'.");
        array = default;
        return false;
    }

    private static bool TryRelief(string s, out Relief relief)
    {
        switch (s)
        {
            case "flat":
                relief = Relief.Flat;
                return true;
            case "hills":
                relief = Relief.Hills;
                return true;
            case "mountains":
                relief = Relief.Mountains;
                return true;
            default:
                relief = Relief.Flat;
                return false;
        }
    }

    private static string ToSnake(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(pascal[i]));
        }

        return sb.ToString();
    }

    private readonly struct Reader(JsonElement element, string at, List<string> errors)
    {
        public string String(string name)
        {
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
            {
                return v.GetString()!;
            }

            errors.Add($"{at}.{name}: required text.");
            return string.Empty;
        }

        public string Key(string name, HashSet<string> seen)
        {
            string key = String(name);
            if (key.Length == 0)
            {
                return key;
            }

            foreach (char c in key)
            {
                if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                {
                    errors.Add($"{at}.{name}: '{key}' may contain only a-z, 0-9 and _.");
                    break;
                }
            }

            if (!seen.Add(key))
            {
                errors.Add($"{at}.{name}: duplicate key '{key}'.");
            }

            return key;
        }

        public int Int(string name, int min, int max)
        {
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n))
            {
                if (n < min || n > max)
                {
                    errors.Add($"{at}.{name}: {n} is outside {min}..{max}.");
                }

                return n;
            }

            errors.Add($"{at}.{name}: required whole number.");
            return min;
        }

        public Amount Amount(string name, Amount min, Amount max) =>
            ParseAmount(name, element.TryGetProperty(name, out JsonElement v) ? v : default, min, max);

        public List<GoodQuantity> Goods(string name, List<GoodDefinition> goods)
        {
            var list = new List<GoodQuantity>();
            if (!element.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{at}.{name}: required object of good keys and amounts.");
                return list;
            }

            foreach (JsonProperty p in v.EnumerateObject())
            {
                int id = goods.FindIndex(g => g.Key == p.Name);
                Amount amount = ParseAmount($"{name}.{p.Name}", p.Value, Core.Numerics.Amount.Epsilon, Core.Numerics.Amount.FromUnits(1_000_000));
                if (id < 0)
                {
                    errors.Add($"{at}.{name}: unknown good '{p.Name}'.");
                    continue;
                }

                if (list.Exists(q => q.GoodId == id))
                {
                    errors.Add($"{at}.{name}: good '{p.Name}' is listed twice.");
                    continue;
                }

                list.Add(new GoodQuantity(id, amount));
            }

            // Fixed order by good id, so nothing depends on the order in the file.
            list.Sort((a, b) => a.GoodId.CompareTo(b.GoodId));
            return list;
        }

        private Amount ParseAmount(string name, JsonElement v, Amount min, Amount max)
        {
            string? text = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };
            if (!Core.Numerics.Amount.TryParse(text, out Amount amount))
            {
                errors.Add($"{at}.{name}: required amount such as \"12\" or \"0.25\" (at most 2 decimal places).");
                return min;
            }

            if (amount < min || amount > max)
            {
                errors.Add($"{at}.{name}: {amount} is outside {min}..{max}.");
            }

            return amount;
        }

        public bool Bool(string name)
        {
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return v.GetBoolean();
            }

            errors.Add($"{at}.{name}: required true or false.");
            return false;
        }

        public (int Min, int Max) Range(string name, int min, int max)
        {
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2
                && v[0].TryGetInt32(out int a) && v[1].TryGetInt32(out int b))
            {
                if (a > b || a < min || b > max)
                {
                    errors.Add($"{at}.{name}: [{a}, {b}] must be ordered and inside {min}..{max}.");
                }

                return (a, b);
            }

            errors.Add($"{at}.{name}: required [min, max].");
            return (min, max);
        }

        public List<string> StringList(string name)
        {
            var list = new List<string>();
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0)
            {
                foreach (JsonElement s in v.EnumerateArray())
                {
                    if (s.ValueKind == JsonValueKind.String)
                    {
                        list.Add(s.GetString()!);
                    }
                    else
                    {
                        errors.Add($"{at}.{name}: every entry must be text.");
                    }
                }

                return list;
            }

            errors.Add($"{at}.{name}: required non-empty list.");
            return list;
        }

        public T Enum<T>(string name)
            where T : struct, System.Enum
        {
            string s = String(name);
            foreach (T value in System.Enum.GetValues<T>())
            {
                if (ToSnake(value.ToString()) == s)
                {
                    return value;
                }
            }

            if (s.Length > 0)
            {
                errors.Add($"{at}.{name}: unknown value '{s}'.");
            }

            return default;
        }

        public uint Color(string name)
        {
            string s = String(name);
            if (s.Length == 7 && s[0] == '#' && uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            {
                return rgb;
            }

            if (s.Length > 0)
            {
                errors.Add($"{at}.{name}: '{s}' must look like #RRGGBB.");
            }

            return 0;
        }
    }
}

/// <summary>Game content failed validation.</summary>
public sealed class ContentException : Exception
{
    /// <summary>Creates the exception from a list of problems.</summary>
    public ContentException(IReadOnlyList<string> errors)
        : base("Game data is invalid:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", errors))
    {
        Errors = errors;
    }

    /// <summary>Every problem found.</summary>
    public IReadOnlyList<string> Errors { get; }
}
