using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The action bar at the bottom (like World of Warcraft): tabs for kinds of buildings and a row of slots. Click a slot or
/// press its key on the bottom row (Z, X, C…; WASD stays for the camera), then click a green hex on the map.
/// </summary>
public partial class PlayGame
{
    private static readonly string[] Tabs = ["Food", "Production", "Homes & stores", "Military", "Society", "Roads & works"];
    private const int RoadTab = 5;
    private static readonly Key[] SlotKeys = [Key.Z, Key.X, Key.C, Key.V, Key.B, Key.N, Key.M, Key.Comma, Key.Period, Key.Slash];
    private static readonly string[] SlotNames = ["Z", "X", "C", "V", "B", "N", "M", ",", ".", "/"];

    private HBoxContainer _tabsBox = null!;
    private HBoxContainer _slotsBox = null!;
    private int _tab;
    private string _barSig = "";
    private readonly List<int> _slotTypes = [];

    private void BuildActionBar(Control root)
    {
        var bar = new PanelContainer { AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1, OffsetTop = -6, OffsetBottom = -6, GrowHorizontal = Control.GrowDirection.Both, GrowVertical = Control.GrowDirection.Begin };
        root.AddChild(bar);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 4);
        bar.AddChild(col);
        var head = new HBoxContainer();
        col.AddChild(head);
        _tabsBox = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _tabsBox.AddThemeConstantOverride("separation", 2);
        head.AddChild(_tabsBox);

