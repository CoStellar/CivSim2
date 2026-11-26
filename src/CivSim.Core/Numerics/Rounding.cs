namespace CivSim.Core.Numerics;

/// <summary>How integer division results are rounded. All modes are exact and platform-independent.</summary>
public enum Rounding
{
    /// <summary>Round to nearest; ties go away from zero (2.5 → 3, -2.5 → -3). Default for the economy.</summary>
    HalfAwayFromZero = 0,

    /// <summary>Round toward negative infinity.</summary>
    Floor = 1,

    /// <summary>Round toward positive infinity.</summary>
    Ceiling = 2,

    /// <summary>Drop the fraction (round toward zero).</summary>
    TowardZero = 3,
}
