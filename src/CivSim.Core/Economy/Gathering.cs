using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Economy;

/// <summary>
/// Good and bad years (GDD 4.4): every turn each 32×32 map chunk gets a yield factor between
/// 100 − swing and 100 + swing percent, drawn from the climate stream in chunk order.
/// </summary>
public sealed class YieldYears
{
    private readonly int _swing;
    private readonly List<int[]> _percent = [];

    /// <summary>Creates the table; all years are average until the first draw.</summary>
    public YieldYears(int swingPercent)
    {
        _swing = swingPercent;
    }

    /// <summary>Yield factor of the chunk holding <paramref name="cell"/> this turn, in percent.</summary>
    public int PercentAt(WorldState world, int cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        int w = world.Id.Value;
        return w < _percent.Count ? _percent[w][world.Map.Chunks.ChunkOfIndex(cell)] : 100;
    }

    /// <summary>Draws this turn's factors.</summary>
    public void Draw(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        DeterministicRandom rng = state.Random[RandomStreamId.Climate];
        for (int w = 0; w < state.Worlds.Count; w++)
        {
            int chunks = state.Worlds[w].Map.Chunks.Count;
            if (_percent.Count <= w)
            {
                _percent.Add(new int[chunks]);
            }

            int[] table = _percent[w];
            for (int c = 0; c < chunks; c++)
            {
                table[c] = _swing == 0 ? 100 : 100 - _swing + rng.NextInt((2 * _swing) + 1);
            }
        }
    }
}

/// <summary>
/// World phase (GDD 11.1, phase 1): draws the turn's good and bad years, and lets wild food grow back on cells no
/// settlement works (cells that are worked grow back inside <see cref="GatheringSystem"/>, year by year).
/// </summary>
public sealed class WildFoodSystem(GameContent content, YieldYears years) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly YieldYears _years = years ?? throw new ArgumentNullException(nameof(years));

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.World;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        _years.Draw(state);
        // Land nobody claims grows back over the whole world step; claimed land grows back in its owner's own steps.
        long keep = Spoilage.KeptShare(_content.Population.WildRegrowthBasisPointsPerYear, state.StepMonths);
        foreach (WorldState world in state.Worlds)
        {
            WorldMap map = world.Map;
            ReadOnlySpan<ushort> wild = map.WildFood.Values;
            ReadOnlySpan<int> claim = map.Claim.Values;
            for (int i = 0; i < wild.Length; i++)
            {
                if (wild[i] < WorldMap.FullWildFood && claim[i] == 0)
                {
                    long missing = IntMath.MulDiv(WorldMap.FullWildFood - wild[i], keep, Spoilage.One, Rounding.Floor);
                    map.WildFood.Set(i, (ushort)(WorldMap.FullWildFood - missing));
                }
            }
        }
    }
}

/// <summary>
/// First step of the economy phase (GDD 11.1, phase 7, extraction): the gathering strata of every settlement
/// collect plants, meat and hides from the cells of its influence zone (GDD 4.4, 5.4). A cell gives its full yield
/// only while its wild food is untouched; gathering eats the wild food and it grows back slowly. The turn is
/// computed year by year, so a tribe that gathers too hard sees its land run down within the turn.
/// </summary>
public sealed class GatheringSystem : ITurnSystem
{
    private const long Full = 1_000_000;

    private readonly GameContent _content;
    private readonly PopulationRules _rules;
    private readonly YieldYears _years;
    private readonly HexCoord[] _area;
    private readonly List<int> _cells = [];
    private readonly List<long> _food = [];
    private readonly List<long> _worked = [];
    private readonly List<int> _wild = [];
    private readonly Amount[] _gathered;
    private readonly Dictionary<int, (int Known, Research.TechBenefits Benefits)> _benefits = [];
    private Research.TechBenefits? _none;
    private Research.TechBenefits _current = null!;
    private long[] _addedStamp = [];
    private long _addedRun;

