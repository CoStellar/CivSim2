"""Builds every sound of the game into audio/ from rules, no downloaded samples: the music as notes (.mid, played by the
game on audio/instruments.sf2), ambient loops and effects as .ogg.

Needs: python3 with numpy, scipy, mido; fluidsynth with the FluidR3 GM sound font (Debian/Ubuntu: fluid-soundfont-gm);
ffmpeg with libvorbis. Usage: python3 tools/audio/make_audio.py [--only name,name] [--out audio]
The same seeds give the same notes; the rendered sound may differ slightly between FluidSynth versions.
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import ambient  # noqa: E402
import music  # noqa: E402
import sfx  # noqa: E402
import styles  # noqa: E402
import trim_sf2  # noqa: E402
from synth import write_wav  # noqa: E402

SOUNDFONT = "/usr/share/sounds/sf2/FluidR3_GM.sf2"
PIECE_SECONDS = 300  # each piece about 5 minutes, looped by the game (Patryk, 2026-11-24)

LOUDNESS = {"music": -20, "ambient": -24, "sfx": -18}


def run(cmd):
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def render_midi(mid, wav, gain=0.5, sf=SOUNDFONT):
    run(["fluidsynth", "-ni", "-g", str(gain), "-r", "44100", "-F", wav, sf, mid])


def encode(wav, ogg, kind, fade_in=0.0, fade_out=0.0, loudnorm=True, trim=False, quality=4):
    filters = []
    if trim:
        filters.append("silenceremove=start_periods=1:start_threshold=-60dB")
        filters.append("areverse,silenceremove=start_periods=1:start_threshold=-60dB,areverse")
    if loudnorm:
        filters.append(f"loudnorm=I={LOUDNESS[kind]}:TP=-1.5:LRA=14")
    if fade_in:
        filters.append(f"afade=t=in:d={fade_in}")
    if fade_out:
        filters.append(f"areverse,afade=t=in:d={fade_out},areverse")
    cmd = ["ffmpeg", "-y", "-i", wav]
    if filters:
        cmd += ["-af", ",".join(filters)]
    cmd += ["-ar", "44100", "-c:a", "libvorbis", "-q:a", str(quality), ogg]
    run(cmd)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", default=os.path.join(HERE, "..", "..", "audio"))
    p.add_argument("--only", default="")
    p.add_argument("--preview", default="", help="also render the music to .ogg here (for listening)")
    p.add_argument("--preview-seconds", type=float, default=0, help="only the first seconds of each piece")
    a = p.parse_args()
    only = set(filter(None, a.only.split(",")))
    out = os.path.abspath(a.out)
    for sub in ("music", "ambient", "sfx"):
        os.makedirs(os.path.join(out, sub), exist_ok=True)

    def wanted(name):
        return not only or name in only

    with tempfile.TemporaryDirectory() as tmp:
        # The instruments: the GM sound font cut down to what the music uses; the game plays the notes on it.
        if wanted("instruments"):
            used = styles.programs() | {(0, p) for _, (_, parts, _) in music.stingers().items() for p, _ in parts}
            keys = styles.drum_keys() | {n for _, (_, _, drums) in music.stingers().items() for _, n, _ in drums}
            trim_sf2.trim(SOUNDFONT, os.path.join(out, "instruments.sf2"), used, keys)

        for index, (name, style) in enumerate(styles.tracks()):
            if not wanted(name) and not wanted(style.name) and not wanted("music"):
                continue
            c = music.Composer(style, 7919 * (index + 1))
            beats = c.compose(150 if name == "menu" else PIECE_SECONDS)
            mid = os.path.join(out, "music", name + ".mid")
            c.write(mid)
            print(f"music {name}: {beats * 60 / style.tempo:.0f} s, {style.mode}, {style.tempo} bpm")
            if a.preview:
                os.makedirs(a.preview, exist_ok=True)
                wav = os.path.join(tmp, name + ".wav")
                render_midi(mid, wav, sf=os.path.join(out, "instruments.sf2"))
                if a.preview_seconds:
                    cut = os.path.join(tmp, name + "_cut.wav")
                    run(["ffmpeg", "-y", "-i", wav, "-t", str(a.preview_seconds), cut])
                    wav = cut
                encode(wav, os.path.join(a.preview, name + ".ogg"), "music", 0, 4 if a.preview_seconds else 0, quality=0)

        for name, (tempo, parts, drums) in music.stingers().items():
            if not wanted(name):
                continue
            mid = os.path.join(tmp, name + ".mid")
            wav = os.path.join(tmp, name + ".wav")
            music.stinger(mid, tempo, parts, drums)
            render_midi(mid, wav, 0.6)
            encode(wav, os.path.join(out, "sfx", name + ".ogg"), "sfx", 0, 0.4, trim=True)
            print(f"sting {name}")

        for i, name in enumerate(ambient.LOOPS):
            if not wanted(name):
                continue
            wav = os.path.join(tmp, name + ".wav")
            write_wav(wav, ambient.render(name, 1000 + i))
            encode(wav, os.path.join(out, "ambient", name + ".ogg"), "ambient", quality=2)
            print(f"ambient {name}")

        for i, name in enumerate(sfx.EFFECTS):
            if not wanted(name):
                continue
            wav = os.path.join(tmp, name + ".wav")
            write_wav(wav, sfx.render(name, 2000 + i))
            # Short effects: no loudness pass (it needs a few seconds of sound); synth.normalize set their peak.
            encode(wav, os.path.join(out, "sfx", name + ".ogg"), "sfx", loudnorm=False)
            print(f"sfx {name}")


if __name__ == "__main__":
    main()
