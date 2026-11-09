using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Perception;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Units;

/// <summary>Units of stage 6 PR 3a: routes (T3), the pool (T4), orders (T5) and the unit commands.</summary>
public class UnitTests
{
    // The live clock and the real speeds: a flat cell takes settlers 20 ticks.
    private static readonly GameContent Live = ContentLoader.Load();

    private static GameState NewGame(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Live, out WorldGenReport _);

    // Every cell is known to the civilization (as if it had seen it this year).
    private static void KnowAll(GameState state, CivilizationState civ)
    {
        Array.Fill(civ.SeenYear, (int)(state.Month / 12) + 1);
    }

    // A flat grassland patch without rivers around the first camp, so costs are plain to compute.
    private static (WorldState World, HexCoord Center) FlatLand(GameState state, int radius)
    {
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        HexCoord center = state.Settlements[0].Cell;
        byte grass = (byte)Live.Biomes.ToList().FindIndex(b => b.Key == "grassland");
        var area = new HexCoord[HexGlobe.RangeSize(radius)];
        int count = geo.Range(center, radius, area);
        for (int k = 0; k < count; k++)
        {
            int i = geo.Index(area[k]);
            world.Map.Biome.Set(i, grass);
            world.Map.Relief.Set(i, (byte)Relief.Flat);
        }

        for (int e = 0; e < world.Map.River.Length; e++)
        {
            world.Map.River.Set(e, 0);
        }

        return (world, center);
    }

    [Fact]
    public void TheRouteIsTheCheapestAndGoesAroundMountains()
    {
        GameState state = NewGame(21);
        CivilizationState civ = state.Civilizations[0];
        KnowAll(state, civ);
        (WorldState world, HexCoord center) = FlatLand(state, 8);
        HexGlobe geo = world.Geometry;

        // A wall of mountains three cells thick across the straight way, with a way around it.
        HexCoord from = geo.Wrap(center.Col - 4, center.Row);
        HexCoord to = geo.Wrap(center.Col + 4, center.Row);
        var wall = new HashSet<int>();
        for (int dc = -1; dc <= 1; dc++)
        {
            for (int dr = -3; dr <= 3; dr++)
            {
                int i = geo.Index(geo.Wrap(center.Col + dc, center.Row + dr));
                world.Map.Relief.Set(i, (byte)Relief.Mountains);
                wall.Add(i);
            }
        }

        var finder = new RouteFinder();
        int a = geo.Index(from);
        int b = geo.Index(to);
        List<int> path = [.. finder.Path(Live, state, civ, world, a, b, 20)!];
        int ticks = finder.RouteTicks(Live, state, civ, world, a, b, 20);

        // The cheapest way: what a plain search over every cell (Dijkstra) finds.
        Assert.Equal(Cheapest(state, civ, world, a, b, 20), ticks);
        Assert.DoesNotContain(path, c => wall.Contains(c));
        Assert.Equal(b, path[^1]);

        // Walking it costs exactly what the route says.
        int sum = 0;
        int at = a;
        foreach (int c in path)
        {
            sum += RouteFinder.StepTicks(Live, world, geo.FromIndex(at), Side(geo, at, c), c, 20);
            at = c;
        }

        Assert.Equal(ticks, sum);
    }

    [Fact]
    public void AUnitFindsItsWayOnceAndWalksItInTicks()
    {
        GameState state = NewGame(22);
        CivilizationState civ = state.Civilizations[0];
        KnowAll(state, civ);
        (WorldState world, HexCoord center) = FlatLand(state, 8);
        HexGlobe geo = world.Geometry;
        HexCoord target = geo.Wrap(center.Col + 6, center.Row);
        Unit u = state.Units.Add(Live.Units.First(UnitRole.Settlers), civ.Id, world.Id, geo.Index(center), state.Settlements[0].Id.Value, UnitOrder.Move,
            Live.Strata.Count, Live.Skills.Count, Live.Goods.Count);
        u.Target = geo.Index(target);

        var system = new UnitSystem(Live);
        var runner = new TurnRunner(state);
        runner.AddSystem(system);
        int ticks = 0;
        while (u.Cell != u.Target && ticks < 1000)
        {
            runner.RunTurn();
            ticks++;
        }

        // Six flat cells at 20 ticks each, one search for the whole way.
        Assert.Equal(u.Target, u.Cell);
        Assert.Equal(6 * 20, ticks);
        Assert.Equal(1, system.RouteSearches);
        Assert.Equal(UnitOrder.Idle, u.Order);
    }

