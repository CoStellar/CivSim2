using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivSim.Game.Net;
using CivSim.Protocol.Client;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// The helmsman's controls (stage 6 PR P2, ADR 0027), shown only when the viewer leads a civilization: five lever tiles (who leads
/// each lever and the AI's last decision on it; click to take or give back), the research of the age, and a panel with orders for
/// what is selected on the map, research choices, diplomacy and "what the AI did for me". Every button sends a protocol command;
/// the server checks it and the refusal reason is shown.
/// </summary>
public partial class HelmHud : CanvasLayer
{
    private const float LeftWidth = 420;
    private const float TopHeight = 52;
    private const float PanelWidth = 330;

    private readonly ClientSession _session;
    private readonly Button[] _levers = new Button[5];
    private Label _progressText = null!;
    private ProgressBar _progress = null!;
    private Label _selectionTitle = null!;
    private VBoxContainer _orders = null!;
    private Label _hint = null!;
    private Label _error = null!;
    private VBoxContainer _research = null!;
    private VBoxContainer _diplomacy = null!;
    private RichTextLabel _aiLog = null!;
    private HelmSelection _selection = new(null, null);
    private (int World, int Col, int Row)? _selectedCell;
    private HelmAction? _pending;
    private string _ordersKey = string.Empty;
    private string _researchKey = string.Empty;
    private string _diplomacyKey = string.Empty;
    private long _aiLogTurn = -1;
    private double _errorTimer;

    /// <summary>Builds the controls for <paramref name="session"/> (a player session).</summary>
    public HelmHud(ClientSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Layer = 2;
        _session.CommandRefused += reason => ShowError(reason);
    }

    /// <summary>Required by Godot; not used.</summary>
    public HelmHud()
        : this(null!)
    {
    }

    /// <summary>True while an order waits for the player to click its target on the map.</summary>
    public bool Targeting => _pending is not null;

