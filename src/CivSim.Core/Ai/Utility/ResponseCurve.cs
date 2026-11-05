using System;

namespace CivSim.Core.Ai.Utility;

/// <summary>Shape of a <see cref="ResponseCurve"/>.</summary>
public enum CurveKind : byte
{
    /// <summary>A straight line between the two points.</summary>
    Linear = 0,

    /// <summary>An S-curve between the two points (smoothstep: slow, fast, slow).</summary>
    Smooth = 1,

    /// <summary>The first output below the threshold <c>X1</c>, the second from it on.</summary>
    Step = 2,
}

/// <summary>
/// Turns one world value into a factor in per mille (GDD 8.3: "krzywa odpowiedzi"). The curve runs from
/// (<see cref="X0"/>, <see cref="Y0"/>) to (<see cref="X1"/>, <see cref="Y1"/>) and stays flat outside; outputs are never
/// below 0. Integer arithmetic only.
/// </summary>
/// <param name="Kind">Shape between the points.</param>
/// <param name="X0">Input where the curve starts.</param>
/// <param name="Y0">Output at and below <paramref name="X0"/>, per mille.</param>
/// <param name="X1">Input where the curve ends (greater than <paramref name="X0"/>).</param>
/// <param name="Y1">Output at and above <paramref name="X1"/>, per mille.</param>
public readonly record struct ResponseCurve(CurveKind Kind, long X0, int Y0, long X1, int Y1)
{
    /// <summary>A straight line from (x0, y0) to (x1, y1).</summary>
    public static ResponseCurve Linear(long x0, int y0, long x1, int y1) => Checked(new(CurveKind.Linear, x0, y0, x1, y1));

    /// <summary>An S-curve from (x0, y0) to (x1, y1).</summary>
    public static ResponseCurve Smooth(long x0, int y0, long x1, int y1) => Checked(new(CurveKind.Smooth, x0, y0, x1, y1));

    /// <summary><paramref name="below"/> under <paramref name="threshold"/>, <paramref name="from"/> at and above it.</summary>
    public static ResponseCurve Step(long threshold, int below, int from) => Checked(new(CurveKind.Step, threshold - 1, below, threshold, from));

    /// <summary>The factor for <paramref name="x"/>, per mille, 0 or more.</summary>
    public int Evaluate(long x)
    {
        long y;
        if (Kind == CurveKind.Step)
        {
            y = x >= X1 ? Y1 : Y0;
        }
        else if (x <= X0)
        {
            y = Y0;
        }
        else if (x >= X1)
        {
            y = Y1;
        }
        else
        {
            long span = X1 - X0;
            long t = x - X0;
            if (Kind == CurveKind.Smooth)
            {
                // smoothstep 3t^2 - 2t^3 with t in [0, span]: the fraction is kept as a numerator over span^3.
                Int128 s = span;
                Int128 tt = t;
                Int128 num = ((3 * s) - (2 * tt)) * tt * tt;
                y = Y0 + (long)((Y1 - Y0) * num / (s * s * s));
            }
            else
            {
                y = Y0 + ((Y1 - Y0) * t / span);
            }
        }

        return (int)Math.Max(0, y);
    }

    private static ResponseCurve Checked(ResponseCurve c) =>
        c.X1 > c.X0 ? c : throw new ArgumentException("A curve needs x1 greater than x0.");
}
