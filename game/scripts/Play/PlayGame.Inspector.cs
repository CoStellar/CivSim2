using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>The side panel on the left: what is selected, its numbers and what can be done with it.</summary>
public partial class PlayGame
{
    private bool _showCaravans;

    private void Inspect(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        if (v.Picking)
        {
            if (_selected is { } p)
            {
                HexItems(p, items);
            }

            return;
        }

        if (_unit >= 0)
        {
            foreach (PlayUnitView u in v.Units)
            {
                if (u.Id == _unit)
                {
                    UnitItems(v, u, c, items);
                    return;
                }
            }

            _unit = -1;
        }

        if (_selected is not { } sel)
        {
            items.Add(new HeaderItem("Nothing selected", "Click your settlement to start", new Color("6a5a48"), "?"));
            items.Add(new LineItem("Pick a building in the bar at the bottom, then click a green hex. Press H for all controls and first steps.", UiKit.Muted, 14));
            return;
        }

        foreach (PlaySettlementView s in v.Settlements)
        {
            if (s.Col == sel.Col && s.Row == sel.Row)
            {
                SettlementItems(v, c, s, items);
                return;
            }
        }

        // Up to four buildings share a hex: each gets its own block, in the order of its places.
        var here = v.Buildings.Where(b => b.Col == sel.Col && b.Row == sel.Row).OrderBy(b => System.Numerics.BitOperations.TrailingZeroCount(b.Slots)).ToList();
        if (here.Count > 0)
        {
            HexOverview(v, c, sel, here, items);
            return;
        }

        HexItems(sel, items);
    }

