using System;
using CivSim.Core.Content;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>
/// The world clock and the rhythms of civilizations (ADR 0018). Game time is a counter of months that only counts.
/// Every civilization computes its own state (economy, people, research, perception) and takes its planned decisions only
/// in its own steps, whose length is the turn length of its own era (GDD 6.2). Steps are pinned to the calendar
/// (a 25-year rhythm steps in years 0, 25, 50…), and a step always covers exactly the months until the next one, so no
/// time is lost when eras differ or change. A turn of the runner is one world step: from now to the nearest month in
/// which some civilization steps. The world itself (climate, regrowth outside settlements, the sea) is computed only in
/// world steps, over the months since the last one.
/// </summary>
public static class WorldClock
{
    /// <summary>Months of a step when no eras are loaded (50 years).</summary>
    public const int DefaultStepMonths = 600;

    /// <summary>
    /// Length of an own step of <paramref name="civ"/>, months: its era's turn length, shortened on planets with live
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

    /// <summary>Turn length of <paramref name="era"/>, months (GDD 6.2): the rhythm of planned decisions.</summary>
    public static int EraMonths(GameContent content, int era)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Eras.Count == 0 ? DefaultStepMonths : Math.Max(1, content.Eras[Math.Clamp(era, 0, content.Eras.Count - 1)].MonthsPerTurn);
    }

    /// <summary>
    /// Starts the world step that begins at <see cref="GameState.Month"/>: every living civilization whose next step is
    /// due now steps (its <see cref="CivilizationState.StepMonths"/> become the months until its following step, pinned
    /// to the calendar), the others wait (0), and the world step lasts until the nearest next step of anyone.
    /// Called when a game is created and after every world step; calling it again in the same month changes nothing.
    /// </summary>
    public static void Begin(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        long now = state.Month;
        long next = long.MaxValue;
        foreach (CivilizationState civ in state.Civilizations)
        {
            if (!civ.IsAlive)
            {
                civ.StepMonths = 0;
                continue;
            }

            if (civ.NextStepMonth <= now)
            {
                int rhythm = RhythmMonths(content, state, civ);
                long end = ((now / rhythm) + 1) * rhythm;
                civ.StepMonths = (int)(end - now);
                civ.StepStartMonth = now;
                civ.NextStepMonth = end;
                civ.Steps++;
            }
            else
            {
                civ.StepMonths = civ.StepStartMonth == now ? (int)(civ.NextStepMonth - now) : 0;
            }

            next = Math.Min(next, civ.NextStepMonth);
        }

        if (next == long.MaxValue)
        {
            // Nobody lives: the world still turns, at the slowest rhythm (or one step of a planet's live year).
            WorldGen.SeasonRules seasons = content.Seasons;
            int rest = seasons.Live(state.Settings.Planet) ? Math.Min(DefaultStepMonths, seasons.StepMonths(state.Settings.Planet)) : EraMonths(content, 0);
            next = ((now / rest) + 1) * rest;
        }

        state.StepMonths = (int)(next - now);
    }

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
        return s.IsActive ? MonthsOf(state, s.Owner) : state.StepMonths;
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
}
