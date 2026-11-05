using System;
using CivSim.Core.Content;
using CivSim.Core.Numerics;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Economy;

/// <summary>
/// Last step of the economy phase (GDD 11.1, phase 7): goods spoil by their yearly rate converted to the turn
/// length, then everything above the warehouse capacity is lost (GDD 4.3, CORE simplified: one capacity per
/// settlement). Selling the surplus at a discount comes with the trade network (Stage 3e).
/// </summary>
public sealed class StorageSystem : ITurnSystem
{
    private readonly GameContent _content;
    private readonly long[] _keptShare;
    private readonly long[] _weights;
    private readonly Amount[] _losses;
    private int _cachedMonths = -1;

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
        RefreshShares(state.MonthsPerTurn(_content));
        long spoiled = 0;
        long overflow = 0;
        foreach (SettlementState s in state.Settlements)
        {
            Span<Amount> stock = s.Stock;
            for (int g = 0; g < stock.Length; g++)
            {
                if (stock[g] > Amount.Zero && _keptShare[g] != Spoilage.One)
                {
                    Amount left = Spoilage.Apply(stock[g], _keptShare[g]);
                    spoiled += (stock[g] - left).Hundredths;
                    stock[g] = left;
                }
            }

            overflow += TrimToCapacity(s, _content.SettlementLevels[s.Level].StorageCapacity);
        }

        LastSpoiledHundredths = spoiled;
        LastOverflowHundredths = overflow;
    }

    // Removes the surplus from every good in proportion to how much of it is stored.
    private long TrimToCapacity(SettlementState s, Amount capacity)
    {
        Amount total = s.TotalStock();
        if (total <= capacity)
        {
            return 0;
        }

        Span<Amount> stock = s.Stock;
        for (int g = 0; g < stock.Length; g++)
        {
            _weights[g] = stock[g].Hundredths;
        }

        Amount excess = total - capacity;
        excess.SplitProportionally(_weights, _losses);
        for (int g = 0; g < stock.Length; g++)
        {
            stock[g] -= _losses[g];
        }

        return excess.Hundredths;
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
