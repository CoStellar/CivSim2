using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CivSim.Core.Diplomacy;
using CivSim.Core.Simulation;
using CivSim.Core.State;

namespace CivSim.Server;

/// <summary>
/// Long run ("benchmark for hours"): while the game runs, writes what the program and the simulation do into a folder,
/// so a run of several hours can be studied afterwards. Files:
/// <list type="bullet">
/// <item><c>stats.csv</c>: one row every few seconds: memory, processor, frame rate, turn time, backlog, and the state of
/// the world (people, civilizations, wars, treaties).</item>
/// <item><c>turns.csv</c>: one row per turn with the time of each of the 11 phases.</item>
/// <item><c>events.csv</c>: every chronicle line with its turn, year and kind (war, peace, secession...).</item>
/// <item><c>log.txt</c>: start settings, slow turns, a window falling behind, new memory peaks, errors, the end.</item>
/// <item><c>summary.txt</c>: the main numbers at the end.</item>
/// </list>
/// Runs on the simulation thread; only <see cref="Frame"/> is called from the client thread. It reads the state, never
/// changes it, so the game is the same with or without it.
/// </summary>
public sealed class LongRunRecorder : IDisposable
{
    /// <summary>A turn slower than this is written to the log.</summary>
    public const double SlowTurnMs = 1000;

    /// <summary>A client frame slower than this counts as slow (below 30 frames per second).</summary>
    public const double SlowFrameMs = 33.4;

    /// <summary>A new peak of the process memory is logged when it is this much above the last logged one.</summary>
    public const long PeakStepBytes = 256L * 1024 * 1024;

    /// <summary>Column names of <c>stats.csv</c>.</summary>
    public const string StatsHeader =
        "elapsed_s,clock,turn,year,turns,turns_per_s,turn_ms_avg,turn_ms_p95,turn_ms_max," +
        "working_set_mb,private_mb,gc_heap_mb,gc_committed_mb,gc_gen0,gc_gen1,gc_gen2,gc_pause_pct,cpu_pct,cpu_cores,threads," +
        "fps,client_ms_avg,client_ms_max,frame_gap_max_ms,slow_frames,frames,godot_static_mb,video_mb,godot_objects,godot_nodes,backlog_max,clients_behind," +
        "civs_alive,civs_total,settlements,settlements_total,people,era_max,stability_avg,unrest_civs," +
        "wars,truces,trade,non_aggression,alliances,proposals,relations," +
        "events,ev_war,ev_peace,ev_treaty,ev_secession,ev_coup,ev_died_out,ev_era,ev_settlement,ev_unrest,ev_epidemic,ev_nature,ev_other";

    /// <summary>Kinds of chronicle lines, in the order of the ev_ columns of <c>stats.csv</c>.</summary>
    public static readonly string[] Kinds =
        ["war", "peace", "treaty", "secession", "coup", "died_out", "era", "settlement", "unrest", "epidemic", "nature", "other"];

    /// <summary>
    /// Routine lines (unrest, new camps and the rest) written to <c>events.csv</c> at most; important ones (wars, peace,
    /// treaties, secessions, coups, civilizations dying out, eras) are always written. Epidemics and droughts, fires and
    /// floods come by the thousand and are only counted.
    /// </summary>
    public const long RoutineEventLimit = 1_000_000;

    private static readonly string[] PhaseNames =
        ["world", "perception", "decisions", "commands", "movement", "combat", "economy", "population", "state", "science", "record"];

    private readonly object _frameLock = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly long _start = Stopwatch.GetTimestamp();
    private readonly TimeSpan _limit;
    private readonly TimeSpan _sampleEvery;
    private readonly StreamWriter _stats;
    private readonly StreamWriter _turns;
    private readonly StreamWriter _events;
    private readonly StreamWriter _log;
    private readonly int _unrestBelow;
    private readonly List<double> _turnMs = new(4096);
    private long _lastSample;
    private TimeSpan _lastCpu;
    private TimeSpan _lastGcPause;
    private long _peakLogged;
    private long _peakWorkingSet;
    private readonly long _firstWorkingSet;
    private int _behindLogged;
    private bool _finished;

