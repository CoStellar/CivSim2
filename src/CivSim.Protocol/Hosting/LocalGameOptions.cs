namespace CivSim.Protocol.Hosting;

/// <summary>
/// Settings for a new local game, as the client chooses them on screen (GDD 3, "Ustawienia świata").
/// Plain values only, so the client never needs core types. Words are the lower-case setting names
/// (for example <c>"continents"</c>, <c>"cold"</c>).
/// </summary>
public sealed record LocalGameOptions(ulong Seed, int Width, int Height, int Civilizations)
{
    /// <summary>continents, pangea or archipelago.</summary>
    public string WorldType { get; init; } = "continents";

    /// <summary>Share of water, 30–80.</summary>
    public int WaterPercent { get; init; } = 60;

    /// <summary>temperate, cold or hot.</summary>
    public string Climate { get; init; } = "temperate";

    /// <summary>normal, dry or wet.</summary>
    public string Moisture { get; init; } = "normal";

    /// <summary>How fast humidity falls away from the sea: normal, dry or humid.</summary>
    public string Inland { get; init; } = "normal";

    /// <summary>normal, flat or mountainous.</summary>
    public string Relief { get; init; } = "normal";

    /// <summary>normal, scarce or rich.</summary>
    public string Resources { get; init; } = "normal";

    /// <summary>Map size presets from GDD 3.</summary>
    public static LocalGameOptions ForSize(string size, ulong seed) => size switch
    {
        "test" => new LocalGameOptions(seed, 64, 40, 4),
        "large" => new LocalGameOptions(seed, 384, 240, 20),
        "huge" => new LocalGameOptions(seed, 512, 320, 32),
        _ => new LocalGameOptions(seed, 256, 160, 12),
    };
}
