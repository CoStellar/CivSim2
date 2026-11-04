using System;
using CivSim.Core.Random;
using CivSim.Core.State;

namespace CivSim.Core.Hashing;

/// <summary>Computes the determinism hash of a whole <see cref="GameState"/>.</summary>
public static class GameStateHash
{
    /// <summary>
    /// Hashes every piece of state that affects the future of the game, in id order. Two runs with the same
    /// seed and commands must produce the same value on every platform. Who controls a civilization (AI or a
    /// player) is session data, not game state, so it is not hashed.
    /// </summary>
    public static ulong Compute(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var h = new StateHasher();

        h.Add(state.Settings.Seed);
        h.Add(state.Settings.MapWidth);
        h.Add(state.Settings.MapHeight);
        h.Add(state.Settings.CivilizationCount);
        h.Add((int)state.Settings.WorldType);
        h.Add(state.Settings.WaterPercent);
        h.Add(state.Turn);

        Span<RandomState> rng = stackalloc RandomState[RandomStreams.Count];
        state.Random.CopyStates(rng);
        foreach (RandomState r in rng)
        {
            h.Add(r.S0);
            h.Add(r.S1);
            h.Add(r.S2);
            h.Add(r.S3);
        }

        h.Add(state.Worlds.Count);
        foreach (WorldState w in state.Worlds)
        {
            h.Add(w.Id.Value);
            h.Add(w.Name);
            h.Add(w.Width);
            h.Add(w.Height);
            foreach (Map.IMapLayerInfo layer in w.Map.Layers)
            {
                h.Add(layer.Name);
                h.Add(layer.AsBytes());
            }
        }

        h.Add(state.Civilizations.Count);
        foreach (CivilizationState c in state.Civilizations)
        {
            h.Add(c.Id.Value);
            h.Add(c.Name);
            h.Add(c.IsAlive);
            h.Add(c.StartCell.Col);
            h.Add(c.StartCell.Row);
        }

        return h.Value;
    }
}
