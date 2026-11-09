using System;
using System.Collections.Generic;
using System.Globalization;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Governance;

/// <summary>What stability is made of (GDD 7.3), points; <see cref="Total"/> is the stability.</summary>
public readonly record struct StabilityParts(int Mood, int Bonus, int Overload, int Crisis, int Anarchy, int CapitalLost)
{
    /// <summary>The stability, 0–100.</summary>
    public int Total => Math.Clamp(Mood + Bonus - Overload - Crisis - Anarchy - CapitalLost, 0, 100);
}

/// <summary>
/// Capital, administration and stability (GDD 7.3; stage 4f PR 3). The capital is the leader's settlement: the first camp,
/// the largest village once the people settle down (stage 5b), and when it is lost the largest settlement left. Every settlement uses administration points, more the farther it is
/// from the capital by road; a state over its government's capacity loses stability. Stability is the mood of the
/// settlements weighed by the political influence of their strata; below a threshold unrest lowers output.
/// </summary>
public static class Stability
{
    /// <summary>Gives every civilization of a new game its first camp as the capital.</summary>
    public static void Begin(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (CivilizationState civ in state.Civilizations)
        {
            civ.Capital = Largest(state, civ.Id);
        }
    }

    /// <summary>True while <paramref name="civ"/> is in anarchy after a change of government (no state decisions, GDD 7.2).</summary>
    public static bool InAnarchy(CivilizationState civ, long month)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return month < civ.AnarchyUntilMonth;
    }

    /// <summary>The administration points the government of <paramref name="civ"/> manages (unlimited without governments).</summary>
    public static int Capacity(GameContent content, CivilizationState civ) => Governments.Of(content, civ)?.Administration ?? int.MaxValue;

    /// <summary>
    /// Moves the capital of <paramref name="civ"/> when it is no longer a lived-in settlement of its own: to the largest
    /// one left. A capital taken by another civilization lowers stability for some years. A capital that is still a
    /// wandering camp moves to the largest settled village as soon as there is one (stage 5b). Returns the chronicle line or null.
    /// </summary>
    public static string? KeepCapital(GameContent content, GameState state, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        SettlementState? old = civ.Capital == CivilizationState.NoCapital ? null : state.FindSettlement(new SettlementId(civ.Capital));
        if (old is { IsActive: true } && old.Owner == civ.Id)
        {
            if (!content.SettlementLevels[old.Level].Mobile)
            {
                return null;
            }

            int settled = Largest(state, civ.Id, content);
            if (settled == CivilizationState.NoCapital)
            {
                return null;
            }

            civ.Capital = settled;
            return $"{Governments.Title(content, civ)} made {state.FindSettlement(new SettlementId(settled))!.Name} its capital.";
        }

        int next = Largest(state, civ.Id);
        civ.Capital = next;
        if (old is null || old.Owner == civ.Id || next == CivilizationState.NoCapital)
        {
            return null; // abandoned or merged by its own people: the leader simply moves
        }

        civ.CapitalLostUntilMonth = Math.Max(civ.CapitalLostUntilMonth, state.ActionMonth + (content.Stability.CapitalLostYears * 12L));
        string name = state.FindSettlement(new SettlementId(next))!.Name;
        return $"{Governments.Title(content, civ)} lost its capital {old.Name}; {name} is the new capital.";
    }

    /// <summary>
    /// The parts of the stability of <paramref name="civ"/> for its step starting in <paramref name="month"/> with
    /// <paramref name="adminUsed"/> administration points used. A crisis, anarchy or a lost capital counts for the part of
    /// the step it lasts.
    /// </summary>
    public static StabilityParts Parts(GameContent content, GameState state, CivilizationState civ, int adminUsed, long month)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        StabilityRules rules = content.Stability;
        GovernmentDefinition? g = Governments.Of(content, civ);
        long weight = 0;
        long sum = 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.Owner != civ.Id)
            {
                continue;
            }

            ReadOnlySpan<int> people = s.PopulationView;
            for (int k = 0; k < people.Length; k++)
            {
                long w = (long)people[k] * (g?.InfluenceOf(k) ?? 1);
                weight += w;
                sum += w * s.Mood;
            }
        }

        int mood = weight > 0 ? (int)(sum / weight) : 50;
        int capacity = Capacity(content, civ);
        int overload = 0;
        if (adminUsed > capacity)
        {
            long overPercent = (long)(adminUsed - capacity) * 100 / capacity;
            overload = (int)Math.Min(rules.OverloadMaxPenalty, overPercent * rules.OverloadPenaltyPer100Percent / 100);
        }

        int window = content.Time.PulseActionMonths; // ADR 0025: crises and anarchy count in months of action; a pulse lasts this many
        return new StabilityParts(
            mood,
            g?.StabilityBonus ?? 0,
            overload,
            Share(rules.CrisisPenalty, civ.CrisisUntilMonth, month, window),
            Share(rules.AnarchyPenalty, civ.AnarchyUntilMonth, month, window),
            Share(rules.CapitalLostPenalty, civ.CapitalLostUntilMonth, month, window));
    }

    // A penalty for the part of the pulse (window months of action from month) it lasts.
    private static int Share(int penalty, long until, long month, int window) =>
        (int)((long)penalty * Math.Clamp(until - month, 0, window) / window);

    /// <summary>
    /// Recomputes the administration, stability and settlement loyalty of <paramref name="civ"/> (in its own step) and
    /// sets the unrest effect when stability is low. <paramref name="roads"/> finds the road cost from the capital.
    /// </summary>
    public static void Update(GameContent content, GameState state, CivilizationState civ, RoadCosts roads, EffectDefinition? unrest)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(roads);
        StabilityRules rules = content.Stability;
        SettlementState? capital = civ.Capital == CivilizationState.NoCapital ? null : state.FindSettlement(new SettlementId(civ.Capital));
        if (capital is not null)
        {
            WorldState world = state.Worlds[capital.World.Value];
            roads.Targets.Clear();
            foreach (SettlementState s in state.Settlements)
            {
                if (s.IsActive && s.Owner == civ.Id && s.World == capital.World)
                {
                    roads.Targets.Add(world.Geometry.Index(s.Cell));
                }
            }

            roads.From(content, world, world.Geometry.Index(capital.Cell), rules.RoadSearchCost);
        }

        long used = 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == civ.Id)
            {
                used += rules.AdminPerSettlement + ((long)rules.AdminPerRoadCost * RoadCost(state, roads, capital, s, rules.RoadSearchCost));
            }
        }

        civ.AdminUsed = (int)Math.Min(int.MaxValue, used);
        int before = civ.Stability;
        civ.Stability = Parts(content, state, civ, civ.AdminUsed, state.ActionMonth).Total;
        int reach = civ.Government >= 0 && civ.Government < content.Governments.Count ? content.Governments[civ.Government].Reach : 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == civ.Id)
            {
                int cost = RoadCost(state, roads, capital, s, rules.RoadSearchCost);
                s.Loyalty = reach > 0 && cost > reach && s.Id.Value != civ.Capital
                    ? 0 // beyond the government's reach
                    : Math.Clamp(civ.Stability - (cost * rules.LoyaltyPer10RoadCost / 10), 0, 100);
            }
        }

        if (unrest is null)
        {
            return;
        }

        if (civ.Stability < rules.UnrestBelow)
        {
            // Until just after the next update (the next own step, at least a year away), which sets it again.
            int strength = -(rules.UnrestBelow - civ.Stability) * rules.UnrestOutputPercentPerPoint;
            state.Effects.Add(unrest, WorldId.Earth, civ.Id.Value, strength, state.Month + Math.Max(12, civ.StepMonths) + 1);
        }
        else if (before < rules.UnrestBelow)
        {
            state.Effects.Add(unrest, WorldId.Earth, civ.Id.Value, 0, state.Month); // replaces the unrest; gone at once
        }
    }

    /// <summary>The unrest line for the chronicle when <paramref name="civ"/> falls below the threshold, or null.</summary>
    public static string? UnrestLine(GameContent content, CivilizationState civ, int before)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(civ);
        int below = content.Stability.UnrestBelow;
        if (civ.Stability < below && before >= below)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Unrest spread in the {Governments.Title(content, civ)}: stability fell to {civ.Stability}.");
        }

        return civ.Stability >= below && before < below
            ? string.Create(CultureInfo.InvariantCulture, $"Calm returned to the {Governments.Title(content, civ)}: stability rose to {civ.Stability}.")
            : null;
    }

    private static int RoadCost(GameState state, RoadCosts roads, SettlementState? capital, SettlementState s, int far)
    {
        if (capital is null || s.World != capital.World)
        {
            return far;
        }

        return roads.To(state.Worlds[s.World.Value].Geometry.Index(s.Cell), far);
    }

    // The lived-in settlement of the civilization with the most people (ties: the lowest id), or NoCapital.
    private static int Largest(GameState state, CivId civ, GameContent? settledOnly = null)
    {
        int best = CivilizationState.NoCapital;
        int most = -1;
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.Owner != civ || (settledOnly is not null && settledOnly.SettlementLevels[s.Level].Mobile))
            {
                continue;
            }

            int people = s.TotalPopulation();
            if (people > most)
            {
                best = s.Id.Value;
                most = people;
            }
        }

        return best;
    }
}