    private void SettlementItems(PlayViewMessage v, PlayContentMessage c, PlaySettlementView s, List<UiItem> items)
    {
        PlayLevelInfo level = c.Levels[s.Level];
        bool mine = s.Civ == v.Civ;
        if (s.Remembered)
        {
            items.Add(new HeaderItem(s.Name, $"{level.Name} of {PeopleName(v, s.Civ)} - as we last saw it", new Color("6a6a6a"), level.Name[..1]));
            items.Add(new LineItem("Out of sight. Send scouts to see what is there now.", UiKit.Muted, 14));
            return;
        }

        items.Add(new HeaderItem(s.Name, mine ? level.Name : $"{level.Name} of {PeopleName(v, s.Civ)}", mine ? new Color("c8a050") : new Color("8a5050"), level.Name[..1]));
        if (s.Besieged)
        {
            items.Add(new LineItem("UNDER ATTACK", UiKit.Bad, 18));
        }

        items.Add(new LineItem($"Defence {s.DefensePercent}%" + (s.DefensePercent > 100 ? " (walls)" : ""), UiKit.Muted, 14));
        if (!mine)
        {
            items.Add(new BarItem("People", 1, $"{s.People}", new Color("4f7fb8")));
            return;
        }

        items.Add(new BarItem("People", s.Housing <= 0 ? 0 : (float)s.People / s.Housing, $"{s.People} of {s.Housing} homes", new Color("4f7fb8")));
        items.Add(new LineItem(s.Idle > 0 ? $"{s.Idle} without work" + (s.Gathering ? " (they gather around)" : " (idle)") : "Everyone has work", UiKit.Muted, 13));
        GatherItems(c, s, items);
        int work = s.WorkPermille / 10;
        if (work != 100)
        {
            items.Add(new LineItem($"Workers work at {work}%" + (v.Winter ? " (winter; clothing helps)" : " (clothing)"), work < 100 ? UiKit.Warn : UiKit.Good, 13));
        }

        if (s.HealPerMinute > 0)
        {
            items.Add(new LineItem($"Herbalists heal {s.HealPerMinute} wounded a minute in its land", UiKit.Muted, 13));
        }

        // Rations: everyone it feeds eats once a minute; short of food the idle go hungry first, then the workers, the units last.
        {
            int food = c.Goods.ToList().FindIndex(x => x.Key == "food");
            bool enough = s.Stock is { } st && food >= 0 && food < st.Length && st[food] >= s.RationFood;
            string who = s.RationUnits > 0 ? $"{s.People} residents and {s.RationPeople} in {s.RationUnits} units" : $"{s.People} residents";
            items.Add(new LineItem($"Ration: {UiKit.Num(s.RationFood)} food for {who} in {v.RationSeconds} s", s.HungryPeople > 0 || s.RationShort > 0 ? UiKit.Bad : enough ? UiKit.Muted : UiKit.Warn, 13));
            if (s.HungryPeople > 0 || s.RationShort > 0)
            {
                string hungry = (s.HungryPeople > 0 ? $"{s.HungryPeople} people ({s.HungryWorkers} of them workers)" : "") + (s.RationShort > 0 ? (s.HungryPeople > 0 ? " and " : "") + $"{s.RationShort} units" : "");
                items.Add(new LineItem(s.RationDead > 0 ? $"Starving: {hungry} without food, {s.RationDead} died at the last ration" : $"Hungry: {hungry} got no ration and work weaker", UiKit.Bad, 13));
            }
        }

        foreach (PlayAlertView a in v.Alerts ?? [])
        {
            if (a.Settlement == s.Id)
            {
                items.Add(new LineItem("! " + a.Text, a.Kind <= 2 ? UiKit.Bad : UiKit.Warn, 14));
            }
        }

        SupplyItems(v, c, s, items);

        if (s.Stock is { } stock && s.Capacity is { } cap)
        {
            items.Add(new SectionItem("Store"));
            var cells = new List<GoodCell>();
            for (int g = 0; g < stock.Length; g++)
            {
                long rate = s.Rate?[g] ?? 0;
                if (g < 3 || stock[g] >= 100 || rate != 0)
                {
                    cells.Add(new GoodCell(c.Goods[g].Key, c.Goods[g].Name, stock[g], cap[g], rate));
                }
            }

            items.Add(new GoodsItem(cells));

            items.Add(new SectionItem("Train"));
            if (c.Units.Any(x => x.MaxPeople > 0 && v.Techs[x.Tech].Known))
            {
                items.Add(TrainSizeItem());
            }

            var tiles = new List<TileCell>();
            foreach (PlayUnitInfo ut in c.Units)
            {
                if (!v.Techs[ut.Tech].Known || ut.System)
                {
                    continue;
                }

                int kind = ut.Id;
                int sid = s.Id;
                int size = _trainSize > 0 && ut.MaxPeople > 0 ? Math.Clamp(_trainSize, ut.MinPeople, ut.MaxPeople) : ut.People;
                long[] price = [.. ut.Price.Select(p => p * size / Math.Max(1, ut.People))];
                bool shock = _trainShock && ut.MaxPeople > 0;
                bool can = CanPay(stock, price) && s.People > size;
                string tip = $"{ut.Name}: {size} people, {Amounts(c, price)}, {ut.TrainSeconds * size / Math.Max(1, ut.People)} s"
                    + (shock ? " (faster: weapons only)." : ".")
                    + (ut.MaxPeople > 0 ? $"\nThey take the best weapons{(shock ? "" : " and armour")} the store holds for all {size}." : "")
                    + (ut.Army ? $"\nStrength {ut.Strength} each" + (ut.Range > 1 ? $", shoots {ut.Range} hexes." : ".") : "")
                    + (ut.Founds ? "\nThey found a new camp where you send them." : "")
                    + (ut.CanHide ? "\nCan walk hidden." : "")
                    + (ut.NeedsBuilding.Length > 0 ? $"\nNeeds a {ut.NeedsBuilding.Replace('_', ' ')} in the settlement." : "")
                    + (can ? "" : "\n\nNot enough people or goods here now.");
                tiles.Add(new TileCell(ut.Name, UiKit.Short(ut.Name), ut.Army ? new Color("b84040") : new Color("4f8f7f"), tip,
                    () => _session!.Play(PlayCommandKind.Train, sid, c: _trainSize > 0 && ut.MaxPeople > 0 ? size : 0, flag: shock, b: kind), can, $"{ut.Name}"));
            }

            items.Add(new TilesItem(tiles));
            var training = v.Units.Where(u => u.Civ == v.Civ && u.State == 0 && u.Col == s.Col && u.Row == s.Row).Select(u => c.Units[u.Kind].Name.ToLowerInvariant()).ToList();
            if (training.Count > 0)
            {
                items.Add(new LineItem("Training: " + string.Join(", ", training), UiKit.Muted, 13));
            }

            items.Add(new SectionItem("Settlement"));
            if (s.Level + 1 < c.Levels.Count && Reached(v, c, c.Levels[s.Level + 1].Tech))
            {
                PlayLevelInfo next = c.Levels[s.Level + 1];
                bool tech = next.Tech < 0 || v.Techs[next.Tech].Known;
                bool can = tech && CanPay(stock, next.UpgradePrice);
                string tip = $"Becomes a {next.Name.ToLowerInvariant()}: land of {next.Radius} hexes around, {next.Housing} homes in the centre. Costs {Amounts(c, next.UpgradePrice)}."
                    + (tech ? "" : $"\nNeeds {c.Techs[next.Tech].Name}.");
                int sid = s.Id;
                items.Add(new ActItem($"Upgrade to {next.Name.ToLowerInvariant()}", () => _session!.Play(PlayCommandKind.Upgrade, sid), tip, can));
            }

            items.Add(new ActItem(_showCaravans ? "Hide caravans and routes" : "Caravans and routes...", () => _showCaravans = !_showCaravans,
                "Send goods to another of our settlements, once or on a standing route."));
            if (_showCaravans)
            {
                CaravanItems(v, c, s, stock, items);
            }
        }
    }

