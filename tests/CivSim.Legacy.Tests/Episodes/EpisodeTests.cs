using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Episodes;

/// <summary>Episodes with a clock of their own and the first war campaigns (ADR 0022; stage 4e PR 2).</summary>
public class EpisodeTests
{
    private static readonly GameContent Content = ContentLoader.Load(); // the live clock (ADR 0025): episodes step on ticks

    private static int Warriors => Content.Strata.Single(s => s.Key == "warriors").Id;

    // Two civilizations in their own villages a few cells apart on known land, everyone else gone.
    private static (GameState State, SettlementState Home, SettlementState Target) Front(ulong seed, int distance = 6)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        state.RemoveSettlements(_ => true);
        WorldState world = state.Worlds[0];
        HexGlobe geo = world.Geometry;
        CivilizationState a = state.Civilizations[0];
        CivilizationState b = state.Civilizations[1];
        for (int i = 0; i < geo.CellCount; i++)
        {
            HexCoord from = geo.FromIndex(i);
            HexCoord to = new(from.Col, from.Row + distance);
            if (to.Row >= geo.Height || !Land(world, i))
            {
                continue;
            }

            bool way = true;
            for (int r = 1; r <= distance && way; r++)
            {
                way = Land(world, geo.Index(new HexCoord(from.Col, from.Row + r)));
            }

            if (!way)
            {
                continue;
            }

            SettlementState home = Village(state, a, from, 120, 30);
            SettlementState target = Village(state, b, to, 60, 4);
            foreach (CivilizationState c in state.Civilizations)
            {
                c.SeenYear = new int[geo.CellCount];
                System.Array.Fill(c.SeenYear, 1);
            }

            foreach (CivilizationState x in state.Civilizations)
            {
                foreach (CivilizationState y in state.Civilizations)
                {
                    if (x != y)
                    {
                        Contacts.Meet(Content, state, x, y.Id, ContactWay.Sighted);
                    }
                }
            }

            return (state, home, target);
        }

