using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
    private MapView? _map;
    private Camera2D _camera = null!;
    private Hud? _hud;
    private StartMenu? _menu;
    private ArtLibrary _art = null!;
    private string _dataDirectory = string.Empty;
    private bool _dragging;
    private bool _bound;
    private int _benchmarkFrames;
    private int _frames;
    private double _frameMsSum;
    private double _frameMsMax;
    private int _slowFrames;
    private long _benchmarkStart;
    private double _statusTimer;
    private string? _screenshot;
    private int _screenshotFrames = -1;
    private float _startZoom = 0.5f;
    private MapLayer? _startOverlay;
    private string? _menuScreenshot;
    private StartSettings? _menuSettings;
    private long _screenshotTurn;
    private readonly List<MapLayer> _extraLayers = [];
    private int _watch = WelcomeMessage.NoCiv;
    private readonly List<(MapLayer Layer, int Opacity)> _opacities = [];
    private bool _openLayerSettings;
    private bool _renameDialog;

    /// <inheritdoc />
    public override void _Ready()
    {
        GetNodeOrNull<Label>("StatusLabel")?.QueueFree();
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
            }

            skipMenu |= Array.IndexOf(StartSettings.ArgumentNames, args[i]) >= 0;
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

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _saved.Save();
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

    private void ShowMenu()
    {
        StopGame();
        _menu = new StartMenu(LocalServerHost.Describe(_dataDirectory), _menuSettings ?? _saved.Start.Copy(), _dataDirectory, _art, ArtPack.Discover(ArtLibrary.FindArtDirectory()));
        _menuSettings = null;
        _menu.StartRequested += settings => StartGame(settings, save: true);
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
            _saved.Save();
        }

        _menu?.QueueFree();
        _menu = null;
        _host = LocalServerHost.Start(options, _dataDirectory);
        _session = new ClientSession(_host.Connect(), ClientRole.Observer, "Viewer");
        SimulationSpeed speed = StartSettings.ParseSpeed(settings.Speed);
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
        if (_session?.View?.Settlements is not { } settlements || _map is null || _session.WatchedCiv < 0)
        {
            return;
        }

        Vector2? best = null;
        int bestPeople = -1;
        foreach (SettlementView s in settlements)
        {
            if (s.Owner != _session.WatchedCiv || s.Status != 0 || s.World != 0)
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
                    }

        if (_host?.Error is { } error)
        {
            GD.PrintErr(error);
            _host.Dispose();
            _host = null;
        }

        Benchmark(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
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
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left && mb.Pressed && _map is not null:
                _map.SelectAt(GetGlobalMousePosition());
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
                _session.SetSpeed(SimulationSpeed.X5);
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

    private void Benchmark(double frameMs)
    {
        if (_benchmarkFrames <= 0 || !_bound || _map is null)
        {
            return;
        }

        if (_frames == 0)
        {
            _benchmarkStart = Stopwatch.GetTimestamp();

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
                $"benchmark: {_frames} frames, client work avg {_frameMsSum / _frames:F3} ms, max {_frameMsMax:F3} ms, frames over 16 ms: {_slowFrames}, wall {_frames / wall:F1} fps, chunks drawn {_map.VisibleChunks}, turn {_session?.View?.Turn}"));
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
