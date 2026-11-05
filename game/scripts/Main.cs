using System;
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
/// Developer viewer (Stage 2): starts a local server on its own thread, connects to it as an observer and draws
/// what the server sends. It never touches the game state directly (CLAUDE.md rule 10).
/// Command line (after <c>--</c>): <c>--seed N --map-size test|standard|large|huge --speed pause|x1|x2|x5|max
/// --benchmark FRAMES</c>, plus the world settings of <see cref="LocalGameOptions"/>.
/// </summary>
public partial class Main : Node2D
{
    private const float MinZoom = 0.04f;
    private const float MaxZoom = 4f;
    private const float PanSpeed = 900f;

    private LocalServerHost? _host;
    private ClientSession? _session;
    private MapView _map = null!;
    private Camera2D _camera = null!;
    private Hud _hud = null!;
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
    private MapOverlay _startOverlay = MapOverlay.Biomes;

    /// <inheritdoc />
    public override void _Ready()
    {
        GetNodeOrNull<Label>("StatusLabel")?.QueueFree();
        RenderingServer.SetDefaultClearColor(new Color(0.04f, 0.04f, 0.05f));
        string[] args = GameArguments();
        LocalGameOptions options = ParseOptions(args, out SimulationSpeed speed, out _benchmarkFrames);
        for (int i = 0; i + 1 < args.Length; i++)
        {
            switch (args[i])
            {
                case "--screenshot":
                    _screenshot = args[i + 1];
                    break;
                case "--zoom":
                    _startZoom = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    break;
                case "--overlay":
                    _startOverlay = Enum.Parse<MapOverlay>(args[i + 1], ignoreCase: true);
                    break;
            }
        }

        _host = LocalServerHost.Start(options, FindDataDirectory());
        _session = new ClientSession(_host.Connect(), ClientRole.Observer, "Viewer");
        if (speed != SimulationSpeed.Paused)
        {
            _session.SetSpeed(speed);
        }

        _map = new MapView();
        AddChild(_map);
        _camera = new Camera2D { Zoom = new Vector2(0.5f, 0.5f) };
        AddChild(_camera);
        _camera.MakeCurrent();
        _hud = new Hud();
        AddChild(_hud);
        _hud.SpeedChosen += s => _session.SetSpeed(s);
        _hud.OverlayChosen += o => _map.Overlay = o;
        _session.ChronicleAdded += lines => _hud.AddChronicle(lines);
        GD.Print($"CivSim 2 viewer: protocol v{ProtocolInfo.Version}, seed {options.Seed}, map {options.Width}x{options.Height}");
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        _host?.Dispose();
        _host = null;
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        long start = Stopwatch.GetTimestamp();
        if (_session is null)
        {
            return;
        }

        _session.Pump();
        if (!_bound && _session.Worlds.TryGetValue(0, out ClientWorld? earth) && _session.Content is not null)
        {
            _map.Bind(_session, earth);
            Vector2 size = _map.Period;
            _camera.Position = size / 2;
            _camera.Zoom = new Vector2(_startZoom, _startZoom);
            _map.Overlay = _startOverlay;
            _hud.ShowOverlay(_startOverlay);
            _bound = true;
            _screenshotFrames = 20;
        }

        MoveCamera((float)delta);
        _map.SetGrid(_camera.Zoom.X >= 0.6f);
        _map.Refresh(VisibleRect());

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

    // --screenshot file.png: saves what the window shows a few frames after the map arrived, then quits.
    private void Screenshot()
    {
        if (_screenshot is null || _screenshotFrames < 0)
        {
            return;
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
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left && mb.Pressed:
                _map.SelectAt(GetGlobalMousePosition());
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
                MapOverlay next = (MapOverlay)(((int)_map.Overlay + 1) % Enum.GetValues<MapOverlay>().Length);
                _map.Overlay = next;
                _hud.ShowOverlay(next);
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

        // The planet wraps: keep the camera inside one copy of the map; the renderer draws the neighbours.
        Vector2 p = _map.Period;
        if (p.X > 0 && p.Y > 0)
        {
            _camera.Position = new Vector2(Mathf.PosMod(_camera.Position.X, p.X), Mathf.PosMod(_camera.Position.Y, p.Y));
        }
    }

    private void ZoomAt(float factor, Vector2 screenPos)
    {
        Vector2 before = ScreenToWorld(screenPos);
        float z = Mathf.Clamp(_camera.Zoom.X * factor, MinZoom, MaxZoom);

        // Never zoom out so far that more than two copies of the map fit on the screen.
        Vector2 view = GetViewportRect().Size;
        if (_map.Period.X > 0)
        {
            z = Mathf.Max(z, Mathf.Min(view.X / (2 * _map.Period.X), view.Y / (2 * _map.Period.Y)));
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
        if (_benchmarkFrames <= 0 || !_bound)
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
            _map.Overlay = (MapOverlay)((_frames / 120) % 6);
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

    private static LocalGameOptions ParseOptions(string[] args, out SimulationSpeed speed, out int benchmark)
    {
        ulong seed = 1;
        string size = "standard";
        speed = SimulationSpeed.Paused;
        benchmark = 0;
        string? worldType = null;
        string? climate = null;
        string? moisture = null;
        string? inland = null;
        string? relief = null;
        string? resources = null;
        int? water = null;
        int? civs = null;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            string v = args[i + 1];
            switch (args[i])
            {
                case "--seed":
                    seed = ulong.Parse(v, CultureInfo.InvariantCulture);
                    break;
                case "--map-size":
                    size = v;
                    break;
                case "--speed":
                    speed = v switch { "x1" => SimulationSpeed.X1, "x2" => SimulationSpeed.X2, "x5" => SimulationSpeed.X5, "max" => SimulationSpeed.Max, _ => SimulationSpeed.Paused };
                    break;
                case "--benchmark":
                    benchmark = int.Parse(v, CultureInfo.InvariantCulture);
                    break;
                case "--world-type":
                    worldType = v;
                    break;
                case "--climate":
                    climate = v;
                    break;
                case "--inland":
                    inland = v;
                    break;
                case "--moisture":
                    moisture = v;
                    break;
                case "--relief":
                    relief = v;
                    break;
                case "--resources":
                    resources = v;
                    break;
                case "--water":
                    water = int.Parse(v, CultureInfo.InvariantCulture);
                    break;
                case "--civs":
                    civs = int.Parse(v, CultureInfo.InvariantCulture);
                    break;
            }
        }

        LocalGameOptions o = LocalGameOptions.ForSize(size, seed);
        return o with
        {
            Civilizations = civs ?? o.Civilizations,
            WorldType = worldType ?? o.WorldType,
            WaterPercent = water ?? o.WaterPercent,
            Climate = climate ?? o.Climate,
            Moisture = moisture ?? o.Moisture,
            Inland = inland ?? o.Inland,
            Relief = relief ?? o.Relief,
            Resources = resources ?? o.Resources,
        };
    }
}
