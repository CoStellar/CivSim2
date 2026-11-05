using System;
using System.Runtime.CompilerServices;

namespace CivSim.Core.Ai.Utility;

/// <summary>
/// The score of one possible action (GDD 8.3): a base value in the action's own units (food, value added...) times
/// every factor, times the goal weight and the character multiplier, minus the cost. Factors are applied one after
/// another with integer division (value × per mille / 1000), so the result is the same on every machine, and a factor
/// equal to zero rules the action out. Every factor is kept (up to <see cref="Capacity"/>) so the decision can say why.
/// A plain struct with inline storage: evaluating thousands of cells allocates nothing.
/// </summary>
public struct Evaluation
{
    /// <summary>Most factors one evaluation keeps; more are still applied but not recorded.</summary>
    public const int Capacity = 8;

    private FactorBuffer _factors;

    /// <summary>Starts an evaluation from a base value.</summary>
    public static Evaluation Of(long baseValue) => new() { Value = baseValue, Base = baseValue };

    /// <summary>The base value the evaluation started from.</summary>
    public long Base { get; private set; }

    /// <summary>The score so far.</summary>
    public long Value { get; private set; }

    /// <summary>Total cost subtracted.</summary>
    public long Cost { get; private set; }

    /// <summary>Recorded factors.</summary>
    public int FactorCount { get; private set; }

    /// <summary>Factor <paramref name="index"/> in the order applied.</summary>
    public readonly Factor this[int index] =>
        (uint)index < (uint)FactorCount ? _factors[index] : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Multiplies the score by <paramref name="perMille"/>/1000 and records the factor.</summary>
    public void Apply(FactorId id, int perMille, long input = 0)
    {
        if (perMille < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(perMille), "A factor is 0 or more; use Note to explain without scoring.");
        }

        Value = Value * perMille / Factor.Neutral;
        Record(new Factor(id, perMille, input));
    }

    /// <summary>Applies a factor read from a response curve.</summary>
    public void Apply(FactorId id, ResponseCurve curve, long input) => Apply(id, curve.Evaluate(input), input);

    /// <summary>
    /// The goal weight from the strategist and the character multiplier (GDD 8.3). Both are 1000 (neutral) until
    /// stage 4b gives civilizations goals and character; neutral values leave the score exactly as it was.
    /// </summary>
    public void Weigh(int goalWeight, int character)
    {
        if (goalWeight != Factor.Neutral)
        {
            Apply(FactorId.GoalWeight, goalWeight);
        }

        if (character != Factor.Neutral)
        {
            Apply(FactorId.Character, character);
        }
    }

    /// <summary>Records a world value that explains a rule-based choice without changing the score.</summary>
    public void Note(FactorId id, long input) => Record(new Factor(id, Factor.NoteOnly, input));

    /// <summary>Subtracts a cost (GDD 8.3: "− koszt").</summary>
    public void Subtract(FactorId id, long cost)
    {
        Value -= cost;
        Cost += cost;
        Record(new Factor(id, Factor.NoteOnly, cost));
    }

    /// <summary>
    /// The factors that shaped the score most (farthest from neutral, then notes, then neutral factors; on ties the one
    /// applied first),
    /// written into <paramref name="top"/>. Returns how many were written.
    /// </summary>
    public readonly int Top(Span<Factor> top)
    {
        int n = Math.Min(top.Length, FactorCount);
        Span<bool> used = stackalloc bool[Capacity];
        for (int t = 0; t < n; t++)
        {
            int pick = -1;
            for (int k = 0; k < FactorCount; k++)
            {
                if (!used[k] && (pick < 0 || _factors[k].Weight > _factors[pick].Weight))
                {
                    pick = k;
                }
            }

            used[pick] = true;
            top[t] = _factors[pick];
        }

        return n;
    }

    private void Record(Factor factor)
    {
        if (FactorCount < Capacity)
        {
            _factors[FactorCount++] = factor;
        }
    }

    [InlineArray(Capacity)]
    private struct FactorBuffer
    {
        private Factor _first;
    }
}
