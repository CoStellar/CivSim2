using System;
using System.Collections.Generic;

namespace CivSim.Core.Units;

/// <summary>Kinds of units (stage 6 PR 3a, ADR 0024). Values are part of the save format: only append.</summary>
public enum UnitKind : byte
{
    /// <summary>People who leave a settlement to found a new camp where they are sent.</summary>
    Settlers = 0,

    /// <summary>A few hunters who walk into the land around their settlement and come back with what they saw.</summary>
    Scouts = 1,
}

/// <summary>How a kind of unit moves and sees (data/units.json).</summary>
public sealed class UnitKindDefinition
{
    /// <summary>The kind.</summary>
    public UnitKind Kind { get; init; }

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Ticks to cross one flat, open cell (10 ticks = 1 s at x1).</summary>
    public int TicksPerCell { get; init; } = 20;

    /// <summary>Cells it sees around itself as it walks.</summary>
    public int SightCells { get; init; } = 1;
}

/// <summary>Rules of units: speed, terrain, scouting, settlers (data/units.json, stage 6 PR 3a).</summary>
public sealed record UnitRules
{
    /// <summary>The kinds, indexed by <see cref="UnitKind"/>.</summary>
    public IReadOnlyList<UnitKindDefinition> Kinds { get; init; } = [];

    /// <summary>Ticks a migrating camp (a journey) needs to cross one flat, open cell.</summary>
    public int JourneyTicksPerCell { get; init; } = 20;

    /// <summary>
    /// Extra cost of hills, percent of a flat cell, on top of the biome's cost (biomes.json movementCost × 100: grassland 100,
    /// forests 200, swamp and jungle 300).
    /// </summary>
    public int HillsExtraPercent { get; init; } = 50;

    /// <summary>Extra cost of mountains, percent of a flat cell, on top of the biome's cost.</summary>
    public int MountainsExtraPercent { get; init; } = 200;

    /// <summary>Cost of walking over sea ice, percent of a flat cell.</summary>
    public int IcePercent { get; init; } = 200;

    /// <summary>Extra cost of crossing a river on the edge between two cells, percent of a flat cell.</summary>
    public int RiverPercent { get; init; } = 100;

    /// <summary>Most cells one route search may look at (keeps a tick short).</summary>
    public int RouteNodeLimit { get; init; } = 20000;

    /// <summary>A settlement sends a new scout this many years of action after its last one came back.</summary>
    public int ScoutEveryYears { get; init; } = 5;

    /// <summary>Cells a scout walks out before it turns back.</summary>
    public int ScoutTripCells { get; init; } = 15;

    /// <summary>Months of action settlers carry food for when they leave (as much as there is to spare).</summary>
    public int SettlerFoodMonths { get; init; } = 6;

    /// <summary>Percent of hungry settlers who die in every month of action without food.</summary>
    public int HungerDeathPercent { get; init; } = 5;

    /// <summary>When the chosen place is taken on arrival, settlers look for another within this many cells.</summary>
    public int FoundSearchCells { get; init; } = 3;

    /// <summary>
    /// A copy in which every kind and a migrating camp cross a flat cell in <paramref name="ticksPerCell"/> ticks (tests,
    /// whose pulse is one tick, walk a cell in a tick with it).
    /// </summary>
    public UnitRules WithSpeed(int ticksPerCell)
    {
        var kinds = new List<UnitKindDefinition>();
        foreach (UnitKindDefinition k in Kinds)
        {
            kinds.Add(new UnitKindDefinition { Kind = k.Kind, Name = k.Name, TicksPerCell = ticksPerCell, SightCells = k.SightCells });
        }

        return this with { Kinds = kinds, JourneyTicksPerCell = ticksPerCell };
    }

    /// <summary>The definition of <paramref name="kind"/>.</summary>
    public UnitKindDefinition Of(UnitKind kind) =>
        (int)kind < Kinds.Count ? Kinds[(int)kind] : throw new ArgumentOutOfRangeException(nameof(kind));
}