    // Answers to "needs food / needs a good": bring it from one of our settlements that has some to spare.
    private void SupplyItems(PlayViewMessage v, PlayContentMessage c, PlaySettlementView s, List<UiItem> items)
    {
        var goods = new List<int>();
        foreach (PlayAlertView a in v.Alerts ?? [])
        {
            if (a.Settlement == s.Id && a.Good >= 0 && a.Kind is 2 or 3 && !goods.Contains(a.Good))
            {
                goods.Add(a.Good);
            }
        }

        foreach (int g in goods)
        {
            var from = v.Settlements.Where(o => o.Civ == v.Civ && o.Id != s.Id && o.Stock is { } st && st[g] >= 4000).OrderBy(o => Dist(o, s)).Take(3).ToList();
            string name = c.Goods[g].Name.ToLowerInvariant();
            if (from.Count == 0)
            {
                items.Add(new LineItem($"No settlement of ours has {name} to spare.", UiKit.Muted, 13));
            }

            foreach (PlaySettlementView o in from)
            {
                int fromId = o.Id;
                int to = s.Id;
                int good = g;
                items.Add(new ActItem($"Bring 20 {name} from {o.Name} (has {o.Stock![g] / 100})", () => _session!.Play(PlayCommandKind.SendCaravan, fromId, good, flag: false, b: to, c: 20),
                    "One caravan. It walks on the map and can be raided.", Accent: UiKit.Good));
                items.Add(new ActItem($"Supply route: {name} from {o.Name}", () => _session!.Play(PlayCommandKind.Route, fromId, good, _routeKeep, flag: false, b: to, c: 20, d: _routeUntil),
                    $"A caravan keeps bringing 20 a trip while {o.Name} keeps {_routeKeep}" + (_routeUntil > 0 ? $" and here are fewer than {_routeUntil}." : ".")));
            }
        }
    }

    // Limits of new standing routes (G3, 2026-11-19): what the source keeps and how much the target should reach.
    private int _routeKeep = 30;
    private int _routeUntil;

    private CustomItem RouteLimitsItem() => new("routelimits", () =>
    {
        var row = new HBoxContainer();
        SpinBox Spin(int value, string tip, Action<int> set)
        {
            var box = new SpinBox { MinValue = 0, MaxValue = 5000, Step = 10, Value = value, TooltipText = tip, CustomMinimumSize = new Vector2(UiKit.W(90), 0) };
            box.ValueChanged += x => set((int)x);
            return box;
        }

        row.AddChild(UiKit.Label("Routes: keep", 13, UiKit.Muted));
        row.AddChild(Spin(_routeKeep, "What stays in the settlement the goods leave.", x => _routeKeep = x));
        row.AddChild(UiKit.Label(" until target has", 13, UiKit.Muted));
        row.AddChild(Spin(_routeUntil, "Trips stop while the target has this much (0 = always).", x => _routeUntil = x));
        return row;
    });

    private void CaravanItems(PlayViewMessage v, PlayContentMessage c, PlaySettlementView s, long[] stock, List<UiItem> items)
    {
        items.Add(RouteLimitsItem());
        var others = v.Settlements.Where(o => o.Civ == v.Civ && o.Id != s.Id).OrderBy(o => Dist(o, s)).Take(2).ToList();
        var goods = Enumerable.Range(0, stock.Length).Where(g => stock[g] >= 2000).OrderByDescending(g => stock[g]).Take(2).ToList();
        if (others.Count == 0)
        {
            items.Add(new LineItem("We have no other settlement to send goods to.", UiKit.Muted, 13));
        }
        else if (goods.Count == 0)
        {
            items.Add(new LineItem("Nothing here to send yet (20 or more of a good).", UiKit.Muted, 13));
        }

        foreach (PlaySettlementView o in others)
        {
            foreach (int g in goods)
            {
                int from = s.Id;
                int to = o.Id;
                int good = g;
                string name = c.Goods[g].Name.ToLowerInvariant();
                items.Add(new ActItem($"Send 20 {name} to {o.Name}", () => _session!.Play(PlayCommandKind.SendCaravan, from, good, flag: false, b: to, c: 20)));
                items.Add(new ActItem($"Route: {name} to {o.Name}, every trip", () => _session!.Play(PlayCommandKind.Route, from, good, _routeKeep, flag: false, b: to, c: 20, d: _routeUntil),
                    $"The caravans take the shortest way; {s.Name} keeps {_routeKeep}" + (_routeUntil > 0 ? $", they go while {o.Name} has fewer than {_routeUntil}." : ".")));
                items.Add(new ActItem($"Draw the way: {name} to {o.Name}", () => ArmDraw(DrawMode.Route, (from, to, good)), "Draw over the hexes the way the caravans take, there and back."));
            }
        }

        foreach (PlayRouteView r in v.Routes ?? [])
        {
            if (r.From == s.Id)
            {
                int index = r.Index;
                string to = v.Settlements.FirstOrDefault(x => x.Id == r.To)?.Name ?? "?";
                items.Add(new ActItem($"End route: {r.Amount} {c.Goods[r.Good].Name.ToLowerInvariant()} to {to}" + (r.OnTheRoad ? " (on the road)" : ""), () => _session!.Play(PlayCommandKind.EndRoute, index), Accent: UiKit.Warn));
            }
        }
    }

