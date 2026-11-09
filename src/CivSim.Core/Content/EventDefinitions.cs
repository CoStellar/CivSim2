using System.Collections.Generic;

namespace CivSim.Core.Content;

/// <summary>What a condition of an event looks at (GDD 10.2: the chance of an event follows the state of the world).</summary>
public enum EventConditionKind
{
    /// <summary>Humidity of the settlement's cell is below <see cref="EventCondition.Value"/>.</summary>
    HumidityBelow,

    /// <summary>Humidity of the settlement's cell is above <see cref="EventCondition.Value"/>.</summary>
    HumidityAbove,

    /// <summary>A river runs along an edge of the settlement's cell.</summary>
    River,

    /// <summary>One of <see cref="EventCondition.Biomes"/> lies within <see cref="EventCondition.Value"/> cells.</summary>
    BiomeNear,

    /// <summary>The settlement has at least <see cref="EventCondition.Value"/> people.</summary>
    PeopleAtLeast,

    /// <summary>The settlement has settled down (its level cannot move).</summary>
    Settled,

    /// <summary>
    /// The settlement's owner has at least <see cref="EventCondition.Value"/> people in all its settlements. An event
    /// that requires it never strikes a smaller civilization, not even by spreading.
    /// </summary>
    CivilizationPeopleAtLeast,
}

/// <summary>
/// One condition of an event. When it holds, the chance is multiplied by <see cref="Percent"/>% (0 rules the event
/// out); a <see cref="Required"/> condition that does not hold rules the event out.
/// </summary>
public sealed record EventCondition(EventConditionKind Kind, int Value, IReadOnlyList<int> Biomes, int Percent, bool Required);

/// <summary>A technology that softens an event: the strength of every effect drops by <see cref="Percent"/>%.</summary>
public sealed record EventSoftening(int Technology, int Percent);

/// <summary>
/// An effect an event lays (GDD 10.1). The effect type decides the target: a settlement effect falls on the struck
/// settlement, a civilization effect on its owner, a cell effect on every land cell within <see cref="RadiusCells"/>.
/// </summary>
public sealed record EventEffect(int Effect, int Strength, int Years, int RadiusCells);

/// <summary>
/// A random event (GDD 10.2, <c>data/events.json</c>, stage 3h). Every turn each lived-in settlement may be struck,
/// with a yearly chance shaped by its conditions. A struck settlement gets the effects, nearby land may get more
/// fertile (a flood), and the event may spread to the other settlements of its trade network (an epidemic).
/// </summary>
public sealed record EventDefinition(
    int Id,
    string Key,
    string Name,
    int TimesPer10000Years,
    IReadOnlyList<EventCondition> Conditions,
    IReadOnlyList<EventSoftening> SoftenedBy,
    IReadOnlyList<EventEffect> Effects,
    int FertilityGain,
    int FertilityRadius,
    int SpreadPercent,
    string Chronicle);
