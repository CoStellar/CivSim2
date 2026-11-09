using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Population;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Perception;

/// <summary>
/// Map memory (GDD 8.2, CORE simplified: oral memory; stage 3g). Every lived-in settlement sees the cells around it;
/// a civilization knows a cell for <see cref="PerceptionRules.OralMemoryYears"/> after it last saw it. The AI chooses
/// places only among known cells, and the server shows a player only what its civilization knows.
/// </summary>
public static class MapMemory
{
    /// <summary>Marks the cells every lived-in settlement sees this year.</summary>
    public static void See(GameContent content, GameState state)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        WorldState earth = state.Worlds[0];
        HexGlobe geo = earth.Geometry;
        int radius = Math.Min(content.Perception.SightCells, geo.MaxUniqueRadius);
        Span<HexCoord> area = HexGlobe.RangeSize(radius) <= 512 ? stackalloc HexCoord[HexGlobe.RangeSize(radius)] : new HexCoord[HexGlobe.RangeSize(radius)];
        int stamp = (int)(state.Month / 12) + 1;
        foreach (SettlementState s in state.Settlements)
        {
            if (!s.IsActive || s.World != earth.Id)
            {
                continue;
            }

            CivilizationState? civ = state.FindCivilization(s.Owner);
            if (civ is null || !Simulation.WorldClock.Steps(civ))
            {
                continue; // a civilization looks around in its own steps (ADR 0018)
            }

            if (civ.SeenYear.Length != geo.CellCount)
            {
                int[] seen = civ.SeenYear;
                Array.Resize(ref seen, geo.CellCount);
                civ.SeenYear = seen;
            }

            int count = geo.Range(s.Cell, radius, area);
            for (int k = 0; k < count; k++)
            {
                civ.SeenYear[geo.Index(area[k])] = stamp;
            }
        }
    }


    /// <summary>True when the civilization knows the Earth cell now (seen, and not yet forgotten).</summary>
    public static bool Knows(GameContent content, GameState state, CivilizationState civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        if (cell < 0 || cell >= civ.SeenYear.Length || civ.SeenYear[cell] == 0)
        {
            return false;
        }

        return (state.Month / 12) - (civ.SeenYear[cell] - 1) <= content.Perception.OralMemoryYears;
    }

    /// <summary>
    /// True when the civilization saw the Earth cell in its last own step (its camps and scouts look around at the start of
    /// each own step): what lives there now, foreign camps and their warriors, is known only for such cells.
    /// </summary>
    public static bool SeesNow(GameState state, CivilizationState civ, int cell)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(civ);
        return cell >= 0 && cell < civ.SeenYear.Length && civ.SeenYear[cell] != 0 && civ.SeenYear[cell] - 1 >= civ.StepStartMonth / 12;
    }

    /// <summary>Every Earth cell the civilization knows now.</summary>
    public static bool[] KnownCells(GameContent content, GameState state, CivilizationState civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        var known = new bool[state.Worlds[0].Geometry.CellCount];
        for (int i = 0; i < known.Length; i++)
        {
            known[i] = Knows(content, state, civ, i);
        }

        return known;
    }

    /// <summary>True when people can walk on the cell: land, or sea ice.</summary>
    public static bool Walkable(GameContent content, WorldMap map, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        return !content.Biomes[map.Biome[cell]].IsWater || map.Feature[cell] == (byte)WorldGen.TerrainFeature.IceField;
    }

    /// <summary>
    /// Shortest walk over known, walkable cells from <paramref name="from"/> to <paramref name="to"/>, within
    /// <see cref="PerceptionRules.JourneyCells"/>; the cells after the start, or null when there is no way.
    /// </summary>
    public static List<int>? Path(GameContent content, GameState state, CivilizationState civ, WorldState world, int from, int to) =>
        new PathFinder().Find(content, state, civ, world, from, to);
}

/// <summary>Breadth-first search over known, walkable cells with buffers kept between searches (no allocations once warm).</summary>
public sealed class PathFinder
{
    private int[] _came = [];
    private int[] _stamp = [];
    private int[] _queue = [];
    private int _run;
    private readonly List<int> _path = [];

