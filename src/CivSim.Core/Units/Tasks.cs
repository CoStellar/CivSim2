using System;
using System.Collections.Generic;
using CivSim.Core.Commands;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Numerics;
using CivSim.Core.Perception;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Core.Units;

/// <summary>Where the work of an area task comes from (data/tasks.json).</summary>
public enum TaskSource : byte
{
    /// <summary>The biome of the cell gives the good (biomes.json gather); the yield grows with the biome's value.</summary>
    Biome = 0,

    /// <summary>The cell has the deposit on the surface; the deposit loses its richness and the task ends when it runs out.</summary>
    Deposit = 1,

    /// <summary>
    /// A field (stage 6 PR 4b-2): fertile land, farmed as the farming rule of population.json says (grain by fertility and growing
    /// season, at most farmersPerCell people, the land wears out and rests).
    /// </summary>
    Farmland = 2,

    /// <summary>A fishing ground (stage 6 PR 4b-2): land next to water; the yield grows with the best water next to it (the kind's waters).</summary>
    Shore = 3,
}

/// <summary>A kind of area task (stage 6 PR 4a, GDD 4.4): woodcutting, a flint mine, a quarry; later fields, fishing grounds, building.</summary>
public sealed class TaskKindDefinition
{
    /// <summary>Index in <see cref="TaskRules.Kinds"/>.</summary>
    public int Id { get; init; }

    /// <summary>Data key, e.g. <c>woodcutting</c>.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Name shown to players.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Where the work comes from.</summary>
    public TaskSource Source { get; init; }

    /// <summary>The good made (index in goods).</summary>
    public int Good { get; init; }

    /// <summary>Deposit id (map value) for <see cref="TaskSource.Deposit"/>, else 0.</summary>
    public int Deposit { get; init; }

    /// <summary>Yield of one person in a year: per unit of the biome's gather value (biome kinds) or as it is (deposit kinds).</summary>
    public Amount PerPersonPerYear { get; init; }

    /// <summary>Technology needed, or -1.</summary>
    public int RequiresTechnology { get; init; } = -1;

    /// <summary>Preferred skill of the group's people (their stratum works), or -1.</summary>
    public int Skill { get; init; } = -1;

    /// <summary>Only a settlement that cannot move may run it (fields of a village, stage 6 PR 4b-2).</summary>
    public bool SettledOnly { get; init; }

    /// <summary>For <see cref="TaskSource.Shore"/>: the value of every biome's water next to the cell (index = biome id, 0 = none).</summary>
    public IReadOnlyList<Amount> Waters { get; init; } = [];

    /// <summary>True when the task makes food (fields, fishing grounds): the governor keeps its skilled people at it (G9, stage 6 PR 4b-2).</summary>
    public bool MakesFood { get; init; }
}

/// <summary>Rules of area tasks (data/tasks.json, stage 6 PR 4a).</summary>
public sealed record TaskRules
{
    /// <summary>Task kinds, index = id.</summary>
    public IReadOnlyList<TaskKindDefinition> Kinds { get; init; } = [];

    /// <summary>While true settlements still gather wood, flint and stone from their whole zone (until the AI gives tasks, PR 4b).</summary>
    public bool ZoneGathersMaterials { get; init; } = true;

    /// <summary>While true a village's farmers farm the best land of its zone by themselves; false: only fields given as tasks (stage 6 PR 4b-2).</summary>
    public bool ZoneFarms { get; init; } = true;

    /// <summary>While true gatherers also fish the water next to the zone; false: fish only from fishing grounds given as tasks (stage 6 PR 4b-2).</summary>
    public bool ZoneFishes { get; init; } = true;

    /// <summary>How far beyond the settlement's zone a task may be given, in cells.</summary>
    public int RangeBeyondZoneCells { get; init; } = 2;

    /// <summary>A settlement runs one task for every this many people.</summary>
    public int PeoplePerTask { get; init; } = 25;

