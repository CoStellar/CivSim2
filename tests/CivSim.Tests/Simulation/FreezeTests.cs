using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Events;
using CivSim.Core.Governance;
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
    public void TheDataFreezesGovernmentsStrataInfluenceRevoltsPactsAndAlliances()
    {
        Assert.False(Live.GovernmentsEnabled);
        Assert.False(Live.StrataInfluenceEnabled);
        Assert.False(Live.Stability.RevoltsEnabled);
        Assert.False(Live.Treaties.Kinds[(int)TreatyKind.NonAggression].Enabled);
        Assert.False(Live.Treaties.Kinds[(int)TreatyKind.DefensiveAlliance].Enabled);
        Assert.True(Live.Treaties.Kinds[(int)TreatyKind.Peace].Enabled); // contact, war, peace and barter stay
        Assert.True(Live.Treaties.Kinds[(int)TreatyKind.Trade].Enabled);

        GameContent thawed = Live.Thawed();
        Assert.True(thawed.GovernmentsEnabled && thawed.StrataInfluenceEnabled && thawed.Stability.RevoltsEnabled);
        Assert.All(thawed.Treaties.Kinds, k => Assert.True(k.Enabled));
    }

    [Fact]
    public void APeopleKeepsItsGovernmentAndSignsNoPactUntilTheBronzeAge()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(32), Live, out WorldGenReport _);
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        int other = a.Government == 0 ? 1 : 0; // every people keeps the government it starts with (by its character)
        Assert.Equal("Other governments come with a later age.", Governments.CannotChange(Live, state, a.Id, other));
        Assert.Null(Governments.CannotChange(Live.Thawed(), state, a.Id, other));

        a.Relations.Add(new Relation(b.Id) { Level = ContactLevel.Met });
        b.Relations.Add(new Relation(a.Id) { Level = ContactLevel.Met });
        Assert.Equal("Such treaties come with a later age.", Treaties.CannotSign(Live, state, a.Id, b.Id, TreatyKind.NonAggression));
        Assert.Equal("Such treaties come with a later age.", Treaties.CannotSign(Live, state, a.Id, b.Id, TreatyKind.DefensiveAlliance));
        Assert.Null(Treaties.CannotSign(Live, state, a.Id, b.Id, TreatyKind.Trade));
        Assert.NotEqual("Such treaties come with a later age.", Treaties.CannotSign(Live.Thawed(), state, a.Id, b.Id, TreatyKind.NonAggression));
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
