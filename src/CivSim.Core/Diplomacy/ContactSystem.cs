using System;
using System.Globalization;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// Discovery of civilizations and the memory of relations (GDD 8.5; stage 4c): who has met whom, rumors of distant
/// peoples, remembered events with their fading, and the sympathy and trust that follow from them.
/// </summary>
public static class Contacts
{
    /// <summary>
    /// Records that <paramref name="observer"/> has met <paramref name="other"/> now. The first real contact (also after
    /// a rumor) sets the first opinion from the circumstances and the observer's openness. True for a first contact.
    /// </summary>
    public static bool Meet(GameContent content, GameState state, CivilizationState observer, CivId other, ContactWay way)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(observer);
        Relation r = observer.AddRelation(other);
        r.LastContactMonth = state.Month;
        if (r.HasMet)
        {
            return false;
        }

        if (r.Level == 0)
        {
            r.FirstMonth = state.Month;
        }

        r.Level = ContactLevel.Met;
        r.Way = way;
        r.MetMonth = state.Month;
        r.Opening = Opening(content.Relations, observer, way);
        r.RumorCell = -1;
        r.RumorPeople = 0;
        r.RumorFrom = -1;
        r.Recount(state.Month);
        return true;
    }

    /// <summary>First opinion, hundredths: openness from −<see cref="RelationRules.OpeningSympathy"/> to +, plus the circumstances.</summary>
    public static int Opening(RelationRules rules, CivilizationState observer, ContactWay way)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(observer);
        int openness = observer.Trait(Ai.Trait.Openness);
        int opening = (int)((long)(openness - Ai.Character.Average) * rules.OpeningSympathy / Ai.Character.Average);
        return opening + (way == ContactWay.Border ? rules.BorderOpening : 0);
    }

    /// <summary>
    /// <paramref name="attacker"/> attacked a camp of <paramref name="victim"/> (GDD 8.5: an attack on us). The victim
    /// remembers it the longer the more vengeful it is. True when this was also their first contact.
    /// </summary>
    public static bool RecordAttack(GameContent content, GameState state, CivId victim, CivId attacker)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? v = state.FindCivilization(victim);
        if (v is null || victim == attacker)
        {
            return false;
        }

        bool first = Meet(content, state, v, attacker, ContactWay.Attack);
        RelationEventDefinition def = content.Relations.Events[(int)RelationEvent.Attack];
        int years = def.Years + (int)((long)(def.VengefulYears - def.Years) * v.Trait(Ai.Trait.Vengefulness) / 1000);
        Relation r = v.Relation(attacker)!;
        r.Remember(
            new RelationMemory { Kind = RelationEvent.Attack, Sympathy = def.Sympathy, Trust = def.Trust, EndMonth = state.Month, FadeMonths = Math.Max(1, years * 12) },
            content.Relations.MaxMemories,
            state.Month);
        r.Recount(state.Month);

        // The attacker sees its victims too.
        CivilizationState? a = state.FindCivilization(attacker);
        if (a is not null)
        {
            Meet(content, state, a, victim, ContactWay.Sighted);
        }

        return first;
    }

    /// <summary>Keeps an ongoing event of <paramref name="kind"/> going for one more turn of <paramref name="months"/> months.</summary>
    public static void Sustain(RelationRules rules, Relation r, RelationEvent kind, long month, int months)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(r);
        RelationEventDefinition def = rules.Events[(int)kind];
        int k = r.FindOngoing(kind);
        RelationMemory m = k >= 0
            ? r.Memories[k]
            : new RelationMemory { Kind = kind, EndMonth = RelationMemory.Ongoing, FadeMonths = Math.Max(1, def.Years * 12) };
        m.Sympathy = Grow(m.Sympathy, def.Sympathy, def.SympathyPerYear, months);
        m.Trust = Grow(m.Trust, def.Trust, def.TrustPerYear, months);
        if (k >= 0)
        {
            r.SetMemory(k, m);
        }
        else
        {
            r.Remember(m, rules.MaxMemories, month);
        }
    }

    /// <summary>Ends the ongoing event of <paramref name="kind"/>, if any: from now on it fades.</summary>
    public static void End(Relation r, RelationEvent kind, long month)
    {
        ArgumentNullException.ThrowIfNull(r);
        int k = r.FindOngoing(kind);
        if (k >= 0)
        {
            RelationMemory m = r.Memories[k];
            m.EndMonth = month;
            r.SetMemory(k, m);
        }
    }

    // One turn of growth towards the cap (all at once when there is no yearly rate).
    private static int Grow(int now, int cap, int perYear, int months)
    {
        if (perYear == 0)
        {
            return cap;
        }

        long next = now + ((long)perYear * months / 12);
        return (int)(cap < 0 ? Math.Max(cap, next) : Math.Min(cap, next));
    }

    /// <summary>Sympathy of <paramref name="observer"/> for <paramref name="other"/>, hundredths; 0 for a civilization it never met.</summary>
    public static int Sympathy(CivilizationState observer, CivId other)
    {
        ArgumentNullException.ThrowIfNull(observer);
        Relation? r = observer.Relation(other);
        return r is { HasMet: true } ? r.Sympathy : 0;
    }
}

