using System;
using System.Collections.Generic;
using CivSim.Core.Map;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Build a work (ditch, palisade, wall, aqueduct) over drawn hexes.</summary>
public sealed record BuildWorkCommand(int Civ, int[] Cells, int Work) : SimCommand(Civ);

/// <summary>Remove a work from a hex of ours.</summary>
public sealed record RemoveWorkCommand(int Civ, int Cell, int Work) : SimCommand(Civ);

/// <summary>Attack a wall of an enemy: the unit breaks it from a hex next to it.</summary>
public sealed record AttackWallCommand(int Civ, int Unit, int Cell) : SimCommand(Civ);

/// <summary>
/// Works on hexes (decision of 2026-11-18): ditches, palisades, walls and aqueducts are states of a hex, drawn hex by hex like
/// roads. They stay under whatever is built on the hex and go only when the player removes them (or an enemy breaks a wall).
/// Ditches and aqueducts carry water from a hex next to water, a few hexes at most: a farm on a watered hex makes more, a
/// settlement an aqueduct reaches grows faster. Palisades wound enemies at war standing on them; walls stop foreign walkers
/// until broken. Roads bridge the rivers they cross once the people knows Masonry. The water is worked out again only when a
/// ditch or an aqueduct is built or removed (docs/wydajnosc.md: work on change).
/// </summary>
public sealed partial class SimGame
{
    /// <summary>Bit of a ditch or aqueduct hex that water reaches.</summary>
    public const ushort WateredBit = 1 << 4;

    /// <summary>Bit of an aqueduct hex that water reaches.</summary>
    public const ushort FlowingBit = 1 << 5;

    /// <summary>First of the three bits of the bridges on a hex's own edges.</summary>
    public const int BridgeShift = 8;

    /// <summary>Works on each hex: bit per work kind, then the water bits.</summary>
    public ushort[] WorksAt { get; private set; } = [];

    /// <summary>People that built the palisade or wall of a hex (−1 = none).</summary>
    public int[] WorksCiv { get; private set; } = [];

    /// <summary>Damage a wall can still take, hundredths.</summary>
    public long[] WallLeft { get; private set; } = [];

    /// <summary>Bridges, per edge of the map (as rivers).</summary>
    public byte[] BridgeAt { get; private set; } = [];

    private readonly SortedSet<int> _waterWorks = []; // hexes with a ditch or aqueduct

    private void InitWorks()
    {
        WorksAt = new ushort[Geo.CellCount];
        WorksCiv = new int[Geo.CellCount];
        Array.Fill(WorksCiv, -1);
        WallLeft = new long[Geo.CellCount];
        BridgeAt = new byte[Map.River.Length];
    }

    /// <summary>Works of the hex with the bridges of its own edges in bits 8–10 (for the view).</summary>
    public ushort WorksView(int cell)
    {
        int bridges = 0;
        for (int k = 0; k < 3; k++)
        {
            int e = (cell * 3) + k;
            if (e < BridgeAt.Length && BridgeAt[e] != 0)
            {
                bridges |= 1 << k;
            }
        }

        return (ushort)(WorksAt[cell] | (bridges << BridgeShift));
    }

    private string? BuildWork(Civ civ, int[] cells, int work)
    {
        if ((uint)work >= (uint)Content.Works.Count)
        {
            return "No such work.";
        }

        WorkKind kind = Content.Works[work];
        if (!civ.KnownTech[kind.Tech])
        {
            return $"{kind.Name} needs {Content.Techs[kind.Tech].Name}.";
        }

        var path = new List<int>();
        string? why = cells.Length == 0 ? "Draw it first." : DrawnPath(cells[0], cells, false, path);
        if (why is not null)
        {
            return why;
        }

        path.Insert(0, cells[0]);
        if (kind.FromWater > 0)
        {
            if (!NextToWater(path[0]))
            {
                return $"{kind.Name} must start next to water (a river, a lake or the sea).";
            }

            if (path.Count > kind.FromWater)
            {
                return $"{kind.Name} reaches at most {kind.FromWater} hexes from the water.";
            }
        }

        var project = new RoadProject { Civ = civ.Id, Level = 0, Work = work };
        foreach (int cell in path)
        {
            if (OwnerAt[cell] != civ.Id)
            {
                return "Works go only on our own land.";
            }

            if ((WorksAt[cell] & kind.Bit) == 0)
            {
                project.Cells.Add(cell);
            }
        }

        if (project.Cells.Count == 0)
        {
            return $"The {kind.Name.ToLowerInvariant()} is already there.";
        }

        RoadProjects.Add(project);
        News.Add((civ.Id, $"Work begins: {project.Cells.Count} hexes of {kind.Name.ToLowerInvariant()}."));
        return null;
    }

    // A hex of a work project is done.
    private void FinishWorkHex(Civ civ, WorkKind kind, int cell)
    {
        WorksAt[cell] |= (ushort)kind.Bit;
        if (kind.Strength > 0 || kind.WoundPercentPerSecond > 0)
        {
            WorksCiv[cell] = civ.Id;
        }

        if (kind.Strength > 0)
        {
            WallLeft[cell] = kind.Strength;
        }

        if (kind.FromWater > 0)
        {
            _waterWorks.Add(cell);
            UpdateWater();
        }

        Map.MarkCellDirty(cell);
    }

