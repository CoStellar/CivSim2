using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MeltySynth;

namespace CivSim.Game.Audio;

/// <summary>
/// The music (Patryk, 2026-11-24): pieces of about five minutes, stored as notes (.mid) and played live on the game's
/// instruments (<c>audio/instruments.sf2</c>, a cut-down FluidR3 GM, MIT) with MeltySynth (MIT). A playlist is "era_terrain";
/// each piece plays <see cref="AudioManifest.PlaysBeforeNext"/> times in a loop, then the next one of the list follows.
/// A change of playlist cross-fades. A piece of the same name as .ogg in audio/custom/ (or the user folder sounds/) plays
/// instead of the notes.
/// </summary>
public partial class MusicPlayer : Node
{
    private const int Rate = 44100;
    private const float BufferSeconds = 0.6f;

    private readonly List<IVoice> _voices = [];
    private AudioManifest _manifest = new();
    private string _musicDirectory = "";
    private string[] _overrides = [];
    private Task<SoundFont?>? _font;
    private AudioStreamGeneratorPlayback? _playback;
    private AudioStreamPlayer _out = null!;
    private float[] _left = [];
    private float[] _right = [];
    private float[] _mixL = [];
    private float[] _mixR = [];
    private Vector2[] _frames = [];
    private string _playlist = "";
    private string[] _tracks = [];
    private int _track;
    private string? _pending;

    /// <summary>The playlist playing now ("" = none).</summary>
    public string Playlist => _playlist;

    /// <summary>Uses the manifest and the folders of <see cref="AudioManager"/>; starts loading the instruments.</summary>
    public void Setup(AudioManifest manifest, string audioDirectory, string[] overrides)
    {
        _manifest = manifest;
        _musicDirectory = Path.Combine(audioDirectory, "music");
        _overrides = overrides;
        string font = Path.Combine(audioDirectory, "instruments.sf2");
        _font = Task.Run(() =>
        {
            try
            {
                return File.Exists(font) ? new SoundFont(font) : null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                GD.PrintErr($"audio: {font} not read: {ex.Message}");
                return null;
            }
        });
    }

