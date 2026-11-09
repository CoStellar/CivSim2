using System;
using System.Collections.Generic;

namespace CivSim.Core.Content;

/// <summary>
/// All game content loaded from <c>data/</c>. Created by <c>CivSim.Data</c>; the core only reads it.
/// Lists are ordered by id and ids are dense from 0 (biomes) or 1 (deposits; 0 means "none" on the map).
/// </summary>
public sealed class GameContent
{
    private readonly Dictionary<string, BiomeDefinition> _biomesByKey = new(StringComparer.Ordinal);

    /// <summary>Creates the content. Call only with validated data.</summary>
    public GameContent(IReadOnlyList<BiomeDefinition> biomes, IReadOnlyList<DepositDefinition> deposits)
    {
        Biomes = biomes;
        Deposits = deposits;
        foreach (BiomeDefinition b in biomes)
        {
            _biomesByKey.Add(b.Key, b);
        }
    }

    /// <summary>Biomes, index = id.</summary>
    public IReadOnlyList<BiomeDefinition> Biomes { get; }

    /// <summary>Deposits; <c>Deposits[i].Id == i + 1</c>.</summary>
    public IReadOnlyList<DepositDefinition> Deposits { get; }

    /// <summary>Finds a biome by key, e.g. "grassland".</summary>
    public BiomeDefinition Biome(string key) =>
        _biomesByKey.TryGetValue(key, out BiomeDefinition? b) ? b : throw new KeyNotFoundException($"Unknown biome '{key}'.");

    /// <summary>The single biome of a water kind.</summary>
    public BiomeDefinition WaterBiome(BiomeKind kind)
    {
        foreach (BiomeDefinition b in Biomes)
        {
            if (b.Kind == kind)
            {
                return b;
            }
        }

        throw new KeyNotFoundException($"No biome of kind {kind}.");
    }

    /// <summary>The deposit with this map id (1-based).</summary>
    public DepositDefinition Deposit(int id) => Deposits[id - 1];

    /// <summary>Goods, index = id (<c>data/goods.json</c>).</summary>
    public IReadOnlyList<GoodDefinition> Goods { get; init; } = [];

    /// <summary>Production recipes, index = id (<c>data/recipes.json</c>).</summary>
    public IReadOnlyList<RecipeDefinition> Recipes { get; init; } = [];

    /// <summary>Technologies (data/technologies.json, stage 3f), index = id.</summary>
    public IReadOnlyList<TechnologyDefinition> Technologies { get; init; } = [];

    /// <summary>Random events (data/events.json, stage 3h), index = id.</summary>
    public IReadOnlyList<EventDefinition> Events { get; init; } = [];

    /// <summary>Sight, map memory and journeys (data/perception.json, stage 3g).</summary>
    public PerceptionRules Perception { get; init; } = new();

    /// <summary>How the AI advisors decide (data/ai.json, stage 4a).</summary>
    public AiRules Ai { get; init; } = new();

    /// <summary>Contact, rumors and the memory of relations (data/relations.json, stage 4c).</summary>
    public Diplomacy.RelationRules Relations { get; init; } = new();

    /// <summary>Peoples' languages (data/names.json, GDD 5.6).</summary>
    public Naming.NameRules Names { get; init; } = new();

    /// <summary>Diplomacy and trade between civilizations (data/diplomacy.json, stage 4d).</summary>
    public Diplomacy.TreatyRules Treaties { get; init; } = new();

    /// <summary>War: goals, score, weariness, terms of peace (data/war.json, stage 4e).</summary>
    public Diplomacy.WarRules War { get; init; } = new();

    /// <summary>Research and diffusion rules (stage 3f).</summary>
    public ResearchRules Research { get; init; } = new();

    /// <summary>Technology by key.</summary>
    public TechnologyDefinition Technology(string key) => Find(Technologies, t => t.Key, key, "technology");

