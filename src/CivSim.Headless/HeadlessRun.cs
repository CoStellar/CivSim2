using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Headless.Png;

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
    long WorkingSetBytes)
{
    /// <summary>Summary of the generated world.</summary>
    public WorldGenReport? World { get; init; }

    /// <summary>PNG files written.</summary>
    public IReadOnlyList<string> PngFiles { get; init; } = [];

    /// <summary>Rule violations found by the world validator (empty = valid).</summary>
    public IReadOnlyList<string> WorldProblems { get; init; } = [];
}

/// <summary>Runs a game without a window and writes the optional CSV report.</summary>
public static class HeadlessRun
{
    /// <summary>Runs <see cref="HeadlessOptions.Turns"/> turns and returns the summary.</summary>
    public static HeadlessResult Execute(HeadlessOptions options, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        GameContent content = ContentLoader.Load(ContentLoader.DefaultDirectory);
        GameState state = GameState.CreateNew(options.ToSettings(), content, out WorldGenReport world);
        GameSettings settings = state.Settings;
        List<string> problems = WorldValidator.Validate(
            content, state.Worlds[0].Map, world, WorldGenOptions.From(settings));
        IReadOnlyList<string> pngs = [];
        if (options.PngFolder is not null)
        {
            var renderer = new MapRenderer(state.Worlds[0], content, world.Starts, options.PngScale);
            pngs = renderer.SaveAll(options.PngFolder, string.Create(CultureInfo.InvariantCulture, $"seed{options.Seed}_{options.MapSize.ToString().ToLowerInvariant()}"));
        }

        var runner = new TurnRunner(state);
        var turnMs = new double[Math.Max(1, options.Turns)];
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

        if (options.Turns == 0)
        {
            turnMs = [];
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

        int n = turnMs.Length;
        return new HeadlessResult(
            runner.State.Turn,
            GameStateHash.Compute(runner.State),
            n == 0 ? 0 : sum / n,
            n == 0 ? 0 : sorted[Math.Min(n - 1, (int)Math.Ceiling(n * 0.95) - 1)],
            n == 0 ? 0 : sorted[^1],
            n == 0 ? 0 : totalAllocated / n,
            managed,
            Environment.WorkingSet)
        {
            World = world,
            PngFiles = pngs,
            WorldProblems = problems,
        };
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
