using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CivSim.Sim.Content;

/// <summary>
/// A test scenario (Patryk, 2026-11-19): a game that starts at a chosen moment of development, with every civilization set up as
/// written, to test later technologies, war and the rest without playing up to them. Read from
/// <c>data/sim/scenarios/*.json</c>; every field is optional and falls back to a normal start.
/// </summary>
public sealed record SimScenario
{
    /// <summary>File name without extension.</summary>
    public string Key { get; init; } = "";

    /// <summary>Shown in the menu.</summary>
    public string Name { get; init; } = "";

    /// <summary>What it is for.</summary>
    public string Description { get; init; } = "";

    /// <summary>World seed.</summary>
    public ulong Seed { get; init; } = 1;

    /// <summary>Legacy map size (the map is denser, see density).</summary>
    public int Width { get; init; } = 98;

    /// <summary>Legacy map height.</summary>
    public int Height { get; init; } = 60;

    /// <summary>Number of civilizations.</summary>
    public int CivCount { get; init; } = 4;

    /// <summary>Everyone knows the whole map and every civilization from the start.</summary>
    public bool RevealMap { get; init; }

    /// <summary>Every hex we know is looked at by prospectors and surveyors: its deposits show (later eras' ores as "???").</summary>
    public bool Surveyed { get; init; }

    /// <summary>The debug keys work (goods, finish research, reveal the map, more people).</summary>
    public bool Debug { get; init; } = true;

    /// <summary>How each civilization starts, by index (0 is the player).</summary>
    public IReadOnlyList<ScenarioPeople> Civs { get; init; } = [];

    /// <summary>Pairs of civilizations at war from the start.</summary>
    public IReadOnlyList<int[]> Wars { get; init; } = [];

    /// <summary>Two civilizations whose armies start on a field between them, facing each other (empty = at home).</summary>
    public int[] Field { get; init; } = [];

    /// <summary>Hexes between the two armies of the field.</summary>
    public int FieldGap { get; init; } = 4;

    /// <summary>No peace can be proposed (to test battles to the end).</summary>
    public bool NoPeace { get; init; }

    /// <summary>The scenarios folder of a data directory (default: the game's data).</summary>
    public static string Folder(string? dataDirectory = null) => Path.Combine(dataDirectory ?? CivSim.Data.ContentLoader.DefaultDirectory, "sim", "scenarios");

    /// <summary>Reads every scenario in a folder, sorted by key.</summary>
    public static List<SimScenario> LoadAll(string directory)
    {
        var list = new List<SimScenario>();
        if (!Directory.Exists(directory))
        {
            return list;
        }

        foreach (string path in Directory.GetFiles(directory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            list.Add(Load(path));
        }

        return list;
    }

    /// <summary>Reads one scenario file.</summary>
    public static SimScenario Load(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        JsonElement r = doc.RootElement;
        string key = Path.GetFileNameWithoutExtension(path);
        var civs = new List<ScenarioPeople>();
        if (r.TryGetProperty("civs", out JsonElement cs))
        {
            foreach (JsonElement c in cs.EnumerateArray())
            {
                civs.Add(new ScenarioPeople
                {
                    Era = Str(c, "era"),
                    Techs = Strs(c, "techs"),
                    People = Int(c, "people", 0),
                    Level = Str(c, "level"),
                    Stock = Amounts(c, "stock"),
                    Settlements = Int(c, "settlements", 0),
                    SettlementPeople = Int(c, "settlementPeople", 20),
                    Buildings = Amounts(c, "buildings").ToDictionary(p => p.Key, p => (int)p.Value),
                    Units = Amounts(c, "units").ToDictionary(p => p.Key, p => (int)p.Value),
                    Aggression = c.TryGetProperty("aggression", out JsonElement ag) ? ag.GetInt32() : null,
                    Expansion = c.TryGetProperty("expansion", out JsonElement ex) ? ex.GetInt32() : null,
                    Ai = c.TryGetProperty("ai", out JsonElement ai) ? ai.GetBoolean() : null,
                    UnitPeople = Int(c, "unitPeople", 0),
                    Weapon = Int(c, "weapon", -1),
                    Armour = Int(c, "armour", -1),
                    WallRing = Int(c, "wallRing", 0),
                    RoadStar = Int(c, "roadStar", 0),
                    WorkRings = Amounts(c, "workRings").ToDictionary(p => p.Key, p => (int)p.Value),
                });
            }
        }

        var wars = new List<int[]>();
        if (r.TryGetProperty("wars", out JsonElement ws))
        {
            foreach (JsonElement w in ws.EnumerateArray())
            {
                wars.Add([.. w.EnumerateArray().Select(x => x.GetInt32())]);
            }
        }

        return new SimScenario
        {
            Key = key,
            Name = Str(r, "name") is { Length: > 0 } n ? n : key,
            Description = Str(r, "description"),
            Seed = r.TryGetProperty("seed", out JsonElement s) ? s.GetUInt64() : 1,
            Width = Int(r, "width", 98),
            Height = Int(r, "height", 60),
            CivCount = Int(r, "civilizations", Math.Max(2, civs.Count)),
            RevealMap = r.TryGetProperty("revealMap", out JsonElement rm) && rm.GetBoolean(),
            Surveyed = r.TryGetProperty("surveyed", out JsonElement sv) && sv.GetBoolean(),
            Debug = !r.TryGetProperty("debug", out JsonElement db) || db.GetBoolean(),
            Civs = civs,
            Wars = wars,
            Field = r.TryGetProperty("field", out JsonElement fl) ? [.. fl.EnumerateArray().Select(x => x.GetInt32())] : [],
            FieldGap = Int(r, "fieldGap", 4),
            NoPeace = r.TryGetProperty("noPeace", out JsonElement np) && np.GetBoolean(),
        };

        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) ? v.GetString() ?? "" : "";

        static int Int(JsonElement e, string name, int fallback) => e.TryGetProperty(name, out JsonElement v) ? v.GetInt32() : fallback;

        static string[] Strs(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) ? [.. v.EnumerateArray().Select(x => x.GetString() ?? "")] : [];

        static Dictionary<string, double> Amounts(JsonElement e, string name)
        {
            var d = new Dictionary<string, double>();
            if (e.TryGetProperty(name, out JsonElement v))
            {
                foreach (JsonProperty p in v.EnumerateObject())
                {
                    d[p.Name] = p.Value.GetDouble();
                }
            }

            return d;
        }
    }
}

