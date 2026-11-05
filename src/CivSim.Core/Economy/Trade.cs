using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.State;

namespace CivSim.Core.Economy;

/// <summary>
/// Trade networks (GDD 4, CORE simplified: no physical transport; stage 3e). The lived-in settlements of one
/// civilization on one world form a network when a chain of settlements links them, each link at most
/// <see cref="TradeRules.LinkCells"/> apart. Members are listed in settlement order, networks in order of their
/// first member, so everything built on them is deterministic.
/// </summary>
public static class TradeNetworks
{
    /// <summary>Indices into <see cref="GameState.Settlements"/>, one list per network.</summary>
    public static List<List<int>> Build(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        IReadOnlyList<SettlementState> all = state.Settlements;
        var parent = new int[all.Count];
        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        for (int a = 0; a < all.Count; a++)
        {
            SettlementState sa = all[a];
            if (!sa.IsActive)
            {
                continue;
            }

            WorldState world = state.Worlds[sa.World.Value];
            for (int b = a + 1; b < all.Count; b++)
            {
                SettlementState sb = all[b];
                if (sb.IsActive && sb.Owner == sa.Owner && sb.World == sa.World
                    && world.Geometry.Distance(sa.Cell, sb.Cell) <= content.Trade.LinkCells)
                {
                    int ra = Root(parent, a);
                    int rb = Root(parent, b);
                    parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
                }
            }
        }

        var byRoot = new Dictionary<int, List<int>>();
        var networks = new List<List<int>>();
        for (int i = 0; i < all.Count; i++)
        {
            if (!all[i].IsActive)
            {
                continue;
            }

            int r = Root(parent, i);
            if (!byRoot.TryGetValue(r, out List<int>? members))
            {
                members = [];
                byRoot[r] = members;
                networks.Add(members);
            }

            members.Add(i);
        }

        return networks;
    }

    /// <summary>Share of goods that arrives over <paramref name="cells"/> of distance, percent.</summary>
    public static int ArrivingPercent(GameContent content, int cells)
    {
        ArgumentNullException.ThrowIfNull(content);
        return 100 - Math.Min(90, content.Trade.LossPercentPerCell * cells);
    }

    private static int Root(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }
}

/// <summary>What a settlement needs per year and per turn (stage 3e): its people's needs and its recipe's inputs.</summary>
public static class Demand
{
    /// <summary>Food (any food) the people need per year, hundredths.</summary>
    public static long FoodPerYear(GameContent content, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        long sum = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length && k < content.Strata.Count; k++)
        {
            for (int iNeed = 0; iNeed < content.Strata[k].Needs.Count; iNeed++)
            {
                NeedDefinition need = content.Strata[k].Needs[iNeed];
                if (need.AnyOfCategory && need.Category == GoodCategory.Food)
                {
                    sum += need.PerYear.Hundredths * people[k];
                }
            }
        }

        return sum;
    }

    /// <summary>One good the people need per year by name (tools, clothing), hundredths.</summary>
    public static long NeedPerYear(GameContent content, SettlementState s, int good)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        long sum = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length && k < content.Strata.Count; k++)
        {
            for (int iNeed = 0; iNeed < content.Strata[k].Needs.Count; iNeed++)
            {
                NeedDefinition need = content.Strata[k].Needs[iNeed];
                if (!need.AnyOfCategory && need.GoodId == good)
                {
                    sum += need.PerYear.Hundredths * people[k];
                }
            }
        }

        return sum;
    }

    /// <summary>People of the strata that work recipes (artisans).</summary>
    public static int Crafters(GameContent content, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        int n = 0;
        ReadOnlySpan<int> people = s.PopulationView;
        for (int k = 0; k < people.Length && k < content.Strata.Count; k++)
        {
            n += content.Strata[k].Crafts ? people[k] : 0;
        }

        return n;
    }

    /// <summary>Recipe runs the artisans could do in <paramref name="months"/>, hundredths of a run (inputs aside).</summary>
    public static long Runs(GameContent content, SettlementState s, RecipeDefinition recipe, int months)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        return (long)Crafters(content, s) * 100 * months / (12L * Math.Max(1, recipe.Years) * Math.Max(1, recipe.Workers));
    }

    /// <summary>Of a good, what the settlement's recipe uses in <paramref name="months"/> at full work, hundredths.</summary>
    public static long RecipeInput(GameContent content, SettlementState s, int good, int months)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        if (s.RecipeId < 0 || s.RecipeId >= content.Recipes.Count)
        {
            return 0;
        }

        RecipeDefinition recipe = content.Recipes[s.RecipeId];
        long runs = Runs(content, s, recipe, months);
        long sum = 0;
        for (int iQ = 0; iQ < recipe.Inputs.Count; iQ++)
        {
            GoodQuantity q = recipe.Inputs[iQ];
            sum += q.GoodId == good ? q.Amount.Hundredths * runs / 100 : 0;
        }

        return sum;
    }

    /// <summary>True when the recipe's goods belong to an era already reached (technologies come in stage 3f).</summary>
    public static bool Allowed(GameContent content, GameState state, RecipeDefinition recipe)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(recipe);
        int era = state.CalendarEra() + 1;
        for (int iQ = 0; iQ < recipe.Inputs.Count; iQ++)
        {
            GoodQuantity q = recipe.Inputs[iQ];
            if (content.Goods[q.GoodId].Era > era)
            {
                return false;
            }
        }

        for (int iQ = 0; iQ < recipe.Outputs.Count; iQ++)
        {
            GoodQuantity q = recipe.Outputs[iQ];
            if (content.Goods[q.GoodId].Era > era)
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// Internal prices of a trade network (GDD 4.6, stage 3e): the good's value times demand over supply, within
/// <see cref="TradeRules.PriceRangePercent"/>. Demand is a year of the members' needs and recipe inputs; supply is what
/// the network has in stock. All foods share the price ratio of food as a whole (people eat any food).
/// </summary>
public static class Market
{
    /// <summary>Price of every good in barter units (hundredths) for the network <paramref name="members"/>.</summary>
    public static Amount[] Prices(GameContent content, GameState state, IReadOnlyList<int> members)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(members);
        int goods = content.Goods.Count;
        var demand = new long[goods];
        var supply = new long[goods];
        long foodDemand = 0;
        long foodSupply = 0;
        foreach (int m in members)
        {
            SettlementState s = state.Settlements[m];
            foodDemand += Demand.FoodPerYear(content, s);
            ReadOnlySpan<Amount> stock = s.StockView;
            for (int g = 0; g < goods && g < stock.Length; g++)
            {
                supply[g] += stock[g].Hundredths;
                foodSupply += content.Goods[g].IsFood ? stock[g].Hundredths : 0;
                demand[g] += Demand.NeedPerYear(content, s, g) + Demand.RecipeInput(content, s, g, 12);
            }
        }

        var prices = new Amount[goods];
        for (int g = 0; g < goods; g++)
        {
            int ratio = content.Goods[g].IsFood ? Ratio(content, foodDemand, foodSupply) : Ratio(content, demand[g], supply[g]);
            prices[g] = content.Goods[g].Value.MultiplyPercent(ratio, Rounding.HalfAwayFromZero);
        }

        return prices;
    }

    private static int Ratio(GameContent content, long demand, long supply)
    {
        (int min, int max) = content.Trade.PriceRangePercent;
        if (demand <= 0)
        {
            return min;
        }

        return supply <= 0 ? max : (int)Math.Clamp(demand * 100 / supply, min, max);
    }
}