    /// <summary>People of a group when the command gives none.</summary>
    public int DefaultPeoplePerGroup { get; init; } = 10;

    /// <summary>Most people of one group.</summary>
    public int MaxPeoplePerGroup { get; init; } = 30;

    /// <summary>The kind with <paramref name="key"/>, or null.</summary>
    public TaskKindDefinition? Find(string key)
    {
        foreach (TaskKindDefinition k in Kinds)
        {
            if (k.Key == key)
            {
                return k;
            }
        }

        return null;
    }
}

/// <summary>Rules every task command and the task system share (stage 6 PR 4a).</summary>
public static class AreaTasks
{
    /// <summary>Work groups of <paramref name="settlement"/> out now.</summary>
    public static int RunningAt(GameState state, int settlement)
    {
        ArgumentNullException.ThrowIfNull(state);
        int n = 0;
        UnitBook book = state.Units;
        for (int k = 0; k < book.Count; k++)
        {
            Unit u = book[k];
            if (!u.Gone && u.Role == UnitRole.Workers && u.Home == settlement)
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>How many tasks <paramref name="s"/> may run at once: one for every <see cref="TaskRules.PeoplePerTask"/> of its people.</summary>
    public static int Allowed(GameContent content, GameState state, SettlementState s)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        int people = s.TotalPopulation();
        for (int k = 0; k < state.Units.Count; k++)
        {
            Unit u = state.Units[k];
            if (!u.Gone && u.Role == UnitRole.Workers && u.Home == s.Id.Value)
            {
                people += u.TotalPeople(); // its groups still belong to it
            }
        }

        return people / Math.Max(1, content.Tasks.PeoplePerTask);
    }

    /// <summary>True when <paramref name="cell"/> gives the task's work: the biome gives its good, or the deposit is on the surface.</summary>
    public static bool Fits(GameContent content, WorldMap map, TaskKindDefinition kind, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(kind);
        return kind.Source switch
        {
            TaskSource.Deposit => map.Deposit[cell] == kind.Deposit && map.DepositDepth[cell] == 0 && map.DepositRichness[cell] > 0,
            TaskSource.Farmland => Economy.FarmingSystem.GrainPerFarmer(content, map, cell) > 0,
            TaskSource.Shore => false, // needs the map's geometry: see Fits(content, world, kind, cell)
            _ => BiomeValue(content, map, kind, cell) > Amount.Zero,
        };
    }

    /// <summary>Like <see cref="Fits(GameContent, WorldMap, TaskKindDefinition, int)"/>, with the neighbours a fishing ground needs.</summary>
    public static bool Fits(GameContent content, WorldState world, TaskKindDefinition kind, int cell)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(kind);
        return kind.Source == TaskSource.Shore
            ? !content.Biomes[world.Map.Biome[cell]].IsWater && WaterValue(content, world, kind, cell) > Amount.Zero
            : Fits(content, world.Map, kind, cell);
    }

    /// <summary>The best water next to <paramref name="cell"/> for a fishing ground (the kind's waters), or zero.</summary>
    public static Amount WaterValue(GameContent content, WorldState world, TaskKindDefinition kind, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(kind);
        Span<HexCoord> n6 = stackalloc HexCoord[6];
        world.Geometry.GetNeighbors(world.Geometry.FromIndex(cell), n6);
        Amount best = Amount.Zero;
        for (int k = 0; k < n6.Length; k++)
        {
            int b = world.Map.Biome[world.Geometry.Index(n6[k])];
            if (b < kind.Waters.Count && kind.Waters[b] > best)
            {
                best = kind.Waters[b];
            }
        }

        return best;
    }

    /// <summary>The biome's gather value of the task's good on <paramref name="cell"/> (biomes.json gather).</summary>
    public static Amount BiomeValue(GameContent content, WorldMap map, TaskKindDefinition kind, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(kind);
        BiomeDefinition biome = content.Biomes[map.Biome[cell]];
        foreach (GoodQuantity g in biome.Gather)
        {
            if (g.GoodId == kind.Good)
            {
                return g.Amount;
            }
        }

        return Amount.Zero;
    }