    /// <summary>
    /// Shortest walk from <paramref name="from"/> to <paramref name="to"/> (see <see cref="MapMemory.Path"/>). The list
    /// is reused by the next search.
    /// </summary>
    public List<int>? Find(GameContent content, GameState state, CivilizationState civ, WorldState world, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(world);
        HexGlobe geo = world.Geometry;
        if (_came.Length != geo.CellCount)
        {
            _came = new int[geo.CellCount];
            _stamp = new int[geo.CellCount];
            _queue = new int[geo.CellCount];
            _run = 0;
        }

        _run++;
        int limit = content.Perception.JourneyCells;
        HexCoord start = geo.FromIndex(from);
        int head = 0;
        int tail = 0;
        _queue[tail++] = from;
        _stamp[from] = _run;
        _came[from] = -1;
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        while (head < tail)
        {
            int cur = _queue[head++];
            if (cur == to)
            {
                _path.Clear();
                for (int c = to; c != from; c = _came[c])
                {
                    _path.Add(c);
                }

                _path.Reverse();
                return _path;
            }

            geo.GetNeighbors(geo.FromIndex(cur), n6);
            foreach (HexCoord n in n6)
            {
                int ni = geo.Index(n);
                if (_stamp[ni] == _run || !MapMemory.Walkable(content, world.Map, ni) || !MapMemory.Knows(content, state, civ, ni)
                    || geo.Distance(start, n) > limit)
                {
                    continue;
                }

                _stamp[ni] = _run;
                _came[ni] = cur;
                _queue[tail++] = ni;
            }
        }

        return null;
    }
}

/// <summary>Perception phase (GDD 11.1, phase 2; stage 3g): settlements see around them and refresh the map memory.</summary>
public sealed class PerceptionSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Perception;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        MapMemory.See(_content, context.State);

        // Stage 6 PR 3a: scouting is done by visible scout units (Units.UnitSystem), no longer by invisible trips.
    }
}

/// <summary>
/// A camp sets out for a known place farther than one move (GDD 5.4 and 8.2; stage 3g): it walks there over several
/// turns, over known land, sea ice and land bridges.
/// </summary>
public sealed record JourneyCommand(GameContent Content, SettlementId Settlement, HexCoord Target) : ICommand, IChronicledCommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Movement;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        CommandResult own = SettlementRules.CheckOwn(state, civ, Settlement, out SettlementState? s);
        if (!own.Success)
        {
            return own;
        }

        if (!s!.IsActive || !Content.SettlementLevels[s.Level].Mobile)
        {
            return CommandResult.Fail("Only a lived-in camp can set out.");
        }

        WorldState world = state.Worlds[s.World.Value];
        int target = world.Geometry.Index(Target);
        CivilizationState c = state.FindCivilization(civ)!;
        if (Content.Biomes[world.Map.Biome[target]].IsWater || !MapMemory.Knows(Content, state, c, target))
        {
            return CommandResult.Fail("The camp can only set out for known land.");
        }

        return MapMemory.Path(Content, state, c, world, world.Geometry.Index(s.Cell), target) is null
            ? CommandResult.Fail("The camp knows no way there.")
            : CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ) => ApplyWithChronicle(state, civ);

    /// <inheritdoc />
    public string? ApplyWithChronicle(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState s = state.FindSettlement(Settlement)!;
        WorldState world = state.Worlds[s.World.Value];
        s.JourneyTarget = world.Geometry.Index(Target);
        s.JourneyNext = -1;
        s.JourneyProgress = 0;
        s.JourneyPath.Clear();
        s.JourneyPathAt = 0;
        s.JourneyPathGoal = -1; // the way is found once, in the camp's first step (JourneySystem)
        return $"{state.FindCivilization(civ)!.Name} set out with {s.Name} for land {world.Geometry.Distance(s.Cell, Target)} cells "
            + $"{SettlementRules.Direction(world, s.Cell, Target)}.";
    }
}

