using CivSim.Core.Random;

namespace CivSim.Core.WorldGen;

/// <summary>
/// Integer value noise that tiles on the torus. Everything is fixed-point (65536 = 1.0), so the same seed
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
