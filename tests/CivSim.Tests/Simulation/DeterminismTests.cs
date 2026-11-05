using CivSim.Core.Commands;
using CivSim.Core.Hashing;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using Xunit;

namespace CivSim.Tests.Simulation;

/// <summary>Same seed + same commands = same state hash. Must pass in every PR (CLAUDE.md rule 4).</summary>
public class DeterminismTests
{
    internal static TurnRunner CreateRunner(ulong seed)
    {
        var runner = new TurnRunner(GameState.CreateNew(GameSettings.Test(seed)));
        runner.AddSystem(new RandomConsumer());
        runner.AddCommandSource(new RandomRenamer());
        return runner;
    }

    internal static ulong RunAndHash(ulong seed, int turns)
    {
        TurnRunner runner = CreateRunner(seed);
        for (int i = 0; i < turns; i++)
        {
            runner.RunTurn();
        }

        return GameStateHash.Compute(runner.State);
    }

    [Fact]
    public void TwoRunsWithTheSameSeedGiveTheSameHash()
    {
        Assert.Equal(RunAndHash(2026, 1000), RunAndHash(2026, 1000));
    }

    [Fact]
    public void DifferentSeedsGiveDifferentHashes()
    {
        Assert.NotEqual(RunAndHash(1, 200), RunAndHash(2, 200));
    }

    [Fact]
    public void HashChangesWithTurnAndWithState()
    {
        TurnRunner runner = CreateRunner(5);
        ulong h0 = GameStateHash.Compute(runner.State);
        runner.RunTurn();
        ulong h1 = GameStateHash.Compute(runner.State);
        Assert.NotEqual(h0, h1);

        runner.Commands.Enqueue(PlayerId.Ai, new CivId(3), new RenameCivilizationCommand("Different"));
        TurnRunner twin = CreateRunner(5);
        twin.RunTurn();
        runner.RunTurn();
        twin.RunTurn();
        Assert.NotEqual(GameStateHash.Compute(twin.State), GameStateHash.Compute(runner.State));
    }

    [Fact]
    public void HashIsStableAcrossVersionsAndPlatforms()
    {
        // Golden value checked on Windows, Linux and macOS in CI. If it changes on purpose, update it and say why in the PR.
        Assert.Equal(GoldenHash, RunAndHash(2026, 100));
    }

    private const ulong GoldenHash = 7827293887259829592UL;
}