        // The windows, like the small menu next to a World of Warcraft bar.
        head.AddChild(new VSeparator());
        for (int i = 0; i < WindowNames.Length; i++)
        {
            string name = WindowNames[i];
            head.AddChild(UiKit.Button($"{i + 1} {name}", () => OpenWindow(name), $"{name} (key {i + 1})"));
        }
        _slotsBox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _slotsBox.AddThemeConstantOverride("separation", 4);
        col.AddChild(_slotsBox);
    }

    private static int TabOf(PlayBuildingInfo b)
    {
        string g = b.Groups is { Length: > 0 } groups ? groups[0] : "";
        return g switch
        {
            "food" or "farm" => 0,
            "tools" or "mine" or "craft" => 1,
            "house" or "store" or "road" => 2,
            "fort" or "army" => 3,
            _ => 4,
        };
    }

    private void UpdateActionBar(PlayViewMessage v, PlayContentMessage c)
    {
        var known = new List<PlayBuildingInfo>();
        var counts = new int[Tabs.Length];
        foreach (PlayBuildingInfo b in c.Buildings)
        {
            if (b.Id != 0 && v.Techs[b.Tech].Known)
            {
                known.Add(b);
                counts[TabOf(b)]++;
            }
        }

        // Roads are drawn, not placed: their slots carry minus their level.
        foreach (PlayRoadInfo r in c.Roads ?? [])
        {
            counts[RoadTab] += v.Techs[r.Tech].Known ? 1 : 0;
        }

        foreach (PlayWorkInfo wk in c.Works ?? [])
        {
            counts[RoadTab] += v.Techs[wk.Tech].Known ? 1 : 0;
        }

        if (counts[_tab] == 0)
        {
            _tab = Array.FindIndex(counts, n => n > 0) is var first && first >= 0 ? first : 0;
        }

        var affordable = new List<bool>();
        _slotTypes.Clear();
        if (_tab == RoadTab)
        {
            foreach (PlayRoadInfo r in c.Roads ?? [])
            {
                if (v.Techs[r.Tech].Known)
                {
                    _slotTypes.Add(-r.Level);
                    affordable.Add(CanPay(v.Stock, r.Price));
                }
            }

            // Works on hexes: drawn like roads; their slots carry minus (100 + id).
            foreach (PlayWorkInfo wk in c.Works ?? [])
            {
                if (v.Techs[wk.Tech].Known)
                {
                    _slotTypes.Add(-(WorkSlotBase + wk.Id));
                    affordable.Add(CanPay(v.Stock, wk.Price));
                }
            }
        }

        foreach (PlayBuildingInfo b in known)
        {
            if (TabOf(b) == _tab && _tab != RoadTab)
            {
                _slotTypes.Add(b.Id);
                affordable.Add(CanPay(v.Stock, b.Price));
            }
        }

        // E6 (2026-11-20): in the tutorial the building of the next step lights up (and its tab).
        string glow = v.TutorialOn && v.Tutorial?.FirstOrDefault(x => !x.Done) is { } next ? next.Highlight : "";
        int glowTab = glow.Length > 0 && c.Buildings.FirstOrDefault(x => x.Key == glow) is { } gb ? TabOf(gb) : -1;
        string sig = $"{_tab}|{string.Join(",", counts)}|{string.Join(",", _slotTypes)}|{string.Join(",", affordable)}|{_placing}|{v.Picking}|{_drawMode}{_drawRoad}|{glow}";
        if (sig == _barSig)
        {
            return;
        }

        _barSig = sig;
        UiKit.Clear(_tabsBox);
        for (int i = 0; i < Tabs.Length; i++)
        {
            if (counts[i] == 0)
            {
                continue;
            }

            int tab = i;
            var b = new Button { Text = Tabs[i], ToggleMode = true, ButtonPressed = i == _tab, FocusMode = Control.FocusModeEnum.None, TooltipText = "Kinds of buildings (Tab)" };
            if (i == glowTab && i != _tab)
            {
                b.AddThemeStyleboxOverride("normal", UiKit.Flat(new Color(0.3f, 0.24f, 0.08f, 0.95f), UiKit.Heading, 4, 3));
            }

            b.Pressed += () =>
            {
                _tab = tab;
                _barSig = "";
                UpdateUi();
            };
            _tabsBox.AddChild(b);
        }

        UiKit.Clear(_slotsBox);
        for (int i = 0; i < _slotTypes.Count; i++)
        {
            if (_slotTypes[i] < 0)
            {
                _slotsBox.AddChild(RoadSlot(c, -_slotTypes[i], i, affordable[i]));
                continue;
            }

            PlayBuildingInfo t = c.Buildings[_slotTypes[i]];
            int id = t.Id;
            bool can = affordable[i];
            var slot = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(UiKit.W(112), 58 + (2 * Ui.PixelTheme.FontSize)), TooltipText = Describe(c, t) + (can ? "" : "\n\nWe do not have the goods for it now."), ToggleMode = true, ButtonPressed = _placing == id };
            if (t.Key == glow)
            {
                slot.AddThemeStyleboxOverride("normal", UiKit.Flat(new Color(0.3f, 0.24f, 0.08f, 0.95f), UiKit.Heading, 4, 3));
            }

            var inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            inner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            inner.AddThemeConstantOverride("separation", 1);
            Color color = UiKit.GroupColor(t.Groups);
            inner.AddChild(new Glyph(can ? color : color.Darkened(0.55f), UiKit.Short(t.Name), 44) { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter });
            Label name = UiKit.Label(t.Name, 11, can ? UiKit.Text : UiKit.Muted);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            name.CustomMinimumSize = new Vector2(UiKit.W(104), 0);
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            inner.AddChild(name);
            slot.AddChild(inner);
            if (i < SlotKeys.Length)
            {
                Label key = UiKit.Label(SlotNames[i], 11, UiKit.Heading);
                key.Position = new Vector2(5, 2);
                key.MouseFilter = Control.MouseFilterEnum.Ignore;
                slot.AddChild(key);
            }

            slot.Pressed += () =>
            {
                if (_replacing >= 0)
                {
                    // 15B: the picked kind goes up on the places of the building chosen in the inspector.
                    _session!.Play(PlayCommandKind.ReplaceBuilding, _replacing, b: id);
                    _replacing = -1;
                    _barSig = "";
                    UpdateUi();
                    return;
                }

                if (_placing == id)
                {
                    StopPlacing();
                }
                else
                {
                    StartPlacing(id);
                }

                _barSig = "";
                UpdateUi();
            };
            _slotsBox.AddChild(slot);
        }
    }

    // Keys Z…/ pick the slots of the open tab; Tab moves to the next tab. True when the key was used.
    private bool ActionKey(Key key)
    {
        if (key == Key.Tab)
        {
            _tab = (_tab + 1) % Tabs.Length;
            _barSig = "";
            UpdateUi();
            return true;
        }

        int slot = Array.IndexOf(SlotKeys, key);
        if (slot < 0 || slot >= _slotTypes.Count)
        {
            return false;
        }

        if (_slotTypes[slot] < 0)
        {
            ArmRoad(-_slotTypes[slot]);
        }
        else
        {
            StartPlacing(_slotTypes[slot]);
        }

        _barSig = "";
        UpdateUi();
        return true;
    }

    private const int WorkSlotBase = 100;

    private Button RoadSlot(PlayContentMessage c, int level, int index, bool can)
    {
        string tip;
        string label;
        Color color;
        if (level >= WorkSlotBase)
        {
            PlayWorkInfo wk = c.Works![level - WorkSlotBase];
            label = wk.Name;
            tip = $"{wk.Name}: {Amounts(c, wk.Price)} and {wk.Seconds} s a hex, paid by our nearest settlement.\n{wk.Text}\nDraw it hex by hex on our land; remove it from the hex panel.";
            color = wk.Key switch { "ditch" => new Color(0.35f, 0.62f, 0.95f), "aqueduct" => new Color(0.55f, 0.75f, 0.95f), "wall" => new Color(0.72f, 0.72f, 0.7f), _ => new Color(0.55f, 0.38f, 0.2f) };
        }
        else
        {
            PlayRoadInfo road = c.Roads![level - 1];
            label = road.Name;
            tip = $"{road.Name}: {Amounts(c, road.Price)} and {road.Seconds} s a hex, paid by our nearest settlement.\n"
                + (level == 1 ? "Walking on it is faster; caravans and armies take it by themselves." : "Much faster than a track, also over hills; a major trade route.")
                + "\nDraw it hex by hex, also into empty land: settlers who found a camp at its end get goods along it. With Masonry it bridges the rivers it crosses.";
            color = level == 1 ? new Color(0.66f, 0.52f, 0.34f) : new Color(0.78f, 0.74f, 0.64f);
        }

        PlayRoadInfo r = new(level, label, [], 0, 0);
        var slot = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(UiKit.W(112), 58 + (2 * Ui.PixelTheme.FontSize)), TooltipText = tip, ToggleMode = true, ButtonPressed = _drawMode == DrawMode.Road && _drawRoad == level };
        var inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        inner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        inner.AddChild(new Glyph(can ? color : color.Darkened(0.55f), UiKit.Short(r.Name), 44) { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter });
        Label name = UiKit.Label(r.Name, 11, can ? UiKit.Text : UiKit.Muted);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.MouseFilter = Control.MouseFilterEnum.Ignore;
        inner.AddChild(name);
        slot.AddChild(inner);
        if (index < SlotKeys.Length)
        {
            Label key = UiKit.Label(SlotNames[index], 11, UiKit.Heading);
            key.Position = new Vector2(5, 2);
            key.MouseFilter = Control.MouseFilterEnum.Ignore;
            slot.AddChild(key);
        }

        slot.Pressed += () =>
        {
            ArmRoad(level);
            _barSig = "";
            UpdateUi();
        };
        return slot;
    }

    private void ArmRoad(int level)
    {
        StopPlacing();
        _drawRoad = level;
        ArmDraw(DrawMode.Road);
    }

    private static long[] Times(long[] a, int n) => [.. a.Select(x => x * n)];

    private static string Describe(PlayContentMessage content, PlayBuildingInfo b)
    {
        var lines = new List<string> { b.Name, $"Costs {Amounts(content, b.Price)}; builds in {b.BuildSeconds} s once the materials are there." };
        if (b.Carriers > 0)
        {
            lines.Add($"Building it takes {b.Carriers} of our people away while they carry the materials (they come back)."); // E9
        }
        if (b.Workers > 0)
        {
            lines.Add($"Jobs: {b.Workers}. Each worker a minute makes {Amounts(content, b.Produces)}" + (Any(b.Consumes) ? $" from {Amounts(content, b.Consumes)}" : "")
                + $"; all {b.Workers} together: {Amounts(content, Times(b.Produces, b.Workers))} a minute.");
        }

        if (b.Housing > 0)
        {
            lines.Add($"Homes for {b.Housing} people.");
        }

        if (b.Storage > 0)
        {
            lines.Add($"Room for {b.Storage / 100} more of every good.");
        }

        if (!string.IsNullOrEmpty(b.Needs))
        {
            lines.Add($"Needs {b.Needs}.");
        }

        return string.Join("\n", lines);
    }
}