/// <summary>
/// Road cost from one cell (GDD 7.3: administration counts the road, not the straight line): the cheapest sum of the
/// movement costs of the land cells entered, searched up to a limit, or until every cell of <see cref="Targets"/> is
/// settled. Buffers are kept between searches.
/// </summary>
public sealed class RoadCosts
{
    private readonly PriorityQueue<int, int> _queue = new();
    private Population.Influence? _influence;
    private int[] _cost = [];
    private int[] _stamp = [];
    private int[] _target = [];
    private int _run;

    /// <summary>Cells the next search needs; it stops once all are settled (empty = search up to the limit).</summary>
    public List<int> Targets { get; } = [];

    /// <summary>Searches from <paramref name="start"/> on <paramref name="world"/> up to <paramref name="limit"/>.</summary>
    public void From(GameContent content, WorldState world, int start, int limit)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(world);
        _influence ??= new Population.Influence(content);
        HexGlobe geo = world.Geometry;
        if (_cost.Length != geo.CellCount)
        {
            _cost = new int[geo.CellCount];
            _stamp = new int[geo.CellCount];
            _target = new int[geo.CellCount];
            _run = 0;
        }

        _run++;
        int left = 0;
        foreach (int t in Targets)
        {
            if (_target[t] != _run)
            {
                _target[t] = _run;
                left++;
            }
        }