    /// <inheritdoc/>
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var stream = new AudioStreamGenerator { MixRate = Rate, BufferLength = BufferSeconds };
        _out = new AudioStreamPlayer { Bus = "Music", Stream = stream };
        AddChild(_out);
        _out.Play();
        _playback = (AudioStreamGeneratorPlayback)_out.GetStreamPlayback();
    }

    /// <summary>Plays a playlist: a key of sounds.json "music", else the pieces music/&lt;key&gt;_1, _2... found.</summary>
    public void Play(string playlist)
    {
        if (playlist == _playlist && _pending is null)
        {
            return;
        }

        if (_font is { IsCompleted: false })
        {
            _pending = playlist; // the instruments are still loading
            return;
        }

        _pending = null;
        string[] tracks = Tracks(playlist);
        if (tracks.Length == 0)
        {
            GD.PrintErr($"audio: no music for {playlist}");
            return;
        }

        _playlist = playlist;
        _tracks = tracks;
        _track = (int)(GD.Randi() % (uint)tracks.Length);
        StartTrack();
    }

    /// <summary>Fades the music out.</summary>
    public void Stop()
    {
        _playlist = "";
        _pending = null;
        foreach (IVoice v in _voices)
        {
            v.Target = 0;
        }
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_pending is not null && _font is { IsCompleted: true })
        {
            string p = _pending;
            _playlist = "";
            Play(p);
        }

        float fadeStep = (float)(delta / Math.Max(0.1, _manifest.MusicFadeSeconds));
        foreach (IVoice v in _voices)
        {
            v.Fade(fadeStep);
        }

        // The piece that is heard has played its rounds: the next one of the list.
        if (_voices.LastOrDefault() is { Target: > 0 } current && current.Played >= current.Length * Math.Max(1, _manifest.PlaysBeforeNext) - _manifest.MusicFadeSeconds && _tracks.Length > 0)
        {
            _track = (_track + 1) % _tracks.Length;
            StartTrack();
        }

        _voices.RemoveAll(v =>
        {
            bool gone = v.Gain <= 0 && v.Target <= 0;
            if (gone)
            {
                v.Dispose();
            }

            return gone;
        });
        Fill();
    }

    // Renders the note voices into the generator: as many frames as it can take.
    private void Fill()
    {
        if (_playback is null)
        {
            return;
        }

        int frames = _playback.GetFramesAvailable();
        if (frames <= 0)
        {
            return;
        }

        if (_frames.Length < frames)
        {
            _left = new float[frames];
            _right = new float[frames];
            _mixL = new float[frames];
            _mixR = new float[frames];
            _frames = new Vector2[frames];
        }

        Array.Clear(_mixL, 0, frames);
        Array.Clear(_mixR, 0, frames);
        foreach (IVoice v in _voices)
        {
            v.Render(_left.AsSpan(0, frames), _right.AsSpan(0, frames), _mixL, _mixR, frames, Rate);
        }

        float gain = _manifest.MusicGain;
        for (int i = 0; i < frames; i++)
        {
            _frames[i] = new Vector2(_mixL[i] * gain, _mixR[i] * gain);
        }

        _playback.PushBuffer(frames == _frames.Length ? _frames : _frames[..frames]);
    }

    private void StartTrack()
    {
        string name = _tracks[_track];
        // D10 (tests 2026-11-24): a broken .ogg put in by hand falls back to the notes instead of throwing.
        IVoice? voice = (OggOverride(name) is { } ogg ? (IVoice?)OggVoice.Make(this, ogg) : null)
            ?? NoteVoice.Make(_font?.Result, Path.Combine(_musicDirectory, name + ".mid"));
        if (voice is null)
        {
            GD.PrintErr($"audio: music {name} not found");
            return;
        }

        foreach (IVoice v in _voices)
        {
            v.Target = 0;
        }

        voice.Target = 1;
        _voices.Add(voice);
    }

    private string[] Tracks(string playlist)
    {
        if (_manifest.Music.TryGetValue(playlist, out string[]? listed))
        {
            return listed;
        }

        var found = new List<string>();
        for (int k = 1; k < 100; k++)
        {
            string name = $"{playlist}_{k}";
            if (!File.Exists(Path.Combine(_musicDirectory, name + ".mid")) && OggOverride(name) is null)
            {
                break;
            }

            found.Add(name);
        }

        return [.. found];
    }

    private string? OggOverride(string name) =>
        _overrides.Select(d => Path.Combine(d, name + ".ogg")).FirstOrDefault(File.Exists);

    /// <summary>A piece being heard, fading in or out.</summary>
    private interface IVoice : IDisposable
    {
        float Gain { get; }

        float Target { get; set; }

        /// <summary>Seconds played.</summary>
        double Played { get; }

        /// <summary>Seconds of one round of the piece.</summary>
        double Length { get; }

        void Fade(float step);

        void Render(Span<float> left, Span<float> right, float[] mixL, float[] mixR, int frames, int rate);
    }

    /// <summary>Notes played on the instruments.</summary>
    private sealed class NoteVoice : IVoice
    {
        private readonly MidiFileSequencer _sequencer;
        private readonly double _length;
        private double _played;
        private float _gain;

        private NoteVoice(SoundFont font, MidiFile midi)
        {
            var synth = new Synthesizer(font, new SynthesizerSettings(Rate) { EnableReverbAndChorus = true });
            _sequencer = new MidiFileSequencer(synth);
            _sequencer.Play(midi, true);
            _length = midi.Length.TotalSeconds;
        }

        public float Gain => _gain;

        public float Target { get; set; }

        public double Played => _played;

        public double Length => _length;

        public static NoteVoice? Make(SoundFont? font, string path)
        {
            if (font is null || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return new NoteVoice(font, new MidiFile(path));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
            {
                GD.PrintErr($"audio: {path} not read: {ex.Message}");
                return null;
            }
        }

        public void Fade(float step) => _gain = _gain < Target ? Math.Min(Target, _gain + step) : Math.Max(Target, _gain - step);

        public void Render(Span<float> left, Span<float> right, float[] mixL, float[] mixR, int frames, int rate)
        {
            _sequencer.Render(left, right);
            _played += (double)frames / rate;
            float g = _gain;
            for (int i = 0; i < frames; i++)
            {
                mixL[i] += left[i] * g;
                mixR[i] += right[i] * g;
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A recorded piece (.ogg) put in by hand; it plays on its own player.</summary>
    private sealed class OggVoice : IVoice
    {
        private readonly AudioStreamPlayer _player;
        private readonly double _length;
        private float _gain;

        private OggVoice(Node parent, AudioStreamOggVorbis stream)
        {
            stream.Loop = true;
            _length = stream.GetLength();
            _player = new AudioStreamPlayer { Bus = "Music", Stream = stream, VolumeDb = -60 };
            parent.AddChild(_player);
            _player.Play();
        }

        /// <summary>The recorded piece, or null when the file is not a readable Ogg Vorbis stream.</summary>
        public static OggVoice? Make(Node parent, string path)
        {
            AudioStreamOggVorbis? stream = AudioStreamOggVorbis.LoadFromFile(path);
            if (stream is null || stream.GetLength() <= 0)
            {
                GD.PrintErr($"audio: {path} is not a readable .ogg, the notes play instead");
                return null;
            }

            return new OggVoice(parent, stream);
        }

        public float Gain => _gain;

        public float Target { get; set; }

        public double Played { get; private set; }

        public double Length => _length;

        public void Fade(float step)
        {
            _gain = _gain < Target ? Math.Min(Target, _gain + step) : Math.Max(Target, _gain - step);
            _player.VolumeDb = _gain <= 0.001f ? -60 : Mathf.LinearToDb(_gain);
        }

        public void Render(Span<float> left, Span<float> right, float[] mixL, float[] mixR, int frames, int rate) =>
            Played += (double)frames / rate;

        public void Dispose() => _player.QueueFree();
    }
}
