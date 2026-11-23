using System;
using System.Collections.Generic;
using System.Linq;
using CivSim.Core.Map;
using CivSim.Core.Random;
using CivSim.Core.WorldGen;
using CivSim.Sim.Content;

namespace CivSim.Sim.State;

/// <summary>What wins the game, chosen when it is created.</summary>
public enum GameGoal
{
    /// <summary>The last people standing wins.</summary>
    LastStanding = 0,

    /// <summary>The first people to reach <see cref="SimSettings.GoalEra"/> wins.</summary>
    FirstToEra = 1,

    /// <summary>After <see cref="SimSettings.GoalMinutes"/> the best score wins and the worst loses.</summary>
    BestScore = 2,
}

/// <summary>Settings of a new game.</summary>
public sealed record SimSettings(ulong Seed, int LegacyWidth = 98, int LegacyHeight = 60, int Civilizations = 4)
{
    /// <summary>World type.</summary>
    public WorldType WorldType { get; init; } = WorldType.Continents;

    /// <summary>Water percent.</summary>
    public int WaterPercent { get; init; } = 60;

    /// <summary>Peoples choose their start in turns (the start phase); otherwise the generator's starts are used.</summary>
    public bool StartPhase { get; init; }

    /// <summary>What wins the game.</summary>
    public GameGoal Goal { get; init; } = GameGoal.FirstToEra;

    /// <summary>Era to reach for <see cref="GameGoal.FirstToEra"/> (4 = Middle Ages).</summary>
    public int GoalEra { get; init; } = 4;

    /// <summary>Minutes of play for <see cref="GameGoal.BestScore"/>.</summary>
    public int GoalMinutes { get; init; } = 120;

    /// <summary>The AI leads every people (the host hands people to players afterwards); false in rule tests.</summary>
    public bool Ai { get; init; } = true;

    /// <summary>12B: peoples start near each other on the main continent (startPhase.neighbourMaxHexes); test scenarios keep the generator's starts.</summary>
    public bool NearStarts { get; init; } = true;
}

/// <summary>
/// The whole state of a game of the new simulation (ADR 0028) and its tick loop. Changes only through commands
/// (<see cref="Apply"/>) and <see cref="Tick"/>. Follows docs/wydajnosc.md: the economy is a set of rates per civilization
/// recomputed only when something changes, constructions end at a scheduled tick, and the rhythms are staggered by civilization.
/// </summary>
public sealed partial class SimGame
{
    private const int DoneBuilding = 0;
    private const int DoneTraining = 1;
    private const int UnitStep = 2;
    private const int DoneResearch = 3;
    private readonly CivSim.Core.Random.DeterministicRandom _events;
    private readonly CivSim.Core.Random.DeterministicRandom _combat;

    /// <summary>Random stream of the AI players.</summary>
    public CivSim.Core.Random.DeterministicRandom AiRandom { get; }
    private readonly PriorityQueue<(int Kind, int Id), (long Tick, int Kind, int Id)> _schedule = new();

    private SimGame(SimContent content, SimSettings settings, WorldMap map, CivSim.Core.Random.DeterministicRandom events)
    {
        _events = events;
        var streams = new CivSim.Core.Random.RandomStreams(settings.Seed);
        _combat = streams[CivSim.Core.Random.RandomStreamId.Combat];
        AiRandom = streams[CivSim.Core.Random.RandomStreamId.Ai];
        Content = content;
        Settings = settings;
        Map = map;
        Geo = map.Geometry;
        OwnerAt = new short[Geo.CellCount];
        SettlementAt = new int[Geo.CellCount];
        BuildingAt = new int[Geo.CellCount];
        RoadAt = new byte[Geo.CellCount];
        InitWorks();
        GrowTrees();
        Array.Fill(OwnerAt, (short)-1);
        Array.Fill(SettlementAt, -1);
        Array.Fill(BuildingAt, -1);
        SlotAt = new int[Geo.CellCount * SlotsPerHex];
        Array.Fill(SlotAt, -1);
    }

    /// <summary>Rules.</summary>
    public SimContent Content { get; }

    /// <summary>Settings the game was created with.</summary>
    public SimSettings Settings { get; }

    /// <summary>The world map (terrain from the world generator).</summary>
    public WorldMap Map { get; }

    /// <summary>Map geometry.</summary>
    public HexGlobe Geo { get; }

    /// <summary>Ticks since the start.</summary>
    public long Ticks { get; private set; }

    /// <summary>Owner civilization of each hex (−1 = nobody).</summary>
    public short[] OwnerAt { get; }

    /// <summary>Settlement whose territory each hex is (−1 = none).</summary>
    public int[] SettlementAt { get; }

    /// <summary>Building on each hex (−1 = none).</summary>
    public int[] BuildingAt { get; }

