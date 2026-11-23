using System;
using System.Collections.Generic;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// A keyboard cursor over the buttons of the screen (Patryk, 2026-11-23: "can every window be opened and every thing be
/// chosen from the keyboard?"). The screen's buttons take no keyboard focus, so the map keeps its keys (Space pauses, the
/// arrows move the camera); instead Enter turns this cursor on: the arrows move a ring to the nearest button in that
/// direction, Enter presses it, Esc turns the cursor off. The ring shows the button's tooltip, so what a mouse player reads
/// on hover a keyboard player reads too. It looks only inside the given roots (an open window, or the side panel and the
/// bars), and finds its button again by position when a panel is built anew.
/// </summary>
public sealed partial class KeyboardCursor : Control
{
    private readonly Panel _ring;
    private readonly PanelContainer _tipPanel;
    private Control? _tip;
    private string _tipText = "";
    private Button? _target;
    private Vector2 _lastCentre = new(-1, -1);

    /// <summary>Creates the cursor; add it on top of the screen's interface.</summary>
    public KeyboardCursor()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        Visible = false;
        _ring = new Panel { MouseFilter = MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(1f, 0.85f, 0.3f, 0.12f), BorderColor = new Color(1f, 0.85f, 0.3f), DrawCenter = true };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(4);
        _ring.AddThemeStyleboxOverride("panel", style);
        AddChild(_ring);
        _tipPanel = new PanelContainer { MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        // The same parchment as the mouse tooltips (its text is dark).
        if (Ui.PixelTheme.Current?.GetStylebox("panel", "TooltipPanel") is { } parchment)
        {
            _tipPanel.AddThemeStyleboxOverride("panel", parchment);
        }
        AddChild(_tipPanel);
    }

    /// <summary>True while the cursor is on.</summary>
    public bool Active => Visible;

    /// <summary>The roots whose buttons the cursor walks (set every frame by the screen).</summary>
    public IReadOnlyList<Control> Roots { get; set; } = [];

    /// <summary>Turns the cursor on at the first button of the roots (top left first).</summary>
    public void Start()
    {
        Visible = true;
        _target = null;
        _lastCentre = new Vector2(-1, -1);
        foreach (Control root in Roots)
        {
            if (!IsInstanceValid(root) || !root.IsVisibleInTree())
            {
                continue;
            }

            var mine = new List<Button>();
            Collect(root, mine);
            SortByPlace(mine);
            Button? first = mine.Find(Shown) ?? (mine.Count > 0 ? mine[0] : null);
            if (first is not null)
            {
                Pick(first);
                return;
            }
        }

        Refresh();
    }

