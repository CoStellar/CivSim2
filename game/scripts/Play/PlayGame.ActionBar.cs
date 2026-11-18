using System;
using System.Collections.Generic;
using CivSim.Protocol.Messages;
using Godot;

namespace CivSim.Game.Play;

/// <summary>
/// The action bar at the bottom (like World of Warcraft): tabs for kinds of buildings and a row of slots. Click a slot or
/// press its number key (1, 2, 3…, like a World of Warcraft bar), then click a green hex on the map.
/// </summary>
public partial class PlayGame
{
    private static readonly string[] Tabs = ["Food", "Production", "Homes & stores", "Military", "Society"];
    private static readonly Key[] SlotKeys = [Key.Key1, Key.Key2, Key.Key3, Key.Key4, Key.Key5, Key.Key6, Key.Key7, Key.Key8, Key.Key9, Key.Key0];

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
            head.AddChild(UiKit.Button(name, () => OpenWindow(name), $"{name} (F{i + 1})"));
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

        if (counts[_tab] == 0)
        {
            _tab = Array.FindIndex(counts, n => n > 0) is var t && t >= 0 ? t : 0;
        }

        var affordable = new List<bool>();
        _slotTypes.Clear();
        foreach (PlayBuildingInfo b in known)
        {
            if (TabOf(b) == _tab)
            {
                _slotTypes.Add(b.Id);
                affordable.Add(CanPay(v.Stock, b.Price));
            }
        }

        string sig = $"{_tab}|{string.Join(",", counts)}|{string.Join(",", _slotTypes)}|{string.Join(",", affordable)}|{_placing}|{v.Picking}";
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
            PlayBuildingInfo t = c.Buildings[_slotTypes[i]];
            int id = t.Id;
            bool can = affordable[i];
            var slot = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(112, 92), TooltipText = Describe(c, t) + (can ? "" : "\n\nWe do not have the goods for it now."), ToggleMode = true, ButtonPressed = _placing == id };
            var inner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
            inner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            inner.AddThemeConstantOverride("separation", 1);
            Color color = UiKit.GroupColor(t.Groups);
            inner.AddChild(new Glyph(can ? color : color.Darkened(0.55f), UiKit.Short(t.Name), 38) { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter });
            Label name = UiKit.Label(t.Name, 11, can ? UiKit.Text : UiKit.Muted);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            name.CustomMinimumSize = new Vector2(104, 0);
            name.MouseFilter = Control.MouseFilterEnum.Ignore;
            inner.AddChild(name);
            slot.AddChild(inner);
            if (i < SlotKeys.Length)
            {
                Label key = UiKit.Label(i == 9 ? "0" : $"{i + 1}", 11, UiKit.Heading);
                key.Position = new Vector2(5, 2);
                key.MouseFilter = Control.MouseFilterEnum.Ignore;
                slot.AddChild(key);
            }

            slot.Pressed += () =>
            {
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

    // Keys 1…0 pick the slots of the open tab; Tab moves to the next tab. True when the key was used.
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

        StartPlacing(_slotTypes[slot]);
        _barSig = "";
        UpdateUi();
        return true;
    }

    private static string Describe(PlayContentMessage content, PlayBuildingInfo b)
    {
        var lines = new List<string> { b.Name, $"Costs {Amounts(content, b.Price)}; builds in {b.BuildSeconds} s once the materials are there." };
        if (b.Workers > 0)
        {
            lines.Add($"Jobs: {b.Workers}. Each worker a minute makes {Amounts(content, b.Produces)}" + (Any(b.Consumes) ? $" from {Amounts(content, b.Consumes)}." : "."));
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