    [Fact]
    public void SettlersFoundACampWhereTheyStandOrGoHome()
    {
        GameState state = NewGame(23);
        CivilizationState civ = state.Civilizations[0];
        KnowAll(state, civ);
        (WorldState world, HexCoord center) = FlatLand(state, 8);
        HexGlobe geo = world.Geometry;
        SettlementState home = state.Settlements[0];
        home.Population[0] += 100; // enough people to split
        home.ScoutOut = true; // no scouts leave in this test (they take people, stage 6 PR 3b)
        int before = home.TotalPopulation();

        // Settlers that left home stand where they are told.
        var split = new Core.Commands.SplitSettlementCommand(Live, home.Id, geo.Wrap(center.Col + 6, center.Row), 30);
        Assert.True(split.Validate(state, civ.Id).Success);
        split.Apply(state, civ.Id);
        Unit u = state.Units[state.Units.Count - 1];
        int people = u.TotalPeople();
        Assert.Equal(before - people, home.TotalPopulation());

        var runner = new TurnRunner(state);
        runner.AddSystem(new UnitSystem(Live));
        var here = new MoveUnitCommand(Live, u.Id, geo.Wrap(center.Col + 1, center.Row));
        Assert.True(here.Validate(state, civ.Id).Success, here.Validate(state, civ.Id).Reason);
        here.Apply(state, civ.Id);
        for (int t = 0; t < 40; t++)
        {
            runner.RunTurn();
        }

        // Too close to home for a camp: they may not found here, so they go home and join it.
        Assert.False(new FoundCampCommand(Live, u.Id).Validate(state, civ.Id).Success);
        var back = new ReturnUnitCommand(Live, u.Id);
        Assert.True(back.Validate(state, civ.Id).Success);
        back.Apply(state, civ.Id);
        for (int t = 0; t < 60 && state.Units.Find(u.Id) is not null; t++)
        {
            runner.RunTurn();
        }

        Assert.Null(state.Units.Find(u.Id));
        Assert.Equal(before, home.TotalPopulation());

        // A second group walks far enough and founds its camp there on command.
        split = new Core.Commands.SplitSettlementCommand(Live, home.Id, geo.Wrap(center.Col + 6, center.Row), 30);
        split.Apply(state, civ.Id);
        u = state.Units[state.Units.Count - 1];
        new MoveUnitCommand(Live, u.Id, geo.Wrap(center.Col + 5, center.Row)).Apply(state, civ.Id);
        for (int t = 0; t < 200 && u.Order != UnitOrder.Idle; t++)
        {
            runner.RunTurn();
        }

        int camps = state.Settlements.Count;
        var found = new FoundCampCommand(Live, u.Id);
        Assert.True(found.Validate(state, civ.Id).Success, found.Validate(state, civ.Id).Reason);
        found.Apply(state, civ.Id);
        runner.RunTurn();
        Assert.Equal(camps + 1, state.Settlements.Count);
        Assert.Equal(geo.Wrap(center.Col + 5, center.Row), state.Settlements[^1].Cell);
    }

    [Fact]
    public void OthersCannotCommandOurUnits()
    {
        GameState state = NewGame(24);
        CivilizationState civ = state.Civilizations[0];
        Unit u = state.Units.Add(Live.Units.First(UnitRole.Scouts), civ.Id, state.Worlds[0].Id, state.Worlds[0].Geometry.Index(state.Settlements[0].Cell), 0, UnitOrder.Idle,
            Live.Strata.Count, Live.Skills.Count, Live.Goods.Count);
        Assert.False(new ReturnUnitCommand(Live, u.Id).Validate(state, state.Civilizations[1].Id).Success);
        Assert.False(new ReturnUnitCommand(Live, 999).Validate(state, civ.Id).Success);
        Assert.False(new FoundCampCommand(Live, u.Id).Validate(state, civ.Id).Success); // scouts do not found camps
    }