    // True when the button lies inside the visible part of every scroll container it sits in.
    private static bool Shown(Button b)
    {
        Rect2 r = b.GetGlobalRect();
        for (Node? n = b.GetParent(); n is not null; n = n.GetParent())
        {
            if (n is ScrollContainer s && !s.GetGlobalRect().Encloses(r))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Turns the cursor off.</summary>
    public void Stop()
    {
        Visible = false;
        _target = null;
        _tipPanel.Visible = false;
    }

    /// <summary>Moves the cursor to the nearest button in a direction (x, y in -1..1).</summary>
    public void Move(Vector2 direction)
    {
        List<Button> all = Buttons();
        if (all.Count == 0)
        {
            return;
        }

        if (_target is null || !IsInstanceValid(_target))
        {
            Pick(all[0]);
            return;
        }

        // Stay inside the same part of the screen (the side panel, a bar, the window) while it has a button that way.
        Control? home = null;
        foreach (Control root in Roots)
        {
            if (IsInstanceValid(root) && root.IsAncestorOf(_target))
            {
                home = root;
                break;
            }
        }

        if (home is not null && Nearest(all.FindAll(b => home.IsAncestorOf(b)), direction) is { } near)
        {
            Pick(near);
            return;
        }

        if (Nearest(all, direction) is { } far)
        {
            Pick(far);
        }
    }

    // The nearest button from the cursor in a direction, or null.
    private Button? Nearest(List<Button> all, Vector2 direction)
    {
        Vector2 from = _target!.GetGlobalRect().GetCenter();
        Vector2 across = new(-direction.Y, direction.X);
        Button? best = null;
        float bestScore = float.MaxValue;
        bool bestInCone = false;
        foreach (Button b in all)
        {
            if (b == _target)
            {
                continue;
            }

            Vector2 d = b.GetGlobalRect().GetCenter() - from;
            float along = d.Dot(direction);
            if (along <= 1)
            {
                continue;
            }

            // Within about 60 degrees of the direction the nearest wins (straight and sideways steps count the same);
            // a button further aside is taken only when none lies in that cone.
            float side = Math.Abs(d.Dot(across));
            bool inCone = side <= 2 * along;
            float score = inCone ? along + side : along + (3 * side);
            if ((inCone && !bestInCone) || (inCone == bestInCone && score < bestScore))
            {
                bestScore = score;
                best = b;
                bestInCone = inCone;
            }
        }

        return best;
    }

    /// <summary>Presses the button under the cursor. Returns a reason when it cannot be pressed, else null.</summary>
    public string? Press()
    {
        if (_target is null || !IsInstanceValid(_target))
        {
            return "Nothing to press here.";
        }

        if (_target.Disabled)
        {
            return "This cannot be done now (the note under the cursor says why).";
        }

        if (_target.ToggleMode && _target.ButtonGroup is null)
        {
            _target.ButtonPressed = !_target.ButtonPressed;
        }

        _target.EmitSignal(BaseButton.SignalName.Pressed);
        return null;
    }

    /// <summary>Keeps the ring on its button; finds the nearest button again when the panel was built anew.</summary>
    public void Refresh()
    {
        if (!Visible)
        {
            return;
        }

        if (_target is null || !IsInstanceValid(_target) || !_target.IsVisibleInTree() || !Inside(_target))
        {
            List<Button> all = Buttons();
            _target = null;
            if (all.Count == 0)
            {
                _ring.Visible = false;
                _tipPanel.Visible = false;
                return;
            }

            Button pick = all[0];
            if (_lastCentre.X >= 0)
            {
                float best = float.MaxValue;
                foreach (Button b in all)
                {
                    float d = b.GetGlobalRect().GetCenter().DistanceSquaredTo(_lastCentre);
                    if (d < best)
                    {
                        best = d;
                        pick = b;
                    }
                }
            }

            Pick(pick);
            return;
        }

        Place();
    }

    private void Pick(Button b)
    {
        _target = b;
        _lastCentre = b.GetGlobalRect().GetCenter();
        ScrollInto(b);
        Place();
    }

    private void Place()
    {
        if (_target is null)
        {
            return;
        }

        Rect2 r = _target.GetGlobalRect().Grow(3);
        _ring.Visible = true;
        _ring.Position = r.Position;
        _ring.Size = r.Size;
        _lastCentre = r.GetCenter();

        string tip = _target.TooltipText ?? "";
        if (tip != _tipText)
        {
            _tipText = tip;
            _tip?.QueueFree();
            _tip = null;
            if (tip.Length > 0)
            {
                _tip = UiKit.RichTip(tip);
                _tip.MouseFilter = MouseFilterEnum.Ignore;
                _tipPanel.AddChild(_tip);
            }

            _tipPanel.Size = Vector2.Zero;
        }

        _tipPanel.Visible = tip.Length > 0;
        _tipPanel.ResetSize(); // shrink to this tooltip (the last one may have been bigger)
        if (_tipPanel.Visible)
        {
            Vector2 screen = GetViewportRect().Size;
            Vector2 size = _tipPanel.GetCombinedMinimumSize();
            float x = Math.Clamp(r.Position.X, 4, Math.Max(4, screen.X - size.X - 4));
            float y = r.End.Y + 6;
            if (y + size.Y > screen.Y - 4)
            {
                y = Math.Max(4, r.Position.Y - size.Y - 6);
            }

            _tipPanel.Position = new Vector2(x, y);
        }
    }

    // Scrolls every scroll container the button sits in so that it shows.
    private static void ScrollInto(Control b)
    {
        for (Node? n = b.GetParent(); n is not null; n = n.GetParent())
        {
            if (n is ScrollContainer s)
            {
                s.EnsureControlVisible(b);
            }
        }
    }

    private bool Inside(Control c)
    {
        foreach (Control root in Roots)
        {
            if (IsInstanceValid(root) && root.IsAncestorOf(c))
            {
                return true;
            }
        }

        return false;
    }

    // The visible buttons of the roots, top to bottom, then left to right.
    private List<Button> Buttons()
    {
        var list = new List<Button>();
        foreach (Control root in Roots)
        {
            if (IsInstanceValid(root) && root.IsVisibleInTree())
            {
                Collect(root, list);
            }
        }

        SortByPlace(list);
        return list;
    }

    private static void SortByPlace(List<Button> list) => list.Sort((a, b) =>
    {
        Vector2 pa = a.GetGlobalRect().Position;
        Vector2 pb = b.GetGlobalRect().Position;
        int byRow = Math.Abs(pa.Y - pb.Y) > 8 ? pa.Y.CompareTo(pb.Y) : 0;
        return byRow != 0 ? byRow : pa.X.CompareTo(pb.X);
    });

    private static void Collect(Node n, List<Button> list)
    {
        foreach (Node child in n.GetChildren())
        {
            if (child is Control { Visible: false })
            {
                continue;
            }

            if (child is Button b && b.Size.X > 0)
            {
                list.Add(b);
            }

            Collect(child, list);
        }
    }
}
