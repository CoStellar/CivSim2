using System.Linq;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;
using Xunit;

namespace CivSim.Sim.Tests;

/// <summary>Decisions of Patryk after the tests of 2026-11-20 (claude/decyzje-testy-2026-11-20.md): 1C, 2B, 3B, 5B, 6C, 13B, 14C.</summary>
public sealed class WarRulesTests
{
    private static readonly SimContent Content = SimContent.Load();

    private static void Run(SimGame g, int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            g.Tick();
        }
    }

    private static Unit Soldiers(SimGame g, Civ civ, string kind, int near, int people = 0)
    {
        Settlement home = g.Settlements[civ.Settlements[0]];
        for (int i = 0; i < home.Stock.Length; i++)
        {
            home.Stock[i] = 500 * SimContent.One;
        }

        home.People = 80;
        civ.KnownTech[Content.Unit(kind).Tech] = true;
        int before = g.Units.Count;
        Assert.Null(g.Apply(new TrainCommand(civ.Id, home.Id, Content.Unit(kind).Id, people)));
        Unit u = g.Units.Skip(before).First(x => x.Civ == civ.Id && Content.Units[x.Kind].Army);
        for (int i = 0; i < 5000 && u.State == UnitState.Training; i++)
        {
            g.Tick();
        }

        g.Place(u, g.Geo.Index(g.Geo.Neighbor(g.Geo.FromIndex(near), HexDirection.N)));
        return u;
    }

    // A settlement of b brought to the point of falling to a.
    private static (SimGame G, Settlement Target) Fallen()
    {
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        for (ulong seed = 2; !g.Reachable(g.Civs[1], g.Civs[0]) && seed < 40; seed++)
        {
            g = SimGame.Create(Content, new SimSettings(seed, 66, 40, 2) { Ai = false }).Basics(); // the loot caravan needs a way home
        }

        Civ a = g.Civs[0];
        Settlement target = g.Settlements[g.Civs[1].Settlements[0]];
        for (int i = 0; i < target.Stock.Length; i++)
        {
            target.Stock[i] = 200 * SimContent.One;
        }

        Unit u = Soldiers(g, a, "warriors", target.Center);
        u.People = 40;
        g.Apply(new AttackCommand(a.Id, u.Id, target.Center));
        for (int i = 0; i < 3000 && target.FallenTo < 0; i++)
        {
            g.Tick();
        }

        Assert.Equal(a.Id, target.FallenTo);
        return (g, target);
    }

    [Fact]
    public void TheAssaultWaitsForTheBlockade()
    {
        // 13B: nobody falls in the first siege.assaultAfterSeconds of a blockade.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Settlement target = g.Settlements[g.Civs[1].Settlements[0]];
        Unit u = Soldiers(g, g.Civs[0], "warriors", target.Center);
        u.People = 40;
        Assert.Null(g.Apply(new AttackCommand(0, u.Id, target.Center)));
        Run(g, Content.Siege.AssaultAfterTicks - 20);
        Assert.True(target.Besieged);
        Assert.Equal(0, g.Score(0, 1)); // nobody killed yet
        Run(g, 200);
        Assert.True(g.Score(0, 1) > 0, "the assault began");
    }

    [Fact]
    public void TheWarScoreCountsFromTheStartOfTheWar()
    {
        // 1C: wins of an old war do not make the next war pay at once.
        (SimGame g, Settlement target) = Fallen();
        Assert.True(g.Score(0, 1) > 0);
        g.Apply(new TakeSettlementCommand(0, target.Id, TakeChoice.Sack));
        g.MakePeace(g.Civs[0], g.Civs[1], 0);
        Run(g, (Content.Peace.Minutes[0] * SimContent.TicksPerMinute) + 1);
        Assert.Null(g.Apply(new DeclareWarCommand(0, 1)));
        Assert.Equal(0, g.Score(0, 1));
        var demand = new PeaceTerms(false, 0, -1, [(Content.Food, SimContent.One)]);
        Assert.NotNull(g.Apply(new ProposePeaceCommand(0, 1, demand))); // and no demands from an even war
        Assert.Null(g.Apply(new ProposePeaceCommand(0, 1, PeaceTerms.White())));
    }

    [Fact]
    public void LootGoesHomeInACaravan()
    {
        // 2B: the loot no longer jumps to our settlement; and the same settlement is safe for 10 minutes.
        (SimGame g, Settlement target) = Fallen();
        Settlement home = g.Settlements[g.Civs[0].Settlements[0]];
        int wood = Content.GoodId("wood");
        long before = home.Stock[wood];
        long theirs = target.Stock[wood];
        Assert.Null(g.Apply(new TakeSettlementCommand(0, target.Id, TakeChoice.Sack)));
        Unit? caravan = g.Units.LastOrDefault(x => x.Civ == 0 && x.Job == CarrierJob.ToSettlement && x.Target == home.Id && x.State != UnitState.Gone);
        Assert.NotNull(caravan);
        Assert.Equal(theirs * Content.Siege.SackPercent / 100, caravan.Cargo![wood]);
        Assert.True(home.Stock[wood] <= before, "nothing arrives before the caravan");
        Assert.Equal(g.Ticks + (600 * SimContent.TicksPerSecond), target.SackedUntil);
    }

    [Fact]
    public void ARazedStoreKeepsAQuarter()
    {
        // 3B: 75% of a razed settlement's store is lost.
        (SimGame g, Settlement target) = Fallen();
        long food = target.Stock[Content.Food];
        Assert.Null(g.Apply(new TakeSettlementCommand(0, target.Id, TakeChoice.Raze)));
        Assert.True(target.Abandoned);
        Assert.Equal(food * Content.RuinKeepPercent / 100, target.Stock[Content.Food]);
        Assert.Equal(25, Content.RuinKeepPercent);
    }

    [Fact]
    public void ReputationHasAFloor()
    {
        // 6C: reputation never falls below war.reputationFloor.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        a.Known[g.Settlements[g.Civs[1].Settlements[0]].Center] = true;
        for (int k = 0; k < 40; k++)
        {
            Assert.Null(g.Apply(new DeclareWarCommand(0, 1)));
            g.MakePeace(a, g.Civs[1], 0);
            g.EndWhiteTruceForTest(0, 1); // P5: a white peace forbids a new war for 3 minutes
        }

        Assert.Equal(Content.War.ReputationFloor, a.Reputation);
    }

    [Fact]
    public void SettlersCountOnlySettlementsTheirPeopleKnows()
    {
        // 5B: the refusal gave away a city the people had never seen.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        Settlement theirs = g.Settlements[g.Civs[1].Settlements[0]];
        int cell = Enumerable.Range(0, g.Geo.CellCount).First(c => g.OwnerAt[c] < 0 && !Content.World.Biomes[g.Map.Biome[c]].IsWater
            && g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(theirs.Center)) < Content.FoundMinDistance && !g.Visible(a, theirs.Center));
        Assert.NotNull(g.CannotFound(cell));
        Assert.Null(g.CannotFound(cell, a));
    }

    [Fact]
    public void AKillCountsForEveryKindFightingNearby()
    {
        // 14C: archers no longer take all the experience; the spearmen next to them share it.
        SimGame g = SimGame.Create(Content, new SimSettings(1, 66, 40, 2) { Ai = false }).Basics();
        Civ a = g.Civs[0];
        Civ b = g.Civs[1];
        int field = Enumerable.Range(0, g.Geo.CellCount).First(c => !Content.World.Biomes[g.Map.Biome[c]].IsWater && g.OwnerAt[c] < 0
            && g.Settlements.All(s => g.Geo.Distance(g.Geo.FromIndex(c), g.Geo.FromIndex(s.Center)) > 8));
        Unit enemy = Soldiers(g, b, "warriors", field, 30);
        g.Place(enemy, field);
        Unit spear = Soldiers(g, a, "spearmen", field, 20);
        Unit bows = Soldiers(g, a, "archers", field, 20);
        g.Place(bows, g.Geo.Index(g.Geo.Neighbor(g.Geo.FromIndex(field), HexDirection.S)));
        Assert.Null(g.Apply(new AttackCommand(0, spear.Id, field)));
        Assert.Null(g.Apply(new AttackCommand(0, bows.Id, field)));
        Run(g, 300);
        int both = a.KindKills[spear.Kind] + a.KindKills[bows.Kind];
        Assert.True(both > 0, "somebody was killed");
        Assert.True(a.KindKills[spear.Kind] > 0 && a.KindKills[bows.Kind] > 0, $"spearmen {a.KindKills[spear.Kind]}, archers {a.KindKills[bows.Kind]}");
    }
}
