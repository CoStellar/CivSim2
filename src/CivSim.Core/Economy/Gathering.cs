using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Economy;

/// <summary>
/// First step of the economy phase (GDD 11.1, phase 7, extraction): the gathering strata of every settlement
/// collect plants, meat and hides from the land cells around it (GDD 4.4, 5.3: hunting and foraging). What the land
/// can give is limited by the biome, fertility and relief of each cell; what people can carry home is limited by the
/// number of workers. Yields are per year and multiplied by the turn length.
/// </summary>
public sealed class GatheringSystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly PopulationRules _rules;
    private readonly Amount[] _potential;
    private readonly HexCoord[] _area;
    private readonly int _plants;
    private readonly int _meat;
    private readonly int _hides;

    /// <summary>Creates the system.</summary>
    public GatheringSystem(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _rules = content.Population;
        _potential = new Amount[content.Goods.Count];
        _area = new HexCoord[HexTorus.RangeSize(_rules.GatherRadius)];
        _plants = _rules.PlantsGood;
        _meat = _rules.MeatGood;
        _hides = _rules.HidesGood;
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Economy;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        int months = state.MonthsPerTurn(_content);
        foreach (SettlementState s in state.Settlements)
        {
            s.FoodGathered = 0;
            int workers = Workers(s);
            if (workers == 0)
            {
                continue;
            }

            WorldState world = state.Worlds[s.World.Value];
            Array.Clear(_potential);
            Amount food = LandPotential(state, world, s);
            if (food <= Amount.Zero)
            {
                continue;
            }

            // Workers can carry home only so much; the land can give only so much.
            int output = 100 + state.Effects.Total(_content.Effects, EffectStat.OutputPercent, EffectTarget.Settlement, s.World, s.Id.Value);
            Amount capacity = (_rules.FoodPerWorkerPerYear * workers).MultiplyPercent(Math.Max(0, output), Rounding.Floor);
            long share = capacity >= food ? Spoilage.One : IntMath.MulDiv(capacity.Hundredths, Spoilage.One, food.Hundredths, Rounding.Floor);
            long gathered = 0;
            for (int g = 0; g < _potential.Length; g++)
            {
                if (_potential[g] <= Amount.Zero)
                {
                    continue;
                }

                Amount perTurn = _potential[g].MultiplyRatio(share, Spoilage.One, Rounding.Floor).MultiplyRatio(months, 12, Rounding.Floor);
                s.Add(g, perTurn);
                if (_content.Goods[g].IsFood)
                {
                    gathered += perTurn.Hundredths;
                }
            }

            s.FoodGathered = gathered;
        }
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

        return workers;
    }

    // Fills _potential with what the land around the settlement gives per year; returns the food part.
    private Amount LandPotential(GameState state, WorldState world, SettlementState s)
    {
        WorldMap map = world.Map;
        int count = world.Geometry.Range(s.Cell, Math.Min(_rules.GatherRadius, world.Geometry.MaxUniqueRadius), _area);
        bool anyEffects = state.Effects.Count > 0;
        for (int k = 0; k < count; k++)
        {
            int i = world.Geometry.Index(_area[k]);
            BiomeDefinition biome = _content.Biomes[map.Biome[i]];
            if (biome.IsWater)
            {
                continue;
            }

            int percent = map.Relief[i] switch
            {
                (byte)Relief.Hills => _rules.HillsPercent,
                (byte)Relief.Mountains => _rules.MountainsPercent,
                _ => 100,
            };
            if (anyEffects)
            {
                percent = percent * Math.Max(0, 100 + state.Effects.Total(_content.Effects, EffectStat.ExtractionPercent, EffectTarget.Cell, s.World, i)) / 100;
            }

            if (percent <= 0)
            {
                continue;
            }

            // Biome food, better on fertile land: fertility 0 gives half, 50 gives the full value, 100 gives 150%.
            Amount cellFood = (_rules.FoodPerBiomeFoodPerYear * biome.Food).MultiplyRatio((50L + map.Fertility[i]) * percent, 100L * 100, Rounding.Floor);
            Amount plants = cellFood.MultiplyPercent(_rules.PlantsPercent, Rounding.Floor);
            Amount meat = cellFood - plants;
            AddPotential(_plants, plants);
            AddPotential(_meat, meat);
            AddPotential(_hides, meat.Multiply(_rules.HidesPerMeat, Rounding.Floor));

            int deposit = map.Deposit[i];
            if (deposit != 0 && map.DepositDepth[i] == 0)
            {
                foreach (GoodQuantity q in _content.Deposit(deposit).Gather)
                {
                    AddPotential(q.GoodId, q.Amount.MultiplyPercent(percent, Rounding.Floor));
                }
            }
        }

        Amount food = Amount.Zero;
        for (int g = 0; g < _potential.Length; g++)
        {
            if (_content.Goods[g].IsFood)
            {
                food += _potential[g];
            }
        }

        return food;
    }

    private void AddPotential(int good, Amount amount)
    {
        if (good >= 0)
        {
            _potential[good] += amount;
        }
    }
}
