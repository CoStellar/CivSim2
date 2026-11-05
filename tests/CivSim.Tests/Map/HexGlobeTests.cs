using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using Xunit;

namespace CivSim.Tests.Map;

public class HexGlobeTests
{
    [Theory]
    [InlineData(5, 6)]
    [InlineData(6, 5)]
    [InlineData(2, 2)]
    [InlineData(8, 6)] // half the width must be odd
    [InlineData(64, 40)]
    public void InvalidSizesAreRejected(int w, int h)
    {
        Assert.Throws<ArgumentException>(() => new HexGlobe(w, h));
    }

    [Fact]
    public void IndexRoundTripsForEveryCell()
    {
        var map = new HexGlobe(66, 40);
        for (int i = 0; i < map.CellCount; i++)
        {
            Assert.Equal(i, map.Index(map.FromIndex(i)));
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Index(new HexCoord(66, 0)));
    }

    [Fact]
    public void WrapGoesAroundTheGlobeAndOverThePoles()
    {
        var map = new HexGlobe(10, 6);
        Assert.Equal(new HexCoord(0, 0), map.Wrap(10, 0));
        Assert.Equal(new HexCoord(9, 3), map.Wrap(-1, 3));

        // One row beyond the north pole is the top row on the other side of the planet.
        Assert.Equal(new HexCoord(7, 0), map.Wrap(2, -1));
        Assert.Equal(new HexCoord(7, 2), map.Wrap(2, -3));

        // The same over the south pole.
        Assert.Equal(new HexCoord(6, 5), map.Wrap(1, 6));

        // Going over the pole twice brings you back.
        Assert.Equal(new HexCoord(3, 2), map.Wrap(3, 2 - (2 * 6)));
    }

    [Fact]
    public void RowsAreEqualAreaLatitudeBands()
    {
        var map = new HexGlobe(10, 8);
        const long one = 1_000_000;
        Assert.Equal(875_000, map.SinLatitude(new HexCoord(0, 0), one)); // 1 − 1/8
        Assert.Equal(125_000, map.SinLatitude(new HexCoord(0, 3), one));
        Assert.Equal(-125_000, map.SinLatitude(new HexCoord(0, 4), one));
        Assert.Equal(750_000, map.SinLatitude(new HexCoord(1, 0), one)); // odd columns sit half a row lower
        Assert.Equal(map.SinLatitude(new HexCoord(2, 1), one), -map.SinLatitude(new HexCoord(2, 6), one));
    }

    [Fact]
    public void AxialRoundTrips()
    {
        var map = new HexGlobe(18, 10);
        for (int i = 0; i < map.CellCount; i++)
        {
            HexCoord c = map.FromIndex(i);
            Assert.Equal(c, map.FromAxial(HexGlobe.ToAxial(c)));
        }
    }

