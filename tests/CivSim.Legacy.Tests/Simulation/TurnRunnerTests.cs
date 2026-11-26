using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using Xunit;

namespace CivSim.Tests.Simulation;

public class TurnRunnerTests
{
    [Fact]
    public void NewGameHasEarthAndCivilizations()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(1));
        Assert.Equal(0, state.Turn);
        Assert.Single(state.Worlds);
        Assert.Equal(WorldId.Earth, state.Worlds[0].Id);
        Assert.Equal(4, state.Civilizations.Count);
    }

    [Fact]
    public void CommonChronicleLinesAreSummedUpInOneLine()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(1));
        string a = state.Civilizations[0].Name;
        string b = state.Civilizations[1].Name;
        const string Droughts = "Drought in {0} settlements";
        var runner = new TurnRunner(state);
        runner.AddSystem(new GroupedChronicler(
            ("A drought struck Kora.", Droughts, 0),
            ("A new leader in Arvi.", "New leaders in {0} peoples", 1),
            ("A drought struck Bela.", Droughts, 1),
            ("A drought struck Rao.", Droughts, 0),
            ("A drought struck the wilds.", Droughts, -1)));
        runner.RunTurn();
        Assert.Equal(
            ["Before.", $"Drought in 4 settlements: {a} (2), {b}, no one.", "A new leader in Arvi.", "After."],
            runner.LastChronicle);

        // Next turn the groups start afresh.
        runner.RunTurn();
        Assert.Equal(4, runner.LastChronicle.Count);
    }

    [Fact]
    public void OddMapHeightIsRejected()
    {
        Assert.Throws<System.ArgumentException>(() => GameState.CreateNew(new GameSettings(1, 64, 41, 2)));
    }

    [Fact]
    public void PhasesRunInGddOrderEveryTurn()
    {
        var runner = new TurnRunner(GameState.CreateNew(GameSettings.Test(1)));
        var log = new List<TurnPhase>();
        for (int p = TurnPhases.Count - 1; p >= 0; p--)
        {
            runner.AddSystem(new PhaseRecorder((TurnPhase)p, log)); // registered backwards on purpose
        }

        runner.RunTurn();
        runner.RunTurn();

        var expected = new List<TurnPhase>();
        for (int t = 0; t < 2; t++)
        {
            for (int p = 0; p < TurnPhases.Count; p++)
            {
                expected.Add((TurnPhase)p);
            }
        }

        Assert.Equal(expected, log);
        Assert.Equal(2, runner.State.Turn);
    }

    [Fact]
    public void ReportMeasuresEveryPhase()
    {
        var runner = new TurnRunner(GameState.CreateNew(GameSettings.Test(1)));
        TurnReport report = runner.RunTurn();
        long sum = 0;
        for (int p = 0; p < TurnPhases.Count; p++)
        {
            Assert.True(report.PhaseTicks((TurnPhase)p) >= 0);
            sum += report.PhaseTicks((TurnPhase)p);
        }

        Assert.Equal(1, report.Turn);
        Assert.True(report.TotalTicks >= sum);
    }

    [Fact]
    public void ValidCommandsApplyAndInvalidOnesAreReported()
    {
        var runner = new TurnRunner(GameState.CreateNew(GameSettings.Test(1)));
        runner.Commands.Enqueue(PlayerId.Ai, new CivId(1), new RenameCivilizationCommand("Rome"));
        runner.Commands.Enqueue(PlayerId.Ai, new CivId(2), new RenameCivilizationCommand(""));
        runner.Commands.Enqueue(PlayerId.Ai, new CivId(42), new RenameCivilizationCommand("Ghost"));

        TurnReport report = runner.RunTurn();

        Assert.Equal("Rome", runner.State.FindCivilization(new CivId(1))!.Name);
        Assert.Equal(1, report.AppliedCommands);
        Assert.Equal(2, report.Rejected.Count);
        Assert.Equal(0, runner.Commands.Count);
    }

    [Fact]
    public void SnapshotIsPublishedAfterEachTurnAndIsImmutable()
    {
        var runner = new TurnRunner(GameState.CreateNew(GameSettings.Test(1)));
        Assert.Null(runner.Snapshots.Latest);

        runner.Commands.Enqueue(PlayerId.Ai, new CivId(0), new RenameCivilizationCommand("Carthage"));
        runner.RunTurn();
        GameSnapshot first = runner.Snapshots.Latest!;
        Assert.Equal(1, first.Turn);
        Assert.Equal("Carthage", first.Civilizations[0].Name);

        runner.Commands.Enqueue(PlayerId.Ai, new CivId(0), new RenameCivilizationCommand("Babylon"));
        runner.RunTurn();

        Assert.Equal("Carthage", first.Civilizations[0].Name); // old snapshot unchanged
        Assert.Equal("Babylon", runner.Snapshots.Latest!.Civilizations[0].Name);
        Assert.Equal(2, runner.Snapshots.Version);
    }
}
