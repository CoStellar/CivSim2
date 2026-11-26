using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivSim.Protocol;
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
    private static readonly (int Id, string Label)[] Goals = [(1, "First to the Middle Ages"), (0, "Last civilization standing"), (2, "Best score after 2 hours")];

    private readonly StartSettings _settings;
    private readonly string _dataDirectory;
    private readonly Map.ArtLibrary? _art;
    private readonly IReadOnlyList<Map.ArtPack> _packs;
    private VBoxContainer _content = null!;
    private ManualView? _manual;
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

    /// <summary>The section shown first: "new", "load", "manual", "tests" or "settings".</summary>
    public string Section
    {
        get => _section;
        set => _section = value;
    }

    /// <summary>The settings as edited so far.</summary>
    public StartSettings Settings => _settings;

    /// <summary>Raised when another art pack is chosen, or the pictures should be checked again (same key).</summary>
    public event Action<string>? ArtPackChosen;

    /// <summary>Raised when the user presses Quit.</summary>
    public event Action? QuitRequested;

    /// <summary>Raised to join a game on another computer: address, port, password (MP3).</summary>
    public event Action<string, int, string, bool>? JoinRequested;

    /// <summary>Raised to host a multiplayer game: the game (from New game), the lobby's settings and the port (MP3).</summary>
    public event Action<PlayOptions, LobbyConfig, int>? HostRequested;

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
        // The manual gets a bigger page, so its text reads without a second scroll bar.
        var page = new HBoxContainer { CustomMinimumSize = _section == "manual" ? new Vector2(1500, 820) : new Vector2(1000, 720) };
        page.AddThemeConstantOverride("separation", 32);
        center.AddChild(page);

        // Left: the title and the sections.
        var nav = new VBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        nav.AddThemeConstantOverride("separation", 10);
        page.AddChild(nav);
        var title = new Label { Text = "CivSim 2", ThemeTypeVariation = "Heading" };
        title.AddThemeFontSizeOverride("font_size", 40);
        nav.AddChild(title);
        nav.AddChild(new Label { Text = "Lead your civilization through the ages.", Modulate = new Color(0.75f, 0.75f, 0.75f) });
        nav.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        Button? current = null;
        foreach ((string key, string label) in new[] { ("new", "New game"), ("multi", "Multiplayer"), ("load", "Load game"), ("manual", "How to play"), ("tests", "Test scenarios"), ("settings", "Settings") })
        {
            var b = new Button { Text = label, CustomMinimumSize = new Vector2(240, 44), ToggleMode = true, ButtonPressed = key == _section, TooltipText = key == "manual" ? "The manual: the goal, the eras, war and peace, every key (F1)" : "" };
            b.Pressed += () => Show(key);
            nav.AddChild(b);
            current = key == _section ? b : current;
        }

        nav.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var quit = new Button { Text = "Quit", CustomMinimumSize = new Vector2(240, 44) };
        quit.Pressed += () => QuitRequested?.Invoke();
        nav.AddChild(quit);
        nav.AddChild(new Label { Text = $"v{ProtocolInfo.GameVersion} - {ProtocolInfo.GameStage}", Modulate = new Color(0.6f, 0.6f, 0.6f) });

        // Right: what the chosen section asks for.
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(700, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        page.AddChild(panel);
        // 2026-11-24: the section scrolls, its main button (Play) stays in a footer under it, never cut off.
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddChild(scroll);
        _footer = new HBoxContainer();
        body.AddChild(_footer);
        _content = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(_content);
        Fill();

        // The whole menu works from the keyboard: something always has the focus (arrows and Tab move it, Enter presses).
        Callable.From(() =>
        {
            if (GetViewport()?.GuiGetFocusOwner() is null)
            {
                current?.GrabFocus();
            }
        }).CallDeferred();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key)
        {
            return;
        }

        if (key.Keycode == Key.F1 && !key.Echo)
        {
            Show(_section == "manual" ? "new" : "manual");
            GetViewport().SetInputAsHandled();
        }
        else if (_manual is not null && key.Keycode is Key.Pageup or Key.Pagedown or Key.Home or Key.End && _manual.HandleKey(key.Keycode))
        {
            GetViewport().SetInputAsHandled();
        }
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

    // How to play: the manual (data/sim/manual.md), the same as F1 in the game.
    private void ManualSection()
    {
        _manual = new ManualView(Manual.Load(_dataDirectory), focusable: true) { CustomMinimumSize = new Vector2(0, 790) };
        _content.AddChild(_manual);
        Callable.From(() => _manual?.FocusChapter()).CallDeferred();
    }

    private HBoxContainer _footer = null!;

    /// <summary>A message shown once at the top of the menu (why a game could not start).</summary>
    public static string? Notice { get; set; }

    private void Fill()
    {
        _manual = null;
        if (Notice is { Length: > 0 } notice)
        {
            Notice = null;
            _content.AddChild(new Label { Text = notice, Modulate = new Color(1f, 0.5f, 0.45f), AutowrapMode = TextServer.AutowrapMode.WordSmart });
        }

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
            case "manual":
                ManualSection();
                break;
            case "multi":
                MultiplayerSection();
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
        AdvancedWorldSection();

        _content.AddChild(Heading("Game"));
        GridContainer game = Grid();
        game.AddChild(new Label { Text = "Civilizations", CustomMinimumSize = new Vector2(120, 0) });
        var civs = new SpinBox { MinValue = 2, MaxValue = 8, Value = Math.Clamp(_settings.CivCount, 2, 8), Rounded = true, CustomMinimumSize = new Vector2(260, 0), TooltipText = "Your civilization and the AI civilizations" };
        civs.ValueChanged += v => _settings.CivCount = (int)v;
        game.AddChild(civs);
        Choice(game, "Goal", [.. Goals.Select(g => g.Label)], Math.Max(0, Array.FindIndex(Goals, g => g.Id == _settings.Goal)), i => _settings.Goal = Goals[i].Id);
        Choice(game, "Ores", ["Fair: every civilization has metals near", "Realistic: where the world put them"], _settings.FairOres ? 0 : 1, i => _settings.FairOres = i == 0);
        var choose = new CheckBox { Text = "Choose where we start", ButtonPressed = _settings.ChooseStart, TooltipText = "Civilizations pick their start in turns on the map; otherwise the world gives each one a place" };
        choose.Toggled += on => _settings.ChooseStart = on;
        game.AddChild(new Control());
        game.AddChild(choose);
        var tutorial = new CheckBox { Text = "Tutorial: first steps on screen", ButtonPressed = _settings.Tutorial, TooltipText = "A task list of the first steps; it can be turned on and off in the game (Tutorial, key T)" };
        tutorial.Toggled += on => _settings.Tutorial = on;
        game.AddChild(new Control());
        game.AddChild(tutorial);

        var play = new Button { Text = "Play", CustomMinimumSize = new Vector2(220, 48), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        play.Pressed += () =>
        {
            // Advanced world settings are checked before the game starts: a bad number is shown here, not a game that stops.
            PlayOptions options;
            try
            {
                options = NewGameOptions();
            }
            catch (FormatException e)
            {
                ShowWorldNote(e.Message, problem: true);
                return;
            }

            if (options.World is { } w && PlayWorlds.Check(w, _dataDirectory, out _) is { } why)
            {
                ShowWorldNote(why, problem: true);
                return;
            }

            PlayRequested?.Invoke(options);
        };
        _footer.AddChild(play);
        play.GrabFocus();
    }

    // Multiplayer (MP3): the nickname, joining a game by address and port, hosting one with the world and game of New game.
    private void MultiplayerSection()
    {
        _content.AddChild(Heading("You"));
        GridContainer you = Grid();
        Text(you, "Nickname", _settings.Nickname, v => _settings.Nickname = v, "the name other players see (kept for next time)");

        _content.AddChild(Heading("Join a game"));
        GridContainer join = Grid();
        Text(join, "Address", _settings.JoinAddress, v => _settings.JoinAddress = v.Trim(), "the host's address, e.g. 192.168.1.20");
        LineEdit joinPort = Text(join, "Port", _settings.JoinPort.ToString(CultureInfo.InvariantCulture), v => _settings.JoinPort = PortOf(v, _settings.JoinPort), "7777 unless the host says otherwise");
        _ = joinPort;
        LineEdit joinPassword = Secret(join, "Password", "if the game has one");
        var watchOnly = new CheckBox
        {
            Text = "Only watch",
            TooltipText = "In a game that already runs: see the whole map, chat, but never lead a civilization in it.\nWithout it you wait for the players to let you lead a civilization the AI leads now.",
        };
        _content.AddChild(watchOnly);
        var joinButton = new Button { Text = "Join", CustomMinimumSize = new Vector2(200, 44), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        joinButton.Pressed += () => JoinRequested?.Invoke(_settings.JoinAddress, _settings.JoinPort, joinPassword.Text, watchOnly.ButtonPressed);
        _content.AddChild(joinButton);

        _content.AddChild(Heading("Host a game"));
        _content.AddChild(Muted("The world and the game are those of New game, or a saved game: its players get their places back. Players join with your address and the port below; "
            + "over the internet the port must be open on your router (a relay server comes later)."));
        string mine = MyAddresses();
        if (mine.Length > 0)
        {
            _content.AddChild(Muted("Your addresses: " + mine));
        }

        GridContainer host = Grid();

        // A new world from New game, or a saved game whose players come back to their places in the lobby (MP5).
        IReadOnlyList<(string Path, string Name, DateTime SavedAt, long Minute, string Era)> hostSaves = PlaySaves.List();
        string? hostLoad = null;
        Choice(host, "Game", ["New game (its settings)", .. hostSaves.Select(v => $"{v.Name}: minute {v.Minute}, {v.Era}")], 0,
            i => hostLoad = i == 0 ? null : hostSaves[i - 1].Path);
        Text(host, "Game name", _settings.HostGameName, v => _settings.HostGameName = v, "what players see in the lobby");
        LineEdit hostPassword = Secret(host, "Password", "empty: anyone may join");
        Text(host, "Port", _settings.HostPort.ToString(CultureInfo.InvariantCulture), v => _settings.HostPort = PortOf(v, _settings.HostPort), "1024 to 65535");
        host.AddChild(new Label { Text = "Places for players", CustomMinimumSize = new Vector2(120, 0) });
        var seats = new SpinBox { MinValue = 2, MaxValue = 8, Value = Math.Clamp(_settings.HostSeats, 2, 8), Rounded = true, CustomMinimumSize = new Vector2(260, 0), TooltipText = "The AI leads the other civilizations; at most as many as the civilizations of New game" };
        seats.ValueChanged += v => _settings.HostSeats = (int)v;
        host.AddChild(seats);
        var warning = Muted("x2 is harder: less time for every decision.");
        Choice(host, "Speed", ["x1", "x2"], _settings.HostSpeed == 2 ? 1 : 0, i =>
        {
            _settings.HostSpeed = i + 1;
            warning.Visible = i == 1;
        });
        warning.Visible = _settings.HostSpeed == 2;
        _content.AddChild(warning);
        var hostButton = new Button { Text = "Host", CustomMinimumSize = new Vector2(200, 44), SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        hostButton.Pressed += () =>
        {
            PlayOptions options;
            if (hostLoad is not null)
            {
                options = new PlayOptions(0)
                {
                    LoadPath = hostLoad,
                    HumanSeats = Math.Clamp(_settings.HostSeats, 1, 8),
                    StartSpeed = _settings.HostSpeed == 2 ? 2 : 1,
                };
                HostRequested?.Invoke(options, new LobbyConfig { GameName = _settings.HostGameName.Trim() is { Length: > 0 } g ? g : "CivSim game", Password = hostPassword.Text }, _settings.HostPort);
                return;
            }

            try
            {
                options = NewGameOptions();
            }
            catch (FormatException e)
            {
                Notice = "New game: " + e.Message;
                Show("multi");
                return;
            }

            options = options with
            {
                Tutorial = false,
                HumanSeats = Math.Clamp(_settings.HostSeats, 1, options.Civilizations),
                StartSpeed = _settings.HostSpeed == 2 ? 2 : 1,
            };
            HostRequested?.Invoke(options, new LobbyConfig { GameName = _settings.HostGameName.Trim() is { Length: > 0 } n ? n : "CivSim game", Password = hostPassword.Text }, _settings.HostPort);
        };
        _content.AddChild(hostButton);
        joinButton.GrabFocus();
    }

    private static int PortOf(string text, int fallback) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int p) && p is >= 1024 and <= 65535 ? p : fallback;

    private static LineEdit Secret(GridContainer g, string label, string hint)
    {
        g.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(120, 0) });
        var edit = new LineEdit { Secret = true, PlaceholderText = hint, TooltipText = hint, CustomMinimumSize = new Vector2(260, 0) };
        g.AddChild(edit);
        return edit;
    }

    // The computer's own addresses in the local network (what friends type to join).
    private static string MyAddresses()
    {
        try
        {
            var found = new List<string>();
            foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation a in ni.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        found.Add(a.Address.ToString());
                    }
                }
            }

            return string.Join(", ", found);
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            return "";
        }
    }

    private PlayOptions NewGameOptions() => NewGameOptions(_settings);

    /// <summary>The game New game starts with these settings (also the game a multiplayer host offers, MP3).</summary>
    public static PlayOptions NewGameOptions(StartSettings _settings)
    {
        ulong seed = ulong.TryParse(_settings.Seed, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong s) ? s : 1;
        (string _, string _, int width, int height) = Sizes.FirstOrDefault(x => x.Key == _settings.MapSize, Sizes[1]);
        return new PlayOptions(seed, width, height, Math.Clamp(_settings.CivCount, 2, 8))
        {
            StartPhase = _settings.ChooseStart,
            Tutorial = _settings.Tutorial,
            Goal = _settings.Goal,
            FairOres = _settings.FairOres,
            WorldType = Math.Max(0, Array.FindIndex(WorldTypes, w => w.Key == _settings.WorldType)),
            WaterPercent = Waters.FirstOrDefault(w => w.Key == _settings.Water, Waters[1]).Percent,
            World = _settings.ToPlayWorld(),
        };
    }

    private static readonly (string Key, string Label)[] Moistures = [("preset", "As the planet"), ("dry", "Dry"), ("normal", "Normal"), ("wet", "Wet")];
    private static readonly (string Key, string Label)[] Inlands = [("normal", "Normal"), ("dry", "Dry: deserts inside continents"), ("humid", "Humid: green far inland")];
    private static readonly (string Key, string Label)[] Reliefs = [("preset", "As the planet"), ("flat", "Flat"), ("normal", "Normal"), ("mountainous", "Mountainous")];
    private static readonly (string Key, string Label)[] Deposits = [("scarce", "Scarce"), ("normal", "Normal"), ("rich", "Rich")];

    private Label? _worldNote;

    // Advanced world settings (Patryk, 2026-11-25, PR W1): hidden behind a switch; the second level holds the numbers of the planet
    // and its star. Off, the world is a normal one whatever the fields hold.
    private void AdvancedWorldSection()
    {
        var toggle = new CheckButton
        {
            Text = "Advanced world settings",
            ButtonPressed = _settings.AdvancedWorld,
            TooltipText = "The planet, its rain, relief and deposits, and the numbers of the planet and its star",
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        _content.AddChild(toggle);
        var box = new VBoxContainer { Visible = _settings.AdvancedWorld };
        box.AddThemeConstantOverride("separation", 8);
        _content.AddChild(box);
        toggle.Toggled += on =>
        {
            _settings.AdvancedWorld = on;
            box.Visible = on;
        };

        IReadOnlyList<(string Key, string Name)> planets;
        try
        {
            planets = PlayWorlds.Presets(_dataDirectory);
        }
        catch (Exception e) when (e is System.IO.IOException or InvalidOperationException or ArgumentException)
        {
            box.AddChild(Muted("The planet presets could not be read: " + e.Message));
            return;
        }

        GridContainer g = Grid(box);
        Choice(g, "Planet", [.. planets.Select(p => p.Name)], Math.Max(0, planets.ToList().FindIndex(p => p.Key == _settings.Planet)), i => _settings.Planet = planets[i].Key);
        Choice(g, "Rain", [.. Moistures.Select(m => m.Label)], Math.Max(0, Array.FindIndex(Moistures, m => m.Key == _settings.Moisture)), i => _settings.Moisture = Moistures[i].Key);
        Choice(g, "Inland humidity", [.. Inlands.Select(m => m.Label)], Math.Max(0, Array.FindIndex(Inlands, m => m.Key == _settings.Inland)), i => _settings.Inland = Inlands[i].Key);
        Choice(g, "Relief", [.. Reliefs.Select(m => m.Label)], Math.Max(0, Array.FindIndex(Reliefs, m => m.Key == _settings.Relief)), i => _settings.Relief = Reliefs[i].Key);
        Choice(g, "Deposits", [.. Deposits.Select(m => m.Label)], Math.Max(0, Array.FindIndex(Deposits, m => m.Key == _settings.Resources)), i => _settings.Resources = Deposits[i].Key);

        var expert = new Button { Text = "Planet and star numbers (show)", ToggleMode = true, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        box.AddChild(expert);
        var numbers = new VBoxContainer { Visible = false };
        box.AddChild(numbers);
        expert.Toggled += on =>
        {
            numbers.Visible = on;
            expert.Text = on ? "Planet and star numbers (hide)" : "Planet and star numbers (show)";
        };
        numbers.AddChild(Muted("Empty = as the planet. Use a dot for decimals. Two of brightness, distance, year and light are enough: the rest follows."));
        GridContainer n = Grid(numbers);
        Text(n, "Axial tilt (degrees)", _settings.Tilt, v => _settings.Tilt = v, "as the planet (Earth 23.4)");
        Text(n, "Eccentricity", _settings.Eccentricity, v => _settings.Eccentricity = v, "as the planet (Earth 0.017)");
        Text(n, "Star brightness (Suns)", _settings.Luminosity, v => _settings.Luminosity = v, "as the planet (Sun 1)");
        Text(n, "Distance (AU)", _settings.Distance, v => _settings.Distance = v, "as the planet (Earth 1)");
        Text(n, "Year (Earth years)", _settings.Year, v => _settings.Year = v, "as the planet (Earth 1)");
        Text(n, "Light (Earth = 1)", _settings.Light, v => _settings.Light = v, "0.356 to 1.107");
        Text(n, "CO2 (bar)", _settings.Co2, v => _settings.Co2 = v, "carbon cycle (Earth 0.00028)");
        Text(n, "Pressure (bar)", _settings.Pressure, v => _settings.Pressure = v, "as the planet (Earth 1)");
        Text(n, "Volcanism (% of Earth)", _settings.Volcanism, v => _settings.Volcanism = v, "as the planet (Earth 100)");
        Text(n, "Star swing (per mille)", _settings.StarVariability, v => _settings.StarVariability = v, "0 to 100");
        Text(n, "Star cycle (years)", _settings.StarCycle, v => _settings.StarCycle = v, "none");

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        box.AddChild(row);
        var check = new Button { Text = "Check the planet" };
        check.Pressed += () =>
        {
            try
            {
                if (_settings.ToPlayWorld() is not { } w)
                {
                    return;
                }

                string? why = PlayWorlds.Check(w, _dataDirectory, out IReadOnlyList<string> adjusted);
                ShowWorldNote(why ?? (adjusted.Count == 0 ? "The planet is fine." : "The planet is fine. " + string.Join(" ", adjusted)), problem: why is not null);
            }
            catch (FormatException e)
            {
                ShowWorldNote(e.Message, problem: true);
            }
        };
        row.AddChild(check);
        var reset = new Button { Text = "Back to Earth" };
        reset.Pressed += () =>
        {
            _settings.ResetWorld();
            Show(_section);
        };
        row.AddChild(reset);
        _worldNote = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(560, 0), Visible = false };
        box.AddChild(_worldNote);
    }

    private void ShowWorldNote(string text, bool problem)
    {
        if (_worldNote is null)
        {
            Notice = text;
            Show(_section);
            return;
        }

        _worldNote.Text = text;
        _worldNote.Modulate = problem ? new Color(1f, 0.5f, 0.45f) : new Color(0.6f, 0.9f, 0.6f);
        _worldNote.Visible = true;
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
            var problem = new Label { Modulate = new Color(1f, 0.5f, 0.45f), Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            load.Pressed += () =>
            {
                // A damaged or edited save is refused here with its reason, instead of a game that stops while loading.
                try
                {
                    PlaySaves.Read(file);
                }
                catch (Exception e) when (e is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException)
                {
                    problem.Text = "This save cannot be loaded: " + e.Message;
                    problem.Visible = true;
                    return;
                }

                PlayRequested?.Invoke(new PlayOptions(0) { LoadPath = file });
            };
            row.AddChild(load);
            row.AddChild(new Label
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"{name}  -  minute {minute}, {era}  -  {at.ToLocalTime():yyyy-MM-dd HH:mm}"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
            _content.AddChild(problem);
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
        // 2026-11-24 (Patryk): the size of the whole interface, also in the game's "?" panel.
        _content.AddChild(Heading("Interface"));
        var sizeRow = new HBoxContainer();
        _content.AddChild(sizeRow);
        sizeRow.AddChild(new Label { Text = "Interface size", CustomMinimumSize = new Vector2(120, 0) });
        var size = new OptionButton { CustomMinimumSize = new Vector2(260, 0), TooltipText = "Every panel, button and text this big" };
        int now = ViewerSettings.Load().UiScale;
        foreach (int percent in ViewerSettings.UiScales)
        {
            size.AddItem($"{percent}%");
        }

        size.Select(Math.Max(0, Array.IndexOf(ViewerSettings.UiScales, now)));
        size.ItemSelected += i =>
        {
            ViewerSettings saved = ViewerSettings.Load();
            saved.UiScale = ViewerSettings.UiScales[(int)i];
            saved.Save();
            ViewerSettings.ApplyUiScale(GetWindow(), saved.UiScale);
        };
        sizeRow.AddChild(size);

        _content.AddChild(Heading("Sound"));
        _content.AddChild(Audio.AudioPanel.Build());

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

    private GridContainer Grid(Container? parent = null)
    {
        var grid = new GridContainer { Columns = 2 };
        grid.AddThemeConstantOverride("h_separation", 16);
        grid.AddThemeConstantOverride("v_separation", 8);
        (parent ?? _content).AddChild(grid);
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
