using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using CivSim.Core.Content;

namespace CivSim.Sim.Content;

/// <summary>A good the player stores (food, wood, stone…). Ids are list indexes.</summary>
public sealed record SimGood(int Id, string Key, string Name)
{
    /// <summary>Worth of one unit for barter, percent of food.</summary>
    public int WorthPercent { get; init; } = 100;
}

/// <summary>A level of a settlement: territory radius, housing and storage it gives, price of reaching it.</summary>
public sealed record SettlementLevel(int Id, string Key, string Name, int Radius, int Housing, long Storage, long[] UpgradePrice)
{
    /// <summary>Technology needed to reach this level (−1 = none).</summary>
    public int Tech { get; init; } = -1;

    /// <summary>Other technologies needed as well (a level can need an ordinary and a political technology).</summary>
    public int[] AlsoTechs { get; init; } = [];
}

/// <summary>An era (Stone Age … Middle Ages).</summary>
public sealed record SimEra(int Id, string Key, string Name);

/// <summary>What a technology effect changes.</summary>
public enum EffectKind
{
    /// <summary>Production of a building (target: building key or *), percent.</summary>
    Production,

    /// <summary>Homes of a building, added.</summary>
    Housing,

    /// <summary>Room of a building (target: key or *), percent.</summary>
    Storage,

    /// <summary>Strength of a unit (target: unit key or *), percent.</summary>
    Strength,

    /// <summary>Walking speed of a unit (target: unit key, carriers or *), percent faster.</summary>
    Speed,

    /// <summary>Training time of a unit (target: unit key or *), percent shorter.</summary>
    Training,

    /// <summary>Research price, percent cheaper.</summary>
    Research,

    /// <summary>Growth, percent of people per minute added.</summary>
    Growth,

    /// <summary>Harm of an event (target: event key), percent less.</summary>
    Resist,

    /// <summary>Damage to settlements (target: unit key or *), percent.</summary>
    Siege,

    /// <summary>Defence of fortifications (target: building key or *), percent.</summary>
    Defense,

    /// <summary>Chance of rarer thinkers and advisors, percent.</summary>
    Luck,

    /// <summary>Load a carrier person takes (target: unit key or carriers), whole units added.</summary>
    Cargo,

    /// <summary>Least fertility a building needs (target: building key or group), points lower.</summary>
    Fertility,
}

/// <summary>One effect of a technology.</summary>
public sealed record TechEffect(EffectKind Kind, string Target, int Value);

/// <summary>A technology: experience needed in earlier technologies (worker-ticks), price (hundredths) and time (ticks).</summary>
public sealed record SimTech(int Id, string Key, string Name, int Era, bool KnownAtStart, (int Tech, long WorkerTicks)[] Experience, long[] Price, int ResearchTicks)
{
    /// <summary>What it changes besides what it unlocks.</summary>
    public TechEffect[] Effects { get; init; } = [];

    /// <summary>An upgrade of its era (better arms, tools…) rather than a base technology.</summary>
    public bool Upgrade { get; init; }

    /// <summary>One line on what it gives, for the player.</summary>
    public string Gives { get; init; } = "";

    /// <summary>Branch of the tree (basics, food, industry, building, military, governance, trade, science).</summary>
    public string Category { get; init; } = "";

    /// <summary>Technologies that must be known first, with no experience asked in them.</summary>
    public int[] Requires { get; init; } = [];

    /// <summary>Goods the people must have made in all (hundredths): experience gained by making, not by working time.</summary>
    public (int Good, long Amount)[] Made { get; init; } = [];

    /// <summary>A first step of the basics branch: it does not count toward the next era.</summary>
    public bool Basic => Category == SimContent.BasicsCategory;
}

/// <summary>A work drawn on hexes (ditch, palisade, wall, aqueduct): price and time per hex and what it does.</summary>
public sealed record WorkKind(int Id, string Key, string Name, int Tech, long[] Price, int TicksPerHex)
{
    /// <summary>Its bit in <c>SimGame.WorksAt</c>.</summary>
    public int Bit => 1 << Id;

    /// <summary>Longest reach from water, hexes (ditches, aqueducts; 0 = needs no water).</summary>
    public int FromWater { get; init; }

    /// <summary>Farms on a watered hex make this much more, percent.</summary>
    public int FarmPercent { get; init; }

    /// <summary>Enemies at war standing on it lose this share of their people a second, percent.</summary>
    public int WoundPercentPerSecond { get; init; }

    /// <summary>Damage it takes before it breaks, hundredths (walls; 0 = does not stop anyone).</summary>
    public long Strength { get; init; }

    /// <summary>Growth a settlement it reaches gains, percent of people a minute.</summary>
    public int GrowthBonus { get; init; }

    /// <summary>Epidemics in a settlement it reaches are this much milder, percent.</summary>
    public int ResistEpidemicPercent { get; init; }
}

/// <summary>A level of road: price and time per hex, walking cost on it (percent).</summary>
public sealed record RoadLevel(int Level, string Key, string Name, int Tech, long[] Price, int TicksPerHex, int WalkPercent);

/// <summary>A rarity of thinkers and advisors.</summary>
public sealed record Rarity(int Id, string Key, string Name, int Weight, int CostPercent, int ExperiencePercent, int PriceTimes);

/// <summary>A kind of unit (settlers, scouts; armies in stage E). Price in hundredths.</summary>
public sealed record UnitType(int Id, string Key, string Name, int People, long[] Price, int TrainTicks, int TicksPerHex, int HiddenTicksPerHex, int Sight, bool Founds)
{
    /// <summary>Technology that allows it.</summary>
    public int Tech { get; init; }

    /// <summary>Other technologies it needs as well (caravans: the wheel and horseback riding).</summary>
    public int[] AlsoTechs { get; init; } = [];

    /// <summary>Unit kind it turns into when a technology becomes known (hunters become archers with Archery): (tech, unit kind).</summary>
    public (int Tech, int Kind)[] Becomes { get; set; } = [];

    /// <summary>Ships that carry armies.</summary>
    public bool CarriesArmies { get; init; }

    /// <summary>People of armies a ship carries.</summary>
    public int Carries { get; init; }

    /// <summary>Horses a caravan takes with it (they come back with it).</summary>
    public long HorsesPerCaravan { get; init; }

    /// <summary>Tags technology effects aim at (melee, ranged, cavalry, carriers…).</summary>
    public string[] Groups { get; init; } = [];

    /// <summary>Building the settlement must have to train it (empty = none).</summary>
    public string NeedsBuilding { get; init; } = "";

    /// <summary>Extra damage against settlements, percent.</summary>
    public int SiegePercent { get; init; }

    /// <summary>Made by the game, not trained (porters, caravans).</summary>
    public bool System { get; init; }

    /// <summary>Moves on water only (ships).</summary>
    public bool Naval { get; init; }

    /// <summary>Goods carried per person, hundredths (porters, caravans).</summary>
    public long CargoPerPerson { get; init; }

    /// <summary>A fighting unit.</summary>
    public bool Army { get; init; }

    /// <summary>Called-up workers: free, weak, go back to work when disbanded.</summary>
    public bool Levy { get; init; }

    /// <summary>Strength per person (warriors = 10).</summary>
    public int Strength { get; init; }

    /// <summary>Reach in hexes (1 = next hex).</summary>
    public int Range { get; init; } = 1;

