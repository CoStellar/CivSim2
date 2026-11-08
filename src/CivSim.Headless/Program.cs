using System;
using System.Globalization;
using System.Linq;
using CivSim.Core;

namespace CivSim.Headless;

/// <summary>Entry point for running the simulation without a window.</summary>
public static class Program
{
    /// <summary>Runs the headless simulation. Exit code 0 = success, 2 = bad arguments.</summary>
    public static int Main(string[] args)
    {
        HeadlessOptions? options = HeadlessOptions.Parse(args, out string? error);
        if (options is null)
        {
            if (!string.IsNullOrEmpty(error))
            {
                Console.Error.WriteLine(error);
            }

            Console.Error.WriteLine(HeadlessOptions.Usage);
            return string.IsNullOrEmpty(error) ? 0 : 2;
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{CoreInfo.Name} headless: seed {options.Seed}, {options.Turns} turns, map {options.MapSize}"));

        HeadlessResult r = HeadlessRun.Execute(options, Console.Out);

        CultureInfo inv = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Create(inv, $"turns:              {r.Turns}"));
        Console.WriteLine(string.Create(inv, $"state hash:         {r.StateHash:X16}"));
        Console.WriteLine(string.Create(inv, $"turn time avg:      {r.AverageTurnMs:F4} ms"));
        Console.WriteLine(string.Create(inv, $"turn time p95:      {r.P95TurnMs:F4} ms"));
        Console.WriteLine(string.Create(inv, $"turn time max:      {r.MaxTurnMs:F4} ms"));
        Console.WriteLine(string.Create(inv, $"allocated per turn: {r.AllocatedBytesPerTurn} bytes"));
        Console.WriteLine(string.Create(inv, $"managed memory:     {r.ManagedMemoryBytes / 1024.0 / 1024.0:F1} MB"));
        Console.WriteLine(string.Create(inv, $"working set:        {r.WorkingSetBytes / 1024.0 / 1024.0:F1} MB"));
        Console.WriteLine(string.Create(inv, $"game time:          {r.Years} years"));
        Console.WriteLine(string.Create(inv, $"settlements:        {r.Settlements}, food in stock {r.FoodUnits}"));
        Console.WriteLine(string.Create(inv, $"people:             {r.People}, civilizations alive {r.AliveCivilizations}"));
        Console.WriteLine(string.Create(inv, $"governments:        {r.Governments}"));
        Console.WriteLine(string.Create(inv, $"leaders:            {r.Leaders.Successions} successions, about {r.Leaders.AverageReignYears} years per reign"));
        Console.WriteLine(string.Create(inv, $"stability:          average {r.Stability.Average}, unrest in {r.Stability.UnrestPercent}% of civilization-years, {r.Stability.CapitalsLost} capitals lost"));
        Console.WriteLine(string.Create(inv, $"revolts:            {r.Revolts.Secessions} secessions (new civilizations), {r.Revolts.Coups} coups"));
        Console.WriteLine(string.Create(inv, $"contacts:           {r.Contacts.Met} known with contact, {r.Contacts.Rumors} only from rumors (one way each)"));
        TreatyTally t = r.Treaties;
        Console.WriteLine(string.Create(inv, $"diplomacy:          {t.Signed[2]} trade agreements, {t.Signed[3]} pacts, {t.Signed[4]} alliances, {t.Wars} wars declared ({t.Broken} breaking a treaty), {t.Signed[1]} peaces, {t.AlliesJoined} allies joined, {t.AlliesFailed} let down, {t.Epidemics} epidemics carried by merchants"));
        Console.WriteLine(string.Create(inv, $"in force at end:    {t.InForce[0]} wars, {t.InForce[1]} truces, {t.InForce[2]} trade, {t.InForce[3]} pacts, {t.InForce[4]} alliances; goods traded worth {t.TotalWorth / 100} in all"));
        if (r.World is { } w)
        {
            Console.WriteLine(string.Create(inv, $"world gen time:     {w.ElapsedMs:F1} ms"));
            Console.WriteLine(string.Create(inv, $"water:              {w.WaterPercent}%"));
            Console.WriteLine(string.Create(inv, $"rivers / lakes:     {w.Rivers} / {w.Lakes} ({w.RiverEdges} river edges)"));
            Console.WriteLine(string.Create(inv, $"starts:             {w.Starts.Count}, min distance {w.MinStartDistance}, fairness {w.StartFairnessPercent}%"));
            Console.WriteLine(string.Create(inv, $"world validator:    {(r.WorldProblems.Count == 0 ? "ok" : $"{r.WorldProblems.Count} problems")}"));
            foreach (string p in r.WorldProblems.Take(20))
            {
                Console.WriteLine($"  - {p}");
            }
        }

        foreach (string png in r.PngFiles)
        {
            Console.WriteLine($"png:                {png}");
        }

        if (options.ReportPath is not null)
        {
            Console.WriteLine($"report:             {options.ReportPath}");
        }

        return r.WorldProblems.Count == 0 ? 0 : 1;
    }
}
