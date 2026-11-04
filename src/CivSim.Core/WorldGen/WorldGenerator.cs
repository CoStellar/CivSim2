using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Random;

namespace CivSim.Core.WorldGen;

/// <summary>
/// Builds a world from the seed in the order of GDD 3: land, sea level, temperature, humidity, biomes, rivers
/// and lakes, features and fertility, deposits, start positions. Integer math only; the only randomness comes
/// from the seed and the <see cref="RandomStreamId.World"/> stream, so the same seed gives the same map everywhere.
/// </summary>
public sealed class WorldGenerator
{
    /// <summary>Elevation of the sea surface (GDD 3: "poziom morza ok. 100").</summary>
    public const byte SeaLevel = 100;

    /// <summary>Radius used for start position scores (GDD 3, step 10).</summary>
    public const int StartRadius = 3;

    private readonly GameContent _content;
    private readonly WorldMap _map;
    private readonly HexTorus _geo;
    private readonly DeterministicRandom _rng;
    private readonly ulong _seed;
    private readonly WorldGenOptions _options;
    private readonly HexCoord[] _n6 = new HexCoord[6];
    private readonly int _deepId;
    private readonly int _shallowId;
    private readonly int _coastId;
    private readonly int _lakeId;
    private readonly int _grasslandId;
    private bool[] _water = [];

    private WorldGenerator(GameContent content, WorldMap map, DeterministicRandom rng, ulong seed, WorldGenOptions options)
    {
        _content = content;
        _map = map;
        _geo = map.Geometry;
        _rng = rng;
        _seed = seed;
        _options = options;
        _deepId = content.WaterBiome(BiomeKind.DeepOcean).Id;
        _shallowId = content.WaterBiome(BiomeKind.ShallowSea).Id;
        _coastId = content.WaterBiome(BiomeKind.Coast).Id;
        _lakeId = content.WaterBiome(BiomeKind.Lake).Id;
        _grasslandId = content.Biome("grassland").Id;
    }

