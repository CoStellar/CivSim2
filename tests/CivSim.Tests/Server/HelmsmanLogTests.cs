using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Protocol.Messages;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Server;

/// <summary>Playtest P-05 (2026-11-16): the panel of what the AI did for the player speaks in plain words.</summary>
public class HelmsmanLogTests
{
    private static readonly GameContent Content = TestContent.Load();

    [Fact]
    public void ThePanelTellsWhatAndWhyInWords()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(5), Content, out _);
        CivilizationState us = state.Civilizations[0];
        us.Controller = new PlayerId(1);
        SettlementState[] camps = [.. state.Settlements.Where(s => s.Owner == us.Id).Take(1), .. state.Settlements.Where(s => s.Owner != us.Id).Take(1)];
        int a = camps[0].Id.Value;
        int b = camps[1].Id.Value; // a second settlement name, only to tell the merged places apart
        Factor goal = new(FactorId.GoalWeight, 1330, 0);
        Factor character = new(FactorId.Character, 820, 0);
        Factor crowded = new(FactorId.Crowding, Factor.NoteOnly, 57);
        var log = new DecisionLog();
        log.Add(new Decision(5, us.Id, AdvisorKind.Governor, AiAction.SplitCamp, a, -1, 1, goal, character, default));
        log.Add(new Decision(5, us.Id, AdvisorKind.Governor, AiAction.SplitCamp, b, -1, 1, goal, character, default));
        log.Add(new Decision(5, us.Id, AdvisorKind.Governor, AiAction.SetRecipe, a, 0, 1, goal, default, default));
        log.Add(new Decision(5, us.Id, AdvisorKind.Strategist, AiAction.SetStrategy, a, 7, 1, crowded, default, default));
        log.Add(new Decision(6, us.Id, AdvisorKind.Strategist, AiAction.SetStrategy, a, 7, 1, crowded, default, default));
        log.Add(new Decision(6, us.Id, AdvisorKind.Scholar, AiAction.SetResearch, -1, 0, 1, new Factor(FactorId.ServesGoal, Factor.NoteOnly, (long)Goal.Security), default, default));
        var panel = new HelmsmanLog(Content);
        panel.Add(state, log);

        string[] lines = [.. panel.For(us.Id).Select(d => d.Text)];
        Assert.Equal(3, lines.Length); // the recipe is on no lever; the same strategy again is no news
        Assert.Equal($"Split camps {camps[0].Name}, {camps[1].Name}: the people have grown.", lines[0]);
        Assert.Equal("New direction for our people: the camp is 57% full.", lines[1]);
        Assert.Equal($"Research {Content.Technologies[0].Name}: to keep us safe.", lines[2]);
        Assert.DoesNotContain(lines, l => l.Contains("goal weight", System.StringComparison.Ordinal) || l.Contains("character", System.StringComparison.Ordinal));
    }
}
