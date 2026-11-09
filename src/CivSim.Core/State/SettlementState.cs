using System;
using CivSim.Core.Map;
using CivSim.Core.Numerics;

namespace CivSim.Core.State;

/// <summary>What has become of a settlement (GDD 5.5, CORE simplified: two states after abandonment).</summary>
public enum SettlementStatus
{
    /// <summary>People live here.</summary>
    Active = 0,

    /// <summary>Empty, buildings standing; anyone whose land it is can move in at once or take what was left.</summary>
    Abandoned = 1,

    /// <summary>Empty and falling apart; it can still be resettled or plundered, until it disappears.</summary>
    Ruins = 2,
}

/// <summary>
/// One settlement (GDD 5.3): a camp, village, town... with its own warehouse. Goods exist only here, never in a
/// civilization-wide treasury (GDD 4). An empty settlement is property that decays (GDD 5.5): see <see cref="Status"/>.
/// </summary>
public sealed class SettlementState
{
    private readonly Amount[] _stock;
    private readonly int[] _population;
    private readonly int[] _skilled;
    private readonly int[] _trainees;

    /// <summary>
    /// Creates a settlement with an empty warehouse for <paramref name="goodCount"/> goods and no people in
    /// <paramref name="strataCount"/> strata, and nobody skilled in <paramref name="skillCount"/> skills.
    /// </summary>
    public SettlementState(SettlementId id, CivId owner, WorldId world, HexCoord cell, string name, int level, long foundedTurn, int goodCount, int strataCount = 0, int skillCount = 0)
    {
        _population = new int[strataCount];
        _skilled = new int[skillCount];
        _trainees = new int[skillCount];
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

    /// <summary>People per stratum id (GDD 5.1).</summary>
    public Span<int> Population => _population;

    /// <summary>Read-only view of the people per stratum.</summary>
    public ReadOnlySpan<int> PopulationView => _population;

    /// <summary>On a journey (stage 6 PR 3a): the next cell it will camp on, or -1 when not chosen yet.</summary>
    public int JourneyNext { get; set; } = -1;

    /// <summary>On a journey: ticks the way to <see cref="JourneyNext"/> costs.</summary>
    public int JourneyStepTicks { get; set; }

    /// <summary>On a journey: ticks already spent on the way to <see cref="JourneyNext"/>.</summary>
    public int JourneyProgress { get; set; }

    /// <summary>True while a party of its scouts is out (stage 6 PR 3a).</summary>
    public bool ScoutOut { get; set; }

    /// <summary>Month of action its last scouts came back in, or -1 before any went out (stage 6 PR 3a).</summary>
    public long ScoutBackMonth { get; set; } = -1;

    /// <summary>People who know each skill (stage 4F), index = skill id; they are counted in their stratum too.</summary>
    public Span<int> Skilled => _skilled;

    /// <summary>Read-only view of <see cref="Skilled"/>.</summary>
    public ReadOnlySpan<int> SkilledView => _skilled;

    /// <summary>Apprentices learning each skill (stage 4F), index = skill id; counted in the skill's stratum.</summary>
    public Span<int> Trainees => _trainees;

    /// <summary>Read-only view of <see cref="Trainees"/>.</summary>
    public ReadOnlySpan<int> TraineesView => _trainees;

    /// <summary>All people in the settlement.</summary>
    public int TotalPopulation()
    {
        int total = 0;
        foreach (int p in _population)
        {
            total += p;
        }

        return total;
    }

    /// <summary>Active, abandoned or ruins.</summary>
    public SettlementStatus Status { get; set; }

    /// <summary>True when people live here.</summary>
    public bool IsActive => Status == SettlementStatus.Active;

    /// <summary>Game month in which the settlement was abandoned (meaningful when not active).</summary>
    public long AbandonedMonth { get; set; }

    /// <summary>
    /// Civilization with a claim on this settlement (its former owner), or <see cref="NoClaim"/>. The claim stays after
    /// someone else takes the settlement over, until <see cref="ClaimUntilMonth"/>: that is a border dispute (GDD 8.5).
    /// </summary>
    public int ClaimCiv { get; set; } = NoClaim;

    /// <summary>Last game month of the claim.</summary>
    public long ClaimUntilMonth { get; set; }

    /// <summary>Value of <see cref="ClaimCiv"/> when nobody has a claim.</summary>
    public const int NoClaim = -1;

    /// <summary>True when <see cref="ClaimCiv"/> has a claim at <paramref name="month"/>.</summary>
    public bool HasClaim(long month) => ClaimCiv != NoClaim && month <= ClaimUntilMonth;

    /// <summary>Recipe the settlement's artisans work (index into <c>GameContent.Recipes</c>), or <see cref="NoRecipe"/> (stage 3e).</summary>
    public int RecipeId { get; set; } = NoRecipe;

    /// <summary>Value of <see cref="RecipeId"/> when nothing is made.</summary>
    public const int NoRecipe = -1;

    /// <summary>Cell index a camp is walking to over several turns (stage 3g), or <see cref="NoJourney"/>.</summary>
    public int JourneyTarget { get; set; } = NoJourney;

    /// <summary>Value of <see cref="JourneyTarget"/> when the camp stays.</summary>
    public const int NoJourney = -1;

    /// <summary>Producers who farmed this turn (set by farming before gathering; the others gather).</summary>
    public int Farmers { get; set; }

    /// <summary>Recipe runs done in the last turn, hundredths (for reports and the AI).</summary>
    public long RunsMade { get; set; }

    /// <summary>Game month of the last move (or of founding); the tribe AI waits a while between moves.</summary>
    public long MovedMonth { get; set; }

    /// <summary>Health 0–100 (GDD 5.2, CORE simplified: one value). Set by the population system.</summary>
    public int Health { get; set; } = 100;

    /// <summary>Mood (satisfaction) 0–100; 50 = basic needs met, nothing more.</summary>
    public int Mood { get; set; } = 50;

    /// <summary>Loyalty to the state 0–100 (GDD 7.3; stage 4f PR 3): the owner's stability minus a penalty for the road cost from the capital.</summary>
    public int Loyalty { get; set; } = 50;

    /// <summary>Food gathered in the last turn, in hundredths.</summary>
    public long FoodGathered { get; set; }

    /// <summary>Food the people needed in the last turn, in hundredths.</summary>
    public long FoodNeeded { get; set; }

    /// <summary>Food actually eaten in the last turn, in hundredths.</summary>
    public long FoodEaten { get; set; }

    /// <summary>Share of higher needs met in the last turn, per mille (1000 = all).</summary>
    public int HigherNeedsPerMille { get; set; }

    /// <summary>Share of the people fed in the last turn, per mille (1000 = everyone).</summary>
    public int FedPerMille => FoodNeeded <= 0 ? 1000 : (int)Math.Min(1000, FoodEaten * 1000 / FoodNeeded);

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