    [Fact]
    public void ThePoolReusesSlotsAndKeepsTheOrderOfIds()
    {
        var book = new UnitBook();
        Unit a = book.Add(Live.Units.First(UnitRole.Scouts), new CivId(0), WorldId.Earth, 1, 0, UnitOrder.Idle, 2, 1, 3);
        Unit b = book.Add(Live.Units.First(UnitRole.Settlers), new CivId(1), WorldId.Earth, 2, 0, UnitOrder.Idle, 2, 1, 3);
        b.People[1] = 7;
        a.Gone = true;
        Assert.Equal(1, book.RemoveGone());
        Unit c = book.Add(Live.Units.First(UnitRole.Settlers), new CivId(2), WorldId.Earth, 3, 0, UnitOrder.Idle, 2, 1, 3);
        Assert.Equal(a.Slot, c.Slot); // the slot of the gone scout
        Assert.Equal(0, c.People[1]);  // and nothing of it is left
        Assert.Equal([1L, 2L], book.All.Select(u => u.Id).ToArray());
        Assert.Equal(7, book.Find(1)!.Value.People[1]);
        Assert.Null(book.Find(0));
        Assert.Throws<ArgumentException>(() => book.Add(Live.Units.First(UnitRole.Scouts), new CivId(0), WorldId.Earth, 1, 0, UnitOrder.Idle, 3, 1, 3));
    }

    [Fact]
    public void WalkingUnitsAllocateNothingInATick()
    {
        // A game in which every settlement keeps scouts walking (long trips, so they are still out while measured).
        GameContent content = Live.WithUnits(Live.Units with { ScoutTripCells = 150 });
        GameState state = GameState.CreateNew(GameSettings.Test(25), content, out WorldGenReport _);
        foreach (CivilizationState civ in state.Civilizations)
        {
            KnowAll(state, civ);
        }

        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, content);
        for (int t = 0; t < 1500; t++)
        {
            runner.RunTurn(); // a game with scouts out
        }


        // The measured system walks the same units alone for a while first: its pool, trails and route arrays reach their size.
        var system = new UnitSystem(content);
        var warm = (TurnContext)Activator.CreateInstance(
            typeof(TurnContext), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [state], null)!;
        for (int t = 0; t < 300; t++)
        {
            system.Execute(warm);
        }

        // One context for all the measured ticks (the runner makes one per tick; only the units are measured here).
        var context = (TurnContext)Activator.CreateInstance(
            typeof(TurnContext), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, [state], null)!;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long units = 0;
        for (int t = 0; t < 600; t++)
        {
            system.Execute(context);
            units += state.Units.Count;
        }

        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(units > 600, "no units walked");
        Assert.True(bytes == 0, $"{bytes} bytes in 600 ticks");
    }

    private static HexDirection Side(HexGlobe geo, int from, int to)
    {
        for (int d = 0; d < 6; d++)
        {
            if (geo.Index(geo.Neighbor(geo.FromIndex(from), (HexDirection)d)) == to)
            {
                return (HexDirection)d;
            }
        }

        throw new InvalidOperationException("not neighbours");
    }

    // Dijkstra over every walkable known cell with the same step costs.
    private static int Cheapest(GameState state, CivilizationState civ, WorldState world, int from, int to, int baseTicks)
    {
        HexGlobe geo = world.Geometry;
        var cost = new int[geo.CellCount];
        Array.Fill(cost, int.MaxValue);
        var done = new bool[geo.CellCount];
        cost[from] = 0;
        var queue = new PriorityQueue<int, int>();
        queue.Enqueue(from, 0);
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        while (queue.TryDequeue(out int cur, out _))
        {
            if (done[cur])
            {
                continue;
            }

            done[cur] = true;
            if (cur == to)
            {
                return cost[cur];
            }

            geo.GetNeighbors(geo.FromIndex(cur), n6);
            for (int d = 0; d < 6; d++)
            {
                int ni = geo.Index(n6[d]);
                if (!MapMemory.Knows(Live, state, civ, ni))
                {
                    continue;
                }

                int step = RouteFinder.StepTicks(Live, world, geo.FromIndex(cur), (HexDirection)d, ni, baseTicks);
                if (step > 0 && cost[cur] + step < cost[ni])
                {
                    cost[ni] = cost[cur] + step;
                    queue.Enqueue(ni, cost[ni]);
                }
            }
        }

        return -1;
    }
}
