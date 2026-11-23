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

            // Accept terms no worse than the course of the war suggests, with some give from peaceful characters. E1
            // (2026-11-19): tribute only after real losses (tributeNeedsKills people killed by them).
            int fair = g.SuggestedTribute(offer.From, civ.Id);
            bool hurt = g.Score(offer.From, civ.Id) >= g.Content.War.TributeNeedsKills;
            bool accept = offer.TributePercent <= 0 ? offer.TributePercent <= fair + ((100 - civ.Aggression) / 10)
                : hurt && offer.TributePercent <= fair + ((100 - civ.Aggression) / 10);
            g.Apply(new AnswerPeaceCommand(civ.Id, offer.From, accept));
        }
    }
}
