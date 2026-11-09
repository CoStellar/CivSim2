using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Events;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Events;

/// <summary>Drought, wildfire, flood and epidemic from data/events.json (stage 3h, GDD 10).</summary>
public class EventTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static EventDefinition Event(string key) => Content.Events.First(e => e.Key == key);

    // A fresh world without the starting camps, and its land cells.
    private static (GameState State, WorldState World, HexCoord[] Land) Empty(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        state.RemoveSettlements(_ => true);
        WorldState world = state.Worlds[0];
        var land = new List<HexCoord>();
        for (int i = 0; i < world.Geometry.CellCount; i++)
        {
            if (!Content.Biomes[world.Map.Biome[i]].IsWater)
            {
                land.Add(world.Geometry.FromIndex(i));
            }
        }

        return (state, world, land.ToArray());
    }

    private static SettlementState Camp(GameState state, CivId civ, HexCoord cell, int people)
    {
        SettlementState s = state.FoundSettlement(civ, state.Worlds[0].Id, cell, "Camp", Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Content.Strata[0].Id] = people;
        return s;
    }

    // The same event, certain to strike (10 000 times in 10 000 years, no conditions).
    private static EventDefinition Certain(string key) => Event(key) with { TimesPer10000Years = 10_000, Conditions = [] };

    private static void Run(GameState state, ITurnSystem system)
    {
        var runner = new TurnRunner(state);
        runner.AddSystem(system);
        runner.RunTurn();
    }

    [Fact]
    public void EventsLoadFromData()
    {
        Assert.Equal(["drought", "wildfire", "flood", "epidemic"], Content.Events.Select(e => e.Key));
        Assert.Contains(Event("drought").SoftenedBy, s => s.Technology == Content.Technology("irrigation").Id);
        Assert.Equal(Content.Effect("extraction_reduction").Id, Event("drought").Effects[0].Effect);
        Assert.True(Event("epidemic").SpreadPercent > 0);
    }

    [Fact]
    public void ChanceFollowsTheLand()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(71);
        var system = new EventSystem(Content);
        SettlementState camp = Camp(state, state.Civilizations[0].Id, land[0], 100);
        int cell = world.Geometry.Index(camp.Cell);
        for (int d = 0; d < 6; d++)
        {
            world.Map.River.Set(world.Geometry.EdgeIndex(world.Geometry.Edge(camp.Cell, (HexDirection)d)), 0);
        }

        world.Map.Humidity.Set(cell, 20);
        long dry = system.YearChanceMillionths(state, world, camp, Event("drought"));
        world.Map.Humidity.Set(cell, 60);
        long normal = system.YearChanceMillionths(state, world, camp, Event("drought"));
        world.Map.Humidity.Set(cell, 90);
        long wet = system.YearChanceMillionths(state, world, camp, Event("drought"));
        Assert.True(dry > normal && normal > wet);
        Assert.Equal(0, wet);

        Assert.Equal(0, system.YearChanceMillionths(state, world, camp, Event("flood"))); // no river
        world.Map.River.Set(world.Geometry.EdgeIndex(world.Geometry.Edge(camp.Cell, HexDirection.N)), 1);
        Assert.True(system.YearChanceMillionths(state, world, camp, Event("flood")) > 0);

        // Epidemics need a large civilization (GDD 10.2): give the owner a big village far away.
        Camp(state, camp.Owner, land.First(c => world.Geometry.Distance(camp.Cell, c) > 10), 20_000);
        Assert.Equal(0, system.YearChanceMillionths(state, world, camp, Event("epidemic"))); // under 300 people
        camp.Population[0] = 400;
        long small = system.YearChanceMillionths(state, world, camp, Event("epidemic"));
        camp.Population[0] = 1500;
        Assert.True(small > 0);
        Assert.True(system.YearChanceMillionths(state, world, camp, Event("epidemic")) > small); // crowding
    }

    [Fact]
    public void EpidemicsSpareSmallCivilizations()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(77);
        var system = new EventSystem(Content);
        SettlementState village = Camp(state, state.Civilizations[0].Id, land[0], 5_000);
        Assert.Equal(0, system.YearChanceMillionths(state, world, village, Event("epidemic")));

        // The same village in a civilization of more than 10 000 people can fall ill.
        Camp(state, village.Owner, land.First(c => world.Geometry.Distance(village.Cell, c) > 10), 6_000);
        Assert.True(system.YearChanceMillionths(state, world, village, Event("epidemic")) > 0);
    }

    [Fact]
    public void AnEpidemicDoesNotSpreadIntoASmallCivilization()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(78);
        HexCoord a = land[0];
        HexCoord b = land.First(c => world.Geometry.Distance(a, c) is > 0 and <= 4);
        SettlementState big = Camp(state, state.Civilizations[0].Id, a, 200);
        SettlementState linked = Camp(state, state.Civilizations[0].Id, b, 100);
        EventDefinition epidemic = Event("epidemic") with
        {
            TimesPer10000Years = 10_000,
            Conditions =
            [
                new EventCondition(EventConditionKind.PeopleAtLeast, 101, [], 100, true),
                new EventCondition(EventConditionKind.CivilizationPeopleAtLeast, 1_000, [], 100, true),
            ],
            SpreadPercent = 100,
        };
        Run(state, new EventSystem(Content, [epidemic]));
        int depopulation = Content.Effect("depopulation").Id;
        Assert.DoesNotContain(state.Effects.All, e => e.DefinitionId == depopulation); // 300 people: too small

        big.Population[0] = 2_000;
        Run(state, new EventSystem(Content, [epidemic]));
        Assert.Contains(state.Effects.All, e => e.DefinitionId == depopulation && e.TargetId == linked.Id.Value);
    }

    [Fact]
    public void TurnChanceComesFromTheYearlyChance()
    {
        Assert.Equal(0, EventSystem.TurnChanceMillionths(0, 600));
        Assert.Equal(1000, EventSystem.TurnChanceMillionths(1000, 12));
        long fifty = EventSystem.TurnChanceMillionths(1000, 600); // 1 - 0.999^50 = 4.88%
        Assert.InRange(fifty, 48_700, 48_900);
        Assert.InRange(EventSystem.TurnChanceMillionths(1000, 18), 1490, 1500);
    }

    [Fact]
    public void TechnologySoftensAndLongTurnsDiluteEffects()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(72), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        EventDefinition drought = Event("drought");
        EventEffect effect = drought.Effects[0]; // -40 for 8 years
        Assert.Equal(-40, EventSystem.Strength(Content, civ, drought, effect, 12));
        Assert.Equal(-40 * 8 / 50, EventSystem.Strength(Content, civ, drought, effect, 600));
        CivSim.Core.Research.Technologies.EnsureSize(Content, civ);
        civ.KnownTechnologies[Content.Technology("irrigation").Id] = true;
        Assert.Equal(-20, EventSystem.Strength(Content, civ, drought, effect, 12));
    }

    [Fact]
    public void ADroughtLowersFarmingAroundTheVillage()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(73);
        CivilizationState civ = state.Civilizations[0];
        SettlementState village = Camp(state, civ.Id, land[land.Length / 2], 200);
        village.Level = SettleCommand.VillageLevel(Content, village.Level);
        village.Skilled[Content.SkillRules.FarmingSkill] = village.PopulationView[0]; // only farmers farm (stage 4F)
        Run(state, new CivSim.Core.Population.InfluenceSystem(Content, TurnPhase.World));
        foreach (HexCoord c in land)
        {
            world.Map.Fertility.Set(world.Geometry.Index(c), 80);
        }

        var farming = new FarmingSystem(Content);
        Run(state, farming);
        long before = village.FoodGathered;
        Assert.True(before > 0);

        Run(state, new EventSystem(Content, [Certain("drought")]));
        Assert.Contains(state.Effects.All, e => e.Target == EffectTarget.Cell && e.TargetId == world.Geometry.Index(village.Cell));
        Run(state, farming);
        Assert.True(village.FoodGathered < before);
    }

    [Fact]
    public void AFloodLeavesRicherSoil()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(74);
        SettlementState camp = Camp(state, state.Civilizations[0].Id, land[land.Length / 3], 100);
        int cell = world.Geometry.Index(camp.Cell);
        world.Map.Fertility.Set(cell, 40);
        Run(state, new EventSystem(Content, [Certain("flood")]));
        Assert.Equal(40 + Event("flood").FertilityGain, world.Map.Fertility[cell]);
        Assert.Contains(state.Effects.All, e => e.Target == EffectTarget.Settlement && e.TargetId == camp.Id.Value);
    }

    [Fact]
    public void AnEpidemicSpreadsAlongTheTradeNetwork()
    {
        (GameState state, WorldState world, HexCoord[] land) = Empty(75);
        CivId civ = state.Civilizations[0].Id;
        HexCoord a = land[0];
        HexCoord b = land.First(c => world.Geometry.Distance(a, c) is > 0 and <= 4);
        HexCoord far = land.First(c => world.Geometry.Distance(a, c) > 30);
        SettlementState first = Camp(state, civ, a, 100);
        SettlementState linked = Camp(state, civ, b, 100);
        SettlementState alone = Camp(state, civ, far, 100);

        // Only the first camp can be struck on its own; the epidemic always spreads to its network.
        EventDefinition epidemic = Event("epidemic") with
        {
            TimesPer10000Years = 10_000,
            Conditions = [new EventCondition(EventConditionKind.PeopleAtLeast, 101, [], 100, true)],
            SpreadPercent = 100,
        };
        first.Population[0] = 200;
        Run(state, new EventSystem(Content, [epidemic]));
        int depopulation = Content.Effect("depopulation").Id;
        bool Sick(SettlementState s) => state.Effects.All.Any(e => e.DefinitionId == depopulation && e.TargetId == s.Id.Value);
        Assert.True(Sick(first));
        Assert.True(Sick(linked));
        Assert.False(Sick(alone));
    }

    [Fact]
    public void EventsAreDeterministic()
    {
        static ulong Hash()
        {
            GameState state = GameState.CreateNew(GameSettings.Test(76), Content, out _);
            var runner = new TurnRunner(state);
            CoreSystems.Register(runner, Content);
            for (int t = 0; t < 30; t++)
            {
                runner.RunTurn();
            }

            return GameStateHash.Compute(state);
        }

        Assert.Equal(Hash(), Hash());
    }
}
