using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The goods at the top left (Patryk, 2026-11-24): every good a chip with its picture, the stock and under it the trend, or
/// what is about to happen. The chip's frame warns: red when the good runs out within <see cref="RunsOutMinutes"/> minutes,
/// yellow when a workshop waits for it or the store is full (its makers stop). The tooltip lays out the store, the trend,
/// when it runs out, where it is missing, and who makes and who uses it (pictures and names, only what we know).
/// </summary>
public partial class PlayGame
{
    private const int RunsOutMinutes = 5;

    // State of a chip: 0 fine, 1 a store full or a workshop waiting, 2 running out soon.
    private readonly record struct GoodState(int Good, long Stock, long Rate, long Room, int Warn, string Trend, Color TrendColor, string Tip);

    private void UpdateGoods(PlayViewMessage v, PlayContentMessage c)
    {
        var cells = new List<GoodState>();
        for (int g = 0; g < v.Stock.Length; g++)
        {
            long rate = v.RatePerMinute[g];
            bool waited = (v.Alerts ?? []).Any(a => a.Kind == 3 && a.Good == g); // a workshop waits for it: show it, framed
            if (g > 2 && v.Stock[g] < 100 && rate == 0 && !waited)
            {
                continue;
            }

            cells.Add(StateOf(v, c, g));
        }

        string layout = string.Join(",", cells.Select(x => x.Good));
        string sig = $"{v.People}{v.Hungry}" + string.Join(";", cells.Select(x => $"{x.Good}:{x.Stock / 100}:{x.Rate}:{x.Warn}:{x.Trend}:{x.Tip.Length}"));
        if (sig == _resSig)
        {
            return;
        }

        _resSig = sig;
        string peopleTip = $"# People\n@All our settlements|{v.People}" + (v.Hungry ? "\nSome settlements go hungry!" : "");
        if (layout == _resLayout && _resBox.GetChildCount() == cells.Count + 1)
        {
            // The same goods as before: only numbers, frames and tooltips change in the chips that are there.
            SetGoodChip(_resBox.GetChild<Control>(0), $"{v.People}", v.Hungry ? "hungry" : "", v.Hungry ? UiKit.Bad : UiKit.Muted, v.Hungry ? 2 : 0, peopleTip);
            for (int k = 0; k < cells.Count; k++)
            {
                GoodState x = cells[k];
                SetGoodChip(_resBox.GetChild<Control>(k + 1), $"{x.Stock / 100}", x.Trend, x.TrendColor, x.Warn, x.Tip);
            }

            return;
        }

        _resLayout = layout;
        UiKit.Clear(_resBox);
        _resBox.AddChild(GoodChip(new Color("e0c090"), "P", null));
        SetGoodChip(_resBox.GetChild<Control>(0), $"{v.People}", v.Hungry ? "hungry" : "", v.Hungry ? UiKit.Bad : UiKit.Muted, v.Hungry ? 2 : 0, peopleTip);
        foreach (GoodState x in cells)
        {
            PlayGoodInfo good = c.Goods[x.Good];
            Control chip = GoodChip(UiKit.GoodColor(good.Key), UiKit.Short(good.Name)[..1], good.Key);
            _resBox.AddChild(chip);
            SetGoodChip(chip, $"{x.Stock / 100}", x.Trend, x.TrendColor, x.Warn, x.Tip);
        }
    }

    /// <summary>Screenshots: the mouse over the last good of the top bar.</summary>
    public void ScriptHoverGood()
    {
        if (_resBox.GetChildCount() > 0 && _resBox.GetChild(_resBox.GetChildCount() - 1) is Control chip)
        {
            ScriptMouseMove(chip.GetGlobalRect().GetCenter());
        }
    }

