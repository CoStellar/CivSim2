using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CivSim.Game.Map;

/// <summary>
/// Layers of the stage 5 map view. Cell layers colour whole hexes and are blended on the CPU into one tint per hex;
/// drawn layers (lines, sprites) are separate nodes on top. Values are saved by name in the viewer settings.
/// </summary>
public enum MapLayer
{
    /// <summary>Biome tiles with relief and features (the base; textured when zoomed in).</summary>
    Biomes,

    /// <summary>Height of land and depth of water (hypsometric and bathymetric tints).</summary>
    Elevation,

    /// <summary>Hill shading: slopes facing the light from the top left are lighter, the others darker.</summary>
    Hillshade,

    /// <summary>Annual mean temperature.</summary>
    Temperature,

    /// <summary>Humidity.</summary>
    Humidity,

    /// <summary>Soil fertility.</summary>
    Fertility,

    /// <summary>Share of the year plants can grow (stage 1b).</summary>
    Growing,

    /// <summary>Difference between summer and winter (stage 1b).</summary>
    Seasons,

    /// <summary>Temperature of the current season (live seasons).</summary>
    SeasonNow,

    /// <summary>Wild food left around camps (stage 3c).</summary>
    WildFood,

    /// <summary>Deposits: colour far away, icons near.</summary>
    Deposits,

    /// <summary>Owner civilization of each cell.</summary>
    Owner,

    /// <summary>Influence zones: owner colour, stronger near the settlement.</summary>
    Influence,

    /// <summary>
    /// Map memory and fog of war of the followed civilization: at full opacity unknown cells are covered completely
    /// (dark grey, and nothing drawn on them), remembered cells partly. Always on top of the other cell layers.
    /// </summary>
    Fog,

    /// <summary>Rivers on hex edges.</summary>
    Rivers,

    /// <summary>Farmed fields of villages.</summary>
    Fields,

    /// <summary>Borders between civilizations on hex edges.</summary>
    Borders,

    /// <summary>Settlements: camps, villages, abandoned settlements and ruins.</summary>
    Settlements,

    /// <summary>Units: wandering camps with the way to their target.</summary>
    Units,

    /// <summary>Markers of active events (drought, fire, flood, epidemic).</summary>
    Events,

    /// <summary>Relations between civilizations: lines between their largest settlements.</summary>
    Relations,
}

/// <summary>One layer's switch, opacity and place in the stack.</summary>
public sealed class LayerSetting
{
    /// <summary>The layer.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<MapLayer>))]
    public MapLayer Layer { get; set; }

    /// <summary>Drawn at all.</summary>
    public bool Enabled { get; set; }

    /// <summary>Opacity, 0–100%.</summary>
    public int Opacity { get; set; } = 100;
}

/// <summary>
/// The ordered list of layers, bottom first. Cell layers always lie under drawn layers; within each group the order
/// can be changed. Every change bumps <see cref="Version"/>, so the map recolours only when something changed.
/// </summary>
public sealed class LayerStack
{
    private readonly List<LayerSetting> _layers = [];

    /// <summary>Creates the default stack (GDD 12.2 order: biome, overlays, then objects and units).</summary>
    public LayerStack()
    {
        foreach (MapLayer layer in Enum.GetValues<MapLayer>())
        {
            _layers.Add(new LayerSetting { Layer = layer, Enabled = DefaultOn(layer), Opacity = DefaultOpacity(layer) });
        }
    }

    /// <summary>Raised after any change.</summary>
    public event Action? Changed;

    /// <summary>Counts changes.</summary>
    public int Version { get; private set; }

    /// <summary>Layers bottom first.</summary>
    public IReadOnlyList<LayerSetting> Layers => _layers;

    /// <summary>True for layers that colour whole cells (blended into the hex tint).</summary>
    public static bool IsCellLayer(MapLayer layer) => layer <= MapLayer.Fog;

    /// <summary>Setting of a layer.</summary>
    public LayerSetting this[MapLayer layer] => _layers.Find(l => l.Layer == layer)!;