    // The whole hex at a glance (Patryk, 2026-11-19): its land, its four places as a picture, its protection and works, then
    // every building with its state and its buttons side by side.
    private void HexOverview(PlayViewMessage v, PlayContentMessage c, (int Col, int Row) sel, List<PlayBuildingView> here, List<UiItem> items)
    {
        HexHeader(sel, items);
        var places = new PlaceCell?[4];
        int[] owner = [-1, -1, -1, -1];
        foreach (PlayBuildingView b in here)
        {
            PlayBuildingInfo t = c.Buildings[b.Type];
            (string state, Color color) = ShortState(b, t);
            for (int k = 0; k < 4; k++)
            {
                if ((b.Slots & (1 << k)) != 0)
                {
                    owner[k] = b.Id;
                    places[k] = new PlaceCell(t.Name, UiKit.Short(t.Name), UiKit.GroupColor(t.Groups), state, color);
                }
            }
        }

        int used = owner.Count(x => x >= 0);
        items.Add(new SectionItem($"Places: {used} of 4 taken"));
        items.Add(new PlacesItem(places, owner));
        WorksItems(sel, items, always: true);
        int n = 0;
        foreach (PlayBuildingView b in here)
        {
            BuildingItems(v, c, b, items, ++n);
        }
    }

    // A word or two for a building's state and its colour (the border of its place in the picture).
    private static (string Text, Color Color) ShortState(PlayBuildingView b, PlayBuildingInfo t) => b.State switch
    {
        _ when b.Remembered => ("as last seen", UiKit.Muted),
        5 when b.Porters > 0 => ($"materials coming, {b.ProgressPermille / 10}%", UiKit.Warn),
        5 => ("materials missing", UiKit.Bad),
        0 => ($"building {b.ProgressPermille / 10}%", new Color("8fb040")),
        1 => ("paused", UiKit.Warn),
        3 => ("stopped", UiKit.Bad),
        _ when b.Starved => ("waiting for goods", UiKit.Warn),
        _ when t.Workers > 0 && b.Workers == 0 => ("no workers", UiKit.Bad),
        _ when t.Workers > 0 => ($"working {b.Workers}/{t.Workers}", UiKit.Good),
        _ => ("standing", UiKit.Good),
    };

