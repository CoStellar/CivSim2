using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using CivSim.Core.Content;
using CivSim.Core.Numerics;

namespace CivSim.Data;

/// <summary>
/// Loads and validates game content from the <c>data/</c> folder. Every problem is collected with its file and
/// position, so a broken data file gives a readable list instead of a crash deep inside the simulation.
/// </summary>
public static class ContentLoader
{
    /// <summary>Name of the data folder next to the executable.</summary>
    public const string DefaultFolderName = "data";

    /// <summary>The data folder copied next to the running program.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, DefaultFolderName);

    /// <summary>Loads content from <paramref name="directory"/>. Throws <see cref="ContentException"/> with all errors.</summary>
    public static GameContent Load(string? directory = null)
    {
        directory ??= DefaultDirectory;
        var errors = new List<string>();
        List<GoodDefinition> goods = LoadGoods(Path.Combine(directory, "goods.json"), errors);
        List<BiomeDefinition> biomes = LoadBiomes(Path.Combine(directory, "biomes.json"), goods, errors);
        List<DepositDefinition> deposits = LoadDeposits(Path.Combine(directory, "resources.json"), biomes, goods, errors);
        ValidateBiomeSet(biomes, errors);
        List<StratumDefinition> strata = LoadStrata(Path.Combine(directory, "strata.json"), goods, errors);
        PopulationRules population = LoadPopulation(Path.Combine(directory, "population.json"), strata, goods, errors);
        List<RecipeDefinition> recipes = LoadRecipes(Path.Combine(directory, "recipes.json"), goods, errors);
        (List<SettlementLevelDefinition> levels, int tribeLevel, List<GoodQuantity> tribeStock, AssetRules assets) =
            LoadSettlements(Path.Combine(directory, "settlements.json"), goods, errors);
        List<EraDefinition> eras = LoadEras(Path.Combine(directory, "eras.json"), errors);
        List<EffectDefinition> effects = LoadEffects(Path.Combine(directory, "effects.json"), errors);
        TradeRules trade = LoadTrade(Path.Combine(directory, "trade.json"), errors);
        PerceptionRules perception = LoadPerception(Path.Combine(directory, "perception.json"), errors);
        AiRules ai = LoadAi(Path.Combine(directory, "ai.json"), errors);
        Core.Diplomacy.RelationRules relations = LoadRelations(Path.Combine(directory, "relations.json"), errors);
        Core.Naming.NameRules names = LoadNames(Path.Combine(directory, "names.json"), errors);
        (List<TechnologyDefinition> technologies, ResearchRules research) =
            LoadTechnologies(Path.Combine(directory, "technologies.json"), goods, biomes, deposits, recipes, effects, errors);
        ResolveEraThresholds(Path.Combine(directory, "eras.json"), eras, technologies, errors);
        (List<SkillDefinition> skills, SkillRules skillRules) = LoadSkills(Path.Combine(directory, "skills.json"), strata, technologies, errors);
        SkillLinks skillLinks = LinkSkills(skills, technologies, recipes.Count, errors);
        LoadPeopleRequirements(Path.Combine(directory, "technologies.json"), technologies, skills, strata, errors);
        List<EventDefinition> events = LoadEvents(Path.Combine(directory, "events.json"), biomes, effects, technologies, errors);
        Core.Diplomacy.TreatyRules treaties = LoadTreaties(Path.Combine(directory, "diplomacy.json"), technologies, errors);
        Core.Diplomacy.WarRules war = LoadWar(Path.Combine(directory, "war.json"), goods, technologies, biomes, errors);
        List<GovernmentDefinition> governments = LoadGovernments(Path.Combine(directory, "governments.json"), eras, strata, errors);
        StabilityRules stability = LoadStability(Path.Combine(directory, "stability.json"), effects, errors);
        LeaderRules leaders = LoadLeaders(Path.Combine(directory, "leaders.json"), errors);
        Core.Simulation.TimeRules time = LoadTime(Path.Combine(directory, "time.json"), errors);
        Core.Units.UnitRules units = LoadUnits(Path.Combine(directory, "units.json"), biomes, errors);
        for (int l = 0; l < levels.Count; l++)
        {
            if (levels[l].RequiresKey.Length > 0)
            {
                int tech = technologies.FindIndex(t => t.Key == levels[l].RequiresKey);
                if (tech < 0)
                {
                    errors.Add($"settlements.json: levels[{l}].requires: unknown technology '{levels[l].RequiresKey}'.");
                }

                levels[l] = levels[l] with { RequiresTechnology = tech };
            }
        }
        (List<PlanetPreset> planets, Core.WorldGen.SeasonRules seasons, Core.WorldGen.ClimateCycleRules cycles) = LoadPlanets(Path.Combine(directory, "planets.json"), errors);
        if (errors.Count > 0)
        {
            throw new ContentException(errors);
        }

        return new GameContent(biomes, deposits)
        {
            Goods = goods,
            Recipes = recipes,
            SettlementLevels = levels,
            Eras = eras,
            Effects = effects,
            NewTribeLevel = tribeLevel,
            NewTribeStock = tribeStock,
            Strata = strata,
            Skills = skills,
            SkillRules = skillRules,
            SkillLinks = skillLinks,
            Population = population,
            Assets = assets,
            Planets = planets,
            Seasons = seasons,
            ClimateCycles = cycles,
            Trade = trade,
            Perception = perception,
            Ai = ai,
            Relations = relations,
            Names = names,
            Treaties = treaties,
            War = war,
            Governments = governments,
            Leaders = leaders,
            Time = time,
            Units = units,
            Stability = stability,
            Events = events,
            Technologies = technologies,
            Research = research,
        };
    }

