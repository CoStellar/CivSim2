using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivSim.Protocol.Hosting;
using CivSim.Server;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// Start menu of the game (sprint to the demo, 2026-11-20): New game (world and game settings), Load game, Test scenarios,
/// Settings (art), Quit. One section shows at a time, so the player sees only what he is choosing now. Built in code.
/// </summary>
public partial class StartMenu : CanvasLayer
{
    // Map sizes of the new game: (key, label, width, height). Widths are 4k + 2 (the globe). The keys are those the settings
    // file already holds ("small" was 98 × 60 before too, the default).
    private static readonly (string Key, string Label, int Width, int Height)[] Sizes =
    [
        ("test", "Small (quick games)", 66, 40),
        ("small", "Normal", 98, 60),
        ("standard", "Large (slower)", 130, 80),
    ];

    private static readonly (string Key, string Label)[] WorldTypes = [("continents", "Continents"), ("pangea", "One big continent"), ("archipelago", "Islands")];
    private static readonly (string Key, string Label, int Percent)[] Waters = [("less", "Less sea", 45), ("", "Normal", 60), ("more", "More sea", 72)];
    private static readonly (int Id, string Label)[] Goals = [(1, "First to the Middle Ages"), (0, "Last people standing"), (2, "Best score after 2 hours")];

    private readonly StartSettings _settings;
    private readonly string _dataDirectory;
    private readonly Map.ArtLibrary? _art;
    private readonly IReadOnlyList<Map.ArtPack> _packs;
    private VBoxContainer _content = null!;
    private string _section = "new";

    /// <summary>Creates the menu filled with <paramref name="settings"/> (edited in place).</summary>
    public StartMenu(LocalSetupInfo setup, StartSettings settings, string dataDirectory, Map.ArtLibrary? art = null, IReadOnlyList<Map.ArtPack>? packs = null)
    {
        _ = setup; // the planet presets belong to the legacy viewer
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

    /// <summary>Raised when the user presses Quit.</summary>
    public event Action? QuitRequested;

    /// <summary>Raised to start a game: a new one, a test scenario or a save to load.</summary>
    public event Action<PlayOptions>? PlayRequested;

    /// <inheritdoc />
    public override void _Ready() => Build();

    private void Build()
    {
        var root = new PanelContainer { Theme = PixelTheme.Current };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        var center = new CenterContainer();
        root.AddChild(center);
        var page = new HBoxContainer { CustomMinimumSize = new Vector2(1000, 600) };
        page.AddThemeConstantOverride("separation", 32);
        center.AddChild(page);

        // Left: the title and the sections.
        var nav = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        nav.AddThemeConstantOverride("separation", 10);
        page.AddChild(nav);
        var title = new Label { Text = "CivSim 2", ThemeTypeVariation = "Heading" };
        title.AddThemeFontSizeOverride("font_size", 40);
        nav.AddChild(title);
        nav.AddChild(new Label { Text = "Lead your people through the ages.", Modulate = new Color(0.75f, 0.75f, 0.75f) });
        nav.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        foreach ((string key, string label) in new[] { ("new", "New game"), ("load", "Load game"), ("tests", "Test scenarios"), ("settings", "Settings") })
        {
            var b = new Button { Text = label, CustomMinimumSize = new Vector2(240, 44), ToggleMode = true, ButtonPressed = key == _section };
            b.Pressed += () => Show(key);
            nav.AddChild(b);
        }

        nav.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(240, 44) };
        quit.Pressed += () => QuitRequested?.Invoke();
        nav.AddChild(quit);

        // Right: what the chosen section asks for.
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(700, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        page.AddChild(panel);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(scroll);
        _content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_content);
        Fill();
    }

    private void Show(string section)
    {
        _section = section;
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }

        Build();
    }

    private void Fill()
    {
        switch (_section)
        {
            case "load":
                LoadSection();
                break;
            case "tests":
                TestsSection();
                break;
            case "settings":
                SettingsSection();
                break;
            default:
                NewGameSection();
                break;
        }
    }

    // New game: the world, then the game, then Play.
    private void NewGameSection()
    {
        _content.AddChild(Heading("World"));
        GridContainer world = Grid();
        LineEdit seed = Text(world, "Seed", _settings.Seed, v => _settings.Seed = v, "a whole number: the same seed gives the same world");
        var random = new Button { Text = "Random seed" };
        random.Pressed += () =>
        {
            // The menu picks a seed for the user; the simulation itself never uses this randomness.
            seed.Text = ((ulong)System.Random.Shared.NextInt64(1, 1_000_000_000)).ToString(CultureInfo.InvariantCulture);
            _settings.Seed = seed.Text;
        };
        world.AddChild(new Control());
        world.AddChild(random);
        Choice(world, "Map size", [.. Sizes.Select(s => s.Label)], Math.Max(0, Array.FindIndex(Sizes, s => s.Key == _settings.MapSize)), i => _settings.MapSize = Sizes[i].Key);
        Choice(world, "Land", [.. WorldTypes.Select(w => w.Label)], Math.Max(0, Array.FindIndex(WorldTypes, w => w.Key == _settings.WorldType)), i => _settings.WorldType = WorldTypes[i].Key);
        Choice(world, "Sea", [.. Waters.Select(w => w.Label)], Math.Max(0, Array.FindIndex(Waters, w => w.Key == _settings.Water)), i => _settings.Water = Waters[i].Key);

        _content.AddChild(Heading("Game"));
        GridContainer game = Grid();
        game.AddChild(new Label { Text = "Peoples", CustomMinimumSize = new Vector2(120, 0) });
        var peoples = new SpinBox { MinValue = 2, MaxValue = 8, Value = Math.Clamp(_settings.Peoples, 2, 8), Rounded = true, CustomMinimumSize = new Vector2(260, 0), TooltipText = "Your people and the AI peoples" };
        peoples.ValueChanged += v => _settings.Peoples = (int)v;
        game.AddChild(peoples);
        Choice(game, "Goal", [.. Goals.Select(g => g.Label)], Math.Max(0, Array.FindIndex(Goals, g => g.Id == _settings.Goal)), i => _settings.Goal = Goals[i].Id);
        var choose = new CheckBox { Text = "Choose where we start", ButtonPressed = _settings.ChooseStart, TooltipText = "Peoples pick their start in turns on the map; otherwise the world gives each one a place" };
        choose.Toggled += on => _settings.ChooseStart = on;
        game.AddChild(new Control());
        game.AddChild(choose);

        _content.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        var play = new Button { Text = "Play", CustomMinimumSize = new Vector2(220, 48), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        play.Pressed += () => PlayRequested?.Invoke(NewGameOptions());
        _content.AddChild(play);
        play.GrabFocus();
    }

    private PlayOptions NewGameOptions()
    {
        ulong seed = ulong.TryParse(_settings.Seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong s) ? s : 1;
        (string _, string _, int width, int height) = Sizes.FirstOrDefault(x => x.Key == _settings.MapSize, Sizes[1]);
        return new PlayOptions(seed, width, height, Math.Clamp(_settings.Peoples, 2, 8))
        {
            StartPhase = _settings.ChooseStart,
            Goal = _settings.Goal,
            WorldType = Math.Max(0, Array.FindIndex(WorldTypes, w => w.Key == _settings.WorldType)),
            WaterPercent = Waters.FirstOrDefault(w => w.Key == _settings.Water, Waters[1]).Percent,
        };
    }

    // Load game: the saves, newest first (the game saves itself every few minutes as "autosave").
    private void LoadSection()
    {
        _content.AddChild(Heading("Load game"));
        IReadOnlyList<(string Path, string Name, DateTime SavedAt, long Minute, string Era)> saves = PlaySaves.List();
        if (saves.Count == 0)
        {
            _content.AddChild(Muted("No saved games yet. In a game press Save (or F5); the game also saves itself every 5 minutes as \"autosave\"."));
        }

        foreach ((string path, string name, DateTime at, long minute, string era) in saves)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            _content.AddChild(row);
            var load = new Button { Text = "Load", CustomMinimumSize = new Vector2(100, 36) };
            string file = path;
            load.Pressed += () => PlayRequested?.Invoke(new PlayOptions(0) { LoadPath = file });
            row.AddChild(load);
            row.AddChild(new Label
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"{name}  -  minute {minute}, {era}  -  {at.ToLocalTime():yyyy-MM-dd HH:mm}"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
        }

        var open = new Button { Text = "Open saves folder", TooltipText = PlaySaves.Folder, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        open.Pressed += () =>
        {
            System.IO.Directory.CreateDirectory(PlaySaves.Folder);
            OS.ShellOpen(PlaySaves.Folder);
        };
        _content.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        _content.AddChild(open);
    }

    // Test scenarios: games that start at a chosen moment of development (data/sim/scenarios).
    private void TestsSection()
    {
        _content.AddChild(Heading("Test scenarios"));
        _content.AddChild(Muted("A game that starts at a chosen moment of development, to try later eras and war without playing up to them. Debug keys work here."));
        foreach ((string key, string name, string about) in Scenarios(_dataDirectory))
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            _content.AddChild(row);
            var play = new Button { Text = "Play", CustomMinimumSize = new Vector2(100, 36) };
            string k = key;
            play.Pressed += () => PlayRequested?.Invoke(new PlayOptions(1) { StartPhase = false, Scenario = k });
            row.AddChild(play);
            var text = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            text.AddChild(new Label { Text = name });
            text.AddChild(Muted(about));
            row.AddChild(text);
        }
    }

    // Key, name and description of every test scenario in the data.
    private static List<(string Key, string Name, string About)> Scenarios(string dataDirectory)
    {
        var list = new List<(string, string, string)>();
        string folder = System.IO.Path.Combine(dataDirectory, "sim", "scenarios");
        if (!System.IO.Directory.Exists(folder))
        {
            return list;
        }

        foreach (string path in System.IO.Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
                string key = System.IO.Path.GetFileNameWithoutExtension(path);
                string name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? key : key;
                string about = doc.RootElement.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                list.Add((key, name, about));
            }
            catch (System.Text.Json.JsonException)
            {
                // A broken file is left out of the menu; the scenario tests report it.
            }
        }

        return list;
    }

    // Settings: the art pack (the font is chosen in the game's help window).
    private void SettingsSection()
    {
        _content.AddChild(Heading("Art"));
        var row = new HBoxContainer();
        _content.AddChild(row);
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
        _content.AddChild(buttons);
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

        if (_art is null)
        {
            return;
        }

        var sb = new System.Text.StringBuilder();
        if (_art.Pack is { } pack)
        {
            sb.Append(CultureInfo.InvariantCulture, $"[color=#f2c279]{pack.Name}[/color]: {_art.PackFiles} of {_art.KnownFiles} pictures used.\n");
        }
        else
        {
            sb.Append("Put a folder with pictures into the packs folder (same names and sizes as art/placeholder, list in manifest.json) and pick it here.\n");
        }

        sb.Append(_art.Issues.Count == 0 ? "[color=#7ba154]No problems found.[/color]" : $"[color=#ec271e]{_art.Issues.Count} files not used:[/color]\n");
        foreach (Map.ArtIssue issue in _art.Issues)
        {
            sb.Append(CultureInfo.InvariantCulture, $"• {issue.Source}/{issue.File}: {issue.Problem}\n");
        }

        _content.AddChild(new RichTextLabel { BbcodeEnabled = true, Text = sb.ToString(), CustomMinimumSize = new Vector2(600, 160), ScrollActive = true });
        _content.AddChild(Muted("The font is chosen in the game: press ? (or H) during play."));
    }

    private static Label Heading(string text) => new() { Text = text, ThemeTypeVariation = "Heading" };

    private static Label Muted(string text) => new()
    {
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        CustomMinimumSize = new Vector2(560, 0),
        Modulate = new Color(0.72f, 0.72f, 0.72f),
    };

    private GridContainer Grid()
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        _content.AddChild(grid);
        return grid;
    }

    private static LineEdit Text(GridContainer g, string label, string value, Action<string> set, string hint)
    {
        g.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
        var edit = new LineEdit { Text = value, PlaceholderText = hint, TooltipText = hint, CustomMinimumSize = new Vector2(260, 0) };
        edit.TextChanged += t => set(t);
        g.AddChild(edit);
        return edit;
    }

    private static void Choice(GridContainer g, string label, string[] values, int current, Action<int> set)
    {
        g.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
        var option = new OptionButton { CustomMinimumSize = new Vector2(260, 0) };
        foreach (string v in values)
        {
            option.AddItem(v);
        }

        option.Select(current);
        set(current);
        option.ItemSelected += i => set((int)i);
        g.AddChild(option);
    }
}
