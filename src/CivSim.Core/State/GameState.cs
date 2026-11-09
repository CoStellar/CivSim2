using System;
using System.Collections.Generic;
using CivSim.Core.Content;
using CivSim.Core.Map;
using CivSim.Core.Random;
using CivSim.Core.WorldGen;

namespace CivSim.Core.State;

/// <summary>
/// The complete authoritative state of one game. Only the server owns it; clients receive filtered views.
/// Lists are always kept sorted by id so iteration order is deterministic.
/// </summary>
public sealed class GameState
{
    private readonly List<WorldState> _worlds = [];
    private readonly List<CivilizationState> _civilizations = [];
    private readonly List<SettlementState> _settlements = [];

    private GameState(GameSettings settings, RandomStreams random)
    {
        Settings = settings;
        Random = random;
    }

    /// <summary>Settings the game was created with.</summary>
    public GameSettings Settings { get; }

    /// <summary>Number of completed turns. A new game is at turn 0.</summary>
    public long Turn { get; internal set; }

    /// <summary>Per-system random streams.</summary>
    public RandomStreams Random { get; }

    /// <summary>All worlds, sorted by id. Earth is always first.</summary>
    public IReadOnlyList<WorldState> Worlds => _worlds;

    /// <summary>All civilizations, sorted by id.</summary>
    public IReadOnlyList<CivilizationState> Civilizations => _civilizations;

    /// <summary>
    /// Game time in months since the start. Every rate is defined per year (CLAUDE.md rule 6), so systems
    /// convert with the current turn length instead of counting turns.
    /// </summary>
    public long Month { get; internal set; }

    /// <summary>All settlements, sorted by id.</summary>
    public IReadOnlyList<SettlementState> Settlements => _settlements;

    /// <summary>Id the next settlement will get.</summary>
    public int NextSettlementId { get; private set; }

    /// <summary>Active effects (GDD 10.1).</summary>
    public EffectBook Effects { get; } = new();

    /// <summary>Treaties, wars and proposals between civilizations (GDD 9.1; stage 4d).</summary>
    public Diplomacy.TreatyBook Treaties { get; } = new();

    /// <summary>
    /// Months of the current world step (ADR 0018): from <see cref="Month"/> to the nearest month in which some
    /// civilization steps. World systems (climate, regrowth, the sea) compute this many months; civilizations compute
    /// their own <see cref="CivilizationState.StepMonths"/>. Set by <see cref="Simulation.WorldClock.Begin"/>.
    /// </summary>
    public int StepMonths { get; internal set; }

    /// <summary>
    /// Months the world itself computes in this world step (ADR 0022): in a full step (some civilization steps) the months
    /// until the next step of any civilization; in a step of episodes only, 0 (the world waits while a campaign runs).
    /// </summary>
    public int WorldMonths { get; internal set; }

    /// <summary>
    /// Months of action since the start (ADR 0025): <see cref="Turn"/> divided by data/time.json ticksPerActionMonth. War,
    /// diplomacy and politics count their timers in these (decision of 2026-11-09: one year of action = 6 s at x1), not in
    /// the year counter <see cref="Month"/>. Set by <see cref="Simulation.WorldClock.Begin"/>.
    /// </summary>
    public long ActionMonth { get; internal set; }

    /// <summary>Sets the months of action, as saved (loading).</summary>
    public void SetActionMonthForLoad(long actionMonth) => ActionMonth = actionMonth;

    /// <summary>Units walking over the map (stage 6 PR 3a): settlers and scouts.</summary>
    public Units.UnitBook Units { get; } = new();

    /// <summary>Running episodes: war campaigns and, later, other events with a course (ADR 0022).</summary>
    public Episodes.EpisodeBook Episodes { get; } = new();

    /// <summary>Sets the months the world computes in this step, as saved (loading).</summary>
    public void SetWorldMonthsForLoad(int worldMonths) => WorldMonths = worldMonths;