    [Fact]
    public void NeighborsMatchTheOddQLayoutInsideTheMap()
    {
        var map = new HexGlobe(18, 10);
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
    public void NeighborsWrapAroundAndOverThePoles()
    {
        var map = new HexGlobe(10, 6);
        Assert.Equal(new HexCoord(9, 2), map.Neighbor(new HexCoord(0, 3), HexDirection.NW));
        Assert.Equal(new HexCoord(0, 3), map.Neighbor(new HexCoord(9, 2), HexDirection.SE));

        // North of the top row is the top row half a world away, and north from there leads back.
        Assert.Equal(new HexCoord(5, 0), map.Neighbor(new HexCoord(0, 0), HexDirection.N));
        Assert.Equal(new HexCoord(0, 0), map.Neighbor(new HexCoord(5, 0), HexDirection.N));
        Assert.True(map.CrossesPole(new HexCoord(0, 0), HexDirection.N));
        Assert.Equal(HexDirection.N, map.Back(new HexCoord(0, 0), HexDirection.N));
        Assert.Equal(HexDirection.S, map.Back(new HexCoord(0, 3), HexDirection.N));
        Assert.Equal(new HexCoord(8, 5), map.Neighbor(new HexCoord(3, 5), HexDirection.S));
    }

    [Theory]
    [InlineData(6, 4)]
    [InlineData(10, 8)]
    [InlineData(6, 12)]
    [InlineData(18, 10)]
    public void EveryCellHasSixDistinctMutualNeighbors(int w, int h)
    {
        var map = new HexGlobe(w, h);
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
                Assert.Equal(c, map.Neighbor(n[d], map.Back(c, (HexDirection)d)));
            }
        }
    }

    [Theory]
    [InlineData(6, 4)]
    [InlineData(10, 8)]
    [InlineData(6, 12)]
    [InlineData(18, 10)]
    [InlineData(30, 6)]
    public void DistanceEqualsBreadthFirstSearchForAllPairs(int w, int h)
    {
        var map = new HexGlobe(w, h);
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
        var map = new HexGlobe(66, 40);
        Assert.Equal(1, map.Distance(new HexCoord(0, 4), new HexCoord(65, 4)));
        Assert.Equal(1, map.Distance(new HexCoord(5, 0), new HexCoord(38, 0))); // over the pole
        Assert.Equal(39, map.Distance(new HexCoord(5, 0), new HexCoord(5, 39))); // pole to pole is far
        Assert.Equal(map.Distance(new HexCoord(3, 7), new HexCoord(50, 33)), map.Distance(new HexCoord(50, 33), new HexCoord(3, 7)));
    }

    [Fact]
    public void LineIsContinuousAndHasDistancePlusOneCells()
    {
        var map = new HexGlobe(34, 20);
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
        var map = new HexGlobe(10, 6);
        Assert.Equal([new HexCoord(2, 2)], map.Line(new HexCoord(2, 2), new HexCoord(2, 2)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(9)]
    public void RingCellsAreAllAtTheRadiusAndUnique(int radius)
    {
        var map = new HexGlobe(66, 40);
        HexCoord center = new(1, 2); // near the seam on purpose
        var buffer = new HexCoord[HexGlobe.RingSize(radius)];
        int count = map.Ring(center, radius, buffer);

        Assert.Equal(HexGlobe.RingSize(radius), count);
        Assert.Equal(count, new HashSet<HexCoord>(buffer).Count);
        foreach (HexCoord c in buffer)
        {
            Assert.Equal(radius, map.Distance(center, c));
        }
    }

    [Fact]
    public void RangeCoversExactlyTheCellsWithinTheRadius()
    {
        var map = new HexGlobe(66, 40);
        HexCoord center = new(65, 37); // near the south pole and the seam on purpose
        const int radius = 5;
        var buffer = new HexCoord[HexGlobe.RangeSize(radius)];
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
        foreach ((int w, int h) in new[] { (10, 8), (18, 10), (66, 40) })
        {
            var map = new HexGlobe(w, h);
            int r = map.MaxUniqueRadius;
            var buffer = new HexCoord[HexGlobe.RangeSize(r)];
            for (int i = 0; i < map.CellCount; i++)
            {
                int count = map.Range(map.FromIndex(i), r, buffer);
                Assert.Equal(count, new HashSet<HexCoord>(buffer).Count);
            }
        }
    }

    [Fact]
    public void EdgesAreSharedByBothCellsAndIndexedUniquely()
    {
        var map = new HexGlobe(10, 6);
        var indexes = new HashSet<int>();
        for (int i = 0; i < map.CellCount; i++)
        {
            HexCoord c = map.FromIndex(i);
            for (int d = 0; d < 6; d++)
            {
                var dir = (HexDirection)d;
                HexEdge fromHere = map.Edge(c, dir);
                HexEdge fromThere = map.Edge(map.Neighbor(c, dir), map.Back(c, dir));
                Assert.Equal(fromHere, fromThere);

                (HexCoord x, HexCoord y) = map.EdgeCells(fromHere);
                Assert.True((x == c && y == map.Neighbor(c, dir)) || (y == c && x == map.Neighbor(c, dir)));
                indexes.Add(map.EdgeIndex(fromHere));
            }
        }

        Assert.Equal(map.EdgeCount, indexes.Count);
    }
}
