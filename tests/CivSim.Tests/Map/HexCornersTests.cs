using System;
using CivSim.Core.Map;
using Xunit;

namespace CivSim.Tests.Map;

public class HexCornersTests
{
    [Theory]
    [InlineData(8, 6)]
    [InlineData(64, 40)]
    public void CornerGraphIsConsistent(int width, int height)
    {
        var geo = new HexTorus(width, height);
        var corners = new HexCorners(geo);
        Assert.Equal(geo.CellCount * 2, corners.Count);
        Span<HexCoord> cells = stackalloc HexCoord[3];
        Span<HexCoord> other = stackalloc HexCoord[3];
        Span<int> nbr = stackalloc int[3];
        Span<HexEdge> edges = stackalloc HexEdge[3];
        Span<int> back = stackalloc int[3];
        Span<HexEdge> backEdges = stackalloc HexEdge[3];
        for (int k = 0; k < corners.Count; k++)
        {
            // The three cells around a corner touch each other.
            corners.Cells(k, cells);
            Assert.Equal(1, geo.Distance(cells[0], cells[1]));
            Assert.Equal(1, geo.Distance(cells[1], cells[2]));
            Assert.Equal(1, geo.Distance(cells[0], cells[2]));

            corners.Neighbors(k, nbr, edges);
            for (int d = 0; d < 3; d++)
            {
                // Neighbouring corners are mutual and the edge between them is the side shared by two cells of both corners.
                corners.Neighbors(nbr[d], back, backEdges);
                Assert.Contains(k, back.ToArray());
                (HexCoord a, HexCoord b) = geo.EdgeCells(edges[d]);
                corners.Cells(nbr[d], other);
                Assert.Contains(a, cells.ToArray());
                Assert.Contains(b, cells.ToArray());
                Assert.Contains(a, other.ToArray());
                Assert.Contains(b, other.ToArray());
            }
        }
    }
}
