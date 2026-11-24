using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Game.Ui;
using CivSim.Protocol;
using CivSim.Protocol.Hosting;
using CivSim.Protocol.Messages;
using CivSim.Server;
using Godot;

namespace CivSim.Game;

/// <summary>
/// Developer viewer (stages 2 and 5). Shows the start menu, then starts a local server on its own thread with the
/// chosen settings, connects to it as an observer and draws what the server sends. It never touches the game state
/// directly (CLAUDE.md rule 10). Any start setting on the command line (after <c>--</c>, e.g. <c>--seed 3
/// --map-size test</c>) skips the menu, like before stage 5; <c>--no-menu</c> starts with the saved settings, and
/// <c>--benchmark FRAMES</c>, <c>--screenshot file.png</c> (with <c>--screenshot-turn N</c>), <c>--menu-screenshot file.png</c>,
/// <c>--zoom</c>, <c>--overlay LAYER</c> (only that cell layer), <c>--layer LAYER</c> (switch one on) and <c>--watch CIV</c>
/// (that civilization's fog), <c>--opacity LAYER=PERCENT</c> and <c>--layer-settings</c> (open that window) are for tests.
/// </summary>
public partial class Main : Node2D
{
    private const float MinZoom = 0.04f;
    private const float MaxZoom = 4f;
    private const float PanSpeed = 900f;

    private readonly ViewerSettings _saved = ViewerSettings.Load();
    private readonly LayerStack _layers = new();
    private LocalServerHost? _host;
    private ClientSession? _session;
    private Play.PlayGame? _play;
    private bool _playArgs;
    private string? _playScenario;
    private string? _playScreenshot;
    private double _playSeconds = 6;
    private bool _playDemo;
    private bool _playLong;
    private string _playWindow = "Summary";
    private MapView? _map;
    private Camera2D _camera = null!;
    private Hud? _hud;
    private HelmHud? _helm;
    private StartMenu? _menu;
    private ArtLibrary _art = null!;
    private string _dataDirectory = string.Empty;
    private bool _dragging;
    private bool _bound;
    private bool _playerCamera;
    private int _benchmarkFrames;
    private int _frames;
    private double _frameMsSum;
    private double _frameMsMax;
    private int _slowFrames;
    private long _benchmarkStart;
    private int _benchmarkGc;
    private double _statusTimer;
    private string? _screenshot;
    private int _screenshotFrames = -1;
    private float _startZoom = 0.5f;
    private MapLayer? _startOverlay;
    private string? _menuScreenshot;
    private string? _menuSection;
    private StartSettings? _menuSettings;
    private StartSettings? _lastSettings;
    private AcceptDialog? _dataDialog;
    private Action? _retry;
    private long _screenshotTurn;
    private readonly List<MapLayer> _extraLayers = [];
    private int _watch = WelcomeMessage.NoCiv;
    private readonly List<(MapLayer Layer, int Opacity)> _opacities = [];
    private bool _openLayerSettings;
    private bool _renameDialog;
    private double _engineTimer;
    private long _lastFrame;

