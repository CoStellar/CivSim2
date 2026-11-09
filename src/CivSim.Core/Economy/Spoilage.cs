using CivSim.Core.Numerics;

namespace CivSim.Core.Economy;

/// <summary>
/// Converts a yearly spoilage rate into the share that survives one turn (CLAUDE.md rule 6). Integer only:
/// the kept share is a fixed-point number with <see cref="One"/> = 100%.
/// </summary>
public static class Spoilage
{
    /// <summary>Fixed-point one (100% kept).</summary>
    public const long One = 1_000_000_000;

    /// <summary>
    /// Share of a good kept after <paramref name="months"/> when it loses <paramref name="basisPointsPerYear"/>
    /// (100 = 1%) every year. Whole years compound; a remaining part of a year loses its proportional share.
    /// </summary>
    public static long KeptShare(int basisPointsPerYear, int months)
    {
        long kept = One;
        if (basisPointsPerYear <= 0 || months <= 0)
        {
            return kept;
        }

        long keepPerYear = 10_000 - basisPointsPerYear;
        for (int y = 0; y < months / 12; y++)
        {
            kept = IntMath.MulDiv(kept, keepPerYear, 10_000);
        }

        int rest = months % 12;
        if (rest > 0)
        {
            kept = IntMath.MulDiv(kept, (12 * 10_000) - (basisPointsPerYear * (long)rest), 12 * 10_000);
        }

        return kept;
    }

    /// <summary>What is left of <paramref name="amount"/> after a turn with the kept share <paramref name="keptShare"/>.</summary>
    public static Amount Apply(Amount amount, long keptShare) => amount.MultiplyRatio(keptShare, One, Rounding.Floor);
}
