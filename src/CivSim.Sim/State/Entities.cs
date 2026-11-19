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
    /// <summary>Building speed of its construction, percent (builders' yards in its settlement when it was placed).</summary>
    public int Speed { get; set; } = 100;

    /// <summary>Branch of research each worker speeds (schools, academies, universities), one per job.</summary>
    public string[] Focus { get; set; } = [];

    /// <summary>Places of its hex it takes: bits 0–3 (top left, top right, bottom left, bottom right).</summary>
    public int Slots { get; set; } = 1;

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
    /// <summary>Its people gather food, wood and stone around it when they have no job (the player's order; on at the start).</summary>
    public bool Gathering { get; set; } = true;

    /// <summary>Richness of its land per good, percent (mean of its land hexes), worked out when its land changes.</summary>
    public int[] ForageRichness { get; set; } = [];

    /// <summary>What a worker does now, per mille (winter and clothing), as last worked out.</summary>
    public int WorkPermille { get; set; } = 1000;

    /// <summary>Homes in warm (stone) houses, worked out with the economy.</summary>
    public int WarmHomes { get; set; }

    /// <summary>Room for goods kept only in their own buildings (horses in stables), hundredths per good.</summary>
    public long[] LimitRoom { get; set; } = [];

    /// <summary>An aqueduct brings water into its land.</summary>
    public bool Aqueduct { get; set; }

    /// <summary>Growth its bathhouses and aqueduct add, percent of people a minute (worked out with the economy).</summary>
    public int GrowthBonus { get; set; }

    /// <summary>How much milder epidemics are here, percent (bathhouses, aqueduct).</summary>
    public int EpidemicResist { get; set; }

    /// <summary>Wounded soldiers its herbalists heal a minute (worked out with the economy).</summary>
    public int HealPerMinute { get; set; }

    /// <summary>Number of hexes the richness was worked out for (worked out again when the land grows).</summary>
    public int ForageCells { get; set; } = -1;

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

    /// <summary>Food everyone it feeds eats a minute (residents, its units, the advisor in the capital), paid at the ration.</summary>
    public long Eating { get; set; }

    /// <summary>Units it could not feed at the last ration.</summary>
    public int RationShort { get; set; }

    /// <summary>Residents it could not feed at the last ration (idle first, then workers).</summary>
    public int HungryPeople { get; set; }

    /// <summary>Of them, workers.</summary>
    public int HungryWorkers { get; set; }

    /// <summary>Missed rations in a row for its residents.</summary>
    public int HungerStreak { get; set; }

    /// <summary>Work of its workers in permille after hunger.</summary>
    public int WorkersFedPermille { get; set; } = 1000;

    /// <summary>Work of its people without a job (gathering) in permille after hunger.</summary>
    public int IdleFedPermille { get; set; } = 1000;

    /// <summary>People (residents and units) who starved to death at the last ration.</summary>
    public int RationDead { get; set; }

    /// <summary>Hundredths of a person of damage taken but not yet a death.</summary>
    public long Wounds { get; set; }

    /// <summary>Under attack in the last combat step (for the view).</summary>
    public bool Besieged { get; set; }

    /// <summary>People whose sight this settlement adds to (−1 = none).</summary>
    public int WatchCiv { get; set; } = -1;

    /// <summary>Sight radius counted for it.</summary>
    public int WatchRadius { get; set; }
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
        Made = new long[goods];
        MadeRate = new long[goods];
    }

    /// <summary>Goods made in all, hundredths × ticks per minute (experience by making: technologies that ask for goods made).</summary>
    public long[] Made { get; }

    /// <summary>Goods made per minute now, hundredths (recomputed with the economy).</summary>
    public long[] MadeRate { get; }

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

    /// <summary>Per hex: 1 + the era in which prospectors last looked at it (0 = never).</summary>
    public byte[] RockSeen { get; set; } = [];

    /// <summary>Per hex: surveyors have looked at its soil.</summary>
    public bool[] SoilSeen { get; set; } = [];

    /// <summary>Map chunks whose hexes were explored, came into sight or went out of it since the server last looked (the
    /// server clears it).</summary>
    public HashSet<int> NewlyKnown { get; } = [];

    /// <summary>Observers per hex: units and settlements that see it now (the fog of war; seen while above zero).</summary>
    public short[] Vision { get; set; } = [];

    /// <summary>Owner of each hex when this people last saw it.</summary>
    public short[] SeenOwner { get; set; } = [];

    /// <summary>Road level of each hex when this people last saw it.</summary>
    public byte[] SeenRoad { get; set; } = [];

    /// <summary>Works of each hex as last seen (ditches, palisades, walls, aqueducts, bridges).</summary>
    public ushort[] SeenWorks { get; set; } = [];

    /// <summary>Buildings remembered on hexes not seen now, by hex.</summary>
    public SortedDictionary<int, BuildingMemory> SeenBuildings { get; } = [];

    /// <summary>Settlements remembered on hexes not seen now, by centre hex.</summary>
    public SortedDictionary<int, SettlementMemory> SeenSettlements { get; } = [];

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

    /// <summary>AI: goods it saves for the technology it wants next (null = none); building and training leave them.</summary>
    public long[]? AiSaving { get; set; }

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
    public int Kind { get; set; }

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

    /// <summary>Wounded soldiers carried with the unit: they do not fight; herbalists bring them back.</summary>
    public int Injured { get; set; }

    /// <summary>Ship the unit is aboard (−1 = none): off the map until it lands.</summary>
    public int Aboard { get; set; } = -1;

    /// <summary>Hex of an enemy wall the unit is breaking (−1 = none).</summary>
    public int TargetWall { get; set; } = -1;

    /// <summary>Horses a caravan took with it, hundredths (back home with it).</summary>
    public long Horses { get; set; }

    /// <summary>Share of a wounded soldier gathered from the fallen, percent.</summary>
    public int InjuredShare { get; set; }

    /// <summary>Hundredths of a wounded soldier healed so far.</summary>
    public long HealProgress { get; set; }

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

    /// <summary>Settlement the unit comes from: carriers go back to it, and it feeds the unit's rations.</summary>
    public int Home { get; set; } = -1;

    /// <summary>Missed rations in a row: 0 fed, 1 hungry, 2 starving.</summary>
    public byte Hunger { get; set; }

    /// <summary>Hex the carriers go back to (their storehouse).</summary>
    public int ReturnCell { get; set; } = -1;

    /// <summary>Trade route that sent this caravan (−1 = none).</summary>
    public int Route { get; set; } = -1;

    /// <summary>Drawn patrol loop (empty = not patrolling).</summary>
    public List<int> Patrol { get; } = [];

    /// <summary>Hex and era of its last survey (prospectors and surveyors look again only when these change).</summary>
    public int SurveyedAt { get; set; } = -1;

    /// <summary>The last patrol loop it walked, kept after it stops, to walk it again.</summary>
    public List<int> LastPatrol { get; } = [];

    /// <summary>Where it is in its patrol cycle.</summary>
    public PatrolStage PatrolStage { get; set; }

    /// <summary>Hex its sight is counted from (−1 = not seeing).</summary>
    public int WatchCell { get; set; } = -1;

    /// <summary>Sight counted at <see cref="WatchCell"/>.</summary>
    public int WatchRadius { get; set; }

    /// <summary>Level of its weapons (0 = bare: clubs and sticks; 1–4 stone, bronze, iron, steel).</summary>
    public byte Weapon { get; set; }

    /// <summary>Level of its armour (0 = none; 1–4 hide, bronze, iron, plate).</summary>
    public byte Armour { get; set; }

    /// <summary>How it fights on its own.</summary>
    public Stance Stance { get; set; }

    /// <summary>Hexes walked since it last touched an enemy (a charge needs a run).</summary>
    public int Run { get; set; }

    /// <summary>Hexes it guards (painted by the player): it goes at enemies on them and comes back.</summary>
    public List<int> Guard { get; } = [];

    /// <summary>People it lost in the last step of battle (for the view).</summary>
    public int LastLoss { get; set; }

    /// <summary>People it had when trained or split (its full strength; 0 = as many as now).</summary>
    public int Size { get; set; }

    /// <summary>Units it struck in the last step of battle (for the view's arrows).</summary>
    public List<int> Struck { get; } = [];

    /// <summary>Tick its last search for a way to its target failed (−1 = never).</summary>
    public long ChaseFailTick { get; set; } = -100_000;

    /// <summary>Failed searches for a way to its target in a row.</summary>
    public int ChaseFails { get; set; }

    /// <summary>Tick of its last charge (−1 = never).</summary>
    public long ChargeTick { get; set; } = -1;

    /// <summary>Tick of its last step on the way (a charge needs a fresh run).</summary>
    public long StepTick { get; set; } = -1;

    /// <summary>Tick it last fought (for the view and the slowing of time).</summary>
    public long FoughtTick { get; set; } = -1;
}

/// <summary>How an army unit fights when not told otherwise.</summary>
public enum Stance : byte
{
    /// <summary>Strikes what it touches or reaches, does not walk after it.</summary>
    Hold = 0,

    /// <summary>Goes at enemies it sees within its sight.</summary>
    Aggressive = 1,

    /// <summary>Strikes only the enemy it was sent at (to walk past or pull back).</summary>
    Passive = 2,
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

    /// <summary>What stays at the source, hundredths (G3, 2026-11-19).</summary>
    public long KeepAtSource { get; init; }

    /// <summary>Trips leave while the target has less than this, hundredths (0 = no limit).</summary>
    public long UntilTargetHas { get; init; }

    /// <summary>Caravan on the road (−1 = none).</summary>
    public int Caravan { get; set; } = -1;

    /// <summary>Ended by the player.</summary>
    public bool Ended { get; set; }

    /// <summary>Hexes the player drew for the way there (empty = the shortest way); the way back is the same, reversed.</summary>
    public List<int> Path { get; } = [];
}
