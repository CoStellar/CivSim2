using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Headless;

/// <summary>
/// A test scenario without Godot (2026-11-19 tests): <c>--sim-scenario key [--minutes 10] [--ai-all]</c> plays it and prints,
/// every minute, each people's army and land, and at the end the time a tick took. <c>--ai-all</c> lets the AI lead every
/// people, the player's too.
/// </summary>
public static class SimScenarioRun
{
    /// <summary>Runs the scenario; exit code 0, 2 when it is unknown.</summary>
    public static int Execute(string[] args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        string key = args.SkipWhile(a => a != "--sim-scenario").Skip(1).FirstOrDefault() ?? "";
        int minutes = int.TryParse(args.SkipWhile(a => a != "--minutes").Skip(1).FirstOrDefault(), out int m) ? m : 10;
        SimContent content = SimContent.Load();
        SimScenario? scenario = SimScenario.LoadAll(SimScenario.Folder()).FirstOrDefault(s => s.Key == key || s.Key.StartsWith(key, StringComparison.Ordinal));
        if (scenario is null)
        {
            output.WriteLine($"No scenario '{key}'. Known: {string.Join(", ", SimScenario.LoadAll(SimScenario.Folder()).Select(s => s.Key))}");
            return 2;
        }

        SimGame g = SimGame.FromScenario(content, scenario);
        if (args.Contains("--ai-all"))
        {
            foreach (Civ c in g.Civs)
            {
                c.IsAi = true;
            }
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        output.WriteLine($"Scenario {scenario.Key}: {scenario.Name}, {minutes} min.");
        double totalMs = 0;
        double worst = 0;
        long worstTick = 0;
        int slow = 0;
        long ticks = 0;
        for (int minute = 1; minute <= minutes; minute++)
        {
            for (int t = 0; t < SimContent.TicksPerMinute; t++)
            {
                long start = Stopwatch.GetTimestamp();
                g.Tick();
                double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (ms > worst)
                {
                    worst = ms;
                    worstTick = g.Ticks;
                }

                slow += ms > 16 ? 1 : 0;
                ticks++;
                totalMs += ms;
            }

            foreach ((int civ, string text) in g.News)
            {
                if (civ == 0 && (text.Contains("war", StringComparison.OrdinalIgnoreCase) || text.Contains("peace", StringComparison.OrdinalIgnoreCase) || text.Contains("down to", StringComparison.Ordinal)))
                {
                    output.WriteLine($"   news: {text}");
                }
            }

            g.News.Clear();
            output.WriteLine($"{minute,3} min  " + string.Join("  |  ", g.Civs.Select(c => Line(g, c))));
        }

        output.WriteLine(string.Create(inv, $"tick: mean {totalMs / Math.Max(1, ticks):F3} ms, worst {worst:F1} ms (tick {worstTick}), over 16 ms: {slow}"));
        return 0;
    }

    private static string Line(SimGame g, Civ c)
    {
        var army = c.Units.Select(id => g.Units[id]).Where(u => g.Content.Units[u.Kind].Army && u.State != UnitState.Gone).ToList();
        int people = c.Settlements.Sum(s => g.Settlements[s].People);
        int fighting = army.Count(u => u.Fighting);
        string wars = string.Join(",", g.Civs.Where(o => o.Id != c.Id && g.AtWar(c.Id, o.Id)).Select(o => o.Id));
        return $"{c.Id}: {people} home, army {army.Sum(u => u.People)} in {army.Count} ({fighting} fighting), era {c.Era}" + (wars.Length > 0 ? $", war {wars}" : "") + (c.Dead ? " DEAD" : "");
    }
}
