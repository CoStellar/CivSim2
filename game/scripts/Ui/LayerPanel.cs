using System;
using System.Collections.Generic;
using CivSim.Game.Map;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// The layer list of the stage 5 map view: each layer can be switched on and off, faded with a slider and moved up
/// or down inside its group (cell layers always lie under drawn layers). Top of the list = top of the map. Shown in
/// the layer settings window; the top bar has the quick on/off icons.
/// </summary>
public partial class LayerPanel : VBoxContainer
{
    private static readonly Dictionary<MapLayer, string> Names = new()
    {
        [MapLayer.Biomes] = "Biomes and terrain",
        [MapLayer.Elevation] = "Height and depth",
        [MapLayer.Hillshade] = "Hill shading",
        [MapLayer.Temperature] = "Temperature",
        [MapLayer.Humidity] = "Humidity",
        [MapLayer.Fertility] = "Fertility",
        [MapLayer.Growing] = "Growing season",
        [MapLayer.Seasons] = "Season swing",
        [MapLayer.SeasonNow] = "Temperature now",
        [MapLayer.WildFood] = "Wild food",
        [MapLayer.Deposits] = "Deposits",
        [MapLayer.Owner] = "Owner",
        [MapLayer.Influence] = "Influence zones",
        [MapLayer.Fog] = "Memory and fog",
        [MapLayer.Rivers] = "Rivers",
        [MapLayer.Fields] = "Fields",
        [MapLayer.Borders] = "Borders",
        [MapLayer.Settlements] = "Settlements",
        [MapLayer.Units] = "Units",
        [MapLayer.Events] = "Events",
        [MapLayer.Relations] = "Relations",
    };

    private readonly LayerStack _layers;
    private VBoxContainer _rows = null!;
    private int _version = -1;

    /// <summary>Creates the panel for <paramref name="layers"/>.</summary>
    public LayerPanel(LayerStack layers)
    {
        _layers = layers;
    }

    /// <summary>Required by Godot; not used.</summary>
    public LayerPanel()
        : this(new LayerStack())
    {
    }

    /// <summary>Display name of a layer.</summary>
    public static string NameOf(MapLayer layer) => Names.GetValueOrDefault(layer, layer.ToString());

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 4);
        var head = new HBoxContainer();
        AddChild(head);
        head.AddChild(new Label { Text = "Layers: top of the list is drawn on top. The memory and fog layer shows the followed civilization's knowledge.", SizeFlagsHorizontal = SizeFlags.ExpandFill, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) });
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 2);
        AddChild(_rows);
        Rebuild();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // Switches changed elsewhere (the icon bar) show here too.
        if (_layers.Version != _version)
        {
            _version = _layers.Version;
            foreach (Node row in _rows.GetChildren())
            {
                if (row is HBoxContainer { } h && h.GetChildCount() > 0 && h.GetChild(0) is CheckBox check && check.HasMeta("layer"))
                {
                    check.SetPressedNoSignal(_layers[(MapLayer)(int)check.GetMeta("layer")].Enabled);
                }
            }
        }
    }

    private void Rebuild()
    {
        foreach (Node child in _rows.GetChildren())
        {
            child.QueueFree();
        }

        IReadOnlyList<LayerSetting> list = _layers.Layers;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            LayerSetting s = list[i];
            if (i < list.Count - 1 && LayerStack.IsCellLayer(s.Layer) && !LayerStack.IsCellLayer(list[i + 1].Layer))
            {
                _rows.AddChild(new HSeparator());
            }

            _rows.AddChild(Row(s));
        }
    }

    private HBoxContainer Row(LayerSetting s)
    {
        MapLayer layer = s.Layer;
        var row = new HBoxContainer();
        var check = new CheckBox
        {
            Text = NameOf(layer),
            ButtonPressed = s.Enabled,
            CustomMinimumSize = new Vector2(190, 0),
            FocusMode = FocusModeEnum.None,
            ClipText = true,
        };
        check.SetMeta("layer", (int)layer);
        check.Toggled += on => _layers.SetEnabled(layer, on);
        row.AddChild(check);
        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 5,
            Value = s.Opacity,
            CustomMinimumSize = new Vector2(90, 0),
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.None,
            TooltipText = "Opacity",
        };
        slider.ValueChanged += v => _layers.SetOpacity(layer, (int)v);
        row.AddChild(slider);
        if (layer != MapLayer.Biomes)
        {
            row.AddChild(Arrow("▲", "Move up", () => Move(layer, +1)));
            row.AddChild(Arrow("▼", "Move down", () => Move(layer, -1)));
        }

        return row;
    }

    private static Button Arrow(string text, string tip, Action pressed)
    {
        var b = new Button { Text = text, TooltipText = tip, FocusMode = FocusModeEnum.None, Flat = true };
        b.Pressed += pressed;
        return b;
    }

    private void Move(MapLayer layer, int direction)
    {
        if (_layers.Move(layer, direction))
        {
            Rebuild();
        }
    }
}
