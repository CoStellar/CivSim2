"""Builds every sound of the game into audio/ (music, ambient loops, effects) from rules, no downloaded samples.

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
from synth import write_wav  # noqa: E402

SOUNDFONT = "/usr/share/sounds/sf2/FluidR3_GM.sf2"

# Two pieces per era (they take turns), and the menu theme.
TRACKS = [("menu", "menu", 11)] + [
    (f"{era}_{i}", era, seed) for era in ("stone_age", "neolithic", "bronze_age", "iron_age", "middle_ages") for i, seed in ((1, 101), (2, 202))
]

LOUDNESS = {"music": -20, "ambient": -24, "sfx": -18}


def run(cmd):
    subprocess.run(cmd, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def render_midi(mid, wav, gain=0.5):
    run(["fluidsynth", "-ni", "-g", str(gain), "-r", "44100", "-F", wav, SOUNDFONT, mid])


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
    a = p.parse_args()
    only = set(filter(None, a.only.split(",")))
    out = os.path.abspath(a.out)
    for sub in ("music", "ambient", "sfx"):
        os.makedirs(os.path.join(out, sub), exist_ok=True)

    def wanted(name):
        return not only or name in only

    with tempfile.TemporaryDirectory() as tmp:
        for name, era, seed in TRACKS:
            if not wanted(name):
                continue
            c = music.Composer(music.STYLES[era], seed)
            beats = c.compose()
            mid = os.path.join(tmp, name + ".mid")
            wav = os.path.join(tmp, name + ".wav")
            c.write(mid)
            render_midi(mid, wav)
            encode(wav, os.path.join(out, "music", name + ".ogg"), "music", 1.5, 5, trim=True, quality=3)
            print(f"music {name}: {beats * 60 / music.STYLES[era].tempo:.0f} s")

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
