using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Battle of 2026-11-19: arms from the stores, sizes, front and flank, ground, zone of control, leaving, charges.</summary>
public sealed class CombatTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    private static SimGame War()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        g.Civs[0].Known[g.Settlements[g.Civs[1].Settlements[0]].Center] = true;
        Assert.Null(g.Apply(new DeclareWarCommand(0, 1)));
        return g;
    }

    // A field far from both civilizations, made flat open grassland at one height with no trees, rivers or roads.
    private static int Field(SimGame g)
    {
        int grass = Content.World.Biome("grassland").Id;
        int best = -1;
        for (int i = 0; i < g.Geo.CellCount && best < 0; i += 7)
        {
            bool far = true;
            foreach (Settlement s in g.Settlements)
            {
                far &= g.Geo.Distance(g.Geo.FromIndex(i), g.Geo.FromIndex(s.Center)) > 12;
            }

            if (far && g.Geo.Distance(g.Geo.FromIndex(i), g.Geo.FromIndex(g.Settlements[0].Center)) < 25)
            {
                best = i;
            }
        }

        Assert.True(best >= 0);
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(6)];
        int n = g.Geo.Range(g.Geo.FromIndex(best), 6, area);
        for (int k = 0; k < n; k++)
        {
            int c = g.Geo.Index(area[k]);
            g.Map.Biome.Set(c, (byte)grass);
            g.Map.Relief.Set(c, 0);
            g.Map.Elevation.Set(c, 100);
            g.Trees[c] = SimGame.NoTrees;
            for (int side = 0; side < 3; side++)
            {
                g.Map.River.Set((c * 3) + side, 0);
            }
        }

        foreach (Civ c in g.Civs)
        {
            c.Era = Content.Eras.Count - 1; // rivers no bar
            for (int i = 0; i < g.Geo.CellCount; i++)
            {
                c.Known[i] = true;
            }
        }

        return best;
    }

    private static int Beside(SimGame g, int cell, HexDirection d) => g.Geo.Index(g.Geo.Neighbor(g.Geo.FromIndex(cell), d));

    // A trained unit put on the cell with the given arms.
    private static Unit Soldiers(SimGame g, int civ, string kind, int cell, int people = 0, byte weapon = 1, byte armour = 1)
    {
        Settlement home = g.Settlements[g.Civs[civ].Settlements[0]];
        for (int i = 0; i < home.Stock.Length; i++)
        {
            home.Stock[i] = 900 * SimContent.One;
        }

        home.People = 200;
        foreach (string law in new[] { "code_of_laws", "civil_service", "feudalism" })
        {
            g.Civs[civ].KnownTech[Content.TechId(law)] = true; // room in the army limit for the tests' many units
        }

        UnitType t = Content.Unit(kind);
        foreach (int tech in t.AlsoTechs.Append(t.Tech))
        {
            if (tech >= 0)
            {
                g.Civs[civ].KnownTech[tech] = true;
            }
        }

        Assert.Null(g.Apply(new TrainCommand(civ, home.Id, Content.Unit(kind).Id, people)));
        Unit u = g.Units[^1];
        for (int i = 0; i < 5000 && u.State == UnitState.Training; i++)
        {
            g.Tick();
        }

        Assert.Equal(UnitState.Idle, u.State);
        g.Place(u, cell);
        u.Weapon = weapon;
        u.Armour = armour;
        return u;
    }

    private static long Damage(SimGame g, Unit a, Unit d) => g.StrikeDamage(a, d, Math.Min(12, a.People), Content.Units[a.Kind].Ranged);

    [Fact]
    public void TrainingTakesTheBestArmsTheStoresHoldForAll()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Settlement s = g.Settlements[g.Civs[0].Settlements[0]];
        s.People = 100;
        s.Stock[Content.GoodId("food")] = 500 * SimContent.One;
        s.Stock[Content.GoodId("wood")] = 500 * SimContent.One;
        s.Stock[Content.GoodId("iron_weapons")] = 9 * SimContent.One; // too few for 10
        s.Stock[Content.GoodId("bronze_weapons")] = 15 * SimContent.One;
        s.Stock[Content.GoodId("hide_armour")] = 10 * SimContent.One;
        g.Civs[0].KnownTech[Content.TechId("flint_weapons")] = true;
        Assert.Null(g.Apply(new TrainCommand(0, s.Id, Content.Unit("warriors").Id, 10)));
        Unit u = g.Units[^1];
        Assert.Equal(2, u.Weapon); // bronze: iron is short by one
        Assert.Equal(1, u.Armour);
        Assert.Equal(5 * SimContent.One, s.Stock[Content.GoodId("bronze_weapons")]);
        Assert.Equal(9 * SimContent.One, s.Stock[Content.GoodId("iron_weapons")]);
        Assert.Equal(0, s.Stock[Content.GoodId("hide_armour")]);
    }

    [Fact]
    public void SizeSetsThePeopleThePriceAndTheTime()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Settlement s = g.Settlements[g.Civs[0].Settlements[0]];
        s.People = 100;
        s.Stock[Content.GoodId("food")] = 500 * SimContent.One;
        s.Stock[Content.GoodId("wood")] = 500 * SimContent.One;
        g.Civs[0].KnownTech[Content.TechId("flint_weapons")] = true;
        UnitType t = Content.Unit("warriors");
        Assert.Null(g.Apply(new TrainCommand(0, s.Id, t.Id, 20)));
        Unit big = g.Units[^1];
        Assert.Equal(20, big.People);
        Assert.Equal(80, s.People);
        Assert.Equal((500 - 60) * SimContent.One, s.Stock[Content.GoodId("food")]); // 30 food per 10
        Assert.Null(g.Apply(new TrainCommand(0, s.Id, t.Id, 99, Shock: true)));
        Unit shock = g.Units[^1];
        Assert.Equal(t.MaxPeople, shock.People);
        Assert.Equal(0, shock.Armour);
        Assert.True(shock.NextTick - g.Ticks < big.NextTick - g.Ticks + t.TrainTicks); // 30 people, but at shock speed
    }

    [Fact]
    public void AnArmouryKeepsArmsAtHalfRoom()
    {
        Assert.True(Content.HalfRoom(Content.Building("barracks").Id, Content.GoodId("iron_weapons")));
        Assert.False(Content.HalfRoom(Content.Building("barracks").Id, Content.GoodId("food")));
        Assert.False(Content.HalfRoom(Content.Building("woodcutter").Id, Content.GoodId("iron_weapons")));
    }

    [Fact]
    public void BetterWeaponsAndArmourChangeTheDamage()
    {
        SimGame g = War();
        int f = Field(g);
        Unit a = Soldiers(g, 0, "warriors", f, 10, weapon: 1, armour: 0);
        Unit d = Soldiers(g, 1, "warriors", Beside(g, f, HexDirection.N), 10, weapon: 1, armour: 0);
        long plain = Damage(g, a, d);
        a.Weapon = 3;
        Assert.Equal(plain * 180 / 100, Damage(g, a, d), plain / 20 + 1);
        d.Armour = 4;
        Assert.Equal(plain * 180 / 260, Damage(g, a, d), plain / 20 + 1);
    }

    [Fact]
    public void MoreEnemiesAroundHitHarder()
    {
        SimGame g = War();
        int f = Field(g);
        Unit target = Soldiers(g, 1, "warriors", f, 10);
        target.Stance = Stance.Passive; // no fighting while the others train
        Unit a = Soldiers(g, 0, "warriors", Beside(g, f, HexDirection.N), 10);
        a.Stance = Stance.Passive;
        long one = Damage(g, a, target);
        Soldiers(g, 0, "warriors", Beside(g, f, HexDirection.SE), 10).Stance = Stance.Passive;
        Soldiers(g, 0, "warriors", Beside(g, f, HexDirection.SW), 10).Stance = Stance.Passive;
        g.Place(a, a.Cell); // as after a step: contacts counted anew
        long three = Damage(g, a, target);
        Assert.True(three > one * 14 / 10, $"{one} -> {three}");
    }

    [Fact]
    public void HeightAndTreesCount()
    {
        SimGame g = War();
        int f = Field(g);
        int up = Beside(g, f, HexDirection.N);
        Unit a = Soldiers(g, 0, "warriors", up, 10);
        Unit d = Soldiers(g, 1, "warriors", f, 10);
        Assert.Equal(0, g.Ground(a, d, false));
        g.Map.Relief.Set(up, 1);
        g.Map.Elevation.Set(up, 160);
        Assert.Equal(Content.Combat.ReliefPercent + Content.Combat.ElevationMaxPercent, g.Ground(a, d, false));
        Assert.Equal(-Content.Combat.ReliefPercent - Content.Combat.ElevationMaxPercent, g.Ground(d, a, false));
        g.Trees[f] = SimGame.Forest;
        Assert.Equal(Content.Combat.ReliefPercent + Content.Combat.ElevationMaxPercent + Content.Combat.TreeCover.Melee, g.Ground(a, d, false));
    }

    [Fact]
    public void WalkingNextToAnEnemyStops()
    {
        SimGame g = War();
        int f = Field(g);
        HexCoord c = g.Geo.FromIndex(f);
        Soldiers(g, 1, "warriors", f, 10);
        int start = g.Geo.Index(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.Neighbor(c, HexDirection.N), HexDirection.N), HexDirection.N));
        Unit u = Soldiers(g, 0, "warriors", start, 10);
        int past = g.Geo.Index(g.Geo.Neighbor(g.Geo.Neighbor(c, HexDirection.S), HexDirection.S));
        Assert.Null(g.Apply(new MoveCommand(0, u.Id, past, false)));
        Run(g, 200);
        Assert.Equal(UnitState.Idle, u.State);
        Assert.Equal(1, g.Geo.Distance(g.Geo.FromIndex(u.Cell), c));
    }

    [Fact]
    public void LeavingAnEnemyCostsHalfAStrikeAndRidersAQuarter()
    {
        SimGame g = War();
        int f = Field(g);
        Unit e = Soldiers(g, 1, "warriors", f, 30);
        int beside = Beside(g, f, HexDirection.N);
        int away = Beside(g, beside, HexDirection.N);
        Unit foot = Soldiers(g, 0, "warriors", beside, 30, weapon: 1, armour: 0);
        long full = g.StrikeDamage(e, foot, 12, false);
        Assert.InRange(g.LeavingDamage(foot, away, out _), (full / 2) - 1, (full / 2) + 1);
        Assert.Equal(0, g.LeavingDamage(foot, Beside(g, f, HexDirection.NE), out _)); // still beside it
        g.Place(foot, g.Geo.Index(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.FromIndex(away), HexDirection.N), HexDirection.N)));
        Unit riders = Soldiers(g, 0, "horsemen", beside, 10, weapon: 1, armour: 0);
        long quarter = g.StrikeDamage(e, riders, 12, false) / 4;
        Assert.InRange(g.LeavingDamage(riders, away, out _), quarter - 1, quarter + 1);
        Soldiers(g, 0, "warriors", Beside(g, f, HexDirection.S), 10); // one of ours stays with the enemy
        Assert.Equal(0, g.LeavingDamage(riders, away, out _));
    }

    [Trait("Category", "Slow")]
    [Fact]
    public void WalkingAwayFromAFightHurts()
    {
        static int Lost(bool friendStays)
        {
            SimGame g = War();
            int f = Field(g);
            Soldiers(g, 1, "warriors", f, 30);
            int beside = Beside(g, f, HexDirection.N);
            if (friendStays)
            {
                Soldiers(g, 0, "warriors", Beside(g, f, HexDirection.S), 30).Stance = Stance.Passive;
            }

            Unit u = Soldiers(g, 0, "warriors", beside, 30, weapon: 1, armour: 0);
            u.Stance = Stance.Passive;
            int before = u.People;
            Assert.Null(g.Apply(new MoveCommand(0, u.Id, Beside(g, Beside(g, beside, HexDirection.N), HexDirection.N), false)));
            Run(g, 60);
            return ((before - u.People) * 100) + (int)u.Wounds;
        }

        int alone = Lost(false);
        int covered = Lost(true);
        Assert.True(alone > covered, $"{alone} vs {covered}");
    }

    [Fact]
    public void RidersAfterARunChargeOnce()
    {
        SimGame g = War();
        int f = Field(g);
        Unit d = Soldiers(g, 1, "warriors", f, 30);
        Unit r = Soldiers(g, 0, "horsemen", Beside(g, f, HexDirection.N), 10);
        long still = g.StrikeDamage(r, d, 10, false);
        r.Run = Content.Combat.ChargeHexes;
        r.StepTick = g.Ticks;
        int before = d.People * 100 - (int)d.Wounds;
        while (g.Ticks % Content.Combat.StepTicks != 0)
        {
            g.Tick();
        }

        r.StepTick = g.Ticks;
        g.Tick();
        int charged = before - (d.People * 100 - (int)d.Wounds);
        Assert.Equal(0, r.Run);
        Assert.True(charged > still * 15 / 10, $"{still} vs {charged}");
    }

    [Fact]
    public void ArchersShootOnlyStanding()
    {
        SimGame g = War();
        int f = Field(g);
        Unit d = Soldiers(g, 1, "warriors", f, 30);
        d.Stance = Stance.Passive;
        int two = g.Geo.Index(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.FromIndex(f), HexDirection.N), HexDirection.N));
        g.Civs[0].KnownTech[Content.TechId("archery")] = true;
        Unit a = Soldiers(g, 0, "archers", two, 10);
        a.State = UnitState.Moving; // pretend it walks
        a.NextTick = long.MaxValue;
        Run(g, 20);
        Assert.Equal(0, d.Wounds + ((30 - d.People) * 100));
        a.State = UnitState.Idle;
        Run(g, 20);
        Assert.True(d.Wounds + ((30 - d.People) * 100) > 0);
    }

    [Fact]
    public void SplitMakesASecondUnitBeside()
    {
        SimGame g = War();
        int f = Field(g);
        Unit u = Soldiers(g, 0, "warriors", f, 30, weapon: 2, armour: 1);
        Assert.Null(g.Apply(new SplitCommand(0, u.Id, 12)));
        Unit part = g.Units[^1];
        Assert.Equal(18, u.People);
        Assert.Equal(12, part.People);
        Assert.Equal(2, part.Weapon);
        Run(g, 60);
        Assert.Equal(1, g.Geo.Distance(g.Geo.FromIndex(u.Cell), g.Geo.FromIndex(part.Cell)));
        Assert.NotNull(g.Apply(new SplitCommand(0, part.Id, 10))); // 2 would be left
    }

    [Fact]
    public void FormationPutsMeleeInFrontAndArchersBehind()
    {
        SimGame g = War();
        int f = Field(g);
        HexCoord c = g.Geo.FromIndex(f);
        int from = g.Geo.Index(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.Neighbor(g.Geo.Neighbor(c, HexDirection.S), HexDirection.S), HexDirection.S), HexDirection.S));
        g.Civs[0].KnownTech[Content.TechId("archery")] = true;
        Unit m1 = Soldiers(g, 0, "warriors", from, 10);
        Unit m2 = Soldiers(g, 0, "warriors", from, 10);
        Unit a1 = Soldiers(g, 0, "archers", from, 10);
        Assert.Null(g.Apply(new FormationCommand(0, [m1.Id, m2.Id, a1.Id], f, (int)HexDirection.N)));
        Run(g, 400);
        List<int> row = g.FormationRow(f, (int)HexDirection.N, 2);
        Assert.Equal(row[0], m1.Cell);
        Assert.Equal(row[1], m2.Cell);
        Assert.Equal(Beside(g, f, HexDirection.S), a1.Cell);
    }

    [Fact]
    public void StrikesAreKeptForTheViewAndWeakUnitsAreTold()
    {
        SimGame g = War();
        int f = Field(g);
        Unit a = Soldiers(g, 0, "warriors", f, 30);
        Unit d = Soldiers(g, 1, "warriors", Beside(g, f, HexDirection.N), 10);
        Assert.Equal(10, d.Size);
        Run(g, Content.Combat.StepTicks + 1);
        Assert.Contains(d.Id, a.Struck);
        g.News.Clear();
        for (int i = 0; i < 2000 && d.State != UnitState.Gone && d.People * 100 > d.Size * 50; i++)
        {
            g.Tick();
        }

        Assert.Contains(g.News, n => n.Item1 == 1 && n.Item2.Contains("down to", StringComparison.Ordinal));
    }

    [Fact]
    public void ARefusedMoveLeavesTheWalkerOnItsWay()
    {
        SimGame g = War();
        int f = Field(g);
        Unit u = Soldiers(g, 0, "warriors", f, 10);
        int far = Beside(g, Beside(g, Beside(g, f, HexDirection.S), HexDirection.S), HexDirection.S);
        Assert.Null(g.Apply(new MoveCommand(0, u.Id, far, false)));
        g.Tick();
        int water = Enumerable.Range(0, g.Geo.CellCount).First(c => Content.World.Biomes[g.Map.Biome[c]].IsWater);
        Assert.NotNull(g.Apply(new MoveCommand(0, u.Id, water, false)));
        Assert.NotNull(g.Apply(new MoveCommand(0, u.Id, far, false, [far, water])));
        Assert.True(u.Path.Count > 0);
        Run(g, 200);
        Assert.Equal(far, u.Cell);
        Assert.Equal(UnitState.Idle, u.State);
    }

    [Fact]
    public void SoldiersJoinASettlementAndLoseTheirArms()
    {
        // P5 (Patryk, 2026-11-23): army units may join a settlement on our land; their people come home, their arms do not.
        SimGame g = War();
        Settlement home = g.Settlements[g.Civs[0].Settlements[0]];
        Unit u = Soldiers(g, 0, "warriors", home.Center, 10, weapon: 2, armour: 1);
        int before = home.People;
        long[] stock = (long[])home.Stock.Clone();
        u.Injured = 2;
        Assert.Null(g.Apply(new DisbandCommand(0, u.Id)));
        Assert.Equal(UnitState.Gone, u.State);
        Assert.Equal(before + 12, home.People);
        Assert.Equal(stock, home.Stock);
    }
}
