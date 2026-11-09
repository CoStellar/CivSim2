using System;
using System.Collections.Generic;

namespace CivSim.Core.Units;

/// <summary>
/// What a kind of unit is for (stage 6 PR 3a–3b). The kinds themselves are data (units.json: new kinds of later eras, such
/// as archers, riflemen, cavalry or tanks, are new entries of an existing role). Values are part of the save format: only append.
/// </summary>
public enum UnitRole : byte
{
    /// <summary>People who leave a settlement to found a new camp where they are sent.</summary>
    Settlers = 0,

    /// <summary>A few hunters who walk into the land around their settlement and come back with what they saw.</summary>
    Scouts = 1,

    /// <summary>Warriors of a campaign: they march, fight, flee, come home and rest.</summary>
    Army = 2,

    /// <summary>People who carry food from a settlement to an army and go home.</summary>
    Carriers = 3,
}

/// <summary>
/// A kind of unit (data/units.json): its role, what it needs, how it moves, sees and reaches (stage 6 PR 3a–3b). Later eras
/// add kinds with their own speed, terrain and range; a campaign marches as the best army kind its civilization knows.
/// </summary>
public sealed class UnitKindDefinition
{
    /// <summary>Index in <see cref="UnitRules.Kinds"/> (saved with every unit: kinds are only appended).</summary>
    public int Id { get; init; }

    /// <summary>Key in the data.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What it is for.</summary>
    public UnitRole Role { get; init; }

    /// <summary>Technology the civilization must know for this kind, or -1 (none).</summary>
    public int RequiresTechnology { get; init; } = -1;

    /// <summary>Ticks to cross one flat, open cell (10 ticks = 1 s at x1).</summary>
    public int TicksPerCell { get; init; } = 20;

    /// <summary>Cells it sees around itself as it walks.</summary>
    public int SightCells { get; init; } = 1;

    /// <summary>
    /// Armies: how far it fights from, in cells (1 = next to the target: sticks, spears, swords; more for bows, guns,
    /// artillery in later eras).
    /// </summary>
    public int RangeCells { get; init; } = 1;

    /// <summary>Scouts: people of the settlement in one party (hunters first), who come back with it.</summary>
    public int People { get; init; }

    /// <summary>Extra cost of hills for this kind, percent, or -1 for <see cref="UnitRules.HillsExtraPercent"/>.</summary>
    public int HillsExtraPercent { get; init; } = -1;

    /// <summary>Extra cost of mountains for this kind, percent, or -1 for <see cref="UnitRules.MountainsExtraPercent"/>.</summary>
    public int MountainsExtraPercent { get; init; } = -1;

    /// <summary>Extra cost of a river for this kind, percent, or -1 for <see cref="UnitRules.RiverPercent"/>.</summary>
    public int RiverPercent { get; init; } = -1;

    /// <summary>
    /// Cost of a biome for this kind, percent, by biome index; 0 = the biome's movementCost × 100 (cavalry fast on the
    /// plains, motorized troops slow in swamps: later eras).
    /// </summary>
    public IReadOnlyList<int> BiomePercent { get; init; } = [];
}

/// <summary>Rules of units: speed, terrain, scouting, settlers (data/units.json, stage 6 PR 3a).</summary>
public sealed record UnitRules
{
    /// <summary>The kinds, in the order of the data (<see cref="UnitKindDefinition.Id"/>).</summary>
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
            kinds.Add(new UnitKindDefinition
            {
                Id = k.Id, Key = k.Key, Name = k.Name, Role = k.Role, RequiresTechnology = k.RequiresTechnology, TicksPerCell = ticksPerCell,
                SightCells = k.SightCells, RangeCells = k.RangeCells, People = k.People, HillsExtraPercent = k.HillsExtraPercent,
                MountainsExtraPercent = k.MountainsExtraPercent, RiverPercent = k.RiverPercent, BiomePercent = k.BiomePercent,
            });
        }

        return this with { Kinds = kinds, JourneyTicksPerCell = ticksPerCell };
    }

    /// <summary>The kind with <paramref name="id"/>.</summary>
    public UnitKindDefinition Of(int id) =>
        (uint)id < (uint)Kinds.Count ? Kinds[id] : throw new ArgumentOutOfRangeException(nameof(id));

    /// <summary>The first kind of <paramref name="role"/> (the one that needs nothing).</summary>
    public UnitKindDefinition First(UnitRole role)
    {
        foreach (UnitKindDefinition k in Kinds)
        {
            if (k.Role == role)
            {
                return k;
            }
        }

        throw new InvalidOperationException($"No unit kind with the role {role}.");
    }

    /// <summary>
    /// The best kind of <paramref name="role"/> <paramref name="knows"/> allows: the last one in the data whose technology is
    /// known (kinds are listed from the simplest), else the first.
    /// </summary>
    public UnitKindDefinition Best(UnitRole role, Func<int, bool> knows)
    {
        ArgumentNullException.ThrowIfNull(knows);
        UnitKindDefinition best = First(role);
        foreach (UnitKindDefinition k in Kinds)
        {
            if (k.Role == role && (k.RequiresTechnology < 0 || knows(k.RequiresTechnology)))
            {
                best = k;
            }
        }

        return best;
    }
}
