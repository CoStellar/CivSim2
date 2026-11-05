using System;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Economy;

/// <summary>
/// Economy phase, after extraction (GDD 11.1, phase 7: consumption and needs): every stratum takes what it needs
/// from its own settlement's warehouse (GDD 5.1). Food comes first and the most perishable food is eaten first, so
/// grain keeps for bad years. How much was eaten and how many higher needs were met feed the population phase.
/// </summary>
public sealed class ConsumptionSystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly int[] _foodOrder;

    /// <summary>Creates the system.</summary>
    public ConsumptionSystem(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        int foods = 0;
        foreach (GoodDefinition g in content.Goods)
        {
            foods += g.IsFood ? 1 : 0;
        }

        _foodOrder = new int[foods];
        int n = 0;
        foreach (GoodDefinition g in content.Goods)
        {
            if (g.IsFood)
            {
                _foodOrder[n++] = g.Id;
            }
        }

        // Most perishable first; ties by id, so the order is fixed.
        Array.Sort(_foodOrder, (a, b) =>
        {
            int c = content.Goods[b].SpoilageBasisPointsPerYear.CompareTo(content.Goods[a].SpoilageBasisPointsPerYear);
            return c != 0 ? c : a.CompareTo(b);
        });
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
            Consume(s, months);
        }
    }

    private void Consume(SettlementState s, int months)
    {
        long foodNeeded = 0;
        long foodEaten = 0;
        long higherNeeded = 0;
        long higherMet = 0;
        ReadOnlySpan<int> people = s.PopulationView;

        // Basic needs of every stratum first, then higher needs, so nobody starves for someone's luxuries.
        for (int pass = 0; pass < 2; pass++)
        {
            bool basic = pass == 0;
            for (int k = 0; k < people.Length; k++)
            {
                if (people[k] == 0)
                {
                    continue;
                }

                foreach (NeedDefinition need in _content.Strata[k].Needs)
                {
                    if (need.Basic != basic)
                    {
                        continue;
                    }

                    Amount wanted = (need.PerYear * people[k]).MultiplyRatio(months, 12, Rounding.Ceiling);
                    Amount got = need.AnyOfCategory ? TakeCategory(s, need.Category, wanted) : TakeGood(s, need.GoodId, wanted);
                    bool isFood = need.AnyOfCategory ? need.Category == GoodCategory.Food : _content.Goods[need.GoodId].IsFood;
                    if (basic && isFood)
                    {
                        foodNeeded += wanted.Hundredths;
                        foodEaten += got.Hundredths;
                    }
                    else if (!basic)
                    {
                        higherNeeded += wanted.Hundredths;
                        higherMet += got.Hundredths;
                    }
                }
            }
        }

        s.FoodNeeded = foodNeeded;
        s.FoodEaten = foodEaten;
        s.HigherNeedsPerMille = higherNeeded == 0 ? 1000 : (int)(higherMet * 1000 / higherNeeded);
    }

    private Amount TakeCategory(SettlementState s, GoodCategory category, Amount wanted)
    {
        Amount got = Amount.Zero;
        if (category == GoodCategory.Food)
        {
            foreach (int g in _foodOrder)
            {
                got += TakeGood(s, g, wanted - got);
                if (got >= wanted)
                {
                    break;
                }
            }

            return got;
        }

        foreach (GoodDefinition g in _content.Goods)
        {
            if (g.Category == category)
            {
                got += TakeGood(s, g.Id, wanted - got);
                if (got >= wanted)
                {
                    break;
                }
            }
        }

        return got;
    }

    private static Amount TakeGood(SettlementState s, int good, Amount wanted)
    {
        Amount have = s.StockView[good];
        Amount take = Amount.Min(have, wanted);
        if (take > Amount.Zero)
        {
            s.TryTake(good, take);
        }

        return take;
    }
}
