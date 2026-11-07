using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using CivSim.Core.Ai;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.Research;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Data;
using CivSim.Server.Saves;
using Xunit;

namespace CivSim.Tests.Ai;

/// <summary>
/// Patryk's rule of 2026-11-06: every value of the AI must change something. Each trait, each goal weight and each
/// parameter of <c>data/ai.json</c> is changed in turn, the AI decides in a fixed set of saved situations (Stone Age
/// camps, Neolithic villages), and at least one decision (or the strategy it sets) must come out different. A value
/// that changes nothing fails the test: give it a role or remove it.
/// </summary>
public class DeadValueTests
{
    private static readonly GameContent Content = ContentLoader.Load();
    private static readonly Lazy<byte[][]> Situations = new(MakeSituations);

    // Turns the civilizations live on from each situation when a value works over time.
    private const int Turns = 3;

    [Fact]
    public void EveryTraitChangesADecision()
    {
        // Some traits work through memory and first impressions (openness, vengefulness): the civilizations live a few
        // turns, and their opinions of each other count as well as their decisions.
        string baseline = Fingerprint(Content, null, Turns);
        string[] results = new string[Character.TraitCount * 2];
        System.Threading.Tasks.Parallel.For(0, results.Length, k => results[k] = Fingerprint(Content, s =>
        {
            foreach (CivilizationState civ in s.Civilizations)
            {
                civ.Traits[k / 2] = k % 2 == 0 ? 0 : 1000;
            }
        }, Turns));
        var dead = new List<string>();
        for (int t = 0; t < Character.TraitCount; t++)
        {
            if (results[2 * t] == baseline && results[(2 * t) + 1] == baseline)
            {
                dead.Add(((Trait)t).ToString());
            }
        }

        Assert.True(dead.Count == 0, "Traits that change nothing: " + string.Join(", ", dead));
    }

    [Fact]
    public void EveryGoalWeightChangesADecision()
    {
        // The strategist is held back, so the weights stay as set.
        string baseline = Fingerprint(Content, s => Pin(s, -1, 0));
        bool[] changes = new bool[Character.GoalCount];
        System.Threading.Tasks.Parallel.For(0, changes.Length, g =>
            changes[g] = new[] { 300, 3000 }.Any(v => Fingerprint(Content, s => Pin(s, g, v)) != baseline));
        var dead = new List<string>();
        for (int g = 0; g < Character.GoalCount; g++)
        {
            if (!changes[g])
            {
                dead.Add(((Goal)g).ToString());
            }
        }

        Assert.True(dead.Count == 0, "Goal weights that change nothing: " + string.Join(", ", dead));
    }

    [Fact]
    public void EveryAiParameterChangesADecision()
    {
        string[] baseline = Enumerable.Range(0, Situations.Value.Length).Select(k => Fingerprint(Content, null, 0, k)).ToArray();
        (string Name, AiRules[] Variants)[] all = Variants(Content.Ai).Select(v => (v.Name, v.Variants.ToArray())).ToArray();
        bool[] changes = new bool[all.Length];
        System.Threading.Tasks.Parallel.For(0, all.Length, k => changes[k] = all[k].Variants.Any(rules => Changes(rules, baseline)));
        var dead = new List<string>();
        for (int k = 0; k < all.Length; k++)
        {
            if (!changes[k])
            {
                dead.Add(all[k].Name);
            }
        }

        Assert.True(dead.Count == 0, "Parameters of data/ai.json that change nothing: " + string.Join(", ", dead));
    }

    [Fact]
    public void TheCheckSeesAChange()
    {
        // Guard against a fingerprint that never differs (the test above would pass for nothing).
        Assert.NotEqual(Fingerprint(Content, null), Fingerprint(With(Content, Content.Ai with { PickAmongTop = 1 }), null));
        Assert.Equal(Fingerprint(Content, null), Fingerprint(Content, null));
    }

