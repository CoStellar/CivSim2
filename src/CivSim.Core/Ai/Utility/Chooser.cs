using System;
using CivSim.Core.Random;

namespace CivSim.Core.Ai.Utility;

/// <summary>
/// Keeps the best few evaluated actions and picks one (GDD 8.3: the AI draws among its 3 best actions with chances by
/// score). Only actions scoring above zero take part. Ties keep the action offered first, so the result depends only on
/// the order of offers. With room for one action it returns the best and draws no random number. Reused between
/// decisions: no allocation.
/// </summary>
public sealed class Chooser
{
    /// <summary>Most actions a chooser can keep.</summary>
    public const int MaxRoom = 3;

    private readonly int[] _keys = new int[MaxRoom];
    private readonly Evaluation[] _evaluations = new Evaluation[MaxRoom];

    /// <summary>Creates a chooser keeping the best <paramref name="room"/> actions (1-3).</summary>
    public Chooser(int room = 1)
    {
        Room = room is >= 1 and <= MaxRoom ? room : throw new ArgumentOutOfRangeException(nameof(room));
    }

    /// <summary>How many of the best actions take part in the draw.</summary>
    public int Room { get; }

    /// <summary>Actions kept so far.</summary>
    public int Count { get; private set; }

    /// <summary>Lowest score an offer must beat to be kept: the worst kept score once full, else 0.</summary>
    public long Threshold => Count < Room ? 0 : _evaluations[Count - 1].Value;

    /// <summary>Forgets every action.</summary>
    public void Clear() => Count = 0;

    /// <summary>True when an action scoring <paramref name="score"/> would be kept (to skip costly checks early).</summary>
    public bool WouldKeep(long score) => score > Threshold;

    /// <summary>Offers an action identified by <paramref name="key"/>. Returns true when it was kept.</summary>
    public bool Offer(int key, in Evaluation evaluation)
    {
        if (!WouldKeep(evaluation.Value))
        {
            return false;
        }

        int last = Math.Min(Count, Room - 1);
        int at = last;
        while (at > 0 && evaluation.Value > _evaluations[at - 1].Value)
        {
            at--;
        }

        for (int k = last; k > at; k--)
        {
            _keys[k] = _keys[k - 1];
            _evaluations[k] = _evaluations[k - 1];
        }

        _keys[at] = key;
        _evaluations[at] = evaluation;
        Count = Math.Min(Count + 1, Room);
        return true;
    }

    /// <summary>Key of the kept action <paramref name="rank"/> (0 = best).</summary>
    public int KeyAt(int rank) => _keys[Checked(rank)];

    /// <summary>Evaluation of the kept action <paramref name="rank"/> (0 = best).</summary>
    public ref readonly Evaluation EvaluationAt(int rank) => ref _evaluations[Checked(rank)];

    /// <summary>
    /// Rank of the chosen action, or -1 when none was kept. With one kept action, or no generator, the best; otherwise
    /// a draw weighted by score from <paramref name="random"/>.
    /// </summary>
    public int Pick(DeterministicRandom? random)
    {
        if (Count == 0)
        {
            return -1;
        }

        if (Count == 1 || random is null)
        {
            return 0;
        }

        // Weights relative to the best, so large base values never overflow the draw.
        Span<int> weights = stackalloc int[MaxRoom];
        long best = _evaluations[0].Value;
        int total = 0;
        for (int k = 0; k < Count; k++)
        {
            weights[k] = (int)Math.Max(1, (long)((Int128)_evaluations[k].Value * 1000 / best));
            total += weights[k];
        }

        int roll = random.NextInt(total);
        for (int k = 0; k < Count; k++)
        {
            roll -= weights[k];
            if (roll < 0)
            {
                return k;
            }
        }

        return Count - 1;
    }

    private int Checked(int rank) => (uint)rank < (uint)Count ? rank : throw new ArgumentOutOfRangeException(nameof(rank));
}
