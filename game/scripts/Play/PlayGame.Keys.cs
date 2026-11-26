using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Game.Map;
using CivSim.Game.Ui;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// Keyboard play (Patryk, 2026-11-23: "can every window be opened, and every thing chosen, from the keyboard?"):
/// the keyboard cursor over the buttons (Enter), going round our settlements (Q, E), units (R) and alerts (J),
/// back to the selection (Home), scrolling the side panel or window (Page Up, Page Down) and the manual (F1).
/// </summary>
public partial class PlayGame
{
    private KeyboardCursor _cursor = null!;
    private PanelContainer _manualPanel = null!;
    private ManualView? _manualView;
    private List<ManualChapter>? _manualChapters;
    private int _manualChapter;
    private int _alertTurn = -1;

    /// <summary>For screenshots: presses a key as the player would (through the whole input path).</summary>
    public void ScriptKey(Key key, bool shift = false)
    {
        foreach (bool down in new[] { true, false })
        {
            Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = down, ShiftPressed = shift });
        }
    }

    // The manual's chapters, read once from data/sim/manual.md.
    private List<ManualChapter> ManualChapters() => _manualChapters ??= _dataDirectory is null ? [] : Manual.Load(_dataDirectory);

    private void BuildKeysUi(Control root, int topH)
    {
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        int half = (int)Math.Min(UiKit.W(620), (screen.X / 2) - 16);
        _manualPanel = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f, OffsetLeft = -half, OffsetRight = half, OffsetTop = -(screen.Y / 2) + topH + 8, OffsetBottom = (screen.Y / 2) - 40, Visible = false };
        root.AddChild(_manualPanel);
        _cursor = new KeyboardCursor();
        root.AddChild(_cursor);
    }

    // F1: the manual over the game (it keeps running; Space still pauses).
    private void ToggleManual(int chapter = -1)
    {
        if (_manualPanel.Visible && chapter < 0)
        {
            CloseManual();
            return;
        }

        _cursor.Stop();
        _help.Visible = false;
        UiKit.Clear(_manualPanel);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 6);
        _manualPanel.AddChild(col);
        var head = new HBoxContainer();
        col.AddChild(head);
        Label title = UiKit.Label("HOW TO PLAY", 22, UiKit.Heading);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        head.AddChild(title);
        head.AddChild(UiKit.Button("Close", CloseManual, "Close the manual (Esc or F1)"));
        col.AddChild(new HSeparator());
        _manualView = new ManualView(ManualChapters(), focusable: false, chapter >= 0 ? chapter : _manualChapter) { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        col.AddChild(_manualView);
        _manualPanel.Visible = true;
    }

    private void CloseManual()
    {
        if (_manualView is not null)
        {
            _manualChapter = _manualView.Chapter;
        }

        _manualPanel.Visible = false;
        UiKit.Clear(_manualPanel);
        _manualView = null;
    }

    // The index of a chapter by its title (the help panel opens the manual on "Controls").
    private int ManualChapterOf(string title) => Math.Max(0, ManualChapters().FindIndex(c => c.Title.Equals(title, StringComparison.OrdinalIgnoreCase)));

    // Where the keyboard cursor may go: the topmost thing open, else the whole screen around the map.
    private List<Control> CursorRoots()
    {
        if (_endPanel.Visible)
        {
            return [_endPanel];
        }

        if (_help.Visible)
        {
            return [_help];
        }

        if (_window.Visible)
        {
            // The window's content first (the cursor starts there), then its row of window buttons.
            return [_windowBox, _window];
        }

        return [_inspector, _top, _rightCol, _tutorialPanel, _chroniclePanel, _tabsBox.GetParent<Control>().GetParent<Control>()];
    }

    /// <inheritdoc/>
    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } key || _session is null || _map is null)
        {
            return;
        }

        // The manual and the cursor take their keys before the buttons and the map do.
        if (_manualPanel.Visible)
        {
            if (key.Keycode is Key.Escape or Key.F1 && !key.Echo)
            {
                CloseManual();
                GetViewport().SetInputAsHandled();
            }
            else if (_manualView?.HandleKey(key.Keycode) == true)
            {
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (!_cursor.Active)
        {
            return;
        }

        bool used = true;
        switch (key.Keycode)
        {
            case Key.Up:
                _cursor.Move(Vector2.Up);
                break;
            case Key.Down:
                _cursor.Move(Vector2.Down);
                break;
            case Key.Left:
                _cursor.Move(Vector2.Left);
                break;
            case Key.Right:
                _cursor.Move(Vector2.Right);
                break;
            case Key.Enter or Key.KpEnter when !key.Echo:
                if (_cursor.Press() is { } why)
                {
                    Toast(why);
                }

                _uiTimer = 0;
                break;
            case Key.Escape when !key.Echo:
                _cursor.Stop();
                break;
            default:
                used = false;
                break;
        }

        if (used)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    // The keys of this part; true when the key was used.
    private bool MoreKeys(InputEventKey k)
    {
        if (!k.Pressed)
        {
            return false;
        }

        switch (k.Keycode)
        {
            case Key.F1 when !k.Echo:
                ToggleManual();
                return true;
            case Key.Enter or Key.KpEnter when !k.Echo:
                _cursor.Roots = CursorRoots();
                _cursor.Start();
                return true;
            case Key.Q when !k.Echo:
                NextSettlement(-1);
                return true;
            case Key.E when !k.Echo:
                NextSettlement(1);
                return true;
            case Key.R when !k.Echo:
                NextUnit(k.ShiftPressed ? -1 : 1);
                return true;
            case Key.J when !k.Echo:
                NextAlert();
                return true;
            case Key.Y when !k.Echo:
                return FocusChat();
            case Key.Home when !k.Echo:
                BackToSelection();
                return true;
            case Key.Pageup or Key.Pagedown:
                ScrollPanel(k.Keycode == Key.Pageup ? -1 : 1);
                return true;
            default:
                return false;
        }
    }

    // Q and E: the previous or next of our settlements, selected and in the middle of the screen.
    private void NextSettlement(int step)
    {
        if (_session?.PlayView is not { } v)
        {
            return;
        }

        List<PlaySettlementView> ours = [.. v.Settlements.Where(s => s.Civ == v.Civ).OrderBy(s => s.Id)];
        if (ours.Count == 0)
        {
            Toast("We have no settlement yet.");
            return;
        }

        int at = _selected is { } sel ? ours.FindIndex(s => s.Col == sel.Col && s.Row == sel.Row) : -1;
        int next = at < 0 ? (step > 0 ? 0 : ours.Count - 1) : (at + step + ours.Count) % ours.Count;
        _overlay?.SelectedUnits.Clear();
        Focus(ours[next].Col, ours[next].Row);
        _overlay?.Restatic();
        UpdateUi();
    }

    // R (Shift + R back): the next of our units on the map (not carriers), selected so a right click sends it.
    private void NextUnit(int step)
    {
        if (_session?.PlayView is not { } v || _session.PlayContent is not { } c)
        {
            return;
        }

        List<PlayUnitView> ours = [.. v.Units.Where(u => u.Civ == v.Civ && u.State != 0 && !c.Units[u.Kind].System).OrderBy(u => u.Id)];
        if (ours.Count == 0)
        {
            Toast("We have no units out: train them in a settlement's panel.");
            return;
        }

        int at = ours.FindIndex(u => u.Id == _unit);
        int next = at < 0 ? (step > 0 ? 0 : ours.Count - 1) : (at + step + ours.Count) % ours.Count;
        PlayUnitView pick = ours[next];
        Select([pick], add: false);
        _camera.Position = HexLayout.Center(pick.Col, pick.Row);
    }

    // J: goes to the alerts one after another (what the click on an alert does).
    private void NextAlert()
    {
        IReadOnlyList<PlayAlertView> alerts = _session?.PlayView?.Alerts ?? [];
        if (alerts.Count == 0)
        {
            Toast("No alerts now.");
            return;
        }

        _alertTurn = (_alertTurn + 1) % alerts.Count;
        PlayAlertView a = alerts[_alertTurn];
        if (a.Kind == 10)
        {
            OpenWindow("Research", toggle: false);
        }
        else
        {
            _overlay?.SelectedUnits.Clear();
            Focus(a.Col, a.Row);
            UpdateUi();
        }

        Toast($"Alert {_alertTurn + 1} of {alerts.Count}: {a.Text}");
    }

    // Home: the camera back to what is selected, or to our first settlement.
    private void BackToSelection()
    {
        if (_unit >= 0 && _session?.PlayView?.Units.FirstOrDefault(u => u.Id == _unit) is { } u)
        {
            _camera.Position = HexLayout.Center(u.Col, u.Row);
        }
        else if (_selected is { } s)
        {
            _camera.Position = HexLayout.Center(s.Col, s.Row);
        }
        else
        {
            NextSettlement(1);
        }
    }

    // Page Up and Page Down: scroll the help or the open window, else the side panel.
    private void ScrollPanel(int direction)
    {
        ScrollContainer? scroll = _help.Visible ? FindScroll(_help) : _window.Visible ? FindScroll(_window) : _inspectorScroll;
        if (scroll is null)
        {
            return;
        }

        scroll.ScrollVertical += direction * (int)Math.Max(80, scroll.Size.Y - 60);
    }

    private static ScrollContainer? FindScroll(Node n)
    {
        foreach (Node child in n.GetChildren())
        {
            if (child is ScrollContainer s)
            {
                return s;
            }

            if (FindScroll(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // Arrows move the camera only while nothing else takes them.
    private bool ArrowsForCamera => !_cursor.Active && !_manualPanel.Visible;
}
