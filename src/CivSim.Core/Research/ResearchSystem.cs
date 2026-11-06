using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Research;

/// <summary>
/// Research (GDD 6, phase 10; stage 3f). Every living civilization's people make research points; they go to the
/// technology it researches. When the points reach the technology's cost now (base cost, cut by inspirations and by
/// neighbours that know it), the technology is learned, its effects start, and the civilization may enter a new era.
/// Points left over carry on to the next technology, also while no technology is chosen.
/// </summary>
public sealed class ResearchSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.ScienceAndInfluence;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        if (_content.Technologies.Count == 0)
        {
            return;
        }

        CivFacts[]? facts = null;
        for (int c = 0; c < state.Civilizations.Count; c++)
        {
            CivilizationState civ = state.Civilizations[c];
            int months = civ.IsAlive ? civ.StepMonths : 0;
            if (months == 0)
            {
                continue; // research advances in the civilization's own steps (ADR 0018)
            }

            Technologies.EnsureSize(_content, civ);
            civ.LastScience = Technologies.Science(_content, state, civ.Id, months);
            civ.ResearchProgress += civ.LastScience;

            // A technology is learned only while the civilization has the people it needs (stage 4F).
            if (!Technologies.CanResearch(_content, state, civ, civ.Researching))
            {
                continue;
            }

            // The cost never falls below the minimum share: measure the situation only when that much is collected.
            long floor = _content.Technologies[civ.Researching].Cost.Hundredths * _content.Research.MinCostPercent / 100;
            if (civ.ResearchProgress < floor)
            {
                continue;
            }

            facts ??= CivFacts.Measure(_content, state);
            long cost = Technologies.Cost(_content, state, facts, c, civ.Researching);
            if (civ.ResearchProgress < cost)
            {
                continue;
            }

            civ.ResearchProgress -= cost;
            int learned = civ.Researching;
            civ.Researching = CivilizationState.NoResearch;
            foreach (string line in Technologies.Learn(_content, state, civ, learned))
            {
                context.Chronicle(line);
            }
        }
    }
}

/// <summary>Chooses the technology a civilization researches (GDD 6.1; stage 3f).</summary>
public sealed record SetResearchCommand(GameContent Content, int Technology) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Economy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? c = state.FindCivilization(civ);
        if (c is null || !c.IsAlive)
        {
            return CommandResult.Fail("Civilization does not exist.");
        }

        if (!Technologies.Available(Content, c, Technology))
        {
            return CommandResult.Fail("That technology is already known or its requirements are not.");
        }

        return Technologies.PeopleReady(Content, state, c, Technology)
            ? CommandResult.Ok
            : CommandResult.Fail("The civilization does not have the people this technology needs yet.");
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.FindCivilization(civ)!.Researching = Technology;
    }
}

/// <summary>
/// The scholar advisor: research choices of AI civilizations (GDD 6.1, 8.1; priority times cost since PR B of 2026-11-06).
/// When nothing is being researched, every available technology is scored by its priority: what it would give this
/// civilization per year, in barter units, each part weighed by the goal it serves (strategist's weight and character):
/// knowledge for its own sake (science), its field (society: influence), gathering on the cells of its zone (fishing by the
/// sea, hunting among game; food serves security, other goods prosperity), the recipes it opens for its artisans, its
/// effects (fire against the cold only where it is cold), villages (security, by the fertile share of the zone), and a share of the best technology it opens
/// the way to. The cost is an obstacle: cheapness against the cheapest choice, squared for an incurious civilization and
/// softer for a curious one. The AI draws among the best three, so the choice is not always the best one.
/// </summary>
public sealed class ScholarAdvisor(GameContent content) : Ai.Advisor(content, Ai.Utility.AdvisorKind.Scholar)
{
    private readonly GameContent _content = content;
    private long[] _own = [];
    private Ai.Goal[] _goalOf = [];
    private long[] _crafters = [];

