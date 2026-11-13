using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CivSim.Data;

/// <summary>
/// Checks data files against the JSON Schemas in <c>data/schema/</c> (stage 6, T14): a misspelled key is an error at load
/// instead of a value that is silently ignored. Supports the subset of JSON Schema the schemas use: <c>type</c>,
/// <c>properties</c>, <c>additionalProperties</c> (false or a schema for maps) and <c>items</c>. Values themselves
/// (ranges, known keys of other content) are checked by <see cref="ContentLoader"/>.
/// </summary>
public static class SchemaValidator
{
    /// <summary>The folder of schemas inside a data folder.</summary>
    public const string FolderName = "schema";

    /// <summary>A key allowed in every object of every file: notes for people, never read.</summary>
    public const string CommentKey = "comment";

    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// Checks every <c>*.json</c> of <paramref name="directory"/> that has a schema in its <c>schema</c> folder and adds
    /// readable errors to <paramref name="errors"/>. Does nothing when the folder has no <c>schema</c> folder
    /// (test folders with a few changed files).
    /// </summary>
    public static void ValidateDirectory(string directory, List<string> errors)
    {
        string schemaDir = Path.Combine(directory, FolderName);
        if (!Directory.Exists(schemaDir))
        {
            return;
        }

        string[] dataFiles = Directory.GetFiles(directory, "*.json");
        Array.Sort(dataFiles, StringComparer.Ordinal);
        foreach (string dataPath in dataFiles)
        {
            string name = Path.GetFileName(dataPath);
            string schemaPath = Path.Combine(schemaDir, Path.GetFileNameWithoutExtension(name) + ".schema.json");
            if (!File.Exists(schemaPath))
            {
                errors.Add($"{name}: no schema {FolderName}/{Path.GetFileName(schemaPath)} (write one with docs/systems/tools/infer_schema.py).");
                continue;
            }

            try
            {
                using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath), Options);
                using JsonDocument data = JsonDocument.Parse(File.ReadAllText(dataPath), Options);
                var found = new List<string>();
                Validate(data.RootElement, schema.RootElement, string.Empty, found);
                foreach (string e in found)
                {
                    errors.Add($"{name}: {e}");
                }
            }
            catch (JsonException)
            {
                // Broken data JSON is reported by the loader with its line; a broken schema is reported here.
                if (!IsValidJson(schemaPath))
                {
                    errors.Add($"{FolderName}/{Path.GetFileName(schemaPath)}: invalid JSON.");
                }
            }
        }
    }

    /// <summary>Checks <paramref name="value"/> against <paramref name="schema"/>; <paramref name="at"/> names the place in errors.</summary>
    public static void Validate(JsonElement value, JsonElement schema, string at, List<string> errors)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("type", out JsonElement type) && !Matches(value, type))
        {
            errors.Add($"{(at.Length == 0 ? "the file" : at)}: must be {Describe(type)}, not {Describe(value.ValueKind)}.");
            return;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            ValidateObject(value, schema, at, errors);
        }
        else if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out JsonElement items))
        {
            int i = 0;
            foreach (JsonElement item in value.EnumerateArray())
            {
                Validate(item, items, $"{at}[{i++}]", errors);
            }
        }
    }

    private static void ValidateObject(JsonElement value, JsonElement schema, string at, List<string> errors)
    {
        bool hasProperties = schema.TryGetProperty("properties", out JsonElement properties) && properties.ValueKind == JsonValueKind.Object;
        bool hasExtra = schema.TryGetProperty("additionalProperties", out JsonElement extra);
        foreach (JsonProperty p in value.EnumerateObject())
        {
            string where = at.Length == 0 ? p.Name : $"{at}.{p.Name}";
            if (hasProperties && properties.TryGetProperty(p.Name, out JsonElement sub))
            {
                Validate(p.Value, sub, where, errors);
            }
            else if (p.Name == CommentKey)
            {
                continue;
            }
            else if (hasExtra && extra.ValueKind == JsonValueKind.False)
            {
                string? near = hasProperties ? Nearest(p.Name, properties) : null;
                errors.Add(near is null
                    ? $"{where}: unknown key."
                    : $"{where}: unknown key, did you mean '{near}'?");
            }
            else if (hasExtra && extra.ValueKind == JsonValueKind.Object)
            {
                Validate(p.Value, extra, where, errors);
            }
        }
    }

    private static bool Matches(JsonElement value, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.String)
        {
            return Matches(value, type.GetString()!);
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement t in type.EnumerateArray())
            {
                if (t.ValueKind == JsonValueKind.String && Matches(value, t.GetString()!))
                {
                    return true;
                }
            }

            return false;
        }

        return true;
    }

    private static bool Matches(JsonElement value, string type) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "number" or "integer" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true,
    };

    private static string Describe(JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.Array)
        {
            var names = new List<string>();
            foreach (JsonElement t in type.EnumerateArray())
            {
                names.Add(Article(t.GetString() ?? "?"));
            }

            return string.Join(" or ", names);
        }

        return Article(type.GetString() ?? "?");
    }

    private static string Describe(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "a list",
        JsonValueKind.String => "text",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "true/false",
        _ => "null",
    };

    private static string Article(string type) => type switch
    {
        "object" => "an object",
        "array" => "a list",
        "string" => "text",
        "number" or "integer" => "a number",
        "boolean" => "true/false",
        _ => type,
    };

    // The known key closest to a misspelled one: same letters in another case, or at most 3 edits away (Levenshtein).
    private static string? Nearest(string name, JsonElement properties)
    {
        string? best = null;
        int bestDistance = int.MaxValue;
        foreach (JsonProperty p in properties.EnumerateObject())
        {
            int d = string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ? 0 : Distance(name, p.Name);
            if (d < bestDistance || (d == bestDistance && string.CompareOrdinal(p.Name, best) < 0))
            {
                best = p.Name;
                bestDistance = d;
            }
        }

        return bestDistance <= Math.Max(1, Math.Min(3, name.Length / 3)) ? best : null;
    }

    private static int Distance(string a, string b)
    {
        var row = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++)
        {
            row[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            int previous = row[0];
            row[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int current = row[j];
                int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + cost);
                previous = current;
            }
        }

        return row[b.Length];
    }

    private static bool IsValidJson(string path)
    {
        try
        {
            using JsonDocument _ = JsonDocument.Parse(File.ReadAllText(path), Options);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
