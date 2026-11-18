using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>The windows in the middle of the screen: settlements, research tree, peoples, thinkers and the summary.</summary>
public partial class PlayGame
{
    private static readonly string[] Categories = ["food", "industry", "building", "trade", "military", "governance", "science"];

    private void UpdateWindow(PlayViewMessage v, PlayContentMessage c)
    {
        _window.Visible = _bigMode.Length > 0;
        if (_bigMode.Length == 0)
        {
            return;
        }

        if (_bigMode == "People")
        {
            _bigMode = "Thinkers";
        }

        _windowTitle.Text = _bigMode switch
        {
            "Research" => "Research",
            "Settlements" => "Our settlements",
            "Peoples" => "Other peoples",
            "Thinkers" => "Thinkers and advisors",
            _ => "Summary",
        };
        var items = new List<UiItem>();
        switch (_bigMode)
        {
            case "Research":
                ResearchItems(v, c, items);
                break;
            case "Settlements":
                SettlementTable(v, c, items);
                break;
            case "Peoples":
                PeoplesItems(v, c, items);
                break;
            case "Thinkers":
                ThinkerItems(v, c, items);
                break;
            default:
                SummaryItems(v, items);
                break;
        }

        string sig = _bigMode + UiRender.Sig(items);
        if (sig == _bigSignature)
        {
            return;
        }

        _bigSignature = sig;
        UiRender.Render(_windowBox, items, () => _uiTimer = 0.1);
    }

    // The tree as a grid: an era a column, a branch a row; each technology a card coloured by what we can do with it.
    private void ResearchItems(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        if (v.Researching >= 0)
        {
            items.Add(new BarItem("Researching", v.ResearchPermille / 1000f, $"{c.Techs[v.Researching].Name}  {v.ResearchPermille / 10}%", new Color("5a8ad0")));
        }
        else
        {
            items.Add(new LineItem("Nothing is being researched. Click a bright card to start.", UiKit.Warn, 15));
        }

        if (v.Thinker is { } t)
        {
            items.Add(new LineItem($"Our thinker {t.Name} ({t.Rarity.ToLowerInvariant()}): research costs {t.CostPercent}%, needs {t.ExperiencePercent}% of the experience.", UiKit.Muted, 13));
        }

        items.Add(new LineItem("Green: known.  Bright: can start now.  Dim: needs something first (hover to see what).  An era opens after five technologies of the one before.", UiKit.Muted, 13));
        var sig = new StringBuilder();
        foreach (PlayTechState st in v.Techs)
        {
            sig.Append(st.Known ? 'K' : st.Blocked is null ? 'A' : 'B').Append(st.Blocked).Append(st.Price is null ? "" : string.Join(",", st.Price));
        }

        sig.Append(v.Researching).Append(v.Era).Append(v.EraKnown);
        items.Add(new CustomItem(sig.ToString(), () => TechGrid(v, c)));
    }

    private Control TechGrid(PlayViewMessage v, PlayContentMessage c)
    {
        var grid = new GridContainer { Columns = c.Eras.Count + 1 };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        grid.AddChild(new Control());
        for (int era = 0; era < c.Eras.Count; era++)
        {
            string state = era <= v.Era ? "" : era == v.Era + 1 && v.EraNeed > 0 ? $"opens at {v.EraNeed} of the {c.Eras[v.Era]} ({v.EraKnown} now)" : "later";
            var head = new VBoxContainer { CustomMinimumSize = new Vector2(UiKit.W(176), 0) };
            head.AddChild(UiKit.Label(c.Eras[era], 16, era <= v.Era ? UiKit.Heading : UiKit.Muted));
            if (state.Length > 0)
            {
                head.AddChild(UiKit.Label(state, 11, UiKit.Muted, wrap: true));
            }

            grid.AddChild(head);
        }

        foreach (string cat in Categories)
        {
            var label = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin };
            label.AddChild(new Glyph(UiKit.CategoryColor(cat), UiKit.Short(cat)[..1], 24, round: true) { SizeFlagsVertical = Control.SizeFlags.ShrinkBegin });
            label.AddChild(UiKit.Label(char.ToUpperInvariant(cat[0]) + cat[1..], 14, UiKit.Text));
            grid.AddChild(label);
            for (int era = 0; era < c.Eras.Count; era++)
            {
                var cell = new VBoxContainer();
                cell.AddThemeConstantOverride("separation", 3);
                foreach (PlayTechInfo info in c.Techs)
                {
                    if (info.Era == era && info.Category == cat)
                    {
                        cell.AddChild(TechCard(v, c, info));
                    }
                }

                grid.AddChild(cell);
            }
        }

