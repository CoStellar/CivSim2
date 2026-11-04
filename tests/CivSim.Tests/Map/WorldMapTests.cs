using System.Collections.Generic;
using System.IO;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Map;

public class WorldMapTests
{
    [Theory]
    [InlineData(64, 40, 2, 2)]
    [InlineData(256, 160, 8, 5)]
    [InlineData(512, 320, 16, 10)]
    [InlineData(384, 240, 12, 8)]
    public void ChunkGridCoversTheMap(int w, int h, int cx, int cy)
    {
        var chunks = new MapChunks(new HexTorus(w, h));
        Assert.Equal(cx, chunks.ChunksX);
        Assert.Equal(cy, chunks.ChunksY);

        // Every cell belongs to exactly the chunk whose bounds contain it.
        var cellsPerChunk = new int[chunks.Count];
        for (int i = 0; i < w * h; i++)
        {
            int c = chunks.ChunkOfIndex(i);
            Assert.Equal(c, chunks.ChunkOf(chunks.Geometry.FromIndex(i)));
            cellsPerChunk[c]++;
        }

        for (int c = 0; c < chunks.Count; c++)
        {
            (_, _, int bw, int bh) = chunks.Bounds(c);
            Assert.Equal(bw * bh, cellsPerChunk[c]);
        }
    }

    [Fact]
    public void BorderChunksArePartialWhenSizeIsNotAMultipleOf32()
    {
        var chunks = new MapChunks(new HexTorus(64, 40));
        Assert.Equal((0, 32, 32, 8), chunks.Bounds(2));
    }

    [Fact]
    public void DirtySetTracksAndListsInOrder()
    {
        var dirty = new DirtyChunks(130);
        Assert.True(dirty.IsEmpty);
        dirty.Mark(129);
        dirty.Mark(3);
        dirty.Mark(64);
        dirty.Mark(3);

        var list = new List<int>();
        dirty.CopyTo(list);
        Assert.Equal([3, 64, 129], list);
        Assert.Equal(3, dirty.DirtyCount);

        dirty.Clear();
        Assert.True(dirty.IsEmpty);
        dirty.MarkAll();
        Assert.Equal(130, dirty.DirtyCount);
    }

    [Fact]
    public void SettingAValueMarksOnlyItsChunkAndOnlyWhenItChanges()
    {
        var map = new WorldMap(new HexTorus(64, 40));
        map.Dirty.Clear();

        map.Elevation.Set(0, 0); // unchanged
        Assert.True(map.Dirty.IsEmpty);

        int cell = map.Geometry.Index(new HexCoord(40, 35)); // chunk 3
        map.Elevation.Set(cell, 120);
        Assert.Equal(1, map.Dirty.DirtyCount);
        Assert.True(map.Dirty.IsDirty(3));

        map.Dirty.Clear();
        map.River.Set((cell * HexEdge.PerCell) + 2, 1); // edge layer marks the owning cell's chunk
        Assert.True(map.Dirty.IsDirty(3));
    }

    [Fact]
    public void NewMapStartsAllDirtyWithNoOwners()
    {
        var map = new WorldMap(new HexTorus(64, 40));
        Assert.Equal(map.Chunks.Count, map.Dirty.DirtyCount);
        Assert.Equal(WorldMap.NoOwner, map.Owner[123]);
    }

    [Fact]
    public void SnapshotListsChangedChunksAndTheTurnClearsThem()
    {
        var runner = new TurnRunner(GameState.CreateNew(GameSettings.Test(1)));
        runner.RunTurn();
        Assert.Equal([0, 1, 2, 3], runner.Snapshots.Latest!.Worlds[0].ChangedChunks.AsSpan().ToArray());

        runner.RunTurn();
        Assert.Empty(runner.Snapshots.Latest!.Worlds[0].ChangedChunks);
    }

    [Fact]
    public void MapIsPartOfTheHashAndOfTheSave()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(4));
        ulong before = GameStateHash.Compute(state);
        WorldMap map = state.Worlds[0].Map;
        map.Elevation.Set(77, 200);
        map.Owner.Set(78, 2);
        map.River.Set(5, 3);
        ulong after = GameStateHash.Compute(state);
        Assert.NotEqual(before, after);

        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);

        Assert.Equal(after, GameStateHash.Compute(loaded));
        Assert.Equal(200, loaded.Worlds[0].Map.Elevation[77]);
        Assert.Equal(2, loaded.Worlds[0].Map.Owner[78]);
    }
}
