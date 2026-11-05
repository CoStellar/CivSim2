using System.Linq;
using System;
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
        PlanetPreset preset = content.Planets.FirstOrDefault(p => p.Key == options.Planet)
            ?? throw new ArgumentException($"Unknown planet '{options.Planet}'.", nameof(options));
        GameSettings settings = new GameSettings(options.Seed, options.Width, options.Height, options.Civilizations).WithPreset(preset);
        (PlanetParameters planet, _) = PlanetSetup.Build(preset.Planet, preset.AutoCo2, new PlanetOverrides
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
        var host = new LocalServerHost(GameServer.CreateNew(settings, content));
        host._thread.Start();
        return host;
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
    }
}
