using System;
using CivSim.Core.Content;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>
/// The clock of the live game (ADR 0025). One turn of the runner is one tick (<see cref="GameState.Turn"/>). Every
/// civilization develops in pulses: in the tick of its pulse it computes its own state (economy, people, research,
/// perception) and takes its planned decisions over the turn length of its own era (GDD 6.2), exactly like an own step of
/// ADR 0018; pulses come every <see cref="TimeRules.TicksPerPulse"/> ticks, each civilization in a tick of its own, so the
/// work is spread over the ticks. The world (climate, regrowth outside settlements, the sea) has its pulse in ticks that
/// are multiples of the pulse length and moves <see cref="GameState.Month"/> by the turn length of the most advanced living
/// civilization: the year counter is only a number for players. Action (episodes, war timers) counts in ticks and never
/// stops the world.
/// </summary>
public static class WorldClock
{
    /// <summary>Months of a step when no eras are loaded (50 years).</summary>
    public const int DefaultStepMonths = 600;

    /// <summary>
    /// Length of a pulse of <paramref name="civ"/>, months: its era's turn length, shortened on planets with live
    /// seasons to one step of the planet's year (decision of 2026-11-05).
    /// </summary>
    public static int RhythmMonths(GameContent content, GameState state, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        int months = EraMonths(content, civ.Era);
        WorldGen.SeasonRules seasons = content.Seasons;
        return seasons.Live(state.Settings.Planet) ? Math.Max(1, Math.Min(months, seasons.StepMonths(state.Settings.Planet))) : months;
    }

    /// <summary>Turn length of <paramref name="era"/>, months (GDD 6.2): the months a pulse of that era covers.</summary>
    public static int EraMonths(GameContent content, int era)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Eras.Count == 0 ? DefaultStepMonths : Math.Max(1, content.Eras[Math.Clamp(era, 0, content.Eras.Count - 1)].MonthsPerTurn);
    }

    /// <summary>
    /// Tick of <paramref name="civ"/>'s pulse within every <see cref="TimeRules.TicksPerPulse"/> ticks: never the world's
    /// tick (0) when pulses are longer than one tick, and different for neighbouring ids, so the work is spread.
    /// </summary>
    public static int PulseOffset(TimeRules time, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(civ);
        int p = time.TicksPerPulse;
        return p <= 1 ? 0 : 1 + (int)(((long)civ.Id.Value * 7) % (p - 1));
    }

    /// <summary>True when the world has its pulse in <paramref name="tick"/>.</summary>
    public static bool WorldPulse(TimeRules time, long tick)
    {
        ArgumentNullException.ThrowIfNull(time);
        return tick % Math.Max(1, time.TicksPerPulse) == 0;
    }

    /// <summary>
    /// Months the world covers in its pulse: the turn length of the most advanced living civilization (the shortest
    /// rhythm), or of the stone age (or one step of a planet's live year) when nobody lives.
    /// </summary>
    public static int WorldPulseMonths(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        int months = int.MaxValue;
        foreach (CivilizationState civ in state.Civilizations)
        {
            if (civ.IsAlive)
            {
                months = Math.Min(months, RhythmMonths(content, state, civ));
            }
        }

        if (months == int.MaxValue)
        {
            WorldGen.SeasonRules seasons = content.Seasons;
            months = seasons.Live(state.Settings.Planet) ? Math.Min(DefaultStepMonths, seasons.StepMonths(state.Settings.Planet)) : EraMonths(content, 0);
        }

        return months;
    }

    /// <summary>
    /// Starts the tick <see cref="GameState.Turn"/>: every living civilization whose pulse falls in it pulses (its
    /// first pulse comes at once, in the first tick it lives; its
    /// <see cref="CivilizationState.StepMonths"/> become its rhythm), the others wait (0); in a world pulse the world
    /// covers <see cref="WorldPulseMonths"/> and the year counter moves by them after the tick (<see cref="GameState.StepMonths"/>).
    /// Called when a game is created and after every tick; calling it again in the same tick changes nothing.
    /// </summary>
    public static void Begin(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        TimeRules time = content.Time;
        long tick = state.Turn;
        int p = Math.Max(1, time.TicksPerPulse);
        int inPulse = (int)(tick % p);
        foreach (CivilizationState civ in state.Civilizations)
        {
            if (!civ.IsAlive)
            {
                civ.StepMonths = 0;
                continue;
            }

            if (civ.LastPulseTick == tick)
            {
                continue; // started already (Begin called twice in one tick)
            }

            if (civ.LastPulseTick < 0 || inPulse == PulseOffset(time, civ)) // a new civilization pulses at once, then in its own tick
            {
                int rhythm = RhythmMonths(content, state, civ);
                civ.StepMonths = rhythm;
                civ.StepStartMonth = state.Month;
                civ.NextStepMonth = state.Month + rhythm;
                civ.LastPulseTick = tick;
                civ.Steps++;
            }
            else
            {
                civ.StepMonths = 0;
            }
        }

        state.ActionMonth = tick / Math.Max(1, time.TicksPerActionMonth);
        int world = WorldPulse(time, tick) ? WorldPulseMonths(content, state) : 0;
        state.WorldMonths = world;
        state.StepMonths = world;
    }

    /// <summary>The month in which the year counter will stand after the next world pulse.</summary>
    public static long NextWorldMonth(GameContent content, GameState state) => state.Month + WorldPulseMonths(content, state);


    /// <summary>True when <paramref name="civ"/> computes its own state and takes planned decisions in this world step.</summary>
    public static bool Steps(CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.IsAlive && civ.StepMonths > 0;
    }

    /// <summary>
    /// True in the own step of <paramref name="civ"/> that starts a new era-length period: the step for slow, planned
    /// choices (splits, recipes). Without live seasons that is every own step.
    /// </summary>
    public static bool PlansNow(GameContent content, GameState state, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Steps(civ))
        {
            return false;
        }

        int era = EraMonths(content, civ.Era);
        return civ.StepMonths >= era || state.Month % era < civ.StepMonths;
    }

    /// <summary>
    /// Months a settlement computes in this world step: its civilization's own step while people live in it, the world
    /// step once it is abandoned (what is left there spoils with the world's time).
    /// </summary>
    public static int MonthsOf(GameState state, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(s);
        return s.IsActive ? MonthsOf(state, s.Owner) : state.WorldMonths;
    }

    /// <summary>Months <paramref name="owner"/> computes in this world step (0 when it waits, or is unknown or dead).</summary>
    public static int MonthsOf(GameState state, CivId owner)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? civ = state.FindCivilization(owner);
        return civ is { IsAlive: true } ? civ.StepMonths : 0;
    }
}

/// <summary>
/// Reasons for a civilization to decide outside its own rhythm (reactive decisions, ADR 0018), kept until its next
/// decisions phase. Values are part of the save format: only append.
/// </summary>
[Flags]
public enum Alarm : byte
{
    /// <summary>Nothing happened.</summary>
    None = 0,

    /// <summary>Another civilization declared war on this one.</summary>
    WarDeclared = 1,

    /// <summary>A camp of this civilization was attacked.</summary>
    Attacked = 2,

    /// <summary>This civilization declared war, or joined one: its commander plans a campaign (stage 4e).</summary>
    WentToWar = 4,
}
