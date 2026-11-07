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

    /// <summary>Relations at the end of the run among living civilizations: met (both ways counted) and rumors only.</summary>
    public (int Met, int Rumors) Contacts { get; init; }

    /// <summary>Diplomacy over the run (stage 4d): what happened and what is in force at the end.</summary>
    public TreatyTally Treaties { get; init; } = new();

    /// <summary>Number of settlements at the end of the run.</summary>
    public int Settlements { get; init; }

    /// <summary>People in all settlements at the end of the run.</summary>
    public long People { get; init; }

    /// <summary>Civilizations still alive at the end of the run.</summary>
    public int AliveCivilizations { get; init; }

    /// <summary>Average stability over civilization-years, the percent of civilization-years in unrest and the capitals lost (stage 4f PR 3).</summary>
    public (int Average, int UnrestPercent, int CapitalsLost) Stability { get; init; }

    /// <summary>Leaders who came to power after the first ones, and how many years a leader ruled on average (stage 4f PR 2).</summary>
    public (long Successions, long AverageReignYears) Leaders { get; init; }

    /// <summary>Living civilizations by government at the end of the run, e.g. "7 Chiefdom, 5 Council of Elders" (stage 4f).</summary>
    public string Governments { get; init; } = string.Empty;

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
        GameState state = GameState.CreateNew(options.ToSettings(content), content, out WorldGenReport world);
        GameSettings settings = state.Settings;
        foreach (string note in options.PlanetNotes)
        {
            log.WriteLine($"note:               {note}");
        }

        foreach (string line in PlanetSummary.Lines(settings.Planet))
        {
            log.WriteLine(line);
        }

        List<string> problems = WorldValidator.Validate(
            content, state.Worlds[0].Map, world, WorldGenOptions.From(settings));
        IReadOnlyList<string> pngs = [];
        if (options.PngFolder is not null)
        {
            var renderer = new MapRenderer(state.Worlds[0], content, world.Starts, options.PngScale);
            pngs = renderer.SaveAll(options.PngFolder, string.Create(CultureInfo.InvariantCulture, $"seed{options.Seed}_{options.MapSize.ToString().ToLowerInvariant()}"));
        }

        var runner = new TurnRunner(state);
        Core.Ai.AiCouncil council = CoreSystems.Register(runner, content);
        var turnMs = new double[Math.Max(1, options.Turns)];
        long totalAllocated = 0;

        using StreamWriter? csv = options.ReportPath is null ? null : CreateReport(options.ReportPath);
        csv?.WriteLine(CsvHeader());
        using StreamWriter? stats = options.StatsPath is null ? null : CreateReport(options.StatsPath);
        using StreamWriter? decisions = options.DecisionsPath is null ? null : CreateReport(options.DecisionsPath);
        decisions?.WriteLine(DecisionCsv.Header);
        using StreamWriter? relations = options.RelationsPath is null ? null : CreateReport(options.RelationsPath);
        relations?.WriteLine(RelationCsv.Header);
        using StreamWriter? treatyCsv = options.TreatiesPath is null ? null : CreateReport(options.TreatiesPath);
        treatyCsv?.WriteLine(TreatyCsv.Header);
        using StreamWriter? warFile = options.WarsPath is null ? null : CreateReport(options.WarsPath);
        WarCsv? warCsv = warFile is null ? null : new WarCsv(warFile);
        warCsv?.Start();
        using StreamWriter? campaignFile = options.CampaignsPath is null ? null : CreateReport(options.CampaignsPath);
        CampaignCsv? campaignCsv = campaignFile is null ? null : new CampaignCsv(campaignFile);
        campaignCsv?.Start();
        var tally = new TreatyTally();
        var stabilityTally = new StabilityTally(runner.State.Month);
        if (stats is not null)
        {
            WriteStatsHeader(stats, state, content);
            WriteStats(stats, state, content);
        }

        // With live seasons the last planet year of the run is drawn season by season.
        bool live = runner.State.Worlds[0].Map.LiveSeasons || content.Seasons.Live(runner.State.Settings.Planet);
        int yearFrames = live && options.PngFolder is not null ? content.Seasons.StepsPerYear : 0;
        var frames = new List<RgbImage>();
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

            if (decisions is not null)
            {
                DecisionCsv.Write(decisions, council.Log, runner.State);
            }

            if (stats is not null && (runner.State.Turn % options.StatsEvery == 0 || i == options.Turns - 1))
            {
                WriteStats(stats, runner.State, content);
            }

            if (relations is not null && (runner.State.Turn % options.StatsEvery == 0 || i == options.Turns - 1))
            {
                RelationCsv.Write(relations, runner.State);
            }

            tally.Count(runner.LastChronicle);
            stabilityTally.Count(runner.State, content);
            warCsv?.Ended(runner.State);
            campaignCsv?.Seen(runner.State);
            if (treatyCsv is not null && (runner.State.Turn % options.StatsEvery == 0 || i == options.Turns - 1))
            {
                TreatyCsv.Write(treatyCsv, runner.State);
            }

            if (!options.Quiet)
            {
                foreach (string line in runner.LastChronicle)
                {
                    log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  [turn {runner.State.Turn}] {line}"));
                }
            }

            if (yearFrames > 0 && i >= options.Turns - yearFrames)
            {
                var camps = new List<(HexCoord, bool)>();
                foreach (SettlementState s in runner.State.Settlements)
                {
                    if (s.IsActive)
                    {
                        camps.Add((s.Cell, true));
                    }
                }

                frames.Add(new MapRenderer(runner.State.Worlds[0], content, [], 1) { Settlements = camps }.Render(MapView.SeasonNow));
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
            pngs = [.. pngs, .. end.Save(options.PngFolder, prefix, [MapView.Biomes, MapView.Temperature, MapView.Influence, MapView.WildFood, MapView.SeasonNow])];
            if (frames.Count > 0)
            {
                string path = Path.Combine(options.PngFolder, prefix + "_year.png");
                PngWriter.Save(MapRenderer.Strip(frames), path);
                pngs = [.. pngs, path];
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

        int n = turnMs.Length;
        warCsv?.Ongoing(runner.State);
        campaignCsv?.Ongoing();
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
            Leaders = LeaderTally(runner.State),
            Governments = string.Join(", ", runner.State.Civilizations.Where(c => c.IsAlive)
                .GroupBy(c => Core.Governance.Governments.Of(content, c)?.Name ?? "none")
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Count()} {g.Key}"))),
            FoodUnits = FoodUnits(runner.State, content),
            Years = runner.State.Month / 12,
            Contacts = CountContacts(runner.State),
            Treaties = tally.Finish(runner.State),
            Stability = stabilityTally.Finish(),
        };
    }

    // Stability weighed by the months each turn moved the world (a month of war counts as much as a month of peace).
    private sealed class StabilityTally(long startMonth)
    {
        private long _month = startMonth;
        private long _sum;
        private long _months;
        private long _unrest;
        private int _lost;

        public void Count(GameState state, GameContent content)
        {
            long months = state.Month - _month;
            _month = state.Month;
            foreach (CivilizationState c in state.Civilizations)
            {
                if (!c.IsAlive)
                {
                    continue;
                }

                _sum += c.Stability * months;
                _months += months;
                _unrest += c.Stability < content.Stability.UnrestBelow ? months : 0;
                _lost += c.CapitalLostUntilMonth == state.Month + (content.Stability.CapitalLostYears * 12L) ? 1 : 0;
            }
        }

        public (int, int, int) Finish() => _months == 0 ? (0, 0, _lost) : ((int)(_sum / _months), (int)(_unrest * 100 / _months), _lost);
    }

    private static (long Successions, long AverageReignYears) LeaderTally(GameState state)
    {
        long leaders = state.Civilizations.Sum(c => c.Leaders);
        long successions = Math.Max(0, leaders - state.Civilizations.Count);
        long years = state.Month / 12 * state.Civilizations.Count;
        return (successions, leaders == 0 ? 0 : years / leaders);
    }

    private static (int Met, int Rumors) CountContacts(GameState state)
    {
        int met = 0;
        int rumors = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            foreach (Core.Diplomacy.Relation r in c.Relations)
            {
                if (c.IsAlive && state.FindCivilization(r.Other)?.IsAlive == true)
                {
                    met += r.HasMet ? 1 : 0;
                    rumors += r.HasMet ? 0 : 1;
                }
            }
        }

        return (met, rumors);
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
    private static void WriteStatsHeader(StreamWriter stats, GameState state, GameContent content)
    {
        stats.Write("turn,year,people,alive");
        foreach (CivilizationState c in state.Civilizations)
        {
            stats.Write(string.Create(CultureInfo.InvariantCulture, $",civ{c.Id.Value}"));
        }

        stats.Write(",food_stock,avg_health,avg_mood,camps,wild_food_permille,mean_temp_c,ice_north_permille,ice_south_permille,co2_ppm,sea_level_m,higher_needs_permille,crafting_camps,valuables,materials,techs_avg,techs_max,era_max,science_per_turn_avg,villages,village_people,farmers,journeys,known_cells_avg,settlements,spread_cells");
        foreach (SkillDefinition k in content.Skills)
        {
            stats.Write(string.Create(CultureInfo.InvariantCulture, $",skilled_{k.Key}"));
        }

        stats.WriteLine(",trainees,stability_avg,unrest_civs");
    }

    private static void WriteStats(StreamWriter stats, GameState state, GameContent content)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        var perCiv = new long[state.Civilizations.Count];
        long health = 0;
        long mood = 0;
        int populated = 0;
        long higher = 0;
        int crafting = 0;
        long valuables = 0;
        long materials = 0;
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
                higher += s.HigherNeedsPerMille;
                crafting += s.RunsMade > 0 ? 1 : 0;
                populated++;
            }

            foreach (GoodDefinition g in content.Goods)
            {
                valuables += g.Category == GoodCategory.Valuable ? s.StockView[g.Id].Hundredths : 0;
                materials += g.Category == GoodCategory.Bulk ? s.StockView[g.Id].Hundredths : 0;
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

        stats.Write(string.Create(
            inv, $",{FoodUnits(state, content)},{(populated == 0 ? 0 : health / populated)},{(populated == 0 ? 0 : mood / populated)},{populated},{(landCells == 0 ? 0 : wild / landCells)}"
            + $",{state.Worlds[0].Climate.GlobalMeanMillionths / 100_000 / 10.0:F1},{state.Worlds[0].Climate.NorthIce / 1000},{state.Worlds[0].Climate.SouthIce / 1000},{state.Worlds[0].Climate.Co2Microbar},{(state.Worlds[0].Climate.SeaLevel - Core.WorldGen.WorldGenerator.SeaLevel) * content.ClimateCycles.SeaMetersPerStep}"
            + $",{(populated == 0 ? 0 : higher / populated)},{crafting},{valuables / 100},{materials / 100}"
            + $",{TechAverage(state)},{TechMax(state)},{EraMax(state)},{ScienceAverage(state)}"
            + $",{state.Settlements.Count(x => x.IsActive && !content.SettlementLevels[x.Level].Mobile)}"
            + $",{state.Settlements.Where(x => x.IsActive && !content.SettlementLevels[x.Level].Mobile).Sum(x => x.TotalPopulation())}"
            + $",{state.Settlements.Sum(x => x.Farmers)}"
            + $",{state.Settlements.Count(x => x.JourneyTarget >= 0)},{KnownAverage(state, content)}"
            + $",{state.Settlements.Count(x => x.IsActive && x.TotalPopulation() > 0)},{Spread(state)}"));
        long trainees = 0;
        for (int k = 0; k < content.Skills.Count; k++)
        {
            long skilled = 0;
            foreach (SettlementState s in state.Settlements)
            {
                skilled += k < s.SkilledView.Length ? s.SkilledView[k] : 0;
                trainees += k < s.TraineesView.Length ? s.TraineesView[k] : 0;
            }

            stats.Write(string.Create(inv, $",{skilled}"));
        }

        int alive = 0;
        long stability = 0;
        int unrest = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            if (c.IsAlive)
            {
                alive++;
                stability += c.Stability;
                unrest += c.Stability < content.Stability.UnrestBelow ? 1 : 0;
            }
        }

        stats.WriteLine(string.Create(inv, $",{trainees},{(alive == 0 ? 0 : stability / alive)},{unrest}"));
    }

    // How far people live from where their civilization started, cells, averaged over all people (PR C).
    private static long Spread(GameState state)
    {
        long sum = 0;
        long people = 0;
        foreach (SettlementState s in state.Settlements)
        {
            CivilizationState? civ = state.FindCivilization(s.Owner);
            int n = s.TotalPopulation();
            if (!s.IsActive || n == 0 || civ is null || s.World != state.Worlds[0].Id)
            {
                continue;
            }

            sum += (long)state.Worlds[0].Geometry.Distance(s.Cell, civ.StartCell) * n;
            people += n;
        }

        return people == 0 ? 0 : sum / people;
    }

    private static long KnownAverage(GameState state, GameContent content)
    {
        long sum = 0;
        int alive = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            if (c.IsAlive)
            {
                alive++;
                for (int i = 0; i < c.SeenYear.Length; i++)
                {
                    sum += Core.Perception.MapMemory.Knows(content, state, c, i) ? 1 : 0;
                }
            }
        }

        return alive == 0 ? 0 : sum / alive;
    }

    private static string TechAverage(GameState state)
    {
        int alive = 0;
        long known = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            if (c.IsAlive)
            {
                alive++;
                known += c.KnownCount;
            }
        }

        return alive == 0 ? "0" : (known / (double)alive).ToString("F1", CultureInfo.InvariantCulture);
    }

    private static int TechMax(GameState state) => state.Civilizations.Where(c => c.IsAlive).Select(c => c.KnownCount).DefaultIfEmpty(0).Max();

    private static int EraMax(GameState state) => state.Civilizations.Where(c => c.IsAlive).Select(c => c.Era).DefaultIfEmpty(0).Max();

    private static long ScienceAverage(GameState state)
    {
        long[] science = state.Civilizations.Where(c => c.IsAlive).Select(c => c.LastScience).ToArray();
        return science.Length == 0 ? 0 : science.Sum() / science.Length / 100;
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
