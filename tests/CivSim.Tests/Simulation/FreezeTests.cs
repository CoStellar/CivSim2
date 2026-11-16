using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Events;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Simulation;

/// <summary>
/// Systems frozen until the bronze age (ADR 0026 rule 5, stage 6 PR T3a): the flag is in the data, the code stays, the unit tests of
/// those systems switch them on (<see cref="GameContent.Thawed"/>).
/// </summary>
public class FreezeTests
{
    private static readonly GameContent Live = ContentLoader.Load();

    [Fact]
    public void TheDataFreezesTheDynamicClimateWildfireAndFlood()
    {
        Assert.False(Live.ClimateCycles.Enabled);
        Assert.False(Live.Events.Single(e => e.Key == "wildfire").Enabled);
        Assert.False(Live.Events.Single(e => e.Key == "flood").Enabled);
        Assert.True(Live.Events.Single(e => e.Key == "drought").Enabled); // drought and epidemic stay (cards later)
        Assert.True(Live.Events.Single(e => e.Key == "epidemic").Enabled);

        GameContent thawed = Live.Thawed();
        Assert.True(thawed.ClimateCycles.Enabled);
        Assert.All(thawed.Events, e => Assert.True(e.Enabled));
        Assert.False(Live.ClimateCycles.Enabled); // the copy, not the loaded content
    }

    [Fact]
    public void AFrozenClimateKeepsItsStartAndAFrozenEventNeverStrikes()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(31), Live, out WorldGenReport _);
        WorldState world = state.Worlds[0];
        long year = world.Climate.Year;
        int sea = world.Climate.SeaLevel;
        var runner = new TurnRunner(state);
        runner.AddSystem(new ClimateSystem(Live));
        runner.AddSystem(new CalendarSystem(Live));
        for (int t = 0; t < 600; t++)
        {
            runner.RunTurn(); // a minute of the game: a thousand years of the Stone Age
        }

        Assert.True(state.Month / 12 > Live.ClimateCycles.UpdateYears);
        Assert.Equal(year, world.Climate.Year);
        Assert.Equal(sea, world.Climate.SeaLevel);

        var events = new EventSystem(Live);
        SettlementState camp = state.Settlements.First(s => s.IsActive);
        Assert.Equal(0, events.YearChanceMillionths(state, world, camp, Live.Events.Single(e => e.Key == "wildfire")));
        Assert.Equal(0, events.YearChanceMillionths(state, world, camp, Live.Events.Single(e => e.Key == "flood")));
    }
}
