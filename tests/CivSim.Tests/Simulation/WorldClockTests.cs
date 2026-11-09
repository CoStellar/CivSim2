using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Simulation;

/// <summary>The clock of the live game: ticks, pulses of civilizations and of the world (ADR 0025; before it ADR 0018).</summary>
public class WorldClockTests
{
    private static readonly GameContent Content = TestContent.Load();

    // The live clock as in data/time.json (pulses every 30 ticks, each civilization in a tick of its own).
    private static readonly GameContent Live = ContentLoader.Load();

    private static GameState NewState(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out WorldGenReport _);

    // Puts a civilization into an era before the first step, as if it had started there.
    private static void StartIn(GameState state, CivilizationState civ, int era)
    {
        civ.Era = era;
        civ.NextStepMonth = 0;
        civ.StepStartMonth = -1;
        civ.Steps = 0;
        civ.LastPulseTick = -1;
        WorldClock.Begin(Content, state);
    }

    [Fact]
    public void CivilizationsPulseInTicksOfTheirOwnAndTheWorldMovesTheYearCounter()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(1), Live, out WorldGenReport _);
        CivilizationState stone = state.Civilizations[0];
        CivilizationState neolithic = state.Civilizations[1];
        neolithic.Era = 1;
        neolithic.LastPulseTick = -1; // as if it had started in the Neolithic: its first pulse is computed again
        var runner = new TurnRunner(state);
        runner.AddSystem(new CalendarSystem(Live));
        WorldClock.Begin(Live, state);
        int p = Live.Time.TicksPerPulse;
        int stoneAt = WorldClock.PulseOffset(Live.Time, stone);
        int neolithicAt = WorldClock.PulseOffset(Live.Time, neolithic);
        Assert.NotEqual(stoneAt, neolithicAt); // neighbouring ids pulse in different ticks
        Assert.NotEqual(0, stoneAt);            // and never in the world's tick

        var stonePulses = new List<long>();
        var worldPulses = new List<(long Tick, int Months)>();
        for (int t = 0; t < 2 * p; t++)
        {
            if (stone.StepMonths > 0)
            {
                stonePulses.Add(state.Turn);
                Assert.Equal(600, stone.StepMonths); // a Stone Age pulse covers 50 years
            }

            if (neolithic.StepMonths > 0)
            {
                Assert.Equal(300, neolithic.StepMonths); // a Neolithic one 25 years
            }

            if (state.WorldMonths > 0)
            {
                worldPulses.Add((state.Turn, state.WorldMonths));
            }

            runner.RunTurn();
        }

        // The first pulse comes at once, then one in every 30 ticks in its own tick; the world pulses in ticks 0 and 30,
        // each time moving the year counter by the step of the most advanced (the Neolithic): 25 years.
        Assert.Equal([0L, stoneAt, stoneAt + p], stonePulses);
        Assert.Equal([(0L, 300), (p, 300)], worldPulses);
        Assert.Equal(600, state.Month);
    }

    [Fact]
    public void ANewEraChangesThePulseAndTheYearCounter()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(2), Live, out WorldGenReport _);
        CivilizationState civ = state.Civilizations[0];
        var runner = new TurnRunner(state);
        runner.AddSystem(new CalendarSystem(Live));
        WorldClock.Begin(Live, state);
        civ.Era = 2; // the Bronze Age (20 years) arrives
        var pulses = new List<int>();
        int world = 0;
        for (int t = 0; t < 3 * Live.Time.TicksPerPulse; t++)
        {
            runner.RunTurn();
            if (civ.StepMonths > 0)
            {
                pulses.Add(civ.StepMonths);
            }

            world = state.WorldMonths > 0 ? state.WorldMonths : world;
        }

        Assert.All(pulses.Skip(1), m => Assert.Equal(240, m));
        Assert.Equal(240, world); // the year counter now moves by the Bronze Age step
    }

    [Fact]
    public void AWaitingCivilizationDoesNotChangeBetweenItsPulses()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(3), Live, out WorldGenReport _);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Live);
        WorldClock.Begin(Live, state);
        CivilizationState stone = state.Civilizations[0];
        runner.RunTurn(); // tick 0: everybody's first pulse
        while (stone.StepMonths > 0)
        {
            runner.RunTurn();
        }

        SettlementState[] camps = state.Settlements.Where(s => s.Owner == stone.Id && s.IsActive).ToArray();
        int[] people = camps.Select(s => s.TotalPopulation()).ToArray();
        long[] stock = camps.Select(s => s.TotalStock().Hundredths).ToArray();
        long science = stone.ResearchProgress;
        while (stone.StepMonths == 0 && stone.Alarms == Alarm.None)
        {
            Assert.Equal(people, camps.Select(s => s.TotalPopulation()).ToArray());
            Assert.Equal(stock, camps.Select(s => s.TotalStock().Hundredths).ToArray());
            Assert.Equal(science, stone.ResearchProgress);
            runner.RunTurn();
        }

        Assert.True(stone.StepMonths > 0); // its tick again
    }

    [Fact]
    public void AWaitingCivilizationOnlyReactsToAnAlarm()
    {
        GameState state = NewState(4);
        CivilizationState attacker = state.Civilizations[0];
        CivilizationState victim = state.Civilizations[1];
        Contacts.Meet(Content, state, attacker, victim.Id, ContactWay.Sighted);
        Contacts.Meet(Content, state, victim, attacker.Id, ContactWay.Sighted);
        foreach (CivilizationState c in state.Civilizations)
        {
            c.StepMonths = 0; // everybody waits for their own step
        }

        var council = new AiCouncil(Content);
        council.IssueCommands(state, new CommandQueue());
        Assert.DoesNotContain(council.Log.Entries, d => d.Advisor is AdvisorKind.Strategist or AdvisorKind.Governor or AdvisorKind.Treasurer);

        Treaties.DeclareWar(Content, state, attacker.Id, victim.Id);
        Assert.Equal(Alarm.WarDeclared, victim.Alarms);
        council.IssueCommands(state, new CommandQueue());
        Assert.Equal(Alarm.None, victim.Alarms); // answered in this decisions phase, outside its rhythm
    }

    [Fact]
    public void SubYearStepsDoNotRepeatThePlannedDiplomaticReview()
    {
        // Information Era: 6-month steps. The 50-year review falls in exactly one step of the 100 steps of 50 years.
        GameState state = NewState(5);
        foreach (CivilizationState c in state.Civilizations)
        {
            StartIn(state, c, 8);
        }

        long every = Content.Treaties.ReviewEveryYears * 12L;
        CivilizationState civ = state.Civilizations[2];
        int reviews = 0;
        for (long month = 0; month < every; month += civ.StepMonths)
        {
            reviews += (month + (civ.Id.Value * 7L * 12)) % every < civ.StepMonths ? 1 : 0;
        }

        Assert.Equal(6, WorldClock.RhythmMonths(Content, state, civ));
        Assert.Equal(1, reviews);
    }

    [Fact]
    public void AGameWithMixedRhythmsContinuesExactlyAfterLoading()
    {
        GameState state = NewState(6);
        StartIn(state, state.Civilizations[1], 1);
        var original = new TurnRunner(state);
        CoreSystems.Register(original, Content);
        for (int t = 0; t < 3; t++)
        {
            original.RunTurn(); // stops in year 75, where the Stone Age tribes wait
        }

        using var ms = new System.IO.MemoryStream();
        CivSim.Server.Saves.SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        var resumed = new TurnRunner(CivSim.Server.Saves.SaveGameSerializer.Load(ms));
        CoreSystems.Register(resumed, Content);
        Assert.Equal(CivSim.Core.Hashing.GameStateHash.Compute(state), CivSim.Core.Hashing.GameStateHash.Compute(resumed.State));
        for (int t = 0; t < 4; t++)
        {
            original.RunTurn();
            resumed.RunTurn();
        }

        Assert.Equal(CivSim.Core.Hashing.GameStateHash.Compute(state), CivSim.Core.Hashing.GameStateHash.Compute(resumed.State));
    }
}