        _queue.Clear();
        _cost[start] = 0;
        _stamp[start] = _run;
        _queue.Enqueue(start, 0);
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        while (_queue.TryDequeue(out int cur, out int cost))
        {
            if (cost > _cost[cur])
            {
                continue;
            }

            if (_target[cur] == _run)
            {
                _target[cur] = 0; // settled: its cost is final
                if (--left == 0 && Targets.Count > 0)
                {
                    break;
                }
            }

            geo.GetNeighbors(geo.FromIndex(cur), n6);
            foreach (HexCoord n in n6)
            {
                int ni = geo.Index(n);
                int enter = _influence.EnterCost(world.Map, ni);
                if (enter < 0)
                {
                    continue;
                }

                int next = cost + Math.Max(1, enter);
                if (next > limit || (_stamp[ni] == _run && _cost[ni] <= next))
                {
                    continue;
                }

                _stamp[ni] = _run;
                _cost[ni] = next;
                _queue.Enqueue(ni, next);
            }
        }
    }

    /// <summary>The road cost to <paramref name="cell"/> from the last search, or <paramref name="far"/> when it was not reached.</summary>
    public int To(int cell, int far) => cell >= 0 && cell < _stamp.Length && _stamp[cell] == _run ? _cost[cell] : far;
}

/// <summary>
/// State phase of stability (GDD 7.3; stage 4f PR 3): every world step the capital follows its settlements; in its own
/// step (at most once a year) each civilization recomputes administration, stability and loyalty, unrest follows low
/// stability, and below the revolt threshold a revolt may break out (stage 4f PR 4).
/// </summary>
public sealed class StabilitySystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly RoadCosts _roads = new();
    private readonly Random.DeterministicRandom _random = new(0);
    private readonly EffectDefinition? _unrest;

    /// <summary>Creates the system.</summary>
    public StabilitySystem(GameContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
        foreach (EffectDefinition e in content.Effects)
        {
            if (e.Key == content.Stability.UnrestEffect)
            {
                _unrest = e;
            }
        }
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.State;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        int count = state.Civilizations.Count; // a people that breaks away now steps from the next world step
        for (int k = 0; k < count; k++)
        {
            CivilizationState civ = state.Civilizations[k];
            if (!civ.IsAlive)
            {
                continue;
            }

            if (Stability.KeepCapital(_content, state, civ) is { } line)
            {
                context.Chronicle(line);
            }

            // Steps shorter than a year (a war month by month) recompute once a year, in its first month.
            if (!WorldClock.Steps(civ) || (civ.StepMonths < 12 && state.Month % 12 != 0))
            {
                continue;
            }

            int before = civ.Stability;
            Stability.Update(_content, state, civ, _roads, _unrest);
            if (Stability.UnrestLine(_content, civ, before) is { } unrest)
            {
                context.Chronicle(unrest);
            }

            Revolts.Reseed(state, civ, state.Month, _random);
            if ((Revolts.Check(_content, state, civ, _random) ?? Revolts.Drift(_content, state, civ, _random)) is { } revolt)
            {
                context.Chronicle(revolt);
            }
        }
    }
}
