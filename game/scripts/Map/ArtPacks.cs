using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Godot;

namespace CivSim.Game.Map;

/// <summary>
/// An art pack: a folder with pictures under the same paths as <c>art/placeholder/</c> (for example
/// <c>biome/biome_grassland_01.png</c>) and an optional <c>pack.json</c> with name, author and description. Packs live
/// in <c>art/packs/&lt;folder&gt;/</c> next to the game or in the user folder <c>user://art_packs/&lt;folder&gt;/</c>.
/// A pack may hold any subset of the pictures; the rest comes from <c>art/export/</c> and then the placeholders.
/// </summary>
public sealed record ArtPack(string Key, string Name, string Directory, string Author, string Description)
{
    /// <summary>Folder for packs in the user's data (created on demand).</summary>
    public static string UserFolder => ProjectSettings.GlobalizePath("user://art_packs");

    /// <summary>Every pack found, by folder name; a user pack with the same folder name as a game pack wins.</summary>
    public static List<ArtPack> Discover(string artDirectory)
    {
        var found = new SortedDictionary<string, ArtPack>(StringComparer.Ordinal);
        foreach (string root in new[] { Path.Combine(artDirectory, "packs"), UserFolder })
        {
            if (!System.IO.Directory.Exists(root))
            {
                continue;
            }

            foreach (string dir in System.IO.Directory.GetDirectories(root))
            {
                string key = Path.GetFileName(dir);
                found[key] = Read(key, dir);
            }
        }

        return [.. found.Values];
    }

    /// <summary>The pack with this folder name, or null.</summary>
    public static ArtPack? Find(string artDirectory, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return Discover(artDirectory).Find(p => p.Key == key);
    }

    private static ArtPack Read(string key, string dir)
    {
        string name = key;
        string author = string.Empty;
        string description = string.Empty;
        string file = Path.Combine(dir, "pack.json");
        if (File.Exists(file))
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
                JsonElement root = doc.RootElement;
                name = root.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : key;
                author = root.TryGetProperty("author", out JsonElement a) && a.ValueKind == JsonValueKind.String ? a.GetString()! : string.Empty;
                description = root.TryGetProperty("description", out JsonElement d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : string.Empty;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                description = "pack.json could not be read: " + ex.Message;
            }
        }

        return new ArtPack(key, name, dir, author, description);
    }
}

/// <summary>One problem with a picture of an art pack or of art/export.</summary>
/// <param name="Source">Where the file is: the pack name or "art/export".</param>
/// <param name="File">Path inside the source, e.g. <c>biome/biome_grassland_01.png</c>.</param>
/// <param name="Problem">What is wrong and what the viewer does instead.</param>
public sealed record ArtIssue(string Source, string File, string Problem);

/// <summary>
/// Checks the pictures of one source folder against <c>art/placeholder/manifest.json</c> when the viewer starts:
/// a file is used only when its name is known, it is a readable PNG and it has exactly the expected size. Other
/// files are listed as issues and the placeholder (or art/export) is drawn instead, so a wrong file never breaks the map.
/// </summary>
public sealed class ArtCheck
{
    /// <summary>Accepted files: path inside the source → full path on disk.</summary>
    public Dictionary<string, string> Accepted { get; } = new(StringComparer.Ordinal);

    /// <summary>Problems found.</summary>
    public List<ArtIssue> Issues { get; } = [];

    /// <summary>
    /// Checks every file under <paramref name="directory"/>. <paramref name="expected"/> maps paths to sizes.
    /// Returns an empty check when the folder does not exist.
    /// </summary>
    public static ArtCheck Run(string source, string directory, IReadOnlyDictionary<string, (int Width, int Height)> expected)
    {
        var check = new ArtCheck();
        if (!Directory.Exists(directory))
        {
            return check;
        }

        string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (string path in files)
        {
            string rel = Path.GetRelativePath(directory, path).Replace('\\', '/');
            string name = Path.GetFileName(rel);
            if (name is "pack.json" or "art_check.txt" or "README.md" or ".gitkeep" || name.StartsWith('.'))
            {
                continue;
            }

            if (!rel.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                check.Issues.Add(new ArtIssue(source, rel, "not a .png file; ignored (export from LibreSprite as PNG)"));
                continue;
            }

            if (!expected.TryGetValue(rel, out var size))
            {
                // A known file name in the wrong folder is the most likely mistake, so say where it belongs.
                string? right = null;
                foreach (string known in expected.Keys)
                {
                    if (Path.GetFileName(known) == name)
                    {
                        right = known;
                        break;
                    }
                }

                check.Issues.Add(new ArtIssue(source, rel, right is null
                    ? "unknown name; ignored (see art/placeholder/manifest.json for the names the game uses)"
                    : $"wrong folder; ignored (it belongs in {right})"));
                continue;
            }

            var image = new Image();
            if (image.Load(path) != Error.Ok || image.IsEmpty())
            {
                check.Issues.Add(new ArtIssue(source, rel, "cannot be read as a PNG; the default picture is used"));
                continue;
            }

            if (image.GetWidth() != size.Width || image.GetHeight() != size.Height)
            {
                check.Issues.Add(new ArtIssue(source, rel, $"is {image.GetWidth()}×{image.GetHeight()} px, must be {size.Width}×{size.Height} px; the default picture is used"));
                continue;
            }

            check.Accepted[rel] = path;
        }

        return check;
    }

    /// <summary>A readable report, written next to the pack as art_check.txt.</summary>
    public static string Report(string source, ArtCheck check, int expected)
    {
        var sb = new StringBuilder();
        sb.Append($"Art check of {source}: {check.Accepted.Count} of {expected} pictures used, {check.Issues.Count} problems.\n");
        foreach (ArtIssue issue in check.Issues)
        {
            sb.Append($"- {issue.File}: {issue.Problem}\n");
        }

        return sb.ToString();
    }
}
