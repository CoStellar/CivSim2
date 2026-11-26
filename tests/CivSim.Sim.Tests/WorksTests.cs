using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Works drawn on hexes: ditches, palisades, walls, aqueducts and bridges (decisions of 2026-11-18).</summary>
public sealed class WorksTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static void Run(SimGame g, long ticks)
    {
        for (long i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    private static SimGame NewGame(ulong seed = 1)
    {
        SimGame g = SimGame.Create(Content, new SimSettings(seed, 66, 40, 2) { Ai = false }).Basics();
        foreach (Civ c in g.Civs)
        {
            Array.Fill(c.KnownTech, true);
            c.Era = 1;
            Settlement s = g.Settlements[c.Settlements[0]];
            Array.Fill(s.Stock, 900 * SimContent.One);
            s.People = 60;
        }

        return g;
    }

    private static int[] Pairs(SimGame g, IEnumerable<int> cells) => [.. cells]; // commands take hex indices (the server turns col, row pairs into them)

    // A short line of our own land hexes starting next to water, or null.
    private static List<int>? LineFromWater(SimGame g, Civ civ, int length)
    {
        Settlement s = g.Settlements[civ.Settlements[0]];
        foreach (int start in s.Cells)
        {
            if (!g.NextToWater(start) || Content.World.Biomes[g.Map.Biome[start]].IsWater)
            {
                continue;
            }

            var line = new List<int> { start };
            var nb = new HexCoord[6];
            while (line.Count < length)
            {
                g.Geo.GetNeighbors(g.Geo.FromIndex(line[^1]), nb);
                int next = nb.Select(g.Geo.Index).FirstOrDefault(n => !line.Contains(n) && g.OwnerAt[n] == civ.Id && !Content.World.Biomes[g.Map.Biome[n]].IsWater, -1);
                if (next < 0)
                {
                    break;
                }

                line.Add(next);
            }

            if (line.Count == length)
            {
                return line;
            }
        }

        return null;
    }

    // The first seed whose first civilization has water by its land, with a line of its land from the water.
    private static (SimGame Game, List<int> Line) WithWater(int length)
    {
        for (ulong seed = 1; seed < 20; seed++)
        {
            SimGame g = NewGame(seed);
            if (LineFromWater(g, g.Civs[0], length) is { } line)
            {
                return (g, line);
            }
        }

        throw new InvalidOperationException("no start by water");
    }

    [Fact]
    public void ADitchFromWaterWatersItsHexesAndStaysUnderBuildings()
    {
        (SimGame g, List<int>? line) = WithWater(3);
        Civ civ = g.Civs[0];

        WorkKind ditch = Content.Work("ditch")!;
        Assert.Null(g.Apply(new BuildWorkCommand(civ.Id, Pairs(g, line), ditch.Id)));
        Run(g, 3 * ditch.TicksPerHex + (5 * SimContent.TicksPerSecond));
        foreach (int c in line)
        {
            Assert.True((g.WorksAt[c] & ditch.Bit) != 0, "the ditch is built");
            Assert.True((g.WorksAt[c] & SimGame.WateredBit) != 0, "water reaches it");
        }

        BuildingType farm = Content.Building("farm");
        Assert.Equal(25, g.WaterBonus(line[^1], farm));
        BuildingType hut = Content.Building("hut");
        if (g.CannotBuild(civ, line[1], hut) is null)
        {
            Assert.Null(g.Apply(new BuildCommand(civ.Id, line[1], hut.Id)));
            Assert.True((g.WorksAt[line[1]] & ditch.Bit) != 0, "the ditch stays under the hut");
        }

        // Removing the hex next to the water dries the rest.
        Assert.Null(g.Apply(new RemoveWorkCommand(civ.Id, line[0], ditch.Id)));
        if (!line.Skip(1).Any(g.NextToWater))
        {
            Assert.True((g.WorksAt[line[^1]] & SimGame.WateredBit) == 0, "cut off from the water, the ditch is dry");
        }
    }

    [Fact]
    public void ADitchMustStartByWaterAndReachesFourHexes()
    {
        (SimGame g, List<int> line) = WithWater(5);
        Civ civ = g.Civs[0];
        WorkKind ditch = Content.Work("ditch")!;

        Assert.Contains("at most 4", g.Apply(new BuildWorkCommand(civ.Id, Pairs(g, line), ditch.Id)));
        line.Reverse();
        if (!g.NextToWater(line[0]))
        {
            Assert.Contains("next to water", g.Apply(new BuildWorkCommand(civ.Id, Pairs(g, line.Take(2)), ditch.Id)));
        }
    }

    [Fact]
    public void WallsStopForeignWalkersUntilBrokenAndPalisadesWoundEnemies()
    {
        SimGame g = NewGame();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        Settlement sb = g.Settlements[b.Settlements[0]];
        WorkKind wall = Content.Work("wall")!;
        int cell = sb.Cells.First(c => c != sb.Center && !Content.World.Biomes[g.Map.Biome[c]].IsWater && g.Map.Relief[c] < 2);
        Assert.Null(g.Apply(new BuildWorkCommand(b.Id, Pairs(g, [cell]), wall.Id)));
        Run(g, wall.TicksPerHex + (2 * SimContent.TicksPerSecond));
        Assert.True(g.WalledAgainst(a.Id, cell));
        Assert.False(g.WalledAgainst(b.Id, cell));

        // Our warriors next to the wall break it.
        Settlement sa = g.Settlements[a.Settlements[0]];
        Assert.Null(g.Apply(new TrainCommand(a.Id, sa.Id, Content.Unit("warriors").Id)));
        Run(g, Content.Unit("warriors").TrainTicks + 2);
        Unit u = g.Units.Last(x => x.Civ == a.Id);
        var nb = new HexCoord[6];
        g.Geo.GetNeighbors(g.Geo.FromIndex(cell), nb);
        int next = nb.Select(g.Geo.Index).First(n => !g.WalledAgainst(a.Id, n) && !Content.World.Biomes[g.Map.Biome[n]].IsWater);
        g.Place(u, next);
        Assert.Null(g.Apply(new AttackWallCommand(a.Id, u.Id, cell)));
        Assert.True(g.AtWar(a.Id, b.Id));
        Run(g, 2 * SimContent.TicksPerMinute);
        Assert.False(g.WalledAgainst(a.Id, cell));

        // A palisade under an enemy at war wounds it.
        WorkKind palisade = Content.Work("palisade")!;
        Assert.Null(g.Apply(new BuildWorkCommand(b.Id, Pairs(g, [next]), palisade.Id)));
        Run(g, palisade.TicksPerHex + 20);
        int before = u.People;
        Run(g, 30 * SimContent.TicksPerSecond);
        Assert.True(u.People < before || u.State == UnitState.Gone, "the palisade wounds the enemies standing on it");
    }

    [Fact]
    public void RoadsBridgeTheRiversTheyCrossWithMasonry()
    {
        SimGame g = NewGame();
        Civ civ = g.Civs[0];
        civ.Era = 0;
        HexCoord c0 = default;
        int from = -1;
        int to = -1;
        for (int i = 0; i < g.Geo.CellCount && from < 0; i++)
        {
            if (Content.World.Biomes[g.Map.Biome[i]].IsWater)
            {
                continue;
            }

            c0 = g.Geo.FromIndex(i);
            for (int d = 0; d < 6; d++)
            {
                int n = g.Geo.Index(g.Geo.Neighbor(c0, (HexDirection)d));
                if (g.Map.River[g.Geo.EdgeIndex(g.Geo.Edge(c0, (HexDirection)d))] > 0 && !Content.World.Biomes[g.Map.Biome[n]].IsWater)
                {
                    from = i;
                    to = n;
                    break;
                }
            }
        }

        Assert.True(from >= 0);
        int dir = Enumerable.Range(0, 6).First(d => g.Geo.Index(g.Geo.Neighbor(c0, (HexDirection)d)) == to);
        Assert.False(g.Crossable(civ.Id, from, dir));
        g.RoadAt[to] = 1;
        g.TestBridge(civ, from);
        Assert.False(g.Crossable(civ.Id, from, dir), "no road here yet");
        g.RoadAt[from] = 1;
        g.TestBridge(civ, from);
        Assert.True(g.Crossable(civ.Id, from, dir), "the bridge lets everyone cross");
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void TransportsCarryArmiesAndTakeThemDownWhenSunk()
    {
        for (ulong seed = 1; seed < 20; seed++)
        {
            SimGame g = NewGame(seed);
            Civ civ = g.Civs[0];
            Settlement s = g.Settlements[civ.Settlements[0]];
            var nb = new HexCoord[6];
            int land = -1;
            int water = -1;
            foreach (int c in s.Cells)
            {
                if (Content.World.Biomes[g.Map.Biome[c]].IsWater)
                {
                    continue;
                }

                g.Geo.GetNeighbors(g.Geo.FromIndex(c), nb);
                int w = nb.Select(g.Geo.Index).FirstOrDefault(n => Content.World.Biomes[g.Map.Biome[n]].IsWater, -1);
                if (w >= 0)
                {
                    land = c;
                    water = w;
                    break;
                }
            }

            if (land < 0)
            {
                continue;
            }

            UnitType transport = Content.Unit("transport");
            var ship = new Unit { Id = g.Units.Count, Civ = civ.Id, Kind = transport.Id, People = transport.People, Cell = water, State = UnitState.Idle };
            g.Units.Add(ship);
            civ.Units.Add(ship.Id);
            Assert.Null(g.Apply(new TrainCommand(civ.Id, s.Id, Content.Unit("warriors").Id)));
            Run(g, Content.Unit("warriors").TrainTicks + 2);
            Unit w2 = g.Units.Last(x => x.Kind == Content.Unit("warriors").Id);
            g.Place(w2, land);
            Assert.Null(g.Apply(new BoardCommand(civ.Id, w2.Id, ship.Id)));
            Assert.Equal(ship.Id, w2.Aboard);
            Assert.Equal(w2.People, g.Aboard(ship));
            Assert.DoesNotContain(g.ActiveUnits(), x => x.Id == w2.Id);
            Assert.Null(g.Apply(new LandCommand(civ.Id, ship.Id, land)));
            Assert.Equal(-1, w2.Aboard);
            Assert.Equal(land, w2.Cell);

            Assert.Null(g.Apply(new BoardCommand(civ.Id, w2.Id, ship.Id)));
            g.Destroyed(ship, g.Civs[1].Id);
            Assert.Equal(UnitState.Gone, w2.State);
            return;
        }

        throw new InvalidOperationException("no coast");
    }
}
