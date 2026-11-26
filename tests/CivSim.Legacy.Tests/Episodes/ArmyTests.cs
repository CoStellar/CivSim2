using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Protocol.Messages;
using CivSim.Server;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Episodes;

/// <summary>Arms, armies of several settlements, carriers and hunger on campaigns (stage 4e PR 3).</summary>
public class ArmyTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static int Warriors => Content.Strata.Single(s => s.Key == "warriors").Id;

    private static int Producers => Content.Strata[0].Id;

    private static int Good(string key) => Content.Goods.Single(g => g.Key == key).Id;

    // A strip of land cells along one column, long enough for the test; everyone sees and knows everything.
    internal static (GameState State, HexCoord Start) Strip(ulong seed, int length)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        state.RemoveSettlements(_ => true);
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        for (int i = 0; i < geo.CellCount; i++)
        {
            HexCoord from = geo.FromIndex(i);
            if (from.Row + length >= geo.Height)
            {
                continue;
            }

            bool way = true;
            for (int r = 0; r <= length && way; r++)
            {
                way = !Content.Biomes[world.Map.Biome[geo.Index(new HexCoord(from.Col, from.Row + r))]].IsWater;
            }

            if (!way)
            {
                continue;
            }

            foreach (CivilizationState c in state.Civilizations)
            {
                c.SeenYear = new int[geo.CellCount];
                System.Array.Fill(c.SeenYear, 1);
                foreach (CivilizationState y in state.Civilizations)
                {
                    if (c != y)
                    {
                        Contacts.Meet(Content, state, c, y.Id, ContactWay.Sighted);
                    }
                }
            }

            return (state, from);
        }

        throw new Xunit.Sdk.XunitException("No land strip found.");
    }

    internal static SettlementState Village(GameState state, CivilizationState civ, HexCoord start, int offset, int producers, int warriors, int food = 0)
    {
        var cell = new HexCoord(start.Col, start.Row + offset);
        SettlementState s = state.FoundSettlement(civ.Id, state.Worlds[0].Id, cell, civ.Name + " " + offset, Content.SettlementLevel("village").Id, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Producers] = producers;
        s.ScoutOut = true; // no scouts in these tests: they would take people (stage 6 PR 3b)
        s.Population[Warriors] = warriors;
        s.Stock[Good("wild_plants")] = Amount.FromUnits(food);
        return s;
    }

    internal static TurnRunner Runner(GameState state)
    {
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Alarms = Alarm.None;
        }

        var runner = new TurnRunner(state);
        runner.AddSystem(new Core.Units.UnitSystem(Content)); // stage 6 PR 3b: armies and carriers walk as units
        runner.AddSystem(new CampaignSystem(Content));
        runner.AddSystem(new WarSystem(Content));
        runner.AddSystem(new CalendarSystem(Content));
        WorldClock.Begin(Content, state);
        return runner;
    }

    // The step in the month of the launch, then the campaign's first own step a month later.
    internal static void FirstStep(TurnRunner runner)
    {
        runner.RunTurn();
        runner.RunTurn();
    }

    // Runs until no campaign is out (at most 200 ticks); returns the chronicle.
    internal static List<string> RunOut(TurnRunner runner, GameState state, System.Action? each = null)
    {
        var lines = new List<string>();
        for (int k = 0; k < 200 && state.Episodes.Count > 0; k++)
        {
            runner.RunTurn();
            lines.AddRange(runner.LastChronicle);
            each?.Invoke();
        }

        Assert.Equal(0, state.Episodes.Count);
        return lines;
    }

    [Fact]
    public void ArmsAndEraLimitsAreInTheData()
    {
        Assert.Equal(["bare_hands", "fire_hardened_spears", "slings", "stone_tipped_spears"], Content.War.Armaments.Select(a => a.Key));
        Assert.Equal(50, Campaigns.BareHandsPercent(Content));
        ArmamentDefinition stone = Content.War.Armaments[3];
        Assert.Equal(Content.Technology("flint_knapping").Id, stone.Technology);
        Assert.Equal(Good("stone_tools"), stone.Good);
        Assert.Equal(Amount.Parse("0.2"), stone.PerWarrior);
        Assert.All(Content.Eras.Take(2), e => Assert.Equal((50, 50), (e.ArmyLimit, e.TroopsPerCell)));
    }

    [Fact]
    public void EveryWarriorTakesTheStrongestArmsStillInStock()
    {
        (GameState state, HexCoord start) = Strip(81, 2);
        CivilizationState a = state.Civilizations[0];
        SettlementState s = Village(state, a, start, 0, 50, 30);
        s.Stock[Good("stone_tools")] = Amount.FromUnits(1); // 5 warriors
        s.Stock[Good("hides")] = Amount.Parse("0.3"); // 3 warriors
        s.Stock[Good("wood")] = Amount.Parse("0.4"); // 2 warriors

        Assert.Equal(15 * 50, Campaigns.ArmedStrength(Content, a, s, 15, useUp: false)); // nothing known: bare hands
        a.KnownTechnologies = new bool[Content.Technologies.Count];
        foreach (string key in new[] { "fire", "flint_knapping", "tanning" })
        {
            a.KnownTechnologies[Content.Technology(key).Id] = true;
        }

        Assert.Equal((5 * 100) + (3 * 80) + (2 * 70) + (5 * 50), Campaigns.ArmedStrength(Content, a, s, 15, useUp: true));
        Assert.Equal(Amount.Zero, s.Stock[Good("stone_tools")]);
        Assert.Equal(Amount.Zero, s.Stock[Good("hides")]);
        Assert.Equal(Amount.Zero, s.Stock[Good("wood")]);
        Assert.Equal(15 * 50, Campaigns.ArmedStrength(Content, a, s, 15, useUp: false)); // used up
    }

    [Fact]
    public void AnArmyGathersFromSeveralSettlementsAndGoesBackToThem()
    {
        (GameState state, HexCoord start) = Strip(82, 10);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState ally = Village(state, a, start, 0, 80, 15, 100);
        SettlementState home = Village(state, a, start, 3, 120, 30, 200);
        SettlementState target = Village(state, b, start, 9, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        var others = new[] { new CampaignMuster(ally.Id, 10) };
        var launch = new LaunchCampaignCommand(Content, home.Id, target.Id, 20, others);
        Assert.True(launch.Validate(state, a.Id).Success, launch.Validate(state, a.Id).Reason);
        Assert.Contains("2 settlements", launch.ApplyWithChronicle(state, a.Id), System.StringComparison.Ordinal);
        Episode e = Assert.Single(state.Episodes.All);
        Assert.Equal(CampaignPhase.Muster, e.Phase); // the warriors of the ally walk 3 cells: a month of gathering
        Assert.Equal(30, e.Mustered);
        Assert.Equal((10, 5), (home.Population[Warriors], ally.Population[Warriors]));
        Assert.True(Campaigns.FoodIn(Content, e.Food) > Amount.Zero, "they took food along");

        // Stage 6 PR 3b: the ally's warriors walk to the home settlement as a unit; the march starts once they stand there.
        TurnRunner runner = Runner(state);
        for (int t = 0; t < 30 && e.Phase == CampaignPhase.Muster; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(CampaignPhase.March, e.Phase);
        RunOut(runner, state);
        Assert.True(home.Population[Warriors] > 10, "survivors came home");
        Assert.True(ally.Population[Warriors] > 5, "the ally's survivors went back to it");
    }

    [Fact]
    public void ArmiesKeepAThirdOfEverySettlementHomeAndStayWithinTheLimits()
    {
        (GameState state, HexCoord start) = Strip(83, 16);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState far = Village(state, a, start, 0, 80, 15);
        SettlementState ally = Village(state, a, start, 6, 80, 15);
        SettlementState home = Village(state, a, start, 8, 200, 90);
        SettlementState target = Village(state, b, start, 14, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        Assert.False(new LaunchCampaignCommand(Content, home.Id, target.Id, 20, [new CampaignMuster(ally.Id, 11)]).Validate(state, a.Id).Success, "a third of the ally stays home");
        Assert.False(new LaunchCampaignCommand(Content, home.Id, target.Id, 20, [new CampaignMuster(far.Id, 5)]).Validate(state, a.Id).Success, "too far to join");
        Assert.False(new LaunchCampaignCommand(Content, home.Id, target.Id, 45, [new CampaignMuster(ally.Id, 10)]).Validate(state, a.Id).Success, "more than one leader leads");
        Assert.False(new LaunchCampaignCommand(Content, home.Id, target.Id, 20, [new CampaignMuster(ally.Id, 5), new CampaignMuster(ally.Id, 5)]).Validate(state, a.Id).Success, "listed twice");
        Assert.True(new LaunchCampaignCommand(Content, home.Id, target.Id, 40, [new CampaignMuster(ally.Id, 10)]).Validate(state, a.Id).Success);
        Assert.Equal(1, Campaigns.FrontCells(Content, a, 50));
        Assert.Equal(2, Campaigns.FrontCells(Content, a, 51)); // a bigger army would stand on two cells (a front)
    }

    [Fact]
    public void CarriersBringFoodAfterTheArmy()
    {
        (GameState state, HexCoord start) = Strip(84, 14);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState depot = Village(state, a, start, 0, 100, 2, 500);
        SettlementState home = Village(state, a, start, 2, 120, 30);
        SettlementState target = Village(state, b, start, 14, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 20).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        Assert.Equal(Amount.Zero, Campaigns.FoodIn(Content, e.Food)); // the home has nothing to give

        TurnRunner runner = Runner(state);
        FirstStep(runner);
        Assert.Equal(depot.Id.Value, e.ConvoyFrom);
        Assert.True(e.ConvoyCarriers > 0);
        Assert.Equal(100 - e.ConvoyCarriers, depot.Population[Producers]);
        int convoys = 0;
        RunOut(runner, state, () => convoys = System.Math.Max(convoys, e.Convoys));
        Assert.True(convoys > 0, "the carriers reached the army");
        Assert.Equal(100, depot.Population[Producers]); // the carriers went home
    }

    [Fact]
    public void AHungryArmyLosesHeartAndTurnsBack()
    {
        (GameState state, HexCoord start) = Strip(85, 16);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = Village(state, a, start, 0, 120, 30);
        SettlementState target = Village(state, b, start, 16, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 20).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        TurnRunner runner = Runner(state);
        FirstStep(runner);
        Assert.Equal(100 - Content.War.HungerMoralePerMonth, e.Morale);
        Assert.Equal(1, e.HungryMonths);
        List<string> lines = RunOut(runner, state);
        Assert.Contains(lines, l => l.Contains("Hunger broke", System.StringComparison.Ordinal));
        Assert.Equal(4, target.Population[Warriors]);
    }

    [Fact]
    public void AHungryArmyRaidsAVillageOnTheWayForFood()
    {
        (GameState state, HexCoord start) = Strip(86, 14);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState home = Village(state, a, start, 0, 120, 30);
        SettlementState prey = Village(state, b, start, 4, 10, 2, 100);
        SettlementState goal = Village(state, b, start, 14, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, goal.Id, 20).ApplyWithChronicle(state, a.Id);
        Episode e = state.Episodes.All[0];
        TurnRunner runner = Runner(state);
        FirstStep(runner);
        Assert.True(e.Foraging);
        Assert.Equal(prey.Id.Value, e.Target);
        Assert.Contains(runner.LastChronicle, l => l.Contains("turned on " + prey.Name, System.StringComparison.Ordinal));
        int forays = 0;
        RunOut(runner, state, () => forays = System.Math.Max(forays, e.Forays));
        Assert.Equal(1, forays);
    }

    [Fact]
    public void ArmiesAndCarriersSurviveASaveAndShowForObservers()
    {
        (GameState state, HexCoord start) = Strip(87, 14);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState depot = Village(state, a, start, 0, 100, 2, 500);
        SettlementState ally = Village(state, a, start, 1, 80, 15);
        SettlementState home = Village(state, a, start, 2, 120, 30);
        SettlementState target = Village(state, b, start, 14, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        new LaunchCampaignCommand(Content, home.Id, target.Id, 20, [new CampaignMuster(ally.Id, 10)]).ApplyWithChronicle(state, a.Id);
        TurnRunner runner = Runner(state);
        FirstStep(runner);
        Assert.True(state.Episodes.All[0].ConvoyFrom >= 0, "carriers are out");

        ArmyView army = Assert.Single(new ObserverViewBuilder(Content).Build(state).Armies!);
        Assert.Equal((a.Id.Value, b.Id.Value, 30, 1), (army.Owner, army.Enemy, army.Warriors, army.Cells));
        Assert.True(army.Carriers > 0);

        using var stream = new MemoryStream();
        SaveGameSerializer.Save(state, stream);
        stream.Position = 0;
        GameState loaded = SaveGameSerializer.Load(stream);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
        Episode e = Assert.Single(loaded.Episodes.All);
        Assert.Equal([home.Id.Value, ally.Id.Value], e.Members);
        TurnRunner other = Runner(loaded);
        for (int k = 0; k < 5; k++)
        {
            runner.RunTurn();
            other.RunTurn();
        }

        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
    }

    [Fact]
    public void TheCommanderGathersAnArmyFromSettlementsNearby()
    {
        (GameState state, HexCoord start) = Strip(88, 12);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        SettlementState ally = Village(state, a, start, 0, 80, 15, 100);
        SettlementState home = Village(state, a, start, 3, 120, 20, 100);
        SettlementState target = Village(state, b, start, 11, 20, 4);
        Treaties.DeclareWar(Content, state, a.Id, b.Id, WarGoal.Loot, -1);
        var queue = new Core.Commands.CommandQueue();
        new Core.Ai.CommanderAdvisor(Content).IssueCommands(state, queue);
        var list = new List<Core.Commands.CommandEnvelope>();
        queue.DrainSorted(state, state.Turn, list);
        Core.Commands.CommandEnvelope sent = Assert.Single(list, c => c.Civ == a.Id);
        var launch = Assert.IsType<LaunchCampaignCommand>(sent.Command);
        Assert.True(launch.Validate(state, a.Id).Success, launch.Validate(state, a.Id).Reason);
        Assert.Equal(target.Id, launch.Target);
        CampaignMuster joined = Assert.Single(launch.Others!);
        Assert.Equal((launch.Home == home.Id ? ally.Id : home.Id), joined.Settlement);
        Assert.Equal(13 + 10, launch.Warriors + joined.Warriors); // two thirds of each
    }

    [Fact]
    public void BrokenArmsDataIsReported()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-arms-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "war.json");
            File.WriteAllText(path, File.ReadAllText(path)
                .Replace("\"technology\": \"tanning\", \"good\": \"hides\"", "\"technology\": \"tannin\", \"good\": \"hidez\"", System.StringComparison.Ordinal)
                .Replace("{\"key\": \"bare_hands\", \"name\": \"Bare Hands and Sticks\", \"strengthPercent\": 50},", string.Empty, System.StringComparison.Ordinal));
            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("unknown technology 'tannin'", System.StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("unknown good 'hidez'", System.StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("bare hands", System.StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
