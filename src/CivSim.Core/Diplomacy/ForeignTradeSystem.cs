using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Economy;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Diplomacy;

/// <summary>
/// Trade between civilizations (GDD 9.2, CORE simplified: merchants as a flow between networks, without caravans;
/// stage 4d). Every trade agreement links the two nearest settlements of the partners (the gateways, at most
/// <see cref="TreatyRules.ReachCells"/> apart). Each side offers part of its gateway's surplus of the goods the other
/// side's network prices higher than its own, after the transport loss (2% per cell, as in a network), and only as
/// much as the other gateway lacks. It is barter at both sides' internal prices (GDD 4.6): the side offering more
/// worth sends less, so both give the same worth at the mean price. A turn with goods exchanged keeps the memory of
/// active trade going (GDD 8.5); trade partners also spread technologies and epidemics (research, events).
/// </summary>
public sealed class ForeignTradeSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private long[] _sendA = [];
    private long[] _sendB = [];
    private long[] _spareA = [];
    private long[] _spareB = [];
    private int[] _networkOf = [];
    private readonly SettlementsByCiv _byCiv = new();

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Economy;

    /// <summary>Worth of all goods exchanged between civilizations in the last turn, hundredths (for reports).</summary>
    public long LastWorth { get; private set; }

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        LastWorth = 0;
        List<List<int>>? networks = null;
        int months = state.MonthsPerTurn(_content);
        bool sorted = false;
        foreach (Treaty t in state.Treaties.All)
        {
            if (t.Kind != TreatyKind.Trade)
            {
                continue;
            }

            t.LastWorth = 0;
            if (!sorted)
            {
                _byCiv.Build(state);
                sorted = true;
            }

            int ia = state.IndexOfCivilization(t.A);
            int ib = state.IndexOfCivilization(t.B);
            int ga = -1;
            int gb = -1;
            int distance = ia < 0 || ib < 0 ? int.MaxValue : _byCiv.Nearest(state, ia, ib, out ga, out gb);
            if (distance <= _content.Treaties.ReachCells)
            {
                networks ??= Networks(state);
                t.GatewayA = state.Settlements[ga].Id.Value;
                t.GatewayB = state.Settlements[gb].Id.Value;
                t.LastWorth = Exchange(state, networks, ga, gb, distance, months);
            }

            if (t.LastWorth > 0)
            {
                t.LastFlowMonth = state.Month;
                t.TotalWorth += t.LastWorth;
                LastWorth += t.LastWorth;
            }

            Keep(state, t.A, t.B, t.LastWorth > 0, months);
            Keep(state, t.B, t.A, t.LastWorth > 0, months);
        }
    }

    /// <summary>
    /// What trade between two gateway settlements would be worth to both sides, percent of the worth of the goods
    /// that change hands (0 when there is nothing to exchange): the mean gap between the two sides' prices.
    /// </summary>
    public static int GainPercent(GameContent content, Amount[] pricesA, Amount[] pricesB, int arrivingPercent)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(pricesA);
        ArgumentNullException.ThrowIfNull(pricesB);
        long gap = 0;
        long worth = 0;
        for (int g = 0; g < content.Goods.Count; g++)
        {
            long pa = pricesA[g].Hundredths;
            long pb = pricesB[g].Hundredths;
            long gain = Math.Max((pb * arrivingPercent / 100) - pa, (pa * arrivingPercent / 100) - pb);
            if (gain > 0)
            {
                gap += gain;
                worth += (pa + pb) / 2;
            }
        }

        return worth <= 0 ? 0 : (int)Math.Min(1000, gap * 100 / worth);
    }

    private List<List<int>> Networks(GameState state)
    {
        List<List<int>> networks = TradeNetworks.Build(_content, state);
        if (_networkOf.Length < state.Settlements.Count)
        {
            _networkOf = new int[state.Settlements.Count];
        }

        Array.Fill(_networkOf, -1);
        for (int n = 0; n < networks.Count; n++)
        {
            foreach (int m in networks[n])
            {
                _networkOf[m] = n;
            }
        }

        return networks;
    }

    // One turn of barter between the gateways (indices into the settlements); returns the worth exchanged.
    private long Exchange(GameState state, List<List<int>> networks, int ia, int ib, int distance, int months)
    {
        SettlementState a = state.Settlements[ia];
        SettlementState b = state.Settlements[ib];
        if (_networkOf[ia] < 0 || _networkOf[ib] < 0)
        {
            return 0;
        }

        Amount[] pa = Market.Prices(_content, state, networks[_networkOf[ia]]);
        Amount[] pb = Market.Prices(_content, state, networks[_networkOf[ib]]);
        int arriving = TradeNetworks.ArrivingPercent(_content, distance);
        int goods = _content.Goods.Count;
        if (_sendA.Length != goods)
        {
            _sendA = new long[goods];
            _sendB = new long[goods];
            _spareA = new long[goods];
            _spareB = new long[goods];
        }

        Spare(a, months, _spareA);
        Spare(b, months, _spareB);
        Offer(a, b, pa, pb, arriving, months, _spareA, _sendA);
        Offer(b, a, pb, pa, arriving, months, _spareB, _sendB);
        long worthA = Worth(_sendA, pa, pb);
        long worthB = Worth(_sendB, pa, pb);
        if (worthA == 0 && worthB == 0)
        {
            return 0;
        }

        // Barter: the side that gets more pays the rest with other spare goods the partner prices no lower.
        if (worthA > worthB)
        {
            worthB += Pay(pb, pa, _spareB, _sendB, _sendA, worthA - worthB);
        }
        else if (worthB > worthA)
        {
            worthA += Pay(pa, pb, _spareA, _sendA, _sendB, worthB - worthA);
        }

        if (worthA <= 0 || worthB <= 0)
        {
            return 0;
        }

        // What cannot be paid for is not sent: both give the same worth.
        long[] scaled = worthA > worthB ? _sendA : _sendB;
        long more = Math.Max(worthA, worthB);
        long less = Math.Min(worthA, worthB);
        for (int g = 0; g < goods; g++)
        {
            scaled[g] = (long)((Int128)scaled[g] * less / more);
        }

        Move(a, b, _sendA, arriving);
        Move(b, a, _sendB, arriving);
        return Worth(_sendA, pa, pb) + Worth(_sendB, pa, pb);
    }

    // What a gateway can spare of every good, hundredths: food above this turn's needs and the reserve (shared over
    // the foods by stock), other goods above a year of needs and recipe inputs.
    private void Spare(SettlementState s, int months, long[] spare)
    {
        long need = Demand.FoodPerYear(_content, s);
        long keep = (need * months / 12) + (need * _content.Trade.FoodReserveYears.Hundredths / 100);
        long food = FoodStock(s);
        long foodSpare = Math.Max(0, food - keep);
        ReadOnlySpan<Amount> stock = s.StockView;
        for (int g = 0; g < spare.Length; g++)
        {
            long have = g < stock.Length ? stock[g].Hundredths : 0;
            spare[g] = _content.Goods[g].IsFood
                ? (food <= 0 ? 0 : (long)((Int128)have * foodSpare / food))
                : Math.Max(0, have - Demand.NeedPerYear(_content, s, g) - Demand.RecipeInput(_content, s, g, 12));
        }
    }

    // What `from` offers `to`: a share of its spare goods `to` values more after the loss, up to what `to` lacks.
    private void Offer(SettlementState from, SettlementState to, Amount[] pFrom, Amount[] pTo, int arriving, int months, long[] spare, long[] send)
    {
        Array.Clear(send);
        long need = Demand.FoodPerYear(_content, to);
        long toKeep = (need * months / 12) + (need * _content.Trade.FoodReserveYears.Hundredths / 100);
        long toWantFood = Math.Max(0, toKeep - FoodStock(to)) * 100 / Math.Max(1, arriving);
        int share = _content.Treaties.TradeSharePercent;
        ReadOnlySpan<Amount> theirs = to.StockView;
        for (int g = 0; g < send.Length; g++)
        {
            if (pTo[g].Hundredths * arriving <= pFrom[g].Hundredths * 100)
            {
                continue;
            }

            long amount;
            if (_content.Goods[g].IsFood)
            {
                amount = Math.Min(spare[g] * share / 100, toWantFood);
                toWantFood -= amount;
            }
            else
            {
                long have = g < theirs.Length ? theirs[g].Hundredths : 0;
                long want = Demand.NeedPerYear(_content, to, g) + Demand.RecipeInput(_content, to, g, 12) - have;
                amount = Math.Min(spare[g] * share / 100, Math.Max(0, want) * 100 / Math.Max(1, arriving));
            }

            send[g] = Math.Max(0, amount);
        }
    }

    // The payer adds spare goods (not already traded either way) the partner prices at least as high, in good order,
    // up to `owed` worth at the mean price; returns the worth added.
    private long Pay(Amount[] pPayer, Amount[] pPartner, long[] spare, long[] send, long[] theirs, long owed)
    {
        int share = _content.Treaties.TradeSharePercent;
        long paid = 0;
        for (int g = 0; g < send.Length && paid < owed; g++)
        {
            long mean = (pPayer[g].Hundredths + pPartner[g].Hundredths) / 2;
            if (send[g] > 0 || theirs[g] > 0 || mean <= 0 || pPartner[g] < pPayer[g])
            {
                continue;
            }

            long amount = Math.Min(spare[g] * share / 100, ((owed - paid) * 100 / mean) + 1);
            send[g] = amount;
            paid += amount * mean / 100;
        }

        return paid;
    }

    private long FoodStock(SettlementState s)
    {
        long food = 0;
        ReadOnlySpan<Amount> stock = s.StockView;
        for (int g = 0; g < stock.Length && g < _content.Goods.Count; g++)
        {
            food += _content.Goods[g].IsFood ? stock[g].Hundredths : 0;
        }

        return food;
    }

    // Worth of the goods at the mean of both sides' prices, hundredths.
    private static long Worth(long[] send, Amount[] pa, Amount[] pb)
    {
        long worth = 0;
        for (int g = 0; g < send.Length; g++)
        {
            worth += send[g] * ((pa[g].Hundredths + pb[g].Hundredths) / 2) / 100;
        }

        return worth;
    }

    private static void Move(SettlementState from, SettlementState to, long[] send, int arriving)
    {
        for (int g = 0; g < send.Length; g++)
        {
            if (send[g] > 0 && from.TryTake(g, Amount.FromHundredths(send[g])))
            {
                to.Add(g, Amount.FromHundredths(send[g] * arriving / 100));
            }
        }
    }

    // GDD 8.5: active trade grows sympathy and trust while goods flow, and fades once they stop.
    private void Keep(GameState state, CivId civ, CivId partner, bool flowed, int months)
    {
        Relation? r = state.FindCivilization(civ)?.Relation(partner);
        if (r is null)
        {
            return;
        }

        if (flowed)
        {
            Contacts.Sustain(_content.Relations, r, RelationEvent.ActiveTrade, state.Month, months);
        }
        else
        {
            Contacts.End(r, RelationEvent.ActiveTrade, state.Month);
        }
    }
}
