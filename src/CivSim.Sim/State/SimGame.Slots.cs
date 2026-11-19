using System;
using System.Collections.Generic;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// Four places on a hex (decision of 2026-11-18): a small village has a house, a workshop and a herbalist side by side. A
/// building takes 1, 2 (a row or a column) or all 4 places. <see cref="BuildingAt"/> keeps the first building of a hex for the
/// code that asks only whether something stands there.
/// </summary>
public sealed partial class SimGame
{
    // Place sets a building of each size may take, in the order they are tried: rows before columns.
    private static readonly int[][] SlotSets = [[], [1, 2, 4, 8], [3, 12, 5, 10], [], [15]];

    /// <summary>Places still free on the hex, as bits.</summary>
    public int FreeSlots(int cell)
    {
        int free = 0;
        for (int k = 0; k < SlotsPerHex; k++)
        {
            if (SlotAt[(cell * SlotsPerHex) + k] < 0)
            {
                free |= 1 << k;
            }
        }

        return free;
    }

    /// <summary>Places a building of this size would take on the hex (0 = no room).</summary>
    public int SlotsFor(int cell, int size)
    {
        int free = FreeSlots(cell);
        foreach (int set in SlotSets[Math.Clamp(size, 1, 4) is 3 ? 4 : Math.Clamp(size, 1, 4)])
        {
            if ((free & set) == set)
            {
                return set;
            }
        }

        return 0;
    }

    /// <summary>The buildings on a hex, each once, in the order of their first place.</summary>
    public List<int> BuildingsOn(int cell)
    {
        var list = new List<int>(SlotsPerHex);
        for (int k = 0; k < SlotsPerHex; k++)
        {
            int b = SlotAt[(cell * SlotsPerHex) + k];
            if (b >= 0 && !list.Contains(b))
            {
                list.Add(b);
            }
        }

        return list;
    }

    private void PutOnHex(Building b, int slots)
    {
        b.Slots = slots;
        for (int k = 0; k < SlotsPerHex; k++)
        {
            if ((slots & (1 << k)) != 0)
            {
                SlotAt[(b.Cell * SlotsPerHex) + k] = b.Id;
            }
        }

        if (BuildingAt[b.Cell] < 0)
        {
            BuildingAt[b.Cell] = b.Id;
        }
    }

    // A building leaves its hex: its places are free again.
    private void TakeOffHex(Building b)
    {
        for (int k = 0; k < SlotsPerHex; k++)
        {
            int i = (b.Cell * SlotsPerHex) + k;
            if (SlotAt[i] == b.Id)
            {
                SlotAt[i] = -1;
            }
        }

        List<int> left = BuildingsOn(b.Cell);
        BuildingAt[b.Cell] = left.Count > 0 ? left[0] : -1;
    }

    /// <summary>Puts a finished building straight on a hex (tests and start-up).</summary>
    internal void PlaceForTest(Building b)
    {
        int slots = SlotsFor(b.Cell, Content.Buildings[b.Type].Size);
        PutOnHex(b, slots == 0 ? 15 : slots);
    }
}
