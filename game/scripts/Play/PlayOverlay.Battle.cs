using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Game.Map;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// What a battle shows (Patryk, 2026-11-19, no prediction, only what happens): a bar of people under each soldier token (green,
/// yellow from half, red from a quarter, grey for the wounded), losses rising over the unit, "Charge!", arrows from each unit to
/// what it struck (red: the target is struck from two sides or more), and in combat mode the cover and height of the hex under the mouse.
/// </summary>
public partial class PlayOverlay : Node2D
{
    private const double PopupSeconds = 1.4;
    private readonly List<(Vector2 At, string Text, Color Color, double Born)> _popups = [];
    private readonly Dictionary<int, int> _lastPeople = [];
    private readonly HashSet<int> _lastCharging = [];

    // Patryk, 2026-11-24: a strike stays on screen a while and fades (the view holds only the last combat step, so an arrow
    // flickered); archers' arrows fly along their line.
    private const double StrikeSeconds = 1.5;
    private readonly Dictionary<(int From, int To), double> _strikes = [];

    /// <summary>Combat mode is on: the hex under the mouse shows its cover and height.</summary>
    public bool CombatHover { get; set; }

    private void HealthBar(CanvasItem g, Vector2 at, PlayUnitView u)
    {
        const float W = 24;
        float share = Math.Clamp((float)u.People / Math.Max(1, u.Size), 0, 1);
        float hurt = Math.Clamp((float)u.Injured / Math.Max(1, u.Size), 0, share);
        Color fill = share > 0.5f ? new Color(0.35f, 0.85f, 0.35f) : share > 0.25f ? new Color(0.95f, 0.8f, 0.25f) : new Color(0.95f, 0.3f, 0.25f);
        g.DrawRect(new Rect2(at - new Vector2(1, 1), new Vector2(W + 2, 6)), new Color(0, 0, 0, 0.8f));
        g.DrawRect(new Rect2(at, new Vector2(W * (share - hurt), 4)), fill);
        if (hurt > 0)
        {
            g.DrawRect(new Rect2(at + new Vector2(W * (share - hurt), 0), new Vector2(W * hurt, 4)), new Color(0.6f, 0.6f, 0.6f));
        }
    }

    // A new view: losses since the last one rise over the units, a fresh charge is told.
    private void BattleNews(PlayViewMessage view)
    {
        var seen = new HashSet<int>();
        foreach (PlayUnitView u in view.Units)
        {
            seen.Add(u.Id);
            if (_lastPeople.TryGetValue(u.Id, out int before) && u.People < before && (u.Fighting || u.LastLoss > 0))
            {
                Popup(u.Col, u.Row, "-" + (before - u.People).ToString(CultureInfo.InvariantCulture), new Color(1f, 0.35f, 0.3f));
            }

            _lastPeople[u.Id] = u.People;
            foreach (int t in u.Struck ?? [])
            {
                _strikes[(u.Id, t)] = _time;
            }

            if (u.Charging && _lastCharging.Add(u.Id))
            {
                Popup(u.Col, u.Row, "Charge!", new Color(1f, 0.85f, 0.3f));
            }
            else if (!u.Charging)
            {
                _lastCharging.Remove(u.Id);
            }
        }

        // Units gone from the view: their last people fell or they walked away.
        var gone = new List<int>();
        foreach (int id in _lastPeople.Keys)
        {
            if (!seen.Contains(id))
            {
                gone.Add(id);
            }
        }

        foreach (int id in gone)
        {
            _lastPeople.Remove(id);
        }
    }

    private void Popup(int col, int row, string text, Color color) => _popups.Add((HexLayout.Center(col, row) + new Vector2(10, -24), text, color, _time));

