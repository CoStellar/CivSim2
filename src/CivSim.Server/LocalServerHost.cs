using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using CivSim.Core.Content;
using CivSim.Core.State;
using CivSim.Core.WorldGen;
using CivSim.Data;
using CivSim.Protocol.Hosting;
using CivSim.Protocol.Messages;
using CivSim.Protocol.Transport;

namespace CivSim.Server;

/// <summary>
/// Runs a <see cref="GameServer"/> on its own simulation thread inside the client process (single player and the
/// observer viewer). The client only gets <see cref="IConnection"/>s, so it talks to this server exactly as it
/// would to a remote one. Everything public here uses Protocol types only.
/// </summary>
public sealed class LocalServerHost : IDisposable
{
    /// <summary>At maximum speed the map is sent to clients at most this often.</summary>
    public static readonly TimeSpan MaxSpeedViewInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>At the live speeds the map is sent to clients at most this often (ADR 0025: ticks come faster than views are useful).</summary>
    public static readonly TimeSpan LiveViewInterval = TimeSpan.FromMilliseconds(100);

    private readonly ConcurrentQueue<IConnection> _pending = new();
    private readonly ConcurrentQueue<string> _clientLog = new();
    private readonly Thread _thread;
    private readonly GameServer _server;
    private volatile bool _stopping;
    private volatile string? _error;
    private long _turn;
    private long _turnLimit;
    private string? _reportPath;
    private string? _replayPath;
    private StreamWriter? _report;
    private LongRunRecorder? _longRun;
    private volatile bool _longRunFinished;

    private LocalServerHost(GameServer server)
    {
        _server = server;
        _thread = new Thread(Run) { IsBackground = true, Name = "CivSim simulation" };
    }

    /// <summary>Last completed turn (for display only; the authoritative value is on the simulation thread).</summary>
    public long Turn => Interlocked.Read(ref _turn);

    /// <summary>Set when the simulation thread stopped because of an exception.</summary>
    public string? Error => _error;

    /// <summary>Folder of the long run's files, or null when this game is not a long run.</summary>
    public string? LongRunFolder => _longRun?.Folder;

    /// <summary>True when the long run reached its time limit and wrote its summary (the client may quit).</summary>
    public bool LongRunFinished => _longRunFinished;

    /// <summary>The client reports each frame's work time and the time since the previous frame for the long run's statistics. Thread-safe; does nothing outside a long run.</summary>
    public void ReportFrame(double workMs, double gapMs) => _longRun?.Frame(workMs, gapMs);

    /// <summary>The client reports its engine's memory and object counts for the long run. Thread-safe; does nothing outside a long run.</summary>
    public void ReportEngine(long staticMb, long videoMb, long objects, long nodes) => _longRun?.Engine(staticMb, videoMb, objects, nodes);

    /// <summary>The client adds a line to the long run's log (for example its own warnings). Thread-safe; does nothing outside a long run.</summary>
    public void LogLongRun(string text)
    {
        if (_longRun is not null)
        {
            _clientLog.Enqueue(text);
        }
    }

    /// <summary>
    /// Generates a new world from <paramref name="options"/> and starts the simulation thread (paused).
    /// <paramref name="dataDirectory"/> is the folder with the JSON content (null = next to the program).
    /// </summary>
    public static LocalServerHost Start(LocalGameOptions options, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        GameContent content = ContentLoader.Load(dataDirectory);
        (GameSettings settings, _) = BuildSettings(options, content);
        var host = new LocalServerHost(GameServer.CreateNew(settings, content))
        {
            _turnLimit = Math.Max(0, options.TurnLimit),
            _reportPath = string.IsNullOrWhiteSpace(options.ReportPath) ? null : options.ReportPath,
            _replayPath = !string.IsNullOrWhiteSpace(options.ReplayPath) ? options.ReplayPath
                : !string.IsNullOrWhiteSpace(options.LongRunFolder) ? System.IO.Path.Combine(options.LongRunFolder, "replay.civreplay") : null,
        };
        if (host._replayPath is not null)
        {
            host._server.StartRecording(newGame: true);
        }

        if (!string.IsNullOrWhiteSpace(options.LongRunFolder))
        {
            host._longRun = new LongRunRecorder(
                options.LongRunFolder,
                TimeSpan.FromSeconds(Math.Max(0, options.LongRunSeconds)),
                TimeSpan.FromSeconds(Math.Max(1, options.LongRunSampleSeconds)),
                content.Stability.UnrestBelow,
                Math.Max(0, options.LongRunStillYears));
            host._longRun.Log(string.Create(
                CultureInfo.InvariantCulture,
                $"start: seed {options.Seed}, map {options.Width}x{options.Height}, {options.Civilizations} civilizations, planet {options.Planet}, world {options.WorldType}, " +
                $"limit {(options.LongRunSeconds > 0 ? options.LongRunSeconds + " s" : "none")}, ends after {(options.LongRunStillYears > 0 ? options.LongRunStillYears + " still years" : "no still years")}, row every {Math.Max(1, options.LongRunSampleSeconds)} s, " +
                $"{Environment.ProcessorCount} processor threads, {Environment.OSVersion}, .NET {Environment.Version}"));
        }

        host._thread.Start();
        return host;
    }

