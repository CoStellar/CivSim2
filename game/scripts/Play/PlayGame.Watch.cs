using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The full view of observers and fallen players (Patryk, 2026-11-26): a bar to pick whose eyes to watch through, the table of every
/// civilization (a click goes to its capital), the intentions log (AI decisions and summaries of players' orders) with a filter, and the
/// routes and targets of every unit on the map. Players never get any of it.
/// </summary>
public partial class PlayGame
{
    private Control? _watchRoot;
    private PanelContainer? _watchBar;
    private OptionButton? _eyePick;
    private CheckButton? _intentRoutes;
    private CheckButton? _intentLogOn;
    private PanelContainer? _tablePanel;
    private GridContainer? _tableGrid;
    private PanelContainer? _intentPanel;
    private VBoxContainer? _intentList;
    private ScrollContainer? _intentScroll;
    private OptionButton? _intentFilter;
    private string _eyeShown = "";
    private string _tableShown = "";
    private long _intentsShown = -1;
    private int _intentFilterCiv = -1;

    // Called at the end of the regular refresh of the interface.
    private void UpdateWatch(PlayViewMessage v, PlayContentMessage c)
    {
        bool spectator = v.Spectator && _session is not null;
        if (_watchRoot is null)
        {
            return;
        }

        if (!spectator)
        {
            if (_watchBar is not null)
            {
                _watchBar.Visible = false;
                _tablePanel!.Visible = false;
                _intentPanel!.Visible = false;
            }

            if (_overlay is not null)
            {
                _overlay.EveryRoute = false;
            }

            return;
        }

        if (_watchBar is null)
        {
            BuildWatchUi(_watchRoot);
        }

        _watchBar!.Visible = true;
        IReadOnlyList<PlayOverviewRow>? rows = _session!.Overview;
        ShowEyes(rows, v);
        if (_overlay is not null)
        {
            _overlay.EveryRoute = _intentRoutes!.ButtonPressed;
        }

        _intentPanel!.Visible = _intentLogOn!.ButtonPressed;
        if (_intentPanel.Visible)
        {
            ShowIntents(rows);
        }

        if (_tablePanel!.Visible && rows is not null)
        {
            ShowTable(rows, c);
        }

        // The top says what we watch: everything, or one civilization through its eyes.
        _eraLabel.Text = v.Lost && v.Civ < 0 ? "Our civilization is gone" : v.Civ < 0 ? "Watching the whole map" : $"Through the eyes of {NameOf(rows, v.Civ)}";
        if (_chatAllies is not null && IsInstanceValid(_chatAllies))
        {
            _chatAllies.Visible = false;
            _chatInput!.PlaceholderText = "Only spectators read it (Enter)";
        }
    }

    private void BuildWatchUi(Control root)
    {
        // The bar: top middle, under the top bar.
        _watchBar = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, OffsetLeft = -UiKit.W(330), OffsetRight = UiKit.W(330), OffsetTop = 96 };
        root.AddChild(_watchBar);
        var bar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        bar.AddThemeConstantOverride("separation", 10);
        _watchBar.AddChild(bar);
        bar.AddChild(UiKit.Label("WATCH", 13, UiKit.Heading));
        _eyePick = new OptionButton { CustomMinimumSize = new Vector2(UiKit.W(200), 0), TooltipText = "The whole map, or what one civilization sees and knows (read only)", FocusMode = Control.FocusModeEnum.None };
        _eyePick.ItemSelected += i => WatchEye(_eyePick.GetItemId((int)i));
        bar.AddChild(_eyePick);
        bar.AddChild(UiKit.Button("Civilizations", () => _tablePanel!.Visible = !_tablePanel.Visible, "Every civilization at a glance; a click on a name shows its capital"));
        _intentRoutes = new CheckButton { Text = "Routes", ButtonPressed = true, TooltipText = "Where every unit goes, what it attacks, and every caravan route", FocusMode = Control.FocusModeEnum.None };
        bar.AddChild(_intentRoutes);
        _intentLogOn = new CheckButton { Text = "Intentions", ButtonPressed = true, TooltipText = "What the AI decides and what the players order, summed up", FocusMode = Control.FocusModeEnum.None };
        bar.AddChild(_intentLogOn);

