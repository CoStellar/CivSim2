using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The caravans window (Patryk, 2026-11-24: the side panel offered only the two nearest settlements and the two goods there
/// was most of, 20 at a time, and nothing could be asked for). Step by step: send from this settlement or ask for goods here;
/// the other settlement; the good, with what the giving settlement has; how much; then one caravan or a standing route.
/// The routes from and to this settlement are listed with a button to end each.
/// </summary>
public partial class PlayGame
{
    private int _carHere = -1;
    private bool _carAsk;
    private int _carOther = -1;
    private int _carGood = -1;
    private int _carAmount = 25;
    private static readonly int[] CarAmounts = [10, 25, 50, 100];

    /// <summary>Screenshots: the caravans window of our first settlement, asking for goods.</summary>
    public void ScriptCaravans()
    {
        if (_session?.PlayView is { } v && v.Settlements.FirstOrDefault(s => s.Civ == v.Civ && s.Stock is not null) is { } home)
        {
            _selected = (home.Col, home.Row);
            OpenCaravans(home.Id, ask: true);
        }
    }

    // Opens the window for a settlement of ours; asking (true) or sending (false) first.
    private void OpenCaravans(int settlement, bool ask, int good = -1)
    {
        if (_carHere != settlement)
        {
            _carOther = -1;
        }

        _carHere = settlement;
        _carAsk = ask;
        if (good >= 0)
        {
            _carGood = good;
        }

        OpenWindow("Caravans", toggle: false);
    }

    private void CaravanWindowItems(PlayViewMessage v, PlayContentMessage c, List<UiItem> items)
    {
        PlaySettlementView? here = v.Settlements.FirstOrDefault(x => x.Id == _carHere && x.Civ == v.Civ && x.Stock is not null);
        if (here is null)
        {
            items.Add(new LineItem("Select one of our settlements first (click it on the map, or Settlements, key 1).", UiKit.Muted, 14));
            return;
        }

        var others = v.Settlements.Where(o => o.Civ == v.Civ && o.Id != here.Id && o.Stock is not null).OrderBy(o => Dist(o, here)).ToList();

        // 1. Which way.
        items.Add(new SectionItem("1. What to do"));
        items.Add(new ButtonsItem(
        [
            new ActItem($"Send goods from {here.Name}", () => { _carAsk = false; _uiTimer = 0; }, $"A caravan leaves {here.Name} with goods for another of our settlements.", Accent: _carAsk ? null : UiKit.Good),
            new ActItem($"Ask for goods in {here.Name}", () => { _carAsk = true; _uiTimer = 0; }, $"Another of our settlements sends a caravan with goods to {here.Name}.", Accent: _carAsk ? UiKit.Good : null),
        ]));

        if (others.Count == 0)
        {
            items.Add(new LineItem("We have no other settlement yet: found one with settlers to move goods between them.", UiKit.Muted, 14));
            RouteListItems(v, c, here, items);
            return;
        }

        // 2. The other settlement.
        if (_carOther < 0 || others.All(o => o.Id != _carOther))
        {
            _carOther = others[0].Id;
        }

        items.Add(new SectionItem(_carAsk ? "2. From which settlement" : "2. To which settlement"));
        items.Add(new ButtonsItem([.. others.Select(o =>
        {
            int id = o.Id;
            string tip = $"# {o.Name}\n@Distance|about {Dist(o, here)} hexes\n@People|{o.People}" + (o.Besieged ? "\nUnder siege: no caravans come or go." : "");
            return new ActItem($"{o.Name} ({Dist(o, here)} hexes)", () => { _carOther = id; _uiTimer = 0; }, tip, !o.Besieged, _carOther == id ? UiKit.Good : null);
        })]));

        PlaySettlementView other = others.First(o => o.Id == _carOther);
        PlaySettlementView from = _carAsk ? other : here;
        PlaySettlementView to = _carAsk ? here : other;
        long[] source = from.Stock!;
        long[] target = to.Stock!;

        // 3. The good: every good the giving settlement has, with how much is there and at the other end.
        items.Add(new SectionItem($"3. Which good ({from.Name} has)"));
        var goods = Enumerable.Range(0, source.Length).Where(g => source[g] >= 100).ToList();
        if (goods.Count == 0)
        {
            items.Add(new LineItem($"{from.Name} has nothing to send.", UiKit.Muted, 14));
            RouteListItems(v, c, here, items);
            return;
        }

        if (!goods.Contains(_carGood))
        {
            _carGood = goods.OrderByDescending(g => source[g]).First();
        }

        items.Add(new TilesItem([.. goods.Select(g =>
        {
            int good = g;
            PlayGoodInfo info = c.Goods[g];
            string tip = $"# {info.Name}\n@In {from.Name}|{source[g] / 100}\n@In {to.Name}|{target[g] / 100}";
            return new TileCell(info.Name, UiKit.Short(info.Name), UiKit.GoodColor(info.Key), tip, () => { _carGood = good; _uiTimer = 0; }, true,
                $"{source[g] / 100}", "goods/good_" + info.Key, good == _carGood);
        })]));

        // 4. How much.
        long have = source[_carGood] / 100;
        string goodName = c.Goods[_carGood].Name.ToLowerInvariant();
        items.Add(new SectionItem($"4. How much {goodName}"));
        var amounts = CarAmounts.Where(a => a < have).Select(a => (a, $"{a}")).ToList();
        amounts.Add(((int)Math.Min(have, 10000), $"all {have}"));
        if (!amounts.Any(x => x.Item1 == _carAmount))
        {
            _carAmount = amounts.Count > 1 ? amounts[^2].Item1 : amounts[0].Item1;
        }

        items.Add(new ButtonsItem([.. amounts.Select(x =>
        {
            int n = x.Item1;
            return new ActItem(x.Item2, () => { _carAmount = n; _uiTimer = 0; }, $"{n} {goodName} a caravan.", Accent: _carAmount == n ? UiKit.Good : null);
        })]));

        // 5. Go.
        items.Add(new SectionItem("5. Send"));
        int f = from.Id, t = to.Id, gd = _carGood, amount = _carAmount;
        items.Add(new LineItem($"{amount} {goodName}: {from.Name} → {to.Name}", UiKit.Heading, 15));
        items.Add(new ButtonsItem(
        [
            new ActItem("One caravan", () => _session!.Play(PlayCommandKind.SendCaravan, f, gd, flag: false, b: t, c: amount),
                $"# One caravan\nIt takes {amount} {goodName} from {from.Name} to {to.Name} along the shortest way and comes back.\nIt walks on the map and can be raided.", !from.Besieged && !to.Besieged, UiKit.Good),
            new ActItem("Standing route", () => _session!.Play(PlayCommandKind.Route, f, gd, _routeKeep, flag: false, b: t, c: amount, d: _routeUntil),
                $"# Standing route\nA caravan keeps carrying {amount} {goodName} a trip while {from.Name} keeps {_routeKeep}" + (_routeUntil > 0 ? $" and {to.Name} has fewer than {_routeUntil}." : ".") + "\nThe limits are set below.", !from.Besieged && !to.Besieged),
            new ActItem("Route along a drawn way", () => { ArmDraw(DrawMode.Route, (f, t, gd)); OpenWindow(""); },
                $"Draw over the hexes the way the caravans take from {from.Name} to {to.Name}."),
        ]));
        items.Add(RouteLimitsItem());
        RouteListItems(v, c, here, items);
    }

    // The routes from and to this settlement, each with a button to end it.
    private void RouteListItems(PlayViewMessage v, PlayContentMessage c, PlaySettlementView here, List<UiItem> items)
    {
        var routes = (v.Routes ?? []).Where(r => r.From == here.Id || r.To == here.Id).ToList();
        items.Add(new SectionItem($"Standing routes of {here.Name} ({routes.Count})"));
        if (routes.Count == 0)
        {
            items.Add(new LineItem("None yet.", UiKit.Muted, 13));
        }

        foreach (PlayRouteView r in routes)
        {
            int index = r.Index;
            string a = v.Settlements.FirstOrDefault(x => x.Id == r.From)?.Name ?? "?";
            string b = v.Settlements.FirstOrDefault(x => x.Id == r.To)?.Name ?? "?";
            items.Add(new ActItem($"End: {r.Amount} {c.Goods[r.Good].Name.ToLowerInvariant()} {a} → {b}" + (r.OnTheRoad ? " (on the road)" : ""),
                () => _session!.Play(PlayCommandKind.EndRoute, index), "The caravan on the road finishes its trip; no new trips.", Accent: UiKit.Warn));
        }
    }
}