    /// <summary>Biomes where it fights better.</summary>
    public bool[] BonusBiomes { get; init; } = [];

    /// <summary>Reliefs where it fights better.</summary>
    public bool[] BonusReliefs { get; init; } = [];

    /// <summary>Bonus on its terrain, percent.</summary>
    public int BonusPercent { get; init; }

    /// <summary>Technologies that make it stronger, with the percent.</summary>
    public (int Tech, int Percent)[] Upgrades { get; init; } = [];
}

/// <summary>Rules of fighting.</summary>
public sealed record WarRules(int CombatEveryTicks, int LowLuckPercent, int HighLuckPercent, int DefenderPercent, int DefenderStrength, int CaptureBelowPeople, int SurpriseReputation);

/// <summary>Choosing the start (startPhase in the data): ticks to pick, the block radius and its fuzz, the scoring radii.</summary>
public sealed record StartPhaseRules(int PickTicks, int BlockRadius, int BlockFuzz, int ScoreRadius, int RoomRadius);

/// <summary>Rations: every <paramref name="EveryTicks"/> a settlement feeds all its people, residents and units (Patryk, 2026-11-19).</summary>
public sealed record RationRules(int EveryTicks, int HungryPercent, int StarvingPercent, int StarveLosePercent);

/// <summary>A building type from <c>data/sim/game.json</c>. Amounts in hundredths; rates per worker per minute.</summary>
public sealed class BuildingType
{
    /// <summary>Index in <see cref="SimContent.Buildings"/>, from 1 (0 means no building).</summary>
    public int Id { get; init; }

    /// <summary>Data key.</summary>
    public string Key { get; init; } = "";

    /// <summary>Display name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Jobs.</summary>
    public int Workers { get; init; }

    /// <summary>Price per good, hundredths.</summary>
    public long[] Price { get; init; } = [];

    /// <summary>Construction time in ticks.</summary>
    public int BuildTicks { get; init; }

    /// <summary>Allowed biomes (empty = any land).</summary>
    public bool[] Biomes { get; init; } = [];

    /// <summary>Allowed reliefs (empty = any).</summary>
    public bool[] Reliefs { get; init; } = [];

    /// <summary>Needs water next to the hex (sea, lake or a river edge).</summary>
    public bool NeedsWater { get; init; }

    /// <summary>A surface deposit that also allows the building where terrain does not (0 = none).</summary>
    public int OrDeposit { get; init; }

    /// <summary>Production per worker per minute, hundredths.</summary>
    public long[] Produces { get; init; } = [];

    /// <summary>Consumption per worker per minute, hundredths.</summary>
    public long[] Consumes { get; init; } = [];

    /// <summary>Deposit on the hex that raises production (0 = none).</summary>
    public int BonusDeposit { get; init; }

    /// <summary>Extra production in percent with the bonus deposit.</summary>
    public int BonusPercent { get; init; }

    /// <summary>Housing added.</summary>
    public int Housing { get; init; }

    /// <summary>Storage per good added, hundredths.</summary>
    public long Storage { get; init; }

    /// <summary>True when the building has inputs.</summary>
    public bool HasInputs { get; init; }

    /// <summary>Technology that allows it; its workers give experience in that technology.</summary>
    public int Tech { get; init; }

    /// <summary>Least fertility of the hex (0 = any).</summary>
    public int MinFertility { get; init; }

    /// <summary>Research is this much cheaper while the building works (percent).</summary>
    public int ResearchDiscountPercent { get; init; }

    /// <summary>Tags technology effects aim at (food, farm, tools, craft, store, fort…).</summary>
    public string[] Groups { get; init; } = [];

    /// <summary>Reliefs that raise production.</summary>
    public bool[] BonusReliefs { get; init; } = [];

    /// <summary>Extra production in percent on those reliefs.</summary>
    public int BonusReliefPercent { get; init; }

    /// <summary>Defence of its settlement, percent (fortifications).</summary>
    public int DefensePercent { get; init; }

    /// <summary>Training time in its settlement, percent (barracks).</summary>
    public int TrainPercent { get; init; }

    /// <summary>Places it takes on its hex: 1, 2 or 4 (the whole hex) of the four (decision of 2026-11-18).</summary>
    public int Size { get; init; } = 1;

    /// <summary>Its production in winter, percent, on top of every worker's winter slowdown (gathering and fields).</summary>
    public int WinterPercent { get; init; } = 100;

    /// <summary>Other technologies it needs as well.</summary>
    public int[] AlsoTechs { get; init; } = [];

    /// <summary>Research of the branches its workers are set to is this much faster, percent at full staff (schools, academies, universities).</summary>
    public int ResearchPercent { get; init; }

    /// <summary>Building in its settlement is this much faster at full staff, percent (builders' yards).</summary>
    public int BuildPercent { get; init; }

    /// <summary>Epidemics in its settlement are this much milder at full staff, percent (bathhouses).</summary>
    public int ResistEpidemicPercent { get; init; }

    /// <summary>Growth in its settlement, percent of people a minute added at full staff (bathhouses).</summary>
    public int GrowthBonus { get; init; }

    /// <summary>Its homes are warm (stone houses): people living in them lose less work in winter.</summary>
    public bool Warm { get; init; }

    /// <summary>Most of a good it can hold (stables: 16 horses each), hundredths per good; 0 = no limit of its own.</summary>
    public long[] GoodLimit { get; init; } = [];

    /// <summary>It may also stand on a grove outside its biomes (woodcutters), giving the grove's share of wood.</summary>
    public bool OrTrees { get; init; }

    /// <summary>Better kind it can be upgraded to in place (0 = none).</summary>
    public int UpgradesTo { get; set; }

    internal string UpgradeKey { get; init; } = "";

    /// <summary>Its people may cross the rivers next to it (raft landings).</summary>
    public bool Crossing { get; init; }

    /// <summary>Buildings of this group in the same settlement produce <see cref="SupportPercent"/> more while it works.</summary>
    public string SupportGroup { get; init; } = "";

    /// <summary>See <see cref="SupportGroup"/>.</summary>
    public int SupportPercent { get; init; }

    /// <summary>Wounded soldiers of its people it heals a minute when they stand in its settlement's land.</summary>
    public int HealsPerMinute { get; init; }
}

/// <summary>The rules of the new game, loaded from <c>data/sim/game.json</c> plus the world content of the legacy loader.</summary>
public sealed class SimContent
{
    /// <summary>Hundredths in one unit.</summary>
    public const long One = 100;

    /// <summary>Ticks per second.</summary>
    public const int TicksPerSecond = 10;

    /// <summary>Ticks per minute.</summary>
    public const int TicksPerMinute = 600;

    /// <summary>World content (biomes, deposits, climate) used by the world generator.</summary>
    public required GameContent World { get; init; }

    /// <summary>Hex density against the legacy map.</summary>
    public int Density { get; init; }

    /// <summary>Economy rhythm.</summary>
    public int EconomyEveryTicks { get; init; }

    /// <summary>Population rhythm.</summary>
    public int PopulationEveryTicks { get; init; }

    /// <summary>Goods.</summary>
    public required IReadOnlyList<SimGood> Goods { get; init; }

    /// <summary>Settlement levels, from the camp up.</summary>
    public required IReadOnlyList<SettlementLevel> Levels { get; init; }

    /// <summary>Building types; index 0 is an empty placeholder so that ids start at 1.</summary>
    public required IReadOnlyList<BuildingType> Buildings { get; init; }