    /// <summary>Opacity 0–1 when the layer is on, otherwise 0.</summary>
    public float Alpha(MapLayer layer)
    {
        LayerSetting s = this[layer];
        return s.Enabled ? Math.Clamp(s.Opacity, 0, 100) / 100f : 0f;
    }

    /// <summary>Position of a layer from the bottom (for drawing order).</summary>
    public int IndexOf(MapLayer layer) => _layers.FindIndex(l => l.Layer == layer);

    /// <summary>Switches a layer on or off.</summary>
    public void SetEnabled(MapLayer layer, bool on)
    {
        this[layer].Enabled = on;
        Bump();
    }

    /// <summary>Sets a layer's opacity (0–100).</summary>
    public void SetOpacity(MapLayer layer, int opacity)
    {
        this[layer].Opacity = Math.Clamp(opacity, 0, 100);
        Bump();
    }

    /// <summary>Moves a layer one place up (+1) or down (−1) inside its group.</summary>
    public bool Move(MapLayer layer, int direction)
    {
        int i = IndexOf(layer);
        int j = i + Math.Sign(direction);
        if (j < 0 || j >= _layers.Count || IsCellLayer(_layers[j].Layer) != IsCellLayer(layer) || _layers[j].Layer is MapLayer.Biomes or MapLayer.Fog || layer is MapLayer.Biomes or MapLayer.Fog)
        {
            return false;
        }

        (_layers[i], _layers[j]) = (_layers[j], _layers[i]);
        Bump();
        return true;
    }

    /// <summary>Shows only <paramref name="layer"/> over the biomes (command line <c>--overlay</c>, benchmark).</summary>
    public void Solo(MapLayer layer)
    {
        foreach (LayerSetting s in _layers)
        {
            if (IsCellLayer(s.Layer))
            {
                s.Enabled = s.Layer == MapLayer.Biomes || s.Layer == layer;
            }
        }

        Bump();
    }

    /// <summary>Takes switches, opacities and order from saved settings; unknown or missing layers keep their defaults.</summary>
    public void Load(IReadOnlyList<LayerSetting>? saved)
    {
        if (saved is null)
        {
            return;
        }

        var ordered = new List<LayerSetting>();
        foreach (LayerSetting s in saved)
        {
            LayerSetting? mine = _layers.Find(l => l.Layer == s.Layer);
            if (mine is not null && !ordered.Contains(mine))
            {
                mine.Enabled = s.Enabled;
                mine.Opacity = Math.Clamp(s.Opacity, 0, 100);
                ordered.Add(mine);
            }
        }

        foreach (LayerSetting s in _layers)
        {
            if (!ordered.Contains(s))
            {
                ordered.Add(s);
            }
        }

        // Keep the two groups apart and the biomes at the bottom whatever the file says.
        // (a stable sort: List.Sort would shuffle layers of the same group).
        _layers.Clear();
        _layers.AddRange(System.Linq.Enumerable.OrderBy(ordered, l => Group(l.Layer)));
        Bump();
    }

    /// <summary>A copy of the settings for saving.</summary>
    public List<LayerSetting> Save() => _layers.ConvertAll(l => new LayerSetting { Layer = l.Layer, Enabled = l.Enabled, Opacity = l.Opacity });

    private static int Group(MapLayer layer) => layer == MapLayer.Biomes ? 0 : layer == MapLayer.Fog ? 2 : IsCellLayer(layer) ? 1 : 3;

    private static bool DefaultOn(MapLayer layer) => layer is MapLayer.Biomes or MapLayer.Hillshade or MapLayer.Fog or MapLayer.Influence or MapLayer.Rivers or MapLayer.Fields
        or MapLayer.Borders or MapLayer.Settlements or MapLayer.Units or MapLayer.Events;

    private static int DefaultOpacity(MapLayer layer) => layer switch
    {
        MapLayer.Influence => 45,
        MapLayer.Hillshade => 50,
        MapLayer.Owner => 60,
        MapLayer.Fog => 100,
        MapLayer.Relations => 80,
        _ => 100,
    };

    private void Bump()
    {
        Version++;
        Changed?.Invoke();
    }
}
