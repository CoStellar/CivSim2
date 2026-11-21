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
    private static readonly string[] Categories = ["basics", "food", "industry", "building", "trade", "military", "governance", "science"];

    // Half the width of the windows; the research tree takes the whole screen.
    private int _windowHalf;

    private void UpdateWindow(PlayViewMessage v, PlayContentMessage c)
    {
        _window.Visible = _bigMode.Length > 0;
        float half = _bigMode == "Research" ? (GetViewport().GetVisibleRect().Size.X / 2) - 12 : _windowHalf;
        _window.OffsetLeft = -half;
        _window.OffsetRight = half;
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
            "Units" => "Our units and patrols",
            "Tutorial" => "Tutorial",
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
            case "Units":
                UnitTable(v, c, items);
                break;
            case "Tutorial":
                TutorialItems(v, items);
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

        items.Add(new LineItem("Green: known.  Bright: can start now.  Dim: needs something first (hover to see what).  Lines lead from what a technology needs; the stripe on the left is its branch.  An era opens after five technologies of the one before; the basics do not count.", UiKit.Muted, 13));
        items.Add(new CustomItem("legend" + string.Join(",", c.Categories ?? []), () => Legend(c)));
        var sig = new StringBuilder();
        foreach (PlayTechState st in v.Techs)
        {
            sig.Append(st.Known ? 'K' : st.Blocked is null ? 'A' : 'B').Append(st.Blocked).Append(st.Price is null ? "" : string.Join(",", st.Price));
        }

        sig.Append(v.Researching).Append(v.Era).Append(v.EraKnown);
        items.Add(new CustomItem(sig.ToString(), () =>
        {
            var tree = new TechTree(v, c, info => TechCard(v, c, info));
            float x = tree.EraX(v.Era);
            Callable.From(() =>
            {
                if (tree.GetParent()?.GetParent() is ScrollContainer scroll && _researchScrolled != v.Era)
                {
                    _researchScrolled = v.Era;
                    scroll.ScrollHorizontal = (int)Math.Max(0, x - 40);
                }
            }).CallDeferred();
            return tree;
        }));
    }

    // Scrolled once to the people's era when the tree opens.
    private int _researchScrolled = -1;

    // The branches and their colours (the stripe on each card).
    private static Control Legend(PlayContentMessage c)
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 12);
        foreach (string cat in (IEnumerable<string>?)c.Categories ?? Categories)
        {
            var one = new HBoxContainer();
            one.AddChild(new Glyph(UiKit.CategoryColor(cat), "", 14, round: true) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            one.AddChild(UiKit.Label(char.ToUpperInvariant(cat[0]) + cat[1..], 13, UiKit.Text));
            row.AddChild(one);
        }

        return row;
    }

    // A card: its state in the frame, its branch in the stripe on the left.
    private static StyleBoxTexture CardBox(Color bg, Color border, Color branch)
    {
        // Two layers: the branch's stripe under the card, the card shifted right over it.
        var img = Image.CreateEmpty(16, 8, false, Image.Format.Rgba8);
        img.Fill(border);
        for (int y = 1; y < 7; y++)
        {
            for (int x = 6; x < 15; x++)
            {
                img.SetPixel(x, y, bg);
            }

            for (int x = 0; x < 5; x++)
            {
                img.SetPixel(x, y, branch);
            }
        }

        return new StyleBoxTexture
        {
            Texture = ImageTexture.CreateFromImage(img),
            TextureMarginLeft = 6, TextureMarginRight = 2, TextureMarginTop = 2, TextureMarginBottom = 2,
            ContentMarginLeft = 10, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.Stretch, AxisStretchVertical = StyleBoxTexture.AxisStretchMode.Stretch,
        };
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
        string name = (info.Gateway.Length > 0 ? "» " : "") + (info.Upgrade ? $"{info.Name} +" : info.Name);
        var tip = new StringBuilder($"{info.Name}" + (info.Upgrade ? " (upgrade of its era)" : "")
            + (info.Gateway.Length > 0 ? $"\nGATEWAY to the {info.Gateway}: needed, besides enough technologies of its era, to enter it." : "") + $"\n{info.Gives}");
        if (info.Unlocks.Length > 0)
        {
            tip.Append($"\n\n{info.Unlocks}");
        }
        if (!st.Known)
        {
            int secs = st.Seconds > 0 ? st.Seconds : info.Seconds;
            tip.Append($"\n\nPrice: {(st.Price is null ? "?" : Amounts(c, st.Price))}, {secs} s" + (secs < info.Seconds ? $" (schools: {info.Seconds} s without)." : "."));
            if (info.Needs.Length > 0)
            {
                tip.Append($"\nNeeds experience: {info.Needs} points (1 point = 1 worker for 1 minute in their buildings).");
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
        if (info.Gateway.Length > 0 && !st.Known)
        {
            border = new Color(1f, 0.82f, 0.3f); // gateways stand out in gold
        }

        b.AddThemeStyleboxOverride("normal", CardBox(bg, border, cat));
        b.AddThemeStyleboxOverride("hover", CardBox(bg.Lightened(0.12f), border, cat));
        b.AddThemeStyleboxOverride("pressed", CardBox(bg, border, cat));
        b.AddThemeStyleboxOverride("disabled", CardBox(bg, border, cat));
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

    // Units picked in the Units window, for orders to a group.
    private readonly HashSet<int> _unitPicks = [];

    // Every unit of ours, patrols first: stop, recall or send out again one, a picked group or all of them (Patryk, 2026-11-19).
    // E6/E7 (Patryk, 2026-11-20): the tutorial's own panel: every step with its state, and the task list on screen on or off.
    private void TutorialItems(PlayViewMessage v, List<UiItem> items)
    {
        IReadOnlyList<PlayTutorialStep> steps = v.Tutorial ?? [];
        int done = steps.Count(x => x.Done);
        items.Add(new LineItem($"First steps: {done} of {steps.Count} done. The progress is kept also with the task list off.", UiKit.Muted, 13));
        bool next = true;
        foreach (PlayTutorialStep st in steps)
        {
            Color color = st.Done ? UiKit.Good : next ? UiKit.Heading : UiKit.Text;
            items.Add(new LineItem((st.Done ? "[x] " : next ? "[>] " : "[ ] ") + st.Text, color, 14));
            next &= st.Done;
        }

        bool on = v.TutorialOn;
        items.Add(new ButtonsItem(
        [
            new ActItem(on ? "Hide the task list" : "Show the task list on screen", () => _session?.SetTutorial(!on),
                "The next steps stay on screen above the chronicle; the building to build next lights up in the bar."),
        ]));
    }

    private void UnitTable(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        var mine = v.Units.Where(u => u.Civ == v.Civ && !c.Units[u.Kind].System)
            .OrderBy(u => u.Patrol is null ? 1 : 0).ThenBy(u => u.Hunger == 0 ? 1 : 0).ThenBy(u => u.Id).ToList();
        _unitPicks.IntersectWith(mine.Select(u => u.Id));
        int patrols = mine.Count(u => u.Patrol is not null);
        int stopped = mine.Count(u => u.Patrol is null && u.LastPatrol is not null);
        int hungry = mine.Count(u => u.Hunger > 0);
        int carriers = v.Units.Count(u => u.Civ == v.Civ && c.Units[u.Kind].System);
        items.Add(new LineItem($"{mine.Count} unit(s): {patrols} on patrol, {stopped} with a patrol to resume" + (carriers > 0 ? $"; {carriers} carrier group(s) on their tasks" : "") +
            $". Their settlements feed them every minute: next ration in {v.RationSeconds} s.", UiKit.Muted, 13));
        if (hungry > 0)
        {
            items.Add(new LineItem($"{hungry} unit(s) hungry: their settlement had no food for the ration. Strength and speed {c.HungryPercent}%; the next missed ration kills some.", UiKit.Bad, 14));
        }

        if (mine.Count == 0)
        {
            items.Add(new LineItem("No units. Train scouts or soldiers in a settlement.", UiKit.Muted, 14));
            return;
        }

        // Orders for all of them, and for the picked ones.
        var all = mine.Where(u => u.State != 0).ToList();
        var picked = all.Where(u => _unitPicks.Contains(u.Id)).ToList();
        items.Add(new ButtonsItem(
        [
            new ActItem($"Stop all patrols ({patrols})", () => Orders(v, all.Where(u => u.Patrol is not null), UnitOrder.Stop), "They stop where they are.", patrols > 0, UiKit.Warn),
            new ActItem($"Resume all ({stopped})", () => Orders(v, all, UnitOrder.Resume), "Every stopped unit walks its last patrol again.", stopped > 0, UiKit.Good),
            new ActItem("Recall all", () => Orders(v, all, UnitOrder.Recall), "Every unit walks back to the settlement that feeds it and waits there.", all.Count > 0),
        ]));
        items.Add(new ButtonsItem(
        [
            new ActItem($"Picked ({picked.Count}): stop", () => Orders(v, picked, UnitOrder.Stop), "", picked.Count > 0, UiKit.Warn),
            new ActItem("resume", () => Orders(v, picked, UnitOrder.Resume), "They walk their last patrol again.", picked.Any(u => u.LastPatrol is not null), UiKit.Good),
            new ActItem("recall", () => Orders(v, picked, UnitOrder.Recall), "Back to the settlement that feeds them.", picked.Count > 0),
            new ActItem("pick patrols", () =>
            {
                _unitPicks.Clear();
                _unitPicks.UnionWith(mine.Where(u => u.Patrol is not null || u.LastPatrol is not null).Select(u => u.Id));
                _bigSignature = "";
            }, "Pick every unit that patrols or patrolled."),
            new ActItem("pick none", () =>
            {
                _unitPicks.Clear();
                _bigSignature = "";
            }, "", _unitPicks.Count > 0),
        ]));

        var cells = new List<CellItem>
        {
            new("Pick", UiKit.Heading), new("Unit", UiKit.Heading), new("People", UiKit.Heading), new("Doing", UiKit.Heading), new("Fed by", UiKit.Heading),
            new("Food", UiKit.Heading), new("", UiKit.Heading), new("", UiKit.Heading),
        };
        foreach (PlayUnitView u in mine)
        {
            int id = u.Id;
            int col = u.Col;
            int row = u.Row;
            PlayUnitView unit = u;
            bool on = _unitPicks.Contains(id);
            cells.Add(new CellItem(on ? "[x]" : "[ ]", on ? UiKit.Good : UiKit.Muted, () =>
            {
                if (!_unitPicks.Remove(id))
                {
                    _unitPicks.Add(id);
                }

                _bigSignature = "";
            }));
            cells.Add(new CellItem(c.Units[u.Kind].Name, null, () =>
            {
                Focus(col, row); // first: it clears the selection
                _unit = id; // A18 (tests 2026-11-20): the click selects the unit, not only moves the camera
                if (_overlay is not null)
                {
                    _overlay.SelectedUnit = id;
                }

                _bigMode = "";
                UpdateUi();
            }));
            cells.Add(new CellItem($"{u.People}" + (u.Injured > 0 ? $" ({u.Injured} wounded)" : ""), UiKit.Text));
            string doing = u.State == 0 ? "training" : u.Fighting ? "FIGHTING" : u.Patrol is not null ? $"patrol, {u.Patrol.Length / 2} hexes" : u.State == 2 ? "walking" : "standing";
            cells.Add(new CellItem(doing, u.Fighting ? UiKit.Bad : u.Patrol is not null ? UiKit.Good : UiKit.Muted));
            cells.Add(new CellItem(v.Settlements.FirstOrDefault(x => x.Id == u.Home)?.Name ?? "-", UiKit.Muted));
            cells.Add(new CellItem(u.Hunger switch { 0 => "fed", 1 => "HUNGRY", _ => "STARVING" }, u.Hunger == 0 ? UiKit.Muted : UiKit.Bad));
            if (u.State == 0)
            {
                cells.Add(new CellItem("", UiKit.Muted));
                cells.Add(new CellItem("", UiKit.Muted));
                continue;
            }

            cells.Add(u.Patrol is not null || u.State == 2
                ? new CellItem(u.Patrol is not null ? "[Stop patrol]" : "[Stop]", UiKit.Warn, () => Orders(v, [unit], UnitOrder.Stop))
                : u.LastPatrol is not null
                    ? new CellItem("[Resume patrol]", UiKit.Good, () => Orders(v, [unit], UnitOrder.Resume))
                    : new CellItem("", UiKit.Muted));
            cells.Add(new CellItem("[Recall]", UiKit.Text, () => Orders(v, [unit], UnitOrder.Recall)));
        }

        items.Add(new TableItem(8, cells));
    }

    private enum UnitOrder
    {
        Stop,
        Resume,
        Recall,
    }

    // Stop: a move to where it stands ends the walk or patrol. Resume: its last patrol again. Recall: back to the settlement
    // that feeds it (or our nearest one), where it waits.
    private void Orders(PlayViewMessage v, IEnumerable<PlayUnitView> units, UnitOrder order)
    {
        var ours = v.Settlements.Where(s => s.Civ == v.Civ && !s.Remembered).ToList();
        foreach (PlayUnitView u in units)
        {
            switch (order)
            {
                case UnitOrder.Stop:
                    _session!.Play(PlayCommandKind.Move, u.Id, u.Col, u.Row);
                    break;
                case UnitOrder.Resume when u.LastPatrol is not null && u.Patrol is null:
                    _session!.Play(PlayCommandKind.ResumePatrol, u.Id);
                    break;
                case UnitOrder.Recall:
                    PlaySettlementView? home = ours.FirstOrDefault(s => s.Id == u.Home) ?? ours.OrderBy(s => Math.Abs(s.Col - u.Col) + Math.Abs(s.Row - u.Row)).FirstOrDefault();
                    if (home is not null)
                    {
                        _session!.Play(PlayCommandKind.Move, u.Id, home.Col, home.Row);
                    }

                    break;
            }
        }

        _uiTimer = 0.1;
    }

    private static string AlertWord(int kind) => kind switch
    {
        0 => "attacked",
        1 => "enemy near",
        2 => "hungry",
        3 => "needs goods",
        4 => "no homes",
        5 => "store full",
        7 => "clothing",
        8 => "rations",
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
