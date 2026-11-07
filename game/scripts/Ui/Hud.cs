using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// Developer HUD (stage 5; GDD 12.4 layout, simplified). Top bar: time, speed, the followed civilization, layer
/// icons and panel switches. Left: tabs with the selected cell and the civilizations (table, statistics, details of
/// the followed one). Right: the chronicle (optionally only lines about the followed civilization). Bottom: turn
/// times. Every panel can be hidden, and the layer settings (opacity, order) open in their own window.
/// Built in code so it needs no art beyond the layer icons.
/// </summary>
public partial class Hud : CanvasLayer
{
    private static readonly (SimulationSpeed Speed, string Label, string Tip)[] Speeds =
    [
        (SimulationSpeed.Paused, "❚❚", "Pause (Space, 1)"), (SimulationSpeed.X1, "▶", "One turn every 2 s (2)"),
        (SimulationSpeed.X2, "▶▶", "One turn every second (3)"), (SimulationSpeed.X5, "▶▶▶", "Five turns every 2 s (4)"),
        (SimulationSpeed.Max, "»", "As fast as possible (5)"),
    ];

    private readonly Dictionary<SimulationSpeed, Button> _speedButtons = [];
    private readonly List<int> _followIds = [WelcomeMessage.NoCiv];
    private readonly List<ChronicleEntry> _chronicleLines = [];
    private Label _turn = null!;
    private Label _perf = null!;
    private OptionButton _follow = null!;
    private Button _track = null!;
    private Control _left = null!;
    private TabContainer _tabs = null!;
    private Control _right = null!;
    private PanelContainer _layerWindow = null!;
    private RichTextLabel _inspector = null!;
    private RichTextLabel _chronicle = null!;
    private CheckBox _chronicleFollowed = null!;
    private TurnChart _chart = null!;
    private CivilizationPanel _civs = null!;
    private int _followCount = -1;
    private Button _renameSettlement = null!;
    private SettlementView? _selectedSettlement;
    private ClientSession? _session;
    private Control _root = null!;
    private int _chronicleFilter = int.MinValue;

    /// <summary>Raised when the user picks a speed.</summary>
    public event Action<SimulationSpeed>? SpeedChosen;

    /// <summary>Raised when the user wants the start menu back (the game is stopped).</summary>
    public event Action? MenuRequested;

    /// <summary>Raised when the user picks a civilization to follow (<see cref="WelcomeMessage.NoCiv"/> = none).</summary>
    public event Action<int>? FollowChosen;

    /// <summary>Raised when the user asks to centre the map on the followed civilization.</summary>
    public event Action? GoToFollowed;

    /// <summary>Raised when panels are shown or hidden: (left, right, chart, layer window).</summary>
    public event Action<HudPanels>? PanelsChanged;

    /// <summary>The layer settings (opacity and order), to be set before the HUD enters the tree.</summary>
    public LayerPanel? Layers { get; set; }

    /// <summary>The layer icon bar, to be set before the HUD enters the tree.</summary>
    public LayerBar? LayerIcons { get; set; }

    /// <summary>Which panels start open.</summary>
    public HudPanels Panels { get; set; } = new();

    /// <summary>True while the camera should keep the followed civilization in view.</summary>
    public bool Tracking => _track.ButtonPressed;

