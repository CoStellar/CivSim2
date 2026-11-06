using System;
using System.IO;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>The advisor council and its "Why?" log (GDD 8.1, 8.7; stage 4a).</summary>
public class AdvisorTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static (TurnRunner Runner, AiCouncil Council) NewGame(ulong seed, GameContent content)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), content, out _);
        var runner = new TurnRunner(state);
        AiCouncil council = CoreSystems.Register(runner, content);
        return (runner, council);
    }

    [Fact]
    public void AiRulesLoadFromData()
    {
        Assert.Equal(3, Content.Ai.PickAmongTop);
        Assert.Equal(15, Content.Ai.ReviewEveryTurns);
    }

    [Fact]
    public void EveryAiCommandHasARecordedReason()
    {
        (TurnRunner runner, AiCouncil council) = NewGame(31, Content);
        var seen = new System.Collections.Generic.HashSet<AiAction>();
        for (int t = 0; t < 120; t++)
        {
            // Decisions of a turn, issued again on a copy of the queue: the log must match the commands one for one.
            var queue = new CommandQueue();
            council.IssueCommands(runner.State, queue);
            Assert.Equal(queue.Count, council.Log.Entries.Count);
            foreach (Decision d in council.Log.Entries)
            {
                Assert.Equal(runner.State.Turn + 1, d.Turn);
                Assert.NotNull(runner.State.FindCivilization(d.Civ));
                Assert.True(d.Score >= 0 || d.Action == AiAction.SetRecipe, $"{d.Action} scored {d.Score}");
                seen.Add(d.Action);
            }

            runner.RunTurn();
        }

        Assert.Contains(AiAction.SetResearch, seen);
        Assert.Contains(AiAction.MoveCamp, seen);
    }

    [Fact]
    public void CampMovesNameFoodComfortOrDistanceAsReasons()
    {
        (TurnRunner runner, AiCouncil council) = NewGame(31, Content);
        for (int t = 0; t < 120; t++)
        {
            runner.RunTurn();
            foreach (Decision d in council.Log.Entries.Where(d => d.Action == AiAction.MoveCamp))
            {
                Assert.Equal(AdvisorKind.Governor, d.Advisor);
                FactorId[] ids = d.Factors().Select(f => f.Id).ToArray();
                Assert.NotEmpty(ids);
                Assert.All(ids, id => Assert.Contains(id, new[]
                {
                    FactorId.Food, FactorId.Comfort, FactorId.Distance, FactorId.OwnCampsite, FactorId.BetterThanHere, FactorId.Fed,
                    FactorId.Direction, FactorId.GoalWeight, FactorId.Character,
                }));
                return;
            }
        }

        Assert.Fail("no camp moved in 120 turns");
    }

    [Fact]
    public void ResearchChoicesRecordTheirCost()
    {
        (TurnRunner runner, AiCouncil council) = NewGame(5, Content);
        runner.RunTurn();
        Decision research = Assert.Single(council.Log.Entries, d => d.Action == AiAction.SetResearch && d.Civ == runner.State.Civilizations[0].Id);
        Assert.Equal(AdvisorKind.Scholar, research.Advisor);
        Factor cost = Assert.Single(research.Factors(), f => f.Id == FactorId.ResearchCost);
        Assert.True(cost.Input > 0);
        Assert.Contains("research cost", Decision.Describe(cost), StringComparison.Ordinal);
    }

    // Content with ai.json replaced, everything else from data/.
    internal static GameContent WithAi(string name, string json)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"civsim-ai-{name}-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
        }

        File.WriteAllText(Path.Combine(folder, "ai.json"), json);
        return ContentLoader.Load(folder);
    }

    // The real ai.json with the draw among the best switched off.
    private static string PickBestOnly() =>
        File.ReadAllText(Path.Combine(ContentLoader.DefaultDirectory, "ai.json")).Replace("\"pickAmongTop\": 3", "\"pickAmongTop\": 1", StringComparison.Ordinal);

    private static ulong Run(GameContent content, int turns, Action<GameState>? setup = null)
    {
        (TurnRunner runner, _) = NewGame(77, content);
        setup?.Invoke(runner.State);
        for (int t = 0; t < turns; t++)
        {
            runner.RunTurn();
        }

        return GameStateHash.Compute(runner.State);
    }

    [Fact]
    public void DrawingAmongTheThreeBestIsDeterministic()
    {
        GameContent best = WithAi("best", PickBestOnly());
        Assert.Equal(1, best.Ai.PickAmongTop);
        ulong a = Run(Content, 60);
        Assert.Equal(a, Run(Content, 60));
        Assert.NotEqual(Run(best, 60), a);
    }

    [Fact]
    public void FullyCautiousCivilizationsAlwaysTakeTheBest()
    {
        // Caution 1000: the draw among the three best always ends with the best, exactly as with room for one.
        GameContent best = WithAi("best1", PickBestOnly());
        static void Cautious(GameState state)
        {
            foreach (CivilizationState civ in state.Civilizations)
            {
                civ.Traits[(int)Trait.Caution] = 1000;
            }
        }

        Assert.Equal(Run(best, 60, Cautious), Run(Content, 60, Cautious));
    }
}
