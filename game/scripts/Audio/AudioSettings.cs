using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace CivSim.Game.Audio;

/// <summary>Volumes the player chose (user://audio_settings.json), percent 0–100.</summary>
public sealed class AudioSettings
{
    private const string FileName = "user://audio_settings.json";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Everything together.</summary>
    public int Master { get; set; } = 80;

    /// <summary>Music, Ambient, Effects, Interface (the order of <see cref="AudioManager.Buses"/>).</summary>
    public int[] Volumes { get; set; } = [60, 40, 80, 70];

    /// <summary>All sound off (key K).</summary>
    public bool Muted { get; set; }

    /// <summary>A copy to change.</summary>
    public AudioSettings Copy() => new() { Master = Master, Volumes = [.. Volumes], Muted = Muted };

    /// <summary>Reads the saved volumes; defaults when there are none or the file is broken.</summary>
    public static AudioSettings Load()
    {
        try
        {
            string path = ProjectSettings.GlobalizePath(FileName);
            if (File.Exists(path) && JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(path), Json) is { } s)
            {
                if (s.Volumes is not { Length: 4 })
                {
                    s.Volumes = new AudioSettings().Volumes;
                }

                return s;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            GD.PrintErr($"audio settings not read: {ex.Message}");
        }

        return new AudioSettings();
    }

    /// <summary>Writes the volumes.</summary>
    public void Save()
    {
        try
        {
            File.WriteAllText(ProjectSettings.GlobalizePath(FileName), JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            GD.PrintErr($"audio settings not saved: {ex.Message}");
        }
    }
}
