using System;
using System.Globalization;
using System.Text;
using CivSim.Core.Content;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Governance;

/// <summary>
/// Leaders (GDD 7.5; stage 4f PR 2): a name from the people's language, an age and 2–3 traits that shift the
/// civilization's character while the leader rules. Leaders age and die; who comes next, and whether a succession
/// crisis follows, depends on the government. Every draw comes from a generator derived from the seed, the leaders
/// stream, the civilization and the month, so no shared stream moves.
/// </summary>
public static class Leaders
{
    // Leader names come from numbers far from the place-name counter and the renaming suggestions.
    private const ulong NameBase = 1UL << 42;

    // Derived generators: entity = civilization + this offset (Character.Roll uses the bare civilization id).
    private const ulong EntityOffset = 1UL << 20;

    /// <summary>Gives every civilization of a new game its first leader (after its government is chosen).</summary>
    public static void CrownFirst(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        if (content.Leaders.Traits.Count == 0)
        {
            return;
        }

        var random = new DeterministicRandom(0);
        foreach (CivilizationState civ in state.Civilizations)
        {
            random.Reseed(state.Random.DeriveSeed(RandomStreamId.Leaders, (ulong)civ.Id.Value + EntityOffset, 0));
            Crown(content, state, civ, random, state.Month, content.Leaders.StartAge, council: false);
        }
    }

    /// <summary>The leader as a short text, e.g. "Chief Tarok, 54, conqueror, builder"; empty without a leader.</summary>
    public static string Describe(GameContent content, GameState state, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        if (civ.LeaderName.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append(Styled(content, civ)).Append(", ").Append((Age(civ, state.Month)).ToString(CultureInfo.InvariantCulture));
        foreach (int t in civ.LeaderTraits)
        {
            sb.Append(", ").Append(content.Leaders.Traits[t].Name);
        }

        return sb.ToString();
    }

    /// <summary>The leader's age in whole years in <paramref name="month"/>.</summary>
    public static int Age(CivilizationState civ, long month)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return (int)Math.Max(0, (month - civ.LeaderBornMonth) / 12);
    }

    /// <summary>
    /// Lives the leader of <paramref name="civ"/> through <paramref name="months"/> months from <paramref name="from"/>: in each
    /// year (or the part of a year left) the leader may die, and an elected leader's term may end; a successor follows each
    /// time. Writes at most one chronicle line through <paramref name="chronicle"/>.
    /// </summary>
    public static void Live(GameContent content, GameState state, CivilizationState civ, long from, int months, DeterministicRandom random, Action<string>? chronicle)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(random);
        if (civ.LeaderName.Length == 0 || content.Leaders.Traits.Count == 0)
        {
            return;
        }

        LeaderRules rules = content.Leaders;
        string? first = null;
        int successions = 0;
        bool crisis = false;
        long last = from;
        for (int done = 0; done < months;)
        {
            int span = Math.Min(12, months - done);
            long month = from + done;
            done += span;
            GovernmentDefinition? g = Governments.Of(content, civ);
            int perMille = (int)((long)rules.DeathPerMille(Age(civ, month)) * span / 12);
            bool dies = random.NextInt(1000) < perMille;
            bool termEnds = !dies && g is { TermYears: > 0 } && from + done - civ.LeaderSinceMonth >= g.TermYears * 12L;
            if (!dies && !termEnds)
            {
                continue;
            }

            long when = from + done;
            string before = Styled(content, civ);
            int age = Age(civ, when);
            first ??= dies ? $"{before} of {Governments.Title(content, civ)} died at {age}" : $"The term of {before} of {Governments.Title(content, civ)} ended";
            (int Min, int Max) ages = g?.SuccessorAge ?? rules.StartAge;
            Crown(content, state, civ, random, when, ages, council: g?.Succession == Succession.Council);
            crisis = g is not null && g.CrisisYears > 0 && random.NextInt(100) < g.CrisisPercent;
            if (crisis)
            {
                civ.CrisisUntilMonth = Math.Max(civ.CrisisUntilMonth, when + (g!.CrisisYears * 12L));
            }

            successions++;
            last = when;
        }

