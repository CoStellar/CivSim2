using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Game.Ui;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The screen layout (Patryk's decision, docs/projekt-gry.md section 11): resources at the top left, era and years in the
/// top middle, the real time of the game and the speed at the top right, the advisor under it, alerts on the right, the
/// chronicle at the bottom right (opens wider), the side panel on the left and the action bar at the bottom. Windows
/// (research, settlements, peoples…) open in the middle and close with Esc.
/// </summary>
public partial class PlayGame
{
    private static readonly string[] WindowNames = ["Settlements", "Research", "Peoples", "Thinkers", "Summary", "Units"];

    // Decorative years at the start of each era and at the end of the last (only for the screen, never for the rules).
    private static readonly int[] EraYears = [-10000, -6000, -3000, -1200, 500, 1500];

    private HFlowContainer _resBox = null!;
    private PanelContainer _top = null!;
    private Control _rightCol = null!;
    private float _topBottom;
    private Label _eraLabel = null!;
    private Label _yearLabel = null!;
    private VBoxContainer _eraBarBox = null!;
    private Label _clockLabel = null!;
    private readonly List<(Button Button, SimulationSpeed Speed)> _speedButtons = [];
    private Button _researchChip = null!;
    private Button _patrolsButton = null!;
    private PanelContainer _advisorPanel = null!;
    private Label _advisorName = null!;
    private Label _advisorText = null!;
    private Label _eventLabel = null!;
    private VBoxContainer _alertsBox = null!;
    private RichTextLabel _chronicleText = null!;
    private PanelContainer _chroniclePanel = null!;
    private bool _chronicleOpen;
    private Button _chronicleToggle = null!;
    private PanelContainer _inspector = null!;
    private VBoxContainer _inspectorBox = null!;
    private ScrollContainer _inspectorScroll = null!;
    private string _inspectorSig = "";
    private PanelContainer _banner = null!;
    private Label _bannerText = null!;
    private PanelContainer _window = null!;
    private Label _windowTitle = null!;
    private VBoxContainer _windowBox = null!;
    private PanelContainer _help = null!;
    private PanelContainer _toastPanel = null!;
    private Label _toast = null!;
    private double _toastTime;
    private string _alertSig = "";
    private string _alertLayout = "";
    private string _resSig = "";

    private CanvasLayer? _uiLayer;

    // A22 (tests 2026-11-20): a screen at the end of the game, shown once; the game goes on behind it if the player wants.
    private PanelContainer _endPanel = null!;
    private PanelContainer _tutorialPanel = null!;
    private VBoxContainer _tutorialBox = null!;
    private string _tutorialSig = "";
    private Label _hexTip = null!;
    private PanelContainer _hexTipPanel = null!;
    private Label _endTitle = null!;
    private Label _endText = null!;
    private bool _endShown;

    // The chronicle sits above the height of the action bar, so a wide bar never covers it.
    private static int ChronicleBottom => -((3 * PixelTheme.FontSize) + 104);

    // Builds the screen again (after the font changed), keeping what is open.
    private void RebuildUi()
    {
        bool help = _help.Visible;
        _uiLayer?.QueueFree();
        _speedButtons.Clear();
        _resSig = _alertSig = _inspectorSig = _barSig = _bigSignature = "";
        BuildUi();
        _help.Visible = help;
        if (_chronicleOpen)
        {
            _chronicleOpen = false;
            ToggleChronicle();
        }

        RefreshChronicle();
        UpdateUi();
    }

    private void BuildUi()
    {
        var ui = new CanvasLayer { Layer = 10 };
        _uiLayer = ui;
        AddChild(ui);
        int topH = Math.Max(48, (PixelTheme.FontSize * 2) + 8);
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = PixelTheme.Current };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ui.AddChild(root);

        // ---- Top bar: resources | era and years | time, speed, windows ----
        var top = new PanelContainer { AnchorRight = 1, OffsetBottom = topH };
        top.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.11f, 0.09f, 0.08f, 0.94f), new Color(0.35f, 0.28f, 0.2f), 0, 2));
        root.AddChild(top);
        _top = top;
        _topBottom = topH;
        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 10);
        top.AddChild(topRow);

        // A15 (tests 2026-11-20): the goods stop before the era in the middle and wrap onto more rows; the bar grows with them
        // and what sits under it moves down (UpdateUnderTop).
        _resBox = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, CustomMinimumSize = new Vector2(Math.Max(160, (screen.X / 2) - UiKit.W(240) - 16), 0) };
        _resBox.AddThemeConstantOverride("h_separation", 12);
        _resBox.AddThemeConstantOverride("v_separation", 2);
        topRow.AddChild(_resBox);
        topRow.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        var right = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin }; // stays one row when the goods take two
        right.AddThemeConstantOverride("separation", 4);
        topRow.AddChild(right);
        _clockLabel = UiKit.Label("0:00", 16, UiKit.Text);
        _clockLabel.TooltipText = "Time of the game.";
        _clockLabel.CustomMinimumSize = new Vector2(64, 0);
        _clockLabel.HorizontalAlignment = HorizontalAlignment.Center;
        right.AddChild(_clockLabel);
        var group = new ButtonGroup();
        foreach ((string text, SimulationSpeed speed, string tip) in new[]
                 {
                     ("||", SimulationSpeed.Paused, "Pause (Space)"), ("1x", SimulationSpeed.X1, "Normal speed (- slower, + faster)"),
                     ("2x", SimulationSpeed.X2, "Double speed (+ / -)"), ("4x", SimulationSpeed.X4, "Four times (+ / -)"),
                 })
        {
            var b = new Button { Text = text, ToggleMode = true, ButtonGroup = group, FocusMode = Control.FocusModeEnum.None, TooltipText = tip, CustomMinimumSize = new Vector2(38, 0) };
            b.Pressed += () => _session?.SetSpeed(speed);
            right.AddChild(b);
            _speedButtons.Add((b, speed));
        }

        right.AddChild(new VSeparator());
        _patrolsButton = UiKit.Button("Patrols", () => OpenWindow("Units"), "Our patrols and units: stop one or all of them (P)");
        right.AddChild(_patrolsButton);
        right.AddChild(UiKit.Button("Routes", ToggleRoutes, "Show or hide all our routes, patrols and marches (G)"));
        right.AddChild(UiKit.Button("?", () => _help.Visible = !_help.Visible, "The keys and the font (H)"));
        right.AddChild(UiKit.Button("Manual", () => ToggleManual(), "How to play: the goal, the eras, war and peace, every key (F1)"));
        right.AddChild(UiKit.Button("Tutorial", () => OpenWindow("Tutorial"), "First steps: what to do next, and the task list on screen (T)"));
        right.AddChild(UiKit.Button("Save", SaveNow, "Save the game (it also saves itself every 5 minutes as 'autosave'); load it from the start menu (F5)"));
        right.AddChild(UiKit.Button("Menu", () => MenuRequested?.Invoke(), "Back to the start menu"));

        // Era and years in the middle of the top bar, over the bar.
        var centre = new VBoxContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -UiKit.W(240), OffsetRight = UiKit.W(240), OffsetTop = 0, MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.AddThemeConstantOverride("separation", 0);
        root.AddChild(centre);
        _eraLabel = UiKit.Label("", 24, UiKit.Heading);
        _eraLabel.HorizontalAlignment = HorizontalAlignment.Center;
        centre.AddChild(_eraLabel);
        _eraBarBox = new VBoxContainer();
        centre.AddChild(_eraBarBox);
        _researchChip = UiKit.Button("Choose research", () => OpenWindow("Research"), "What we are researching. Click to open the research tree (2).");
        _researchChip.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _researchChip.CustomMinimumSize = new Vector2(UiKit.W(260), 0);
        centre.AddChild(_researchChip);
        _yearLabel = UiKit.Label("", 12, UiKit.Muted);
        _yearLabel.HorizontalAlignment = HorizontalAlignment.Center;

        // ---- Advisor (top right) and alerts under it ----
        // The advisor and, under it, the alerts: one column at the right.
        var rightCol = new VBoxContainer { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -UiKit.W(380), OffsetRight = -8, OffsetTop = topH + 8, MouseFilter = Control.MouseFilterEnum.Ignore };
        rightCol.AddThemeConstantOverride("separation", 8);
        root.AddChild(rightCol);
        _rightCol = rightCol;
        _advisorPanel = new PanelContainer();
        rightCol.AddChild(_advisorPanel);
        var adv = new HBoxContainer();
        adv.AddThemeConstantOverride("separation", 8);
        _advisorPanel.AddChild(adv);
        adv.AddChild(new Glyph(new Color("8a6a40"), "Ad", 40));
        var advText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        adv.AddChild(advText);
        _advisorName = UiKit.Label("", 15, UiKit.Heading);
        advText.AddChild(_advisorName);
        _eventLabel = UiKit.Label("", 14, UiKit.Bad, wrap: true);
        advText.AddChild(_eventLabel);
        _advisorText = UiKit.Label("", 14, UiKit.Text, wrap: true);
        advText.AddChild(_advisorText);

        _alertsBox = new VBoxContainer();
        _alertsBox.AddThemeConstantOverride("separation", 4);
        rightCol.AddChild(_alertsBox);

        // ---- Chronicle (bottom right, opens wider) ----
        _chroniclePanel = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -UiKit.W(440), OffsetRight = -8, OffsetTop = ChronicleBottom - 110, OffsetBottom = ChronicleBottom };
        root.AddChild(_chroniclePanel);
        var chron = new VBoxContainer();
        _chroniclePanel.AddChild(chron);
        var chronHead = new HBoxContainer();
        chron.AddChild(chronHead);
        Label chronTitle = UiKit.Label("CHRONICLE", 13, UiKit.Heading);
        chronTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        chronHead.AddChild(chronTitle);
        _chronicleToggle = UiKit.Button("more", ToggleChronicle, "Show more of the chronicle (L)");
        chronHead.AddChild(_chronicleToggle);
        _chronicleText = new RichTextLabel { ScrollFollowing = true, FitContent = false, SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _chronicleText.AddThemeFontSizeOverride("normal_font_size", PixelTheme.FontSize);
        chron.AddChild(_chronicleText);

        // ---- Tutorial task list (above the chronicle), E6 ----
        _tutorialPanel = new PanelContainer { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -UiKit.W(440), OffsetRight = -8, OffsetBottom = ChronicleBottom - 118, OffsetTop = ChronicleBottom - 118, GrowVertical = Control.GrowDirection.Begin, Visible = false };
        _tutorialPanel.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.12f, 0.1f, 0.06f, 0.95f), UiKit.Heading, 4, 2));
        root.AddChild(_tutorialPanel);
        _tutorialBox = new VBoxContainer();
        _tutorialPanel.AddChild(_tutorialBox);

        // ---- What lies under the mouse (E8) ----
        _hexTipPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _hexTipPanel.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.08f, 0.07f, 0.06f, 0.88f), new Color(0.4f, 0.33f, 0.24f), 3, 1));
        _hexTip = UiKit.Label("", 13, UiKit.Text);
        _hexTip.MouseFilter = Control.MouseFilterEnum.Ignore;
        _hexTipPanel.AddChild(_hexTip);
        root.AddChild(_hexTipPanel);

        // ---- Side panel (left) ----
        _inspector = new PanelContainer { OffsetLeft = 8, OffsetTop = topH + 8, OffsetRight = 8 + UiKit.W(332) };
        root.AddChild(_inspector);
        _inspectorScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(UiKit.W(320), 0) };
        _inspector.AddChild(_inspectorScroll);
        _inspectorBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _inspectorBox.AddThemeConstantOverride("separation", 6);
        _inspectorScroll.AddChild(_inspectorBox);

        // ---- Action bar (bottom) ----
        BuildActionBar(root);

        // ---- Placing banner, toast, windows, help ----
        _banner = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -UiKit.W(380), OffsetRight = UiKit.W(380), OffsetTop = topH + 70 + PixelTheme.FontSize, Visible = false };
        _banner.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.16f, 0.24f, 0.12f, 0.95f), new Color(0.5f, 0.75f, 0.35f), 6, 2));
        root.AddChild(_banner);
        _bannerText = UiKit.Label("", 15, UiKit.Text, wrap: true);
        _bannerText.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.AddChild(_bannerText);

        _toastPanel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -UiKit.W(320), OffsetRight = UiKit.W(320), OffsetTop = topH + 140, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _toastPanel.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.3f, 0.1f, 0.08f, 0.95f), UiKit.Bad, 6, 2));
        root.AddChild(_toastPanel);
        _toast = UiKit.Label("", 15, UiKit.Text, wrap: true);
        _toast.HorizontalAlignment = HorizontalAlignment.Center;
        _toastPanel.AddChild(_toast);

        int half = (int)Math.Min(UiKit.W(560), (screen.X / 2) - 16);
        _windowHalf = half;
        _window = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -half, OffsetRight = half, OffsetTop = -(screen.Y / 2) + topH + 8, OffsetBottom = (screen.Y / 2) - 150, Visible = false };
        root.AddChild(_window);
        var win = new VBoxContainer();
        _window.AddChild(win);
        var winHead = new HBoxContainer();
        win.AddChild(winHead);
        _windowTitle = UiKit.Label("", 22, UiKit.Heading);
        _windowTitle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        winHead.AddChild(_windowTitle);
        for (int i = 0; i < WindowNames.Length; i++)
        {
            string name = WindowNames[i];
            winHead.AddChild(UiKit.Button($"{i + 1} {name}", () => OpenWindow(name, toggle: false), $"key {i + 1}"));
        }

        winHead.AddChild(UiKit.Button("Close", () => OpenWindow(""), "Close (Esc)"));
        win.AddChild(new HSeparator());
        var winScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        win.AddChild(winScroll);
        _windowBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _windowBox.AddThemeConstantOverride("separation", 6);
        winScroll.AddChild(_windowBox);

        _endPanel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -UiKit.W(300), OffsetRight = UiKit.W(300), OffsetTop = -120, OffsetBottom = 120, Visible = false };
        _endPanel.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.12f, 0.1f, 0.08f, 0.98f), UiKit.Heading, 6, 3));
        root.AddChild(_endPanel);
        var end = new VBoxContainer();
        end.AddThemeConstantOverride("separation", 10);
        _endPanel.AddChild(end);
        _endTitle = UiKit.Label("", 24, UiKit.Heading);
        _endTitle.HorizontalAlignment = HorizontalAlignment.Center;
        end.AddChild(_endTitle);
        _endText = UiKit.Label("", 15, UiKit.Text, wrap: true);
        _endText.HorizontalAlignment = HorizontalAlignment.Center;
        _endText.CustomMinimumSize = new Vector2(UiKit.W(560), 0);
        end.AddChild(_endText);
        var endButtons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        endButtons.AddThemeConstantOverride("separation", 10);
        end.AddChild(endButtons);
        endButtons.AddChild(UiKit.Button("Summary", () =>
        {
            _endPanel.Visible = false;
            OpenWindow("Summary", toggle: false);
        }, "How the game went, people by people (5)"));
        endButtons.AddChild(UiKit.Button("Go on watching", () => _endPanel.Visible = false, "Close this and let the world go on"));
        endButtons.AddChild(UiKit.Button("Menu", () => MenuRequested?.Invoke(), "Back to the start menu"));
        int helpHalf = (int)Math.Min(UiKit.W(360), (screen.X / 2) - 16);
        _help = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -helpHalf, OffsetRight = helpHalf, OffsetTop = -(screen.Y / 2) + topH + 8, OffsetBottom = (screen.Y / 2) - 150, Visible = false };
        root.AddChild(_help);
        var helpScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _help.AddChild(helpScroll);
        var help = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        helpScroll.AddChild(help);
        help.AddChild(UiKit.Label("FONT", 18, UiKit.Heading));
        var fonts = new HFlowContainer();
        help.AddChild(fonts);
        foreach (FontChoice f in PixelTheme.Fonts)
        {
            string key = f.Key;
            var b = new Button { Text = f.Name, ToggleMode = true, ButtonPressed = PixelTheme.FontKey == key, FocusMode = Control.FocusModeEnum.None, TooltipText = "Use this font for the whole screen." };
            b.Pressed += () =>
            {
                PixelTheme.UseFont(key);
                ViewerSettings settings = ViewerSettings.Load();
                settings.Font = key;
                settings.Save();
                RebuildUi();
            };
            fonts.AddChild(b);
        }

        // The controls: the "Controls" chapter of the manual (data/sim/manual.md), so the keys are written in one place.
        var controls = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        controls.AddThemeFontSizeOverride("normal_font_size", PixelTheme.FontSize);
        List<ManualChapter> chapters = ManualChapters();
        int controlsAt = ManualChapterOf("Controls");
        controls.Text = chapters.Count > 0 ? Manual.ToBbCode(chapters[controlsAt], PixelTheme.FontSize) : "The manual (data/sim/manual.md) is missing.";
        help.AddChild(controls);
        var helpButtons = new HBoxContainer();
        helpButtons.AddThemeConstantOverride("separation", 8);
        help.AddChild(helpButtons);
        helpButtons.AddChild(UiKit.Button("The whole manual (F1)", () => ToggleManual(0), "How to play: the goal, the first minutes, the eras, war and peace, every key"));
        helpButtons.AddChild(UiKit.Button("Close", () => _help.Visible = false, "Close (Esc or H)"));
        BuildKeysUi(root, topH);
    }

    private void OpenWindow(string name, bool toggle = true)
    {
        _bigMode = toggle && _bigMode == name ? "" : name;
        _bigSignature = "";
        if (_bigMode == "Summary")
        {
            _session?.AskSummary();
        }

        UpdateUi();
    }

    private void ToggleChronicle()
    {
        _chronicleOpen = !_chronicleOpen;
        _chroniclePanel.OffsetTop = ChronicleBottom - (_chronicleOpen ? 420 : 110);
        _chronicleToggle.Text = _chronicleOpen ? "less" : "more";
        RefreshChronicle();
    }

    private void RefreshChronicle()
    {
        int n = _chronicleOpen ? 60 : 5;
        int from = Math.Max(0, _chronicle.Count - n);
        _chronicleText.Text = string.Join("\n", _chronicle.GetRange(from, _chronicle.Count - from));
    }

    private void UpdateUi()
    {
        if (_session?.PlayView is not { } v || _session.PlayContent is not { } c)
        {
            return;
        }

        UpdateTop(v, c);
        UpdateUnderTop();
        UpdateTutorial(v);
        UpdateAdvisor(v);
        UpdateAlerts(v);
        UpdateActionBar(v, c);
        UpdateBanner(c);
        var items = new List<UiItem>();
        Inspect(v, c, items);
        string sig = UiRender.Sig(items);
        if (sig != _inspectorSig)
        {
            _inspectorSig = sig;
            UiRender.Render(_inspectorBox, items, () => _uiTimer = 0.1);
        }

        _inspector.Visible = items.Count > 0;

        // The side panel grows with what it shows, up to the action bar; then it scrolls.
        float most = GetViewport().GetVisibleRect().Size.Y - (2 * PixelTheme.FontSize) - 200;
        float want = Math.Min(_inspectorBox.GetCombinedMinimumSize().Y + 4, most);
        _inspectorScroll.CustomMinimumSize = new Vector2(UiKit.W(320), want);
        _inspector.Size = Vector2.Zero;
        UpdateWindow(v, c);
    }

    private void UpdateTop(PlayViewMessage v, PlayContentMessage c)
    {
        foreach ((Button b, SimulationSpeed s) in _speedButtons)
        {
            b.SetPressedNoSignal(v.Speed == s);
        }

        _clockLabel.Text = v.Debug ? $"TEST {Clock()}" : Clock();
        _clockLabel.TooltipText = v.Debug ? "Test scenario. F5: +200 of every good. F6: finish the research. F7: reveal the map. F8: +10 people. F9: next era." : "";
        _clockLabel.MouseFilter = v.Debug ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
        int patrols = v.Units.Count(u => u.Civ == v.Civ && u.Patrol is not null);
        int hungry = v.Units.Count(u => u.Civ == v.Civ && u.Hunger > 0);
        _patrolsButton.Visible = patrols > 0 || hungry > 0;
        _patrolsButton.Text = hungry > 0 ? $"Units: {hungry} hungry" : $"Patrols {patrols}";
        _patrolsButton.Modulate = hungry > 0 ? new Color(1f, 0.55f, 0.5f) : Colors.White;
        foreach ((Button b, SimulationSpeed _) in _speedButtons)
        {
            b.Visible = !v.Picking; // choosing the start goes in real time
        }

        if (v.Picking)
        {
            _eraLabel.Text = v.MyPick ? (v.PickNoLimit ? "Your turn: choose the start" : $"Your turn: {v.PickSecondsLeft} s") : $"{v.WhoPicks} are choosing…";
            SetResources("pick", () => _resBox.AddChild(UiKit.Label("Choosing the start", 14, UiKit.Muted)));
            return;
        }

        _eraLabel.Text = v.Lost ? "Our people are gone" : v.Winner is { } w ? $"{w} have won" : c.Eras[v.Era];
        if (!_endShown && (v.Lost || v.Winner is not null))
        {
            _endShown = true;
            _endTitle.Text = v.Won ? "VICTORY" : v.Lost ? "OUR PEOPLE ARE GONE" : "THE GAME IS WON";
            _endText.Text = (v.Won ? $"We have won after {Clock()} of play." : v.Lost ? $"No settlement of ours is left after {Clock()} of play." : $"{v.Winner} have won after {Clock()} of play.")
                + (v.Goal is { Length: > 0 } goal ? $"\nGoal: {goal}" : "") + $"\nOur score: {v.Score}. The game is paused; it can go on behind this.";
            _endPanel.Visible = true;
            _session?.SetSpeed(SimulationSpeed.Paused);
        }

        _eraLabel.TooltipText = v.Goal ?? "";
        float progress = v.EraNeed > 0 ? Math.Min(1f, (float)v.EraKnown / v.EraNeed) : 1f;
        int y0 = EraYears[Math.Min(v.Era, EraYears.Length - 2)];
        int y1 = EraYears[Math.Min(v.Era + 1, EraYears.Length - 1)];
        int year = (int)(y0 + ((y1 - y0) * progress));
        string yearText = year < 0 ? $"{-year} BC" : $"AD {year}";
        string next = v.EraNeed > 0 && v.Era + 1 < c.Eras.Count ? $"{yearText}   {Math.Min(v.EraKnown, v.EraNeed)}/{v.EraNeed} to the {c.Eras[v.Era + 1]}" : yearText;
        string sig = $"{next}|{progress:0.00}";
        if (_eraBarBox.GetMeta("sig", "").AsString() != sig)
        {
            _eraBarBox.SetMeta("sig", sig);
            UiKit.Clear(_eraBarBox);
            _eraBarBox.AddChild(UiKit.Bar(progress, UiKit.Heading, UiKit.W(460), PixelTheme.FontSize + 6, next));
        }

        _researchChip.Text = v.Researching >= 0 ? $"Researching {c.Techs[v.Researching].Name} {v.ResearchPermille / 10}%" : "Choose research (2)";
        _researchChip.AddThemeColorOverride("font_color", v.Researching >= 0 ? UiKit.Text : UiKit.Warn);
        if (v.Researching < 0)
        {
            // E2: an empty research queue is easy to miss: the chip pulses.
            float pulse = 0.5f + (0.5f * Mathf.Sin((float)_time * 4f));
            _researchChip.AddThemeStyleboxOverride("normal", UiKit.Flat(new Color(0.35f * pulse, 0.25f * pulse, 0.05f, 0.95f), UiKit.Warn, 4, 3));
        }
        else
        {
            _researchChip.RemoveThemeStyleboxOverride("normal");
        }

        // Resources: an icon, the amount and the trend a minute, for goods we have or make.
        var cells = new List<(int Good, long Stock, long Rate, bool Full)>();
        for (int g = 0; g < v.Stock.Length; g++)
        {
            long rate = v.RatePerMinute[g];
            if (g > 2 && v.Stock[g] < 100 && rate == 0)
            {
                continue;
            }

            cells.Add((g, v.Stock[g], rate, v.Capacity[g] > 0 && v.Stock[g] >= v.Capacity[g] * 98 / 100));
        }

        string resSig = $"{v.People}{v.Hungry}" + string.Join(";", cells.ConvertAll(x => $"{x.Good}:{x.Stock / 100}:{x.Rate}:{x.Full}"));
        // 2026-11-23: the same goods as before: only the numbers change, in the chips that are there (no flicker, the
        // tooltip under the mouse stays).
        string layout = string.Join(",", cells.ConvertAll(x => x.Good));
        if (resSig != _resSig && layout == _resLayout && _resBox.GetChildCount() == cells.Count + 1)
        {
            _resSig = resSig;
            SetChip(_resBox.GetChild<Control>(0), $"{v.People}", v.Hungry ? "hungry" : "", v.Hungry ? UiKit.Bad : UiKit.Muted,
                $"People: {v.People}" + (v.Hungry ? ". Some settlements go hungry!" : "."));
            for (int k = 0; k < cells.Count; k++)
            {
                (int g, long stock, long rate, bool full) = cells[k];
                PlayGoodInfo good = c.Goods[g];
                Color trend = full ? UiKit.Warn : rate > 0 ? UiKit.Good : rate < 0 ? UiKit.Bad : UiKit.Muted;
                SetChip(_resBox.GetChild<Control>(k + 1), $"{stock / 100}", full ? "full" : UiKit.Rate(rate), trend,
                    $"{good.Name}: {stock / 100} in all our settlements (room for {v.Capacity[g] / 100}), {UiKit.Rate(rate)} a minute." + (full ? " Full: its makers stop." : ""));
            }

            return;
        }

        _resLayout = layout;
        SetResources(resSig, () =>
        {
            Control people = Chip(new Color("e0c090"), "P", $"{v.People}", v.Hungry ? "hungry" : "", v.Hungry ? UiKit.Bad : UiKit.Muted,
                $"People: {v.People}" + (v.Hungry ? ". Some settlements go hungry!" : "."));
            _resBox.AddChild(people);
            foreach ((int g, long stock, long rate, bool full) in cells)
            {
                PlayGoodInfo good = c.Goods[g];
                Color trend = full ? UiKit.Warn : rate > 0 ? UiKit.Good : rate < 0 ? UiKit.Bad : UiKit.Muted;
                _resBox.AddChild(Chip(UiKit.GoodColor(good.Key), UiKit.Short(good.Name)[..1], $"{stock / 100}", full ? "full" : UiKit.Rate(rate), trend,
                    $"{good.Name}: {stock / 100} in all our settlements (room for {v.Capacity[g] / 100}), {UiKit.Rate(rate)} a minute." + (full ? " Full: its makers stop." : ""), good.Key));
            }
        });
    }

    private void SetResources(string sig, Action fill)
    {
        if (sig == _resSig)
        {
            return;
        }

        _resSig = sig;
        UiKit.Clear(_resBox);
        fill();
    }

    private string _resLayout = "";

    // A chip's numbers changed in place: the amount and trend labels are the chip's second child's two labels.
    private static void SetChip(Control chip, string amount, string trend, Color trendColor, string tooltip)
    {
        chip.TooltipText = tooltip;
        if (chip.GetChildCount() > 1 && chip.GetChild(1) is VBoxContainer col && col.GetChildCount() > 1
            && col.GetChild(0) is Label a && col.GetChild(1) is Label t)
        {
            a.Text = amount;
            t.Text = trend;
            t.AddThemeColorOverride("font_color", trendColor);
        }
    }

    private static Control Chip(Color color, string letter, string amount, string trend, Color trendColor, string tooltip, string? good = null)
    {
        var row = new HBoxContainer { TooltipText = tooltip, MouseFilter = Control.MouseFilterEnum.Pass };
        row.AddThemeConstantOverride("separation", 4);
        // 2026-11-23 (Patryk): the goods' pictures in the top bar instead of letters.
        Texture2D? icon = good is null ? null : UiKit.Picture?.Invoke("goods", "good_" + good);
        row.AddChild(new Glyph(color, letter, icon is null ? 26 : 34, round: icon is null, icon: icon));
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", -4);
        col.AddChild(UiKit.Label(amount, 16));
        col.AddChild(UiKit.Label(trend, 12, trendColor));
        row.AddChild(col);
        return row;
    }

    private void UpdateAdvisor(PlayViewMessage v)
    {
        bool hasEvent = v.Event is not null;
        string season = v.SeasonSeconds <= 0 ? "" : v.Winter ? $"WINTER: {v.SeasonSeconds / 60}:{v.SeasonSeconds % 60:00} left" : v.SeasonSeconds <= 60 ? $"Winter in {v.SeasonSeconds} s" : "";
        _eventLabel.Visible = hasEvent || season.Length > 0;
        _eventLabel.Text = hasEvent ? $"{v.Event!.ToUpperInvariant()}: {v.EventSecondsLeft} s left" + (season.Length > 0 ? $"   {season}" : "") + (v.EventInfo is { } info ? $"\n{info}" : "") : season;
        if (v.Advisor is { } a)
        {
            _advisorName.Text = $"{a.Name}, {a.Rarity.ToLowerInvariant()} advisor";
            _advisorText.Text = v.Advice ?? "Nothing to worry about right now.";
            _advisorText.AddThemeColorOverride("font_color", v.Advice is null ? UiKit.Muted : UiKit.Text);
        }
        else
        {
            _advisorName.Text = "No advisor";
            _advisorText.Text = v.AdvisorOffer is { } o ? $"{o.Name} offers advice on {o.Fields}. Hire in Thinkers (4)." : "Nobody tells us what goes wrong.";
            _advisorText.AddThemeColorOverride("font_color", UiKit.Muted);
        }

        _advisorPanel.Visible = !v.Picking;
    }

    // Alerts on the right, under the advisor: what needs a reaction. A click goes to the place.
    private void UpdateAlerts(PlayViewMessage v)
    {
        IReadOnlyList<PlayAlertView> alerts = v.Alerts ?? [];
        string sig = string.Join("|", System.Linq.Enumerable.Select(alerts, a => a.Text));
        if (sig == _alertSig)
        {
            return;
        }

        // 2026-11-23: the same alerts as before whose text only counts down ("food runs out in 2:32"): the texts change in
        // place, so the list does not flicker and a hovered alert stays.
        string layout = string.Join("|", System.Linq.Enumerable.Select(alerts, a => $"{a.Kind}:{a.Col}:{a.Row}:{a.Good}"));
        if (layout == _alertLayout && alerts.Count <= 7 && _alertsBox.GetChildCount() == alerts.Count)
        {
            _alertSig = sig;
            for (int k = 0; k < alerts.Count; k++)
            {
                if (_alertsBox.GetChild(k).GetChildOrNull<Node>(0)?.GetChildOrNull<Label>(1) is { } label)
                {
                    label.Text = alerts[k].Text;
                }
            }

            return;
        }

        _alertLayout = layout;
        _alertSig = sig;
        UiKit.Clear(_alertsBox);
        int shown = 0;
        foreach (PlayAlertView a in alerts)
        {
            if (++shown > 7)
            {
                _alertsBox.AddChild(UiKit.Label($"and {alerts.Count - 7} more: see Settlements (1)", 13, UiKit.Muted));
                break;
            }

            int col = a.Col;
            int row = a.Row;
            Color color = a.Kind <= 2 ? UiKit.Bad : UiKit.Warn;

            // A17 (tests 2026-11-20): the whole text, wrapped, instead of a line cut at the edge of the screen.
            var b = new PanelContainer { TooltipText = "Click to go there.", MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            StyleBoxFlat normal = UiKit.Flat(new Color(0.14f, 0.11f, 0.1f, 0.92f), color, 4, 2);
            StyleBoxFlat hover = UiKit.Flat(new Color(0.22f, 0.17f, 0.14f, 0.95f), color, 4, 2);
            b.AddThemeStyleboxOverride("panel", normal);
            b.MouseEntered += () => b.AddThemeStyleboxOverride("panel", hover);
            b.MouseExited += () => b.AddThemeStyleboxOverride("panel", normal);
            var inner = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            inner.AddThemeConstantOverride("separation", 6);
            // 2026-11-23: a picture per kind of alert; for a good that is missing or full, the good's own picture.
            Texture2D? alertPic = (a.Kind is 3 or 5) && a.Good >= 0 && _session?.PlayContent is { } pc && a.Good < pc.Goods.Count
                ? UiKit.Picture?.Invoke("goods", "good_" + pc.Goods[a.Good].Key)
                : null;
            alertPic ??= UiKit.Picture?.Invoke("ui", "alert_" + a.Kind);
            inner.AddChild(new Glyph(color, "!", alertPic is null ? 26 : 36, round: true, icon: alertPic) { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            Label text = UiKit.Label(a.Text, 13, UiKit.Text, wrap: true);
            text.MouseFilter = Control.MouseFilterEnum.Ignore;
            text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            text.CustomMinimumSize = new Vector2(UiKit.W(380) - 60, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            inner.AddChild(text);
            b.AddChild(inner);
            int kind = a.Kind;
            b.GuiInput += e =>
            {
                if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                {
                    if (kind == 10)
                    {
                        OpenWindow("Research", toggle: false); // E2: nothing is researched: the tree opens
                    }
                    else
                    {
                        Focus(col, row);
                    }
                }
            };
            _alertsBox.AddChild(b);
        }
    }

    // A15: everything under the top bar starts where the bar ends (the goods may take two rows).
    private void UpdateUnderTop()
    {
        float bottom = Math.Max(_top.Size.Y, _top.GetCombinedMinimumSize().Y);
        if (Math.Abs(bottom - _topBottom) < 0.5f)
        {
            return;
        }

        float shift = bottom - _topBottom;
        _topBottom = bottom;
        _inspector.OffsetTop += shift;
        _rightCol.OffsetTop += shift;
        _banner.OffsetTop += shift;
        _toastPanel.OffsetTop += shift;
        _window.OffsetTop += shift;
        _help.OffsetTop += shift;
    }

    // E6: the next steps of the tutorial on screen, when it is on.
    private void UpdateTutorial(PlayViewMessage v)
    {
        IReadOnlyList<PlayTutorialStep> steps = v.Tutorial ?? [];
        bool show = v.TutorialOn && !v.Picking && steps.Any(x => !x.Done);
        _tutorialPanel.Visible = show;
        string sig = show ? string.Join("|", steps.Select(x => $"{x.Done}{x.Text}")) : "";
        if (!show || sig == _tutorialSig)
        {
            return;
        }

        _tutorialSig = sig;
        UiKit.Clear(_tutorialBox);
        var head = new HBoxContainer();
        Label title = UiKit.Label($"TUTORIAL  {steps.Count(x => x.Done)}/{steps.Count}", 13, UiKit.Heading);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        head.AddChild(UiKit.Button("all", () => OpenWindow("Tutorial", toggle: false), "Every step (T)"));
        head.AddChild(UiKit.Button("hide", () => _session?.SetTutorial(false), "Hide the task list (the progress is kept; Tutorial at the top shows it again)"));
        _tutorialBox.AddChild(head);
        int shown = 0;
        foreach (PlayTutorialStep st in steps)
        {
            if (st.Done)
            {
                continue;
            }

            Label l = UiKit.Label((shown == 0 ? "> " : "  ") + st.Text, 13, shown == 0 ? UiKit.Heading : UiKit.Muted, wrap: true);
            l.CustomMinimumSize = new Vector2(UiKit.W(420), 0);
            _tutorialBox.AddChild(l);
            if (++shown == 3)
            {
                break;
            }
        }
    }

    // E8 (2026-11-20): what lies under the mouse (biome, hills, river, whose land), also while building and choosing the start.
    private void UpdateHexTip()
    {
        if (_session is null || OverUi() || MouseCell() is not { } h || !_session.Worlds.TryGetValue(0, out ClientWorld? w) || _session.Content is not { } content || _session.PlayView is not { } v)
        {
            _hexTipPanel.Visible = false;
            return;
        }

        int i = w.Index(h.Col, h.Row);
        if (w.Visibility[i] == MapChunkMessage.Unknown && !v.Picking)
        {
            _hexTipPanel.Visible = false;
            return;
        }

        BiomeInfo b = content.Biomes[w.Biome[i]];
        bool river = RiverAt(w, h.Col, h.Row);
        string text = b.Name + (w.Relief[i] > 0 ? $", {content.Reliefs[w.Relief[i]].ToLowerInvariant()}" : "") + (river ? ", by a river" : "");
        if (!b.IsWater)
        {
            text += w.Owner[i] < 0 ? " - nobody's land" : w.Owner[i] == v.Civ ? " - our land" : " - land of another people";
        }

        if (v.Picking && v.MyPick && !b.IsWater)
        {
            // E13: a quick word on the place (what everyone sees while choosing: terrain, water, trees).
            var good = new List<string>();
            if (river)
            {
                good.Add("fresh water");
            }

            if (b.Key is "grassland" or "plains" or "savanna" or "temperate_forest" or "floodplain")
            {
                good.Add("good land");
            }

            if (w.Relief[i] == 1)
            {
                good.Add("hills to defend");
            }

            if (w.Trees[i] > 0)
            {
                good.Add("wood");
            }

            text += good.Count >= 2 ? $"\nGood start: {string.Join(", ", good)}" : good.Count == 1 ? $"\nFair start: {good[0]}" : "\nPoor start: little food or water";
        }

        _hexTip.Text = text;
        _hexTipPanel.Visible = true;
        _hexTipPanel.Size = Vector2.Zero;
        _hexTipPanel.Position = GetViewport().GetMousePosition() + new Vector2(18, 20);
    }

    // A river on any of the hex's six edges (its own N, NE, SE and the neighbours' facing edges).
    private static bool RiverAt(ClientWorld w, int col, int row)
    {
        int i = w.Index(col, row);
        if (w.River[i] != 0)
        {
            return true;
        }

        foreach (int e in new[] { 3, 4, 5 })
        {
            (int nc, int nr) = OverlayColors.Neighbor(col, row, e);
            if (nr >= 0 && nr < w.Height)
            {
                int n = w.Index(nc, nr);
                int bits = w.River[n];
                int facing = e - 3; // S faces their N, SW their NE, NW their SE
                if (((bits >> (facing * 2)) & 3) != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void UpdateBanner(PlayContentMessage c)
    {
        if (_session?.PlayView is { Picking: true } pick)
        {
            _banner.Visible = true;
            _bannerText.Text = pick.MyPick
                ? (pick.PickNoLimit ? "Click a land hex on the map where your people should start (take your time: the game waits)." : $"Click a land hex on the map where your people should start ({pick.PickSecondsLeft} s left; then the AI chooses for you).")
                  + "\nGreen under the mouse: you may start there. Red circles: taken by other peoples; start on the main continent, within 60 hexes by land of a circle. Deposits and soil stay hidden until everyone has chosen."
                : $"{pick.WhoPicks} are choosing where to start. Your turn comes; look around the map meanwhile.";
            return;
        }

        string? hint = DrawHint();
        _banner.Visible = _placing > 0 || hint is not null;
        if (hint is not null)
        {
            _bannerText.Text = hint;
        }
        else if (_placing > 0)
        {
            PlayBuildingInfo t = c.Buildings[_placing];
            _bannerText.Text = $"Placing {t.Name} ({Amounts(c, t.Price)}). Click a green hex.  Shift: place more.  Right click or Esc: cancel.";
        }
    }

    private void UpdateToast(double delta)
    {
        if (_toastTime > 0)
        {
            _toastTime -= delta;
            _toastPanel.Visible = _toastTime > 0;
        }
    }

    // A save named after the game's seed and minute.
    private void SaveNow()
    {
        if (_session?.PlayView is null)
        {
            return;
        }

        _session.SaveGame(""); // the server names it after the game's seed (or scenario) and minute
    }

    private void Toast(string text)
    {
        GD.Print($"toast: {text}");
        _toast.Text = text;
        _toastPanel.Visible = true;
        _toastTime = 4;
    }

    // Moves the camera to a hex and selects it.
    private void Focus(int col, int row)
    {
        _camera.Position = HexLayout.Center(col, row);
        _selected = (col, row);
        _unit = -1;
        if (_overlay is not null)
        {
            _overlay.SelectedUnit = -1;
        }

        _uiTimer = 0;
    }

    private string Clock()
    {
        long ticks = _session?.PlayView?.Tick ?? 0;
        long s = ticks / 10;
        return $"{s / 60}:{s % 60:00}";
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

    // icons: each good led by its picture's mark, for rich tooltips (RichTipButton).
    private static string Amounts(PlayContentMessage content, long[] a, bool icons = false)
    {
        var parts = new List<string>();
        for (int g = 0; g < a.Length; g++)
        {
            if (a[g] != 0)
            {
                parts.Add(icons
                    ? UiKit.GoodMark(content.Goods[g].Key) + $"{UiKit.Num(a[g])}\u00A0{content.Goods[g].Name.ToLowerInvariant().Replace(' ', '\u00A0')}"
                    : $"{UiKit.Num(a[g])} {content.Goods[g].Name.ToLowerInvariant()}");
            }
        }

        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    private static bool CanPay(long[] stock, long[] price)
    {
        for (int g = 0; g < price.Length && g < stock.Length; g++)
        {
            if (stock[g] < price[g])
            {
                return false;
            }
        }

        return true;
    }
}
