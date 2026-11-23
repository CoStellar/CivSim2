using System;
using System.Linq;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Research;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>How the AI values land with its technologies, and how villages send settlers (PR C).</summary>
public class LandAndMigrationTests
{
    private static readonly GameContent Content = TestContent.Load();

    private static GameState NewState(ulong seed) => GameState.CreateNew(GameSettings.Test(seed), Content, out WorldGenReport _);

    [Fact]
    public void FishersValueTheCoastMoreThanThoseWhoCannotFish()
    {
        GameState state = NewState(21);
        WorldState earth = state.Worlds[0];
        HexGlobe geo = earth.Geometry;
        int coast = Content.Biome("coast").Id;
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        HexCoord shore = default;
        bool found = false;
        for (int i = 0; i < geo.CellCount && !found; i++)
        {
            // Warm land with some food of its own on the shore of a coastal sea.
            if (Content.Biomes[earth.Map.Biome[i]].IsWater || earth.Map.Temperature[i] - (earth.Map.Seasonality[i] / 2) < 5 || Content.Biomes[earth.Map.Biome[i]].Food == 0)
            {
                continue;
            }

            geo.GetNeighbors(geo.FromIndex(i), n6);
            foreach (HexCoord c in n6)
            {
                if (earth.Map.Biome[geo.Index(c)] == coast)
                {
                    shore = geo.FromIndex(i);
                    found = true;
                    break;
                }
            }
        }

        Assert.True(found);
        CivilizationState landlubbers = state.Civilizations[0];
        CivilizationState fishers = state.Civilizations[1];
        Technologies.EnsureSize(Content, landlubbers);
        Technologies.EnsureSize(Content, fishers);
        foreach (TechnologyDefinition t in Content.Technologies.Where(t => t.Key is "flint_knapping" or "fishing"))
        {
            Technologies.Learn(Content, state, fishers, t.Id);
        }

        var governor = new GovernorAdvisor(Content);
        long dry = governor.SiteValue(state, earth, landlubbers, shore);
        long wet = governor.SiteValue(state, earth, fishers, shore);

        Assert.True(wet > dry, $"fishers {wet}, others {dry}");
    }

    [Fact]
    public void ACrowdedVillageSendsSettlersAndAQuietOneDoesNot()
    {
        Assert.Equal(1, Settlers(people: 900));
        Assert.Equal(0, Settlers(people: 300));
    }

    [Fact]
    public void AHungryVillageSendsSettlersToFeedFewerMouths()
    {
        // AI fixes after stage 4g: a village fed at half is not crowded, but it sends a group out to feed fewer mouths.
        Assert.Equal(1, Settlers(people: 300, fedPercent: 50));
        Assert.Equal(0, Settlers(people: 300, fedPercent: Content.Ai.VillageHungryFedPercent));
    }

    [Fact]
    public void ASmallHungryVillageStaysWhereItIs()
    {
        // Decision of 2026-11-09 (stage 6 PR 3a): a village does not move into another, even a small hungry one next
        // to a well-fed neighbour (before, it joined it).
        Assert.Equal(0, SmallVillageJoins(neighbourFedPercent: 100));
        Assert.Equal(0, SmallVillageJoins(neighbourFedPercent: 50));
    }

    private static int SmallVillageJoins(int neighbourFedPercent)
    {
        GameState state = NewState(22);
        CivilizationState civ = state.Civilizations[0];
        Technologies.EnsureSize(Content, civ);
        Technologies.Learn(Content, state, civ, Content.Technology("agriculture").Id);
        SettlementState village = state.Settlements.First(s => s.Owner == civ.Id && s.IsActive);
        village.Level = SettleCommand.VillageLevel(Content, village.Level);
        Span<int> few = village.Population;
        few.Clear();
        few[0] = Content.Population.MinSplitPeople - 10;
        village.FoodNeeded = 1000;
        village.FoodEaten = 400;
        WorldState earth = state.Worlds[0];
        HexCoord near = earth.Geometry.Neighbor(earth.Geometry.Neighbor(village.Cell, HexDirection.N), HexDirection.N);
        SettlementState neighbour = state.FoundSettlement(civ.Id, earth.Id, near, "Neighbour", village.Level, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        neighbour.Population[0] = 300;
        neighbour.FoodNeeded = 1000;
        neighbour.FoodEaten = neighbourFedPercent * 10;
        foreach (CivilizationState c in state.Civilizations)
        {
            c.StepMonths = 0;
        }

        civ.StepMonths = Content.Ai.VillageSettlersEveryYears * 12;
        var council = new AiCouncil(Content);
        council.IssueCommands(state, new CommandQueue());
        return council.Log.Entries.Count(d => d.Action == AiAction.JoinCamp && d.Subject == village.Id.Value && d.Target == neighbour.Id.Value);
    }

    // Decisions to send settlers from a village of `people` that knows all land around it.
    private static int Settlers(int people, int fedPercent = 100)
    {
        GameState state = NewState(22);
        CivilizationState civ = state.Civilizations[0];
        Technologies.EnsureSize(Content, civ);
        Technologies.Learn(Content, state, civ, Content.Technology("agriculture").Id);
        Array.Fill(civ.SeenYear, 1);
        civ.Traits[(int)Trait.Expansiveness] = 500; // neutral: settlers from 80% full on
        SettlementState village = state.Settlements.First(s => s.Owner == civ.Id && s.IsActive);
        village.Level = SettleCommand.VillageLevel(Content, village.Level);
        Span<int> crowd = village.Population;
        crowd.Clear();
        crowd[0] = people;
        village.FoodNeeded = 1000;
        village.FoodEaten = fedPercent * 10;
        foreach (CivilizationState c in state.Civilizations)
        {
            c.StepMonths = 0; // only this civilization plans now
        }

        // A step of a whole settlers' period (villageSettlersEveryYears), so the village is due whatever its stagger.
        civ.StepMonths = Content.Ai.VillageSettlersEveryYears * 12;
        var council = new AiCouncil(Content);
        council.IssueCommands(state, new CommandQueue());
        return council.Log.Entries.Count(d => d.Action == AiAction.SendSettlers && d.Subject == village.Id.Value);
    }
}