    /// <summary>
    /// Loads the data files of <paramref name="dataDirectory"/> (null = next to the program) and returns every problem found,
    /// empty when the data is valid (stage 6, T14: the viewer shows these instead of stopping, and reloads data with F5).
    /// </summary>
    public static IReadOnlyList<string> CheckData(string? dataDirectory = null)
    {
        try
        {
            ContentLoader.Load(dataDirectory);
            return [];
        }
        catch (ContentException ex)
        {
            return ex.Errors;
        }
    }

    /// <summary>The planet presets the start menu offers (stage 5), read from the data files.</summary>
    public static LocalSetupInfo Describe(string? dataDirectory = null)
    {
        GameContent content = ContentLoader.Load(dataDirectory);
        var planets = new List<PlanetPresetInfo>(content.Planets.Count);
        foreach (PlanetPreset p in content.Planets)
        {
            PlanetParameters planet = p.Planet;
            planets.Add(new PlanetPresetInfo(
                p.Key,
                p.Name,
                planet.AxialTiltTenths,
                planet.EccentricityThousandths,
                planet.LuminosityThousandths,
                planet.DistanceThousandths,
                planet.YearHundredths,
                (planet.LightMillionths + 500) / 1000,
                planet.VolcanismPercent,
                planet.PressureHundredths,
                p.WaterPercent,
                p.Moisture.ToString().ToLowerInvariant(),
                p.Relief.ToString().ToLowerInvariant()));
        }

        return new LocalSetupInfo(planets);
    }

