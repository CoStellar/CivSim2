using System;
using System.Collections.Generic;
using CivSim.Core.Random;
using Xunit;

namespace CivSim.Tests.Random;

public class DeterministicRandomTests
{
    [Fact]
    public void SplitMix64MatchesReferenceVector()
    {
        ulong state = 0;
        Assert.Equal(0xE220A8397B1DCDAFUL, SplitMix64.Next(ref state));
        Assert.Equal(0x6E789E6AA1B965F4UL, SplitMix64.Next(ref state));
        Assert.Equal(0x06C45D188009454FUL, SplitMix64.Next(ref state));
    }

    [Fact]
    public void Xoshiro256StarStarMatchesReferenceVector()
    {
        var rng = new DeterministicRandom(new RandomState(1, 2, 3, 4));
        Assert.Equal(11520UL, rng.NextULong());
        Assert.Equal(0UL, rng.NextULong());
        Assert.Equal(1509978240UL, rng.NextULong());
        Assert.Equal(1215971899390074240UL, rng.NextULong());
    }

    [Fact]
    public void SameSeedGivesSameSequence()
    {
        var a = new DeterministicRandom(12345);
        var b = new DeterministicRandom(12345);
        for (int i = 0; i < 10_000; i++)
        {
            Assert.Equal(a.NextULong(), b.NextULong());
        }
    }

    [Fact]
    public void DifferentSeedsGiveDifferentSequences()
    {
        var a = new DeterministicRandom(1);
        var b = new DeterministicRandom(2);
        int equal = 0;
        for (int i = 0; i < 1000; i++)
        {
            if (a.NextULong() == b.NextULong())
            {
                equal++;
            }
        }

        Assert.Equal(0, equal);
    }

    [Fact]
    public void RestoredStateContinuesTheSameSequence()
    {
        var a = new DeterministicRandom(99);
        for (int i = 0; i < 37; i++)
        {
            a.NextULong();
        }

        var b = new DeterministicRandom(a.State);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextULong(), b.NextULong());
        }
    }

    [Fact]
    public void ZeroStateIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new DeterministicRandom(new RandomState(0, 0, 0, 0)));
    }

    [Fact]
    public void NextIntStaysInRange()
    {
        var rng = new DeterministicRandom(7);
        for (int i = 0; i < 100_000; i++)
        {
            int v = rng.NextInt(-5, 6);
            Assert.InRange(v, -5, 5);
        }

        Assert.Equal(0, rng.NextInt(1));
        Assert.InRange(rng.NextInt(int.MinValue, int.MaxValue), int.MinValue, int.MaxValue - 1);
    }

    [Fact]
    public void NextIntRejectsBadRanges()
    {
        var rng = new DeterministicRandom(7);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
    }

    [Fact]
    public void NextIntIsUniform()
    {
        // Chi-square test over 10 buckets; 9 degrees of freedom, critical value at p = 0.001 is 27.88.
        const int buckets = 10;
        const int samples = 200_000;
        var counts = new int[buckets];
        var rng = new DeterministicRandom(2026);
        for (int i = 0; i < samples; i++)
        {
            counts[rng.NextInt(buckets)]++;
        }

        double expected = samples / (double)buckets;
        double chi2 = 0;
        foreach (int c in counts)
        {
            chi2 += (c - expected) * (c - expected) / expected;
        }

        Assert.True(chi2 < 27.88, $"chi-square {chi2:F2} too high");
    }

    [Fact]
    public void ChanceMatchesProbability()
    {
        var rng = new DeterministicRandom(5);
        int hits = 0;
        const int samples = 100_000;
        for (int i = 0; i < samples; i++)
        {
            if (rng.ChancePercent(30))
            {
                hits++;
            }
        }

        Assert.InRange(hits, 29_400, 30_600);
        Assert.False(rng.Chance(0, 10));
        Assert.True(rng.Chance(10, 10));
    }

    [Fact]
    public void ShuffleIsAPermutationAndDeterministic()
    {
        int[] a = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        int[] b = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        new DeterministicRandom(3).Shuffle<int>(a);
        new DeterministicRandom(3).Shuffle<int>(b);
        Assert.Equal(a, b);
        Assert.Equal(10, new HashSet<int>(a).Count);
        Assert.NotEqual([0, 1, 2, 3, 4, 5, 6, 7, 8, 9], a);
    }
}
