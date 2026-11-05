using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
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

    /// <summary>Number of settlements at the end of the run.</summary>
    public int Settlements { get; init; }

    /// <summary>People in all settlements at the end of the run.</summary>
    public long People { get; init; }

    /// <summary>Civilizations still alive at the end of the run.</summary>
    public int AliveCivilizations { get; init; }

    /// <summary>Food in all warehouses at the end of the run, in whole units.</summary>
    public long FoodUnits { get; init; }

    /// <summary>Game time at the end of the run, in years.</summary>
    public long Years { get; init; }
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
        CoreSystems.Register(runner, content);
        var turnMs = new double[Math.Max(1, options.Turns)];
        long totalAllocated = 0;

        using StreamWriter? csv = options.ReportPath is null ? null : CreateReport(options.ReportPath);
        csv?.WriteLine(CsvHeader());
        using StreamWriter? stats = options.StatsPath is null ? null : CreateReport(options.StatsPath);
        if (stats is not null)
        {
            WriteStatsHeader(stats, state);
            WriteStats(stats, state, content);
        }

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

            if (stats is not null && (runner.State.Turn % options.StatsEvery == 0 || i == options.Turns - 1))
            {
                WriteStats(stats, runner.State, content);
            }

            if (!options.Quiet)
            {
                foreach (string line in runner.LastChronicle)
                {
                    log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  [turn {runner.State.Turn}] {line}"));
                }
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
        else if (options.PngFolder is not null)
        {
            // After the run: where the tribes live and how much wild food they left.
            var camps = new List<(HexCoord, bool)>();
            foreach (SettlementState s in runner.State.Settlements)
            {
                camps.Add((s.Cell, s.TotalPopulation() > 0));
            }

            var end = new MapRenderer(runner.State.Worlds[0], content, [], options.PngScale) { Settlements = camps };
            string prefix = string.Create(CultureInfo.InvariantCulture, $"seed{options.Seed}_{options.MapSize.ToString().ToLowerInvariant()}_turn{runner.State.Turn}");
            pngs = [.. pngs, .. end.Save(options.PngFolder, prefix, [MapView.Influence, MapView.WildFood])];
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
            Settlements = runner.State.Settlements.Count,
            People = People(runner.State),
            AliveCivilizations = runner.State.Civilizations.Count(c => c.IsAlive),
            FoodUnits = FoodUnits(runner.State, content),
            Years = runner.State.Month / 12,
        };
    }

    private static long People(GameState state)
    {
        long people = 0;
        foreach (SettlementState s in state.Settlements)
        {
            people += s.TotalPopulation();
        }

        return people;
    }

    // One row: turn, year, total people, then people of each civilization, then total food in stock.
    private static void WriteStatsHeader(StreamWriter stats, GameState state)
    {
        stats.Write("turn,year,people,alive");
        foreach (CivilizationState c in state.Civilizations)
        {
            stats.Write(string.Create(CultureInfo.InvariantCulture, $",civ{c.Id.Value}"));
        }

        stats.WriteLine(",food_stock,avg_health,avg_mood,camps,wild_food_permille");
    }

    private static void WriteStats(StreamWriter stats, GameState state, GameContent content)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        var perCiv = new long[state.Civilizations.Count];
        long health = 0;
        long mood = 0;
        int populated = 0;
        foreach (SettlementState s in state.Settlements)
        {
            int people = s.TotalPopulation();
            int index = state.IndexOfCivilization(s.Owner);
            if (index >= 0)
            {
                perCiv[index] += people;
            }

            if (people > 0)
            {
                health += s.Health;
                mood += s.Mood;
                populated++;
            }
        }

        long total = 0;
        foreach (long p in perCiv)
        {
            total += p;
        }

        stats.Write(string.Create(inv, $"{state.Turn},{state.Month / 12},{total},{state.Civilizations.Count(c => c.IsAlive)}"));
        foreach (long p in perCiv)
        {
            stats.Write(string.Create(inv, $",{p}"));
        }

        // Average wild food left on land cells that have any food (how hard the land is used).
        long wild = 0;
        long landCells = 0;
        Core.Map.WorldMap map = state.Worlds[0].Map;
        for (int i = 0; i < map.WildFood.Length; i++)
        {
            if (content.Biomes[map.Biome[i]].Food > 0 && !content.Biomes[map.Biome[i]].IsWater)
            {
                wild += map.WildFood[i];
                landCells++;
            }
        }

        stats.WriteLine(string.Create(
            inv, $",{FoodUnits(state, content)},{(populated == 0 ? 0 : health / populated)},{(populated == 0 ? 0 : mood / populated)},{populated},{(landCells == 0 ? 0 : wild / landCells)}"));
    }

    private static long FoodUnits(GameState state, GameContent content)
    {
        long hundredths = 0;
        foreach (SettlementState s in state.Settlements)
        {
            ReadOnlySpan<Core.Numerics.Amount> stock = s.StockView;
            for (int g = 0; g < stock.Length; g++)
            {
                if (content.Goods[g].IsFood)
                {
                    hundredths += stock[g].Hundredths;
                }
            }
        }

        return hundredths / Core.Numerics.Amount.Scale;
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