    /// <summary>People of a new tribe.</summary>
    public int StartPeople { get; init; }

    /// <summary>Stock of a new tribe, hundredths.</summary>
    public required long[] StartStock { get; init; }

    /// <summary>Food per person per minute, hundredths.</summary>
    public long FoodPerPersonPerMinute { get; init; }

    /// <summary>Goods a gatherer picks up per minute around a settlement of richness 100, hundredths, per good.</summary>
    public required long[] ForagePerMinute { get; init; }

    /// <summary>Food and wood richness of each biome: index biome * 2 (+0 food, +1 wood).</summary>
    public required int[] ForageBiome { get; init; }

    /// <summary>Stone richness of each relief (flat, hills, mountains).</summary>
    public int[] ForageRelief { get; init; } = [0, 0, 0];

    /// <summary>Richness a surface deposit adds: index deposit * goods + good.</summary>
    public required int[] ForageDeposit { get; init; }

    /// <summary>Food richness a river by the hex adds.</summary>
    public int ForageRiverFood { get; init; }

    /// <summary>Winter comes every this many ticks (0 = never).</summary>
    public int SeasonCycleTicks { get; init; }

    /// <summary>Winter lasts this many ticks, at the end of each cycle.</summary>
    public int WinterTicks { get; init; }

    /// <summary>Peoples are told this many ticks before winter.</summary>
    public int WinterWarnTicks { get; init; }

    /// <summary>Every worker's output in winter, percent.</summary>
    public int WinterWorkPercent { get; init; } = 100;

    /// <summary>Work is rounded down to steps of this many permille (seasons.workStepPermille).</summary>
    public int WorkStepPermille { get; init; } = 25;

    /// <summary>Schools and academies speed a branch of research at most this much, percent (research.maxSupportPercent).</summary>
    public int MaxResearchSupportPercent { get; init; } = 50;

    /// <summary>Choosing the start: time, the block around taken starts and the scoring radii (startPhase).</summary>
    public StartPhaseRules StartPhase { get; init; } = new(300, 10, 3, 3, 6);

    /// <summary>Id of clothing (−1 = none).</summary>
    public int Clothing { get; init; } = -1;

    /// <summary>Output a set of clothing adds to a worker, percent.</summary>
    public int ClothingBonusPercent { get; init; }

    /// <summary>Highest output with clothing in winter, percent.</summary>
    public int ClothingWinterMaxPercent { get; init; } = 100;

    /// <summary>Clothing worn out per person per minute, hundredths.</summary>
    public long ClothingWearPerPersonPerMinute { get; init; }

    /// <summary>Share of the fallen who are wounded instead of dead, percent (herbalists heal them).</summary>
    public int InjuredPercent { get; init; }

    /// <summary>From this era rivers no longer stop walkers; before it they cross only by a crossing building of their own.</summary>
    public int RiversFreeEra { get; init; } = int.MaxValue;

    /// <summary>Technologies of the basics branch: cheap first steps that do not count toward the next era.</summary>
    public const string BasicsCategory = "basics";

    /// <summary>Growth percent of people per minute.</summary>
    public int GrowthPercentPerMinute { get; init; }

    /// <summary>Least growth per minute (people).</summary>
    public int MinGrowthPerMinute { get; init; }

    /// <summary>Starvation loss percent per minute.</summary>
    public int StarvePercentPerMinute { get; init; }

    /// <summary>Extra price, percent, for resuming a paused construction.</summary>
    public int ResumeCostPercent { get; init; }

    /// <summary>Rations of the units, paid by their home settlement.</summary>
    public RationRules Rations { get; init; } = new(600, 75, 50, 20);

    /// <summary>Technologies of its era a people must know to enter the next era (eras are levels, not one gate technology).</summary>
    public int EraAdvanceTechs { get; init; } = 5;

    /// <summary>Branches of the technology tree, in display order.</summary>
    public string[] TechCategories { get; init; } = [];

    /// <summary>Id of food.</summary>
    public int Food { get; init; }

    /// <summary>Unit kinds.</summary>
    public required IReadOnlyList<UnitType> Units { get; init; }

    /// <summary>Least distance between settlement centres.</summary>
    public int FoundMinDistance { get; init; }

    /// <summary>Share of a building's price that counts toward its upgrade, percent.</summary>
    public int UpgradeCreditPercent { get; init; } = 50;

    /// <summary>Eras.</summary>
    public required IReadOnlyList<SimEra> Eras { get; init; }

    /// <summary>Technologies.</summary>
    public required IReadOnlyList<SimTech> Techs { get; init; }

    /// <summary>Discount per known people that knows a technology, percent.</summary>
    public int NeighbourDiscountPercent { get; init; }

    /// <summary>Most discount from neighbours, percent.</summary>
    public int MaxNeighbourDiscountPercent { get; init; }

    /// <summary>Rarities of thinkers and advisors.</summary>
    public required IReadOnlyList<Rarity> Rarities { get; init; }

    /// <summary>Thinker rules.</summary>
    public int ThinkerRetireTicks { get; init; }

    /// <summary>Ticks between thinker candidates.</summary>
    public int ThinkerOfferTicks { get; init; }

    /// <summary>Edge lost per thinker generation, percent.</summary>
    public int GenerationLossPercent { get; init; }

    /// <summary>Base price of hiring a thinker.</summary>
    public required long[] ThinkerPrice { get; init; }

    /// <summary>Ticks between advisor candidates.</summary>
    public int AdvisorOfferTicks { get; init; }

    /// <summary>Base price of hiring an advisor.</summary>
    public required long[] AdvisorPrice { get; init; }

    /// <summary>Food an advisor eats per minute, hundredths.</summary>
    public long AdvisorUpkeep { get; init; }

    /// <summary>Rules of fighting.</summary>
    public required WarRules War { get; init; }

    /// <summary>Research discount per trade partner, percent.</summary>
    public int TradeResearchDiscount { get; init; }

    /// <summary>Least worth (percent of what it gives) the AI wants from a barter.</summary>
    public int AcceptWorthPercent { get; init; }

    /// <summary>How long peoples must have known each other before a treaty, ticks.</summary>
    public int TreatyKnownTicks { get; init; }

    /// <summary>Farthest distance between settlements for a treaty, hexes.</summary>
    public int TreatyBorderHexes { get; init; }

    /// <summary>Time without fighting before a treaty, ticks.</summary>
    public int TreatyPeaceTicks { get; init; }

    /// <summary>Least reputation of the other people for a treaty.</summary>
    public int TreatyMinReputation { get; init; }

    /// <summary>Least time between two offers to the same people, ticks.</summary>
    public int OfferCooldownTicks { get; init; }

    /// <summary>Wait after a refusal, ticks.</summary>
    public int RefusalCooldownTicks { get; init; }

    /// <summary>The shared pool of events.</summary>
    public required IReadOnlyList<State.SimEvent> Events { get; init; }

    /// <summary>Ticks between two events of a people (at least).</summary>
    public int EventEveryTicks { get; init; }

    /// <summary>Random extra ticks between events.</summary>
    public int EventSpreadTicks { get; init; }

    /// <summary>Advisor fields.</summary>
    public required IReadOnlyList<string> AdvisorFields { get; init; }

    /// <summary>Room every finished building adds when its type gives none of its own, hundredths.</summary>
    public long BuildingStorage { get; init; }

