using System;
using System.Collections.Generic;
using CivSim.Core.Ai.Utility;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.State;
using CivSim.Core.Units;

namespace CivSim.Core.Ai;

/// <summary>
/// G9 (stage 6 PR 4b, GDD 4.4, lever 3 "what we do on the land"): the governor's work groups. Materials (wood, flint, stone) come only
/// from area tasks, so the governor sends groups for what the artisans of a trade network would work: a known recipe that adds value
/// at today's prices of its outputs and uses a task's good. When the network's stock and the yearly yield of its groups fall short of
/// <see cref="AiRules.TaskReserveYears"/> years of that use, the best fed settlement sends a group to the cell of the best yield in its
/// reach, as big as the shortfall; a group whose good the network holds for <see cref="AiRules.TaskRecallAboveYears"/> years of the
/// artisans' use (of any known recipe, worth it today or not), or no
/// longer needs, is called home. At most one new group per network in a plan.
/// </summary>
public sealed class TaskPlanner
{
    private readonly GameContent _content;
    private readonly long[] _demand;
    private readonly long[] _supply;
    private readonly long[] _want;
    private readonly long[] _use;
    private readonly long[] _oneWant; // one settlement's _want, added to the network's
    private readonly long[] _oneUse;
    private readonly long[] _running;
    private readonly HexCoord[] _area;

    /// <summary>Creates the planner for <paramref name="content"/>.</summary>
    public TaskPlanner(GameContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        int goods = content.Goods.Count;
        _demand = new long[goods];
        _supply = new long[goods];
        _want = new long[goods];
        _use = new long[goods];
        _oneWant = new long[goods];
        _oneUse = new long[goods];
        _running = new long[goods];
        _area = new HexCoord[HexGlobe.RangeSize(Math.Max(1, content.Population.InfluenceMaxRange + content.Tasks.RangeBeyondZoneCells))];
    }

    /// <summary>The work groups started (and called home) this turn, with their "Why?".</summary>
    public List<(ICommand Command, AiAction Action, int Subject, int Target, Evaluation Why)> Orders { get; } = [];

    /// <summary>Plans the work groups of <paramref name="civ"/>'s networks into <see cref="Orders"/>.</summary>
    public void Plan(GameState state, CivilizationState civ, IReadOnlyList<List<int>> networks)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(networks);
        Orders.Clear();
        if (_content.Tasks.Kinds.Count == 0 || _content.Tasks.ZoneGathersMaterials)
        {
            return; // while settlements still gather materials from their zone, the AI needs no groups (stage 6 PR 4a)
        }

