using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Economy;
using CivSim.Core.Map;
using CivSim.Core.Perception;
using CivSim.Core.State;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>
/// Builds what the observer's developer layers draw (stage 5): journeys, fields, event effects, relations and the
/// fog of a watched civilization. Runs on the simulation thread and only reads the state.
/// </summary>
public sealed class ObserverViewBuilder(GameContent content)
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly FarmingSystem _farming = new(content);
    private readonly List<long> _keys = [];
    private HexCoord[] _area = [];
    private int[] _worked = [];

    /// <summary>Everything except the fog.</summary>
    public ObserverDetailsMessage Build(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new ObserverDetailsMessage(state.Turn, Journeys(state), Fields(state), Events(state), Relations(state));
    }

    /// <summary>
    /// What <paramref name="civ"/> knows of Earth, one byte per cell: unknown, remembered, or seen in the last turn
    /// (cells stamped with the newest year in the civilization's memory).
    /// </summary>
    public FogMessage? Fog(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? c = state.FindCivilization(civ);
        if (c is null)
        {
            return null;
        }

        WorldState earth = state.Worlds[0];
        var cells = new byte[earth.Geometry.CellCount];
        if (c.SeenYear.Length == cells.Length)
        {
            bool[] known = MapMemory.KnownCells(_content, state, c);
            int newest = 0;
            foreach (int year in c.SeenYear)
            {
                newest = Math.Max(newest, year);
            }

            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = !known[i] ? MapChunkMessage.Unknown
                    : c.SeenYear[i] == newest ? MapChunkMessage.Visible
                    : MapChunkMessage.Remembered;
            }
        }

        return new FogMessage(civ.Value, earth.Id.Value, state.Turn, cells);
    }

    /// <summary>Era, people, settlements and technologies of every civilization (the observer's civilization table).</summary>
    public List<CivilizationView> Civilizations(GameState state, IReadOnlyList<CivilizationView> plain)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plain);
        var people = new Dictionary<int, (long People, int Settlements)>();
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive)
            {
                (long p, int n) = people.GetValueOrDefault(s.Owner.Value);
                people[s.Owner.Value] = (p + s.TotalPopulation(), n + 1);
            }
        }

        var list = new List<CivilizationView>(plain.Count);
        foreach (CivilizationView v in plain)
        {
            CivilizationState? c = state.FindCivilization(new CivId(v.Id));
            (long p, int n) = people.GetValueOrDefault(v.Id);
            list.Add(c is null ? v : v with
            {
                Era = c.Era >= 0 && c.Era < _content.Eras.Count ? _content.Eras[c.Era].Name : string.Empty,
                People = p,
                Settlements = n,
                Technologies = c.KnownCount,
            });
        }

        return list;
    }

    /// <summary>Everything about one civilization for the followed-civilization panel, or null when it is unknown.</summary>
    public CivilizationDetailsMessage? Details(GameState state, CivId id)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? c = state.FindCivilization(id);
        if (c is null)
        {
            return null;
        }

        var techs = new List<string>();
        for (int t = 0; t < c.KnownTechnologies.Length && t < _content.Technologies.Count; t++)
        {
            if (c.KnownTechnologies[t])
            {
                techs.Add(_content.Technologies[t].Name);
            }
        }

        string researching = string.Empty;
        int percent = 0;
        if (c.Researching >= 0 && c.Researching < _content.Technologies.Count)
        {
            TechnologyDefinition tech = _content.Technologies[c.Researching];
            researching = tech.Name;
            percent = tech.Cost.Hundredths <= 0 ? 0 : (int)Math.Clamp(c.ResearchProgress * 100 / tech.Cost.Hundredths, 0, 100);
        }

        var people = new long[_content.Strata.Count];
        var levels = new int[_content.SettlementLevels.Count];
        var stock = new long[_content.Goods.Count];
        long gathered = 0;
        long needed = 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.Owner != id || !s.IsActive)
            {
                continue;
            }

            ReadOnlySpan<int> pop = s.PopulationView;
            for (int k = 0; k < pop.Length && k < people.Length; k++)
            {
                people[k] += pop[k];
            }

            if (s.Level >= 0 && s.Level < levels.Length)
            {
                levels[s.Level]++;
            }

            ReadOnlySpan<Core.Numerics.Amount> goods = s.StockView;
            for (int g = 0; g < goods.Length && g < stock.Length; g++)
            {
                stock[g] += goods[g].Hundredths;
            }

            gathered += s.FoodGathered;
            needed += s.FoodNeeded;
        }

        var relations = new List<RelationDetail>(c.Relations.Count);
        foreach (Relation r in c.Relations)
        {
            var memories = new List<(int Strength, string Text)>();
            foreach (RelationMemory m in r.Memories)
            {
                int strength = m.Strength(state.Month);
                if (strength > 0)
                {
                    memories.Add((strength, $"{m.Kind} {strength / 10}%"));
                }
            }

            memories.Sort((a, b) => b.Strength.CompareTo(a.Strength));
            relations.Add(new RelationDetail(r.Other.Value, (int)r.Level, r.Sympathy, r.Trust, TreatyFlags(state, id, r.Other), memories.ConvertAll(m => m.Text)));
        }

        string era = c.Era >= 0 && c.Era < _content.Eras.Count ? _content.Eras[c.Era].Name : string.Empty;
        return new CivilizationDetailsMessage(
            id.Value,
            state.Turn,
            c.Name,
            c.IsAlive,
            era,
            techs,
            researching,
            percent,
            c.LastScience,
            Enum.GetNames<Core.Ai.Trait>(),
            [.. c.Traits],
            Enum.GetNames<Core.Ai.Goal>(),
            [.. c.GoalWeights],
            Names(_content.Strata, s => s.Name),
            people,
            Names(_content.SettlementLevels, l => l.Name),
            levels,
            gathered,
            needed,
            Names(_content.Goods, g => g.Name),
            stock,
            relations);
    }

    private static List<string> Names<T>(IReadOnlyList<T> items, Func<T, string> name)
    {
        var list = new List<string>(items.Count);
        foreach (T item in items)
        {
            list.Add(name(item));
        }

        return list;
    }

    private static int TreatyFlags(GameState state, CivId a, CivId b)
    {
        TreatyBook t = state.Treaties;
        int flags = 0;
        flags |= t.Has(a, b, TreatyKind.War) ? RelationView.War : 0;
        flags |= t.Has(a, b, TreatyKind.Peace) ? RelationView.Peace : 0;
        flags |= t.Has(a, b, TreatyKind.Trade) ? RelationView.Trade : 0;
        flags |= t.Has(a, b, TreatyKind.NonAggression) ? RelationView.NonAggression : 0;
        flags |= t.Has(a, b, TreatyKind.DefensiveAlliance) ? RelationView.Alliance : 0;
        return flags;
    }

    private static List<JourneyView> Journeys(GameState state)
    {
        var list = new List<JourneyView>();
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.JourneyTarget == SettlementState.NoJourney)
            {
                continue;
            }

            WorldState? world = World(state, s.World);
            if (world is null)
            {
                continue;
            }

            HexCoord target = world.Geometry.FromIndex(s.JourneyTarget);
            list.Add(new JourneyView(s.Id.Value, s.Owner.Value, s.World.Value, s.Cell.Col, s.Cell.Row, target.Col, target.Row, s.TotalPopulation()));
        }

        return list;
    }

    // Which cells the villages farm. The farming system keeps this only for the turn, so it is worked out again the
    // same way (best grain per farmer first among the settlement's claimed cells); effects of events are left out.
    private List<FieldView> Fields(GameState state)
    {
        var list = new List<FieldView>();
        int perCell = Math.Max(1, _content.Population.FarmersPerCell);
        foreach (WorldState world in state.Worlds)
        {
            if (_worked.Length != world.Geometry.CellCount)
            {
                _worked = new int[world.Geometry.CellCount];
            }

            Array.Clear(_worked);
            int radius = Math.Min(_content.Population.InfluenceMaxRange, world.Geometry.MaxUniqueRadius);
            if (_area.Length < HexGlobe.RangeSize(radius))
            {
                _area = new HexCoord[HexGlobe.RangeSize(radius)];
            }

            foreach (SettlementState s in state.Settlements)
            {
                if (s.World != world.Id || !s.IsActive || s.Farmers <= 0)
                {
                    continue;
                }

                int count = world.Geometry.Range(s.Cell, radius, _area);
                int claim = s.Id.Value + 1;
                _keys.Clear();
                for (int k = 0; k < count; k++)
                {
                    int i = world.Geometry.Index(_area[k]);
                    long perFarmer = world.Map.Claim[i] == claim ? _farming.GrainPerFarmer(world.Map, i) : 0;
                    if (perFarmer > 0)
                    {
                        _keys.Add((-perFarmer << 24) | (uint)i);
                    }
                }

                _keys.Sort();
                int left = s.Farmers;
                foreach (long key in _keys)
                {
                    if (left <= 0)
                    {
                        break;
                    }

                    int cell = (int)(key & 0xFF_FFFF);
                    int farmers = Math.Min(left, perCell - _worked[cell]);
                    if (farmers <= 0)
                    {
                        continue;
                    }

                    _worked[cell] += farmers;
                    left -= farmers;
                    HexCoord c = world.Geometry.FromIndex(cell);
                    list.Add(new FieldView(world.Id.Value, c.Col, c.Row, s.Owner.Value, Math.Min(100, _worked[cell] * 100 / perCell)));
                }
            }
        }

        return list;
    }

    private List<CellEventView> Events(GameState state)
    {
        var list = new List<CellEventView>();
        foreach (EffectInstance e in state.Effects.All)
        {
            if (e.EndMonth == EffectInstance.Permanent || e.DefinitionId < 0 || e.DefinitionId >= _content.Effects.Count)
            {
                continue;
            }

            string key = _content.Effects[e.DefinitionId].Key;
            switch (e.Target)
            {
                case EffectTarget.Cell when World(state, e.World) is { } world && e.TargetId >= 0 && e.TargetId < world.Geometry.CellCount:
                    HexCoord c = world.Geometry.FromIndex(e.TargetId);
                    list.Add(new CellEventView(e.World.Value, c.Col, c.Row, key, e.Strength, e.EndMonth));
                    break;
                case EffectTarget.Settlement when state.FindSettlement(new SettlementId(e.TargetId)) is { } s:
                    list.Add(new CellEventView(s.World.Value, s.Cell.Col, s.Cell.Row, key, e.Strength, e.EndMonth));
                    break;
            }
        }

        return list;
    }

    private static List<RelationView> Relations(GameState state)
    {
        var list = new List<RelationView>();
        IReadOnlyList<CivilizationState> civs = state.Civilizations;
        for (int x = 0; x < civs.Count; x++)
        {
            for (int y = x + 1; y < civs.Count; y++)
            {
                CivilizationState a = civs[x];
                CivilizationState b = civs[y];
                Relation? ab = a.Relation(b.Id);
                Relation? ba = b.Relation(a.Id);
                if (ab is null && ba is null)
                {
                    continue;
                }

                int contact = Math.Max((int)(ab?.Level ?? 0), (int)(ba?.Level ?? 0));
                int flags = TreatyFlags(state, a.Id, b.Id);
                list.Add(new RelationView(a.Id.Value, b.Id.Value, contact, ab?.Sympathy ?? 0, ba?.Sympathy ?? 0, flags));
            }
        }

        return list;
    }

    private static WorldState? World(GameState state, WorldId id)
    {
        foreach (WorldState w in state.Worlds)
        {
            if (w.Id == id)
            {
                return w;
            }
        }

        return null;
    }
}
