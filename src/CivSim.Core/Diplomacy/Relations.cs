using System;
using System.Collections.Generic;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>How much a civilization knows of another (GDD 8.5, 9.1). Values are part of the save format: only append.</summary>
public enum ContactLevel : byte
{
    /// <summary>Only a rumor: the people exist, roughly in some direction; no diplomacy yet.</summary>
    Rumor = 1,

    /// <summary>Real contact: the civilizations have met.</summary>
    Met = 2,
}

/// <summary>How a civilization first learned of another. Values are part of the save format: only append.</summary>
public enum ContactWay : byte
{
    /// <summary>Its settlements or scouts saw a camp or settlement of the other.</summary>
    Sighted = 0,

    /// <summary>The influence zones of the two touch.</summary>
    Border = 1,

    /// <summary>The other attacked one of its camps.</summary>
    Attack = 2,

    /// <summary>A rumor from a civilization it knows.</summary>
    Rumor = 3,
}

/// <summary>
/// Kinds of remembered events between two civilizations (GDD 8.5). Each has a sympathy and trust value and fades
/// after it ends. Values are part of the save format and index <see cref="RelationRules.Events"/>: only append.
/// </summary>
public enum RelationEvent : byte
{
    /// <summary>They attacked us (stage 4c: a camp drove out or tried to drive out one of ours).</summary>
    Attack = 0,

    /// <summary>They broke a treaty with us (stage 4d).</summary>
    BrokenTreaty = 1,

    /// <summary>They live in a settlement we claim (ongoing while the claim lasts).</summary>
    BorderDispute = 2,

    /// <summary>We trade with them (ongoing; stage 4d).</summary>
    ActiveTrade = 3,

    /// <summary>We fight the same enemy (ongoing; stage 4e).</summary>
    CommonEnemy = 4,

    /// <summary>They helped us in a war (stage 4e).</summary>
    WarHelp = 5,

    /// <summary>They live under another form of government (ongoing; stage 4f).</summary>
    OtherGovernment = 6,

    /// <summary>They declared war on us, or on someone we know, for land or loot (stage 4e, GDD 9.3: aggression).</summary>
    Aggression = 7,

    /// <summary>They broke away from us (stage 4f PR 4, GDD 7.3: secession).</summary>
    Secession = 8,
}

/// <summary>One kind of remembered event (data/relations.json). Points are in hundredths; rates per year.</summary>
public sealed record RelationEventDefinition
{
    /// <summary>The kind this definition describes.</summary>
    public RelationEvent Kind { get; init; }

    /// <summary>Sympathy the event gives at once, or the most an ongoing event reaches, hundredths of a point.</summary>
    public int Sympathy { get; init; }

    /// <summary>Trust the event gives at once, or the most an ongoing event reaches, hundredths of a point.</summary>
    public int Trust { get; init; }

    /// <summary>Sympathy an ongoing event adds per year until it reaches <see cref="Sympathy"/>; 0 = all at once.</summary>
    public int SympathyPerYear { get; init; }

    /// <summary>Trust an ongoing event adds per year until it reaches <see cref="Trust"/>; 0 = all at once.</summary>
    public int TrustPerYear { get; init; }

    /// <summary>Years over which the memory fades to nothing once the event is over.</summary>
    public int Years { get; init; }

    /// <summary>Fading years for the most vengeful civilization (vengefulness 1000); <see cref="Years"/> for none.</summary>
    public int VengefulYears { get; init; }

    /// <summary>True for kinds that last while a condition holds (border dispute, trade, common enemy, government).</summary>
    public static bool IsOngoing(RelationEvent kind) =>
        kind is RelationEvent.BorderDispute or RelationEvent.ActiveTrade or RelationEvent.CommonEnemy or RelationEvent.OtherGovernment;
}

/// <summary>Contact, rumors and the memory of relations (data/relations.json, GDD 8.5; stage 4c).</summary>
public sealed record RelationRules
{
    /// <summary>Chance per century that a civilization hears of a people one of the civilizations it met has met, percent.</summary>
    public int RumorChancePercentPerCentury { get; init; } = 20;

    /// <summary>How wrong a rumor may be: the strength is off by up to this share, the place by up to this share of the distance.</summary>
    public int RumorErrorPercent { get; init; } = 50;

