using System;
using System.Collections.Generic;
using CivSim.Core.Map;

namespace CivSim.Sim.State;

/// <summary>What a people remembers of a settlement it no longer sees.</summary>
public sealed record SettlementMemory(int Settlement, int Civ, string Name, int Level, int Center);

/// <summary>What a people remembers of a building it no longer sees.</summary>
public sealed record BuildingMemory(int Building, int Type, int Civ, BuildingState State, int Cell);

/// <summary>
/// The fog of war (decisions of 2026-11-18): every hex is unknown, remembered or seen now. Each people counts its observers
/// per hex: its units (by their sight) and settlements (their land and a few hexes beyond). A hex is seen while its count is
/// above zero. When the last observer leaves, the people remembers what was there (buildings, settlements, owner) and sees it
/// grey until it looks again, like Age of Empires. Moving one unit changes only the hexes of its old and new sight.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Hexes a settlement sees beyond its own land.</summary>
    public const int SettlementSightBeyond = 3;

    /// <summary>True when <paramref name="civ"/> sees the hex now (always true while the start is being chosen).</summary>
    public bool Visible(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return Picking ? civ.Known[cell] : civ.Vision[cell] > 0;
    }

    /// <summary>Owner of a hex as the people knows it: live where seen, remembered elsewhere.</summary>
    public short OwnerSeen(Civ civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return Visible(civ, cell) ? OwnerAt[cell] : civ.SeenOwner[cell];
    }

    /// <summary>Puts a unit on a hex at once, with its sight (tests and scripted scenes).</summary>
    internal void Place(Unit u, int cell)
    {
        u.Cell = cell;
        Watch(u);
    }

    // A unit starts or moves on: its sight goes with it.
    private void Watch(Unit u)
    {
        Unwatch(u);
        if (u.State is UnitState.Gone or UnitState.Training)
        {
            return;
        }

        u.WatchCell = u.Cell;
        u.WatchRadius = Content.Units[u.Kind].Sight;
        See(Civs[u.Civ], u.WatchCell, u.WatchRadius, +1);
    }

    // The unit stops seeing (gone, or about to see from elsewhere).
    private void Unwatch(Unit u)
    {
        if (u.WatchCell >= 0)
        {
            See(Civs[u.Civ], u.WatchCell, u.WatchRadius, -1);
            u.WatchCell = -1;
        }
    }

    // A settlement sees its land and a few hexes beyond; called when founded, raised or taken.
    private void Watch(Settlement s)
    {
        Unwatch(s);
        if (s.Abandoned)
        {
            return;
        }

        s.WatchCiv = s.Civ;
        s.WatchRadius = Content.Levels[s.Level].Radius + SettlementSightBeyond;
        See(Civs[s.Civ], s.Center, s.WatchRadius, +1);
    }

    private void Unwatch(Settlement s)
    {
        if (s.WatchCiv >= 0)
        {
            See(Civs[s.WatchCiv], s.Center, s.WatchRadius, -1);
            s.WatchCiv = -1;
        }
    }

    // Adds or removes one observer over a disc of hexes.
    private void See(Civ civ, int center, int radius, int delta)
    {
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(radius)];
        int n = Geo.Range(Geo.FromIndex(center), radius, area);
        for (int k = 0; k < n; k++)
        {
            int i = Geo.Index(area[k]);
            int before = civ.Vision[i];
            int after = before + delta;
            civ.Vision[i] = (short)Math.Max(0, after);
            if (before == 0 && after > 0)
            {
                // Seen now: explored for good; the memory of the hex gives way to what is there.
                civ.Known[i] = true;
                Forget(civ, i);
                civ.NewlyKnown.Add(Map.Chunks.ChunkOfIndex(i));
            }
            else if (before > 0 && after <= 0)
            {
                Remember(civ, i);
                civ.NewlyKnown.Add(Map.Chunks.ChunkOfIndex(i));
            }
        }
    }

    // The last observer left the hex: keep what was there.
    private void Remember(Civ civ, int cell)
    {
        civ.SeenOwner[cell] = OwnerAt[cell];
        int b = BuildingAt[cell];
        if (b >= 0 && Buildings[b].State != BuildingState.Removed)
        {
            Building x = Buildings[b];
            civ.SeenBuildings[cell] = new BuildingMemory(x.Id, x.Type, x.Civ, x.State, cell);
        }
        else
        {
            civ.SeenBuildings.Remove(cell);
        }

        int sid = SettlementAt[cell];
        if (sid >= 0 && Settlements[sid].Center == cell && !Settlements[sid].Abandoned)
        {
            Settlement s = Settlements[sid];
            civ.SeenSettlements[cell] = new SettlementMemory(s.Id, s.Civ, s.Name, s.Level, cell);
        }
        else
        {
            civ.SeenSettlements.Remove(cell);
        }
    }

    private static void Forget(Civ civ, int cell)
    {
        civ.SeenBuildings.Remove(cell);
        civ.SeenSettlements.Remove(cell);
    }

    /// <summary>Remembered buildings of a people, in the order of their hexes.</summary>
    public IEnumerable<BuildingMemory> RememberedBuildings(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.SeenBuildings.Values;
    }

    /// <summary>Remembered settlements of a people.</summary>
    public IEnumerable<SettlementMemory> RememberedSettlements(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        return civ.SeenSettlements.Values;
    }
}
