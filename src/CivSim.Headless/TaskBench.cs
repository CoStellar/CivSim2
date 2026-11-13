using System;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Simulation;
using CivSim.Core.State;
using CivSim.Core.Units;

namespace CivSim.Headless;

/// <summary>
/// Benchmark helper of stage 6 PR 4a (<c>--work-groups</c>): every second it gives every settlement of at least
/// <see cref="MinPeople"/> people that has never had one a woodcutting task on the cell of its zone with the most wood, through the
/// normal command, as a player would. Not an AI: it fills the map with work groups to measure the tick (the AI gives tasks in PR 4b).
/// Small camps are left alone: ten people less to gather food starves a camp of 25 (measured 2026-11-13).
/// </summary>
public sealed class TaskBench(GameContent content, int limit) : ICommandSource
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly TaskKindDefinition _wood = content.Tasks.Find("woodcutting") ?? throw new InvalidOperationException("tasks.json has no woodcutting.");

    /// <summary>Settlements smaller than this get no task.</summary>
    public const int MinPeople = 100;

    private readonly System.Collections.Generic.HashSet<int> _given = [];

    /// <summary>Work groups started so far.</summary>
    public int Started { get; private set; }

    /// <inheritdoc />
    public void IssueCommands(GameState state, CommandQueue queue)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(queue);
        if (state.Turn % Math.Max(1, _content.Time.TicksPerSecond) != 0 || Started >= limit)
        {
            return;
        }

        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(4)];
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || Started >= limit || s.TotalPopulation() < MinPeople || _given.Contains(s.Id.Value)
                || AreaTasks.RunningAt(state, s.Id.Value) >= AreaTasks.Allowed(_content, state, s))
            {
                continue;
            }

            WorldState world = state.Worlds[s.World.Value];
            int count = world.Geometry.Range(s.Cell, Math.Min(4, world.Geometry.MaxUniqueRadius), area);
            HexCoord best = default;
            long bestWood = 0;
            for (int k = 0; k < count; k++)
            {
                int i = world.Geometry.Index(area[k]);
                long wood = AreaTasks.BiomeValue(_content, world.Map, _wood, i).Hundredths;
                if (wood > bestWood && !Taken(state, s.Owner, i))
                {
                    var probe = new StartTaskCommand(_content, s.Id, _wood.Key, area[k]);
                    if (probe.Validate(state, s.Owner).Success)
                    {
                        best = area[k];
                        bestWood = wood;
                    }
                }
            }

            if (bestWood > 0)
            {
                queue.Enqueue(PlayerId.Ai, s.Owner, new StartTaskCommand(_content, s.Id, _wood.Key, best));
                _given.Add(s.Id.Value);
                Started++;
            }
        }
    }

    private static bool Taken(GameState state, CivId owner, int cell)
    {
        for (int k = 0; k < state.Units.Count; k++)
        {
            Unit u = state.Units[k];
            if (!u.Gone && u.Role == UnitRole.Workers && u.Owner == owner && u.Target == cell)
            {
                return true;
            }
        }

        return false;
    }
}