/// <summary>
/// Perception phase, after the map memory (GDD 11.1, phase 2; stage 4c). A civilization meets another when its
/// settlements or scouts see one of the other's settlements, or when their influence zones touch; it hears rumors of
/// peoples that civilizations it met have met. Ongoing events (border disputes) go on or end, faded memories are
/// forgotten, and sympathy and trust are recounted.
/// </summary>
public sealed class ContactSystem(GameContent content) : ITurnSystem
{
    private const ulong RumorSalt = 0xD1UL << 40;

    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly DeterministicRandom _random = new(0);
    private int[] _indexOfId = [];
    private bool[] _touch = [];
    private bool[] _dispute = [];

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Perception;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        int n = state.Civilizations.Count;
        if (n < 2)
        {
            return;
        }

        Prepare(state, n);
        Sightings(context, state);
        Borders(context, state, n);
        Disputes(state, n);
        Rumors(context, state);
        long month = state.Month;
        foreach (CivilizationState civ in state.Civilizations)
        {
            foreach (Relation r in civ.Relations)
            {
                r.Forget(month);
                r.Recount(month);
            }
        }
    }

    private void Prepare(GameState state, int n)
    {
        int maxId = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            maxId = Math.Max(maxId, c.Id.Value);
        }

        if (_indexOfId.Length <= maxId)
        {
            _indexOfId = new int[maxId + 1];
        }

        Array.Fill(_indexOfId, -1);
        for (int k = 0; k < n; k++)
        {
            _indexOfId[state.Civilizations[k].Id.Value] = k;
        }

        if (_touch.Length != n * n)
        {
            _touch = new bool[n * n];
            _dispute = new bool[n * n];
        }
    }

    // Settlements and scouts saw a foreign settlement this year (GDD 8.5: the scouts see its camp or settlement).
    private void Sightings(TurnContext context, GameState state)
    {
        WorldState earth = state.Worlds[0];
        HexGlobe geo = earth.Geometry;
        int stamp = (int)(state.Month / 12) + 1;
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.World != earth.Id)
            {
                continue;
            }

            int cell = geo.Index(s.Cell);
            foreach (CivilizationState civ in state.Civilizations)
            {
                if (!civ.IsAlive || civ.Id == s.Owner || cell >= civ.SeenYear.Length || civ.SeenYear[cell] != stamp)
                {
                    continue;
                }

                if (Contacts.Meet(_content, state, civ, s.Owner, ContactWay.Sighted))
                {
                    context.Chronicle(Sighted(state, earth, civ, s));
                }
            }
        }
    }

    private static string Sighted(GameState state, WorldState earth, CivilizationState civ, SettlementState seen)
    {
        SettlementState? near = null;
        int best = int.MaxValue;
        foreach (SettlementState own in state.Settlements)
        {
            if (own.Owner == civ.Id && own.IsActive && own.World == earth.Id)
            {
                int d = earth.Geometry.Distance(own.Cell, seen.Cell);
                if (d < best)
                {
                    best = d;
                    near = own;
                }
            }
        }

        string other = state.FindCivilization(seen.Owner)!.Name;
        string where = near is null
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" {best} cells {Away(earth, near.Cell, seen.Cell)} of {near.Name}");
        string whose = seen.Name.StartsWith(other, StringComparison.Ordinal) ? string.Empty : $" of {other}";
        return $"First contact: scouts of {civ.Name} saw {seen.Name}{whose}{where}.";
    }

    // Influence zones touch (GDD 8.5: direct contact). Each pair of touching land cells of two civilizations counts once.
    private void Borders(TurnContext context, GameState state, int n)
    {
        Array.Clear(_touch, 0, n * n);
        WorldState earth = state.Worlds[0];
        HexGlobe geo = earth.Geometry;
        ReadOnlySpan<short> owner = earth.Map.Owner.Values;
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        int cells = geo.CellCount;
        for (int i = 0; i < cells; i++)
        {
            short a = owner[i];
            if (a < 0)
            {
                continue;
            }

            geo.GetNeighbors(geo.FromIndex(i), n6);
            foreach (HexCoord c in n6)
            {
                short b = owner[geo.Index(c)];
                if (b > a)
                {
                    int ia = _indexOfId[a];
                    int ib = _indexOfId[b];
                    if (ia >= 0 && ib >= 0)
                    {
                        _touch[(ia * n) + ib] = true;
                    }
                }
            }
        }

        for (int ia = 0; ia < n; ia++)
        {
            for (int ib = ia + 1; ib < n; ib++)
            {
                if (!_touch[(ia * n) + ib])
                {
                    continue;
                }

                CivilizationState a = state.Civilizations[ia];
                CivilizationState b = state.Civilizations[ib];
                if (!a.IsAlive || !b.IsAlive)
                {
                    continue;
                }

                bool firstA = Contacts.Meet(_content, state, a, b.Id, ContactWay.Border);
                bool firstB = Contacts.Meet(_content, state, b, a.Id, ContactWay.Border);
                if (firstA && firstB)
                {
                    context.Chronicle($"First contact: the lands of {a.Name} and {b.Name} now touch.");
                }
                else if (firstA || firstB)
                {
                    (CivilizationState who, CivilizationState whom) = firstA ? (a, b) : (b, a);
                    context.Chronicle($"First contact: {who.Name} met {whom.Name} where their lands touch.");
                }
            }
        }
    }

    // A civilization lives in a settlement another still claims (GDD 8.5: border dispute, while it lasts).
    private void Disputes(GameState state, int n)
    {
        Array.Clear(_dispute, 0, n * n);
        long month = state.Month;
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || !s.HasClaim(month) || s.ClaimCiv == s.Owner.Value || s.ClaimCiv < 0 || s.ClaimCiv >= _indexOfId.Length
                || s.Owner.Value < 0 || s.Owner.Value >= _indexOfId.Length)
            {
                continue;
            }

            int claimer = _indexOfId[s.ClaimCiv];
            int holder = _indexOfId[s.Owner.Value];
            if (claimer >= 0 && holder >= 0)
            {
                _dispute[(claimer * n) + holder] = true;
            }
        }

        int months = state.MonthsPerTurn(_content);
        for (int ia = 0; ia < n; ia++)
        {
            CivilizationState a = state.Civilizations[ia];
            foreach (Relation r in a.Relations)
            {
                int ib = r.Other.Value < _indexOfId.Length ? _indexOfId[r.Other.Value] : -1;
                bool disputed = a.IsAlive && r.HasMet && ib >= 0 && state.Civilizations[ib].IsAlive && _dispute[(ia * n) + ib];
                if (disputed)
                {
                    Contacts.Sustain(_content.Relations, r, RelationEvent.BorderDispute, month, months);
                }
                else
                {
                    Contacts.End(r, RelationEvent.BorderDispute, month);
                }
            }
        }
    }

    // Rumors (GDD 8.5): a civilization may hear of a people that one it has met has met. The news is rough: the place
    // is off by up to a share of the distance and the strength exaggerated or played down.
    private void Rumors(TurnContext context, GameState state)
    {
        RelationRules rules = _content.Relations;
        int years = Math.Max(1, state.MonthsPerTurn(_content) / 12);
        int chance = (int)Math.Min(1000L, (long)rules.RumorChancePercentPerCentury * 10 * years / 100);
        if (chance <= 0)
        {
            return;
        }

        WorldState earth = state.Worlds[0];
        foreach (CivilizationState a in state.Civilizations)
        {
            if (!a.IsAlive)
            {
                continue;
            }

            bool seeded = false;
            bool heardOne = false;
            for (int i = 0; i < a.Relations.Count; i++)
            {
                Relation via = a.Relations[i];
                CivilizationState? b = state.FindCivilization(via.Other);
                if (!via.HasMet || b is null || !b.IsAlive)
                {
                    continue;
                }

                foreach (Relation heard in b.Relations)
                {
                    CivilizationState? c = state.FindCivilization(heard.Other);
                    if (!heard.HasMet || c is null || !c.IsAlive || c.Id == a.Id || a.Relation(c.Id) is not null)
                    {
                        continue;
                    }

                    if (!seeded)
                    {
                        _random.Reseed(state.Random.DeriveSeed(RandomStreamId.Ai, RumorSalt | (uint)a.Id.Value, state.Turn));
                        seeded = true;
                    }

                    if (_random.NextInt(1000) >= chance)
                    {
                        continue;
                    }

                    // One rumor per civilization and turn (hearing changes the list being read).
                    string? line = Hear(state, earth, rules, a, b, c);
                    if (line is not null)
                    {
                        context.Chronicle(line);
                        heardOne = true;
                        break;
                    }
                }

                if (heardOne)
                {
                    break;
                }
            }
        }
    }

    private string? Hear(GameState state, WorldState earth, RelationRules rules, CivilizationState a, CivilizationState b, CivilizationState c)
    {
        SettlementState? home = Largest(state, earth, a.Id);
        SettlementState? them = Largest(state, earth, c.Id);
        if (home is null || them is null)
        {
            return null;
        }

        HexGlobe geo = earth.Geometry;
        int distance = geo.Distance(home.Cell, them.Cell);
        int off = distance * rules.RumorErrorPercent / 100;
        var direction = (HexDirection)_random.NextInt(6);
        int steps = off == 0 ? 0 : _random.NextInt(off + 1);
        HexCoord said = them.Cell;
        for (int k = 0; k < steps; k++)
        {
            said = geo.Neighbor(said, direction);
        }

        int people = 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.Owner == c.Id && s.IsActive)
            {
                people += s.TotalPopulation();
            }
        }

        int error = rules.RumorErrorPercent;
        int percent = 100 - error + _random.NextInt((2 * error) + 1);
        Relation r = a.AddRelation(c.Id);
        r.Level = ContactLevel.Rumor;
        r.Way = ContactWay.Rumor;
        r.FirstMonth = state.Month;
        r.RumorCell = geo.Index(said);
        r.RumorPeople = (int)((long)people * percent / 100);
        r.RumorFrom = b.Id.Value;
        string away = Away(earth, home.Cell, said);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{a.Name} heard from {b.Name} of a people called {c.Name}, about {Round(r.RumorPeople)} strong, somewhere {(away == "near" ? "nearby" : "to the " + away)}.");
    }

    private static SettlementState? Largest(GameState state, WorldState earth, CivId civ)
    {
        SettlementState? best = null;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.Owner == civ && s.IsActive && s.World == earth.Id && (best is null || s.TotalPopulation() > best.TotalPopulation()))
            {
                best = s;
            }
        }

        return best;
    }

    private static string Away(WorldState world, HexCoord from, HexCoord to)
    {
        string d = SettlementRules.Direction(world, from, to);
        return d.Length == 0 ? "near" : d;
    }

    // People as a rumor tells them: two significant digits.
    private static int Round(int people)
    {
        int unit = 1;
        while (people / unit >= 100)
        {
            unit *= 10;
        }

        return Math.Max(10, people / unit * unit);
    }
}