    // Totals over the whole run.
    private long _totalTurns;
    private double _totalTurnMs;
    private double _maxTurnMs;
    private readonly long[] _kindCount = new long[Kinds.Length];
    private long _eventCount;
    private long _routineWritten;
    private long _rows;

    // Client frames since the last row (guarded by _frameLock).
    private long _frames;
    private double _frameMsSum;
    private double _frameMsMax;
    private double _gapMax;
    private (long StaticMb, long VideoMb, long Objects, long Nodes) _engine;
    private long _slowFrames;
    private long _totalFrames;
    private long _totalSlowFrames;

    /// <summary>
    /// Opens the files in <paramref name="folder"/> (created when missing). <paramref name="limit"/> zero means no time
    /// limit; <paramref name="sampleEvery"/> is the time between rows of <c>stats.csv</c>.
    /// </summary>
    public LongRunRecorder(string folder, TimeSpan limit, TimeSpan sampleEvery, int unrestBelow = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(Folder);
        _limit = limit;
        _sampleEvery = sampleEvery > TimeSpan.Zero ? sampleEvery : TimeSpan.FromSeconds(10);
        _unrestBelow = unrestBelow;
        _stats = Open("stats.csv");
        _turns = Open("turns.csv");
        _events = Open("events.csv");
        _log = Open("log.txt");
        _stats.WriteLine(StatsHeader);
        _turns.WriteLine("turn,year,elapsed_s,turn_ms," + string.Join(',', Array.ConvertAll(PhaseNames, p => p + "_ms")) + ",events");
        _events.WriteLine("turn,year,elapsed_s,kind,text");
        _lastSample = _start;
        _process.Refresh();
        _lastCpu = _process.TotalProcessorTime;
        _lastGcPause = GC.GetTotalPauseDuration();
        _firstWorkingSet = _process.WorkingSet64;
        _peakLogged = _firstWorkingSet;
        _peakWorkingSet = _firstWorkingSet;
    }

    /// <summary>Full path of the folder with the files.</summary>
    public string Folder { get; }

    /// <summary>True once the time limit passed (or <see cref="Finish"/> was called); nothing more is written.</summary>
    public bool Finished => _finished;