    private void PaintBattle(CanvasItem g)
    {
        if (_view is null || _content is null)
        {
            return;
        }

        // Who strikes whom: count the strikers of each target first (two or more: the target is flanked).
        var byId = new Dictionary<int, PlayUnitView>();
        var strikers = new Dictionary<int, int>();
        foreach (PlayUnitView u in _view.Units)
        {
            byId[u.Id] = u;
        }

        foreach (PlayUnitView u in _view.Units)
        {
            foreach (int t in u.Struck ?? [])
            {
                if (_content.Units[u.Kind].Range > 1)
                {
                    continue; // arrows of archers do not flank
                }

                strikers[t] = strikers.GetValueOrDefault(t) + 1;
            }
        }

        var old = new List<(int, int)>();
        foreach (((int from, int to), double at) in _strikes)
        {
            double age = (_time - at) / StrikeSeconds;
            if (age >= 1 || !byId.TryGetValue(from, out PlayUnitView? u) || !byId.TryGetValue(to, out PlayUnitView? target))
            {
                old.Add((from, to));
                continue;
            }

            bool ranged = _content.Units[u.Kind].Range > 1;
            bool ours = u.Civ == _view.Civ;
            bool flanked = strikers.GetValueOrDefault(to) >= 2;
            float fade = 1f - (float)(age * age);
            Color color = flanked ? new Color(1f, 0.25f, 0.2f, 0.95f * fade) : ours ? new Color(1f, 0.85f, 0.35f, 0.9f * fade) : new Color(1f, 0.55f, 0.3f, 0.9f * fade);
            foreach (Vector2 start in Copies(u.Col, u.Row))
            {
                Vector2 a = start + UnitBody;
                Vector2 b = Nearest(a, HexLayout.Center(target.Col, target.Row) + UnitBody);
                // As thick on screen at any zoom (far away the lines were hairs).
                float k = Math.Clamp(1.2f / Math.Max(0.1f, _zoom), 1f, 4f);
                Arrow(g, a, b, color, (ranged ? 2.5f : flanked ? 4.5f : 3.5f) * k, ranged, k);
                if (ranged)
                {
                    Volley(g, a, b, color, k);
                }
            }
        }

        foreach ((int, int) k in old)
        {
            _strikes.Remove(k);
        }

        // Losses and charges rising over the units.
        foreach ((Vector2 at, string text, Color color, double born) in _popups)
        {
            float age = (float)((_time - born) / PopupSeconds);
            var c = new Color(color, 1f - (age * age));
            foreach (Vector2 copy in Copies(0, 0))
            {
                Vector2 p = at + copy - HexLayout.Center(0, 0) + new Vector2(0, -22f * age);
                g.DrawString(UiKit.GlyphFont, p + new Vector2(-40, 1), text, HorizontalAlignment.Center, 80, 15, new Color(0, 0, 0, c.A));
                g.DrawString(UiKit.GlyphFont, p + new Vector2(-41, 0), text, HorizontalAlignment.Center, 80, 15, c);
            }
        }

        if (CombatHover && _hover is { } h)
        {
            PaintCover(g, h);
        }
    }

    // The copy of b nearest to a, east-west wrapping.
    private Vector2 Nearest(Vector2 a, Vector2 b)
    {
        if (_period.X > 0)
        {
            while (b.X - a.X > _period.X / 2)
            {
                b.X -= _period.X;
            }

            while (a.X - b.X > _period.X / 2)
            {
                b.X += _period.X;
            }
        }

        return b;
    }