/// <summary>
/// Movement phase (stage 3g): camps on a journey walk <see cref="PerceptionRules.MarchCellsPerCentury"/> cells per century (at most
/// <see cref="PopulationRules.MaxMoveCells"/> per own step) along the
/// shortest known way, carrying what they can; they stop before a cell where another settlement lives, and end the
/// journey when they arrive or the way is gone (forgotten land, melted ice).
/// </summary>
public sealed class JourneySystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly Units.RouteFinder _routes = new();

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Movement;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        int baseTicks = _content.Units.JourneyTicksPerCell;
        for (int n = 0; n < state.Settlements.Count; n++)
        {
            SettlementState s = state.Settlements[n];
            if (s.JourneyTarget == SettlementState.NoJourney)
            {
                continue;
            }

            WorldState world = state.Worlds[s.World.Value];
            CivilizationState? civ = state.FindCivilization(s.Owner);
            if (civ is null || !s.IsActive)
            {
                Stop(s);
                continue;
            }

            // Stage 6 PR 3a: the camp walks in ticks, cell by cell, over the cheapest known way (A*), and pitches at
            // every free land cell on it (it crosses sea ice and lived cells without stopping). Since PR 3b the way is
            // found once for the goal and remembered (known issue 10); it is searched again only when it closes.
            if (s.JourneyNext < 0 && !NextCamp(state, civ, world, s, baseTicks))
            {
                Stop(s); // no way, or nowhere to camp on it
                continue;
            }

            if (++s.JourneyProgress < s.JourneyStepTicks)
            {
                continue;
            }

            if (Lived(state, world, s.JourneyNext, s))
            {
                s.JourneyNext = -1; // someone camped there meanwhile: look again
                continue;
            }

            SettlementAssets.Relocate(_content, state, s, world.Geometry.FromIndex(s.JourneyNext));
            int at = s.JourneyPath.IndexOf(s.JourneyNext, s.JourneyPathAt);
            s.JourneyPathAt = at < 0 ? s.JourneyPath.Count : at + 1;
            bool done = s.JourneyNext == s.JourneyTarget;
            s.JourneyNext = -1;
            s.JourneyProgress = 0;
            if (done)
            {
                s.JourneyTarget = SettlementState.NoJourney;
                context.Chronicle($"{civ.Name} reached the end of its journey with {s.Name}.");
            }
        }
    }

    // Chooses the next camp of a journey: the first free land cell on the remembered way (found once, A*), and the ticks
    // the walk there costs. The way is searched again when there is none for this goal or a cell on it closed (melted
    // ice, forgotten land). False when there is no way or no free land on it.
    private bool NextCamp(GameState state, CivilizationState civ, WorldState world, SettlementState s, int baseTicks)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (s.JourneyPathGoal != s.JourneyTarget || attempt > 0)
            {
                List<int>? path = _routes.Path(_content, state, civ, world, world.Geometry.Index(s.Cell), s.JourneyTarget, baseTicks);
                s.JourneyPath.Clear();
                s.JourneyPathAt = 0;
                s.JourneyPathGoal = s.JourneyTarget;
                if (path is null || path.Count == 0)
                {
                    s.JourneyPathGoal = -1;
                    return false;
                }

                s.JourneyPath.AddRange(path);
            }

            int ticks = 0;
            bool closed = false;
            HexCoord here = s.Cell;
            for (int k = s.JourneyPathAt; k < s.JourneyPath.Count; k++)
            {
                int cell = s.JourneyPath[k];
                HexCoord next = world.Geometry.FromIndex(cell);
                int step = StepTicks(world, here, next, cell, baseTicks);
                if (step <= 0 || !MapMemory.Knows(_content, state, civ, cell))
                {
                    closed = true; // the way closed: search again from here
                    break;
                }

                ticks += step;
                here = next;
                if (!_content.Biomes[world.Map.Biome[cell]].IsWater && !Lived(state, world, cell, s))
                {
                    s.JourneyNext = cell;
                    s.JourneyStepTicks = Math.Max(1, ticks);
                    s.JourneyProgress = 0;
                    return true;
                }
            }

            if (!closed)
            {
                return false; // nowhere to camp on the rest of the way
            }
        }

        return false;
    }

    private int StepTicks(WorldState world, HexCoord from, HexCoord to, int toIndex, int baseTicks)
    {
        for (int d = 0; d < 6; d++)
        {
            if (world.Geometry.Neighbor(from, (HexDirection)d) == to)
            {
                return Units.RouteFinder.StepTicks(_content, world, from, (HexDirection)d, toIndex, baseTicks);
            }
        }

        return baseTicks;
    }

    private static void Stop(SettlementState s)
    {
        s.JourneyTarget = SettlementState.NoJourney;
        s.JourneyNext = -1;
        s.JourneyProgress = 0;
        s.JourneyPath.Clear();
        s.JourneyPathAt = 0;
        s.JourneyPathGoal = -1;
    }

    // True when another lived-in settlement stands on cell (the spatial grid of settlements, T2).
    private static bool Lived(GameState state, WorldState world, int cell, SettlementState self)
    {
        HexCoord at = world.Geometry.FromIndex(cell);
        foreach (int i in state.Grid.Within(world, at, 0))
        {
            SettlementState o = state.Settlements[i];
            if (o != self && o.IsActive && o.World == world.Id && o.Cell == at)
            {
                return true;
            }
        }

        return false;
    }
}
