using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CivSim.Core.Content;

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
        if (errors.Count > 0)
        {
            throw new ContentException(errors);
        }

        return new GameContent(biomes, deposits);
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
