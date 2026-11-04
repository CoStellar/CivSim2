using System.Collections.Generic;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Protocol.Messages;

namespace CivSim.Server;

/// <summary>
/// Builds the view one player may see. Today every civilization's name is public; fog of war and map memory
/// (Stage 3) will filter here, on the server, so the client never receives hidden information.
/// </summary>
public static class ViewBuilder
{
    /// <summary>Creates the view of <paramref name="viewer"/> from a finished snapshot.</summary>
    public static GameViewMessage Build(GameSnapshot snapshot, CivId viewer)
    {
        var worlds = new List<WorldView>(snapshot.Worlds.Length);
        foreach (WorldSnapshot w in snapshot.Worlds)
        {
            worlds.Add(new WorldView(w.Id.Value, w.Name, w.Width, w.Height));
        }

        var civs = new List<CivilizationView>(snapshot.Civilizations.Length);
        foreach (CivilizationSnapshot c in snapshot.Civilizations)
        {
            civs.Add(new CivilizationView(c.Id.Value, c.Name, c.IsAlive));
        }

        return new GameViewMessage(snapshot.Turn, viewer.Value, worlds, civs);
    }
}
