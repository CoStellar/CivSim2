using System;
using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Core.Map;
using CivSim.Core.Population;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Population;

/// <summary>Skills of stage 4F, PR 1: data, settlement counts, trimming, save and hash.</summary>
public class SkillTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Skill(string key) => Content.Skills.Single(s => s.Key == key).Id;

    private static int Stratum(string key) => Content.Strata.Single(s => s.Key == key).Id;

    private static SettlementState Village() =>
        new(new SettlementId(0), new CivId(0), WorldId.Earth, new HexCoord(0, 0), "Test", 0, 0, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);

    [Fact]
    public void SkillDataLoadsWithStrataAndTechnologies()
    {
        Assert.NotEmpty(Content.Skills);
        foreach (SkillDefinition s in Content.Skills)
        {
            Assert.InRange(s.StratumId, 0, Content.Strata.Count - 1);
            Assert.InRange(s.TechnologyId, 0, Content.Technologies.Count - 1);
        }

        Assert.Equal(Stratum("artisans"), Content.Skills[Skill("potter")].StratumId);
        Assert.Equal("pottery", Content.Technologies[Content.Skills[Skill("potter")].TechnologyId].Key);
    }

    [Fact]
    public void BrokenSkillDataReportsEveryError()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-skills-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "skills.json");
            string text = File.ReadAllText(path)
                .Replace("\"stratum\": \"producers\", \"technology\": \"fishing\"", "\"stratum\": \"priests\", \"technology\": \"fishing\"", StringComparison.Ordinal)
                .Replace("\"technology\": \"pottery\"", "\"technology\": \"alchemy\"", StringComparison.Ordinal)
                .Replace("\"key\": \"baker\"", "\"key\": \"potter\"", StringComparison.Ordinal);
            File.WriteAllText(path, text);

            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("priests", StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("alchemy", StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("duplicate key 'potter'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void TrimKeepsSkillsWithinTheirStratum()
    {
        SettlementState s = Village();
        s.Population[Stratum("artisans")] = 10;
        s.Skilled[Skill("potter")] = 6;
        s.Skilled[Skill("knapper")] = 3;
        s.Trainees[Skill("potter")] = 4;

        // 13 skilled and learning for 10 artisans: apprentices give way first.
        Skills.Trim(Content.Skills, s);
        Assert.Equal(9, s.SkilledView[Skill("potter")] + s.SkilledView[Skill("knapper")]);
        Assert.Equal(1, s.TraineesView[Skill("potter")]);

        // Half the artisans die: each skill keeps its share, rounded up, and the sum fits.
        s.Population[Stratum("artisans")] = 4;
        Skills.Trim(Content.Skills, s);
        Assert.Equal(0, s.TraineesView[Skill("potter")]);
        Assert.Equal(4, s.SkilledView[Skill("potter")] + s.SkilledView[Skill("knapper")]);
        Assert.True(s.SkilledView[Skill("knapper")] >= 1, "a skill is lost only with its last holder");

        // Nobody left: nobody skilled.
        s.Population[Stratum("artisans")] = 0;
        Skills.Trim(Content.Skills, s);
        Assert.All(s.SkilledView.ToArray(), n => Assert.Equal(0, n));
    }

    [Fact]
    public void TrimLeavesOtherStrataAlone()
    {
        SettlementState s = Village();
        s.Population[Stratum("producers")] = 50;
        s.Skilled[Skill("hunter")] = 20;
        s.Skilled[Skill("potter")] = 5; // no artisans: trimmed
        Skills.Trim(Content.Skills, s);
        Assert.Equal(20, s.SkilledView[Skill("hunter")]);
        Assert.Equal(0, s.SkilledView[Skill("potter")]);
    }

    [Fact]
    public void NewSettlementsHaveRoomForEverySkill()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(3), Content, out WorldGenReport _);
        Assert.All(state.Settlements, s =>
        {
            Assert.Equal(Content.Skills.Count, s.SkilledView.Length);
            Assert.Equal(Content.Skills.Count, s.TraineesView.Length);
        });
    }

    [Fact]
    public void SkillsSurviveSaveAndChangeTheHash()
    {
        GameState state = GameState.CreateNew(GameSettings.Test(5), Content, out WorldGenReport _);
        ulong without = GameStateHash.Compute(state);
        SettlementState first = state.Settlements[0];
        first.Skilled[Skill("knapper")] = 2;
        first.Trainees[Skill("knapper")] = 3;
        ulong with = GameStateHash.Compute(state);
        Assert.NotEqual(without, with);

        using var ms = new MemoryStream();
        SaveGameSerializer.Save(state, ms);
        ms.Position = 0;
        GameState loaded = SaveGameSerializer.Load(ms);
        Assert.Equal(with, GameStateHash.Compute(loaded));
        Assert.Equal(2, loaded.Settlements[0].SkilledView[Skill("knapper")]);
        Assert.Equal(3, loaded.Settlements[0].TraineesView[Skill("knapper")]);
    }
}
