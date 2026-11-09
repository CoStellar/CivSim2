using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Diplomacy;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Governance;

/// <summary>
/// Revolts (GDD 7.3; stage 4f PR 4): below a stability threshold a revolt may break out. When distant settlements have
/// lost their loyalty, the largest group of them breaks away as a new civilization with a daughter language, another
/// government and a character of its own (decision of Patryk, 2026-11-07); otherwise the revolt is a coup: a new leader
/// and another government. Every draw comes from a generator derived from the seed, the leaders stream, the civilization
/// and the month, so no shared stream moves.
/// </summary>
public static class Revolts
{
    // Derived generators: entity = civilization + this offset (leaders use 1 << 20, the character the bare id).
    private const ulong EntityOffset = 2UL << 20;

    /// <summary>The generator for revolts of <paramref name="civ"/> in the step starting at <paramref name="month"/>.</summary>
    public static void Reseed(GameState state, CivilizationState civ, long month, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(random);
        random.Reseed(state.Random.DeriveSeed(RandomStreamId.Leaders, (ulong)civ.Id.Value + EntityOffset, month));
    }

    /// <summary>
    /// The chance, per mille, that a revolt breaks out in the step of <paramref name="civ"/> (at least a year): 0 above
    /// the threshold and in the calm after a revolt.
    /// </summary>
    public static int ChancePerMille(GameContent content, GameState state, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        StabilityRules rules = content.Stability;
        if (civ.Stability >= rules.RevoltBelow || state.ActionMonth < civ.RevoltCalmUntilMonth)
        {
            return 0;
        }

        return PerStep(rules.RevoltPerMillePerYear, civ);
    }

    /// <summary>
    /// Lets the rebels of a calm <paramref name="civ"/> (see <see cref="Rebels"/>; most of all the settlements beyond the
    /// government's reach) drift away as a new civilization with <see cref="StabilityRules.DriftPerMillePerYear"/> a year
    /// (GDD 7.3, stage 5b). Not in the calm after a revolt nor when the game is full. Returns the chronicle line, or null.
    /// </summary>
    public static string? Drift(GameContent content, GameState state, CivilizationState civ, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(random);
        StabilityRules rules = content.Stability;
        if (rules.DriftPerMillePerYear <= 0 || state.ActionMonth < civ.RevoltCalmUntilMonth || state.Civilizations.Count >= rules.MaxCivilizations)
        {
            return null;
        }

        int chance = PerStep(rules.DriftPerMillePerYear, civ);
        if (random.NextInt(1000) >= chance || Rebels(content, state, civ) is not { } rebels)
        {
            return null;
        }

        civ.RevoltCalmUntilMonth = state.ActionMonth + (rules.RevoltCalmYears * 12L);
        return Secede(content, state, civ, rebels, random);
    }

    // The chance, per mille, of something with perMilleYear a year happening in the step of civ (at least a year).
    private static int PerStep(int perMilleYear, CivilizationState civ)
    {
        int years = Math.Max(12, civ.StepMonths) / 12;
        long calm = 1_000_000;
        for (int y = 0; y < years && calm > 0; y++)
        {
            calm = calm * (1000 - perMilleYear) / 1000;
        }

        return (int)((1_000_000 - calm) / 1000);
    }

    /// <summary>
    /// Lets a revolt break out in <paramref name="civ"/> with <see cref="ChancePerMille"/>: a secession when there are
    /// enough rebels and room for another civilization, else a coup. Returns the chronicle line, or null when all stays calm.
    /// </summary>
    public static string? Check(GameContent content, GameState state, CivilizationState civ, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        int chance = ChancePerMille(content, state, civ);
        if (chance <= 0 || random.NextInt(1000) >= chance)
        {
            return null;
        }

        return Revolt(content, state, civ, random);
    }

    /// <summary>A revolt breaks out in <paramref name="civ"/> now (see <see cref="Check"/>). Returns the chronicle line.</summary>
    public static string Revolt(GameContent content, GameState state, CivilizationState civ, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(random);
        StabilityRules rules = content.Stability;
        civ.RevoltCalmUntilMonth = state.ActionMonth + (rules.RevoltCalmYears * 12L);
        List<SettlementState>? rebels = Rebels(content, state, civ);
        return rebels is not null && state.Civilizations.Count < rules.MaxCivilizations
            ? Secede(content, state, civ, rebels, random)
            : Coup(content, state, civ, random);
    }

