using System;
using System.Collections.Generic;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Episodes;
using CivSim.Core.Numerics;
using CivSim.Core.Perception;
using CivSim.Core.Population;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>
/// The commander (GDD 8.1; stage 4e PR 2–3, ADR 0022), first simple version: in a war, when no campaign of its own is out
/// against the enemy and the people are not worn out, it gathers an army from one settlement and its own settlements
/// nearby (up to the leader's limit of the era) and sends it against an enemy settlement it knows: the one the war is
/// about, or else the nearest. It sends it only when the chance to win, from what it sees now (armed strength of both
/// sides), is at least <see cref="WarRules.MinWinChancePercent"/> (decision of 2026-11-07: no war it is sure to lose).
/// It decides in its own steps and, at once, when a war began or one of its camps was attacked. Battles in rounds and
/// decisions every month of a campaign come in the next PRs.
/// </summary>
public sealed class CommanderAdvisor : Advisor
{
    private readonly PathFinder _paths = new();
    private readonly List<(int Home, int Target, int Warriors, int First, int Count)> _options = [];
    private readonly List<CampaignMuster> _musters = [];
    private readonly List<(int Distance, int Id)> _near = [];

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
        _musters.Clear();
        Chooser.Clear();
        int goal = war.Goal == WarGoal.Land && war.Initiator == civ.Id ? war.GoalSettlement : -1;
        int limit = Campaigns.Era(Content, civ).ArmyLimit;
        foreach (SettlementState home in state.Settlements)
        {
            if (!home.IsActive || home.Owner != civ.Id || home.World != world.Id || state.Episodes.OutFrom(civ.Id, home.Id.Value))
            {
                continue;
            }

            int own = Math.Min(Campaigns.Sendable(Content, home), limit);
            if (own <= 0)
            {
                continue;
            }

            SettlementState? target = Target(state, civ, world, home, enemy, goal);
            if (target is null)
            {
                continue;
            }

            // The army: our warriors and those of our settlements nearby, nearest first, up to what one leader leads.
            int first = _musters.Count;
            int warriors = own;
            long attack = Campaigns.ArmedStrength(Content, civ, home, own, useUp: false);
            Amount food = Campaigns.SpareFood(Content, home);
            Gather(state, civ, world, home, limit, ref warriors, ref attack, ref food);
            Amount month = Campaigns.FoodFor(Content, warriors, 1);
            int foodMonths = (int)Math.Min(120, food.Hundredths / Math.Max(1, month.Hundredths));
            if (warriors < Content.War.MinWarriors || foodMonths < Content.War.MinFoodMonths)
            {
                _musters.RemoveRange(first, _musters.Count - first);
                continue;
            }

            // In sight: only with a good chance. Out of sight: the warriors go to look (an even chance until they see).
            int chance = Campaigns.WinChancePercent(Content, state, civ, target, attack);
            if ((chance >= 0 && chance < Content.War.MinWinChancePercent)
                || _paths.Find(Content, state, civ, world, world.Geometry.Index(home.Cell), world.Geometry.Index(target.Cell)) is null)
            {
                _musters.RemoveRange(first, _musters.Count - first);
                continue;
            }

            chance = chance < 0 ? 50 : chance;
            Evaluation e = Evaluation.Of(Base);
            e.Apply(FactorId.WinChance, chance * 10, chance);
            e.Note(FactorId.Distance, world.Geometry.Distance(home.Cell, target.Cell));
            e.Note(FactorId.FoodMonths, foodMonths);
            e.Weigh(GoalWeight(civ, Goal.Conquest), CharacterMultiplier(civ, Goal.Conquest));
            _options.Add((home.Id.Value, target.Id.Value, own, first, _musters.Count - first));
            Chooser.Offer(_options.Count - 1, e);
        }

        if (Chooser.Count == 0)
        {
            return;
        }

        (int h, int t, int n, int from, int count) = _options[Chooser.KeyAt(0)];
        Evaluation why = Chooser.EvaluationAt(0);
        CampaignMuster[] others = count == 0 ? [] : _musters.GetRange(from, count).ToArray();
        Issue(state, queue, civ.Id, new LaunchCampaignCommand(Content, new SettlementId(h), new SettlementId(t), n, others), AiAction.LaunchCampaign, h, t, why);
    }

    // Adds the warriors of own settlements within the muster range of home (nearest first, ties by id) until the limit.
    private void Gather(GameState state, CivilizationState civ, WorldState world, SettlementState home, int limit, ref int warriors, ref long attack, ref Amount food)
    {
        _near.Clear();
        foreach (SettlementState s in state.Settlements)
        {
            if (s != home && s.IsActive && s.Owner == civ.Id && s.World == world.Id && !state.Episodes.OutFrom(civ.Id, s.Id.Value))
            {
                int d = world.Geometry.Distance(home.Cell, s.Cell);
                if (d <= Content.War.MusterCells && Campaigns.Sendable(Content, s) > 0)
                {
                    _near.Add((d, s.Id.Value));
                }
            }
        }

        _near.Sort();
        foreach ((_, int id) in _near)
        {
            if (warriors >= limit)
            {
                break;
            }

            SettlementState s = state.FindSettlement(new SettlementId(id))!;
            int n = Math.Min(Campaigns.Sendable(Content, s), limit - warriors);
            warriors += n;
            attack += Campaigns.ArmedStrength(Content, civ, s, n, useUp: false);
            food += Campaigns.SpareFood(Content, s);
            _musters.Add(new CampaignMuster(s.Id, n));
        }
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
