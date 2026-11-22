using System.Collections.Generic;
using System.Text.Json;

namespace CivSim.Sim.Content;

/// <summary>
/// Reads JSON properties of the game data and, while a check runs, remembers every property name the game asked for.
/// <see cref="SimContent.Check"/> then reports keys in data/sim/game.json that the game never reads: a typo such as
/// "foodMarginPercnt" would otherwise fall back to the default without a word (2026-11-22).
/// </summary>
internal static class JsonRead
{
    [System.ThreadStatic]
    private static HashSet<string>? _read;

    /// <summary>Starts remembering property names on this thread; returns the set that fills up.</summary>
    public static HashSet<string> Begin() => _read = [];

    /// <summary>Stops remembering.</summary>
    public static void End() => _read = null;

    /// <summary>Like <see cref="JsonElement.GetProperty(string)"/>, remembered.</summary>
    public static JsonElement Prop(this JsonElement e, string name)
    {
        _read?.Add(name);
        return e.GetProperty(name);
    }

    /// <summary>Like <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>, remembered.</summary>
    public static bool TryProp(this JsonElement e, string name, out JsonElement value)
    {
        _read?.Add(name);
        return e.TryGetProperty(name, out value);
    }

    /// <summary>Like <see cref="JsonElement.EnumerateObject"/>: every key of a map the game walks counts as read.</summary>
    public static JsonElement.ObjectEnumerator Entries(this JsonElement e)
    {
        if (_read is { } read)
        {
            foreach (JsonProperty p in e.EnumerateObject())
            {
                read.Add(p.Name);
            }
        }

        return e.EnumerateObject();
    }
}