    /// <inheritdoc />
    public override void _Ready()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = PixelTheme.Current };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);
        _root = root;

        // Top bar.
        var top = new PanelContainer { ThemeTypeVariation = "TopBar" };
        top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        root.AddChild(top);
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);
        top.AddChild(bar);
        _turn = new Label { Text = "Connecting...", CustomMinimumSize = new Vector2(170, 0), VerticalAlignment = VerticalAlignment.Center };
        bar.AddChild(_turn);
        var group = new ButtonGroup();
        foreach ((SimulationSpeed speed, string label, string tip) in Speeds)
        {
            var b = new Button { Text = label, TooltipText = tip, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(40, 0) };
            SimulationSpeed sp = speed;
            b.Pressed += () => SpeedChosen?.Invoke(sp);
            bar.AddChild(b);
            _speedButtons[speed] = b;
        }

        bar.AddChild(new VSeparator());
        _follow = new OptionButton { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(160, 0), TooltipText = "Follow a civilization: its panel, statistics and fog of war (F = next)" };
        _follow.AddItem("follow: nobody");
        _follow.ItemSelected += i => FollowChosen?.Invoke(_followIds[(int)i]);
        bar.AddChild(_follow);
        var go = new Button { Text = "Go", FocusMode = Control.FocusModeEnum.None, TooltipText = "Centre the map on its largest settlement (G)" };
        go.Pressed += () => GoToFollowed?.Invoke();
        bar.AddChild(go);
        _track = new Button { Text = "Track", ToggleMode = true, FocusMode = Control.FocusModeEnum.None, TooltipText = "Keep the camera on it while it moves (T)" };
        bar.AddChild(_track);
        bar.AddChild(new VSeparator());
        if (LayerIcons is not null)
        {
            bar.AddChild(LayerIcons);
            LayerIcons.SettingsPressed += () => SetPanel(_layerWindow, !_layerWindow.Visible);
        }

        bar.AddChild(new VSeparator());
        bar.AddChild(PanelToggle("Info", "Cell and civilization panel (I)", () => _left));
        bar.AddChild(PanelToggle("Log", "Chronicle (L)", () => _right));
        bar.AddChild(PanelToggle("Chart", "Turn times (C)", () => _chart));
        var menu = new Button { Text = "Menu", FocusMode = Control.FocusModeEnum.None, TooltipText = "Stop this game and go back to the start menu (Esc)" };
        menu.Pressed += () => MenuRequested?.Invoke();
        bar.AddChild(menu);
        _perf = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, ClipText = true };
        bar.AddChild(_perf);

        // Left: cell and civilization tabs.
        var left = new PanelContainer { CustomMinimumSize = new Vector2(LeftWidth, 0), Visible = Panels.Left };
        left.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        left.OffsetTop = TopHeight;
        left.OffsetBottom = 0;
        root.AddChild(left);
        _left = left;
        _tabs = new TabContainer();
        left.AddChild(_tabs);
        var cell = new VBoxContainer { Name = "Cell" };
        _tabs.AddChild(cell);
        _renameSettlement = new Button { Text = "Rename settlement", Visible = false, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        _renameSettlement.Pressed += () =>
        {
            if (_selectedSettlement is { } st)
            {
                OpenRename(NameTarget.Settlement, st.Id, st.Owner, st.Name);
            }
        };
        cell.AddChild(_renameSettlement);
        _inspector = new RichTextLabel { BbcodeEnabled = true, Text = "Click a hex to inspect it.", SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        cell.AddChild(_inspector);
        _civs = new CivilizationPanel { Name = "Civilizations" };
        _civs.FollowChosen += civ => FollowChosen?.Invoke(civ);
        _civs.RenameRequested += (civ, name) => OpenRename(NameTarget.Civilization, civ, civ, name);
        _tabs.AddChild(_civs);

        // Right: chronicle.
        var right = new PanelContainer { Visible = Panels.Right };
        right.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        right.OffsetLeft = -ChronicleWidth;
        right.OffsetTop = -ChronicleHeight;
        right.OffsetRight = 0;
        right.OffsetBottom = 0;
        root.AddChild(right);
        _right = right;
        var box = new VBoxContainer();
        right.AddChild(box);
        var head = new HBoxContainer();
        box.AddChild(head);
        head.AddChild(new Label { Text = "Chronicle", ThemeTypeVariation = "Heading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var grow = new Button { Text = "↕", FocusMode = Control.FocusModeEnum.None, TooltipText = "Taller or shorter chronicle" };
        grow.Pressed += () => right.OffsetTop = right.OffsetTop < -ChronicleHeight - 1 ? -ChronicleHeight : TopHeight - GetViewport().GetVisibleRect().Size.Y;
        head.AddChild(grow);
        _chronicleFollowed = new CheckBox { Text = "followed", FocusMode = Control.FocusModeEnum.None, TooltipText = "Show only lines about the followed civilization" };
        _chronicleFollowed.Toggled += _ => _chronicleFilter = int.MinValue;
        head.AddChild(_chronicleFollowed);
        _chronicle = new RichTextLabel { ScrollFollowing = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill, BbcodeEnabled = true };
        box.AddChild(_chronicle);

        // Bottom: turn times, between the side panels.
        _chart = new TurnChart { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = Panels.Chart };
        _chart.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _chart.OffsetTop = -72;
        root.AddChild(_chart);

        // Layer settings window (opacity and order), closed unless the user opens it.
        _layerWindow = new PanelContainer { Visible = Panels.LayerWindow, CustomMinimumSize = new Vector2(420, 0) };
        _layerWindow.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _layerWindow.OffsetLeft = -440;
        _layerWindow.OffsetTop = TopHeight + 4;
        _layerWindow.OffsetRight = -20;
        root.AddChild(_layerWindow);
        var wbox = new VBoxContainer();
        _layerWindow.AddChild(wbox);
        var whead = new HBoxContainer();
        wbox.AddChild(whead);
        whead.AddChild(new Label { Text = "Layer settings", ThemeTypeVariation = "Heading", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var close = new Button { Text = "✕", Flat = true, FocusMode = Control.FocusModeEnum.None };
        close.Pressed += () => SetPanel(_layerWindow, false);
        whead.AddChild(close);
        if (Layers is not null)
        {
            wbox.AddChild(Layers);
        }

        foreach ((Button b, Func<Control> p) in _toggles)
        {
            b.SetPressedNoSignal(p().Visible);
        }

        LayoutChart();
    }

    /// <summary>Shows or hides the layer settings window (Tab).</summary>
    public void ToggleLayerPanel() => SetPanel(_layerWindow, !_layerWindow.Visible);

    /// <summary>Shows or hides a panel by key: I info, L log, C chart.</summary>
    public void TogglePanel(char key)
    {
        switch (key)
        {
            case 'I':
                SetPanel(_left, !_left.Visible);
                break;
            case 'L':
                SetPanel(_right, !_right.Visible);
                break;
            case 'C':
                SetPanel(_chart, !_chart.Visible);
                break;
        }
    }

    /// <summary>Switches camera tracking (T).</summary>
    public void ToggleTracking() => _track.ButtonPressed = !_track.ButtonPressed;

    /// <summary>Opens the civilization tab (after following one).</summary>
    public void ShowCivilizationTab()
    {
        _tabs.CurrentTab = 1;
        SetPanel(_left, true);
    }

    /// <summary>Opens the cell tab (after clicking a hex).</summary>
    public void ShowCellTab() => _tabs.CurrentTab = 0;

    /// <summary>Appends chronicle lines.</summary>
    public void AddChronicle(IReadOnlyList<ChronicleEntry> lines)
    {
        _chronicleLines.AddRange(lines);
        if (_chronicleLines.Count > 2000)
        {
            _chronicleLines.RemoveRange(0, _chronicleLines.Count - 2000);
        }

        foreach (ChronicleEntry e in lines)
        {
            if (_chronicleFilter == int.MinValue || Matches(e))
            {
                _chronicle.AppendText(Line(e));
            }
        }
    }

    /// <summary>Refreshes the top bar, the chart, the inspector and the civilization panel.</summary>
    public void UpdateStatus(ClientSession session, MapView map, double fps)
    {
        GameViewMessage? view = session.View;
        if (view is not null)
        {
            _turn.Text = string.Create(CultureInfo.InvariantCulture, $"Turn {view.Turn}  ·  Year {view.Month / 12:N0}");
            if (_speedButtons.TryGetValue(view.Speed, out Button? b) && !b.ButtonPressed)
            {
                b.SetPressedNoSignal(true);
            }

            UpdateFollowList(view, session.WatchedCiv);
        }

        string error = session.LastError is null ? string.Empty : "   ! " + session.LastError;
        ClientWorld? w = map.World;
        _perf.Text = string.Create(CultureInfo.InvariantCulture, $"{fps:F0} FPS{error}");
        _perf.TooltipText = w is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $"{w.Name} {w.Width}×{w.Height}");
        if (_chart.Visible)
        {
            _chart.SetValues(session.TurnMilliseconds);
        }

        _session = session;
        if (_left.Visible)
        {
            _inspector.Text = Inspect(session, map);
            _civs.UpdateFrom(session);
            _selectedSettlement = null;
            if (map.Selected is { } sel && map.World is { } world && view?.Settlements is { } settlements)
            {
                foreach (SettlementView st in settlements)
                {
                    if (st.World == world.Id && st.Col == sel.Col && st.Row == sel.Row)
                    {
                        _selectedSettlement = st;
                    }
                }
            }

            _renameSettlement.Visible = _selectedSettlement is not null;
        }

        // The chronicle filter follows the followed civilization.
        int filter = _chronicleFollowed.ButtonPressed ? session.WatchedCiv : int.MinValue;
        if (filter != _chronicleFilter)
        {
            _chronicleFilter = filter;
            _followedName = filter == int.MinValue ? null : CivilizationPanel.CivName(session, filter);
            _chronicle.Clear();
            foreach (ChronicleEntry e in _chronicleLines)
            {
                if (filter == int.MinValue || Matches(e))
                {
                    _chronicle.AppendText(Line(e));
                }
            }
        }
    }

    private string? _followedName;

    /// <summary>Opens the rename dialog for the followed civilization (test argument <c>--rename-dialog</c>).</summary>
    public void RenameFollowed()
    {
        if (_session?.CivDetails is { } d)
        {
            OpenRename(NameTarget.Civilization, d.CivId, d.CivId, d.Name);
        }
    }

    // Opens the rename dialog (GDD 5.6) in the middle of the screen; one at a time.
    private void OpenRename(NameTarget target, int id, int owner, string current)
    {
        if (_session is null)
        {
            return;
        }

        foreach (Node child in _root.GetChildren())
        {
            if (child is RenameDialog old)
            {
                old.QueueFree();
            }
        }

        _root.AddChild(new RenameDialog(_session, target, id, owner, current));
    }

    private bool Matches(ChronicleEntry e) => _followedName is null || e.Text.Contains(_followedName, StringComparison.Ordinal);

    private static string Line(ChronicleEntry e) => string.Create(CultureInfo.InvariantCulture, $"[color=gray][{e.Turn}][/color] {e.Text}\n");

    private void UpdateFollowList(GameViewMessage view, int watched)
    {
        if (view.Civilizations.Count != _followCount)
        {
            _followCount = view.Civilizations.Count;
            _follow.Clear();
            _followIds.Clear();
            _follow.AddItem("follow: nobody");
            _followIds.Add(WelcomeMessage.NoCiv);
            foreach (CivilizationView c in view.Civilizations)
            {
                _follow.AddItem(c.Name);
                _followIds.Add(c.Id);
            }
        }

        int index = Math.Max(0, _followIds.IndexOf(watched));
        if (_follow.Selected != index)
        {
            _follow.Select(index);
        }
    }

    private Button PanelToggle(string text, string tip, Func<Control> panel)
    {
        var b = new Button { Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
        b.Toggled += on => SetPanel(panel(), on);
        _toggles.Add((b, panel));
        return b;
    }

    private readonly List<(Button Button, Func<Control> Panel)> _toggles = [];

    private void SetPanel(Control panel, bool on)
    {
        panel.Visible = on;
        foreach ((Button b, Func<Control> p) in _toggles)
        {
            if (ReferenceEquals(p(), panel))
            {
                b.SetPressedNoSignal(on);
            }
        }

        LayoutChart();
        PanelsChanged?.Invoke(new HudPanels { Left = _left.Visible, Right = _right.Visible, Chart = _chart.Visible, LayerWindow = _layerWindow.Visible });
    }

    // The chart sits between the side panels and the side panels stop above it.
    private void LayoutChart()
    {
        _chart.OffsetLeft = _left.Visible ? LeftWidth : 0;
        _chart.OffsetRight = _right.Visible ? -ChronicleWidth : 0;
    }

    private const float LeftWidth = 420;
    private const float TopHeight = 52;
    private const float ChronicleWidth = 400;
    private const float ChronicleHeight = 230;

    private static string Inspect(ClientSession session, MapView map)
    {
        if (map.Selected is not { } sel || map.World is not { } w)
        {
            return "Click a hex to inspect it.\n\n[color=gray]Mouse: left = select, right/middle drag = move, wheel = zoom.\nKeys: WASD/arrows move, +/- zoom, Space pause, 1-5 speed, Tab layer settings, F follow next, G go to, T track, I/L/C panels, Esc menu.[/color]";
        }

        int i = w.Index(sel.Col, sel.Row);
        ContentMessage? c = session.Content;
        var sb = new StringBuilder();
        CultureInfo inv = CultureInfo.InvariantCulture;
        sb.Append(inv, $"[b]Cell {sel.Col}, {sel.Row}[/b]\n");
        if (w.Visibility[i] == MapChunkMessage.Unknown)
        {
            sb.Append("Not discovered.");
            return sb.ToString();
        }

        string biome = c is not null && w.Biome[i] < c.Biomes.Count ? c.Biomes[w.Biome[i]].Name : w.Biome[i].ToString(inv);
        string relief = c is not null && w.Relief[i] < c.Reliefs.Count ? c.Reliefs[w.Relief[i]] : w.Relief[i].ToString(inv);
        string feature = c is not null && w.Feature[i] < c.Features.Count ? c.Features[w.Feature[i]] : w.Feature[i].ToString(inv);
        sb.Append(inv, $"Biome: {biome}\nRelief: {relief}\nFeature: {feature}\n");
        sb.Append(inv, $"Elevation: {w.Elevation[i]} (sea level 100){(OverlayColors.Depth(w, c, i) > 0 ? $", water depth about {OverlayColors.Depth(w, c, i) * 40} m" : string.Empty)}\nTemperature: {w.Temperature[i]} °C (summer {w.Temperature[i] + (w.Seasonality[i] / 2)}, winter {w.Temperature[i] - (w.Seasonality[i] / 2)})\nGrowing season: {w.GrowingSeason[i]}% of the year\n{(session.View?.LiveSeasons == true ? $"This season: {w.SeasonTemperature[i]} °C, plants grow {w.SeasonGrowth[i]}% of the turn\n" : string.Empty)}Humidity: {w.Humidity[i]}\nFertility: {w.Fertility[i]}\n");
        int d = w.Deposit[i];
        if (d != 0)
        {
            string name = c is not null && d <= c.Deposits.Count ? c.Deposits[d - 1].Name : d.ToString(inv);
            string depth = w.DepositDepth[i] switch { 0 => "surface", 1 => "shallow", _ => "deep" };
            sb.Append(inv, $"Deposit: {name}, {w.DepositRichness[i]} units, {depth}\n");
        }
        else
        {
            sb.Append("Deposit: none\n");
        }

        var rivers = new List<string>();
        string[] sides = ["N", "NE", "SE"];
        for (int e = 0; e < MapChunkMessage.RiverEdgesPerCell; e++)
        {
            int size = w.River[(i * MapChunkMessage.RiverEdgesPerCell) + e];
            if (size > 0)
            {
                rivers.Add(string.Create(inv, $"{sides[e]} (size {size})"));
            }
        }

        sb.Append("Rivers on own edges: ").Append(rivers.Count == 0 ? "none" : string.Join(", ", rivers)).Append('\n');
        sb.Append("Owner: ").Append(w.Owner[i] < 0 ? "none" : FindCiv(session, w.Owner[i])).Append('\n');
        AppendSettlement(sb, session, w.Id, sel.Col, sel.Row);
        AppendDetails(sb, session, w.Id, sel.Col, sel.Row);
        return sb.ToString();
    }

    // Settlement on the selected cell: owner, level and warehouse (only when the server sent it).
    private static void AppendSettlement(StringBuilder sb, ClientSession session, int world, int col, int row)
    {
        if (session.View?.Settlements is not { } settlements)
        {
            return;
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        ContentMessage? c = session.Content;
        foreach (SettlementView s in settlements)
        {
            if (s.World != world || s.Col != col || s.Row != row)
            {
                continue;
            }

            string level = c?.SettlementLevels is { } levels && s.Level < levels.Count ? levels[s.Level] : s.Level.ToString(inv);
            string state = s.Status switch { 1 => "Abandoned ", 2 => "Ruins of a ", _ => string.Empty };
            sb.Append(inv, $"\n[b]{s.Name}[/b]\n{state}{level} of {FindCiv(session, s.Owner)}\n");
            if (s.Status != 0 && session.View is { } now)
            {
                sb.Append(inv, $"Empty for {(now.Month - s.AbandonedMonth) / 12} years\n");
            }

            if (s.ClaimCiv >= 0 && session.View is { } v && v.Month <= s.ClaimUntilMonth)
            {
                sb.Append(inv, $"Claimed by {FindCiv(session, s.ClaimCiv)} for {(s.ClaimUntilMonth - v.Month) / 12} more years\n");
            }

            if (s.Stock is null)
            {
                sb.Append("[color=gray]Warehouse not visible.[/color]\n");
                continue;
            }

            if (s.Population is { } people)
            {
                int headcount = 0;
                var mix = new List<string>();
                for (int k = 0; k < people.Length; k++)
                {
                    headcount += people[k];
                    string stratum = c?.Strata is { } strata && k < strata.Count ? strata[k] : k.ToString(inv);
                    mix.Add(string.Create(inv, $"{stratum} {people[k]}"));
                }

                int fed = s.FoodNeeded <= 0 ? 100 : (int)Math.Min(100, s.FoodEaten * 100 / s.FoodNeeded);
                sb.Append(inv, $"People: {headcount} ({string.Join(", ", mix)})\n");
                sb.Append(inv, $"Health: {s.Health}   Mood: {s.Mood}   Fed: {fed}%\n");
                sb.Append(inv, $"Food last turn: gathered {s.FoodGathered / 100.0:F0}, needed {s.FoodNeeded / 100.0:F0}\n");
            }

            long total = 0;
            var lines = new StringBuilder();
            for (int g = 0; g < s.Stock.Length; g++)
            {
                if (s.Stock[g] == 0)
                {
                    continue;
                }

                total += s.Stock[g];
                string name = c?.Goods is { } goods && g < goods.Count ? goods[g].Name : g.ToString(inv);
                lines.Append(inv, $"  {name}: {s.Stock[g] / 100.0:F2}\n");
            }

            sb.Append(inv, $"Warehouse ({total / 100.0:F2} units):\n");
            sb.Append(lines.Length == 0 ? "  empty\n" : lines.ToString());
        }
    }

    // Observer layers on the selected cell: a field, a wandering camp, active events.
    private static void AppendDetails(StringBuilder sb, ClientSession session, int world, int col, int row)
    {
        if (session.Details is not { } d)
        {
            return;
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        foreach (FieldView f in d.Fields)
        {
            if (f.World == world && f.Col == col && f.Row == row)
            {
                sb.Append(inv, $"Field of {FindCiv(session, f.Owner)}, {f.WorkedPercent}% worked\n");
            }
        }

        foreach (JourneyView j in d.Journeys)
        {
            if (j.World == world && j.Col == col && j.Row == row)
            {
                sb.Append(inv, $"Wandering camp ({j.People} people) going to {j.TargetCol}, {j.TargetRow}\n");
            }
        }

        foreach (ArmyView a in d.Armies ?? [])
        {
            if (world == 0 && a.Col == col && a.Row == row)
            {
                string phase = a.Phase switch { 1 => "going home", 2 => "gathering", _ => "marching" };
                sb.Append(inv, $"Army of {FindCiv(session, a.Owner)} against {FindCiv(session, a.Enemy)}: {a.Warriors} warriors {phase}, arms {a.ArmsPercent}%, morale {a.Morale}%, food for {a.FoodMonths} months\n");
            }

            if (world == 0 && a.Carriers > 0 && a.ConvoyCol == col && a.ConvoyRow == row)
            {
                sb.Append(inv, $"{a.Carriers} carriers of {FindCiv(session, a.Owner)} bringing food to their army\n");
            }
        }

        foreach (CellEventView e in d.Events)
        {
            if (e.World == world && e.Col == col && e.Row == row && session.View is { } v)
            {
                sb.Append(inv, $"Event: {e.Effect.Replace('_', ' ')} {e.Strength}, {Math.Max(0, (e.EndMonth - v.Month) / 12)} more years\n");
            }
        }
    }

    private static string FindCiv(ClientSession session, int id)
    {
        if (session.View is { } v)
        {
            foreach (CivilizationView c in v.Civilizations)
            {
                if (c.Id == id)
                {
                    return c.Name;
                }
            }
        }

        return id.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>Which HUD panels are open (saved with the viewer settings).</summary>
public sealed class HudPanels
{
    /// <summary>Cell and civilization panel.</summary>
    public bool Left { get; set; } = true;

    /// <summary>Chronicle.</summary>
    public bool Right { get; set; } = true;

    /// <summary>Turn-time chart.</summary>
    public bool Chart { get; set; } = true;

    /// <summary>Layer settings window.</summary>
    public bool LayerWindow { get; set; }
}
