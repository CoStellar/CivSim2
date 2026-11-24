using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The alert strip at the top right (Patryk, 2026-11-24): one small icon per kind of alert, framed in the colour of how
/// serious it is, with the number of such alerts on it. The texts show only in the icon's tooltip, one after another, so the
/// screen keeps its room. A click goes to the next place of that kind (or opens the research tree). The advisor is the first
/// icon of the strip.
/// </summary>
public partial class PlayGame
{
    // Index = alert kind (AlertKind in the simulation): its name and how serious it is (2 critical, 1 warning, 0 notice).
    private static readonly (string Name, int Weight)[] AlertKinds =
    [
        ("Under attack", 2), ("Enemies near", 2), ("Hunger", 2), ("Goods missing", 0), ("No homes", 0), ("Store full", 0),
        ("Materials lost", 1), ("Clothing for winter", 1), ("Unit rations", 1), ("Food running out", 1), ("Nothing researched", 0),
        ("Strangers", 1),
    ];

    private static readonly Color[] WeightColors = [new(0.93f, 0.78f, 0.3f), new(0.95f, 0.55f, 0.2f), new(0.9f, 0.25f, 0.2f)];
    private readonly Dictionary<int, int> _alertNext = [];

    // The icons by key (-1 the advisor, else the alert kind) with what they show, so only a changed icon is made again and a
    // hovered one keeps its tooltip while other alerts come and go.
    private readonly Dictionary<int, (Control Icon, string Look)> _alertIcons = [];
    private readonly Dictionary<int, PlayAlertView[]> _alertGroups = [];

    private void UpdateAlerts(PlayViewMessage v)
    {
        IReadOnlyList<PlayAlertView> alerts = v.Alerts ?? [];
        string advice = v.Picking ? "" : AdvisorTip(v);
        string sig = advice + "#" + string.Join("|", alerts.Select(a => $"{a.Kind}:{a.Text}"));
        if (sig == _alertSig)
        {
            return;
        }

        _alertSig = sig;
        var wanted = new List<(int Key, string Look, string Tip, Func<Control> Make)>();
        if (advice.Length > 0)
        {
            // Grey: no advisor; green: ours, nothing to worry about; orange: ours, and he warns of something.
            bool worry = v.Advice is not null;
            bool have = v.Advisor is not null;
            Color adColor = !have ? new Color(0.5f, 0.5f, 0.5f) : worry ? WeightColors[1] : new Color(0.45f, 0.62f, 0.4f);
            wanted.Add((-1, $"ad{worry}{have}", advice, () => AlertIcon(
                adColor, UiKit.Picture?.Invoke("ui", "advisor"), "Ad", 0, advice,
                () => OpenWindow("Thinkers", toggle: false))));
        }

        // Most serious first, then by kind; the alerts of a kind in the order the server gave them.
        _alertGroups.Clear();
        foreach (IGrouping<int, PlayAlertView> group in alerts.GroupBy(a => a.Kind).OrderByDescending(g => Weight(g.Key)).ThenBy(g => g.Key))
        {
            int kind = group.Key;
            PlayAlertView[] all = [.. group];
            _alertGroups[kind] = all;
            int good = (kind is 3 or 5) && all.All(a => a.Good == all[0].Good) ? all[0].Good : -1;
            string tip = AlertTip(kind, all);
            wanted.Add((kind, $"{all.Length}:{good}", tip, () =>
            {
                Texture2D? pic = good >= 0 && _session?.PlayContent is { } pc && good < pc.Goods.Count ? UiKit.Picture?.Invoke("goods", "good_" + pc.Goods[good].Key) : null;
                pic ??= UiKit.Picture?.Invoke("ui", "alert_" + kind);
                return AlertIcon(WeightColors[Weight(kind)], pic, "!", all.Length, tip, () => AlertClicked(kind));
            }));
        }

        foreach (int key in _alertIcons.Keys.Where(k => !wanted.Any(w => w.Key == k)).ToList())
        {
            _alertIcons[key].Icon.QueueFree();
            _alertIcons.Remove(key);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            (int key, string look, string tip, Func<Control> make) = wanted[i];
            if (_alertIcons.TryGetValue(key, out (Control Icon, string Look) have) && have.Look == look)
            {
                have.Icon.TooltipText = tip;
            }
            else
            {
                have.Icon?.QueueFree();
                have = (make(), look);
                _alertIcons[key] = have;
                _alertsBox.AddChild(have.Icon);
            }

            _alertsBox.MoveChild(have.Icon, i);
        }
    }

