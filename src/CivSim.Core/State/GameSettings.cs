namespace CivSim.Core.State;

/// <summary>Settings chosen when a game is created. Fixed for the whole game.</summary>
public sealed record GameSettings(ulong Seed, int MapWidth, int MapHeight, int CivilizationCount)
{
    /// <summary>Settings for quick tests: small map, four civilizations.</summary>
    public static GameSettings Test(ulong seed) => new(seed, 64, 40, 4);
}