/// <summary>How one civilization starts in a scenario. Empty fields keep the normal start.</summary>
public sealed record ScenarioPeople
{
    /// <summary>A finished stone wall on every hex of its land this far from its centre (0 = none).</summary>
    public int WallRing { get; init; }

    /// <summary>Finished roads from the first settlement's centre, this many hexes in each of the six directions: paved
    /// on every other one, tracks on the rest (to see roads, junctions and bridges); 0 = none.</summary>
    public int RoadStar { get; init; }

    /// <summary>Finished works (key: distance) on every hex of its land at that distance from the centre.</summary>
    public IReadOnlyDictionary<string, int> WorkRings { get; init; } = new Dictionary<string, int>();

    /// <summary>Size of every army unit it starts with (0 = the kind's default).</summary>
    public int UnitPeople { get; init; }

    /// <summary>Weapon level of its starting soldiers (−1 = the best its stock holds).</summary>
    public int Weapon { get; init; } = -1;

    /// <summary>Armour level of its starting soldiers (−1 = the best its stock holds).</summary>
    public int Armour { get; init; } = -1;

    /// <summary>Era key: every technology of the eras before it is known, and the civilization is in it.</summary>
    public string Era { get; init; } = "";

    /// <summary>More technologies known (keys), with what they need.</summary>
    public string[] Techs { get; init; } = [];

    /// <summary>People of the first settlement (0 = as usual).</summary>
    public int People { get; init; }

    /// <summary>Level of the first settlement (key, e.g. village).</summary>
    public string Level { get; init; } = "";

    /// <summary>Goods in the first settlement.</summary>
    public Dictionary<string, double> Stock { get; init; } = [];

    /// <summary>More settlements around the first.</summary>
    public int Settlements { get; init; }

    /// <summary>People of each of them.</summary>
    public int SettlementPeople { get; init; } = 20;

    /// <summary>Finished buildings in the first settlement: key and how many.</summary>
    public Dictionary<string, int> Buildings { get; init; } = [];

    /// <summary>Units ready at the first settlement: key and how many.</summary>
    public Dictionary<string, int> Units { get; init; } = [];

    /// <summary>AI character (0–100), or as rolled.</summary>
    public int? Aggression { get; init; }

    /// <summary>AI character (0–100), or as rolled.</summary>
    public int? Expansion { get; init; }

    /// <summary>Played by the AI (default: all but people 0).</summary>
    public bool? Ai { get; init; }
}