    /// <inheritdoc />
    public override void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        CivFacts[]? facts = null;
        for (int c = 0; c < state.Civilizations.Count; c++)
        {
            CivilizationState civ = state.Civilizations[c];
            // The scholar chooses again when nothing can be researched now (stage 4F: the people may be missing).
            if (!civ.IsAlive || civ.Controller != PlayerId.Ai || Technologies.CanResearch(_content, state, civ, civ.Researching))
            {
                continue;
            }

            if (facts is null)
            {
                facts = CivFacts.Measure(_content, state);
                CountCrafters(state);
            }

            long cheapest = long.MaxValue;
            for (int t = 0; t < _content.Technologies.Count; t++)
            {
                if (Technologies.CanResearch(_content, state, civ, t))
                {
                    cheapest = Math.Min(cheapest, Math.Max(1, Technologies.Cost(_content, state, facts, c, t)));
                }
            }

            if (cheapest == long.MaxValue)
            {
                continue;
            }

            Priorities(state, civ, facts[c], _crafters[c]);
            int curiosity = civ.Trait(Ai.Trait.Curiosity);
            Chooser.Clear();
            for (int t = 0; t < _content.Technologies.Count; t++)
            {
                if (!Technologies.CanResearch(_content, state, civ, t))
                {
                    continue;
                }

                // The worth of the technology, plus a share of the best one it opens the way to.
                long leads = 0;
                foreach (TechnologyDefinition next in _content.Technologies)
                {
                    if (!civ.Knows(next.Id) && (Contains(next.Requires, t) || Contains(next.RequiresAny, t)))
                    {
                        leads = Math.Max(leads, _own[next.Id]);
                    }
                }

                long lead = leads * _content.Ai.Scholar.LeadsToPercent / 100;
                long priority = Math.Max(1, _own[t] + lead);
                Ai.Utility.Evaluation e = Ai.Utility.Evaluation.Of(priority);
                e.Note(Ai.Utility.FactorId.ServesGoal, (long)_goalOf[t]);
                if (lead > 0)
                {
                    e.Note(Ai.Utility.FactorId.LeadsTo, lead);
                }

                long cost = Math.Max(1, Technologies.Cost(_content, state, facts, c, t));
                e.Apply(Ai.Utility.FactorId.ResearchCost, CostFactor(cheapest, cost, curiosity), cost);
                Chooser.Offer(t, e);
            }

            int pick = Pick(civ, RandomFor(state, civ.Id.Value));
            if (pick >= 0)
            {
                int best = Chooser.KeyAt(pick);
                Issue(state, queue, civ.Id, new SetResearchCommand(_content, best), Ai.Utility.AiAction.SetResearch, -1, best, Chooser.EvaluationAt(pick));
            }
        }
    }

    /// <summary>
    /// The cost obstacle, per mille: r × (r + (1 − r) × curiosity × relief), where r is the cheapest cost against this
    /// one. Without curiosity a technology twice as dear gets a quarter, with full curiosity (and full relief) a half.
    /// </summary>
    public int CostFactor(long cheapest, long cost, int curiosity)
    {
        long r = Math.Clamp(cheapest * Ai.Utility.Factor.Neutral / Math.Max(1, cost), 0, Ai.Utility.Factor.Neutral);
        long soft = r + ((Ai.Utility.Factor.Neutral - r) * Math.Clamp(curiosity, 0, 1000) * _content.Ai.Scholar.CuriosityCostReliefPercent / 100 / 1000);
        return (int)(r * soft / Ai.Utility.Factor.Neutral);
    }

    /// <summary>
    /// The scholar's own worth per year of every technology for <paramref name="civ"/> (before the cost and the share
    /// of what it leads to), from the facts of its zone and its artisans; 0 for technologies it knows.
    /// </summary>
    public long[] Worths(GameState state, CivilizationState civ, CivFacts facts, long crafters)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(facts);
        Priorities(state, civ, facts, crafters);
        return (long[])_own.Clone();
    }

    // Worth per year of every technology for this civilization (its own gains, weighed by goals), into _own and _goalOf.
    private void Priorities(GameState state, CivilizationState civ, CivFacts facts, long crafters)
    {
        int count = _content.Technologies.Count;
        if (_own.Length != count)
        {
            _own = new long[count];
            _goalOf = new Ai.Goal[count];
        }

        Span<long> parts = stackalloc long[Ai.Character.GoalCount];
        ScholarRules rules = _content.Ai.Scholar;
        long people = facts.People;
        int tolerance = state.Effects.Count == 0
            ? 0
            : state.Effects.Total(_content.Effects, EffectStat.ColdToleranceDegrees, EffectTarget.Civilization, WorldId.Earth, civ.Id.Value);
        int lacking = facts.ColdestWinter == int.MaxValue ? 0 : Math.Max(0, _content.Population.ComfortColdBelow - tolerance - facts.ColdestWinter);
        for (int t = 0; t < count; t++)
        {
            TechnologyDefinition tech = _content.Technologies[t];
            if (civ.Knows(t))
            {
                _own[t] = 0;
                continue;
            }

            parts.Clear();
            parts[(int)Ai.Goal.Science] += people * rules.BaseWorthPerPerson.Hundredths;
            Ai.Goal? field = tech.Field switch
            {
                TechField.Science => Ai.Goal.Science,
                TechField.Society => Ai.Goal.Influence,
                TechField.Military => Ai.Goal.Conquest,
                _ => null,
            };
            if (field is { } f)
            {
                parts[(int)f] += people * rules.FieldWorthPerPerson.Hundredths;
            }

            // Gathering: the extra yields on the cells of the zone (and the water next to it).
            foreach (GatherBonus g in tech.Gather)
            {
                long cells = g.DepositId > 0 ? facts.DepositCells[g.DepositId] : facts.BiomeCells[g.BiomeId];
                foreach (GoodQuantity q in g.Goods)
                {
                    parts[(int)GoalOf(q.GoodId)] += cells * q.Amount.Hundredths * _content.Goods[q.GoodId].Value.Hundredths / 100 * rules.GatherWorthPercent / 100;
                }
            }

            // Recipes: value added at the goods' worth per year of the civilization's artisans.
            foreach (int r in tech.Recipes)
            {
                RecipeDefinition recipe = _content.Recipes[r];
                long added = 0;
                bool fed = true;
                foreach (GoodQuantity q in recipe.Outputs)
                {
                    added += q.Amount.Hundredths * _content.Goods[q.GoodId].Value.Hundredths / 100;
                }

                foreach (GoodQuantity q in recipe.Inputs)
                {
                    added -= q.Amount.Hundredths * _content.Goods[q.GoodId].Value.Hundredths / 100;
                    fed &= facts.Goods[q.GoodId] > 0 || Gives(tech, q.GoodId);
                }

                long perYear = Math.Max(0, added) * crafters / Math.Max(1, (long)recipe.Workers * recipe.Years);
                Ai.Goal goal = recipe.Outputs.Count > 0 ? GoalOf(recipe.Outputs[0].GoodId) : _content.Ai.GoalOfGoods;
                parts[(int)goal] += fed ? perYear : perYear * rules.MissingInputPercent / 100;
            }

            // Effects on the civilization, by what one point of their stat is worth.
            foreach (TechEffect te in tech.Effects)
            {
                EffectStat stat = _content.Effects[te.EffectId].Stat;
                foreach (EffectWorth w in rules.Effects)
                {
                    if (w.Stat != stat)
                    {
                        continue;
                    }

                    long points = w.LowerIsBetter ? -te.Strength : te.Strength;
                    if (w.OnlyWhenCold)
                    {
                        points = Math.Min(points, lacking);
                    }

                    parts[(int)w.Goal] += Math.Max(0, points) * people * w.PerHundredPeoplePerPoint.Hundredths / 100;
                }
            }

            // Villages: farming feeds more people on the same land, as far as the zone is fertile (1.0 for land of the
            // best fertility, 100, everywhere; poor or barren land counts less).
            if (OpensVillages(t) && facts.LandCells > 0)
            {
                parts[(int)_content.Ai.GoalOfFood] += people * rules.VillageWorthPerPerson.Hundredths * facts.FarmYield / (150L * facts.LandCells);
            }

            long sum = 0;
            long top = -1;
            for (int goal = 0; goal < parts.Length; goal++)
            {
                if (parts[goal] == 0)
                {
                    continue;
                }

                var g = (Ai.Goal)goal;
                long weighed = parts[goal] * GoalWeight(civ, g) / Ai.Utility.Factor.Neutral * CharacterMultiplier(civ, g) / Ai.Utility.Factor.Neutral;
                sum += weighed;
                if (weighed > top)
                {
                    top = weighed;
                    _goalOf[t] = g;
                }
            }

            _own[t] = sum;
        }
    }

    private Ai.Goal GoalOf(int good) => _content.Goods[good].IsFood ? _content.Ai.GoalOfFood : _content.Ai.GoalOfGoods;

    private static bool Gives(TechnologyDefinition tech, int good)
    {
        foreach (GatherBonus g in tech.Gather)
        {
            foreach (GoodQuantity q in g.Goods)
            {
                if (q.GoodId == good)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool OpensVillages(int technology)
    {
        foreach (SettlementLevelDefinition level in _content.SettlementLevels)
        {
            if (level.RequiresTechnology == technology)
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(IReadOnlyList<int> list, int value)
    {
        for (int k = 0; k < list.Count; k++)
        {
            if (list[k] == value)
            {
                return true;
            }
        }

        return false;
    }

    // Artisans of every civilization (people who work recipes), by civilization index.
    private void CountCrafters(GameState state)
    {
        if (_crafters.Length != state.Civilizations.Count)
        {
            _crafters = new long[state.Civilizations.Count];
        }

        Array.Clear(_crafters);
        foreach (SettlementState s in state.Settlements)
        {
            int c = s.IsActive ? state.IndexOfCivilization(s.Owner) : -1;
            if (c >= 0)
            {
                _crafters[c] += Economy.Demand.Crafters(_content, s);
            }
        }
    }
}
