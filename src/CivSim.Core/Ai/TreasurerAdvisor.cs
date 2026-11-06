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
/// stage 4a, goods by goals since PR B of 2026-11-06). Once per era-length period, every settlement with artisans picks
/// the recipe worth most among the recipes its civilization knows and whose inputs the network has; nothing when no
/// recipe pays. A recipe is worth its outputs minus its inputs at the network's internal prices, every good weighed by
/// the goal it serves (food: security, other goods: prosperity) with the strategist's weight and the character, so a
/// hungry civilization bakes bread before it weaves cloth. Valuables count more for a trading civilization whose goods go
/// to a partner. An input is priced without the deciding settlement's own appetite for it, so a recipe does not stop
/// just because it uses up its own inputs; supply chains show through prices, because recipe inputs are part of demand.
/// It issues the same command a player would.
/// </summary>
public sealed class TreasurerAdvisor(GameContent content) : Advisor(content, AdvisorKind.Treasurer)
{
    private readonly GameContent _content = content;
    private long[] _demand = [];
    private long[] _supply = [];
    private int[] _weight = [];
    private Goal[] _goal = [];

    /// <inheritdoc />
    public override void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        if (_content.Recipes.Count == 0)
        {
            return;
        }

        int goods = _content.Goods.Count;
        if (_demand.Length != goods)
        {
            _demand = new long[goods];
            _supply = new long[goods];
            _weight = new int[goods];
            _goal = new Goal[goods];
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

            bool tallied = false;
            long foodDemand = 0;
            long foodSupply = 0;
            int trade = 0;
            foreach (int m in members)
            {
                SettlementState s = state.Settlements[m];
                if (Demand.Crafters(_content, s) == 0)
                {
                    continue;
                }

                if (!tallied)
                {
                    (foodDemand, foodSupply) = Market.Tally(_content, state, members, _demand, _supply);
                    trade = TradeBonus(state, civ);
                    Weights(civ);
                    tallied = true;
                }

                int pick = Best(civ, s, foodDemand, foodSupply, trade, RandomFor(state, s.Id.Value), out long incumbent);
                int best = pick < 0 ? SettlementState.NoRecipe : Chooser.KeyAt(pick);

                // Hysteresis (GDD 8.6): the recipe the artisans make goes on while it still pays, unless another
                // one is clearly better. Before stage 4b they stopped every time a turn used up the inputs.
                long challenger = pick < 0 ? 0 : Chooser.EvaluationAt(pick).Value;
                bool keep = incumbent > 0 && challenger * 100 <= incumbent * (100 + _content.Ai.RecipeSwitchBetterByPercent);
                int chosen = s.RecipeId;
                if (best != s.RecipeId && !keep)
                {
                    Evaluation why = pick < 0 ? NothingPays() : Chooser.EvaluationAt(pick);
                    Issue(state, queue, civ.Id, new SetRecipeCommand(_content, s.Id, best), AiAction.SetRecipe, s.Id.Value, best, why);
                    chosen = best;
                }

                // The next settlements of the network see this choice: its inputs are wanted, its outputs come.
                Plan(s, chosen, ref foodSupply);
            }
        }
    }

    // A year of the chosen recipe (or of the one given up) changes what the rest of the network sees.
    private void Plan(SettlementState s, int chosen, ref long foodSupply)
    {
        if (chosen == s.RecipeId)
        {
            AddYear(s, chosen, ref foodSupply);
            return;
        }

        // Tally() counted the inputs of the recipe made now; the new one replaces it.
        if (s.RecipeId >= 0 && s.RecipeId < _content.Recipes.Count)
        {
            RecipeDefinition old = _content.Recipes[s.RecipeId];
            long runs = Demand.Runs(_content, s, old, 12);
            foreach (GoodQuantity q in old.Inputs)
            {
                _demand[q.GoodId] -= q.Amount.Hundredths * runs / 100;
            }
        }

        if (chosen >= 0)
        {
            RecipeDefinition recipe = _content.Recipes[chosen];
            long runs = Demand.Runs(_content, s, recipe, 12);
            foreach (GoodQuantity q in recipe.Inputs)
            {
                _demand[q.GoodId] += q.Amount.Hundredths * runs / 100;
            }

            AddYear(s, chosen, ref foodSupply);
        }
    }

    private void AddYear(SettlementState s, int recipeId, ref long foodSupply)
    {
        if (recipeId < 0 || recipeId >= _content.Recipes.Count)
        {
            return;
        }

        RecipeDefinition recipe = _content.Recipes[recipeId];
        long runs = Demand.Runs(_content, s, recipe, 12);
        foreach (GoodQuantity q in recipe.Outputs)
        {
            long made = q.Amount.Hundredths * runs / 100;
            _supply[q.GoodId] += made;
            foodSupply += _content.Goods[q.GoodId].IsFood ? made : 0;
        }
    }

    // Per good: the goal it serves and its weight for this civilization (goal weight times character), per mille.
    private void Weights(CivilizationState civ)
    {
        for (int g = 0; g < _weight.Length; g++)
        {
            Goal goal = _content.Goods[g].IsFood ? _content.Ai.GoalOfFood : _content.Ai.GoalOfGoods;
            _goal[g] = goal;
            _weight[g] = (int)((long)GoalWeight(civ, goal) * CharacterMultiplier(civ, goal) / Factor.Neutral);
        }
    }

    // Extra worth of valuables, per mille above neutral, while goods flow to a trade partner: the bonus times tradefulness.
    private int TradeBonus(GameState state, CivilizationState civ)
    {
        foreach (Diplomacy.Treaty t in state.Treaties.All)
        {
            if (t.Kind == Diplomacy.TreatyKind.Trade && t.Involves(civ.Id) && t.LastFlowMonth >= 0)
            {
                return _content.Ai.TradeGoodsBonusPercent * 10 * civ.Trait(Trait.Tradefulness) / 1000;
            }
        }

        return 0;
    }

    // Rank in the chooser of the allowed recipe the network can feed with the highest weighed value added; -1 when none
    // adds value. The recipe the artisans make now counts as fed: production simply waits for its inputs.
    private int Best(CivilizationState civ, SettlementState s, long foodDemand, long foodSupply, int trade, Random.DeterministicRandom? random, out long incumbent)
    {
        Chooser.Clear();
        incumbent = 0;
        int current = s.RecipeId;
        for (int r = 0; r < _content.Recipes.Count; r++)
        {
            RecipeDefinition recipe = _content.Recipes[r];
            if (!Research.Technologies.KnowsRecipe(_content, civ, r))
            {
                continue;
            }

            bool fed = true;
            bool forTrade = false;
            long plain = 0;
            long value = 0;
            for (int iQ = 0; iQ < recipe.Inputs.Count; iQ++)
            {
                GoodQuantity q = recipe.Inputs[iQ];
                fed &= _supply[q.GoodId] >= q.Amount.Hundredths;
                // What others would pay for it: the network's demand without this settlement's own recipe, at most the
                // good's base worth. Empty stores make production wait for its inputs, they do not make the work worse.
                long others = _demand[q.GoodId] - Demand.RecipeInput(_content, s, q.GoodId, 12);
                long price = Math.Min(
                    Market.Price(_content, q.GoodId, others, _supply[q.GoodId], foodDemand, foodSupply).Hundredths,
                    _content.Goods[q.GoodId].Value.Hundredths);
                plain -= price * q.Amount.Hundredths;
                value -= price * q.Amount.Hundredths * _weight[q.GoodId] / Factor.Neutral;
            }

            Goal serves = _content.Ai.GoalOfGoods;
            // Outputs at the price they would fetch after a year of this work: a store that fills up is worth less.
            long runs = Demand.Runs(_content, s, recipe, 12);
            for (int iQ = 0; iQ < recipe.Outputs.Count; iQ++)
            {
                GoodQuantity q = recipe.Outputs[iQ];
                long made = q.Amount.Hundredths * runs / 100;
                bool food = _content.Goods[q.GoodId].IsFood;
                long price = Market.Price(_content, q.GoodId, _demand[q.GoodId], _supply[q.GoodId] + made, foodDemand, foodSupply + (food ? made : 0)).Hundredths;
                long worth = price * q.Amount.Hundredths * _weight[q.GoodId] / Factor.Neutral;
                if (trade > 0 && _content.Goods[q.GoodId].Category == GoodCategory.Valuable)
                {
                    worth = worth * (Factor.Neutral + trade) / Factor.Neutral;
                    forTrade = true;
                }

                plain += price * q.Amount.Hundredths;
                value += worth;
                serves = iQ == 0 ? _goal[q.GoodId] : serves;
            }

            // Value added at internal prices, each good by its goal; a recipe the network cannot feed scores zero.
            Evaluation e = Evaluation.Of(value);
            e.Note(FactorId.ValueAdded, plain);
            e.Note(FactorId.ServesGoal, (long)serves);
            if (forTrade)
            {
                e.Note(FactorId.ForTrade, trade);
            }

            e.Apply(FactorId.InputsAvailable, fed || r == current ? Factor.Neutral : 0, fed ? 1 : 0);
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
}
