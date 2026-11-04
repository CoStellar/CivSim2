namespace CivSim.Core.Simulation;

/// <summary>The 11 phases of a turn, in execution order (GDD 11.1). Values are indexes into timing arrays.</summary>
public enum TurnPhase
{
    /// <summary>1. Weather and climate, regrowth, effect expiry, event conditions.</summary>
    World = 0,

    /// <summary>2. Visibility and fog of war, influence maps, resource forecasts.</summary>
    Perception = 1,

    /// <summary>3. AI advisors and player orders produce commands (parallel, read-only).</summary>
    Decisions = 2,

    /// <summary>4. Commands are validated and applied: diplomacy, economy, movement; civilization order rotates.</summary>
    CommandResolution = 3,

    /// <summary>5. Armies, caravans, ships, settlers.</summary>
    Movement = 4,

    /// <summary>6. Combat and sieges.</summary>
    Combat = 5,

    /// <summary>7. Extraction, trade network, recipes, consumption, spoilage and storage limits.</summary>
    Economy = 6,

    /// <summary>8. Growth, health, migration, layer changes.</summary>
    Population = 7,

    /// <summary>9. Administration, stability, revolts, succession.</summary>
    State = 8,

    /// <summary>10. Research, technology diffusion, influence zones, era change.</summary>
    ScienceAndInfluence = 9,

    /// <summary>11. Statistics, chronicle, snapshot for the viewer, autosave.</summary>
    Record = 10,
}

/// <summary>Helpers for <see cref="TurnPhase"/>.</summary>
public static class TurnPhases
{
    /// <summary>Number of phases.</summary>
    public const int Count = 11;
}
