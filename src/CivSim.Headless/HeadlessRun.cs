using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CivSim.Core.Hashing;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Headless;

/// <summary>Result of a headless run.</summary>
public sealed record HeadlessResult(
    long Turns,
    ulong StateHash,
    double AverageTurnMs,
    double P95TurnMs,
    double MaxTurnMs,
    long AllocatedBytesPerTurn,
    long ManagedMemoryBytes,
    long WorkingSetBytes);

/// <summary>Runs a game without a window and writes the optional CSV report.</summary>
public static class HeadlessRun
{
    /// <summary>Runs <see cref="HeadlessOptions.Turns"/> turns and returns the summary.</summary>
    public static HeadlessResult Execute(HeadlessOptions options, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var runner = new TurnRunner(GameState.CreateNew(MapSizes.Settings(options.MapSize, options.Seed)));
        var turnMs = new double[options.Turns];
        long totalAllocated = 0;

        using StreamWriter? csv = options.ReportPath is null ? null : CreateReport(options.ReportPath);
        csv?.WriteLine(CsvHeader());

        for (int i = 0; i < options.Turns; i++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            TurnReport report = runner.RunTurn();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            totalAllocated += allocated;
            turnMs[i] = Stopwatch.GetElapsedTime(0, report.TotalTicks).TotalMilliseconds;

            if (csv is not null)
            {
                WriteRow(csv, report, allocated);
            }

            if (!options.Quiet && (i + 1) % 1000 == 0)
            {
                log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  turn {i + 1}/{options.Turns}"));
            }
        }

        double[] sorted = (double[])turnMs.Clone();
        Array.Sort(sorted);
        double sum = 0;
        foreach (double ms in turnMs)
        {
            sum += ms;
        }

        long managed = GC.GetTotalMemory(forceFullCollection: true);
        GC.KeepAlive(runner); // measure memory while the game is still alive

        return new HeadlessResult(
            runner.State.Turn,
            GameStateHash.Compute(runner.State),
            sum / turnMs.Length,
            sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)],
            sorted[^1],
            totalAllocated / options.Turns,
            managed,
            Environment.WorkingSet);
    }

    /// <summary>The CSV header line.</summary>
    public static string CsvHeader()
    {
        var parts = new string[TurnPhases.Count + 3];
        parts[0] = "turn";
        parts[1] = "total_ms";
        for (int p = 0; p < TurnPhases.Count; p++)
        {
            parts[p + 2] = ((TurnPhase)p).ToString().ToLowerInvariant() + "_ms";
        }

        parts[^1] = "allocated_bytes";
        return string.Join(',', parts);
    }

    private static StreamWriter CreateReport(string path)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        return new StreamWriter(path) { NewLine = "\n" };
    }

    private static void WriteRow(StreamWriter csv, TurnReport report, long allocated)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        csv.Write(report.Turn.ToString(inv));
        csv.Write(',');
        csv.Write(Stopwatch.GetElapsedTime(0, report.TotalTicks).TotalMilliseconds.ToString("F4", inv));
        for (int p = 0; p < TurnPhases.Count; p++)
        {
            csv.Write(',');
            csv.Write(report.PhaseDuration((TurnPhase)p).TotalMilliseconds.ToString("F4", inv));
        }

        csv.Write(',');
        csv.WriteLine(allocated.ToString(inv));
    }
}