    /// <summary>
    /// True when <paramref name="cell"/> is claimed by <paramref name="s"/> or at most <see cref="TaskRules.RangeBeyondZoneCells"/>
    /// cells from a cell it claims.
    /// </summary>
    public static bool InReach(GameContent content, WorldState world, SettlementState s, int cell)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(s);
        int claim = s.Id.Value + 1;
        if (world.Map.Claim[cell] == claim || world.Geometry.Index(s.Cell) == cell)
        {
            return true;
        }

        int radius = Math.Min(content.Tasks.RangeBeyondZoneCells, Math.Min(6, world.Geometry.MaxUniqueRadius));
        if (radius <= 0)
        {
            return false;
        }

        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(radius)];
        int count = world.Geometry.Range(world.Geometry.FromIndex(cell), radius, area);
        for (int k = 0; k < count; k++)
        {
            if (world.Map.Claim[world.Geometry.Index(area[k])] == claim)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The stratum whose people do the plain work of every task: the first stratum that gathers (producers).</summary>
    public static int LabourStratum(GameContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        for (int k = 0; k < content.Strata.Count; k++)
        {
            if (content.Strata[k].Gathers)
            {
                return k;
            }
        }

        return 0;
    }

    /// <summary>People of the task's skill in <paramref name="s"/> (they go first, from their own stratum), or 0.</summary>
    public static int Experts(GameContent content, SettlementState s, TaskKindDefinition kind)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(kind);
        if (kind.Skill < 0 || kind.Skill >= s.Skilled.Length || kind.Skill >= content.Skills.Count)
        {
            return 0;
        }

        int stratum = content.Skills[kind.Skill].StratumId;
        return stratum < s.PopulationView.Length ? Math.Min(s.Skilled[kind.Skill], s.PopulationView[stratum]) : 0;
    }

    /// <summary>
    /// People of <paramref name="s"/> free to go: the experts of the task's skill and the producers not bound to another skill of theirs
    /// (hunters, fishers, farmers keep their work).
    /// </summary>
    public static int FreePeople(GameContent content, SettlementState s, TaskKindDefinition kind)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(s);
        int labour = LabourStratum(content);
        ReadOnlySpan<int> people = s.PopulationView;
        if (labour >= people.Length)
        {
            return 0;
        }

        int bound = 0;
        Span<int> skilled = s.Skilled;
        for (int k = 0; k < skilled.Length && k < content.Skills.Count; k++)
        {
            bound += content.Skills[k].StratumId == labour && k != kind.Skill ? skilled[k] : 0;
        }

        int experts = Experts(content, s, kind);
        int plain = Math.Max(0, people[labour] - bound - (kind.Skill >= 0 && kind.Skill < content.Skills.Count && content.Skills[kind.Skill].StratumId == labour ? experts : 0));
        return experts + plain;
    }

    /// <summary>
    /// What a group of <paramref name="people"/> makes on <paramref name="cell"/> in <paramref name="months"/> months
    /// (people × perPersonPerYear × the biome's value for biome kinds × months / 12).
    /// </summary>
    public static Amount Yield(GameContent content, WorldMap map, TaskKindDefinition kind, int cell, int people, int months)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(kind);
        Amount perYear = kind.PerPersonPerYear * people;
        if (kind.Source == TaskSource.Biome)
        {
            perYear = perYear.MultiplyRatio(BiomeValue(content, map, kind, cell).Hundredths, 100, Rounding.Floor);
        }
        else if (kind.Source == TaskSource.Farmland)
        {
            // A field: the farming rule (population.json farming), at most farmersPerCell farmers on the cell.
            perYear = Amount.FromHundredths(Economy.FarmingSystem.GrainPerFarmer(content, map, cell) * Math.Min(people, content.Population.FarmersPerCell));
        }

        return perYear.MultiplyRatio(months, 12, Rounding.Floor);
    }

    /// <summary>Like <see cref="Yield(GameContent, WorldMap, TaskKindDefinition, int, int, int)"/>, with the water a fishing ground stands by.</summary>
    public static Amount Yield(GameContent content, WorldState world, TaskKindDefinition kind, int cell, int people, int months)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(kind);
        if (kind.Source != TaskSource.Shore)
        {
            return Yield(content, world.Map, kind, cell, people, months);
        }

        Amount perYear = (kind.PerPersonPerYear * people).MultiplyRatio(WaterValue(content, world, kind, cell).Hundredths, 100, Rounding.Floor);
        return perYear.MultiplyRatio(months, 12, Rounding.Floor);
    }
}

