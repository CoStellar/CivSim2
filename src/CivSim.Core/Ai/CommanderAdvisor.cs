using System;
using System.Collections.Generic;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Perception;
using CivSim.Core.Population;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// The commander (GDD 8.1; stage 4e PR 2, ADR 0022), first simple version: in a war, when no campaign of its own is out
/// against the enemy and the people are not worn out, it sends the warriors of one settlement against an enemy
/// settlement it knows: the one the war is about, or else the nearest. It sends them only when the chance to win, from
/// what it sees now, is at least <see cref="WarRules.MinWinChancePercent"/> (decision of 2026-11-07: no war it is sure to
/// lose). It decides in its own steps and, at once, when a war began or one of its camps was attacked. Battles in
/// rounds, armies of several settlements and decisions every month of a campaign come in the next PRs.
/// </summary>
public sealed class CommanderAdvisor : Advisor
{
    private readonly PathFinder _paths = new();
    private readonly List<(int Home, int Target, int Warriors)> _options = [];

    /// <summary>Creates the commander.</summary>
    public CommanderAdvisor(GameContent content)
        : base(content, AdvisorKind.Commander)
    {
    }

    /// <inheritdoc />
    public override void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        if (state.Treaties.All.Count == 0)
        {
            return;
        }

        foreach (CivilizationState civ in state.Civilizations)
        {
            bool now = Simulation.WorldClock.Steps(civ) || civ.Alarms != Simulation.Alarm.None;
            if (!civ.IsAlive || civ.Controller != PlayerId.Ai || !now)
            {
                continue;
            }

            foreach (Treaty war in state.Treaties.All)
            {
                if (war.Kind != TreatyKind.War || !war.Involves(civ.Id))
                {
                    continue;
                }

                CivId enemy = war.Other(civ.Id);
                if (state.Episodes.Campaigning(civ.Id, enemy) || Wars.WearinessOf(war, civ.Id) >= Content.War.PeaceAbove)
                {
                    continue;
                }

                Consider(state, queue, civ, war, enemy);
            }
        }
    }

    private void Consider(GameState state, CommandQueue queue, CivilizationState civ, Treaty war, CivId enemy)
    {
        WorldState world = state.Worlds[0];
        _options.Clear();
        Chooser.Clear();
        int goal = war.Goal == WarGoal.Land && war.Initiator == civ.Id ? war.GoalSettlement : -1;
        foreach (SettlementState home in state.Settlements)
        {
            if (!home.IsActive || home.Owner != civ.Id || home.World != world.Id || state.Episodes.OutFrom(civ.Id, home.Id.Value))
            {
                continue;
            }

            int warriors = SettlementAssets.Warriors(Content, home) * Content.War.SendPercent / 100;
            if (warriors < Content.War.MinWarriors)
            {
                continue;
            }

            SettlementState? target = Target(state, civ, world, home, enemy, goal);
            if (target is null)
            {
                continue;
            }

            // In sight: only with a good chance. Out of sight: the warriors go to look (an even chance until they see).
            int chance = Campaigns.WinChancePercent(Content, state, civ, target, warriors);
            if (chance >= 0 && chance < Content.War.MinWinChancePercent)
            {
                continue;
            }

            chance = chance < 0 ? 50 : chance;

            if (_paths.Find(Content, state, civ, world, world.Geometry.Index(home.Cell), world.Geometry.Index(target.Cell)) is null)
            {
                continue;
            }

            Evaluation e = Evaluation.Of(Base);
            e.Apply(FactorId.WinChance, chance * 10, chance);
            e.Note(FactorId.Distance, world.Geometry.Distance(home.Cell, target.Cell));
            e.Weigh(GoalWeight(civ, Goal.Conquest), CharacterMultiplier(civ, Goal.Conquest));
            _options.Add((home.Id.Value, target.Id.Value, warriors));
            Chooser.Offer(_options.Count - 1, e);
        }

        if (Chooser.Count == 0)
        {
            return;
        }

        (int h, int t, int n) = _options[Chooser.KeyAt(0)];
        Evaluation why = Chooser.EvaluationAt(0);
        Issue(state, queue, civ.Id, new LaunchCampaignCommand(Content, new SettlementId(h), new SettlementId(t), n), AiAction.LaunchCampaign, h, t, why);
    }

    // The settlement the war is about when it is known, otherwise the nearest known enemy settlement.
    private SettlementState? Target(GameState state, CivilizationState civ, WorldState world, SettlementState home, CivId enemy, int goal)
    {
        SettlementState? best = null;
        int bestDistance = int.MaxValue;
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.Owner != enemy || s.World != world.Id || !MapMemory.Knows(Content, state, civ, world.Geometry.Index(s.Cell)))
            {
                continue;
            }

            if (s.Id.Value == goal)
            {
                return s;
            }

            int d = world.Geometry.Distance(home.Cell, s.Cell);
            if (d < bestDistance && d <= Content.Perception.JourneyCells)
            {
                bestDistance = d;
                best = s;
            }
        }

        return best;
    }

    private const long Base = 1000;
}
