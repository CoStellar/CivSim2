using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CivSim.Core.Diplomacy;

namespace CivSim.Headless;

/// <summary>
/// Batch tests of the AI (GDD 8.7; stage 4g): many games without a window on consecutive seeds, one CSV row per game with
/// what the AI did. Games run in parallel; each is the same as a single run of its seed, and the rows are written in seed order.
/// </summary>
public static class BatchRun
{
    /// <summary>Column names of the batch CSV.</summary>
    public const string Header =
        "seed,turns,years,alive_end,peak_alive,died_out,first_death_year,secessions,coups,government_changes,max_era,era_years," +
        "wars,wars_loot,wars_land,wars_revenge,wars_breaking_treaty,peaces,trade,pacts,alliances,allies_joined,allies_failed," +
        "conquests,stability_avg,unrest_percent,capitals_lost,hungry_settlement_years,hunger_without_reaction,village_hunger," +
        "settlements,people,leaders,avg_turn_ms,max_turn_ms,state_hash";

    /// <summary>Runs the batch and writes the CSV. Returns the exit code (0 = every world valid).</summary>
    public static int Execute(HeadlessOptions options, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);
        var rows = new string[options.Batch];
        int problems = 0;
        int done = 0;
        object gate = new();
        log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"batch: {options.Batch} games from seed {options.Seed}, {options.Turns} turns, map {options.MapSize}, {options.Jobs} at once"));
        Parallel.For(0, options.Batch, new ParallelOptions { MaxDegreeOfParallelism = options.Jobs }, k =>
        {
            ulong seed = options.Seed + (ulong)k;
            HeadlessOptions one = options with
            {
                Seed = seed,
                Quiet = true,
                ReportPath = null,
                StatsPath = null,
                DecisionsPath = null,
                RelationsPath = null,
                TreatiesPath = null,
                WarsPath = null,
                CampaignsPath = null,
                PngFolder = null,
                Batch = 0,
                BatchPath = null,
            };
            HeadlessResult r = HeadlessRun.Execute(one, TextWriter.Null);
            rows[k] = Row(seed, r);
            if (r.WorldProblems.Count > 0)
            {
                Interlocked.Increment(ref problems);
            }

            int n = Interlocked.Increment(ref done);
            lock (gate)
            {
                log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  game {n}/{options.Batch}: seed {seed}, {r.Years} years, {r.AliveCivilizations} alive, {r.Treaties.Wars} wars"));
            }
        });

        using (var writer = new StreamWriter(options.BatchPath!, false, new UTF8Encoding(false)))
        {
            writer.WriteLine(Header);
            foreach (string row in rows)
            {
                writer.WriteLine(row);
            }
        }

        log.WriteLine($"batch csv:          {options.BatchPath}");
        return problems == 0 ? 0 : 1;
    }

    private static string Row(ulong seed, HeadlessResult r)
    {
        GameTally t = r.Tally!;
        TreatyTally d = r.Treaties;
        int maxEra = 0;
        var eras = new StringBuilder();
        for (int e = 0; e < t.EraYears.Length; e++)
        {
            if (t.EraYears[e] >= 0)
            {
                maxEra = e;
                eras.Append(eras.Length > 0 ? ";" : string.Empty).Append(t.EraYears[e].ToString(CultureInfo.InvariantCulture));
            }
        }

        int wars = t.WarsByGoal[(int)WarGoal.Loot] + t.WarsByGoal[(int)WarGoal.Land] + t.WarsByGoal[(int)WarGoal.Revenge];
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{seed},{r.Turns},{r.Years},{r.AliveCivilizations},{t.PeakAlive},{t.DiedOut},{t.FirstDeathYear},{r.Revolts.Secessions},{r.Revolts.Coups},{t.GovernmentChanges},{maxEra},{eras}," +
            $"{wars},{t.WarsByGoal[(int)WarGoal.Loot]},{t.WarsByGoal[(int)WarGoal.Land]},{t.WarsByGoal[(int)WarGoal.Revenge]},{d.Broken},{d.Signed[1]},{d.Signed[2]},{d.Signed[3]},{d.Signed[4]},{d.AlliesJoined},{d.AlliesFailed}," +
            $"{t.Conquests},{r.Stability.Average},{r.Stability.UnrestPercent},{r.Stability.CapitalsLost},{t.HungryYears},{t.HungerWithoutReaction},{t.VillageHungerSteps}," +
            $"{r.Settlements},{r.People},{r.Leaders.Successions},{r.AverageTurnMs:F2},{r.MaxTurnMs:F1},{r.StateHash:X16}");
    }
}
