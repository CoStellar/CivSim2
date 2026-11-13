using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CivSim.Game.Map;
using CivSim.Protocol.Hosting;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// What the start menu asks (stage 5, GDD 3 "Ustawienia świata" plus simulation options), as the user typed it.
/// Planet values left empty mean "as the preset". Numbers use a dot for decimals.
/// </summary>
public sealed class StartSettings
{
    /// <summary>World seed (digits).</summary>
    public string Seed { get; set; } = "1";

    /// <summary>test, standard, large or huge.</summary>
    public string MapSize { get; set; } = "standard";

    /// <summary>Number of civilizations; 0 = default of the map size.</summary>
    public int Civilizations { get; set; }

    /// <summary>continents, pangea or archipelago.</summary>
    public string WorldType { get; set; } = "continents";

    /// <summary>Planet preset key.</summary>
    public string Planet { get; set; } = "earth";

    /// <summary>Water percent (30–80) or empty for the preset.</summary>
    public string Water { get; set; } = string.Empty;

    /// <summary>preset, dry, normal or wet.</summary>
    public string Moisture { get; set; } = "preset";

    /// <summary>normal, dry or humid.</summary>
    public string Inland { get; set; } = "normal";

    /// <summary>preset, flat, normal or mountainous.</summary>
    public string Relief { get; set; } = "preset";

    /// <summary>scarce, normal or rich.</summary>
    public string Resources { get; set; } = "normal";

    /// <summary>Axial tilt in degrees.</summary>
    public string Tilt { get; set; } = string.Empty;

    /// <summary>Orbit eccentricity (0–0.3).</summary>
    public string Eccentricity { get; set; } = string.Empty;

    /// <summary>Star brightness in Suns.</summary>
    public string Luminosity { get; set; } = string.Empty;

    /// <summary>Distance in AU.</summary>
    public string Distance { get; set; } = string.Empty;

    /// <summary>Year in Earth years.</summary>
    public string Year { get; set; } = string.Empty;

    /// <summary>Light compared with Earth's.</summary>
    public string Light { get; set; } = string.Empty;

    /// <summary>CO₂ in bar (e.g. 0.00028); empty = carbon cycle.</summary>
    public string Co2 { get; set; } = string.Empty;

    /// <summary>Total pressure in bar.</summary>
    public string Pressure { get; set; } = string.Empty;

    /// <summary>Volcanism in percent of Earth's.</summary>
    public string Volcanism { get; set; } = string.Empty;

    /// <summary>Star brightness swing per mille (0–100).</summary>
    public string StarVariability { get; set; } = string.Empty;

    /// <summary>Period of the star's cycle in years.</summary>
    public string StarCycle { get; set; } = string.Empty;

    /// <summary>Civilization to lead (stage 6 PR P2): 0 = watch every civilization as an observer, N = lead civilization number N (id N − 1).</summary>
    public int Lead { get; set; }

    /// <summary>pause, x1, x2, x4 or max.</summary>
    public string Speed { get; set; } = "pause";

    /// <summary>Pause after this many turns (0 = never).</summary>
    public long TurnLimit { get; set; }

    /// <summary>Write a CSV line per turn.</summary>
    public bool Report { get; set; }

    /// <summary>Where the CSV goes; empty = user folder.</summary>
    public string ReportPath { get; set; } = string.Empty;

    /// <summary>Long run length, e.g. "4h", "30m", "90s" or hours as a number ("2.5"); empty or "0" = no long run.</summary>
    public string LongRun { get; set; } = string.Empty;

    /// <summary>Folder for the long run's files; empty = long_runs/ next to the project (user folder in an exported game).</summary>
    public string LongRunFolder { get; set; } = string.Empty;

    /// <summary>Seconds between rows of the long run's stats.csv.</summary>
    public int LongRunEvery { get; set; } = 10;

    /// <summary>Command-line names of the start settings (after <c>--</c>); any of them skips the start menu.</summary>
    public static readonly string[] ArgumentNames =
    [
        "--seed", "--map-size", "--civs", "--world-type", "--planet", "--water", "--moisture", "--inland", "--relief", "--resources",
        "--tilt", "--eccentricity", "--luminosity", "--distance", "--year", "--light", "--co2", "--pressure", "--volcanism",
        "--star-variability", "--star-cycle", "--speed", "--turn-limit", "--report",
        "--long-run", "--long-run-folder", "--long-run-every", "--lead",
    ];

