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

    // ADR 0018: the world clock and the civilizations' own rhythms joined the state (in a world where everyone is in the
    // same era the simulation runs as before).
    // Names from peoples' languages (GDD 5.6): names, language seeds and name counters joined the hash; the simulation
    // itself is unchanged (statistics of 300 turns identical before and after).
    // ADR 0022 (episodes): the months the world computes and the running episodes joined the hash; alarms bring a step a
    // month later, and wars are fought in campaigns.
    // Stage 4f (ADR 0023): governments joined the state and change the AI (goal weights, caution, pace of reviews) and
    // the readiness of declared wars.
    // Stage 4f PR 2: leaders joined the state; their traits shift the character the AI decides with.
    // ADR 0025 (stage 6 PR 1): one turn is now a tick; civilizations pulse every 30 ticks, each in a tick of its own, and
    // war timers and episodes count in ticks (100 turns = 100 ticks, about 4 pulses of every civilization).
    // Stage 6 PR 2 (decision of 2026-11-09): truces, proposals, memories, crises, anarchy, revolts, claims and the
    // diplomat's review count in months of action (one year = 6 s at x1); long durations shortened; the months of action
    // joined the hash.
    private const ulong GoldenHash = 4987176402803861661UL;
}
