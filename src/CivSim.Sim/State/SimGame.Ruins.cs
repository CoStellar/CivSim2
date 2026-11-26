using System;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>
/// Ruins (Patryk, 2026-11-19, G2): a settlement nobody lives in for ruins.emptySeconds falls to ruin. Its land is nobody's and its
/// buildings stand ruined; settlers who stand on or next to its centre take it again for their people, and its buildings come
/// back as sites needing ruins.rebuildPercent of their price. The last settlement of a civilization never falls to ruin.
/// </summary>
public sealed partial class SimGame
{
    // Once a second per civilization.
    private void RuinStep(Civ civ)
    {
        if (DieOut(civ))
        {
            return;
        }

        for (int k = civ.Settlements.Count - 1; k >= 0; k--)
        {
            Settlement s = Settlements[civ.Settlements[k]];
            if (s.People > 0 || civ.Settlements.Count <= 1)
            {
                s.EmptySince = -1;
                continue;
            }

            if (s.EmptySince < 0)
            {
                s.EmptySince = Ticks;
            }
            else if (Ticks - s.EmptySince >= Content.RuinEmptySeconds * SimContent.TicksPerSecond)
            {
                Ruin(civ, s);
            }
        }
    }

    // P5 (Patryk, 2026-11-23): a civilization with nobody left, in its settlements or in its units, has lost: its empty settlements
    // fall to ruin and the civilization is no more. While a unit lives, the people goes on (it can join a settlement and start again).
    private bool DieOut(Civ civ)
    {
        if (civ.Dead || civ.Settlements.Count == 0)
        {
            return false;
        }

        foreach (int sid in civ.Settlements)
        {
            if (Settlements[sid].People > 0)
            {
                return false;
            }
        }

        foreach (int id in civ.Units)
        {
            if (Units[id].State != UnitState.Gone && Units[id].People > 0)
            {
                return false;
            }
        }

        for (int k = civ.Settlements.Count - 1; k >= 0; k--)
        {
            Ruin(civ, Settlements[civ.Settlements[k]], silent: true);
        }

        Perish(civ, null);
        return true;
    }

    private void Ruin(Civ civ, Settlement s, bool silent = false)
    {
        Unwatch(s);
        s.Abandoned = true;
        s.EmptySince = -1;
        civ.Settlements.Remove(s.Id);
        DisownWorks(s.Cells, civ.Id); // A6 (tests 2026-11-20): walls of ruins belong to nobody
        foreach (int cell in s.Cells)
        {
            OwnerAt[cell] = -1;
            Map.Owner.Set(cell, -1);
            SettlementAt[cell] = -1;
            Map.MarkCellDirty(cell);
        }

        s.Cells.Clear();
        foreach (Building b in Buildings)
        {
            if (b.Settlement == s.Id && b.State != BuildingState.Removed)
            {
                civ.Buildings.Remove(b.Id);
                b.State = BuildingState.Ruined;
                b.Workers = 0;
                b.Priority = false;
            }
        }

        // Units that called it home go to the civilization's first settlement.
        foreach (int id in civ.Units)
        {
            if (Units[id].Home == s.Id && civ.Settlements.Count > 0)
            {
                Units[id].Home = civ.Settlements[0];
            }
        }

        civ.Dirty = true;
        s.Guard = 0;
        if (!silent)
        {
            News.Add((civ.Id, $"{s.Name} stood empty and fell to ruin. Settlers can take it again."));
        }
    }

    /// <summary>The ruin whose centre is the cell or next to it, if any.</summary>
    public Settlement? RuinNear(int cell)
    {
        foreach (Settlement s in Settlements)
        {
            if (s.Abandoned && Distance(s.Center, cell) <= 1)
            {
                return s;
            }
        }

        return null;
    }

    // Settlers take a ruin: its land again, its buildings as sites to rebuild.
    private void Revive(Civ civ, Settlement s, int people, long[]? cargo)
    {
        int former = s.Civ;
        s.Civ = civ.Id;
        s.Abandoned = false;
        s.People = people;
        if (cargo is not null)
        {
            for (int g = 0; g < cargo.Length && g < s.Stock.Length; g++)
            {
                s.Stock[g] += Math.Max(0, cargo[g]);
            }
        }

        civ.Settlements.Add(s.Id);
        Claim(s);
        AdoptWorks(s, -1);
        foreach (Building b in Buildings)
        {
            if (b.Settlement != s.Id || b.State != BuildingState.Ruined)
            {
                continue;
            }

            BuildingType t = Content.Buildings[b.Type];
            b.Civ = civ.Id;
            b.State = BuildingState.Delivery;
            b.RemainingTicks = Math.Max(1, t.BuildTicks);
            b.Delivered = new long[Content.Goods.Count];
            b.OnTheWay = new long[Content.Goods.Count];
            civ.Buildings.Add(b.Id);
            var standing = new long[Content.Goods.Count];
            for (int g = 0; g < t.Price.Length; g++)
            {
                standing[g] = t.Price[g] * (100 - Content.RuinRebuildPercent) / 100; // what still stands of it
            }

            Deliver(b, standing);
            SendMaterials(b);
        }

        Watch(s);
        civ.Dirty = true;
        SyncTotals(civ);
        NewsAbout(civ.Id, former, $"{civ.Name} settled the ruins of {s.Name}.");
    }
}
