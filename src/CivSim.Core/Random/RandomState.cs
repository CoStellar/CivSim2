namespace CivSim.Core.Random;

/// <summary>The full 256-bit state of a <see cref="DeterministicRandom"/>, for saving and hashing.</summary>
public readonly record struct RandomState(ulong S0, ulong S1, ulong S2, ulong S3)
{
    /// <summary>True when every word is zero, which is not a valid xoshiro state.</summary>
    public bool IsZero => (S0 | S1 | S2 | S3) == 0;
}