        // One chronicle line per step, however many leaders followed one another in it.
        if (first is not null && chronicle is not null)
        {
            string more = successions > 1 ? string.Create(CultureInfo.InvariantCulture, $"; {successions - 1} more leaders followed") : string.Empty;
            chronicle(string.Create(
                CultureInfo.InvariantCulture,
                $"{first}{more}; {Describe(content, state, civ, last)} leads them now{(crisis ? ", after a struggle for power" : string.Empty)}."));
        }
    }

    private static string Describe(GameContent content, GameState state, CivilizationState civ, long month)
    {
        var sb = new StringBuilder(Styled(content, civ));
        sb.Append(" (").Append(Age(civ, month).ToString(CultureInfo.InvariantCulture));
        foreach (int t in civ.LeaderTraits)
        {
            sb.Append(", ").Append(content.Leaders.Traits[t].Name);
        }

        return sb.Append(')').ToString();
    }

    // "Chief Tarok".
    private static string Styled(GameContent content, CivilizationState civ) =>
        Governments.Of(content, civ) is { } g ? $"{g.LeaderTitle} {civ.LeaderName}" : civ.LeaderName;

    // A new leader: a name, an age in the range and traits. A council weighs several candidates and takes the one whose
    // traits fit the people's own character best.
    private static void Crown(GameContent content, GameState state, CivilizationState civ, DeterministicRandom random, long month, (int Min, int Max) ages, bool council)
    {
        LeaderRules rules = content.Leaders;
        int candidates = council ? Math.Max(1, rules.CouncilCandidates) : 1;
        int[] best = [];
        long bestFit = long.MinValue;
        for (int k = 0; k < candidates; k++)
        {
            int[] traits = DrawTraits(rules, random);
            long fit = 0;
            foreach (int t in traits)
            {
                for (int x = 0; x < rules.Traits[t].Shifts.Count; x++)
                {
                    int own = civ.OwnTrait((Ai.Trait)x);
                    fit += (long)rules.Traits[t].Shifts[x] * (own - Ai.Character.Average);
                }
            }

            if (fit > bestFit)
            {
                best = traits;
                bestFit = fit;
            }
        }

        int age = ages.Min + random.NextInt(Math.Max(1, ages.Max - ages.Min + 1));
        civ.LeaderTraits = best;
        civ.LeaderShift = ShiftOf(rules, best);
        civ.LeaderBornMonth = month - (age * 12L);
        civ.LeaderSinceMonth = month;
        civ.LeaderName = Naming.Names.Language(content.Names, civ).PlaceName(NameBase + (ulong)civ.Leaders);
        civ.Leaders++;
    }

    // traitCount distinct traits, in the order drawn.
    private static int[] DrawTraits(LeaderRules rules, DeterministicRandom random)
    {
        int n = rules.Traits.Count;
        int count = Math.Min(n, rules.TraitCount.Min + random.NextInt(Math.Max(1, rules.TraitCount.Max - rules.TraitCount.Min + 1)));
        var traits = new int[count];
        for (int k = 0; k < count; k++)
        {
            int pick = random.NextInt(n - k);
            // The pick-th trait not taken yet.
            for (int t = 0; t < n; t++)
            {
                if (Array.IndexOf(traits, t, 0, k) >= 0)
                {
                    continue;
                }

                if (pick-- == 0)
                {
                    traits[k] = t;
                    break;
                }
            }
        }

        return traits;
    }

    /// <summary>What leader traits <paramref name="traits"/> add to each character trait, per mille.</summary>
    public static int[] ShiftOf(LeaderRules rules, int[] traits)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(traits);
        var shift = new int[Ai.Character.TraitCount];
        foreach (int t in traits)
        {
            for (int x = 0; x < rules.Traits[t].Shifts.Count && x < shift.Length; x++)
            {
                shift[x] += rules.Traits[t].Shifts[x];
            }
        }

        return shift;
    }

    /// <summary>The generator for the leader of <paramref name="civ"/> in the step starting at <paramref name="month"/>.</summary>
    public static void Reseed(GameState state, CivilizationState civ, long month, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(random);
        random.Reseed(state.Random.DeriveSeed(RandomStreamId.Leaders, (ulong)civ.Id.Value + EntityOffset, month));
    }
}

/// <summary>State phase of leaders (GDD 7.5; stage 4f PR 2): in its own step each civilization's leader ages, may die and is followed.</summary>
public sealed class LeaderSystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly DeterministicRandom _random = new(0);

    /// <summary>Creates the system.</summary>
    public LeaderSystem(GameContent content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.State;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        foreach (CivilizationState civ in state.Civilizations)
        {
            if (!WorldClock.Steps(civ))
            {
                continue;
            }

            Leaders.Reseed(state, civ, state.Month, _random);
            Leaders.Live(_content, state, civ, state.Month, civ.StepMonths, _random, context.Chronicle);
        }
    }
}