    /// <summary>Creates the system.</summary>
    public GatheringSystem(GameContent content, YieldYears years)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _rules = content.Population;
        _years = years ?? throw new ArgumentNullException(nameof(years));
        _area = new HexCoord[HexGlobe.RangeSize(Math.Max(1, _rules.InfluenceMaxRange))];
        _gathered = new Amount[content.Goods.Count];
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Economy;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        foreach (SettlementState s in state.Settlements)
        {
            int months = WorldClock.MonthsOf(state, s.Owner);
            if (months == 0)
            {
                continue; // its civilization waits for its own step (ADR 0018)
            }

            long farmed = s.Farmers > 0 ? s.FoodGathered : 0; // farming ran first this turn
            s.FoodGathered = farmed;
            WorldState world = state.Worlds[s.World.Value];
            _current = Benefits(state, s.Owner);
            CollectCells(state, world, s);
            if (_cells.Count == 0)
            {
                continue;
            }

            // Effects on the settlement and on its civilization (cooking, irrigation) both count.
            int output = 100 + (state.Effects.Count == 0
                ? 0
                : state.Effects.Total(_content.Effects, EffectStat.OutputPercent, EffectTarget.Settlement, s.World, s.Id.Value)
                    + state.Effects.Total(_content.Effects, EffectStat.OutputPercent, EffectTarget.Civilization, WorldId.Earth, s.Owner.Value));
            Amount capacityPerYear = (_rules.FoodPerWorkerPerYear * Workers(s)).MultiplyPercent(Math.Max(0, output), Rounding.Floor);
            WorkTheLand(world.Map, capacityPerYear.Hundredths, months);
            Deliver(world, s);
        }
    }

    /// <summary>What the technologies of <paramref name="civ"/> add to gathering (cached until it learns something).</summary>
    public Research.TechBenefits Benefits(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        _none ??= Research.TechBenefits.Empty(_content);
        CivilizationState? c = state.FindCivilization(civ);
        if (c is null || c.KnownCount == 0)
        {
            return _none;
        }

        if (!_benefits.TryGetValue(civ.Value, out (int Known, Research.TechBenefits Benefits) cached) || cached.Known != c.KnownCount)
        {
            cached = (c.KnownCount, Research.TechBenefits.Of(_content, c));
            _benefits[civ.Value] = cached;
        }

        return cached.Benefits;
    }

    // The settlement's cells and the food each would give per year if untouched (hundredths).
    private void CollectCells(GameState state, WorldState world, SettlementState s)
    {
        _cells.Clear();
        _food.Clear();
        _worked.Clear();
        _wild.Clear();
        WorldMap map = world.Map;
        int claim = s.Id.Value + 1;
        int count = world.Geometry.Range(s.Cell, Math.Min(_rules.InfluenceMaxRange, world.Geometry.MaxUniqueRadius), _area);
        bool shore = _current.FromWater;
        if (shore && _addedStamp.Length != world.Geometry.CellCount)
        {
            _addedStamp = new long[world.Geometry.CellCount];
        }

        _addedRun++;
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        for (int k = 0; k < count; k++)
        {
            int i = world.Geometry.Index(_area[k]);
            if (map.Claim[i] != claim)
            {
                // Fishers also work the water next to the zone (zones do not cross water).
                if (!shore || !_content.Biomes[map.Biome[i]].IsWater || _addedStamp[i] == _addedRun || !NextToClaim(world, i, claim, n6))
                {
                    continue;
                }

                _addedStamp[i] = _addedRun;
            }

            int percent = CellPercent(state, world, s, i);
            _cells.Add(i);
            _food.Add(percent <= 0 ? 0 : CellFoodPerYear(map, i, false, _current).MultiplyPercent(percent, Rounding.Floor).Hundredths);
            _worked.Add(0);
            _wild.Add(map.WildFood[i]);
        }
    }

    private static bool NextToClaim(WorldState world, int cell, int claim, Span<HexCoord> n6)
    {
        world.Geometry.GetNeighbors(world.Geometry.FromIndex(cell), n6);
        foreach (HexCoord c in n6)
        {
            if (world.Map.Claim[world.Geometry.Index(c)] == claim)
            {
                return true;
            }
        }

        return false;
    }

    // Relief, good or bad year and effects, in percent of the biome's yield.
    private int CellPercent(GameState state, WorldState world, SettlementState s, int i)
    {
        WorldMap map = world.Map;
        int percent = map.Relief[i] switch
        {
            (byte)Relief.Hills => _rules.HillsPercent,
            (byte)Relief.Mountains => _rules.MountainsPercent,
            _ => 100,
        };
        percent = percent * _years.PercentAt(world, i) / 100;
        if (state.Effects.Count > 0)
        {
            percent = percent * Math.Max(0, 100 + state.Effects.Total(_content.Effects, EffectStat.ExtractionPercent, EffectTarget.Cell, s.World, i)) / 100;
        }

        return percent;
    }

    /// <summary>Food a land cell gives per year when its wild food is untouched (biome, fertility, growing season, deposit).</summary>
    public Amount CellFoodPerYear(WorldMap map, int cell) => CellFoodPerYear(map, cell, nextTurn: false);

    /// <summary>
    /// Like <see cref="CellFoodPerYear(WorldMap, int)"/>; with live seasons <paramref name="nextTurn"/> gives the food the
    /// cell will give in the next turn's season (what the tribe AI expects).
    /// </summary>
    public Amount CellFoodPerYear(WorldMap map, int cell, bool nextTurn) => CellFoodPerYear(map, cell, nextTurn, _none ??= Research.TechBenefits.Empty(_content));

    /// <summary>
    /// Like <see cref="CellFoodPerYear(WorldMap, int, bool)"/> for a civilization with these technology benefits: water
    /// gives food only through them (fishing), deposits and biomes give their extra food.
    /// </summary>
    public Amount CellFoodPerYear(WorldMap map, int cell, bool nextTurn, Research.TechBenefits benefits)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(benefits);
        BiomeDefinition biome = _content.Biomes[map.Biome[cell]];
        Amount extra = FoodOf(benefits.ForBiome(biome.Id)) + FoodOf(benefits.ForDeposit(map.DepositDepth[cell] == 0 ? map.Deposit[cell] : 0));
        if (biome.IsWater)
        {
            return extra;
        }

        // Biome food, better on fertile land: fertility 0 gives half, 50 gives the full value, 100 gives 150%.
        // A short growing season and long winters (stage 1b) cut it (ClimateModel.WildFoodPercent).
        // Live seasons: what grows in this turn's season, hunting still gives a little in winter.
        long season = map.LiveSeasons
            ? _content.Seasons.WinterFoodPercent + ((100L - _content.Seasons.WinterFoodPercent) * (nextTurn ? map.NextSeasonGrowth[cell] : map.SeasonGrowth[cell]) / 100)
            : WorldGen.ClimateModel.WildFoodPercent(map.GrowingSeason[cell], map.YearHundredths, _rules.SurvivableDeadSeasonHundredths);
        Amount food = (_rules.FoodPerBiomeFoodPerYear * biome.Food).MultiplyRatio((50L + map.Fertility[cell]) * season, 10_000, Rounding.Floor);
        int deposit = map.Deposit[cell];
        if (deposit != 0 && map.DepositDepth[cell] == 0)
        {
            food += FoodOf(_content.Deposit(deposit).Gather);
        }

        return food + extra;
    }

    private Amount FoodOf(IReadOnlyList<GoodQuantity> goods)
    {
        Amount food = Amount.Zero;
        for (int k = 0; k < goods.Count; k++)
        {
            if (_content.Goods[goods[k].GoodId].IsFood)
            {
                food += goods[k].Amount;
            }
        }

        return food;
    }

    // Year by year: what the land offers, what the workers can carry, how much wild food is eaten and grows back.
    private void WorkTheLand(WorldMap map, long capacityPerYear, int months)
    {
        int steps = Math.Max(1, months / 12);
        int stepMonths = months / steps;
        for (int y = 0; y < steps; y++)
        {
            long offered = 0;
            for (int c = 0; c < _cells.Count; c++)
            {
                offered += _food[c] * _wild[c] / WorldMap.FullWildFood;
            }

            // Share of what the land offers that the workers take this year (Full = everything).
            long share = offered <= 0 ? 0 : Math.Min(Full, IntMath.MulDiv(capacityPerYear, Full, offered, Rounding.Floor));
            for (int c = 0; c < _cells.Count; c++)
            {
                int w = _wild[c];
                _worked[c] += share * w / WorldMap.FullWildFood * stepMonths / 12;
                long eaten = IntMath.MulDiv(share * _rules.WildDepletionPerMillePerYear, stepMonths, Full * 12, Rounding.Floor);
                long regrown = IntMath.MulDiv((WorldMap.FullWildFood - w) * (long)_rules.WildRegrowthBasisPointsPerYear, stepMonths, 10_000L * 12, Rounding.Floor);
                _wild[c] = (int)Math.Clamp(w - eaten + regrown, 0, WorldMap.FullWildFood);
            }
        }

        for (int c = 0; c < _cells.Count; c++)
        {
            map.WildFood.Set(_cells[c], (ushort)_wild[c]);
        }
    }

    // Turns worked shares into goods: plants and meat from the biome, hides with the meat, biome materials (wood),
    // deposit goods (which run out when the deposit is not renewable).
    private void Deliver(WorldState world, SettlementState s)
    {
        Array.Clear(_gathered);
        WorldMap map = world.Map;
        for (int c = 0; c < _cells.Count; c++)
        {
            long worked = _worked[c];
            if (worked == 0 || _food[c] == 0)
            {
                continue;
            }

            int i = _cells[c];
            long fullFood = CellFoodPerYear(map, i, false, _current).Hundredths;
            if (fullFood == 0)
            {
                continue;
            }

            // _food holds the cell's food after relief, year and effects; scale every good the same way.
            long scale = IntMath.MulDiv(_food[c], Full, fullFood, Rounding.Floor);
            BiomeDefinition biome = _content.Biomes[map.Biome[i]];
            IReadOnlyList<GoodQuantity> biomeExtra = _current.ForBiome(biome.Id);
            for (int k = 0; k < biomeExtra.Count; k++)
            {
                Add(biomeExtra[k].GoodId, biomeExtra[k].Amount, scale, worked);
            }

            if (!biome.IsWater)
            {
                Amount biomeFood = (_rules.FoodPerBiomeFoodPerYear * biome.Food).MultiplyRatio(50L + map.Fertility[i], 100, Rounding.Floor);
                Amount plants = biomeFood.MultiplyPercent(_rules.PlantsPercent, Rounding.Floor);
                Amount meat = biomeFood - plants;
                Add(_rules.PlantsGood, plants, scale, worked);
                Add(_rules.MeatGood, meat, scale, worked);
                Add(_rules.HidesGood, meat.Multiply(_rules.HidesPerMeat, Rounding.Floor), scale, worked);
                for (int k = 0; k < biome.Gather.Count; k++)
                {
                    Add(biome.Gather[k].GoodId, biome.Gather[k].Amount, scale, worked);
                }
            }

            int deposit = map.Deposit[i];
            if (deposit != 0 && map.DepositDepth[i] == 0)
            {
                DepositDefinition def = _content.Deposit(deposit);
                long taken = 0;
                for (int k = 0; k < def.Gather.Count; k++)
                {
                    taken += Add(def.Gather[k].GoodId, def.Gather[k].Amount, scale, worked);
                }

                IReadOnlyList<GoodQuantity> techExtra = _current.ForDeposit(deposit);
                for (int k = 0; k < techExtra.Count; k++)
                {
                    taken += Add(techExtra[k].GoodId, techExtra[k].Amount, scale, worked);
                }

                // Flint and stone run out (GDD 4.4): the richness is in whole units; an empty deposit is gone.
                if (!def.Renewable && taken > 0)
                {
                    long left = map.DepositRichness[i] - ((taken + 99) / 100);
                    if (left <= 0)
                    {
                        map.Deposit.Set(i, 0);
                        map.DepositRichness.Set(i, 0);
                    }
                    else
                    {
                        map.DepositRichness.Set(i, (int)left);
                    }
                }
            }
        }

        long food = 0;
        for (int g = 0; g < _gathered.Length; g++)
        {
            if (_gathered[g] > Amount.Zero)
            {
                s.Add(g, _gathered[g]);
                food += _content.Goods[g].IsFood ? _gathered[g].Hundredths : 0;
            }
        }

        s.FoodGathered += food;
    }

    // perYear × scale × worked, where scale and worked are fractions of Full (worked already counts the years).
    // Returns what was added, in hundredths.
    private long Add(int good, Amount perYear, long scale, long worked)
    {
        if (good < 0 || perYear <= Amount.Zero)
        {
            return 0;
        }

        long scaled = IntMath.MulDiv(perYear.Hundredths, scale, Full, Rounding.Floor);
        long added = IntMath.MulDiv(scaled, worked, Full, Rounding.Floor);
        _gathered[good] += Amount.FromHundredths(added);
        return added;
    }

    private int Workers(SettlementState s)
    {
        int workers = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length; k++)
        {
            if (_content.Strata[k].Gathers)
            {
                workers += people[k];
            }
        }

        return Math.Max(0, workers - s.Farmers);
    }
}
