using System.Linq;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Protocol.Messages;
using Xunit;

namespace CivSim.Tests.Server;

/// <summary>Playtest P-09, P-10, P-12 (2026-11-16): what the player reads is in game time, with numbers, with safe defaults.</summary>
public class PlayerMessagesTests
{
    private static readonly GameContent Content = TestContent.Load();

    [Theory]
    [InlineData(10, 1, "1 s")]
    [InlineData(10, 360, "36 s")]
    [InlineData(10, 600, "1 min")]
    [InlineData(10, 2000, "3 min 20 s")]
    [InlineData(10, 0, "1 s")]
    public void DurationsAreToldInGameTime(int ticksPerSecond, long ticks, string text) =>
        Assert.Equal(text, new TimeRules { TicksPerSecond = ticksPerSecond }.PlayTime(ticks)); // P-09

    [Fact]
    public void ARefusedSplitTellsThePeopleOfThatSettlement()
    {
        // P-10: "A settlement needs 60 people to split." did not say how many it had.
        GameState state = GameState.CreateNew(GameSettings.Test(5), Content, out _);
        SettlementState camp = state.Settlements.First(s => s.Owner.Value == 0);
        int people = camp.TotalPopulation();
        Assert.True(people < Content.Population.MinSplitPeople, "the test needs a camp too small to split");
        var split = new SplitSettlementCommand(Content, camp.Id, camp.Cell, 30);
        CommandResult result = split.Validate(state, camp.Owner);
        Assert.False(result.Success);
        Assert.Equal($"{camp.Name} has {people} people; a settlement needs {Content.Population.MinSplitPeople} to split.", result.Reason);
    }

    [Fact]
    public void AWarWithoutAGoalIsForLoot() =>
        Assert.Equal((int)WarGoal.Loot, new DeclareWarDto(1).Goal); // P-12: revenge was refused without a wrong to avenge
}
