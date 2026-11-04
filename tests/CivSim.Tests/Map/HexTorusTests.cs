using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using Xunit;

namespace CivSim.Tests.Map;

public class HexTorusTests
{
    [Theory]
    [InlineData(5, 6)]
    [InlineData(6, 5)]
    [InlineData(2, 2)]
    public void InvalidSizesAreRejected(int w, int h)
    {
        Assert.Throws<ArgumentException>(() => new HexTorus(w, h));
    }

    [Fact]
    public void IndexRoundTripsForEveryCell()
    {
        var map = new HexTorus(64, 40);
        for (int i = 0; i < map.CellCount; i++)
        {
            Assert.Equal(i, map.Index(map.FromIndex(i)));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Index(new HexCoord(64, 0)));
    }

    [Fact]
    public void WrapWorksInBothDirections()
    {
        var map = new HexTorus(8, 6);
        Assert.Equal(new HexCoord(0, 0), map.Wrap(8, 6));
        Assert.Equal(new HexCoord(7, 5), map.Wrap(-1, -1));
        Assert.Equal(new HexCoord(1, 2), map.Wrap(17, -10));
    }

    [Fact]
    public void AxialRoundTrips()
    {
        var map = new HexTorus(16, 10);
        for (int i = 0; i < map.CellCount; i++)
        {
            HexCoord c = map.FromIndex(i);
            Assert.Equal(c, map.FromAxial(HexTorus.ToAxial(c)));
        }
    }

    [Fact]
    public void NeighborsMatchTheOddQLayoutInsideTheMap()
    {
        var map = new HexTorus(16, 10);
        // Even column: neighbours to the sides are on the same and the previous row.
        Assert.Equal(new HexCoord(4, 4), map.Neighbor(new HexCoord(4, 5), HexDirection.N));
        Assert.Equal(new HexCoord(5, 4), map.Neighbor(new HexCoord(4, 5), HexDirection.NE));
        Assert.Equal(new HexCoord(5, 5), map.Neighbor(new HexCoord(4, 5), HexDirection.SE));
        Assert.Equal(new HexCoord(4, 6), map.Neighbor(new HexCoord(4, 5), HexDirection.S));
        Assert.Equal(new HexCoord(3, 5), map.Neighbor(new HexCoord(4, 5), HexDirection.SW));
        Assert.Equal(new HexCoord(3, 4), map.Neighbor(new HexCoord(4, 5), HexDirection.NW));

        // Odd column (shifted down): side neighbours are on the same and the next row.
        Assert.Equal(new HexCoord(6, 5), map.Neighbor(new HexCoord(5, 5), HexDirection.NE));
        Assert.Equal(new HexCoord(6, 6), map.Neighbor(new HexCoord(5, 5), HexDirection.SE));
        Assert.Equal(new HexCoord(4, 6), map.Neighbor(new HexCoord(5, 5), HexDirection.SW));
        Assert.Equal(new HexCoord(4, 5), map.Neighbor(new HexCoord(5, 5), HexDirection.NW));
    }

    [Fact]
    public void NeighborsWrapAcrossTheSeams()
    {
        var map = new HexTorus(8, 6);
        Assert.Equal(new HexCoord(0, 5), map.Neighbor(new HexCoord(0, 0), HexDirection.N));
        Assert.Equal(new HexCoord(7, 5), map.Neighbor(new HexCoord(0, 0), HexDirection.NW));
        Assert.Equal(new HexCoord(0, 0), map.Neighbor(new HexCoord(7, 5), HexDirection.SE));
        Assert.Equal(new HexCoord(3, 0), map.Neighbor(new HexCoord(3, 5), HexDirection.S));
    }

    [Theory]
    [InlineData(8, 6)]
    [InlineData(10, 8)]
    [InlineData(6, 12)]
    [InlineData(16, 10)]
    public void EveryCellHasSixDistinctMutualNeighbors(int w, int h)
    {
        var map = new HexTorus(w, h);
        Span<HexCoord> n = stackalloc HexCoord[6];
        for (int i = 0; i < map.CellCount; i++)
        {
            HexCoord c = map.FromIndex(i);
            map.GetNeighbors(c, n);
            var set = new HashSet<HexCoord>(n.ToArray());
            Assert.Equal(6, set.Count);
            Assert.DoesNotContain(c, set);
            for (int d = 0; d < 6; d++)
            {
                Assert.Equal(c, map.Neighbor(n[d], ((HexDirection)d).Opposite()));
            }
        }
    }

