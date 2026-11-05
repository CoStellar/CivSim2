using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// Production choices of AI civilizations (stage 3e, until the treasurer advisor of stage 4). Once per era turn, every
/// settlement with artisans picks the recipe worth most at its network's internal prices (outputs minus inputs) among
/// the recipes whose inputs the network has; nothing when no recipe pays. Needs drive prices, so a tribe without
/// clothing makes clothing, and switches to tools once its stores are full. It issues the same command a player would.
/// </summary>
public sealed class EconomyAi(GameContent content) : ICommandSource
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <inheritdoc />
    public void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        if (_content.Recipes.Count == 0 || !state.IsDecisionTurn(_content))
        {
            return;
        }

        foreach (List<int> members in TradeNetworks.Build(_content, state))
        {
            SettlementState first = state.Settlements[members[0]];
            CivilizationState? civ = state.FindCivilization(first.Owner);
            if (civ is null || !civ.IsAlive || civ.Controller != PlayerId.Ai)
            {
                continue;
            }

            Amount[]? prices = null;
            long[]? supply = null;
            foreach (int m in members)
            {
                SettlementState s = state.Settlements[m];
                if (Demand.Crafters(_content, s) == 0)
                {
                    continue;
                }

                prices ??= Market.Prices(_content, state, members);
                supply ??= Supply(state, members);
                int best = Best(state, prices, supply);
                if (best != s.RecipeId)
                {
                    queue.Enqueue(PlayerId.Ai, civ.Id, new SetRecipeCommand(_content, s.Id, best));
                }
            }
        }
    }

    // The allowed recipe the network can feed with the highest value added; NoRecipe when none adds value.
    private int Best(GameState state, Amount[] prices, long[] supply)
    {
        int best = SettlementState.NoRecipe;
        long bestValue = 0;
        for (int r = 0; r < _content.Recipes.Count; r++)
        {
            RecipeDefinition recipe = _content.Recipes[r];
            if (!Demand.Allowed(_content, state, recipe))
            {
                continue;
            }

            bool fed = true;
            long value = 0;
            for (int iQ = 0; iQ < recipe.Inputs.Count; iQ++)
            {
                GoodQuantity q = recipe.Inputs[iQ];
                fed &= supply[q.GoodId] >= q.Amount.Hundredths;
                value -= prices[q.GoodId].Hundredths * q.Amount.Hundredths;
            }

            for (int iQ = 0; iQ < recipe.Outputs.Count; iQ++)
            {
                GoodQuantity q = recipe.Outputs[iQ];
                value += prices[q.GoodId].Hundredths * q.Amount.Hundredths;
            }

            if (fed && value > bestValue)
            {
                best = r;
                bestValue = value;
            }
        }

        return best;
    }

    private long[] Supply(GameState state, List<int> members)
    {
        var supply = new long[_content.Goods.Count];
        foreach (int m in members)
        {
            ReadOnlySpan<Amount> stock = state.Settlements[m].StockView;
            for (int g = 0; g < supply.Length && g < stock.Length; g++)
            {
                supply[g] += stock[g].Hundredths;
            }
        }

        return supply;
    }
}
