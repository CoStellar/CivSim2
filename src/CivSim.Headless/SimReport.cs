using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Headless;

/// <summary>
/// The AI of the new game over many seeds (2026-11-19): a small change moves the result of one game by 20–30%, so tuning
/// needs a mean over many. <c>--sim-report [--seeds 12] [--minutes 40] [--peoples 4] [--size 98x60] [--csv file]</c>. The default map is the client's default ("Normal", 98×60; decision 8B of 2026-11-20).
/// </summary>
public static class SimReport
{
    private sealed record Row(ulong Seed, int People, int Settlements, int Units, int Soldiers, int Dead, double Era, int Neolithic, int HungryPercent, int Wars, int Buildings, int Techs)
    {
        /// <summary>Minute the first people entered each era (index = era; −1 = not reached).</summary>
        public int[] FirstEra { get; init; } = [];

        /// <summary>Working buildings (built, with jobs) with nobody in them at the end, percent.</summary>
        public int EmptyPercent { get; init; }

        /// <summary>Working buildings that use goods and stand without them at the end, percent.</summary>
        public int StarvedPercent { get; init; }

        /// <summary>Free jobs in working buildings at the end, all peoples.</summary>
        public int FreeJobs { get; init; }

        /// <summary>Food a minute after rations at the end, mean of the peoples.</summary>
        public double FoodPerMinute { get; init; }

        /// <summary>Peoples whose food falls at the end.</summary>
        public int FoodFalling { get; init; }

        /// <summary>Settlements taken, sacked and razed.</summary>
        public int Taken { get; init; }

        /// <summary>Highest veteran level of a kind, mean over the peoples.</summary>
        public double VeteranLevel { get; init; }

        /// <summary>Peoples at their army limit at the end.</summary>
        public int AtArmyLimit { get; init; }

        /// <summary>Soldiers starved in the field.</summary>
        public int FieldDeaths { get; init; }

        /// <summary>Settlements sacked.</summary>
        public int Sacked { get; init; }

        /// <summary>Settlements razed.</summary>
        public int Razed { get; init; }

        /// <summary>Settlements that are towns or more at the end.</summary>
        public int Towns { get; init; }

        /// <summary>Clothing in store at the end, all peoples.</summary>
        public long Clothing { get; init; }

        /// <summary>The longest tick, milliseconds (wall clock; run with --serial for clean numbers).</summary>
        public double WorstTickMs { get; init; }

        /// <summary>Ticks longer than 25 ms (a tick's share of a second at speed x4).</summary>
        public int SlowTicks { get; init; }

        /// <summary>Peaces made (ADR 0030).</summary>
        public int Peaces { get; init; }

        /// <summary>Worth given in a peace, mean of the peaces (food units).</summary>
        public double PeaceWorth { get; init; }

        /// <summary>Settlements that changed hands in peaces.</summary>
        public int ByPeace { get; init; }

        /// <summary>Highest war weariness at the end, mean of the peoples.</summary>
        public double Weariness { get; init; }

        /// <summary>Wars going on at the end.</summary>
        public int WarsNow { get; init; }
    }

    /// <summary>Runs the report; exit code 0.</summary>
    public static int Execute(string[] args, TextWriter output)
    {
        int seeds = Arg(args, "--seeds", 12);
        int minutes = Arg(args, "--minutes", 40);
        int peoples = Arg(args, "--peoples", 4);
        (int width, int height) = Size(args);
        string? csv = args.SkipWhile(a => a != "--csv").Skip(1).FirstOrDefault();
        SimContent content = SimContent.Load();
        var rows = new Row[seeds];
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = args.Contains("--serial") ? 1 : Environment.ProcessorCount };
        Parallel.For(0, seeds, parallel, k => rows[k] = Play(content, (ulong)(k + 1), minutes, peoples, width, height));

        CultureInfo inv = CultureInfo.InvariantCulture;
        output.WriteLine(string.Create(inv, $"New game AI: {seeds} seeds, {peoples} peoples, {minutes} minutes each (map {width}x{height})."));
        output.WriteLine("seed  people  settl  units  soldiers  dead  era   neolithic  hungry%  wars  buildings  techs");
        foreach (Row r in rows)
        {
            output.WriteLine(string.Create(inv, $"{r.Seed,4}  {r.People,6}  {r.Settlements,5}  {r.Units,5}  {r.Soldiers,8}  {r.Dead,4}  {r.Era,4:F1}  {(r.Neolithic < 0 ? "-" : r.Neolithic.ToString(inv)),9}  {r.HungryPercent,7}  {r.Wars,4}  {r.Buildings,9}  {r.Techs,5}"));
        }