    private void BuildingItems(PlayViewMessage v, PlayContentMessage c, PlayBuildingView b, List<UiItem> items, int number = 0)
    {
        PlayBuildingInfo t = c.Buildings[b.Type];
        if (number > 0)
        {
            // In the hex overview: a section per building, its buttons in one row.
            items.Add(new SectionItem($"{number}. {t.Name}" + (b.Civ == v.Civ ? "" : $" ({PeopleName(v, b.Civ)})")));
        }

        if (b.Remembered)
        {
            items.Add(new HeaderItem(t.Name, $"{PeopleName(v, b.Civ)} - as we last saw it", new Color("6a6a6a"), UiKit.Short(t.Name)));
            items.Add(new LineItem("Out of sight. It may be gone or changed.", UiKit.Muted, 14));
            return;
        }

        string state = b.State switch
        {
            5 when b.Porters > 0 => "Materials on the way",
            5 => "Stopped: materials missing",
            0 => "Being built",
            1 => "Construction paused",
            3 => "Stopped",
            _ => b.Starved ? "Waiting for goods" : t.Workers > 0 ? (b.Workers == 0 ? "No workers" : "Working") : "Standing",
        };
        bool mine = b.Civ == v.Civ;
        if (number == 0)
        {
            items.Add(new HeaderItem(t.Name, mine ? state : $"{state} - {PeopleName(v, b.Civ)}", UiKit.GroupColor(t.Groups), UiKit.Short(t.Name)));
        }
        else
        {
            items.Add(new LineItem(state, ShortState(b, t).Color, 14));
        }
        if (mine && b.Focus is { Length: > 0 } focus && c.Categories is { } cats)
        {
            // Every worker of a school speeds one branch of research; click to change it.
            items.Add(new SectionItem("Research it speeds"));
            int bid = b.Id;
            for (int k = 0; k < focus.Length; k++)
            {
                int worker = k;
                string now = focus[k];
                var choices = cats.Where(x => x != "basics").ToList();
                int next = (choices.IndexOf(now) + 1) % choices.Count;
                int index = cats.ToList().IndexOf(choices[next]);
                bool works = k < b.Workers;
                items.Add(new ActItem($"Worker {k + 1}: {now}" + (works ? "" : " (no worker)"), () => _session!.Play(PlayCommandKind.SetFocus, bid, b: worker, c: index),
                    $"Click to make this worker speed {choices[next]} instead. Every worker speeds one branch; a branch is at most 50% faster.", works));
            }
        }

        if (b.State is 0 or 1 or 5)
        {
            items.Add(new BarItem("Built", b.ProgressPermille / 1000f, $"{b.ProgressPermille / 10}%", new Color("8fb040")));
            if (b.State == 5 && b.Porters > 0)
            {
                items.Add(new LineItem($"{b.Porters} group(s) of porters are walking here from the nearest storehouse. The work goes on as they arrive.", UiKit.Muted, 13));
            }

            if (b.Missing is { } missing)
            {
                items.Add(new LineItem($"Missing: {missing}", b.Porters == 0 ? UiKit.Bad : UiKit.Warn, 14));
            }
        }

        if (b.State == 2 && t.Workers > 0)
        {
            items.Add(new BarItem("Workers", (float)b.Workers / t.Workers, $"{b.Workers} of {t.Workers}", new Color("4f7fb8")));
            items.Add(new LineItem($"Each worker a minute: {Amounts(c, t.Produces)}" + (Any(t.Consumes) ? $" from {Amounts(c, t.Consumes)}" : ""), UiKit.Muted, 13));
            if (b.Starved)
            {
                items.Add(new LineItem($"Waiting for {Amounts(c, t.Consumes)}.", UiKit.Warn, 14));
            }

            if (b.YieldPercent > 100)
            {
                items.Add(new LineItem($"Rich spot: +{b.YieldPercent - 100}%", UiKit.Good, 14));
            }
        }

        if (t.Housing > 0)
        {
            items.Add(new LineItem($"Homes for {t.Housing} people", UiKit.Muted, 13));
        }

        if (!mine)
        {
            return;
        }

        int id = b.Id;
        int start = items.Count;

        // Upgrade in place, once the better kind is known.
        if (t.UpgradesTo > 0)
        {
            PlayBuildingInfo to = c.Buildings[t.UpgradesTo];
            bool known = v.Techs.FirstOrDefault(x => x.Id == to.Tech)?.Known ?? false;
            if (!known)
            {
                // Only within reach: nothing from an era we have not entered (Patryk, 2026-11-19).
                if (Reached(v, c, to.Tech))
                {
                    items.Add(new LineItem($"Later: upgrade to {to.Name} with {c.Techs[to.Tech].Name}", UiKit.Muted, 13));
                }

                start = items.Count;
            }
            else if (b.UpgradeBlocked is { } why)
            {
                items.Add(new ActItem($"Upgrade to {to.Name}", () => _session!.Play(PlayCommandKind.UpgradeBuilding, id),
                    why.Length == 0 ? $"Rebuilt in place: half of its price counts, porters bring the rest ({b.UpgradeCost}). It stops working while it is rebuilt." : why,
                    why.Length == 0, UiKit.Good));
                if (why.Length > 0 && number > 0)
                {
                    items.Insert(start, new LineItem($"Upgrade to {to.Name}: {why}", UiKit.Warn, 13));
                    start++;
                }
                else if (why.Length == 0)
                {
                    items.Insert(start, new LineItem($"Upgrade to {to.Name} costs {b.UpgradeCost}", UiKit.Muted, 13));
                    start++;
                }
            }
        }

        if (b.Missing is not null && b.State is 0 or 1 or 5)
        {
            items.Add(new ActItem($"Bring the missing materials", () => _session!.Play(PlayCommandKind.BringMaterials, id), $"Porters carry {b.Missing} from the nearest storehouse. We pay for it again.", Accent: UiKit.Good));
        }

        if (b.State == 0)
        {
            items.Add(new ActItem("Pause construction", () => _session!.Play(PlayCommandKind.PauseConstruction, id, flag: true)));
        }
        else if (b.State == 1)
        {
            items.Add(new ActItem("Resume construction", () => _session!.Play(PlayCommandKind.PauseConstruction, id, flag: false), "Resuming costs a part of the price again."));
        }
        else if (b.State == 2 && t.Workers > 0)
        {
            items.Add(new ActItem(b.Priority ? "Normal priority" : "Workers here first", () => _session!.Play(PlayCommandKind.Priority, id, flag: !b.Priority), "Workers go to priority buildings first."));
            items.Add(new ActItem("Stop work", () => _session!.Play(PlayCommandKind.Stop, id, flag: true), "Its workers go to other jobs."));
        }
        else if (b.State == 3)
        {
            items.Add(new ActItem("Start work", () => _session!.Play(PlayCommandKind.Stop, id, flag: false)));
        }

        items.Add(new ActItem("Tear down", () => _session!.Play(PlayCommandKind.Demolish, id), "The building is gone; its places are free again.", Accent: UiKit.Bad));
        if (number > 0)
        {
            // Side by side: the buttons of this building in one row.
            var acts = items.Skip(start).OfType<ActItem>().ToList();
            items.RemoveAll(x => x is ActItem a && acts.Contains(a));
            items.Add(new ButtonsItem(acts));
        }
    }