    /// <summary>Unit kind of porters.</summary>
    public int Porters { get; init; }

    /// <summary>Unit kind of caravans.</summary>
    public int Caravans { get; init; }

    /// <summary>Technology needed to trade with other peoples (caravans abroad, trade agreements).</summary>
    public int TradeTech { get; init; }

    /// <summary>Technology needed to build roads (−1 = none).</summary>
    public int RoadTech { get; init; } = -1;

    /// <summary>Walking cost on a road, percent of the hex's normal cost.</summary>
    public int RoadCostPercent { get; init; } = 50;

    /// <summary>Levels of roads (index + 1 is the level on the map: 1 track, 2 paved road).</summary>
    public IReadOnlyList<RoadLevel> RoadLevels { get; init; } = [];

    /// <summary>How far from our nearest settlement roads may be built, in hexes.</summary>
    public int RoadReach { get; init; } = 25;

    /// <summary>Goods kept only in buildings that give room for them (horses: stables), per good.</summary>
    public bool[] LimitedGoods => _limited ??= LimitedOf();

    private bool[]? _limited;

    private bool[] LimitedOf()
    {
        var f = new bool[Goods.Count];
        foreach (BuildingType b in Buildings)
        {
            for (int g = 0; g < b.GoodLimit.Length; g++)
            {
                f[g] |= b.GoodLimit[g] > 0;
            }
        }

        return f;
    }

    /// <summary>Share of each biome's land hexes with a grove, percent (open land; forests are all trees).</summary>
    public int[] GroveChance { get; init; } = [];

    /// <summary>Wood a grove gives woodcutters and gatherers, percent of a forest's.</summary>
    public int GroveWoodPercent { get; init; } = 60;

    /// <summary>Biomes that are forest (all trees).</summary>
    public bool[] ForestBiomes { get; init; } = [];

    /// <summary>Works drawn on hexes, in data order (ditch, palisade, wall, aqueduct).</summary>
    public IReadOnlyList<WorkKind> Works { get; init; } = [];

    /// <summary>Technology after which roads bridge the rivers they cross (−1 = never).</summary>
    public int BridgeTech { get; init; } = -1;

    /// <summary>Price of a bridge, hundredths per good.</summary>
    public long[] BridgePrice { get; init; } = [];

    /// <summary>Finds a work by key (null when the data has none).</summary>
    public WorkKind? Work(string key)
    {
        foreach (WorkKind w in Works)
        {
            if (w.Key == key)
            {
                return w;
            }
        }

        return null;
    }

    /// <summary>Goods a caravan brings from a settlement to a new camp joined to it by road.</summary>
    public long[] RoadSupply { get; init; } = [];

    /// <summary>Finds a technology by key.</summary>
    public int TechId(string key)
    {
        foreach (SimTech t in Techs)
        {
            if (t.Key == key)
            {
                return t.Id;
            }
        }

        throw new KeyNotFoundException($"Unknown technology '{key}'.");
    }

    /// <summary>Walking cost of each hex in percent of a flat open hex.</summary>
    public required int[] WalkCost { get; init; }

    /// <summary>Finds a unit kind by key.</summary>
    public UnitType Unit(string key)
    {
        foreach (UnitType u in Units)
        {
            if (u.Key == key)
            {
                return u;
            }
        }

        throw new KeyNotFoundException($"Unknown unit '{key}'.");
    }

    /// <summary>Finds a good by key.</summary>
    public int GoodId(string key)
    {
        foreach (SimGood g in Goods)
        {
            if (g.Key == key)
            {
                return g.Id;
            }
        }

        throw new KeyNotFoundException($"Unknown good '{key}'.");
    }

    /// <summary>Finds a building type by key.</summary>
    public BuildingType Building(string key)
    {
        foreach (BuildingType b in Buildings)
        {
            if (b.Key == key)
            {
                return b;
            }
        }

        throw new KeyNotFoundException($"Unknown building '{key}'.");
    }

    /// <summary>Loads the rules from a data folder (default: the folder next to the program).</summary>
    public static SimContent Load(string? directory = null)
    {
        directory ??= CivSim.Data.ContentLoader.DefaultDirectory;
        GameContent world = CivSim.Data.ContentLoader.Load(directory);
        string path = Path.Combine(directory, "sim", "game.json");
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = doc.RootElement;

        var goods = new List<SimGood>();
        foreach (JsonElement g in root.GetProperty("goods").EnumerateArray())
        {
            goods.Add(new SimGood(goods.Count, g.GetProperty("key").GetString()!, g.GetProperty("name").GetString()!)
            {
                WorthPercent = g.TryGetProperty("worth", out JsonElement w) ? (int)(Hundredths(w)) : 100,
            });
        }

        int GoodOf(string key)
        {
            int i = goods.FindIndex(x => x.Key == key);
            return i >= 0 ? i : throw new InvalidDataException($"sim/game.json: unknown good '{key}'.");
        }

        long[] Amounts(JsonElement e)
        {
            var a = new long[goods.Count];
            if (e.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty p in e.EnumerateObject())
                {
                    a[GoodOf(p.Name)] += Hundredths(p.Value);
                }
            }

            return a;
        }

        int DepositOf(JsonElement b, string name)
        {
            if (!b.TryGetProperty(name, out JsonElement e) || string.IsNullOrEmpty(e.GetString()))
            {
                return 0;
            }

            string key = e.GetString()!;
            foreach (DepositDefinition d in world.Deposits)
            {
                if (d.Key == key)
                {
                    return d.Id;
                }
            }

            throw new InvalidDataException($"sim/game.json: unknown deposit '{key}'.");
        }

        var eras = new List<SimEra>();
        foreach (JsonElement e in root.GetProperty("eras").EnumerateArray())
        {
            eras.Add(new SimEra(eras.Count, e.GetProperty("key").GetString()!, e.GetProperty("name").GetString()!));
        }

        var techKeys = new List<string>();
        foreach (JsonElement t in root.GetProperty("technologies").EnumerateArray())
        {
            techKeys.Add(t.GetProperty("key").GetString()!);
        }