    /// <summary>
    /// A copy of these settings with the values given on the command line (same names and units as before stage 5:
    /// <c>--tilt 23.4</c>, <c>--co2 0.0004</c> in bar...). Options are matched by name; others are skipped.
    /// </summary>
    public StartSettings WithArguments(IReadOnlyList<string> args)
    {
        StartSettings s = (StartSettings)MemberwiseClone();
        for (int i = 0; i + 1 < args.Count; i++)
        {
            string v = args[i + 1];
            switch (args[i])
            {
                case "--seed": s.Seed = v; break;
                case "--map-size": s.MapSize = v; s.Civilizations = 0; break;
                case "--civs": s.Civilizations = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--world-type": s.WorldType = v; break;
                case "--planet": s.Planet = v; break;
                case "--water": s.Water = v; break;
                case "--moisture": s.Moisture = v; break;
                case "--inland": s.Inland = v; break;
                case "--relief": s.Relief = v; break;
                case "--resources": s.Resources = v; break;
                case "--tilt": s.Tilt = v; break;
                case "--eccentricity": s.Eccentricity = v; break;
                case "--luminosity": s.Luminosity = v; break;
                case "--distance": s.Distance = v; break;
                case "--year": s.Year = v; break;
                case "--light": s.Light = v; break;
                case "--co2": s.Co2 = v; break;
                case "--pressure": s.Pressure = v; break;
                case "--volcanism": s.Volcanism = v; break;
                case "--star-variability": s.StarVariability = v; break;
                case "--star-cycle": s.StarCycle = v; break;
                case "--speed": s.Speed = v; break;
                case "--turn-limit": s.TurnLimit = long.Parse(v, CultureInfo.InvariantCulture); break;
                case "--report": s.Report = true; s.ReportPath = v; break;
                case "--art-pack": s.ArtPack = v; break;
                case "--long-run": s.LongRun = v; break;
                case "--long-run-folder": s.LongRunFolder = v; break;
                case "--lead": s.Lead = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--long-run-every": s.LongRunEvery = int.Parse(v, CultureInfo.InvariantCulture); break;
            }
        }

        return s;
    }

    /// <summary>A copy (the menu edits copies, so Cancel never changes the saved settings).</summary>
    public StartSettings Copy() => (StartSettings)MemberwiseClone();

    /// <summary>Folder name of the art pack; empty = default pictures.</summary>
    public string ArtPack { get; set; } = string.Empty;

    /// <summary>Speed word to the protocol value.</summary>
    public static SimulationSpeed ParseSpeed(string? word) => word switch
    {
        "x1" => SimulationSpeed.X1,
        "x2" => SimulationSpeed.X2,
        "x4" or "x5" => SimulationSpeed.X4, // "x5" is the old word, kept for saved settings
        "max" => SimulationSpeed.Max,
        _ => SimulationSpeed.Paused,
    };

