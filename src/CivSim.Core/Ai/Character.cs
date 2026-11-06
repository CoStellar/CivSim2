using System;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Ai;

/// <summary>The eight traits of a civilization's character (GDD 8.4). Values are part of the save format: only append.</summary>
public enum Trait : byte
{
    /// <summary>Readiness to take by force.</summary>
    Aggression = 0,

    /// <summary>Urge to spread over new land.</summary>
    Expansiveness = 1,

    /// <summary>Interest in exchange and wealth.</summary>
    Tradefulness = 2,

    /// <summary>Interest in knowledge.</summary>
    Curiosity = 3,

    /// <summary>Openness to strangers.</summary>
    Openness = 4,

    /// <summary>Caution: a cautious civilization almost always takes its best option.</summary>
    Caution = 5,

    /// <summary>Vengefulness: how long wrongs are remembered (stage 4c).</summary>
    Vengefulness = 6,

    /// <summary>Loyalty to allies (stage 4d).</summary>
    Loyalty = 7,
}

/// <summary>Rolls and reads civilization character (GDD 8.4; stage 4b).</summary>
public static class Character
{
    /// <summary>Number of traits.</summary>
    public const int TraitCount = (int)Trait.Loyalty + 1;

    /// <summary>Number of goals.</summary>
    public const int GoalCount = (int)Goal.Conquest + 1;

    /// <summary>A trait value in the middle of the scale, per mille.</summary>
    public const int Average = 500;

    /// <summary>
    /// Rolls the traits of every civilization of a new game, 0–1000 each, leaning to the middle (the mean of two
    /// draws). The generator is derived from the seed, the leaders stream and the civilization, so no other stream moves.
    /// </summary>
    public static void Roll(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (CivilizationState civ in state.Civilizations)
        {
            DeterministicRandom random = state.Random.Derive(RandomStreamId.Leaders, (ulong)civ.Id.Value, 0);
            var traits = new int[TraitCount];
            for (int t = 0; t < traits.Length; t++)
            {
                traits[t] = (random.NextInt(1001) + random.NextInt(1001)) / 2;
            }

            civ.Traits = traits;
        }
    }

    /// <summary>The trait that makes a civilization like actions serving <paramref name="goal"/>.</summary>
    public static Trait TraitFor(Goal goal) => goal switch
    {
        Goal.Expansion => Trait.Expansiveness,
        Goal.Security => Trait.Caution,
        Goal.Prosperity => Trait.Tradefulness,
        Goal.Science => Trait.Curiosity,
        Goal.Influence => Trait.Openness,
        _ => Trait.Aggression,
    };

    /// <summary>
    /// The character multiplier for actions serving <paramref name="goal"/>, per mille: 500 + the trait, so 0.5 for a
    /// civilization without that trait and 1.5 for one full of it (GDD 8.3 allows 0.5–2).
    /// </summary>
    public static int Multiplier(CivilizationState civ, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return 500 + civ.Trait(TraitFor(goal));
    }
}
