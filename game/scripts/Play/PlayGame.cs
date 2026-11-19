using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using CivSim.Server;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The screen of the new game (ADR 0028, docs/projekt-gry.md section 11): the map, resources at the top left, time in the
/// middle, the chronicle at the bottom right, the building bar at the bottom and the selected settlement or building on the
/// left. Talks to a local <see cref="PlayHost"/> through the protocol only.
/// </summary>
public partial class PlayGame : Node2D
{
    private const float PanSpeed = 900f;
    private const float MinZoom = 0.08f;
    private const float MaxZoom = 3.5f;

    private readonly ulong _seed;
    private readonly bool _startPhase;
    private readonly int _goal;
    private readonly LayerStack _layers;
    private readonly ArtLibrary _art;
    private readonly string? _dataDirectory;
    private readonly List<string> _chronicle = [];
    private PlayHost? _host;
    private ClientSession? _session;
    private MapView? _map;
    private PlayOverlay? _overlay;
    private Camera2D _camera = null!;
    private bool _bound;
    private bool _centred;
    private int _placing;
    private (int Col, int Row)? _selected;
    private int _unit = -1;
    private bool _pickView;
    private readonly HashSet<int> _hiddenWalk = [];
    private double _uiTimer;
    private Vector2? _dragFrom;

    private string _bigMode = "";
    private string _bigSignature = "";
    private string? _screenshot;
    private int _screenshotFrames = -1;
    private readonly List<(double At, Action Do)> _script = [];
    private double _time;

    private readonly string? _scenario;

    /// <summary>Creates the screen; the game starts when the node enters the tree.</summary>
    public PlayGame(ulong seed, LayerStack layers, ArtLibrary art, string? dataDirectory, bool startPhase = true, int goal = 1, string? scenario = null)
    {
        _scenario = scenario;
        _goal = goal;
        _startPhase = startPhase;
        _seed = seed;
        _layers = layers;
        _art = art;
        _dataDirectory = dataDirectory;
    }

    /// <summary>Raised when the player wants the start menu back.</summary>
    public event Action? MenuRequested;

    /// <summary>Saves a screenshot after <paramref name="seconds"/> of play, then quits (for automated checks).</summary>
    public void ScreenshotAfter(string path, double seconds, IEnumerable<(double At, Action<PlayGame> Do)>? steps = null)
    {
        _screenshot = path;
        _script.Add((seconds, () => _screenshotFrames = 3));
        if (steps is not null)
        {
            foreach ((double at, Action<PlayGame> act) in steps)
            {
                _script.Add((at, () => act(this)));
            }
        }
    }

    /// <summary>Starts placing a building by key (used by the automated screenshot script).</summary>
    public void ScriptBuildNearCentre(string key, int nth)
    {
        if (_session?.PlayContent is not { } content || _session.PlayView is not { } view)
        {
            return;
        }

        int type = -1;
        foreach (PlayBuildingInfo b in content.Buildings)
        {
            if (b.Key == key)
            {
                type = b.Id;
            }
        }

        _session.PlacesReceived += Once;
        _session.AskPlaces(type);

        void Once(PlacesMessage m)
        {
            _session.PlacesReceived -= Once;
            if (m.Cells.Length >= 2 * (nth + 1))
            {
                _session.Play(PlayCommandKind.Build, type, m.Cells[2 * nth], m.Cells[(2 * nth) + 1]);
            }
        }
    }

    /// <summary>Script step: starts placing a building by key (to see the placing mode).</summary>
    public void ScriptPlace(string key)
    {
        if (_session?.PlayContent is { } content)
        {
            foreach (PlayBuildingInfo b in content.Buildings)
            {
                if (b.Key == key)
                {
                    StartPlacing(b.Id);
                }
            }
        }
    }

    /// <summary>Script step: builds a track from the first settlement a few hexes up and to the side.</summary>
    public void ScriptRoad()
    {
        if (_session?.PlayView is not { } v)
        {
            return;
        }

        foreach (PlaySettlementView s in v.Settlements)
        {
            if (s.Civ == v.Civ)
            {
                int[] cells = [s.Col, s.Row, s.Col, s.Row - 1, s.Col, s.Row - 2, s.Col + 1, s.Row - 3 + (s.Col & 1), s.Col + 2, s.Row - 3];
                _session.Play(PlayCommandKind.BuildRoad, 1, cells: cells);
                return;
            }
        }
    }

    /// <summary>Script step: opens the research or people window.</summary>
    public void ScriptOpen(string mode)
    {
        _bigMode = mode;
        _bigSignature = "";
        if (mode == "Summary")
        {
            _session?.AskSummary();
        }
    }