    // True when the rules change a decision; rules the game would refuse to load (out of range) change nothing.
    // Situations are compared one by one, so a change is usually found after the first few.
    private static bool Changes(AiRules rules, string[] baseline)
    {
        try
        {
            GameContent content = With(Content, rules);
            for (int k = 0; k < baseline.Length; k++)
            {
                if (Fingerprint(content, null, 0, k) != baseline[k])
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception e) when (e is ArgumentException or ArithmeticException)
        {
            return false;
        }
    }

    // Goal weights fixed (one goal set to `value`, the rest neutral) and no strategy review in the situation.
    private static void Pin(GameState state, int goal, int value)
    {
        foreach (CivilizationState civ in state.Civilizations)
        {
            var weights = Enumerable.Repeat(1000, Character.GoalCount).ToArray();
            if (goal >= 0)
            {
                weights[goal] = value;
            }

            civ.GoalWeights = weights;
            civ.StrategyEra = civ.Era;
            civ.StrategyReviewMonth = long.MaxValue;
        }
    }

    // Every way to change one parameter: each number doubled, halved and set to its extremes, each goal swapped.
    private static IEnumerable<(string Name, IEnumerable<AiRules> Variants)> Variants(AiRules rules)
    {
        foreach (PropertyInfo p in typeof(AiRules).GetProperties())
        {
            if (p.PropertyType == typeof(int))
            {
                int v = (int)p.GetValue(rules)!;
                yield return (p.Name, Numbers(v).Select(n => Set(rules, p, n)));
            }
            else if (p.PropertyType == typeof(Goal))
            {
                yield return (p.Name, Enum.GetValues<Goal>().Where(g => !g.Equals(p.GetValue(rules))).Select(g => Set(rules, p, g)));
            }
            else if (p.PropertyType == typeof(CommanderRules))
            {
                foreach (PropertyInfo q in typeof(CommanderRules).GetProperties())
                {
                    int v = (int)q.GetValue(rules.Commander)!;
                    yield return ("commander." + q.Name, Numbers(v).Select(n => rules with { Commander = Set(rules.Commander, q, n) }));
                }
            }
            else if (p.PropertyType == typeof(ScholarRules))
            {
                foreach (PropertyInfo q in typeof(ScholarRules).GetProperties())
                {
                    if (q.PropertyType == typeof(int))
                    {
                        int v = (int)q.GetValue(rules.Scholar)!;
                        yield return ("scholar." + q.Name, Numbers(v).Select(n => rules with { Scholar = Set(rules.Scholar, q, n) }));
                    }
                    else if (q.PropertyType == typeof(Amount))
                    {
                        long v = ((Amount)q.GetValue(rules.Scholar)!).Hundredths;
                        yield return ("scholar." + q.Name, Numbers(v).Select(n => rules with { Scholar = Set(rules.Scholar, q, Amount.FromHundredths(n)) }));
                    }
                }

                for (int k = 0; k < rules.Scholar.Effects.Count; k++)
                {
                    int at = k;
                    EffectWorth w = rules.Scholar.Effects[k];
                    yield return ("scholar.effects." + w.Stat, Numbers(w.PerHundredPeoplePerPoint.Hundredths).Select(n =>
                    {
                        var list = rules.Scholar.Effects.ToList();
                        list[at] = w with { PerHundredPeoplePerPoint = Amount.FromHundredths(n) };
                        return rules with { Scholar = rules.Scholar with { Effects = list } };
                    }));
                }
            }
        }
    }

    private static IEnumerable<int> Numbers(long v) =>
        new[] { 0L, v / 2, (v * 2) + 1, v * 10, 1_000_000L }.Where(n => n != v).Select(n => (int)n).Distinct();

    private static T Set<T>(T record, PropertyInfo property, object value)
        where T : class
    {
        var copy = (T)typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(record, null)!;
        property.SetValue(copy, value);
        return copy;
    }

    // The same content with other AI rules (a shallow copy: everything else is shared and read only).
    private static GameContent With(GameContent content, AiRules rules) =>
        Set(content, typeof(GameContent).GetProperty(nameof(GameContent.Ai))!, rules);

    // Every decision the AI takes in every situation, as text: who decided what about which subject and target, and
    // the strategy it set. With `turns` = 0 the AI decides once in each situation, twice in fact: as it stands, and
    // with every civilization choosing research and reviewing its relations (as after a declaration of war). With more
    // turns the civilizations live on, and their opinions of each other count too.
    private static string Fingerprint(GameContent content, Action<GameState>? change, int turns = 0) =>
        string.Concat(Enumerable.Range(0, Situations.Value.Length).Select(k => Fingerprint(content, change, turns, k)));

    // The fingerprint of one situation.
    private static string Fingerprint(GameContent content, Action<GameState>? change, int turns, int situation)
    {
        var text = new StringBuilder();
        byte[] saved = Situations.Value[situation];
        if (turns == 0)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                GameState state = SaveGameSerializer.Load(new MemoryStream(saved));
                change?.Invoke(state);
                if (pass == 1)
                {
                    Force(state);
                }

                var council = new AiCouncil(content);
                var queue = new CommandQueue();
                council.IssueCommands(state, queue);
                Write(text, council, queue, state);
            }

            return text.ToString();
        }

