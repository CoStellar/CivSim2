using System;
using System.Collections.Generic;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Kinds of treaties.</summary>
public enum TreatyKind
{
    /// <summary>Trade agreement: barter is possible and research is cheaper.</summary>
    Trade = 0,

    /// <summary>Alliance: an ally joins a war declared on its partner. Needs a trade agreement.</summary>
    Alliance = 1,
}

/// <summary>Offer a treaty to another people.</summary>
public sealed record ProposeTreatyCommand(int Civ, int Other, TreatyKind Kind) : SimCommand(Civ);

/// <summary>Answer a treaty offer.</summary>
public sealed record AnswerTreatyCommand(int Civ, int From, TreatyKind Kind, bool Accept) : SimCommand(Civ);

/// <summary>End a treaty.</summary>
public sealed record CancelTreatyCommand(int Civ, int Other, TreatyKind Kind) : SimCommand(Civ);

/// <summary>Offer goods for goods to a trade partner (amounts in hundredths).</summary>
public sealed record BarterCommand(int Civ, int Other, int GiveGood, long GiveAmount, int WantGood, long WantAmount) : SimCommand(Civ);

/// <summary>Answer a barter offer.</summary>
public sealed record AnswerBarterCommand(int Civ, int From, bool Accept) : SimCommand(Civ);

/// <summary>A treaty offer waiting for an answer.</summary>
public sealed record TreatyOffer(int From, int To, TreatyKind Kind);

/// <summary>A barter offer waiting for an answer (from a person; the AI answers at once).</summary>
public sealed record BarterOffer(int From, int To, int GiveGood, long GiveAmount, int WantGood, long WantAmount);

public sealed partial class SimGame
{
    private bool[]? _trade;
    private bool[]? _alliance;

    /// <summary>Treaty offers waiting for an answer.</summary>
    public List<TreatyOffer> TreatyOffers { get; } = [];

    /// <summary>Barter offers waiting for a person's answer.</summary>
    public List<BarterOffer> BarterOffers { get; } = [];

    /// <summary>True when the two peoples have the treaty.</summary>
    public bool Has(int a, int b, TreatyKind kind)
    {
        bool[] t = Treaties(kind);
        return a != b && t[(a * Civs.Count) + b];
    }

    /// <summary>Number of trade partners of a people.</summary>
    public int Partners(int civ)
    {
        int n = 0;
        for (int o = 0; o < Civs.Count; o++)
        {
            n += Has(civ, o, TreatyKind.Trade) ? 1 : 0;
        }

        return n;
    }

    private bool[] Treaties(TreatyKind kind) => kind == TreatyKind.Trade
        ? _trade ??= new bool[Civs.Count * Civs.Count]
        : _alliance ??= new bool[Civs.Count * Civs.Count];

    private void SetTreaty(int a, int b, TreatyKind kind, bool on)
    {
        bool[] t = Treaties(kind);
        t[(a * Civs.Count) + b] = on;
        t[(b * Civs.Count) + a] = on;
        if (kind == TreatyKind.Trade && !on)
        {
            SetTreaty(a, b, TreatyKind.Alliance, false);
        }
    }

    private string? ProposeTreaty(Civ civ, int other, TreatyKind kind)
    {
        if ((uint)other >= (uint)Civs.Count || other == civ.Id || Civs[other].Dead || (!Met(civ, Civs[other]) && !Met(Civs[other], civ)))
        {
            return "We have not met this people.";
        }

        if (AtWar(civ.Id, other))
        {
            return "Not while we are at war.";
        }

        if (Has(civ.Id, other, kind))
        {
            return "We already have this treaty.";
        }

        if (kind == TreatyKind.Alliance && !Has(civ.Id, other, TreatyKind.Trade))
        {
            return "An alliance needs a trade agreement first.";
        }

        TreatyOffers.RemoveAll(o => o.From == civ.Id && o.To == other && o.Kind == kind);
        TreatyOffers.Add(new TreatyOffer(civ.Id, other, kind));
        News.Add((other, $"{civ.Name} offer us {(kind == TreatyKind.Trade ? "a trade agreement" : "an alliance")}."));
        return null;
    }

    private string? AnswerTreaty(Civ civ, int from, TreatyKind kind, bool accept)
    {
        TreatyOffer? offer = TreatyOffers.Find(o => o.From == from && o.To == civ.Id && o.Kind == kind);
        if (offer is null)
        {
            return "There is no such offer.";
        }

        TreatyOffers.Remove(offer);
        string what = kind == TreatyKind.Trade ? "trade agreement" : "alliance";
        if (!accept || AtWar(from, civ.Id))
        {
            News.Add((from, $"{civ.Name} refused our {what}."));
            return null;
        }

        SetTreaty(from, civ.Id, kind, true);
        NewsAbout(from, civ.Id, $"{Civs[from].Name} and {civ.Name} signed a{(kind == TreatyKind.Alliance ? "n" : "")} {what}.");
        return null;
    }

