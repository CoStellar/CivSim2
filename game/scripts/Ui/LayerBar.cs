using System;
using System.Text;
using CivSim.Game.Map;
using Godot;

namespace CivSim.Game.Ui;

/// <summary>
/// One icon button per map layer in the top bar (stage 5): a click switches the layer on or off; the tooltip names
/// it. Opacity and order live in the layer settings window. Icons are <c>ui/icon_layer_&lt;layer&gt;.png</c>
/// (16×16, drawn twice as large), so Patryk's icons replace the placeholders by name.
/// </summary>
public partial class LayerBar : HBoxContainer
{
    private readonly LayerStack _layers;
    private readonly ArtLibrary _art;
    private readonly System.Collections.Generic.Dictionary<MapLayer, Button> _buttons = [];
    private int _version = -1;

    /// <summary>Creates the bar.</summary>
    public LayerBar(LayerStack layers, ArtLibrary art)
    {
        _layers = layers;
        _art = art;
    }

    /// <summary>Required by Godot; not used.</summary>
    public LayerBar()
        : this(new LayerStack(), new ArtLibrary(ArtLibrary.FindArtDirectory()))
    {
    }

    /// <summary>Raised when the settings button is pressed.</summary>
    public event Action? SettingsPressed;

    /// <summary>File-name form of a layer: SeasonNow → season_now.</summary>
    public static string FileName(MapLayer layer)
    {
        var sb = new StringBuilder();
        foreach (char ch in layer.ToString())
        {
            if (char.IsUpper(ch) && sb.Length > 0)
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 2);
        foreach (MapLayer layer in Enum.GetValues<MapLayer>())
        {
            var b = new Button
            {
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                TooltipText = LayerPanel.NameOf(layer),
                CustomMinimumSize = new Vector2(34, 32),
                ExpandIcon = true,
                IconAlignment = HorizontalAlignment.Center,
            };
            Texture2D? icon = _art.Get("ui", "icon_layer_" + FileName(layer));
            if (icon is not null)
            {
                b.Icon = icon;
            }
            else
            {
                b.Text = LayerPanel.NameOf(layer)[..2];
            }

            MapLayer l = layer;
            b.Toggled += on => _layers.SetEnabled(l, on);
            AddChild(b);
            _buttons[layer] = b;
            if (layer == MapLayer.Fog)
            {
                AddChild(new VSeparator());
            }
        }

        var settings = new Button { FocusMode = FocusModeEnum.None, TooltipText = "Layer settings: opacity and order", CustomMinimumSize = new Vector2(34, 32), ExpandIcon = true };
        Texture2D? cog = _art.Get("ui", "icon_settings");
        if (cog is not null)
        {
            settings.Icon = cog;
        }
        else
        {
            settings.Text = "⚙";
        }

        settings.Pressed += () => SettingsPressed?.Invoke();
        AddChild(new VSeparator());
        AddChild(settings);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_layers.Version == _version)
        {
            return;
        }

        _version = _layers.Version;
        foreach ((MapLayer layer, Button b) in _buttons)
        {
            bool on = _layers[layer].Enabled;
            b.SetPressedNoSignal(on);
            b.Modulate = on ? Colors.White : new Color(1, 1, 1, 0.45f);
        }
    }
}