    /// <summary>
    /// The host options these settings describe. Throws <see cref="FormatException"/> with a readable message when a
    /// field is not a number.
    /// </summary>
    public LocalGameOptions ToOptions()
    {
        if (!ulong.TryParse(Seed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
        {
            throw new FormatException("Seed must be a whole number.");
        }

        LocalGameOptions o = LocalGameOptions.ForSize(MapSize, seed);
        string report = Report
            ? (string.IsNullOrWhiteSpace(ReportPath) ? ProjectSettings.GlobalizePath("user://turn_report.csv") : ReportPath.Trim())
            : string.Empty;
        long longRun = ParseDuration(LongRun);
        string? longRunFolder = null;
        if (longRun > 0)
        {
            string root = string.IsNullOrWhiteSpace(LongRunFolder) ? DefaultLongRunRoot() : LongRunFolder.Trim();
            longRunFolder = Path.Combine(root, string.Create(CultureInfo.InvariantCulture, $"{DateTime.Now:yyyy-MM-dd_HH-mm}_seed{seed}_{MapSize}"));
        }

        return o with
        {
            Civilizations = Civilizations > 0 ? Math.Min(64, Civilizations) : o.Civilizations,
            WorldType = WorldType,
            Planet = Planet,
            WaterPercent = Int(Water, "Water"),
            Moisture = Moisture == "preset" ? null : Moisture,
            Inland = Inland,
            Relief = Relief == "preset" ? null : Relief,
            Resources = Resources,
            AxialTiltTenths = (int?)Scaled(Tilt, 10, "Axial tilt"),
            EccentricityPerMille = (int?)Scaled(Eccentricity, 1000, "Eccentricity"),
            LuminosityThousandths = Scaled(Luminosity, 1000, "Star brightness"),
            DistanceThousandths = Scaled(Distance, 1000, "Distance"),
            YearThousandths = Scaled(Year, 1000, "Year"),
            LightThousandths = Scaled(Light, 1000, "Light"),
            Co2Microbar = (int?)Scaled(Co2, 1_000_000, "CO2"),
            PressureHundredths = (int?)Scaled(Pressure, 100, "Pressure"),
            VolcanismPercent = Int(Volcanism, "Volcanism"),
            StarVariabilityPerMille = Int(StarVariability, "Star variability"),
            StarCycleYears = Int(StarCycle, "Star cycle"),
            TurnLimit = Math.Max(0, TurnLimit),
            ReportPath = string.IsNullOrEmpty(report) ? null : report,
            LongRunFolder = longRunFolder,
            LongRunSeconds = longRun,
            LongRunSampleSeconds = Math.Clamp(LongRunEvery, 1, 3600),
        };
    }

    /// <summary>
    /// Seconds of a long run: "4h", "30m", "90s", or a number of hours ("2.5"). Empty or "0" = 0 (no long run).
    /// Throws <see cref="FormatException"/> with a readable message for anything else.
    /// </summary>
    public static long ParseDuration(string? text)
    {
        string t = (text ?? string.Empty).Trim().ToLowerInvariant().Replace(',', '.');
        if (t.Length == 0)
        {
            return 0;
        }

        int unit = t[^1] switch { 'h' => 3600, 'm' => 60, 's' => 1, _ => 0 };
        string number = unit == 0 ? t : t[..^1].Trim();
        if (!decimal.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal value) || value > 1000 * 3600)
        {
            throw new FormatException($"Long run: '{text}' is not a length of time (use for example 4h, 30m or 90s).");
        }

        return (long)Math.Round(value * (unit == 0 ? 3600 : unit));
    }

    // Next to the project while developing (I:\CivSim\CivSim2\long_runs), the user folder in an exported game.
    private static string DefaultLongRunRoot() => OS.HasFeature("editor")
        ? ProjectSettings.GlobalizePath("res://../long_runs")
        : ProjectSettings.GlobalizePath("user://long_runs");

    private static int? Int(string text, string what) => (int?)Scaled(text, 1, what);

    private static long? Scaled(string text, int scale, string what)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value))
        {
            throw new FormatException($"{what}: '{text}' is not a number (use a dot for decimals).");
        }

        return (long)Math.Round(value * scale);
    }
}

/// <summary>Everything the viewer remembers between runs (user://viewer_settings.json).</summary>
public sealed class ViewerSettings
{
    private const string File = "user://viewer_settings.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Last start settings.</summary>
    public StartSettings Start { get; set; } = new();

    /// <summary>Layer switches, opacities and order.</summary>
    public List<LayerSetting>? Layers { get; set; }

    /// <summary>Which HUD panels were open.</summary>
    public HudPanels Panels { get; set; } = new();

    /// <summary>Reads the saved settings; defaults when there are none or the file is broken.</summary>
    public static ViewerSettings Load()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath(File);
            if (System.IO.File.Exists(path))
            {
                return JsonSerializer.Deserialize<ViewerSettings>(System.IO.File.ReadAllText(path), Json) ?? new ViewerSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            GD.PrintErr($"viewer settings not read: {ex.Message}");
        }

        return new ViewerSettings();
    }

    /// <summary>Writes the settings.</summary>
    public void Save()
    {
        try
        {
            System.IO.File.WriteAllText(ProjectSettings.GlobalizePath(File), JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            GD.PrintErr($"viewer settings not saved: {ex.Message}");
        }
    }
}