    /// <inheritdoc />
    public override void _Ready()
    {
        GetNodeOrNull<Label>("StatusLabel")?.QueueFree();
        ViewerSettings.ApplyUiScale(GetWindow(), _saved.UiScale);
        RenderingServer.SetDefaultClearColor(new Color(0.04f, 0.04f, 0.05f));
        _dataDirectory = FindDataDirectory();
        string[] early = GameArguments();
        int packArg = Array.IndexOf(early, "--art-pack");
        if (packArg >= 0 && packArg + 1 < early.Length)
        {
            // A pack on the command line is shown as chosen in the menu too (saved only when the user presses Start).
            _menuSettings = _saved.Start.Copy();
            _menuSettings.ArtPack = early[packArg + 1];
        }

        LoadArt(_menuSettings?.ArtPack ?? _saved.Start.ArtPack);
        _layers.Load(_saved.Layers);
        _layers.Changed += () => _saved.Layers = _layers.Save();
        _camera = new Camera2D { Zoom = new Vector2(0.5f, 0.5f) };
        AddChild(_camera);
        _camera.MakeCurrent();

        string[] args = GameArguments();
        bool skipMenu = false;
        for (int i = 0; i < args.Length; i++)
        {
            string next = i + 1 < args.Length ? args[i + 1] : string.Empty;
            switch (args[i])
            {
                case "--screenshot":
                    _screenshot = next;
                    break;
                case "--layer":
                    _extraLayers.Add(Enum.Parse<MapLayer>(next, ignoreCase: true));
                    break;
                case "--opacity":
                    string[] kv = next.Split('=');
                    _opacities.Add((Enum.Parse<MapLayer>(kv[0], ignoreCase: true), int.Parse(kv[1], CultureInfo.InvariantCulture)));
                    break;
                case "--rename-dialog":
                    _renameDialog = true;
                    break;
                case "--layer-settings":
                    _openLayerSettings = true;
                    break;
                case "--watch":
                    _watch = int.Parse(next, CultureInfo.InvariantCulture);
                    break;
                case "--screenshot-turn":
                    _screenshotTurn = long.Parse(next, CultureInfo.InvariantCulture);
                    break;
                case "--menu-screenshot":
                    _menuScreenshot = next;
                    break;
                case "--menu-section":
                    _menuSection = next;
                    break;
                case "--zoom":
                    _startZoom = float.Parse(next, CultureInfo.InvariantCulture);
                    break;
                case "--overlay":
                    _startOverlay = Enum.Parse<MapLayer>(next, ignoreCase: true);
                    break;
                case "--benchmark":
                    _benchmarkFrames = int.Parse(next, CultureInfo.InvariantCulture);
                    skipMenu = true;
                    break;
                case "--no-menu":
                    skipMenu = true;
                    break;
                case "--play":
                    _playArgs = true;
                    break;
                case "--play-screenshot":
                    _playScreenshot = next;
                    break;
                case "--play-window":
                    _playWindow = next;
                    break;
                case "--play-long":
                    _playLong = true;
                    break;
                case "--play-scenario":
                    _playScenario = next;
                    _playArgs = true;
                    break;
                case "--play-demo":
                    _playDemo = true;
                    break;
                case "--play-seconds":
                    _playSeconds = double.Parse(next, CultureInfo.InvariantCulture);
                    break;
            }

            skipMenu |= Array.IndexOf(StartSettings.ArgumentNames, args[i]) >= 0;
        }

        if (_playArgs)
        {
            StartPlay(_saved.Start.WithArguments(args).Seed);
            return;
        }

        if (skipMenu)
        {
            // Command-line values apply to this run only; the menu keeps the settings the user saved there.
            StartGame(_saved.Start.WithArguments(args), save: false);
        }
        else
        {
            ShowMenu();
        }
    }

