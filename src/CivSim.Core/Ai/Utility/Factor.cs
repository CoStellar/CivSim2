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
