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
    private HexCoord[] _area = [];

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
        int months = state.MonthsPerTurn(_content);
        DeterministicRandom rng = state.Random[RandomStreamId.Events];
        IReadOnlyList<SettlementState> all = state.Settlements;
        _struck.Clear();
        for (int si = 0; si < all.Count; si++)
        {
            SettlementState s = all[si];
            if (!s.IsActive)
            {
                continue;
            }

            WorldState world = WorldOf(state, s.World);
            for (int e = 0; e < _events.Count; e++)
            {
                EventDefinition ev = _events[e];
                long chance = TurnChanceMillionths(YearChanceMillionths(state, world, s, ev), months);
                if (chance > 0 && rng.NextInt((int)Million) < chance)
                {
                    Strike(context, world, s, ev, months);
                    _struck.Add((si, e));
                }
            }
        }

        Spread(context, months, rng);
    }

    /// <summary>Yearly chance of <paramref name="ev"/> for settlement <paramref name="s"/>, millionths (0 when ruled out).</summary>
    public long YearChanceMillionths(GameState state, WorldState world, SettlementState s, EventDefinition ev)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(ev);
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

    private int Area(HexGlobe geo, HexCoord center, int radius)
    {
        radius = Math.Min(radius, geo.MaxUniqueRadius);
        if (_area.Length < HexGlobe.RangeSize(radius))
        {
            _area = new HexCoord[HexGlobe.RangeSize(radius)];
        }

        return geo.Range(center, radius, _area);
    }

    private void Strike(TurnContext context, WorldState world, SettlementState s, EventDefinition ev, int months)
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

        context.Chronicle(ev.Chronicle
            .Replace("{settlement}", s.Name, StringComparison.Ordinal)
            .Replace("{civilization}", owner?.Name ?? "no one", StringComparison.Ordinal));
    }

    // Events that spread (an epidemic) may strike the other settlements of the struck one's trade network, once a turn.
    private void Spread(TurnContext context, int months, DeterministicRandom rng)
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

            foreach (int m in networks[networkOf[source]])
            {
                if (_struck.Contains((m, e)) || rng.NextInt(100) >= ev.SpreadPercent)
                {
                    continue;
                }

                SettlementState target = state.Settlements[m];
                Strike(context, WorldOf(state, target.World), target, ev, months);
                _struck.Add((m, e));
            }
        }
    }
}
