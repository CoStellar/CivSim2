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

    private readonly ConcurrentQueue<IConnection> _pending = new();
    private readonly Thread _thread;
    private readonly GameServer _server;
    private volatile bool _stopping;
    private volatile string? _error;
    private long _turn;
    private long _turnLimit;
    private string? _reportPath;
    private StreamWriter? _report;

    private LocalServerHost(GameServer server)
    {
        _server = server;
        _thread = new Thread(Run) { IsBackground = true, Name = "CivSim simulation" };
    }

    /// <summary>Last completed turn (for display only; the authoritative value is on the simulation thread).</summary>
    public long Turn => Interlocked.Read(ref _turn);

    /// <summary>Set when the simulation thread stopped because of an exception.</summary>
    public string? Error => _error;

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
        };
        host._thread.Start();
        return host;
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
    }

    /// <summary>Time between turns at a speed, or null when paused.</summary>
    public static TimeSpan? TurnInterval(SimulationSpeed speed) => speed switch
    {
        SimulationSpeed.X1 => TimeSpan.FromSeconds(2),
        SimulationSpeed.X2 => TimeSpan.FromSeconds(1),
        SimulationSpeed.X5 => TimeSpan.FromMilliseconds(400),
        SimulationSpeed.Max => TimeSpan.Zero,
        _ => null,
    };

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
                TimeSpan? interval = TurnInterval(_server.Speed);
                if (!ran && interval is { } wait && _server.ConnectedObservers > 0 && Stopwatch.GetElapsedTime(lastTurn) >= wait)
                {
                    lastTurn = Stopwatch.GetTimestamp();
                    bool max = _server.Speed == SimulationSpeed.Max;
                    _server.AdvanceTurn(sendViews: !max);
                    unsent = max;
                    ran = true;
                    Report();
                    if (_turnLimit > 0 && _server.State.Turn == _turnLimit)
                    {
                        _server.Pause($"Turn limit {_turnLimit} reached; the simulation is paused.");
                        unsent = true;
                    }
                }

                if (unsent && (_server.Speed != SimulationSpeed.Max || Stopwatch.GetElapsedTime(lastSend) >= MaxSpeedViewInterval))
                {
                    _server.SendViews();
                    lastSend = Stopwatch.GetTimestamp();
                    unsent = false;
                }

                if (!ran && !unsent && _server.HasUnsentChronicle)
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
        }
        finally
        {
            _report?.Dispose();
            _report = null;
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
