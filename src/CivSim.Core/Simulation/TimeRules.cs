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

    /// <summary>Months of action one pulse lasts (at least 1).</summary>
    public int PulseActionMonths => System.Math.Max(1, TicksPerPulse / System.Math.Max(1, TicksPerActionMonth));

    /// <summary>Ticks of <paramref name="months"/> months of action.</summary>
    public long ActionTicks(long months) => months * TicksPerActionMonth;

    /// <summary>
    /// <paramref name="ticks"/> as time of the game at speed x1 for the chronicle: "40 s", "2 min", "3 min 20 s". Durations are
    /// told in minutes and seconds of the game, years only as the date of an entry (ADR 0026; playtest P-09: "ready in 1
    /// year" and "after 600 years of war" in the same war of 36 seconds).
    /// </summary>
    public string PlayTime(long ticks)
    {
        long seconds = System.Math.Max(1, (System.Math.Max(0, ticks) + (TicksPerSecond / 2)) / System.Math.Max(1, TicksPerSecond));
        long minutes = seconds / 60;
        seconds %= 60;
        System.Globalization.CultureInfo c = System.Globalization.CultureInfo.InvariantCulture;
        return minutes == 0 ? seconds.ToString(c) + " s"
            : seconds == 0 ? minutes.ToString(c) + " min"
            : minutes.ToString(c) + " min " + seconds.ToString(c) + " s";
    }
}
