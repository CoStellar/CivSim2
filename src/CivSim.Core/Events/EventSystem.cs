using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Events;

/// <summary>
/// Random events (GDD 10.2, stage 3h): drought, wildfire, flood and epidemic from <c>data/events.json</c>. Their chance
/// follows the state of the world (dry land, forests, rivers, crowded villages), technologies soften them, and they act
/// only through effects (GDD 10.1), fertility and the chronicle. Runs in the world phase, after old effects expire, on
/// the events random stream: settlements in list order, events in data order, so a run is deterministic.
/// </summary>
/// <param name="content">Game content.</param>
/// <param name="events">Events to roll instead of <see cref="GameContent.Events"/> (tests).</param>
public sealed class EventSystem(GameContent content, IReadOnlyList<EventDefinition>? events = null) : ITurnSystem
{
    private const long Million = 1_000_000;

    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly IReadOnlyList<EventDefinition> _events = events ?? content.Events;
    private readonly List<(int Settlement, int Event)> _struck = [];
    private readonly Dictionary<string, string> _groups = new(StringComparer.Ordinal);

    // The chronicle group of an event: several strikes in a turn are summed up in one line (stage 5b).
    private string GroupOf(EventDefinition ev)
    {
        if (!_groups.TryGetValue(ev.Key, out string? group))
        {
            group = ev.Name + " in {0} settlements";
            _groups[ev.Key] = group;
        }

        return group;
    }
    private HexCoord[] _area = [];
    private long[] _civPeople = []; // people of each civilization (by id) this turn, for CivilizationPeopleAtLeast

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.World;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_events.Count == 0)
        {
            return;
        }

        GameState state = context.State;
        DeterministicRandom rng = state.Random[RandomStreamId.Events];
        IReadOnlyList<SettlementState> all = state.Settlements;
        _struck.Clear();
        CountCivilizationPeople(state);
        for (int si = 0; si < all.Count; si++)
        {
            SettlementState s = all[si];
            // Events are drawn for the civilization's own step, over its whole length (ADR 0018).
            int months = s.IsActive ? WorldClock.MonthsOf(state, s.Owner) : 0;
            if (months == 0)
            {
                continue;
            }

            WorldState world = WorldOf(state, s.World);
            for (int e = 0; e < _events.Count; e++)
            {
                EventDefinition ev = _events[e];
                long chance = TurnChanceMillionths(Chance(world, s, ev), months);
                if (chance > 0 && rng.NextInt((int)Million) < chance)
                {
                    Strike(context, world, s, ev, months);
                    _struck.Add((si, e));
                }
            }
        }

        Spread(context, rng);
    }

    /// <summary>Yearly chance of <paramref name="ev"/> for settlement <paramref name="s"/>, millionths (0 when ruled out).</summary>
    public long YearChanceMillionths(GameState state, WorldState world, SettlementState s, EventDefinition ev)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(ev);
        CountCivilizationPeople(state);
        return Chance(world, s, ev);
    }

    // Yearly chance with the civilization head counts already taken this turn.
    private long Chance(WorldState world, SettlementState s, EventDefinition ev)
    {
        long chance = ev.TimesPer10000Years * 100L; // per 10 000 years -> millionths per year
        foreach (EventCondition c in ev.Conditions)
        {
            if (chance == 0)
            {
                return 0;
            }

            if (Holds(world, s, c))
            {
                chance = chance * c.Percent / 100;
            }
            else if (c.Required)
            {
                return 0;
            }
        }

        return Math.Min(chance, Million);
    }

    /// <summary>Chance of at least one event in a turn of <paramref name="months"/> from a yearly chance, millionths.</summary>
    public static long TurnChanceMillionths(long yearMillionths, int months)
    {
        if (yearMillionths <= 0 || months <= 0)
        {
            return 0;
        }

        long keep = Million;
        for (int y = 0; y < months / 12; y++)
        {
            keep = keep * (Million - yearMillionths) / Million;
        }

        keep = keep * (Million - (yearMillionths * (months % 12) / 12)) / Million;
        return Million - keep;
    }

    /// <summary>
    /// Strength of an event effect in a turn: softened by technologies the owner knows, and scaled by the share of the
    /// turn it lasts when the turn is longer. Zero means the effect is too weak to lay.
    /// </summary>
    public static int Strength(GameContent content, CivilizationState? owner, EventDefinition ev, EventEffect effect, int months)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(effect);
        int soften = 0;
        foreach (EventSoftening x in ev.SoftenedBy)
        {
            soften += owner is not null && owner.Knows(x.Technology) ? x.Percent : 0;
        }

        long strength = (long)effect.Strength * Math.Max(0, 100 - soften) / 100;
        long lastMonths = effect.Years * 12L;
        if (months > lastMonths)
        {
            strength = strength * lastMonths / months;
        }

        return (int)strength;
    }

    private static WorldState WorldOf(GameState state, WorldId id)
    {
        foreach (WorldState w in state.Worlds)
        {
            if (w.Id == id)
            {
                return w;
            }
        }

        throw new InvalidOperationException($"No world {id.Value}.");
    }

    private bool Holds(WorldState world, SettlementState s, EventCondition c)
    {
        WorldMap map = world.Map;
        HexGlobe geo = world.Geometry;
        int cell = geo.Index(s.Cell);
        switch (c.Kind)
        {
            case EventConditionKind.HumidityBelow:
                return map.Humidity[cell] < c.Value;
            case EventConditionKind.HumidityAbove:
                return map.Humidity[cell] > c.Value;
            case EventConditionKind.PeopleAtLeast:
                return s.TotalPopulation() >= c.Value;
            case EventConditionKind.Settled:
                return !_content.SettlementLevels[s.Level].Mobile;
            case EventConditionKind.CivilizationPeopleAtLeast:
                return CivilizationPeople(s.Owner) >= c.Value;
            case EventConditionKind.River:
                for (int d = 0; d < 6; d++)
                {
                    if (map.River[geo.EdgeIndex(geo.Edge(s.Cell, (HexDirection)d))] != 0)
                    {
                        return true;
                    }
                }

                return false;
            case EventConditionKind.BiomeNear:
                int n = Area(geo, s.Cell, c.Value);
                for (int k = 0; k < n; k++)
                {
                    int biome = map.Biome[geo.Index(_area[k])];
                    foreach (int b in c.Biomes)
                    {
                        if (b == biome)
                        {
                            return true;
                        }
                    }
                }

                return false;
            default:
                return false;
        }
    }

    // Head count of every civilization, from its active settlements; the array is reused between turns.
    private void CountCivilizationPeople(GameState state)
    {
        int size = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            size = Math.Max(size, c.Id.Value + 1);
        }

        if (_civPeople.Length < size)
        {
            _civPeople = new long[size];
        }

        Array.Clear(_civPeople);
        foreach (SettlementState s in state.Settlements)
        {
            int owner = s.Owner.Value;
            if (s.IsActive && owner >= 0 && owner < _civPeople.Length)
            {
                _civPeople[owner] += s.TotalPopulation();
            }
        }
    }

    private long CivilizationPeople(CivId civ) => civ.Value >= 0 && civ.Value < _civPeople.Length ? _civPeople[civ.Value] : 0;

    // A spreading event jumps to small settlements of a struck network (disease does not ask how big a village is),
    // but never into a civilization below a required size (GDD 10.2: epidemics strike only large civilizations).
    private bool CanSpreadTo(SettlementState target, EventDefinition ev)
    {
        foreach (EventCondition c in ev.Conditions)
        {
            if (c.Required && c.Kind == EventConditionKind.CivilizationPeopleAtLeast && CivilizationPeople(target.Owner) < c.Value)
            {
                return false;
            }
        }

        return true;
    }

    private int Area(HexGlobe geo, HexCoord center, int radius)
    {
        radius = Math.Min(radius, geo.MaxUniqueRadius);
        if (_area.Length < HexGlobe.RangeSize(radius))
        {
            _area = new HexCoord[HexGlobe.RangeSize(radius)];
        }

        return geo.Range(center, radius, _area);
    }

    private void Strike(TurnContext context, WorldState world, SettlementState s, EventDefinition ev, int months, bool chronicle = true)
    {
        GameState state = context.State;
        CivilizationState? owner = state.FindCivilization(s.Owner);
        HexGlobe geo = world.Geometry;
        WorldMap map = world.Map;
        foreach (EventEffect e in ev.Effects)
        {
            int strength = Strength(_content, owner, ev, e, months);
            if (strength == 0)
            {
                continue;
            }

            EffectDefinition def = _content.Effects[e.Effect];
            long end = state.Month + Math.Max(e.Years * 12L, months);
            switch (def.Target)
            {
                case EffectTarget.Settlement:
                    state.Effects.Add(def, s.World, s.Id.Value, strength, end);
                    break;
                case EffectTarget.Civilization:
                    state.Effects.Add(def, WorldId.Earth, s.Owner.Value, strength, end);
                    break;
                case EffectTarget.Cell:
                    int n = Area(geo, s.Cell, e.RadiusCells);
                    for (int k = 0; k < n; k++)
                    {
                        int i = geo.Index(_area[k]);
                        if (!_content.Biomes[map.Biome[i]].IsWater)
                        {
                            state.Effects.Add(def, s.World, i, strength, end);
                        }
                    }

                    break;
            }
        }

        if (ev.FertilityGain > 0)
        {
            int n = Area(geo, s.Cell, ev.FertilityRadius);
            for (int k = 0; k < n; k++)
            {
                int i = geo.Index(_area[k]);
                if (!_content.Biomes[map.Biome[i]].IsWater)
                {
                    map.Fertility.Set(i, (byte)Math.Min(100, map.Fertility[i] + ev.FertilityGain));
                }
            }
        }

        if (chronicle)
        {
            context.Chronicle(
                ev.Chronicle
                    .Replace("{settlement}", s.Name, StringComparison.Ordinal)
                    .Replace("{civilization}", owner?.Name ?? "no one", StringComparison.Ordinal),
                GroupOf(ev),
                owner?.Id.Value ?? -1);
        }
    }

    // Events that spread (an epidemic) may strike the other settlements of the struck one's trade network, once a turn.
    private void Spread(TurnContext context, DeterministicRandom rng)
    {
        bool any = false;
        foreach ((int _, int e) in _struck)
        {
            any |= _events[e].SpreadPercent > 0;
        }

        if (!any)
        {
            return;
        }

        GameState state = context.State;
        List<List<int>> networks = TradeNetworks.Build(_content, state);
        var networkOf = new int[state.Settlements.Count];
        Array.Fill(networkOf, -1);
        for (int n = 0; n < networks.Count; n++)
        {
            foreach (int m in networks[n])
            {
                networkOf[m] = n;
            }
        }

        int count = _struck.Count;
        for (int x = 0; x < count; x++)
        {
            (int source, int e) = _struck[x];
            EventDefinition ev = _events[e];
            if (ev.SpreadPercent == 0 || networkOf[source] < 0)
            {
                continue;
            }

            int months = WorldClock.MonthsOf(state, state.Settlements[source]);
            foreach (int m in networks[networkOf[source]])
            {
                if (_struck.Contains((m, e)) || rng.NextInt(100) >= ev.SpreadPercent)
                {
                    continue;
                }

                SettlementState target = state.Settlements[m];
                if (!CanSpreadTo(target, ev))
                {
                    continue;
                }

                Strike(context, WorldOf(state, target.World), target, ev, months, chronicle: false);
                _struck.Add((m, e));
                context.Chronicle($"The {ev.Name.ToLowerInvariant()} spread from {state.Settlements[source].Name} to {target.Name}.", GroupOf(ev), target.Owner.Value);
            }

            AlongTradeRoutes(context, networks, networkOf, source, e, months, rng);
        }
    }

    // GDD 9.2: trade carries disease. A trade agreement whose goods flowed last turn links the gateways of the two
    // partners; when the struck settlement's network holds one gateway, the other may be struck too.
    private void AlongTradeRoutes(TurnContext context, List<List<int>> networks, int[] networkOf, int source, int e, int months, DeterministicRandom rng)
    {
        GameState state = context.State;
        EventDefinition ev = _events[e];
        long recent = state.Month - (2L * months);
        foreach (Diplomacy.Treaty t in state.Treaties.All)
        {
            if (t.Kind != Diplomacy.TreatyKind.Trade || t.LastFlowMonth < recent || t.GatewayA < 0 || t.GatewayB < 0)
            {
                continue;
            }

            int ga = IndexOfSettlement(state, t.GatewayA);
            int gb = IndexOfSettlement(state, t.GatewayB);
            if (ga < 0 || gb < 0)
            {
                continue;
            }

            int other = networkOf[ga] >= 0 && networkOf[ga] == networkOf[source] ? gb
                : networkOf[gb] >= 0 && networkOf[gb] == networkOf[source] ? ga
                : -1;
            if (other < 0 || !state.Settlements[other].IsActive || _struck.Contains((other, e)) || rng.NextInt(100) >= ev.SpreadPercent)
            {
                continue;
            }

            SettlementState target = state.Settlements[other];
            if (!CanSpreadTo(target, ev))
            {
                continue;
            }

            Strike(context, WorldOf(state, target.World), target, ev, months, chronicle: false);
            _struck.Add((other, e));
            string partner = state.FindCivilization(target.Owner)?.Name ?? "no one";
            context.Chronicle($"Merchants carried the {ev.Name.ToLowerInvariant()} from {state.Settlements[source].Name} to {target.Name} ({partner}).", GroupOf(ev), target.Owner.Value);
        }
    }

    private static int IndexOfSettlement(GameState state, int id)
    {
        IReadOnlyList<SettlementState> all = state.Settlements;
        int lo = 0;
        int hi = all.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int v = all[mid].Id.Value;
            if (v == id)
            {
                return mid;
            }

            if (v < id)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }
}
