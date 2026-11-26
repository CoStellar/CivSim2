using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>
/// Random commands (2026-11-19 tests: 160 000 of them found 6 places that threw) and the invariants checked after every
/// tick: a walking unit has a way left, nothing is negative.
/// </summary>
public sealed class FuzzTests
{
    private static readonly SimContent Content = SimContent.Load();

    /// <summary>Checks what must hold after any tick; returns the first broken rule or null.</summary>
    public static string? Broken(SimGame g)
    {
        foreach (Unit u in g.Units)
        {
            if (u.State == UnitState.Moving && u.PathIndex >= u.Path.Count && u.Job == CarrierJob.None && u.PatrolStage == PatrolStage.None)
            {
                return $"unit {u.Id} ({g.Content.Units[u.Kind].Key}) walks with no way left";
            }

            if (u.State != UnitState.Gone && u.People < 0)
            {
                return $"unit {u.Id} has {u.People} people";
            }

            if (u.State != UnitState.Gone && (u.Horses < 0 || u.Provisions < 0 || (u.Cargo is { } c && c.Any(x => x < 0))))
            {
                return $"unit {u.Id} carries less than nothing";
            }

            if (u.State != UnitState.Gone && g.Civs[u.Civ].Dead)
            {
                return $"unit {u.Id} of a civilization that is no more";
            }
        }

        foreach (Civ civ in g.Civs)
        {
            if (civ.Researching >= 0 && civ.KnownTech[civ.Researching])
            {
                return $"{civ.Name} research {g.Content.Techs[civ.Researching].Key}, which they know";
            }
        }

        foreach (Settlement s in g.Settlements)
        {
            if (s.People < 0)
            {
                return $"{s.Name} has {s.People} people";
            }

            for (int i = 0; i < s.Stock.Length; i++)
            {
                if (s.Stock[i] < 0)
                {
                    return $"{s.Name} has {s.Stock[i]} of {g.Content.Goods[i].Key}";
                }
            }
        }

        return null;
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void RandomCommandsAreRefusedNotThrown()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(3, 66, 40, 3)).Basics();
        for (int i = 0; i < 2 * SimContent.TicksPerMinute; i++)
        {
            g.Tick(); // some units and buildings to aim at
        }

        Type[] kinds = [.. typeof(SimCommand).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(SimCommand)) && !t.IsAbstract).OrderBy(t => t.Name)];
        var rng = new Random(7);
        var failures = new List<string>();
        for (int n = 0; n < 40_000 && failures.Count < 10; n++)
        {
            Type kind = kinds[rng.Next(kinds.Length)];
            ConstructorInfo ctor = kind.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            object?[] args = [.. ctor.GetParameters().Select(p => Arg(p.ParameterType, p.Name ?? "", g, rng))];
            var command = (SimCommand)ctor.Invoke(args);
            try
            {
                g.Apply(command);
                if (n % 20 == 0)
                {
                    g.Tick();
                    if (Broken(g) is string broken)
                    {
                        failures.Add($"after {kind.Name}: {broken}");
                    }
                }
            }
            catch (Exception e) when (e is not Xunit.Sdk.XunitException)
            {
                failures.Add($"{kind.Name}({string.Join(", ", args.Select(Show))}): {e.GetType().Name} {e.Message}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void ACaravanOfAllOurHorsesLeavesNoneBelowZero()
    {
        // A10 (tests 2026-11-20): the caravan's own horses left first, then the load counted before took the same horses again.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement home = g.Settlements[civ.Settlements[0]];
        int site = Enumerable.Range(0, g.Geo.CellCount).First(c => g.OwnerAt[c] < 0 && !Content.World.Biomes[g.Map.Biome[c]].IsWater
            && g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(home.Center)) == 8 && g.FindPath(home.Center, c, new System.Collections.Generic.List<int>(), false, civ.Id));
        Settlement camp = g.FoundSettlement(civ, site, 30);
        foreach (string t in new[] { "the_wheel", "horseback_riding", "carrying" })
        {
            if (Content.Techs.Any(x => x.Key == t))
            {
                civ.KnownTech[Content.TechId(t)] = true;
            }
        }

        int horses = Content.GoodId("horses");
        home.Stock[horses] = 5 * SimContent.One;
        g.Apply(new SendCaravanCommand(civ.Id, home.Id, camp.Id, horses, 5 * SimContent.One));
        Assert.True(g.Units[^1].Horses > 0, "a caravan with horses, not porters");
        Assert.True(home.Stock[horses] >= 0, $"{home.Stock[horses]} horses left");
        Assert.Null(Broken(g));
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void AiGamesKeepTheInvariants()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(2, 66, 40, 4));
        for (int i = 0; i < 12 * SimContent.TicksPerMinute; i++)
        {
            g.Tick();
            if (i % 10 == 0 && Broken(g) is string broken)
            {
                Assert.Fail($"tick {g.Ticks}: {broken}");
            }
        }
    }

    private static string Show(object? a) => a switch
    {
        null => "null",
        int[] arr => "[" + string.Join(",", arr) + "]",
        _ => a.ToString() ?? "",
    };

    private static object? Arg(Type type, string name, SimGame g, Random rng)
    {
        if (name == "Civ")
        {
            return rng.Next(-1, g.Civs.Count + 1);
        }

        if (type == typeof(int))
        {
            int top = name.Contains("Cell", StringComparison.Ordinal) ? g.Geo.CellCount
                : name.Contains("Unit", StringComparison.Ordinal) ? g.Units.Count
                : name.Contains("Building", StringComparison.Ordinal) ? g.Buildings.Count
                : name.Contains("Settlement", StringComparison.Ordinal) ? g.Settlements.Count : 64;
            return rng.Next(10) == 0 ? rng.Next(-3, 3) * 1000 : rng.Next(-2, top + 2);
        }

        if (type == typeof(long))
        {
            return (long)rng.Next(-1000, 100_000);
        }

        if (type == typeof(bool))
        {
            return rng.Next(2) == 0;
        }

        if (type == typeof(int[]))
        {
            if (rng.Next(12) == 0)
            {
                return null;
            }

            int len = rng.Next(0, 6);
            int c = rng.Next(g.Geo.CellCount);
            int[] cells = new int[len];
            for (int k = 0; k < len; k++)
            {
                cells[k] = rng.Next(4) == 0 ? rng.Next(-5, g.Geo.CellCount + 5) : c + k;
            }

            return cells;
        }

        if (type == typeof(long[]))
        {
            return rng.Next(8) == 0 ? null : Enumerable.Range(0, rng.Next(0, Content.Goods.Count + 2)).Select(_ => (long)rng.Next(-500, 5000)).ToArray();
        }

        if (type.IsEnum)
        {
            return Enum.ToObject(type, rng.Next(-1, 6));
        }

        if (type == typeof(string))
        {
            return rng.Next(4) == 0 ? null : "x";
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
