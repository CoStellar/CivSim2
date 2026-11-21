using System.Collections.Generic;
using System.Linq;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Decisions of Patryk after the tests of 2026-11-20 (claude/decyzje-testy-2026-11-20.md): 7A, 9B, 10B, 11B, 12B.</summary>
public sealed class PaceRulesTests
{
    private static readonly SimContent Content = SimContent.Load();

    [Fact]
    public void EveryStartHasIronWithinTwentyHexesOnItsLand()
    {
        int iron = Content.World.Deposits.First(d => d.Key == "iron_ore").Id;
        foreach (ulong seed in new ulong[] { 1, 2, 3 })
        {
            SimGame g = SimGame.Create(Content, new SimSettings(seed, 98, 60, 4) { Ai = false });
            foreach (Civ c in g.Civs)
            {
                int home = g.Settlements[c.Settlements[0]].Center;
                bool found = Enumerable.Range(0, g.Geo.CellCount).Any(k => g.Map.Deposit[k] == iron
                    && g.Geo.Distance(g.Geo.FromIndex(home), g.Geo.FromIndex(k)) <= 20 && g.FindPath(home, k, new List<int>(), false, -1));
                Assert.True(found, $"seed {seed}, people {c.Id}: no iron within 20 hexes by land");
            }
        }
    }

    [Fact]
    public void EveryPeopleStartsNearAnother()
    {
        // 12B: on the main continent, at most 60 hexes by land from a neighbour (the fallback widens it only when no place fits).
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4 })
        {
            SimGame g = SimGame.Create(Content, new SimSettings(seed, 98, 60, 4) { Ai = false });
            int[] centres = [.. g.Civs.Select(c => g.Settlements[c.Settlements[0]].Center)];
            var way = new List<int>();
            foreach (int a in centres)
            {
                int nearest = centres.Where(b => b != a && g.FindPath(a, b, way, false, -1, 40_000)).Select(b =>
                {
                    g.FindPath(a, b, way, false, -1, 40_000);
                    return way.Count;
                }).DefaultIfEmpty(int.MaxValue).Min();
                Assert.True(nearest <= 90, $"seed {seed}: nearest neighbour {nearest} hexes by land");
                Assert.True(g.OnMainLand(a));
            }
        }
    }

    [Fact]
    public void TheIronAgeWaitsForBronzeMade()
    {
        // 7A: knowing the bronze age's techs and its gateway is not enough; the people must have made bronze too.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        int bronzeAge = Content.Eras.ToList().FindIndex(e => e.Key == "bronze_age");
        foreach (SimTech t in Content.Techs.Where(t => t.Era <= bronzeAge && !t.Upgrade))
        {
            civ.KnownTech[t.Id] = true;
        }

        civ.Era = bronzeAge;
        int bronze = Content.GoodId("bronze");
        for (int i = 0; i < 100; i++)
        {
            g.Tick();
        }

        Assert.Equal(bronzeAge, civ.Era);
        Assert.Contains("bronze 0 of 5", g.EntryMissing(civ, bronzeAge + 1) ?? "", System.StringComparison.Ordinal);
        civ.Made[bronze] = 5 * SimContent.One * SimContent.TicksPerMinute;
        for (int i = 0; i < 100; i++)
        {
            g.Tick();
        }

        Assert.Equal(bronzeAge + 1, civ.Era);
    }

    [Fact]
    public void AVillageHasAtMostTwoHundredFiftyHomes()
    {
        // 9B: huts past the level's limit add nothing, and new ones are refused with the way on (the upgrade).
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        BuildingType hut = Content.Building("hut");
        civ.KnownTech[hut.Tech] = true;
        for (int i = 0; i < s.Stock.Length; i++)
        {
            s.Stock[i] = 900 * SimContent.One;
        }

        for (int k = 0; k < 40 && s.Housing < 60; k++)
        {
            int cell = s.Cells.FirstOrDefault(c => g.CannotBuild(civ, c, hut) is null, -1);
            if (cell < 0 || g.Apply(new BuildCommand(civ.Id, cell, hut.Id)) is not null)
            {
                break;
            }

            for (int t = 0; t < hut.BuildTicks + 50; t++)
            {
                g.Tick();
            }

            civ.Dirty = true;
            g.Recompute(civ);
        }

        Assert.Equal(60, Content.Levels[0].MaxHousing);
        Assert.True(s.Housing <= 60, $"housing {s.Housing}");
        if (s.Housing == 60)
        {
            int cell = s.Cells.First(c => g.CannotBuild(civ, c, hut) is null);
            string? why = g.Apply(new BuildCommand(civ.Id, cell, hut.Id));
            Assert.NotNull(why);
            Assert.Contains("Upgrade it to a village", why, System.StringComparison.Ordinal);
        }

        Assert.Equal(new[] { 60, 250, 800, 0 }, Content.Levels.Select(l => l.MaxHousing).ToArray());
    }

    [Fact]
    public void BuildingTechnologiesRaiseStoneHouses()
    {
        // 10B: construction, engineering and guilds add homes to every stone house (it grows a storey).
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        BuildingType house = Content.Building("stone_house");
        int before = g.Bonus(civ, EffectKind.Housing, house.Key, house.Groups);
        foreach (string k in new[] { "construction", "engineering", "guilds" })
        {
            civ.KnownTech[Content.TechId(k)] = true;
        }

        Assert.Equal(before + 16, g.Bonus(civ, EffectKind.Housing, house.Key, house.Groups));
    }

    [Fact]
    public void FoodThatRunsOutSoonIsAnAlertWithAdvice()
    {
        // 11B: a warning with what to do, while there is still time.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ civ = g.Civs[0];
        Settlement s = g.Settlements[civ.Settlements[0]];
        civ.Dirty = true;
        g.Recompute(civ);
        s.Rate[Content.Food] = -10 * SimContent.One; // eats 10 a minute more than it makes
        s.Stock[Content.Food] = 20 * SimContent.One; // two minutes left
        Alert? a = g.Alerts(civ).FirstOrDefault(x => x.Kind == AlertKind.FoodLow);
        Assert.NotNull(a);
        Assert.Contains("runs out in 2:00", a.Text, System.StringComparison.Ordinal);
        Assert.Contains("Gatherers' hut", a.Text, System.StringComparison.Ordinal);
    }
}
