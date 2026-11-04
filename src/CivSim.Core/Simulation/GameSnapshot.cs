using System.Collections.Immutable;
using CivSim.Core.State;

namespace CivSim.Core.Simulation;

/// <summary>
/// Immutable picture of the game after a turn. Readers on other threads (server views, viewer) use only this,
/// never the live <see cref="GameState"/>.
/// </summary>
public sealed record GameSnapshot(long Turn, ImmutableArray<WorldSnapshot> Worlds, ImmutableArray<CivilizationSnapshot> Civilizations)
{
    /// <summary>Copies the public parts of <paramref name="state"/>.</summary>
    public static GameSnapshot From(GameState state)
    {
        var worlds = ImmutableArray.CreateBuilder<WorldSnapshot>(state.Worlds.Count);
        var changed = new System.Collections.Generic.List<int>();
        foreach (WorldState w in state.Worlds)
        {
            changed.Clear();
            w.Map.Dirty.CopyTo(changed);
            worlds.Add(new WorldSnapshot(w.Id, w.Name, w.Width, w.Height, [.. changed]));
        }

        var civs = ImmutableArray.CreateBuilder<CivilizationSnapshot>(state.Civilizations.Count);
        foreach (CivilizationState c in state.Civilizations)
        {
            civs.Add(new CivilizationSnapshot(c.Id, c.Name, c.Controller, c.IsAlive));
        }

        return new GameSnapshot(state.Turn, worlds.MoveToImmutable(), civs.MoveToImmutable());
    }
}

/// <summary>
/// Immutable copy of a world's public data. <paramref name="ChangedChunks"/> lists the map chunks that changed
/// in the turn (ascending ids), so readers can redraw or resend only those.
/// </summary>
public sealed record WorldSnapshot(WorldId Id, string Name, int Width, int Height, ImmutableArray<int> ChangedChunks);

/// <summary>Immutable copy of a civilization's public data.</summary>
public sealed record CivilizationSnapshot(CivId Id, string Name, PlayerId Controller, bool IsAlive);
