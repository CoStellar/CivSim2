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
        bool anyStep = false;
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

            anyStep |= civ.StepMonths > 0;
            next = Math.Min(next, civ.NextStepMonth);
        }

        bool noneAlive = next == long.MaxValue;
        if (noneAlive)
        {
            // Nobody lives: the world still turns, at the slowest rhythm (or one step of a planet's live year).
            WorldGen.SeasonRules seasons = content.Seasons;
            int rest = seasons.Live(state.Settings.Planet) ? Math.Min(DefaultStepMonths, seasons.StepMonths(state.Settings.Planet)) : EraMonths(content, 0);
            next = ((now / rest) + 1) * rest;
        }

        // ADR 0022: the world itself covers the months until the next step of a civilization; episodes (and alarms that
        // wait for a reaction) put steps of their own in between, in which the world waits.
        state.WorldMonths = anyStep || noneAlive ? (int)(next - now) : 0;
        state.StepMonths = (int)(Math.Max(now + 1, Math.Min(next, Soonest(content, state, now))) - now);
    }

    /// <summary>
    /// Ends the current world step early when something in it needs a step soon (ADR 0022): a campaign was sent, a war
    /// was declared (alarms wait for a reaction next month) or a quiet war runs out. Called before time moves on, after
    /// the decisions and commands of the step. The civilizations' own steps and the world's months stay as they were.
    /// </summary>
    public static void Shorten(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        long now = state.Month;
        long soonest = Soonest(content, state, now);
        if (soonest - now < state.StepMonths)
        {
            state.StepMonths = (int)Math.Max(1, soonest - now);
        }
    }

    // The nearest month something other than a civilization's own step needs a world step: an episode, a reaction to an
    // alarm (next month), the end of a quiet war.
    private static long Soonest(GameContent content, GameState state, long now)
    {
        long soonest = Math.Min(state.Episodes.NextMonth(), QuietWarEnds(content, state, now));
        foreach (CivilizationState civ in state.Civilizations)
        {
            if (civ.IsAlive && civ.Alarms != Alarm.None)
            {
                return Math.Min(soonest, now + 1); // a reaction comes in the next month, not in the next long step
            }
        }

        return soonest;
    }

    // ADR 0022: the month the first quiet war ends by itself (a white peace), so that it ends in time, not at the next long step.
    private static long QuietWarEnds(GameContent content, GameState state, long now)
    {
        long quiet = content.War.QuietPeaceYears * 12L;
        long soonest = long.MaxValue;
        foreach (Diplomacy.Treaty t in state.Treaties.All)
        {
            if (t.Kind == Diplomacy.TreatyKind.War)
            {
                long end = Math.Max(t.SinceMonth, t.LastCampaignMonth) + quiet;
                soonest = Math.Min(soonest, Math.Max(now + 1, end));
            }
        }

        return soonest;
    }

    /// <summary>The month of the next step of any living civilization (<see cref="long.MaxValue"/> when none lives).</summary>
    public static long NextCivilizationMonth(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        long next = long.MaxValue;
        foreach (CivilizationState civ in state.Civilizations)
        {
            if (civ.IsAlive)
            {
                next = Math.Min(next, civ.NextStepMonth);
            }
        }

        return next;
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