    private string? RemoveWork(Civ civ, int cell, int work)
    {
        if ((uint)cell >= (uint)Geo.CellCount || (uint)work >= (uint)Content.Works.Count)
        {
            return "Nothing to remove.";
        }

        WorkKind kind = Content.Works[work];
        if ((WorksAt[cell] & kind.Bit) == 0)
        {
            return $"There is no {kind.Name.ToLowerInvariant()} here.";
        }

        if (OwnerAt[cell] != civ.Id && WorksCiv[cell] != civ.Id)
        {
            return "Not on our land.";
        }

        ClearWork(cell, kind);
        return null;
    }

    private void ClearWork(int cell, WorkKind kind)
    {
        WorksAt[cell] &= (ushort)~kind.Bit;
        if (kind.Strength > 0)
        {
            WallLeft[cell] = 0;
        }

        if ((WorksAt[cell] & (WallBit | PalisadeBit)) == 0)
        {
            WorksCiv[cell] = -1;
        }

        if (kind.FromWater > 0)
        {
            bool still = false;
            foreach (WorkKind w in Content.Works)
            {
                still |= w.FromWater > 0 && (WorksAt[cell] & w.Bit) != 0;
            }

            if (!still)
            {
                _waterWorks.Remove(cell);
            }

            UpdateWater();
        }

        Map.MarkCellDirty(cell);
    }

    // Tick spikes (2026-11-20): looked up by name once, not at every step of every way search.
    private int _wallBit = -1;
    private int _palisadeBit = -1;

    private ushort WallBit => (ushort)(_wallBit >= 0 ? _wallBit : (_wallBit = Content.Work("wall")?.Bit ?? 0));

    private ushort PalisadeBit => (ushort)(_palisadeBit >= 0 ? _palisadeBit : (_palisadeBit = Content.Work("palisade")?.Bit ?? 0));

    /// <summary>True when foreign walkers may not enter the hex: a wall of another people stands there.</summary>
    public bool WalledAgainst(int civ, int cell) => civ >= 0 && WallBit != 0 && (WorksAt[cell] & WallBit) != 0 && WorksCiv[cell] != civ;

    // Water runs from the ditch and aqueduct hexes next to water along their own kind, as far as the kind reaches. Settlements an
    // aqueduct reaches are marked.
    private void UpdateWater()
    {
        foreach (int cell in _waterWorks)
        {
            WorksAt[cell] &= unchecked((ushort)~(WateredBit | FlowingBit));
        }

        foreach (Settlement s in Settlements)
        {
            s.Aqueduct = false;
        }

        Span<HexCoord> nb = stackalloc HexCoord[6];
        foreach (WorkKind kind in Content.Works)
        {
            if (kind.FromWater <= 0)
            {
                continue;
            }

            ushort mark = kind.GrowthBonus > 0 ? FlowingBit : WateredBit;
            var queue = new Queue<(int Cell, int Steps)>();
            var seen = new HashSet<int>();
            foreach (int cell in _waterWorks)
            {
                if ((WorksAt[cell] & kind.Bit) != 0 && NextToWater(cell))
                {
                    queue.Enqueue((cell, 1));
                    seen.Add(cell);
                }
            }

            while (queue.Count > 0)
            {
                (int cell, int steps) = queue.Dequeue();
                WorksAt[cell] |= mark;
                if (kind.GrowthBonus > 0 && SettlementAt[cell] >= 0)
                {
                    Settlements[SettlementAt[cell]].Aqueduct = true;
                }

                if (steps >= kind.FromWater)
                {
                    continue;
                }

                Geo.GetNeighbors(Geo.FromIndex(cell), nb);
                for (int d = 0; d < 6; d++)
                {
                    int n = Geo.Index(nb[d]);
                    if ((WorksAt[n] & kind.Bit) != 0 && seen.Add(n))
                    {
                        queue.Enqueue((n, steps + 1));
                    }
                }
            }
        }

        foreach (Civ c in Civs)
        {
            c.Dirty = true;
        }
    }

    /// <summary>Farms on this hex make this much more for its water (percent).</summary>
    public int WaterBonus(int cell, BuildingType t)
    {
        ArgumentNullException.ThrowIfNull(t);
        if ((WorksAt[cell] & WateredBit) == 0 || Array.IndexOf(t.Groups, "farm") < 0)
        {
            return 0;
        }

        int best = 0;
        foreach (WorkKind w in Content.Works)
        {
            best = Math.Max(best, w.FarmPercent);
        }

        return best;
    }