    // The order to gather around the settlement, with what one gatherer brings here (the land's richness).
    private void GatherItems(PlayContentMessage c, PlaySettlementView s, List<UiItem> items)
    {
        int sid = s.Id;
        var per = new List<string>();
        if (s.Richness is { } rich)
        {
            foreach (string key in new[] { "food", "wood", "stone" })
            {
                int g = c.Goods.ToList().FindIndex(x => x.Key == key);
                if (g >= 0 && g < rich.Length && rich[g] > 0)
                {
                    per.Add($"{key} {rich[g]}%");
                }
            }
        }

        string land = per.Count == 0 ? "nothing to gather here" : string.Join(", ", per);
        items.Add(new ActItem(s.Gathering ? "Stop gathering" : "Gather around", () => _session!.Play(PlayCommandKind.Gather, sid, flag: !s.Gathering),
            $"People without a job pick up food, wood and stone around the settlement. How much depends on the land: {land} of a rich land."));
    }

    private void UnitItems(PlayViewMessage v, PlayUnitView u, PlayContentMessage c, List<UiItem> items)
    {
        PlayUnitInfo t = c.Units[u.Kind];
        string state = u.State == 0 ? "Training" : u.Fighting ? "FIGHTING" : u.State == 2 ? "Walking" : "Standing";
        items.Add(new HeaderItem(t.Name, $"{u.People} people - {state}", t.Army ? new Color("b84040") : new Color("4f8f7f"), UiKit.Short(t.Name)));
        if (u.Cargo is { } load && load != "nothing")
        {
            items.Add(new LineItem($"Carrying {load}", UiKit.Text, 14));
        }

        if (t.Carries > 0)
        {
            items.Add(new LineItem(u.Aboard > 0 ? $"{u.Aboard} of {t.Carries} soldiers aboard: right click a land hex next to the ship to land them" : $"Carries up to {t.Carries} soldiers: right click this ship with a land unit next to it", UiKit.Muted, 13));
        }

        if (u.Injured > 0)
        {
            items.Add(new LineItem($"{u.Injured} wounded: bring them to a settlement with herbalists", UiKit.Warn, 14));
        }

        // Prospectors and surveyors: they look at the land only while they stand; what they know around them is listed.
        if (t.Survey > 0 && u.State != 0)
        {
            string what = t.Survey == 1 ? "the rock (stone, clay, flint, ores of our era)" : "the soil (fertility, wild grain, cattle, sheep)";
            items.Add(new LineItem(u.State == 1 ? $"Standing: they look at {what} within 3 hexes" : $"Walking: they look at {what} only when they stand", u.State == 1 ? UiKit.Good : UiKit.Muted, 13));
            if (u.Found is { Length: > 0 } found && _session?.Content is { } world)
            {
                items.Add(new SectionItem("Found around"));
                var acts = new List<ActItem>();
                for (int k = 0; k + 2 < found.Length; k += 3)
                {
                    int col = found[k + 1];
                    int row = found[k + 2];
                    int far = Math.Max(Math.Abs(col - u.Col), Math.Abs(row - u.Row));
                    acts.Add(new ActItem($"{world.Deposits[found[k] - 1].Name} ({far})", () => Focus(col, row), "Show it on the map"));
                }

                items.Add(new ButtonsItem(acts));
            }
            else if (u.State == 1)
            {
                items.Add(new LineItem("Nothing of this kind around here.", UiKit.Muted, 13));
            }
        }

        // Rations: the home settlement feeds the unit once a minute, wherever it is.
        if (u.State != 0)
        {
            string home = v.Settlements.FirstOrDefault(x => x.Id == u.Home)?.Name ?? "our nearest settlement";
            if (u.Hunger == 0)
            {
                items.Add(new LineItem($"Fed by {home}: ration in {v.RationSeconds} s", UiKit.Muted, 13));
            }
            else
            {
                int pct = u.Hunger == 1 ? c.HungryPercent : c.StarvingPercent;
                items.Add(new LineItem(u.Hunger == 1
                    ? $"HUNGRY: {home} had no food for it. Strength and speed {pct}%; missing the next ration ({v.RationSeconds} s) kills some"
                    : $"STARVING: strength and speed {pct}%, some die at every missed ration. Bring food to {home}", UiKit.Bad, 14));
            }
        }

        if (t.Army)
        {
            items.Add(new LineItem($"Strength {t.Strength} each" + (t.Range > 1 ? $", shoots {t.Range} hexes, only standing" : ""), UiKit.Text, 14));
            CombatItems(v, c, u, items);
        }

        int id = u.Id;
        if (t.System)
        {
            items.Add(new LineItem("Carriers follow their task. Guard them with soldiers: killed carriers lose their load.", UiKit.Muted, 13));
            return;
        }

        items.Add(new LineItem(t.Army ? "Right click a hex to go there, an enemy to attack it. Hold the right button and draw to choose the way." : "Right click a hex to send them there. Hold the right button and draw to choose the way.", UiKit.Muted, 13));
        int more = _overlay?.SelectedUnits.Count ?? 0;
        if (more > 1)
        {
            items.Add(new LineItem($"{more} units selected: orders go to all of them.", UiKit.Warn, 14));
        }

        if (u.State != 0)
        {
            items.Add(new ActItem("Draw a patrol", () => ArmDraw(DrawMode.Patrol), "Draw a loop over the hexes. They walk it again and again; their settlement feeds them at the ration. Stop patrols in the Units window (P or 6).", Accent: UiKit.Good));
        }
        if (t.CanHide)
        {
            bool hidden = _hiddenWalk.Contains(id);
            items.Add(new LineItem(hidden ? "They walk hidden: slower, seen only up close." : "They walk in the open: faster, seen from afar.", UiKit.Muted, 13));
            items.Add(new ActItem(hidden ? "Walk in the open" : "Walk hidden", () =>
            {
                if (!_hiddenWalk.Remove(id))
                {
                    _hiddenWalk.Add(id);
                }
            }));
        }

        if (t.Founds && u.State == 1)
        {
            items.Add(new ActItem("Found a camp here", () => _session!.Play(PlayCommandKind.Found, id), "A new settlement with these people and the goods they carry.", Accent: UiKit.Good));
        }

        if (u.State != 0)
        {
            items.Add(new ActItem("Go home (join the settlement here)", () => _session!.Play(PlayCommandKind.Disband, id)));
        }
    }