    /// <summary>
    /// Checks start settings without generating a world: the resulting star, orbit, air and climate, the notes about
    /// values that were moved into range, or the error that would stop <see cref="Start"/>.
    /// </summary>
    public static PlanetPreview Preview(LocalGameOptions options, string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            GameContent content = ContentLoader.Load(dataDirectory);
            (GameSettings settings, IReadOnlyList<string> notes) = BuildSettings(options, content);
            settings.Planet.Validate();
            return new PlanetPreview(PlanetSummary.Lines(settings.Planet), notes, null);
        }
#pragma warning disable CA1031 // Any bad setting is shown to the user instead of crashing the menu.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new PlanetPreview([], [], ex.Message);
        }
    }

    // Turns the client's plain options into core settings (GDD 3, "Ustawienia świata").
    private static (GameSettings Settings, IReadOnlyList<string> Notes) BuildSettings(LocalGameOptions options, GameContent content)
    {
        PlanetPreset preset = content.Planets.FirstOrDefault(p => p.Key == options.Planet)
            ?? throw new ArgumentException($"Unknown planet '{options.Planet}'.", nameof(options));
        GameSettings settings = new GameSettings(options.Seed, options.Width, options.Height, options.Civilizations).WithPreset(preset);
        (PlanetParameters planet, IReadOnlyList<string> notes) = PlanetSetup.Build(preset.Planet, preset.AutoCo2, new PlanetOverrides
        {
            AxialTiltTenths = options.AxialTiltTenths,
            EccentricityThousandths = options.EccentricityPerMille,
            LuminosityThousandths = options.LuminosityThousandths,
            DistanceThousandths = options.DistanceThousandths,
            YearThousandths = options.YearThousandths,
            LightThousandths = options.LightThousandths,
            Co2Microbar = options.Co2Microbar,
            PressureHundredths = options.PressureHundredths,
            VolcanismPercent = options.VolcanismPercent,
            StarVariabilityPerMille = options.StarVariabilityPerMille,
            StarCycleYears = options.StarCycleYears,
        });
        settings = settings with
        {
            Planet = planet,
            WorldType = Parse<WorldType>(options.WorldType),
            WaterPercent = Math.Clamp(options.WaterPercent ?? settings.WaterPercent, 30, 80),
            Moisture = options.Moisture is null ? settings.Moisture : Parse<WorldMoisture>(options.Moisture),
            Inland = Parse<WorldInland>(options.Inland),
            Relief = options.Relief is null ? settings.Relief : Parse<WorldRelief>(options.Relief),
            Resources = Parse<WorldResources>(options.Resources),
        };
        return (settings, notes);
    }

    /// <summary>Opens a new in-memory connection to the server. Send Hello on it first.</summary>
    public IConnection Connect()
    {
        (InMemoryConnection client, InMemoryConnection server) = InMemoryConnection.CreatePair();
        _pending.Enqueue(server);
        return client;
    }

    /// <summary>Stops the simulation thread.</summary>
    public void Dispose()
    {
        _stopping = true;
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        // The replay (T11) is written once the simulation thread has stopped, so the recording is complete.
        if (_replayPath is not null && !_thread.IsAlive && _server.Replay is { } replay)
        {
            try
            {
                replay.Write(_replayPath);
            }
            catch (System.IO.IOException ex)
            {
                _error ??= $"The replay could not be written: {ex.Message}";
            }
            catch (UnauthorizedAccessException ex)
            {
                _error ??= $"The replay could not be written: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Time between ticks at a speed (ADR 0025: <paramref name="ticksPerSecond"/> ticks a second at x1, two and four
    /// times as many at x2 and x4), or null when paused.
    /// </summary>
    public static TimeSpan? TurnInterval(SimulationSpeed speed, int ticksPerSecond)
    {
        int multiplier = speed switch
        {
            SimulationSpeed.X1 => 1,
            SimulationSpeed.X2 => 2,
            SimulationSpeed.X4 => 4,
            _ => 0,
        };
        return speed == SimulationSpeed.Max ? TimeSpan.Zero
            : multiplier == 0 ? null
            : TimeSpan.FromTicks(TimeSpan.TicksPerSecond / (Math.Max(1, ticksPerSecond) * (long)multiplier));
    }

    private static T Parse<T>(string text)
        where T : struct, Enum
    {
        return Enum.TryParse(text, ignoreCase: true, out T value) && Enum.IsDefined(value)
            ? value
            : throw new ArgumentException($"Unknown {typeof(T).Name} '{text}'.");
    }

    private void Run()
    {
        try
        {
            long lastTurn = Stopwatch.GetTimestamp();
            long lastSend = lastTurn;
            bool unsent = false;
            while (!_stopping)
            {
                while (_pending.TryDequeue(out IConnection? connection))
                {
                    _server.Accept(connection);
                }

                bool ran = _server.Pump();
                TimeSpan? interval = TurnInterval(_server.Speed, _server.TicksPerSecond);
                if (!ran && interval is { } wait && _server.ConnectedObservers > 0 && Stopwatch.GetElapsedTime(lastTurn) >= wait)
                {
                    // Keep the pace even when a tick runs late (at most one tick behind; Max runs as fast as it can).
                    long now = Stopwatch.GetTimestamp();
                    lastTurn = wait == TimeSpan.Zero || Stopwatch.GetElapsedTime(lastTurn) >= wait * 2 ? now : lastTurn + (long)(wait.TotalSeconds * Stopwatch.Frequency);
                    Core.Simulation.TurnReport report = _server.AdvanceTurn(sendViews: false);
                    unsent = true;
                    ran = true;
                    Report();
                    _longRun?.TurnDone(_server.State, report, _server.Runner.LastChronicle);
                    if (_turnLimit > 0 && _server.State.Turn == _turnLimit)
                    {
                        _server.Pause($"Turn limit {_turnLimit} reached; the simulation is paused.");
                        _longRun?.Log($"turn limit {_turnLimit} reached; the simulation is paused");
                        unsent = true;
                    }
                }

                LongRunTick();

                TimeSpan viewInterval = _server.Speed == SimulationSpeed.Max ? MaxSpeedViewInterval : LiveViewInterval;
                if (unsent && (_server.Speed == SimulationSpeed.Paused || Stopwatch.GetElapsedTime(lastSend) >= viewInterval))
                {
                    _server.SendViews();
                    lastSend = Stopwatch.GetTimestamp();
                    unsent = false;
                }

                if (!ran && !unsent && (_server.HasUnsentChronicle || _server.HasDeferredViews))
                {
                    _server.SendViews();
                }

                Interlocked.Exchange(ref _turn, _server.State.Turn);
                if (!ran)
                {
                    Thread.Sleep(2);
                }
            }
        }
#pragma warning disable CA1031 // The thread must report any failure instead of killing the process.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _error = ex.ToString();
            _longRun?.Log("error on the simulation thread: " + ex);
            _longRun?.Finish(_server, "error on the simulation thread");
        }
        finally
        {
            _report?.Dispose();
            _report = null;
            _longRun?.Finish(_server, "the game was closed before the time limit");
        }
    }

    // Long run: the client's log lines, a stats row every few seconds, and the end at the time limit.
    private void LongRunTick()
    {
        if (_longRun is null || _longRun.Finished)
        {
            return;
        }

        while (_clientLog.TryDequeue(out string? line))
        {
            _longRun.Log(line);
        }

        if (_longRun.Tick(_server))
        {
            _server.Pause("Long run finished: the statistics are in " + _longRun.Folder);
            _longRunFinished = true;
        }
    }

    // One CSV line per turn (start menu option "turn report"): turn, year, turn time, people, lived-in settlements.
    private void Report()
    {
        if (_reportPath is null)
        {
            return;
        }

        if (_report is null)
        {
            string? dir = Path.GetDirectoryName(Path.GetFullPath(_reportPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _report = new StreamWriter(_reportPath, append: false) { AutoFlush = true };
            _report.WriteLine("turn,year,turn_ms,people,settlements");
        }

        GameState state = _server.State;
        long people = 0;
        int settlements = 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive)
            {
                people += s.TotalPopulation();
                settlements++;
            }
        }

        double ms = _server.LastTurnMicroseconds / 1000.0;
        _report.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{state.Turn},{state.Month / 12},{ms:F2},{people},{settlements}"));
    }
}
