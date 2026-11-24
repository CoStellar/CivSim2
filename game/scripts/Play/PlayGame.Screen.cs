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
    private static readonly string[] WindowNames = ["Settlements", "Research", "Civilizations", "Thinkers", "Summary", "Units"];

    // Decorative years at the start of each era and at the end of the last (only for the screen, never for the rules).
    private static readonly int[] EraYears = [-10000, -6000, -3000, -1200, 500, 1500];

    private HFlowContainer _resBox = null!;
    private PanelContainer _top = null!;
    private Control _rightCol = null!;
    private float _topBottom;
    private Label _eraLabel = null!;
    private TextureRect _eraIcon = null!;
    private TextureButton _emblemButton = null!;
    private string _emblemPic = "";

    // The era whose interface frames are in use (an era key), so the theme is built again only when the era changes.
    private string? _themeEra;
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
    private Glyph _eventGlyph = null!;
    private TextureRect _eventIcon = null!;
    private HFlowContainer _alertsBox = null!;
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
        _alertIcons.Clear();
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
        if (PixelTheme.Current is null)
        {
            top.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.11f, 0.09f, 0.08f, 0.94f), new Color(0.35f, 0.28f, 0.2f), 0, 2));
        }
        else
        {
            top.ThemeTypeVariation = "TopBar"; // 2026-11-24: the era's bar (leather, wicker, bronze, iron, cloth)
        }
        root.AddChild(top);
        _top = top;
        _topBottom = topH;
        var topRow = new HBoxContainer();
        topRow.AddThemeConstantOverride("separation", 10);
        top.AddChild(topRow);

        // 2026-11-24 (Patryk): our emblem, large and always in sight at the left of the top bar; it opens the window about
        // our whole civilization. It takes the look of our era like the rest of the interface.
        _emblemButton = new TextureButton
        {
            TooltipText = "Our civilization (key 0)", FocusMode = Control.FocusModeEnum.None, StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            IgnoreTextureSize = true, CustomMinimumSize = new Vector2(80, 80), TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, Visible = false,
        };
        _emblemButton.Pressed += () => OpenWindow("Civilization");
        _emblemButton.MouseEntered += () => _emblemButton.Modulate = new Color(1.15f, 1.15f, 1.15f);
        _emblemButton.MouseExited += () => _emblemButton.Modulate = Colors.White;
        topRow.AddChild(_emblemButton);

        // A15 (tests 2026-11-20): the goods stop before the era in the middle and wrap onto more rows; the bar grows with them
        // and what sits under it moves down (UpdateUnderTop).
        // 2026-11-24: three columns that cannot overlap at any interface size: the goods (they wrap), the era, the right group.
        _resBox = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(160, 0) };
        _resBox.AddThemeConstantOverride("h_separation", 12);
        _resBox.AddThemeConstantOverride("v_separation", 2);
        topRow.AddChild(_resBox);
        _topRow = topRow;

        // 2026-11-24 (Patryk): two rows at the right: time, speed and the game's buttons; under them the windows (keys 1-6),
        // which left the action bar at the bottom.
        // Both rows wrap button by button and share the width with the goods, so a large interface takes more rows, not
        // more width (2026-11-24: at 150% the goods were squeezed into five rows).
        var rightRows = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rightRows.AddThemeConstantOverride("separation", 2);
        topRow.AddChild(rightRows);
        var right = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.End };
        right.AddThemeConstantOverride("h_separation", 4);
        right.AddThemeConstantOverride("v_separation", 2);
        rightRows.AddChild(right);
        var windows = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.End };
        windows.AddThemeConstantOverride("h_separation", 4);
        windows.AddThemeConstantOverride("v_separation", 2);
        rightRows.AddChild(windows);
        for (int i = 0; i < WindowNames.Length; i++)
        {
            string name = WindowNames[i];
            Button wb = UiKit.Button($"{i + 1} {name}", () => OpenWindow(name), $"{name} (key {i + 1})");
            UiKit.SetIcon(wb, "ui/window_" + name.ToLowerInvariant()); // 2026-11-24 (Patryk): a picture on each window button
            wb.AddThemeFontSizeOverride("font_size", Math.Max(12, PixelTheme.FontSize * 3 / 4));
            windows.AddChild(wb);
        }
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
        right.AddChild(UiKit.Button("?", () => _help.Visible = !_help.Visible, "The keys, the font and the interface size (H)"));
        right.AddChild(UiKit.Button("Manual", () => ToggleManual(), "How to play: the goal, the eras, war and peace, every key (F1)"));
        right.AddChild(UiKit.Button("Tutorial", () => OpenWindow("Tutorial"), "First steps: what to do next, and the task list on screen (T)"));
        right.AddChild(UiKit.Button("Save", SaveNow, "Save the game (it also saves itself every 5 minutes as 'autosave'); load it from the start menu (F5)"));
        right.AddChild(UiKit.Button("Menu", () => MenuRequested?.Invoke(), "Back to the start menu"));

        // Era and years in the middle of the top bar, over the bar.
        var centre = _centreBox = new VBoxContainer { CustomMinimumSize = new Vector2(UiKit.W(320), 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        centre.AddThemeConstantOverride("separation", 0);
        _topRow.AddChild(centre);
        _topRow.MoveChild(centre, 2); // between the goods and the right group (after the emblem and the goods)
        _eraLabel = UiKit.Label("", 24, UiKit.Heading);
        _eraLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _eraLabel.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        _eraIcon = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(28, 28), TextureFilter = CanvasItem.TextureFilterEnum.Nearest, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        var eraRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        eraRow.AddThemeConstantOverride("separation", 6);
        eraRow.AddChild(_eraIcon);
        eraRow.AddChild(_eraLabel);
        centre.AddChild(eraRow);
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
        _eventGlyph = new Glyph(new Color("8a4030"), "!", 32, round: true);
        adv.AddChild(_eventGlyph);
        _eventIcon = new TextureRect { CustomMinimumSize = new Vector2(32, 32), StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Nearest, SizeFlagsVertical = Control.SizeFlags.ShrinkBegin, Visible = false };
        adv.AddChild(_eventIcon);
        var advText = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        adv.AddChild(advText);
        _advisorName = UiKit.Label("", 15, UiKit.Heading);
        advText.AddChild(_advisorName);
        _eventLabel = UiKit.Label("", 14, UiKit.Bad, wrap: true);
        advText.AddChild(_eventLabel);
        _advisorText = UiKit.Label("", 14, UiKit.Text, wrap: true);
        advText.AddChild(_advisorText);

        _alertsBox = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.End, MouseFilter = Control.MouseFilterEnum.Ignore };
        _alertsBox.AddThemeConstantOverride("h_separation", 6);
        _alertsBox.AddThemeConstantOverride("v_separation", 6);
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
        // 2026-11-24 (Patryk): hints and messages sit right under the top bar (and the research chip), not over the map's middle.
        int underTop = Math.Max(topH, (3 * PixelTheme.FontSize) + 30) + 4;
        _banner = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -UiKit.W(380), OffsetRight = UiKit.W(380), OffsetTop = underTop, Visible = false };
        _banner.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.16f, 0.24f, 0.12f, 0.95f), new Color(0.5f, 0.75f, 0.35f), 6, 2));
        root.AddChild(_banner);
        _bannerText = UiKit.Label("", 13, UiKit.Text, wrap: true);
        _bannerText.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.AddChild(_bannerText);

        _toastPanel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -UiKit.W(320), OffsetRight = UiKit.W(320), OffsetTop = underTop + (2 * PixelTheme.FontSize) + 16, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _toastPanel.AddThemeStyleboxOverride("panel", UiKit.Flat(new Color(0.3f, 0.1f, 0.08f, 0.95f), UiKit.Bad, 6, 2));
        root.AddChild(_toastPanel);
        _toast = UiKit.Label("", 13, UiKit.Text, wrap: true);
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
            // The picture and the key only (the name is in the tooltip and the window's title), so the row fits narrow windows.
            Button hb = UiKit.Button($"{i + 1}", () => OpenWindow(name, toggle: false), $"{name} (key {i + 1})");
            UiKit.SetIcon(hb, "ui/window_" + name.ToLowerInvariant(), 2);
            winHead.AddChild(hb);
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
        }, "How the game went, civilization by civilization (5)"));
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

        // 2026-11-24 (Patryk): the size of the whole interface.
        help.AddChild(UiKit.Label("INTERFACE SIZE", 18, UiKit.Heading));
        var sizes = new HFlowContainer();
        help.AddChild(sizes);
        int scaleNow = ViewerSettings.Load().UiScale;
        foreach (int percent in ViewerSettings.UiScales)
        {
            int p = percent;
            var b = new Button { Text = $"{p}%", ToggleMode = true, ButtonPressed = scaleNow == p, FocusMode = Control.FocusModeEnum.None, TooltipText = "Make every panel, button and text this big." };
            b.Pressed += () => SetUiScale(p);
            sizes.AddChild(b);
        }

        // 2026-11-24 (Patryk): the art pack, switched while playing (the start menu has the same choice).
        help.AddChild(UiKit.Label("ART PACK", 18, UiKit.Heading));
        var packs = new HFlowContainer();
        help.AddChild(packs);
        var packChoices = new List<(string Key, string Name, string Tip)> { ("", "Default", "The pictures of the game (Patryk's art, then the placeholders).") };
        packChoices.AddRange(ArtPack.Discover(ArtLibrary.FindArtDirectory()).Select(p => (p.Key, p.Name, p.Description.Length > 0 ? p.Description : "Pictures from the folder " + p.Key)));
        foreach ((string key, string name, string tip) in packChoices)
        {
            var b = new Button { Text = name, ToggleMode = true, ButtonPressed = (_art.Pack?.Key ?? "") == key, FocusMode = Control.FocusModeEnum.None, TooltipText = tip };
            b.Pressed += () => UseArtPack(key);
            packs.AddChild(b);
        }

        // 2026-11-24 (Patryk): how strongly the civilizations' colour covers their land.
        help.AddChild(UiKit.Label("CIVILIZATION COLOUR ON LAND", 18, UiKit.Heading));
        var tint = new HSlider { MinValue = 0, MaxValue = 60, Step = 1, Value = _layers[MapLayer.Influence].Opacity, CustomMinimumSize = new Vector2(UiKit.W(300), 24), TooltipText = "0: only the borders; more: the land takes the civilization's colour" };
        tint.ValueChanged += x =>
        {
            _layers.SetOpacity(MapLayer.Influence, (int)x);
            _layers.SetOpacity(MapLayer.Owner, (int)x);
        };
        help.AddChild(tint);

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

    // Another art pack: the shared library switches (Main saves the choice), then the coloured pictures, the map and the
    // interface are made again from it.
    private void UseArtPack(string key)
    {
        ArtPackChosen?.Invoke(key);
        _overlay?.ClearSprites();
        UiKit.ClearPictures();
        ApplyTheme();
    }

    // Builds the interface theme for the people's era from the current pictures and lays the screen out again.
    private void ApplyTheme()
    {
        if (PixelTheme.Build(_art, _themeEra) is { } theme)
        {
            PixelTheme.Current = theme;
            GetWindow().Theme = theme;
        }

        RebuildUi();
    }

    // Saves and applies the interface size, then lays the screen out again for the new size.
    private void SetUiScale(int percent)
    {
        ViewerSettings settings = ViewerSettings.Load();
        settings.UiScale = percent;
        settings.Save();
        float zoom = Zoom; // the map stays as near as it was
        ViewerSettings.ApplyUiScale(GetWindow(), percent);
        Zoom = zoom;
        bool help = _help.Visible;
        RebuildUi();
        _help.Visible = help;
    }

    private void OpenWindow(string name, bool toggle = true)
    {
        _bigMode = toggle && _bigMode == name ? "" : name;
        _bigSignature = "";
        if (_bigMode is "Summary" or "Civilization")
        {
            _session?.AskSummary();
        }

        UpdateUi();
    }

    private void ToggleChronicle()
    {
        _chronicleOpen = !_chronicleOpen;
        _chroniclePanel.OffsetTop = _chroniclePanel.OffsetBottom - (_chronicleOpen ? 420 : 110);
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
        PlaceBanner();
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

        // The side panel grows with what it shows, up to the action bar (when the bar reaches under it) or the bottom of the
        // screen; then it scrolls (2026-11-24: at 1440p it ran under the bar).
        Vector2 screenSize = GetViewport().GetVisibleRect().Size;
        Rect2 bar = _actionBar.GetGlobalRect();
        float panelRight = _inspector.Position.X + UiKit.W(332);
        float bottom = bar.Position.X < panelRight ? bar.Position.Y - 8 : screenSize.Y - 8;
        float most = Math.Max(120, bottom - _inspector.Position.Y - 16);
        float want = Math.Min(_inspectorBox.GetCombinedMinimumSize().Y + 4, most);
        _inspectorScroll.CustomMinimumSize = new Vector2(UiKit.W(320), want);
        _inspector.Size = Vector2.Zero;
        UpdateWindow(v, c);
        PlaceChronicle();
    }

    // The chronicle (and the tutorial list over it) sits at the bottom right, above the action bar only when the bar reaches
    // under it (2026-11-24: the bar is narrower now, so usually it goes down to the edge).
    private void PlaceChronicle()
    {
        Vector2 screenSize = GetViewport().GetVisibleRect().Size;
        Rect2 bar = _actionBar.GetGlobalRect();
        float left = screenSize.X - UiKit.W(440);
        float bottom = bar.End.X > left ? bar.Position.Y - screenSize.Y - 8 : -8;
        float height = _chronicleOpen ? 420 : 110;
        if (Math.Abs(_chroniclePanel.OffsetBottom - bottom) > 0.5f || Math.Abs(_chroniclePanel.OffsetTop - (bottom - height)) > 0.5f)
        {
            _chroniclePanel.OffsetBottom = bottom;
            _chroniclePanel.OffsetTop = bottom - height;
            _tutorialPanel.OffsetBottom = bottom - height - 8;
            _tutorialPanel.OffsetTop = bottom - height - 8;
        }
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

        _eraLabel.Text = v.Lost ? "Our civilization is gone" : v.Winner is { } w ? $"{w} have won" : c.Eras[v.Era];
        string eraKey = UiKit.KeyOf(c.Eras[v.Era]);
        if (eraKey != _themeEra)
        {
            // 2026-11-24 (Patryk): the interface takes the frames of the people's era; built again after this update.
            _themeEra = eraKey;
            Callable.From(ApplyTheme).CallDeferred();
        }

        if (UiKit.Picture?.Invoke("ui", "era_" + eraKey) is { } eraPic && _eraIcon.Texture != eraPic)
        {
            _eraIcon.Texture = eraPic;
        }

        _eraIcon.Visible = _eraIcon.Texture is not null && !v.Lost && v.Winner is null;
        string emblem = UiKit.EmblemName(eraKey, v.Civ) + (v.Lost ? "|grey" : "");
        if (emblem != _emblemPic || _emblemButton.TextureNormal is null)
        {
            _emblemPic = emblem;
            Texture2D? pic = UiKit.PortraitOf(UiKit.EmblemName(eraKey, v.Civ), v.Civ, v.Lost);
            _emblemButton.TextureNormal = pic is null ? null : UiKit.Scaled(pic, 2);
        }

        _emblemButton.Visible = _emblemButton.TextureNormal is not null;
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
            _eraBarBox.AddChild(UiKit.Bar(progress, UiKit.Heading, UiKit.W(320), PixelTheme.FontSize + 6, next));
        }

        // Patryk, 2026-11-24: the chip shows only what is being researched; "Choose research" was the same as key 2 and the
        // Research button, and the reminder is an alert now (it comes a while after a research ends).
        _researchChip.Visible = v.Researching >= 0;
        if (v.Researching >= 0)
        {
            _researchChip.Text = $"Researching {c.Techs[v.Researching].Name} {v.ResearchPermille / 10}%";
        }

        UpdateGoods(v, c);
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
    private VBoxContainer _centreBox = null!;
    private HBoxContainer _topRow = null!;

    private void UpdateAdvisor(PlayViewMessage v)
    {
        bool hasEvent = v.Event is not null;
        string season = v.SeasonSeconds <= 0 ? "" : v.Winter ? $"WINTER: {v.SeasonSeconds / 60}:{v.SeasonSeconds % 60:00} left" : v.SeasonSeconds <= 60 ? $"Winter in {v.SeasonSeconds} s" : "";
        _eventLabel.Visible = hasEvent || season.Length > 0;
        _eventLabel.Text = hasEvent ? $"{v.Event!.ToUpperInvariant()}: {v.EventSecondsLeft} s left" + (season.Length > 0 ? $"   {season}" : "") + (v.EventInfo is { } info ? $"\n{info}" : "") : season;

        // 2026-11-24 (Patryk): the event's picture (drought, hard winter, epidemic, fire, flood); the winter shows the snowflake.
        string eventKey = hasEvent ? "event_" + UiKit.KeyOf(v.Event!) : season.Length > 0 ? "event_hard_winter" : "";
        Texture2D? eventPic = eventKey.Length > 0 ? UiKit.Picture?.Invoke("ui", eventKey) : null;
        _eventIcon.Texture = eventPic;
        _eventIcon.Visible = eventPic is not null;
        _eventGlyph.Visible = eventPic is null;
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

        // 2026-11-24 (Patryk): the advisor is an icon in the alert strip; this panel shows only an event or the winter.
        _advisorName.Visible = false;
        _advisorText.Visible = false;
        _advisorPanel.Visible = !v.Picking && _eventLabel.Visible;
    }


    // Hints and messages right under the top bar or the research chip, whichever reaches lower (2026-11-24).
    private void PlaceBanner()
    {
        float under = Math.Max(_topBottom, _centreBox.GetCombinedMinimumSize().Y) + 4;
        _banner.OffsetTop = under;
        _toastPanel.OffsetTop = under + (2 * PixelTheme.FontSize) + 16;
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
        PlaceBanner();
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
            text += w.Owner[i] < 0 ? " - nobody's land" : w.Owner[i] == v.Civ ? " - our land" : " - land of another civilization";
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
                ? (pick.PickNoLimit ? "Click a land hex on the map where your civilization should start (take your time: the game waits)." : $"Click a land hex on the map where your civilization should start ({pick.PickSecondsLeft} s left; then the AI chooses for you).")
                  + "\nGreen under the mouse: you may start there. Red circles: taken by other civilizations; start on the main continent, within 60 hexes by land of a circle. Deposits and soil stay hidden until everyone has chosen."
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
