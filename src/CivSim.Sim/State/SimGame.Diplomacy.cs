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

    /// <summary>
    /// Trade agreements a people can keep: one per finished trading post, without limit once it knows Currency (barter is
    /// limited at first, decision of 2026-11-18).
    /// </summary>
    public int TradeRoom(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        int currency = Content.Techs.Count;
        for (int t = 0; t < Content.Techs.Count; t++)
        {
            currency = Content.Techs[t].Key == "currency" ? t : currency;
        }

        if (currency < Content.Techs.Count && civ.KnownTech[currency])
        {
            return int.MaxValue;
        }

        int posts = 0;
        foreach (int id in civ.Buildings)
        {
            Building b = Buildings[id];
            posts += b.State == BuildingState.Active && Array.IndexOf(Content.Buildings[b.Type].Groups, "trade") >= 0 ? 1 : 0;
        }

        return posts;
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

    private long[]? _offerAt;
    private long[]? _metSince;
    private long[]? _peaceSince;

    /// <summary>
    /// The conditions for a treaty between two peoples (decision of 2026-11-17): known for a while, settlements close,
    /// no fighting lately, and a good name.
    /// </summary>
    public bool TreatyConditions(Civ civ, Civ other)
    {
        ArgumentNullException.ThrowIfNull(civ);
        ArgumentNullException.ThrowIfNull(other);
        if (other.Id == civ.Id || other.Dead || AtWar(civ.Id, other.Id) || other.Reputation <= Content.TreatyMinReputation)
        {
            return false;
        }

        int pair = (civ.Id * Civs.Count) + other.Id;
        _metSince ??= new long[Civs.Count * Civs.Count];
        bool met = Met(civ, other) || Met(other, civ);
        if (!met)
        {
            _metSince[pair] = 0;
            return false;
        }

        if (_metSince[pair] == 0)
        {
            _metSince[pair] = Ticks;
        }

        if (Ticks - _metSince[pair] < Content.TreatyKnownTicks)
        {
            return false;
        }

        _peaceSince ??= new long[Civs.Count * Civs.Count];
        if (_peaceSince[pair] > 0 && Ticks - _peaceSince[pair] < Content.TreatyPeaceTicks)
        {
            return false;
        }

        (Settlement a, Settlement b) = ClosestPair(civ, other);
        int gap = Geo.Distance(Geo.FromIndex(a.Center), Geo.FromIndex(b.Center)) - Content.Levels[a.Level].Radius - Content.Levels[b.Level].Radius;
        return gap <= Content.TreatyBorderHexes;
    }

    // Remembers when a war ended (no treaty for a while after).
    private void PeaceMade(int a, int b)
    {
        _peaceSince ??= new long[Civs.Count * Civs.Count];
        _peaceSince[(a * Civs.Count) + b] = Ticks;
        _peaceSince[(b * Civs.Count) + a] = Ticks;
    }

    private long[] Offers() => _offerAt ??= new long[Civs.Count * Civs.Count];

    /// <summary>Tick from which <paramref name="from"/> may offer <paramref name="to"/> a treaty again.</summary>
    public long LastOffer(int from, int to) => Offers()[(from * Civs.Count) + to];

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

        if (kind == TreatyKind.Trade && Content.TradeTech >= 0 && !civ.KnownTech[Content.TradeTech])
        {
            return $"Trade agreements need {Content.Techs[Content.TradeTech].Name}.";
        }

        if (kind == TreatyKind.Trade && Partners(civ.Id) >= TradeRoom(civ))
        {
            return $"Every trade agreement needs a trading post of ours (we have {TradeRoom(civ)}, used by {Partners(civ.Id)}).";
        }

        long last = LastOffer(civ.Id, other);
        if (Ticks < last)
        {
            return $"We offered them something not long ago; wait {(last - Ticks) / SimContent.TicksPerSecond} s.";
        }

        Offers()[(civ.Id * Civs.Count) + other] = Ticks + Content.OfferCooldownTicks;

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
        if (accept && kind == TreatyKind.Trade && Partners(civ.Id) >= TradeRoom(civ))
        {
            return $"Every trade agreement needs a trading post of ours (we have {TradeRoom(civ)}).";
        }
        if (!accept || AtWar(from, civ.Id))
        {
            Offers()[(from * Civs.Count) + civ.Id] = Ticks + Content.RefusalCooldownTicks;
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

    /// <summary>True when caravans can walk between the two peoples' nearest settlements.</summary>
    public bool Reachable(Civ a, Civ b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Settlements.Count == 0 || b.Settlements.Count == 0)
        {
            return false;
        }

        (Settlement sa, Settlement sb) = ClosestPair(a, b);
        return FindPath(sa.Center, sb.Center, _probe, false, sa.Civ) || SeaWay(a, b);
    }

    private readonly List<int> _probe = [];

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
            return $"We have only {civ.Stock[give] / SimContent.One} {GoodText(give)}.";
        }

        Civ partner = Civs[other];
        if (!Reachable(civ, partner))
        {
            return $"No way on land leads to {partner.Name}, and no sea way between our harbours: goods cannot reach them.";
        }

        if (!partner.IsAi)
        {
            BarterOffers.RemoveAll(o => o.From == civ.Id && o.To == other);
            BarterOffers.Add(new BarterOffer(civ.Id, other, give, giveAmount, want, wantAmount));
            News.Add((other, $"{civ.Name} offer {giveAmount / SimContent.One} {GoodText(give)} for {wantAmount / SimContent.One} {GoodText(want)}."));
            return null;
        }

        // The AI takes what is worth enough to it (fair worth once it knows currency) and keeps at least twice what it gives.
        int wantWorth = partner.KnownTech[Content.TechId("currency")] ? 100 : Content.AcceptWorthPercent;
        bool fair = Worth(give, giveAmount) * 100 >= Worth(want, wantAmount) * wantWorth;
        if (!fair || partner.Stock[want] < wantAmount * 2)
        {
            return $"{partner.Name} turn the offer down.";
        }

        TradeCaravans(civ, partner, give, giveAmount, want, wantAmount);
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

        TradeCaravans(seller, civ, offer.GiveGood, offer.GiveAmount, offer.WantGood, offer.WantAmount);
        return null;
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