    private GoodState StateOf(PlayViewMessage v, PlayContentMessage c, int g)
    {
        PlayGoodInfo good = c.Goods[g];
        long stock = v.Stock[g];
        long rate = v.RatePerMinute[g];
        long room = v.Capacity[g];
        bool full = room > 0 && stock >= room * 98 / 100;
        long secondsLeft = rate < 0 && stock > 0 ? stock * 60 / -rate : -1;
        bool runsOut = rate < 0 && (stock <= 0 || secondsLeft < RunsOutMinutes * 60);
        var waiting = (v.Alerts ?? []).Where(a => a.Kind == 3 && a.Good == g).ToList();

        int warn = runsOut ? 2 : full || waiting.Count > 0 ? 1 : 0;
        string trend = full ? "full" : runsOut && secondsLeft >= 0 ? $"{UiKit.Rate(rate)} · {secondsLeft / 60}:{secondsLeft % 60:00}" : UiKit.Rate(rate);
        Color trendColor = runsOut ? UiKit.Bad : full ? UiKit.Warn : rate > 0 ? UiKit.Good : rate < 0 ? UiKit.Bad : UiKit.Muted;

        var tip = new List<string> { $"# {good.Name}", $"@In store|{stock / 100} of {room / 100} room, in all our settlements", $"@A minute|{UiKit.Rate(rate)}" };
        if (runsOut)
        {
            tip.Add(stock <= 0 ? "@Runs out|it is out: what needs it stands still" : $"@Runs out|in {secondsLeft / 60}:{secondsLeft % 60:00} at this pace");
        }
        else if (rate > 0 && room > 0 && !full)
        {
            long toFull = (room - stock) * 60 / rate;
            tip.Add($"@Store full|in {toFull / 60}:{toFull % 60:00}: then its makers stop");
        }

        if (full)
        {
            tip.Add("@Full|its makers stop: build a storehouse, use it or send it away");
        }

        foreach (PlayAlertView a in waiting.Take(4))
        {
            tip.Add("@Missing|" + a.Text);
        }

        // Who makes and who uses it, among the buildings we know (what we do not know stays hidden).
        string Names(IEnumerable<PlayBuildingInfo> list) => string.Join(", ", list.Take(6).Select(b => UiKit.GoodMark("p:buildings/building_" + b.Key) + b.Name.Replace(' ', ' ')));
        var known = c.Buildings.Where(b => b.Id != 0 && b.Tech >= 0 && b.Tech < v.Techs.Count && v.Techs[b.Tech].Known).ToList();
        var makers = known.Where(b => b.Produces.Length > g && b.Produces[g] > 0).ToList();
        var users = known.Where(b => b.Consumes.Length > g && b.Consumes[g] > 0).ToList();
        if (makers.Count + users.Count > 0)
        {
            tip.Add("");
        }

        if (makers.Count > 0)
        {
            tip.Add($"@Made by|{Names(makers)}");
        }

        if (users.Count > 0)
        {
            tip.Add($"@Used by|{Names(users)}");
        }

        return new GoodState(g, stock, rate, room, warn, trend, trendColor, string.Join('\n', tip));
    }

    // A chip: a frame, the good's picture, the stock over the trend.
    private static Control GoodChip(Color color, string letter, string? good)
    {
        var frame = new RichTipPanel { MouseFilter = Control.MouseFilterEnum.Stop };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 4);
        Texture2D? icon = good is null ? null : UiKit.Picture?.Invoke("goods", "good_" + good);
        row.AddChild(new Glyph(color, letter, icon is null ? 26 : 34, round: icon is null, icon: icon) { MouseFilter = Control.MouseFilterEnum.Ignore });
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", -4);
        Label amount = UiKit.Label("", 16);
        amount.MouseFilter = Control.MouseFilterEnum.Ignore;
        col.AddChild(amount);
        Label trend = UiKit.Label("", 12);
        trend.MouseFilter = Control.MouseFilterEnum.Ignore;
        col.AddChild(trend);
        row.AddChild(col);
        frame.AddChild(row);
        return frame;
    }

    private static readonly StyleBoxFlat[] ChipFrames =
    [
        UiKit.Flat(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), 4, 2),
        UiKit.Flat(new Color(0.3f, 0.24f, 0.06f, 0.55f), UiKit.Warn, 4, 2),
        UiKit.Flat(new Color(0.35f, 0.08f, 0.06f, 0.6f), UiKit.Bad, 4, 2),
    ];

    private static void SetGoodChip(Control chip, string amount, string trend, Color trendColor, int warn, string tip)
    {
        chip.TooltipText = tip;
        chip.AddThemeStyleboxOverride("panel", ChipFrames[Math.Clamp(warn, 0, 2)]);
        if (chip.GetChildOrNull<Control>(0)?.GetChildOrNull<Control>(1) is { } col && col.GetChildOrNull<Label>(0) is { } a && col.GetChildOrNull<Label>(1) is { } t)
        {
            a.Text = amount;
            t.Text = trend;
            t.AddThemeColorOverride("font_color", trendColor);
        }
    }
}