    // The new game (ADR 0028): replaces the legacy viewer while it runs.
    private void StartPlay(string seedText, int goal = 1, string? scenario = null, CivSim.Server.PlayOptions? options = null)
    {
        // 2026-11-22: game.json may have changed while the menu was open (typos, unknown keys): check it before a game.
        if (!DataReady(() => StartPlay(seedText, goal, scenario, options)))
        {
            return;
        }

        StopGame();
        _menu?.QueueFree();
        _menu = null;
        ulong seed = ulong.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong s) ? s : 1;
        _play = new Play.PlayGame(seed, _layers, _art, _dataDirectory, startPhase: !_playDemo, goal: goal, scenario: scenario ?? _playScenario, options: options);
        if (_playScreenshot is not null)
        {
            // --play-demo builds a few things on the way, to check the screen without clicking.
            (double, Action<Play.PlayGame>)[] demo = _playDemo
                ? [(1.5, g => g.ScriptBuildNearCentre("gatherers_hut", 0)), (1.7, g => g.ScriptBuildNearCentre("gatherers_hut", 0)),
                   (1.9, g => g.ScriptBuildNearCentre("woodcutter", 0)), (2.0, g => g.ScriptSpeed(SimulationSpeed.X4)),
                   (8, g => g.ScriptResearch("dwellings")), (12, g => g.ScriptResearch("scouting")),
                   (12.5, g => g.ScriptBuildNearCentre("hut", 2)), (12.7, g => g.ScriptBuildNearCentre("hut", 2)), (12.9, g => g.ScriptBuildNearCentre("hut", 2)),
                   (16, g => g.ScriptTrain("scouts")), (18, g => g.ScriptMoveFirst(-8, -6, false)),
                   (19, g => g.ScriptSpeed(SimulationSpeed.X4)), (40, g => g.ScriptResearchAny()), (41, g => g.ScriptOpen("Research"))]
                : [];
            if (_playDemo && _playWindow == "Far")
            {
                demo = [.. demo, (3, g => g.ScriptRoad()), (_playSeconds - 1, g => g.ScriptZoom(0.3f))];
            }
            else if (_playDemo && _playWindow == "Mid")
            {
                demo = [.. demo, (3, g => g.ScriptRoad()), (_playSeconds - 1, g => g.ScriptZoom(0.6f))];
            }
            else if (_playDemo && _playWindow == "Road")
            {
                demo = [.. demo, (3, g => g.ScriptRoad())];
            }
            else if (_playWindow == "Place")
            {
                // Placing a building (2026-11-23): the free places of every hex it may go on show as slots.
                demo = [(_playSeconds - 2, g => g.ScriptPlace("potter")), (_playSeconds - 1.5, g => g.ScriptZoom(2.2f))];
            }
            else if (_playWindow == "TechTip")
            {
                // A technology's tooltip with the goods' pictures (2026-11-23), over a tree that is not built anew.
                demo = [.. demo, (_playSeconds - 3, g => g.ScriptOpen("Research")), (_playSeconds - 2, g => g.ScriptHoverCard("agriculture"))];
            }
            else if (_playWindow is "Deposit" or "DepositFar")
            {
                // 2026-11-24: how a known deposit shows on its hex, near and from the middle distance.
                float zoom = _playWindow == "Deposit" ? 2.2f : 0.9f;
                demo = [(_playSeconds - 2, g => g.ScriptFocusDeposit()), (_playSeconds - 1, g => g.ScriptZoom(zoom))];
            }
            else if (_playWindow == "Help")
            {
                demo = [(_playSeconds - 1.5, g => g.ScriptKey(Key.H))];
            }
            else if (_playWindow == "AlertTip")
            {
                demo = [.. demo, (_playSeconds - 1.2, g => g.ScriptHoverAlert())];
            }
            else if (_playWindow == "SlotTip")
            {
                demo = [.. demo, (_playSeconds - 2, g => g.ScriptHoverSlot())];
            }
            else if (_playWindow == "PanelEnd")
            {
                // The end of the side panel (training buttons and the like), 2026-11-23.
                demo = [.. demo, (_playSeconds - 0.8, g => g.ScriptPanelEnd())];
            }
            else if (_playWindow == "Ways")
            {
                // The roads and works tab of the bar with its pictures, over the map from near (2026-11-23).
                demo = [.. demo, (_playSeconds - 1, g => g.ScriptZoom(3.5f)), (_playSeconds - 0.8, g => g.ScriptTab("Roads & works"))];
            }
            else if (_playWindow is "Close" or "Closest")
            {
                // The pictures from near (decisions G1-G6, 2026-11-23): the map zoomed in on our first settlement.
                float zoom = _playWindow == "Close" ? 3.5f : 5f;
                demo = [.. demo, (_playSeconds - 1, g => g.ScriptZoom(zoom))];
            }
            else if (_playWindow == "Manual")
            {
                // The manual over the game (F1), turned to its second chapter with the Right key (2026-11-23).
                demo = [(_playSeconds - 1.5, g => g.ScriptKey(Key.F1)), (_playSeconds - 1, g => g.ScriptKey(Key.Right))];
            }
            else if (_playWindow == "Controls")
            {
                demo = [(_playSeconds - 1.5, g => g.ScriptKey(Key.F1)), (_playSeconds - 1.2, g => g.ScriptKey(Key.Right)), (_playSeconds - 1, g => g.ScriptKey(Key.Right))];
            }
            else if (_playWindow == "Cursor")
            {
                // The keyboard cursor (Enter) moved down the side panel with the arrows (2026-11-23).
                demo = [(_playSeconds - 2, g => g.ScriptKey(Key.Enter)), (_playSeconds - 1.6, g => g.ScriptKey(Key.Down)), (_playSeconds - 1.3, g => g.ScriptKey(Key.Down))];
            }
            else if (_playWindow == "CursorResearch")
            {
                // The keyboard cursor in the research tree: 2 opens it, Enter the cursor, the arrows walk the cards.
                demo = [(_playSeconds - 2.5, g => g.ScriptKey(Key.Key2)), (_playSeconds - 2, g => g.ScriptKey(Key.Enter)), (_playSeconds - 1.6, g => g.ScriptKey(Key.Down)), (_playSeconds - 1.3, g => g.ScriptKey(Key.Right))];
            }
            else if (_playWindow == "KeysSettlement")
            {
                // Q, E: our settlements; R: our units (2026-11-23).
                demo = [(_playSeconds - 2, g => g.ScriptKey(Key.R)), (_playSeconds - 1, g => g.ScriptKey(Key.E))];
            }
            else if (_playWindow == "Pick")
            {
                demo = [(5.0, g => g.ScriptMouseClick(new Vector2(800, 450)))];
            }
            else if (_playWindow == "PickHover")
            {
                demo = [(_playSeconds - 1, g => g.GetViewport().WarpMouse(new Vector2(800, 450)))];
            }
            else if (_playDemo && _playWindow == "ResearchHover")
            {
                demo = [.. demo, (_playSeconds - 2, g => g.ScriptOpen("Research")), (_playSeconds - 1, g => g.ScriptHoverTech("pottery"))];
            }
            else if (_playWindow == "Prospect")
            {
                demo = [(1.0, g => g.ScriptSpeed(SimulationSpeed.X4)), (1.5, g => g.ScriptSendKind("prospectors", 9, -3)), (_playSeconds - 1.5, g => g.ScriptZoom(0.9f))];
            }
            else if (_playWindow == "Battle")
            {
                // A test scenario with armies on a field: combat mode, all soldiers picked, sent in formation at the enemy.
                demo = [(1.0, g => g.ScriptBattle(0)), (1.5, g => g.ScriptBattle(1)), (2.0, g => g.ScriptSpeed(SimulationSpeed.X1)), (_playSeconds - 0.6, g => g.ScriptBattle(3))];
            }
            else if (_playWindow == "Victory")
            {
                // The end of the game screen: a test scenario's debug key moves our people on by eras up to the goal.
                demo = [(1.0, g => g.ScriptDebug(4)), (1.6, g => g.ScriptDebug(4)), (2.2, g => g.ScriptDebug(4)), (2.5, g => g.ScriptSpeed(SimulationSpeed.X4))];
            }
            else if (_playWindow == "BattleBox")
            {
                demo = [(1.0, g => g.ScriptBattle(0)), (1.5, g => g.ScriptBattle(2))];
            }
            else if (_playDemo && _playWindow == "Hex")
            {
                demo = [.. demo, (_playSeconds - 1, g => g.ScriptSelectBusiestHex())];
            }
            else if (_playDemo && _playWindow == "Units")
            {
                demo = [.. demo, (24, g => g.ScriptPatrolFirst()), (_playSeconds - 1, g => g.ScriptOpen("Units"))];
            }
            else if (_playDemo && _playWindow == "Slots")
            {
                demo = [.. demo, (_playSeconds - 1, g => g.ScriptZoom(2.6f))];
            }
            else if (_playDemo && _playWindow == "Place")
            {
                demo = [.. demo, (_playSeconds - 1, g => g.ScriptPlace("storehouse"))];
            }
            else if (_playWindow is { Length: > 0 } && _playWindow != "Summary")
            {
                // A23 (tests 2026-11-20): with a scenario too, not only with --play-demo.
                demo = [.. demo, (_playSeconds - 1, g => g.ScriptOpen(_playWindow))];
            }

            if (_playLong)
            {
                demo = [(1, g => g.ScriptSpeed(SimulationSpeed.Max)), (_playSeconds - 3, g => g.ScriptSpeed(SimulationSpeed.Paused)),
                        (_playSeconds - 2.5, g => g.ScriptOpen(_playWindow)), (_playSeconds - 2, g => g.ScriptZoom(0.35f))];
            }

            _play.ScreenshotAfter(_playScreenshot, _playSeconds, demo);
        }