    /// <summary>
    /// The rebels of <paramref name="civ"/> who would break away: the largest group (most people; ties: the lowest first id)
    /// of its settlements other than the capital with loyalty below the threshold, linked within the trade link distance
    /// on one world, none of them out on or the target of a campaign. Null when no group has enough people.
    /// </summary>
    public static List<SettlementState>? Rebels(GameContent content, GameState state, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        StabilityRules rules = content.Stability;
        var rebels = new List<SettlementState>();
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive && s.Owner == civ.Id && s.Id.Value != civ.Capital && s.Loyalty < rules.SecessionLoyaltyBelow
                && !state.Episodes.OutFrom(civ.Id, s.Id.Value) && !Targeted(state, s.Id.Value))
            {
                rebels.Add(s);
            }
        }

        if (rebels.Count == 0)
        {
            return null;
        }

        // Groups of rebels linked within the trade link distance (union by the lower index).
        var group = new int[rebels.Count];
        for (int i = 0; i < group.Length; i++)
        {
            group[i] = i;
        }

        for (int a = 0; a < rebels.Count; a++)
        {
            for (int b = a + 1; b < rebels.Count; b++)
            {
                if (rebels[a].World == rebels[b].World
                    && state.Worlds[rebels[a].World.Value].Geometry.Distance(rebels[a].Cell, rebels[b].Cell) <= content.Trade.LinkCells)
                {
                    int ra = Root(group, a);
                    int rb = Root(group, b);
                    group[Math.Max(ra, rb)] = Math.Min(ra, rb);
                }
            }
        }

        var people = new long[rebels.Count];
        for (int i = 0; i < rebels.Count; i++)
        {
            people[Root(group, i)] += rebels[i].TotalPopulation();
        }

        int best = -1;
        for (int i = 0; i < rebels.Count; i++)
        {
            if (Root(group, i) == i && (best < 0 || people[i] > people[best]))
            {
                best = i;
            }
        }

        if (people[best] < rules.SecessionMinPeople)
        {
            return null;
        }

        var result = new List<SettlementState>();
        for (int i = 0; i < rebels.Count; i++)
        {
            if (Root(group, i) == best)
            {
                result.Add(rebels[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// <paramref name="rebels"/> break away from <paramref name="parent"/> as a new civilization (GDD 7.3, decision of
    /// 2026-11-07): a daughter language and a name related to the parent's, another government, a character near the
    /// parent's, a leader of its own, the parent's technologies and map memory, and the contacts the parent had. The parent
    /// remembers the secession; war comes only if its diplomat chooses it. Returns the chronicle line.
    /// </summary>
    public static string Secede(GameContent content, GameState state, CivilizationState parent, List<SettlementState> rebels, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(rebels);
        ArgumentNullException.ThrowIfNull(random);
        StabilityRules rules = content.Stability;
        string parentTitle = Governments.Title(content, parent);
        CivilizationState child = state.AddCivilization(string.Empty);
        child.ParentCiv = parent.Id.Value;
        child.FoundedMonth = state.Month;
        child.LanguageSeed = parent.LanguageSeed;
        child.LanguageMutations = [.. parent.LanguageMutations, random.NextULong()];
        child.Name = Naming.Names.DaughterPeople(content.Names, state, child, parent.Name);
        child.Era = parent.Era;
        child.KnownTechnologies = (bool[])parent.KnownTechnologies.Clone();
        child.KnownCount = parent.KnownCount;
        child.SeenYear = (int[])parent.SeenYear.Clone();
        child.GoalWeights = (int[])parent.GoalWeights.Clone();
        child.Traits = new int[parent.Traits.Length];
        for (int t = 0; t < child.Traits.Length; t++)
        {
            int shift = random.NextInt(-rules.SecessionCharacterShift, rules.SecessionCharacterShift + 1);
            child.Traits[t] = Math.Clamp(parent.Traits[t] + shift, 0, 1000);
        }

        child.Government = Governments.Leaning(content, child, child.Era, parent.Government); // a splinter of other views
        Leaders.Seize(content, state, child, random, state.Month);
        child.NextStepMonth = parent.NextStepMonth;
        child.StepStartMonth = parent.StepStartMonth;
        child.RevoltCalmUntilMonth = state.ActionMonth + (rules.RevoltCalmYears * 12L);

        var moved = new HashSet<int>();
        SettlementState largest = rebels[0];
        foreach (SettlementState s in rebels)
        {
            s.Owner = child.Id;
            s.JourneyTarget = SettlementState.NoJourney;
            moved.Add(s.Id.Value + 1); // the claim layer stores settlement id + 1
            if (s.TotalPopulation() > largest.TotalPopulation())
            {
                largest = s;
            }
        }

        child.StartCell = largest.Cell;
        child.Capital = largest.Id.Value;
        foreach (WorldState world in state.Worlds)
        {
            ReadOnlySpan<int> claims = world.Map.Claim.Values;
            for (int i = 0; i < claims.Length; i++)
            {
                if (claims[i] != 0 && moved.Contains(claims[i]))
                {
                    world.Map.Owner.Set(i, (short)child.Id.Value);
                }
            }
        }

        Inherit(content, state, parent, child);
        string first = rebels[0].Name;
        string others = rebels.Count > 1
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $" and {rebels.Count - 1} other settlement{(rebels.Count > 2 ? "s" : string.Empty)}")
            : string.Empty;
        return $"{first}{others} broke away from the {parentTitle} and founded the {Governments.Title(content, child)}, led by {Leaders.Styled(content, child)}.";
    }

    /// <summary>
    /// A coup in <paramref name="civ"/>: a new leader seizes power and brings in the government the people lean to most
    /// other than the present one (when another is open); anarchy follows. Returns the chronicle line.
    /// </summary>
    public static string Coup(GameContent content, GameState state, CivilizationState civ, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(random);
        string before = Governments.Title(content, civ);
        int government = Governments.Leaning(content, civ, civ.Era, civ.Government);
        bool changed = government != civ.Government && content.Governments.Count > 0;
        if (changed)
        {
            Governments.EnactNow(content, state, civ, government);
        }
        else
        {
            civ.AnarchyUntilMonth = Math.Max(civ.AnarchyUntilMonth, state.ActionMonth + (content.Stability.AnarchyYears * 12L));
        }

        Leaders.Seize(content, state, civ, random, state.Month);
        string became = changed ? $"; it became the {Governments.Title(content, civ)}" : string.Empty;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"A coup in the {before}: {Leaders.Styled(content, civ)} seized power{became}; {content.Stability.AnarchyYears} years of anarchy follow.");
    }

    // The new people knows whom the parent knew (with a first opinion of its own); the two meet, and the parent remembers.
    private static void Inherit(GameContent content, GameState state, CivilizationState parent, CivilizationState child)
    {
        foreach (Relation r in parent.Relations)
        {
            Relation n = child.AddRelation(r.Other);
            n.Level = r.Level;
            n.Way = r.Way;
            n.FirstMonth = state.Month;
            n.MetMonth = r.HasMet ? state.Month : 0;
            n.LastContactMonth = r.LastContactMonth;
            n.Opening = r.HasMet ? Contacts.Opening(content.Relations, child, r.Way) : 0;
            n.RumorCell = r.RumorCell;
            n.RumorPeople = r.RumorPeople;
            n.RumorFrom = r.RumorFrom;
            n.Recount(state.ActionMonth);
        }

        Contacts.Meet(content, state, child, parent.Id, ContactWay.Border);
        Contacts.Meet(content, state, parent, child.Id, ContactWay.Border);
        RelationEventDefinition def = content.Relations.Events[(int)RelationEvent.Secession];
        int years = def.Years + (int)((long)(def.VengefulYears - def.Years) * parent.Trait(Ai.Trait.Vengefulness) / 1000);
        Relation memory = parent.Relation(child.Id)!;
        memory.Remember(
            new RelationMemory { Kind = RelationEvent.Secession, Sympathy = def.Sympathy, Trust = def.Trust, EndMonth = state.ActionMonth, FadeMonths = Math.Max(1, years * 12) },
            content.Relations.MaxMemories,
            state.ActionMonth);
        memory.Recount(state.ActionMonth);
    }

    private static bool Targeted(GameState state, int settlement)
    {
        foreach (Episodes.Episode e in state.Episodes.All)
        {
            if (!e.Ended && (e.Target == settlement || e.GoalTarget == settlement))
            {
                return true;
            }
        }

        return false;
    }

    private static int Root(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }
}
