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

    /// <summary>Hundredths of a person of damage taken but not yet a death.</summary>
    public long Wounds { get; set; }

    /// <summary>Under attack in the last combat step (for the view).</summary>
    public bool Besieged { get; set; }
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

    /// <summary>Technologies known.</summary>
    public bool[] KnownTech { get; set; } = [];

    /// <summary>Experience per technology, worker-ticks.</summary>
    public long[] Experience { get; set; } = [];

    /// <summary>Workers now giving experience per technology (recomputed with the economy).</summary>
    public int[] ExperienceRate { get; set; } = [];

    /// <summary>Technology being researched (−1 = none).</summary>
    public int Researching { get; set; } = -1;

    /// <summary>Tick the research ends.</summary>
    public long ResearchEnd { get; set; }

    /// <summary>The thinker (one at a time).</summary>
    public Person? Thinker { get; set; }

    /// <summary>A thinker offering service, or null.</summary>
    public Person? ThinkerOffer { get; set; }

    /// <summary>The advisor (one at most).</summary>
    public Person? Advisor { get; set; }

    /// <summary>An advisor offering service, or null.</summary>
    public Person? AdvisorOffer { get; set; }

    /// <summary>Tick of the next thinker candidate.</summary>
    public long NextThinkerOffer { get; set; }

    /// <summary>Tick of the next advisor candidate.</summary>
    public long NextAdvisorOffer { get; set; }

    /// <summary>Language for names of places and people.</summary>
    public CivSim.Core.Naming.NameLanguage? Language { get; set; }

    /// <summary>Names taken so far.</summary>
    public int NamesUsed { get; set; }

    /// <summary>Places named so far.</summary>
    public int PlacesNamed { get; set; }

    /// <summary>How other peoples see this one: falls with surprise attacks and broken truces.</summary>
    public int Reputation { get; set; }

    /// <summary>Led by the AI (false for a person).</summary>
    public bool IsAi { get; set; } = true;

    /// <summary>Character of an AI: how warlike, 0–100.</summary>
    public int Aggression { get; set; }

    /// <summary>Character of an AI: how eager to spread, 0–100.</summary>
    public int Expansion { get; set; }
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

    /// <summary>Enemy unit it was ordered to attack (−1 = none).</summary>
    public int TargetUnit { get; set; } = -1;

    /// <summary>Enemy settlement it was ordered to attack (−1 = none).</summary>
    public int TargetSettlement { get; set; } = -1;

    /// <summary>Hundredths of a person of damage taken but not yet a death.</summary>
    public long Wounds { get; set; }

    /// <summary>Fought in the last combat step (for the view).</summary>
    public bool Fighting { get; set; }
}