    /// <summary>The most advanced era among living civilizations (for reports).</summary>
    public int TopEra()
    {
        int era = 0;
        foreach (CivilizationState c in _civilizations)
        {
            if (c.IsAlive && c.Era > era)
            {
                era = c.Era;
            }
        }

        return era;
    }

    /// <summary>Founds a settlement with an empty warehouse and returns it.</summary>
    public SettlementState FoundSettlement(CivId owner, WorldId world, HexCoord cell, string name, int level, int goodCount, int strataCount = 0, int skillCount = 0)
    {
        var settlement = new SettlementState(new SettlementId(NextSettlementId++), owner, world, cell, name, level, Turn, goodCount, strataCount, skillCount);
        _settlements.Add(settlement);
        return settlement;
    }

    /// <summary>Removes every settlement that matches <paramref name="gone"/>, keeping the id order. Returns how many went.</summary>
    public int RemoveSettlements(Predicate<SettlementState> gone) => _settlements.RemoveAll(gone);

    /// <summary>Finds a settlement by id, or null.</summary>
    public SettlementState? FindSettlement(SettlementId id)
    {
        int lo = 0;
        int hi = _settlements.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int cmp = _settlements[mid].Id.Value.CompareTo(id.Value);
            if (cmp == 0)
            {
                return _settlements[mid];
            }

            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return null;
    }

    /// <summary>Adds a settlement while loading. Ids must arrive in ascending order.</summary>
    public void AddSettlementForLoad(SettlementState settlement, int nextSettlementId)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        if (_settlements.Count > 0 && _settlements[^1].Id.Value >= settlement.Id.Value)
        {
            throw new InvalidOperationException("Settlements must be added in ascending id order.");
        }

