using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace CivSim.Game.Audio;

/// <summary>
/// The game's sound (autoload "Audio", 2026-11-24): music that changes with the era, ambient loops that follow the camera,
/// and short effects. Which file plays for what is in <c>audio/sounds.json</c>; the files are read as plain .ogg at run time
/// (no Godot import), first from <c>audio/custom/</c> and the user folder <c>sounds/</c>, then from <c>audio/</c>.
/// Four buses (Music, Ambient, Effects, Interface) go to Master; their volumes are <see cref="AudioSettings"/>.
/// </summary>
public partial class AudioManager : Node
{
    /// <summary>The bus names, in the order of <see cref="AudioSettings.Volumes"/> after Master.</summary>
    public static readonly string[] Buses = ["Music", "Ambient", "Effects", "Interface"];

    private const int EffectVoices = 10;
    private const int InterfaceVoices = 4;
    private const float Silent = -60f;

    private readonly Dictionary<string, AudioStream?> _streams = [];
    private readonly Dictionary<string, double> _lastPlayed = [];
    private readonly List<AudioStreamPlayer> _effects = [];
    private readonly List<AudioStreamPlayer> _interface = [];
    private readonly Dictionary<string, (AudioStreamPlayer Player, float Target)> _ambient = [];
    private AudioManifest _manifest = new();
    private string _directory = "";
    private string[] _overrides = [];
    private AudioStreamPlayer _musicA = null!;
    private AudioStreamPlayer _musicB = null!;
    private string _playlist = "";
    private int _track;
    private double _nextTrackAt = -1;
    private double _now;

    /// <summary>The running manager (null before the autoload is ready, e.g. in tools).</summary>
    public static AudioManager? Instance { get; private set; }

    /// <summary>Volumes and mute as saved.</summary>
    public AudioSettings Settings { get; private set; } = new();

    /// <summary>Raised when the volumes change (the settings panel follows the K key).</summary>
    public event Action? SettingsChanged;

    /// <summary>Plays a sound event of <c>audio/sounds.json</c> (e.g. "click"); does nothing when no manager runs.</summary>
    public static void Play(string sound) => Instance?.PlaySound(sound);

    /// <inheritdoc/>
    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        _directory = FindAudioDirectory();
        _overrides = [Path.Combine(_directory, "custom"), ProjectSettings.GlobalizePath("user://sounds")];
        _manifest = AudioManifest.Load(Path.Combine(_directory, "sounds.json"));
        foreach (string bus in Buses)
        {
            if (AudioServer.GetBusIndex(bus) < 0)
            {
                AudioServer.AddBus();
                int i = AudioServer.BusCount - 1;
                AudioServer.SetBusName(i, bus);
                AudioServer.SetBusSend(i, "Master");
            }
        }

        for (int i = 0; i < EffectVoices; i++)
        {
            _effects.Add(Player("Effects"));
        }

        for (int i = 0; i < InterfaceVoices; i++)
        {
            _interface.Add(Player("Interface"));
        }

        _musicA = Player("Music");
        _musicB = Player("Music");
        _musicA.Finished += TrackFinished;
        _musicB.Finished += TrackFinished;
        Apply(AudioSettings.Load(), save: false);
        GetTree().NodeAdded += HookButton;
        SetMusic("menu");
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        _now += delta;
        float fade = (float)(delta / Math.Max(0.1, _manifest.MusicFadeSeconds));
        FadeTowards(_musicA, _musicA.HasMeta("on") ? 1f : 0f, fade);
        FadeTowards(_musicB, _musicB.HasMeta("on") ? 1f : 0f, fade);
        foreach ((AudioStreamPlayer player, float target) in _ambient.Values)
        {
            FadeTowards(player, target, (float)delta / 2.5f);
        }