/// <summary>
/// Start an area task (stage 6 PR 4a, GDD 4.4): <paramref name="Settlement"/> sends a work group of <paramref name="People"/> people
/// (0 = the default size) to <paramref name="Cell"/> to do the task of kind <paramref name="Kind"/>. Players and the AI give it alike.
/// Call the group back with <see cref="ReturnUnitCommand"/>.
/// </summary>
public sealed record StartTaskCommand(GameContent Content, SettlementId Settlement, string Kind, HexCoord Cell, int People = 0) : ICommand
{
    /// <inheritdoc />
    public CommandCategory Category => CommandCategory.Economy;

    /// <inheritdoc />
    public CommandResult Validate(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState? s = state.FindSettlement(Settlement);
        if (s is not { IsActive: true } || s.Owner != civ)
        {
            return CommandResult.Fail("That settlement is not ours.");
        }

        TaskKindDefinition? kind = Content.Tasks.Find(Kind ?? string.Empty);
        if (kind is null)
        {
            return CommandResult.Fail($"There is no task '{Kind}'.");
        }

        CivilizationState c = state.FindCivilization(civ)!;
        if (kind.RequiresTechnology >= 0 && !c.Knows(kind.RequiresTechnology))
        {
            return CommandResult.Fail($"{kind.Name} needs {Content.Technologies[kind.RequiresTechnology].Name}.");
        }

        WorldState world = state.Worlds[s.World.Value];
        if (!world.Geometry.Contains(Cell))
        {
            return CommandResult.Fail("That cell is off the map.");
        }

        int cell = world.Geometry.Index(Cell);
        if (!MapMemory.Walkable(Content, world.Map, cell) || !MapMemory.Knows(Content, state, c, cell))
        {
            return CommandResult.Fail("Tasks can only be given on known land.");
        }

        short owner = world.Map.Owner[cell];
        if (owner != WorldMap.NoOwner && owner != civ.Value)
        {
            return CommandResult.Fail("That land is someone else's.");
        }

        if (!AreaTasks.InReach(Content, world, s, cell))
        {
            return CommandResult.Fail($"Tasks reach at most {Content.Tasks.RangeBeyondZoneCells} cells beyond the settlement's land.");
        }

        if (kind.SettledOnly && Content.SettlementLevels[s.Level].Mobile)
        {
            return CommandResult.Fail($"Only a settled village runs {kind.Name}.");
        }

        if (!AreaTasks.Fits(Content, world, kind, cell))
        {
            return CommandResult.Fail(kind.Source switch
            {
                TaskSource.Deposit => $"{kind.Name} needs {Content.Deposit(kind.Deposit).Name} on that cell.",
                TaskSource.Farmland => "That land is too poor to farm.",
                TaskSource.Shore => $"{kind.Name} needs fishing water next to the cell.",
                _ => $"That land gives no {Content.Goods[kind.Good].Name}.",
            });
        }

        for (int k = 0; k < state.Units.Count; k++)
        {
            Unit u = state.Units[k];
            if (!u.Gone && u.Role == UnitRole.Workers && u.Owner == civ && u.Target == cell && u.World == s.World)
            {
                return CommandResult.Fail("A work group of ours already works there.");
            }
        }

        if (AreaTasks.RunningAt(state, s.Id.Value) >= AreaTasks.Allowed(Content, state, s))
        {
            return CommandResult.Fail($"{s.Name} runs one task for every {Content.Tasks.PeoplePerTask} people.");
        }

        int people = People <= 0 ? Content.Tasks.DefaultPeoplePerGroup : People;
        if (people > Content.Tasks.MaxPeoplePerGroup)
        {
            return CommandResult.Fail($"A work group has at most {Content.Tasks.MaxPeoplePerGroup} people.");
        }

        if (AreaTasks.FreePeople(Content, s, kind) < people || s.TotalPopulation() - people < 1)
        {
            return CommandResult.Fail($"{s.Name} has not {people} free people for {kind.Name}.");
        }

        if (cell != world.Geometry.Index(s.Cell))
        {
            UnitKindDefinition walker = Content.Units.First(UnitRole.Workers);
            if (new RouteFinder().RouteTicks(Content, state, c, world, world.Geometry.Index(s.Cell), cell, walker.TicksPerCell, walker) < 0)
            {
                return CommandResult.Fail("The work group knows no way there.");
            }
        }

        return CommandResult.Ok;
    }

