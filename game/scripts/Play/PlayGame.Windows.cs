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
        float screenX = GetViewport().GetVisibleRect().Size.X;
        float half = _bigMode == "Research" ? (screenX / 2) - 12 : _windowHalf;
        float left = (screenX / 2) - half, right = (screenX / 2) + half;

        // 2026-11-24 (Patryk: some pictures do not fit): a window stands beside the side panel, not over it, so the panel's
        // numbers and buttons stay whole; only when the screen is too narrow for both it covers the panel as before.
        float panelRight = _inspector.Visible ? _inspector.Position.X + _inspector.Size.X + 8 : 0;
        if (left < panelRight)
        {
            float width = right - left;
            left = panelRight;
            right = Math.Min(screenX - 8, left + width);
            if (right - left < UiKit.W(520))
            {
                left = (screenX / 2) - half;
                right = (screenX / 2) + half;
            }
        }

        _window.OffsetLeft = left - (screenX / 2);
        _window.OffsetRight = right - (screenX / 2);
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
            "Civilizations" => "Other civilizations",
            "Thinkers" => "Thinkers and advisors",
            "Units" => "Our units and patrols",
            "Tutorial" => "Tutorial",
            "Civilization" => "Our civilization",
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
            case "Civilizations":
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
            case "Civilization":
                OurCivilizationItems(v, c, items);
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
            Texture2D? mark = UiKit.Picture?.Invoke("ui", "category_" + cat);
            one.AddChild(new Glyph(UiKit.CategoryColor(cat), "", mark is null ? 14 : 22, round: mark is null, icon: mark) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
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
        // The tooltip (Patryk, 2026-11-23): the name, what it gives, then the facts one a line.
        var tip = new StringBuilder("# " + info.Name + (info.Upgrade ? " (upgrade of its era)" : ""));
        tip.Append($"\n{info.Gives}");

        // 2026-11-24 (Patryk): what it unlocks with pictures and names, one kind a line.
        string Named(IEnumerable<(string Pic, string Name)> things) => string.Join(", ", things.Select(t => UiKit.GoodMark("p:" + t.Pic) + t.Name.Replace(' ', ' ')));
        var newBuildings = c.Buildings.Where(x => x.Id != 0 && x.Tech == info.Id).Select(x => ("buildings/building_" + x.Key, x.Name)).ToList();
        var newUnits = c.Units.Where(x => x.Tech == info.Id && x.Tech > 0).Select(x => ("units/unit_" + x.Key, x.Name)).ToList();
        var newWorks = (c.Roads ?? []).Where(x => x.Tech == info.Id).Select(x => ("works/work_icon_" + (x.Level == 1 ? "track" : "road"), x.Name))
            .Concat((c.Works ?? []).Where(x => x.Tech == info.Id).Select(x => ("works/work_icon_" + x.Key, x.Name))).ToList();
        if (newBuildings.Count + newUnits.Count + newWorks.Count > 0)
        {
            tip.Append('\n');
        }
        else if (info.Unlocks.Length > 0)
        {
            tip.Append($"\n{info.Unlocks}"); // nothing to picture (an upgrade, a rule): the words
        }

        if (newBuildings.Count > 0)
        {
            tip.Append($"\n@Buildings|{Named(newBuildings)}");
        }

        if (newUnits.Count > 0)
        {
            tip.Append($"\n@Units|{Named(newUnits)}");
        }

        if (newWorks.Count > 0)
        {
            tip.Append($"\n@Roads and works|{Named(newWorks)}");
        }

        tip.Append('\n');
        if (info.Gateway.Length > 0)
        {
            tip.Append($"\n@Gateway|to the {info.Gateway}: needed, besides enough technologies of its era, to enter it");
        }

        if (st.Known)
        {
            tip.Append("\n@State|known");
        }
        else
        {
            int secs = st.Seconds > 0 ? st.Seconds : info.Seconds;
            tip.Append($"\n@Cost|{(st.Price is null ? "?" : Amounts(c, st.Price, icons: true))}");
            tip.Append($"\n@Research time|{secs} s" + (secs < info.Seconds ? $" ({info.Seconds} s without schools)" : ""));
            if (info.Needs.Length > 0)
            {
                tip.Append($"\n@Experience|{info.Needs}");
                tip.Append("\n(1 point of experience = 1 worker for 1 minute in their buildings)");
            }

            if (st.Blocked is { } why)
            {
                tip.Append($"\n@Not yet|{why}");
            }
        }

        // 2026-11-24 (Patryk): the card shows its picture (or the branch's mark), its name, and under it the pictures of what
        // it unlocks: buildings, units, roads and works.
        var b = new RichTipButton { FocusMode = Control.FocusModeEnum.None, TooltipText = tip.ToString(), CustomMinimumSize = new Vector2(TechTree.CardWidth, TechTree.CardHeight) };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(6, 4) };
        row.AddThemeConstantOverride("separation", 6);
        b.AddChild(row);
        Texture2D? techPic = UiKit.Picture?.Invoke("techs", "tech_" + info.Key);
        row.AddChild(new Glyph(cat.Darkened(st.Known || st.Blocked is null ? 0.1f : 0.5f), UiKit.Short(info.Name), TechTree.CardHeight - 10, icon: techPic) { MouseFilter = Control.MouseFilterEnum.Ignore });
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 2);
        row.AddChild(col);
        Label title = UiKit.Label(name, 14, ink);
        title.MouseFilter = Control.MouseFilterEnum.Ignore;
        title.ClipText = true;
        title.CustomMinimumSize = new Vector2(TechTree.CardWidth - TechTree.CardHeight - 8, 0);
        col.AddChild(title);
        var unlocks = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        unlocks.AddThemeConstantOverride("separation", 2);
        col.AddChild(unlocks);
        foreach (Texture2D? pic in UnlockPictures(c, info.Id).Take(6))
        {
            if (pic is not null)
            {
                unlocks.AddChild(new TextureRect { Texture = pic, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, CustomMinimumSize = new Vector2(26, 26), MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = st.Known || st.Blocked is null ? Colors.White : new Color(0.6f, 0.6f, 0.6f) });
            }
        }
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

    // The pictures of what a technology unlocks: its buildings, units, roads and works.
    private static IEnumerable<Texture2D?> UnlockPictures(PlayContentMessage c, int tech)
    {
        foreach (PlayBuildingInfo x in c.Buildings.Where(x => x.Id != 0 && x.Tech == tech))
        {
            yield return UiKit.PictureOf("buildings/building_" + x.Key);
        }

        foreach (PlayUnitInfo x in c.Units.Where(x => x.Tech == tech && x.Tech > 0))
        {
            yield return UiKit.PictureOf("units/unit_" + x.Key);
        }

        foreach (PlayRoadInfo x in (c.Roads ?? []).Where(x => x.Tech == tech))
        {
            yield return UiKit.PictureOf("works/work_icon_" + (x.Level == 1 ? "track" : "road"));
        }

        foreach (PlayWorkInfo x in (c.Works ?? []).Where(x => x.Tech == tech))
        {
            yield return UiKit.PictureOf("works/work_icon_" + x.Key);
        }
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

    // A peace offer being put together for each people we fight (ADR 0030): goods and amounts, maybe a settlement, a length.
    private sealed class PeaceDraft
    {
        public bool WeGive;
        public int Length = 1;
        public int Settlement = -1;
        public readonly SortedDictionary<int, int> Goods = [];
    }

    private readonly Dictionary<int, PeaceDraft> _peaceDrafts = [];

    private void PeaceItems(PlayViewMessage v, PlayContentMessage c, PlayPeopleView p, List<UiItem> items)
    {
        int other = p.Civ;
        if (!_peaceDrafts.TryGetValue(other, out PeaceDraft? d))
        {
            d = new PeaceDraft();
            _peaceDrafts[other] = d;
        }

        int[] minutes = c.PeaceMinutes is { Length: > 0 } pm ? pm : [3, 6, 10];
        d.Length = Math.Clamp(d.Length, 0, minutes.Length - 1);
        string started = p.WeStarted ? " We started this war." : p.TheyStarted ? " They started this war." : "";
        items.Add(new LineItem($"War so far: we {p.OurScore}, they {p.TheirScore}.{started}", UiKit.Text, 14));
        items.Add(new LineItem($"War weariness: we {p.OurWeariness}, they {p.TheirWeariness}" + (p.OurWorkLoss > 0 ? $" (our workshops work {p.OurWorkLoss}% less)." : "."),
            p.OurWeariness >= 50 ? UiKit.Warn : UiKit.Text, 14));
        items.Add(new LineItem(p.Favour > 0 ? $"The war favours us: they should give about {p.FairWorth} worth ({p.Favour}% of what they have)."
            : p.Favour < 0 ? $"The war favours them: we should give about {-p.FairWorth} worth. We may offer a white peace or give something."
            : "Nobody is ahead: a white peace is fair.", p.Favour < 0 ? UiKit.Warn : UiKit.Text, 14));

        if (p.TheirPeace is { } theirs)
        {
            items.Add(new LineItem($"They offer {theirs}.", UiKit.Warn, 14));
            items.Add(new ButtonsItem([
                new ActItem("Accept their peace", () => _session!.Play(PlayCommandKind.AnswerPeace, other, flag: true), Accent: UiKit.Good),
                new ActItem("Refuse", () => _session!.Play(PlayCommandKind.AnswerPeace, other, flag: false))]));
        }

        // Length of peace, who gives, and the suggestion.
        var lengths = new List<ActItem>();
        for (int k = 0; k < minutes.Length; k++)
        {
            int kk = k;
            lengths.Add(new ActItem($"{minutes[k]} min", () => d.Length = kk, "Length of peace. Breaking a long peace costs more reputation.", Accent: d.Length == k ? UiKit.Good : null));
        }

        items.Add(new ButtonsItem(lengths));
        items.Add(new ButtonsItem([
            new ActItem("Suggest terms", () => FillDraft(d, p.Suggested), "Terms the course of the war suggests: what they should accept."),
            new ActItem("White peace", () => { d.Goods.Clear(); d.Settlement = -1; d.WeGive = false; }),
            new ActItem("We demand", () => { if (d.WeGive) { d.Goods.Clear(); } d.WeGive = false; }, "Only the side the war favours may demand.", p.Favour > 0, d.WeGive ? null : UiKit.Good),
            new ActItem("We give", () => { if (!d.WeGive) { d.Goods.Clear(); d.Settlement = -1; } d.WeGive = true; }, "Give goods to buy peace.", Accent: d.WeGive ? UiKit.Good : null)]));

        // Goods: what the giver has and what the terms name.
        var cells = new List<CellItem> { new("Good", UiKit.Muted), new(d.WeGive ? "We have" : "They have", UiKit.Muted), new("Terms", UiKit.Muted), new(""), new("") };
        for (int g = 0; g < c.Goods.Count; g++)
        {
            long has = d.WeGive ? v.Stock[g] / 100 : p.TheirGoods is { } tg && g < tg.Length ? tg[g] : -1;
            int named = d.Goods.TryGetValue(g, out int n) ? n : 0;
            if (has <= 0 && named == 0)
            {
                continue;
            }

            int gg = g;
            int step = has >= 200 ? 50 : has >= 40 ? 10 : 1;
            cells.Add(new CellItem(c.Goods[g].Name));
            cells.Add(new CellItem(has.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            cells.Add(new CellItem(named > 0 ? named.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-", named > 0 ? UiKit.Good : UiKit.Muted));
            cells.Add(new CellItem($"-{step}", UiKit.Text, () => SetDraftGood(d, gg, named - step)));
            cells.Add(new CellItem($"+{step}", UiKit.Text, () => SetDraftGood(d, gg, (int)Math.Min(has, named + step))));
        }

        if (cells.Count > 5)
        {
            items.Add(new TableItem(5, cells));
        }

        // Settlement: 2B, not their first, besieged by us or near our land; only when demanding.
        if (!d.WeGive && p.Demandable is { Length: > 0 } places)
        {
            var acts = new List<ActItem> { new("No settlement", () => d.Settlement = -1, Accent: d.Settlement < 0 ? UiKit.Good : null) };
            foreach (int sid in places)
            {
                int ss = sid;
                string name = SettlementName(v, sid);
                acts.Add(new ActItem(name, () => d.Settlement = ss, "It passes to us with its people and buildings.", p.Favour > 0, d.Settlement == sid ? UiKit.Good : null));
            }

            items.Add(new ButtonsItem(acts));
        }

        var pairs = new List<int>();
        var words = new List<string>();
        foreach ((int g, int n) in d.Goods)
        {
            pairs.Add(g);
            pairs.Add(n);
            words.Add($"{n} {c.Goods[g].Name.ToLowerInvariant()}");
        }

        if (d.Settlement >= 0)
        {
            words.Add(SettlementName(v, d.Settlement));
        }

        string text = words.Count == 0 ? $"White peace for {minutes[d.Length]} min." : $"Peace for {minutes[d.Length]} min; {(d.WeGive ? "we give" : "they give")} {string.Join(", ", words)}.";
        items.Add(new LineItem(text, UiKit.Text, 14));
        int[] goodsPairs = [.. pairs];
        int length = d.Length;
        int settlement = d.Settlement;
        bool give = d.WeGive;
        items.Add(new ActItem("Offer these terms", () => _session!.Play(PlayCommandKind.ProposePeace, other, flag: give, b: length, c: settlement + 1, cells: goodsPairs),
            "They answer at once if they are led by the AI.", Accent: UiKit.Good));
    }

    private static string SettlementName(PlayViewMessage v, int id)
    {
        foreach (PlaySettlementView s in v.Settlements)
        {
            if (s.Id == id)
            {
                return s.Name;
            }
        }

        return $"settlement {id}";
    }

    private static void SetDraftGood(PeaceDraft d, int good, int amount)
    {
        if (amount <= 0)
        {
            d.Goods.Remove(good);
        }
        else
        {
            d.Goods[good] = amount;
        }
    }

    // Fills the draft from the server's suggestion: [we give, length, settlement, good, amount, ...].
    private static void FillDraft(PeaceDraft d, int[]? s)
    {
        if (s is not { Length: >= 3 })
        {
            return;
        }

        d.Goods.Clear();
        d.WeGive = s[0] == 1;
        d.Settlement = s[2];
        for (int k = 3; k + 1 < s.Length; k += 2)
        {
            d.Goods[s[k]] = s[k + 1];
        }
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
        9 => "food low",
        10 => "no research",
        11 => "strangers",
        _ => "materials missing",
    };

    private void PeoplesItems(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        items.Add(new HeaderItem("Our people", c.Eras[v.Era], new Color("c8a050"), "Us", UiKit.EmblemName(UiKit.KeyOf(c.Eras[v.Era]), v.Civ), v.Civ, Portrait: true));
        if (v.Peoples.Count == 0)
        {
            items.Add(new LineItem("We have met no other civilization yet. Send scouts to find them.", UiKit.Muted, 15));
        }

        foreach (PlayPeopleView p in v.Peoples)
        {
            int other = p.Civ;
            string status = p.Dead ? "gone" : p.AtWar ? "AT WAR" : p.Alliance ? "allies" : p.Trade ? "trade partners" : "at peace";
            // 2026-11-24 (Patryk): each people's emblem, in the look of our era (board, stone, shield, banner, heraldry).
            items.Add(new HeaderItem(p.Name, status + (p.Reputation < 0 ? $" - known for treachery ({p.Reputation})" : ""), p.AtWar ? new Color("b84040") : new Color("6a7a5a"), UiKit.Short(p.Name), UiKit.EmblemName(UiKit.KeyOf(c.Eras[v.Era]), p.Civ), p.Civ, p.Dead, Portrait: true));
            if (p.Dead)
            {
                continue;
            }

            if (p.AtWar)
            {
                PeaceItems(v, c, p, items);
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
            items.Add(new HeaderItem(t.Name, $"{t.Rarity} thinker", new Color("8a5ab8"), UiKit.Short(t.Name), ThinkerPortrait(t), Portrait: true));
            items.Add(new LineItem($"Research costs {t.CostPercent}% and needs {t.ExperiencePercent}% of the experience.", UiKit.Text, 14));
        }
        else
        {
            items.Add(new LineItem("We have no thinker.", UiKit.Muted, 14));
        }

        if (v.ThinkerOffer is { } to)
        {
            items.Add(new HeaderItem($"On offer: {to.Name}", $"{to.Rarity} thinker: research costs {to.CostPercent}%, experience {to.ExperiencePercent}%", new Color("8a5ab8"), UiKit.Short(to.Name), ThinkerPortrait(to), Portrait: true));
            items.Add(new ActItem($"Hire {to.Name} for {Amounts(c, to.HirePrice)}", () => _session!.Play(PlayCommandKind.Hire, 0, flag: false), Accent: UiKit.Good));
        }

        items.Add(new SectionItem("Advisor"));
        if (v.Advisor is { } a)
        {
            items.Add(new HeaderItem(a.Name, $"{a.Rarity} advisor on {a.Fields}", new Color("8a6a40"), UiKit.Short(a.Name), AdvisorPortrait(a), Portrait: true));
            items.Add(new ActItem("Let the advisor go", () => _session!.Play(PlayCommandKind.Dismiss, 0)));
        }
        else
        {
            items.Add(new LineItem("We have no advisor: nobody tells us what is going wrong.", UiKit.Muted, 14));
        }

        if (v.AdvisorOffer is { } ao)
        {
            items.Add(new HeaderItem($"On offer: {ao.Name}", $"{ao.Rarity} advisor on {ao.Fields}", new Color("8a6a40"), UiKit.Short(ao.Name), AdvisorPortrait(ao), Portrait: true));
            items.Add(new ActItem($"Hire {ao.Name} for {Amounts(c, ao.HirePrice)}", () => _session!.Play(PlayCommandKind.Hire, 0, flag: true), Accent: UiKit.Good));
        }
    }

    // 2026-11-24 (Patryk): every thinker and advisor has a portrait with looks of their own; a thinker is dressed by rarity,
    // an advisor holds the thing of their (first) field.
    private static string ThinkerPortrait(PlayPersonView t) => UiKit.PortraitName("thinker", UiKit.KeyOf(t.Rarity), t.Name);

    private static string AdvisorPortrait(PlayPersonView a)
    {
        string field = a.Fields.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "economy";
        return UiKit.PortraitName("advisor", UiKit.KeyOf(field), a.Name);
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

    // 2026-11-24 (Patryk): the window behind our emblem: our whole civilization at a glance.
    private void OurCivilizationItems(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        string eraKey = UiKit.KeyOf(c.Eras[v.Era]);
        PlayHistoryView? ours = _session?.Summary?.Peoples.FirstOrDefault(p => p.Civ == v.Civ);
        string name = ours?.Name ?? "Our people";
        string next = v.Era + 1 < c.Eras.Count ? $"{v.EraKnown} of {v.EraNeed} technologies to the {c.Eras[v.Era + 1]}" : "the last era";
        items.Add(new HeaderItem(name, $"{c.Eras[v.Era]} - {next}", new Color("c8a050"), "Us", UiKit.EmblemName(eraKey, v.Civ), v.Civ, v.Lost, Portrait: true));
        if (v.Goal is { Length: > 0 } goal)
        {
            items.Add(new LineItem("Goal: " + goal, UiKit.Heading, 15));
        }

        items.Add(new LineItem($"Score {v.Score}. Played {Clock()}.", UiKit.Muted, 14));

        var settlements = v.Settlements.Where(s => s.Civ == v.Civ && !s.Remembered).ToList();
        int known = v.Techs.Count(t => t.Known);
        int units = v.Units.Count(u => u.Civ == v.Civ);
        int buildings = v.Buildings.Count(b => b.Civ == v.Civ);
        int housing = settlements.Sum(s => s.Housing);
        items.Add(new SectionItem("Our land and people"));
        items.Add(new TableItem(2,
        [
            new("People", UiKit.Muted), new($"{v.People} (homes for {housing})"),
            new("Settlements", UiKit.Muted), new(string.Join(", ", settlements.GroupBy(s => s.Level).OrderByDescending(g => g.Key).Select(g => $"{g.Count()} {(g.Key < c.Levels.Count ? c.Levels[g.Key].Name.ToLowerInvariant() : "?")}")) is { Length: > 0 } list ? $"{settlements.Count}: {list}" : "0"),
            new("Buildings", UiKit.Muted), new($"{buildings}"),
            new("Technologies", UiKit.Muted), new($"{known} of {c.Techs.Count}"),
            new("Units", UiKit.Muted), new($"{units} (army {v.ArmyUnits} of {v.ArmyLimit})"),
            new("Food", UiKit.Muted), new(v.Hungry ? "hungry" : "enough", v.Hungry ? UiKit.Bad : UiKit.Text),
        ]));

        items.Add(new SectionItem("Our civilization's minds"));
        items.Add(new LineItem(v.Thinker is { } t ? $"Thinker: {t.Name} ({t.Rarity.ToLowerInvariant()})" : "No thinker.", UiKit.Text, 14));
        items.Add(new LineItem(v.Advisor is { } a ? $"Advisor: {a.Name} ({a.Rarity.ToLowerInvariant()}, {a.Fields.ToLowerInvariant()})" : "No advisor.", UiKit.Text, 14));

        items.Add(new SectionItem("Other civilizations"));
        var met = v.Peoples.Where(p => !p.Dead).ToList();
        if (met.Count == 0)
        {
            items.Add(new LineItem("We have met no other civilization yet.", UiKit.Muted, 14));
        }
        else
        {
            items.Add(new LineItem($"We know {met.Count}: at war with {met.Count(p => p.AtWar)}, trading with {met.Count(p => p.Trade)}, allied with {met.Count(p => p.Alliance)}.", UiKit.Text, 14));
        }

        var acts = new List<ActItem>();
        for (int i = 0; i < WindowNames.Length; i++)
        {
            string w = WindowNames[i];
            acts.Add(new ActItem($"{i + 1} {w}", () => OpenWindow(w, toggle: false), Pic: "ui/window_" + w.ToLowerInvariant()));
        }

        items.Add(new ButtonsItem(acts));
    }
}
