using System;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Population;

/// <summary>
/// Population phase (GDD 11.1, phase 8; GDD 5.2): hunger, health, mood, births and deaths, and the slow drift of
/// people between strata. Uses what the economy phase recorded (food needed and eaten, higher needs met). Rates are
/// per year and compound over the turn, so the result does not depend on the era's turn length.
/// </summary>
public sealed class PopulationSystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly PopulationRules _rules;
    private readonly long[] _weights;
    private readonly Amount[] _shares;
    private readonly int[] _target;

    /// <summary>Creates the system.</summary>
    public PopulationSystem(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _rules = content.Population;
        _weights = new long[content.Strata.Count];
        _shares = new Amount[content.Strata.Count];
        _target = new int[content.Strata.Count];
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Population;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        foreach (SettlementState s in state.Settlements)
        {
            int months = WorldClock.MonthsOf(state, s.Owner);
            int before = s.TotalPopulation();
            if (before == 0 || months == 0)
            {
                continue;
            }

            UpdateHealthAndMood(state, s, before);
            int after = NewTotal(state, s, before, months);
            ChangeTotal(s, before, after);
            Drift(s, months);
            Skills.Trim(_content.Skills, s); // deaths and drift take the same share from the skills (stage 4F)
        }

        foreach (CivilizationState civ in state.Civilizations)
        {
            if (civ.IsAlive && !HasPeople(state, civ.Id))
            {
                civ.IsAlive = false;
                civ.DiedOutMonth = state.Month;
                context.Chronicle($"{civ.Name} has died out.");
            }
        }
    }

    /// <summary>
    /// Growth factor (fixed point, <see cref="Spoilage.One"/> = no change) for a yearly rate in hundredths of a per
    /// mille, compounded over <paramref name="months"/>.
    /// </summary>
    public static long GrowthFactor(long hundredthsPerMillePerYear, int months)
    {
        long perYear = Math.Max(0, 100_000 + hundredthsPerMillePerYear);
        long factor = Spoilage.One;
        for (int y = 0; y < months / 12; y++)
        {
            factor = IntMath.MulDiv(factor, perYear, 100_000);
        }

        int rest = months % 12;
        if (rest > 0)
        {
            factor = IntMath.MulDiv(factor, (12 * 100_000L) + (hundredthsPerMillePerYear * rest), 12 * 100_000L);
        }

        return Math.Max(0, factor);
    }

    private void UpdateHealthAndMood(GameState state, SettlementState s, int people)
    {
        int fed = s.FedPerMille;
        int health = 100 - ((1000 - fed) * _rules.HungerPenaltyPercent / 1000);
        int max = _content.SettlementLevels[s.Level].MaxPopulation;
        if (max > 0 && people > max)
        {
            health -= (int)Math.Min(100, (long)(people - max) * _rules.CrowdingPenaltyPercent / max);
        }

        s.Health = Math.Clamp(health, 0, 100);

        int mood = (fed * 60 / 1000) + (s.HigherNeedsPerMille * 40 / 1000);
        mood += state.Effects.Count == 0
            ? 0
            : state.Effects.Total(_content.Effects, EffectStat.Mood, EffectTarget.Settlement, s.World, s.Id.Value)
                + state.Effects.Total(_content.Effects, EffectStat.Mood, EffectTarget.Civilization, WorldId.Earth, s.Owner.Value);
        s.Mood = Math.Clamp(mood, 0, 100);
    }

    private int NewTotal(GameState state, SettlementState s, int people, int months)
    {
        // People who got no food: most of them die during the turn.
        long unfed = IntMath.MulDiv(people, 1000 - s.FedPerMille, 1000, Rounding.Ceiling);
        long starved = IntMath.MulDiv(unfed, _rules.StarvationDeathPercent, 100, Rounding.Ceiling);
        long survivors = people - starved;

        // Everyone else: births and deaths per year, in hundredths of a per mille.
        long rate = (_rules.BirthsPerMille - _rules.DeathsPerMille) * 100L;
        rate += (s.Mood - 50) * _rules.MoodBirthsPerMillePerPoint.Hundredths;
        int sick = Math.Max(0, _rules.LowHealthBelow - s.Health);
        rate -= sick * _rules.LowHealthDeathsPerMillePerPoint.Hundredths;
        if (state.Effects.Count > 0)
        {
            rate -= state.Effects.Total(_content.Effects, EffectStat.DepopulationPerMillePerYear, EffectTarget.Settlement, s.World, s.Id.Value) * 100L;
        }

        long factor = GrowthFactor(rate, months);
        return (int)Math.Min(int.MaxValue, IntMath.MulDiv(survivors, factor, Spoilage.One));
    }

    // Losses come from every stratum in proportion; newborns join the strata by their target shares.
    private void ChangeTotal(SettlementState s, int before, int after)
    {
        Span<int> people = s.Population;
        if (after == before)
        {
            return;
        }

        if (after < before)
        {
            for (int k = 0; k < people.Length; k++)
            {
                _weights[k] = people[k];
            }
        }
        else
        {
            for (int k = 0; k < people.Length; k++)
            {
                _weights[k] = _content.Strata[k].TargetSharePercent;
            }
        }

        Amount.FromHundredths(Math.Abs(after - before)).SplitProportionally(_weights, _shares);
        int sign = after < before ? -1 : 1;
        for (int k = 0; k < people.Length; k++)
        {
            people[k] = Math.Max(0, people[k] + (sign * (int)_shares[k].Hundredths));
        }
    }

    // People move slowly towards the target mix of strata (GDD 5.1: a few percent per year).
    private void Drift(SettlementState s, int months)
    {
        Span<int> people = s.Population;
        int total = s.TotalPopulation();
        if (total == 0)
        {
            return;
        }

        for (int k = 0; k < _weights.Length; k++)
        {
            _weights[k] = _content.Strata[k].TargetSharePercent;
        }

        Amount.FromHundredths(total).SplitProportionally(_weights, _shares);
        long closePerMille = Math.Min(1000, _rules.StrataShiftPercentPerYear.Hundredths * months / 12 / 10);
        int sum = 0;
        int largest = 0;
        for (int k = 0; k < people.Length; k++)
        {
            _target[k] = (int)_shares[k].Hundredths;
            people[k] += (int)((_target[k] - people[k]) * closePerMille / 1000);
            sum += people[k];
            if (people[k] > people[largest])
            {
                largest = k;
            }
        }

        // Rounding never creates or removes people.
        people[largest] += total - sum;
    }

    private static bool HasPeople(GameState state, CivId civ)
    {
        foreach (SettlementState s in state.Settlements)
        {
            if (s.Owner == civ && s.TotalPopulation() > 0)
            {
                return true;
            }
        }

        return false;
    }
}