        // The table: in the middle of the screen.
        _tablePanel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -UiKit.W(620), OffsetRight = UiKit.W(620), OffsetTop = -260, OffsetBottom = 260, Visible = false };
        root.AddChild(_tablePanel);
        var tbox = new VBoxContainer();
        tbox.AddThemeConstantOverride("separation", 8);
        _tablePanel.AddChild(tbox);
        var head = new HBoxContainer();
        tbox.AddChild(head);
        Label title = UiKit.Label("CIVILIZATIONS", 13, UiKit.Heading);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        head.AddChild(UiKit.Button("Close", () => _tablePanel.Visible = false));
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto };
        tbox.AddChild(scroll);
        _tableGrid = new GridContainer { Columns = 10, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _tableGrid.AddThemeConstantOverride("h_separation", 16);
        _tableGrid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(_tableGrid);

        // The intentions log: bottom left.
        _intentPanel = new PanelContainer { AnchorTop = 1, AnchorBottom = 1, OffsetLeft = 8, OffsetRight = UiKit.W(470), OffsetTop = -330, OffsetBottom = -8 };
        root.AddChild(_intentPanel);
        var ibox = new VBoxContainer();
        _intentPanel.AddChild(ibox);
        var ihead = new HBoxContainer();
        ibox.AddChild(ihead);
        Label ititle = UiKit.Label("INTENTIONS", 13, UiKit.Heading);
        ititle.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        ititle.TooltipText = "AI decisions as they are made; players' orders summed up every few seconds";
        ihead.AddChild(ititle);
        _intentFilter = new OptionButton { CustomMinimumSize = new Vector2(UiKit.W(170), 0), FocusMode = Control.FocusModeEnum.None, TooltipText = "Only one civilization" };
        _intentFilter.ItemSelected += i =>
        {
            _intentFilterCiv = _intentFilter.GetItemId((int)i);
            _intentsShown = -1;
        };
        ihead.AddChild(_intentFilter);
        _intentScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        ibox.AddChild(_intentScroll);
        _intentList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _intentScroll.AddChild(_intentList);
    }

    // The civilizations to watch through, and to filter the log by (when the list changes).
    private void ShowEyes(IReadOnlyList<PlayOverviewRow>? rows, PlayViewMessage v)
    {
        if (rows is null)
        {
            return;
        }

        string sig = string.Join("|", rows.Select(r => r.Name + r.Dead)) + "/" + v.Civ;
        if (sig == _eyeShown)
        {
            return;
        }

        _eyeShown = sig;
        Fill(_eyePick!, "Whole map", rows.Where(r => !r.Dead), v.Civ);
        Fill(_intentFilter!, "All civilizations", rows, _intentFilterCiv);
    }

    private static void Fill(OptionButton pick, string all, IEnumerable<PlayOverviewRow> rows, int selected)
    {
        pick.Clear();
        pick.AddItem(all, -1);
        foreach (PlayOverviewRow r in rows)
        {
            pick.AddItem(r.Name + (r.Player is { } p ? $" ({p})" : ""), r.Civ);
        }

        int index = pick.GetItemIndex(selected);
        pick.Select(index >= 0 ? index : 0);
    }

    private void ShowTable(IReadOnlyList<PlayOverviewRow> rows, PlayContentMessage c)
    {
        var sig = new StringBuilder();
        foreach (PlayOverviewRow r in rows)
        {
            sig.Append(CultureInfo.InvariantCulture, $"{r.Civ}{r.Player}{r.Dead}{r.People}{r.Settlements}{r.Era}{r.Researching}{r.ResearchPercent / 5}{r.ArmyUnits}{r.Soldiers}{r.Score}{r.Wars.Length}{r.Alliances.Length}{r.Trade.Length};");
        }

        if (sig.ToString() == _tableShown)
        {
            return;
        }

        _tableShown = sig.ToString();
        UiKit.Clear(_tableGrid!);
        foreach (string h in new[] { "Civilization", "Led by", "People", "Settlements", "Era", "Research", "Army", "Food·wood·stone", "Score", "War · allies · trade" })
        {
            _tableGrid!.AddChild(UiKit.Label(h, 13, UiKit.Muted));
        }

        int food = Good(c, "food"), wood = Good(c, "wood"), stone = Good(c, "stone");
        foreach (PlayOverviewRow r in rows.OrderByDescending(r => r.Score))
        {
            Color colour = r.Dead ? UiKit.Muted : PlayOverlay.CivColor(r.Civ).Lightened(0.3f);
            var name = new HBoxContainer();
            name.AddThemeConstantOverride("separation", 6);
            name.AddChild(new ColorRect { Color = PlayOverlay.CivColor(r.Civ), CustomMinimumSize = new Vector2(10, 10), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            PlayOverviewRow row = r;
            Button go = UiKit.Button(r.Name, () =>
            {
                if (row.CapitalCol >= 0)
                {
                    Focus(row.CapitalCol, row.CapitalRow);
                    _tablePanel!.Visible = false;
                }
            }, r.CapitalCol >= 0 ? "Show its capital (the biggest settlement)" : "It has no settlement");
            go.AddThemeColorOverride("font_color", colour);
            name.AddChild(go);
            _tableGrid!.AddChild(name);
            _tableGrid.AddChild(Cell(r.Dead ? "gone" : r.Player ?? "AI", r.Player is null ? UiKit.Muted : UiKit.Text));
            _tableGrid.AddChild(Cell(r.People.ToString(CultureInfo.InvariantCulture)));
            _tableGrid.AddChild(Cell(r.Settlements.ToString(CultureInfo.InvariantCulture)));
            _tableGrid.AddChild(Cell((uint)r.Era < (uint)c.Eras.Count ? c.Eras[r.Era] : "?"));
            _tableGrid.AddChild(Cell(r.Researching >= 0 && r.Researching < c.Techs.Count ? $"{c.Techs[r.Researching].Name} {r.ResearchPercent}%" : "nothing", r.Researching >= 0 ? UiKit.Text : UiKit.Warn));
            Label army = Cell(string.Create(CultureInfo.InvariantCulture, $"{r.ArmyUnits} / {r.Soldiers}"));
            army.TooltipText = "Army units / soldiers in them";
            army.MouseFilter = Control.MouseFilterEnum.Pass;
            _tableGrid.AddChild(army);
            Label stock = Cell(string.Create(CultureInfo.InvariantCulture, $"{At(r.Stock, food)}·{At(r.Stock, wood)}·{At(r.Stock, stone)}"));
            stock.TooltipText = string.Join("\n", c.Goods.Where(g => At(r.Stock, g.Id) > 0).Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Name}: {At(r.Stock, g.Id)}")));
            stock.MouseFilter = Control.MouseFilterEnum.Pass;
            _tableGrid.AddChild(stock);
            _tableGrid.AddChild(Cell(r.Score.ToString(CultureInfo.InvariantCulture)));
            Label rel = Cell(Relations(rows, r), r.Wars.Length > 0 ? UiKit.Bad : UiKit.Text);
            _tableGrid.AddChild(rel);
        }
    }

    private static Label Cell(string text, Color? colour = null) => UiKit.Label(text, 13, colour ?? UiKit.Text);

    private static int Good(PlayContentMessage c, string key) => c.Goods.FirstOrDefault(g => g.Key == key)?.Id ?? -1;

    private static long At(long[] stock, int good) => (uint)good < (uint)stock.Length ? stock[good] : 0;

    private static string Relations(IReadOnlyList<PlayOverviewRow> rows, PlayOverviewRow r)
    {
        string Names(int[] ids) => ids.Length == 0 ? "-" : string.Join(", ", ids.Select(i => NameOf(rows, i)));
        return r.Dead ? "" : $"{Names(r.Wars)} · {Names(r.Alliances)} · {Names(r.Trade)}";
    }

    private static string NameOf(IReadOnlyList<PlayOverviewRow>? rows, int civ) => rows?.FirstOrDefault(r => r.Civ == civ)?.Name ?? "?";

    private void ShowIntents(IReadOnlyList<PlayOverviewRow>? rows)
    {
        if (_session is null || _session.IntentsReceived == _intentsShown)
        {
            return;
        }

        _intentsShown = _session.IntentsReceived;
        UiKit.Clear(_intentList!);
        IEnumerable<PlayIntentLine> lines = _session.Intents.Where(l => _intentFilterCiv < 0 || l.Civ == _intentFilterCiv);
        foreach (PlayIntentLine l in lines.Reverse().Take(60).Reverse())
        {
            long s = l.Tick / 10;
            string who = NameOf(rows, l.Civ) + (l.Player ? rows?.FirstOrDefault(r => r.Civ == l.Civ)?.Player is { } p ? $" ({p})" : " (player)" : " (AI)");
            _intentList!.AddChild(Parts(true, [
                (string.Create(CultureInfo.InvariantCulture, $"{s / 60}:{s % 60:00} "), UiKit.Muted),
                (who, PlayOverlay.CivColor(l.Civ).Lightened(0.3f)),
                (": " + l.Text, l.Kind is PlayIntentLine.War ? UiKit.Bad : l.Kind is PlayIntentLine.Peace or PlayIntentLine.Diplomacy ? UiKit.Good : UiKit.Text),
            ]));
        }

        Callable.From(() => _intentScroll!.ScrollVertical = (int)_intentScroll.GetVScrollBar().MaxValue).CallDeferred();
    }

    /// <summary>A spectator watches through the eyes of a civilization (automated checks; −1: the whole map).</summary>
    public void ScriptEye(int civ) => WatchEye(civ);

    // Through these eyes from now on, and the camera to that civilization's home.
    private void WatchEye(int civ)
    {
        _session?.WatchThrough(civ);
        if (_session?.Overview?.FirstOrDefault(r => r.Civ == civ) is { CapitalCol: >= 0 } home)
        {
            Focus(home.CapitalCol, home.CapitalRow);
        }
    }

    /// <summary>A spectator opens the table of civilizations (automated checks).</summary>
    public void ScriptTable()
    {
        if (_tablePanel is not null)
        {
            _tablePanel.Visible = true;
        }
    }

    private void ForgetWatchUi()
    {
        _watchBar = null;
        _tablePanel = null;
        _intentPanel = null;
        _eyeShown = "";
        _tableShown = "";
        _intentsShown = -1;
    }
}