        GameState lived = SaveGameSerializer.Load(new MemoryStream(saved));
        change?.Invoke(lived);
        Force(lived);

        // A wrong done in every situation: the second people attacked a camp of the first.
        CivSim.Core.Diplomacy.Contacts.RecordAttack(content, lived, lived.Civilizations[0].Id, lived.Civilizations[1].Id);
        var runner = new TurnRunner(lived);
        AiCouncil ai = CoreSystems.Register(runner, content);
        for (int t = 0; t < turns; t++)
        {
            runner.RunTurn();
            Write(text, ai, null, lived);
        }

        foreach (CivilizationState civ in lived.Civilizations)
        {
            foreach (CivSim.Core.Diplomacy.Relation r in civ.Relations)
            {
                text.Append(civ.Id.Value).Append(" sees ").Append(r.Other.Value).Append(' ').Append(r.Sympathy).Append(' ').Append(r.Trust);
                foreach (CivSim.Core.Diplomacy.RelationMemory m in r.Memories)
                {
                    // How long a wrong is remembered.
                    text.Append(' ').Append(m.Kind).Append(':').Append(m.FadeMonths);
                }

                text.Append('\n');
            }
        }

        return text.ToString();
    }

    // Every civilization chooses research, reviews its relations (as after a declaration of war) and, unless the test
    // holds the strategist back, its strategy now.
    private static void Force(GameState state)
    {
        foreach (CivilizationState civ in state.Civilizations)
        {
            civ.Researching = CivilizationState.NoResearch;
            civ.Alarms |= Alarm.WarDeclared;
            if (civ.StrategyReviewMonth != long.MaxValue)
            {
                civ.StrategyReviewMonth = 0;
            }
        }
    }

    private static void Write(StringBuilder text, AiCouncil council, CommandQueue? queue, GameState state)
    {
        foreach (Decision d in council.Log.Entries)
        {
            text.Append(d.Civ.Value).Append(' ').Append(d.Advisor).Append(' ').Append(d.Action).Append(' ')
                .Append(d.Subject).Append(' ').Append(d.Target).Append('\n');
        }

        if (queue is not null)
        {
            var commands = new List<CommandEnvelope>();
            queue.DrainSorted(state, state.Turn, commands);
            foreach (CommandEnvelope c in commands)
            {
                if (c.Command is SetStrategyCommand s)
                {
                    text.Append("strategy ").Append(string.Join(',', s.Weights)).Append(' ').Append(s.Target).Append(' ').Append(s.ReviewMonth).Append('\n');
                }
            }
        }

        text.Append("--\n");
    }

    // Saved states in which the AI has many choices: Stone Age camps early and later, and Neolithic villages with
    // pottery, bread and trade partners.
    private static byte[][] MakeSituations()
    {
        var saved = new List<byte[]>();
        Record(saved, 11, [1, 3, 5, 8, 11, 14, 18], neolithic: false, extremes: false);
        Record(saved, 12, [1, 4, 8, 12, 16, 20], neolithic: true, extremes: false);
        Record(saved, 13, [2, 6, 10, 15, 20], neolithic: true, extremes: true);

        // Rare situations made on purpose from Stone Age camps: a hungry camp in a poor valley that knows rich land far
        // away (a journey), a hungry camp next to a weak foreign camp (driving it out), an idle camp next to goods left
        // in an empty camp (plunder).
        var wide = new List<byte[]>();
        Record(wide, 14, [3, 6, 9], neolithic: false, extremes: false, width: 130, height: 80, peoples: 10);
        int stone = saved.Count;
        for (int k = 0; k < wide.Count; k++)
        {
            GameState state = SaveGameSerializer.Load(new MemoryStream(wide[k]));
            Craft(state, k);
            using var ms = new MemoryStream();
            SaveGameSerializer.Save(state, ms);
            saved.Add(ms.ToArray());
        }

        // A market: villages of the Neolithic peoples with flowing trade hold grain, hides and wood, so bread for the
        // table competes with clothing for the partner.
        GameState market = SaveGameSerializer.Load(new MemoryStream(saved[stone - 1]));
        Market(market);
        using (var ms = new MemoryStream())
        {
            SaveGameSerializer.Save(market, ms);
            saved.Add(ms.ToArray());
        }

        // A war (stage 4e PR 5): a people with a strong village sees a weaker enemy village nearby, and the enemy's army
        // stands next to the strong village; the commanders of both decide.
        GameState war = SaveGameSerializer.Load(new MemoryStream(saved[stone - 1]));
        War(war);
        using (var ms = new MemoryStream())
        {
            SaveGameSerializer.Save(war, ms);
            saved.Add(ms.ToArray());
        }

        // Stone Age hunters who could learn tanning (stage 4F: technologies need their people).
        Record(saved, 15, [1, 4], neolithic: false, extremes: false, width: 130, height: 80, peoples: 10, hunters: true);

        Assert.True(saved.Count > stone);
        return [.. saved];
    }

    private static void Craft(GameState state, int shift)
    {
        // Home camps (the first camp of each people) are never moved or emptied for another people's situation, and no
        // camp is used for two situations.
        var homes = state.Civilizations.Select(c => state.Settlements.FirstOrDefault(x => x.Owner == c.Id && x.IsActive)).Where(x => x is not null).ToHashSet();
        WorldState earth = state.Worlds[0];
        CivSim.Core.Map.HexGlobe geo = earth.Geometry;
        int stamp = (int)(state.Month / 12) + 1;
        int reach = Content.Population.MaxMoveCells;
        for (int c = 0; c < state.Civilizations.Count; c++)
        {
            CivilizationState civ = state.Civilizations[c];
            SettlementState[] camps = state.Settlements.Where(x => x.Owner == civ.Id && x.IsActive).ToArray();
            if (!civ.IsAlive || camps.Length == 0)
            {
                continue;
            }

            SettlementState home = camps[0];
            home.MovedMonth = -1_000_000;
            int role = (c + shift) % 5;

            switch (role)
            {
                case 0:
                    // A journey: the land within one move is poor, rich land farther away is known.
                    Hungry(home);
                    Array.Fill(civ.SeenYear, stamp);
                    var near = new CivSim.Core.Map.HexCoord[CivSim.Core.Map.HexGlobe.RangeSize(reach + 3)];
                    int n = geo.Range(home.Cell, reach + 3, near);
                    for (int k = 0; k < n; k++)
                    {
                        int i = geo.Index(near[k]);
                        earth.Map.WildFood.Set(i, 30);
                    }

                    break;
                case 1:
                    // Driving out: a small foreign camp a few cells away holds the only good land around, seen now.
                    Hungry(home);
                    SettlementState? foreign = state.Settlements.FirstOrDefault(x => x.IsActive && x.Owner != civ.Id && !homes.Contains(x));
                    if (foreign is null && FreeLandNear(state, home.Cell, 5, out CivSim.Core.Map.HexCoord outpost))
                    {
                        // Peoples grow slowly (stage 4F), so a few turns in nobody may have split yet: a neighbour sends
                        // out a small group.
                        CivilizationState? other = state.Civilizations.FirstOrDefault(x => x.IsAlive && x.Id != civ.Id);
                        foreign = other is null
                            ? null
                            : state.FoundSettlement(other.Id, earth.Id, outpost, "Outpost", Content.NewTribeLevel, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
                    }

                    if (foreign is not null && homes.Add(foreign) && FreeLandNear(state, home.Cell, 5, out CivSim.Core.Map.HexCoord spot))
                    {
                        var poor = new CivSim.Core.Map.HexCoord[CivSim.Core.Map.HexGlobe.RangeSize(reach + 3)];
                        int np = geo.Range(home.Cell, reach + 3, poor);
                        for (int k = 0; k < np; k++)
                        {
                            int i = geo.Index(poor[k]);
                            if (geo.Distance(poor[k], spot) > 2)
                            {
                                earth.Map.WildFood.Set(i, (ushort)(earth.Map.WildFood[i] / 10));
                            }
                        }

                        foreign.Cell = spot;
                        Span<int> people = foreign.Population;
                        people.Clear();
                        people[0] = 4; // a handful of gatherers

                        civ.SeenYear[geo.Index(spot)] = stamp;
                        (civ.Relation(foreign.Owner) ?? civ.AddRelation(foreign.Owner)).Sympathy = -5000; // an old grudge
                    }

                    break;
                case 3:
                    // Hungry, not starving, in a poor spot: whether it looks around now depends on its search rhythm.
                    home.FoodNeeded = Math.Max(100, home.FoodNeeded);
                    home.FoodEaten = home.FoodNeeded * 9 / 10;
                    var spot3 = new CivSim.Core.Map.HexCoord[CivSim.Core.Map.HexGlobe.RangeSize(3)];
                    int n3 = geo.Range(home.Cell, 3, spot3);
                    for (int k = 0; k < n3; k++)
                    {
                        int i = geo.Index(spot3[k]);
                        earth.Map.WildFood.Set(i, (ushort)(earth.Map.WildFood[i] / 10));
                    }

                    break;
                case 4:
                    // The most expansive people at the highest expansion weight, its camp 45% full: only the lower limit
                    // of splitting keeps it from sending a group out.
                    civ.Traits[(int)Trait.Expansiveness] = 1000;
                    civ.GoalWeights = Enumerable.Repeat(1000, Character.GoalCount).ToArray();
                    civ.GoalWeights[(int)Goal.Expansion] = 1600;
                    civ.StrategyEra = civ.Era;
                    civ.StrategyReviewMonth = long.MaxValue;
                    home.FoodEaten = home.FoodNeeded;
                    int room = Content.SettlementLevels[home.Level].MaxPopulation * 45 / 100;
                    Span<int> crowd4 = home.Population;
                    int total = home.TotalPopulation();
                    for (int k = 0; k < crowd4.Length; k++)
                    {
                        crowd4[k] = total == 0 ? 0 : crowd4[k] * room / total;
                    }

                    break;
                default:
                    // Plunder: an empty camp with goods two cells away; the home camp is fed and its stores are low.
                    home.FoodEaten = home.FoodNeeded;
                    home.Stock.Clear();
                    Span<int> crowd = home.Population;
                    while (home.TotalPopulation() > 40)
                    {
                        for (int k = 0; k < crowd.Length; k++)
                        {
                            crowd[k] -= crowd[k] / 4;
                        }
                    }

                    SettlementState? left = camps.Length > 1 ? camps[1] : state.Settlements.FirstOrDefault(x => x.IsActive && x.Owner != civ.Id && !homes.Contains(x));
                    if (left is not null && homes.Add(left) && FreeLandNear(state, home.Cell, 2, out CivSim.Core.Map.HexCoord site))
                    {
                        left.Owner = civ.Id; // an old camp of its own
                        left.Cell = site;
                        left.Status = SettlementStatus.Abandoned;
                        left.Population.Clear();
                        left.Add(0, Amount.FromUnits(40));
                    }

                    break;
            }
        }
    }

    private static void Market(GameState state)
    {
        int grain = Content.Good("grain").Id;
        int hides = Content.Good("hides").Id;
        int wood = Content.Good("wood").Id;
        int clothing = Content.Good("hide_clothing").Id;
        foreach (CivilizationState civ in state.Civilizations)
        {
            // Every step of the Neolithic peoples plans production now.
            // A step of a whole settlers' period, so every village is due to send settlers (G7) whatever its stagger.
            civ.StepMonths = Math.Max(civ.StepMonths, CivSim.Core.Simulation.WorldClock.RhythmMonths(Content, state, civ));
            civ.StepMonths = Math.Max(civ.StepMonths, Content.Ai.VillageSettlersEveryYears * 12);
            civ.Traits[(int)Trait.Tradefulness] = 500;
            foreach (SettlementState s in state.Settlements.Where(x => x.Owner == civ.Id && x.IsActive))
            {
                s.RecipeId = SettlementState.NoRecipe;

                // Artisans who can both sew and bake (stage 4F skills), now sewing what is already plentiful: bread is
                // the challenger, and whether they switch depends on the hysteresis.
                int artisans = Content.Strata.First(x => x.Crafts).Id;
                int tanner = Content.Skills.First(x => x.Key == "tanner").Id;
                int baker = Content.Skills.First(x => x.Key == "baker").Id;
                if (civ.Knows(Content.Skills[tanner].TechnologyId) && civ.Knows(Content.Skills[baker].TechnologyId) && s.PopulationView[artisans] >= 2)
                {
                    s.Skilled.Clear();
                    s.Skilled[tanner] = s.PopulationView[artisans] / 2;
                    s.Skilled[baker] = s.PopulationView[artisans] / 2;
                    s.RecipeId = Content.Recipes.First(x => x.Key == "sew_hide_clothing").Id;
                }

                for (int g = 0; g < Content.Goods.Count; g++)
                {
                    if (Content.Goods[g].IsFood)
                    {
                        s.Stock[g] = default; // an empty larder
                    }
                }

                s.Add(clothing, Amount.FromUnits(200)); // and plenty of clothing already

                // Villages 70% full: whether they send settlers depends on the threshold and the civilization's drive.
                if (!Content.SettlementLevels[s.Level].Mobile)
                {
                    int room = Content.SettlementLevels[s.Level].MaxPopulation * 70 / 100;
                    int total = s.TotalPopulation();
                    Span<int> crowd = s.Population;
                    for (int k = 0; k < crowd.Length; k++)
                    {
                        crowd[k] = total == 0 ? 0 : (int)((long)crowd[k] * room / total);
                    }
                }
                s.Add(grain, Amount.FromUnits(60));
                s.Add(hides, Amount.FromUnits(60));
                s.Add(wood, Amount.FromUnits(60));
            }
        }
    }

    private static void War(GameState state)
    {
        CivilizationState[] alive = state.Civilizations.Where(c => c.IsAlive && state.Settlements.Any(x => x.Owner == c.Id && x.IsActive)).ToArray();
        CivilizationState a = alive[0];
        CivilizationState b = alive[1];
        int warriors = Content.Strata.Single(x => x.Key == "warriors").Id;
        int grain = Content.Good("grain").Id;
        SettlementState strong = state.Settlements.First(x => x.Owner == a.Id && x.IsActive);
        strong.Population[warriors] = 60;
        strong.Stock[grain] = Amount.FromUnits(500);
        strong.Stock[Content.Good("stone_tools").Id] = Amount.FromUnits(50);
        Assert.True(FreeLandNear(state, strong.Cell, 5, out CivSim.Core.Map.HexCoord at));
        SettlementState weak = state.FoundSettlement(b.Id, state.Worlds[0].Id, at, "Weak", Content.SettlementLevel("village").Id, Content.Goods.Count, Content.Strata.Count, Content.Skills.Count);
        weak.Population[Content.Strata[0].Id] = 40;
        weak.Population[warriors] = 30;
        weak.Stock[grain] = Amount.FromUnits(300);
        int stamp = (int)(state.Month / 12) + 1;
        foreach (CivilizationState c in new[] { a, b })
        {
            Array.Fill(c.SeenYear, stamp);
            c.StepStartMonth = Math.Min(c.StepStartMonth, state.Month);
        }

        if (state.Treaties.Find(a.Id, b.Id, CivSim.Core.Diplomacy.TreatyKind.War) is null)
        {
            CivSim.Core.Diplomacy.Treaties.DeclareWar(Content, state, a.Id, b.Id, CivSim.Core.Diplomacy.WarGoal.Loot, -1);
        }

        new CivSim.Core.Episodes.LaunchCampaignCommand(Content, weak.Id, strong.Id, 20).Apply(state, b.Id);
        CivSim.Core.Episodes.Episode army = state.Episodes.All[^1];
        CivSim.Core.Map.HexGlobe geo = state.Worlds[0].Geometry;
        army.Cell = geo.Index(geo.Neighbor(strong.Cell, CivSim.Core.Map.HexDirection.N));
        army.Phase = CivSim.Core.Episodes.CampaignPhase.March;
    }

    private static void Hungry(SettlementState s)
    {
        s.FoodNeeded = Math.Max(100, s.FoodNeeded);
        s.FoodEaten = s.FoodNeeded / 2;
    }

    // A land cell `distance` cells from `center` with no settlement on it.
    private static bool FreeLandNear(GameState state, CivSim.Core.Map.HexCoord center, int distance, out CivSim.Core.Map.HexCoord cell)
    {
        WorldState earth = state.Worlds[0];
        var ring = new CivSim.Core.Map.HexCoord[CivSim.Core.Map.HexGlobe.RangeSize(distance)];
        int n = earth.Geometry.Range(center, distance, ring);
        for (int k = n - 1; k >= 0; k--)
        {
            int i = earth.Geometry.Index(ring[k]);
            CivSim.Core.Map.HexCoord at = ring[k];
            if (!Content.Biomes[earth.Map.Biome[i]].IsWater && !state.Settlements.Any(x => x.Cell == at))
            {
                cell = at;
                return true;
            }
        }

        cell = center;
        return false;
    }

    private static void Record(List<byte[]> saved, ulong seed, int[] turns, bool neolithic, bool extremes, int width = 66, int height = 40, int peoples = 8, bool hunters = false)
    {
        // A crowded world (8 peoples on the test map, more on a wider one) where everybody has met everybody: trade,
        // pacts, wars and camps next to foreign camps happen within a few turns.
        GameState state = GameState.CreateNew(new GameSettings(seed, width, height, peoples), Content, out _);
        foreach (CivilizationState a in state.Civilizations)
        {
            foreach (CivilizationState b in state.Civilizations)
            {
                if (a.Id != b.Id)
                {
                    CivSim.Core.Diplomacy.Contacts.Meet(Content, state, a, b.Id, CivSim.Core.Diplomacy.ContactWay.Sighted);
                }
            }
        }

        if (extremes)
        {
            // Characters at the ends of the scale: the most expansive, aggressive, cautious... next to the least.
            foreach (CivilizationState civ in state.Civilizations)
            {
                Array.Fill(civ.Traits, civ.Id.Value % 2 == 0 ? 1000 : 0);
            }
        }

        if (neolithic)
        {
            // Every civilization starts with the Stone Age technologies and agriculture, so villages, pottery and bread come.
            foreach (CivilizationState civ in state.Civilizations)
            {
                Technologies.EnsureSize(Content, civ);
                foreach (TechnologyDefinition t in Content.Technologies)
                {
                    // Half of the peoples know the whole Neolithic too: the others lag behind and trade for cloth.
                    if (t.Era == 1 || t.Key == "agriculture" || (civ.Id.Value % 2 == 0 && t.Era == 2))
                    {
                        Technologies.Learn(Content, state, civ, t.Id);
                    }
                }
            }
        }

        if (hunters)
        {
            // Stone Age hunters (stage 4F): peoples who know fire, flint and cooperative hunting and have the hunters that
            // tanning needs, so clothing against the cold competes with herds in the choice of the scholar.
            foreach (CivilizationState civ in state.Civilizations)
            {
                foreach (string key in new[] { "fire", "flint_knapping", "cooperative_hunting" })
                {
                    Technologies.Learn(Content, state, civ, Content.Technology(key).Id);
                }
            }

            int hunter = Content.Skills.First(x => x.Key == "hunter").Id;
            foreach (SettlementState s in state.Settlements)
            {
                s.Skilled[hunter] = Math.Min(12, s.PopulationView[Content.Skills[hunter].StratumId]);
            }

            // The first people live in a cold valley (winter below the comfort limit), where fire and clothing count.
            SettlementState cold = state.Settlements.First(x => x.Owner == state.Civilizations[0].Id);
            int cell = state.Worlds[0].Geometry.Index(cold.Cell);
            state.Worlds[0].Map.Temperature.Set(cell, (sbyte)(Content.Population.ComfortColdBelow - 8 + (state.Worlds[0].Map.Seasonality[cell] / 2)));
        }

        if (neolithic)
        {
            // Trade partners whose goods already flow: production for exchange counts.
            for (int c = 0; c + 1 < state.Civilizations.Count; c += 2)
            {
                CivSim.Core.Diplomacy.Treaty trade = state.Treaties.Add(new CivSim.Core.Diplomacy.Treaty(
                    state.Civilizations[c].Id, state.Civilizations[c + 1].Id, CivSim.Core.Diplomacy.TreatyKind.Trade, state.Civilizations[c].Id, 0));
                trade.LastFlowMonth = 0;

                // And a non-aggression pact, so that a defensive alliance (and with it loyalty) is on the table.
                state.Treaties.Add(new CivSim.Core.Diplomacy.Treaty(
                    state.Civilizations[c].Id, state.Civilizations[c + 1].Id, CivSim.Core.Diplomacy.TreatyKind.NonAggression, state.Civilizations[c].Id, 0));
            }
        }

        var runner = new TurnRunner(state);
        CoreSystems.Register(runner, Content);
        for (int t = 0; t <= turns.Max(); t++)
        {
            if (turns.Contains(t))
            {
                using var ms = new MemoryStream();
                SaveGameSerializer.Save(state, ms);
                saved.Add(ms.ToArray());
            }

            runner.RunTurn();
        }
    }
}
