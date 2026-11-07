namespace CivSim.Core.Ai.Utility;

/// <summary>
/// What a factor of an AI evaluation measures (GDD 8.3). Names show up in the "Why?" record of a decision, so they
/// describe the world, not the code. Values are part of decision logs: only append.
/// </summary>
public enum FactorId : byte
{
    /// <summary>Nothing (an empty slot).</summary>
    None = 0,

    /// <summary>Food per year the land of a place gives.</summary>
    Food = 1,

    /// <summary>How pleasant the temperature of a place is for the civilization.</summary>
    Comfort = 2,

    /// <summary>How near a place is (far places are worth less).</summary>
    Distance = 3,

    /// <summary>An old campsite of the tribe: known land.</summary>
    OwnCampsite = 4,

    /// <summary>How well the camp was fed last turn.</summary>
    Fed = 5,

    /// <summary>How much better the new place is than the old one.</summary>
    BetterThanHere = 6,

    /// <summary>Grain the land would give by farming against what the people eat.</summary>
    FarmYield = 7,

    /// <summary>How full the camp is against the most people its level holds.</summary>
    Crowding = 8,

    /// <summary>People in the camp.</summary>
    People = 9,

    /// <summary>Goods left in an empty settlement nearby.</summary>
    LeftGoods = 10,

    /// <summary>Own warriors against the defenders of a camp.</summary>
    Strength = 11,

    /// <summary>Value of the outputs of a recipe minus its inputs at internal prices.</summary>
    ValueAdded = 12,

    /// <summary>Whether the trade network has the inputs of a recipe.</summary>
    InputsAvailable = 13,

    /// <summary>Research points a technology costs today.</summary>
    ResearchCost = 14,

    /// <summary>Weight the strategist gives to the goal the action serves (stage 4b; neutral until then).</summary>
    GoalWeight = 15,

    /// <summary>Multiplier from the civilization's character (stage 4b; neutral until then).</summary>
    Character = 16,

    /// <summary>The place lies closer to the strategist's expansion target (GDD 7.6).</summary>
    Direction = 17,

    /// <summary>Sympathy for the people living at the place (GDD 8.5; stage 4c), points from −100 to 100.</summary>
    Opinion = 18,

    /// <summary>Trust in the other civilization (GDD 8.5; stage 4d), points from −100 to 100.</summary>
    Trust = 19,

    /// <summary>Known warriors of a rival against ours, percent (stage 4d).</summary>
    Threat = 20,

    /// <summary>What trade would gain both sides, percent of the goods' worth (stage 4d).</summary>
    TradeGain = 21,

    /// <summary>A reason for war: a disputed settlement or an attack to avenge (GDD 9.3: no war without one).</summary>
    Cause = 22,

    /// <summary>A treaty that war would break: loyalty holds the civilization back (stage 4d).</summary>
    Treaty = 23,

    /// <summary>Years the war has lasted (war weariness until stage 4e).</summary>
    WarYears = 24,

    /// <summary>The two civilizations are at war (stage 4d).</summary>
    War = 25,

    /// <summary>The goal the largest part of the worth serves (its <see cref="Ai.Goal"/> number).</summary>
    ServesGoal = 26,

    /// <summary>Share of the worth of the best technology this one opens the way to (PR B).</summary>
    LeadsTo = 27,

    /// <summary>Goods of this kind go to a trade partner (production for exchange, PR B).</summary>
    ForTrade = 28,

    /// <summary>What farming would grow against what the camp gathers now, % (settling, PR C).</summary>
    OverGathering = 29,

    /// <summary>Fertility of a village's fields against their natural fertility, % (PR C).</summary>
    WornLand = 30,

    /// <summary>Unknown land cells around a place (explorers, PR C).</summary>
    Unknown = 31,

    /// <summary>Our war weariness, 0..100 (stage 4e).</summary>
    Weariness = 32,

    /// <summary>What the terms of a peace are worth to us, war score points (stage 4e).</summary>
    Terms = 33,

    /// <summary>Chance to win a fight as the commander sees it, percent (stage 4e).</summary>
    WinChance = 34,
}

/// <summary>
/// One factor of an evaluation: its multiplier in per mille (1000 = neutral, 0 = rules the action out, above 1000 = a
/// boost) and the raw world value it came from, for the "Why?" record.
/// </summary>
/// <param name="Id">What the factor measures.</param>
/// <param name="PerMille">Multiplier in thousandths; <see cref="NoteOnly"/> for a factor that only explains.</param>
/// <param name="Input">The world value behind it (food in hundredths, cells, people...).</param>
public readonly record struct Factor(FactorId Id, int PerMille, long Input)
{
    /// <summary>Marks a factor that is recorded to explain a rule but does not change the score.</summary>
    public const int NoteOnly = -1;

    /// <summary>The neutral multiplier: 1.0.</summary>
    public const int Neutral = 1000;

    /// <summary>
    /// How much the factor shaped the decision: its distance from neutral. Notes count 1: they rank after every
    /// multiplier that moved the score, but before multipliers that changed nothing.
    /// </summary>
    public int Weight => PerMille == NoteOnly ? 1 : System.Math.Abs(PerMille - Neutral);
}
