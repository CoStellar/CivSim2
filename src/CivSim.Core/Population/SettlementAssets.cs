using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Population;

/// <summary>
/// Settlements as property (GDD 5.5): carrying goods, abandoning a settlement and leaving goods behind. Commands and
/// systems share these helpers so every way of leaving a place follows the same rules.
/// </summary>
public static class SettlementAssets
{
    /// <summary>Name prefix of a campsite left behind by a move; dropped again when people move back in.</summary>
    public const string OldSitePrefix = "Old ";

    /// <summary>
    /// How much <paramref name="people"/> of <paramref name="civ"/> carry: the per-person amount from
    /// <c>data/settlements.json</c>, raised or lowered by <see cref="EffectStat.CarryCapacityPercent"/> effects on the
    /// civilization (pack animals, carts and boats will come from technologies).
    /// </summary>
    public static Amount CarryCapacity(GameContent content, GameState state, CivId civ, int people)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        int percent = 100;
        if (state.Effects.Count > 0)
        {
            percent += state.Effects.Total(content.Effects, EffectStat.CarryCapacityPercent, EffectTarget.Civilization, WorldId.Earth, civ.Value);
        }

        return (content.Assets.CarryPerPerson * Math.Max(0, people)).MultiplyPercent(Math.Max(0, percent), Rounding.Floor);
    }

    /// <summary>
    /// Moves goods from <paramref name="from"/> to <paramref name="to"/>, at most <paramref name="capacity"/> in total and
    /// at most <paramref name="sharePercent"/> of each good. When the load is too heavy, every good is cut in the same
    /// proportion. Returns what was moved.
    /// </summary>
    public static Amount Carry(SettlementState from, SettlementState to, Amount capacity, int sharePercent = 100)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        Span<Amount> stock = from.Stock;
        long wanted = 0;
        for (int g = 0; g < stock.Length; g++)
        {
            wanted += stock[g].MultiplyPercent(sharePercent, Rounding.Floor).Hundredths;
        }

        if (wanted <= 0 || capacity <= Amount.Zero)
        {
            return Amount.Zero;
        }

        long room = Math.Min(wanted, capacity.Hundredths);
        Amount moved = Amount.Zero;
        for (int g = 0; g < stock.Length; g++)
        {
            Amount share = stock[g].MultiplyPercent(sharePercent, Rounding.Floor);
            Amount taken = Amount.FromHundredths(IntMath.MulDiv(share.Hundredths, room, wanted, Rounding.Floor));
            if (taken > Amount.Zero)
            {
                stock[g] -= taken;
                to.Stock[g] += taken;
                moved += taken;
            }
        }

        // Rounding down can leave a few hundredths of room: fill it in good id order.
        for (int g = 0; g < stock.Length && moved.Hundredths < room; g++)
        {
            Amount extra = Amount.FromHundredths(Math.Min(room - moved.Hundredths, stock[g].Hundredths));
            stock[g] -= extra;
            to.Stock[g] += extra;
            moved += extra;
        }

        return moved;
    }

    /// <summary>
    /// Marks an empty settlement as abandoned now. Its former owner gets a claim for <see cref="AssetRules.ClaimYears"/>
    /// if it is still alive; whatever was left stays in the warehouse (food keeps spoiling).
    /// </summary>
    public static void Abandon(GameContent content, GameState state, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(s);
        s.Population.Clear();
        s.Status = SettlementStatus.Abandoned;
        s.AbandonedMonth = state.Month;
        s.FoodGathered = 0;
        s.FoodNeeded = 0;
        s.FoodEaten = 0;
        s.HigherNeedsPerMille = 0;
        if (state.FindCivilization(s.Owner) is { IsAlive: true })
        {
            s.ClaimCiv = s.Owner.Value;
            s.ClaimUntilMonth = state.Month + (content.Assets.ClaimYears * 12L);
        }
        else
        {
            s.ClaimCiv = SettlementState.NoClaim;
        }
    }

    /// <summary>
    /// Moves a settlement with its people to <paramref name="target"/>. They take what they can carry; anything left
    /// stays behind as an abandoned campsite on the old cell (GDD 5.5: nomads return to their old campsites).
    /// Returns the campsite, or null when nothing was left.
    /// </summary>
    public static SettlementState? Relocate(GameContent content, GameState state, SettlementState s, HexCoord target)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(s);
        Amount capacity = CarryCapacity(content, state, s.Owner, s.TotalPopulation());
        SettlementState? site = null;
        if (s.TotalStock() > capacity)
        {
            site = state.FoundSettlement(s.Owner, s.World, s.Cell, OldSitePrefix + s.Name, s.Level, s.StockView.Length, s.PopulationView.Length, s.SkilledView.Length);
            Carry(s, site, Amount.MaxValue);
            Carry(site, s, capacity);
            Abandon(content, state, site);
        }

        s.Cell = target;
        s.MovedMonth = state.Month;
        return site;
    }

    /// <summary>Warriors in a settlement (strata that fight, GDD 5.4).</summary>
    public static int Warriors(GameContent content, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        int warriors = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length && k < content.Strata.Count; k++)
        {
            warriors += content.Strata[k].Fights ? people[k] : 0;
        }

        return warriors;
    }

    /// <summary>Removes <paramref name="percent"/> of the people (rounded up), only from fighting strata when <paramref name="warriorsOnly"/>.</summary>
    public static void Lose(GameContent content, SettlementState s, int percent, bool warriorsOnly)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        Span<int> people = s.Population;
        for (int k = 0; k < people.Length; k++)
        {
            if (!warriorsOnly || (k < content.Strata.Count && content.Strata[k].Fights))
            {
                people[k] -= (int)IntMath.MulDiv(people[k], percent, 100, Rounding.Ceiling);
            }
        }
    }
}

/// <summary>
/// Population phase, after <see cref="PopulationSystem"/>: a settlement whose people all died or left is abandoned
/// (GDD 5.5: abandonment out of necessity).
/// </summary>
public sealed class AbandonmentSystem(GameContent content) : ITurnSystem
{
    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Population;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (SettlementState s in context.State.Settlements)
        {
            if (s.IsActive && s.TotalPopulation() == 0)
            {
                SettlementAssets.Abandon(content, context.State, s);
            }
        }
    }
}

/// <summary>
/// World phase: abandoned settlements fall into ruins and ruins disappear (GDD 5.5, CORE simplified: two states).
/// How long each state lasts depends on the settlement level (<c>data/settlements.json</c>).
/// </summary>
public sealed class DecaySystem(GameContent content) : ITurnSystem
{
    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.World;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        bool anyGone = false;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive)
            {
                continue;
            }

            SettlementLevelDefinition level = content.SettlementLevels[s.Level];
            long years = (state.Month - s.AbandonedMonth) / 12;
            if (years >= level.RuinsUntilYears)
            {
                anyGone = true;
            }
            else if (s.Status == SettlementStatus.Abandoned && years >= level.AbandonedYears)
            {
                s.Status = SettlementStatus.Ruins;
            }
        }

        if (anyGone)
        {
            state.RemoveSettlements(s => !s.IsActive && (state.Month - s.AbandonedMonth) / 12 >= content.SettlementLevels[s.Level].RuinsUntilYears);
        }
    }
}