    // A bridge on the edge between two neighbouring road hexes across a river, once the people knows how (paid by the nearest
    // settlement; without the goods the road is built and the river waits for its bridge).
    private void BridgeRoad(Civ civ, int cell)
    {
        if (Content.BridgeTech < 0 || !civ.KnownTech[Content.BridgeTech] || RoadAt[cell] == 0)
        {
            return;
        }

        HexCoord c = Geo.FromIndex(cell);
        for (int d = 0; d < 6; d++)
        {
            int e = Geo.EdgeIndex(Geo.Edge(c, (HexDirection)d));
            int n = Geo.Index(Geo.Neighbor(c, (HexDirection)d));
            if (Map.River[e] == 0 || BridgeAt[e] != 0 || RoadAt[n] == 0)
            {
                continue;
            }

            Settlement? payer = NearestOwn(civ, cell);
            if (payer is null || !CanAfford(payer.Stock, Content.BridgePrice, 100))
            {
                continue;
            }

            Pay(payer.Stock, Content.BridgePrice, 100);
            BridgeAt[e] = 1;
            Map.MarkCellDirty(cell);
            Map.MarkCellDirty(n);
            News.Add((civ.Id, "A bridge now crosses the river on our road."));
        }
    }

    /// <summary>Lays the bridges of a road hex (tests).</summary>
    internal void TestBridge(Civ civ, int cell) => BridgeRoad(civ, cell);

    // Once a second: palisades wound enemies at war standing on them.
    private void PalisadeStep()
    {
        WorkKind? palisade = Content.Work("palisade");
        if (palisade is null)
        {
            return;
        }

        // A copy: a unit the palisade kills leaves its people's list.
        _onPalisades.Clear();
        foreach (Unit u in ActiveUnits())
        {
            if ((WorksAt[u.Cell] & palisade.Bit) != 0)
            {
                _onPalisades.Add(u);
            }
        }

        foreach (Unit u in _onPalisades)
        {
            int cell = u.Cell;
            if (u.State == UnitState.Gone || (WorksAt[cell] & palisade.Bit) == 0 || !Content.Units[u.Kind].Army || WorksCiv[cell] < 0 || !AtWar(u.Civ, WorksCiv[cell]))
            {
                continue;
            }

            u.Wounds += Math.Max(SimContent.One / 4, (long)u.People * SimContent.One * palisade.WoundPercentPerSecond / 100);
            int dead = (int)Math.Min(u.People, u.Wounds / SimContent.One);
            if (dead <= 0)
            {
                continue;
            }

            u.Wounds -= dead * SimContent.One;
            u.People -= dead;
            Wound(u, dead);
            if (u.People <= 0)
            {
                Destroyed(u, WorksCiv[cell]);
            }
        }
    }

    private readonly List<Unit> _onPalisades = [];

    private string? AttackWall(Civ civ, int id, int cell)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State is UnitState.Gone or UnitState.Training || !Content.Units[Units[id].Kind].Army)
        {
            return "Only our soldiers break walls.";
        }

        if ((uint)cell >= (uint)Geo.CellCount || !WalledAgainst(civ.Id, cell))
        {
            return "There is no enemy wall there.";
        }

        int owner = WorksCiv[cell];
        if (!AtWar(civ.Id, owner))
        {
            DeclareWar(civ, owner, surprise: true);
        }

        Unit u = Units[id];
        u.TargetWall = cell;
        u.TargetUnit = -1;
        u.TargetSettlement = -1;
        if (Distance(u.Cell, cell) <= 1)
        {
            return null;
        }

        // Walk to the nearest hex next to the wall.
        Span<HexCoord> nb = stackalloc HexCoord[6];
        Geo.GetNeighbors(Geo.FromIndex(cell), nb);
        int best = -1;
        for (int d = 0; d < 6; d++)
        {
            int n = Geo.Index(nb[d]);
            if (Cost(n, false) > 0 && !WalledAgainst(civ.Id, n) && (best < 0 || Distance(u.Cell, n) < Distance(u.Cell, best)))
            {
                best = n;
            }
        }

        if (best < 0 || !PathFor(u, best))
        {
            u.TargetWall = -1;
            return "No way to the wall.";
        }

        StartWalk(u, false);
        return null;
    }

    // Once a second: soldiers next to the wall they attack break it (siege engines much faster).
    private void WallStep()
    {
        foreach (Unit u in ActiveUnits())
        {
            int cell = u.TargetWall;
            if (cell < 0)
            {
                continue;
            }

            if (!WalledAgainst(u.Civ, cell))
            {
                u.TargetWall = -1;
                continue;
            }

            if (u.State == UnitState.Moving || Distance(u.Cell, cell) > Content.Units[u.Kind].Range)
            {
                continue;
            }

            UnitType t = Content.Units[u.Kind];
            u.Fighting = true;
            WallLeft[cell] -= Power(u) / 100 * (100 + t.SiegePercent) / 100;
            if (WallLeft[cell] <= 0)
            {
                int owner = WorksCiv[cell];
                ClearWork(cell, Content.Work("wall")!);
                News.Add((u.Civ, "We broke through a wall."));
                if (owner >= 0)
                {
                    News.Add((owner, "Enemies broke through one of our walls."));
                }

                u.TargetWall = -1;
            }
        }
    }
}
