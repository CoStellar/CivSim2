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
        Assert.Equal(1, Content.Ai.PickAmongTop);
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
                Assert.All(ids, id => Assert.Contains(id, new[] { FactorId.Food, FactorId.Comfort, FactorId.Distance, FactorId.OwnCampsite, FactorId.BetterThanHere, FactorId.Fed }));
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
        Assert.Equal(FactorId.ResearchCost, research.First.Id);
        Assert.Equal(Core.Research.ScholarAdvisor.TechnologyWorth - research.First.Input, research.Score);
        Assert.Contains("research cost", Decision.Describe(research.First), StringComparison.Ordinal);
    }

    [Fact]
    public void DrawingAmongTheThreeBestIsDeterministic()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"civsim-ai-top3-{Environment.ProcessId}");
        Directory.CreateDirectory(folder);
        foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
        {
            File.Copy(file, Path.Combine(folder, Path.GetFileName(file)), overwrite: true);
        }

        File.WriteAllText(Path.Combine(folder, "ai.json"), "{ \"version\": 1, \"pickAmongTop\": 3 }");
        GameContent top3 = ContentLoader.Load(folder);
        Assert.Equal(3, top3.Ai.PickAmongTop);

        static ulong Run(GameContent content)
        {
            (TurnRunner runner, _) = NewGame(77, content);
            for (int t = 0; t < 60; t++)
            {
                runner.RunTurn();
            }

            return GameStateHash.Compute(runner.State);
        }

        ulong a = Run(top3);
        Assert.Equal(a, Run(top3));
        Assert.NotEqual(Run(Content), a);
    }
}
