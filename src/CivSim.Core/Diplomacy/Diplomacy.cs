using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CivSim.Core.Content;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// The rules of treaties and war (GDD 9.1; stage 4d): who may sign what, signing, declaring war with its broken
/// treaties and called allies, and the reputation that follows. Commands and systems call these; nothing else
/// changes the treaty book.
/// </summary>
public static class Treaties
{
    private const ulong AllySalt = 0xD4UL << 40;

    /// <summary>Why <paramref name="from"/> may not offer <paramref name="kind"/> to <paramref name="to"/> now, or null when it may.</summary>
    public static string? CannotPropose(GameContent content, GameState state, CivId from, CivId to, TreatyKind kind)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        if (CannotSign(content, state, from, to, kind) is { } reason)
        {
            return reason;
        }

        TreatyBook book = state.Treaties;
        int mine = book.FindProposal(from, to, kind);
        if (mine >= 0)
        {
            TreatyProposal p = book.Proposals[mine];
            if (p.Status == ProposalStatus.Pending)
            {
                return "That proposal is already waiting for an answer.";
            }

            if (state.Month < p.Month + (content.Treaties.RetryYears * 12L))
            {
                return "They declined that proposal not long ago.";
            }
        }

        int theirs = book.FindProposal(to, from, kind);
        return theirs >= 0 && book.Proposals[theirs].Status == ProposalStatus.Pending
            ? "They have already proposed the same; answer their proposal."
            : null;
    }

    /// <summary>Why the two may not sign <paramref name="kind"/> now, or null when they may.</summary>
    public static string? CannotSign(GameContent content, GameState state, CivId x, CivId y, TreatyKind kind)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? a = state.FindCivilization(x);
        CivilizationState? b = state.FindCivilization(y);
        if (a is not { IsAlive: true } || b is not { IsAlive: true } || x == y)
        {
            return "Both civilizations must exist.";
        }

        if (a.Relation(y) is not { HasMet: true } || b.Relation(x) is not { HasMet: true })
        {
            return "Diplomacy needs real contact on both sides.";
        }

        if (kind == TreatyKind.War)
        {
            return "War is declared, not signed.";
        }

        TreatyBook book = state.Treaties;
        bool war = book.AtWar(x, y);
        if (kind == TreatyKind.Peace)
        {
            return war ? null : "Peace ends a war; they are not at war.";
        }

        if (war)
        {
            return "They are at war; make peace first.";
        }

        if (book.Has(x, y, kind))
        {
            return "That treaty is already in force.";
        }

        if (kind == TreatyKind.DefensiveAlliance && !book.Has(x, y, TreatyKind.NonAggression))
        {
            return "An alliance needs a non-aggression pact first.";
        }

        int tech = content.Treaties.Kinds[(int)kind].RequiresTechnology;
        return tech >= 0 && (!a.Knows(tech) || !b.Knows(tech)) ? "Both sides must know the technology this treaty needs." : null;
    }

    /// <summary>Why <paramref name="civ"/> may not declare war on <paramref name="target"/> now, or null when it may.</summary>
    public static string? CannotDeclareWar(GameState state, CivId civ, CivId target)
    {
        ArgumentNullException.ThrowIfNull(state);
        CivilizationState? a = state.FindCivilization(civ);
        CivilizationState? b = state.FindCivilization(target);
        if (a is not { IsAlive: true } || b is not { IsAlive: true } || civ == target)
        {
            return "Both civilizations must exist.";
        }

        if (a.Relation(target) is not { HasMet: true })
        {
            return "War needs real contact.";
        }

        return state.Treaties.AtWar(civ, target) ? "They are already at war." : null;
    }

    /// <summary>Signs <paramref name="kind"/> between <paramref name="from"/> (who proposed) and <paramref name="to"/>; returns the chronicle line.</summary>
    public static string Sign(GameContent content, GameState state, CivId from, CivId to, TreatyKind kind)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        TreatyBook book = state.Treaties;
        string a = Name(state, from);
        string b = Name(state, to);
        var treaty = new Treaty(from, to, kind, from, state.Month);
        int years = content.Treaties.Kinds[(int)kind].Years;
        treaty.UntilMonth = years > 0 ? state.Month + (years * 12L) : 0;
        if (kind == TreatyKind.Peace)
        {
            Treaty? war = book.Find(from, to, TreatyKind.War);
            long warYears = war is null ? 0 : (state.Month - war.SinceMonth) / 12;
            book.Remove(from, to, TreatyKind.War);
            book.Add(treaty);
            return string.Create(CultureInfo.InvariantCulture, $"{a} and {b} made peace after {warYears} years of war.");
        }

        book.Add(treaty);
        return kind switch
        {
            TreatyKind.Trade => $"{a} and {b} agreed to trade.",
            TreatyKind.NonAggression => $"{a} and {b} swore not to attack each other.",
            _ => $"{a} and {b} formed a defensive alliance.",
        };
    }

    /// <summary>
    /// <paramref name="civ"/> declares war on <paramref name="target"/> (GDD 9.1): every treaty between them ends; a
    /// broken pact is remembered by the victim and, more weakly, by everyone who knows both; the victim's defensive
    /// allies join the war or let it down. Returns the chronicle line.
    /// </summary>
    public static string DeclareWar(GameContent content, GameState state, CivId civ, CivId target)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        TreatyBook book = state.Treaties;
        bool broke = EndTreaties(content, book, civ, target);
        book.Add(new Treaty(civ, target, TreatyKind.War, civ, state.Month));
        book.RemoveProposals(p => (p.From == civ && p.To == target) || (p.From == target && p.To == civ));
        if (state.FindCivilization(target) is { } victim)
        {
            victim.Alarms |= Simulation.Alarm.WarDeclared; // it answers at once, outside its rhythm (ADR 0018)
        }
        var line = new StringBuilder();
        line.Append(CultureInfo.InvariantCulture, $"{Name(state, civ)} declared war on {Name(state, target)}");
        if (broke)
        {
            line.Append(", breaking their treaty");
            Broke(content, state, civ, target);
        }

        line.Append('.');
        CallAllies(content, state, civ, target, line);
        return line.ToString();
    }

    /// <summary>
    /// <paramref name="breaker"/> broke a treaty with <paramref name="victim"/>: the victim remembers it (GDD 8.5) and so,
    /// at <see cref="TreatyRules.WitnessPercent"/>, does every living civilization that has met both (GDD 9.1: reputation).
    /// </summary>
    public static void Broke(GameContent content, GameState state, CivId breaker, CivId victim)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        foreach (CivilizationState w in state.Civilizations)
        {
            if (!w.IsAlive || w.Id == breaker)
            {
                continue;
            }

            int percent = w.Id == victim ? 100 : content.Treaties.WitnessPercent;
            Relation? r = w.Relation(breaker);
            if (percent <= 0 || r is not { HasMet: true } || (w.Id != victim && w.Relation(victim) is not { HasMet: true }))
            {
                continue;
            }

            Remember(content, state, r, RelationEvent.BrokenTreaty, percent);
        }
    }

    /// <summary>Adds a one-time memory of <paramref name="kind"/> at <paramref name="percent"/> of its strength.</summary>
    public static void Remember(GameContent content, GameState state, Relation r, RelationEvent kind, int percent = 100)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(r);
        RelationEventDefinition def = content.Relations.Events[(int)kind];
        r.Remember(
            new RelationMemory
            {
                Kind = kind,
                Sympathy = def.Sympathy * percent / 100,
                Trust = def.Trust * percent / 100,
                EndMonth = state.Month,
                FadeMonths = Math.Max(1, def.Years * 12),
            },
            content.Relations.MaxMemories,
            state.Month);
        r.Recount(state.Month);
    }

    // Removes every treaty between the two; true when one of them bound against war.
    private static bool EndTreaties(GameContent content, TreatyBook book, CivId x, CivId y)
    {
        bool broke = false;
        for (int k = 0; k < content.Treaties.Kinds.Count; k++)
        {
            var kind = (TreatyKind)k;
            if (kind != TreatyKind.War && book.Remove(x, y, kind))
            {
                broke |= content.Treaties.Kinds[k].BindsAgainstWar;
            }
        }

        return broke;
    }

    // The defensive allies of the victim (GDD 9.1): each joins with a chance from its loyalty, or lets its ally down.
    private static void CallAllies(GameContent content, GameState state, CivId aggressor, CivId victim, StringBuilder line)
    {
        TreatyBook book = state.Treaties;
        List<CivId>? allies = null;
        foreach (Treaty t in book.All)
        {
            if (t.Kind == TreatyKind.DefensiveAlliance && t.Involves(victim) && t.Other(victim) != aggressor)
            {
                (allies ??= []).Add(t.Other(victim));
            }
        }

        if (allies is null)
        {
            return;
        }

        CivilizationState v = state.FindCivilization(victim)!;
        foreach (CivId ally in allies)
        {
            CivilizationState? z = state.FindCivilization(ally);
            if (z is not { IsAlive: true } || book.AtWar(ally, aggressor))
            {
                continue;
            }

            // An ally of both sides stays out without shame.
            if (book.Has(ally, aggressor, TreatyKind.DefensiveAlliance))
            {
                continue;
            }

            int baseChance = content.Treaties.AllyJoinBasePerMille;
            int chance = baseChance + ((1000 - baseChance) * z.Trait(Ai.Trait.Loyalty) / 1000);
            DeterministicRandom rng = state.Random.Derive(RandomStreamId.Ai, AllySalt | ((ulong)(uint)ally.Value << 16) | (uint)aggressor.Value, state.Turn);
            Relation? fromVictim = v.Relation(ally);
            if (rng.NextInt(1000) < chance)
            {
                EndTreaties(content, book, ally, aggressor);
                book.Add(new Treaty(ally, aggressor, TreatyKind.War, ally, state.Month));
                if (fromVictim is not null)
                {
                    Remember(content, state, fromVictim, RelationEvent.WarHelp);
                }

                line.Append(CultureInfo.InvariantCulture, $" {z.Name} joined the war at the side of its ally.");
            }
            else
            {
                book.Remove(ally, victim, TreatyKind.DefensiveAlliance);
                if (fromVictim is not null)
                {
                    Remember(content, state, fromVictim, RelationEvent.BrokenTreaty);
                }

                line.Append(CultureInfo.InvariantCulture, $" {z.Name} let its ally down.");
            }
        }
    }

    private static string Name(GameState state, CivId civ) => state.FindCivilization(civ)?.Name ?? civ.ToString();
}
