using System;
using System.Collections.Generic;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>Send a unit on a patrol: it walks the drawn loop, goes to the nearest settlement for food, and walks it again.</summary>
public sealed record PatrolCommand(int Civ, int Unit, int[] Loop) : SimCommand(Civ);

/// <summary>A unit walks its last patrol again (Patryk, 2026-11-19).</summary>
public sealed record ResumePatrolCommand(int Civ, int Unit) : SimCommand(Civ);

/// <summary>Where a patrolling unit is in its cycle.</summary>
public enum PatrolStage : byte
{
    /// <summary>Not patrolling.</summary>
    None = 0,

    /// <summary>Walking to the first hex of the loop.</summary>
    ToStart = 1,

    /// <summary>Walking the loop.</summary>
    OnLoop = 2,
}

/// <summary>
/// Paths drawn hex by hex (decision of 2026-11-18): the player draws the exact hexes a unit, a patrol or a caravan route goes
/// through. A drawn path must be connected (each hex next to the one before) and passable. When it starts away from the
/// unit, the way to its first hex is found as usual.
/// </summary>
public sealed partial class SimGame
{
    /// <summary>
    /// Builds the full walk from <paramref name="from"/> along the drawn hexes into <paramref name="path"/>. Returns null, or why
    /// the drawing cannot be walked.
    /// </summary>
    public string? DrawnPath(int from, IReadOnlyList<int> drawn, bool naval, List<int> path, int civ = -1)
    {
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(path);
        path.Clear();
        if (drawn.Count == 0)
        {
            return "Draw a path first.";
        }

        if ((uint)from >= (uint)Geo.CellCount)
        {
            return "The path leaves the map.";
        }

        foreach (int c in drawn)
        {
            if ((uint)c >= (uint)Geo.CellCount)
            {
                return "The path leaves the map."; // B5: refused, not thrown
            }
        }

        int start = 0;
        if (drawn[0] == from)
        {
            start = 1;
        }
        else if (Distance(from, drawn[0]) > 1)
        {
            // The drawing starts away from the unit: find the way to its first hex.
            if (!FindPath(from, drawn[0], path, naval, civ))
            {
                return "There is no way to the start of the drawn path.";
            }

            start = 1;
        }

        int last = path.Count > 0 ? path[^1] : from;
        for (int k = start; k < drawn.Count; k++)
        {
            int cell = drawn[k];
            if ((uint)cell >= (uint)Geo.CellCount)
            {
                return "The path leaves the map.";
            }

            if (cell == last)
            {
                continue;
            }

            if (Distance(last, cell) != 1)
            {
                return "A drawn path must go from hex to neighbouring hex.";
            }

            if (Cost(cell, naval) == 0)
            {
                return naval ? "Ships sail only on water." : "The path crosses a hex nobody can walk.";
            }

            if (!naval && WalledAgainst(civ, cell))
            {
                return "A wall of another people stands there: break it first.";
            }

            if (!naval && !Crossable(civ, last, DirectionTo(last, cell)))
            {
                return "The path crosses a river: build a raft landing by it first.";
            }

            path.Add(cell);
            last = cell;
        }

        return null;
    }

    // Moves a unit along hexes the player drew.
    private string? MoveDrawn(Civ civ, Unit u, int[] drawn, bool hidden)
    {
        UnitType t = Content.Units[u.Kind];
        string? why = DrawnPath(u.Cell, drawn, t.Naval, _way, u.Civ);
        if (why is not null)
        {
            return why; // the old way stays (B1)
        }

        u.Path.Clear();
        u.Path.AddRange(_way);

        u.Patrol.Clear();
        u.PatrolStage = PatrolStage.None;
        StartWalk(u, hidden && t.HiddenTicksPerHex > 0);
        _ = civ;
        return null;
    }

    private string? ResumePatrol(Civ civ, int id)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id)
        {
            return "Not our unit.";
        }

        return Units[id].LastPatrol.Count == 0 ? "This unit has not patrolled yet." : Patrol(civ, id, [.. Units[id].LastPatrol]);
    }

    private string? Patrol(Civ civ, int id, int[] loop)
    {
        if ((uint)id >= (uint)Units.Count || Units[id].Civ != civ.Id || Units[id].State is UnitState.Gone or UnitState.Training)
        {
            return "Not our unit, or it is not ready.";
        }

        Unit u = Units[id];
        UnitType t = Content.Units[u.Kind];
        if (t.System)
        {
            return "Porters and caravans follow their task.";
        }

        // The loop itself must be walkable from its own first hex.
        var check = new List<int>();
        string? why = loop.Length < 2 ? "Draw a longer patrol." : DrawnPath(loop[0], loop, t.Naval, check, civ.Id);
        if (why is not null)
        {
            return why;
        }

        u.Patrol.Clear();
        u.Patrol.AddRange(loop);
        u.LastPatrol.Clear();
        u.LastPatrol.AddRange(loop);
        u.TargetUnit = -1;
        u.TargetSettlement = -1;
        u.TargetWall = -1;
        NextPatrolLeg(u, PatrolStage.ToStart);
        return null;
    }

    // Starts the next part of the patrol cycle.
    private void NextPatrolLeg(Unit u, PatrolStage stage)
    {
        UnitType t = Content.Units[u.Kind];
        u.PatrolStage = stage;
        switch (stage)
        {
            case PatrolStage.ToStart:
                if (!FindPath(u.Cell, u.Patrol[0], u.Path, t.Naval, u.Civ))
                {
                    StopPatrol(u, "cannot reach the start of its patrol");
                    return;
                }

                if (u.Path.Count == 0)
                {
                    NextPatrolLeg(u, PatrolStage.OnLoop);
                    return;
                }

                break;
            case PatrolStage.OnLoop:
                if (DrawnPath(u.Cell, u.Patrol, t.Naval, u.Path, u.Civ) is not null)
                {
                    StopPatrol(u, "cannot walk its patrol any more");
                    return;
                }

                break;
        }

        StartWalk(u, u.Hidden);
    }

    // A patrolling unit reached the end of a part of its cycle.
    private void PatrolArrived(Unit u)
    {
        switch (u.PatrolStage)
        {
            case PatrolStage.ToStart:
                NextPatrolLeg(u, PatrolStage.OnLoop);
                break;
            case PatrolStage.OnLoop:
                NextPatrolLeg(u, PatrolStage.ToStart); // round again: the home settlement feeds it wherever it is
                break;
        }
    }

    private void StopPatrol(Unit u, string why)
    {
        u.Patrol.Clear();
        u.PatrolStage = PatrolStage.None;
        u.Path.Clear();
        u.State = UnitState.Idle;
        News.Add((u.Civ, $"Our {Content.Units[u.Kind].Name.ToLowerInvariant()} {why} and stopped."));
    }

    // Sets a unit walking along its prepared path.
    private void StartWalk(Unit u, bool hidden)
    {
        u.Hidden = hidden;
        u.PathIndex = 0;
        if (u.Path.Count == 0)
        {
            u.State = UnitState.Idle;
            return;
        }

        u.State = UnitState.Moving;
        u.NextTick = Ticks + StepTicks(u, u.Path[0]);
        ScheduleUnit(u, UnitStep);
    }
}
