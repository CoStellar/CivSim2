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
/// The civilization tab of the left panel (stage 5): a table of every civilization (click a row to follow it), the
/// statistics chart, and everything about the followed one: era, people by stratum, settlements, food, research,
/// character, goals, stores and relations with the others.
/// </summary>
public partial class CivilizationPanel : VBoxContainer
{
    private Tree _table = null!;
    private HistoryChart _chart = null!;
    private RichTextLabel _details = null!;
    private int _followed = WelcomeMessage.NoCiv;
    private object? _lastView;
    private object? _lastDetails;
    private bool _filling;

    /// <summary>Raised when the user clicks a civilization in the table.</summary>
    public event Action<int>? FollowChosen;

    /// <summary>Raised when the user wants to rename the followed civilization: (id, current name).</summary>
    public event Action<int, string>? RenameRequested;

    private Button _rename = null!;

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 6);
        _table = new Tree
        {
            Columns = 5,
            HideRoot = true,
            ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(0, 170),
            SelectMode = Tree.SelectModeEnum.Row,
            FocusMode = FocusModeEnum.None,
        };
        string[] titles = ["Civilization", "Era", "People", "Sett.", "Tech"];
        for (int c = 0; c < titles.Length; c++)
        {
            _table.SetColumnTitle(c, titles[c]);
            _table.SetColumnExpand(c, c == 0);
            _table.SetColumnCustomMinimumWidth(c, c == 0 ? 120 : c == 1 ? 80 : 52);
        }

        _table.ItemSelected += () =>
        {
            if (!_filling && _table.GetSelected() is { } item)
            {
                FollowChosen?.Invoke((int)item.GetMetadata(0).AsInt64());
            }
        };
        AddChild(_table);

        var measures = new HBoxContainer();
        AddChild(measures);
        var group = new ButtonGroup();
        foreach (HistoryChart.Measure m in Enum.GetValues<HistoryChart.Measure>())
        {
            var b = new Button { Text = m.ToString(), ToggleMode = true, ButtonGroup = group, FocusMode = FocusModeEnum.None, ButtonPressed = m == HistoryChart.Measure.People };
            HistoryChart.Measure measure = m;
            b.Pressed += () =>
            {
                _chart.Shown = measure;
                _chart.Refresh();
            };
            measures.AddChild(b);
        }

        _chart = new HistoryChart { CustomMinimumSize = new Vector2(0, 150), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_chart);
        _rename = new Button { Text = "Rename people", Visible = false, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        _rename.Pressed += () =>
        {
            if (_lastDetails is CivilizationDetailsMessage d)
            {
                RenameRequested?.Invoke(d.CivId, d.Name);
            }
        };
        AddChild(_rename);
        _details = new RichTextLabel { BbcodeEnabled = true, SizeFlagsVertical = SizeFlags.ExpandFill, Text = "Pick a civilization to follow." };
        AddChild(_details);
    }

    /// <summary>Refreshes the table, the chart and the details.</summary>
    public void UpdateFrom(ClientSession session)
    {
        _chart.Show(session.History, session.WatchedCiv);
        if (!ReferenceEquals(session.View, _lastView) || session.WatchedCiv != _followed)
        {
            _lastView = session.View;
            _followed = session.WatchedCiv;
            FillTable(session.View);
        }

        if (!ReferenceEquals(session.CivDetails, _lastDetails))
        {
            _lastDetails = session.CivDetails;
            _details.Text = session.CivDetails is { } d ? Describe(d, session) : session.WatchedCiv < 0 ? "Pick a civilization to follow." : "Waiting for the server...";
            _rename.Visible = session.CivDetails is not null && session.CivId == WelcomeMessage.NoCiv;
        }
    }

    private void FillTable(GameViewMessage? view)
    {
        if (view is null)
        {
            return;
        }

        _filling = true;
        _table.Clear();
        TreeItem root = _table.CreateItem();
        var civs = new List<CivilizationView>(view.Civilizations);
        civs.Sort((a, b) => a.IsAlive != b.IsAlive ? b.IsAlive.CompareTo(a.IsAlive) : b.People.CompareTo(a.People));
        CultureInfo inv = CultureInfo.InvariantCulture;
        foreach (CivilizationView c in civs)
        {
            TreeItem item = _table.CreateItem(root);
            string name = c.Title.Length > 0 ? c.Title : c.Name;
            item.SetText(0, c.IsAlive ? name : name + " †");
            item.SetCustomColor(0, c.IsAlive ? OverlayColors.CivColor(c.Id) : Colors.Gray);
            item.SetText(1, c.Era);
            item.SetText(2, c.People < 0 ? "?" : c.People.ToString("N0", inv));
            item.SetText(3, c.Settlements < 0 ? "?" : c.Settlements.ToString(inv));
            item.SetText(4, c.Technologies < 0 ? "?" : c.Technologies.ToString(inv));
            item.SetMetadata(0, c.Id);
            if (c.Id == _followed)
            {
                item.Select(0);
            }
        }

        _filling = false;
    }

    private static string Describe(CivilizationDetailsMessage d, ClientSession session)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        string color = OverlayColors.CivColor(d.CivId).ToHtml(false);
        sb.Append(inv, $"[b][color=#{color}]{(d.Title.Length > 0 ? d.Title : d.Name)}[/color][/b]{(d.IsAlive ? string.Empty : "  (died out)")}\n");
        sb.Append(inv, $"Era: {d.Era}\n");
        if (d.Government.Length > 0)
        {
            sb.Append(inv, $"Government: {d.Government}\n");
        }

        if (d.Leader.Length > 0)
        {
            sb.Append(inv, $"Leader: {d.Leader}\n");
        }

        long people = 0;
        var strata = new List<string>();
        for (int k = 0; k < d.People.Count; k++)
        {
            people += d.People[k];
            strata.Add(string.Create(inv, $"{Pick(d.Strata, k)} {d.People[k]:N0}"));
        }

        sb.Append(inv, $"People: [b]{people:N0}[/b] ({string.Join(", ", strata)})\n");
        var levels = new List<string>();
        for (int k = 0; k < d.Settlements.Count; k++)
        {
            if (d.Settlements[k] > 0)
            {
                levels.Add(string.Create(inv, $"{d.Settlements[k]} × {Pick(d.SettlementLevels, k)}"));
            }
        }

        sb.Append("Settlements: ").Append(levels.Count == 0 ? "none" : string.Join(", ", levels)).Append('\n');
        int fed = d.FoodNeeded <= 0 ? 100 : (int)Math.Min(999, d.FoodGathered * 100 / d.FoodNeeded);
        sb.Append(inv, $"Food last turn: {d.FoodGathered / 100:N0} gathered for {d.FoodNeeded / 100:N0} needed ({fed}%)\n");

        sb.Append(inv, $"\n[b]Research[/b]  ({d.Technologies.Count} known, science {d.ScienceLastTurn / 100.0:F1} last turn)\n");
        sb.Append(d.Researching.Length > 0 ? string.Create(inv, $"Now: {d.Researching} {d.ResearchPercent}%\n") : "Now: nothing\n");
        sb.Append("[color=gray]").Append(d.Technologies.Count == 0 ? "no technologies" : string.Join(", ", d.Technologies)).Append("[/color]\n");

        sb.Append("\n[b]Character[/b] (0–1000)\n");
        for (int k = 0; k < d.Traits.Count; k++)
        {
            sb.Append(inv, $"{Pick(d.TraitNames, k)} {d.Traits[k]}{(k % 2 == 1 ? "\n" : "    ")}");
        }

        sb.Append("\n[b]Goals of the strategist[/b] (weight ×1000)\n");
        for (int k = 0; k < d.GoalWeights.Count; k++)
        {
            sb.Append(inv, $"{Pick(d.GoalNames, k)} {d.GoalWeights[k]}    ");
        }

        sb.Append("\n\n[b]Stores[/b]\n");
        var goods = new List<(long Amount, string Name)>();
        for (int g = 0; g < d.Stock.Count; g++)
        {
            if (d.Stock[g] > 0)
            {
                goods.Add((d.Stock[g], Pick(d.Goods, g)));
            }
        }

        goods.Sort((a, b) => b.Amount.CompareTo(a.Amount));
        sb.Append(goods.Count == 0 ? "empty" : string.Join(", ", goods.ConvertAll(x => string.Create(inv, $"{x.Name} {x.Amount / 100:N0}")))).Append('\n');

        sb.Append("\n[b]Relations[/b]\n");
        if (d.Relations.Count == 0)
        {
            sb.Append("[color=gray]knows no one yet[/color]\n");
        }

        foreach (RelationDetail r in d.Relations)
        {
            string other = CivName(session, r.Other);
            string oc = OverlayColors.CivColor(r.Other).ToHtml(false);
            string contact = r.Contact >= RelationView.Met ? string.Empty : " (rumor)";
            sb.Append(inv, $"[color=#{oc}]{other}[/color]{contact}: sympathy {r.Sympathy / 100}, trust {r.Trust / 100}{Treaties(r.Treaties)}\n");
            if (r.Memories.Count > 0)
            {
                sb.Append("  [color=gray]").Append(string.Join(", ", r.Memories)).Append("[/color]\n");
            }
        }

        return sb.ToString();
    }

    private static string Treaties(int flags)
    {
        var list = new List<string>();
        if ((flags & RelationView.War) != 0)
        {
            list.Add("[color=#ec271e]WAR[/color]");
        }

        if ((flags & RelationView.Peace) != 0)
        {
            list.Add("peace");
        }

        if ((flags & RelationView.Trade) != 0)
        {
            list.Add("trade");
        }

        if ((flags & RelationView.NonAggression) != 0)
        {
            list.Add("pact");
        }

        if ((flags & RelationView.Alliance) != 0)
        {
            list.Add("alliance");
        }

        return list.Count == 0 ? string.Empty : " — " + string.Join(", ", list);
    }

    private static string Pick(IReadOnlyList<string> names, int k) => k < names.Count ? names[k] : k.ToString(CultureInfo.InvariantCulture);

    /// <summary>Name of a civilization from the latest view.</summary>
    public static string CivName(ClientSession session, int id)
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
