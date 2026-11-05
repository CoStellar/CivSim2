using System;
using CivSim.Core.Map;
using CivSim.Core.Numerics;

namespace CivSim.Core.State;

/// <summary>
/// One settlement (GDD 5.3): a camp, village, town... with its own warehouse. Goods exist only here, never in a
/// civilization-wide treasury (GDD 4). Population, buildings and abandonment are added in later stages.
/// </summary>
public sealed class SettlementState
{
    private readonly Amount[] _stock;

    /// <summary>Creates a settlement with an empty warehouse for <paramref name="goodCount"/> goods.</summary>
    public SettlementState(SettlementId id, CivId owner, WorldId world, HexCoord cell, string name, int level, long foundedTurn, int goodCount)
    {
        Id = id;
        Owner = owner;
        World = world;
        Cell = cell;
        Name = name;
        Level = level;
        FoundedTurn = foundedTurn;
        _stock = new Amount[goodCount];
    }

    /// <summary>Settlement id.</summary>
    public SettlementId Id { get; }

    /// <summary>Civilization that owns the settlement.</summary>
    public CivId Owner { get; set; }

    /// <summary>World the settlement is on.</summary>
    public WorldId World { get; }

    /// <summary>Centre cell.</summary>
    public HexCoord Cell { get; set; }

    /// <summary>Display name.</summary>
    public string Name { get; set; }

    /// <summary>Level id (index into <c>GameContent.SettlementLevels</c>).</summary>
    public int Level { get; set; }

    /// <summary>Turn in which the settlement was founded (0 = start of the game).</summary>
    public long FoundedTurn { get; }

    /// <summary>Goods in the warehouse, index = good id. Never negative.</summary>
    public Span<Amount> Stock => _stock;

    /// <summary>Read-only view of the warehouse.</summary>
    public ReadOnlySpan<Amount> StockView => _stock;

    /// <summary>Sum of all goods in the warehouse.</summary>
    public Amount TotalStock()
    {
        Amount total = Amount.Zero;
        foreach (Amount a in _stock)
        {
            total += a;
        }

        return total;
    }

    /// <summary>Adds <paramref name="amount"/> of a good.</summary>
    public void Add(int goodId, Amount amount)
    {
        if (amount.IsNegative)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Use TryTake to remove goods.");
        }

        _stock[goodId] += amount;
    }

    /// <summary>Removes <paramref name="amount"/> of a good if there is enough. Returns false and changes nothing otherwise.</summary>
    public bool TryTake(int goodId, Amount amount)
    {
        if (amount.IsNegative || _stock[goodId] < amount)
        {
            return false;
        }

        _stock[goodId] -= amount;
        return true;
    }
}
