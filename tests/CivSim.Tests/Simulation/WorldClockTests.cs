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

/// <summary>The world clock and the rhythms of civilizations (ADR 0018).</summary>
public class WorldClockTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static GameState NewState(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out WorldGenReport _);

    // Puts a civilization into an era before the first step, as if it had started there.
    private static void StartIn(GameState state, CivilizationState civ, int era)
    {
        civ.Era = era;
        civ.NextStepMonth = 0;
        civ.StepStartMonth = -1;
        civ.Steps = 0;
        WorldClock.Begin(Content, state);
    }

    [Fact]
    public void EachCivilizationStepsInItsOwnRhythmPinnedToTheCalendar()
    {
        GameState state = NewState(1);
        CivilizationState stone = state.Civilizations[0];
        CivilizationState neolithic = state.Civilizations[1];
        StartIn(state, neolithic, 1);
        var runner = new TurnRunner(state);
        runner.AddSystem(new CalendarSystem(Content));

        var seen = new List<(long Month, int World, int Stone, int Neolithic)>();
        for (int t = 0; t < 4; t++)
        {
            seen.Add((state.Month, state.StepMonths, stone.StepMonths, neolithic.StepMonths));
            runner.RunTurn();
        }

        // The world steps every 25 years; the Stone Age tribe only every 50 years (years 0 and 50), each time for 50 years.
        Assert.Equal([(0L, 300, 600, 300), (300L, 300, 0, 300), (600L, 300, 600, 300), (900L, 300, 0, 300)], seen);
        Assert.Equal(3, stone.Steps);       // years 0, 50 and the one now beginning, 100
        Assert.Equal(5, neolithic.Steps);
    }

    [Fact]
    public void ANewEraStartsWithAShorterStepAndNoTimeIsLost()
    {
        GameState state = NewState(2);
        CivilizationState civ = state.Civilizations[0];
        foreach (CivilizationState c in state.Civilizations)
        {
            StartIn(state, c, 1);
        }

        var runner = new TurnRunner(state);
        runner.AddSystem(new CalendarSystem(Content));
        long counted = civ.StepMonths;
        runner.RunTurn();                       // year 25: the second Neolithic step begins
        counted += civ.StepMonths;
        civ.Era = 2;                            // the Bronze Age (20 years) arrives during that step
        var steps = new List<int>();
        while (state.Month < 1440)
        {
            runner.RunTurn();
            if (civ.StepMonths > 0)
            {
                steps.Add(civ.StepMonths);
                counted += civ.StepMonths;
            }
        }

        // From year 50 the steps are pinned to multiples of 20 years: 10 years to year 60, then 20-year steps.
        Assert.Equal([120, 240, 240, 240], steps.Take(4).ToList());
        Assert.Equal(civ.NextStepMonth, counted); // every month from the start was computed exactly once
    }

    [Fact]
    public void AWaitingCivilizationDoesNotChangeBetweenItsSteps()
    {
        GameState state = NewState(3);
        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        CivilizationState stone = state.Civilizations[0];
        StartIn(state, state.Civilizations[1], 1);
        runner.RunTurn(); // year 0: both step; the Stone Age tribe computes 50 years at once

        Assert.Equal(0, stone.StepMonths); // year 25: it waits
        SettlementState[] camps = state.Settlements.Where(s => s.Owner == stone.Id && s.IsActive).ToArray();
        int[] people = camps.Select(s => s.TotalPopulation()).ToArray();
        long[] stock = camps.Select(s => s.TotalStock().Hundredths).ToArray();
        long science = stone.ResearchProgress;
        runner.RunTurn();

        Assert.Equal(people, camps.Select(s => s.TotalPopulation()).ToArray());
        Assert.Equal(stock, camps.Select(s => s.TotalStock().Hundredths).ToArray());
        Assert.Equal(science, stone.ResearchProgress);
        Assert.All(camps, s => Assert.Equal(s.Cell, state.FindSettlement(s.Id)!.Cell));
        Assert.True(stone.StepMonths > 0); // year 50: its turn again
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
