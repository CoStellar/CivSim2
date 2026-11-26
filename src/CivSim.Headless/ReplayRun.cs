using System;
using System.Globalization;
using System.IO;
using CivSim.Core.Content;
using CivSim.Core.Hashing;
using CivSim.Data;
using CivSim.Server.Replays;

namespace CivSim.Headless;

/// <summary>Plays a replay file again and says whether the game came out the same (T11, <c>--replay</c>).</summary>
public static class ReplayRun
{
    /// <summary>Replays <paramref name="path"/>. Exit code 0 = the replay matches its recording, 1 = it drifted apart, 2 = no file.</summary>
    public static int Execute(string path, TextWriter log)
    {
        ArgumentNullException.ThrowIfNull(log);
        ReplayData data;
        try
        {
            using FileStream file = File.OpenRead(path);
            data = ReplaySerializer.Read(file);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            log.WriteLine($"replay: {ex.Message}");
            return 2;
        }

        CultureInfo inv = CultureInfo.InvariantCulture;
        log.WriteLine(string.Create(inv,
            $"replay:             {path}: seed {data.Settings.Seed}, map {data.Settings.MapWidth}x{data.Settings.MapHeight}, ticks {data.StartTick}-{data.EndTick}, {data.Events.Count} events, {data.Checkpoints.Count} checkpoints"));
        if (data.StartSave is not null)
        {
            log.WriteLine("start:              from a save (the game was loaded)");
        }

        GameContent content = ContentLoader.Load(ContentLoader.DefaultDirectory);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ReplayResult result = ReplayPlayer.Play(data, content);
        log.WriteLine(string.Create(inv, $"checked:            {result.Checked} checkpoints in {clock.Elapsed.TotalSeconds:F1} s"));
        log.WriteLine(string.Create(inv, $"state hash:         {GameStateHash.Compute(result.State):X16} at tick {result.State.Turn}"));
        if (result.Divergence is { } d)
        {
            log.WriteLine(string.Create(inv, $"DIFFERENT:          first at tick {d.Tick}: {d.Expected:X16} recorded, {d.Actual:X16} now"));
            log.WriteLine($"parts that differ:  {string.Join(", ", d.Sections)}");
            return 1;
        }

        log.WriteLine("result:             the same game, bit for bit");
        return 0;
    }
}
