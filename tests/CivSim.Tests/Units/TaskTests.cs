using System;
using System.IO;
using System.Linq;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Protocol.Messages;
using CivSim.Server;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Units;

/// <summary>Area tasks of stage 6 PR 4a (GDD 4.4): data, the start command, the work group, the yield in the pulse, deposits that run out.</summary>
public class TaskTests
{
    // Real speeds (a flat cell takes a work group 20 ticks); the civilization's pulse is set by hand.
    private static readonly GameContent Live = ContentLoader.Load();

    private static (GameState State, SettlementState Home, CivilizationState Civ, WorldState World) NewGame(ulong seed, int people = 60)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Live, out WorldGenReport _);
        CivilizationState civ = state.Civilizations[0];
        Array.Fill(civ.SeenYear, (int)(state.Month / 12) + 1);
        SettlementState home = state.Settlements[0];
        home.ScoutOut = true; // no scouts take people in these tests
        home.Population.Clear();
        home.Population[Producers()] = people;
        home.Stock[Live.Goods.ToList().FindIndex(g => g.IsFood)] = Amount.FromHundredths(1_000_000); // the group eats from home
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        byte forest = (byte)Live.Biomes.ToList().FindIndex(b => b.Key == "deciduous_forest");
        var area = new HexCoord[HexGlobe.RangeSize(6)];
        int count = geo.Range(home.Cell, 6, area);
        for (int k = 0; k < count; k++)
        {
            int i = geo.Index(area[k]);
            world.Map.Biome.Set(i, forest);
            world.Map.Relief.Set(i, (byte)Relief.Flat);
            world.Map.Deposit.Set(i, 0);
            world.Map.Owner.Set(i, WorldMap.NoOwner);
            world.Map.Claim.Set(i, geo.Distance(home.Cell, area[k]) <= 2 ? home.Id.Value + 1 : 0);
        }

        for (int e = 0; e < world.Map.River.Length; e++)
        {
            world.Map.River.Set(e, 0);
        }

        return (state, home, civ, world);
    }

    private static int Producers() => Live.Strata.ToList().FindIndex(s => s.Gathers);

    private static TurnRunner Runner(GameState state)
    {
        var runner = new TurnRunner(state);
        runner.AddSystem(new UnitSystem(Live));
        runner.AddSystem(new TaskSystem(Live));
        return runner;
    }

    private static int Workers(GameState state) => state.Units.All.Count(u => u.Role == UnitRole.Workers && !u.Gone);

    private static Unit Group(GameState state) => state.Units.All.Single(u => u.Role == UnitRole.Workers);

    [Fact]
    public void TaskDataLoads()
    {
        TaskRules rules = Live.Tasks;
        Assert.True(rules.ZoneGathersMaterials); // until the AI gives tasks itself (PR 4b)
        TaskKindDefinition wood = rules.Find("woodcutting")!;
        Assert.Equal(TaskSource.Biome, wood.Source);
        Assert.Equal("wood", Live.Goods[wood.Good].Key);
        TaskKindDefinition flint = rules.Find("flint_mine")!;
        Assert.Equal(TaskSource.Deposit, flint.Source);
        Assert.Equal("flint", Live.Deposit(flint.Deposit).Key);
        Assert.Equal("flint_knapping", Live.Technologies[flint.RequiresTechnology].Key);
        Assert.Equal(UnitRole.Workers, Live.Units.First(UnitRole.Workers).Role);
    }

    [Fact]
    public void TheCommandChecksTheRules()
    {
        (GameState state, SettlementState home, CivilizationState civ, WorldState world) = NewGame(41);
        HexGlobe geo = world.Geometry;
        HexCoord near = geo.Wrap(home.Cell.Col + 2, home.Cell.Row);
        HexCoord far = geo.Wrap(home.Cell.Col + 6, home.Cell.Row);

        Assert.False(new StartTaskCommand(Live, home.Id, "dig_for_gold", near).Validate(state, civ.Id).Success);
        Assert.False(new StartTaskCommand(Live, home.Id, "woodcutting", far).Validate(state, civ.Id).Success); // 4 cells beyond the zone
        Assert.False(new StartTaskCommand(Live, home.Id, "woodcutting", near, 31).Validate(state, civ.Id).Success); // more than a group
        Assert.False(new StartTaskCommand(Live, home.Id, "woodcutting", near).Validate(state, new CivId(civ.Id.Value + 1)).Success);

        // A flint mine needs the deposit and flint knapping.
        int i = geo.Index(near);
        int flint = Live.Deposits.Single(d => d.Key == "flint").Id;
        world.Map.Deposit.Set(i, (ushort)flint);
        world.Map.DepositDepth.Set(i, 0);
        world.Map.DepositRichness.Set(i, 100);
        Assert.False(new StartTaskCommand(Live, home.Id, "flint_mine", near).Validate(state, civ.Id).Success);
        CivSim.Core.Research.Technologies.Learn(Live, state, civ, Live.Technology("flint_knapping").Id);
        CommandResult mine = new StartTaskCommand(Live, home.Id, "flint_mine", near).Validate(state, civ.Id);
        Assert.True(mine.Success, mine.Reason);

        // Woodcutting fits the forest; one task for every 25 people: 60 people run two.
        HexCoord second = geo.Wrap(home.Cell.Col - 2, home.Cell.Row);
        HexCoord third = geo.Wrap(home.Cell.Col, home.Cell.Row + 2);
        Assert.True(new StartTaskCommand(Live, home.Id, "woodcutting", second).Validate(state, civ.Id).Success);
        new StartTaskCommand(Live, home.Id, "woodcutting", second).Apply(state, civ.Id);
        Assert.False(new StartTaskCommand(Live, home.Id, "woodcutting", second).Validate(state, civ.Id).Success); // one group a cell
        new StartTaskCommand(Live, home.Id, "flint_mine", near).Apply(state, civ.Id);
        Assert.False(new StartTaskCommand(Live, home.Id, "woodcutting", third).Validate(state, civ.Id).Success);
        Assert.Equal(2, AreaTasks.RunningAt(state, home.Id.Value));
        Assert.Equal(40, home.TotalPopulation());
    }

    [Fact]
    public void TheGroupWalksThereAndBringsWoodHomeInThePulse()
    {
        (GameState state, SettlementState home, CivilizationState civ, WorldState world) = NewGame(42);
        HexGlobe geo = world.Geometry;
        HexCoord cell = geo.Wrap(home.Cell.Col + 3, home.Cell.Row);
        TaskKindDefinition wood = Live.Tasks.Find("woodcutting")!;
        CommandResult ok = new StartTaskCommand(Live, home.Id, "woodcutting", cell).Validate(state, civ.Id);
        Assert.True(ok.Success, ok.Reason);
        new StartTaskCommand(Live, home.Id, "woodcutting", cell).Apply(state, civ.Id);
        Unit u = Group(state);
        Assert.Equal(10, u.TotalPeople());
        Assert.Equal(50, home.TotalPopulation());

        // On its way (3 forest cells, 2 × 20 ticks each) nothing comes in, even in pulses.
        civ.StepMonths = 12;
        TurnRunner runner = Runner(state);
        Amount before = home.Stock[wood.Good];
        int ticks = 0;
        while (u.Cell != u.Target && ticks < 1000)
        {
            runner.RunTurn();
            ticks++;
        }

        Assert.Equal(3 * 40, ticks);
        Assert.Equal(UnitOrder.Work, u.Order);
        Amount onTheWay = home.Stock[wood.Good] - before;

        // A pulse of a year at work: 10 people × 0.5 × the forest's 6 wood = 30 wood; the tick it arrived in already counts.
        Amount year = AreaTasks.Yield(Live, world.Map, wood, u.Cell, 10, 12);
        Assert.Equal(Amount.FromHundredths(3000), year);
        Assert.Equal(year, onTheWay);
        Amount atWork = home.Stock[wood.Good];
        runner.RunTurn();
        Assert.Equal(atWork + year, home.Stock[wood.Good]);

        // Without a pulse (another civilization's tick) nothing comes in.
        civ.StepMonths = 0;
        Amount paused = home.Stock[wood.Good];
        runner.RunTurn();
        Assert.Equal(paused, home.Stock[wood.Good]);
        Assert.Equal(u.Target, u.Cell); // the group stays at work
    }

    [Fact]
    public void ADepositRunsOutAndTheGroupComesHome()
    {
        (GameState state, SettlementState home, CivilizationState civ, WorldState world) = NewGame(43);
        HexGlobe geo = world.Geometry;
        HexCoord cell = geo.Wrap(home.Cell.Col + 1, home.Cell.Row);
        int i = geo.Index(cell);
        int flint = Live.Deposits.Single(d => d.Key == "flint").Id;
        world.Map.Deposit.Set(i, (ushort)flint);
        world.Map.DepositDepth.Set(i, 0);
        world.Map.DepositRichness.Set(i, 15);
        CivSim.Core.Research.Technologies.Learn(Live, state, civ, Live.Technology("flint_knapping").Id);
        TaskKindDefinition mine = Live.Tasks.Find("flint_mine")!;
        new StartTaskCommand(Live, home.Id, "flint_mine", cell).Apply(state, civ.Id);
        Unit u = Group(state);
        civ.StepMonths = 12;
        TurnRunner runner = Runner(state);
        Amount before = home.Stock[mine.Good];
        var lines = new System.Collections.Generic.List<string>();
        for (int t = 0; t < 400 && Workers(state) > 0; t++)
        {
            runner.RunTurn();
            lines.AddRange(runner.LastChronicle);
        }

        // 10 people × 1 flint a year: the 15 units left last a year and a half, then the mine is gone and the people are home.
        Assert.Equal(Amount.FromHundredths(1500), home.Stock[mine.Good] - before);
        Assert.Equal(0, world.Map.Deposit[i]);
        Assert.Equal(0, Workers(state));
        Assert.Equal(60, home.TotalPopulation());
        Assert.Contains(lines, l => l.Contains("ran out", StringComparison.Ordinal));
    }

    [Fact]
    public void CallingTheGroupBackBringsItsPeopleHome()
    {
        (GameState state, SettlementState home, CivilizationState civ, WorldState world) = NewGame(44);
        HexCoord cell = world.Geometry.Wrap(home.Cell.Col + 2, home.Cell.Row);
        new StartTaskCommand(Live, home.Id, "woodcutting", cell).Apply(state, civ.Id);
        Unit u = Group(state);
        civ.StepMonths = 12; // a pulse of a year in every tick (the group eats with its home)
        TurnRunner runner = Runner(state);
        for (int t = 0; t < 100; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(u.Target, u.Cell);
        Assert.True(new ReturnUnitCommand(Live, u.Id).Validate(state, civ.Id).Success);
        new ReturnUnitCommand(Live, u.Id).Apply(state, civ.Id);
        for (int t = 0; t < 200 && Workers(state) > 0; t++)
        {
            runner.RunTurn();
        }

        Assert.Equal(0, Workers(state));
        Assert.Equal(60, home.TotalPopulation());
    }

    [Fact]
    public void AGroupWhoseHomeHasNoFoodComesBack()
    {
        (GameState state, SettlementState home, CivilizationState civ, WorldState world) = NewGame(45);
        HexCoord cell = world.Geometry.Wrap(home.Cell.Col + 1, home.Cell.Row);
        new StartTaskCommand(Live, home.Id, "woodcutting", cell).Apply(state, civ.Id);
        for (int g = 0; g < Live.Goods.Count; g++)
        {
            if (Live.Goods[g].IsFood)
            {
                home.Stock[g] = Amount.Zero;
            }
        }

        civ.StepMonths = 12;
        TurnRunner runner = Runner(state);
        var lines = new System.Collections.Generic.List<string>();
        for (int t = 0; t < 200 && Workers(state) > 0; t++)
        {
            runner.RunTurn();
            lines.AddRange(runner.LastChronicle);
        }

        Assert.Equal(0, Workers(state));
        Assert.Contains(lines, l => l.Contains("ran out of food", StringComparison.Ordinal));
    }

    [Fact]
    public void TheTaskIsSavedAndHashed()
    {
        (GameState state, SettlementState home, CivilizationState civ, WorldState world) = NewGame(46);
        ulong without = GameStateHash.Compute(state);
        HexCoord cell = world.Geometry.Wrap(home.Cell.Col + 2, home.Cell.Row);
        new StartTaskCommand(Live, home.Id, "woodcutting", cell).Apply(state, civ.Id);
        Unit u = Group(state);
        ulong with = GameStateHash.Compute(state);
        Assert.NotEqual(without, with);
        u.Task = Live.Tasks.Find("quarry")!.Id;
        Assert.NotEqual(with, GameStateHash.Compute(state)); // the task itself is in the hash

        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
        Assert.Equal(u.Task, loaded.Units.All.Single(x => x.Role == UnitRole.Workers).Task);
    }

    [Fact]
    public void TheWireCommandStartsTheTask()
    {
        (GameState state, SettlementState home, CivilizationState civ, WorldState world) = NewGame(47);
        HexCoord cell = world.Geometry.Wrap(home.Cell.Col + 2, home.Cell.Row);
        ICommand? command = CommandMapper.ToCore(new StartTaskDto(home.Id.Value, "woodcutting", cell.Col, cell.Row, 5), Live);
        StartTaskCommand start = Assert.IsType<StartTaskCommand>(command);
        Assert.True(start.Validate(state, civ.Id).Success);
        start.Apply(state, civ.Id);
        Assert.Equal(5, Group(state).TotalPeople());
    }

    [Fact]
    public void WithoutZoneGatheringWoodComesOnlyFromTasksButFoodStill()
    {
        GameContent off = Live.WithTasks(Live.Tasks with { ZoneGathersMaterials = false });
        int wood = Live.Tasks.Find("woodcutting")!.Good;
        (Amount OnWood, long OnFood) = Gather(Live, wood);
        (Amount OffWood, long OffFood) = Gather(off, wood);
        Assert.True(OnWood > Amount.Zero);
        Assert.Equal(Amount.Zero, OffWood);
        Assert.True(OffFood > 0);
        Assert.Equal(OnFood, OffFood);
    }

    // A year of gathering of the forest camp of NewGame with <paramref name="content"/>: its wood and its food.
    private static (Amount Wood, long Food) Gather(GameContent content, int wood)
    {
        (GameState state, SettlementState home, CivilizationState civ, WorldState _) = NewGame(48);
        civ.StepMonths = 12;
        Amount before = home.Stock[wood];
        var runner = new TurnRunner(state);
        runner.AddSystem(new CivSim.Core.Economy.GatheringSystem(content, new CivSim.Core.Economy.YieldYears(0)));
        runner.RunTurn();
        return (home.Stock[wood] - before, home.FoodGathered);
    }
}