    /// <summary>Fills <paramref name="map"/> and returns a report. <paramref name="rng"/> should be the World stream.</summary>
    public static WorldGenReport Generate(GameContent content, WorldMap map, DeterministicRandom rng, ulong seed, WorldGenOptions options)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(options);

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        var gen = new WorldGenerator(content, map, rng, seed, options);
        var report = new WorldGenReport();
        gen.Run(report);
        map.MarkAllDirty();
        report.ElapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return report;
    }

    private void Run(WorldGenReport report)
    {
        Elevation();
        Relief();
        Temperature();
        int[] distToWater = DistanceTo(isTarget: i => _water[i]);
        Humidity(distToWater);
        Biomes();
        Rivers(report);
        FeaturesAndFertility();
        Deposits();
        StartPositions(report);
        FillReport(report);
    }

    // ---- 1–2. Land and sea level -------------------------------------------------------------------------------

    private void Elevation()
    {
        int w = _geo.Width;
        int h = _geo.Height;
        int baseCellsX = _options.WorldType switch
        {
            WorldType.Pangea => 2,
            WorldType.Archipelago => 10,
            _ => 5,
        };

        int octaves = 5;
        var raw = new int[_geo.CellCount];
        long totalWeight = 0;
        for (int o = 0; o < octaves; o++)
        {
            totalWeight += 16 >> o;
        }

        for (int i = 0; i < raw.Length; i++)
        {
            HexCoord c = _geo.FromIndex(i);
            long sum = 0;
            for (int o = 0; o < octaves; o++)
            {
                int cellsX = Math.Max(1, baseCellsX << o);
                int cellsY = Math.Max(1, (int)(((long)cellsX * h * 1155 + (w * 500L)) / (w * 1000L)));
                (long x, long y) = Position(c, cellsX, cellsY);
                sum += (long)IntNoise.Sample(_seed, o, cellsX, cellsY, x, y) * (16 >> o);
            }

            raw[i] = (int)(sum / totalWeight);
        }

        // Archipelagos: sharpen the noise so land breaks into islands.
        if (_options.WorldType == WorldType.Archipelago)
        {
            for (int i = 0; i < raw.Length; i++)
            {
                long v = raw[i] - 32768;
                raw[i] = (int)Math.Clamp(32768 + (v * 3 / 2), 0, 65535);
            }
        }

        int threshold = Percentile(raw, _options.WaterPercent);
        _water = new bool[raw.Length];
        Span<byte> elevation = _map.Elevation.Raw;
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] < threshold)
            {
                _water[i] = true;
                elevation[i] = (byte)(raw[i] * (SeaLevel - 1) / Math.Max(1, threshold));
            }
            else
            {
                elevation[i] = (byte)(SeaLevel + ((long)(raw[i] - threshold) * (255 - SeaLevel) / Math.Max(1, 65535 - threshold)));
            }
        }
    }

    // Value below which `percent`% of the cells lie (exact, via a histogram).
    private static int Percentile(int[] values, int percent)
    {
        var histogram = new int[65536];
        foreach (int v in values)
        {
            histogram[v]++;
        }

        long target = (long)values.Length * percent / 100;
        long seen = 0;
        for (int v = 0; v < histogram.Length; v++)
        {
            seen += histogram[v];
            if (seen > target)
            {
                return v;
            }
        }

        return 65535;
    }

    // Cell position in lattice fixed-point units. Odd columns sit half a row lower (odd-q layout).
    private (long X, long Y) Position(HexCoord c, int cellsX, int cellsY)
    {
        long x = ((2L * c.Col) + 1) * cellsX * IntNoise.One / (2L * _geo.Width);
        long y = ((2L * c.Row) + 1 + (c.Col & 1)) * cellsY * IntNoise.One / (2L * _geo.Height);
        return (x, y);
    }

    // ---- Relief from elevation rank of land cells ---------------------------------------------------------------

    private void Relief()
    {
        var landHeights = new List<int>();
        for (int i = 0; i < _water.Length; i++)
        {
            if (!_water[i])
            {
                landHeights.Add(_map.Elevation[i]);
            }
        }

        if (landHeights.Count == 0)
        {
            return;
        }

        landHeights.Sort();
        int hills = landHeights[landHeights.Count * 72 / 100];
        int mountains = landHeights[Math.Min(landHeights.Count - 1, landHeights.Count * 92 / 100)];
        Span<byte> relief = _map.Relief.Raw;
        for (int i = 0; i < _water.Length; i++)
        {
            if (_water[i])
            {
                continue;
            }

            int e = _map.Elevation[i];
            relief[i] = (byte)(e >= mountains && e > SeaLevel + 30 ? Content.Relief.Mountains : e >= hills ? Content.Relief.Hills : Content.Relief.Flat);
        }
    }

    // ---- 3. Temperature ------------------------------------------------------------------------------------------

    // 0 at the equator (middle row), 1000 at the poles (top/bottom seam).
    private int Latitude(HexCoord c)
    {
        int h = _geo.Height;
        return Math.Min(1000, Math.Abs((2 * c.Row) + 1 + (c.Col & 1) - h) * 1000 / h);
    }

    private void Temperature()
    {
        Span<sbyte> t = _map.Temperature.Raw;
        for (int i = 0; i < t.Length; i++)
        {
            HexCoord c = _geo.FromIndex(i);
            int lat = Latitude(c);
            int temp = 28 - (55 * lat * lat / 1_000_000);
            int e = _map.Elevation[i];
            if (e > SeaLevel)
            {
                temp -= (e - SeaLevel) * 20 / (255 - SeaLevel);
            }

            (long x, long y) = Position(c, 16, 10);
            temp += ((IntNoise.Sample(_seed ^ 0x7E4D, 0, 16, 10, x, y) - 32768) * 4) / 32768;
            t[i] = (sbyte)Math.Clamp(temp, -30, 40);
        }
    }

    // ---- 4. Humidity ---------------------------------------------------------------------------------------------

    private void Humidity(int[] distToWater)
    {
        Span<byte> hum = _map.Humidity.Raw;
        for (int i = 0; i < hum.Length; i++)
        {
            if (_water[i])
            {
                hum[i] = 100;
                continue;
            }

            HexCoord c = _geo.FromIndex(i);
            int lat = Latitude(c);
            int value = 90 - (4 * Math.Min(distToWater[i], 15));

            // Climate belts: wet equator, dry subtropics, wetter mid-latitudes, dry poles.
            if (lat < 150)
            {
                value += 20;
            }
            else if (lat is >= 250 and < 420)
            {
                value -= 25;
            }
            else if (lat is >= 450 and < 700)
            {
                value += 5;
            }
            else if (lat >= 800)
            {
                value -= 10;
            }

            // Rain shadow: mountains upwind take the rain. Trade winds and polar easterlies come from the east,
            // westerlies from the west.
            int upwind = lat is >= 330 and < 660 ? -1 : 1;
            if (_map.Relief[i] != (byte)Content.Relief.Mountains)
            {
                for (int k = 1; k <= 5; k++)
                {
                    int j = _geo.Index(_geo.Wrap(c.Col + (upwind * k), c.Row));
                    if (_map.Relief[j] == (byte)Content.Relief.Mountains)
                    {
                        value -= 30;
                        break;
                    }
                }
            }

            (long x, long y) = Position(c, 24, 14);
            value += ((IntNoise.Sample(_seed ^ 0x4A11D, 0, 24, 14, x, y) - 32768) * 12) / 32768;
            hum[i] = (byte)Math.Clamp(value, 0, 100);
        }
    }

    // ---- 5. Biomes -----------------------------------------------------------------------------------------------

    private void Biomes()
    {
        int[] distToLand = DistanceTo(isTarget: i => !_water[i]);
        Span<byte> biome = _map.Biome.Raw;
        for (int i = 0; i < biome.Length; i++)
        {
            if (_water[i])
            {
                biome[i] = (byte)(distToLand[i] <= 1 ? _coastId : distToLand[i] <= 3 ? _shallowId : _deepId);
                continue;
            }

            biome[i] = (byte)LandBiome(_map.Temperature[i], _map.Humidity[i], (Content.Relief)_map.Relief[i]);
        }
    }

    private int LandBiome(int temperature, int humidity, Content.Relief relief)
    {
        foreach (BiomeDefinition b in _content.Biomes)
        {
            if (b.Kind != BiomeKind.Land)
            {
                continue;
            }

            if (temperature < b.MinTemperature || temperature > b.MaxTemperature || humidity < b.MinHumidity || humidity > b.MaxHumidity)
            {
                continue;
            }

            // Swamps only form on flat ground.
            if (b.Key == "swamp" && relief != Content.Relief.Flat)
            {
                continue;
            }

            return b.Id;
        }

        return _grasslandId;
    }

    // ---- 6. Rivers and lakes -------------------------------------------------------------------------------------

    // Lakes: fill every land hollow up to its spill height (priority flood from the sea). Small, deep enough
    // hollows become lakes; huge ones (inland basins) stay dry land.
    private void Lakes(WorldGenReport report)
    {
        int n = _geo.CellCount;
        var fill = new int[n];
        var done = new bool[n];
        var queue = new PriorityQueue<int, long>();
        for (int i = 0; i < n; i++)
        {
            fill[i] = _map.Elevation[i];
            if (_water[i])
            {
                done[i] = true;
                queue.Enqueue(i, Key(fill[i], i));
            }
        }

        while (queue.TryDequeue(out int cur, out _))
        {
            _geo.GetNeighbors(_geo.FromIndex(cur), _n6);
            foreach (HexCoord c in _n6)
            {
                int j = _geo.Index(c);
                if (done[j])
                {
                    continue;
                }

                done[j] = true;
                fill[j] = Math.Max(fill[j], fill[cur]);
                queue.Enqueue(j, Key(fill[j], j));
            }
        }

        // Group flooded cells into basins and keep the ones of a sensible size.
        var basin = new int[n];
        Array.Fill(basin, -1);
        var cells = new List<int>();
        var stack = new Stack<int>();
        Span<byte> biome = _map.Biome.Raw;
        for (int i = 0; i < n; i++)
        {
            if (_water[i] || basin[i] >= 0 || fill[i] - _map.Elevation[i] < _options.MinLakeDepth)
            {
                continue;
            }

            cells.Clear();
            stack.Push(i);
            basin[i] = i;
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                cells.Add(cur);
                _geo.GetNeighbors(_geo.FromIndex(cur), _n6);
                foreach (HexCoord c in _n6)
                {
                    int j = _geo.Index(c);
                    if (basin[j] < 0 && !_water[j] && fill[j] > _map.Elevation[j])
                    {
                        basin[j] = i;
                        stack.Push(j);
                    }
                }
            }

            if (cells.Count > _options.MaxLakeCells)
            {
                continue;
            }

            foreach (int c in cells)
            {
                _water[c] = true;
                biome[c] = (byte)_lakeId;
                _map.Relief.Raw[c] = (byte)Content.Relief.Flat;
                _map.Humidity.Raw[c] = 100;
            }

            report.Lakes++;
        }
    }

    // Priority key: lower height first, then lower index, so the flood order is total (deterministic).
    private static long Key(int height, int index) => ((long)height << 32) | (uint)index;

    // Rivers run along cell edges (the corner graph). A priority flood from every corner that touches water gives
    // each land corner one downhill direction that always reaches the sea or a lake (hollows are crossed as if
    // filled). Rain (humidity) is summed downstream; edges carrying enough water become rivers of size 1–3.
    private void Rivers(WorldGenReport report)
    {
        Lakes(report);

        var corners = new HexCorners(_geo);
        int count = corners.Count;
        var height = new int[count];
        var rain = new int[count];
        var isOutlet = new bool[count];
        Span<HexCoord> three = stackalloc HexCoord[3];
        var queue = new PriorityQueue<int, long>();
        var done = new bool[count];
        for (int k = 0; k < count; k++)
        {
            corners.Cells(k, three);
            int sum = 0;
            int humidity = 0;
            bool wet = false;
            foreach (HexCoord c in three)
            {
                int i = _geo.Index(c);
                sum += _map.Elevation[i];
                humidity += _map.Humidity[i];
                wet |= _water[i];
            }

            height[k] = sum;
            rain[k] = wet ? 0 : humidity / 3;
            if (wet)
            {
                isOutlet[k] = true;
                done[k] = true;
                queue.Enqueue(k, Key(sum, k));
            }
        }

        var flowTo = new int[count];
        var flowEdge = new int[count];
        Array.Fill(flowTo, -1);
        var order = new List<int>(count);
        Span<int> nbr = stackalloc int[3];
        Span<HexEdge> nbrEdges = stackalloc HexEdge[3];
        var level = (int[])height.Clone();
        while (queue.TryDequeue(out int cur, out _))
        {
            order.Add(cur);
            corners.Neighbors(cur, nbr, nbrEdges);
            for (int d = 0; d < 3; d++)
            {
                int j = nbr[d];
                if (done[j])
                {
                    continue;
                }

                done[j] = true;
                flowTo[j] = cur;
                flowEdge[j] = _geo.EdgeIndex(nbrEdges[d]);
                level[j] = Math.Max(level[j], level[cur]);
                queue.Enqueue(j, Key(level[j], j));
            }
        }

        // Accumulate rain from the top of each catchment down (reverse flood order).
        var flow = new long[count];
        for (int k = order.Count - 1; k >= 0; k--)
        {
            int c = order[k];
            flow[c] += rain[c];
            if (flowTo[c] >= 0)
            {
                flow[flowTo[c]] += flow[c];
            }
        }

        long small = _options.RiverRain;
        Span<byte> river = _map.River.Raw;
        int mouths = 0;
        for (int k = 0; k < count; k++)
        {
            if (flowTo[k] < 0 || isOutlet[k] || flow[k] < small)
            {
                continue;
            }

            river[flowEdge[k]] = (byte)(flow[k] >= small * 8 ? 3 : flow[k] >= small * 3 ? 2 : 1);
            if (isOutlet[flowTo[k]])
            {
                mouths++;
            }
        }

        report.Rivers = mouths;
    }

    // ---- 7. Features and fertility -------------------------------------------------------------------------------

    private bool HasFreshWater(int i)
    {
        HexCoord c = _geo.FromIndex(i);
        for (int d = 0; d < HexDirections.Count; d++)
        {
            var dir = (HexDirection)d;
            if (_map.River[_geo.EdgeIndex(_geo.Edge(c, dir))] > 0)
            {
                return true;
            }

            if (_map.Biome[_geo.Index(_geo.Neighbor(c, dir))] == _lakeId)
            {
                return true;
            }
        }

        return _map.Feature[i] == (byte)TerrainFeature.Oasis;
    }

    private void FeaturesAndFertility()
    {
        int desertId = _content.Biome("desert").Id;
        Span<byte> feature = _map.Feature.Raw;
        Span<byte> fertility = _map.Fertility.Raw;
        for (int i = 0; i < feature.Length; i++)
        {
            int b = _map.Biome[i];
            int t = _map.Temperature[i];
            if (_water[i])
            {
                if (t <= -8 && b != _lakeId)
                {
                    feature[i] = (byte)TerrainFeature.IceField;
                }
                else if (t >= 24 && (b == _coastId || b == _shallowId) && _rng.Chance(1, 6))
                {
                    feature[i] = (byte)TerrainFeature.Reef;
                }

                fertility[i] = 0;
                continue;
            }

            if (b == desertId && _map.Relief[i] == (byte)Content.Relief.Flat && _rng.Chance(1, 40))
            {
                feature[i] = (byte)TerrainFeature.Oasis;
            }

            int f = _content.Biomes[b].BaseFertility;
            if (HasFreshWater(i))
            {
                f += 25;
            }

            if (_map.Relief[i] == (byte)Content.Relief.Hills)
            {
                f -= 10;
            }
            else if (_map.Relief[i] == (byte)Content.Relief.Mountains)
            {
                f = Math.Min(f, 10);
            }

            if (feature[i] == (byte)TerrainFeature.Oasis)
            {
                f = Math.Max(f, 60);
            }

            f += _rng.NextInt(-8, 9);
            fertility[i] = (byte)Math.Clamp(f, 0, 100);
        }
    }

    // ---- 8. Deposits ---------------------------------------------------------------------------------------------

    private void Deposits()
    {
        Span<ushort> deposit = _map.Deposit.Raw;
        Span<int> richness = _map.DepositRichness.Raw;
        Span<byte> depth = _map.DepositDepth.Raw;
        var allowed = new bool[_content.Biomes.Count];
        foreach (DepositDefinition d in _content.Deposits)
        {
            Array.Clear(allowed);
            foreach (string key in d.Biomes)
            {
                allowed[_content.Biome(key).Id] = true;
            }

            int reliefMask = 0;
            foreach (Content.Relief r in d.Relief)
            {
                reliefMask |= 1 << (int)r;
            }

            for (int i = 0; i < deposit.Length; i++)
            {
                if (deposit[i] != 0 || !allowed[_map.Biome[i]] || (reliefMask & (1 << _map.Relief[i])) == 0)
                {
                    continue;
                }

                if (_rng.Chance(d.PerThousandCells, 1000))
                {
                    deposit[i] = (ushort)d.Id;
                    richness[i] = _rng.NextInt(d.MinRichness, d.MaxRichness + 1);
                    depth[i] = (byte)d.Depth;
                }
            }
        }
    }

    // ---- 10. Start positions with a fairness check ---------------------------------------------------------------

    private int StartScore(HexCoord c, Span<HexCoord> area)
    {
        int n = _geo.Range(c, StartRadius, area);
        int food = 0;
        int water = 0;
        int resources = 0;
        for (int k = 0; k < n; k++)
        {
            int i = _geo.Index(area[k]);
            food += _content.Biomes[_map.Biome[i]].Food * 2;
            food += _map.Fertility[i] / 25;
            if (!_water[i] && HasFreshWater(i))
            {
                water += 2;
            }
            else if (_map.Biome[i] == _coastId)
            {
                water += 1;
            }

            if (_map.Deposit[i] != 0 && _map.DepositDepth[i] == 0)
            {
                resources += 3;
            }
        }

        return food + water + resources;
    }

    // Level 0: the normal rules. Level 1 (too few places): any land outside mountains. Level 2: any land.
    private bool IsStartCandidate(int i, int level)
    {
        if (_water[i] || (level < 2 && _map.Relief[i] == (byte)Content.Relief.Mountains))
        {
            return false;
        }

        if (level > 0)
        {
            return true;
        }

        int t = _map.Temperature[i];
        return t is >= 0 and <= 32 && _content.Biomes[_map.Biome[i]].Food >= 2 && HasFreshWater(i);
    }

    private void StartPositions(WorldGenReport report)
    {
        int civs = _options.CivilizationCount;
        if (civs <= 0)
        {
            return;
        }

        var area = new HexCoord[HexTorus.RangeSize(StartRadius)];
        var candidates = new List<(int Score, int Index)>();
        int land = 0;
        for (int i = 0; i < _water.Length; i++)
        {
            land += _water[i] ? 0 : 1;
        }

        // A good spread: about half the side of the land area each tribe would get on its own.
        int wantedDistance = Math.Max(4, (int)IntMath.Sqrt(land / civs) / 2);
        var chosen = new List<(int Score, int Index)>();
        var attempt = new List<(int Score, int Index)>();
        int chosenSpread = -1;

        // Level 0 uses the normal start rules; when they leave too few or too crowded places, the rules relax.
        for (int level = 0; level <= 2 && chosenSpread < wantedDistance; level++)
        {
            candidates.Clear();
            for (int i = 0; i < _water.Length; i++)
            {
                if (IsStartCandidate(i, level))
                {
                    candidates.Add((StartScore(_geo.FromIndex(i), area), i));
                }
            }

            if (candidates.Count < civs)
            {
                continue;
            }

            // Best score first; ties broken by cell index so the order is total (deterministic).
            candidates.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Index.CompareTo(b.Index));

            // Fairness first: only places at least `fair`% as good as the best one. Among those, spread the tribes
            // out: each next start is the candidate farthest from all starts chosen so far.
            foreach (int fair in (int[])[80, 70, 60, 50, 0])
            {
                int bar = candidates[0].Score * fair / 100;
                int pool = 0;
                while (pool < candidates.Count && candidates[pool].Score >= bar)
                {
                    pool++;
                }

                attempt.Clear();
                PickStarts(candidates, pool, civs, attempt);
                int spread = attempt.Count == civs ? MinDistance(attempt) : -1;
                if (spread > chosenSpread)
                {
                    chosen.Clear();
                    chosen.AddRange(attempt);
                    chosenSpread = spread;
                }

                if (spread >= wantedDistance)
                {
                    break;
                }
            }
        }

        foreach ((int score, int index) in chosen)
        {
            report.Starts.Add(_geo.FromIndex(index));
            report.StartScores.Add(score);
        }

        int minD = int.MaxValue;
        for (int a = 0; a < report.Starts.Count; a++)
        {
            for (int b = a + 1; b < report.Starts.Count; b++)
            {
                minD = Math.Min(minD, _geo.Distance(report.Starts[a], report.Starts[b]));
            }
        }

        report.MinStartDistance = minD == int.MaxValue ? 0 : minD;
        if (report.StartScores.Count > 0)
        {
            int max = int.MinValue;
            int min = int.MaxValue;
            foreach (int s in report.StartScores)
            {
                max = Math.Max(max, s);
                min = Math.Min(min, s);
            }

            report.StartFairnessPercent = max <= 0 ? 100 : min * 100 / max;
        }
    }

    private int MinDistance(List<(int Score, int Index)> starts)
    {
        int min = int.MaxValue;
        for (int a = 0; a < starts.Count; a++)
        {
            for (int b = a + 1; b < starts.Count; b++)
            {
                min = Math.Min(min, _geo.Distance(_geo.FromIndex(starts[a].Index), _geo.FromIndex(starts[b].Index)));
            }
        }

        return min;
    }

    // Farthest-point sampling over the first `pool` candidates (already sorted best first).
    private void PickStarts(List<(int Score, int Index)> candidates, int pool, int civs, List<(int Score, int Index)> chosen)
    {
        if (pool == 0)
        {
            return;
        }

        var nearest = new int[pool];
        Array.Fill(nearest, int.MaxValue);
        int pick = 0;
        while (chosen.Count < civs && pick >= 0)
        {
            chosen.Add(candidates[pick]);
            HexCoord p = _geo.FromIndex(candidates[pick].Index);
            int best = -1;
            for (int k = 0; k < pool; k++)
            {
                nearest[k] = Math.Min(nearest[k], _geo.Distance(p, _geo.FromIndex(candidates[k].Index)));
                if (nearest[k] > 0 && (best < 0 || nearest[k] > nearest[best]))
                {
                    best = k;
                }
            }

            pick = best;
        }
    }

    // ---- Helpers -------------------------------------------------------------------------------------------------

    // Breadth-first distance (in steps) from every cell to the nearest target cell.
    private int[] DistanceTo(Func<int, bool> isTarget)
    {
        var dist = new int[_geo.CellCount];
        Array.Fill(dist, int.MaxValue);
        var queue = new Queue<int>();
        for (int i = 0; i < dist.Length; i++)
        {
            if (isTarget(i))
            {
                dist[i] = 0;
                queue.Enqueue(i);
            }
        }

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            _geo.GetNeighbors(_geo.FromIndex(cur), _n6);
            foreach (HexCoord n in _n6)
            {
                int ni = _geo.Index(n);
                if (dist[ni] == int.MaxValue)
                {
                    dist[ni] = dist[cur] + 1;
                    queue.Enqueue(ni);
                }
            }
        }

        return dist;
    }

    private void FillReport(WorldGenReport report)
    {
        int water = 0;
        var biomes = new int[_content.Biomes.Count];
        var deposits = new int[_content.Deposits.Count + 1];
        for (int i = 0; i < _geo.CellCount; i++)
        {
            water += _water[i] ? 1 : 0;
            biomes[_map.Biome[i]]++;
            deposits[_map.Deposit[i]]++;
        }

        deposits[0] = 0;
        int edges = 0;
        foreach (byte r in _map.River.Values)
        {
            edges += r > 0 ? 1 : 0;
        }

        report.WaterPercent = water * 100 / _geo.CellCount;
        report.BiomeCounts = biomes;
        report.DepositCounts = deposits;
        report.RiverEdges = edges;
    }
}

/// <summary>Options for <see cref="WorldGenerator"/>.</summary>
public sealed record WorldGenOptions(WorldType WorldType, int WaterPercent, int CivilizationCount)
{
    /// <summary>Rain (sum of humidity of the cells upstream) an edge needs to carry a river; 3× and 8× give bigger rivers.</summary>
    public int RiverRain { get; init; } = 2500;

    /// <summary>A hollow must be at least this much below its spill point to hold a lake.</summary>
    public int MinLakeDepth { get; init; } = 2;

    /// <summary>Larger hollows stay dry land (inland basins).</summary>
    public int MaxLakeCells { get; init; } = 40;
}
