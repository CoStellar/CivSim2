using System;
using CivSim.Core.Map;
using CivSim.Core.State;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>
/// Copies map chunks into <see cref="MapChunkMessage"/>s. Runs on the simulation thread, so it may read the state.
/// Cells outside the viewer's knowledge are zeroed here, on the server.
/// </summary>
public static class MapViewBuilder
{
    /// <summary>
    /// Builds one chunk. <paramref name="known"/> is null for observers (everything visible), otherwise a
    /// per-cell flag of what the viewer knows. Returns null when the viewer knows nothing in the chunk.
    /// </summary>
    public static MapChunkMessage? Build(WorldState world, int chunk, long turn, bool[]? known)
    {
        ArgumentNullException.ThrowIfNull(world);
        WorldMap map = world.Map;
        HexGlobe geo = map.Geometry;
        (int x0, int y0, int w, int h) = map.Chunks.Bounds(chunk);
        int n = w * h;
        var visibility = new byte[n];
        var elevation = new byte[n];
        var temperature = new sbyte[n];
        var humidity = new byte[n];
        var biome = new byte[n];
        var relief = new byte[n];
        var feature = new byte[n];
        var fertility = new byte[n];
        var deposit = new ushort[n];
        var richness = new int[n];
        var depth = new byte[n];
        var owner = new short[n];
        var river = new byte[n * MapChunkMessage.RiverEdgesPerCell];
        var wild = new ushort[n];
        var influence = new byte[n];
        var seasons = new byte[n];
        var growing = new byte[n];
        bool any = false;
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                int k = (r * w) + c;
                int i = geo.Index(new HexCoord(x0 + c, y0 + r));
                if (known is not null && !known[i])
                {
                    owner[k] = WorldMap.NoOwner;
                    continue;
                }

                any = true;
                visibility[k] = MapChunkMessage.Visible;
                elevation[k] = map.Elevation[i];
                temperature[k] = map.Temperature[i];
                humidity[k] = map.Humidity[i];
                biome[k] = map.Biome[i];
                relief[k] = map.Relief[i];
                feature[k] = map.Feature[i];
                fertility[k] = map.Fertility[i];
                deposit[k] = map.Deposit[i];
                richness[k] = map.DepositRichness[i];
                depth[k] = map.DepositDepth[i];
                owner[k] = map.Owner[i];
                wild[k] = map.WildFood[i];
                influence[k] = map.Influence[i];
                seasons[k] = map.Seasonality[i];
                growing[k] = map.GrowingSeason[i];
                for (int e = 0; e < MapChunkMessage.RiverEdgesPerCell; e++)
                {
                    river[(k * MapChunkMessage.RiverEdgesPerCell) + e] = map.River[(i * HexEdge.PerCell) + e];
                }
            }
        }

        if (!any)
        {
            return null;
        }

        return new MapChunkMessage(
            world.Id.Value, turn, chunk, x0, y0, w, h, visibility, elevation, temperature, humidity, biome, relief, feature,
            fertility, deposit, richness, depth, owner, river, wild, influence, seasons, growing);
    }
}
