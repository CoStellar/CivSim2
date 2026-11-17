using System.Collections.Generic;

namespace CivSim.Sim.State;

/// <summary>What a building is doing.</summary>
public enum BuildingState : byte
{
    /// <summary>Being built; finishes at <see cref="Building.FinishTick"/>.</summary>
    Construction = 0,

    /// <summary>Construction stopped by the player; <see cref="Building.RemainingTicks"/> are left.</summary>
    ConstructionPaused = 1,

    /// <summary>Working (when it has workers and inputs).</summary>
    Active = 2,

    /// <summary>Stopped by the player: no workers, no production.</summary>
    Stopped = 3,

    /// <summary>Torn down; the id stays unused.</summary>
    Removed = 4,
}

/// <summary>One building on one hex. Identified by its index in <see cref="SimGame.Buildings"/>.</summary>
public sealed class Building
{
    /// <summary>Index in the game's list.</summary>
    public int Id { get; init; }

    /// <summary>Building type id.</summary>
    public int Type { get; init; }

    /// <summary>Hex index.</summary>
    public int Cell { get; init; }

    /// <summary>Owner civilization.</summary>
    public int Civ { get; set; }

    /// <summary>Settlement whose territory it stands on.</summary>
    public int Settlement { get; set; }

    /// <summary>What it is doing.</summary>
    public BuildingState State { get; set; }

    /// <summary>Tick construction ends (while under construction).</summary>
    public long FinishTick { get; set; }

    /// <summary>Ticks of construction left while paused.</summary>
    public int RemainingTicks { get; set; }

    /// <summary>Workers on the job (set when the economy is recomputed).</summary>
    public int Workers { get; set; }

    /// <summary>Workers are given to priority buildings first.</summary>
    public bool Priority { get; set; }

    /// <summary>An input is missing: works no more until the input comes back.</summary>
    public bool Starved { get; set; }

    /// <summary>Production multiplier from a bonus deposit, percent (100 = none).</summary>
    public int YieldPercent { get; init; } = 100;
}

/// <summary>A settlement: a centre hex and the territory around it.</summary>
public sealed class Settlement
{
    /// <summary>Index in the game's list.</summary>
    public int Id { get; init; }

    /// <summary>Owner.</summary>
    public int Civ { get; set; }

    /// <summary>Centre hex.</summary>
    public int Center { get; init; }

    /// <summary>Name.</summary>
    public string Name { get; set; } = "";

    /// <summary>Level id (camp, village…).</summary>
    public int Level { get; set; }

    /// <summary>People living here.</summary>
    public int People { get; set; }

    /// <summary>Part of a person toward the next birth or death, in hundredths × ticks.</summary>
    public long GrowthRemainder { get; set; }

    /// <summary>Housing (level plus houses), recomputed with the economy.</summary>
    public int Housing { get; set; }

    /// <summary>People without a job (recomputed).</summary>
    public int Idle { get; set; }

    /// <summary>Hexes of the territory.</summary>
    public List<int> Cells { get; } = [];

    /// <summary>Abandoned settlements keep their id but nothing else.</summary>
    public bool Abandoned { get; set; }
}

/// <summary>A civilization (player or AI).</summary>
public sealed class Civ
{
    /// <summary>Creates a civilization with stores for <paramref name="goods"/> goods.</summary>
    public Civ(int id, string name, int goods)
    {
        Id = id;
        Name = name;
        Stock = new long[goods];
        Capacity = new long[goods];
        Rate = new long[goods];
        Remainder = new long[goods];
    }

    /// <summary>Index in the game's list.</summary>
    public int Id { get; }

    /// <summary>Name of the people.</summary>
    public string Name { get; set; }

    /// <summary>Stock per good, hundredths (one store for the whole civilization).</summary>
    public long[] Stock { get; }

    /// <summary>Storage per good, hundredths.</summary>
    public long[] Capacity { get; }

    /// <summary>Net change per good per minute, hundredths (recomputed only when something changes).</summary>
    public long[] Rate { get; }

    /// <summary>Carry of the economy step (hundredths × ticks).</summary>
    public long[] Remainder { get; }

    /// <summary>Settlement ids.</summary>
    public List<int> Settlements { get; } = [];

    /// <summary>Building ids (removed ones are dropped).</summary>
    public List<int> Buildings { get; } = [];

    /// <summary>The economy must be recomputed before the next step.</summary>
    public bool Dirty { get; set; } = true;

    /// <summary>Food ran out at the last economy step.</summary>
    public bool Hungry { get; set; }

    /// <summary>Some buildings wait for an input.</summary>
    public bool AnyStarved { get; set; }

    /// <summary>Gone from the game.</summary>
    public bool Dead { get; set; }

    /// <summary>Total people (in settlements; units are counted apart).</summary>
    public int People { get; set; }

    /// <summary>Hexes this people has seen (fog of war).</summary>
    public bool[] Known { get; set; } = [];

    /// <summary>Map chunks with newly seen hexes since the server last looked (the server clears it).</summary>
    public HashSet<int> NewlyKnown { get; } = [];

    /// <summary>Unit ids.</summary>
    public List<int> Units { get; } = [];
}

/// <summary>What a unit is doing.</summary>
public enum UnitState : byte
{
    /// <summary>Being trained in its settlement; appears at <see cref="Unit.NextTick"/>.</summary>
    Training = 0,

    /// <summary>Standing on its hex.</summary>
    Idle = 1,

    /// <summary>Walking its path; the next step is at <see cref="Unit.NextTick"/>.</summary>
    Moving = 2,

    /// <summary>No longer in the game (founded a settlement, disbanded, destroyed).</summary>
    Gone = 3,
}

/// <summary>A group of people on one hex: settlers, scouts, later an army. Identified by its index in <see cref="SimGame.Units"/>.</summary>
public sealed class Unit
{
    /// <summary>Index in the game's list.</summary>
    public int Id { get; init; }

    /// <summary>Owner.</summary>
    public int Civ { get; init; }

    /// <summary>Unit kind id.</summary>
    public int Kind { get; init; }

    /// <summary>People in the group.</summary>
    public int People { get; set; }

    /// <summary>Hex it stands on (or the settlement centre while training).</summary>
    public int Cell { get; set; }

    /// <summary>What it is doing.</summary>
    public UnitState State { get; set; }

    /// <summary>Tick of the next step or of the end of training.</summary>
    public long NextTick { get; set; }

    /// <summary>Hexes still to walk, in order.</summary>
    public List<int> Path { get; } = [];

    /// <summary>Next index in <see cref="Path"/>.</summary>
    public int PathIndex { get; set; }

    /// <summary>Walks hidden (scouts): slower, seen only up close.</summary>
    public bool Hidden { get; set; }
}