    /// <summary>Screenshots: hires the advisor on offer, if any.</summary>
    public void ScriptHireAdvisor()
    {
        if (_session?.PlayView is { Advisor: null, AdvisorOffer: not null })
        {
            _session.Play(PlayCommandKind.Hire, 0, flag: true);
        }
    }

    /// <summary>Screenshots: the mouse over the advisor's icon.</summary>
    public void ScriptHoverAdvisor()
    {
        if (_alertIcons.TryGetValue(-1, out (Control Icon, string Look) ad))
        {
            ScriptMouseMove(ad.Icon.GetGlobalRect().GetCenter());
        }
    }

    // A click on an alert icon: the research tree, or the next place of that kind.
    private void AlertClicked(int kind)
    {
        if (kind == 10)
        {
            OpenWindow("Research", toggle: false);
            return;
        }

        if (!_alertGroups.TryGetValue(kind, out PlayAlertView[]? all) || all.Length == 0)
        {
            return;
        }

        int k = _alertNext.GetValueOrDefault(kind) % all.Length;
        _alertNext[kind] = k + 1;
        Focus(all[k].Col, all[k].Row);
    }

    // The tooltip of a kind: its name, then every alert of it one after another.
    private static string AlertTip(int kind, PlayAlertView[] all)
    {
        string name = kind >= 0 && kind < AlertKinds.Length ? AlertKinds[kind].Name : "Alert";
        var tip = new List<string> { $"# {name}" + (all.Length > 1 ? $" ({all.Length})" : "") };
        foreach (PlayAlertView a in all.Take(12))
        {
            tip.Add("• " + a.Text);
        }

        if (all.Length > 12)
        {
            tip.Add($"…and {all.Length - 12} more (Settlements, key 1)");
        }

        tip.Add("");
        tip.Add(kind == 10 ? "Click: open the research tree." : all.Length > 1 ? "Click: go to the next place." : "Click: go there.");
        return string.Join('\n', tip);
    }

    private static int Weight(int kind) => kind >= 0 && kind < AlertKinds.Length ? AlertKinds[kind].Weight : 1;

    private static string AdvisorTip(PlayViewMessage v)
    {
        if (v.Advisor is { } a)
        {
            return $"# {a.Name}, {a.Rarity.ToLowerInvariant()} advisor\n{v.Advice ?? "Nothing to worry about right now."}\n\nClick: Thinkers (4).";
        }

        return v.AdvisorOffer is { } o
            ? $"# No advisor\n{o.Name} offers advice on {o.Fields}. Hire in Thinkers (4)."
            : "# No advisor\nNobody tells us what goes wrong. Advisors come with Thinkers (4).";
    }

    // One square icon: the picture (or a letter), the frame in the colour of its weight, the count in a corner.
    private static Control AlertIcon(Color color, Texture2D? pic, string letter, int count, string tip, Action click)
    {
        const int Size = 46;
        var box = new RichTipPanel { TooltipText = tip, MouseFilter = Control.MouseFilterEnum.Stop, MouseDefaultCursorShape = Control.CursorShape.PointingHand, CustomMinimumSize = new Vector2(Size, Size) };
        StyleBoxFlat normal = UiKit.Flat(new Color(0.12f, 0.1f, 0.09f, 0.94f), color, 4, 3);
        StyleBoxFlat hover = UiKit.Flat(new Color(0.24f, 0.19f, 0.15f, 0.96f), color, 4, 3);
        box.AddThemeStyleboxOverride("panel", normal);
        box.MouseEntered += () => box.AddThemeStyleboxOverride("panel", hover);
        box.MouseExited += () => box.AddThemeStyleboxOverride("panel", normal);
        box.AddChild(new Glyph(color.Darkened(0.4f), letter, Size - 12, round: true, icon: pic) { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
        if (count > 1)
        {
            var badge = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(Size - 22, Size - 22), Size = new Vector2(20, 18) };
            badge.AddThemeStyleboxOverride("panel", UiKit.Flat(color, color, 6, 0));
            Label n = UiKit.Label(count > 99 ? "99" : count.ToString(System.Globalization.CultureInfo.InvariantCulture), 11, new Color(0.08f, 0.06f, 0.05f));
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.MouseFilter = Control.MouseFilterEnum.Ignore;
            badge.AddChild(n);
            var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            holder.AddChild(badge);
            box.AddChild(holder);
        }

        box.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                click();
            }
        };
        return box;
    }
}

/// <summary>A panel whose tooltip is drawn with <see cref="UiKit.RichTip"/> (headings, lines, pictures).</summary>
public partial class RichTipPanel : PanelContainer
{
    /// <inheritdoc/>
    public override GodotObject _MakeCustomTooltip(string forText) => UiKit.RichTip(forText);
}
