using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;
using CivSim.Sim.State;

namespace CivSim.Sim.Ai;

// The AI's diplomacy: answers to peace offers, treaty offers and what a treaty gains (split from SimAi.cs, 2026-11-22).
public static partial class SimAi
{
    // Answers treaty offers at once. Offers a treaty only when every condition of docs/sim holds (known long enough, close,
    // no recent fighting, good name, and something to gain); the cooldowns live in the treaty rules themselves.
    private static void Diplomacy(SimGame g, Civ civ)
    {
        foreach (TreatyOffer offer in g.TreatyOffers.ToArray())
        {
            if (offer.To != civ.Id)
            {
                continue;
            }

            Civ from = g.Civs[offer.From];
            bool accept = from.Reputation > g.Content.TreatyMinReputation && Interest(g, civ, from, offer.Kind) &&
                (offer.Kind == TreatyKind.Trade ? civ.Aggression < 85 : civ.Aggression < 70 && from.Reputation >= 0);
            g.Apply(new AnswerTreatyCommand(civ.Id, offer.From, offer.Kind, accept));
        }

        foreach (Civ other in g.Civs)
        {
            if (!g.TreatyConditions(civ, other) || g.Ticks < g.LastOffer(civ.Id, other.Id))
            {
                continue;
            }

            if (!g.Has(civ.Id, other.Id, TreatyKind.Trade) && civ.Aggression < 70 && Interest(g, civ, other, TreatyKind.Trade))
            {
                g.Apply(new ProposeTreatyCommand(civ.Id, other.Id, TreatyKind.Trade));
                return;
            }

            if (g.Has(civ.Id, other.Id, TreatyKind.Trade) && !g.Has(civ.Id, other.Id, TreatyKind.Alliance) && civ.Aggression < 50
                && other.Reputation >= 0 && Interest(g, civ, other, TreatyKind.Alliance))
            {
                g.Apply(new ProposeTreatyCommand(civ.Id, other.Id, TreatyKind.Alliance));
                return;
            }
        }
    }

    // What the AI gains: trade when the other makes a good it lacks, or knows a craft it does not (seen in their land);
    // an alliance when both fight the same enemy.
    private static bool Interest(SimGame g, Civ civ, Civ other, TreatyKind kind)
    {
        if (kind == TreatyKind.Alliance)
        {
            foreach (Civ third in g.Civs)
            {
                if (third.Id != civ.Id && third.Id != other.Id && g.AtWar(civ.Id, third.Id) && g.AtWar(other.Id, third.Id))
                {
                    return true;
                }
            }

            return false;
        }

        foreach (int id in other.Buildings)
        {
            Building b = g.Buildings[id];
            if (!civ.Known[b.Cell] || b.State != BuildingState.Active)
            {
                continue;
            }

            BuildingType t = g.Content.Buildings[b.Type];
            if (!SimGame.Knows(civ, t))
            {
                return true;
            }

            for (int i = 0; i < t.Produces.Length; i++)
            {
                if (t.Produces[i] > 0 && civ.Rate[i] <= 0 && civ.Stock[i] < 20 * SimContent.One)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void AnswerPeace(SimGame g, Civ civ)
    {
        foreach (PeaceOffer offer in g.PeaceOffers.ToArray())
        {
            if (offer.To != civ.Id)
            {
                continue;
            }

            g.Apply(new AnswerPeaceCommand(civ.Id, offer.From, AcceptsPeace(g, civ, offer)));
            continue;
        }
    }

    /// <summary>
    /// 5A (ADR 0030): the AI values the terms in worth and takes them when they are no worse than the course of the war asks,
    /// with up to peace.aiGiveMarginPercent more give the more peaceful it is; 3B: a long peace is welcome while it loses,
    /// a short one while it wins. E1: it gives anything only after real losses or when weary.
    /// </summary>
    internal static bool AcceptsPeace(SimGame g, Civ civ, PeaceOffer offer)
    {
        PeaceRules r = g.Content.Peace;
        long fair = g.FairWorth(civ.Id, offer.From); // what the proposer should give us (negative: we should give)
        long value = offer.Terms.IsWhite ? 0 : (offer.Terms.WeGive ? 1 : -1) * g.TermsWorth(offer.Terms);
        int length = Math.Clamp(offer.Terms.Length, 0, r.Minutes.Length - 1);
        int margin = r.AiGiveMarginPercent * (100 - civ.Aggression) / 100;
        int[] lengthPct = fair < 0 ? r.AiLosingLengthPercent : r.AiWinningLengthPercent;
        margin += length < lengthPct.Length ? lengthPct[length] : 0;
        if (value < 0)
        {
            bool hurt = g.Score(offer.From, civ.Id) >= g.Content.War.TributeNeedsKills || g.Weariness(civ.Id, offer.From) >= g.Content.Weariness.AiPeaceAt;
            if (!hurt)
            {
                return false;
            }
        }

        // We take terms worth at least what the war asks, less our margin of that (or of a tenth of the offer when the war asks nothing).
        long basis = Math.Max(Math.Abs(fair), Math.Abs(value) / 10);
        return value >= fair - (basis * margin / 100);
    }
}
