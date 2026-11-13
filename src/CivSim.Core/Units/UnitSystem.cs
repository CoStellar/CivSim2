using System;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Perception;
using CivSim.Core.Population;
using CivSim.Core.Random;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Units;

/// <summary>
/// Walks the units in every tick (stage 6 PR 3a, ADR 0024): a unit spends the ticks a cell costs
/// (<see cref="RouteFinder.StepTicks"/>) and then stands on it, sees around itself and goes on; settlers found their camp
/// where they were sent, scouts walk out and come back, a unit sent home joins its settlement. In the pulse of a
/// civilization its settlements send scouts out (one party each, <see cref="UnitRules.ScoutEveryYears"/> years of action
/// after the last came back). Settlers eat what they carry once a month of action.
/// </summary>
public sealed class UnitSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));
    private readonly RouteFinder _routes = new();

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Movement;

    /// <summary>Route searches the units ran so far (T3: a way is found once per goal, not in every tick).</summary>
    public long RouteSearches => _routes.Searches;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        SendScouts(state);
        UnitBook book = state.Units;
        bool meal = state.Turn % Math.Max(1, _content.Time.TicksPerActionMonth) == 0;
        for (int k = 0; k < book.Count; k++)
        {
            Unit u = book[k];
            if (u.Gone)
            {
                continue;
            }

            CivilizationState? civ = state.FindCivilization(u.Owner);
            if (civ is null || !civ.IsAlive)
            {
                u.Gone = true; // nobody is left to go back to
                continue;
            }

            if (meal && u.Role == UnitRole.Settlers)
            {
                Eat(context, state, u, civ);
                if (u.Gone)
                {
                    continue;
                }
            }


            Walk(context, state, u, civ);
        }

        book.RemoveGone();
    }

    /// <summary>The cell <paramref name="u"/> walks to now, or -1 when it stands.</summary>
    public static int Goal(GameState state, Unit u)
    {
        ArgumentNullException.ThrowIfNull(state);
        switch (u.Order)
        {
            case UnitOrder.Move:
            case UnitOrder.Found:
            case UnitOrder.Attack:
            case UnitOrder.Work:
                return u.Target;
            case UnitOrder.Return:
            case UnitOrder.Flee:
                SettlementState? home = u.Home >= 0 ? state.FindSettlement(new SettlementId(u.Home)) : null;
                return home is { IsActive: true } ? state.Worlds[u.World.Value].Geometry.Index(home.Cell) : u.Cell;
            default:
                return -1;
        }
    }

    /// <summary>
    /// True when <paramref name="civ"/> may found a camp on <paramref name="cell"/>: dry land nobody else holds, no
    /// settlement there and none of its own camps closer than <see cref="PopulationRules.MinDistanceBetweenOwnCamps"/>.
    /// </summary>
    public static bool CanFound(GameContent content, GameState state, CivId civ, WorldState world, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        if (content.Biomes[world.Map.Biome[cell]].IsWater)
        {
            return false;
        }

        short owner = world.Map.Owner[cell];
        if (owner != WorldMap.NoOwner && owner != civ.Value)
        {
            return false;
        }

        HexCoord at = world.Geometry.FromIndex(cell);
        foreach (int i in state.Grid.Within(world, at, Math.Max(0, content.Population.MinDistanceBetweenOwnCamps - 1)))
        {
            SettlementState s = state.Settlements[i];
            if (!s.IsActive)
            {
                continue;
            }

            if (s.Cell == at || (s.Owner == civ && s.TotalPopulation() > 0
                && world.Geometry.Distance(s.Cell, at) < content.Population.MinDistanceBetweenOwnCamps))
            {
                return false;
            }
        }

        return true;
    }

    // One step of the way: the unit spends ticks on its next cell and stands on it once the cell is paid for.
    private void Walk(TurnContext context, GameState state, Unit u, CivilizationState civ)
    {
        WorldState world = state.Worlds[u.World.Value];
        HexGlobe geo = world.Geometry;
        UnitKindDefinition kind = _content.Units.Of(u.Kind);
        int baseTicks = kind.TicksPerCell;
        if (u.Order == UnitOrder.Attack && u.Target >= 0 && geo.Distance(geo.FromIndex(u.Cell), geo.FromIndex(u.Target)) <= kind.RangeCells)
        {
            // An army within its range of the target stands there (its campaign decides when it fights, stage 6 PR 3b).
            u.Next = -1;
            u.Progress = 0;
            return;
        }

        if (u.Next < 0)
        {
            if (u.Role == UnitRole.Scouts && (u.Order == UnitOrder.Scout || (u.Order == UnitOrder.Return && u.Trail.Count > 0)))
            {
                u.Next = ScoutStep(state, u, world); // out in its direction, back over its trail
            }
            else
            {
                int goal = Goal(state, u);
                if (goal < 0)
                {
                    return; // idle
                }

                if (goal == u.Cell)
                {
                    Arrive(context, state, u, civ, world);
                    return;
                }

                u.Next = NextOnPath(state, u, civ, world, goal, baseTicks);
                if (u.Next < 0)
                {
                    NoWay(context, state, u, civ, world);
                    return;
                }
            }

            if (u.Next < 0)
            {
                return;
            }
        }

        u.Progress++;
        HexCoord at = geo.FromIndex(u.Cell);
        HexCoord next = geo.FromIndex(u.Next);
        int side = Side(geo, at, next);
        int cost = side < 0 ? baseTicks : RouteFinder.StepTicks(_content, world, at, (HexDirection)side, u.Next, baseTicks, kind);
        u.StepCost = cost;
        if (cost == 0)
        {
            u.Next = -1; // the way closed (should not happen: land does not move); look again
            u.Progress = 0;
            return;
        }

        if (u.Progress < cost)
        {
            return;
        }

        u.Progress = 0;
        u.Cell = u.Next;
        u.Next = -1;
        if (u.Role == UnitRole.Scouts && u.Order == UnitOrder.Scout)
        {
            u.Trail.Add(u.Cell);
        }

        See(state, u, civ, world);
        if (u.Role == UnitRole.Scouts && u.Order == UnitOrder.Scout)
        {
            if (u.Trail.Count > _content.Units.ScoutTripCells)
            {
                u.Order = UnitOrder.Return; // far enough: back the way it came
            }
        }
        else if (u.Role == UnitRole.Scouts && u.Order == UnitOrder.Return && u.Trail.Count > 0)
        {
            // back over its trail (ScoutStep): home when the trail ends
        }
        else if (Goal(state, u) == u.Cell)
        {
            Arrive(context, state, u, civ, world);
        }
    }

    // The next cell of the unit's way to goal (T3): the way is found once and walked; it is found again only for a new goal,
    // when the unit stands off its way, or when the next cell cannot be entered.
    private int NextOnPath(GameState state, Unit u, CivilizationState civ, WorldState world, int goal, int baseTicks)
    {
        HexGlobe geo = world.Geometry;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1 || u.PathGoal != goal || u.PathAt >= u.Path.Count
                || Side(geo, geo.FromIndex(u.Cell), geo.FromIndex(u.Path[u.PathAt])) < 0)
            {
                System.Collections.Generic.List<int>? way = _routes.Path(_content, state, civ, world, u.Cell, goal, baseTicks, _content.Units.Of(u.Kind));
                u.Path.Clear();
                u.PathAt = 0;
                u.PathGoal = goal;
                if (way is null || way.Count == 0)
                {
                    u.PathGoal = -1;
                    return -1;
                }

                u.Path.AddRange(way);
            }

            int next = u.Path[u.PathAt];
            int side = Side(geo, geo.FromIndex(u.Cell), geo.FromIndex(next));
            if (side >= 0 && RouteFinder.StepTicks(_content, world, geo.FromIndex(u.Cell), (HexDirection)side, next, baseTicks, _content.Units.Of(u.Kind)) > 0)
            {
                u.PathAt++;
                return next;
            }
        }

        return -1;
    }

    // A scout walks out in its direction and turns at water; coming back it walks its trail backwards.
    private int ScoutStep(GameState state, Unit u, WorldState world)
    {
        HexGlobe geo = world.Geometry;
        if (u.Order == UnitOrder.Return)
        {
            // Back over the trail: the first cell of the trail is home.
            if (u.Trail.Count <= 1)
            {
                ScoutHome(state, u);
                return -1;
            }

            u.Trail.RemoveAt(u.Trail.Count - 1);
            return u.Trail[^1];
        }

        if (u.Order != UnitOrder.Scout)
        {
            return -1;
        }

        DeterministicRandom rng = state.Random[RandomStreamId.Population];
        for (int tries = 0; tries < 6; tries++)
        {
            int next = geo.Index(geo.Neighbor(geo.FromIndex(u.Cell), (HexDirection)u.Direction));
            if (MapMemory.Walkable(_content, world.Map, next))
            {
                return next;
            }

            u.Direction = (u.Direction + 1 + rng.NextInt(5)) % 6;
        }

        u.Order = UnitOrder.Return; // shut in by water: back home
        return -1;
    }

    private static void ScoutHome(GameState state, Unit u)
    {
        u.Gone = true;
        SettlementState? home = u.Home >= 0 ? state.FindSettlement(new SettlementId(u.Home)) : null;
        if (home is not null)
        {
            home.ScoutOut = false;
            home.ScoutBackMonth = state.ActionMonth;
            Unload(u, home); // the hunters are back (stage 6 PR 3b); an abandoned home still takes them (they find it empty)
        }
    }

    private static int Side(HexGlobe geo, HexCoord from, HexCoord to)
    {
        for (int d = 0; d < 6; d++)
        {
            if (geo.Neighbor(from, (HexDirection)d) == to)
            {
                return d;
            }
        }

        return -1;
    }

    // The unit sees the cells around the one it stands on (it reveals the map to its people, ADR 0024).
    private void See(GameState state, Unit u, CivilizationState civ, WorldState world)
    {
        HexGlobe geo = world.Geometry;
        if (civ.SeenYear.Length != geo.CellCount)
        {
            return;
        }

        int radius = Math.Min(_content.Units.Of(u.Kind).SightCells, geo.MaxUniqueRadius);
        int stamp = (int)(state.Month / 12) + 1;
        civ.SeenYear[u.Cell] = stamp;
        if (radius <= 0)
        {
            return;
        }

        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(Math.Min(radius, 6))];
        int count = geo.Range(geo.FromIndex(u.Cell), Math.Min(radius, 6), area);
        for (int k = 0; k < count; k++)
        {
            civ.SeenYear[geo.Index(area[k])] = stamp;
        }
    }

    // Settlers eat a month of food from what they carry; without it some of them die.
    private void Eat(TurnContext context, GameState state, Unit u, CivilizationState civ)
    {
        int people = u.TotalPeople();
        Amount need = CivSim.Core.Episodes.Campaigns.FoodFor(_content, people, 1);
        for (int g = 0; g < u.Cargo.Length && g < _content.Goods.Count && need > Amount.Zero; g++)
        {
            if (_content.Goods[g].IsFood && u.Cargo[g] > Amount.Zero)
            {
                Amount n = Amount.Min(u.Cargo[g], need);
                u.Cargo[g] -= n;
                need -= n;
            }
        }

        if (need <= Amount.Zero || _content.Units.HungerDeathPercent == 0)
        {
            return;
        }

        Span<int> p = u.People;
        int dead = 0;
        for (int k = 0; k < p.Length; k++)
        {
            int d = (int)Math.Max(p[k] > 0 ? 1 : 0, (long)p[k] * _content.Units.HungerDeathPercent / 100);
            d = Math.Min(d, p[k]);
            p[k] -= d;
            dead += d;
        }

        if (u.TotalPeople() == 0)
        {
            u.Gone = true;
            context.Chronicle($"Settlers of {civ.Name} starved on the way.");
        }
    }

    // The unit stands on its goal: settlers found their camp, a unit sent home joins it, a moved unit waits.
    private void Arrive(TurnContext context, GameState state, Unit u, CivilizationState civ, WorldState world)
    {
        switch (u.Order)
        {
            case UnitOrder.Move:
            case UnitOrder.Flee:
                u.Order = UnitOrder.Idle;
                return;
            case UnitOrder.Work:
                u.Next = -1; // at work on its task's cell (TaskSystem brings its yield home in the pulse)
                u.Progress = 0;
                return;
            case UnitOrder.Return when u.Episode >= 0:
                u.Order = UnitOrder.Idle; // an army home: its campaign brings the survivors in (CampaignSystem)
                return;
            case UnitOrder.Found:
                int site = FindSite(state, civ, world, u.Cell);
                if (site == u.Cell)
                {
                    Found(context, state, u, civ, world);
                }
                else if (site >= 0)
                {
                    u.Target = site; // taken: a free place close by
                }
                else
                {
                    u.Order = UnitOrder.Return;
                    context.Chronicle($"Settlers of {civ.Name} found no free land and turned home.");
                }

                return;
            case UnitOrder.Return when u.Role == UnitRole.Scouts:
                ScoutHome(state, u);
                return;
            case UnitOrder.Return:
                Rejoin(context, state, u, civ, world);
                return;
        }
    }

    // There is no known way to the goal: settlers try to found where they stand, others stop.
    private void NoWay(TurnContext context, GameState state, Unit u, CivilizationState civ, WorldState world)
    {
        if (u.Role == UnitRole.Settlers && u.Order is UnitOrder.Found or UnitOrder.Return && CanFound(_content, state, civ.Id, world, u.Cell))
        {
            Found(context, state, u, civ, world);
            return;
        }

        if (u.Episode < 0 && u.Order == UnitOrder.Return)
        {
            // No known way home: scouts and carriers find their way back by themselves (their people are not lost).
            if (u.Role == UnitRole.Scouts)
            {
                ScoutHome(state, u);
            }
            else if (u.Role != UnitRole.Settlers)
            {
                Rejoin(context, state, u, civ, world, now: true);
            }

            return;
        }

        if (u.Role == UnitRole.Workers)
        {
            u.Order = UnitOrder.Return; // no known way to its task: it comes home
            u.ForgetPath();
            return;
        }

        u.Order = UnitOrder.Idle; // an army or gathering warriors: their campaign sees it stand and decides
    }

    // The nearest cell within the search radius where the settlers may found (the goal itself first), or -1.
    private int FindSite(GameState state, CivilizationState civ, WorldState world, int cell)
    {
        if (CanFound(_content, state, civ.Id, world, cell))
        {
            return cell;
        }

        HexGlobe geo = world.Geometry;
        int radius = Math.Min(_content.Units.FoundSearchCells, geo.MaxUniqueRadius);
        if (radius <= 0)
        {
            return -1;
        }

        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(radius)];
        int count = geo.Range(geo.FromIndex(cell), radius, area);
        HexCoord at = geo.FromIndex(cell);
        int best = -1;
        int bestDistance = int.MaxValue;
        for (int k = 0; k < count; k++)
        {
            int c = geo.Index(area[k]);
            int d = geo.Distance(at, area[k]);
            if (d < bestDistance || (d == bestDistance && c < best))
            {
                if (MapMemory.Knows(_content, state, civ, c) && CanFound(_content, state, civ.Id, world, c))
                {
                    best = c;
                    bestDistance = d;
                }
            }
        }

        return best;
    }

    private void Found(TurnContext context, GameState state, Unit u, CivilizationState civ, WorldState world)
    {
        SettlementState camp = state.FoundSettlement(
            civ.Id, u.World, world.Geometry.FromIndex(u.Cell), Naming.Names.NextPlace(_content.Names, state, civ), _content.NewTribeLevel,
            _content.Goods.Count, _content.Strata.Count, _content.Skills.Count);
        camp.MovedMonth = state.Month;
        if (u.ByPlayer)
        {
            camp.PlayerOrderTick = state.Turn; // the player chose this place: the AI does not merge or move the new camp at once
        }

        Unload(u, camp);
        u.Gone = true;
        context.Chronicle($"{camp.TotalPopulation()} settlers of {civ.Name} founded {camp.Name}.");
    }

    // Back home: the people, their skills and their goods join the settlement again (or the nearest own one).
    private void Rejoin(TurnContext context, GameState state, Unit u, CivilizationState civ, WorldState world, bool now = false)
    {
        SettlementState? home = u.Home >= 0 ? state.FindSettlement(new SettlementId(u.Home)) : null;
        if (home is not { IsActive: true })
        {
            home = null;
            int best = int.MaxValue;
            HexCoord at = world.Geometry.FromIndex(u.Cell);
            foreach (SettlementState s in state.Settlements)
            {
                if (s.Owner == civ.Id && s.IsActive && s.World == u.World)
                {
                    int d = world.Geometry.Distance(at, s.Cell);
                    if (d < best)
                    {
                        best = d;
                        home = s;
                    }
                }
            }
        }

        if (home is null)
        {
            if (u.Role == UnitRole.Settlers && CanFound(_content, state, civ.Id, world, u.Cell))
            {
                Found(context, state, u, civ, world);
            }
            else
            {
                u.Gone = true;
            }

            return;
        }

        if (!now && world.Geometry.Index(home.Cell) != u.Cell && u.Home != home.Id.Value)
        {
            u.Home = home.Id.Value; // walk to the new home first
            u.ForgetPath();
            return;
        }

        Unload(u, home);
        u.Gone = true;
    }

    private static void Unload(Unit u, SettlementState into)
    {
        Span<int> people = into.Population;
        for (int k = 0; k < u.People.Length && k < people.Length; k++)
        {
            people[k] += u.People[k];
        }

        for (int k = 0; k < u.Skilled.Length && k < into.Skilled.Length; k++)
        {
            into.Skilled[k] += u.Skilled[k];
        }

        for (int k = 0; k < u.Trainees.Length && k < into.Trainees.Length; k++)
        {
            into.Trainees[k] += u.Trainees[k];
        }

        Span<Amount> stock = into.Stock;
        for (int g = 0; g < u.Cargo.Length && g < stock.Length; g++)
        {
            stock[g] += u.Cargo[g];
        }

        u.People.Clear();
        u.Cargo.Clear();
    }

    // The party's people (stage 6 PR 3b): people of the kind's skill first (hunters), from their stratum, then others of
    // that stratum who have no skill; they come back with the party (ScoutHome).
    private void TakeScouts(SettlementState s, Unit scout, UnitKindDefinition kind)
    {
        int want = kind.People;
        if (want <= 0)
        {
            return;
        }

        int stratum = kind.Skill >= 0 ? _content.Skills[kind.Skill].StratumId : 0;
        Span<int> people = s.Population;
        Span<int> skilled = s.Skilled;
        if (stratum >= people.Length)
        {
            return;
        }

        int bound = 0;
        for (int k = 0; k < skilled.Length && k < _content.Skills.Count; k++)
        {
            bound += _content.Skills[k].StratumId == stratum ? skilled[k] : 0;
        }

        int experts = kind.Skill >= 0 && kind.Skill < skilled.Length ? Math.Min(want, skilled[kind.Skill]) : 0;
        int plain = Math.Min(want - experts, Math.Max(0, people[stratum] - bound));
        if (experts > 0)
        {
            skilled[kind.Skill] -= experts;
            scout.Skilled[kind.Skill] = experts;
        }

        people[stratum] -= experts + plain;
        scout.People[stratum] = experts + plain;
    }

    // In its civilization's pulse every settlement with no scouts out sends a party once its last one has rested.
    private void SendScouts(GameState state)
    {
        UnitRules rules = _content.Units;
        if (rules.ScoutTripCells <= 0 || state.Worlds.Count == 0)
        {
            return;
        }

        WorldState earth = state.Worlds[0];
        HexGlobe geo = earth.Geometry;
        DeterministicRandom rng = state.Random[RandomStreamId.Population];
        long rest = rules.ScoutEveryYears * 12L;
        for (int k = 0; k < state.Settlements.Count; k++)
        {
            SettlementState s = state.Settlements[k]; // by index: no enumerator in a tick
            if (!s.IsActive || s.World != earth.Id || s.ScoutOut || s.TotalPopulation() == 0)
            {
                continue;
            }

            CivilizationState? civ = state.FindCivilization(s.Owner);
            if (civ is null || !WorldClock.Steps(civ) || civ.SeenYear.Length != geo.CellCount
                || (s.ScoutBackMonth >= 0 && state.ActionMonth - s.ScoutBackMonth < rest))
            {
                continue;
            }

            UnitKindDefinition kind = rules.Best(UnitRole.Scouts, civ.Knows);
            if (s.TotalPopulation() < kind.People * 4)
            {
                continue; // too few people to spare a party
            }

            Unit scout = state.Units.Add(
                kind, s.Owner, s.World, geo.Index(s.Cell), s.Id.Value, UnitOrder.Scout,
                _content.Strata.Count, _content.Skills.Count, _content.Goods.Count);
            TakeScouts(s, scout, kind);
            scout.Direction = rng.NextInt(6);
            if (scout.Trail.Capacity < rules.ScoutTripCells + 2)
            {
                scout.Trail.Capacity = rules.ScoutTripCells + 2; // once per slot of the pool: a walking scout allocates nothing
            }

            scout.Trail.Add(scout.Cell);
            s.ScoutOut = true;
        }
    }
}