    /// <summary>Script step: sets the zoom.</summary>
    public void ScriptZoom(float zoom) => _camera.Zoom = new Vector2(zoom, zoom);

    /// <summary>Script step: sets the speed.</summary>
    public void ScriptSpeed(SimulationSpeed speed) => _session?.SetSpeed(speed);

    /// <summary>Script step: a real left click (press and release) at a point of the screen, through Godot's input.</summary>
    public void ScriptMouseClick(Vector2 at)
    {
        GetViewport().WarpMouse(at);
        foreach (bool down in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = at, GlobalPosition = at });
        }
    }

    /// <summary>Script step: sends the first own unit of a kind by an offset and selects it.</summary>
    public void ScriptSendKind(string key, int dCol, int dRow)
    {
        if (_session?.PlayView is not { } v || _session.PlayContent is not { } c)
        {
            return;
        }

        PlayUnitView? u = v.Units.FirstOrDefault(x => x.Civ == v.Civ && c.Units[x.Kind].Key == key);
        if (u is not null)
        {
            _session.Play(PlayCommandKind.Move, u.Id, u.Col + dCol, u.Row + dRow);
            _unit = u.Id;
            if (_overlay is not null)
            {
                _overlay.SelectedUnit = u.Id;
            }

            UpdateUi();
        }
    }

    /// <summary>Script step: lights a technology in the tree as if the mouse were on it.</summary>
    public void ScriptHoverTech(string key)
    {
        if (_session?.PlayContent is { } c && c.Techs.FirstOrDefault(t => t.Key == key) is { } t)
        {
            TechTree.Hovered = t.Id;
            _bigSignature = "";
            UpdateUi();
        }
    }

    /// <summary>Script step: moves the mouse there as a real move (hover effects).</summary>
    public void ScriptMouseMove(Vector2 at)
    {
        GetViewport().WarpMouse(at);
        Input.ParseInputEvent(new InputEventMouseMotion { Position = at, GlobalPosition = at, Relative = new Vector2(1, 0) });
    }

    /// <summary>Script step: researches a technology by key.</summary>
    public void ScriptResearch(string key)
    {
        if (_session?.PlayContent is { } c && c.Techs.FirstOrDefault(t => t.Key == key) is { } t)
        {
            _session.Play(PlayCommandKind.Research, t.Id);
        }
    }

    /// <summary>Script step: researches the first technology that can start now.</summary>
    public void ScriptResearchAny()
    {
        if (_session?.PlayView is not { } v)
        {
            return;
        }

        foreach (PlayTechState t in v.Techs)
        {
            if (!t.Known && t.Blocked is null)
            {
                _session.Play(PlayCommandKind.Research, t.Id);
                return;
            }
        }
    }

    /// <summary>Script step: trains a unit kind in the first own settlement.</summary>
    public void ScriptTrain(string key)
    {
        if (_session?.PlayContent is not { } c || _session.PlayView is not { } v)
        {
            return;
        }

        foreach (PlayUnitInfo u in c.Units)
        {
            if (u.Key == key)
            {
                foreach (PlaySettlementView s in v.Settlements)
                {
                    if (s.Civ == v.Civ)
                    {
                        _session.Play(PlayCommandKind.Train, s.Id, b: u.Id);
                        return;
                    }
                }
            }
        }
    }

    /// <summary>Script step: selects the hex of our building with the most neighbours on its hex.</summary>
    public void ScriptSelectBusiestHex()
    {
        if (_session?.PlayView is not { } v)
        {
            return;
        }

        var best = v.Buildings.Where(b => b.Civ == v.Civ).GroupBy(b => (b.Col, b.Row)).OrderByDescending(x => x.Count()).FirstOrDefault();
        if (best is not null)
        {
            _selected = best.Key;
            _unit = -1;
            UpdateUi();
        }
    }

    /// <summary>Script step: the first own standing unit patrols up and down its column, four hexes north of it.</summary>
    public void ScriptPatrolFirst()
    {
        if (_session?.PlayView is not { } v)
        {
            return;
        }

        foreach (PlayUnitView u in v.Units)
        {
            if (u.Civ == v.Civ && u.State != 0 && !_session.PlayContent!.Units[u.Kind].System)
            {
                int c = u.Col;
                int r = u.Row;
                _session.Play(PlayCommandKind.Patrol, u.Id, cells: [c, r - 1, c, r - 2, c, r - 3, c, r - 4, c, r - 3, c, r - 2]);
                return;
            }
        }
    }

    /// <summary>Script step: sends the first own standing unit by an offset from where it stands, and selects it.</summary>
    public void ScriptMoveFirst(int dCol, int dRow, bool hidden)
    {
        if (_session?.PlayView is not { } v)
        {
            return;
        }

        foreach (PlayUnitView u in v.Units)
        {
            if (u.Civ == v.Civ && u.State == 1)
            {
                _session.Play(PlayCommandKind.Move, u.Id, u.Col + dCol, u.Row + dRow, hidden);
                _unit = u.Id;
                if (_overlay is not null)
                {
                    _overlay.SelectedUnit = u.Id;
                }

                return;
            }
        }
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        _camera = new Camera2D { Zoom = new Vector2(1.6f, 1.6f) };
        AddChild(_camera);
        _camera.MakeCurrent();
        _host = PlayHost.Start(new PlayOptions(_seed) { StartPhase = _startPhase && _scenario is null, Goal = _goal, Scenario = _scenario }, _dataDirectory);
        _session = new ClientSession(_host.Connect(), ClientRole.Player, "Player");
        _session.CommandRefused += reason => Toast(reason);
        _session.PlacesReceived += m =>
        {
            if (m.Type == _placing)
            {
                _overlay?.SetPlaces(m.Cells);
                if (m.Cells.Length == 0)
                {
                    Toast("No hex of your land suits this building.");
                }
            }
        };
        _session.PlayNews += news =>
        {
            foreach (string n in news)
            {
                _chronicle.Add($"[{Clock()}] {n}");
            }

            if (_chronicle.Count > 200)
            {
                _chronicle.RemoveRange(0, _chronicle.Count - 200);
            }

            RefreshChronicle();
        };
        AddChild(new ParchmentBackground()); // the unknown world: an old map, not black
        _map = new MapView(_layers, _art);
        AddChild(_map);
        _overlay = new PlayOverlay { ZIndex = 50 };
        AddChild(_overlay);
        BuildUi();
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _session?.Close();
        _host?.Dispose();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_session is null || _map is null || _overlay is null)
        {
            return;
        }

        _time += delta;
        for (int i = _script.Count - 1; i >= 0; i--)
        {
            if (_time >= _script[i].At)
            {
                Action act = _script[i].Do;
                _script.RemoveAt(i);
                act();
            }
        }

        _session.Pump();
        if (!_bound && _session.Worlds.TryGetValue(0, out ClientWorld? earth) && _session.Content is not null && _session.PlayContent is not null)
        {
            _map.Bind(_session, earth);
            _overlay.World = earth;
            _overlay.PickProblem = StartProblem;
            _bound = true;
            _session.SetSpeed(SimulationSpeed.X1);
        }

        if (!_centred && !_pickView && _session.PlayView is { Picking: true })
        {
            _camera.Position = _map.Period / 2;
            _camera.Zoom = new Vector2(0.3f, 0.3f);
            _pickView = true;
        }

        if (!_centred && _session.PlayView is { Picking: false } v)
        {
            foreach (PlaySettlementView s in v.Settlements)
            {
                if (s.Civ == v.Civ)
                {
                    _camera.Position = HexLayout.Center(s.Col, s.Row);
                    _centred = true;
                    _selected = (s.Col, s.Row);
                    break;
                }
            }
        }

        MoveCamera((float)delta);
        Rect2 visible = VisibleRect();
        _map.SetGrid(_camera.Zoom.X >= 0.6f);
        _map.Refresh(visible, _camera.Zoom.X);
        if (_placing > 0 || _combat)
        {
            _overlay.SetHover(MouseCell());
        }
        else if (_overlay.CombatHover)
        {
            _overlay.SetHover(null);
        }

        _overlay.CombatHover = _combat && _placing == 0;

        _overlay.SetSelected(_selected);
        _map.SetLift(_selected, _overlay.Lift, _camera.Position);
        if (_session.PlayContent is { } content)
        {
            _overlay.Show(content, _session.PlayView, _map.Period, visible, _camera.Zoom.X);
        }

        _uiTimer -= delta;
        if (_uiTimer <= 0)
        {
            _uiTimer = 0.2;
            UpdateUi();
        }

        UpdateToast(delta);

        if (_host?.Error is { } error)
        {
            GD.PrintErr(error);
            Toast("The game stopped: " + error.Split('\n')[0]);
            _host.Dispose();
            _host = null;
        }

        if (_screenshotFrames >= 0 && --_screenshotFrames < 0 && _screenshot is not null)
        {
            GetViewport().GetTexture().GetImage().SavePng(_screenshot);
            GD.Print($"screenshot: {_screenshot}");
            GetTree().Quit();
        }
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent e)
    {
        if (_session is null || _map is null)
        {
            return;
        }

        if (CombatInput(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true } w:
                ZoomAt(1.15f, w.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true } w:
                ZoomAt(1 / 1.15f, w.Position);
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Middle } m:
                _dragFrom = m.Pressed ? m.Position : null;
                break;
            case InputEventMouseMotion when _drawing:
                ExtendDraw();
                break;
            case InputEventMouseMotion motion when _dragFrom is { } from:
                _camera.Position -= (motion.Position - from) / _camera.Zoom.X;
                _dragFrom = motion.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } left:
                if (left.Pressed && _drawMode is DrawMode.Patrol or DrawMode.Route or DrawMode.Road && !_drawing)
                {
                    BeginDraw(_drawMode);
                }
                else if (!left.Pressed && _drawing && _drawMode is DrawMode.Patrol or DrawMode.Route or DrawMode.Road)
                {
                    FinishDraw();
                }
                else if (left.Pressed)
                {
                    Click();
                }

                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right } right:
                if (right.Pressed && _placing > 0)
                {
                    StopPlacing();
                }
                else if (right.Pressed && _drawMode != DrawMode.None)
                {
                    CancelDraw();
                }
                else if (right.Pressed && SelectedUnitIds().Count > 0)
                {
                    // Press: start drawing; a quick click without moving is a plain "go there".
                    BeginDraw(DrawMode.Move);
                }
                else if (!right.Pressed && _drawing && _drawMode == DrawMode.Move)
                {
                    FinishDraw();
                }

                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                if (_drawMode != DrawMode.None)
                {
                    CancelDraw();
                }
                else if (_help.Visible)
                {
                    _help.Visible = false;
                }
                else if (_bigMode.Length > 0)
                {
                    OpenWindow("");
                }
                else if (_placing > 0)
                {
                    StopPlacing();
                    UpdateUi();
                }
                else if (_selected is not null || _unit >= 0)
                {
                    _selected = null;
                    _unit = -1;
                    UpdateUi();
                }
                else
                {
                    MenuRequested?.Invoke();
                }

                break;
            case InputEventKey { Pressed: true, Keycode: Key.Space }:
                _session.SetSpeed(_session.PlayView?.Speed == SimulationSpeed.Paused ? SimulationSpeed.X1 : SimulationSpeed.Paused);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Equal or Key.KpAdd or Key.Plus }:
                _session.SetSpeed(_session.PlayView?.Speed switch { SimulationSpeed.Paused => SimulationSpeed.X1, SimulationSpeed.X1 => SimulationSpeed.X2, _ => SimulationSpeed.X4 });
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Minus or Key.KpSubtract }:
                _session.SetSpeed(_session.PlayView?.Speed switch { SimulationSpeed.X4 => SimulationSpeed.X2, SimulationSpeed.X2 => SimulationSpeed.X1, _ => SimulationSpeed.Paused });
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4 or Key.Key5 or Key.Key6 } f:
                OpenWindow(WindowNames[(int)(f.Keycode - Key.Key1)]);
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.P }:
                OpenWindow("Units"); // patrols are listed first there
                break;
            case InputEventKey { Pressed: true, Echo: false, Keycode: Key.F5 or Key.F6 or Key.F7 or Key.F8 or Key.F9 } dk when _session.PlayView?.Debug == true:
                // Test scenarios only: F5 goods, F6 finish research, F7 reveal the map, F8 more people, F9 next era.
                _session.Play(PlayCommandKind.Debug, (int)(dk.Keycode - Key.F5));
                break;
            case InputEventKey { Pressed: true, Keycode: Key.G }:
                ToggleRoutes();
                break;
            case InputEventKey { Pressed: true, Keycode: Key.H }:
                _help.Visible = !_help.Visible;
                break;
            case InputEventKey { Pressed: true, Keycode: Key.L }:
                ToggleChronicle();
                break;
            case InputEventKey { Pressed: true, Echo: false } k when ActionKey(k.Keycode):
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    // Why the player cannot start on a hex (the same rules as the server: land, not mountains, away from the taken starts).
    private string? StartProblem((int Col, int Row) h)
    {
        if (_session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w) || _session.Content is not { } content || _session.PlayView is not { } v)
        {
            return null;
        }

        int i = w.Index(h.Col, h.Row);
        if (content.Biomes[w.Biome[i]].IsWater)
        {
            return "Water: a people starts on land";
        }

        if (w.Relief[i] == 2)
        {
            return "Mountains: not here";
        }

        if (v.TakenStarts is { } taken)
        {
            Vector2 at = HexLayout.Center(h.Col, h.Row);
            for (int k = 0; k + 1 < taken.Length; k += 2)
            {
                Vector2 d = HexLayout.Center(taken[k], taken[k + 1]) - at;
                float wrap = _map!.Period.X;
                d.X = wrap > 0 ? Math.Abs(((d.X % wrap) + (wrap * 1.5f)) % wrap - (wrap / 2)) : d.X;
                if (d.Length() < v.BlockRadius * HexLayout.ColumnStep)
                {
                    return "Too close to another people";
                }
            }
        }

        return null;
    }

    private void Click()
    {
        if (MouseCell() is not { } cell || _session is null)
        {
            return;
        }

        if (_session.PlayView is { Picking: true } pick)
        {
            if (pick.MyPick)
            {
                _session.Play(PlayCommandKind.PickStart, 0, cell.Col, cell.Row);
            }
            else
            {
                Toast($"{pick.WhoPicks} are choosing now; wait for your turn.");
            }

            return;
        }

        if (_placing > 0)
        {
            if (_overlay?.IsPlace(cell.Col, cell.Row) == true)
            {
                _session.Play(PlayCommandKind.Build, _placing, cell.Col, cell.Row);
                if (!Input.IsKeyPressed(Key.Shift))
                {
                    StopPlacing();
                }
                else
                {
                    _session.AskPlaces(_placing);
                }
            }
            else
            {
                Toast(PlaceProblem());
            }

            return;
        }

        bool add = Input.IsKeyPressed(Key.Shift) && _overlay is not null;
        if (!add)
        {
            _overlay?.SelectedUnits.Clear();
        }

        _selected = cell;
        int before = _unit;
        _unit = -1;
        if (_session.PlayView is { } view)
        {
            foreach (PlayUnitView u in view.Units)
            {
                if (u.Civ == view.Civ && u.State != 0 && u.Col == cell.Col && u.Row == cell.Row)
                {
                    // With Shift every unit on the hex joins the selection.
                    if (add)
                    {
                        _overlay!.SelectedUnits.Add(u.Id);
                    }

                    _unit = _unit < 0 ? u.Id : _unit;
                }
            }
        }

        if (add && before >= 0)
        {
            _overlay!.SelectedUnits.Add(before);
        }

        if (_overlay is not null)
        {
            _overlay.SelectedUnit = _unit;
            _overlay.Restatic();
        }

        UpdateUi();
    }

    private bool IsArmy(int unit)
    {
        if (_session?.PlayView is not { } v || _session.PlayContent is not { } c)
        {
            return false;
        }

        foreach (PlayUnitView u in v.Units)
        {
            if (u.Id == unit)
            {
                return c.Units[u.Kind].Army;
            }
        }

        return false;
    }

    private bool EnemyAt(int col, int row)
    {
        if (_session?.PlayView is not { } v)
        {
            return false;
        }

        foreach (PlayUnitView u in v.Units)
        {
            if (u.Civ != v.Civ && u.Col == col && u.Row == row)
            {
                return true;
            }
        }

        foreach (PlaySettlementView s in v.Settlements)
        {
            if (s.Civ != v.Civ && s.Col == col && s.Row == row)
            {
                return true;
            }
        }

        return false;
    }

    private string PlaceProblem()
    {
        if (_session?.PlayContent is not { } c || _placing <= 0)
        {
            return "";
        }

        PlayBuildingInfo t = c.Buildings[_placing];
        return string.IsNullOrEmpty(t.Needs) ? "Build on a free hex of your land." : $"{t.Name} needs {t.Needs}, on a free hex of your land.";
    }

    private void StartPlacing(int type)
    {
        if (_session is null)
        {
            return;
        }

        _placing = type;
        _overlay?.SetPlaces([]);
        _session.AskPlaces(type);
    }

    private void StopPlacing()
    {
        _placing = 0;
        _overlay?.SetPlaces(null);
        _overlay?.SetHover(null);
    }

    private (int Col, int Row)? MouseCell()
    {
        if (_session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w))
        {
            return null;
        }

        (int col, int row) = HexLayout.CellAt(GetGlobalMousePosition());
        if (row < 0 || row >= w.Height)
        {
            return null;
        }

        return (((col % w.Width) + w.Width) % w.Width, row);
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
        _camera.Zoom = new Vector2(z, z);
        _camera.Position += before - ScreenToWorld(screenPos);
    }

    private Vector2 ScreenToWorld(Vector2 screen) => GetViewport().GetCanvasTransform().AffineInverse() * screen;

    private Rect2 VisibleRect()
    {
        Vector2 size = GetViewportRect().Size / _camera.Zoom;
        return new Rect2(_camera.Position - (size / 2), size);
    }
}
