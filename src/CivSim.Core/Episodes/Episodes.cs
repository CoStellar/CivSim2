using System;
using System.Collections.Generic;
using CivSim.Core.Numerics;
using CivSim.Core.State;

namespace CivSim.Core.Episodes;

/// <summary>Kinds of episodes (ADR 0022). Values are part of the save format: only append.</summary>
public enum EpisodeKind : byte
{
    /// <summary>A war campaign: warriors march out, fight at the target and come back (stage 4e).</summary>
    Campaign = 0,
}

/// <summary>Where a campaign stands. Values are part of the save format: only append.</summary>
public enum CampaignPhase : byte
{
    /// <summary>Marching to the target.</summary>
    March = 0,

    /// <summary>Going home with the survivors and the loot.</summary>
    Return = 1,

    /// <summary>Warriors of several settlements gather at the settlement the campaign leaves from (stage 4e PR 3).</summary>
    Muster = 2,

    /// <summary>Fighting a battle at its target, a round every few ticks (stage 6); nobody can call it back meanwhile.</summary>
    Battle = 3,
}

/// <summary>
/// One event with a course of its own (ADR 0022): it has a short step in months and is computed in every step of its own
/// until it ends, while the civilizations around it keep the long steps of their eras. Today the only kind is the war
/// campaign; its fields are the campaign's.
/// </summary>
public sealed class Episode
{
    /// <summary>Unique id, in the order episodes began.</summary>
    public long Id { get; internal set; }

    /// <summary>What kind of episode it is.</summary>
    public EpisodeKind Kind { get; init; }

    /// <summary>The civilization that started it (a campaign: the attacker).</summary>
    public CivId Civ { get; init; }

    /// <summary>The other side (a campaign: the defender).</summary>
    public CivId Other { get; init; }

    /// <summary>Game month it began.</summary>
    public long StartMonth { get; init; }

    /// <summary>Tick of its next step (ADR 0025: episodes run on ticks, the world never waits for them).</summary>
    public long NextTick { get; set; }

    /// <summary>Length of its steps, months of action (each lasts data/time.json ticksPerActionMonth ticks).</summary>
    public int TickMonths { get; init; } = 1;

    /// <summary>Campaign: settlement the warriors came from (and go back to).</summary>
    public int Home { get; set; } = -1;

    /// <summary>Campaign: the settlement it marches on.</summary>
    public int Target { get; set; } = -1;

    /// <summary>Campaign: warriors on the march.</summary>
    public int Warriors { get; set; }

    /// <summary>Campaign: cell it stands on (world 0).</summary>
    public int Cell { get; set; }

    /// <summary>Campaign: march or return.</summary>
    public CampaignPhase Phase { get; set; }

    /// <summary>Campaign: warriors killed so far.</summary>
    public int Dead { get; set; }

    /// <summary>Campaign: goods carried home, index = good.</summary>
    public Amount[] Loot { get; set; } = [];

    /// <summary>Campaign: the settlement the war campaign is about; <see cref="Target"/> differs while it raids for food.</summary>
    public int GoalTarget { get; set; } = -1;

    /// <summary>Campaign: settlements that sent warriors, the one it leaves from first (stage 4e PR 3).</summary>
    public int[] Members { get; set; } = [];

    /// <summary>Campaign: warriors each of <see cref="Members"/> sent.</summary>
    public int[] MemberWarriors { get; set; } = [];

    /// <summary>Campaign: warriors who set out.</summary>
    public int Mustered { get; set; }

    /// <summary>Campaign: average strength of its warriors' arms, percent (data/war.json armaments).</summary>
    public int ArmsPercent { get; set; } = 100;

    /// <summary>Campaign: morale, 0..100; strength in a fight is multiplied by it.</summary>
    public int Morale { get; set; } = 100;

    /// <summary>Campaign: tick the muster ends and the march begins.</summary>
    public long MusterUntilTick { get; set; }

    /// <summary>Campaign: food it carries, index = good.</summary>
    public Amount[] Food { get; set; } = [];

    /// <summary>Campaign: months it went hungry.</summary>
    public int HungryMonths { get; set; }

    /// <summary>Campaign: raids for food it made.</summary>
    public int Forays { get; set; }

    /// <summary>Campaign: true while it marches on a settlement for food (<see cref="Target"/>) instead of its goal.</summary>
    public bool Foraging { get; set; }

    /// <summary>Campaign: the settlement it last raided for food, or -1 (it does not raid the same one twice in a row).</summary>
    public int LastPrey { get; set; } = -1;