    [Theory]
    [InlineData(8, 6)]
    [InlineData(10, 8)]
    [InlineData(6, 12)]
    [InlineData(16, 10)]
    [InlineData(4, 4)]
    public void DistanceEqualsBreadthFirstSearchForAllPairs(int w, int h)
    {
        var map = new HexTorus(w, h);
        Span<HexCoord> n = stackalloc HexCoord[6];
        var dist = new int[map.CellCount];
        var queue = new Queue<int>();
        for (int s = 0; s < map.CellCount; s++)
        {
            Array.Fill(dist, -1);
            dist[s] = 0;
            queue.Enqueue(s);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                map.GetNeighbors(map.FromIndex(cur), n);
                foreach (HexCoord next in n)
                {
                    int ni = map.Index(next);
                    if (dist[ni] < 0)
                    {
                        dist[ni] = dist[cur] + 1;
                        queue.Enqueue(ni);
                    }
                }
            }

            HexCoord a = map.FromIndex(s);
            for (int t = 0; t < map.CellCount; t++)
            {
                Assert.Equal(dist[t], map.Distance(a, map.FromIndex(t)));
            }
        }
    }

    [Fact]
    public void DistanceIsSymmetricAndUsesTheShortWayRound()
    {
        var map = new HexTorus(64, 40);
        Assert.Equal(1, map.Distance(new HexCoord(0, 0), new HexCoord(63, 0)));
        Assert.Equal(1, map.Distance(new HexCoord(5, 0), new HexCoord(5, 39)));
        Assert.Equal(map.Distance(new HexCoord(3, 7), new HexCoord(50, 33)), map.Distance(new HexCoord(50, 33), new HexCoord(3, 7)));
    }

    [Fact]
    public void LineIsContinuousAndHasDistancePlusOneCells()
    {
        var map = new HexTorus(32, 20);
        var rng = new Core.Random.DeterministicRandom(1);
        var buffer = new HexCoord[64];
        for (int t = 0; t < 2000; t++)
        {
            HexCoord a = map.FromIndex(rng.NextInt(map.CellCount));
            HexCoord b = map.FromIndex(rng.NextInt(map.CellCount));
            int count = map.Line(a, b, buffer);

            Assert.Equal(map.Distance(a, b) + 1, count);
            Assert.Equal(a, buffer[0]);
            Assert.Equal(b, buffer[count - 1]);
            for (int i = 1; i < count; i++)
            {
                Assert.Equal(1, map.Distance(buffer[i - 1], buffer[i]));
                Assert.Equal(i, map.Distance(a, buffer[i]));
            }
        }
    }

    [Fact]
    public void LineOfACellToItselfIsOneCell()
    {
        var map = new HexTorus(8, 6);
        Assert.Equal([new HexCoord(2, 2)], map.Line(new HexCoord(2, 2), new HexCoord(2, 2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void RingCellsAreAllAtTheRadiusAndUnique(int radius)
    {
        var map = new HexTorus(64, 40);
        HexCoord center = new(1, 2); // near the seam on purpose
        var buffer = new HexCoord[HexTorus.RingSize(radius)];
        int count = map.Ring(center, radius, buffer);

        Assert.Equal(HexTorus.RingSize(radius), count);
        Assert.Equal(count, new HashSet<HexCoord>(buffer).Count);
        foreach (HexCoord c in buffer)
        {
            Assert.Equal(radius, map.Distance(center, c));
        }
    }

    [Fact]
    public void RangeCoversExactlyTheCellsWithinTheRadius()
    {
        var map = new HexTorus(64, 40);
        HexCoord center = new(63, 39);
        const int radius = 5;
        var buffer = new HexCoord[HexTorus.RangeSize(radius)];
        int count = map.Range(center, radius, buffer);
        var set = new HashSet<HexCoord>(buffer[..count]);

        Assert.Equal(91, count);
        Assert.Equal(count, set.Count);
        for (int i = 0; i < map.CellCount; i++)
        {
            HexCoord c = map.FromIndex(i);
            Assert.Equal(map.Distance(center, c) <= radius, set.Contains(c));
        }
    }

    [Fact]
    public void MaxUniqueRadiusIsReallyUnique()
    {
        var map = new HexTorus(10, 8);
        int r = map.MaxUniqueRadius;
        var buffer = new HexCoord[HexTorus.RangeSize(r)];
        int count = map.Range(new HexCoord(0, 0), r, buffer);
        Assert.Equal(count, new HashSet<HexCoord>(buffer).Count);
    }

    [Fact]
    public void EdgesAreSharedByBothCellsAndIndexedUniquely()
    {
        var map = new HexTorus(8, 6);
        var indexes = new HashSet<int>();
        for (int i = 0; i < map.CellCount; i++)
        {
            HexCoord c = map.FromIndex(i);
            for (int d = 0; d < 6; d++)
            {
                var dir = (HexDirection)d;
                HexEdge fromHere = map.Edge(c, dir);
                HexEdge fromThere = map.Edge(map.Neighbor(c, dir), dir.Opposite());
                Assert.Equal(fromHere, fromThere);

                (HexCoord x, HexCoord y) = map.EdgeCells(fromHere);
                Assert.True((x == c && y == map.Neighbor(c, dir)) || (y == c && x == map.Neighbor(c, dir)));
                indexes.Add(map.EdgeIndex(fromHere));
            }
        }

        Assert.Equal(map.EdgeCount, indexes.Count);
    }
}
