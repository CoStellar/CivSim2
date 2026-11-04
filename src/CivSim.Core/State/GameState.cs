using System;
using System.Collections.Generic;
using CivSim.Core.Content;
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

    /// <summary>Creates a new game: Earth plus <see cref="GameSettings.CivilizationCount"/> AI civilizations.</summary>
    public static GameState CreateNew(GameSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.MapHeight % 2 != 0)
        {
            throw new ArgumentException("Map height must be even for the hex torus.", nameof(settings));
        }

        var state = new GameState(settings, new RandomStreams(settings.Seed));
        state._worlds.Add(new WorldState(WorldId.Earth, "Earth", settings.MapWidth, settings.MapHeight));
        for (int i = 0; i < settings.CivilizationCount; i++)
        {
            state._civilizations.Add(new CivilizationState(new CivId(i), $"Civilization {i + 1}", PlayerId.Ai));
        }

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
        var options = new WorldGenOptions(settings.WorldType, settings.WaterPercent, settings.CivilizationCount);
        report = WorldGenerator.Generate(content, earth.Map, state.Random[RandomStreamId.World], settings.Seed, options);
        for (int i = 0; i < state._civilizations.Count && i < report.Starts.Count; i++)
        {
            state._civilizations[i].StartCell = report.Starts[i];
        }

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
