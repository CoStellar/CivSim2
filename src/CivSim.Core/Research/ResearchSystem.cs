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
/// Research choices of AI civilizations (stage 3f, until the scholar advisor of stage 4): when nothing is being
/// researched, the cheapest available technology at today's cost (inspirations and neighbours count), ties in data
/// order. Situations steer it: a tribe by the sea finds fishing cheap, one among game finds hunting cheap.
/// </summary>
public sealed class ResearchAi(GameContent content) : ICommandSource
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <inheritdoc />
    public void IssueCommands(GameState state, CommandQueue queue)
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
            int best = -1;
            long bestCost = long.MaxValue;
            for (int t = 0; t < _content.Technologies.Count; t++)
            {
                if (!Technologies.Available(_content, civ, t))
                {
                    continue;
                }

                long cost = Technologies.Cost(_content, state, facts, c, t);
                if (cost < bestCost)
                {
                    best = t;
                    bestCost = cost;
                }
            }

            if (best >= 0)
            {
                queue.Enqueue(PlayerId.Ai, civ.Id, new SetResearchCommand(_content, best));
            }
        }
    }
}