        if (_nextTrackAt >= 0 && _now >= _nextTrackAt)
        {
            _nextTrackAt = -1;
            StartTrack(next: true);
        }
    }

    /// <inheritdoc/>
    public override void _UnhandledKeyInput(InputEvent e)
    {
        // K: sound on and off (anywhere a text field does not take the key).
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.K, CtrlPressed: false, AltPressed: false })
        {
            AudioSettings s = Settings.Copy();
            s.Muted = !s.Muted;
            Apply(s);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Uses and saves new volumes.</summary>
    public void Apply(AudioSettings settings, bool save = true)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings;
        SetBus("Master", settings.Muted ? 0 : settings.Master);
        for (int i = 0; i < Buses.Length; i++)
        {
            SetBus(Buses[i], settings.Volumes[i]);
        }

        if (save)
        {
            settings.Save();
        }

        SettingsChanged?.Invoke();
    }

    /// <summary>Plays the playlist of <c>music</c> in sounds.json (an era key or "menu"); the same one keeps playing.</summary>
    public void SetMusic(string playlist)
    {
        if (playlist == _playlist || !_manifest.Music.ContainsKey(playlist))
        {
            return;
        }

        _playlist = playlist;
        _track = 0;
        StartTrack(next: false);
    }

    /// <summary>Fades the music out (end of the game); <see cref="SetMusic"/> starts it again.</summary>
    public void StopMusic()
    {
        _playlist = "";
        _nextTrackAt = -1;
        _musicA.RemoveMeta("on");
        _musicB.RemoveMeta("on");
    }

    /// <summary>Sets how loud an ambient layer of sounds.json plays, 0 to 1 (it fades there over a few seconds).</summary>
    public void SetAmbient(string layer, float level)
    {
        if (!_ambient.TryGetValue(layer, out (AudioStreamPlayer Player, float Target) a))
        {
            if (level <= 0 || !_manifest.Ambient.TryGetValue(layer, out string? file) || Stream("ambient", file) is not { } stream)
            {
                return;
            }

            a = (Player("Ambient"), 0f);
            a.Player.Stream = stream;
            a.Player.VolumeDb = Silent;
            a.Player.Play();
        }

        _ambient[layer] = (a.Player, Math.Clamp(level, 0f, 1f));
    }

    /// <summary>Fades every ambient layer out.</summary>
    public void ClearAmbient()
    {
        foreach (string layer in _ambient.Keys.ToList())
        {
            SetAmbient(layer, 0);
        }
    }

    /// <summary>The ambient layer of a biome key (sounds.json "biomeAmbient"), or null.</summary>
    public string? AmbientOfBiome(string biome) => _manifest.BiomeAmbient.GetValueOrDefault(biome);

    /// <summary>The ambient layers sounds.json knows.</summary>
    public IEnumerable<string> AmbientLayers => _manifest.Ambient.Keys;

    private void PlaySound(string sound)
    {
        if (!_manifest.Sounds.TryGetValue(sound, out SoundEntry? entry))
        {
            return;
        }

        if (_lastPlayed.TryGetValue(sound, out double last) && _now - last < entry.Cooldown)
        {
            return;
        }

        if (Stream("sfx", entry.File) is not { } stream)
        {
            return;
        }

        _lastPlayed[sound] = _now;
        List<AudioStreamPlayer> voices = entry.Bus == "Interface" ? _interface : _effects;
        AudioStreamPlayer player = voices.FirstOrDefault(p => !p.Playing) ?? voices.MinBy(p => p.GetPlaybackPosition() == 0 ? double.MaxValue : -p.GetPlaybackPosition())!;
        player.Stream = stream;
        player.VolumeDb = entry.VolumeDb;
        player.PitchScale = entry.Pitch > 0 ? 1f + (((GD.Randf() * 2) - 1) * entry.Pitch) : 1f;
        player.Play();
    }

    private void StartTrack(bool next)
    {
        if (!_manifest.Music.TryGetValue(_playlist, out string[]? tracks) || tracks.Length == 0)
        {
            return;
        }

        if (next)
        {
            _track = (_track + 1) % tracks.Length;
        }

        AudioStream? stream = Stream("music", tracks[_track]);
        if (stream is null)
        {
            return;
        }

        // The old track fades out on one player while the new one fades in on the other.
        AudioStreamPlayer from = _musicA.HasMeta("on") ? _musicA : _musicB;
        AudioStreamPlayer to = from == _musicA ? _musicB : _musicA;
        from.RemoveMeta("on");
        to.Stream = stream;
        to.VolumeDb = Silent;
        to.SetMeta("on", true);
        to.Play();
        _nextTrackAt = -1;
    }

    private void TrackFinished()
    {
        if (_playlist.Length == 0 || _musicA.Playing || _musicB.Playing)
        {
            return;
        }

        double[] pause = _manifest.MusicPauseSeconds;
        double lo = pause.Length > 0 ? pause[0] : 20;
        double hi = pause.Length > 1 ? pause[1] : lo;
        _nextTrackAt = _now + lo + (GD.Randf() * Math.Max(0, hi - lo));
    }

    private static void FadeTowards(AudioStreamPlayer player, float target, float step)
    {
        float now = player.VolumeDb <= Silent ? 0f : Mathf.DbToLinear(player.VolumeDb);
        float next = now < target ? Math.Min(target, now + step) : Math.Max(target, now - step);
        player.VolumeDb = next <= 0.001f ? Silent : Mathf.LinearToDb(next);
        if (next <= 0.001f && target <= 0 && player.Playing && player.Bus != "Ambient")
        {
            player.Stop();
        }
    }

    private AudioStreamPlayer Player(string bus)
    {
        var p = new AudioStreamPlayer { Bus = bus };
        AddChild(p);
        return p;
    }

    private static void SetBus(string name, int percent)
    {
        int i = AudioServer.GetBusIndex(name);
        if (i < 0)
        {
            return;
        }

        AudioServer.SetBusMute(i, percent <= 0);
        AudioServer.SetBusVolumeDb(i, percent <= 0 ? Silent : Mathf.LinearToDb(Math.Clamp(percent, 0, 100) / 100f));
    }

    // A click for every button of the game, wherever it is made.
    private void HookButton(Node node)
    {
        if (node is BaseButton button)
        {
            button.Pressed += () => PlaySound("click");
        }
    }

    private AudioStream? Stream(string folder, string file)
    {
        string key = folder + "/" + file;
        if (_streams.TryGetValue(key, out AudioStream? cached))
        {
            return cached;
        }

        AudioStream? stream = null;
        foreach (string root in _overrides.Append(Path.Combine(_directory, folder)))
        {
            foreach (string ext in new[] { ".ogg", ".wav" })
            {
                string path = Path.Combine(root, file + ext);
                if (File.Exists(path))
                {
                    stream = ext == ".ogg" ? AudioStreamOggVorbis.LoadFromFile(path) : AudioStreamWav.LoadFromFile(path);
                    break;
                }
            }

            if (stream is not null)
            {
                break;
            }
        }

        if (stream is null)
        {
            GD.PrintErr($"audio: {key} not found");
        }
        else if (folder == "ambient")
        {
            SetLoop(stream);
        }

        _streams[key] = stream;
        return stream;
    }

    private static void SetLoop(AudioStream stream)
    {
        switch (stream)
        {
            case AudioStreamOggVorbis ogg:
                ogg.Loop = true;
                break;
            case AudioStreamWav wav:
                wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                wav.LoopEnd = (int)(wav.GetLength() * wav.MixRate);
                break;
        }
    }

    /// <summary>The audio/ folder: next to the project in the editor, next to the exe in the exported game.</summary>
    public static string FindAudioDirectory()
    {
        string[] candidates =
        [
            ProjectSettings.GlobalizePath("res://../audio"),
            Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".", "audio"),
        ];
        return candidates.FirstOrDefault(d => File.Exists(Path.Combine(d, "sounds.json"))) ?? candidates[0];
    }
}