    // Arrows in flight from archers to their target: three small darts moving along the line.
    private void Volley(CanvasItem g, Vector2 a, Vector2 b, Color color, float scale = 1f)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 20)
        {
            return;
        }

        Vector2 dir = d / len;
        Vector2 side = new(-dir.Y, dir.X);
        for (int k = 0; k < 3; k++)
        {
            float t = (float)(((_time * 1.6) + (k / 3.0)) % 1.0);
            Vector2 p = a.Lerp(b, 0.15f + (0.7f * t)) + (side * ((k - 1) * 4f * scale));
            g.DrawLine(p - (dir * 7 * scale), p, new Color(0.15f, 0.1f, 0.05f, color.A), 3f * scale);
            g.DrawLine(p - (dir * 7 * scale), p, new Color(0.95f, 0.92f, 0.85f, color.A), 1.5f * scale);
            g.DrawColoredPolygon([p + (dir * 3 * scale), p - (side * 2.5f * scale), p + (side * 2.5f * scale)], new Color(0.95f, 0.92f, 0.85f, color.A));
        }
    }

    private static void Arrow(CanvasItem g, Vector2 a, Vector2 b, Color color, float width, bool dashed, float head = 1f)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len < 4)
        {
            return;
        }

        Vector2 dir = d / len;
        Vector2 end = b - (dir * 12); // stop at the edge of the token
        Vector2 start = a + (dir * 12);
        if (dashed)
        {
            g.DrawDashedLine(start, end, color, width, 5);
        }
        else
        {
            g.DrawLine(start, end, color, width);
        }

        Vector2 side = new(-dir.Y, dir.X);
        g.DrawColoredPolygon([end + (dir * 6 * head), end - (dir * 4 * head) + (side * 5 * head), end - (dir * 4 * head) - (side * 5 * head)], color);
    }

    // Cover and height of the hex under the mouse for one of our soldiers standing there: green good, yellow some, red open.
    private void PaintCover(CanvasItem g, (int Col, int Row) h)
    {
        if (World is not { } w || _content?.Combat is not { } r || _view is null)
        {
            return;
        }

        int i = w.Index(h.Col, h.Row);
        if (w.Visibility[i] == MapChunkMessage.Unknown)
        {
            return;
        }

        int ranged = 0;
        int melee = 0;
        string what = "";
        bool ours = w.Owner[i] == _view.Civ;
        int wallBit = WorkBit("wall");
        int palisadeBit = WorkBit("palisade");
        if (ours && wallBit != 0 && (w.Works[i] & wallBit) != 0)
        {
            (ranged, melee, what) = (r.WallRanged, r.WallMelee, "Wall");
        }
        else if (ours && palisadeBit != 0 && (w.Works[i] & palisadeBit) != 0)
        {
            (ranged, melee, what) = (r.PalisadeRanged, r.PalisadeMelee, "Palisade");
        }
        else if (w.Trees[i] != 0)
        {
            (ranged, melee, what) = (r.TreeRanged, r.TreeMelee, "Trees");
        }

        bool open = w.Road[i] != 0 || Array.IndexOf(r.OpenBiomes, (int)w.Biome[i]) >= 0;
        var lines = new List<string>();
        if (what.Length > 0)
        {
            lines.Add($"{what}: arrows {ranged}%, melee {melee}% (lost when struck from two sides)");
        }

        if (open)
        {
            lines.Add($"Open ground: {r.OpenPercent}% more taken");
            ranged += r.OpenPercent;
            melee += r.OpenPercent;
        }

        // Height against the nearest enemy soldier we see.
        PlayUnitView? foe = null;
        float best = float.MaxValue;
        Vector2 here = HexLayout.Center(h.Col, h.Row);
        foreach (PlayUnitView u in _view.Units)
        {
            if (u.Civ != _view.Civ && _content.Units[u.Kind].Army)
            {
                float d = (Nearest(here, HexLayout.Center(u.Col, u.Row)) - here).Length();
                if (d < best && d < HexLayout.RowStep * 8)
                {
                    best = d;
                    foe = u;
                }
            }
        }

        int height = 0;
        if (foe is not null)
        {
            int j = w.Index(foe.Col, foe.Row);
            int relief = Math.Clamp((w.Relief[i] - w.Relief[j]) * r.ReliefPercent, -r.ReliefMaxPercent, r.ReliefMaxPercent);
            int elevation = Math.Clamp((w.Elevation[i] - w.Elevation[j]) / Math.Max(1, r.ElevationStep) * r.ElevationPercent, -r.ElevationMaxPercent, r.ElevationMaxPercent);
            height = relief + elevation;
            if (height != 0)
            {
                lines.Add(height > 0 ? $"Above the nearest enemy: strike +{height}%" : $"Below the nearest enemy: strike {height}%");
            }
        }

        int score = -Math.Min(ranged, melee) + height;
        Color color = score >= 15 ? new Color(0.35f, 0.95f, 0.4f) : score > 0 ? new Color(0.95f, 0.85f, 0.3f) : score < 0 ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 1f, 1f, 0.6f);
        foreach (Vector2 c in Copies(h.Col, h.Row))
        {
            g.DrawPolyline(Closed(Hex(c, 0.9f)), color, 3f);
            if (height != 0)
            {
                Vector2 tip = c + new Vector2(-14, height > 0 ? -10 : 10);
                float s = height > 0 ? -1 : 1;
                g.DrawColoredPolygon([tip, tip + new Vector2(-6, -8 * s), tip + new Vector2(6, -8 * s)], color);
            }

            for (int k = 0; k < lines.Count; k++)
            {
                Vector2 p = c + new Vector2(-170, -36 - ((lines.Count - k) * 16)); // above the hex: the bar may hide what is below
                g.DrawString(UiKit.GlyphFont, p + new Vector2(1, 1), lines[k], HorizontalAlignment.Center, 340, 13, new Color(0, 0, 0, 0.9f));
                g.DrawString(UiKit.GlyphFont, p, lines[k], HorizontalAlignment.Center, 340, 13, color);
            }
        }
    }

    private int WorkBit(string key)
    {
        foreach (PlayWorkInfo wk in _content?.Works ?? [])
        {
            if (wk.Key == key)
            {
                return 1 << wk.Id;
            }
        }

        return 0;
    }
}
