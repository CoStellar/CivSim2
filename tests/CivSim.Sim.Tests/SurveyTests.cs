using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>What is known of the ground (Patryk, 2026-11-19): our land, then prospectors and surveyors standing still.</summary>
public sealed class SurveyTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static int Hexes(SimGame g, int a, int b) => g.Geo.Distance(g.Geo.FromIndex(a), g.Geo.FromIndex(b));

    private static int DepositId(string key) => Content.World.Deposits.First(d => d.Key == key).Id;

    private static (SimGame G, Civ Civ) NewGame()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(3, 98, 60, 2) { Ai = false }).Basics();
        return (g, g.Civs[0]);
    }

    private static Unit Put(SimGame g, Civ civ, string kind, int cell)
    {
        UnitType t = Content.Unit(kind);
        var u = new Unit { Id = g.Units.Count, Civ = civ.Id, Kind = t.Id, People = t.People, Cell = cell, State = UnitState.Idle, Home = civ.Settlements[0] };
        g.Units.Add(u);
        civ.Units.Add(u.Id);
        return u;
    }

    private static void Seconds(SimGame g, int s)
    {
        for (int i = 0; i < s * SimContent.TicksPerSecond; i++)
        {
            g.Tick();
        }
    }

    [Fact]
    public void ProspectingShowsSignsOfEraOresAndProspectorsSearchThem()
    {
        // 2026-11-22: signs on seen land near our settlements; the ore lies within offsetHexes of the centre; a search walks there.
        (SimGame g, Civ civ) = NewGame();
        int copper = DepositId("copper");
        civ.Era = Content.Eras.ToList().FindIndex(e => e.Key == "neolithic");
        int home = g.Settlements[civ.Settlements[0]].Center;
        int ore = Enumerable.Range(0, g.Geo.CellCount)
            .Where(i => g.Map.Deposit[i] == copper && g.OwnerAt[i] < 0 && Hexes(g, home, i) <= Content.Lodes.ReachHexes)
            .OrderBy(i => Hexes(g, home, i)).First();
        civ.Known[ore] = true;
        Seconds(g, Content.Lodes.RecountTicks / SimContent.TicksPerSecond);
        Assert.Empty(civ.Signs); // no Prospecting yet

        civ.KnownTech[Content.Lodes.SignTech] = true;
        Seconds(g, Content.Lodes.RecountTicks / SimContent.TicksPerSecond);
        OreSign sign = Assert.Single(civ.Signs, x => x.Deposit == copper && Hexes(g, x.Center, ore) <= Content.Lodes.OffsetHexes);

        Unit p = Put(g, civ, "prospectors", home);
        Assert.Null(g.Apply(new SearchSignsCommand(civ.Id, p.Id)));
        Seconds(g, 120);
        Assert.True(g.KnowsDeposit(civ, ore)); // found: the sign goes at the next count
        Seconds(g, Content.Lodes.RecountTicks / SimContent.TicksPerSecond);
        Assert.DoesNotContain(civ.Signs, x => x.Center == sign.Center && x.Deposit == copper && Hexes(g, x.Center, ore) <= Content.Lodes.OffsetHexes);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void EveryDepositHasASizeAndAMineOnASmallOneGivesHalf()
    {
        (SimGame g, Civ civ) = NewGame();
        int[] sizes = [.. Content.Lodes.Sizes.Select(z => z.Percent)];
        Assert.All(Enumerable.Range(0, g.Geo.CellCount).Where(i => g.Map.Deposit[i] != 0), i => Assert.Contains(g.DepositSizePercent(i), sizes));
        int stone = DepositId("stone");
        Assert.Contains(Enumerable.Range(0, g.Geo.CellCount), i => g.Map.Deposit[i] == stone && g.DepositSizePercent(i) == 50);
        Assert.Contains(Enumerable.Range(0, g.Geo.CellCount), i => g.Map.Deposit[i] == stone && g.DepositSizePercent(i) == 150);
        Assert.Equal("small", g.DepositSizeName(Enumerable.Range(0, g.Geo.CellCount).First(i => g.Map.Deposit[i] != 0 && g.DepositSizePercent(i) == 50)));
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void BeyondOurLandOresAreFoundOnlyByStandingProspectorsOfTheirEra()
    {
        (SimGame g, Civ civ) = NewGame();
        int copper = DepositId("copper");
        int cell = Enumerable.Range(0, g.Geo.CellCount).First(i => g.Map.Deposit[i] == copper && g.OwnerAt[i] < 0);
        civ.Known[cell] = true;
        Assert.False(g.KnowsDeposit(civ, cell)); // seen is not known

        // Stone Age: copper is not recognised yet, even standing next to it.
        Unit p = Put(g, civ, "prospectors", cell);
        Seconds(g, 2);
        Assert.False(g.KnowsDeposit(civ, cell));
        Assert.True(civ.RockSeen[cell] > 0);

        // In the Neolithic (the era of Copper working), before knowing it: found, once it stands there again.
        civ.Era = Content.Eras.ToList().FindIndex(e => e.Key == "neolithic");
        Seconds(g, 2);
        Assert.True(g.KnowsDeposit(civ, cell));
        Assert.Contains(g.FoundAround(p), f => f.Cell == cell);
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void WalkingSurveyorsSeeNothingStandingOnesTheSoilAround()
    {
        (SimGame g, Civ civ) = NewGame();
        Settlement home = g.Settlements[civ.Settlements[0]];
        int far = Enumerable.Range(0, g.Geo.CellCount).First(i => g.OwnerAt[i] < 0 && !Content.World.Biomes[g.Map.Biome[i]].IsWater
            && g.Geo.Distance(g.Geo.FromIndex(i), g.Geo.FromIndex(home.Center)) > 12);
        civ.Known[far] = true;
        Assert.False(g.KnowsSoil(civ, far));
        Assert.True(g.KnowsSoil(civ, home.Cells.First(c => c != home.Center))); // our land is known

        Unit s = Put(g, civ, "surveyors", far);
        s.State = UnitState.Moving; // walking: looks at nothing
        Seconds(g, 2);
        Assert.False(g.KnowsSoil(civ, far));
        s.State = UnitState.Idle;
        Seconds(g, 2);
        Assert.True(g.KnowsSoil(civ, far));
    }

    [Fact]
    public void OnOurLandOresAreKnownFromTheirEra()
    {
        (SimGame g, Civ civ) = NewGame();
        int flint = DepositId("flint");
        int cell = Enumerable.Range(0, g.Geo.CellCount).FirstOrDefault(i => g.Map.Deposit[i] == flint && g.OwnerAt[i] == civ.Id, -1);
        if (cell < 0)
        {
            return; // no flint on our land with this seed
        }

        Assert.True(g.KnowsDeposit(civ, cell));
    }
}
