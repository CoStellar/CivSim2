namespace CivSim.Core.Random;

/// <summary>
/// Named random streams, one per system, so that a change in one system never shifts the rolls of another.
/// Values are part of the save format and of determinism: never renumber, only append.
/// </summary>
public enum RandomStreamId
{
    /// <summary>World generation (map, deposits, start positions).</summary>
    World = 0,

    /// <summary>Weather and climate during play.</summary>
    Climate = 1,

    /// <summary>Random events (drought, fire, flood, epidemic...).</summary>
    Events = 2,

    /// <summary>AI decisions. Parallel AI uses per-entity derived generators, see <see cref="RandomStreams.Derive"/>.</summary>
    Ai = 3,

    /// <summary>Combat resolution.</summary>
    Combat = 4,

    /// <summary>Population: growth, health, migration.</summary>
    Population = 5,

    /// <summary>Research: inspirations, diffusion.</summary>
    Research = 6,

    /// <summary>Leaders: traits, succession.</summary>
    Leaders = 7,
}