        void Line(string name, Func<Row, double> f)
        {
            double[] v = [.. rows.Select(f)];
            double mean = v.Average();
            double sd = Math.Sqrt(v.Select(x => (x - mean) * (x - mean)).Average());
            double[] sorted = [.. v.OrderBy(x => x)];
            output.WriteLine(string.Create(inv, $"{name,-22} mean {mean,8:F1}   median {sorted[sorted.Length / 2],8:F1}   spread ±{sd,7:F1}   (±{(mean == 0 ? 0 : sd * 100 / mean),3:F0}%)"));
        }

        output.WriteLine();
        Line("people", r => r.People);
        Line("settlements", r => r.Settlements);
        Line("units", r => r.Units);
        Line("soldiers (people)", r => r.Soldiers);
        Line("peoples died out", r => r.Dead);
        Line("era (mean of peoples)", r => r.Era);
        Line("hungry % of samples", r => r.HungryPercent);
        Line("wars declared", r => r.Wars);
        Line("buildings", r => r.Buildings);
        Line("empty working bldgs %", r => r.EmptyPercent);
        Line("bldgs short of inputs %", r => r.StarvedPercent);
        Line("free jobs", r => r.FreeJobs);
        Line("food/min per people", r => r.FoodPerMinute);
        Line("peoples losing food", r => r.FoodFalling);
        Line("towns (level 2+)", r => r.Towns);
        Line("settlements taken", r => r.Taken);
        Line("settlements sacked", r => r.Sacked);
        Line("settlements razed", r => r.Razed);
        Line("peaces made", r => r.Peaces);
        Line("worth per peace", r => r.PeaceWorth);
        Line("settlements by peace", r => r.ByPeace);
        Line("wars at the end", r => r.WarsNow);
        Line("war weariness at end", r => r.Weariness);
        Line("soldiers starved in field", r => r.FieldDeaths);
        Line("top veteran level", r => r.VeteranLevel);
        Line("peoples at army limit", r => r.AtArmyLimit);
        Line("clothing in store", r => r.Clothing);
        Line("worst tick (ms)", r => r.WorstTickMs);
        Line("ticks over 25 ms", r => r.SlowTicks);
        Line("technologies known", r => r.Techs);
        var neo = rows.Where(r => r.Neolithic >= 0).Select(r => (double)r.Neolithic).ToList();
        if (neo.Count > 0)
        {
            output.WriteLine(string.Create(inv, $"{"first Neolithic (min)",-22} mean {neo.Average(),8:F1}   in {neo.Count} of {seeds} games"));
        }

        // When the first people of each game enters each era.
        for (int e = 2; e < content.Eras.Count; e++)
        {
            int era = e;
            var at = rows.Where(r => r.FirstEra.Length > era && r.FirstEra[era] >= 0).Select(r => (double)r.FirstEra[era]).OrderBy(x => x).ToList();
            output.WriteLine(at.Count == 0
                ? $"first {content.Eras[era].Name,-16} not reached in {minutes} min"
                : string.Create(inv, $"{"first " + content.Eras[era].Name + " (min)",-22} mean {at.Average(),8:F1}   median {at[at.Count / 2],8:F1}   in {at.Count} of {seeds} games"));
        }

        if (csv is not null)
        {
            using var w = new StreamWriter(csv);
            w.WriteLine("seed,people,settlements,units,soldiers,dead,era,neolithic,hungry_percent,wars,buildings,techs");
            foreach (Row r in rows)
            {
                w.WriteLine(string.Create(inv, $"{r.Seed},{r.People},{r.Settlements},{r.Units},{r.Soldiers},{r.Dead},{r.Era:F2},{r.Neolithic},{r.HungryPercent},{r.Wars},{r.Buildings},{r.Techs}"));
            }

            output.WriteLine($"csv: {csv}");
        }

