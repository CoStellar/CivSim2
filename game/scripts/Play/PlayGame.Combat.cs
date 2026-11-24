using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Game.Map;
using CivSim.Game.Net;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// Combat mode (Patryk, 2026-11-19): F switches it. In it the left button selects soldiers only: a click picks one unit, a drag
/// draws a box, Ctrl + click picks the unit and every unit of its kind standing next to it in a chain, Shift + drag paints the
/// hexes the selected units guard. A right click with several soldiers sends them in formation. Z hold, X aggressive, C passive,
/// V split in half, B refit at home, N stop guarding.
/// </summary>
public sealed partial class PlayGame
{
    private bool _combat;
    private Vector2? _boxFrom;
    private ReferenceRect? _box;
    private List<(int Col, int Row)>? _guardPaint;

    // Size of the next soldiers trained (0 = the kind's default) and weapons-only training.
    private int _trainSize;
    private bool _trainShock;

    /// <summary>True while combat mode is on.</summary>
    public bool CombatMode => _combat;

    private void ToggleCombat()
    {
        _combat = !_combat;
        _boxFrom = null;
        _guardPaint = null;
        if (_box is not null)
        {
            _box.Visible = false;
        }

        if (_overlay is not null)
        {
            _overlay.GuardPaint = null;
        }

        if (_combat)
        {
            KeepOnlySoldiers();
        }

        Toast(_combat ? "Combat mode: the left button picks soldiers (drag a box, Ctrl a line of one kind, Shift paints guard hexes). F to leave." : "Combat mode off.");
        UpdateUi();
    }

