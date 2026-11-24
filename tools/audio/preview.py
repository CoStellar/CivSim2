"""Renders the first seconds of each music piece to .ogg for listening outside the game.

Usage: python3 tools/audio/preview.py OUT_DIR [SECONDS] [name ...]
"""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor

import mido

HERE = os.path.dirname(os.path.abspath(__file__))
AUDIO = os.path.join(HERE, "..", "..", "audio")


def cut(src: str, dst: str, seconds: float):
    mid = mido.MidiFile(src)
    tempo = 500000
    for track in mid.tracks:
        for msg in track:
            if msg.type == "set_tempo":
                tempo = msg.tempo
                break
    limit = mido.second2tick(seconds, mid.ticks_per_beat, tempo)
    out = mido.MidiFile(ticks_per_beat=mid.ticks_per_beat)
    for track in mid.tracks:
        t, new = 0, mido.MidiTrack()
        for msg in track:
            t += msg.time
            if t > limit and msg.type == "note_on" and msg.velocity > 0:
                continue
            new.append(msg)
        out.tracks.append(new)
    out.save(dst)


def render(name: str, out: str, seconds: float):
    target = os.path.join(out, name + ".ogg")
    if os.path.exists(target):
        return
    with tempfile.TemporaryDirectory() as tmp:
        mid = os.path.join(tmp, "a.mid")
        wav = os.path.join(tmp, "a.wav")
        cut(os.path.join(AUDIO, "music", name + ".mid"), mid, seconds)
        subprocess.run(["fluidsynth", "-ni", "-g", "0.5", "-r", "44100", "-F", wav, os.path.join(AUDIO, "instruments.sf2"), mid],
                       check=True, capture_output=True)
        subprocess.run(["ffmpeg", "-y", "-i", wav, "-t", str(seconds + 3), "-af",
                        f"loudnorm=I=-20:TP=-1.5,areverse,afade=t=in:d=4,areverse", "-c:a", "libvorbis", "-q:a", "0", target],
                       check=True, capture_output=True)
    print(name, flush=True)


def main():
    out = sys.argv[1]
    seconds = float(sys.argv[2]) if len(sys.argv) > 2 else 75
    names = sys.argv[3:] or sorted(f[:-4] for f in os.listdir(os.path.join(AUDIO, "music")) if f.endswith(".mid"))
    os.makedirs(out, exist_ok=True)
    with ThreadPoolExecutor(4) as pool:
        list(pool.map(lambda n: render(n, out, seconds), names))


if __name__ == "__main__":
    main()