    /// <inheritdoc />
    public void Apply(GameState state, CivId civ)
    {
        ArgumentNullException.ThrowIfNull(state);
        SettlementState s = state.FindSettlement(Settlement)!;
        TaskKindDefinition kind = Content.Tasks.Find(Kind)!;
        WorldState world = state.Worlds[s.World.Value];
        UnitKindDefinition walker = Content.Units.First(UnitRole.Workers);
        Unit u = state.Units.Add(walker, civ, s.World, world.Geometry.Index(s.Cell), s.Id.Value, UnitOrder.Work,
            Content.Strata.Count, Content.Skills.Count, Content.Goods.Count);
        u.Target = world.Geometry.Index(Cell);
        u.Task = kind.Id;
        int want = People <= 0 ? Content.Tasks.DefaultPeoplePerGroup : People;
        int experts = Math.Min(want, AreaTasks.Experts(Content, s, kind));
        if (experts > 0)
        {
            int stratum = Content.Skills[kind.Skill].StratumId;
            s.Skilled[kind.Skill] -= experts;
            u.Skilled[kind.Skill] = experts;
            s.Population[stratum] -= experts;
            u.People[stratum] += experts;
        }

        int labour = AreaTasks.LabourStratum(Content);
        s.Population[labour] -= want - experts;
        u.People[labour] += want - experts;
    }
}

/// <summary>
/// Area tasks in the civilization's pulse (stage 6 PR 4a): every work group standing on its task's cell brings its yield to its home's stock;
/// a deposit loses its richness and a task whose deposit ran out, or whose home is gone, calls its group back.
/// </summary>
public sealed class TaskSystem(GameContent content) : ITurnSystem
{
    private readonly GameContent _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <inheritdoc />
    public TurnPhase Phase => TurnPhase.Economy;

    /// <inheritdoc />
    public void Execute(TurnContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        GameState state = context.State;
        UnitBook book = state.Units;
        for (int k = 0; k < book.Count; k++)
        {
            Unit u = book[k];
            if (u.Gone || u.Role != UnitRole.Workers || u.Order != UnitOrder.Work || u.Task < 0 || u.Task >= _content.Tasks.Kinds.Count)
            {
                continue;
            }

            int months = WorldClock.MonthsOf(state, u.Owner);
            if (months == 0)
            {
                continue; // its civilization's pulse is in another tick
            }

            SettlementState? home = state.FindSettlement(new SettlementId(u.Home));
            if (home is not { IsActive: true } || home.Owner != u.Owner)
            {
                u.Order = UnitOrder.Return; // nobody to work for: it joins the nearest own settlement
                u.ForgetPath();
                continue;
            }

            // The group eats with its settlement, in its pulse, from the home's stock, after what it made this pulse came in (a field
            // feeds its farmers first, stage 6 PR 4b-2); without food it comes home (stage 6 PR 4a).
            if (u.Cell == u.Target)
            {
                Work(context, u, home, months);
            }

            if (!u.Gone && u.Order == UnitOrder.Work && !Eat(home, u.TotalPeople(), months))
            {
                u.Order = UnitOrder.Return;
                u.ForgetPath();
                context.Chronicle($"The work group of {home.Name} ran out of food and comes home.", home.Owner.Value, inner: true);
            }
        }
    }

