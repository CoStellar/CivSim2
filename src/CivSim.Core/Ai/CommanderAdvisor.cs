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
/// The commander (GDD 8.1; stage 4e PR 2–5, ADR 0022). In a war, when no campaign of its own is out against the enemy
/// and the people are not worn out, it gathers an army from one settlement and its own settlements nearby (up to the
/// leader's limit of the era) and sends it against an enemy settlement it knows: the one the war is about, or else the
/// nearest. It plays the battle through in its head from what it sees (<see cref="Campaigns.Expect"/>: no drawing, no
/// hidden knowledge) and sends the army only with an expected chance of at least
/// <see cref="CommanderRules.MinWinChancePercent"/> and losses of at most <see cref="CommanderRules.MaxLossPercent"/>
/// (decision of 2026-11-07: no war it is sure to lose), and only with food for the march beyond the carriers' reach
/// there and back and <see cref="CommanderRules.MinFoodMonths"/> to spare. It decides in its own steps and, at once, when
/// a war began or one of its camps was attacked. Every month of a campaign it looks again and calls the army home when
/// the chance it expects falls below <see cref="CommanderRules.RecallChancePercent"/> or the food will not bring it back.
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
            if (!civ.IsAlive || civ.Controller != PlayerId.Ai)
            {
                continue;
            }

            Review(state, queue, civ);
            if (!Simulation.WorldClock.Steps(civ) && civ.Alarms == Simulation.Alarm.None && !BecameReady(state, civ))
            {
                continue;
            }

            foreach (Treaty war in state.Treaties.All)
            {
                // A war we declared waits for our government's enactment delay (GDD 7.2; stage 4f).
                if (war.Kind != TreatyKind.War || !war.Involves(civ.Id) || !war.ReadyFor(civ.Id, state.Turn))
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

    // A war we declared became ready for our armies this month (the world clock steps on that month).
    private static bool BecameReady(GameState state, CivilizationState civ)
    {
        foreach (Treaty war in state.Treaties.All)
        {
            if (war.Kind == TreatyKind.War && war.Initiator == civ.Id && war.ReadyTick == state.Turn)
            {
                return true;
            }
        }

        return false;
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
            int farthest = 0;
            Gather(state, civ, world, home, limit, ref warriors, ref attack, ref food, ref farthest);
            if (Weigh(state, civ, world, home, target, warriors, attack, food, Campaigns.MarchMonths(Content, farthest)) is not { } e)
            {
                _musters.RemoveRange(first, _musters.Count - first);
                continue;
            }

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

    // The worth of sending `warriors` of strength `attack` with `food` from home against target, or null when the
    // commander would not: too few, a battle it expects to lose or too costly, no way, or too little food for the road.
    private Evaluation? Weigh(GameState state, CivilizationState civ, WorldState world, SettlementState home, SettlementState target, int warriors, long attack, Amount food, int musterMonths)
    {
        CommanderRules rules = Content.Ai.Commander;
        if (warriors < Content.War.MinWarriors)
        {
            return null;
        }

        // In sight: the battle as it would be (first without the river, which only helps the defenders, to spare the
        // search for a way). Out of sight: the warriors go to look, an even chance until they see.
        ExpectedBattle? battle = Campaigns.Expect(Content, state, civ, target, warriors, attack, 100, -1);
        if (battle is { } b0 && (b0.ChancePercent < rules.MinWinChancePercent || b0.LossPercent > rules.MaxLossPercent))
        {
            return null;
        }

        List<int>? path = _paths.Find(Content, state, civ, world, world.Geometry.Index(home.Cell), world.Geometry.Index(target.Cell));
        if (path is null)
        {
            return null;
        }

        int from = path.Count >= 2 ? path[^2] : world.Geometry.Index(home.Cell);
        battle = battle is null ? null : Campaigns.Expect(Content, state, civ, target, warriors, attack, 100, from);
        if (battle is { } b && (b.ChancePercent < rules.MinWinChancePercent || b.LossPercent > rules.MaxLossPercent))
        {
            return null;
        }

        // Food for the road: what the warriors can take along must last the march beyond the carriers' reach there and
        // back, with some to spare.
        Amount carried = Amount.Min(food, Amount.Min(Campaigns.FoodFor(Content, warriors, Content.War.CarryFoodMonths), SettlementAssets.CarryCapacity(Content, state, civ.Id, warriors)));
        int foodMonths = Campaigns.MonthsOf(Content, carried, warriors);
        int beyond = Campaigns.BeyondSupplyCells(Content, state, civ.Id, world.Geometry.Index(target.Cell));
        if (foodMonths < (2 * Campaigns.MarchMonths(Content, beyond)) + rules.MinFoodMonths)
        {
            return null;
        }

        // Food for the whole campaign (stage 4e PR 6): the gathering, the march there, a month at the target and the march
        // back, from what the warriors take and what carriers can bring from our settlements near home and near the target.
        int months = musterMonths + (2 * Campaigns.MarchMonths(Content, path.Count)) + 1 + rules.MinFoodMonths;
        if (Campaigns.MonthsOf(Content, Reachable(state, civ, world, home, target), warriors) < months)
        {
            return null;
        }

        int chance = battle?.ChancePercent ?? 50;
        Evaluation e = Evaluation.Of(Base);
        e.Apply(FactorId.WinChance, chance * 10, chance);
        if (battle is { } seen)
        {
            e.Apply(FactorId.ExpectedLoss, 1000 - (seen.LossPercent * 5), seen.LossPercent);
        }

        e.Note(FactorId.Distance, path.Count);
        e.Note(FactorId.FoodMonths, foodMonths);
        e.Note(FactorId.BeyondSupply, beyond);
        e.Weigh(GoalWeight(civ, Goal.Conquest), CharacterMultiplier(civ, Goal.Conquest));
        return e;
    }

    // Every month of a campaign: the commander looks again at what its army sees and at its food, and calls it home when
    // the battle ahead promises no victory or the food will not bring the warriors back within the carriers' reach.
    private void Review(GameState state, CommandQueue queue, CivilizationState civ)
    {
        CommanderRules rules = Content.Ai.Commander;
        WorldState world = state.Worlds[0];
        foreach (Episode e in state.Episodes.All)
        {
            if (e.Ended || e.Kind != EpisodeKind.Campaign || e.Civ != civ.Id || e.Phase is not (CampaignPhase.Muster or CampaignPhase.March))
            {
                continue;
            }

            SettlementState? target = state.FindSettlement(new SettlementId(e.Target));
            if (target is not { IsActive: true } || target.Owner != e.Other)
            {
                continue; // the campaign turns back by itself
            }

            int cell = world.Geometry.Index(target.Cell);
            int distance = world.Geometry.Distance(world.Geometry.FromIndex(e.Cell), target.Cell);
            int from = distance <= 1 ? e.Cell : -1;
            ExpectedBattle? battle = Campaigns.Expect(Content, state, civ, target, e.Warriors, Campaigns.BattleStrength(Content, e), e.Morale, from);
            int left = Campaigns.FoodMonthsLeft(Content, e);
            int beyond = Campaigns.BeyondSupplyCells(Content, state, civ.Id, cell);
            int need = Campaigns.MarchMonths(Content, Math.Min(distance, beyond)) + Campaigns.MarchMonths(Content, beyond);
            bool hopeless = battle is { } b && b.ChancePercent < rules.RecallChancePercent;
            bool starving = !e.Foraging && left < need;
            if (!hopeless && !starving)
            {
                continue;
            }

            Evaluation why = Evaluation.Of(Base);
            if (battle is { } seen)
            {
                why.Note(FactorId.WinChance, seen.ChancePercent);
                why.Note(FactorId.ExpectedLoss, seen.LossPercent);
            }

            why.Note(FactorId.FoodMonths, left);
            if (starving)
            {
                why.Note(FactorId.BeyondSupply, beyond);
            }

            Issue(state, queue, civ.Id, new RecallCampaignCommand(e.Id), AiAction.RecallCampaign, e.Home, e.Target, why);
        }
    }

    // Adds the warriors of own settlements within the muster range of home (nearest first, ties by id) until the limit.
    private void Gather(GameState state, CivilizationState civ, WorldState world, SettlementState home, int limit, ref int warriors, ref long attack, ref Amount food, ref int farthest)
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
            farthest = Math.Max(farthest, world.Geometry.Distance(home.Cell, s.Cell));
            _musters.Add(new CampaignMuster(s.Id, n));
        }
    }

    // Food our settlements within the carriers' reach of home or of the target can give a campaign.
    private Amount Reachable(GameState state, CivilizationState civ, WorldState world, SettlementState home, SettlementState target)
    {
        Amount food = Amount.Zero;
        int reach = Content.War.CarrierCells;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == civ.Id && s.World == world.Id
                && (world.Geometry.Distance(home.Cell, s.Cell) <= reach || world.Geometry.Distance(target.Cell, s.Cell) <= reach))
            {
                food += Campaigns.SpareFood(Content, s);
            }
        }

        return food;
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