    /// <summary>Trade network and internal prices (data/trade.json, stage 3e).</summary>
    public TradeRules Trade { get; init; } = new();

    /// <summary>Settlement levels from smallest to largest, index = id (<c>data/settlements.json</c>).</summary>
    public IReadOnlyList<SettlementLevelDefinition> SettlementLevels { get; init; } = [];

    /// <summary>Eras in order, index = id (<c>data/eras.json</c>).</summary>
    public IReadOnlyList<EraDefinition> Eras { get; init; } = [];

    /// <summary>Effect types, index = id (<c>data/effects.json</c>).</summary>
    public IReadOnlyList<EffectDefinition> Effects { get; init; } = [];

    /// <summary>Population strata, index = id (<c>data/strata.json</c>).</summary>
    public IReadOnlyList<StratumDefinition> Strata { get; init; } = [];

    /// <summary>Skills, index = id (<c>data/skills.json</c>, stage 4F).</summary>
    public IReadOnlyList<SkillDefinition> Skills { get; init; } = [];

    /// <summary>How skills are learned and passed on (<c>data/skills.json</c>, stage 4F).</summary>
    public SkillRules SkillRules { get; init; } = new();

    /// <summary>Which skill works which recipe and which technology's gathering.</summary>
    public SkillLinks SkillLinks { get; init; } = SkillLinks.None;

    /// <summary>Forms of government, index = id (<c>data/governments.json</c>, stage 4f). Empty in hand-made test content.</summary>
    public IReadOnlyList<GovernmentDefinition> Governments { get; init; } = [];

    /// <summary>Leaders: traits, ages, death (<c>data/leaders.json</c>, stage 4f PR 2).</summary>
    public LeaderRules Leaders { get; init; } = new();

    /// <summary>Stability and administration (<c>data/stability.json</c>, stage 4f PR 3).</summary>
    public StabilityRules Stability { get; init; } = new();

    /// <summary>Growth, health and gathering rules (<c>data/population.json</c>).</summary>
    public PopulationRules Population { get; init; } = new();

    /// <summary>When seasons are played turn by turn (data/planets.json, "seasons").</summary>
    public WorldGen.SeasonRules Seasons { get; init; } = new();

    /// <summary>Milankovitch cycles and ice sheets (data/planets.json "climateCycles", stage 1c-B).</summary>
    public WorldGen.ClimateCycleRules ClimateCycles { get; init; } = new();

    /// <summary>Planet presets (stage 1b).</summary>
    public IReadOnlyList<PlanetPreset> Planets { get; init; } = [];

    /// <summary>The planet preset with <paramref name="key"/>.</summary>
    public PlanetPreset Planet(string key) => Find(Planets, p => p.Key, key, "planet");

    /// <summary>Claims and carrying (GDD 5.5).</summary>
    public AssetRules Assets { get; init; } = new();

    /// <summary>Level of the camp a new tribe starts with.</summary>
    public int NewTribeLevel { get; init; }

    /// <summary>Goods in a new tribe's camp.</summary>
    public IReadOnlyList<GoodQuantity> NewTribeStock { get; init; } = [];

    /// <summary>Finds a good by key, e.g. "grain".</summary>
    public GoodDefinition Good(string key) => Find(Goods, g => g.Key, key, "good");

    /// <summary>Finds an effect type by key, e.g. "damage".</summary>
    public EffectDefinition Effect(string key) => Find(Effects, e => e.Key, key, "effect");

    /// <summary>Finds a settlement level by key, e.g. "camp".</summary>
    public SettlementLevelDefinition SettlementLevel(string key) => Find(SettlementLevels, l => l.Key, key, "settlement level");

    private static T Find<T>(IReadOnlyList<T> list, Func<T, string> key, string wanted, string what)
    {
        foreach (T item in list)
        {
            if (key(item) == wanted)
            {
                return item;
            }
        }

        throw new KeyNotFoundException($"Unknown {what} '{wanted}'.");
    }
}