    // Skills of data/skills.json (stage 4F, ADR 0020): each belongs to a stratum and is opened by a technology.
    private static (List<SkillDefinition> Skills, SkillRules Rules) LoadSkills(string path, List<StratumDefinition> strata, List<TechnologyDefinition> technologies, List<string> errors)
    {
        var result = new List<SkillDefinition>();
        var rules = new SkillRules();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "skills", path, errors, out JsonElement array))
        {
            return (result, rules);
        }

        string file = Path.GetFileName(path);
        if (doc.RootElement.TryGetProperty("rules", out JsonElement re) && re.ValueKind == JsonValueKind.Object)
        {
            var rr = new Reader(re, $"{file}: rules", errors);
            rules = new SkillRules
            {
                FirstMasters = rr.Int("firstMasters", 1, 1000),
                ApprenticesPerMaster = rr.Int("apprenticesPerMaster", 0, 100),
                ApprenticeWorkPercent = rr.Int("apprenticeWorkPercent", 0, 100),
                MoveMinMasters = rr.Int("moveMinMasters", 1, 1000),
                MoveSpareAbove = rr.Int("moveSpareAbove", 0, 1000),
                MoveSharePercent = rr.Int("moveSharePercent", 0, 100),
                TeachForOthersPercent = rr.Int("teachForOthersPercent", 0, 1000),
            };
        }
        else
        {
            errors.Add($"{file}: rules: required object.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{file}: skills[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            string stratumKey = r.String("stratum");
            int stratum = strata.FindIndex(x => x.Key == stratumKey);
            if (stratumKey.Length > 0 && stratum < 0)
            {
                errors.Add($"{at}.stratum: unknown stratum '{stratumKey}'.");
            }

            string techKey = r.String("technology");
            int tech = technologies.FindIndex(x => x.Key == techKey);
            if (techKey.Length > 0 && tech < 0)
            {
                errors.Add($"{at}.technology: unknown technology '{techKey}'.");
            }

            int years = r.Int("trainingYears", 0, 100);
            int jobs = r.Int("jobsPercent", 1, 100);
            result.Add(new SkillDefinition(i, key, name, stratum, tech, years) { JobsPercent = jobs });
            i++;
        }

        if (re.ValueKind == JsonValueKind.Object && re.TryGetProperty("farmingSkill", out JsonElement fs) && fs.ValueKind == JsonValueKind.String)
        {
            int farming = result.FindIndex(x => x.Key == fs.GetString());
            if (farming < 0)
            {
                errors.Add($"{file}: rules.farmingSkill: unknown skill '{fs.GetString()}'.");
            }

            rules = rules with { FarmingSkill = farming };
        }

        return (result, rules);
    }

    // People a technology needs (stage 4F, ADR 0020): read after the skills, which come after the technologies.
    private static void LoadPeopleRequirements(string path, List<TechnologyDefinition> technologies, List<SkillDefinition> skills, List<StratumDefinition> strata, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "technologies", path, errors, out JsonElement array))
        {
            return;
        }

        string file = Path.GetFileName(path);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            if (i < technologies.Count)
            {
                technologies[i] = technologies[i] with
                {
                    RequiresPeople = ReadPeople(e, "requiresPeople", $"{file}: technologies[{i}].requiresPeople"),
                    RequiresPeopleAny = ReadPeople(e, "requiresPeopleAny", $"{file}: technologies[{i}].requiresPeopleAny"),
                };
            }

            i++;
        }

        // The people a technology needs come from earlier technologies: a skill must be opened by one of the technologies
        // it requires, directly or through others. Anything else is a loop or a step the tree does not show.
        for (int t = 0; t < technologies.Count; t++)
        {
            HashSet<int> earlier = Earlier(technologies, t);
            CheckEarlier(technologies[t].RequiresPeople, "requiresPeople");
            CheckEarlier(technologies[t].RequiresPeopleAny, "requiresPeopleAny");

            void CheckEarlier(IReadOnlyList<PeopleRequirement> people, string name)
            {
                for (int n = 0; n < people.Count; n++)
                {
                    int skill = people[n].SkillId;
                    if (skill < 0 || skill >= skills.Count || skills[skill].TechnologyId < 0)
                    {
                        continue;
                    }

                    int from = skills[skill].TechnologyId;
                    if (!earlier.Contains(from))
                    {
                        errors.Add($"{file}: technologies[{t}].{name}[{n}]: skill '{skills[skill].Key}' comes with '{technologies[from].Key}', which '{technologies[t].Key}' does not require (directly or through other technologies).");
                    }
                }
            }
        }

        List<PeopleRequirement> ReadPeople(JsonElement tech, string name, string at)
        {
            var list = new List<PeopleRequirement>();
            if (!tech.TryGetProperty(name, out JsonElement items))
            {
                return list;
            }

            if (items.ValueKind != JsonValueKind.Array)
            {
                errors.Add($"{at}: must be a list.");
                return list;
            }

            int n = 0;
            foreach (JsonElement item in items.EnumerateArray())
            {
                string where = $"{at}[{n}]";
                var r = new Reader(item, where, errors);
                int skill = -1;
                int stratum = -1;
                if (item.TryGetProperty("skill", out JsonElement sk) && sk.ValueKind == JsonValueKind.String)
                {
                    skill = skills.FindIndex(x => x.Key == sk.GetString());
                    if (skill < 0)
                    {
                        errors.Add($"{where}.skill: unknown skill '{sk.GetString()}'.");
                    }
                }
                else if (item.TryGetProperty("stratum", out JsonElement st) && st.ValueKind == JsonValueKind.String)
                {
                    stratum = strata.FindIndex(x => x.Key == st.GetString());
                    if (stratum < 0)
                    {
                        errors.Add($"{where}.stratum: unknown stratum '{st.GetString()}'.");
                    }
                }
                else
                {
                    errors.Add($"{where}: needs a skill or a stratum.");
                }

                int count = r.Int("count", 1, 1_000_000);
                bool one = item.TryGetProperty("inOneSettlement", out JsonElement o) && o.ValueKind == JsonValueKind.True;
                list.Add(new PeopleRequirement(skill, stratum, count, one));
                n++;
            }

            return list;
        }
    }

    // Every technology that must come before `id` (requires and requiresAny, through other technologies too).
    private static HashSet<int> Earlier(List<TechnologyDefinition> techs, int id)
    {
        var seen = new HashSet<int>();
        var todo = new Stack<int>();
        todo.Push(id);
        while (todo.Count > 0)
        {
            TechnologyDefinition t = techs[todo.Pop()];
            foreach (int r in t.Requires.Concat(t.RequiresAny))
            {
                if (r >= 0 && r < techs.Count && seen.Add(r))
                {
                    todo.Push(r);
                }
            }
        }

        return seen;
    }

    // Each technology opens at most one skill; the skill works the technology's recipes and its gathering.
    private static SkillLinks LinkSkills(List<SkillDefinition> skills, List<TechnologyDefinition> technologies, int recipeCount, List<string> errors)
    {
        var ofTech = Enumerable.Repeat(-1, technologies.Count).ToArray();
        var ofRecipe = Enumerable.Repeat(-1, recipeCount).ToArray();
        foreach (SkillDefinition k in skills)
        {
            if (k.TechnologyId < 0)
            {
                continue;
            }

            if (ofTech[k.TechnologyId] >= 0)
            {
                errors.Add($"skills.json: skills '{skills[ofTech[k.TechnologyId]].Key}' and '{k.Key}' both come with technology '{technologies[k.TechnologyId].Key}'.");
                continue;
            }

            ofTech[k.TechnologyId] = k.Id;
            foreach (int r in technologies[k.TechnologyId].Recipes)
            {
                if (r >= 0 && r < recipeCount)
                {
                    ofRecipe[r] = k.Id;
                }
            }
        }

        return new SkillLinks(ofRecipe, ofTech);
    }

    private static List<StratumDefinition> LoadStrata(string path, List<GoodDefinition> goods, List<string> errors)
    {
        var result = new List<StratumDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "strata", path, errors, out JsonElement array))
        {
            return result;
        }

        string file = Path.GetFileName(path);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        int shares = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{file}: strata[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            bool gathers = r.Bool("gathers");
            int share = (int)r.Amount("targetShare", Amount.Zero, Amount.FromUnits(100)).WholeUnits;
            shares += share;
            var needs = new List<NeedDefinition>();
            if (e.TryGetProperty("needs", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
            {
                int n = 0;
                foreach (JsonElement ne in list.EnumerateArray())
                {
                    needs.Add(ReadNeed(new Reader(ne, $"{at}.needs[{n}]", errors), ne, goods, errors, $"{at}.needs[{n}]"));
                    n++;
                }
            }

            if (!needs.Exists(x => x.Basic && x.AnyOfCategory && x.Category == GoodCategory.Food))
            {
                errors.Add($"{at}.needs: a basic need for the food category is required.");
            }

            bool fights = e.TryGetProperty("fights", out JsonElement f) && f.ValueKind == JsonValueKind.True;
            bool crafts = e.TryGetProperty("crafts", out JsonElement cr) && cr.ValueKind == JsonValueKind.True;
            int science = e.TryGetProperty("sciencePerThousandPerYear", out _) ? r.Int("sciencePerThousandPerYear", 0, 1_000_000) : 0;
            result.Add(new StratumDefinition(i, key, name, gathers, share, needs) { Fights = fights, Crafts = crafts, SciencePerThousandPerYear = science });
            i++;
        }

        if (result.Count == 0)
        {
            errors.Add($"{file}: at least one stratum is required.");
        }
        else if (shares != 100)
        {
            errors.Add($"{file}: targetShare of all strata must add up to 100, not {shares}.");
        }

        if (!result.Exists(x => x.Gathers))
        {
            errors.Add($"{file}: at least one stratum must gather.");
        }

        return result;
    }

    private static NeedDefinition ReadNeed(Reader r, JsonElement e, List<GoodDefinition> goods, List<string> errors, string at)
    {
        Amount perYear = r.Amount("perYear", Amount.Epsilon, Amount.FromUnits(1000));
        string kind = r.String("kind");
        if (kind is not ("basic" or "higher") && kind.Length > 0)
        {
            errors.Add($"{at}.kind: '{kind}' must be basic or higher.");
        }

        bool basic = kind == "basic";
        if (e.TryGetProperty("good", out JsonElement g) && g.ValueKind == JsonValueKind.String)
        {
            int id = goods.FindIndex(x => x.Key == g.GetString());
            if (id < 0)
            {
                errors.Add($"{at}.good: unknown good '{g.GetString()}'.");
                id = 0;
            }

            return new NeedDefinition(id, goods.Count > 0 ? goods[id].Category : GoodCategory.Food, perYear, basic);
        }

        GoodCategory category = r.Enum<GoodCategory>("category");
        return new NeedDefinition(-1, category, perYear, basic);
    }

    private static PopulationRules LoadPopulation(string path, List<StratumDefinition> strata, List<GoodDefinition> goods, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new PopulationRules();
        }

        string file = Path.GetFileName(path);
        JsonElement root = doc.RootElement;
        JsonElement Section(string name)
        {
            if (root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Object)
            {
                return v;
            }

            errors.Add($"{file}: missing object '{name}'.");
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        var growth = new Reader(Section("growth"), $"{file}: growth", errors);
        var health = new Reader(Section("health"), $"{file}: health", errors);
        var gathering = new Reader(Section("gathering"), $"{file}: gathering", errors);
        var wild = new Reader(Section("wildFood"), $"{file}: wildFood", errors);
        var years = new Reader(Section("goodAndBadYears"), $"{file}: goodAndBadYears", errors);
        var influence = new Reader(Section("influence"), $"{file}: influence", errors);
        var migration = new Reader(Section("migration"), $"{file}: migration", errors);
        var conquest = new Reader(Section("conquest"), $"{file}: conquest", errors);
        var comfort = new Reader(Section("comfort"), $"{file}: comfort", errors);
        var farming = new Reader(Section("farming"), $"{file}: farming", errors);
        var tribe = new int[strata.Count];
        JsonElement tribeElement = Section("newTribe");
        var tribeReader = new Reader(tribeElement, $"{file}: newTribe", errors);
        int total = 0;
        foreach (StratumDefinition s in strata)
        {
            tribe[s.Id] = tribeReader.Int(s.Key, 0, 1_000_000);
            total += tribe[s.Id];
        }

        if (total == 0 && strata.Count > 0)
        {
            errors.Add($"{file}: newTribe must have at least one person.");
        }

        int GoodOfIn(Reader section, string name)
        {
            string key = section.String(name);
            int id = goods.FindIndex(g => g.Key == key);
            if (id < 0 && key.Length > 0)
            {
                errors.Add($"{file}: {name}: unknown good '{key}'.");
            }

            return id;
        }

        int GoodOf(string name)
        {
            string key = gathering.String(name);
            int id = goods.FindIndex(g => g.Key == key);
            if (id < 0 && key.Length > 0)
            {
                errors.Add($"{file}: gathering.{name}: unknown good '{key}'.");
            }

            return id;
        }

        Amount unit = Amount.FromUnits(1);
        return new PopulationRules
        {
            BirthsPerMille = growth.Amount("birthsPerMille", Amount.Zero, Amount.FromUnits(1000)),
            ChildMortalityPercent = growth.Amount("childMortalityPercent", Amount.Zero, Amount.FromUnits(100)),
            AdultDeathsPerMille = growth.Amount("adultDeathsPerMille", Amount.Zero, Amount.FromUnits(1000)),
            VillageDeathsPerMille = growth.Amount("villageDeathsPerMille", Amount.Zero, Amount.FromUnits(1000)),
            VillageBirthsPerMille = growth.Amount("villageBirthsPerMille", Amount.Zero, Amount.FromUnits(1000)),
            StarvationDeathPercent = growth.Int("starvationDeathPercent", 0, 100),
            CampStarvationDeathPercent = growth.Int("campStarvationDeathPercent", 0, 100),
            LowHealthDeathsPerMillePerPoint = growth.Amount("lowHealthDeathsPerMillePerPoint", Amount.Zero, Amount.FromUnits(100)),
            MoodBirthsPerMillePerPoint = growth.Amount("moodBirthsPerMillePerPoint", Amount.Zero, Amount.FromUnits(100)),
            StrataShiftPercentPerYear = growth.Amount("strataShiftPercentPerYear", Amount.Zero, Amount.FromUnits(100)),
            HungerPenaltyPercent = health.Int("hungerPenaltyPercent", 0, 100),
            CrowdingPenaltyPercent = health.Int("crowdingPenaltyPercent", 0, 1000),
            LowHealthBelow = health.Int("lowHealthBelow", 0, 100),
            FoodPerWorkerPerYear = gathering.Amount("foodPerWorkerPerYear", Amount.Epsilon, Amount.FromUnits(1000)),
            FoodPerBiomeFoodPerYear = gathering.Amount("foodPerBiomeFoodPerYear", Amount.Zero, Amount.FromUnits(1000)),
            PlantsPercent = gathering.Int("plantsPercent", 0, 100),
            HidesPerMeat = gathering.Amount("hidesPerMeat", Amount.Zero, unit * 10),
            HillsPercent = gathering.Int("hillsPercent", 0, 100),
            MountainsPercent = gathering.Int("mountainsPercent", 0, 100),
            PlantsGood = GoodOf("plantsGood"),
            MeatGood = GoodOf("meatGood"),
            HidesGood = GoodOf("hidesGood"),
            GrainGood = GoodOfIn(farming, "grainGood"),
            GrainPerFarmerPerYear = farming.Amount("grainPerFarmerPerYear", Amount.Zero, Amount.FromUnits(1000)),
            FarmersPerCell = farming.Int("farmersPerCell", 1, 100_000),
            MinFarmFertility = farming.Int("minFertility", 0, 100),
            ExhaustionHundredthsPerYear = (int)farming.Amount("exhaustionPerYear", Amount.Zero, Amount.FromUnits(100)).Hundredths,
            RecoveryHundredthsPerYear = (int)farming.Amount("recoveryPerYear", Amount.Zero, Amount.FromUnits(100)).Hundredths,
            SurvivableDeadSeasonHundredths = (int)gathering.Amount("survivableDeadSeasonYears", Amount.FromHundredths(1), Amount.FromUnits(1000)).Hundredths,
            WildDepletionPerMillePerYear = wild.Int("depletionPerMillePerYear", 0, 1000),
            WildRegrowthBasisPointsPerYear = (int)wild.Amount("regrowthPercentPerYear", Amount.Zero, Amount.FromUnits(100)).Hundredths,
            YieldSwingPercent = years.Int("yieldSwingPercent", 0, 90),
            InfluenceBaseRange = influence.Int("baseRange", 0, 20),
            WarriorsPerRange = influence.Int("warriorsPerRange", 1, 100_000),
            InfluenceMaxRange = influence.Int("maxRange", 0, 20),
            HillsCost = influence.Int("hillsCost", 0, 10),
            MountainsCost = influence.Int("mountainsCost", 0, 10),
            InfluencePerStep = influence.Int("strengthPerStep", 1, 255),
            InfluenceHomeCost = influence.Int("homeCost", 0, 20),
            MaxMoveCells = migration.Int("maxMoveCells", 0, 50),
            MoveWhenFedBelowPercent = migration.Int("moveWhenFedBelowPercent", 0, 100),
            MoveWhenBetterByPercent = migration.Int("moveWhenBetterByPercent", 0, 1000),
            MinStepsBetweenMoves = migration.Int("minStepsBetweenMoves", 0, 1000),
            ComfortColdBelow = comfort.Int("coldBelow", -60, 60),
            ComfortHotAbove = comfort.Int("hotAbove", -60, 80),
            ComfortPercentPerDegree = comfort.Int("percentPerDegree", 0, 100),
            ComfortMinPercent = comfort.Int("minPercent", 0, 100),
            DistancePenaltyPercentPerCell = migration.Int("distancePenaltyPercentPerCell", 0, 100),
            SplitAbovePercentOfLevel = migration.Int("splitAbovePercentOfLevel", 1, 1000),
            SplitPercent = migration.Int("splitPercent", 1, 90),
            MinSplitPeople = migration.Int("minSplitPeople", 2, 1_000_000),
            MinDistanceBetweenOwnCamps = migration.Int("minDistanceBetweenOwnCamps", 1, 50),
            SplitNeedsFoodPercent = migration.Int("splitNeedsFoodPercent", 0, 1000),
            MergeBelowPeople = migration.Int("mergeBelowPeople", 0, 1_000_000),
            OwnCampsiteBonusPercent = migration.Int("ownCampsiteBonusPercent", 0, 1000),
            AttackAboveStrengthPercent = migration.Int("attackAboveStrengthPercent", 1, 10_000),
            DefenderNonWarriorPercent = conquest.Int("defenderNonWarriorPercent", 0, 100),
            WinnerWarriorLossPercent = conquest.Int("winnerWarriorLossPercent", 0, 100),
            LoserWarriorLossPercent = conquest.Int("loserWarriorLossPercent", 0, 100),
            FleeingLossPercent = conquest.Int("fleeingLossPercent", 0, 100),
            NewTribe = tribe,
        };
    }

    private static List<GoodDefinition> LoadGoods(string path, List<string> errors)
    {
        var result = new List<GoodDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "goods", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: goods[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            int era = r.Int("era", 1, 10);
            GoodCategory category = r.Enum<GoodCategory>("category");
            Amount spoilage = r.Amount("spoilagePerYear", Amount.Zero, Amount.FromUnits(100));
            uint color = r.Color("color");
            Amount value = r.Amount("value", Amount.FromHundredths(1), Amount.FromUnits(10_000));
            if (category != GoodCategory.Food && spoilage > Amount.Zero)
            {
                // CORE simplified (GDD 16): only food spoils.
                errors.Add($"{Path.GetFileName(path)}: goods[{i}].spoilagePerYear: only food may spoil.");
            }

            result.Add(new GoodDefinition(i, key, name, era, category, (int)spoilage.Hundredths, color) { Value = value });
            i++;
        }

        if (result.Count == 0)
        {
            errors.Add($"{Path.GetFileName(path)}: at least one good is required.");
        }

        if (result.Count > 255)
        {
            errors.Add($"{Path.GetFileName(path)}: at most 255 goods are supported.");
        }

        return result;
    }

    private static List<RecipeDefinition> LoadRecipes(string path, List<GoodDefinition> goods, List<string> errors)
    {
        var result = new List<RecipeDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "recipes", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: recipes[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            List<GoodQuantity> inputs = r.Goods("inputs", goods);
            List<GoodQuantity> outputs = r.Goods("outputs", goods);
            int workers = r.Int("workers", 1, 1000);
            int years = r.Int("years", 1, 100);
            result.Add(new RecipeDefinition(i, key, name, inputs, outputs, workers, years));
            i++;
        }

        return result;
    }

    private static (List<SettlementLevelDefinition> Levels, int TribeLevel, List<GoodQuantity> TribeStock, AssetRules Assets) LoadSettlements(
        string path, List<GoodDefinition> goods, List<string> errors)
    {
        var levels = new List<SettlementLevelDefinition>();
        var stock = new List<GoodQuantity>();
        int tribeLevel = 0;
        var assets = new AssetRules();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "levels", path, errors, out JsonElement array))
        {
            return (levels, tribeLevel, stock, assets);
        }

        string file = Path.GetFileName(path);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{file}: levels[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            (int min, int max) = r.Range("population", 0, int.MaxValue);
            Amount storage = r.Amount("storage", Amount.FromUnits(1), Amount.FromUnits(1_000_000_000));
            bool mobile = r.Bool("mobile");
            int abandoned = r.Int("abandonedYears", 0, 100_000);
            int ruins = r.Int("ruinsUntilYears", 0, 1_000_000);
            if (ruins < abandoned)
            {
                errors.Add($"{file}: levels[{i}].ruinsUntilYears must not be smaller than abandonedYears.");
            }

            if (levels.Count > 0 && (min < levels[^1].MinPopulation || storage < levels[^1].StorageCapacity))
            {
                errors.Add($"{file}: levels[{i}] must not be smaller than the level before it.");
            }

            string requires = e.TryGetProperty("requires", out JsonElement rq) && rq.ValueKind == JsonValueKind.String ? rq.GetString()! : string.Empty;
            levels.Add(new SettlementLevelDefinition(i, key, name, min, max, storage, mobile) { AbandonedYears = abandoned, RuinsUntilYears = ruins, RequiresKey = requires });
            i++;
        }

        if (levels.Count == 0)
        {
            errors.Add($"{file}: at least one level is required.");
        }

        if (doc.RootElement.TryGetProperty("newTribe", out JsonElement tribe) && tribe.ValueKind == JsonValueKind.Object)
        {
            var r = new Reader(tribe, $"{file}: newTribe", errors);
            string level = r.String("level");
            tribeLevel = levels.FindIndex(l => l.Key == level);
            if (tribeLevel < 0 && level.Length > 0)
            {
                errors.Add($"{file}: newTribe.level: unknown level '{level}'.");
            }

            stock = r.Goods("stock", goods);
        }
        else
        {
            errors.Add($"{file}: missing object 'newTribe'.");
        }

        JsonElement Object(string name)
        {
            if (doc.RootElement.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Object)
            {
                return v;
            }

            errors.Add($"{file}: missing object '{name}'.");
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        var claims = new Reader(Object("abandonment"), $"{file}: abandonment", errors);
        var carrying = new Reader(Object("carrying"), $"{file}: carrying", errors);
        assets = new AssetRules
        {
            ClaimYears = claims.Int("claimYears", 0, 100_000),
            CarryPerPerson = carrying.Amount("perPerson", Amount.Zero, Amount.FromUnits(100_000)),
            SettleMinPeople = new Reader(Object("settle"), $"{file}: settle", errors).Int("minPeople", 1, 1_000_000),
        };

        return (levels, Math.Max(0, tribeLevel), stock, assets);
    }

    // Forms of government of data/governments.json (stage 4f, ADR 0023).
    private static List<GovernmentDefinition> LoadGovernments(string path, List<EraDefinition> eras, List<StratumDefinition> strata, List<string> errors)
    {
        var result = new List<GovernmentDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "governments", path, errors, out JsonElement array))
        {
            return result;
        }

        string file = Path.GetFileName(path);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{file}: governments[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            string title = r.String("title");
            if (title.Length > 0 && !title.Contains("{people}", StringComparison.Ordinal))
            {
                errors.Add($"{at}.title: must contain {{people}}.");
            }

            string era = r.String("fromEra");
            int fromEra = eras.FindIndex(x => x.Key == era);
            if (era.Length > 0 && fromEra < 0)
            {
                errors.Add($"{at}.fromEra: unknown era '{era}'.");
            }

            result.Add(new GovernmentDefinition(i, key, name)
            {
                Title = title,
                FromEra = Math.Max(0, fromEra),
                EnactYears = r.Int("enactYears", 0, 100),
                ReviewPercent = r.Int("reviewPercent", 10, 1000),
                CautionShift = r.Int("cautionShift", -1000, 1000),
                GoalPercent = EnumMap<Core.Ai.Goal>(e, "goalPercent", at, 100, 0, 1000, errors),
                Leaning = EnumMap<Core.Ai.Trait>(e, "leaning", at, 0, 0, 100, errors),
                LeaderTitle = r.String("leaderTitle"),
                Succession = ParseSuccession(r.String("succession"), at, errors),
                SuccessorAge = r.Range("successorAge", 0, 120),
                CrisisPercent = r.Int("crisisPercent", 0, 100),
                CrisisYears = r.Int("crisisYears", 0, 100),
                TermYears = r.Int("termYears", 0, 100),
                StabilityBonus = r.Int("stabilityBonus", -100, 100),
                Administration = r.Int("administration", 1, 1_000_000),
                Reach = r.Int("reach", 0, 1000),
                StrataInfluence = StrataMap(e, "strataInfluence", at, strata, 1, 0, 100, errors),
            });
            i++;
        }

        if (!result.Exists(g => g.FromEra == 0))
        {
            errors.Add($"{file}: at least one government must be open from the first era.");
        }

        return result;
    }

    private static Succession ParseSuccession(string text, string at, List<string> errors)
    {
        foreach (Succession s in Enum.GetValues<Succession>())
        {
            if (ToSnake(s.ToString()) == text)
            {
                return s;
            }
        }

        if (text.Length > 0)
        {
            errors.Add($"{at}.succession: unknown succession '{text}'.");
        }

        return Succession.Council;
    }

    // Leaders of data/leaders.json (stage 4f PR 2, GDD 7.5).
    private static LeaderRules LoadLeaders(string path, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "traits", path, errors, out JsonElement array))
        {
            return new LeaderRules();
        }

        string file = Path.GetFileName(path);
        var r = new Reader(doc.RootElement, file, errors);
        var traits = new List<LeaderTraitDefinition>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{file}: traits[{i}]";
            var t = new Reader(e, at, errors);
            traits.Add(new LeaderTraitDefinition(i, t.Key("key", keys), t.String("name"))
            {
                Shifts = EnumMap<Core.Ai.Trait>(e, "shifts", at, 0, -1000, 1000, errors),
            });
            i++;
        }

        var death = new List<(int Age, int PerMille)>();
        if (doc.RootElement.TryGetProperty("deathByAge", out JsonElement d) && d.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement p in d.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 2 || !p[0].TryGetInt32(out int age) || !p[1].TryGetInt32(out int perMille)
                    || perMille < 0 || perMille > 1000 || (death.Count > 0 && age <= death[^1].Age))
                {
                    errors.Add($"{file}: deathByAge: each point is [age, per mille 0..1000], ages rising.");
                    continue;
                }

                death.Add((age, perMille));
            }
        }

        if (death.Count == 0)
        {
            errors.Add($"{file}: deathByAge: at least one point is required.");
        }

        (int min, int max) count = r.Range("traitCount", 0, 10);
        if (count.max > traits.Count)
        {
            errors.Add($"{file}: traitCount: a leader cannot have more traits than there are.");
        }

        return new LeaderRules
        {
            Traits = traits,
            TraitCount = count,
            StartAge = r.Range("startAge", 0, 120),
            CouncilCandidates = r.Int("councilCandidates", 1, 20),
            DeathByAge = death,
        };
    }

    // An object {"stratum_key": number} read into an array indexed by stratum id, missing = fallback.
    private static int[] StrataMap(JsonElement parent, string name, string at, List<StratumDefinition> strata, int fallback, int min, int max, List<string> errors)
    {
        var result = new int[strata.Count];
        Array.Fill(result, fallback);
        if (!parent.TryGetProperty(name, out JsonElement o))
        {
            return result;
        }

        if (o.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{at}.{name}: must be an object.");
            return result;
        }

        foreach (JsonProperty p in o.EnumerateObject())
        {
            int k = strata.FindIndex(x => x.Key == p.Name);
            if (k < 0)
            {
                errors.Add($"{at}.{name}: unknown stratum '{p.Name}'.");
                continue;
            }

            if (!p.Value.TryGetInt32(out int n) || n < min || n > max)
            {
                errors.Add($"{at}.{name}.{p.Name}: must be a whole number in {min}..{max}.");
                continue;
            }

            result[k] = n;
        }

        return result;
    }

    // Stability and administration of data/stability.json (stage 4f PR 3, GDD 7.3).
    // Units (stage 6 PR 3a): speeds, terrain costs, scouting and settlers.
    private static Core.Units.UnitRules LoadUnits(string path, List<BiomeDefinition> biomes, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new Core.Units.UnitRules();
        }

        string file = Path.GetFileName(path);
        var r = new Reader(doc.RootElement, file, errors);
        int count = Enum.GetValues<Core.Units.UnitKind>().Length;
        var kinds = new Core.Units.UnitKindDefinition?[count];
        if (TryArray(doc.RootElement, "kinds", path, errors, out JsonElement array))
        {
            int i = 0;
            foreach (JsonElement e in array.EnumerateArray())
            {
                string at = $"{file}: kinds[{i}]";
                var x = new Reader(e, at, errors);
                Core.Units.UnitKind kind = x.Enum<Core.Units.UnitKind>("kind");
                if (kinds[(int)kind] is not null)
                {
                    errors.Add($"{at}.kind: '{e.GetProperty("kind")}' is listed twice.");
                }

                kinds[(int)kind] = new Core.Units.UnitKindDefinition
                {
                    Kind = kind,
                    Name = x.String("name"),
                    TicksPerCell = x.Int("ticksPerCell", 1, 10000),
                    SightCells = x.Int("sightCells", 0, 20),
                };
                i++;
            }
        }

        for (int k = 0; k < count; k++)
        {
            if (kinds[k] is null)
            {
                errors.Add($"{file}: kinds: missing kind '{ToSnake(((Core.Units.UnitKind)k).ToString())}'.");
                kinds[k] = new Core.Units.UnitKindDefinition { Kind = (Core.Units.UnitKind)k };
            }
        }

        JsonElement terrain = doc.RootElement.TryGetProperty("terrain", out JsonElement te) ? te : default;
        var t = new Reader(terrain, $"{file}: terrain", errors);
        var scouts = new Reader(doc.RootElement.TryGetProperty("scouts", out JsonElement se) ? se : default, $"{file}: scouts", errors);
        var settlers = new Reader(doc.RootElement.TryGetProperty("settlers", out JsonElement st) ? st : default, $"{file}: settlers", errors);
        return new Core.Units.UnitRules
        {
            Kinds = kinds!,
            JourneyTicksPerCell = r.Int("journeyTicksPerCell", 1, 10000),
            HillsExtraPercent = t.Int("hillsExtraPercent", 0, 10000),
            MountainsExtraPercent = t.Int("mountainsExtraPercent", 0, 10000),
            IcePercent = t.Int("icePercent", 100, 10000),
            RiverPercent = t.Int("riverPercent", 0, 10000),
            RouteNodeLimit = r.Int("routeNodeLimit", 100, 10_000_000),
            ScoutEveryYears = scouts.Int("everyYears", 0, 1000),
            ScoutTripCells = scouts.Int("tripCells", 1, 200),
            SettlerFoodMonths = settlers.Int("foodMonths", 0, 120),
            HungerDeathPercent = settlers.Int("hungerDeathPercent", 0, 100),
            FoundSearchCells = settlers.Int("foundSearchCells", 0, 20),
        };
    }

    // Time of the live game (ADR 0025, stage 6): ticks, pulses and months of action.
    private static Core.Simulation.TimeRules LoadTime(string path, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new Core.Simulation.TimeRules();
        }

        var r = new Reader(doc.RootElement, Path.GetFileName(path), errors);
        return new Core.Simulation.TimeRules
        {
            TicksPerSecond = r.Int("ticksPerSecond", 1, 1000),
            TicksPerPulse = r.Int("ticksPerPulse", 1, 10000),
            TicksPerActionMonth = r.Int("ticksPerActionMonth", 1, 1000),
        };
    }

    private static StabilityRules LoadStability(string path, List<EffectDefinition> effects, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new StabilityRules();
        }

        string file = Path.GetFileName(path);
        var r = new Reader(doc.RootElement, file, errors);
        string unrest = r.String("unrestEffect");
        EffectDefinition? effect = effects.Find(e => e.Key == unrest);
        if (effect is null)
        {
            errors.Add($"{file}: unrestEffect: unknown effect '{unrest}'.");
        }
        else if (effect.Stat != EffectStat.OutputPercent || effect.Target != EffectTarget.Civilization)
        {
            errors.Add($"{file}: unrestEffect: '{unrest}' must change output_percent of a civilization.");
        }

        return new StabilityRules
        {
            UnrestBelow = r.Int("unrestBelow", 0, 100),
            UnrestOutputPercentPerPoint = r.Int("unrestOutputPercentPerPoint", 0, 100),
            UnrestEffect = unrest,
            CrisisPenalty = r.Int("crisisPenalty", 0, 100),
            AnarchyPenalty = r.Int("anarchyPenalty", 0, 100),
            AnarchyYears = r.Int("anarchyYears", 0, 100),
            CapitalLostPenalty = r.Int("capitalLostPenalty", 0, 100),
            CapitalLostYears = r.Int("capitalLostYears", 0, 100),
            AdminPerSettlement = r.Int("adminPerSettlement", 0, 10_000),
            AdminPerRoadCost = r.Int("adminPerRoadCost", 0, 10_000),
            RoadSearchCost = r.Int("roadSearchCost", 1, 10_000),
            OverloadPenaltyPer100Percent = r.Int("overloadPenaltyPer100Percent", 0, 1000),
            OverloadMaxPenalty = r.Int("overloadMaxPenalty", 0, 100),
            LoyaltyPer10RoadCost = r.Int("loyaltyPer10RoadCost", 0, 1000),
            RevoltBelow = r.Int("revoltBelow", 0, 100),
            RevoltPerMillePerYear = r.Int("revoltPerMillePerYear", 0, 1000),
            RevoltCalmYears = r.Int("revoltCalmYears", 0, 1000),
            SecessionLoyaltyBelow = r.Int("secessionLoyaltyBelow", 0, 100),
            SecessionMinPeople = r.Int("secessionMinPeople", 0, 1_000_000),
            MaxCivilizations = r.Int("maxCivilizations", 1, 64),
            SecessionCharacterShift = r.Int("secessionCharacterShift", 0, 1000),
            DriftPerMillePerYear = r.Int("driftPerMillePerYear", 0, 1000),
        };
    }

    // An object {"enum_name": number} read into an array indexed by the enum (names in snake case), missing = fallback.
    private static int[] EnumMap<TEnum>(JsonElement parent, string name, string at, int fallback, int min, int max, List<string> errors)
        where TEnum : struct, Enum
    {
        TEnum[] values = Enum.GetValues<TEnum>();
        var result = new int[values.Length];
        Array.Fill(result, fallback);
        if (!parent.TryGetProperty(name, out JsonElement o))
        {
            return result;
        }

        if (o.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{at}.{name}: must be an object.");
            return result;
        }

        foreach (JsonProperty p in o.EnumerateObject())
        {
            int k = Array.FindIndex(values, v => ToSnake(v.ToString()) == p.Name);
            if (k < 0)
            {
                errors.Add($"{at}.{name}: unknown name '{p.Name}'.");
                continue;
            }

            if (!p.Value.TryGetInt32(out int n) || n < min || n > max)
            {
                errors.Add($"{at}.{name}.{p.Name}: must be a whole number in {min}..{max}.");
                continue;
            }

            result[Convert.ToInt32(values[k], System.Globalization.CultureInfo.InvariantCulture)] = n;
        }

        return result;
    }

    private static List<EraDefinition> LoadEras(string path, List<string> errors)
    {
        var result = new List<EraDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "eras", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: eras[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            int months = r.Int("monthsPerTurn", 1, 1200);
            result.Add(new EraDefinition(i, key, name, months)
            {
                ArmyLimit = r.Int("armyLimit", 1, 1_000_000),
                TroopsPerCell = r.Int("troopsPerCell", 1, 1_000_000),
            });
            i++;
        }

        if (result.Count == 0)
        {
            errors.Add($"{Path.GetFileName(path)}: at least one era is required.");
        }

        return result;
    }

    private static (List<PlanetPreset> Planets, Core.WorldGen.SeasonRules Seasons, Core.WorldGen.ClimateCycleRules Cycles) LoadPlanets(string path, List<string> errors)
    {
        var result = new List<PlanetPreset>();
        var seasons = new Core.WorldGen.SeasonRules();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "planets", path, errors, out JsonElement array))
        {
            return (result, seasons, new Core.WorldGen.ClimateCycleRules());
        }

        if (doc.RootElement.TryGetProperty("seasons", out JsonElement se) && se.ValueKind == JsonValueKind.Object)
        {
            var r = new Reader(se, $"{Path.GetFileName(path)}: seasons", errors);
            seasons = new Core.WorldGen.SeasonRules
            {
                StepsPerYear = r.Int("stepsPerYear", 4, Core.WorldGen.Insolation.Steps),
                MinStepMonths = r.Int("minStepMonths", 1, 1200),
                WinterFoodPercent = r.Int("winterFoodPercent", 0, 100),
            };
        }
        else
        {
            errors.Add($"{Path.GetFileName(path)}: missing object 'seasons'.");
        }

        var cycles = new Core.WorldGen.ClimateCycleRules();
        if (doc.RootElement.TryGetProperty("climateCycles", out JsonElement cy) && cy.ValueKind == JsonValueKind.Object)
        {
            var r = new Reader(cy, $"{Path.GetFileName(path)}: climateCycles", errors);
            (int fullEdge, int startEdge) = r.Range("sheetEdgeDegrees", 0, 90);
            (int full, int none) = r.Range("glaciationSummerDegrees", -50, 60);
            cycles = new Core.WorldGen.ClimateCycleRules
            {
                UpdateYears = r.Int("updateYears", 10, 100_000),
                TiltAmplitudeTenths = (int)(r.Amount("tiltAmplitude", Amount.Zero, Amount.FromUnits(20)).Hundredths / 10),
                TiltPeriodYears = r.Int("tiltPeriodYears", 1_000, 10_000_000),
                LongEccentricityThousandths = r.Int("longEccentricityPerMille", 0, 100),
                LongEccentricityYears = r.Int("longEccentricityYears", 1_000, 10_000_000),
                ShortEccentricityThousandths = r.Int("shortEccentricityPerMille", 0, 100),
                ShortEccentricityYears = r.Int("shortEccentricityYears", 1_000, 10_000_000),
                PrecessionYears = r.Int("precessionYears", 1_000, 10_000_000),
                GlacialBelow = r.Int("glacialBelowPerMille", -5_000, 5_000) * 1000L,
                FullGlacialVolume = r.Int("fullGlacialVolumePerMille", 1, 5_000) * 1000L,
                WarmAbove = r.Int("warmAbovePerMille", -5_000, 5_000) * 1000L,
                MeltYears = r.Int("meltYears", 100, 10_000_000),
                GrowYears = r.Int("growYears", 100, 10_000_000),
                SunlightYears = r.Int("sunlightYears", 100, 10_000_000),
                FullSheetVolume = r.Int("fullSheetVolumePerMille", 1, 5_000) * 1000L,
                SheetEdgeDegrees = (startEdge, fullEdge),
                GlacialCo2DropPercent = r.Int("glacialCo2DropPercent", 0, 90),
                GlaciationSummerDegrees = (full, none),
                GlacialSeaDropMeters = r.Int("glacialSeaDropMeters", 0, 1_000),
                SeaMetersPerStep = r.Int("seaMetersPerStep", 1, 1_000),
                MeltSeaRiseMeters = r.Int("meltSeaRiseMeters", 0, 1_000),
            };
            if (fullEdge >= startEdge || full >= none)
            {
                errors.Add($"{Path.GetFileName(path)}: climateCycles: sheetEdgeDegrees and glaciationSummerDegrees must be [low, high].");
            }
        }
        else
        {
            errors.Add($"{Path.GetFileName(path)}: missing object 'climateCycles'.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: planets[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            (int tMin, int tMax) = Core.WorldGen.PlanetParameters.TiltRange;
            (int eMin, int eMax) = Core.WorldGen.PlanetParameters.EccentricityRange;
            int tilt = (int)(r.Amount("axialTilt", Amount.FromHundredths(tMin * 10), Amount.FromHundredths(tMax * 10)).Hundredths / 10);
            int eccentricity = r.Int("eccentricityPerMille", eMin, eMax);
            int volcanism = r.Int("volcanism", Core.WorldGen.PlanetParameters.VolcanismRange.Min, Core.WorldGen.PlanetParameters.VolcanismRange.Max);
            long? otherGases = r.OptionalThousandths("otherGases", 100, 50_000);
            long? co2 = r.OptionalThousandths("co2", 1, 10_000_000 / 1000);
            var overrides = new Core.WorldGen.PlanetOverrides
            {
                AxialTiltTenths = tilt,
                EccentricityThousandths = eccentricity,
                LuminosityThousandths = r.OptionalThousandths("luminosity", 1, 100_000_000),
                DistanceThousandths = r.OptionalThousandths("distance", 1, 1_000_000),
                YearThousandths = r.OptionalThousandths("yearLength", 1, 1_000_000),
                LightThousandths = r.OptionalThousandths("light", 1, 10_000),
                Co2Microbar = co2 is long c ? (int)(c * 1000) : null,
                VolcanismPercent = volcanism,
                StarVariabilityPerMille = r.OptionalInt("starVariabilityPerMille", Core.WorldGen.PlanetParameters.StarVariabilityRange.Min, Core.WorldGen.PlanetParameters.StarVariabilityRange.Max),
                StarCycleYears = r.OptionalInt("starCycleYears", Core.WorldGen.PlanetParameters.StarCycleRange.Min, Core.WorldGen.PlanetParameters.StarCycleRange.Max),
            };
            int given = (overrides.LuminosityThousandths is null ? 0 : 1) + (overrides.DistanceThousandths is null ? 0 : 1)
                + (overrides.YearThousandths is null ? 0 : 1) + (overrides.LightThousandths is null ? 0 : 1);
            Core.WorldGen.PlanetParameters planet = Core.WorldGen.PlanetParameters.Earth;
            if (given != 2)
            {
                errors.Add($"{Path.GetFileName(path)}: planets[{i}]: give exactly two of luminosity, distance, yearLength and light.");
            }
            else
            {
                try
                {
                    var start = Core.WorldGen.PlanetParameters.Earth with { BackgroundPressureHundredths = (int)((otherGases ?? 1000) / 10) };
                    (planet, IReadOnlyList<string> notes) = Core.WorldGen.PlanetSetup.Build(start, co2 is null, overrides);
                    foreach (string note in notes)
                    {
                        errors.Add($"{Path.GetFileName(path)}: planets[{i}]: {note}");
                    }
                }
                catch (ArgumentException ex)
                {
                    errors.Add($"{Path.GetFileName(path)}: planets[{i}]: {ex.Message}");
                }
            }

            int water = r.Int("water", 30, 80);
            Core.WorldGen.WorldMoisture moisture = r.Enum<Core.WorldGen.WorldMoisture>("moisture");
            Core.WorldGen.WorldRelief relief = r.Enum<Core.WorldGen.WorldRelief>("relief");
            result.Add(new PlanetPreset(key, name, planet, water, moisture, relief) { AutoCo2 = co2 is null });
            i++;
        }

        if (!result.Exists(p => p.Key == "earth"))
        {
            errors.Add($"{Path.GetFileName(path)}: the 'earth' preset is required.");
        }

        return (result, seasons, cycles);
    }

    private static (List<TechnologyDefinition> Technologies, ResearchRules Rules) LoadTechnologies(
        string path,
        List<GoodDefinition> goods,
        List<BiomeDefinition> biomes,
        List<DepositDefinition> deposits,
        List<RecipeDefinition> recipes,
        List<EffectDefinition> effects,
        List<string> errors)
    {
        var result = new List<TechnologyDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "technologies", path, errors, out JsonElement array))
        {
            return (result, new ResearchRules());
        }

        string file = Path.GetFileName(path);
        var top = new Reader(doc.RootElement, file, errors);
        var rules = new ResearchRules
        {
            DiffusionPercent = top.Int("diffusionPercent", 0, 100),
            MaxNeighbours = top.Int("maxNeighbours", 0, 64),
            ContactCells = top.Int("contactCells", 1, 200),
            MinCostPercent = top.Int("minCostPercent", 1, 100),
            EraSharePercent = top.Int("eraSharePercent", 0, 100),
        };

        // Keys first, so a technology may require one listed after it.
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        int n = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string key = new Reader(e, $"{file}: technologies[{n}]", errors).Key("key", keys);
            ids.TryAdd(key, n);
            n++;
        }

        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{file}: technologies[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.String("key");
            string name = r.String("name");
            int era = r.Int("era", 1, 10);
            TechField field = r.Enum<TechField>("field");
            Amount cost = r.Amount("cost", Amount.FromUnits(1), Amount.FromUnits(100_000_000));
            List<int> requires = TechList(e, "requires", ids, at, errors, required: true);
            List<int> any = TechList(e, "requiresAny", ids, at, errors, required: false);
            var inspirations = new List<Inspiration>();
            if (e.TryGetProperty("inspirations", out JsonElement ins) && ins.ValueKind == JsonValueKind.Array)
            {
                int k = 0;
                foreach (JsonElement x in ins.EnumerateArray())
                {
                    var ir = new Reader(x, $"{at}.inspirations[{k}]", errors);
                    InspirationKind kind = ir.Enum<InspirationKind>("kind");
                    int percent = ir.Int("percent", 1, 100);
                    inspirations.Add(kind switch
                    {
                        InspirationKind.GoodInNetwork => new Inspiration(kind, IdOf(goods.FindIndex(g => g.Key == ir.String("good")), "good", at, errors), ir.Amount("amount", Amount.FromHundredths(1), Amount.FromUnits(1_000_000)), 0, percent),
                        InspirationKind.DepositInZone => new Inspiration(kind, IdOf(deposits.Find(d => d.Key == ir.String("deposit"))?.Id ?? -1, "deposit", at, errors), Amount.Zero, 0, percent),
                        InspirationKind.BiomeInZone => new Inspiration(kind, IdOf(biomes.FindIndex(b => b.Key == ir.String("biome")), "biome", at, errors), Amount.Zero, 0, percent),
                        InspirationKind.WinterBelow => new Inspiration(kind, -1, Amount.Zero, ir.Int("degrees", -100, 100), percent),
                        InspirationKind.PeopleAtLeast => new Inspiration(kind, -1, Amount.Zero, ir.Int("people", 1, 1_000_000_000), percent),
                        _ => new Inspiration(kind, -1, Amount.Zero, 0, percent),
                    });
                    k++;
                }
            }

            var recipeIds = new List<int>();
            var gather = new List<GatherBonus>();
            var techEffects = new List<TechEffect>();
            if (e.TryGetProperty("unlocks", out JsonElement u) && u.ValueKind == JsonValueKind.Object)
            {
                if (u.TryGetProperty("recipes", out JsonElement rs) && rs.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement x in rs.EnumerateArray())
                    {
                        recipeIds.Add(IdOf(recipes.FindIndex(rc => rc.Key == x.GetString()), "recipe", at, errors));
                    }
                }

                if (u.TryGetProperty("gather", out JsonElement gs) && gs.ValueKind == JsonValueKind.Array)
                {
                    int k = 0;
                    foreach (JsonElement x in gs.EnumerateArray())
                    {
                        var gr = new Reader(x, $"{at}.unlocks.gather[{k}]", errors);
                        bool byDeposit = x.TryGetProperty("deposit", out _);
                        int deposit = byDeposit ? IdOf(deposits.Find(d => d.Key == gr.String("deposit"))?.Id ?? -1, "deposit", at, errors) : 0;
                        int biome = byDeposit ? -1 : IdOf(biomes.FindIndex(b => b.Key == gr.String("biome")), "biome", at, errors);
                        gather.Add(new GatherBonus(deposit, biome, gr.Goods("goods", goods)));
                        k++;
                    }
                }

                if (u.TryGetProperty("effects", out JsonElement es) && es.ValueKind == JsonValueKind.Array)
                {
                    int k = 0;
                    foreach (JsonElement x in es.EnumerateArray())
                    {
                        var er = new Reader(x, $"{at}.unlocks.effects[{k}]", errors);
                        int effect = IdOf(effects.FindIndex(ef => ef.Key == er.String("effect")), "effect", at, errors);
                        if (effect >= 0 && effects[effect].Target != EffectTarget.Civilization)
                        {
                            errors.Add($"{at}: effect '{effects[effect].Key}' must target a civilization.");
                        }

                        techEffects.Add(new TechEffect(effect, er.Int("strength", -1000, 1000)));
                        k++;
                    }
                }
            }

            result.Add(new TechnologyDefinition(i, key, name, era, field, cost)
            {
                Requires = requires,
                RequiresAny = any,
                Inspirations = inspirations,
                Recipes = recipeIds,
                Gather = gather,
                Effects = techEffects,
            });
            i++;
        }

        // Every recipe needs a technology, and the requirements must not loop.
        for (int r = 0; r < recipes.Count; r++)
        {
            if (!result.Exists(t => System.Linq.Enumerable.Contains(t.Recipes, r)))
            {
                errors.Add($"{file}: recipe '{recipes[r].Key}' is not unlocked by any technology.");
            }
        }

        var state = new int[result.Count];
        foreach (TechnologyDefinition t in result)
        {
            if (Loops(result, t.Id, state))
            {
                errors.Add($"{file}: the requirements of '{t.Key}' form a loop.");
                break;
            }
        }

        return (result, rules);
    }

    private static List<int> TechList(JsonElement e, string name, Dictionary<string, int> ids, string at, List<string> errors, bool required)
    {
        var list = new List<int>();
        if (!e.TryGetProperty(name, out JsonElement a) || a.ValueKind != JsonValueKind.Array)
        {
            if (required)
            {
                errors.Add($"{at}.{name}: required list of technology keys.");
            }

            return list;
        }

        foreach (JsonElement x in a.EnumerateArray())
        {
            if (x.GetString() is string k && ids.TryGetValue(k, out int id))
            {
                list.Add(id);
            }
            else
            {
                errors.Add($"{at}.{name}: unknown technology '{x}'.");
            }
        }

        return list;
    }

    private static int IdOf(int id, string what, string at, List<string> errors)
    {
        if (id < 0)
        {
            errors.Add($"{at}: unknown {what}.");
        }

        return id;
    }

    // Depth-first search for a cycle (0 = unseen, 1 = on the path, 2 = done).
    private static bool Loops(List<TechnologyDefinition> techs, int id, int[] state)
    {
        if (state[id] == 1)
        {
            return true;
        }

        if (state[id] == 2)
        {
            return false;
        }

        state[id] = 1;
        foreach (int r in techs[id].Requires)
        {
            if (Loops(techs, r, state))
            {
                return true;
            }
        }

        foreach (int r in techs[id].RequiresAny)
        {
            if (Loops(techs, r, state))
            {
                return true;
            }
        }

        state[id] = 2;
        return false;
    }

    private static void ResolveEraThresholds(string path, List<EraDefinition> eras, List<TechnologyDefinition> techs, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !doc.RootElement.TryGetProperty("eras", out JsonElement array))
        {
            return;
        }

        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            if (i < eras.Count && e.TryGetProperty("threshold", out JsonElement t))
            {
                int id = techs.FindIndex(x => x.Key == t.GetString());
                if (id < 0)
                {
                    errors.Add($"{Path.GetFileName(path)}: eras[{i}].threshold: unknown technology '{t}'.");
                }
                else
                {
                    eras[i] = eras[i] with { ThresholdTechnology = id };
                }
            }

            i++;
        }
    }

    private static List<EventDefinition> LoadEvents(
        string path,
        List<BiomeDefinition> biomes,
        List<EffectDefinition> effects,
        List<TechnologyDefinition> technologies,
        List<string> errors)
    {
        var result = new List<EventDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "events", path, errors, out JsonElement array))
        {
            return result;
        }

        string file = Path.GetFileName(path);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{file}: events[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            int chance = r.Int("timesPer10000Years", 0, 10_000);
            string chronicle = r.String("chronicle");
            var conditions = new List<EventCondition>();
            foreach ((JsonElement x, string xat) in Items(e, "conditions", at))
            {
                var cr = new Reader(x, xat, errors);
                EventConditionKind kind = cr.Enum<EventConditionKind>("kind");
                int percent = cr.OptionalInt("percent", 0, 10_000) ?? 100;
                bool required = x.TryGetProperty("required", out _) && cr.Bool("required");
                int value = kind switch
                {
                    EventConditionKind.HumidityBelow or EventConditionKind.HumidityAbove => cr.Int("value", 0, 100),
                    EventConditionKind.BiomeNear => cr.Int("value", 0, 10),
                    EventConditionKind.PeopleAtLeast or EventConditionKind.CivilizationPeopleAtLeast => cr.Int("value", 1, 1_000_000_000),
                    _ => 0,
                };
                var biomeIds = new List<int>();
                if (kind == EventConditionKind.BiomeNear)
                {
                    foreach ((JsonElement b, string bat) in Items(x, "biomes", xat))
                    {
                        biomeIds.Add(IdOf(biomes.FindIndex(d => d.Key == b.GetString()), "biome", bat, errors));
                    }

                    if (biomeIds.Count == 0)
                    {
                        errors.Add($"{xat}.biomes: required list of biome keys.");
                    }
                }

                conditions.Add(new EventCondition(kind, value, biomeIds, percent, required));
            }

            var softened = new List<EventSoftening>();
            foreach ((JsonElement x, string xat) in Items(e, "softenedBy", at))
            {
                var sr = new Reader(x, xat, errors);
                string tech = sr.String("technology");
                softened.Add(new EventSoftening(IdOf(technologies.FindIndex(t => t.Key == tech), "technology", xat, errors), sr.Int("percent", 1, 100)));
            }

            var laid = new List<EventEffect>();
            foreach ((JsonElement x, string xat) in Items(e, "effects", at))
            {
                var er = new Reader(x, xat, errors);
                string effect = er.String("effect");
                int id = IdOf(effects.FindIndex(d => d.Key == effect), "effect", xat, errors);
                int radius = er.OptionalInt("radiusCells", 0, 10) ?? 0;
                laid.Add(new EventEffect(id, er.Int("strength", -10_000, 10_000), er.Int("years", 1, 1000), radius));
            }

            int fertility = r.OptionalInt("fertilityGain", 0, 100) ?? 0;
            int fertilityRadius = r.OptionalInt("fertilityRadius", 0, 10) ?? 0;
            int spread = r.OptionalInt("spreadPercent", 0, 100) ?? 0;
            result.Add(new EventDefinition(i, key, name, chance, conditions, softened, laid, fertility, fertilityRadius, spread, chronicle));
            i++;
        }

        return result;
    }

    // The items of an optional array property, with their error location.
    private static IEnumerable<(JsonElement Item, string At)> Items(JsonElement e, string name, string at)
    {
        if (!e.TryGetProperty(name, out JsonElement a) || a.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        int k = 0;
        foreach (JsonElement x in a.EnumerateArray())
        {
            yield return (x, $"{at}.{name}[{k}]");
            k++;
        }
    }

    private static PerceptionRules LoadPerception(string path, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new PerceptionRules();
        }

        var r = new Reader(doc.RootElement, Path.GetFileName(path), errors);
        return new PerceptionRules
        {
            SightCells = r.Int("sightCells", 0, 50),
            OralMemoryYears = r.Int("oralMemoryYears", 1, 1_000_000),
            JourneyCells = r.Int("journeyCells", 1, 200),
        };
    }

    private static AiRules LoadAi(string path, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new AiRules();
        }

        var r = new Reader(doc.RootElement, Path.GetFileName(path), errors);
        return new AiRules
        {
            PickAmongTop = r.Int("pickAmongTop", 1, 3),
            ReviewEverySteps = r.Int("reviewEverySteps", 1, 100),
            DirectionBonusPercent = r.Int("directionBonusPercent", 0, 100),
            RecipeSwitchBetterByPercent = r.Int("recipeSwitchBetterByPercent", 0, 500),
            CrowdingLowPercent = r.Int("crowdingLowPercent", 0, 99),
            CrowdingHighPercent = r.Int("crowdingHighPercent", 1, 100),
            ExpansionWeightMin = r.Int("expansionWeightMin", 100, 1000),
            ExpansionWeightMax = r.Int("expansionWeightMax", 1000, 3000),
            FedWellPercent = r.Int("fedWellPercent", 1, 100),
            FedBadlyPercent = r.Int("fedBadlyPercent", 0, 99),
            HungerWeightMax = r.Int("hungerWeightMax", 1000, 3000),
            SplitMinPercentOfLevel = r.Int("splitMinPercentOfLevel", 1, 100),
            SearchEverySteps = r.Int("searchEverySteps", 1, 20),
            StarvingFedPercent = r.Int("starvingFedPercent", 0, 100),
            GoalOfFood = r.Enum<Core.Ai.Goal>("goalOfFood"),
            GoalOfGoods = r.Enum<Core.Ai.Goal>("goalOfGoods"),
            Scholar = LoadScholar(doc.RootElement, Path.GetFileName(path), errors),
            Commander = LoadCommander(doc.RootElement, Path.GetFileName(path), errors),
            TradeGoodsBonusPercent = r.Int("tradeGoodsBonusPercent", 0, 500),
            ScienceBehindTechs = r.Int("scienceBehindTechs", 1, 100),
            ScienceWeightMax = r.Int("scienceWeightMax", 1000, 3000),
            InfluenceMetCivs = r.Int("influenceMetCivs", 1, 100),
            InfluenceWeightMax = r.Int("influenceWeightMax", 1000, 3000),
            OverloadExpansionMinPercent = r.Int("overloadExpansionMinPercent", 0, 100),
            UnrestSecurityPercent = r.Int("unrestSecurityPercent", 100, 1000),
            UnrestWarPercent = r.Int("unrestWarPercent", 0, 100),
            GovernmentChangeMarginPercent = r.Int("governmentChangeMarginPercent", 0, 1000),
            GovernmentStabilityWeight = r.Int("governmentStabilityWeight", 0, 10_000),
            GovernmentKeepYears = r.Int("governmentKeepYears", 0, 10_000),
            MoveExpansionSharePercent = r.Int("moveExpansionSharePercent", 0, 100),
            JourneyBetterByPercent = r.Int("journeyBetterByPercent", 0, 1000),
            JourneyPenaltyPerMillePerCell = r.Int("journeyPenaltyPerMillePerCell", 0, 1000),
            FarSearchEveryCells = r.Int("farSearchEveryCells", 1, 10),
            TargetZeroAtJourneys = r.Int("targetZeroAtJourneys", 1, 10),
            SettleFarmYieldPercent = r.Int("settleFarmYieldPercent", 1, 10_000),
            SettleOverGatheringPercent = r.Int("settleOverGatheringPercent", 0, 10_000),
            VillageSettlersAbovePercent = r.Int("villageSettlersAbovePercent", 1, 100),
            VillageWornBelowPercent = r.Int("villageWornBelowPercent", 0, 100),
            VillageSettlersPeople = r.Int("villageSettlersPeople", 1, 100_000),
            VillageSettlersEveryYears = r.Int("villageSettlersEveryYears", 1, 100_000),
            VillageHungryFedPercent = r.Int("villageHungryFedPercent", 0, 100),
            VillageHungrySettlersEveryYears = r.Int("villageHungrySettlersEveryYears", 1, 100_000),
            ExploreAbovePerMille = r.Int("exploreAbovePerMille", 0, 1_000_000),
            ExploreLookCells = r.Int("exploreLookCells", 1, 20),
            ExploreUnknownPercent = r.Int("exploreUnknownPercent", 0, 100),
            ExploreGroupPercent = r.Int("exploreGroupPercent", 1, 90),
            PlunderBelowStoragePercent = r.Int("plunderBelowStoragePercent", 0, 100),
            UncomfortableBelowPercent = r.Int("uncomfortableBelowPercent", 0, 100),
            OpinionSwingPercent = r.Int("opinionSwingPercent", 0, 100),
            ReviewStaggerYears = r.Int("reviewStaggerYears", 0, 1000),
        };
    }

    private static CommanderRules LoadCommander(JsonElement root, string file, List<string> errors)
    {
        if (!root.TryGetProperty("commander", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{file}: missing object 'commander'.");
            return new CommanderRules();
        }

        var r = new Reader(section, $"{file}: commander", errors);
        return new CommanderRules
        {
            MinWinChancePercent = r.Int("minWinChancePercent", 0, 100),
            MaxLossPercent = r.Int("maxLossPercent", 0, 100),
            MinFoodMonths = r.Int("minFoodMonths", 0, 120),
            RecallChancePercent = r.Int("recallChancePercent", 0, 100),
        };
    }

    private static ScholarRules LoadScholar(JsonElement root, string file, List<string> errors)
    {
        if (!root.TryGetProperty("scholar", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{file}: missing object 'scholar'.");
            return new ScholarRules();
        }

        var r = new Reader(section, $"{file}: scholar", errors);
        Amount most = Amount.FromUnits(1000);
        var effects = new List<EffectWorth>();
        if (section.TryGetProperty("effects", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
        {
            int i = 0;
            foreach (JsonElement e in list.EnumerateArray())
            {
                var x = new Reader(e, $"{file}: scholar.effects[{i++}]", errors);
                EffectStat stat = x.Enum<EffectStat>("stat");
                if (effects.Exists(w => w.Stat == stat))
                {
                    errors.Add($"{file}: scholar.effects: stat '{stat}' is listed twice.");
                }

                effects.Add(new EffectWorth(
                    stat,
                    x.Enum<Core.Ai.Goal>("goal"),
                    x.Amount("perHundredPeoplePerPoint", Amount.Zero, most),
                    e.TryGetProperty("lowerIsBetter", out _) && x.Bool("lowerIsBetter"),
                    e.TryGetProperty("onlyWhenCold", out _) && x.Bool("onlyWhenCold")));
            }
        }
        else
        {
            errors.Add($"{file}: scholar.effects: required list.");
        }

        return new ScholarRules
        {
            BaseWorthPerPerson = r.Amount("baseWorthPerPerson", Amount.Zero, most),
            FieldWorthPerPerson = r.Amount("fieldWorthPerPerson", Amount.Zero, most),
            GatherWorthPercent = r.Int("gatherWorthPercent", 0, 1000),
            MissingInputPercent = r.Int("missingInputPercent", 0, 100),
            VillageWorthPerPerson = r.Amount("villageWorthPerPerson", Amount.Zero, most),
            LeadsToPercent = r.Int("leadsToPercent", 0, 100),
            CuriosityCostReliefPercent = r.Int("curiosityCostReliefPercent", 0, 100),
            Effects = effects,
        };
    }

    // Peoples' languages (GDD 5.6): pools of hard and soft sounds, syllable patterns and name lengths.
    private static Core.Naming.NameRules LoadNames(string path, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new Core.Naming.NameRules();
        }

        string file = Path.GetFileName(path);
        JsonElement root = doc.RootElement;
        var r = new Reader(root, file, errors);

        (IReadOnlyList<string>, IReadOnlyList<string>) Pools(string name)
        {
            if (!root.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{file}.{name}: required object with \"hard\" and \"soft\" lists.");
                return ([], []);
            }

            var x = new Reader(e, $"{file}.{name}", errors);
            List<string> hard = x.StringList("hard");
            List<string> soft = x.StringList("soft");
            foreach (string part in hard.Concat(soft))
            {
                if (part.Length == 0 || !part.All(c => c is >= 'a' and <= 'z'))
                {
                    errors.Add($"{file}.{name}: '{part}' must be lower-case letters a-z (the game is in English).");
                }
            }

            return (hard, soft);
        }

        var patterns = new List<(string, int)>();
        if (root.TryGetProperty("patterns", out JsonElement p) && p.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty pattern in p.EnumerateObject())
            {
                if (pattern.Name.Length == 0 || !pattern.Name.All(c => c is 'C' or 'V' or 'K') || !pattern.Value.TryGetInt32(out int weight) || weight < 0)
                {
                    errors.Add($"{file}.patterns.{pattern.Name}: a pattern of C, V and K with a weight of 0 or more.");
                    continue;
                }

                patterns.Add((pattern.Name, weight));
            }
        }

        if (patterns.Count == 0 || patterns.All(x => x.Item2 == 0))
        {
            errors.Add($"{file}.patterns: required, with at least one weight above 0.");
        }

        if (!root.TryGetProperty("language", out JsonElement l) || l.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("mutation", out JsonElement m) || m.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{file}: required objects \"language\" and \"mutation\".");
            return new Core.Naming.NameRules();
        }

        var language = new Reader(l, $"{file}.language", errors);
        var mutation = new Reader(m, $"{file}.mutation", errors);
        return new Core.Naming.NameRules
        {
            Consonants = Pools("consonants"),
            Vowels = Pools("vowels"),
            Codas = Pools("codas"),
            PeopleEndings = Pools("peopleEndings"),
            PlaceEndings = Pools("placeEndings"),
            Patterns = patterns,
            LanguageConsonants = language.Range("consonants", 1, 40),
            LanguageVowels = language.Range("vowels", 1, 20),
            LanguageCodas = language.Range("codas", 0, 20),
            LanguagePeopleEndings = language.Range("peopleEndings", 0, 20),
            LanguagePlaceEndings = language.Range("placeEndings", 0, 20),
            DoubledPercent = language.Range("doubledPercent", 0, 100),
            PeopleEndingPercent = language.Int("peopleEndingPercent", 0, 100),
            PlaceEndingPercent = language.Int("placeEndingPercent", 0, 100),
            PeopleSyllables = r.Range("peopleSyllables", 1, 6),
            PlaceSyllables = r.Range("placeSyllables", 1, 6),
            Length = r.Range("length", 2, 30),
            Mutation = (mutation.Int("consonants", 0, 20), mutation.Int("vowels", 0, 20), mutation.Int("endings", 0, 20)),
        };
    }

    private static Core.Diplomacy.RelationRules LoadRelations(string path, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new Core.Diplomacy.RelationRules();
        }

        string file = Path.GetFileName(path);
        var r = new Reader(doc.RootElement, file, errors);
        int kinds = Enum.GetValues<Core.Diplomacy.RelationEvent>().Length;
        var events = new Core.Diplomacy.RelationEventDefinition?[kinds];
        if (TryArray(doc.RootElement, "events", path, errors, out JsonElement array))
        {
            int i = 0;
            foreach (JsonElement e in array.EnumerateArray())
            {
                string at = $"{file}: events[{i}]";
                var x = new Reader(e, at, errors);
                Core.Diplomacy.RelationEvent kind = x.Enum<Core.Diplomacy.RelationEvent>("kind");
                int years = x.Int("years", 0, 100_000);
                var def = new Core.Diplomacy.RelationEventDefinition
                {
                    Kind = kind,
                    Sympathy = x.SignedHundredths("sympathy", -Core.Diplomacy.Relation.Max, Core.Diplomacy.Relation.Max) ?? 0,
                    Trust = x.SignedHundredths("trust", -Core.Diplomacy.Relation.Max, Core.Diplomacy.Relation.Max) ?? 0,
                    SympathyPerYear = x.SignedHundredths("sympathyPerYear", -Core.Diplomacy.Relation.Max, Core.Diplomacy.Relation.Max) ?? 0,
                    TrustPerYear = x.SignedHundredths("trustPerYear", -Core.Diplomacy.Relation.Max, Core.Diplomacy.Relation.Max) ?? 0,
                    Years = years,
                    VengefulYears = x.OptionalInt("vengefulYears", 0, 100_000) ?? years,
                };
                if (def.SympathyPerYear != 0 && Math.Sign(def.SympathyPerYear) != Math.Sign(def.Sympathy))
                {
                    errors.Add($"{at}.sympathyPerYear: must lead towards sympathy (same sign).");
                }

                if (def.TrustPerYear != 0 && Math.Sign(def.TrustPerYear) != Math.Sign(def.Trust))
                {
                    errors.Add($"{at}.trustPerYear: must lead towards trust (same sign).");
                }

                if (events[(int)kind] is not null)
                {
                    errors.Add($"{at}.kind: '{e.GetProperty("kind")}' is listed twice.");
                }

                events[(int)kind] = def;
                i++;
            }
        }

        for (int k = 0; k < kinds; k++)
        {
            if (events[k] is null)
            {
                errors.Add($"{file}: events: missing kind '{ToSnake(((Core.Diplomacy.RelationEvent)k).ToString())}'.");
            }
        }

        return new Core.Diplomacy.RelationRules
        {
            RumorChancePercentPerCentury = r.Int("rumorChancePercentPerCentury", 0, 100),
            RumorErrorPercent = r.Int("rumorErrorPercent", 0, 90),
            OpeningSympathy = r.SignedHundredths("openingSympathy", 0, Core.Diplomacy.Relation.Max) ?? 0,
            BorderOpening = r.SignedHundredths("borderOpening", -Core.Diplomacy.Relation.Max, Core.Diplomacy.Relation.Max) ?? 0,
            MaxMemories = r.Int("maxMemories", 1, 64),
            Events = Array.ConvertAll(events, e => e ?? new Core.Diplomacy.RelationEventDefinition()),
        };
    }

    private static Core.Diplomacy.TreatyRules LoadTreaties(string path, List<TechnologyDefinition> technologies, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new Core.Diplomacy.TreatyRules();
        }

        string file = Path.GetFileName(path);
        var r = new Reader(doc.RootElement, file, errors);
        int kinds = Enum.GetValues<Core.Diplomacy.TreatyKind>().Length;
        var defs = new Core.Diplomacy.TreatyKindDefinition?[kinds];
        if (TryArray(doc.RootElement, "treaties", path, errors, out JsonElement array))
        {
            int i = 0;
            foreach (JsonElement e in array.EnumerateArray())
            {
                string at = $"{file}: treaties[{i}]";
                var x = new Reader(e, at, errors);
                Core.Diplomacy.TreatyKind kind = x.Enum<Core.Diplomacy.TreatyKind>("kind");
                int tech = -1;
                if (e.TryGetProperty("requires", out JsonElement req) && req.ValueKind == JsonValueKind.String)
                {
                    string key = req.GetString()!;
                    tech = technologies.FindIndex(t => t.Key == key);
                    if (tech < 0)
                    {
                        errors.Add($"{at}.requires: unknown technology '{key}'.");
                    }
                }

                bool binds = e.TryGetProperty("bindsAgainstWar", out _) && x.Bool("bindsAgainstWar");
                if (defs[(int)kind] is not null)
                {
                    errors.Add($"{at}.kind: '{e.GetProperty("kind")}' is listed twice.");
                }

                defs[(int)kind] = new Core.Diplomacy.TreatyKindDefinition
                {
                    Kind = kind,
                    RequiresTechnology = tech,
                    Years = x.OptionalInt("years", 0, 100_000) ?? 0,
                    BindsAgainstWar = binds,
                };
                i++;
            }
        }

        for (int k = 0; k < kinds; k++)
        {
            if (defs[k] is null)
            {
                errors.Add($"{file}: treaties: missing kind '{ToSnake(((Core.Diplomacy.TreatyKind)k).ToString())}'.");
            }
        }

        if (defs[(int)Core.Diplomacy.TreatyKind.Peace] is { Years: 0 })
        {
            errors.Add($"{file}: treaties: peace needs years (the truce after a war).");
        }

        var defaults = new Core.Diplomacy.TreatyRules();
        JsonElement curves = doc.RootElement.TryGetProperty("curves", out JsonElement c) ? c : default;
        Core.Ai.Utility.ResponseCurve Curve(string name, int scale, Core.Ai.Utility.ResponseCurve fallback)
        {
            string at = $"{file}: curves.{name}";
            if (curves.ValueKind != JsonValueKind.Object || !curves.TryGetProperty(name, out JsonElement ce))
            {
                errors.Add($"{at}: required curve {{x0, f0, x1, f1}}.");
                return fallback;
            }

            var cr = new Reader(ce, at, errors);
            int x0 = cr.Int("x0", -1_000_000, 1_000_000);
            int x1 = cr.Int("x1", -1_000_000, 1_000_000);
            long f0 = cr.OptionalThousandths("f0", 0, 10_000) ?? 0;
            long f1 = cr.OptionalThousandths("f1", 0, 10_000) ?? 0;
            if (x1 <= x0)
            {
                errors.Add($"{at}: x1 must be greater than x0.");
                return fallback;
            }

            return Core.Ai.Utility.ResponseCurve.Linear((long)x0 * scale, (int)f0, (long)x1 * scale, (int)f1);
        }

        return new Core.Diplomacy.TreatyRules
        {
            ReviewEveryYears = r.Int("reviewEveryYears", 1, 10_000),
            ProposeAbove = (int)(r.OptionalThousandths("proposeAbove", 0, 100_000) ?? defaults.ProposeAbove),
            AcceptAbove = (int)(r.OptionalThousandths("acceptAbove", 0, 100_000) ?? defaults.AcceptAbove),
            DeclareWarAbove = (int)(r.OptionalThousandths("declareWarAbove", 0, 100_000) ?? defaults.DeclareWarAbove),
            RetryYears = r.Int("retryYears", 0, 100_000),
            PeaceRetryYears = r.Int("peaceRetryYears", 0, 100_000),
            ReachCells = r.Int("reachCells", 1, 1000),
            TradeSharePercent = r.Int("tradeSharePercent", 0, 100),
            WitnessPercent = r.Int("witnessPercent", 0, 100),
            AllyJoinBasePerMille = r.Int("allyJoinBasePerMille", 0, 1000),
            WarRaidPercent = r.Int("warRaidPercent", 0, 1000),
            Opinion = Curve("opinion", 100, defaults.Opinion),
            Trust = Curve("trust", 100, defaults.Trust),
            AllianceTrust = Curve("allianceTrust", 100, defaults.AllianceTrust),
            Threat = Curve("threat", 1, defaults.Threat),
            TradeGain = Curve("tradeGain", 1, defaults.TradeGain),
            Hostility = Curve("hostility", 100, defaults.Hostility),
            Advantage = Curve("advantage", 1, defaults.Advantage),
            WarWeariness = Curve("warWeariness", 1, defaults.WarWeariness),
            PeaceOpinion = Curve("peaceOpinion", 100, defaults.PeaceOpinion),
            PeaceTerms = Curve("peaceTerms", 1, defaults.PeaceTerms),
            WarScore = Curve("warScore", 1, defaults.WarScore),
            Greed = Curve("greed", 100, defaults.Greed),
            LootNeed = Curve("lootNeed", 1, defaults.LootNeed),
            LootGain = Curve("lootGain", 1, defaults.LootGain),
            ConquestChance = Curve("conquestChance", 1, defaults.ConquestChance),
            Kinds = Array.ConvertAll(defs, d => d ?? new Core.Diplomacy.TreatyKindDefinition()),
        };
    }

    // Battles in rounds (stage 4e PR 4): war.json battle, with defence bonuses of terrain.
    private static Core.Episodes.BattleRules LoadBattle(JsonElement root, string file, IReadOnlyList<BiomeDefinition> biomes, List<string> errors)
    {
        if (!root.TryGetProperty("battle", out JsonElement be) || be.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{file}: missing object 'battle'.");
            return new Core.Episodes.BattleRules();
        }

        var b = new Reader(be, $"{file}: battle", errors);
        var list = new List<Core.Episodes.BiomeDefence>();
        int hills = 0, mountains = 0, river = 0;
        if (be.TryGetProperty("terrain", out JsonElement te) && te.ValueKind == JsonValueKind.Object)
        {
            var t = new Reader(te, $"{file}: battle.terrain", errors);
            hills = t.Int("hillsPercent", 0, 1000);
            mountains = t.Int("mountainsPercent", 0, 1000);
            river = t.Int("riverPercent", 0, 1000);
            if (te.TryGetProperty("biomes", out JsonElement arr) && arr.ValueKind == JsonValueKind.Array)
            {
                int i = 0;
                foreach (JsonElement e in arr.EnumerateArray())
                {
                    string at = $"{file}: battle.terrain.biomes[{i}]";
                    var x = new Reader(e, at, errors);
                    string key = x.String("biome");
                    int id = -1;
                    for (int k = 0; k < biomes.Count; k++)
                    {
                        id = biomes[k].Key == key ? k : id;
                    }

                    if (id < 0)
                    {
                        errors.Add($"{at}.biome: unknown biome '{key}'.");
                    }

                    list.Add(new Core.Episodes.BiomeDefence(id, x.Int("percent", 0, 1000)));
                    i++;
                }
            }
        }
        else
        {
            errors.Add($"{file}: battle: missing object 'terrain'.");
        }

        return new Core.Episodes.BattleRules
        {
            Rounds = b.Int("rounds", 1, 100),
            CasualtyPercent = b.Int("casualtyPercent", 0, 100),
            RollPercent = b.Range("rollPercent", 0, 1000),
            MoralePerLossPercent = b.Int("moralePerLossPercent", 0, 100),
            BreakMorale = b.Int("breakMorale", 0, 100),
            DefenderMorale = b.Int("defenderMorale", 0, 100),
            HungryStrengthPercent = b.Int("hungryStrengthPercent", 0, 100),
            RoutLossPercent = b.Int("routLossPercent", 0, 100),
            HillsPercent = hills,
            MountainsPercent = mountains,
            RiverPercent = river,
            Biomes = list,
        };
    }

    private static Core.Diplomacy.WarRules LoadWar(string path, List<GoodDefinition> goods, List<TechnologyDefinition> technologies, List<BiomeDefinition> biomes, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new Core.Diplomacy.WarRules();
        }

        string file = Path.GetFileName(path);
        var r = new Reader(doc.RootElement, file, errors);
        int kinds = Enum.GetValues<Core.Diplomacy.WarGoal>().Length;
        var goals = new Core.Diplomacy.WarGoalDefinition?[kinds];
        if (TryArray(doc.RootElement, "goals", path, errors, out JsonElement array))
        {
            int i = 0;
            foreach (JsonElement e in array.EnumerateArray())
            {
                string at = $"{file}: goals[{i}]";
                var x = new Reader(e, at, errors);
                Core.Diplomacy.WarGoal goal = x.Enum<Core.Diplomacy.WarGoal>("goal");
                if (goals[(int)goal] is not null)
                {
                    errors.Add($"{at}.goal: '{e.GetProperty("goal")}' is listed twice.");
                }

                goals[(int)goal] = new Core.Diplomacy.WarGoalDefinition
                {
                    Goal = goal,
                    Name = x.String("name"),
                    VictimPercent = x.Int("victimPercent", 0, 1000),
                    WitnessPercent = x.Int("witnessPercent", 0, 1000),
                };
                i++;
            }
        }

        for (int k = 0; k < kinds; k++)
        {
            if (goals[k] is null)
            {
                errors.Add($"{file}: goals: missing goal '{ToSnake(((Core.Diplomacy.WarGoal)k).ToString())}'.");
            }
        }

        var armaments = new List<Core.Diplomacy.ArmamentDefinition>();
        if (TryArray(doc.RootElement, "armaments", path, errors, out JsonElement arms))
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            bool bare = false;
            int i = 0;
            foreach (JsonElement e in arms.EnumerateArray())
            {
                string at = $"{file}: armaments[{i}]";
                bare |= !e.TryGetProperty("technology", out _) && !e.TryGetProperty("good", out _);
                var x = new Reader(e, at, errors);
                string key = x.Key("key", keys);
                int tech = -1;
                if (e.TryGetProperty("technology", out JsonElement te))
                {
                    tech = technologies.FindIndex(t => t.Key == te.GetString());
                    if (tech < 0)
                    {
                        errors.Add($"{at}.technology: unknown technology '{te}'.");
                    }
                }

                int good = -1;
                Amount per = Amount.Zero;
                if (e.TryGetProperty("good", out JsonElement ge))
                {
                    good = goods.FindIndex(g => g.Key == ge.GetString());
                    if (good < 0)
                    {
                        errors.Add($"{at}.good: unknown good '{ge}'.");
                    }

                    per = x.Amount("perWarrior", Amount.FromHundredths(1), Amount.FromUnits(1000));
                }

                armaments.Add(new Core.Diplomacy.ArmamentDefinition
                {
                    Id = i,
                    Key = key,
                    Name = x.String("name"),
                    Technology = tech,
                    Good = good,
                    PerWarrior = per,
                    StrengthPercent = x.Int("strengthPercent", 1, 10_000),
                });
                i++;
            }

            if (!bare)
            {
                errors.Add($"{file}: armaments: one level needs neither a technology nor a good (bare hands).");
            }
        }

        JsonElement supplyElement = JsonDocument.Parse("{}").RootElement.Clone();
        if (doc.RootElement.TryGetProperty("supply", out JsonElement se) && se.ValueKind == JsonValueKind.Object)
        {
            supplyElement = se;
        }
        else
        {
            errors.Add($"{file}: missing object 'supply'.");
        }

        var supply = new Reader(supplyElement, $"{file}: supply", errors);
        Core.Episodes.BattleRules battle = LoadBattle(doc.RootElement, file, biomes, errors);

        var defaults = new Core.Diplomacy.WarRules();
        return new Core.Diplomacy.WarRules
        {
            Goals = Array.ConvertAll(goals, g => g ?? new Core.Diplomacy.WarGoalDefinition()),
            Armaments = armaments.Count > 0 ? armaments : defaults.Armaments,
            MusterCells = r.Int("musterCells", 0, 1000),
            Battle = battle,
            FoodPerWarriorPerYear = supply.Amount("foodPerWarriorPerYear", Amount.Zero, Amount.FromUnits(100)),
            CarryFoodMonths = supply.Int("carryFoodMonths", 0, 120),
            SupplySharePercent = supply.Int("supplySharePercent", 0, 100),
            ConvoyBelowMonths = supply.Int("convoyBelowMonths", 0, 120),
            CarrierCells = supply.Int("carrierCells", 0, 1000),
            CarriersPercent = supply.Int("carriersPercent", 0, 100),
            ConvoyCellsPerMonth = supply.Int("convoyCellsPerMonth", 1, 100),
            HungerMoralePerMonth = supply.Int("hungerMoralePerMonth", 0, 100),
            FedMoralePerMonth = supply.Int("fedMoralePerMonth", 0, 100),
            RetreatMorale = supply.Int("retreatMorale", 0, 100),
            ForageCells = supply.Int("forageCells", 0, 1000),
            ScorePerWin = r.Int("scorePerWin", 0, 100),
            ScorePerSettlement = r.Int("scorePerSettlement", 0, 100),
            WearinessPerCampaignMonth = r.SignedHundredths("wearinessPerCampaignMonth", 0, 10_000) ?? defaults.WearinessPerCampaignMonth,
            CampaignStepMonths = r.Int("campaignStepMonths", 1, 120),
            MarchCellsPerMonth = r.Int("marchCellsPerMonth", 1, 100),
            QuietPeaceYears = r.Int("quietPeaceYears", 1, 1000),
            ArmySightCells = r.Int("armySightCells", 0, 100),
            SendPercent = r.Int("sendPercent", 1, 100),
            MinWarriors = r.Int("minWarriors", 1, 10_000),
            WearinessPerLossPercent = r.SignedHundredths("wearinessPerLossPercent", 0, 10_000) ?? defaults.WearinessPerLossPercent,
            PeaceAbove = r.Int("peaceAbove", 0, 100),
            MoodAtFullWeariness = r.Int("moodAtFullWeariness", 0, 100),
            TermsAboveScore = r.Int("termsAboveScore", 1, 100),
            RansomPercentPerScorePoint = r.SignedHundredths("ransomPercentPerScorePoint", 1, 10_000) ?? defaults.RansomPercentPerScorePoint,
            MaxRansomPercent = r.Int("maxRansomPercent", 1, 100),
            CedeWorth = r.Int("cedeWorth", 0, 100),
        };
    }

    private static TradeRules LoadTrade(string path, List<string> errors)
    {
        using JsonDocument? doc = Open(path, errors);
        if (doc is null)
        {
            return new TradeRules();
        }

        var r = new Reader(doc.RootElement, Path.GetFileName(path), errors);
        return new TradeRules
        {
            LinkCells = r.Int("linkCells", 1, 100),
            LossPercentPerCell = r.Int("lossPercentPerCell", 0, 90),
            FoodReserveYears = r.Amount("foodReserveYears", Amount.Zero, Amount.FromUnits(20)),
            PriceRangePercent = r.Range("priceRangePercent", 1, 10_000),
        };
    }

    private static List<EffectDefinition> LoadEffects(string path, List<string> errors)
    {
        var result = new List<EffectDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "effects", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: effects[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            EffectTarget target = r.Enum<EffectTarget>("target");
            EffectStat stat = r.Enum<EffectStat>("stat");
            EffectStacking stacking = r.Enum<EffectStacking>("stacking");
            result.Add(new EffectDefinition(i, key, name, target, stat, stacking));
            i++;
        }

        return result;
    }

    private static List<BiomeDefinition> LoadBiomes(string path, List<GoodDefinition> goods, List<string> errors)
    {
        var result = new List<BiomeDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "biomes", path, errors, out JsonElement array))
        {
            return result;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{Path.GetFileName(path)}: biomes[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            BiomeKind kind = r.Enum<BiomeKind>("kind");
            (int tMin, int tMax) = kind == BiomeKind.Land ? r.Range("temperature", -30, 40) : (0, 0);
            (int hMin, int hMax) = kind == BiomeKind.Land ? r.Range("humidity", 0, 100) : (0, 0);
            int move = r.Int("movementCost", 1, 10);
            int fert = r.Int("baseFertility", 0, 100);
            int food = r.Int("food", 0, 10);
            uint color = r.Color("color");
            List<GoodQuantity> gather = e.TryGetProperty("gather", out _) ? r.Goods("gather", goods) : [];
            result.Add(new BiomeDefinition(i, key, name, kind, tMin, tMax, hMin, hMax, move, fert, food, color) { Gather = gather });
            i++;
        }

        if (result.Count > byte.MaxValue)
        {
            errors.Add($"{Path.GetFileName(path)}: at most 255 biomes are supported.");
        }

        return result;
    }

    private static List<DepositDefinition> LoadDeposits(string path, List<BiomeDefinition> biomes, List<GoodDefinition> goods, List<string> errors)
    {
        var result = new List<DepositDefinition>();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "deposits", path, errors, out JsonElement array))
        {
            return result;
        }

        var biomeKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (BiomeDefinition b in biomes)
        {
            biomeKeys.Add(b.Key);
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            string at = $"{Path.GetFileName(path)}: deposits[{i}]";
            var r = new Reader(e, at, errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            List<string> inBiomes = r.StringList("biomes");
            foreach (string b in inBiomes)
            {
                if (!biomeKeys.Contains(b))
                {
                    errors.Add($"{at}.biomes: unknown biome '{b}'.");
                }
            }

            var relief = new List<Relief>();
            foreach (string s in r.StringList("relief"))
            {
                if (TryRelief(s, out Relief rel))
                {
                    relief.Add(rel);
                }
                else
                {
                    errors.Add($"{at}.relief: '{s}' must be flat, hills or mountains.");
                }
            }

            int per = r.Int("perThousandCells", 1, 1000);
            (int rMin, int rMax) = r.Range("richness", 1, 1_000_000);
            int depth = r.Int("depth", 0, 2);
            bool renewable = r.Bool("renewable");
            uint color = r.Color("color");
            List<GoodQuantity> gather = e.TryGetProperty("gather", out _) ? r.Goods("gather", goods) : [];
            result.Add(new DepositDefinition(i + 1, key, name, inBiomes, relief, per, rMin, rMax, depth, renewable, color) { Gather = gather });
            i++;
        }

        return result;
    }

    private static void ValidateBiomeSet(List<BiomeDefinition> biomes, List<string> errors)
    {
        foreach (BiomeKind kind in new[] { BiomeKind.DeepOcean, BiomeKind.ShallowSea, BiomeKind.Coast, BiomeKind.Lake })
        {
            int n = biomes.FindAll(b => b.Kind == kind).Count;
            if (n != 1)
            {
                errors.Add($"biomes.json: exactly one biome of kind '{ToSnake(kind.ToString())}' is required, found {n}.");
            }
        }

        if (!biomes.Exists(b => b.Key == "grassland" && b.Kind == BiomeKind.Land))
        {
            errors.Add("biomes.json: a land biome 'grassland' is required (fallback for cells no rule matches).");
        }
    }

    private static JsonDocument? Open(string path, List<string> errors)
    {
        if (!File.Exists(path))
        {
            errors.Add($"{path}: file not found.");
            return null;
        }

        try
        {
            return JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            errors.Add($"{Path.GetFileName(path)}: invalid JSON at line {ex.LineNumber + 1}: {ex.Message}");
            return null;
        }
    }

    private static bool TryArray(JsonElement root, string name, string path, List<string> errors, out JsonElement array)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        errors.Add($"{Path.GetFileName(path)}: missing array '{name}'.");
        array = default;
        return false;
    }

    private static bool TryRelief(string s, out Relief relief)
    {
        switch (s)
        {
            case "flat":
                relief = Relief.Flat;
                return true;
            case "hills":
                relief = Relief.Hills;
                return true;
            case "mountains":
                relief = Relief.Mountains;
                return true;
            default:
                relief = Relief.Flat;
                return false;
        }
    }

    private static string ToSnake(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            if (i > 0 && char.IsUpper(pascal[i]))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(pascal[i]));
        }

        return sb.ToString();
    }

    private readonly struct Reader(JsonElement element, string at, List<string> errors)
    {
        public string String(string name)
        {
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()))
            {
                return v.GetString()!;
            }

            errors.Add($"{at}.{name}: required text.");
            return string.Empty;
        }

        public string Key(string name, HashSet<string> seen)
        {
            string key = String(name);
            if (key.Length == 0)
            {
                return key;
            }

            foreach (char c in key)
            {
                if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_'))
                {
                    errors.Add($"{at}.{name}: '{key}' may contain only a-z, 0-9 and _.");
                    break;
                }
            }

            if (!seen.Add(key))
            {
                errors.Add($"{at}.{name}: duplicate key '{key}'.");
            }

            return key;
        }

        /// <summary>An optional whole number; null when the property is missing.</summary>
        public int? OptionalInt(string name, int min, int max) => element.TryGetProperty(name, out _) ? Int(name, min, max) : null;

        public int Int(string name, int min, int max)
        {
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n))
            {
                if (n < min || n > max)
                {
                    errors.Add($"{at}.{name}: {n} is outside {min}..{max}.");
                }

                return n;
            }

            errors.Add($"{at}.{name}: required whole number.");
            return min;
        }

        /// <summary>
        /// An optional decimal with up to 3 places ("1.524" or 84), in thousandths; null when the property is missing.
        /// Parsed digit by digit, without floating point.
        /// </summary>
        public long? OptionalThousandths(string name, long min, long max)
        {
            if (!element.TryGetProperty(name, out JsonElement v))
            {
                return null;
            }

            string? text = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };
            if (!TryThousandths(text, out long value))
            {
                errors.Add($"{at}.{name}: required number such as \"1.524\" (at most 3 decimal places).");
                return null;
            }

            if (value < min || value > max)
            {
                errors.Add($"{at}.{name}: {value / 1000}.{value % 1000:D3} is outside {min / 1000}.{min % 1000:D3}..{max / 1000}.{max % 1000:D3}.");
            }

            return value;
        }

        private static bool TryThousandths(string? text, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int dot = text.IndexOf('.', StringComparison.Ordinal);
            string whole = dot < 0 ? text : text[..dot];
            string frac = dot < 0 ? string.Empty : text[(dot + 1)..];
            if (whole.Length == 0 || whole.Length > 9 || frac.Length > 3)
            {
                return false;
            }

            foreach (char ch in whole + frac)
            {
                if (ch is < '0' or > '9')
                {
                    return false;
                }
            }

            value = (long.Parse(whole, System.Globalization.CultureInfo.InvariantCulture) * 1000)
                + (frac.Length == 0 ? 0 : long.Parse(frac.PadRight(3, '0'), System.Globalization.CultureInfo.InvariantCulture));
            return true;
        }

        /// <summary>
        /// An optional signed decimal with up to 2 places ("-0.5" or 30), in hundredths; null when the property is missing.
        /// </summary>
        public int? SignedHundredths(string name, int min, int max)
        {
            if (!element.TryGetProperty(name, out JsonElement v))
            {
                return null;
            }

            string? text = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };
            bool negative = text is { Length: > 0 } && text[0] == '-';
            if (!TryThousandths(negative ? text![1..] : text, out long thousandths) || thousandths % 10 != 0)
            {
                errors.Add($"{at}.{name}: required number such as \"-0.5\" (at most 2 decimal places).");
                return null;
            }

            long value = (negative ? -thousandths : thousandths) / 10;
            if (value < min || value > max)
            {
                errors.Add($"{at}.{name}: {text} is outside {min / 100}..{max / 100}.");
                return null;
            }

            return (int)value;
        }

        public Amount Amount(string name, Amount min, Amount max) =>
            ParseAmount(name, element.TryGetProperty(name, out JsonElement v) ? v : default, min, max);

        public List<GoodQuantity> Goods(string name, List<GoodDefinition> goods)
        {
            var list = new List<GoodQuantity>();
            if (!element.TryGetProperty(name, out JsonElement v) || v.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{at}.{name}: required object of good keys and amounts.");
                return list;
            }

            foreach (JsonProperty p in v.EnumerateObject())
            {
                int id = goods.FindIndex(g => g.Key == p.Name);
                Amount amount = ParseAmount($"{name}.{p.Name}", p.Value, Core.Numerics.Amount.Epsilon, Core.Numerics.Amount.FromUnits(1_000_000));
                if (id < 0)
                {
                    errors.Add($"{at}.{name}: unknown good '{p.Name}'.");
                    continue;
                }

                if (list.Exists(q => q.GoodId == id))
                {
                    errors.Add($"{at}.{name}: good '{p.Name}' is listed twice.");
                    continue;
                }

                list.Add(new GoodQuantity(id, amount));
            }

            // Fixed order by good id, so nothing depends on the order in the file.
            list.Sort((a, b) => a.GoodId.CompareTo(b.GoodId));
            return list;
        }

        private Amount ParseAmount(string name, JsonElement v, Amount min, Amount max)
        {
            string? text = v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null,
            };
            if (!Core.Numerics.Amount.TryParse(text, out Amount amount))
            {
                errors.Add($"{at}.{name}: required amount such as \"12\" or \"0.25\" (at most 2 decimal places).");
                return min;
            }

            if (amount < min || amount > max)
            {
                errors.Add($"{at}.{name}: {amount} is outside {min}..{max}.");
            }

            return amount;
        }

        public bool Bool(string name)
        {
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return v.GetBoolean();
            }

            errors.Add($"{at}.{name}: required true or false.");
            return false;
        }

        public (int Min, int Max) Range(string name, int min, int max)
        {
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == 2
                && v[0].TryGetInt32(out int a) && v[1].TryGetInt32(out int b))
            {
                if (a > b || a < min || b > max)
                {
                    errors.Add($"{at}.{name}: [{a}, {b}] must be ordered and inside {min}..{max}.");
                }

                return (a, b);
            }

            errors.Add($"{at}.{name}: required [min, max].");
            return (min, max);
        }

        public List<string> StringList(string name)
        {
            var list = new List<string>();
            if (element.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0)
            {
                foreach (JsonElement s in v.EnumerateArray())
                {
                    if (s.ValueKind == JsonValueKind.String)
                    {
                        list.Add(s.GetString()!);
                    }
                    else
                    {
                        errors.Add($"{at}.{name}: every entry must be text.");
                    }
                }

                return list;
            }

            errors.Add($"{at}.{name}: required non-empty list.");
            return list;
        }

        public T Enum<T>(string name)
            where T : struct, System.Enum
        {
            string s = String(name);
            foreach (T value in System.Enum.GetValues<T>())
            {
                if (ToSnake(value.ToString()) == s)
                {
                    return value;
                }
            }

            if (s.Length > 0)
            {
                errors.Add($"{at}.{name}: unknown value '{s}'.");
            }

            return default;
        }

        public uint Color(string name)
        {
            string s = String(name);
            if (s.Length == 7 && s[0] == '#' && uint.TryParse(s.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            {
                return rgb;
            }

            if (s.Length > 0)
            {
                errors.Add($"{at}.{name}: '{s}' must look like #RRGGBB.");
            }

            return 0;
        }
    }
}

/// <summary>Game content failed validation.</summary>
public sealed class ContentException : Exception
{
    /// <summary>Creates the exception from a list of problems.</summary>
    public ContentException(IReadOnlyList<string> errors)
        : base("Game data is invalid:" + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", errors))
    {
        Errors = errors;
    }

    /// <summary>Every problem found.</summary>
    public IReadOnlyList<string> Errors { get; }
}