    // What the group at its task's cell makes in the pulse goes to its home; a task whose cell no longer gives its work ends.
    private void Work(TurnContext context, Unit u, SettlementState home, int months)
    {
        GameState state = context.State;
        TaskKindDefinition kind = _content.Tasks.Kinds[u.Task];
        WorldState world = state.Worlds[u.World.Value];
        WorldMap map = world.Map;
        if (!AreaTasks.Fits(_content, world, kind, u.Cell))
        {
            End(context, u, home, kind);
            return;
        }

        Amount made = AreaTasks.Yield(_content, world, kind, u.Cell, u.TotalPeople(), months);
        if (kind.MakesFood && state.Effects.Count > 0)
        {
            // Drought, wildfire and flood lower what the land gives; cooking and irrigation raise it (as farming in the zone did).
            int percent = (100 + state.Effects.Total(_content.Effects, EffectStat.ExtractionPercent, EffectTarget.Cell, u.World, u.Cell))
                * (100 + state.Effects.Total(_content.Effects, EffectStat.OutputPercent, EffectTarget.Settlement, home.World, home.Id.Value)
                    + state.Effects.Total(_content.Effects, EffectStat.OutputPercent, EffectTarget.Civilization, WorldId.Earth, home.Owner.Value)) / 100;
            made = made.MultiplyPercent(Math.Max(0, percent), Rounding.Floor);
        }

        if (kind.MakesFood && made > Amount.Zero)
        {
            home.FoodGathered += made.Hundredths;
        }

        if (kind.Source == TaskSource.Deposit)
        {
            // Flint and stone run out (GDD 4.4): the richness is in whole units.
            long left = map.DepositRichness[u.Cell];
            long taken = Math.Min(left * 100, made.Hundredths);
            made = Amount.FromHundredths(taken);
            left -= (taken + 99) / 100;
            if (left <= 0)
            {
                map.Deposit.Set(u.Cell, 0);
                map.DepositRichness.Set(u.Cell, 0);
            }
            else
            {
                map.DepositRichness.Set(u.Cell, (int)left);
            }
        }

        if (made > Amount.Zero)
        {
            home.Add(kind.Good, made);
        }

        if (!AreaTasks.Fits(_content, world, kind, u.Cell))
        {
            End(context, u, home, kind);
        }
    }

    // Takes the food of <paramref name="people"/> for <paramref name="months"/> months from the home's stock; false when it is short.
    private bool Eat(SettlementState home, int people, int months)
    {
        Amount need = Episodes.Campaigns.FoodFor(_content, people, months);
        Span<Amount> stock = home.Stock;
        for (int g = 0; g < stock.Length && g < _content.Goods.Count && need > Amount.Zero; g++)
        {
            if (_content.Goods[g].IsFood && stock[g] > Amount.Zero)
            {
                Amount n = Amount.Min(stock[g], need);
                stock[g] -= n;
                need -= n;
            }
        }

        return need <= Amount.Zero;
    }

    private static void End(TurnContext context, Unit u, SettlementState home, TaskKindDefinition kind)
    {
        u.Order = UnitOrder.Return;
        u.ForgetPath();
        // A worn field is the year's round of farming, not news (playtest, 2026-11-16: 18 lines a minute); a mine that runs dry is.
        if (kind.Source != TaskSource.Farmland)
        {
            context.Chronicle($"{kind.Name} of {home.Name} ran out; the work group comes home.", home.Owner.Value, inner: true);
        }
    }
}
