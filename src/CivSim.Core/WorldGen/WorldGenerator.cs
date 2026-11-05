using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Random;
using CivSim.Core.State;

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
    private readonly HexGlobe _geo;
    private readonly DeterministicRandom _rng;
    private readonly ulong _seed;
    private readonly WorldGenOptions _options;
    private readonly HexCoord[] _n6 = new HexCoord[6];
    private readonly int _deepId;
    private readonly int _shallowId;
    private readonly int _coastId;
    private readonly int _lakeId;
    private readonly int _grasslandId;
    private readonly int _iceId;
    private bool[] _water = [];
    private readonly long[] _px;
    private readonly long[] _py;
    private readonly long[] _pz;
    private readonly long[] _climateSin;
    private readonly ClimateModel _climate;
    private readonly int _yearHundredths;

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
        _iceId = content.Biome("ice").Id;
        _climate = new ClimateModel(options.Planet);
        _yearHundredths = options.Planet.YearHundredths;

        // Every cell's point on the unit sphere: noise is sampled there, so it has no seam around the globe or over the poles.
        int n = _geo.CellCount;
        _px = new long[n];
        _py = new long[n];
        _pz = new long[n];
        _climateSin = new long[n];
        for (int i = 0; i < n; i++)
        {
            HexCoord c = _geo.FromIndex(i);
            long sinLat = _geo.SinLatitude(c, FixedTrig.One);
            long cosLat = FixedTrig.Complement(sinLat);
            int lon = (int)_geo.Longitude(c, FixedTrig.FullTurn);
            _px[i] = cosLat * FixedTrig.Cos(lon) / FixedTrig.One;
            _py[i] = cosLat * FixedTrig.Sin(lon) / FixedTrig.One;
            _pz[i] = sinLat;
        }
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
        int[] distToWater = DistanceTo(isTarget: i => _water[i]);
        Temperature(distToWater);
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
            long sum = 0;
            for (int o = 0; o < octaves; o++)
            {
                sum += (long)Noise(_seed, o, baseCellsX << o, i) * (16 >> o);
            }

            raw[i] = (int)(sum / totalWeight);
        }

        // Continent seeds: land grows around a few centres on the sphere, with oceans between them; the noise only
        // shapes the coasts and inland seas. Without them 3D noise joins almost all land into one mass.
        (long X, long Y, long Z, long CosRadius)[] plates = PlateCentres();
        for (int i = 0; i < raw.Length; i++)
        {
            long best = -FixedTrig.One;
            foreach ((long x, long y, long z, long cosR) in plates)
            {
                long dot = ((x * _px[i]) + (y * _py[i]) + (z * _pz[i])) / FixedTrig.One;
                best = Math.Max(best, (dot - cosR) * FixedTrig.One / (FixedTrig.One - cosR));
            }

            // Flat inside a continent, so its hills, basins and lakes still come from the noise.
            long plate = Math.Clamp(best, -FixedTrig.One, PlateTop);
            long plateValue = (plate + FixedTrig.One) * 65535 / (2 * FixedTrig.One);
            raw[i] = (int)(((plateValue * PlateWeightPercent) + ((long)raw[i] * (100 - PlateWeightPercent))) / 100);
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
        int highest = threshold;
        foreach (int v in raw)
        {
            highest = Math.Max(highest, v);
        }

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
                elevation[i] = (byte)(SeaLevel + ((long)(raw[i] - threshold) * (255 - SeaLevel) / Math.Max(1, highest - threshold)));
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

    // Share of the land height that comes from the continent seeds; the rest is noise.
    private const int PlateWeightPercent = 55;

    // Highest value of a continent seed (in FixedTrig.One): beyond it the seed no longer raises the land.
    private const long PlateTop = FixedTrig.One / 5;

    // Centres and angular radii of the continent seeds, spread over the sphere and kept apart (deterministic from the seed).
    private (long X, long Y, long Z, long CosRadius)[] PlateCentres()
    {
        (int count, int minRadius, int maxRadius, int minGap) = _options.WorldType switch
        {
            WorldType.Pangea => (1, 80, 100, 90),
            WorldType.Archipelago => (18, 8, 16, 20),
            _ => (5, 25, 40, 55),
        };
        var rng = new DeterministicRandom(SplitMix64.Combine(_seed, 0x91A7E5));
        count += rng.NextInt(0, Math.Max(1, count / 3) + 1);
        var plates = new List<(long X, long Y, long Z, long CosRadius)>();
        long minDot = FixedTrig.Cos(FixedTrig.Degrees(minGap));
        for (int attempt = 0; attempt < 400 && plates.Count < count; attempt++)
        {
            // Uniform on the sphere, but keep centres out of the polar caps (|latitude| < about 64°).
            long z = (rng.NextInt(-900, 901) * FixedTrig.One) / 1000;
            long r = FixedTrig.Complement(z);
            int lon = rng.NextInt(0, FixedTrig.FullTurn);
            long x = r * FixedTrig.Cos(lon) / FixedTrig.One;
            long y = r * FixedTrig.Sin(lon) / FixedTrig.One;
            bool apart = true;
            foreach ((long px, long py, long pz, _) in plates)
            {
                apart &= ((px * x) + (py * y) + (pz * z)) / FixedTrig.One < minDot;
            }

            if (apart || attempt > 300)
            {
                long cosR = FixedTrig.Cos(FixedTrig.Degrees(rng.NextInt(minRadius, maxRadius + 1)));
                plates.Add((x, y, z, cosR));
            }
        }

        return [.. plates];
    }

    // Noise 0..65535 at a cell, with about `cellsAroundEquator` features around the globe.
    private int Noise(ulong seed, int octave, int cellsAroundEquator, int i) =>
        SphereNoise.Sample(seed, octave, cellsAroundEquator, _px[i], _py[i], _pz[i]);

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
        (int mountainPercent, int hillPercent) = _options.Relief switch
        {
            WorldRelief.Flat => (4, 12),
            WorldRelief.Mountainous => (14, 26),
            _ => (8, 20),
        };
        int hills = landHeights[landHeights.Count * (100 - mountainPercent - hillPercent) / 100];
        int mountains = landHeights[Math.Min(landHeights.Count - 1, landHeights.Count * (100 - mountainPercent) / 100)];
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

    // Sine of the latitude bent by two octaves of noise (up to about 13°), so climate zones follow wavy lines
    // instead of map rows. Computed once per cell.
    private void ClimateLatitudes()
    {
        for (int i = 0; i < _climateSin.Length; i++)
        {
            int wobble = ((Noise(_seed ^ 0xC11A7E, 0, 6, i) - 32768) * 1800) / 32768;
            wobble += ((Noise(_seed ^ 0xC11A7E, 1, 14, i) - 32768) * 650) / 32768;
            int angle = Math.Clamp(FixedTrig.Asin(_pz[i]) + wobble, -FixedTrig.QuarterTurn, FixedTrig.QuarterTurn);
            _climateSin[i] = FixedTrig.Sin(angle);
        }
    }

    // 0 at the equator, 1000 at the poles: the bent latitude in parts of 90°.
    private int ClimateLatitude(int i) => Math.Abs(FixedTrig.Asin(_climateSin[i])) * 1000 / FixedTrig.QuarterTurn;

    // Humidity change by latitude: wet equator, dry subtropics, wetter mid-latitudes, dry poles.
    // Straight lines between these points, so there are no sharp steps between belts.
    private static readonly (int Lat, int Change)[] HumidityBelts =
        [(0, 20), (120, 18), (330, -22), (560, 5), (720, 2), (900, -10), (1000, -10)];

    // Humidity lost with distance from water. Distance is measured in standard-map cells (258 columns),
    // so continents on bigger maps are not drier just because they are wider in cells.
    private int InlandDrying(int distance)
    {
        (int perCell, int maxCells) = _options.Inland switch
        {
            WorldInland.Dry => (4, 15),
            WorldInland.Humid => (2, 10),
            _ => (3, 12),
        };
        int scaled = distance * 258 / _geo.Width;
        return perCell * Math.Min(scaled, maxCells);
    }

    private static int HumidityBelt(int lat)
    {
        for (int k = 1; k < HumidityBelts.Length; k++)
        {
            (int lat1, int ch1) = HumidityBelts[k];
            if (lat <= lat1)
            {
                (int lat0, int ch0) = HumidityBelts[k - 1];
                return ch0 + ((ch1 - ch0) * (lat - lat0) / (lat1 - lat0));
            }
        }

        return HumidityBelts[^1].Change;
    }

    // Annual mean temperature from the planet's sunlight (GDD 3, stage 1b), cooler with height; the seasonal swing
    // (bigger inland, small over water) and the growing season follow from it.
    private void Temperature(int[] distToWater)
    {
        ClimateLatitudes();
        Span<sbyte> t = _map.Temperature.Raw;
        Span<byte> swing = _map.Seasonality.Raw;
        Span<byte> growing = _map.GrowingSeason.Raw;
        for (int i = 0; i < t.Length; i++)
        {
            int temp = _climate.MeanTemperature(_climateSin[i]);
            int e = _map.Elevation[i];
            if (e > SeaLevel)
            {
                temp -= (e - SeaLevel) * 20 / (255 - SeaLevel);
            }

            temp += ((Noise(_seed ^ 0x7E4D, 0, 16, i) - 32768) * 4) / 32768;
            temp = Math.Clamp(temp, -30, 40);
            t[i] = (sbyte)temp;

            // Continentality: 25% over water, 35% on the coast up to 100% six standard-map cells inland.
            int inland = Math.Min(6, distToWater[i] * 258 / _geo.Width);
            int continental = _water[i] ? 25 : 35 + (65 * inland / 6);
            int s = _climate.SeasonalSwing(_climateSin[i], continental);
            swing[i] = (byte)s;
            growing[i] = (byte)ClimateModel.GrowingSeason(temp, s);
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
            int lat = ClimateLatitude(i);
            int value = 90 - InlandDrying(distToWater[i]) + HumidityBelt(lat);

            // Rain shadow: mountains upwind take the rain, more when they are close. Trade winds and polar easterlies
            // come from the east, westerlies from the west. The wind looks along a slanted line, so shadows are not
            // drawn along map rows.
            int upwind = lat is >= 330 and < 660 ? -1 : 1;
            if (_map.Relief[i] != (byte)Content.Relief.Mountains)
            {
                for (int k = 1; k <= 5; k++)
                {
                    int j = _geo.Index(_geo.Wrap(c.Col + (upwind * k), c.Row + ((k * (c.Col & 3)) / 6) - (k / 3)));
                    if (_map.Relief[j] == (byte)Content.Relief.Mountains)
                    {
                        value -= 36 - (5 * k);
                        break;
                    }
                }
            }

            value += ((Noise(_seed ^ 0x4A11D, 0, 12, i) - 32768) * 14) / 32768;
            value += ((Noise(_seed ^ 0x4A11D, 1, 32, i) - 32768) * 8) / 32768;
            value += _options.Moisture switch
            {
                WorldMoisture.Dry => -15,
                WorldMoisture.Wet => 15,
                _ => 0,
            };
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

            biome[i] = (byte)LandBiome(_map.Temperature[i], _map.Humidity[i], (Content.Relief)_map.Relief[i], _map.Seasonality[i], _map.GrowingSeason[i]);
        }
    }

    // Seasons (stage 1b): land whose summer stays below freezing is ice; land with almost no growing season is tundra;
    // very large swings between summer and winter act like drought, so forests give way to steppe and desert.
    private int LandBiome(int temperature, int humidity, Content.Relief relief, int swing, int growing)
    {
        if (temperature + (swing / 2) < 0)
        {
            temperature = Math.Min(temperature, -12);
        }
        else if (growing < 15 && temperature > -11)
        {
            return _content.Biome("tundra").Id;
        }

        humidity = Math.Max(0, humidity - (Math.Max(0, swing - 40) / 2));
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
            bool frozen = false;
            foreach (HexCoord c in three)
            {
                int i = _geo.Index(c);
                sum += _map.Elevation[i];
                humidity += _map.Humidity[i];
                wet |= _water[i];
                frozen |= _map.Biome[i] == _iceId;
            }

            height[k] = sum;
            rain[k] = wet || frozen ? 0 : humidity / 3; // ice caps hold their water
            if (corners.IsPolar(k))
            {
                done[k] = true; // no rivers over the poles
            }
            else if (wet)
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
        int resourcePercent = _options.Resources switch
        {
            WorldResources.Scarce => 60,
            WorldResources.Rich => 160,
            _ => 100,
        };
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

            int chance = Math.Min(1000, d.PerThousandCells * resourcePercent / 100);
            for (int i = 0; i < deposit.Length; i++)
            {
                if (deposit[i] != 0 || !allowed[_map.Biome[i]] || (reliefMask & (1 << _map.Relief[i])) == 0)
                {
                    continue;
                }

                if (_rng.Chance(chance, 1000))
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
            // Land food counts only as far as the seasons allow (the same rule as gathering).
            int season = ClimateModel.WildFoodPercent(_map.GrowingSeason[i], _yearHundredths, _content.Population.SurvivableDeadSeasonHundredths);
            food += _content.Biomes[_map.Biome[i]].Food * 2 * season / 100;
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
        return t is >= 0 and <= 32 && ClimateModel.WildFoodPercent(_map.GrowingSeason[i], _yearHundredths, _content.Population.SurvivableDeadSeasonHundredths) >= 80 && _content.Biomes[_map.Biome[i]].Food >= 2 && HasFreshWater(i);
    }

    private void StartPositions(WorldGenReport report)
    {
        int civs = _options.CivilizationCount;
        if (civs <= 0)
        {
            return;
        }

        var area = new HexCoord[HexGlobe.RangeSize(StartRadius)];
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
    /// <summary>The planet: axial tilt, year, star brightness, orbit (stage 1b).</summary>
    public PlanetParameters Planet { get; init; } = PlanetParameters.Earth;

    /// <summary>How much rain the planet gets.</summary>
    public WorldMoisture Moisture { get; init; }

    /// <summary>How fast humidity falls away from the sea.</summary>
    public WorldInland Inland { get; init; }

    /// <summary>Share of hills and mountains.</summary>
    public WorldRelief Relief { get; init; }

    /// <summary>How many natural deposits there are.</summary>
    public WorldResources Resources { get; init; }

    /// <summary>Options matching the world settings of a game.</summary>
    public static WorldGenOptions From(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new WorldGenOptions(settings.WorldType, settings.WaterPercent, settings.CivilizationCount)
        {
            Planet = settings.Planet,
            Moisture = settings.Moisture,
            Inland = settings.Inland,
            Relief = settings.Relief,
            Resources = settings.Resources,
        };
    }

    /// <summary>Rain (sum of humidity of the cells upstream) an edge needs to carry a river; 3× and 8× give bigger rivers.</summary>
    public int RiverRain { get; init; } = 2500;

    /// <summary>A hollow must be at least this much below its spill point to hold a lake.</summary>
    public int MinLakeDepth { get; init; } = 2;

    /// <summary>Larger hollows stay dry land (inland basins).</summary>
    public int MaxLakeCells { get; init; } = 40;
}