    /// <summary>First opinion from openness: a closed civilization (0) starts at minus this, an open one (1000) at plus this, hundredths.</summary>
    public int OpeningSympathy { get; init; } = 2000;

    /// <summary>First opinion when the first contact is touching borders, hundredths (on top of openness).</summary>
    public int BorderOpening { get; init; } = -500;

    /// <summary>Most remembered events per known civilization; when full, the weakest memory makes room.</summary>
    public int MaxMemories { get; init; } = 12;

    /// <summary>Remembered events, index = <see cref="RelationEvent"/>.</summary>
    public IReadOnlyList<RelationEventDefinition> Events { get; init; } = Defaults();

    /// <summary>The event table of GDD 8.5, durations read as years.</summary>
    public static IReadOnlyList<RelationEventDefinition> Defaults() =>
    [
        new() { Kind = RelationEvent.Attack, Sympathy = -5000, Trust = -2000, Years = 150, VengefulYears = 300 },
        new() { Kind = RelationEvent.BrokenTreaty, Sympathy = -2000, Trust = -4000, Years = 200, VengefulYears = 200 },
        new() { Kind = RelationEvent.BorderDispute, Sympathy = -3000, SympathyPerYear = -100, Years = 20, VengefulYears = 20 },
        new() { Kind = RelationEvent.ActiveTrade, Sympathy = 3000, SympathyPerYear = 50, Trust = 2000, TrustPerYear = 20, Years = 20, VengefulYears = 20 },
        new() { Kind = RelationEvent.CommonEnemy, Sympathy = 2000, Trust = 1000, Years = 50, VengefulYears = 50 },
        new() { Kind = RelationEvent.WarHelp, Sympathy = 3000, Trust = 2500, Years = 200, VengefulYears = 200 },
        new() { Kind = RelationEvent.OtherGovernment, Sympathy = -1000, Years = 0, VengefulYears = 0 },
        new() { Kind = RelationEvent.Aggression, Sympathy = -3000, Trust = -1500, Years = 100, VengefulYears = 100 },
        new() { Kind = RelationEvent.Secession, Sympathy = -6000, Trust = -4000, Years = 200, VengefulYears = 400 },
    ];
}

/// <summary>One remembered event of a relation (GDD 8.5). Points are in hundredths.</summary>
public struct RelationMemory
{
    /// <summary>What happened.</summary>
    public RelationEvent Kind;

    /// <summary>Sympathy at full strength, hundredths.</summary>
    public int Sympathy;

    /// <summary>Trust at full strength, hundredths.</summary>
    public int Trust;

    /// <summary>Game month the event happened or ended, or <see cref="Ongoing"/> while it lasts.</summary>
    public long EndMonth;

    /// <summary>Months over which the memory fades to nothing after <see cref="EndMonth"/>.</summary>
    public int FadeMonths;

    /// <summary>Value of <see cref="EndMonth"/> while the event lasts.</summary>
    public const long Ongoing = -1;

    /// <summary>True while the event lasts.</summary>
    public readonly bool IsOngoing => EndMonth == Ongoing;

    /// <summary>Share of the full strength left at <paramref name="month"/>, per mille (1000 while ongoing, 0 once faded).</summary>
    public readonly int Strength(long month)
    {
        if (EndMonth == Ongoing)
        {
            return 1000;
        }

        long elapsed = month - EndMonth;
        if (elapsed >= FadeMonths)
        {
            return 0;
        }

        return elapsed <= 0 ? 1000 : (int)((FadeMonths - elapsed) * 1000 / FadeMonths);
    }
}

/// <summary>
/// What one civilization knows and remembers of another (GDD 8.5). The list of relations starts empty: a civilization
/// enters it only after a sighting, a direct contact or a rumor.
/// </summary>
public sealed class Relation
{
    private readonly List<RelationMemory> _memories = [];

    /// <summary>Creates a relation with the civilization <paramref name="other"/>.</summary>
    public Relation(CivId other) => Other = other;

    /// <summary>The other civilization.</summary>
    public CivId Other { get; }

    /// <summary>How much is known of it.</summary>
    public ContactLevel Level { get; set; }

    /// <summary>How it was first learned of (for a rumor: <see cref="ContactWay.Rumor"/> until real contact).</summary>
    public ContactWay Way { get; set; }