        return grid;
    }

    private Button TechCard(PlayViewMessage v, PlayContentMessage c, PlayTechInfo info)
    {
        PlayTechState st = v.Techs[info.Id];
        bool now = v.Researching == info.Id;
        Color cat = UiKit.CategoryColor(info.Category);
        (Color bg, Color border, Color ink) = st.Known ? (new Color(0.16f, 0.3f, 0.14f), new Color(0.4f, 0.7f, 0.35f), UiKit.Text)
            : now ? (new Color(0.15f, 0.22f, 0.36f), new Color(0.45f, 0.65f, 1f), UiKit.Text)
            : st.Blocked is null ? (new Color(0.3f, 0.24f, 0.12f), UiKit.Heading, UiKit.Text)
            : (new Color(0.12f, 0.11f, 0.1f), new Color(0.28f, 0.25f, 0.22f), UiKit.Muted);
        string name = info.Upgrade ? $"{info.Name} +" : info.Name;
        var tip = new StringBuilder($"{info.Name}" + (info.Upgrade ? " (upgrade of its era)" : "") + $"\n{info.Gives}");
        if (info.Unlocks.Length > 0)
        {
            tip.Append($"\n\n{info.Unlocks}");
        }
        if (!st.Known)
        {
            tip.Append($"\n\nPrice: {(st.Price is null ? "?" : Amounts(c, st.Price))}, {info.Seconds} s.");
            if (info.Needs.Length > 0)
            {
                tip.Append($"\nNeeds experience: {info.Needs} worker-minutes.");
            }

            if (st.Blocked is { } why)
            {
                tip.Append($"\n\n{why}");
            }
        }

        var b = new Button { Text = name, FocusMode = Control.FocusModeEnum.None, TooltipText = tip.ToString(), CustomMinimumSize = new Vector2(UiKit.W(176), Ui.PixelTheme.FontSize + 14), Alignment = HorizontalAlignment.Left, ClipText = true };
        b.AddThemeFontSizeOverride("font_size", Ui.PixelTheme.FontSize);
        b.AddThemeColorOverride("font_color", ink);
        b.AddThemeColorOverride("font_hover_color", UiKit.Text);
        StyleBoxFlat normal = UiKit.Flat(bg, border, 4, 2);
        normal.BorderWidthLeft = 6;
        normal.BorderColor = border;
        b.AddThemeStyleboxOverride("normal", normal);
        b.AddThemeStyleboxOverride("hover", UiKit.Flat(bg.Lightened(0.12f), border, 4, 2));
        b.AddThemeStyleboxOverride("pressed", normal);
        b.AddThemeStyleboxOverride("disabled", normal);
        _ = cat;
        int id = info.Id;
        if (!st.Known && st.Blocked is null && !now)
        {
            b.Pressed += () =>
            {
                _session!.Play(PlayCommandKind.Research, id);
                _uiTimer = 0.1;
            };
        }

        return b;
    }

    private void SettlementTable(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        var mine = v.Settlements.Where(s => s.Civ == v.Civ && s.Stock is not null).ToList();
        items.Add(new LineItem($"{mine.Count} settlement(s). Click a name to go there.", UiKit.Muted, 13));
        int[] shown = [0, 1, 2, 3];
        var cells = new List<CellItem> { new("Settlement", UiKit.Heading), new("Level", UiKit.Heading), new("People", UiKit.Heading) };
        foreach (int g in shown)
        {
            cells.Add(new CellItem(c.Goods[g].Name, UiKit.Heading));
        }

        cells.Add(new CellItem("Needs you", UiKit.Heading));
        foreach (PlaySettlementView s in mine)
        {
            int col = s.Col;
            int row = s.Row;
            cells.Add(new CellItem(s.Name, null, () =>
            {
                Focus(col, row);
                _bigMode = "";
            }));
            cells.Add(new CellItem(c.Levels[s.Level].Name, UiKit.Muted));
            cells.Add(new CellItem($"{s.People}/{s.Housing}", s.People >= s.Housing ? UiKit.Warn : UiKit.Text));
            foreach (int g in shown)
            {
                long rate = s.Rate![g];
                cells.Add(new CellItem($"{s.Stock![g] / 100} ({UiKit.Rate(rate)})", rate < 0 ? UiKit.Bad : rate > 0 ? UiKit.Good : UiKit.Muted));
            }

            var alerts = (v.Alerts ?? []).Where(a => a.Settlement == s.Id).ToList();
            cells.Add(new CellItem(alerts.Count == 0 ? "-" : string.Join("; ", alerts.Select(a => AlertWord(a.Kind))), alerts.Any(a => a.Kind <= 2) ? UiKit.Bad : alerts.Count > 0 ? UiKit.Warn : UiKit.Muted));
        }

        items.Add(new TableItem(4 + shown.Length, cells));
    }

    private static string AlertWord(int kind) => kind switch
    {
        0 => "attacked",
        1 => "enemy near",
        2 => "hungry",
        3 => "needs goods",
        4 => "no homes",
        5 => "store full",
        _ => "materials missing",
    };

    private void PeoplesItems(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        if (v.Peoples.Count == 0)
        {
            items.Add(new LineItem("We have met no other people yet. Send scouts to find them.", UiKit.Muted, 15));
        }

        foreach (PlayPeopleView p in v.Peoples)
        {
            int other = p.Civ;
            string status = p.Dead ? "gone" : p.AtWar ? "AT WAR" : p.Alliance ? "allies" : p.Trade ? "trade partners" : "at peace";
            items.Add(new HeaderItem(p.Name, status + (p.Reputation < 0 ? $" - known for treachery ({p.Reputation})" : ""), p.AtWar ? new Color("b84040") : new Color("6a7a5a"), UiKit.Short(p.Name)));
            if (p.Dead)
            {
                continue;
            }

            if (p.AtWar)
            {
                items.Add(new LineItem($"War so far: we {p.OurScore}, they {p.TheirScore}.", UiKit.Text, 14));
                int fair = p.SuggestedTribute;
                items.Add(new ActItem(fair > 0 ? $"Offer peace: they pay us {fair}% of their goods" : fair < 0 ? $"Offer peace: we pay them {-fair}% of our goods" : "Offer peace without tribute",
                    () => _session!.Play(PlayCommandKind.ProposePeace, other, b: fair), "The tribute the course of the war suggests."));
                if (fair <= 0)
                {
                    items.Add(new ActItem("Offer peace demanding 10% from them", () => _session!.Play(PlayCommandKind.ProposePeace, other, b: 10)));
                }

                if (p.TheyOfferTribute is { } t)
                {
                    items.Add(new LineItem(t > 0 ? $"They offer peace if we pay {t}% of our goods." : t < 0 ? $"They offer peace and {-t}% of their goods." : "They offer peace.", UiKit.Warn, 14));
                    items.Add(new ActItem("Accept their peace", () => _session!.Play(PlayCommandKind.AnswerPeace, other, flag: true), Accent: UiKit.Good));
                    items.Add(new ActItem("Refuse their peace", () => _session!.Play(PlayCommandKind.AnswerPeace, other, flag: false)));
                }

                continue;
            }

            if (p.TheyOfferTrade)
            {
                items.Add(new LineItem("They offer a trade agreement.", UiKit.Warn, 14));
                items.Add(new ActItem("Accept the trade agreement", () => _session!.Play(PlayCommandKind.AnswerTreaty, other, b: 0, flag: true), Accent: UiKit.Good));
            }
            else if (!p.Trade)
            {
                items.Add(new ActItem("Offer a trade agreement", () => _session!.Play(PlayCommandKind.ProposeTreaty, other, b: 0), "Barter by caravans; research 5% cheaper. Needs Barter, and they must be close and know us for a while."));
            }

            if (p.TheyOfferAlliance)
            {
                items.Add(new LineItem("They offer an alliance.", UiKit.Warn, 14));
                items.Add(new ActItem("Accept the alliance", () => _session!.Play(PlayCommandKind.AnswerTreaty, other, b: 1, flag: true), Accent: UiKit.Good));
            }
            else if (p.Trade && !p.Alliance)
            {
                items.Add(new ActItem("Offer an alliance", () => _session!.Play(PlayCommandKind.ProposeTreaty, other, b: 1), "They join a war declared on us."));
            }

            if (p.Trade)
            {
                foreach ((string text, int give, int want) in BarterIdeas(v, c))
                {
                    items.Add(new ActItem(text, () => _session!.Play(PlayCommandKind.Barter, other, 20, want, b: give, c: 10), "Two caravans carry the goods; nothing changes hands until they arrive."));
                }

                items.Add(new ActItem("End the trade agreement", () => _session!.Play(PlayCommandKind.CancelTreaty, other, b: 0)));
            }

            if (p.TheirBarter is { } offer)
            {
                items.Add(new LineItem($"They offer {offer}.", UiKit.Warn, 14));
                items.Add(new ActItem("Accept the barter", () => _session!.Play(PlayCommandKind.AnswerBarter, other, flag: true), Accent: UiKit.Good));
                items.Add(new ActItem("Refuse the barter", () => _session!.Play(PlayCommandKind.AnswerBarter, other, flag: false)));
            }

            items.Add(new ActItem($"Declare war on {p.Name}", () => _session!.Play(PlayCommandKind.DeclareWar, other), p.Trade ? "Breaks the treaties." : "", Accent: UiKit.Bad));
        }
    }

    // Barter ideas: 20 of what we have most of, for 10 of each good we have least of.
    private static List<(string Text, int Give, int Want)> BarterIdeas(PlayViewMessage v, PlayContentMessage c)
    {
        var list = new List<(string, int, int)>();
        int most = 0;
        for (int g = 1; g < v.Stock.Length; g++)
        {
            most = v.Stock[g] > v.Stock[most] ? g : most;
        }

        if (v.Stock[most] < 2000)
        {
            return list;
        }

        var order = Enumerable.Range(0, v.Stock.Length).Where(g => g != most).OrderBy(g => v.Stock[g]).Take(3);
        foreach (int g in order)
        {
            list.Add(($"Offer 20 {c.Goods[most].Name.ToLowerInvariant()} for 10 {c.Goods[g].Name.ToLowerInvariant()}", most, g));
        }

        return list;
    }

    private void ThinkerItems(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        items.Add(new SectionItem("Thinker"));
        if (v.Thinker is { } t)
        {
            items.Add(new HeaderItem(t.Name, $"{t.Rarity} thinker", new Color("8a5ab8"), UiKit.Short(t.Name)));
            items.Add(new LineItem($"Research costs {t.CostPercent}% and needs {t.ExperiencePercent}% of the experience.", UiKit.Text, 14));
        }
        else
        {
            items.Add(new LineItem("We have no thinker.", UiKit.Muted, 14));
        }

        if (v.ThinkerOffer is { } to)
        {
            items.Add(new LineItem($"On offer: {to.Name}, {to.Rarity.ToLowerInvariant()}: costs {to.CostPercent}%, experience {to.ExperiencePercent}%.", UiKit.Warn, 14));
            items.Add(new ActItem($"Hire {to.Name} for {Amounts(c, to.HirePrice)}", () => _session!.Play(PlayCommandKind.Hire, 0, flag: false), Accent: UiKit.Good));
        }

        items.Add(new SectionItem("Advisor"));
        if (v.Advisor is { } a)
        {
            items.Add(new HeaderItem(a.Name, $"{a.Rarity} advisor on {a.Fields}", new Color("8a6a40"), UiKit.Short(a.Name)));
            items.Add(new ActItem("Let the advisor go", () => _session!.Play(PlayCommandKind.Dismiss, 0)));
        }
        else
        {
            items.Add(new LineItem("We have no advisor: nobody tells us what is going wrong.", UiKit.Muted, 14));
        }

        if (v.AdvisorOffer is { } ao)
        {
            items.Add(new LineItem($"On offer: {ao.Name}, {ao.Rarity.ToLowerInvariant()}, advises on {ao.Fields}.", UiKit.Warn, 14));
            items.Add(new ActItem($"Hire {ao.Name} for {Amounts(c, ao.HirePrice)}", () => _session!.Play(PlayCommandKind.Hire, 0, flag: true), Accent: UiKit.Good));
        }
    }

    private void SummaryItems(PlayViewMessage v, List<UiItem> items)
    {
        if (_session?.Summary is not { } sum)
        {
            items.Add(new LineItem("Loading...", UiKit.Muted, 14));
            return;
        }

        items.Add(new LineItem(v.Goal ?? "", UiKit.Heading, 15));
        items.Add(new BarItem("Our start", sum.StartScore / 100f, $"{sum.StartScore} of 100 (the best free place had {sum.BestStartScore})", UiKit.Heading));
        var crit = new List<CellItem>();
        for (int k = 0; k < sum.Criteria.Count; k++)
        {
            crit.Add(new CellItem(sum.Criteria[k], UiKit.Muted));
            crit.Add(new CellItem($"{sum.StartCriteria[k]}"));
        }

        items.Add(new TableItem(4, crit));
        foreach (PlayHistoryView p in sum.Peoples)
        {
            items.Add(new SectionItem(p.Name));
            var cells = new List<CellItem> { new("Minute", UiKit.Heading), new("People", UiKit.Heading), new("Settlements", UiKit.Heading), new("Buildings", UiKit.Heading), new("Technologies", UiKit.Heading), new("Soldiers", UiKit.Heading), new("Food a minute", UiKit.Heading), new("Event", UiKit.Heading) };
            int step = Math.Max(1, p.Rows.Count / 12);
            for (int i = 0; i < p.Rows.Count; i += step)
            {
                PlayHistoryRow r = p.Rows[i];
                cells.AddRange([new($"{r.Minute}"), new($"{r.People}"), new($"{r.Settlements}"), new($"{r.Buildings}"), new($"{r.Techs}"), new($"{r.Soldiers}"), new($"{r.FoodPerMinute / 100}"), new(r.Event ?? "-", UiKit.Muted)]);
            }

            items.Add(new TableItem(8, cells));
        }

        items.Add(new ActItem("Refresh", () => _session!.AskSummary()));
    }
}