    private void HexItems((int Col, int Row) sel, List<UiItem> items)
    {
        if (_session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w) || _session.Content is not { } content)
        {
            return;
        }

        int i = w.Index(sel.Col, sel.Row);
        if (w.Visibility[i] == 0)
        {
            items.Add(new HeaderItem("Unknown land", "Send scouts to see it", new Color("3a3632"), "?"));
            return;
        }

        HexHeader(sel, items);
        WorksItems(sel, items, always: w.Owner[i] == _session.PlayView?.Civ);
    }

    // True when a technology belongs to an era we have entered (or is none): what lies beyond is not shown.
    private static bool Reached(PlayViewMessage v, PlayContentMessage c, int tech) => tech < 0 || tech >= c.Techs.Count || c.Techs[tech].Era <= v.Era;

    // What the land of a hex is: biome, relief, owner, deposit, grove.
    private void HexHeader((int Col, int Row) sel, List<UiItem> items)
    {
        if (_session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w) || _session.Content is not { } content)
        {
            return;
        }

        int i = w.Index(sel.Col, sel.Row);
        string biome = content.Biomes[w.Biome[i]].Name;
        string relief = content.Reliefs[w.Relief[i]];
        string owner = w.Owner[i] < 0 ? "Nobody's land" : w.Owner[i] == _session.PlayView?.Civ ? "Our land" : "Land of another people";
        items.Add(new HeaderItem(biome, $"{relief} - {owner}", new Color("5f7f4a"), UiKit.Short(biome)));
        // Only what we know of the ground (2026-11-19): beyond our land prospectors find the rock, surveyors the soil.
        if (w.Deposit[i] != 0)
        {
            items.Add(new LineItem($"Deposit: {content.Deposits[w.Deposit[i] - 1].Name}", UiKit.Good, 14));
        }
        else if (w.Survey[i] != 255 && (w.Survey[i] & MapChunkMessage.DepositHidden) != 0)
        {
            items.Add(new LineItem("Deposit: ??? (an ore of a later era: we will know it when our people get there)", UiKit.Warn, 14));
        }

        byte survey = w.Survey[i];
        if (survey != 255)
        {
            bool rock = (survey & MapChunkMessage.RockLooked) != 0;
            bool soil = (survey & MapChunkMessage.SoilKnown) != 0;
            items.Add(new LineItem(soil ? $"Soil: fertility {w.Fertility[i]}" : "Soil not surveyed: surveyors find its fertility", soil ? UiKit.Text : UiKit.Muted, 13));
            if (!rock)
            {
                items.Add(new LineItem("Rock not prospected in this era: prospectors find stone, clay, flint and ores", UiKit.Muted, 13));
            }
        }

        if (w.Trees[i] == 1)
        {
            items.Add(new LineItem("A grove: woodcutters can work it (60% of a forest), gatherers find wood here", UiKit.Good, 14));
        }
    }

    // The works on a hex (ditch, palisade, wall, aqueduct, bridges), with a button to remove each on our land.
    // With always: every kind of work is listed, built or not (the protection of the hex at a glance).
    private void WorksItems((int Col, int Row) sel, List<UiItem> items, bool always = false)
    {
        if (_session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w) || _session.PlayContent is not { } pc)
        {
            return;
        }

        int i = w.Index(sel.Col, sel.Row);
        int works = w.Works[i];
        if (works == 0 && !always)
        {
            return;
        }

        items.Add(new SectionItem("Protection and works"));
        bool ours = w.Owner[i] == _session.PlayView?.Civ;
        if (always)
        {
            var cells = new List<CellItem>();
            foreach (PlayWorkInfo wk in pc.Works ?? [])
            {
                bool has = (works & (1 << wk.Id)) != 0;
                bool known = _session.PlayView?.Techs.FirstOrDefault(x => x.Id == wk.Tech)?.Known ?? false;
                if (!has && _session.PlayView is { } pv && !Reached(pv, pc, wk.Tech))
                {
                    continue; // not before its era
                }

                cells.Add(new CellItem(wk.Name, has ? UiKit.Text : UiKit.Muted));
                cells.Add(new CellItem(has ? "yes" : known ? "no (draw it in Roads & works)" : $"no ({pc.Techs[wk.Tech].Name})", has ? UiKit.Good : UiKit.Muted));
            }

            if ((works & (7 << 8)) != 0)
            {
                cells.Add(new CellItem("Bridge", UiKit.Text));
                cells.Add(new CellItem("yes", UiKit.Good));
            }

            if (cells.Count > 0)
            {
                items.Add(new TableItem(2, cells));
            }
        }

        foreach (PlayWorkInfo wk in pc.Works ?? [])
        {
            if ((works & (1 << wk.Id)) == 0)
            {
                continue;
            }

            bool water = wk.FromWater > 0;
            bool reached = (works & (wk.Key == "aqueduct" ? 32 : 16)) != 0;
            string state = water ? (reached ? " (water flows)" : " (dry: not joined to water)") : "";
            if (!always || (water && !reached))
            {
                items.Add(new LineItem(wk.Name + state, water && !reached ? UiKit.Warn : UiKit.Text, 14));
            }

            if (ours)
            {
                int id = wk.Id;
                items.Add(new ActItem($"Remove the {wk.Name.ToLowerInvariant()}", () => _session!.Play(PlayCommandKind.RemoveWork, id, sel.Col, sel.Row), wk.Text, Accent: UiKit.Bad));
            }
        }

        if ((works & (7 << 8)) != 0 && !always)
        {
            items.Add(new LineItem("A bridge crosses the river here", UiKit.Text, 14));
        }
    }

    private static int Dist(PlaySettlementView a, PlaySettlementView b) => Math.Abs(a.Col - b.Col) + Math.Abs(a.Row - b.Row);

    private static string PeopleName(PlayViewMessage v, int civ)
    {
        foreach (PlayPeopleView p in v.Peoples)
        {
            if (p.Civ == civ)
            {
                return p.Name;
            }
        }

        return "strangers";
    }
}