    /// <summary>Time since the recorder started.</summary>
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_start);

    /// <summary>Kind of a chronicle line (one of <see cref="Kinds"/>).</summary>
    public static string KindOf(string line) => Kinds[KindIndex(line)];

    // Index into Kinds; the order of the tests matters ("An epidemic broke out" before anything else that matches).
    private static int KindIndex(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Contains(" declared war on ", StringComparison.Ordinal) ? 0
            : line.Contains(" made peace after ", StringComparison.Ordinal) ? 1
            : line.EndsWith(" agreed to trade.", StringComparison.Ordinal)
                || line.EndsWith(" swore not to attack each other.", StringComparison.Ordinal)
                || line.EndsWith(" formed a defensive alliance.", StringComparison.Ordinal) ? 2
            : line.Contains(" broke away from the ", StringComparison.Ordinal) ? 3
            : line.StartsWith("A coup in the ", StringComparison.Ordinal) ? 4
            : line.EndsWith(" has died out.", StringComparison.Ordinal) ? 5
            : line.Contains(" entered the ", StringComparison.Ordinal) ? 6
            : line.Contains("pidemic", StringComparison.Ordinal) ? 9
            : line.StartsWith("A drought ", StringComparison.Ordinal)
                || line.StartsWith("A wildfire ", StringComparison.Ordinal)
                || line.StartsWith("The river flooded ", StringComparison.Ordinal) ? 10
            : line.StartsWith("Unrest spread in ", StringComparison.Ordinal)
                || line.StartsWith("Calm returned to ", StringComparison.Ordinal) ? 8
            : line.Contains(" founded ", StringComparison.Ordinal) ? 7
            : 11;
    }

    /// <summary>Writes a line to <c>log.txt</c> with the time since the start.</summary>
    public void Log(string text)
    {
        if (_finished)
        {
            return;
        }

        TimeSpan t = Elapsed;
        _log.WriteLine(string.Create(CultureInfo.InvariantCulture, $"[{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}] {text}"));
    }

    /// <summary>
    /// Called by the client once per frame: the time its own work took, and the time since the previous frame (what
    /// the eye sees; over <see cref="SlowFrameMs"/> the frame counts as slow). Thread-safe.
    /// </summary>
    public void Frame(double workMs, double gapMs)
    {
        lock (_frameLock)
        {
            _frames++;
            _frameMsSum += workMs;
            _frameMsMax = Math.Max(_frameMsMax, workMs);
            _gapMax = Math.Max(_gapMax, gapMs);
            if (gapMs > SlowFrameMs)
            {
                _slowFrames++;
            }
        }
    }

    /// <summary>The client's engine numbers: its own memory, video memory, objects and nodes. Thread-safe.</summary>
    public void Engine(long staticMb, long videoMb, long objects, long nodes)
    {
        lock (_frameLock)
        {
            _engine = (staticMb, videoMb, objects, nodes);
        }
    }

    /// <summary>Records the turn that just ended: its phase times and its chronicle lines.</summary>
    public void TurnDone(GameState state, TurnReport report, IReadOnlyList<string> chronicle)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(chronicle);
        if (_finished)
        {
            return;
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        double ms = Stopwatch.GetElapsedTime(0, report.TotalTicks).TotalMilliseconds;
        double elapsed = Elapsed.TotalSeconds;
        long year = state.Month / 12;
        _turnMs.Add(ms);
        _totalTurns++;
        _totalTurnMs += ms;
        _maxTurnMs = Math.Max(_maxTurnMs, ms);

        var row = new StringBuilder(160);
        row.Append(inv, $"{state.Turn},{year},{elapsed:F1},{ms:F2}");
        for (int p = 0; p < TurnPhases.Count; p++)
        {
            row.Append(inv, $",{report.PhaseDuration((TurnPhase)p).TotalMilliseconds:F2}");
        }

        row.Append(inv, $",{chronicle.Count}");
        _turns.WriteLine(row.ToString());

        foreach (string line in chronicle)
        {
            int kind = KindIndex(line);
            _eventCount++;
            _kindCount[kind]++;
            if (kind is 9 or 10)
            {
                continue; // epidemics and natural disasters: only counted
            }

            if (kind >= 7)
            {
                if (_routineWritten == RoutineEventLimit)
                {
                    Log(string.Create(inv, $"events.csv reached {RoutineEventLimit} routine lines; from now on only wars, peace, treaties, secessions, coups, deaths and eras are written"));
                }

                if (_routineWritten++ >= RoutineEventLimit)
                {
                    continue;
                }
            }

            _events.WriteLine(string.Create(inv, $"{state.Turn},{year},{elapsed:F1},{Kinds[kind]},{Quote(line)}"));
        }

        if (ms > SlowTurnMs)
        {
            Log(string.Create(inv, $"slow turn {state.Turn} (year {year}): {ms:F0} ms, slowest phase {SlowestPhase(report)}"));
        }
    }

    /// <summary>
    /// Writes a row of <c>stats.csv</c> when the sample time has passed (or always with <paramref name="force"/>), and
    /// finishes when the time limit passed. <paramref name="server"/> gives the state and the clients' backlog.
    /// Returns true when the run just finished.
    /// </summary>
    public bool Tick(GameServer server, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (_finished)
        {
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        if (!force && Stopwatch.GetElapsedTime(_lastSample, now) < _sampleEvery)
        {
            return false;
        }

        Sample(server, Stopwatch.GetElapsedTime(_lastSample, now));
        _lastSample = now;
        if (_limit > TimeSpan.Zero && Elapsed >= _limit)
        {
            Finish(server, string.Create(CultureInfo.InvariantCulture, $"time limit {Hours(_limit)} reached"));
            return true;
        }

        return false;
    }

    /// <summary>Writes the last row, the summary and closes the files. Safe to call more than once.</summary>
    public void Finish(GameServer? server, string reason)
    {
        if (_finished)
        {
            return;
        }

        // The last row, unless one was written a moment ago (at the time limit).
        if (server is not null && (_rows == 0 || Stopwatch.GetElapsedTime(_lastSample) > TimeSpan.FromSeconds(1)))
        {
            Sample(server, Stopwatch.GetElapsedTime(_lastSample));
        }

        Log("end: " + reason);
        WriteSummary(server?.State, reason);
        _finished = true;
        Dispose();
    }

    /// <summary>Closes the files.</summary>
    public void Dispose()
    {
        _stats.Dispose();
        _turns.Dispose();
        _events.Dispose();
        _log.Dispose();
        _process.Dispose();
    }

    private void Sample(GameServer server, TimeSpan window)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        GameState state = server.State;
        _process.Refresh();
        TimeSpan cpu = _process.TotalProcessorTime;
        double wall = Math.Max(0.001, window.TotalSeconds);
        double cores = (cpu - _lastCpu).TotalSeconds / wall;
        _lastCpu = cpu;
        TimeSpan pause = GC.GetTotalPauseDuration();
        double pausePct = (pause - _lastGcPause).TotalSeconds * 100 / wall;
        _lastGcPause = pause;
        long workingSet = _process.WorkingSet64;
        GCMemoryInfo gc = GC.GetGCMemoryInfo();

        // Turn times of this window.
        int n = _turnMs.Count;
        double sum = 0;
        double max = 0;
        foreach (double ms in _turnMs)
        {
            sum += ms;
            max = Math.Max(max, ms);
        }

        _turnMs.Sort();
        double p95 = n == 0 ? 0 : _turnMs[Math.Min(n - 1, (int)Math.Ceiling(n * 0.95) - 1)];
        _turnMs.Clear();

        long frames;
        double frameAvg;
        double frameMax;
        double gapMax;
        long slow;
        (long StaticMb, long VideoMb, long Objects, long Nodes) engine;
        lock (_frameLock)
        {
            engine = _engine;
            gapMax = _gapMax;
            _gapMax = 0;
            frames = _frames;
            frameAvg = _frames == 0 ? 0 : _frameMsSum / _frames;
            frameMax = _frameMsMax;
            slow = _slowFrames;
            _totalFrames += _frames;
            _totalSlowFrames += _slowFrames;
            _frames = 0;
            _frameMsSum = 0;
            _frameMsMax = 0;
            _slowFrames = 0;
        }

        // The world.
        int alive = 0;
        int eraMax = 0;
        long stability = 0;
        int unrest = 0;
        long relations = 0;
        foreach (CivilizationState c in state.Civilizations)
        {
            relations += c.Relations.Count;
            if (!c.IsAlive)
            {
                continue;
            }

            alive++;
            eraMax = Math.Max(eraMax, c.Era);
            stability += c.Stability;
            unrest += c.Stability < _unrestBelow ? 1 : 0;
        }

        int active = 0;
        long people = 0;
        foreach (SettlementState s in state.Settlements)
        {
            if (s.IsActive)
            {
                active++;
                people += s.TotalPopulation();
            }
        }

        var treaties = new int[5];
        foreach (Treaty t in state.Treaties.All)
        {
            if ((int)t.Kind < treaties.Length)
            {
                treaties[(int)t.Kind]++;
            }
        }

        int backlog = server.MaxClientBacklog;
        int behind = server.ClientsBehind;
        if (behind > 0)
        {
            // Logged the first time and then at most every tenth time, so a slow window does not fill the log.
            if (_behindLogged % 10 == 0)
            {
                Log(string.Create(inv, $"the window fell behind the simulation (backlog {backlog} messages; {_behindLogged + 1} times so far); views wait until it catches up"));
            }

            _behindLogged++;
        }
        _peakWorkingSet = Math.Max(_peakWorkingSet, workingSet);
        if (workingSet >= _peakLogged + PeakStepBytes)
        {
            _peakLogged = workingSet;
            Log(string.Create(inv, $"memory peak {Mb(workingSet)} MB at turn {state.Turn} (start {Mb(_firstWorkingSet)} MB)"));
        }

        _stats.WriteLine(string.Create(
            inv,
            $"{Elapsed.TotalSeconds:F1},{DateTime.Now:yyyy-MM-dd HH:mm:ss},{state.Turn},{state.Month / 12},{n},{n / wall:F2},{(n == 0 ? 0 : sum / n):F2},{p95:F2},{max:F2}," +
            $"{Mb(workingSet)},{Mb(_process.PrivateMemorySize64)},{Mb(GC.GetTotalMemory(false))},{Mb(gc.TotalCommittedBytes)},{GC.CollectionCount(0)},{GC.CollectionCount(1)},{GC.CollectionCount(2)},{pausePct:F2},{cores * 100 / Environment.ProcessorCount:F1},{cores:F2},{_process.Threads.Count}," +
            $"{frames / wall:F1},{frameAvg:F2},{frameMax:F2},{gapMax:F1},{slow},{frames},{engine.StaticMb},{engine.VideoMb},{engine.Objects},{engine.Nodes},{backlog},{behind}," +
            $"{alive},{state.Civilizations.Count},{active},{state.Settlements.Count},{people},{eraMax},{(alive == 0 ? 0 : stability / alive)},{unrest}," +
            $"{treaties[0]},{treaties[1]},{treaties[2]},{treaties[3]},{treaties[4]},{state.Treaties.Proposals.Count},{relations}," +
            $"{_eventCount},{string.Join(',', _kindCount)}"));
        _rows++;

        // Everything written so far reaches the disk, so a crash or a closed window loses at most one sample.
        _stats.Flush();
        _turns.Flush();
        _events.Flush();
        _log.Flush();
    }

    private void WriteSummary(GameState? state, string reason)
    {
        CultureInfo inv = CultureInfo.InvariantCulture;
        _process.Refresh();
        long end = _process.WorkingSet64;
        long frames;
        long slow;
        lock (_frameLock)
        {
            frames = _totalFrames + _frames;
            slow = _totalSlowFrames + _slowFrames;
        }

        TimeSpan t = Elapsed;
        var lines = new List<string>
        {
            "CivSim 2 long run",
            "ended:            " + reason,
            string.Create(inv, $"run time:         {Hours(t)}"),
            string.Create(inv, $"turns:            {_totalTurns} ({_totalTurns / Math.Max(1, t.TotalSeconds):F1} per second)"),
            string.Create(inv, $"turn time:        avg {(_totalTurns == 0 ? 0 : _totalTurnMs / _totalTurns):F1} ms, max {_maxTurnMs:F0} ms"),
            string.Create(inv, $"memory (process): start {Mb(_firstWorkingSet)} MB, end {Mb(end)} MB, peak {Mb(Math.Max(_peakWorkingSet, end))} MB"),
            string.Create(inv, $"frames:           {frames} ({frames / Math.Max(1, t.TotalSeconds):F1} per second), slow (under 30 fps) {slow}"),
            string.Create(inv, $"garbage collections: gen0 {GC.CollectionCount(0)}, gen1 {GC.CollectionCount(1)}, gen2 {GC.CollectionCount(2)}, pauses {GC.GetTotalPauseDuration().TotalSeconds:F1} s"),
            string.Create(inv, $"events:           {_eventCount} (wars {_kindCount[0]}, peaces {_kindCount[1]}, treaties {_kindCount[2]}, secessions {_kindCount[3]}, coups {_kindCount[4]}, died out {_kindCount[5]}, eras {_kindCount[6]}, epidemics {_kindCount[9]}, disasters {_kindCount[10]})"),
            string.Create(inv, $"stats rows:       {_rows}"),
        };
        if (state is not null)
        {
            int alive = 0;
            foreach (CivilizationState c in state.Civilizations)
            {
                alive += c.IsAlive ? 1 : 0;
            }

            lines.Add(string.Create(inv, $"world at the end: turn {state.Turn}, year {state.Month / 12}, {alive} of {state.Civilizations.Count} civilizations alive"));
        }

        File.WriteAllLines(Path.Combine(Folder, "summary.txt"), lines);
    }

    private StreamWriter Open(string name) => new(Path.Combine(Folder, name), append: false, new UTF8Encoding(false)) { AutoFlush = false };

    private static string SlowestPhase(TurnReport report)
    {
        int best = 0;
        for (int p = 1; p < TurnPhases.Count; p++)
        {
            best = report.PhaseTicks((TurnPhase)p) > report.PhaseTicks((TurnPhase)best) ? p : best;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{PhaseNames[best]} {report.PhaseDuration((TurnPhase)best).TotalMilliseconds:F0} ms");
    }

    private static long Mb(long bytes) => bytes / (1024 * 1024);

    private static string Hours(TimeSpan t) => string.Create(CultureInfo.InvariantCulture, $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s");

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