        throw new Xunit.Sdk.XunitException("No land strip found.");
    }

    private static bool Land(WorldState world, int cell) => !Content.Biomes[world.Map.Biome[cell]].IsWater;

    private static SettlementState Village(GameState state, CivilizationState civ, HexCoord cell, int producers, int warriors)
    {
        SettlementState s = state.FoundSettlement(civ.Id, state.Worlds[0].Id, cell, civ.Name + " Village", Content.SettlementLevel("village").Id, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Content.Strata[0].Id] = producers;
        s.Population[Warriors] = warriors;
        s.ScoutOut = true; // no scouts in these tests (stage 6 PR 3b: they take people)
        s.Stock[Content.Goods.Single(g => g.Key == "wild_plants").Id] = Amount.FromUnits(200);
        return s;
    }

    private static TurnRunner Runner(GameState state)
    {
        var runner = new TurnRunner(state);
        runner.AddSystem(new Core.Units.UnitSystem(Content)); // stage 6 PR 3b: the army walks as a unit
        runner.AddSystem(new CampaignSystem(Content));
        runner.AddSystem(new WarSystem(Content));
        runner.AddSystem(new CalendarSystem(Content));
        WorldClock.Begin(Content, state);
        return runner;
    }

    [Fact]
    public void ACampaignStepsOnTicksAndTheWorldGoesOn()
    {
        (GameState state, SettlementState home, SettlementState target) = Front(71);
        CivilizationState a = state.Civilizations[0];
        target.Population[Content.Strata[0].Id] = 20; // weak enough to attack: 20 against 4 + 25% of 20
        Treaties.DeclareWar(Content, state, a.Id, target.Owner, WarGoal.Loot, -1);
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Alarms = Alarm.None;
        }
        var launch = new LaunchCampaignCommand(Content, home.Id, target.Id, 20);
        Assert.True(launch.Validate(state, a.Id).Success, launch.Validate(state, a.Id).Reason);
        launch.ApplyWithChronicle(state, a.Id);
        Assert.Equal(10, SettlementAssetsWarriors(home)); // 20 left, 10 stayed home

        TurnRunner runner = Runner(state);
        long startMonth = state.Month;
        long startTick = state.Turn;
        Episode campaign = state.Episodes.All[0];
        Assert.Equal(startTick + Content.Time.TicksPerActionMonth, campaign.NextTick); // a month of action is a few ticks
        int ticks = 0;
        while (state.Episodes.Count > 0 && ticks < 2000)
        {
            runner.RunTurn();
            ticks++;
        }

        // ADR 0025: the world never waits for a campaign; the year counter moved on in the world's pulses meanwhile.
        Assert.Equal(0, state.Episodes.Count);
        // Stage 6 PR 3b: the army walks as a unit, 15 ticks per flat cell, there and back (a few hundred ticks).
        Assert.True(ticks < 1000, $"the campaign took {ticks} ticks");
        Assert.True(state.Month > startMonth, "the world went on");
        Assert.True(SettlementAssetsWarriors(home) > 10, "the survivors came home");
        Treaty war = state.Treaties.Find(a.Id, target.Owner, TreatyKind.War)!;
        Assert.Equal(1, war.Campaigns);
        Assert.True(war.CampaignMonths > 0);
        Assert.Equal(ticks / Content.Time.TicksPerActionMonth, war.CampaignMonths, 1.0); // a campaign month per few ticks
        Assert.NotEqual(0, war.Score);
    }

    [Fact]
    public void AQuietWarEndsAfterFiveYearsOfAction()
    {
        (GameState state, _, SettlementState target) = Front(72);
        CivilizationState a = state.Civilizations[0];
        TurnRunner runner = Runner(state);
        long declared = state.Turn;
        Treaties.DeclareWar(Content, state, a.Id, target.Owner, WarGoal.Loot, -1);
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Alarms = Alarm.None;
        }

        long quiet = Content.Time.ActionTicks(Content.War.QuietPeaceYears * 12L);
        for (long k = 0; k < quiet * 2 && state.Treaties.AtWar(a.Id, target.Owner); k++)
        {
            runner.RunTurn();
        }

        Assert.False(state.Treaties.AtWar(a.Id, target.Owner));
        Assert.True(state.Treaties.Has(a.Id, target.Owner, TreatyKind.Peace));
        Assert.Equal(declared + quiet, state.Treaties.Find(a.Id, target.Owner, TreatyKind.Peace)!.SinceTick); // in the very tick it ran out
    }

    [Fact]
    public void ACampaignTurnsBackFromATargetTooStrong()
    {
        (GameState state, SettlementState home, SettlementState target) = Front(73);
        CivilizationState a = state.Civilizations[0];
        target.Population[Warriors] = 200;
        Treaties.DeclareWar(Content, state, a.Id, target.Owner, WarGoal.Loot, -1);
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Alarms = Alarm.None;
        }
        new LaunchCampaignCommand(Content, home.Id, target.Id, 20).ApplyWithChronicle(state, a.Id);
        TurnRunner runner = Runner(state);
        runner.AddCommandSource(new CivSim.Core.Ai.CommanderAdvisor(Content)); // the commander looks every month (PR 5)
        var lines = new List<string>();
        for (int k = 0; k < 50 && state.Episodes.Count > 0; k++)
        {
            runner.RunTurn();
            lines.AddRange(runner.LastChronicle);
        }

        Assert.Contains(lines, l => l.Contains("called its warriors back", System.StringComparison.Ordinal));
        Assert.Equal(200, target.Population[Warriors]);
        Assert.Equal(30, SettlementAssetsWarriors(home));
    }

    [Fact]
    public void CampaignsNeedAWarAndLeaveAThirdAtHome()
    {
        (GameState state, SettlementState home, SettlementState target) = Front(74);
        CivilizationState a = state.Civilizations[0];
        Assert.False(new LaunchCampaignCommand(Content, home.Id, target.Id, 10).Validate(state, a.Id).Success, "no war");
        Treaties.DeclareWar(Content, state, a.Id, target.Owner, WarGoal.Loot, -1);
        Assert.False(new LaunchCampaignCommand(Content, home.Id, target.Id, 25).Validate(state, a.Id).Success, "a third stays home");
        Assert.False(new LaunchCampaignCommand(Content, home.Id, target.Id, Content.War.MinWarriors - 1).Validate(state, a.Id).Success, "too few");
        Assert.True(new LaunchCampaignCommand(Content, home.Id, target.Id, 20).Validate(state, a.Id).Success);
    }

    [Fact]
    public void ACampaignSurvivesASaveOnTheWay()
    {
        (GameState state, SettlementState home, SettlementState target) = Front(75);
        CivilizationState a = state.Civilizations[0];
        Treaties.DeclareWar(Content, state, a.Id, target.Owner, WarGoal.Loot, -1);
        foreach (CivilizationState c in state.Civilizations)
        {
            c.Alarms = Alarm.None;
        }
        new LaunchCampaignCommand(Content, home.Id, target.Id, 20).ApplyWithChronicle(state, a.Id);
        TurnRunner runner = Runner(state);
        runner.RunTurn();

        using var stream = new MemoryStream();
        SaveGameSerializer.Save(state, stream);
        stream.Position = 0;
        GameState loaded = SaveGameSerializer.Load(stream);
        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
        Assert.Equal(state.WorldMonths, loaded.WorldMonths);
        Episode e = Assert.Single(loaded.Episodes.All);
        Assert.Equal(EpisodeKind.Campaign, e.Kind);
        Assert.Equal(20, e.Warriors);

        // The same steps on both give the same state.
        TurnRunner other = Runner(loaded);
        for (int k = 0; k < 6; k++)
        {
            runner.RunTurn();
            other.RunTurn();
        }

        Assert.Equal(GameStateHash.Compute(state), GameStateHash.Compute(loaded));
    }

    private static int SettlementAssetsWarriors(SettlementState s) => s.Population[Warriors];
}
