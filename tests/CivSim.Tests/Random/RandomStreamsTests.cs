using System;
using CivSim.Core.Random;
using Xunit;

namespace CivSim.Tests.Random;

public class RandomStreamsTests
{
    [Fact]
    public void StreamsAreIndependent()
    {
        // Drawing from one stream must not change what another stream returns.
        var a = new RandomStreams(42);
        var b = new RandomStreams(42);
        for (int i = 0; i < 500; i++)
        {
            b[RandomStreamId.Combat].NextULong();
        }

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(a[RandomStreamId.Events].NextULong(), b[RandomStreamId.Events].NextULong());
        }
    }

    [Fact]
    public void StreamsProduceDifferentSequences()
    {
        var streams = new RandomStreams(42);
        ulong[] first = new ulong[RandomStreams.Count];
        for (int i = 0; i < RandomStreams.Count; i++)
        {
            first[i] = streams[(RandomStreamId)i].NextULong();
        }

        Assert.Equal(RandomStreams.Count, new System.Collections.Generic.HashSet<ulong>(first).Count);
    }

    [Fact]
    public void StreamsAreUncorrelated()
    {
        // Pearson correlation of paired outputs should be near zero.
        var streams = new RandomStreams(7);
        const int n = 20_000;
        double sumX = 0, sumY = 0, sumXy = 0, sumX2 = 0, sumY2 = 0;
        for (int i = 0; i < n; i++)
        {
            double x = streams[RandomStreamId.World].NextInt(1000);
            double y = streams[RandomStreamId.Climate].NextInt(1000);
            sumX += x;
            sumY += y;
            sumXy += x * y;
            sumX2 += x * x;
            sumY2 += y * y;
        }

        double r = ((n * sumXy) - (sumX * sumY)) / Math.Sqrt(((n * sumX2) - (sumX * sumX)) * ((n * sumY2) - (sumY * sumY)));
        Assert.InRange(r, -0.03, 0.03);
    }

    [Fact]
    public void DerivedGeneratorDependsOnlyOnKeys()
    {
        var a = new RandomStreams(1);
        var b = new RandomStreams(1);
        b[RandomStreamId.Ai].NextULong();

        Assert.Equal(a.Derive(RandomStreamId.Ai, 3, 10).NextULong(), b.Derive(RandomStreamId.Ai, 3, 10).NextULong());
        Assert.NotEqual(a.Derive(RandomStreamId.Ai, 3, 10).NextULong(), a.Derive(RandomStreamId.Ai, 4, 10).NextULong());
        Assert.NotEqual(a.Derive(RandomStreamId.Ai, 3, 10).NextULong(), a.Derive(RandomStreamId.Ai, 3, 11).NextULong());
        Assert.NotEqual(a.Derive(RandomStreamId.Ai, 3, 10).NextULong(), a.Derive(RandomStreamId.Combat, 3, 10).NextULong());
    }

    [Fact]
    public void StatesRoundTrip()
    {
        var a = new RandomStreams(9);
        a[RandomStreamId.Population].NextULong();
        Span<RandomState> states = stackalloc RandomState[RandomStreams.Count];
        a.CopyStates(states);

        var b = new RandomStreams(123);
        b.RestoreStates(states);
        for (int i = 0; i < RandomStreams.Count; i++)
        {
            Assert.Equal(a[(RandomStreamId)i].NextULong(), b[(RandomStreamId)i].NextULong());
        }
    }

    [Fact]
    public void FirstValuesAreStableAcrossVersions()
    {
        // Golden values: if this test fails, the stream derivation changed and old saves/seeds no longer replay.
        var streams = new RandomStreams(2026);
        ulong world = streams[RandomStreamId.World].NextULong();
        ulong events = streams[RandomStreamId.Events].NextULong();
        Assert.Equal(GoldenWorld, world);
        Assert.Equal(GoldenEvents, events);
    }

    private const ulong GoldenWorld = 12773398452111492464UL;
    private const ulong GoldenEvents = 1794056029472796915UL;
}
