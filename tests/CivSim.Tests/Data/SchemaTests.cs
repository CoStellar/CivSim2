using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CivSim.Data;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Content;

/// <summary>Stage 6, T14: data files are checked against <c>data/schema/</c>, so a misspelled key is an error at load.</summary>
public sealed class SchemaTests
{
    private static readonly string DataDir = ContentLoader.DefaultDirectory;
    private static readonly string SchemaDir = Path.Combine(DataDir, SchemaValidator.FolderName);

    [Fact]
    public void EveryDataFileHasASchemaAndEverySchemaAFile()
    {
        string[] data = [.. Directory.GetFiles(DataDir, "*.json").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal)!];
        string[] schemas = [.. Directory.GetFiles(SchemaDir, "*.schema.json").Select(f => Path.GetFileName(f)[..^".schema.json".Length]).Order(StringComparer.Ordinal)];
        Assert.NotEmpty(data);
        Assert.Equal(data, schemas);
    }

    [Fact]
    public void TheGameDataFollowsItsSchemas()
    {
        var errors = new List<string>();
        SchemaValidator.ValidateDirectory(DataDir, errors);
        Assert.Empty(errors);
    }

    [Fact]
    public void AMisspelledKeyIsAnErrorWithTheNearestKnownKey()
    {
        List<string> errors = Check("""{"kinds": [{"key": "a", "ticksPerCel": 5}]}""");
        Assert.Equal(["kinds[0].ticksPerCel: unknown key, did you mean 'ticksPerCell'?"], errors);
    }

    [Fact]
    public void AnUnknownKeyFarFromAnyKnownOneHasNoSuggestion()
    {
        List<string> errors = Check("""{"somethingElse": 1}""");
        Assert.Equal(["somethingElse: unknown key."], errors);
    }

    [Fact]
    public void AWrongKindOfValueIsAnError()
    {
        List<string> errors = Check("""{"kinds": {"key": "a"}}""");
        Assert.Equal(["kinds: must be a list, not an object."], errors);
    }

    [Fact]
    public void MapsTakeAnyKeyAndCommentsAreAllowedEverywhere()
    {
        List<string> errors = Check("""{"comment": "notes", "kinds": [{"comment": "x", "key": "a", "terrain": {"biomes": {"anything": 150}}}]}""");
        Assert.Empty(errors);
    }

    [Fact]
    public void LoadingFolderDataReportsTheTypoAndTheViewerCheckSeesIt()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim-schema-" + Environment.ProcessId);
        Directory.CreateDirectory(Path.Combine(dir, SchemaValidator.FolderName));
        try
        {
            foreach (string file in Directory.GetFiles(DataDir, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            foreach (string file in Directory.GetFiles(SchemaDir, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, SchemaValidator.FolderName, Path.GetFileName(file)), overwrite: true);
            }

            Assert.Empty(LocalServerHost.CheckData(dir));
            string path = Path.Combine(dir, "units.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"routeNodeLimit\"", "\"routeNodeLimt\"", StringComparison.Ordinal));
            IReadOnlyList<string> errors = LocalServerHost.CheckData(dir);
            Assert.Contains("units.json: routeNodeLimt: unknown key, did you mean 'routeNodeLimit'?", errors);
            ContentException e = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(e.Errors, x => x.StartsWith("units.json: routeNodeLimt", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // Checks a document against the real units.json schema.
    private static List<string> Check(string json)
    {
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(SchemaDir, "units.schema.json")));
        using JsonDocument doc = JsonDocument.Parse(json);
        var errors = new List<string>();
        SchemaValidator.Validate(doc.RootElement, schema.RootElement, string.Empty, errors);
        return errors;
    }
}