        _settlements.Add(settlement);
        NextSettlementId = nextSettlementId;
    }

    /// <summary>Sets the time, the current world step and the next settlement id while loading a save.</summary>
    public void SetCountersForLoad(long month, int nextSettlementId, int stepMonths = 0)
    {
        Month = month;
        NextSettlementId = nextSettlementId;
        StepMonths = stepMonths;
    }

    /// <summary>Creates a new game: Earth plus <see cref="GameSettings.CivilizationCount"/> AI civilizations.</summary>
    public static GameState CreateNew(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!Map.HexGlobe.IsValidSize(settings.MapWidth, settings.MapHeight))
        {
            throw new ArgumentException($"Map size {settings.MapWidth}×{settings.MapHeight} does not close into a globe: the width must be 2 more than a multiple of 4 and the height even.", nameof(settings));
        }

        var state = new GameState(settings, new RandomStreams(settings.Seed));
        state._worlds.Add(new WorldState(WorldId.Earth, settings.PlanetName, settings.MapWidth, settings.MapHeight));
        for (int i = 0; i < settings.CivilizationCount; i++)
        {
            state._civilizations.Add(new CivilizationState(new CivId(i), $"Civilization {i + 1}", PlayerId.Ai)
            {
                LanguageSeed = Naming.Names.LanguageSeed(settings.Seed, i),
            });
        }

        Ai.Character.Roll(state);
        return state;
    }

    /// <summary>
    /// Creates a new game and generates Earth from <paramref name="content"/> (GDD 3). Each civilization gets
    /// a start cell. <paramref name="report"/> describes the generated world.
    /// </summary>
    public static GameState CreateNew(GameSettings settings, GameContent content, out WorldGenReport report)
    {
        ArgumentNullException.ThrowIfNull(content);
        GameState state = CreateNew(settings);
        WorldState earth = state._worlds[0];
        earth.Map.YearHundredths = settings.Planet.YearHundredths;
        earth.Map.LiveSeasons = content.Seasons.Live(settings.Planet);
        var options = WorldGenOptions.From(settings);
        report = WorldGenerator.Generate(content, earth.Map, state.Random[RandomStreamId.World], settings.Seed, options);
        earth.Climate.Co2Microbar = settings.Planet.Co2Microbar;
        earth.Climate.GlobalMeanMillionths = report.GlobalMeanMillionths;
        earth.Climate.StartLandIce = SeaLevel.LandIce(content, earth.Map);
        Naming.Names.NamePeoples(content.Names, state);
        Governance.Governments.ChooseAtStart(content, state);
        Governance.Leaders.CrownFirst(content, state);
        for (int i = 0; i < state._civilizations.Count && i < report.Starts.Count; i++)
        {
            CivilizationState civ = state._civilizations[i];
            civ.StartCell = report.Starts[i];
            if (content.Goods.Count > 0 && content.SettlementLevels.Count > 0)
            {
                // Every tribe starts with one camp and a little food and gear (GDD 5.3, data/settlements.json).
                SettlementState camp = state.FoundSettlement(
                    civ.Id, earth.Id, civ.StartCell, Naming.Names.NextPlace(content.Names, state, civ), content.NewTribeLevel, content.Goods.Count, content.Strata.Count, content.Skills.Count);
                foreach (GoodQuantity q in content.NewTribeStock)
                {
                    camp.Add(q.GoodId, q.Amount);
                }

                for (int k = 0; k < camp.Population.Length && k < content.Population.NewTribe.Count; k++)
                {
                    camp.Population[k] = content.Population.NewTribe[k];
                }
            }
        }

        Governance.Stability.Begin(state);

        // Influence zones exist from the start, so the first turn's gathering knows its land (GDD 5.4).
        if (content.Strata.Count > 0)
        {
            new Population.Influence(content).Recompute(state);
        }

        Simulation.WorldClock.Begin(content, state);
        Perception.MapMemory.See(content, state);
        return state;
    }

    /// <summary>Creates an empty state for loading a save; the caller adds worlds and civilizations in id order.</summary>
    public static GameState CreateForLoad(GameSettings settings, long turn)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new GameState(settings, new RandomStreams(settings.Seed)) { Turn = turn };
    }

    /// <summary>Adds a world while loading. Ids must arrive in ascending order.</summary>
    public void AddWorldForLoad(WorldState world)
    {
        if (_worlds.Count > 0 && _worlds[^1].Id.Value >= world.Id.Value)
        {
            throw new InvalidOperationException("Worlds must be added in ascending id order.");
        }

        _worlds.Add(world);
    }

    /// <summary>Adds a civilization while loading. Ids must arrive in ascending order.</summary>
    public void AddCivilizationForLoad(CivilizationState civ)
    {
        if (_civilizations.Count > 0 && _civilizations[^1].Id.Value >= civ.Id.Value)
        {
            throw new InvalidOperationException("Civilizations must be added in ascending id order.");
        }

        _civilizations.Add(civ);
    }

    /// <summary>
    /// Adds a civilization born during the game (a secession, stage 4f PR 4) with the next free id; ids stay dense and in
    /// order, so it is iterated last.
    /// </summary>
    public CivilizationState AddCivilization(string name)
    {
        int id = _civilizations.Count == 0 ? 0 : _civilizations[^1].Id.Value + 1;
        var civ = new CivilizationState(new CivId(id), name, PlayerId.Ai);
        _civilizations.Add(civ);
        return civ;
    }

    /// <summary>Finds a civilization by id, or null.</summary>
    public CivilizationState? FindCivilization(CivId id)
    {
        int index = IndexOfCivilization(id);
        return index < 0 ? null : _civilizations[index];
    }

    /// <summary>Index of a civilization in <see cref="Civilizations"/>, or -1.</summary>
    public int IndexOfCivilization(CivId id)
    {
        int lo = 0;
        int hi = _civilizations.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            int cmp = _civilizations[mid].Id.Value.CompareTo(id.Value);
            if (cmp == 0)
            {
                return mid;
            }

            if (cmp < 0)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return -1;
    }
}
