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
                    UnitItems(u, c, items);
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
            int used = here.Sum(b => System.Numerics.BitOperations.PopCount((uint)b.Slots));
            if (here.Count > 1 || used < 4)
            {
                items.Add(new LineItem($"{used} of 4 places on this hex taken", UiKit.Muted, 13));
            }

            foreach (PlayBuildingView b in here)
            {
                BuildingItems(v, c, b, items);
            }

            WorksItems(sel, items);
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
            var tiles = new List<TileCell>();
            foreach (PlayUnitInfo ut in c.Units)
            {
                if (!v.Techs[ut.Tech].Known || ut.System)
                {
                    continue;
                }

                int kind = ut.Id;
                int sid = s.Id;
                bool can = CanPay(stock, ut.Price) && s.People > ut.People;
                string tip = $"{ut.Name}: {ut.People} people, {Amounts(c, ut.Price)}, {ut.TrainSeconds} s."
                    + (ut.Army ? $"\nStrength {ut.Strength} each" + (ut.Range > 1 ? $", shoots {ut.Range} hexes." : ".") : "")
                    + (ut.Founds ? "\nThey found a new camp where you send them." : "")
                    + (ut.CanHide ? "\nCan walk hidden." : "")
                    + (ut.NeedsBuilding.Length > 0 ? $"\nNeeds a {ut.NeedsBuilding.Replace('_', ' ')} in the settlement." : "")
                    + (can ? "" : "\n\nNot enough people or goods here now.");
                tiles.Add(new TileCell(ut.Name, UiKit.Short(ut.Name), ut.Army ? new Color("b84040") : new Color("4f8f7f"), tip,
                    () => _session!.Play(PlayCommandKind.Train, sid, b: kind), can, $"{ut.Name}"));
            }

            items.Add(new TilesItem(tiles));
            var training = v.Units.Where(u => u.Civ == v.Civ && u.State == 0 && u.Col == s.Col && u.Row == s.Row).Select(u => c.Units[u.Kind].Name.ToLowerInvariant()).ToList();
            if (training.Count > 0)
            {
                items.Add(new LineItem("Training: " + string.Join(", ", training), UiKit.Muted, 13));
            }

            items.Add(new SectionItem("Settlement"));
            if (s.Level + 1 < c.Levels.Count)
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
                items.Add(new ActItem($"Supply route: {name} from {o.Name}", () => _session!.Play(PlayCommandKind.Route, fromId, good, flag: false, b: to, c: 20),
                    "A caravan keeps bringing 20 a trip while there are at least 10 to send."));
            }
        }
    }

    private void CaravanItems(PlayViewMessage v, PlayContentMessage c, PlaySettlementView s, long[] stock, List<UiItem> items)
    {
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
                items.Add(new ActItem($"Route: {name} to {o.Name}, every trip", () => _session!.Play(PlayCommandKind.Route, from, good, flag: false, b: to, c: 20), "The caravans take the shortest way."));
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

    private void BuildingItems(PlayViewMessage v, PlayContentMessage c, PlayBuildingView b, List<UiItem> items)
    {
        PlayBuildingInfo t = c.Buildings[b.Type];
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
        items.Add(new HeaderItem(t.Name, mine ? state : $"{state} - {PeopleName(v, b.Civ)}", UiKit.GroupColor(t.Groups), UiKit.Short(t.Name)));
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

        items.Add(new ActItem("Tear down", () => _session!.Play(PlayCommandKind.Demolish, id), "The building is gone; the hex is free again.", Accent: UiKit.Bad));
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

    private void UnitItems(PlayUnitView u, PlayContentMessage c, List<UiItem> items)
    {
        PlayUnitInfo t = c.Units[u.Kind];
        string state = u.State == 0 ? "Training" : u.Fighting ? "FIGHTING" : u.State == 2 ? "Walking" : "Standing";
        items.Add(new HeaderItem(t.Name, $"{u.People} people - {state}", t.Army ? new Color("b84040") : new Color("4f8f7f"), UiKit.Short(t.Name)));
        if (u.Cargo is { } load && load != "nothing")
        {
            items.Add(new LineItem($"Carrying {load}", UiKit.Text, 14));
        }

        if (u.Injured > 0)
        {
            items.Add(new LineItem($"{u.Injured} wounded: bring them to a settlement with herbalists", UiKit.Warn, 14));
        }

        if (t.Army)
        {
            items.Add(new LineItem($"Strength {t.Strength} each" + (t.Range > 1 ? $", shoots {t.Range} hexes" : ""), UiKit.Text, 14));
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
            items.Add(new ActItem("Draw a patrol", () => ArmDraw(DrawMode.Patrol), "Draw a loop over the hexes. They walk it, go to the nearest settlement for food, and walk it again.", Accent: UiKit.Good));
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

        string biome = content.Biomes[w.Biome[i]].Name;
        string relief = content.Reliefs[w.Relief[i]];
        string owner = w.Owner[i] < 0 ? "Nobody's land" : w.Owner[i] == _session.PlayView?.Civ ? "Our land" : "Land of another people";
        items.Add(new HeaderItem(biome, $"{relief} - {owner}", new Color("5f7f4a"), UiKit.Short(biome)));
        if (w.Deposit[i] != 0 && w.DepositDepth[i] == 0)
        {
            items.Add(new LineItem($"Deposit: {content.Deposits[w.Deposit[i] - 1].Name}", UiKit.Good, 14));
        }

        WorksItems(sel, items);
    }

    // The works on a hex (ditch, palisade, wall, aqueduct, bridges), with a button to remove each on our land.
    private void WorksItems((int Col, int Row) sel, List<UiItem> items)
    {
        if (_session is null || !_session.Worlds.TryGetValue(0, out ClientWorld? w) || _session.PlayContent is not { } pc)
        {
            return;
        }

        int i = w.Index(sel.Col, sel.Row);
        int works = w.Works[i];
        if (works == 0)
        {
            return;
        }

        items.Add(new SectionItem("On this hex"));
        bool ours = w.Owner[i] == _session.PlayView?.Civ;
        foreach (PlayWorkInfo wk in pc.Works ?? [])
        {
            if ((works & (1 << wk.Id)) == 0)
            {
                continue;
            }

            bool water = wk.FromWater > 0;
            bool reached = (works & (wk.Key == "aqueduct" ? 32 : 16)) != 0;
            string state = water ? (reached ? " (water flows)" : " (dry: not joined to water)") : "";
            items.Add(new LineItem(wk.Name + state, water && !reached ? UiKit.Warn : UiKit.Text, 14));
            if (ours)
            {
                int id = wk.Id;
                items.Add(new ActItem($"Remove the {wk.Name.ToLowerInvariant()}", () => _session!.Play(PlayCommandKind.RemoveWork, id, sel.Col, sel.Row), wk.Text, Accent: UiKit.Bad));
            }
        }

        if ((works & (7 << 8)) != 0)
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