/// <summary>One sound event of sounds.json.</summary>
public sealed class SoundEntry
{
    /// <summary>File name in sfx/ without extension.</summary>
    public string File { get; set; } = "";

    /// <summary>"Effects" (default) or "Interface".</summary>
    public string Bus { get; set; } = "Effects";

    /// <summary>Loudness of this sound in dB (0 = as recorded).</summary>
    public float VolumeDb { get; set; }

    /// <summary>Seconds before the same sound may play again.</summary>
    public double Cooldown { get; set; }

    /// <summary>Random pitch change, e.g. 0.05 = ±5%.</summary>
    public float Pitch { get; set; }
}

/// <summary>audio/sounds.json: playlists, ambient layers and sound events.</summary>
public sealed class AudioManifest
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    /// <summary>Playlist key (era key or "menu") → music files.</summary>
    public Dictionary<string, string[]> Music { get; set; } = [];

    /// <summary>Silence between two pieces, seconds [from, to].</summary>
    public double[] MusicPauseSeconds { get; set; } = [20, 40];

    /// <summary>Seconds a piece takes to fade in or out.</summary>
    public double MusicFadeSeconds { get; set; } = 3;

    /// <summary>Ambient layer → file in ambient/.</summary>
    public Dictionary<string, string> Ambient { get; set; } = [];

    /// <summary>Biome key → ambient layer.</summary>
    public Dictionary<string, string> BiomeAmbient { get; set; } = [];

    /// <summary>Sound event → file and settings.</summary>
    public Dictionary<string, SoundEntry> Sounds { get; set; } = [];

    /// <summary>Unused text for people reading the file.</summary>
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    /// <summary>Reads the manifest; empty when missing or broken (the game then stays silent).</summary>
    public static AudioManifest Load(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                return JsonSerializer.Deserialize<AudioManifest>(System.IO.File.ReadAllText(path), Json) ?? new AudioManifest();
            }

            GD.PrintErr($"audio: {path} missing, no sound");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            GD.PrintErr($"audio: {path} not read: {ex.Message}");
        }

        return new AudioManifest();
    }
}
