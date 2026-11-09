using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Protocol.Hosting;
using CivSim.Server;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// Start menu of the developer viewer (stage 5): world settings from GDD 3, the planet (preset plus any value), and
/// simulation options. A preview under the form shows the star, orbit, air and climate the settings give, or why
/// they cannot start. Built in code so it needs no art.
/// </summary>
public partial class StartMenu : CanvasLayer
{
    private static readonly string[] Sizes = ["test", "standard", "large", "huge"];
    private static readonly string[] WorldTypes = ["continents", "pangea", "archipelago"];
    private static readonly string[] Moistures = ["preset", "dry", "normal", "wet"];
    private static readonly string[] Inlands = ["normal", "dry", "humid"];
    private static readonly string[] Reliefs = ["preset", "flat", "normal", "mountainous"];
    private static readonly string[] ResourceLevels = ["normal", "scarce", "rich"];
    private static readonly string[] SpeedWords = ["pause", "x1", "x2", "x5", "max"];
    private static readonly Dictionary<string, int> DefaultCivs = new() { ["test"] = 4, ["standard"] = 12, ["large"] = 20, ["huge"] = 32 };

    private readonly LocalSetupInfo _setup;
    private readonly StartSettings _settings;
    private readonly string _dataDirectory;
    private readonly List<(LineEdit Edit, Func<PlanetPresetInfo, string> Preset)> _planetFields = [];
    private Label _preview = null!;
    private Label _error = null!;
    private Button _start = null!;
    private double _previewIn = -1;

    /// <summary>Creates the menu filled with <paramref name="settings"/> (edited in place).</summary>
    public StartMenu(LocalSetupInfo setup, StartSettings settings, string dataDirectory, Map.ArtLibrary? art = null, IReadOnlyList<Map.ArtPack>? packs = null)
    {
        _setup = setup;
        _settings = settings;
        _dataDirectory = dataDirectory;
        _art = art;
        _packs = packs ?? [];
    }

    /// <summary>Required by Godot; not used.</summary>
    public StartMenu()
        : this(new LocalSetupInfo([]), new StartSettings(), string.Empty)
    {
    }

    /// <summary>The settings as edited so far.</summary>
    public StartSettings Settings => _settings;

    /// <summary>Raised when another art pack is chosen, or the pictures should be checked again (same key).</summary>
    public event Action<string>? ArtPackChosen;

    private readonly Map.ArtLibrary? _art;
    private readonly IReadOnlyList<Map.ArtPack> _packs;

    /// <summary>Raised when the user presses Start; the settings are valid.</summary>
    public event Action<StartSettings>? StartRequested;

    /// <summary>Raised when the user presses Quit.</summary>
    public event Action? QuitRequested;

    /// <inheritdoc />
    public override void _Ready() => Build();

