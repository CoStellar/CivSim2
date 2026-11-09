using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Data;

namespace CivSim.Tests;

/// <summary>
/// Content for unit tests: the real data, but every civilization pulses in every tick and one month of action is one tick
/// (ADR 0025), so a test that runs one turn sees one step of every civilization, as before the live clock. Tests of the
/// live clock itself, of determinism and of whole games use <see cref="ContentLoader.Load(string?)"/>.
/// </summary>
internal static class TestContent
{
    /// <summary>Time rules with a pulse in every tick.</summary>
    public static readonly TimeRules EveryTick = new() { TicksPerSecond = 10, TicksPerPulse = 1, TicksPerActionMonth = 1 };

    /// <summary>The real content with <see cref="EveryTick"/>.</summary>
    public static GameContent Load() => ContentLoader.Load().WithTime(EveryTick);
}