    /// <inheritdoc />
    public override void _Ready()
    {
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = PixelTheme.Current };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // Lever tiles and the age's research under the top bar, between the left panel and the order panel.
        var strip = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        strip.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        strip.OffsetLeft = LeftWidth + 8;
        strip.OffsetRight = -PanelWidth - 16;
        strip.OffsetTop = TopHeight + 4;
        root.AddChild(strip);
        var tiles = new HBoxContainer();
        tiles.AddThemeConstantOverride("separation", 4);
        strip.AddChild(tiles);
        for (int i = 0; i < 5; i++)
        {
            int lever = i + 1;
            var b = new Button
            {
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 44),
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            };
            b.Toggled += on => _session.SetLever(lever, on);
            tiles.AddChild(b);
            _levers[i] = b;
        }

        var line = new HBoxContainer();
        strip.AddChild(line);
        _progressText = new Label { CustomMinimumSize = new Vector2(260, 0), ClipText = true };
        line.AddChild(_progressText);
        _progress = new ProgressBar { MinValue = 0, MaxValue = 1000, ShowPercentage = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 14), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        line.AddChild(_progress);

        // Order panel on the right, above the chronicle.
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(PanelWidth, 0) };
        panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        panel.OffsetLeft = -PanelWidth - 8;
        panel.OffsetRight = -8;
        panel.OffsetTop = TopHeight + 4;
        panel.OffsetBottom = TopHeight + 4 + 420;
        root.AddChild(panel);
        var tabs = new TabContainer();
        panel.AddChild(tabs);

        var orders = new VBoxContainer { Name = "Orders" };
        tabs.AddChild(orders);
        _selectionTitle = new Label { Text = "Click one of your camps or units.", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        orders.AddChild(_selectionTitle);
        _hint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1f, 0.85f, 0.4f), Visible = false };
        orders.AddChild(_hint);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        orders.AddChild(scroll);
        _orders = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        scroll.AddChild(_orders);
        _error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1f, 0.45f, 0.4f), Visible = false };
        orders.AddChild(_error);

        var research = new ScrollContainer { Name = "Research", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(research);
        _research = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        research.AddChild(_research);

        var diplomacy = new ScrollContainer { Name = "Diplomacy", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        tabs.AddChild(diplomacy);
        _diplomacy = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        diplomacy.AddChild(_diplomacy);

        _aiLog = new RichTextLabel { Name = "AI did", BbcodeEnabled = true, ScrollFollowing = true, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        tabs.AddChild(_aiLog);
    }

    /// <summary>The player clicked a cell while choosing a target: sends the waiting order. Returns false when no order waits.</summary>
    public bool TargetClicked(int world, int col, int row)
    {
        if (_pending is not { } action || _session.View is not { } view)
        {
            return false;
        }

        SettlementView? target = HelmActions.SettlementAt(view, world, col, row);
        if (action.Target == HelmTarget.Settlement && target is null)
        {
            ShowError("Click a cell with a settlement.");
            return true;
        }

        Send(HelmActions.Build(action, _selection, col, row, target));
        CancelTarget();
        return true;
    }

    /// <summary>Stops choosing a target (Esc or right click).</summary>
    public void CancelTarget()
    {
        _pending = null;
        _hint.Visible = false;
        _ordersKey = string.Empty;
    }

    /// <summary>The player selected a cell (not while choosing a target): the order panel shows what of ours stands there.</summary>
    public void Select(int world, int col, int row)
    {
        _selectedCell = (world, col, row);
        _ordersKey = string.Empty;
        Refresh();
    }

    /// <summary>Updates tiles, research and panels from the latest view (a few times a second; rebuilds lists only when they change).</summary>
    public void Refresh()
    {
        if (_session.View is not { } view || _session.CivId < 0)
        {
            return;
        }

        RefreshLevers(view);
        RefreshProgress(view);
        RefreshOrders(view);
        RefreshResearch(view);
        RefreshDiplomacy(view);
        RefreshAiLog(view);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_errorTimer > 0 && (_errorTimer -= delta) <= 0)
        {
            _error.Visible = false;
        }
    }

    private void RefreshLevers(GameViewMessage view)
    {
        for (int i = 0; i < 5; i++)
        {
            int lever = i + 1;
            bool mine = HelmActions.Holds(view.PlayerLevers, lever);
            Button b = _levers[i];
            b.SetPressedNoSignal(mine);
            AiDecisionView? last = HelmActions.LastAiDecision(view, lever);
            string who = mine ? "you" : "AI";
            string news = !mine && last is not null ? ": " + last.Text : string.Empty;
            b.Text = $"{lever}. {HelmActions.LeverNames[i]} ({who}){news}";
            b.TooltipText = $"{HelmActions.LeverNames[i]}: {HelmActions.LeverHelp[i]}\n" +
                (mine ? "You lead it; the AI does not touch it. Click to give it back to the AI." : "The AI leads it. Click to take it.") +
                (last is not null ? $"\nLast AI decision: {last.Text}" : string.Empty);
        }
    }

    private void RefreshProgress(GameViewMessage view)
    {
        CivilizationView? civ = view.Civilizations.FirstOrDefault(c => c.Id == _session.CivId);
        string era = civ?.Era ?? string.Empty;
        if (view.Research is not { } r)
        {
            _progressText.Text = era;
            _progress.Value = 0;
            return;
        }

        string name = r.Researching.Length == 0 ? "nothing" : NameOf(r.Researching, r);
        _progressText.Text = string.Create(CultureInfo.InvariantCulture, $"{era} · researching {name} {r.ProgressPermille / 10}%");
        _progress.Value = r.ProgressPermille;
    }

    private void RefreshOrders(GameViewMessage view)
    {
        if (_selectedCell is not { } cell)
        {
            return;
        }

        _selection = HelmActions.Select(view, _session.CivId, cell.World, cell.Col, cell.Row);
        string key = $"{_selection.Settlement?.Id}|{_selection.Unit?.Id}|{_selection.Unit?.Order}|{view.PlayerLevers}|{_pending?.Key}";
        if (key == _ordersKey)
        {
            return;
        }

        _ordersKey = key;
        Clear(_orders);
        if (_selection.IsEmpty)
        {
            _selectionTitle.Text = "Nothing of ours here. Click one of your camps or units.";
            return;
        }

        var parts = new List<string>();
        if (_selection.Settlement is { } s)
        {
            parts.Add($"{s.Name} ({HelmActions.People(s)} people)");
        }

        if (_selection.Unit is { } u)
        {
            parts.Add($"{UnitRoleName(u.Role)} of {u.People} ({OrderName(u.Order)})");
        }

        _selectionTitle.Text = string.Join(" · ", parts);
        int lastLever = 0;
        foreach (HelmAction action in HelmActions.For(_selection, _session.Content))
        {
            if (action.Lever != lastLever)
            {
                lastLever = action.Lever;
                bool mine = HelmActions.Holds(view.PlayerLevers, action.Lever);
                _orders.AddChild(new Label
                {
                    Text = $"{HelmActions.LeverNames[action.Lever - 1]}{(mine ? string.Empty : " (AI leads; your order still holds)")}",
                    ThemeTypeVariation = "Heading",
                });
            }

            var b = new Button { Text = action.Label, TooltipText = action.Help, FocusMode = Control.FocusModeEnum.None, Alignment = HorizontalAlignment.Left, ToggleMode = action.Target != HelmTarget.None };
            b.SetPressedNoSignal(_pending?.Key == action.Key);
            HelmAction a = action;
            b.Pressed += () => Choose(a);
            _orders.AddChild(b);
        }
    }

    private void RefreshResearch(GameViewMessage view)
    {
        ResearchView? r = view.Research;
        string key = r is null ? string.Empty : r.Researching + "|" + string.Join(",", r.Options.Select(o => o.Key)) + "|" + view.PlayerLevers;
        if (key == _researchKey)
        {
            return;
        }

        _researchKey = key;
        Clear(_research);
        bool mine = HelmActions.Holds(view.PlayerLevers, 2);
        _research.AddChild(new Label
        {
            Text = mine ? "You choose what we research." : "The AI chooses (lever 2). Your choice holds until it is learned.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        if (r is null || r.Options.Count == 0)
        {
            _research.AddChild(new Label { Text = "Nothing can be researched now." });
            return;
        }

        foreach (TechnologyOption o in r.Options)
        {
            var b = new Button
            {
                Text = string.Create(CultureInfo.InvariantCulture, $"{o.Name} (cost {o.Cost})"),
                ToggleMode = true,
                FocusMode = Control.FocusModeEnum.None,
                Alignment = HorizontalAlignment.Left,
            };
            b.SetPressedNoSignal(o.Key == r.Researching);
            string k = o.Key;
            b.Pressed += () => Send(new SetResearchDto(k));
            _research.AddChild(b);
        }
    }

    private void RefreshDiplomacy(GameViewMessage view)
    {
        IReadOnlyList<ProposalView> proposals = view.Proposals ?? [];
        string key = string.Join(",", proposals.Select(p => $"{p.From}:{p.Kind}")) + "|" + string.Join(",", view.Civilizations.Where(c => c.IsAlive).Select(c => c.Id));
        if (key == _diplomacyKey)
        {
            return;
        }

        _diplomacyKey = key;
        Clear(_diplomacy);
        if (proposals.Count > 0)
        {
            _diplomacy.AddChild(new Label { Text = "Proposals to us", ThemeTypeVariation = "Heading" });
            foreach (ProposalView p in proposals)
            {
                var row = new HBoxContainer();
                row.AddChild(new Label { Text = $"{CivName(view, p.From)}: {TreatyName(p.Kind)}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClipText = true });
                ProposalView q = p;
                row.AddChild(SmallButton("Yes", () => Send(new AnswerProposalDto(q.From, q.Kind, true))));
                row.AddChild(SmallButton("No", () => Send(new AnswerProposalDto(q.From, q.Kind, false))));
                _diplomacy.AddChild(row);
            }
        }

        _diplomacy.AddChild(new Label { Text = "Peoples we know", ThemeTypeVariation = "Heading" });
        foreach (CivilizationView c in view.Civilizations)
        {
            if (!c.IsAlive || c.Id == _session.CivId)
            {
                continue;
            }

            _diplomacy.AddChild(new Label { Text = c.Name, ClipText = true });
            var row = new HBoxContainer();
            int id = c.Id;
            row.AddChild(SmallButton("War", () => Send(new DeclareWarDto(id, Goal: 1)), "Declare war for loot (needs real contact)."));
            for (int kind = 1; kind <= HelmActions.TreatyNames.Count; kind++)
            {
                int k = kind;
                row.AddChild(SmallButton(HelmActions.TreatyNames[kind - 1], () => Send(new ProposeTreatyDto(id, k)), $"Propose {HelmActions.TreatyNames[kind - 1]}."));
            }

            _diplomacy.AddChild(row);
        }
    }

    private void RefreshAiLog(GameViewMessage view)
    {
        IReadOnlyList<AiDecisionView> list = view.AiDecisions ?? [];
        long newest = list.Count == 0 ? -1 : list.Max(d => d.Turn) * 1000 + list.Count;
        if (newest == _aiLogTurn)
        {
            return;
        }

        _aiLogTurn = newest;
        _aiLog.Clear();
        foreach (AiDecisionView d in list)
        {
            string lever = d.Lever is >= 1 and <= 5 ? HelmActions.LeverNames[d.Lever - 1] : "simulation";
            _aiLog.AppendText(string.Create(CultureInfo.InvariantCulture, $"[color=#9a9a9a]{d.Turn / 10} s · {lever}[/color] {d.Text}\n"));
        }
    }

    private void Choose(HelmAction action)
    {
        if (action.Target == HelmTarget.None)
        {
            Send(HelmActions.Build(action, _selection));
            return;
        }

        if (_pending?.Key == action.Key)
        {
            CancelTarget();
            return;
        }

        _pending = action;
        _hint.Text = action.Target == HelmTarget.Cell ? $"{action.Label} Click a cell on the map (Esc cancels)." : $"{action.Label} Click a settlement on the map (Esc cancels).";
        _hint.Visible = true;
        _ordersKey = string.Empty;
        Refresh();
    }

    private void Send(ICommandDto? command)
    {
        if (command is null)
        {
            ShowError("That order needs a target.");
            return;
        }

        _error.Visible = false;
        _session.Command(command);
    }

    private void ShowError(string text)
    {
        _error.Text = "Refused: " + text;
        _error.Visible = true;
        _errorTimer = 6;
    }

    private static Button SmallButton(string text, Action pressed, string tip = "")
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += pressed;
        return b;
    }

    private static void Clear(Node box)
    {
        foreach (Node child in box.GetChildren())
        {
            box.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string NameOf(string key, ResearchView r) => r.Options.FirstOrDefault(o => o.Key == key)?.Name ?? key.Replace('_', ' ');

    private static string CivName(GameViewMessage view, int id) => view.Civilizations.FirstOrDefault(c => c.Id == id)?.Name ?? $"people {id}";

    private static string TreatyName(int kind) => kind >= 1 && kind <= HelmActions.TreatyNames.Count ? HelmActions.TreatyNames[kind - 1] : "treaty";

    private static string UnitRoleName(int role) => role switch
    {
        0 => "Settlers",
        1 => "Scouts",
        2 => "Army",
        3 => "Carriers",
        4 => "Work group",
        _ => "Unit",
    };

    private static string OrderName(int order) => order switch
    {
        0 => "waiting",
        1 => "walking",
        2 => "going to found a camp",
        3 => "scouting",
        4 => "going home",
        5 => "attacking",
        6 => "fleeing",
        7 => "working",
        _ => "busy",
    };
}
