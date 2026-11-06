using System;
using System.Collections.Generic;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// The treasurer advisor: production choices of AI civilizations (GDD 8.1; rules of stage 3e, on the shared evaluation since
/// stage 4a). Once per era turn, every
/// settlement with artisans picks the recipe worth most at its network's internal prices (outputs minus inputs) among
/// the recipes its civilization knows and whose inputs the network has; nothing when no recipe pays. Needs drive prices, so a tribe without
/// clothing makes clothing, and switches to tools once its stores are full. It issues the same command a player would.
/// </summary>
public sealed class TreasurerAdvisor(GameContent content) : Advisor(content, AdvisorKind.Treasurer)
{
    private readonly GameContent _content = content;

    /// <inheritdoc />
    public override void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        if (_content.Recipes.Count == 0)
        {
            return;
        }

        foreach (List<int> members in TradeNetworks.Build(_content, state))
        {
            SettlementState first = state.Settlements[members[0]];
            CivilizationState? civ = state.FindCivilization(first.Owner);
            // A planned decision: only in the own steps that start an era-length period (ADR 0018).
            if (civ is null || civ.Controller != PlayerId.Ai || !Simulation.WorldClock.PlansNow(_content, state, civ))
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
                int pick = Best(civ, prices, supply, s.RecipeId, RandomFor(state, s.Id.Value), out long incumbent);
                int best = pick < 0 ? SettlementState.NoRecipe : Chooser.KeyAt(pick);

                // Hysteresis (GDD 8.6): the recipe the artisans make goes on while it still pays, unless another
                // one is clearly better. Before stage 4b they stopped every time a turn used up the inputs.
                long challenger = pick < 0 ? 0 : Chooser.EvaluationAt(pick).Value;
                bool keep = incumbent > 0 && challenger * 100 <= incumbent * (100 + _content.Ai.RecipeSwitchBetterByPercent);
                if (best != s.RecipeId && !keep)
                {
                    Evaluation why = pick < 0 ? NothingPays() : Chooser.EvaluationAt(pick);
                    Issue(state, queue, civ.Id, new SetRecipeCommand(_content, s.Id, best), AiAction.SetRecipe, s.Id.Value, best, why);
                }
            }
        }
    }

    // Rank in the chooser of the allowed recipe the network can feed with the highest value added; -1 when none adds value.
    // The recipe the artisans make now counts as fed: production simply waits for its inputs.
    private int Best(CivilizationState civ, Amount[] prices, long[] supply, int current, Random.DeterministicRandom? random, out long incumbent)
    {
        Chooser.Clear();
        incumbent = 0;
        for (int r = 0; r < _content.Recipes.Count; r++)
        {
            RecipeDefinition recipe = _content.Recipes[r];
            if (!Research.Technologies.KnowsRecipe(_content, civ, r))
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

            // Value added at internal prices; a recipe the network cannot feed scores zero.
            Evaluation e = Evaluation.Of(value);
            e.Note(FactorId.ValueAdded, value);
            e.Apply(FactorId.InputsAvailable, fed || r == current ? Factor.Neutral : 0, fed ? 1 : 0);
            e.Weigh(GoalWeight(civ, Goal.Prosperity), CharacterMultiplier(civ, Goal.Prosperity));
            if (r == current)
            {
                incumbent = e.Value;
            }

            Chooser.Offer(r, e);
        }

        return Pick(civ, random);
    }

    // No recipe adds value, or the network cannot feed any: the artisans stop.
    private static Evaluation NothingPays()
    {
        Evaluation e = Evaluation.Of(0);
        e.Note(FactorId.ValueAdded, 0);
        return e;
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