    /// <summary>Game month of the first news (rumor or contact).</summary>
    public long FirstMonth { get; set; }

    /// <summary>Game month of the first real contact; 0 while only a rumor.</summary>
    public long MetMonth { get; set; }

    /// <summary>Game month the civilization was last seen or touched.</summary>
    public long LastContactMonth { get; set; }

    /// <summary>First opinion (circumstances of the meeting and openness), hundredths; it does not fade.</summary>
    public int Opening { get; set; }

    /// <summary>Do we like them: opening plus the remembered events, −10 000..10 000 hundredths (GDD 8.5).</summary>
    public int Sympathy { get; set; }

    /// <summary>Do they keep their word: the remembered events, −10 000..10 000 hundredths (GDD 8.5).</summary>
    public int Trust { get; set; }

    /// <summary>For a rumor: the Earth cell where the people are said to live (roughly), else -1.</summary>
    public int RumorCell { get; set; } = -1;

    /// <summary>For a rumor: how many people they are said to be (exaggerated or played down).</summary>
    public int RumorPeople { get; set; }

    /// <summary>For a rumor: the civilization the news came from.</summary>
    public int RumorFrom { get; set; } = -1;

    /// <summary>Remembered events, oldest first.</summary>
    public IReadOnlyList<RelationMemory> Memories => _memories;

    /// <summary>
    /// The month of the last peace with them (stage 4f PR 5): wrongs remembered from before it are settled and no longer a
    /// reason for war, though they still colour the opinion until they fade. -1 = never at peace after a war.
    /// </summary>
    public long SettledMonth { get; set; } = -1;

    /// <summary>Tick of the player's last diplomatic decision about this civilization (war, a proposal, an answer), or -1 (stage 6 PR P1).</summary>
    public long PlayerOrderTick { get; set; } = -1;

    /// <summary>True after real contact (diplomacy is possible, GDD 8.5).</summary>
    public bool HasMet => Level == ContactLevel.Met;

    /// <summary>Adds a remembered event (loading and recording); when full, the weakest memory makes room.</summary>
    public void Remember(RelationMemory memory, int maxMemories, long month)
    {
        if (_memories.Count >= Math.Max(1, maxMemories))
        {
            int weakest = 0;
            long least = long.MaxValue;
            for (int k = 0; k < _memories.Count; k++)
            {
                RelationMemory m = _memories[k];
                long weight = ((long)Math.Abs(m.Sympathy) + Math.Abs(m.Trust)) * m.Strength(month);
                if (weight < least)
                {
                    least = weight;
                    weakest = k;
                }
            }

            _memories.RemoveAt(weakest);
        }

        _memories.Add(memory);
    }

    /// <summary>Index of the ongoing memory of <paramref name="kind"/>, or -1.</summary>
    public int FindOngoing(RelationEvent kind)
    {
        for (int k = 0; k < _memories.Count; k++)
        {
            if (_memories[k].Kind == kind && _memories[k].IsOngoing)
            {
                return k;
            }
        }

        return -1;
    }

    /// <summary>Replaces the memory at <paramref name="index"/>.</summary>
    public void SetMemory(int index, RelationMemory memory) => _memories[index] = memory;

    /// <summary>Forgets every memory that has faded by <paramref name="month"/>.</summary>
    public void Forget(long month)
    {
        for (int k = _memories.Count - 1; k >= 0; k--)
        {
            if (_memories[k].Strength(month) == 0)
            {
                _memories.RemoveAt(k);
            }
        }
    }

    /// <summary>Recomputes <see cref="Sympathy"/> and <see cref="Trust"/> from the opening and the memories at <paramref name="month"/>.</summary>
    public void Recount(long month)
    {
        long sympathy = Opening;
        long trust = 0;
        foreach (RelationMemory m in _memories)
        {
            int strength = m.Strength(month);
            sympathy += (long)m.Sympathy * strength / 1000;
            trust += (long)m.Trust * strength / 1000;
        }

        Sympathy = (int)Math.Clamp(sympathy, -Max, Max);
        Trust = (int)Math.Clamp(trust, -Max, Max);
    }

    /// <summary>Largest sympathy or trust, hundredths (100 points).</summary>
    public const int Max = 10_000;
}
