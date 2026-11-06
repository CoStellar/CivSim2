using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Research;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Research;

/// <summary>Technologies need their people (stage 4F, PR 3, ADR 0020).</summary>
public class PeopleRequirementTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    private static int Skill(string key) => Content.Skills.Single(s => s.Key == key).Id;

    private static int Stratum(string key) => Content.Strata.Single(s => s.Key == key).Id;

    private static int Tech(string key) => Content.Technology(key).Id;

    private static (GameState State, CivilizationState Civ, HexCoord[] Land) Empty(ulong seed)
    {
        GameState state = GameState.CreateNew(GameSettings.Test(seed), Content, out _);
        state.RemoveSettlements(_ => true);
        WorldState world = state.Worlds[0];
        var land = new List<HexCoord>();
        for (int i = 0; i < world.Geometry.CellCount && land.Count < 400; i++)
        {
            if (!Content.Biomes[world.Map.Biome[i]].IsWater)
            {
                land.Add(world.Geometry.FromIndex(i));
            }
        }

        CivilizationState civ = state.Civilizations[0];
        Technologies.EnsureSize(Content, civ);
        return (state, civ, land.ToArray());
    }

    private static SettlementState Camp(GameState state, CivId civ, HexCoord cell)
    {
        SettlementState s = state.FoundSettlement(civ, state.Worlds[0].Id, cell, "Camp", Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        s.Population[Stratum("producers")] = 100;
        s.Population[Stratum("artisans")] = 10;
        return s;
    }

    [Fact]
    public void TheDataNamesThePeopleOfEveryLaterTechnology()
    {
        PeopleRequirement hunting = Content.Technologies[Tech("cooperative_hunting")].RequiresPeople.Single();
        Assert.Equal(Skill("knapper"), hunting.SkillId);
        Assert.Equal(5, hunting.Count);
        Assert.Equal(Stratum("thinkers"), Content.Technologies[Tech("agriculture")].RequiresPeople.Single().StratumId);
        Assert.Equal(2, Content.Technologies[Tech("weaving")].RequiresPeopleAny.Count);
        Assert.Empty(Content.Technologies[Tech("fire")].RequiresPeople);
    }

    [Fact]
    public void SkilledPeopleCountWithApprenticesAtHalf()
    {
        (GameState state, CivilizationState civ, HexCoord[] land) = Empty(81);
        SettlementState camp = Camp(state, civ.Id, land[0]);
        Technologies.Learn(Content, state, civ, Tech("flint_knapping"));
        camp.Skilled.Clear();
        camp.Skilled[Skill("knapper")] = 3;
        camp.Trainees[Skill("knapper")] = 2;   // 3 + 2 / 2 = 4 of the 5 needed
        Assert.False(Technologies.PeopleReady(Content, state, civ, Tech("cooperative_hunting")));
        Assert.False(Technologies.CanResearch(Content, state, civ, Tech("cooperative_hunting")));

        camp.Trainees[Skill("knapper")] = 4;   // 3 + 2 = 5
        Assert.True(Technologies.CanResearch(Content, state, civ, Tech("cooperative_hunting")));
    }

    [Fact]
    public void OneSettlementRequirementsDoNotAddUpAcrossSettlements()
    {
        (GameState state, CivilizationState civ, HexCoord[] land) = Empty(82);
        SettlementState a = Camp(state, civ.Id, land[0]);
        SettlementState b = Camp(state, civ.Id, land[40]);
        a.Population[Stratum("thinkers")] = 6;
        b.Population[Stratum("thinkers")] = 6;
        var inOne = new PeopleRequirement(-1, Stratum("thinkers"), 10, InOneSettlement: true);
        var inAll = inOne with { InOneSettlement = false };
        Assert.Equal(6, Technologies.People(Content, state, civ.Id, inOne));
        Assert.Equal(12, Technologies.People(Content, state, civ.Id, inAll));
    }

    [Fact]
    public void AnyOfSeveralPeopleIsEnough()
    {
        (GameState state, CivilizationState civ, HexCoord[] land) = Empty(83);
        SettlementState camp = Camp(state, civ.Id, land[0]);
        Technologies.Learn(Content, state, civ, Tech("fire"));
        Technologies.Learn(Content, state, civ, Tech("flint_knapping"));
        camp.Population[Stratum("thinkers")] = 10;
        Technologies.Learn(Content, state, civ, Tech("agriculture"));
        camp.Skilled.Clear();
        Assert.False(Technologies.PeopleReady(Content, state, civ, Tech("weaving")));
        camp.Skilled[Skill("farmer")] = 20;   // 10 herders or 20 farmers
        Assert.True(Technologies.PeopleReady(Content, state, civ, Tech("weaving")));
    }

    [Fact]
    public void ResearchWaitsForThePeopleAndTheCommandSaysWhy()
    {
        (GameState state, CivilizationState civ, HexCoord[] land) = Empty(84);
        SettlementState camp = Camp(state, civ.Id, land[0]);
        Technologies.Learn(Content, state, civ, Tech("flint_knapping"));
        camp.Skilled.Clear();
        var command = new SetResearchCommand(Content, Tech("cooperative_hunting"));
        CommandResult refused = command.Validate(state, civ.Id);
        Assert.False(refused.Success);
        Assert.Contains("people", refused.Reason, StringComparison.Ordinal);

        // Researching it anyway (set before the people were gone): it is not learned, however many points come in.
        civ.Researching = Tech("cooperative_hunting");
        civ.ResearchProgress = 10_000_000;
        var runner = new TurnRunner(state);
        runner.AddSystem(new ResearchSystem(Content));
        runner.RunTurn();
        Assert.False(civ.Knows(Tech("cooperative_hunting")));

        camp.Skilled[Skill("knapper")] = 5;
        runner.RunTurn();
        Assert.True(civ.Knows(Tech("cooperative_hunting")));
    }

    [Fact]
    public void ThinkersGiveMostOfTheScience()
    {
        StratumDefinition thinkers = Content.Strata[Stratum("thinkers")];
        StratumDefinition artisans = Content.Strata[Stratum("artisans")];
        StratumDefinition producers = Content.Strata[Stratum("producers")];
        Assert.Equal(6 * artisans.SciencePerThousandPerYear, thinkers.SciencePerThousandPerYear);   // 30 : 5
        Assert.Equal(5 * producers.SciencePerThousandPerYear, artisans.SciencePerThousandPerYear);  // 5 : 1
    }

    [Fact]
    public void BrokenPeopleRequirementsAreReported()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-people-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            string path = Path.Combine(dir, "technologies.json");
            string text = File.ReadAllText(path)
                .Replace("{\"skill\": \"hunter\", \"count\": 10}", "{\"skill\": \"shaman\", \"count\": 10}", StringComparison.Ordinal)
                .Replace("{\"stratum\": \"thinkers\", \"count\": 5}", "{\"stratum\": \"priests\", \"count\": 5}", StringComparison.Ordinal);
            File.WriteAllText(path, text);

            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("shaman", StringComparison.Ordinal));
            Assert.Contains(ex.Errors, e => e.Contains("priests", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