    private string? CancelTreaty(Civ civ, int other, TreatyKind kind)
    {
        if ((uint)other >= (uint)Civs.Count || !Has(civ.Id, other, kind))
        {
            return "We have no such treaty.";
        }

        SetTreaty(civ.Id, other, kind, false);
        News.Add((other, $"{civ.Name} ended our {(kind == TreatyKind.Trade ? "trade agreement" : "alliance")}."));
        return null;
    }

    /// <summary>Worth of an amount of a good (percent of food × hundredths).</summary>
    public long Worth(int good, long amount) => amount * Content.Goods[good].WorthPercent / 100;

    private string? Barter(Civ civ, int other, int give, long giveAmount, int want, long wantAmount)
    {
        if ((uint)other >= (uint)Civs.Count || !Has(civ.Id, other, TreatyKind.Trade))
        {
            return "Barter needs a trade agreement.";
        }

        if ((uint)give >= (uint)civ.Stock.Length || (uint)want >= (uint)civ.Stock.Length || giveAmount <= 0 || wantAmount <= 0 || give == want)
        {
            return "Offer some of one good for some of another.";
        }

        if (civ.Stock[give] < giveAmount)
        {
            return $"We have only {civ.Stock[give] / SimContent.One} {Content.Goods[give].Key}.";
        }

        Civ partner = Civs[other];
        if (!partner.IsAi)
        {
            BarterOffers.RemoveAll(o => o.From == civ.Id && o.To == other);
            BarterOffers.Add(new BarterOffer(civ.Id, other, give, giveAmount, want, wantAmount));
            News.Add((other, $"{civ.Name} offer {giveAmount / SimContent.One} {Content.Goods[give].Key} for {wantAmount / SimContent.One} {Content.Goods[want].Key}."));
            return null;
        }

        // The AI takes what is worth enough to it and leaves it at least twice what it gives.
        bool fair = Worth(give, giveAmount) * 100 >= Worth(want, wantAmount) * Content.AcceptWorthPercent;
        if (!fair || partner.Stock[want] < wantAmount * 2)
        {
            return $"{partner.Name} turn the offer down.";
        }

        Exchange(civ, partner, give, giveAmount, want, wantAmount);
        return null;
    }

    private string? AnswerBarter(Civ civ, int from, bool accept)
    {
        BarterOffer? offer = BarterOffers.Find(o => o.From == from && o.To == civ.Id);
        if (offer is null)
        {
            return "There is no such offer.";
        }

        BarterOffers.Remove(offer);
        Civ seller = Civs[from];
        if (!accept)
        {
            News.Add((from, $"{civ.Name} turned our offer down."));
            return null;
        }

        if (seller.Stock[offer.GiveGood] < offer.GiveAmount || civ.Stock[offer.WantGood] < offer.WantAmount)
        {
            return "One side no longer has the goods.";
        }

        Exchange(seller, civ, offer.GiveGood, offer.GiveAmount, offer.WantGood, offer.WantAmount);
        return null;
    }

    private void Exchange(Civ a, Civ b, int give, long giveAmount, int want, long wantAmount)
    {
        a.Stock[give] -= giveAmount;
        b.Stock[give] += giveAmount;
        b.Stock[want] -= wantAmount;
        a.Stock[want] += wantAmount;
        string text = $"{a.Name} gave {giveAmount / SimContent.One} {Content.Goods[give].Key} to {b.Name} for {wantAmount / SimContent.One} {Content.Goods[want].Key}.";
        News.Add((a.Id, text));
        News.Add((b.Id, text));
    }

    // A war ends the treaties between the two; the allies of the attacked join.
    private void TreatiesOnWar(int attacker, int victim)
    {
        SetTreaty(attacker, victim, TreatyKind.Trade, false);
        for (int c = 0; c < Civs.Count; c++)
        {
            if (c != attacker && c != victim && !Civs[c].Dead && Has(c, victim, TreatyKind.Alliance) && !AtWar(c, attacker))
            {
                SetTreaty(c, attacker, TreatyKind.Trade, false);
                SetWar(c, attacker, true);
                NewsAbout(c, attacker, $"{Civs[c].Name} stand by their ally {Civs[victim].Name} and go to war with {Civs[attacker].Name}.");
            }
        }
    }
}
