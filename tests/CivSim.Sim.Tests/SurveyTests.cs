using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>What is known of the ground (Patryk, 2026-11-19): our land, then prospectors and surveyors standing still.</summary>
public sealed class SurveyTests
{
    private static readonly SimContent Content = SimContent.Load();

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
