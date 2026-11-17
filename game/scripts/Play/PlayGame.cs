using System;
using System.Collections.Generic;
using System.Globalization;
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
    private const float MaxZoom = 2.5f;

    private readonly ulong _seed;
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
    private readonly HashSet<int> _hiddenWalk = [];
    private double _uiTimer;
    private Vector2? _dragFrom;

    private Label _resources = null!;
    private Label _clock = null!;
    private Label _side = null!;
    private VBoxContainer _sideButtons = null!;
    private PanelContainer _sidePanel = null!;
    private RichTextLabel _chronicleText = null!;
    private Label _toast = null!;
    private double _toastTime;
    private HBoxContainer _buildBar = null!;
    private string? _screenshot;
    private int _screenshotFrames = -1;
    private readonly List<(double At, Action Do)> _script = [];
    private double _time;

    /// <summary>Creates the screen; the game starts when the node enters the tree.</summary>
    public PlayGame(ulong seed, LayerStack layers, ArtLibrary art, string? dataDirectory)
    {
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
        _camera = new Camera2D { Zoom = new Vector2(0.9f, 0.9f) };
        AddChild(_camera);
        _camera.MakeCurrent();
        _host = PlayHost.Start(new PlayOptions(_seed), _dataDirectory);
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

            _chronicleText.Text = string.Join("\n", _chronicle.GetRange(Math.Max(0, _chronicle.Count - 8), Math.Min(8, _chronicle.Count)));
        };
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
            _bound = true;
            _session.SetSpeed(SimulationSpeed.X1);
            FillBuildBar(_session.PlayContent);
        }

        if (!_centred && _session.PlayView is { } v)
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
        if (_placing > 0)
        {
            _overlay.SetHover(MouseCell());
        }

        _overlay.SetSelected(_selected);
        if (_session.PlayContent is { } content)
        {
            _overlay.Show(content, _session.PlayView, _map.Period, visible);
        }

        _uiTimer -= delta;
        if (_uiTimer <= 0)
        {
            _uiTimer = 0.2;
            UpdateUi();
        }

        if (_toastTime > 0)
        {
            _toastTime -= delta;
            _toast.Visible = _toastTime > 0;
        }

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
            case InputEventMouseMotion motion when _dragFrom is { } from:
                _camera.Position -= (motion.Position - from) / _camera.Zoom.X;
                _dragFrom = motion.Position;
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                Click();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                if (_placing > 0)
                {
                    StopPlacing();
                }
                else if (_unit >= 0 && MouseCell() is { } target)
                {
                    _session.Play(PlayCommandKind.Move, _unit, target.Col, target.Row, _hiddenWalk.Contains(_unit));
                }

                break;
            case InputEventKey { Pressed: true, Keycode: Key.Escape }:
                if (_placing > 0)
                {
                    StopPlacing();
                }
                else
                {
                    MenuRequested?.Invoke();
                }

                break;
            case InputEventKey { Pressed: true, Keycode: Key.Space }:
                _session.SetSpeed(_session.PlayView?.Speed == SimulationSpeed.Paused ? SimulationSpeed.X1 : SimulationSpeed.Paused);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Key1 }:
                _session.SetSpeed(SimulationSpeed.X1);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Key2 }:
                _session.SetSpeed(SimulationSpeed.X2);
                break;
            case InputEventKey { Pressed: true, Keycode: Key.Key3 }:
                _session.SetSpeed(SimulationSpeed.X4);
                break;
            default:
                return;
        }

        GetViewport().SetInputAsHandled();
    }

    private void Click()
    {
        if (MouseCell() is not { } cell || _session is null)
        {
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

        _selected = cell;
        _unit = -1;
        if (_session.PlayView is { } view)
        {
            foreach (PlayUnitView u in view.Units)
            {
                if (u.Civ == view.Civ && u.State != 0 && u.Col == cell.Col && u.Row == cell.Row)
                {
                    _unit = u.Id;
                    break;
                }
            }
        }

        if (_overlay is not null)
        {
            _overlay.SelectedUnit = _unit;
        }

        UpdateUi();
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

    // ---- UI ------------------------------------------------------------------------------------------------------------

    private void BuildUi()
    {
        var ui = new CanvasLayer { Layer = 10 };
        AddChild(ui);

        var top = new PanelContainer { AnchorRight = 1, OffsetBottom = 34 };
        ui.AddChild(top);
        var topRow = new HBoxContainer();
        top.AddChild(topRow);
        _resources = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        topRow.AddChild(_resources);
        _clock = new Label { HorizontalAlignment = HorizontalAlignment.Center, CustomMinimumSize = new Vector2(260, 0) };
        topRow.AddChild(_clock);
        foreach ((string text, SimulationSpeed speed) in new[] { ("||", SimulationSpeed.Paused), ("x1", SimulationSpeed.X1), ("x2", SimulationSpeed.X2), ("x4", SimulationSpeed.X4) })
        {
            var b = new Button { Text = text, CustomMinimumSize = new Vector2(36, 0), FocusMode = Control.FocusModeEnum.None };
            b.Pressed += () => _session?.SetSpeed(speed);
            topRow.AddChild(b);
        }

        var menu = new Button { Text = "Menu", FocusMode = Control.FocusModeEnum.None };
        menu.Pressed += () => MenuRequested?.Invoke();
        topRow.AddChild(menu);

        _sidePanel = new PanelContainer { OffsetLeft = 8, OffsetTop = 44, OffsetRight = 300, OffsetBottom = 420 };
        ui.AddChild(_sidePanel);
        var sideBox = new VBoxContainer();
        _sidePanel.AddChild(sideBox);
        _side = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(280, 0) };
        sideBox.AddChild(_side);
        _sideButtons = new VBoxContainer();
        sideBox.AddChild(_sideButtons);

        var bottom = new PanelContainer { AnchorTop = 1, AnchorBottom = 1, AnchorRight = 1, OffsetTop = -52 };
        ui.AddChild(bottom);
        var scroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        bottom.AddChild(scroll);
        _buildBar = new HBoxContainer();
        scroll.AddChild(_buildBar);

        var chronicle = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -440, OffsetRight = -8, OffsetTop = -230, OffsetBottom = -60 };
        ui.AddChild(chronicle);
        _chronicleText = new RichTextLabel { ScrollFollowing = true, FitContent = false, CustomMinimumSize = new Vector2(420, 160) };
        chronicle.AddChild(_chronicleText);

        _toast = new Label { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -300, OffsetRight = 300, OffsetTop = 44, HorizontalAlignment = HorizontalAlignment.Center, Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _toast.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.45f));
        ui.AddChild(_toast);
    }

    private void FillBuildBar(PlayContentMessage content)
    {
        foreach (Node n in _buildBar.GetChildren())
        {
            n.QueueFree();
        }

        foreach (PlayBuildingInfo b in content.Buildings)
        {
            if (b.Id == 0)
            {
                continue;
            }

            var button = new Button { Text = b.Name, TooltipText = Describe(content, b), FocusMode = Control.FocusModeEnum.None };
            int id = b.Id;
            button.Pressed += () => StartPlacing(id);
            _buildBar.AddChild(button);
        }
    }

    private static string Describe(PlayContentMessage content, PlayBuildingInfo b)
    {
        var lines = new List<string> { b.Name, $"Costs {Amounts(content, b.Price)}, builds in {b.BuildSeconds} s" };
        if (b.Workers > 0)
        {
            lines.Add($"Jobs: {b.Workers}");
            lines.Add($"Each worker a minute: {Amounts(content, b.Produces)}" + (Any(b.Consumes) ? $" from {Amounts(content, b.Consumes)}" : ""));
        }

        if (b.Housing > 0)
        {
            lines.Add($"Houses {b.Housing} people");
        }

        if (b.Storage > 0)
        {
            lines.Add($"Stores {b.Storage / 100} more of every good");
        }

        if (!string.IsNullOrEmpty(b.Needs))
        {
            lines.Add($"Needs {b.Needs}");
        }

        return string.Join("\n", lines);
    }

    private static bool Any(long[] a)
    {
        foreach (long x in a)
        {
            if (x != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string Amounts(PlayContentMessage content, long[] a)
    {
        var parts = new List<string>();
        for (int g = 0; g < a.Length; g++)
        {
            if (a[g] != 0)
            {
                parts.Add($"{Num(a[g])} {content.Goods[g].Name.ToLowerInvariant()}");
            }
        }

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    private static string Num(long hundredths) =>
        hundredths % 100 == 0 ? (hundredths / 100).ToString(CultureInfo.InvariantCulture) : (hundredths / 100.0).ToString("0.#", CultureInfo.InvariantCulture);

    private string Clock()
    {
        long ticks = _session?.PlayView?.Tick ?? 0;
        long s = ticks / 10;
        return $"{s / 60}:{s % 60:00}";
    }

    private void UpdateUi()
    {
        if (_session?.PlayView is not { } v || _session.PlayContent is not { } c)
        {
            return;
        }

        var res = new List<string>();
        for (int g = 0; g < v.Stock.Length; g++)
        {
            long rate = v.RatePerMinute[g];
            string arrow = rate > 0 ? $"+{Num(rate)}" : rate < 0 ? $"-{Num(-rate)}" : "0";
            string full = v.Stock[g] >= v.Capacity[g] ? " FULL" : "";
            res.Add($"{c.Goods[g].Name} {v.Stock[g] / 100} ({arrow}/min){full}");
        }

        res.Add($"People {v.People}" + (v.Hungry ? " HUNGRY" : ""));
        _resources.Text = string.Join("   ", res);
        _resources.AddThemeColorOverride("font_color", v.Hungry ? new Color(1f, 0.5f, 0.4f) : PixelThemeText());
        string speed = v.Speed switch
        {
            SimulationSpeed.Paused => "paused",
            SimulationSpeed.X1 => "x1",
            SimulationSpeed.X2 => "x2",
            SimulationSpeed.X4 => "x4",
            _ => "max",
        };
        _clock.Text = $"Stone Age   {Clock()}   {speed}";
        UpdateSide(v, c);
    }

    private static Color PixelThemeText() => new(0.984f, 0.976f, 0.984f);

    private void UpdateSide(PlayViewMessage v, PlayContentMessage c)
    {
        foreach (Node n in _sideButtons.GetChildren())
        {
            n.QueueFree();
        }

        if (_unit >= 0)
        {
            foreach (PlayUnitView u in v.Units)
            {
                if (u.Id == _unit)
                {
                    UnitPanel(u, c);
                    return;
                }
            }

            _unit = -1;
        }

        if (_selected is not { } sel)
        {
            _side.Text = "Click a hex to see what is there.\nPick a building below, then a green hex to build it. Shift keeps building. Right click cancels.";
            return;
        }

        foreach (PlaySettlementView s in v.Settlements)
        {
            if (s.Col == sel.Col && s.Row == sel.Row)
            {
                PlayLevelInfo level = c.Levels[s.Level];
                string text = $"{s.Name}\n{level.Name}\nPeople {s.People} of {s.Housing} homes\nWithout work: {s.Idle}";
                if (s.Civ == v.Civ && s.Level + 1 < c.Levels.Count)
                {
                    PlayLevelInfo next = c.Levels[s.Level + 1];
                    text += $"\n\nBecome a {next.Name.ToLowerInvariant()} for {Amounts(c, next.UpgradePrice)}: land of {next.Radius} hexes around, {next.Housing} homes.";
                    AddButton($"Upgrade to {next.Name.ToLowerInvariant()}", () => _session!.Play(PlayCommandKind.Upgrade, s.Id));
                }

                if (s.Civ == v.Civ)
                {
                    foreach (PlayUnitInfo ut in c.Units)
                    {
                        int kind = ut.Id;
                        int sid = s.Id;
                        AddButton($"Train {ut.Name.ToLowerInvariant()} ({ut.People} people, {Amounts(c, ut.Price)})", () => _session!.Play(PlayCommandKind.Train, sid, b: kind));
                    }

                    foreach (PlayUnitView u in v.Units)
                    {
                        if (u.Civ == v.Civ && u.State == 0 && u.Col == s.Col && u.Row == s.Row)
                        {
                            text += $"\nTraining: {c.Units[u.Kind].Name.ToLowerInvariant()}";
                        }
                    }
                }

                _side.Text = text;
                return;
            }
        }

        foreach (PlayBuildingView b in v.Buildings)
        {
            if (b.Col != sel.Col || b.Row != sel.Row)
            {
                continue;
            }

            PlayBuildingInfo t = c.Buildings[b.Type];
            string state = b.State switch
            {
                0 => $"Being built: {b.ProgressPermille / 10}%",
                1 => $"Construction paused at {b.ProgressPermille / 10}%",
                3 => "Stopped",
                _ => b.Starved ? $"Waiting for {Amounts(c, t.Consumes)}" : t.Workers > 0 ? (b.Workers == 0 ? "No workers" : "Working") : "Standing",
            };
            string text = $"{t.Name}\n{state}";
            if (t.Workers > 0 && b.State == 2)
            {
                text += $"\nWorkers {b.Workers} of {t.Workers}";
                if (b.YieldPercent > 100)
                {
                    text += $"\nRich spot: +{b.YieldPercent - 100}%";
                }
            }

            _side.Text = text;
            if (b.Civ != v.Civ)
            {
                return;
            }

            int id = b.Id;
            if (b.State == 0)
            {
                AddButton("Pause construction", () => _session!.Play(PlayCommandKind.PauseConstruction, id, flag: true));
            }
            else if (b.State == 1)
            {
                AddButton("Resume (costs extra)", () => _session!.Play(PlayCommandKind.PauseConstruction, id, flag: false));
            }
            else if (b.State == 2 && t.Workers > 0)
            {
                AddButton("Stop work", () => _session!.Play(PlayCommandKind.Stop, id, flag: true));
                AddButton(b.Priority ? "Normal priority" : "Workers here first", () => _session!.Play(PlayCommandKind.Priority, id, flag: !b.Priority));
            }
            else if (b.State == 3)
            {
                AddButton("Start work", () => _session!.Play(PlayCommandKind.Stop, id, flag: false));
            }

            AddButton("Tear down", () => _session!.Play(PlayCommandKind.Demolish, id));
            return;
        }

        _side.Text = HexText(sel);
    }

    private void UnitPanel(PlayUnitView u, PlayContentMessage c)
    {
        PlayUnitInfo t = c.Units[u.Kind];
        string state = u.State == 2 ? "Walking" : "Standing";
        string text = $"{t.Name}: {u.People} people\n{state}\n\nRight click a hex to send them there.";
        int id = u.Id;
        if (t.CanHide)
        {
            bool hidden = _hiddenWalk.Contains(id);
            text += hidden ? "\nThey walk hidden: slower, seen only up close." : "\nThey walk in the open: faster, seen from afar.";
            AddButton(hidden ? "Walk in the open" : "Walk hidden", () =>
            {
                if (!_hiddenWalk.Remove(id))
                {
                    _hiddenWalk.Add(id);
                }
            });
        }

        if (t.Founds && u.State == 1)
        {
            AddButton("Found a camp here", () => _session!.Play(PlayCommandKind.Found, id));
        }

        _side.Text = text;
    }

    private string HexText((int Col, int Row) sel)
    {
        if (_session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w) || _session.Content is not { } content)
        {
            return "";
        }

        int i = w.Index(sel.Col, sel.Row);
        if (w.Visibility[i] == 0)
        {
            return "Unknown land.";
        }

        string biome = content.Biomes[w.Biome[i]].Name;
        string relief = content.Reliefs[w.Relief[i]];
        string text = $"{biome}, {relief.ToLowerInvariant()}";
        if (w.Deposit[i] != 0 && w.DepositDepth[i] == 0)
        {
            text += $"\n{content.Deposits[w.Deposit[i] - 1].Name}";
        }

        text += w.Owner[i] < 0 ? "\nNobody's land" : w.Owner[i] == _session.PlayView?.Civ ? "\nYour land" : "\nLand of another people";
        return text;
    }

    private void AddButton(string text, Action act)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += () =>
        {
            act();
            _uiTimer = 0.15;
        };
        _sideButtons.AddChild(b);
    }

    private void Toast(string text)
    {
        GD.Print($"toast: {text}");
        _toast.Text = text;
        _toast.Visible = true;
        _toastTime = 4;
    }

    // ---- camera ------------------------------------------------------------------------------------------------------

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
