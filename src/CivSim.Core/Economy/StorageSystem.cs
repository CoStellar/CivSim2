using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Economy;

/// <summary>
/// Last step of the economy phase (GDD 11.1, phase 7): goods spoil by their yearly rate converted to the turn
/// length, then everything above the warehouse capacity is lost, the biggest piles first (GDD 4.3, CORE simplified: one capacity per
/// settlement). A settlement in a trade network first sends the surplus to members with room, nearest first, losing
/// the transport share on the way (stage 3e, decision of 2026-11-05: without money there is no selling at a discount).
/// </summary>
public sealed class StorageSystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly long[] _keptShare;
    private readonly long[] _weights;
    private readonly Amount[] _losses;
    private readonly List<long> _order = [];
    private int _cachedMonths = -1;
    private readonly Dictionary<long, long[]> _modified = [];

    /// <summary>Creates the system.</summary>
    public StorageSystem(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _content = content;
        _keptShare = new long[content.Goods.Count];
        _weights = new long[content.Goods.Count];
        _losses = new Amount[content.Goods.Count];
    }

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Economy;

    /// <summary>Goods lost to spoilage in the last turn, all settlements together (hundredths). For reports.</summary>
    public long LastSpoiledHundredths { get; private set; }

    /// <summary>Goods lost above warehouse capacity in the last turn (hundredths). For reports.</summary>
    public long LastOverflowHundredths { get; private set; }

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        long spoiled = 0;
        long overflow = 0;
        foreach (SettlementState s in state.Settlements)
        {
            int months = WorldClock.MonthsOf(state, s);
            if (months == 0)
            {
                continue; // its civilization waits for its own step (ADR 0018)
            }

            RefreshShares(months);
            // Pots and granaries (spoilage_percent on the civilization) slow spoiling.
            int slower = state.Effects.Count == 0
                ? 0
                : state.Effects.Total(_content.Effects, EffectStat.SpoilagePercent, EffectTarget.Civilization, WorldId.Earth, s.Owner.Value);
            long[] kept = slower == 0 ? _keptShare : KeptShares(slower, months);
            Span<Amount> stock = s.Stock;
            for (int g = 0; g < stock.Length; g++)
            {
                if (stock[g] > Amount.Zero && kept[g] != Spoilage.One)
                {
                    Amount left = Spoilage.Apply(stock[g], kept[g]);
                    spoiled += (stock[g] - left).Hundredths;
                    stock[g] = left;
                }
            }

        }

        // Surplus first goes to members of the same network that have room.
        var networkOf = new int[state.Settlements.Count];
        Array.Fill(networkOf, -1);
        List<List<int>> networks = TradeNetworks.Build(_content, state);
        for (int n = 0; n < networks.Count; n++)
        {
            foreach (int m in networks[n])
            {
                networkOf[m] = n;
            }
        }

        // Free room per network: when every member is full there is nowhere to send anything.
        var room = new long[networks.Count];
        for (int n = 0; n < networks.Count; n++)
        {
            foreach (int m in networks[n])
            {
                SettlementState member = state.Settlements[m];
                room[n] += Math.Max(0, (_content.SettlementLevels[member.Level].StorageCapacity - member.TotalStock()).Hundredths);
            }
        }

        for (int i = 0; i < state.Settlements.Count; i++)
        {
            SettlementState s = state.Settlements[i];
            if (WorldClock.MonthsOf(state, s) == 0)
            {
                continue;
            }

            Amount capacity = _content.SettlementLevels[s.Level].StorageCapacity;
            int net = networkOf[i];
            if (net >= 0 && room[net] > 0 && networks[net].Count > 1 && s.TotalStock() > capacity)
            {
                room[net] -= SendSurplus(state, networks[net], i, capacity);
            }

            overflow += TrimToCapacity(s, capacity);
        }

        LastSpoiledHundredths = spoiled;
        LastOverflowHundredths = overflow;
    }

    // Sends the surplus, in proportion to what is stored, to network members with free room, nearest first.
    // Returns the room used at the receivers, hundredths.
    private long SendSurplus(GameState state, List<int> members, int from, Amount capacity)
    {
        long used = 0;
        SettlementState s = state.Settlements[from];
        WorldState world = state.Worlds[s.World.Value];
        if (s.TotalStock() <= capacity)
        {
            return 0;
        }

        // Nearest first, ties by index: each distance is computed once and packed with the index into one sort key.
        _order.Clear();
        foreach (int m in members)
        {
            if (m != from)
            {
                _order.Add(((long)world.Geometry.Distance(s.Cell, state.Settlements[m].Cell) << 32) | (uint)m);
            }
        }

        _order.Sort();
        foreach (long key in _order)
        {
            int m = (int)(key & 0xFFFFFFFF);
            Amount excess = s.TotalStock() - capacity;
            if (excess <= Amount.Zero)
            {
                return used;
            }

            SettlementState to = state.Settlements[m];
            Amount room = _content.SettlementLevels[to.Level].StorageCapacity - to.TotalStock();
            if (room <= Amount.Zero)
            {
                continue;
            }

            int percent = TradeNetworks.InternalArrivingPercent(_content, world.Geometry.Distance(s.Cell, to.Cell));
            long sendMax = room.Hundredths * 100 / Math.Max(1, percent);
            Amount send = Amount.FromHundredths(Math.Min(excess.Hundredths, sendMax));
            Span<Amount> stock = s.Stock;
            for (int g = 0; g < stock.Length; g++)
            {
                _weights[g] = stock[g].Hundredths;
            }

            send.SplitProportionally(_weights, _losses);
            for (int g = 0; g < stock.Length; g++)
            {
                if (_losses[g] > Amount.Zero)
                {
                    stock[g] -= _losses[g];
                    long arrive = _losses[g].Hundredths * percent / 100;
                    to.Add(g, Amount.FromHundredths(arrive));
                    used += arrive;
                }
            }
        }

        return used;
    }

    // Removes the surplus from the biggest piles first, down to an even level (decision of Patryk, 2026-11-17, playtest P-11:
    // cut from every good alike, a camp full of hides lost its wood, flint and food every turn, its work groups starved and were
    // sent out again and again).
    private long TrimToCapacity(SettlementState s, Amount capacity)
    {
        Amount total = s.TotalStock();
        if (total <= capacity)
        {
            return 0;
        }

        Amount excess = total - capacity;
        LevelDown(s, excess.Hundredths);
        return excess.Hundredths;
    }

    // Takes `excess` hundredths, the biggest piles first: every pile above a level comes down to it, the
    // level found so that exactly `excess` goes (the last hundredths from piles at the level, in good order).
    private void LevelDown(SettlementState s, long excess)
    {
        if (excess <= 0)
        {
            return;
        }

        Span<Amount> stock = s.Stock;
        long low = 0;
        long high = 0;
        for (int g = 0; g < stock.Length; g++)
        {
            high = Math.Max(high, stock[g].Hundredths);
        }

        // The lowest level at which the piles above it hold no more than the excess.
        while (low < high)
        {
            long mid = low + ((high - low) / 2);
            if (Above(stock, mid) <= excess)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        long left = excess - Above(stock, low);
        for (int g = 0; g < stock.Length; g++)
        {
            if (stock[g].Hundredths > low)
            {
                stock[g] = Amount.FromHundredths(low);
            }
        }

        for (int g = 0; g < stock.Length && left > 0; g++)
        {
            if (stock[g].Hundredths == low && low > 0)
            {
                stock[g] = Amount.FromHundredths(low - 1);
                left--;
            }
        }
    }

    // Hundredths of all goods above `level`.
    private long Above(Span<Amount> stock, long level)
    {
        long sum = 0;
        for (int g = 0; g < stock.Length; g++)
        {
            if (stock[g].Hundredths > level)
            {
                sum += stock[g].Hundredths - level;
            }
        }

        return sum;
    }

    // Kept shares with spoilage changed by `percent`, cached per (percent, turn length).
    private long[] KeptShares(int percent, int months)
    {
        long key = ((long)percent << 32) | (uint)months;
        if (!_modified.TryGetValue(key, out long[]? kept))
        {
            kept = new long[_keptShare.Length];
            for (int g = 0; g < kept.Length; g++)
            {
                long rate = Math.Max(0, (long)_content.Goods[g].SpoilageBasisPointsPerYear * (100 + percent) / 100);
                kept[g] = Spoilage.KeptShare((int)rate, months);
            }

            _modified[key] = kept;
        }

        return kept;
    }

    private void RefreshShares(int months)
    {
        if (months == _cachedMonths)
        {
            return;
        }

        for (int g = 0; g < _keptShare.Length; g++)
        {
            _keptShare[g] = Spoilage.KeptShare(_content.Goods[g].SpoilageBasisPointsPerYear, months);
        }

        _cachedMonths = months;
    }
}
