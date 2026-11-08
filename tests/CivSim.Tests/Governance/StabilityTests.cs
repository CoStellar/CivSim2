using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Governance;
using CivSim.Core.Map;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Governance;

/// <summary>Capital, administration, stability, loyalty and unrest (GDD 7.3; stage 4f PR 3).</summary>
public class StabilityTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Gov(string key) => Content.Governments.Single(g => g.Key == key).Id;

    private static int Stratum(string key) => Content.Strata.Single(s => s.Key == key).Id;

    private static SettlementState Found(GameState state, CivilizationState civ, int dCol, int people, int mood)
    {
        HexCoord at = state.Worlds[0].Geometry.Wrap(civ.StartCell.Col + dCol, civ.StartCell.Row);
        SettlementState s = state.FoundSettlement(civ.Id, WorldId.Earth, at, "Extra" + state.NextSettlementId, Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Stratum("producers")] = people;
        s.Mood = mood;
        return s;
    }

    [Fact]
    public void TheFirstCampIsTheCapitalAndALostCapitalHurts()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(21), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        SettlementState camp = state.Settlements.First(s => s.Owner == civ.Id);
        Assert.Equal(camp.Id.Value, civ.Capital);
        Assert.Null(Stability.KeepCapital(Content, state, civ));

        // Taken by a neighbour: the largest settlement left becomes the capital, and stability suffers for years.
        SettlementState small = Found(state, civ, 3, 20, 50);
        SettlementState big = Found(state, civ, -3, 90, 50);
        camp.Owner = state.Civilizations[1].Id;
        string? line = Stability.KeepCapital(Content, state, civ);
        Assert.Equal(big.Id.Value, civ.Capital);
        Assert.Equal($"{Governments.Title(Content, civ)} lost its capital {camp.Name}; {big.Name} is the new capital.", line);
        Assert.Equal(state.Month + (Content.Stability.CapitalLostYears * 12L), civ.CapitalLostUntilMonth);

        // Left by its own people: the leader simply moves.
        big.Status = SettlementStatus.Abandoned;
        Assert.Null(Stability.KeepCapital(Content, state, civ));
        Assert.Equal(small.Id.Value, civ.Capital);
    }

    [Fact]
    public void TheCapitalMovesToTheLargestVillageWhenThePeopleSettle()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(23), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        SettlementState camp = state.Settlements.First(s => s.Owner == civ.Id);
        SettlementState small = Found(state, civ, 3, 200, 50);
        SettlementState large = Found(state, civ, -3, 400, 50);
        Assert.Null(Stability.KeepCapital(Content, state, civ)); // only camps: the capital stays with the leader's camp

        small.Level = Core.Economy.SettleCommand.VillageLevel(Content, small.Level);
        large.Level = Core.Economy.SettleCommand.VillageLevel(Content, large.Level);
        string? line = Stability.KeepCapital(Content, state, civ);
        Assert.Equal(large.Id.Value, civ.Capital);
        Assert.Equal($"{Governments.Title(Content, civ)} made {large.Name} its capital.", line);
        Assert.Equal(0, civ.CapitalLostUntilMonth); // no loss of stability

        // A settled capital stays, even when another village grows larger.
        small.Population[Stratum("producers")] = 900;
        Assert.Null(Stability.KeepCapital(Content, state, civ));
        Assert.Equal(large.Id.Value, civ.Capital);
        Assert.NotEqual(camp.Id.Value, civ.Capital);
    }

    [Fact]
    public void StabilityWeighsTheMoodByTheInfluenceOfTheStrata()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(22), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.StepMonths = 600;
        SettlementState camp = state.Settlements.First(s => s.Owner == civ.Id);
        camp.Population.Clear();
        camp.Population[Stratum("producers")] = 100;
        camp.Mood = 80;
        SettlementState elders = Found(state, civ, 3, 0, 20);
        elders.Population[Stratum("producers")] = 0;
        elders.Population[Stratum("thinkers")] = 100;

        // A council listens to the thinkers 4 times as much: (100·80 + 400·20) / 500 = 32.
        civ.Government = Gov("elder_council");
        StabilityParts council = Stability.Parts(Content, state, civ, 0, state.Month);
        Assert.Equal(32, council.Mood);
        Assert.Equal(42, council.Total); // + 10 for the council

        // A chief listens to warriors and leaders: thinkers count once, (100·80 + 100·20) / 200 = 50.
        civ.Government = Gov("chiefdom");
        Assert.Equal(50, Stability.Parts(Content, state, civ, 0, state.Month).Total);
    }

    [Fact]
    public void AnOverloadedStateACrisisAndAnarchyLowerStability()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(23), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Gov("chiefdom");
        civ.StepMonths = 600;
        int capacity = Stability.Capacity(Content, civ);
        Assert.Equal(0, Stability.Parts(Content, state, civ, capacity, state.Month).Overload);
        Assert.Equal(Content.Stability.OverloadPenaltyPer100Percent, Stability.Parts(Content, state, civ, capacity * 2, state.Month).Overload);
        Assert.Equal(Content.Stability.OverloadMaxPenalty, Stability.Parts(Content, state, civ, capacity * 100, state.Month).Overload);

        // A crisis of 5 years counts for a tenth of a 50-year step and fully in a month of war.
        civ.CrisisUntilMonth = state.Month + 60;
        Assert.Equal(Content.Stability.CrisisPenalty / 10, Stability.Parts(Content, state, civ, 0, state.Month).Crisis);
        civ.StepMonths = 1;
        Assert.Equal(Content.Stability.CrisisPenalty, Stability.Parts(Content, state, civ, 0, state.Month).Crisis);

        civ.AnarchyUntilMonth = state.Month + 24;
        Assert.Equal(Content.Stability.AnarchyPenalty, Stability.Parts(Content, state, civ, 0, state.Month).Anarchy);
        Assert.NotNull(Treaties.CannotDeclareWar(Content, state, civ.Id, state.Civilizations[1].Id, WarGoal.Loot, -1));
        Assert.Equal("Our state is in anarchy.", Governments.CannotChange(Content, state, civ.Id, Gov("elder_council")));
    }

    [Fact]
    public void FarSettlementsCostMoreAdministrationAndAreLessLoyal()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(24), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Gov("elder_council");
        civ.StepMonths = 600;
        SettlementState camp = state.Settlements.First(s => s.Owner == civ.Id);
        var roads = new RoadCosts();
        Stability.Update(Content, state, civ, roads, null);
        Assert.Equal(Content.Stability.AdminPerSettlement, civ.AdminUsed); // the capital alone, no road
        Assert.Equal(civ.Stability, camp.Loyalty);

        SettlementState near = Found(state, civ, 1, 50, 60);
        SettlementState far = Found(state, civ, 6, 50, 60);
        Stability.Update(Content, state, civ, roads, null);
        WorldState world = state.Worlds[0];
        int toNear = roads.To(world.Geometry.Index(near.Cell), Content.Stability.RoadSearchCost);
        int toFar = roads.To(world.Geometry.Index(far.Cell), Content.Stability.RoadSearchCost);
        Assert.True(toFar > toNear, $"road {toNear} to the near one, {toFar} to the far one");
        Assert.Equal((3 * Content.Stability.AdminPerSettlement) + ((toNear + toFar) * Content.Stability.AdminPerRoadCost), civ.AdminUsed);
        Assert.True(far.Loyalty <= near.Loyalty && near.Loyalty <= camp.Loyalty);
    }

    [Fact]
    public void LowStabilityBringsUnrestThatLowersOutput()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(25), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Government = Gov("chiefdom");
        civ.StepMonths = 600;
        SettlementState camp = state.Settlements.First(s => s.Owner == civ.Id);
        EffectDefinition unrest = Content.Effects.Single(e => e.Key == Content.Stability.UnrestEffect);
        var roads = new RoadCosts();

        camp.Mood = 10;
        int before = civ.Stability;
        Stability.Update(Content, state, civ, roads, unrest);
        Assert.Equal(10, civ.Stability);
        int output = state.Effects.Total(Content.Effects, EffectStat.OutputPercent, EffectTarget.Civilization, WorldId.Earth, civ.Id.Value);
        Assert.Equal(-(Content.Stability.UnrestBelow - 10) * Content.Stability.UnrestOutputPercentPerPoint, output);
        Assert.StartsWith("Unrest spread in the Chiefdom of ", Stability.UnrestLine(Content, civ, before), System.StringComparison.Ordinal);

        // Calm returns: the unrest is gone.
        camp.Mood = 70;
        before = civ.Stability;
        Stability.Update(Content, state, civ, roads, unrest);
        state.Effects.RemoveExpired(state.Month);
        Assert.Equal(0, state.Effects.Total(Content.Effects, EffectStat.OutputPercent, EffectTarget.Civilization, WorldId.Earth, civ.Id.Value));
        Assert.StartsWith("Calm returned to the ", Stability.UnrestLine(Content, civ, before), System.StringComparison.Ordinal);
    }

    [Fact]
    public void StabilitySurvivesASave()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(26), Content, out _);
        CivilizationState civ = state.Civilizations[0];
        civ.Stability = 17;
        civ.AdminUsed = 333;
        civ.AnarchyUntilMonth = 99;
        civ.CapitalLostUntilMonth = 77;
        state.Settlements[0].Loyalty = 12;
        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(Core.Hashing.GameStateHash.Compute(state), Core.Hashing.GameStateHash.Compute(loaded));
        Assert.Equal(civ.Capital, loaded.Civilizations[0].Capital);
        Assert.Equal(12, loaded.Settlements[0].Loyalty);
    }

    [Fact]
    public void BadStabilityDataIsRejected()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim-stability-" + System.Environment.ProcessId + "-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "stability.json");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"unrestEffect\": \"unrest\"", "\"unrestEffect\": \"tradition\"", System.StringComparison.Ordinal));
            string govs = Path.Combine(dir, "governments.json");
            File.WriteAllText(govs, File.ReadAllText(govs).Replace("{\"thinkers\": 4, \"leaders\": 4}", "{\"priests\": 4}", System.StringComparison.Ordinal));
            ContentException e = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(e.Errors, x => x.Contains("must change output_percent of a civilization", System.StringComparison.Ordinal));
            Assert.Contains(e.Errors, x => x.Contains("unknown stratum 'priests'", System.StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
