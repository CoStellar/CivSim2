using CivSim.Core.Random;

namespace CivSim.Core.WorldGen;

/// <summary>
/// Integer value noise that tiles on a flat rectangle (kept for 2D uses; the world generator uses <see cref="SphereNoise"/>). Everything is fixed-point (65536 = 1.0), so the same seed
/// produces bit-identical maps on every platform. Output range 0..65535.
/// </summary>
public static class IntNoise
{
    /// <summary>Fixed-point one.</summary>
    public const long One = 65536;

    /// <summary>
    /// Samples one octave with a lattice of <paramref name="cellsX"/>×<paramref name="cellsY"/> cells over the whole map,
    /// at position (<paramref name="x"/>, <paramref name="y"/>) in lattice fixed-point units (0 ≤ x &lt; cellsX × One).
    /// </summary>
    public static int Sample(ulong seed, int octave, int cellsX, int cellsY, long x, long y)
    {
        long i0 = x / One;
        long j0 = y / One;
        long fx = x - (i0 * One);
        long fy = y - (j0 * One);
        int ia = (int)(i0 % cellsX);
        int ib = (ia + 1) % cellsX;
        int ja = (int)(j0 % cellsY);
        int jb = (ja + 1) % cellsY;

        long v00 = Lattice(seed, octave, ia, ja);
        long v10 = Lattice(seed, octave, ib, ja);
        long v01 = Lattice(seed, octave, ia, jb);
        long v11 = Lattice(seed, octave, ib, jb);

        long sx = Smooth(fx);
        long sy = Smooth(fy);
        long top = v00 + (((v10 - v00) * sx) / One);
        long bottom = v01 + (((v11 - v01) * sx) / One);
        return (int)(top + (((bottom - top) * sy) / One));
    }

    /// <summary>The random value 0..65535 at one lattice point.</summary>
    public static int Lattice(ulong seed, int octave, int i, int j)
    {
        ulong key = SplitMix64.Combine(SplitMix64.Combine(seed, (ulong)octave), ((ulong)(uint)i << 32) | (uint)j);
        return (int)(SplitMix64.Mix(key) >> 48);
    }

    // 3t² − 2t³ in fixed point.
    private static long Smooth(long t) => (t * t / One) * ((3 * One) - (2 * t)) / One;
}

/// <summary>
/// Integer value noise on the sphere: 3D lattice noise sampled at points on the unit sphere, so the pattern has no
/// seam around the globe or over the poles. Positions are in <see cref="Numerics.FixedTrig.One"/> units. Output 0..65535.
/// </summary>
public static class SphereNoise
{
    /// <summary>
    /// Samples one octave at the unit-sphere point (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>)
    /// with about <paramref name="cellsAroundEquator"/> lattice cells around the equator.
    /// </summary>
    public static int Sample(ulong seed, int octave, int cellsAroundEquator, long x, long y, long z)
    {
        long one = Numerics.FixedTrig.One;
        long scale = cellsAroundEquator * one;
        long lx = (x * scale / (2 * Numerics.FixedTrig.Pi)) + (64 * one);
        long ly = (y * scale / (2 * Numerics.FixedTrig.Pi)) + (64 * one);
        long lz = (z * scale / (2 * Numerics.FixedTrig.Pi)) + (64 * one);
        int i0 = (int)(lx / one);
        int j0 = (int)(ly / one);
        int k0 = (int)(lz / one);
        long fx = Smooth((lx - (i0 * one)) * IntNoise.One / one);
        long fy = Smooth((ly - (j0 * one)) * IntNoise.One / one);
        long fz = Smooth((lz - (k0 * one)) * IntNoise.One / one);
        long c00 = Lerp(Lattice(seed, octave, i0, j0, k0), Lattice(seed, octave, i0 + 1, j0, k0), fx);
        long c10 = Lerp(Lattice(seed, octave, i0, j0 + 1, k0), Lattice(seed, octave, i0 + 1, j0 + 1, k0), fx);
        long c01 = Lerp(Lattice(seed, octave, i0, j0, k0 + 1), Lattice(seed, octave, i0 + 1, j0, k0 + 1), fx);
        long c11 = Lerp(Lattice(seed, octave, i0, j0 + 1, k0 + 1), Lattice(seed, octave, i0 + 1, j0 + 1, k0 + 1), fx);
        return (int)Lerp(Lerp(c00, c10, fy), Lerp(c01, c11, fy), fz);
    }

    private static long Lerp(long a, long b, long t) => a + ((b - a) * t / IntNoise.One);

    private static int Lattice(ulong seed, int octave, int i, int j, int k)
    {
        ulong key = SplitMix64.Combine(SplitMix64.Combine(seed, (ulong)octave), ((ulong)(uint)i << 42) ^ ((ulong)(uint)j << 21) ^ (uint)k);
        return (int)(SplitMix64.Mix(key) >> 48);
    }

    private static long Smooth(long t) => (t * t / IntNoise.One) * ((3 * IntNoise.One) - (2 * t)) / IntNoise.One;
}
