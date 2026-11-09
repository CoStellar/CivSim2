namespace CivSim.Core.Simulation;

/// <summary>
/// Time of the live game (ADR 0025, data/time.json). One turn of the runner is one tick. Action (campaigns, later units)
/// is counted in ticks; development (economy, people, research, planned AI decisions) happens in pulses of each
/// civilization, every <see cref="TicksPerPulse"/> ticks, each civilization in a tick of its own; the world (climate,
/// regrowth, the sea) has its own pulse and moves the year counter, which is only a number shown to players.
/// </summary>
public sealed class TimeRules
{
    /// <summary>Ticks in a real second at speed x1 (the server paces turns with it).</summary>
    public int TicksPerSecond { get; init; } = 10;

    /// <summary>Ticks between two pulses of a civilization, and between two pulses of the world.</summary>
    public int TicksPerPulse { get; init; } = 30;

    /// <summary>Ticks of one month of action: one step of a war campaign, and the unit of war timers (readiness, quiet war).</summary>
    public int TicksPerActionMonth { get; init; } = 5;

    /// <summary>Ticks of <paramref name="months"/> months of action.</summary>
    public long ActionTicks(long months) => months * TicksPerActionMonth;
}