        foreach (List<int> members in networks)
        {
            if (members.Count == 0 || state.Settlements[members[0]].Owner != civ.Id)
            {
                continue;
            }

            (long foodDemand, long foodSupply) = Market.Tally(_content, state, members, _demand, _supply);
            Want(state, civ, members, foodDemand, foodSupply);
            Running(state, civ.Id, members);
            Recall(state, civ.Id, members);
            Start(state, civ, members);
        }
    }

    // What the network's artisans would use of every good in a year: per settlement, the most of any known recipe that adds value
    // (_want, to start a group) and of any known recipe at all (_use, to keep one: prices near zero value must not call groups
    // home and send them out again plan after plan).
    private void Want(GameState state, CivilizationState civ, List<int> members, long foodDemand, long foodSupply)
    {
        Array.Clear(_want);
        Array.Clear(_use);
        foreach (int m in members)
        {
            SettlementState s = state.Settlements[m];
            int away = CraftersAway(state, s); // knappers in a flint mine are the artisans who would work its flint
            int crafters = Demand.Crafters(_content, s) + away;
            if (crafters == 0)
            {
                continue;
            }

            Array.Clear(_oneWant);
            Array.Clear(_oneUse);
            foreach (RecipeDefinition recipe in _content.Recipes)
            {
                if (!Research.Technologies.KnowsRecipe(_content, civ, recipe.Id))
                {
                    continue;
                }

                bool worth = AddedValue(recipe, foodDemand, foodSupply) > 0;

                long runs = Runs(s, recipe, crafters, away);

                foreach (GoodQuantity q in recipe.Inputs)
                {
                    long use = q.Amount.Hundredths * runs / 100;
                    _oneUse[q.GoodId] = Math.Max(_oneUse[q.GoodId], use); // one recipe a settlement: the hungriest for this good
                    _oneWant[q.GoodId] = worth ? Math.Max(_oneWant[q.GoodId], use) : _oneWant[q.GoodId];
                }
            }

            for (int g = 0; g < _want.Length; g++)
            {
                _want[g] += _oneWant[g]; // the network's settlements all draw on the shared store
                _use[g] += _oneUse[g];
            }
        }
    }

    // Runs a year as Demand.Runs counts them, with the artisans out in work groups back home: the knappers in a flint mine are
    // the skilled people who would knap its flint.
    private long Runs(SettlementState s, RecipeDefinition recipe, int crafters, int away)
    {
        int skill = _content.SkillLinks.ForRecipe(recipe.Id);
        int workers = skill < 0 ? crafters : Math.Min(crafters, Population.Skills.Workers(_content, s, skill) + away);
        return (long)workers * 100 / (Math.Max(1, recipe.Years) * (long)Math.Max(1, recipe.Workers));
    }

    // People of the crafting strata out in the settlement's work groups.
    private int CraftersAway(GameState state, SettlementState s)
    {
        int n = 0;
        UnitBook book = state.Units;
        for (int k = 0; k < book.Count; k++)
        {
            Unit u = book[k];
            if (u.Gone || u.Owner != s.Owner || u.Role != UnitRole.Workers || u.Home != s.Id.Value)
            {
                continue;
            }

            for (int st = 0; st < u.People.Length && st < _content.Strata.Count; st++)
            {
                n += _content.Strata[st].Crafts ? u.People[st] : 0;
            }
        }

        return n;
    }

    // Value a run adds at today's prices of its outputs and the base value of its inputs (materials are worth their base value).
    private long AddedValue(RecipeDefinition recipe, long foodDemand, long foodSupply)
    {
        long value = 0;
        foreach (GoodQuantity q in recipe.Outputs)
        {
            Amount price = Market.Price(_content, q.GoodId, _demand[q.GoodId], _supply[q.GoodId], foodDemand, foodSupply);
            value += price.Hundredths * q.Amount.Hundredths / 100;
        }

        foreach (GoodQuantity q in recipe.Inputs)
        {
            value -= _content.Goods[q.GoodId].Value.Hundredths * q.Amount.Hundredths / 100;
        }

        return value;
    }

    // Yearly yield of the network's groups at work now, by good.
    private void Running(GameState state, CivId owner, List<int> members)
    {
        Array.Clear(_running);
        UnitBook book = state.Units;
        for (int k = 0; k < book.Count; k++)
        {
            Unit u = book[k];
            if (u.Gone || u.Owner != owner || u.Role != UnitRole.Workers || u.Task < 0 || u.Task >= _content.Tasks.Kinds.Count
                || !members.Contains(u.Home))
            {
                continue;
            }

            TaskKindDefinition kind = _content.Tasks.Kinds[u.Task];
            WorldState world = state.Worlds[u.World.Value];
            int cell = u.Target >= 0 ? u.Target : u.Cell;
            _running[kind.Good] += AreaTasks.Yield(_content, world.Map, kind, cell, u.TotalPeople(), 12).Hundredths;
        }
    }

    // Groups whose good the network holds plenty of, or no longer needs, come home.
    private void Recall(GameState state, CivId owner, List<int> members)
    {
        UnitBook book = state.Units;
        for (int k = 0; k < book.Count; k++)
        {
            Unit u = book[k];
            if (u.Gone || u.Owner != owner || u.Role != UnitRole.Workers || u.Task < 0 || u.Task >= _content.Tasks.Kinds.Count
                || u.Order == UnitOrder.Return || !members.Contains(u.Home)) // a group of a settlement taken from us is not ours to call
            {
                continue;
            }

            int good = _content.Tasks.Kinds[u.Task].Good;
            long want = _use[good];
            if (want > 0 && _supply[good] < want * _content.Ai.TaskRecallAboveYears)
            {
                continue;
            }

            var why = Evaluation.Of(Factor.Neutral);
            why.Note(FactorId.Shortage, want <= 0 ? -1 : _supply[good] * 12 / want);
            Orders.Add((new ReturnUnitCommand(_content, u.Id), AiAction.RecallTask, u.Home, u.Task, why));
        }
    }

    // The goods the network lacks, most first (stock and yearly yield against the reserve), and a group for the first of them that a
    // fed settlement can send (a flint shortage with no known flint in reach must not keep the woodcutters home).
    private void Start(GameState state, CivilizationState civ, List<int> members)
    {
        long tried = 0; // kinds already tried, by index (the data has a handful)
        for (int round = 0; round < _content.Tasks.Kinds.Count && round < 64; round++)
        {
            TaskKindDefinition? best = null;
            long bestGap = 0;
            foreach (TaskKindDefinition kind in _content.Tasks.Kinds)
            {
                if (kind.Id >= 64 || (tried & (1L << kind.Id)) != 0 || (kind.RequiresTechnology >= 0 && !civ.Knows(kind.RequiresTechnology)))
                {
                    continue;
                }

                long want = _want[kind.Good] * _content.Ai.TaskReserveYears;
                long gap = want - _supply[kind.Good] - (_running[kind.Good] * _content.Ai.TaskReserveYears);
                if (gap > bestGap)
                {
                    best = kind;
                    bestGap = gap;
                }
            }

            if (best is null)
            {
                return;
            }

            tried |= 1L << best.Id;
            foreach (int m in members)
            {
                SettlementState s = state.Settlements[m];
                if (s.FedPerMille < _content.Ai.TaskFedPercent * 10 || AreaTasks.RunningAt(state, s.Id.Value) >= AreaTasks.Allowed(_content, state, s))
                {
                    continue;
                }

                if (Pick(state, civ, s, best, bestGap) is { } order)
                {
                    Orders.Add(order);
                    return; // one new group a network in a plan
                }
            }
        }
    }

    // The cell of the best yield in the settlement's reach that the command accepts, and a group as big as the shortfall.
    private (ICommand, AiAction, int, int, Evaluation)? Pick(GameState state, CivilizationState civ, SettlementState s, TaskKindDefinition kind, long gap)
    {
        WorldState world = state.Worlds[s.World.Value];
        int radius = Math.Min(world.Geometry.MaxUniqueRadius, _content.Population.InfluenceMaxRange + _content.Tasks.RangeBeyondZoneCells);
        int count = world.Geometry.Range(s.Cell, radius, _area);
        int free = Math.Min(AreaTasks.FreePeople(_content, s, kind), s.TotalPopulation() - 1);
        int people = Math.Min(free, _content.Tasks.MaxPeoplePerGroup);
        if (people < _content.Ai.TaskMinPeople)
        {
            return null;
        }

        int bestCell = -1;
        long bestYield = 0;
        for (int k = 0; k < count; k++)
        {
            int cell = world.Geometry.Index(_area[k]);
            if (!AreaTasks.Fits(_content, world.Map, kind, cell) || !AreaTasks.InReach(_content, world, s, cell)
                || !Perception.MapMemory.Knows(_content, state, civ, cell))
            {
                continue;
            }

            long perPerson = AreaTasks.Yield(_content, world.Map, kind, cell, 1, 12).Hundredths;
            if (perPerson <= bestYield)
            {
                continue;
            }

            HexCoord at = world.Geometry.FromIndex(cell);
            if (new StartTaskCommand(_content, s.Id, kind.Key, at, _content.Ai.TaskMinPeople).Validate(state, civ.Id).Success)
            {
                bestCell = cell;
                bestYield = perPerson;
            }
        }

        if (bestCell < 0)
        {
            return null;
        }

        // As many as the shortfall needs in a reserve period, between the minimum and what can go.
        long perReserve = Math.Max(1, bestYield * _content.Ai.TaskReserveYears);
        int size = (int)Math.Clamp((gap + perReserve - 1) / perReserve, _content.Ai.TaskMinPeople, people);
        var why = Evaluation.Of(Factor.Neutral);
        why.Note(FactorId.Shortage, _want[kind.Good] <= 0 ? 0 : _supply[kind.Good] * 12 / _want[kind.Good]);
        why.Note(FactorId.TaskYield, bestYield * size);
        why.Note(FactorId.Fed, s.FedPerMille);
        return (new StartTaskCommand(_content, s.Id, kind.Key, world.Geometry.FromIndex(bestCell), size), AiAction.StartTask, s.Id.Value, kind.Id, why);
    }
}