    /// <summary>Places on a hex (decision of 2026-11-18): four, each holding a building or nothing.</summary>
    public const int SlotsPerHex = 4;

    /// <summary>Building in each place of each hex (index cell × 4 + place), −1 = empty.</summary>
    public int[] SlotAt { get; }

    /// <summary>Civilizations.</summary>
    public List<Civ> Civs { get; } = [];

    /// <summary>Settlements, by id.</summary>
    public List<Settlement> Settlements { get; } = [];

    /// <summary>Buildings, by id.</summary>
    public List<Building> Buildings { get; } = [];

    /// <summary>Units, by id.</summary>
    public List<Unit> Units { get; } = [];

    /// <summary>Events of the last tick for the chronicle (civ −1 = everyone).</summary>
    public List<(int Civ, string Text)> News { get; } = [];

    /// <summary>Map size in hexes for a legacy size and a density: the area grows by the density.</summary>
    public static (int Width, int Height) SizeFor(int legacyWidth, int legacyHeight, int density)
    {
        double k = Math.Sqrt(density);
        int w = (int)Math.Round(legacyWidth * k);
        w -= ((w - 2) % 4 + 4) % 4; // width = 4k + 2
        int h = (int)Math.Round(legacyHeight * k);
        h -= h % 2;
        return (Math.Max(6, w), Math.Max(4, h));
    }

    /// <summary>Creates a game: generates the world and places one camp for each civilization.</summary>
    public static SimGame Create(SimContent content, SimSettings settings)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(settings);
        (int w, int h) = SizeFor(settings.LegacyWidth, settings.LegacyHeight, content.Density);
        var map = new WorldMap(new HexGlobe(w, h));
        var streams = new RandomStreams(settings.Seed);
        var options = new WorldGenOptions(settings.WorldType, settings.WaterPercent, settings.Civilizations);
        WorldGenReport report = WorldGenerator.Generate(content.World, map, streams[RandomStreamId.World], settings.Seed, options);
        var game = new SimGame(content, settings, map, streams[RandomStreamId.Events]);
        game.ShapeLodes();
        for (int c = 0; c < settings.Civilizations && c < report.Starts.Count; c++)
        {
            var language = CivSim.Core.Naming.NameLanguage.Create(content.World.Names, CivSim.Core.Naming.Names.LanguageSeed(settings.Seed, c));
            string name = language.PeopleName(0);
            var civ = new Civ(c, name.Length > 0 ? name : $"Civilization {c + 1}", content.Goods.Count)
            {
                Known = new bool[map.Geometry.CellCount],
                RockSeen = new byte[map.Geometry.CellCount],
                SoilSeen = new bool[map.Geometry.CellCount],
                Vision = new short[map.Geometry.CellCount],
                SeenOwner = NoOwners(map.Geometry.CellCount),
                SeenRoad = new byte[map.Geometry.CellCount],
                SeenWorks = new ushort[map.Geometry.CellCount],
                KnownTech = new bool[content.Techs.Count],
                KindKills = new int[content.Units.Count],
                KindLevel = new int[content.Units.Count],
                KindPending = new int[content.Units.Count],
                KindPicks = new int[content.Units.Count * Math.Max(1, content.Veterans.Options.Length)],
                Experience = new long[content.Techs.Count],
                ExperienceRate = new int[content.Techs.Count],
                Language = language,
                NextThinkerOffer = content.ThinkerOfferTicks,
                NextAdvisorOffer = content.AdvisorOfferTicks / 2,
            };
            foreach (SimTech t in content.Techs)
            {
                civ.KnownTech[t.Id] = t.KnownAtStart;
            }

            civ.Thinker = new Person { Name = language.PeopleName(999), Rarity = 0 };
            civ.IsAi = settings.Ai;
            civ.Aggression = game.AiRandom.NextInt(0, 101);
            civ.Expansion = game.AiRandom.NextInt(0, 101);
            civ.StartError = game.AiRandom.NextInt(0, 31);
            game.Civs.Add(civ);
        }

        if (!settings.StartPhase)
        {
            int[] starts = settings.NearStarts && content.StartPhase.NeighbourMaxHexes > 0
                ? game.NearStarts([.. report.Starts.Take(game.Civs.Count).Select(map.Geometry.Index)])
                : [.. report.Starts.Take(game.Civs.Count).Select(map.Geometry.Index)];
            foreach (Civ civ in game.Civs)
            {
                game.StartMetals(civ, starts[civ.Id]);
                game.FoundSettlement(civ, starts[civ.Id], content.StartPeople);
            }
        }

        if (settings.StartPhase)
        {
            game.ScoreSites();
            game.BeginPicking();
        }