        int TechOf(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out JsonElement v))
            {
                return -1;
            }

            int i = techKeys.IndexOf(v.GetString()!);
            return i >= 0 ? i : throw new InvalidDataException($"sim/game.json: unknown technology '{v.GetString()}'.");
        }

        int[] TechList(JsonElement e, string name)
        {
            var list = new List<int>();
            if (e.TryGetProperty(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    int i = techKeys.IndexOf(k.GetString()!);
                    list.Add(i >= 0 ? i : throw new InvalidDataException($"sim/game.json: unknown technology '{k.GetString()}'."));
                }
            }

            return [.. list];
        }

        (int, long)[] MadeOf(JsonElement t)
        {
            var list = new List<(int, long)>();
            if (t.TryGetProperty("made", out JsonElement m))
            {
                foreach (JsonProperty p in m.EnumerateObject())
                {
                    list.Add((GoodOf(p.Name), Hundredths(p.Value)));
                }
            }

            return [.. list];
        }

        static TechEffect[] EffectsOf(JsonElement t)
        {
            var list = new List<TechEffect>();
            if (t.TryGetProperty("effects", out JsonElement ef))
            {
                foreach (JsonElement e in ef.EnumerateArray())
                {
                    string kind = e.GetProperty("kind").GetString()!;
                    if (!Enum.TryParse(kind, ignoreCase: true, out EffectKind k))
                    {
                        throw new InvalidDataException($"sim/game.json: unknown effect '{kind}'.");
                    }

                    list.Add(new TechEffect(k, e.TryGetProperty("target", out JsonElement tg) ? tg.GetString()! : "*", e.GetProperty("value").GetInt32()));
                }
            }

            return [.. list];
        }

        var techs = new List<SimTech>();
        foreach (JsonElement t in root.GetProperty("technologies").EnumerateArray())
        {
            var exp = new List<(int, long)>();
            if (t.TryGetProperty("experience", out JsonElement xe))
            {
                foreach (JsonProperty p in xe.EnumerateObject())
                {
                    int ti = techKeys.IndexOf(p.Name);
                    if (ti < 0)
                    {
                        throw new InvalidDataException($"sim/game.json: unknown technology '{p.Name}' in experience.");
                    }

                    exp.Add((ti, p.Value.GetInt64() * TicksPerMinute));
                }
            }

            string eraKey = t.GetProperty("era").GetString()!;
            int era = eras.FindIndex(x => x.Key == eraKey);
            techs.Add(new SimTech(
                techs.Count, techKeys[techs.Count], t.GetProperty("name").GetString()!, era >= 0 ? era : throw new InvalidDataException($"sim/game.json: unknown era '{eraKey}'."),
                t.TryGetProperty("known", out JsonElement k) && k.GetBoolean(), [.. exp],
                t.TryGetProperty("price", out JsonElement tp) ? Amounts(tp) : new long[goods.Count],
                t.TryGetProperty("seconds", out JsonElement ts) ? ts.GetInt32() * TicksPerSecond : 0)
            {
                Effects = EffectsOf(t),
                Upgrade = t.TryGetProperty("upgrade", out JsonElement ug) && ug.GetBoolean(),
                Gives = t.TryGetProperty("gives", out JsonElement gv) ? gv.GetString() ?? "" : "",
                Category = t.TryGetProperty("category", out JsonElement cat) ? cat.GetString() ?? "" : "",
                Requires = TechList(t, "requires"),
                Made = MadeOf(t),
            });
        }

        var levels = new List<SettlementLevel>();
        foreach (JsonElement l in root.GetProperty("settlementLevels").EnumerateArray())
        {
            levels.Add(new SettlementLevel(
                levels.Count, l.GetProperty("key").GetString()!, l.GetProperty("name").GetString()!, l.GetProperty("radius").GetInt32(),
                l.GetProperty("housing").GetInt32(), Hundredths(l.GetProperty("storage")), Amounts(l.GetProperty("upgradePrice")))
            {
                Tech = TechOf(l, "tech"),
                AlsoTechs = TechList(l, "alsoTechs"),
            });
        }

        static string[] Strings(JsonElement e, string name)
        {
            var list = new List<string>();
            if (e.TryGetProperty(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    list.Add(k.GetString()!);
                }
            }

            return [.. list];
        }

        static bool[] ReliefsOf(JsonElement e, string name)
        {
            var f = new bool[3];
            if (e.TryGetProperty(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[k.GetString() switch { "flat" => 0, "hills" => 1, "mountains" => 2, _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'.") }] = true;
                }
            }

            return f;
        }

        var buildings = new List<BuildingType> { new() { Id = 0, Key = "", Name = "" } };
        foreach (JsonElement b in root.GetProperty("buildings").EnumerateArray())
        {
            var biomes = new bool[world.Biomes.Count];
            if (b.TryGetProperty("biomes", out JsonElement be))
            {
                foreach (JsonElement k in be.EnumerateArray())
                {
                    biomes[world.Biome(k.GetString()!).Id] = true;
                }
            }

            var reliefs = new bool[3];
            if (b.TryGetProperty("relief", out JsonElement re))
            {
                foreach (JsonElement k in re.EnumerateArray())
                {
                    reliefs[k.GetString() switch
                    {
                        "flat" => 0,
                        "hills" => 1,
                        "mountains" => 2,
                        _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'."),
                    }] = true;
                }
            }

            long[] consumes = b.TryGetProperty("consumes", out JsonElement ce) ? Amounts(ce) : new long[goods.Count];
            bool hasInputs = false;
            foreach (long c in consumes)
            {
                hasInputs |= c > 0;
            }

            buildings.Add(new BuildingType
            {
                Id = buildings.Count,
                Key = b.GetProperty("key").GetString()!,
                Name = b.GetProperty("name").GetString()!,
                Workers = b.GetProperty("workers").GetInt32(),
                Price = Amounts(b.GetProperty("price")),
                BuildTicks = (int)(Hundredths(b.GetProperty("buildSeconds")) * TicksPerSecond / One),
                Biomes = biomes,
                Reliefs = reliefs,
                NeedsWater = b.TryGetProperty("needsWater", out JsonElement w) && w.GetBoolean(),
                OrDeposit = DepositOf(b, "orDeposit"),
                Produces = b.TryGetProperty("produces", out JsonElement pe) ? Amounts(pe) : new long[goods.Count],
                Consumes = consumes,
                HasInputs = hasInputs,
                BonusDeposit = DepositOf(b, "bonusDeposit"),
                BonusPercent = b.TryGetProperty("bonusPercent", out JsonElement bp) ? bp.GetInt32() : 0,
                Housing = b.TryGetProperty("housing", out JsonElement h) ? h.GetInt32() : 0,
                Storage = b.TryGetProperty("storage", out JsonElement s) ? Hundredths(s) : 0,
                Tech = Math.Max(0, TechOf(b, "tech")),
                MinFertility = b.TryGetProperty("minFertility", out JsonElement mf) ? mf.GetInt32() : 0,
                ResearchDiscountPercent = b.TryGetProperty("researchDiscountPercent", out JsonElement rd) ? rd.GetInt32() : 0,
                DefensePercent = b.TryGetProperty("defensePercent", out JsonElement dp) ? dp.GetInt32() : 0,
                BonusReliefs = ReliefsOf(b, "bonusRelief"),
                Groups = Strings(b, "groups"),
                BonusReliefPercent = b.TryGetProperty("bonusReliefPercent", out JsonElement brp) ? brp.GetInt32() : 0,
                TrainPercent = b.TryGetProperty("trainPercent", out JsonElement tp2) ? tp2.GetInt32() : 100,
                WinterPercent = b.TryGetProperty("winterPercent", out JsonElement wp) ? wp.GetInt32() : 100,
                Size = b.TryGetProperty("size", out JsonElement sz) ? sz.GetInt32() is 1 or 2 or 4 ? sz.GetInt32() : throw new InvalidDataException($"sim/game.json: size of '{b.GetProperty("key").GetString()}' must be 1, 2 or 4.") : 1,
                AlsoTechs = TechList(b, "alsoTechs"),
                ResearchPercent = b.TryGetProperty("researchPercent", out JsonElement rpc) ? rpc.GetInt32() : 0,
                BuildPercent = b.TryGetProperty("buildPercent", out JsonElement bpc2) ? bpc2.GetInt32() : 0,
                ResistEpidemicPercent = b.TryGetProperty("resistEpidemicPercent", out JsonElement rep) ? rep.GetInt32() : 0,
                GrowthBonus = b.TryGetProperty("growthBonus", out JsonElement gb) ? gb.GetInt32() : 0,
                GoodLimit = b.TryGetProperty("goodLimit", out JsonElement gl) ? Amounts(gl) : new long[goods.Count],
                Warm = b.TryGetProperty("warm", out JsonElement wm) && wm.GetBoolean(),
                Crossing = b.TryGetProperty("crossing", out JsonElement cr) && cr.GetBoolean(),
                OrTrees = b.TryGetProperty("orTrees", out JsonElement ot) && ot.GetBoolean(),
                UpgradeKey = b.TryGetProperty("upgradesTo", out JsonElement ut) ? ut.GetString() ?? "" : "",
                SupportGroup = b.TryGetProperty("supportGroup", out JsonElement sgp) ? sgp.GetString() ?? "" : "",
                SupportPercent = b.TryGetProperty("supportPercent", out JsonElement spc) ? spc.GetInt32() : 0,
                HealsPerMinute = b.TryGetProperty("healsPerMinute", out JsonElement hpm) ? hpm.GetInt32() : 0,
            });
        }

        foreach (BuildingType upgradable in buildings)
        {
            if (upgradable.UpgradeKey.Length > 0)
            {
                upgradable.UpgradesTo = buildings.FindIndex(x => x.Key == upgradable.UpgradeKey) is int up && up > 0
                    ? up : throw new InvalidDataException($"sim/game.json: '{upgradable.Key}' upgrades to unknown '{upgradable.UpgradeKey}'.");
            }
        }

        bool[] Flags(JsonElement e, string name)
        {
            var f = new bool[world.Biomes.Count];
            if (e.TryGetProperty(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[world.Biome(k.GetString()!).Id] = true;
                }
            }

            return f;
        }

        bool[] Reliefs(JsonElement e, string name)
        {
            var f = new bool[3];
            if (e.TryGetProperty(name, out JsonElement arr))
            {
                foreach (JsonElement k in arr.EnumerateArray())
                {
                    f[k.GetString() switch { "flat" => 0, "hills" => 1, "mountains" => 2, _ => throw new InvalidDataException($"sim/game.json: unknown relief '{k.GetString()}'.") }] = true;
                }
            }

            return f;
        }

        (int, int)[] Upgrades(JsonElement e)
        {
            var list = new List<(int, int)>();
            if (e.TryGetProperty("upgrades", out JsonElement up))
            {
                foreach (JsonProperty p in up.EnumerateObject())
                {
                    int ti = techKeys.IndexOf(p.Name);
                    list.Add((ti >= 0 ? ti : throw new InvalidDataException($"sim/game.json: unknown technology '{p.Name}'."), p.Value.GetInt32()));
                }
            }

            return [.. list];
        }

        var units = new List<UnitType>();
        foreach (JsonElement u in root.GetProperty("units").EnumerateArray())
        {
            int perHex = u.GetProperty("ticksPerHex").GetInt32();
            units.Add(new UnitType(
                units.Count, u.GetProperty("key").GetString()!, u.GetProperty("name").GetString()!, u.GetProperty("people").GetInt32(), Amounts(u.GetProperty("price")),
                (int)(Hundredths(u.GetProperty("trainSeconds")) * TicksPerSecond / One), perHex,
                u.TryGetProperty("hiddenTicksPerHex", out JsonElement hp) ? hp.GetInt32() : 0, u.GetProperty("sight").GetInt32(),
                u.TryGetProperty("founds", out JsonElement f) && f.GetBoolean())
            {
                Tech = Math.Max(0, TechOf(u, "tech")),
                System = u.TryGetProperty("system", out JsonElement sy) && sy.GetBoolean(),
                Groups = Strings(u, "groups"),
                NeedsBuilding = u.TryGetProperty("needsBuilding", out JsonElement nb) ? nb.GetString() ?? "" : "",
                SiegePercent = u.TryGetProperty("siege", out JsonElement sg) ? sg.GetInt32() : 0,
                Naval = u.TryGetProperty("naval", out JsonElement nv) && nv.GetBoolean(),
                CargoPerPerson = u.TryGetProperty("cargoPerPerson", out JsonElement cp) ? Hundredths(cp) : 0,
                Army = u.TryGetProperty("army", out JsonElement ar) && ar.GetBoolean(),
                Levy = u.TryGetProperty("levy", out JsonElement lv) && lv.GetBoolean(),
                Strength = u.TryGetProperty("strength", out JsonElement st) ? st.GetInt32() : 0,
                Range = u.TryGetProperty("range", out JsonElement rg) ? rg.GetInt32() : 1,
                BonusBiomes = Flags(u, "bonusBiomes"),
                BonusReliefs = Reliefs(u, "bonusRelief"),
                BonusPercent = u.TryGetProperty("bonusPercent", out JsonElement bpc) ? bpc.GetInt32() : 0,
                Upgrades = Upgrades(u),
                AlsoTechs = TechList(u, "alsoTechs"),
                CarriesArmies = u.TryGetProperty("carriesArmies", out JsonElement ca) && ca.GetBoolean(),
                Carries = u.TryGetProperty("carries", out JsonElement cr2) ? cr2.GetInt32() : 0,
                HorsesPerCaravan = u.TryGetProperty("horsesPerCaravan", out JsonElement hpc) ? Hundredths(hpc) : 0,
            });
        }

        int ui = 0;
        foreach (JsonElement u in root.GetProperty("units").EnumerateArray())
        {
            if (u.TryGetProperty("becomes", out JsonElement bec))
            {
                var list = new List<(int, int)>();
                foreach (JsonProperty p in bec.EnumerateObject())
                {
                    int ti = techKeys.IndexOf(p.Name);
                    int to = units.FindIndex(x => x.Key == p.Value.GetString());
                    list.Add((ti >= 0 ? ti : throw new InvalidDataException($"sim/game.json: unknown technology '{p.Name}'."), to >= 0 ? to : throw new InvalidDataException($"sim/game.json: unknown unit '{p.Value.GetString()}'.")));
                }

                units[ui].Becomes = [.. list];
            }

            ui++;
        }

        JsonElement tc = root.GetProperty("terrainCost");
        var forest = new bool[world.Biomes.Count];
        foreach (JsonElement k in tc.GetProperty("forestBiomes").EnumerateArray())
        {
            forest[world.Biome(k.GetString()!).Id] = true;
        }

        // Walking cost per biome × relief, flattened: index = biome * 3 + relief; 0 = impassable (water).
        var walk = new int[world.Biomes.Count * 3];
        for (int b = 0; b < world.Biomes.Count; b++)
        {
            for (int r = 0; r < 3; r++)
            {
                int cost = world.Biomes[b].IsWater ? 0 : 100;
                if (cost > 0)
                {
                    cost = cost * (r == 1 ? tc.GetProperty("hills").GetInt32() : r == 2 ? tc.GetProperty("mountains").GetInt32() : 100) / 100;
                    cost = forest[b] ? cost * tc.GetProperty("forest").GetInt32() / 100 : cost;
                }

                walk[(b * 3) + r] = cost;
            }
        }

        JsonElement research = root.GetProperty("research");
        JsonElement thinkers = root.GetProperty("thinkers");
        JsonElement advisors = root.GetProperty("advisors");
        var rarities = new List<Rarity>();
        foreach (JsonElement r in thinkers.GetProperty("rarities").EnumerateArray())
        {
            rarities.Add(new Rarity(
                rarities.Count, r.GetProperty("key").GetString()!, r.GetProperty("name").GetString()!, r.GetProperty("weight").GetInt32(),
                r.GetProperty("costPercent").GetInt32(), r.GetProperty("experiencePercent").GetInt32(), r.GetProperty("priceTimes").GetInt32()));
        }

        var fields = new List<string>();
        foreach (JsonElement f in advisors.GetProperty("fields").EnumerateArray())
        {
            fields.Add(f.GetString()!);
        }

        JsonElement seasons = root.TryGetProperty("seasons", out JsonElement se) ? se : default;
        JsonElement clothing = root.TryGetProperty("clothing", out JsonElement cl) ? cl : default;
        JsonElement forage = root.TryGetProperty("forage", out JsonElement fo) ? fo : default;
        var forageBiome = new int[world.Biomes.Count * 2];
        var forageRelief = new int[3];
        var forageDeposit = new int[(world.Deposits.Count + 1) * goods.Count];
        if (forage.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty p in forage.GetProperty("biomes").EnumerateObject())
            {
                int bi = world.Biome(p.Name).Id;
                forageBiome[bi * 2] = p.Value[0].GetInt32();
                forageBiome[(bi * 2) + 1] = p.Value[1].GetInt32();
            }

            for (int r = 0; r < 3; r++)
            {
                forageRelief[r] = forage.GetProperty("relief")[r].GetInt32();
            }

            foreach (JsonProperty p in forage.GetProperty("deposits").EnumerateObject())
            {
                DepositDefinition dep = world.Deposits.First(x => x.Key == p.Name);
                foreach (JsonProperty gp in p.Value.EnumerateObject())
                {
                    forageDeposit[(dep.Id * goods.Count) + GoodOf(gp.Name)] = gp.Value.GetInt32();
                }
            }
        }

        JsonElement start = root.GetProperty("start");
        JsonElement food = root.GetProperty("food");
        JsonElement growth = root.GetProperty("growth");
        return new SimContent
        {
            World = world,
            Density = root.GetProperty("density").GetInt32(),
            EconomyEveryTicks = root.GetProperty("economyEveryTicks").GetInt32(),
            PopulationEveryTicks = root.GetProperty("populationEveryTicks").GetInt32(),
            Goods = goods,
            Levels = levels,
            Buildings = buildings,
            StartPeople = start.GetProperty("people").GetInt32(),
            StartStock = Amounts(start.GetProperty("stock")),
            FoodPerPersonPerMinute = Hundredths(food.GetProperty("foodPerPersonPerMinute")),
            ForagePerMinute = forage.TryGetProperty("perMinute", out JsonElement fpm) ? Amounts(fpm) : new long[goods.Count],
            ForageBiome = forageBiome,
            ForageRelief = forageRelief,
            ForageDeposit = forageDeposit,
            ForageRiverFood = forage.TryGetProperty("riverFood", out JsonElement frf) ? frf.GetInt32() : 0,
            SeasonCycleTicks = seasons.ValueKind == JsonValueKind.Object ? seasons.GetProperty("cycleMinutes").GetInt32() * TicksPerMinute : 0,
            WinterTicks = seasons.ValueKind == JsonValueKind.Object ? seasons.GetProperty("winterMinutes").GetInt32() * TicksPerMinute : 0,
            WinterWarnTicks = seasons.ValueKind == JsonValueKind.Object ? seasons.GetProperty("warnSeconds").GetInt32() * TicksPerSecond : 0,
            WinterWorkPercent = seasons.ValueKind == JsonValueKind.Object ? seasons.GetProperty("winterWorkPercent").GetInt32() : 100,
            WorkStepPermille = seasons.ValueKind == JsonValueKind.Object && seasons.TryGetProperty("workStepPermille", out JsonElement wsp) ? Math.Max(1, wsp.GetInt32()) : 25,
            MaxResearchSupportPercent = research.TryGetProperty("maxSupportPercent", out JsonElement msp) ? msp.GetInt32() : 50,
            StartPhase = root.TryGetProperty("startPhase", out JsonElement sp)
                ? new StartPhaseRules(sp.GetProperty("pickSeconds").GetInt32() * TicksPerSecond, sp.GetProperty("blockRadius").GetInt32(), sp.GetProperty("blockFuzz").GetInt32(),
                    sp.GetProperty("scoreRadius").GetInt32(), sp.GetProperty("roomRadius").GetInt32())
                : new StartPhaseRules(300, 10, 3, 3, 6),
            Clothing = clothing.ValueKind == JsonValueKind.Object ? GoodOf(clothing.GetProperty("good").GetString()!) : -1,
            ClothingBonusPercent = clothing.ValueKind == JsonValueKind.Object ? clothing.GetProperty("bonusPercent").GetInt32() : 0,
            ClothingWinterMaxPercent = clothing.ValueKind == JsonValueKind.Object ? clothing.GetProperty("winterMaxPercent").GetInt32() : 100,
            ClothingWearPerPersonPerMinute = clothing.ValueKind == JsonValueKind.Object ? Hundredths(clothing.GetProperty("wearPerPersonPerMinute")) : 0,
            InjuredPercent = root.GetProperty("war").TryGetProperty("injuredPercent", out JsonElement inj) ? inj.GetInt32() : 0,
            RiversFreeEra = root.TryGetProperty("rivers", out JsonElement rv) ? eras.FindIndex(e => e.Key == rv.GetProperty("freeFromEra").GetString()) is int rfe && rfe >= 0 ? rfe : int.MaxValue : int.MaxValue,
            GrowthPercentPerMinute = growth.GetProperty("growthPercentPerMinute").GetInt32(),
            MinGrowthPerMinute = growth.GetProperty("minGrowthPerMinute").GetInt32(),
            StarvePercentPerMinute = growth.GetProperty("starvePercentPerMinute").GetInt32(),
            ResumeCostPercent = root.GetProperty("resumeCostPercent").GetInt32(),
            Rations = root.TryGetProperty("rations", out JsonElement ra)
                ? new RationRules(ra.GetProperty("everySeconds").GetInt32() * TicksPerSecond, ra.GetProperty("hungryPercent").GetInt32(),
                    ra.GetProperty("starvingPercent").GetInt32(), ra.GetProperty("starveLosePercent").GetInt32())
                : new RationRules(600, 75, 50, 20),
            EraAdvanceTechs = root.TryGetProperty("eraAdvanceTechs", out JsonElement eat) ? eat.GetInt32() : 5,
            TechCategories = Strings(root, "techCategories"),
            Food = GoodOf("food"),
            Units = units,
            FoundMinDistance = root.GetProperty("foundSettlement").GetProperty("minDistance").GetInt32(),
            UpgradeCreditPercent = root.TryGetProperty("upgradeCreditPercent", out JsonElement ucp) ? ucp.GetInt32() : 50,
            WalkCost = walk,
            Eras = eras,
            Techs = techs,
            NeighbourDiscountPercent = research.GetProperty("neighbourDiscountPercent").GetInt32(),
            MaxNeighbourDiscountPercent = research.GetProperty("maxNeighbourDiscountPercent").GetInt32(),
            Rarities = rarities,
            ThinkerRetireTicks = thinkers.GetProperty("retireMinutes").GetInt32() * TicksPerMinute,
            ThinkerOfferTicks = thinkers.GetProperty("offerMinutes").GetInt32() * TicksPerMinute,
            GenerationLossPercent = thinkers.GetProperty("generationLossPercent").GetInt32(),
            ThinkerPrice = Amounts(thinkers.GetProperty("hirePrice")),
            AdvisorOfferTicks = advisors.GetProperty("offerMinutes").GetInt32() * TicksPerMinute,
            AdvisorPrice = Amounts(advisors.GetProperty("hirePrice")),
            AdvisorUpkeep = Hundredths(advisors.GetProperty("upkeepFoodPerMinute")),
            AdvisorFields = fields,
            BuildingStorage = Hundredths(root.GetProperty("buildingStorage")),
            Porters = units.FindIndex(x => x.Key == "porters"),
            Caravans = units.FindIndex(x => x.Key == "caravan"),
            TradeTech = techKeys.IndexOf(root.GetProperty("diplomacy").GetProperty("tradeTech").GetString()!),
            RoadTech = techKeys.IndexOf("roads"),
            RoadLevels = RoadLevelsOf(root, techKeys, Amounts),
            Works = WorksOf(root, techKeys, Amounts),
            GroveChance = GroveChanceOf(root, world),
            GroveWoodPercent = root.TryGetProperty("trees", out JsonElement tr) && tr.TryGetProperty("groveWoodPercent", out JsonElement gwp) ? gwp.GetInt32() : 60,
            ForestBiomes = ForestOf(root, world),
            BridgeTech = root.TryGetProperty("works", out JsonElement wb) && wb.TryGetProperty("bridgeTech", out JsonElement bt) ? techKeys.IndexOf(bt.GetString()!) : -1,
            BridgePrice = root.TryGetProperty("works", out JsonElement wbp) && wbp.TryGetProperty("bridgePrice", out JsonElement bp2) ? Amounts(bp2) : new long[goods.Count],
            RoadReach = root.TryGetProperty("roads", out JsonElement rr) ? rr.GetProperty("reach").GetInt32() : 25,
            RoadSupply = root.TryGetProperty("roads", out JsonElement rs) ? Amounts(rs.GetProperty("supply")) : new long[goods.Count],
            Events = State.SimGame.LoadEvents(root, goods),
            TradeResearchDiscount = root.GetProperty("diplomacy").GetProperty("researchDiscountPerPartner").GetInt32(),
            AcceptWorthPercent = root.GetProperty("diplomacy").GetProperty("acceptWorthPercent").GetInt32(),
            TreatyKnownTicks = root.GetProperty("diplomacy").GetProperty("knownMinutes").GetInt32() * TicksPerMinute,
            TreatyBorderHexes = root.GetProperty("diplomacy").GetProperty("borderHexes").GetInt32(),
            TreatyPeaceTicks = root.GetProperty("diplomacy").GetProperty("peaceMinutes").GetInt32() * TicksPerMinute,
            TreatyMinReputation = root.GetProperty("diplomacy").GetProperty("minReputation").GetInt32(),
            OfferCooldownTicks = root.GetProperty("diplomacy").GetProperty("offerCooldownMinutes").GetInt32() * TicksPerMinute,
            RefusalCooldownTicks = root.GetProperty("diplomacy").GetProperty("refusalCooldownMinutes").GetInt32() * TicksPerMinute,
            EventEveryTicks = root.TryGetProperty("events", out JsonElement evr) ? evr.GetProperty("everyMinutes").GetInt32() * TicksPerMinute : 0,
            EventSpreadTicks = root.TryGetProperty("events", out JsonElement evs) ? evs.GetProperty("spreadMinutes").GetInt32() * TicksPerMinute : 0,
            War = new WarRules(
                root.GetProperty("war").GetProperty("combatEveryTicks").GetInt32(), root.GetProperty("war").GetProperty("lowLuckPercent").GetInt32(),
                root.GetProperty("war").GetProperty("highLuckPercent").GetInt32(), root.GetProperty("war").GetProperty("defenderPercent").GetInt32(),
                root.GetProperty("war").GetProperty("defenderStrength").GetInt32(), root.GetProperty("war").GetProperty("captureBelowPeople").GetInt32(),
                root.GetProperty("war").GetProperty("surpriseReputation").GetInt32()),
        };
    }

    private static int[] GroveChanceOf(JsonElement root, GameContent world)
    {
        var chance = new int[world.Biomes.Count];
        if (root.TryGetProperty("trees", out JsonElement trees) && trees.TryGetProperty("groveChance", out JsonElement gc))
        {
            foreach (JsonProperty p in gc.EnumerateObject())
            {
                chance[world.Biome(p.Name).Id] = p.Value.GetInt32();
            }
        }

        return chance;
    }

    private static bool[] ForestOf(JsonElement root, GameContent world)
    {
        var forest = new bool[world.Biomes.Count];
        foreach (JsonElement k in root.GetProperty("terrainCost").GetProperty("forestBiomes").EnumerateArray())
        {
            forest[world.Biome(k.GetString()!).Id] = true;
        }

        return forest;
    }

    private static List<WorkKind> WorksOf(JsonElement root, List<string> techKeys, Func<JsonElement, long[]> amounts)
    {
        var list = new List<WorkKind>();
        if (!root.TryGetProperty("works", out JsonElement works))
        {
            return list;
        }

        foreach (JsonElement w in works.GetProperty("kinds").EnumerateArray())
        {
            int Int(string name) => w.TryGetProperty(name, out JsonElement e) ? e.GetInt32() : 0;
            string tech = w.GetProperty("tech").GetString()!;
            int ti = techKeys.IndexOf(tech);
            list.Add(new WorkKind(list.Count, w.GetProperty("key").GetString()!, w.GetProperty("name").GetString()!,
                ti >= 0 ? ti : throw new InvalidDataException($"sim/game.json: unknown technology '{tech}'."), amounts(w.GetProperty("price")),
                Int("seconds") * TicksPerSecond)
            {
                FromWater = Int("fromWater"),
                FarmPercent = Int("farmPercent"),
                WoundPercentPerSecond = Int("woundPercentPerSecond"),
                Strength = Int("strength") * One,
                GrowthBonus = Int("growthBonus"),
                ResistEpidemicPercent = Int("resistEpidemicPercent"),
            });
        }

        return list;
    }

    // Decimal text → hundredths without floating point (the JSON number's raw text is parsed).
    private static List<RoadLevel> RoadLevelsOf(JsonElement root, List<string> techKeys, Func<JsonElement, long[]> amounts)
    {
        var list = new List<RoadLevel>();
        if (!root.TryGetProperty("roads", out JsonElement roads))
        {
            return list;
        }

        foreach (JsonElement l in roads.GetProperty("levels").EnumerateArray())
        {
            list.Add(new RoadLevel(list.Count + 1, l.GetProperty("key").GetString()!, l.GetProperty("name").GetString()!,
                Math.Max(0, techKeys.IndexOf(l.GetProperty("tech").GetString()!)), amounts(l.GetProperty("price")),
                l.GetProperty("seconds").GetInt32() * TicksPerSecond, l.GetProperty("walkPercent").GetInt32()));
        }

        return list;
    }

    private static long Hundredths(JsonElement e)
    {
        string text = e.GetRawText();
        int dot = text.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0)
        {
            return long.Parse(text, CultureInfo.InvariantCulture) * One;
        }

        string frac = (text[(dot + 1)..] + "00")[..2];
        long whole = long.Parse(text[..dot], CultureInfo.InvariantCulture);
        long f = long.Parse(frac, CultureInfo.InvariantCulture);
        return (whole * One) + (text.StartsWith('-') ? -f : f);
    }
}
