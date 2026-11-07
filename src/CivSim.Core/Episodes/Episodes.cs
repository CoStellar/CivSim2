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

    /// <summary>Game month of its next step.</summary>
    public long NextMonth { get; set; }

    /// <summary>Length of its steps, months.</summary>
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

    /// <summary>The month of the nearest step of any episode, or <see cref="long.MaxValue"/> when none runs.</summary>
    public long NextMonth()
    {
        long next = long.MaxValue;
        foreach (Episode e in _episodes)
        {
            if (!e.Ended)
            {
                next = Math.Min(next, e.NextMonth);
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

    /// <summary>True when a campaign of <paramref name="civ"/> left from <paramref name="home"/> and is still out.</summary>
    public bool OutFrom(CivId civ, int home)
    {
        foreach (Episode e in _episodes)
        {
            if (!e.Ended && e.Kind == EpisodeKind.Campaign && e.Civ == civ && e.Home == home)
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