    /// <summary>Campaign: settlement its carriers came from, or -1 when no carriers are on the way.</summary>
    public int ConvoyFrom { get; set; } = -1;

    /// <summary>Campaign: cell its carriers stand on (world 0).</summary>
    public int ConvoyCell { get; set; }

    /// <summary>Campaign: carriers on the way.</summary>
    public int ConvoyCarriers { get; set; }

    /// <summary>Campaign: food the carriers bring, index = good.</summary>
    public Amount[] ConvoyFood { get; set; } = [];

    /// <summary>Campaign: battles it fought (stage 4e PR 4).</summary>
    public int Battles { get; set; }

    /// <summary>Campaign: battles it won.</summary>
    public int Wins { get; set; }

    /// <summary>Campaign: enemies it killed.</summary>
    public int Killed { get; set; }

    /// <summary>Campaign: loads of food carriers handed over.</summary>
    public int Convoys { get; set; }

    /// <summary>Id of the unit the army walks as (stage 6 PR 3b), or -1.</summary>
    public long Army { get; set; } = -1;

    /// <summary>Id of the carriers' unit bringing food (stage 6 PR 3b), or -1.</summary>
    public long Convoy { get; set; } = -1;

    /// <summary>Tick since which the army stands within reach of its target (it attacks a month of action later), or -1.</summary>
    public long InRangeSince { get; set; } = -1;

    /// <summary>True when the army flees after a lost battle (stage 6 PR 3b): nobody can turn it.</summary>
    public bool Fleeing { get; set; }

    /// <summary>The battle it fights in <see cref="CampaignPhase.Battle"/> (stage 6: a round every few ticks).</summary>
    public BattleState Fight { get; set; }

    /// <summary>Tick of the next round of <see cref="Fight"/>.</summary>
    public long RoundTick { get; set; }

    /// <summary>Its warriors killed in the battle under way.</summary>
    public int FightLost { get; set; }

    /// <summary>Enemies it killed in the battle under way.</summary>
    public int FightKilled { get; set; }

    /// <summary>True once it is over (removed at the end of the step).</summary>
    public bool Ended { get; set; }
}

/// <summary>Every running episode, kept in the order they began (ADR 0022).</summary>
public sealed class EpisodeBook
{
    private readonly List<Episode> _episodes = [];

    /// <summary>Running episodes in the order they began.</summary>
    public IReadOnlyList<Episode> All => _episodes;

    /// <summary>Id the next episode will get.</summary>
    public long NextId { get; private set; }

    /// <summary>Number of running episodes.</summary>
    public int Count => _episodes.Count;

    /// <summary>Starts an episode; its id is assigned here.</summary>
    public Episode Begin(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        episode.Id = NextId++;
        _episodes.Add(episode);
        return episode;
    }

    /// <summary>Removes the episodes that ended; returns how many.</summary>
    public int RemoveEnded() => _episodes.RemoveAll(e => e.Ended);

    /// <summary>The tick of the nearest step of any episode, or <see cref="long.MaxValue"/> when none runs.</summary>
    public long NextTick()
    {
        long next = long.MaxValue;
        foreach (Episode e in _episodes)
        {
            if (!e.Ended)
            {
                next = Math.Min(next, e.NextTick);
            }
        }

        return next;
    }

    /// <summary>True when <paramref name="civ"/> has a campaign of its own on the way against <paramref name="other"/>.</summary>
    public bool Campaigning(CivId civ, CivId other)
    {
        foreach (Episode e in _episodes)
        {
            if (!e.Ended && e.Kind == EpisodeKind.Campaign && e.Civ == civ && e.Other == other)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when warriors of <paramref name="home"/> are out on a campaign of <paramref name="civ"/>.</summary>
    public bool OutFrom(CivId civ, int home)
    {
        foreach (Episode e in _episodes)
        {
            if (e.Ended || e.Kind != EpisodeKind.Campaign || e.Civ != civ)
            {
                continue;
            }

            if (e.Home == home || Array.IndexOf(e.Members, home) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Adds an episode as it was saved (loading), with its saved id.</summary>
    public void AddForLoad(Episode episode, long id)
    {
        ArgumentNullException.ThrowIfNull(episode);
        episode.Id = id;
        _episodes.Add(episode);
    }

    /// <summary>Sets the next id as it was saved (loading).</summary>
    public void SetNextIdForLoad(long nextId) => NextId = nextId;
}
