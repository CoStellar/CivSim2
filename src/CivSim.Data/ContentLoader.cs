using System;
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
        List<BiomeDefinition> biomes = LoadBiomes(Path.Combine(directory, "biomes.json"), errors);
        List<GoodDefinition> goods = LoadGoods(Path.Combine(directory, "goods.json"), errors);
        List<DepositDefinition> deposits = LoadDeposits(Path.Combine(directory, "resources.json"), biomes, goods, errors);
        ValidateBiomeSet(biomes, errors);
        List<StratumDefinition> strata = LoadStrata(Path.Combine(directory, "strata.json"), goods, errors);
        PopulationRules population = LoadPopulation(Path.Combine(directory, "population.json"), strata, goods, errors);
        List<RecipeDefinition> recipes = LoadRecipes(Path.Combine(directory, "recipes.json"), goods, errors);
        (List<SettlementLevelDefinition> levels, int tribeLevel, List<GoodQuantity> tribeStock, AssetRules assets) =
            LoadSettlements(Path.Combine(directory, "settlements.json"), goods, errors);
        List<EraDefinition> eras = LoadEras(Path.Combine(directory, "eras.json"), errors);
        List<EffectDefinition> effects = LoadEffects(Path.Combine(directory, "effects.json"), errors);
        (List<PlanetPreset> planets, Core.WorldGen.SeasonRules seasons) = LoadPlanets(Path.Combine(directory, "planets.json"), errors);
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
            Population = population,
            Assets = assets,
            Planets = planets,
            Seasons = seasons,
        };
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
            result.Add(new StratumDefinition(i, key, name, gathers, share, needs) { Fights = fights });
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
            BirthsPerMille = growth.Int("birthsPerMille", 0, 1000),
            DeathsPerMille = growth.Int("deathsPerMille", 0, 1000),
            StarvationDeathPercent = growth.Int("starvationDeathPercent", 0, 100),
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
            MaxMoveCells = migration.Int("maxMoveCells", 0, 50),
            MoveWhenFedBelowPercent = migration.Int("moveWhenFedBelowPercent", 0, 100),
            MoveWhenBetterByPercent = migration.Int("moveWhenBetterByPercent", 0, 1000),
            MinTurnsBetweenMoves = migration.Int("minTurnsBetweenMoves", 0, 1000),
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
            if (category != GoodCategory.Food && spoilage > Amount.Zero)
            {
                // CORE simplified (GDD 16): only food spoils.
                errors.Add($"{Path.GetFileName(path)}: goods[{i}].spoilagePerYear: only food may spoil.");
            }

            result.Add(new GoodDefinition(i, key, name, era, category, (int)spoilage.Hundredths, color));
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

            levels.Add(new SettlementLevelDefinition(i, key, name, min, max, storage, mobile) { AbandonedYears = abandoned, RuinsUntilYears = ruins });
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
        };

        return (levels, Math.Max(0, tribeLevel), stock, assets);
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
            result.Add(new EraDefinition(i, key, name, months));
            i++;
        }

        if (result.Count == 0)
        {
            errors.Add($"{Path.GetFileName(path)}: at least one era is required.");
        }

        return result;
    }

    private static (List<PlanetPreset> Planets, Core.WorldGen.SeasonRules Seasons) LoadPlanets(string path, List<string> errors)
    {
        var result = new List<PlanetPreset>();
        var seasons = new Core.WorldGen.SeasonRules();
        using JsonDocument? doc = Open(path, errors);
        if (doc is null || !TryArray(doc.RootElement, "planets", path, errors, out JsonElement array))
        {
            return (result, seasons);
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

        var keys = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
        foreach (JsonElement e in array.EnumerateArray())
        {
            var r = new Reader(e, $"{Path.GetFileName(path)}: planets[{i}]", errors);
            string key = r.Key("key", keys);
            string name = r.String("name");
            (int tMin, int tMax) = Core.WorldGen.PlanetParameters.TiltRange;
            (int yMin, int yMax) = Core.WorldGen.PlanetParameters.YearRange;
            (int bMin, int bMax) = Core.WorldGen.PlanetParameters.BrightnessRange;
            (int eMin, int eMax) = Core.WorldGen.PlanetParameters.EccentricityRange;
            var planet = new Core.WorldGen.PlanetParameters(
                (int)(r.Amount("axialTilt", Amount.FromHundredths(tMin * 10), Amount.FromHundredths(tMax * 10)).Hundredths / 10),
                (int)r.Amount("yearLength", Amount.FromHundredths(yMin), Amount.FromHundredths(yMax)).Hundredths,
                r.Int("starBrightness", bMin, bMax),
                r.Int("eccentricityPerMille", eMin, eMax))
            {
                SeasonStrengthPercent = r.Int("seasonStrength", Core.WorldGen.PlanetParameters.SeasonStrengthRange.Min, Core.WorldGen.PlanetParameters.SeasonStrengthRange.Max),
            };
            int water = r.Int("water", 30, 80);
            Core.WorldGen.WorldMoisture moisture = r.Enum<Core.WorldGen.WorldMoisture>("moisture");
            Core.WorldGen.WorldRelief relief = r.Enum<Core.WorldGen.WorldRelief>("relief");
            result.Add(new PlanetPreset(key, name, planet, water, moisture, relief));
            i++;
        }

        if (!result.Exists(p => p.Key == "earth"))
        {
            errors.Add($"{Path.GetFileName(path)}: the 'earth' preset is required.");
        }

        return (result, seasons);
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

    private static List<BiomeDefinition> LoadBiomes(string path, List<string> errors)
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
            result.Add(new BiomeDefinition(i, key, name, kind, tMin, tMax, hMin, hMax, move, fert, food, color));
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
