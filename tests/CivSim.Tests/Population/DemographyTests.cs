using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Population;
using CivSim.Core.Research;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Population;

/// <summary>Births, child mortality and adult deaths (stage 4F, PR 4, ADR 0020, GDD 5.2).</summary>
public class DemographyTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static (GameState State, CivilizationState Civ, SettlementState Camp) Camp(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        SettlementState camp = state.Settlements.First(x => x.IsActive);
        CivilizationState civ = state.Civilizations[camp.Owner.Value];
        Technologies.EnsureSize(Content, civ);
        camp.Mood = 50;
        camp.Health = 100;
        return (state, civ, camp);
    }

    [Fact]
    public void HunterGatherersGrowOnePerMilleAYear()
    {
        (GameState state, _, SettlementState camp) = Camp(91);
        var system = new PopulationSystem(Content);

        // 60‰ births × (1 − 49%) − 29.6‰ adult deaths = +1.0‰.
        Assert.Equal(100, system.GrowthRate(state, camp));

        // Mood moves births: at 70 they are 2‰ higher, of which 51% survive childhood.
        camp.Mood = 70;
        Assert.Equal(100 + (200 * 51 / 100), system.GrowthRate(state, camp));
    }

    [Fact]
    public void FireAndPotteryLowerMortalityAndVillagesRaiseIt()
    {
        (GameState state, CivilizationState civ, SettlementState camp) = Camp(92);
        var system = new PopulationSystem(Content);

        // Fire: child mortality 49% → 48%, growth +1.6‰.
        Technologies.Learn(Content, state, civ, Content.Technology("fire").Id);
        Assert.Equal(160, system.GrowthRate(state, camp));

        // Pottery: adult deaths 29.6‰ → 29‰, growth +2.2‰.
        Technologies.Learn(Content, state, civ, Content.Technology("pottery").Id);
        Assert.Equal(220, system.GrowthRate(state, camp));

        // A settled village has 5‰ more births (farmers have children at shorter gaps), of which 52% live,
        // and 2‰ more adult deaths (crowding, dirt, diseases from animals): 65‰ × 52% − 31‰ = +2.8‰.
        camp.Level = Content.SettlementLevel("village").Id;
        Assert.Equal(280, system.GrowthRate(state, camp));
    }

    [Fact]
    public void SicknessStillKills()
    {
        (GameState state, _, SettlementState camp) = Camp(93);
        var system = new PopulationSystem(Content);
        camp.Health = Content.Population.LowHealthBelow - 10;

        // 10 health points below the limit: 10 × 0.2‰ more deaths.
        Assert.Equal(100 - (10 * Content.Population.LowHealthDeathsPerMillePerPoint.Hundredths), system.GrowthRate(state, camp));
    }

    [Fact]
    public void TheRulesComeFromTheData()
    {
        PopulationRules rules = Content.Population;
        Assert.Equal(60, rules.BirthsPerMille.WholeUnits);
        Assert.Equal(49, rules.ChildMortalityPercent.WholeUnits);
        Assert.Equal(2960, rules.AdultDeathsPerMille.Hundredths);
        Assert.Equal(200, rules.VillageDeathsPerMille.Hundredths);
        Assert.Equal(500, rules.VillageBirthsPerMille.Hundredths);
        Assert.Equal(EffectStat.ChildMortalityPerMille, Content.Effects.Single(e => e.Key == "child_care").Stat);
        Assert.Equal(EffectStat.AdultDeathsPerTenThousand, Content.Effects.Single(e => e.Key == "clean_living").Stat);
        Assert.Equal(EffectStat.BirthsPerTenThousand, Content.Effects.Single(e => e.Key == "fertility").Stat);
    }
}