        return 0;
    }

    // --size WxH (legacy width and height, as the client's map sizes: 66x40 small, 98x60 normal, 130x80 large).
    private static (int Width, int Height) Size(string[] args)
    {
        int i = Array.IndexOf(args, "--size");
        if (i >= 0 && i + 1 < args.Length)
        {
            string[] p = args[i + 1].Split('x', 'X');
            if (p.Length == 2 && int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h))
            {
                return (w, h);
            }
        }

        return (98, 60);
    }

    private static Row Play(SimContent content, ulong seed, int minutes, int peoples, int width, int height)
    {
        SimGame g = SimGame.Create(content, new SimSettings(seed, width, height, peoples));
        int samples = 0;
        double worstTick = 0;
        int slowTicks = 0;
        int hungry = 0;
        int neolithic = -1;
        int[] firstEra = [.. Enumerable.Repeat(-1, content.Eras.Count)];
        int wars = 0;
        var atWar = new HashSet<(int, int)>();
        for (int sec = 1; sec <= minutes * 60; sec++)
        {
            for (int t = 0; t < SimContent.TicksPerSecond; t++)
            {
                long started = Stopwatch.GetTimestamp();
                g.Tick();
                double ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                worstTick = Math.Max(worstTick, ms);
                slowTicks += ms > 25 ? 1 : 0;
            }

            g.News.Clear();
            if (sec % 10 != 0)
            {
                continue;
            }

            foreach (Civ c in g.Civs.Where(c => !c.Dead && c.Settlements.Count > 0))
            {
                samples++;
                hungry += c.Hungry ? 1 : 0;
                if (c.Era > 0 && neolithic < 0)
                {
                    neolithic = sec / 60;
                }

                for (int e = 1; e <= c.Era && e < firstEra.Length; e++)
                {
                    firstEra[e] = firstEra[e] < 0 ? sec / 60 : firstEra[e];
                }

                foreach (Civ o in g.Civs.Where(o => o.Id > c.Id))
                {
                    if (g.AtWar(c.Id, o.Id) && atWar.Add((c.Id, o.Id)))
                    {
                        wars++;
                    }
                    else if (!g.AtWar(c.Id, o.Id))
                    {
                        atWar.Remove((c.Id, o.Id));
                    }
                }
            }
        }

        var alive = g.Civs.Where(c => !c.Dead && c.Settlements.Count > 0).ToList();
        int units = 0;
        int soldiers = 0;
        foreach (Civ c in alive)
        {
            foreach (int id in c.Units)
            {
                Unit u = g.Units[id];
                if (u.State != UnitState.Gone)
                {
                    units++;
                    soldiers += content.Units[u.Kind].Army ? u.People : 0;
                }
            }
        }

        // Economy at the end: empty working buildings and free jobs (2026-11-19 tests: A1).
        int working = 0;
        int empty = 0;
        int free = 0;
        int users = 0;
        int starved = 0;
        foreach (Civ c in alive)
        {
            foreach (int id in c.Buildings)
            {
                Building b = g.Buildings[id];
                int jobs = content.Buildings[b.Type].Workers;
                if (b.State != BuildingState.Active || jobs == 0)
                {
                    continue;
                }

                working++;
                empty += b.Workers == 0 ? 1 : 0;
                free += Math.Max(0, jobs - b.Workers);
                if (Array.Exists(content.Buildings[b.Type].Consumes, x => x > 0) && b.Workers > 0)
                {
                    users++;
                    starved += b.Starved ? 1 : 0;
                }
            }
        }

        return new Row(
            seed,
            alive.Sum(c => c.Settlements.Sum(s => g.Settlements[s].People)),
            alive.Sum(c => c.Settlements.Count),
            units,
            soldiers,
            g.Civs.Count - alive.Count,
            alive.Count == 0 ? 0 : alive.Average(c => c.Era),
            neolithic,
            samples == 0 ? 0 : hungry * 100 / samples,
            wars,
            alive.Sum(c => c.Buildings.Count),
            alive.Count == 0 ? 0 : (int)alive.Average(c => c.KnownTech.Count(k => k)))
        {
            FirstEra = firstEra,
            EmptyPercent = working == 0 ? 0 : empty * 100 / working,
            StarvedPercent = users == 0 ? 0 : starved * 100 / users,
            FreeJobs = free,
            FoodPerMinute = alive.Count == 0 ? 0 : alive.Average(c => c.Rate[content.Food] / (double)SimContent.One),
            FoodFalling = alive.Count(c => c.Rate[content.Food] < 0),
            Taken = g.Falls[0],
            Sacked = g.Falls[1],
            Razed = g.Falls[2],
            FieldDeaths = g.FieldDeaths,
            VeteranLevel = alive.Count == 0 ? 0 : alive.Average(c => c.KindLevel.Max()),
            AtArmyLimit = alive.Count(c => g.ArmyUnits(c) >= g.ArmyLimit(c)),
            Towns = alive.Sum(c => c.Settlements.Count(s => g.Settlements[s].Level >= 2)),
            Clothing = content.Clothing < 0 ? 0 : alive.Sum(c => c.Stock[content.Clothing]) / SimContent.One,
            WorstTickMs = worstTick,
            SlowTicks = slowTicks,
            Peaces = g.PeacesMade,
            PeaceWorth = g.PeacesMade == 0 ? 0 : g.PeaceWorthGiven / (double)SimContent.One / g.PeacesMade,
            ByPeace = g.SettlementsByPeace,
            Weariness = alive.Count == 0 ? 0 : alive.Average(c => g.Weariness(c)),
            WarsNow = alive.Sum(c => g.WarsOf(c.Id)) / 2,
        };
    }

    private static int Arg(string[] args, string name, int fallback)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
    }
}