        return game;
    }

    /// <summary>Creates a settlement for <paramref name="civ"/> at <paramref name="center"/> and claims its territory.</summary>
    public Settlement FoundSettlement(Civ civ, int center, int people, long[]? stock = null)
    {
        ArgumentNullException.ThrowIfNull(civ);
        string place = civ.Language?.PlaceName((ulong)civ.PlacesNamed++) ?? "";
        int goods = Content.Goods.Count;
        var s = new Settlement
        {
            Id = Settlements.Count, Civ = civ.Id, Center = center, People = people, Name = place.Length > 0 ? place : $"{civ.Name} {civ.Settlements.Count + 1}",
            Stock = new long[goods], Capacity = new long[goods], Rate = new long[goods], Remainder = new long[goods],
        };

        // The first camp gets the tribe's start goods; later camps get what their settlers carried.
        long[]? goodsIn = stock ?? (civ.Settlements.Count == 0 ? Content.StartStock : null);
        if (goodsIn is not null)
        {
            Array.Copy(goodsIn, s.Stock, goods);
        }

        Array.Fill(s.Capacity, Content.Levels[0].Storage);
        Settlements.Add(s);
        civ.Settlements.Add(s.Id);
        Claim(s);
        Watch(s);
        civ.Dirty = true;
        SyncTotals(civ);
        return s;
    }

    /// <summary>Runs one tick: due constructions, then the economy and population of the civilizations whose turn it is.</summary>
    public void Tick()
    {
        News.Clear();
        if (Picking)
        {
            PickingTick();
            return;
        }

        Ticks++;
        while (_schedule.TryPeek(out (int Kind, int Id) item, out (long Tick, int Kind, int Id) due) && due.Tick <= Ticks)
        {
            _schedule.Dequeue();
            switch (item.Kind)
            {
                case DoneBuilding:
                    Building b = Buildings[item.Id];
                    if (b.State == BuildingState.Construction && b.FinishTick == due.Tick)
                    {
                        if (b.WorkCap >= Math.Max(1, Content.Buildings[b.Type].BuildTicks))
                        {
                            b.State = BuildingState.Active;
                            Civs[b.Civ].Dirty = true;
                            News.Add((b.Civ, $"{Content.Buildings[b.Type].Name} is finished."));
                        }
                        else
                        {
                            // The materials at the site are used up: the work waits for the rest.
                            b.RemainingTicks = Math.Max(1, Content.Buildings[b.Type].BuildTicks) - b.WorkCap;
                            b.State = BuildingState.Delivery;
                        }
                    }

                    break;
                case DoneTraining:
                    Unit trained = Units[item.Id];
                    if (trained.State == UnitState.Training && trained.NextTick == due.Tick)
                    {
                        trained.State = UnitState.Idle;
                        Watch(trained);
                        FillPacks(trained);
                        News.Add((trained.Civ, $"{Content.Units[trained.Kind].Name} are ready."));
                    }

                    break;
                case DoneResearch:
                    FinishResearch(Civs[item.Id], due.Tick);
                    break;
                default:
                    Unit walker = Units[item.Id];
                    if (walker.State == UnitState.Moving && walker.NextTick == due.Tick)
                    {
                        Step(walker);
                    }

                    break;
            }
        }

        if (Ticks % Content.Combat.StepTicks == 0)
        {
            CombatStep();
        }

        if (Ticks % SimContent.TicksPerSecond == 0)
        {
            RoadStep();
            SurveyStep();
            UpdateSigns();
            SearchSigns();
            SeasonStep();
            HealStep();
            PalisadeStep();
            WallStep();
            SiegeStep();
        }

        if (Ticks % SimContent.TicksPerMinute == 0)
        {
            WearinessStep();
            ReputationStep();
            Sample();
            CheckGoal();
        }

        foreach (Civ civ in Civs)
        {
            if (civ.Dead)
            {
                continue;
            }

            if ((Ticks + civ.Id) % Content.EconomyEveryTicks == 0)
            {
                EconomyStep(civ);
            }

            if ((Ticks + (civ.Id * 17)) % Content.Rations.EveryTicks == 0)
            {
                RationStep(civ);
            }

            if ((Ticks + civ.Id) % Content.PopulationEveryTicks == 0)
            {
                PopulationStep(civ);
                PeopleOfNoteStep(civ);
                EventStep(civ);
            }

            if (civ.IsAi && (Ticks + (civ.Id * 7)) % Content.AiThinkEveryTicks == 0)
            {
                Ai.SimAi.Think(this, civ);
            }
        }

        foreach (Civ civ in Civs)
        {
            SettlePlayer(civ);
        }
    }

    /// <summary>The winner, once the goal is met (−1 = the game goes on).</summary>
    public int Winner { get; private set; } = -1;

    /// <summary>Score of a people: people, settlements, technologies and soldiers (the summary and the time goal).</summary>
    public int ScoreOf(Civ civ)
    {
        ArgumentNullException.ThrowIfNull(civ);
        if (civ.Dead)
        {
            return 0;
        }

        int techs = 0;
        foreach (bool k in civ.KnownTech)
        {
            techs += k ? 1 : 0;
        }

        int soldiers = 0;
        foreach (int id in civ.Units)
        {
            soldiers += Content.Units[Units[id].Kind].Army ? Units[id].People : 0;
        }

        return civ.People + (civ.Settlements.Count * 10) + (techs * 20) + soldiers;
    }

    /// <summary>The goal as a sentence.</summary>
    public string GoalText => Settings.Goal switch
    {
        GameGoal.LastStanding => "The last civilization standing wins.",
        GameGoal.FirstToEra => $"The first civilization to reach the {Content.Eras[Math.Clamp(Settings.GoalEra, 0, Content.Eras.Count - 1)].Name} wins.",
        _ => $"After {Settings.GoalMinutes} minutes the best score wins.",
    };

    private void CheckGoal()
    {
        if (Winner >= 0)
        {
            return;
        }

        int alive = 0;
        int last = -1;
        int best = -1;
        int bestScore = -1;
        foreach (Civ c in Civs)
        {
            if (c.Dead)
            {
                continue;
            }

            alive++;
            last = c.Id;
            if (Settings.Goal == GameGoal.FirstToEra && EraOf(c) >= Settings.GoalEra)
            {
                best = c.Id;
            }

            if (Settings.Goal == GameGoal.BestScore && ScoreOf(c) > bestScore)
            {
                bestScore = ScoreOf(c);
                if (Ticks >= (long)Settings.GoalMinutes * SimContent.TicksPerMinute)
                {
                    best = c.Id;
                }
            }
        }

        if (alive == 1 && Civs.Count > 1)
        {
            best = last;
        }

        if (best >= 0)
        {
            Winner = best;
            foreach (Civ c in Civs)
            {
                News.Add((c.Id, c.Id == best ? "We have won the game!" : $"{Civs[best].Name} have won the game."));
            }
        }
    }

    // Once a minute: one line of history per people (constant memory per minute, read by the summary).
    private void Sample()
    {
        foreach (Civ c in Civs)
        {
            if (c.Dead)
            {
                continue;
            }

            int techs = 0;
            foreach (bool k in c.KnownTech)
            {
                techs += k ? 1 : 0;
            }

            int soldiers = 0;
            foreach (int id in c.Units)
            {
                soldiers += Content.Units[Units[id].Kind].Army ? Units[id].People : 0;
            }

            c.History.Add(new HistorySample((int)(Ticks / SimContent.TicksPerMinute), c.People, c.Settlements.Count, c.Buildings.Count, techs, soldiers, c.Rate[Content.Food], EraOf(c), c.Event));
        }
    }

    private void Claim(Settlement s)
    {
        int radius = Content.Levels[s.Level].Radius;
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(radius)];
        int n = Geo.Range(Geo.FromIndex(s.Center), radius, area);
        for (int k = 0; k < n; k++)
        {
            int i = Geo.Index(area[k]);
            if (OwnerAt[i] >= 0 || Content.World.Biomes[Map.Biome[i]].IsWater)
            {
                continue;
            }

            OwnerAt[i] = (short)s.Civ;
            Map.Owner.Set(i, (short)s.Civ);
            SettlementAt[i] = s.Id;
            s.Cells.Add(i);
            Map.MarkCellDirty(i);
        }
    }

    private void Schedule(Building b) => _schedule.Enqueue((DoneBuilding, b.Id), (b.FinishTick, DoneBuilding, b.Id));

    private void ScheduleUnit(Unit u, int kind) => _schedule.Enqueue((kind, u.Id), (u.NextTick, kind, u.Id));

    /// <summary>Marks the hexes within <paramref name="radius"/> of <paramref name="cell"/> as seen by <paramref name="civ"/>.</summary>
    private static short[] NoOwners(int n)
    {
        var a = new short[n];
        Array.Fill(a, (short)-1);
        return a;
    }

    /// <summary>Marks hexes explored without seeing them now (used by tests and scripted starts).</summary>
    public void Reveal(Civ civ, int cell, int radius)
    {
        ArgumentNullException.ThrowIfNull(civ);
        Span<HexCoord> area = stackalloc HexCoord[HexGlobe.RangeSize(radius)];
        int n = Geo.Range(Geo.FromIndex(cell), radius, area);
        for (int k = 0; k < n; k++)
        {
            int i = Geo.Index(area[k]);
            if (!civ.Known[i])
            {
                civ.Known[i] = true;
                civ.NewlyKnown.Add(Map.Chunks.ChunkOfIndex(i));
            }
        }
    }
}
