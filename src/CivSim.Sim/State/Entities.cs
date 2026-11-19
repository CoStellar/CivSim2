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

    /// <summary>Waiting for its materials: porters carry them from the nearest storehouse; building starts when they arrive.</summary>
    Delivery = 5,
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

    /// <summary>Every good it makes is full in its settlement: asleep until there is room.</summary>
    public bool Full { get; set; }

    /// <summary>Groups of porters bringing its materials right now.</summary>
    public int Porters { get; set; }

    /// <summary>Materials already at the site (hundredths, per good).</summary>
    public long[] Delivered { get; set; } = [];

    /// <summary>Materials carried to the site right now.</summary>
    public long[] OnTheWay { get; set; } = [];

    /// <summary>Ticks of work the delivered materials allow (construction stops there until more arrive).</summary>
    public int WorkCap { get; set; }

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

    /// <summary>Goods stored here, hundredths (goods lie where they were made).</summary>
    public long[] Stock { get; set; } = [];

    /// <summary>Room per good, hundredths: the settlement's own plus every building's.</summary>
    public long[] Capacity { get; set; } = [];

    /// <summary>Net change per good per minute, hundredths.</summary>
    public long[] Rate { get; set; } = [];

    /// <summary>Carry of the economy step (hundredths × ticks).</summary>
    public long[] Remainder { get; set; } = [];

    /// <summary>Goods (bit per good) that were empty at the last economy step.</summary>
    public long EmptyMask { get; set; }

    /// <summary>Goods (bit per good) that were full at the last economy step.</summary>
    public long FullMask { get; set; }

    /// <summary>Out of food.</summary>
    public bool Hungry { get; set; }

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

    /// <summary>Era reached: a level, raised by knowing enough technologies of the era before.</summary>
    public int Era { get; set; }

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

    /// <summary>How far below the best free place the AI may start for this people, percent (random per people).</summary>
    public int StartError { get; set; }

    /// <summary>Score of the best free start when this people chose (for the summary).</summary>
    public int BestStartAtPick { get; set; }

    /// <summary>Current event of the shared pool (−1 = none).</summary>
    public int Event { get; set; } = -1;

    /// <summary>Tick the current event ends.</summary>
    public long EventEnd { get; set; }

    /// <summary>Tick of the next event (0 = not drawn yet).</summary>
    public long NextEvent { get; set; }

    /// <summary>A sample of the people's state every minute, for the summary after the game.</summary>
    public List<HistorySample> History { get; } = [];
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

    /// <summary>Goods carried (porters, caravans), hundredths; null for units that carry nothing.</summary>
    public long[]? Cargo { get; set; }

    /// <summary>What a carrier does: porters to a building, caravans to a settlement.</summary>
    public CarrierJob Job { get; set; }

    /// <summary>Building (porters) or settlement (caravans) the cargo goes to.</summary>
    public int Target { get; set; } = -1;

    /// <summary>Settlement the carriers come from and go back to.</summary>
    public int Home { get; set; } = -1;

    /// <summary>Hex the carriers go back to (their storehouse).</summary>
    public int ReturnCell { get; set; } = -1;

    /// <summary>Trade route that sent this caravan (−1 = none).</summary>
    public int Route { get; set; } = -1;
}

/// <summary>One minute of a people's history: what the summary's timeline shows.</summary>
public sealed record HistorySample(int Minute, int People, int Settlements, int Buildings, int Techs, int Soldiers, long FoodPerMinute, int Era, int Event);

/// <summary>What a carrier unit is doing.</summary>
public enum CarrierJob : byte
{
    /// <summary>Not a carrier.</summary>
    None = 0,

    /// <summary>Carrying materials to a construction.</summary>
    ToBuilding = 1,

    /// <summary>Carrying goods to a settlement (ours or a trade partner's).</summary>
    ToSettlement = 2,

    /// <summary>Walking home empty; joins its settlement on arrival.</summary>
    Returning = 3,
}

/// <summary>A standing caravan route between two settlements of one people.</summary>
public sealed class TradeRoute
{
    /// <summary>Owner.</summary>
    public int Civ { get; init; }

    /// <summary>Settlement the goods leave.</summary>
    public int From { get; init; }

    /// <summary>Settlement the goods go to.</summary>
    public int To { get; init; }

    /// <summary>Good carried.</summary>
    public int Good { get; init; }

    /// <summary>Amount per trip, hundredths.</summary>
    public long Amount { get; init; }

    /// <summary>Caravan on the road (−1 = none).</summary>
    public int Caravan { get; set; } = -1;

    /// <summary>Ended by the player.</summary>
    public bool Ended { get; set; }
}
