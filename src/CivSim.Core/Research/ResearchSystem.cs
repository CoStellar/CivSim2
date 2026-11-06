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

        int months = state.MonthsPerTurn(_content);
        CivFacts[]? facts = null;
        for (int c = 0; c < state.Civilizations.Count; c++)
        {
            CivilizationState civ = state.Civilizations[c];
            if (!civ.IsAlive)
            {
                continue;
            }

            Technologies.EnsureSize(_content, civ);
            civ.LastScience = Technologies.Science(_content, state, civ.Id, months);
            civ.ResearchProgress += civ.LastScience;
            if (!Technologies.Available(_content, civ, civ.Researching))
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

        return Technologies.Available(Content, c, Technology)
            ? CommandResult.Ok
            : CommandResult.Fail("That technology is already known or its requirements are not.");
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.FindCivilization(civ)!.Researching = Technology;
    }
}

/// <summary>
/// The scholar advisor: research choices of AI civilizations (GDD 8.1; rule of stage 3f, on the shared evaluation since
/// stage 4a). When nothing is being researched, every available technology scores the same worth minus its cost today
/// (inspirations and neighbours count), so the cheapest wins, ties in data order. Situations steer it: a tribe by the
/// sea finds fishing cheap, one among game finds hunting cheap. Goal weights per technology come with stage 4b.
/// </summary>
public sealed class ScholarAdvisor(GameContent content) : Ai.Advisor(content, Ai.Utility.AdvisorKind.Scholar)
{
    /// <summary>Worth of any technology before its cost, hundredths of research points: far above every real cost.</summary>
    public const long TechnologyWorth = 1_000_000_000_000_000;

    private readonly GameContent _content = content;

    /// <inheritdoc />
    public override void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        CivFacts[]? facts = null;
        for (int c = 0; c < state.Civilizations.Count; c++)
        {
            CivilizationState civ = state.Civilizations[c];
            if (!civ.IsAlive || civ.Controller != PlayerId.Ai || Technologies.Available(_content, civ, civ.Researching))
            {
                continue;
            }

            facts ??= CivFacts.Measure(_content, state);
            Chooser.Clear();
            for (int t = 0; t < _content.Technologies.Count; t++)
            {
                if (!Technologies.Available(_content, civ, t))
                {
                    continue;
                }

                Ai.Utility.Evaluation e = Ai.Utility.Evaluation.Of(TechnologyWorth);
                e.Subtract(Ai.Utility.FactorId.ResearchCost, Technologies.Cost(_content, state, facts, c, t));
                e.Weigh(GoalWeight(civ, Ai.Goal.Science), CharacterMultiplier(civ, Ai.Goal.Science));
                Chooser.Offer(t, e);
            }

            int pick = Chooser.Pick(RandomFor(state, civ.Id.Value));
            if (pick >= 0)
            {
                int best = Chooser.KeyAt(pick);
                Issue(state, queue, civ.Id, new SetResearchCommand(_content, best), Ai.Utility.AiAction.SetResearch, -1, best, Chooser.EvaluationAt(pick));
            }
        }
    }
}