    // Handles input in combat mode; true when used.
    private bool CombatInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F })
        {
            ToggleCombat();
            return true;
        }

        if (!_combat || _session?.PlayView is not { } v || _placing > 0 || _drawMode != DrawMode.None)
        {
            return false;
        }

        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                if (press.ShiftPressed && SelectedSoldiers(v).Count > 0)
                {
                    _guardPaint = [];
                    PaintGuard();
                }
                else
                {
                    _boxFrom = press.Position;
                }

                return true;
            case InputEventMouseMotion motion when _guardPaint is not null:
                PaintGuard();
                return true;
            case InputEventMouseMotion motion when _boxFrom is { } from:
                ShowBox(from, motion.Position);
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } release:
                if (_guardPaint is not null)
                {
                    SendGuard(v, _guardPaint);
                    _guardPaint = null;
                    _overlay!.GuardPaint = null;
                }
                else if (_boxFrom is { } start)
                {
                    _boxFrom = null;
                    _box!.Visible = false;
                    if ((release.Position - start).Length() < 6)
                    {
                        PickSoldier(v, release.CtrlPressed);
                    }
                    else
                    {
                        BoxSelect(v, start, release.Position, release.ShiftPressed);
                    }
                }

                return true;
            case InputEventKey { Pressed: true, Echo: false } k when k.Keycode is Key.Z or Key.X or Key.C or Key.V or Key.B or Key.N:
                SoldierKey(v, k.Keycode);
                return true;
            default:
                return false;
        }
    }

    private void SoldierKey(PlayViewMessage v, Key key)
    {
        List<PlayUnitView> soldiers = SelectedSoldiers(v);
        if (soldiers.Count == 0)
        {
            Toast("Select soldiers first.");
            return;
        }

        foreach (PlayUnitView u in soldiers)
        {
            switch (key)
            {
                case Key.Z or Key.X or Key.C:
                    _session!.Play(PlayCommandKind.Stance, u.Id, b: key == Key.Z ? 0 : key == Key.X ? 1 : 2);
                    break;
                case Key.V:
                    _session!.Play(PlayCommandKind.Split, u.Id, b: u.People / 2);
                    break;
                case Key.B:
                    _session!.Play(PlayCommandKind.Refit, u.Id);
                    break;
                default:
                    _session!.Play(PlayCommandKind.Guard, u.Id);
                    break;
            }
        }

        Toast(key switch
        {
            Key.Z => "Hold: they strike what touches them and stay.",
            Key.X => "Aggressive: they go at enemies they see.",
            Key.C => "Passive: they strike only the enemy you send them at.",
            Key.V => "Split in half.",
            Key.B => "Taking the best arms of the settlement they stand in.",
            _ => "No more guarding.",
        });
    }

    private List<PlayUnitView> SelectedSoldiers(PlayViewMessage v)
    {
        var ids = SelectedUnitIds();
        return [.. v.Units.Where(u => ids.Contains(u.Id) && u.Civ == v.Civ && u.State != 0 && Soldier(u))];
    }

    private bool Soldier(PlayUnitView u) => _session?.PlayContent is { } c && c.Units[u.Kind].Army && !c.Units[u.Kind].Naval;

    private void KeepOnlySoldiers()
    {
        if (_session?.PlayView is not { } v || _overlay is null)
        {
            return;
        }

        Select(SelectedSoldiers(v), add: false);
    }

    private void Select(IEnumerable<PlayUnitView> units, bool add)
    {
        if (_overlay is null)
        {
            return;
        }

        if (!add)
        {
            _overlay.SelectedUnits.Clear();
            _unit = -1;
        }

        foreach (PlayUnitView u in units)
        {
            _overlay.SelectedUnits.Add(u.Id);
            if (_unit < 0)
            {
                _unit = u.Id;
                _selected = (u.Col, u.Row);
            }
        }

        _overlay.SelectedUnit = _unit;
        _overlay.Restatic();
        UpdateUi();
    }

    // A click: our soldier on the hex; with Ctrl the chain of its kind standing next to each other.
    private void PickSoldier(PlayViewMessage v, bool chain)
    {
        if (MouseCell() is not { } cell)
        {
            return;
        }

        PlayUnitView? hit = v.Units.FirstOrDefault(u => u.Civ == v.Civ && u.State != 0 && u.Col == cell.Col && u.Row == cell.Row && Soldier(u));
        if (hit is null)
        {
            Select([], add: false);
            _selected = cell;
            UpdateUi();
            return;
        }

        if (!chain)
        {
            Select([hit], add: false);
            return;
        }

        var picked = new List<PlayUnitView> { hit };
        var open = new Queue<PlayUnitView>(picked);
        while (open.Count > 0)
        {
            PlayUnitView at = open.Dequeue();
            foreach (PlayUnitView u in v.Units)
            {
                if (u.Civ == v.Civ && u.State != 0 && u.Kind == hit.Kind && !picked.Contains(u) && Beside(at, u))
                {
                    picked.Add(u);
                    open.Enqueue(u);
                }
            }
        }

        Select(picked, add: false);
        Toast($"{picked.Count} units of {_session!.PlayContent!.Units[hit.Kind].Name.ToLowerInvariant()} side by side.");
    }

    // Neighbouring hexes (or the same): their centres are one step apart, east-west wrapping.
    private bool Beside(PlayUnitView a, PlayUnitView b)
    {
        Vector2 d = HexLayout.Center(a.Col, a.Row) - HexLayout.Center(b.Col, b.Row);
        float wrap = _map?.Period.X ?? 0;
        if (wrap > 0)
        {
            d.X = Math.Abs(((d.X % wrap) + (wrap * 1.5f)) % wrap - (wrap / 2));
        }

        return d.Length() < HexLayout.RowStep * 1.2f;
    }

    private void ShowBox(Vector2 a, Vector2 b)
    {
        if (_box is null)
        {
            _box = new ReferenceRect { BorderColor = new Color(1f, 0.92f, 0.55f), BorderWidth = 2, EditorOnly = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            _help.GetParent().AddChild(_box);
        }

        _box.Position = new Vector2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));
        _box.Size = (a - b).Abs();
        _box.Visible = true;
    }

    private void BoxSelect(PlayViewMessage v, Vector2 a, Vector2 b, bool add)
    {
        Transform2D toWorld = GetViewport().GetCanvasTransform().AffineInverse();
        Vector2 w1 = toWorld * a;
        Vector2 w2 = toWorld * b;
        var rect = new Rect2(new Vector2(Math.Min(w1.X, w2.X), Math.Min(w1.Y, w2.Y)), (w1 - w2).Abs());
        float wrap = _map?.Period.X ?? 0;
        var picked = new List<PlayUnitView>();
        foreach (PlayUnitView u in v.Units)
        {
            if (u.Civ != v.Civ || u.State == 0 || !Soldier(u))
            {
                continue;
            }

            Vector2 c = HexLayout.Center(u.Col, u.Row);
            if (rect.HasPoint(c) || (wrap > 0 && (rect.HasPoint(c + new Vector2(wrap, 0)) || rect.HasPoint(c - new Vector2(wrap, 0)))))
            {
                picked.Add(u);
            }
        }

        Select(picked, add);
        if (picked.Count > 0)
        {
            Toast($"{picked.Count} units selected.");
        }
    }

    private void PaintGuard()
    {
        if (MouseCell() is { } cell && _guardPaint is not null && !_guardPaint.Contains(cell))
        {
            _guardPaint.Add(cell);
            _overlay!.GuardPaint = [.. _guardPaint];
        }
    }

    private void SendGuard(PlayViewMessage v, List<(int Col, int Row)> cells)
    {
        int[] pairs = [.. cells.SelectMany(c => new[] { c.Col, c.Row })];
        List<PlayUnitView> soldiers = SelectedSoldiers(v);
        foreach (PlayUnitView u in soldiers)
        {
            _session!.Play(PlayCommandKind.Guard, u.Id, cells: pairs);
        }

        Toast($"{soldiers.Count} units guard {cells.Count} hexes: they go at enemies there and come back. N stops it.");
    }

    // A right click with several soldiers on an empty hex: they go in formation (melee in front, archers behind, riders on the
    // ends). True when sent.
    private bool SendFormation(int col, int row)
    {
        if (!_combat || _session?.PlayView is not { } v)
        {
            return false;
        }

        List<PlayUnitView> soldiers = SelectedSoldiers(v);
        if (soldiers.Count < 2 || EnemyAt(col, row))
        {
            return false;
        }

        _session.Play(PlayCommandKind.Formation, 0, col, row, b: -1, cells: [.. soldiers.Select(u => u.Id)]);
        _overlay?.Flash(col, row, new Color(1f, 1f, 1f));
        return true;
    }

    // Combat lines of the unit panel: arms, stance, guard, and the orders of combat mode.
    private void CombatItems(PlayViewMessage v, PlayContentMessage c, PlayUnitView u, List<UiItem> items)
    {
        PlayUnitInfo t = c.Units[u.Kind];
        if (!t.Army || t.Naval)
        {
            return;
        }

        items.Add(new SectionItem("Battle"));
        if (t.MaxPeople > 0)
        {
            items.Add(new LineItem($"Weapons: {ArmsName(c, c.WeaponGoods, u.Weapon, "bare hands and clubs")} (x{Percent(c.WeaponPercent, u.Weapon) / 100.0:0.0#})", UiKit.Text, 14));
            items.Add(new LineItem($"Armour: {ArmsName(c, c.ArmourGoods, u.Armour, "none")} (damage taken /{Percent(c.ArmourPercent, u.Armour) / 100.0:0.0#})", UiKit.Text, 14));
        }

        if (u.LastLoss > 0)
        {
            items.Add(new LineItem($"Lost {u.LastLoss} in the last half second", UiKit.Bad, 14));
        }
        else if (u.Fighting)
        {
            items.Add(new LineItem("Fighting", UiKit.Warn, 14));
        }

        int id = u.Id;
        string[] stances = ["Hold (Z)", "Aggressive (X)", "Passive (C)"];
        string[] tips =
        [
            "Strike what touches them (archers: what is in reach), do not walk after it.",
            "Go at enemies they see.",
            "Strike only the enemy they are sent at: to walk past or pull back.",
        ];
        var acts = new List<ActItem>();
        for (int s = 0; s < stances.Length; s++)
        {
            int stance = s;
            acts.Add(new ActItem((u.Stance == s ? "> " : "") + stances[s], () => _session!.Play(PlayCommandKind.Stance, id, b: stance), tips[s], Accent: u.Stance == s ? UiKit.Good : null));
        }

        items.Add(new ButtonsItem(acts));
        var more = new List<ActItem>();
        int min = Math.Max(1, t.MinPeople);
        more.Add(new ActItem("Split in half (V)", () => _session!.Play(PlayCommandKind.Split, id, b: u.People / 2),
            "Half of them become a new unit beside. While the rest fight on, the part that walks away takes no strike from behind.", u.People / 2 >= min && u.People - (u.People / 2) >= min));
        if (t.MaxPeople > 0)
        {
            more.Add(new ActItem("Refit (B)", () => _session!.Play(PlayCommandKind.Refit, id),
                "Standing on our land: take the best weapons and armour the settlement holds for all of them; the old ones go back to the store."));
        }

        if (u.Guard is { Length: > 0 } guard)
        {
            more.Add(new ActItem($"Stop guarding {guard.Length / 2} hexes (N)", () => _session!.Play(PlayCommandKind.Guard, id)));
        }

        items.Add(new ButtonsItem(more));
        items.Add(new LineItem(_combat
            ? "Combat mode: drag a box to pick soldiers, Ctrl + click a line of one kind, Shift + drag paints guard hexes. Right click with several: they go in formation."
            : "F: combat mode (pick soldiers by box, formations, guard hexes).", UiKit.Muted, 13));
    }

    private static int Percent(int[]? table, int level) => table is { Length: > 0 } ? table[Math.Clamp(level, 0, table.Length - 1)] : 100;

    private static string ArmsName(PlayContentMessage c, int[]? goods, int level, string none) =>
        level <= 0 || goods is null || level >= goods.Length || goods[level] < 0 ? none : c.Goods[goods[level]].Name.ToLowerInvariant();

    // The size slider and weapons-only box above the training tiles (soldiers only).
    private CustomItem TrainSizeItem() => new("trainsize", () =>
    {
        var box = new VBoxContainer();
        var row = new HBoxContainer();
        Label label = UiKit.Label(SizeText(), 14, UiKit.Text);
        label.CustomMinimumSize = new Vector2(UiKit.W(150), 0);
        var slider = new HSlider { MinValue = 0, MaxValue = 30, Step = 1, Value = _trainSize, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Size of the soldiers you train next: more people cost more and train longer. At the left end each kind has its usual size." };
        slider.ValueChanged += x =>
        {
            _trainSize = x < 5 ? 0 : (int)x;
            label.Text = SizeText();
        };
        row.AddChild(label);
        row.AddChild(slider);
        box.AddChild(row);
        var shock = new CheckBox { Text = "Weapons only (no armour, trained faster)", ButtonPressed = _trainShock, FocusMode = Control.FocusModeEnum.None, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TooltipText = "Quick soldiers: they take weapons from the store but no armour." };
        shock.Toggled += on => _trainShock = on;
        box.AddChild(shock);
        return box;
    });

    private string SizeText() => _trainSize == 0 ? "Size: usual" : $"Size: {_trainSize} people";

    /// <summary>
    /// Script step for screenshots of battles: 0 combat mode on and all our soldiers picked, the camera on them; 1 send them in
    /// formation at the enemy's nearest unit (one hex short); 2 show a selection box around them; 3 the mouse behind the first.
    /// </summary>
    public void ScriptBattle(int step)
    {
        if (_session?.PlayView is not { } v)
        {
            return;
        }

        List<PlayUnitView> ours = [.. v.Units.Where(u => u.Civ == v.Civ && u.State != 0 && Soldier(u))];
        if (ours.Count == 0)
        {
            return;
        }

        switch (step)
        {
            case 0:
                if (!_combat)
                {
                    ToggleCombat();
                }

                Select(ours, add: false);
                _camera.Position = HexLayout.Center(ours[0].Col, ours[0].Row);
                Zoom = 1.6f;
                break;
            case 1:
                if (v.Units.Where(u => u.Civ != v.Civ && Soldier(u)).OrderBy(u => Math.Abs(u.Row - ours[0].Row) + Math.Abs(u.Col - ours[0].Col)).FirstOrDefault() is { } foe)
                {
                    int row = foe.Row + Math.Sign(ours[0].Row - foe.Row);
                    SendFormation(foe.Col, row);
                    _camera.Position = (HexLayout.Center(ours[0].Col, ours[0].Row) + HexLayout.Center(foe.Col, foe.Row)) / 2;
                }

                break;
            case 3:
                // The mouse on a hex with trees near our first unit, else behind it (cover markers).
                Transform2D screen = GetViewport().GetCanvasTransform();
                (int Col, int Row) spot = (ours[0].Col, ours[0].Row + 1);
                if (_session.Worlds.TryGetValue(0, out ClientWorld? w))
                {
                    for (int dc = -3; dc <= 3; dc++)
                    {
                        for (int dr = -3; dr <= 3; dr++)
                        {
                            int c = (((ours[0].Col + dc) % w.Width) + w.Width) % w.Width;
                            int r = Math.Clamp(ours[0].Row + dr, 0, w.Height - 1);
                            if (w.Trees[w.Index(c, r)] != 0)
                            {
                                spot = (c, r);
                            }
                        }
                    }
                }

                GetViewport().WarpMouse(screen * HexLayout.Center(spot.Col, spot.Row));
                break;
            default:
                Transform2D toScreen = GetViewport().GetCanvasTransform();
                Vector2 a = toScreen * HexLayout.Center(ours.Min(u => u.Col), ours.Min(u => u.Row));
                Vector2 b = toScreen * HexLayout.Center(ours.Max(u => u.Col), ours.Max(u => u.Row));
                ShowBox(a - new Vector2(30, 30), b + new Vector2(30, 30));
                break;
        }
    }
}
