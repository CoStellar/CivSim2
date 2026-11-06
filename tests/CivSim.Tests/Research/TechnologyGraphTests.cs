using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CivSim.Core.Content;
using CivSim.Data;
using Xunit;

namespace CivSim.Tests.Research;

/// <summary>The tree of technologies and skills can be climbed from nothing (stage 4F, PR 5, ADR 0020).</summary>
public class TechnologyGraphTests
{
    private static readonly GameContent Content = ContentLoader.Load();

    [Fact]
    public void EveryTechnologyAndSkillIsReachableFromNothing()
    {
        // Strata a new tribe starts with, or grows into, are there from the start; a skill is there once its technology
        // is known. The counts are left out: a big enough people reaches them.
        var strata = Content.Strata.Where(s => s.TargetSharePercent > 0 || Content.Population.NewTribe[s.Id] > 0).Select(s => s.Id).ToHashSet();
        var known = new HashSet<int>();
        bool learned = true;
        while (learned)
        {
            learned = false;
            foreach (TechnologyDefinition t in Content.Technologies)
            {
                if (!known.Contains(t.Id) && Ready(t))
                {
                    known.Add(t.Id);
                    learned = true;
                }
            }
        }

        string[] missing = Content.Technologies.Where(t => !known.Contains(t.Id)).Select(t => t.Key).ToArray();
        Assert.True(missing.Length == 0, "Technologies nobody can reach: " + string.Join(", ", missing));
        string[] skills = Content.Skills.Where(k => k.TechnologyId >= 0 && !known.Contains(k.TechnologyId)).Select(k => k.Key).ToArray();
        Assert.True(skills.Length == 0, "Skills nobody can learn: " + string.Join(", ", skills));

        bool Ready(TechnologyDefinition t) =>
            t.Requires.All(known.Contains)
            && (t.RequiresAny.Count == 0 || t.RequiresAny.Any(known.Contains))
            && t.RequiresPeople.All(Have)
            && (t.RequiresPeopleAny.Count == 0 || t.RequiresPeopleAny.Any(Have));

        bool Have(PeopleRequirement p) => p.SkillId >= 0
            ? Content.Skills[p.SkillId].TechnologyId < 0 || known.Contains(Content.Skills[p.SkillId].TechnologyId)
            : strata.Contains(p.StratumId);
    }

    [Fact]
    public void EveryTechnologyAfterTheFirstNeedsPeople()
    {
        // Stage 4F: after fire and flint every technology names its people.
        string[] free = Content.Technologies
            .Where(t => t.Requires.Count + t.RequiresAny.Count > 0 && t.RequiresPeople.Count + t.RequiresPeopleAny.Count == 0)
            .Select(t => t.Key).ToArray();
        Assert.True(free.Length == 0, "Technologies that need no people: " + string.Join(", ", free));
    }

    [Fact]
    public void PeopleFromALaterTechnologyAreReportedAsALoop()
    {
        string dir = Path.Combine(Path.GetTempPath(), "civsim2-graph-" + Environment.ProcessId);
        Directory.CreateDirectory(dir);
        try
        {
            foreach (string file in Directory.GetFiles(ContentLoader.DefaultDirectory, "*.json"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), overwrite: true);
            }

            // Tanning would need potters, but pottery comes after agriculture, which tanning does not require.
            string path = Path.Combine(dir, "technologies.json");
            string text = File.ReadAllText(path).Replace(
                "\"requiresPeople\": [{\"skill\": \"hunter\", \"count\": 10}], \"inspirations\": [{\"kind\": \"good_in_network\", \"good\": \"hides\"",
                "\"requiresPeople\": [{\"skill\": \"potter\", \"count\": 10}], \"inspirations\": [{\"kind\": \"good_in_network\", \"good\": \"hides\"",
                StringComparison.Ordinal);
            Assert.Contains("\"skill\": \"potter\", \"count\": 10}], \"inspirations\": [{\"kind\": \"good_in_network\", \"good\": \"hides\"", text, StringComparison.Ordinal);
            File.WriteAllText(path, text);

            ContentException ex = Assert.Throws<ContentException>(() => ContentLoader.Load(dir));
            Assert.Contains(ex.Errors, e => e.Contains("'potter' comes with 'pottery'", StringComparison.Ordinal) && e.Contains("'tanning'", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
