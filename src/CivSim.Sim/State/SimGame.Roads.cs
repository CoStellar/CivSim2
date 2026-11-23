using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Build a road over drawn hexes (level 1 track, 2 paved road); hexes already that good are skipped.</summary>
public sealed record BuildRoadCommand(int Civ, int[] Cells, int Level) : SimCommand(Civ);

/// <summary>A road being built, hex after hex.</summary>
public sealed class RoadProject
{
    /// <summary>Builder.</summary>
    public int Civ { get; init; }

    /// <summary>Level built.</summary>
    public int Level { get; init; }

    /// <summary>Work built instead of a road (−1 = a road).</summary>
    public int Work { get; init; } = -1;

    /// <summary>Hexes to build, in order.</summary>
    public List<int> Cells { get; } = [];

    /// <summary>Next hex to build (index in <see cref="Cells"/>).</summary>
    public int Next { get; set; }

    /// <summary>Tick the current hex is done.</summary>
    public long DoneTick { get; set; } = -1;

    /// <summary>True when the hex being built was paid for.</summary>
    public bool Paid { get; set; }

    /// <summary>True when all is built or given up.</summary>
    public bool Over => Next >= Cells.Count;
}

/// <summary>
/// Roads are part of the map (decision of 2026-11-18): a level per hex, drawn like rivers and trees. Tracks can be built from
/// the start, paved roads after the Roads technology. Walking on them is cheaper (and on rough land capped), so caravans and
/// armies take them by themselves. The player draws them hex by hex, also into empty land beyond our settlements, to prepare a
/// new village: settlers who found a camp on a road joined to one of our settlements get a caravan of goods from it.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Road level of each hex (0 = none).</summary>
    public byte[] RoadAt { get; private set; } = [];

    /// <summary>Roads being built.</summary>
    public List<RoadProject> RoadProjects { get; } = [];

    private string? BuildRoad(Civ civ, int[] cells, int level)
    {
        if (level < 1 || level > Content.RoadLevels.Count)
        {
            return "No such road.";
        }

        RoadLevel kind = Content.RoadLevels[level - 1];
        if (!civ.KnownTech[kind.Tech])
        {
            return $"A {kind.Name.ToLowerInvariant()} needs {Content.Techs[kind.Tech].Name}.";
        }

        var path = new List<int>();
        string? why = cells.Length == 0 ? "Draw the road first." : DrawnPath(cells[0], cells, false, path);
        if (why is not null)
        {
            return why;
        }

        path.Insert(0, cells[0]);
        var project = new RoadProject { Civ = civ.Id, Level = level };
        foreach (int cell in path)
        {
            if (!civ.Known[cell])
            {
                return "Roads go only over land we know.";
            }

            if (OwnerAt[cell] >= 0 && OwnerAt[cell] != civ.Id)
            {
                return "Not over another civilization's land.";
            }

            Settlement? near = NearestOwn(civ, cell);
            if (near is null || Distance(near.Center, cell) > Content.RoadReach)
            {
                return $"Too far from our settlements (at most {Content.RoadReach} hexes).";
            }

            if (RoadAt[cell] < level)
            {
                project.Cells.Add(cell);
            }
        }

        if (project.Cells.Count == 0)
        {
            return "The road is already there.";
        }

        RoadProjects.Add(project);
        News.Add((civ.Id, $"Road work begins: {project.Cells.Count} hexes of {kind.Name.ToLowerInvariant()}."));
        return null;
    }

    // Once a second: each project pays for its next hex from the nearest settlement (waits while it lacks the goods) and builds it.
    private void RoadStep()
    {
        for (int p = RoadProjects.Count - 1; p >= 0; p--)
        {
            RoadProject r = RoadProjects[p];
            Civ civ = Civs[r.Civ];
            if (civ.Dead || r.Over)
            {
                RoadProjects.RemoveAt(p);
                continue;
            }

            int cell = r.Cells[r.Next];
            WorkKind? work = r.Work >= 0 ? Content.Works[r.Work] : null;
            RoadLevel? kind = work is null ? Content.RoadLevels[r.Level - 1] : null;
            long[] price = work?.Price ?? kind!.Price;
            if (!r.Paid)
            {
                Settlement? payer = NearestOwn(civ, cell);
                if (payer is null || !CanAfford(payer.Stock, price, 100))
                {
                    continue; // waits for goods
                }

                Pay(payer.Stock, price, 100);
                SyncTotals(civ);
                r.Paid = true;
                r.DoneTick = Ticks + Math.Max(1, work?.TicksPerHex ?? kind!.TicksPerHex);
            }

            if (Ticks < r.DoneTick)
            {
                continue;
            }

            if (work is not null)
            {
                if (OwnerAt[cell] == civ.Id)
                {
                    FinishWorkHex(civ, work, cell);
                }
            }
            else if (RoadAt[cell] < r.Level && Content.WalkCost[(Map.Biome[cell] * 3) + Map.Relief[cell]] > 0)
            {
                RoadAt[cell] = (byte)r.Level;
                Map.MarkCellDirty(cell);
                BridgeRoad(civ, cell);
            }

            r.Next++;
            r.Paid = false;
            if (r.Over)
            {
                News.Add((civ.Id, $"The {(work?.Name ?? kind!.Name).ToLowerInvariant()} is built."));
            }
        }
    }

    // Walking cost on a road: a share of the hex's cost, capped so that roads cross rough land.
    private int RoadCost(int cell, int cost)
    {
        int level = RoadAt.Length > 0 ? RoadAt[cell] : 0;
        if (level == 0 || cost == 0)
        {
            return cost;
        }

        return Math.Max(10, Math.Min(cost, 150) * Content.RoadLevels[level - 1].WalkPercent / 100);
    }

    /// <summary>Runs the road supply of a new camp (tests).</summary>
    internal void TestRoadSupply(Civ civ, Settlement camp) => RoadSupply(civ, camp);

    // A new camp on a road that leads to one of our settlements gets a caravan of goods from that settlement.
    private void RoadSupply(Civ civ, Settlement camp)
    {
        Settlement? from = RoadLinked(civ, camp);
        if (from is null)
        {
            return;
        }

        var load = new long[Content.Goods.Count];
        bool any = false;
        for (int g = 0; g < load.Length; g++)
        {
            // Only what the settlement has to spare: never more than half its store.
            load[g] = Math.Min(Content.RoadSupply[g], from.Stock[g] / 2);
            any |= load[g] >= SimContent.One;
        }

        if (!any)
        {
            return;
        }

        Unit c = NewCarrier(civ, Content.Caravans, from, NearestStore(from, camp.Center), load, 100);
        c.Job = CarrierJob.ToSettlement;
        c.Target = camp.Id;
        Walk(c, camp.Center);
        News.Add((civ.Id, $"A caravan from {from.Name} brings goods along the road to {camp.Name}: {CargoText(load)}."));
        SyncTotals(civ);
    }

    // The settlement of ours that a road joins to this camp (searching along road hexes and our own land), or null.
    private Settlement? RoadLinked(Civ civ, Settlement camp)
    {
        var seen = new HashSet<int> { camp.Center };
        var queue = new Queue<int>();
        queue.Enqueue(camp.Center);
        Span<HexCoord> ring = stackalloc HexCoord[6];
        int steps = 0;
        while (queue.Count > 0 && steps++ < 4000)
        {
            int cell = queue.Dequeue();
            int sid = SettlementAt[cell];
            if (sid >= 0 && sid != camp.Id && Settlements[sid].Civ == civ.Id && !Settlements[sid].Abandoned)
            {
                return Settlements[sid];
            }

            int n = Geo.Ring(Geo.FromIndex(cell), 1, ring);
            for (int k = 0; k < n; k++)
            {
                int next = Geo.Index(ring[k]);
                bool way = RoadAt[next] > 0 || (SettlementAt[next] >= 0 && Settlements[SettlementAt[next]].Civ == civ.Id);
                if (way && seen.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return null;
    }
}
