using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Protocol;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;
using CivSim.Server;
using Xunit;

namespace CivSim.Tests.Simulation;

/// <summary>
/// The rise of a civilization made visible (stage 6 PR P2b, ADR 0026 rule 12): settlement looks, milestones in the chronicle with the
/// minute of the game, how long the settlers' food lasts.
/// </summary>
public class RiseTests
{
    private static readonly GameContent Live = ContentLoader.Load();

    [Fact]
    public void ASettlementLooksAsFarAsItsPeopleHaveCome()
    {
        Assert.Equal(["camp", "hearth", "village", "walled", "town"], Live.SettlementLooks.Select(l => l.Key));
        int fire = Live.Technology("fire").Id;
        int stone = Live.Technology("stonemasonry").Id;
        int village = Live.SettlementLevel("village").Id;
        int town = Live.SettlementLevel("town").Id;
        string Look(int level, params int[] known) => Live.SettlementLooks[Live.SettlementLook(level, known.Contains)].Key;

        Assert.Equal("camp", Look(0));
        Assert.Equal("hearth", Look(0, fire));
        Assert.Equal("hearth", Look(0, fire, stone)); // a camp has no wall, whatever it knows
        Assert.Equal("village", Look(village, fire));
        Assert.Equal("walled", Look(village, fire, stone));
        Assert.Equal("town", Look(town, fire, stone));
    }

    [Fact]
    public void TheViewCarriesEachSettlementsLookAndTheContentNamesThem()
    {
        var server = GameServer.CreateNew(GameSettings.Test(91), Live);
        (InMemoryConnection client, InMemoryConnection serverSide) = InMemoryConnection.CreatePair();
        server.Accept(serverSide);
        client.Send(new HelloMessage(ProtocolInfo.Version, "Patryk", ClientRole.Observer));
        server.Pump();
        CivilizationState civ = server.State.Civilizations[0];
        Learn(civ, "fire");
        server.AdvanceTurn();
        var messages = new List<IProtocolMessage>();
        while (client.TryReceive(out IProtocolMessage? m))
        {
            messages.Add(m!);
        }

        ContentMessage content = messages.OfType<ContentMessage>().Single();
        Assert.Equal("hearth", content.SettlementLooks![1]);
        GameViewMessage view = messages.OfType<GameViewMessage>().Last();
        Assert.All(view.Settlements!.Where(s => s.Owner == 0 && s.Status == 0), s => Assert.Equal("hearth", content.SettlementLooks![s.Look]));
        Assert.All(view.Settlements!.Where(s => s.Owner != 0 && s.Status == 0), s => Assert.Equal("camp", content.SettlementLooks![s.Look]));
    }

    [Fact]
    public void MilestonesAreReportedOnceWithTheMinuteAndNotAgainAfterALoad()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(92), Live, out WorldGenReport _);
        var tracker = new MilestoneTracker(Live);
        tracker.Prime(state);
        Assert.Empty(tracker.Check(state));

        CivilizationState civ = state.Civilizations[0];
        Learn(civ, "fire");
        MilestoneReached first = Assert.Single(tracker.Check(state));
        Assert.Equal(Milestone.FirstTechnology, first.Kind);
        Assert.Equal(600, tracker.TicksPerMinute); // a minute at x1
        Assert.Equal($"Minute 1: the {civ.Name} learned their first technology: {Live.Technology("fire").Name}.", first.Text);

        Learn(civ, "flint_knapping");
        civ.Era = 1;
        MilestoneReached era = Assert.Single(tracker.Check(state)); // a second technology is no milestone; a new era is
        Assert.Equal(Milestone.NewEra, era.Kind);
        Assert.Contains(Live.Eras[1].Name, era.Text);

        // A loaded game knows what it has already reached.
        var loaded = new MilestoneTracker(Live);
        loaded.Prime(state);
        Assert.Empty(loaded.Check(state));
    }

    [Fact]
    public void SettlersShowHowManyMonthsTheirFoodLasts()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(93), Live, out WorldGenReport _);
        CivilizationState civ = state.Civilizations[0];
        SettlementState home = state.Settlements.First(s => s.Owner == civ.Id);
        WorldState world = state.Worlds[0];
        Unit u = state.Units.Add(Live.Units.First(UnitRole.Settlers), civ.Id, world.Id, world.Geometry.Index(home.Cell), home.Id.Value, UnitOrder.Idle,
            Live.Strata.Count, Live.Skills.Count, Live.Goods.Count);
        u.People[0] = 12;
        u.Cargo[Live.Good("meat").Id] = Amount.FromUnits(10);
        u.Cargo[Live.Good("hides").Id] = Amount.FromUnits(50); // not food
        Assert.Equal(-1, u.FoodMonths);

        var runner = new TurnRunner(state);
        runner.AddSystem(new UnitSystem(Live));
        runner.RunTurn();

        long month = CivSim.Core.Episodes.Campaigns.FoodFor(Live, 12, 1).Hundredths;
        Assert.Equal((int)(u.Cargo[Live.Good("meat").Id].Hundredths / month), u.FoodMonths); // hides do not count
        Assert.InRange(u.FoodMonths, 8, 10); // 10 units for 12 people eating 1 a month, less what they ate
        Assert.Equal(u.FoodMonths, GameSnapshot.From(state).Units.Single(x => x.Id == u.Id).FoodMonths);
    }

    private static void Learn(CivilizationState civ, string key)
    {
        if (civ.KnownTechnologies.Length < Live.Technologies.Count)
        {
            bool[] known = new bool[Live.Technologies.Count];
            civ.KnownTechnologies.CopyTo(known, 0);
            civ.KnownTechnologies = known;
        }

        civ.KnownTechnologies[Live.Technology(key).Id] = true;
    }
}
