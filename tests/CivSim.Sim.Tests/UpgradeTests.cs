using System;
using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Upgrades in place (Patryk, 2026-11-19): huts become stone houses once masonry is known.</summary>
public sealed class UpgradeTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void AFinishedBuildingCanBeReplacedByAnotherKindOnItsPlaces()
    {
        // 15B (Patryk, 2026-11-20): another kind on the same hex; half of the old price counts toward it.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        Array.Fill(s.Stock, 400 * SimContent.One);
        int cell = s.Cells.First(c => c != s.Center && g.FreeSlots(c) == 15);
        BuildingType hut = Content.Building("hut");
        BuildingType store = Content.Building("storehouse");
        var b = new Building { Id = g.Buildings.Count, Civ = civ.Id, Type = hut.Id, Cell = cell, Settlement = s.Id, State = BuildingState.Active };
        g.Buildings.Add(b);
        civ.Buildings.Add(b.Id);
        g.PlaceForTest(b);

        civ.KnownTech[store.Tech] = false;
        Assert.Contains("needs", g.Apply(new ReplaceBuildingCommand(civ.Id, b.Id, store.Id)));
        civ.KnownTech[store.Tech] = true;
        Assert.NotNull(g.Apply(new ReplaceBuildingCommand(civ.Id, b.Id, hut.Id))); // not by the same kind
        Assert.Null(g.Apply(new ReplaceBuildingCommand(civ.Id, b.Id, store.Id)));
        Assert.Equal(BuildingState.Removed, b.State);
        Building n = g.Buildings[^1];
        Assert.Equal(store.Id, n.Type);
        Assert.Equal(cell, n.Cell);
        Assert.Contains(n.Id, civ.Buildings);
    }

    [Fact]
    public void AHutBecomesAStoneHouseOnItsOwnPlaceWithHalfItsPriceAlreadyThere()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        for (int i = 0; i < s.Stock.Length; i++)
        {
            s.Stock[i] = 400 * SimContent.One;
        }

        int cell = s.Cells.First(c => c != s.Center && g.FreeSlots(c) == 15);
        BuildingType hut = Content.Building("hut");
        BuildingType house = Content.Building("stone_house");
        Assert.Equal(house.Id, hut.UpgradesTo);
        var b = new Building { Id = g.Buildings.Count, Civ = civ.Id, Type = hut.Id, Cell = cell, Settlement = s.Id, State = BuildingState.Active };
        g.Buildings.Add(b);
        civ.Buildings.Add(b.Id);
        g.PlaceForTest(b);
        int slots = b.Slots;

        // Not before masonry.
        Assert.Contains("needs", g.Apply(new UpgradeBuildingCommand(civ.Id, b.Id)));
        foreach (int t in new[] { house.Tech }.Concat(house.AlsoTechs))
        {
            civ.KnownTech[t] = true;
        }

        Assert.Null(g.Apply(new UpgradeBuildingCommand(civ.Id, b.Id)));
        Assert.Equal(BuildingState.Removed, b.State);
        Building n = g.Buildings[^1];
        Assert.Equal(house.Id, n.Type);
        Assert.Equal(cell, n.Cell);
        Assert.Equal(slots, n.Slots);
        Assert.Contains(n.Id, civ.Buildings);
        Assert.DoesNotContain(b.Id, civ.Buildings);
        for (int gd = 0; gd < house.Price.Length; gd++)
        {
            // Half of the hut's price is at the site (or already built in), porters bring the rest.
            Assert.True(n.Delivered[gd] >= System.Math.Min(house.Price[gd], hut.Price[gd] * Content.UpgradeCreditPercent / 100));
            Assert.Equal(house.Price[gd], n.Delivered[gd] + n.OnTheWay[gd]);
        }
    }
}