        _play.MenuRequested += () =>
        {
            _play?.QueueFree();
            _play = null;
            _camera.MakeCurrent();
            CallDeferred(MethodName.ShowMenu);
        };
        AddChild(_play);
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        SaveSettings();
        StopGame();
    }

    // Builds the picture library for an art pack (checking its files) and the pixel theme from it.
    private void LoadArt(string packKey)
    {
        string artDirectory = ArtLibrary.FindArtDirectory();
        ArtPack? pack = ArtPack.Find(artDirectory, packKey);
        if (!string.IsNullOrWhiteSpace(packKey) && pack is null)
        {
            GD.PrintErr($"art: pack '{packKey}' not found; default pictures are used");
        }

        _art = new ArtLibrary(artDirectory, pack);
        GD.Print($"viewer: art {(pack is null ? "default" : "pack " + pack.Name)}, {_art.Issues.Count} art problems");
        Theme? theme = PixelTheme.Build(_art);
        GetWindow().Theme = theme;
        PixelTheme.Current = theme;
    }

    // The font and the interface size are set from inside a game (its own load and save): keep them, then save the rest.
    private void SaveSettings()
    {
        ViewerSettings now = ViewerSettings.Load();
        _saved.Font = now.Font;
        _saved.UiScale = now.UiScale;
        _saved.Save();
    }

    private void ShowMenu()
    {
        StopGame();
        if (!DataReady(ShowMenu))
        {
            return;
        }

        _menu = new StartMenu(LocalServerHost.Describe(_dataDirectory), _menuSettings ?? _saved.Start.Copy(), _dataDirectory, _art, ArtPack.Discover(ArtLibrary.FindArtDirectory()));
        _menuSettings = null;
        _menu.ArtPackChosen += key =>
        {
            // Keep what the user typed, reload the pictures and the theme, and show the menu again with them.
            _menuSettings = _menu?.Settings;
            LoadArt(key);
            _menu?.QueueFree();
            _menu = null;
            CallDeferred(MethodName.ShowMenu);
        };
        _menu.QuitRequested += () => GetTree().Quit();
        _menu.PlayRequested += options =>
        {
            // The menu's choices are kept for next time.
            _saved.Start = _menu?.Settings.Copy() ?? _saved.Start;
            SaveSettings();
            StartPlay(options.Seed.ToString(CultureInfo.InvariantCulture), options.Goal, options.Scenario, options);
        };
        if (_menuSection is not null)
        {
            _menu.Section = _menuSection; // for screenshots of a section (e.g. "manual")
        }

        AddChild(_menu);
        if (_menuScreenshot is not null)
        {
            _screenshotFrames = 20;
        }
    }

    private void StartGame(StartSettings settings, bool save)
    {
        LocalGameOptions options;
        try
        {
            options = settings.ToOptions();
        }
        catch (FormatException ex)
        {
            GD.PrintErr(ex.Message);
            if (_menu is null)
            {
                ShowMenu();
            }

            return;
        }

        if (save)
        {
            _saved.Start = settings.Copy();
            SaveSettings();
        }

        if (!DataReady(() => StartGame(settings, save)))
        {
            return;
        }

        _menu?.QueueFree();
        _menu = null;
        _lastSettings = settings.Copy();
        _host = LocalServerHost.Start(options, _dataDirectory);
        // Stage 6 PR P2: "lead civilization N" connects as its player (fog, levers, orders); 0 watches everything.
        _session = settings.Lead > 0
            ? new ClientSession(_host.Connect(), ClientRole.Player, "Player", settings.Lead - 1)
            : new ClientSession(_host.Connect(), ClientRole.Observer, "Viewer");
        SimulationSpeed speed = StartSettings.ParseSpeed(settings.Speed);
        if (_host.LongRunFolder is { } folder)
        {
            // A long run that starts paused would record nothing: it runs at maximum speed unless a speed was chosen.
            speed = speed == SimulationSpeed.Paused ? SimulationSpeed.Max : speed;
            _host.LogLongRun(string.Create(
                CultureInfo.InvariantCulture,
                $"viewer: Godot {Engine.GetVersionInfo()["string"]}, {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()}), window {GetWindow().Size.X}x{GetWindow().Size.Y}, start speed {speed}"));
            GD.Print($"long run: writing to {folder}");
        }

        if (speed != SimulationSpeed.Paused)
        {
            _session.SetSpeed(speed);
        }

        if (_startOverlay is { } solo)
        {
            _layers.Solo(solo);
        }

        foreach (MapLayer extra in _extraLayers)
        {
            _layers.SetEnabled(extra, true);
        }

        foreach ((MapLayer layer, int opacity) in _opacities)
        {
            _layers.SetOpacity(layer, opacity);
        }

        if (_watch != WelcomeMessage.NoCiv)
        {
            _layers.SetEnabled(MapLayer.Fog, true);
        }

        _map = new MapView(_layers, _art);
        AddChild(_map);
        var icons = new LayerBar(_layers, _art);
        HudPanels panels = _saved.Panels;
        if (_openLayerSettings)
        {
            panels.LayerWindow = true;
        }

        _hud = new Hud { Layers = new LayerPanel(_layers), LayerIcons = icons, Panels = panels };
        AddChild(_hud);
        _hud.SpeedChosen += s => _session?.SetSpeed(s);
        _hud.MenuRequested += ShowMenu;
        _hud.PanelsChanged += panels => _saved.Panels = panels;
        _hud.FollowChosen += Follow;
        _hud.GoToFollowed += () => GoToFollowed(jump: true);
        _session.ChronicleAdded += lines => _hud?.AddChronicle(lines);
        if (_session.IsPlayer)
        {
            _helm = new HelmHud(_session);
            AddChild(_helm);
        }
        _bound = false;
        GD.Print($"CivSim 2 viewer: protocol v{ProtocolInfo.Version}, seed {options.Seed}, map {options.Width}x{options.Height}");
    }

    // Follows a civilization: its fog and details come from the server, the panel opens on its tab.
    private void Follow(int civ)
    {
        if (_session is null || civ == _session.WatchedCiv)
        {
            return;
        }

        _session.Watch(civ);
        if (civ != WelcomeMessage.NoCiv)
        {
            _hud?.ShowCivilizationTab();
            GoToFollowed(jump: true);
        }
    }

    // Next civilization in the list (F); after the last, nobody.
    private void FollowNext()
    {
        if (_session?.View is not { } view || view.Civilizations.Count == 0)
        {
            return;
        }

        int k = -1;
        for (int i = 0; i < view.Civilizations.Count; i++)
        {
            if (view.Civilizations[i].Id == _session.WatchedCiv)
            {
                k = i;
            }
        }

        Follow(k + 1 < view.Civilizations.Count ? view.Civilizations[k + 1].Id : WelcomeMessage.NoCiv);
    }

    // Moves the camera to the followed civilization's most populous settlement: at once, or gently while tracking.
    private void GoToFollowed(bool jump)
    {
        int followed = _session is { IsPlayer: true } ? _session.CivId : _session?.WatchedCiv ?? -1;
        if (_session?.View?.Settlements is not { } settlements || _map is null || followed < 0)
        {
            return;
        }

        Vector2? best = null;
        int bestPeople = -1;
        foreach (SettlementView s in settlements)
        {
            if (s.Owner != followed || s.Status != 0 || s.World != 0)
            {
                continue;
            }

            int people = 0;
            foreach (int n in s.Population ?? [])
            {
                people += n;
            }

            if (people > bestPeople)
            {
                bestPeople = people;
                best = HexLayout.Center(s.Col, s.Row);
            }
        }

        if (best is not { } target)
        {
            return;
        }

        // The nearest copy on the wrapped map, so the camera never flies the long way round.
        target.X += Mathf.Round((_camera.Position.X - target.X) / _map.Period.X) * _map.Period.X;
        _camera.Position = jump ? target : _camera.Position.Lerp(target, 0.08f);
    }

    // F5 (stage 6, T14): read data/ again and start the same game (same settings and seed) with it, without closing
    // the viewer. Bad data keeps the current game running and shows what is wrong.
    private void ReloadData()
    {
        if (_lastSettings is not { } settings || !DataReady(ReloadData))
        {
            return;
        }

        GD.Print("viewer: data reloaded, same game started again");
        StopGame();
        StartGame(settings, save: false);
    }

    // True when the data files load; otherwise shows every problem with a button to try again after fixing the files.
    private bool DataReady(Action retry)
    {
        IReadOnlyList<string> errors = LocalServerHost.CheckData(_dataDirectory);
        _dataDialog?.QueueFree();
        _dataDialog = null;
        if (errors.Count == 0)
        {
            return true;
        }

        const int Shown = 30;
        string text = string.Join("\n", errors.Take(Shown)) + (errors.Count > Shown ? $"\n... and {errors.Count - Shown} more" : string.Empty);
        GD.PrintErr("viewer: game data is invalid:\n" + string.Join("\n", errors));
        _dataDialog = new AcceptDialog
        {
            Title = $"Game data has {errors.Count} problem{(errors.Count == 1 ? string.Empty : "s")} ({_dataDirectory})",
            DialogText = text + "\n\nFix the files and press Reload data.",
            OkButtonText = "Reload data",
            Exclusive = true,
        };
        _dataDialog.Confirmed += () => CallDeferred(MethodName.RunRetry);
        _retry = retry;
        AddChild(_dataDialog);
        _dataDialog.PopupCentered();
        return false;
    }

    private void RunRetry() => _retry?.Invoke();

    private void StopGame()
    {
        _session?.Close();
        _session = null;
        _host?.Dispose();
        _host = null;
        _map?.QueueFree();
        _map = null;
        _hud?.QueueFree();
        _hud = null;
        _helm?.QueueFree();
        _helm = null;
        _bound = false;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        long start = Stopwatch.GetTimestamp();
        if (_session is null || _map is null || _hud is null)
        {
            MenuScreenshot();
            return;
        }

        _session.Pump();
        if (!_bound && _session.Worlds.TryGetValue(0, out ClientWorld? earth) && _session.Content is not null)
        {
            _map.Bind(_session, earth);
            Vector2 size = _map.Period;
            _camera.Position = size / 2;
            _camera.Zoom = new Vector2(_startZoom, _startZoom);
            _bound = true;
            _screenshotFrames = 20;
            if (_watch != WelcomeMessage.NoCiv)
            {
                Follow(_watch);
            }
            else if (_session.IsPlayer)
            {
                _playerCamera = true; // centre on our people once our first settlement is in view
            }
        }

        if (_playerCamera && _session.CivId >= 0 && _session.View?.Settlements is { Count: > 0 })
        {
            _playerCamera = false;
            GoToFollowed(jump: true);
        }

        if (_hud.Tracking)
        {
            GoToFollowed(jump: false);
        }

        MoveCamera((float)delta);
        _map.SetGrid(_camera.Zoom.X >= 0.6f);
        _map.Refresh(VisibleRect(), _camera.Zoom.X);

        _statusTimer -= delta;
        if (_statusTimer <= 0)
        {
            _statusTimer = 0.2;
            _hud.UpdateStatus(_session, _map, Engine.GetFramesPerSecond());
            _helm?.Refresh();
                    }

        if (_host?.Error is { } error)
        {
            GD.PrintErr(error);
            _host.Dispose();
            _host = null;
        }

        double workMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        LongRun(workMs, delta);
        Benchmark(workMs);
        Screenshot();
    }

    // --menu-screenshot file.png: saves the start menu a few frames after it appeared, then quits.
    private void MenuScreenshot()
    {
        if (_menuScreenshot is null || _screenshotFrames < 0 || --_screenshotFrames > 0)
        {
            return;
        }

        GetViewport().GetTexture().GetImage().SavePng(_menuScreenshot);
        GD.Print($"screenshot: {_menuScreenshot}");
        GetTree().Quit();
    }

    // --screenshot file.png: saves what the window shows a few frames after the map arrived, then quits.
    private void Screenshot()
    {
        if (_screenshot is null || _screenshotFrames < 0 || _map is null)
        {
            return;
        }

        // --screenshot-turn N waits until the simulation has run N turns.
        if ((_session?.View?.Turn ?? 0) < _screenshotTurn)
        {
            _screenshotFrames = 20;
            return;
        }

        if (_screenshotFrames == 10)
        {
            GoToFollowed(jump: true);
            if (_renameDialog)
            {
                _hud?.RenameFollowed();
            }
        }

        _screenshotFrames--;
        if (_screenshotFrames == 3)
        {
            _map.SelectAt(_camera.Position);
            _statusTimer = 0;
        }

        if (_screenshotFrames > 0)
        {
            return;
        }

        Image image = GetViewport().GetTexture().GetImage();
        image.SavePng(_screenshot);
        GD.Print($"screenshot: {_screenshot}");
        GetTree().Quit();
    }

    /// <inheritdoc />
    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed:
                ZoomAt(1.15f, mb.Position);
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed:
                ZoomAt(1 / 1.15f, mb.Position);
                break;
            case InputEventMouseButton mb when mb.ButtonIndex is MouseButton.Right or MouseButton.Middle:
                _dragging = mb.Pressed;
                if (mb.Pressed && mb.ButtonIndex == MouseButton.Right && _helm is { Targeting: true })
                {
                    _helm.CancelTarget();
                }

                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left && mb.Pressed && _map is not null:
                (int Col, int Row)? cell = _map.SelectAt(GetGlobalMousePosition());
                if (cell is { } c && _helm is not null)
                {
                    // A player: the click is the target of a waiting order, or selects what of ours stands there.
                    if (!_helm.TargetClicked(0, c.Col, c.Row))
                    {
                        _helm.Select(0, c.Col, c.Row);
                    }
                }

                _hud?.ShowCellTab();
                _statusTimer = 0;
                break;
            case InputEventMouseMotion mm when _dragging:
                _camera.Position -= mm.Relative / _camera.Zoom;
                break;
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key.Keycode);
                break;
        }
    }

    private void HandleKey(Key key)
    {
        if (_session is null)
        {
            return;
        }

        switch (key)
        {
            case Key.Space:
                _session.SetSpeed(_session.View?.Speed == SimulationSpeed.Paused ? SimulationSpeed.X1 : SimulationSpeed.Paused);
                break;
            case Key.Key1:
                _session.SetSpeed(SimulationSpeed.Paused);
                break;
            case Key.Key2:
                _session.SetSpeed(SimulationSpeed.X1);
                break;
            case Key.Key3:
                _session.SetSpeed(SimulationSpeed.X2);
                break;
            case Key.Key4:
                _session.SetSpeed(SimulationSpeed.X4);
                break;
            case Key.Key5:
                _session.SetSpeed(SimulationSpeed.Max);
                break;
            case Key.Tab:
                _hud?.ToggleLayerPanel();
                break;
            case Key.F:
                FollowNext();
                break;
            case Key.G:
                GoToFollowed(jump: true);
                break;
            case Key.T:
                _hud?.ToggleTracking();
                break;
            case Key.I:
                _hud?.TogglePanel('I');
                break;
            case Key.L:
                _hud?.TogglePanel('L');
                break;
            case Key.C:
                _hud?.TogglePanel('C');
                break;
            case Key.F5:
                ReloadData();
                break;
            case Key.Escape when _helm is { Targeting: true }:
                _helm.CancelTarget();
                break;
            case Key.Escape when _menu is null:
                ShowMenu();
                break;
            case Key.Equal or Key.KpAdd:
                ZoomAt(1.25f, GetViewportRect().Size / 2);
                break;
            case Key.Minus or Key.KpSubtract:
                ZoomAt(0.8f, GetViewportRect().Size / 2);
                break;
        }
    }

    private void MoveCamera(float delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left))
        {
            dir.X -= 1;
        }

        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right))
        {
            dir.X += 1;
        }

        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up))
        {
            dir.Y -= 1;
        }

        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down))
        {
            dir.Y += 1;
        }

        _camera.Position += dir * PanSpeed * delta / _camera.Zoom.X;

        // The globe wraps east–west: keep the camera inside one copy of the map, the renderer draws the neighbours.
        // North and south end at the poles, so the camera stops there.
        Vector2 p = _map?.Period ?? Vector2.Zero;
        if (p.X > 0 && p.Y > 0)
        {
            _camera.Position = new Vector2(Mathf.PosMod(_camera.Position.X, p.X), Mathf.Clamp(_camera.Position.Y, 0, p.Y));
        }
    }

    private void ZoomAt(float factor, Vector2 screenPos)
    {
        Vector2 before = ScreenToWorld(screenPos);
        float z = Mathf.Clamp(_camera.Zoom.X * factor, MinZoom, MaxZoom);

        // Never zoom out so far that more than two copies of the map fit side by side.
        Vector2 view = GetViewportRect().Size;
        if (_map is not null && _map.Period.X > 0)
        {
            z = Mathf.Max(z, view.X / (2 * _map.Period.X));
        }

        _camera.Zoom = new Vector2(z, z);
        _camera.Position += before - ScreenToWorld(screenPos);
    }

    private Vector2 ScreenToWorld(Vector2 screen) => _camera.Position + ((screen - (GetViewportRect().Size / 2)) / _camera.Zoom);

    private Rect2 VisibleRect()
    {
        Vector2 size = GetViewportRect().Size / _camera.Zoom;
        return new Rect2(_camera.Position - (size / 2), size);
    }

    // Long run: frame times and the engine's memory go to the host's statistics; at the time limit the game closes.
    private void LongRun(double workMs, double delta)
    {
        if (_host?.LongRunFolder is null)
        {
            return;
        }

        // Measured here: the engine's own delta is capped, so it hides long stalls.
        long now = Stopwatch.GetTimestamp();
        _host.ReportFrame(workMs, _lastFrame == 0 ? delta * 1000 : Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds);
        _lastFrame = now;
        _engineTimer -= delta;
        if (_engineTimer <= 0)
        {
            _engineTimer = 1;
            _host.ReportEngine(
                (long)(OS.GetStaticMemoryUsage() / (1024 * 1024)),
                (long)(Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024 * 1024)),
                (long)Performance.GetMonitor(Performance.Monitor.ObjectCount),
                (long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
        }

        if (_host.LongRunFinished)
        {
            GD.Print($"long run finished: {_host.LongRunFolder}");
            GetTree().Quit();
        }
    }

    private void Benchmark(double frameMs)
    {
        if (_benchmarkFrames <= 0 || !_bound || _map is null)
        {
            return;
        }

        if (_frames == 0)
        {
            _benchmarkStart = Stopwatch.GetTimestamp();
            _benchmarkGc = GC.CollectionCount(2);

            // Fly over the map at the widest zoom: the worst case for the renderer.
            _camera.Zoom = Vector2.One * MinZoom;
            ZoomAt(1f, GetViewportRect().Size / 2);
        }

        _camera.Position += new Vector2(40, 17);
        if (_frames % 120 == 60)
        {
            // Switch cell layers on top of each other: the worst case for recolouring.
            _layers.SetEnabled((MapLayer)(1 + ((_frames / 120) % 11)), true);
        }

        if (frameMs > 16)
        {
            _slowFrames++;
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"slow frame {_frames}: {frameMs:F1} ms"));
        }

        _frames++;
        _frameMsSum += frameMs;
        _frameMsMax = Math.Max(_frameMsMax, frameMs);
        if (_frames >= _benchmarkFrames)
        {
            double wall = Stopwatch.GetElapsedTime(_benchmarkStart).TotalSeconds;
            GD.Print(string.Create(
                CultureInfo.InvariantCulture,
                $"benchmark: {_frames} frames, client work avg {_frameMsSum / _frames:F3} ms, max {_frameMsMax:F3} ms, frames over 16 ms: {_slowFrames}, wall {_frames / wall:F1} fps, chunks drawn {_map.VisibleChunks}, turn {_session?.View?.Turn}, full GCs {GC.CollectionCount(2) - _benchmarkGc}"));
            GetTree().Quit();
        }
    }

    // Game data lives in the repository's data/ folder (next to game/) while developing; exported builds will ship
    // it next to the executable.
    private static string FindDataDirectory()
    {
        string[] candidates =
        [
            ProjectSettings.GlobalizePath("res://../data"),
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "data"),
            ProjectSettings.GlobalizePath("res://.godot/mono/temp/bin/Debug/data"),
        ];
        foreach (string dir in candidates)
        {
            if (File.Exists(Path.Combine(dir, "biomes.json")))
            {
                return dir;
            }
        }

        return candidates[0];
    }

    /// <summary>
    /// Game options come after a lone <c>--</c> on the command line. When that separator is missing,
    /// Godot passes unknown options through as engine arguments, so those are read instead.
    /// Options are matched by name, so other arguments in between are skipped.
    /// </summary>
    private static string[] GameArguments()
    {
        string[] user = OS.GetCmdlineUserArgs();
        return user.Length > 0 ? user : OS.GetCmdlineArgs();
    }
}