    private void Build()
    {
        var root = new PanelContainer { Theme = PixelTheme.Current };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var center = new CenterContainer();
        root.AddChild(center);
        var page = new VBoxContainer { CustomMinimumSize = new Vector2(1180, 0) };
        page.AddThemeConstantOverride("separation", 14);
        center.AddChild(page);

        var title = new Label { Text = "CivSim 2 — simulator", HorizontalAlignment = HorizontalAlignment.Center, ThemeTypeVariation = "Heading" };
        title.AddThemeFontSizeOverride("font_size", 32);
        page.AddChild(title);
        page.AddChild(new Label
        {
            Text = "Set up a world and watch every civilization play itself. Empty planet fields keep the preset's value.",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.75f, 0.75f, 0.75f),
        });

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 24);
        page.AddChild(columns);
        columns.AddChild(WorldColumn());
        columns.AddChild(PlanetColumn());
        columns.AddChild(SimulationColumn());

        var previewPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 170) };
        page.AddChild(previewPanel);
        var previewBox = new VBoxContainer();
        previewPanel.AddChild(previewBox);
        previewBox.AddChild(Heading("Planet preview"));
        _preview = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        previewBox.AddChild(_preview);
        _error = new Label { Modulate = new Color(1f, 0.45f, 0.4f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        previewBox.AddChild(_error);

        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 12);
        page.AddChild(buttons);
        var reset = new Button { Text = "Defaults", CustomMinimumSize = new Vector2(120, 40) };
        reset.Pressed += ResetToDefaults;
        buttons.AddChild(reset);
        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(120, 40) };
        quit.Pressed += () => QuitRequested?.Invoke();
        buttons.AddChild(quit);
        _start = new Button { Text = "Start", CustomMinimumSize = new Vector2(180, 40) };
        _start.Pressed += Start;
        buttons.AddChild(_start);
        _start.GrabFocus();
        UpdatePlaceholders();
        _previewIn = 0;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // The preview reads the data files, so it waits until typing pauses.
        if (_previewIn < 0)
        {
            return;
        }

        _previewIn -= delta;
        if (_previewIn <= 0)
        {
            _previewIn = -1;
            RefreshPreview();
        }
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Enter or Key.KpEnter })
        {
            Start();
        }
    }

    private Control WorldColumn()
    {
        GridContainer g = Column(out VBoxContainer box, "World");
        LineEdit seed = Text(g, "Seed", _settings.Seed, v => _settings.Seed = v, "whole number");
        var random = new Button { Text = "Random seed" };
        random.Pressed += () =>
        {
            // The menu picks a seed for the user; the simulation itself never uses this randomness.
            seed.Text = ((ulong)System.Random.Shared.NextInt64(1, 1_000_000_000)).ToString(CultureInfo.InvariantCulture);
            _settings.Seed = seed.Text;
            Changed();
        };
        g.AddChild(new Control());
        g.AddChild(random);
        SpinBox civs = null!;
        Choice(g, "Map size", Sizes, _settings.MapSize, v =>
        {
            _settings.MapSize = v;
            civs.Value = DefaultCivs[v];
        });
        civs = Number(g, "Civilizations", _settings.Civilizations > 0 ? _settings.Civilizations : DefaultCivs.GetValueOrDefault(_settings.MapSize, 12), 1, 64, v => _settings.Civilizations = (int)v);
        Choice(g, "World type", WorldTypes, _settings.WorldType, v => _settings.WorldType = v);
        PlanetText(g, "Water %", () => _settings.Water, v => _settings.Water = v, p => p.WaterPercent.ToString(CultureInfo.InvariantCulture));
        Choice(g, "Moisture", Moistures, _settings.Moisture, v => _settings.Moisture = v);
        Choice(g, "Inland humidity", Inlands, _settings.Inland, v => _settings.Inland = v);
        Choice(g, "Relief", Reliefs, _settings.Relief, v => _settings.Relief = v);
        Choice(g, "Deposits", ResourceLevels, _settings.Resources, v => _settings.Resources = v);
        return box;
    }

    private Control PlanetColumn()
    {
        GridContainer g = Column(out VBoxContainer box, "Planet and star");
        var keys = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Select(_setup.Planets, p => p.Key));
        Choice(g, "Preset", keys.Count > 0 ? [.. keys] : ["earth"], _settings.Planet, v =>
        {
            _settings.Planet = v;
            UpdatePlaceholders();
        });
        PlanetText(g, "Axial tilt °", () => _settings.Tilt, v => _settings.Tilt = v, p => Fixed(p.AxialTiltTenths, 1));
        PlanetText(g, "Eccentricity", () => _settings.Eccentricity, v => _settings.Eccentricity = v, p => Fixed(p.EccentricityPerMille, 3));
        PlanetText(g, "Star brightness (Suns)", () => _settings.Luminosity, v => _settings.Luminosity = v, p => Fixed(p.LuminosityThousandths, 3));
        PlanetText(g, "Distance (AU)", () => _settings.Distance, v => _settings.Distance = v, p => Fixed(p.DistanceThousandths, 3));
        PlanetText(g, "Year (Earth years)", () => _settings.Year, v => _settings.Year = v, p => Fixed(p.YearHundredths, 2));
        PlanetText(g, "Light (Earth = 1)", () => _settings.Light, v => _settings.Light = v, p => Fixed(p.LightThousandths, 3));
        PlanetText(g, "CO₂ (bar)", () => _settings.Co2, v => _settings.Co2 = v, _ => "carbon cycle");
        PlanetText(g, "Pressure (bar)", () => _settings.Pressure, v => _settings.Pressure = v, p => Fixed(p.PressureHundredths, 2));
        PlanetText(g, "Volcanism %", () => _settings.Volcanism, v => _settings.Volcanism = v, p => p.VolcanismPercent.ToString(CultureInfo.InvariantCulture));
        PlanetText(g, "Star swing ‰", () => _settings.StarVariability, v => _settings.StarVariability = v, _ => "0");
        PlanetText(g, "Star cycle (years)", () => _settings.StarCycle, v => _settings.StarCycle = v, _ => "none");
        box.AddChild(new Label
        {
            Text = "Give at most two of brightness, distance, year and light; the rest follows from them.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(360, 0),
            Modulate = new Color(0.7f, 0.7f, 0.7f),
        });
        return box;
    }

    private Control SimulationColumn()
    {
        GridContainer g = Column(out VBoxContainer box, "Simulation");
        Choice(g, "Start speed", SpeedWords, _settings.Speed, v => _settings.Speed = v);
        Number(g, "Turn limit (0 = none)", _settings.TurnLimit, 0, 1_000_000, v => _settings.TurnLimit = (long)v);
        var report = new CheckBox { Text = "Turn report (CSV)", ButtonPressed = _settings.Report };
        report.Toggled += on =>
        {
            _settings.Report = on;
            Changed();
        };
        g.AddChild(new Label { Text = "Reports" });
        g.AddChild(report);
        Text(g, "Report file", _settings.ReportPath, v => _settings.ReportPath = v, "user://turn_report.csv");
        Text(g, "Long run (4h, 30m)", _settings.LongRun, v => _settings.LongRun = v, "off");
        Text(g, "Long run folder", _settings.LongRunFolder, v => _settings.LongRunFolder = v, "long_runs/ by the project");
        box.AddChild(new Label
        {
            Text = "The simulation pauses when it reaches the turn limit; press a speed to go on. The report has one line per turn: turn, year, turn time, people, settlements. " +
                "A long run records memory, processor, frame rate, turn times, events and a log for the time given, then closes the game; it starts at maximum speed unless another start speed is chosen.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(330, 0),
            Modulate = new Color(0.7f, 0.7f, 0.7f),
        });
        ArtSection(box);
        return box;
    }

    // Art packs: choose one, see which of its files are used and which are wrong, open the folder, check again.
    private void ArtSection(VBoxContainer box)
    {
        box.AddChild(Heading("Art"));
        var row = new HBoxContainer();
        box.AddChild(row);
        row.AddChild(new Label { Text = "Art pack" });
        var choice = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var keys = new List<string> { string.Empty };
        choice.AddItem("default pictures");
        foreach (Map.ArtPack p in _packs)
        {
            choice.AddItem(p.Author.Length > 0 ? $"{p.Name} ({p.Author})" : p.Name);
            keys.Add(p.Key);
        }

        choice.Select(Math.Max(0, keys.IndexOf(_settings.ArtPack)));
        choice.ItemSelected += i =>
        {
            _settings.ArtPack = keys[(int)i];
            ArtPackChosen?.Invoke(_settings.ArtPack);
        };
        row.AddChild(choice);

        var buttons = new HBoxContainer();
        box.AddChild(buttons);
        var open = new Button { Text = "Open packs folder", TooltipText = Map.ArtPack.UserFolder };
        open.Pressed += () =>
        {
            System.IO.Directory.CreateDirectory(Map.ArtPack.UserFolder);
            OS.ShellOpen(Map.ArtPack.UserFolder);
        };
        buttons.AddChild(open);
        var again = new Button { Text = "Check again" };
        again.Pressed += () => ArtPackChosen?.Invoke(_settings.ArtPack);
        buttons.AddChild(again);

        var status = new RichTextLabel { BbcodeEnabled = true, CustomMinimumSize = new Vector2(330, 150), ScrollActive = true };
        box.AddChild(status);
        if (_art is null)
        {
            return;
        }

        var sb = new System.Text.StringBuilder();
        if (_art.Pack is { } pack)
        {
            sb.Append(CultureInfo.InvariantCulture, $"[color=#f2c279]{pack.Name}[/color]: {_art.PackFiles} of {_art.KnownFiles} pictures used.");
            if (pack.Description.Length > 0)
            {
                sb.Append(' ').Append(pack.Description);
            }

            sb.Append('\n');
        }
        else
        {
            sb.Append(CultureInfo.InvariantCulture, $"Put a folder with pictures into the packs folder (same names and sizes as art/placeholder, list in manifest.json) and pick it here.\n");
        }

        if (_art.ExportFiles > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"art/export: {_art.ExportFiles} pictures used.\n");
        }

        if (_art.Issues.Count == 0)
        {
            sb.Append("[color=#7ba154]No problems found.[/color]");
        }
        else
        {
            sb.Append(CultureInfo.InvariantCulture, $"[color=#ec271e]{_art.Issues.Count} files not used:[/color]\n");
            foreach (Map.ArtIssue issue in _art.Issues)
            {
                sb.Append(CultureInfo.InvariantCulture, $"• {issue.Source}/{issue.File}: {issue.Problem}\n");
            }
        }

        status.Text = sb.ToString();
    }

    private void Start()
    {
        if (Validate() is { } problem)
        {
            _error.Text = problem;
            return;
        }

        StartRequested?.Invoke(_settings);
    }

    // Null when the settings can start a game, otherwise what is wrong.
    private string? Validate()
    {
        try
        {
            LocalGameOptions options = _settings.ToOptions();
            return LocalServerHost.Preview(options, _dataDirectory).Error;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    private void RefreshPreview()
    {
        try
        {
            PlanetPreview p = LocalServerHost.Preview(_settings.ToOptions(), _dataDirectory);
            _preview.Text = string.Join("\n", p.Lines) + (p.Notes.Count > 0 ? "\nNote: " + string.Join(" ", p.Notes) : string.Empty);
            _error.Text = p.Error ?? string.Empty;
            _start.Disabled = p.Error is not null;
        }
        catch (FormatException ex)
        {
            _preview.Text = string.Empty;
            _error.Text = ex.Message;
            _start.Disabled = true;
        }
    }

    private void ResetToDefaults()
    {
        var defaults = new StartSettings { Seed = _settings.Seed };
        foreach (System.Reflection.PropertyInfo p in typeof(StartSettings).GetProperties())
        {
            if (p.CanWrite)
            {
                p.SetValue(_settings, p.GetValue(defaults));
            }
        }

        // Rebuild the form with the defaults.
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        _planetFields.Clear();
        Build();
    }

    private void UpdatePlaceholders()
    {
        PlanetPresetInfo? preset = System.Linq.Enumerable.FirstOrDefault(_setup.Planets, p => p.Key == _settings.Planet);
        foreach ((LineEdit edit, Func<PlanetPresetInfo, string> value) in _planetFields)
        {
            edit.PlaceholderText = preset is null ? "preset" : "preset: " + value(preset);
        }

        Changed();
    }

    private void Changed() => _previewIn = 0.3;

    private static GridContainer Column(out VBoxContainer box, string title)
    {
        box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddChild(Heading(title));
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 6);
        box.AddChild(grid);
        return grid;
    }

    private static Label Heading(string text) => new() { Text = text, ThemeTypeVariation = "Heading" };

    private LineEdit Text(GridContainer g, string label, string value, Action<string> set, string hint)
    {
        g.AddChild(new Label { Text = label });
        var edit = new LineEdit { Text = value, PlaceholderText = hint, CustomMinimumSize = new Vector2(190, 0) };
        edit.TextChanged += t =>
        {
            set(t);
            Changed();
        };
        g.AddChild(edit);
        return edit;
    }

    private void PlanetText(GridContainer g, string label, Func<string> get, Action<string> set, Func<PlanetPresetInfo, string> preset)
    {
        LineEdit edit = Text(g, label, get(), set, "preset");
        _planetFields.Add((edit, preset));
    }

    private void Choice(GridContainer g, string label, string[] values, string current, Action<string> set)
    {
        g.AddChild(new Label { Text = label });
        var option = new OptionButton { CustomMinimumSize = new Vector2(190, 0) };
        foreach (string v in values)
        {
            option.AddItem(v);
        }

        int index = Array.IndexOf(values, current);
        option.Select(index < 0 ? 0 : index);
        if (index < 0)
        {
            set(values[0]);
        }

        option.ItemSelected += i =>
        {
            set(values[i]);
            Changed();
        };
        g.AddChild(option);
    }

    private SpinBox Number(GridContainer g, string label, double value, double min, double max, Action<double> set)
    {
        g.AddChild(new Label { Text = label });
        var spin = new SpinBox { MinValue = min, MaxValue = max, Value = value, Rounded = true, CustomMinimumSize = new Vector2(190, 0) };
        spin.ValueChanged += v =>
        {
            set(v);
            Changed();
        };
        g.AddChild(spin);
        return spin;
    }

    // An integer with implied decimals, e.g. Fixed(234, 1) = "23.4".
    private static string Fixed(long value, int decimals)
    {
        decimal d = value;
        for (int i = 0; i < decimals; i++)
        {
            d /= 10;
        }

        return d.ToString(CultureInfo.InvariantCulture);
    }
}
