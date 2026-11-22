using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Governance;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>The AI and the state: overload, unrest and the choice of government (GDD 7.2, 7.3; stage 4f PR 5).</summary>
public class StateAiTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static int Gov(string key) => Content.Governments.Single(g => g.Key == key).Id;

    // A new game in which only the first civilization reviews its strategy now; `change` sets it up.
    private static (GameState State, AiCouncil Council, List<CommandEnvelope> Commands) Review(ulong seed, System.Action<CivilizationState> change)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        foreach (CivilizationState c in state.Civilizations)
        {
            c.StepMonths = 0;
        }

        CivilizationState civ = state.Civilizations[0];
        civ.StepMonths = 12;
        civ.StrategyReviewMonth = 0;
        change(civ);
        var council = new AiCouncil(Content);
        var queue = new CommandQueue();
        council.IssueCommands(state, queue);
        var commands = new List<CommandEnvelope>();
        queue.DrainSorted(state, state.Turn, commands);
        return (state, council, commands);
    }

    private static int[] Weights(List<CommandEnvelope> commands) => commands.Select(c => c.Command).OfType<SetStrategyCommand>().Single().Weights;

    [Fact]
    public void AnOverloadedStateExpandsLessAndUnrestMakesSecurityMatter()
    {
        int[] calm = Weights(Review(41, c => c.Government = Gov("chiefdom")).Commands);
        int[] troubled = Weights(Review(41, c =>
        {
            c.Government = Gov("chiefdom");
            c.AdminUsed = 4 * Stability.Capacity(Content, c); // 25% of the work done: kept at the floor
            c.Stability = Content.Stability.UnrestBelow - 1;
        }).Commands);

        Assert.Equal(calm[(int)Goal.Expansion] * Content.Ai.OverloadExpansionMinPercent / 100, troubled[(int)Goal.Expansion]);
        Assert.Equal(System.Math.Min(SetStrategyCommand.MaxWeight, calm[(int)Goal.Security] * Content.Ai.UnrestSecurityPercent / 100), troubled[(int)Goal.Security]);
        Assert.Equal(calm[(int)Goal.Science], troubled[(int)Goal.Science]);
    }

    [Fact]
    public void ThePeopleBringInAGovernmentThatSuitsThemClearlyBetter()
    {
        // A cautious, open people under a chief: the council suits it far better.
        void Council(CivilizationState c)
        {
            c.Government = Gov("chiefdom");
            System.Array.Fill(c.Traits, 0);
            c.Traits[(int)Trait.Caution] = 900;
            c.Traits[(int)Trait.Openness] = 900;
            c.LeaderShift = new int[Character.TraitCount];
        }

        (_, AiCouncil council, List<CommandEnvelope> commands) = Review(42, Council);
        Decision change = council.Log.Entries.Single(d => d.Action == AiAction.ChangeGovernment);
        Assert.Equal(Gov("elder_council"), change.Target);
        Assert.Contains(commands, c => c.Command is ChangeGovernmentCommand);

        // A little better is not enough (hysteresis): 300 against 280 is under the 30% margin.
        (_, council, _) = Review(42, c =>
        {
            Council(c);
            c.Traits[(int)Trait.Caution] = 150;
            c.Traits[(int)Trait.Openness] = 150;
            c.Traits[(int)Trait.Aggression] = 140;
            c.Traits[(int)Trait.Expansiveness] = 140;
        });
        Assert.DoesNotContain(council.Log.Entries, d => d.Action == AiAction.ChangeGovernment);

        // ...but in unrest the council's stability bonus tips the balance.
        (_, council, _) = Review(42, c =>
        {
            Council(c);
            c.Traits[(int)Trait.Caution] = 150;
            c.Traits[(int)Trait.Openness] = 150;
            c.Traits[(int)Trait.Aggression] = 140;
            c.Traits[(int)Trait.Expansiveness] = 140;
            c.Stability = Content.Stability.UnrestBelow - 1;
        });
        Assert.Contains(council.Log.Entries, d => d.Action == AiAction.ChangeGovernment && d.Target == Gov("elder_council"));

        // Never in anarchy, and not for years after a change (reforms need time).
        (_, council, _) = Review(42, c =>
        {
            Council(c);
            c.AnarchyUntilMonth = 24;
        });
        Assert.DoesNotContain(council.Log.Entries, d => d.Action == AiAction.ChangeGovernment);
        long keep = Content.Ai.GovernmentKeepYears * 12L;
        (GameState state, council, _) = Review(42, c =>
        {
            Council(c);
            c.AnarchyUntilMonth = 1; // the anarchy is over
        });
        Assert.Equal(state.Month < 1 - (Content.Stability.AnarchyYears * 12L) + keep, !council.Log.Entries.Any(d => d.Action == AiAction.ChangeGovernment));
    }
}
