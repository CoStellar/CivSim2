"""Music of the game, composed by rules and played on the FluidR3 GM sound font (MIT licence) through FluidSynth.

Each era has a style: a mode, a tempo, instruments and drum patterns. A track is built from sections (intro, themes, a
bridge, outro); the melody grows from a short motif that repeats with variations over a chord progression.
"""
from __future__ import annotations

import random
from dataclasses import dataclass, field

import mido

MODES = {
    "dorian": [0, 2, 3, 5, 7, 9, 10],
    "aeolian": [0, 2, 3, 5, 7, 8, 10],
    "mixolydian": [0, 2, 4, 5, 7, 9, 10],
    "ionian": [0, 2, 4, 5, 7, 9, 11],
    "phrygian_dom": [0, 1, 4, 5, 7, 8, 10],
    "penta_minor": [0, 3, 5, 7, 10],
    "penta_major": [0, 2, 4, 7, 9],
}

TPB = 480  # ticks per beat
DRUMS = 9  # MIDI channel of percussion


@dataclass
class Note:
    start: float  # beats
    dur: float
    pitch: int
    vel: int
    ch: int


@dataclass
class Style:
    name: str
    root: int                 # MIDI note of the tonic (melody octave)
    mode: str
    tempo: int
    beats: int                # beats per bar
    melody: list[int]         # GM programs taking turns on the melody
    harmony: int | None       # sustained chords
    arp: int | None           # broken chords
    bass: int | None          # drone / bass
    counter: int | None       # second voice in the later sections
    progressions: list[list[int]]
    bridge: list[list[int]]
    rhythms: list[list[float]]
    arp_pattern: list[int]
    arp_step: float
    drums: dict[int, str]     # drum note -> step pattern over one bar (16 steps per 4 beats)
    drums_light: dict[int, str]
    chord_bars: int = 1
    melody_octave_spread: int = 9
    harmony_vel: int = 52
    arp_vel: int = 62
    bass_vel: int = 70
    melody_vel: int = 88
    rest_chance: float = 0.12
    sections: list[str] = field(default_factory=lambda: ["intro", "A", "A2", "B", "A3", "outro"])


def scale_pitch(style: Style, degree: int) -> int:
    steps = MODES[style.mode]
    octave, i = divmod(degree, len(steps))
    return style.root + 12 * octave + steps[i]


def chord_degrees(style: Style, degree: int) -> list[int]:
    """Stacked thirds of the mode (for pentatonic modes: every other note, which sounds open and old)."""
    return [degree, degree + 2, degree + 4]


class Composer:
    def __init__(self, style: Style, seed: int):
        self.s = style
        self.r = random.Random(seed)
        self.notes: list[Note] = []
        self.programs: dict[int, int] = {}
        self._channels: dict[int, int] = {}

    def ch(self, program: int) -> int:
        if program not in self._channels:
            free = [c for c in range(16) if c != DRUMS and c not in self._channels.values()]
            c = free[0]
            self._channels[program] = c
            self.programs[c] = program
        return self._channels[program]

    def add(self, start, dur, pitch, vel, program):
        jitter = self.r.uniform(-0.012, 0.012)
        if dur <= 0.02 or not 0 <= pitch <= 127:
            return
        self.notes.append(Note(max(0.0, start + jitter), dur, pitch, max(1, min(127, vel + self.r.randint(-6, 6))), self.ch(program)))

    # ------------------------------------------------------------ parts

    def motif(self, beats: float) -> list[tuple[float, int]]:
        """A motif: durations and scale steps relative to the chord root."""
        rhythm = self.r.choice(self.s.rhythms)
        while sum(rhythm) < beats:
            rhythm = rhythm + self.r.choice(self.s.rhythms)
        out, total = [], 0.0
        step = self.r.choice([0, 2, 4])
        for d in rhythm:
            if total + d > beats:
                d = beats - total
            if d <= 0:
                break
            out.append((d, step))
            total += d
            step += self.r.choice([-2, -1, -1, 1, 1, 2, 0, 3, -3])
            step = max(-3, min(self.s.melody_octave_spread, step))
        return out

    def vary(self, motif):
        out = []
        for d, st in motif:
            if self.r.random() < 0.3:
                st += self.r.choice([-1, 1, 2, -2])
            out.append((d, st))
        return out

    def melody_phrase(self, start, chords, program, octave=0, vel=None):
        bar = self.s.beats * self.s.chord_bars
        base = self.motif(bar)
        shapes = [base, self.vary(base), self.motif(bar), self.vary(base)]
        t = start
        for i, degree in enumerate(chords):
            shape = shapes[i % len(shapes)]
            last = i == len(chords) - 1
            for j, (d, st) in enumerate(shape):
                if last and j >= len(shape) - 2:
                    # Close the phrase on a chord tone held long.
                    remaining = start + bar * len(chords) - t
                    if j == len(shape) - 1 or remaining <= d:
                        self.add(t, remaining * 0.95, scale_pitch(self.s, degree + self.r.choice([0, 2, 4]) + 7 * octave), (vel or self.s.melody_vel) - 4, program)
                        t += remaining
                        break
                if self.r.random() < self.s.rest_chance and j > 0:
                    t += d
                    continue
                strong = abs((t - start) % self.s.beats) < 0.01
                deg = degree + st
                if strong:
                    tones = [x + 7 * k for x in chord_degrees(self.s, degree) for k in (-1, 0, 1)]
                    deg = min(tones, key=lambda x: abs(x - deg))
                self.add(t, d * 0.92, scale_pitch(self.s, deg + 7 * octave), (vel or self.s.melody_vel) + (8 if strong else 0), program)
                t += d
            t = start + bar * (i + 1)

    def harmony(self, start, chords, program, vel=None, octave=-1):
        bar = self.s.beats * self.s.chord_bars
        for i, degree in enumerate(chords):
            for k, d in enumerate(chord_degrees(self.s, degree)):
                self.add(start + i * bar + k * 0.03, bar * 0.98, scale_pitch(self.s, d + 7 * octave), vel or self.s.harmony_vel, program)

    def arpeggio(self, start, chords, program, vel=None, octave=-1):
        bar = self.s.beats * self.s.chord_bars
        steps = int(round(bar / self.s.arp_step))
        for i, degree in enumerate(chords):
            tones = chord_degrees(self.s, degree)
            tones = tones + [x + 7 for x in tones]
            for k in range(steps):
                idx = self.s.arp_pattern[k % len(self.s.arp_pattern)]
                accent = 10 if k % (steps // max(1, self.s.chord_bars * self.s.beats // 2)) == 0 else 0
                self.add(start + i * bar + k * self.s.arp_step, self.s.arp_step * 1.6, scale_pitch(self.s, tones[idx % len(tones)] + 7 * octave), (vel or self.s.arp_vel) + accent, program)

    def bass_part(self, start, chords, program, vel=None, drone=False):
        bar = self.s.beats * self.s.chord_bars
        if drone:
            total = bar * len(chords)
            self.add(start, total * 0.99, scale_pitch(self.s, 0) - 24, vel or self.s.bass_vel, program)
            self.add(start, total * 0.99, scale_pitch(self.s, 4) - 24, (vel or self.s.bass_vel) - 15, program)
            return
        for i, degree in enumerate(chords):
            p = scale_pitch(self.s, degree) - 24
            self.add(start + i * bar, bar * 0.6, p, vel or self.s.bass_vel, program)
            if self.s.beats >= 4:
                self.add(start + i * bar + bar / 2, bar * 0.45, p + (7 if self.r.random() < 0.5 else 0), (vel or self.s.bass_vel) - 10, program)

    def counter_part(self, start, chords, program, vel=60):
        bar = self.s.beats * self.s.chord_bars
        prev = None
        for i, degree in enumerate(chords):
            tones = [x for x in chord_degrees(self.s, degree)]
            pick = tones[1] if prev is None else min(tones, key=lambda x: abs(x - prev))
            half = bar / 2
            self.add(start + i * bar, half * 0.95, scale_pitch(self.s, pick), vel, program)
            nxt = pick + self.r.choice([-1, 1])
            self.add(start + i * bar + half, half * 0.95, scale_pitch(self.s, nxt), vel - 6, program)
            prev = pick

    def drum_part(self, start, bars, pattern, vel=1.0):
        grid = self.s.beats * 4
        for b in range(bars):
            for note, steps in pattern.items():
                for k, c in enumerate(steps[:grid]):
                    if c == ".":
                        continue
                    v = {"x": 92, "o": 70, "-": 48}.get(c, 70)
                    if self.r.random() < 0.06 and c == "-":
                        continue
                    t = start + b * self.s.beats + k * 0.25
                    self.notes.append(Note(t + self.r.uniform(-0.01, 0.01), 0.2, note, int(min(127, v * vel + self.r.randint(-8, 8))), DRUMS))

    # ------------------------------------------------------------ whole track

    def compose(self) -> float:
        s = self.s
        bar = s.beats * s.chord_bars
        prog_a = self.r.choice(s.progressions)
        prog_b = self.r.choice(s.bridge)
        t = 0.0
        melody_turn = 0
        for section in s.sections:
            if section == "intro":
                chords = [prog_a[0]] * 4
                if s.bass is not None:
                    self.bass_part(t, chords, s.bass, drone=True)
                if s.arp is not None:
                    self.arpeggio(t + bar * 2, chords[2:], s.arp, vel=s.arp_vel - 15)
                self.drum_part(t, 4 * s.chord_bars, s.drums_light, 0.7)
                t += bar * 4
            elif section == "outro":
                chords = [prog_a[0]] * 4
                if s.bass is not None:
                    self.bass_part(t, chords, s.bass, drone=True, vel=s.bass_vel - 10)
                if s.harmony is not None:
                    self.harmony(t, chords[:2], s.harmony, vel=s.harmony_vel - 10)
                if s.arp is not None:
                    self.arpeggio(t, chords[:2], s.arp, vel=s.arp_vel - 20)
                self.drum_part(t, 2 * s.chord_bars, s.drums_light, 0.5)
                self.add(t + bar * 4, 0.1, 0, 1, s.melody[0])  # silent tail so the reverb rings out
                t += bar * 4
            else:
                prog = prog_b if section == "B" else prog_a
                chords = prog + prog  # two phrases
                program = s.melody[melody_turn % len(s.melody)]
                melody_turn += 1
                octave = 1 if section == "B" and s.root < 64 else 0
                self.melody_phrase(t, prog, program, octave)
                self.melody_phrase(t + bar * len(prog), prog, program, octave)
                if s.harmony is not None:
                    self.harmony(t, chords, s.harmony)
                if s.arp is not None and section != "A":
                    self.arpeggio(t, chords, s.arp)
                if s.bass is not None:
                    self.bass_part(t, chords, s.bass, drone=section == "A")
                if s.counter is not None and section in ("A2", "A3"):
                    self.counter_part(t, chords, s.counter)
                pattern = s.drums_light if section in ("A", "B") else s.drums
                self.drum_part(t, len(chords) * s.chord_bars, pattern)
                t += bar * len(chords)
        return t

    def write(self, path: str):
        mid = mido.MidiFile(ticks_per_beat=TPB)
        meta = mido.MidiTrack()
        meta.append(mido.MetaMessage("set_tempo", tempo=mido.bpm2tempo(self.s.tempo), time=0))
        mid.tracks.append(meta)
        events = []
        for ch, prog in self.programs.items():
            events.append((0, 0, mido.Message("program_change", channel=ch, program=prog)))
            events.append((0, 0, mido.Message("control_change", channel=ch, control=91, value=70)))  # reverb send
            events.append((0, 0, mido.Message("control_change", channel=ch, control=10, value=[40, 88, 64, 52, 76, 30, 98][ch % 7])))
        events.append((0, 0, mido.Message("control_change", channel=DRUMS, control=91, value=50)))
        for n in self.notes:
            a = max(0, int(n.start * TPB))
            b = int((n.start + n.dur) * TPB)
            events.append((a, 1, mido.Message("note_on", channel=n.ch, note=n.pitch, velocity=n.vel)))
            events.append((b, 0, mido.Message("note_off", channel=n.ch, note=n.pitch, velocity=0)))
        events.sort(key=lambda e: (e[0], e[1]))
        track = mido.MidiTrack()
        now = 0
        for tick, _, msg in events:
            track.append(msg.copy(time=tick - now))
            now = tick
        mid.tracks.append(track)
        mid.save(path)


# ---------------------------------------------------------------- the era styles (GM programs, 0-based)

R4 = [[1, 1, 2], [2, 1, 1], [1.5, 0.5, 2], [1, 0.5, 0.5, 2], [3, 1], [0.5, 0.5, 1, 2], [2, 2]]
R3 = [[1, 2], [2, 1], [1.5, 0.5, 1], [0.5, 0.5, 2], [3]]

STYLES = {
    "menu": Style("menu", 62, "dorian", 76, 4, [60, 73], 48, 46, 42, 52,
                  [[0, 6, 3, 0], [0, 3, 6, 4], [0, 2, 3, 6]], [[3, 4, 6, 0], [5, 3, 6, 4]], R4,
                  [0, 1, 2, 3, 2, 1, 0, 1], 0.5,
                  {47: "x.......o.......", 42: "................"}, {47: "o...............", 42: "................"},
                  chord_bars=1, harmony_vel=48, melody_vel=84),
    "stone_age": Style("stone_age", 57, "penta_minor", 70, 4, [77, 79], 89, None, 89, 53,
                       [[0, 0, 3, 0], [0, 2, 0, 3], [0, 3, 2, 0]], [[2, 3, 1, 0], [3, 3, 2, 0]], R4,
                       [0], 1.0,
                       {116: "x.......o.......", 41: "....o.......-.o.", 45: "..........-....."},
                       {116: "x...............", 41: "........o......."},
                       chord_bars=2, harmony_vel=38, bass_vel=58, melody_vel=80, rest_chance=0.22),
    "neolithic": Style("neolithic", 62, "penta_major", 84, 4, [75, 79], 89, 108, 42, 53,
                       [[0, 3, 1, 0], [0, 2, 3, 0], [0, 4, 3, 0]], [[1, 3, 2, 4], [3, 1, 4, 0]], R4,
                       [0, 2, 4, 2, 1, 3, 4, 3], 0.5,
                       {64: "x..o..x.x..o..-.", 63: "....x.......x...", 70: "-.-.-.-.-.-.-.-."},
                       {64: "x.......x.......", 70: "-...-...-...-..."},
                       chord_bars=1, harmony_vel=36, arp_vel=58, melody_vel=82),
    "bronze_age": Style("bronze_age", 62, "phrygian_dom", 92, 4, [68, 111], 49, 46, 43, 52,
                        [[0, 1, 0, 6], [0, 1, 6, 0], [0, 6, 1, 0]], [[3, 1, 0, 6], [5, 6, 1, 0]], R4,
                        [0, 1, 2, 1, 3, 2, 1, 2], 0.5,
                        {60: "x..-x.-.x..-x.-.", 61: "...o.....o...o..", 54: "..-...-...-...-."},
                        {60: "x.......x.......", 54: "....-.......-..."},
                        chord_bars=1, harmony_vel=40, arp_vel=60, melody_vel=84),
    "iron_age": Style("iron_age", 60, "aeolian", 78, 4, [73, 71], 48, 46, 42, 60,
                      [[0, 5, 6, 0], [0, 3, 4, 0], [0, 5, 2, 6]], [[3, 4, 2, 5], [5, 6, 4, 4]], R4,
                      [0, 2, 1, 3, 2, 4, 3, 1], 0.5,
                      {47: "x.......x...o...", 45: "....o.......o...", 116: "x..............."},
                      {47: "x...............", 45: "........o......."},
                      chord_bars=1, harmony_vel=44, arp_vel=56, melody_vel=84),
    "middle_ages": Style("middle_ages", 62, "dorian", 104, 3, [74, 110, 68], 52, 24, 42, 15,
                         [[0, 6, 0, 4], [0, 3, 6, 0], [0, 6, 3, 4]], [[2, 3, 6, 4], [5, 6, 3, 4]], R3,
                         [0, 1, 2, 3, 2, 1], 0.5,
                         {54: "x...o...-...", 60: "x.....o...o.", 61: "....-......."},
                         {60: "x...........", 54: "......-....."},
                         chord_bars=1, harmony_vel=36, arp_vel=64, melody_vel=86, rest_chance=0.08,
                         sections=["intro", "A", "A2", "B", "A3", "B", "A3", "outro"]),
}


# ---------------------------------------------------------------- stingers (short pieces for events)


def stinger(path: str, tempo: int, parts: list[tuple[int, list[tuple[float, float, int, int]]]], drums: list[tuple[float, int, int]] = ()):
    """parts: (program, [(start beat, beats, pitch, velocity)]); drums: (start beat, note, velocity)."""
    mid = mido.MidiFile(ticks_per_beat=TPB)
    meta = mido.MidiTrack()
    meta.append(mido.MetaMessage("set_tempo", tempo=mido.bpm2tempo(tempo), time=0))
    mid.tracks.append(meta)
    events = []
    for ch, (program, notes) in enumerate(parts):
        if ch >= DRUMS:
            ch += 1
        events.append((0, 0, mido.Message("program_change", channel=ch, program=program)))
        events.append((0, 0, mido.Message("control_change", channel=ch, control=91, value=80)))
        last = 0
        for s, d, p, v in notes:
            events.append((int(s * TPB), 1, mido.Message("note_on", channel=ch, note=p, velocity=v)))
            events.append((int((s + d) * TPB), 0, mido.Message("note_off", channel=ch, note=p, velocity=0)))
            last = max(last, s + d)
    for s, n, v in drums:
        events.append((int(s * TPB), 1, mido.Message("note_on", channel=DRUMS, note=n, velocity=v)))
        events.append((int((s + 0.25) * TPB), 0, mido.Message("note_off", channel=DRUMS, note=n, velocity=0)))
    events.sort(key=lambda e: (e[0], e[1]))
    track = mido.MidiTrack()
    now = 0
    for tick, _, msg in events:
        track.append(msg.copy(time=tick - now))
        now = tick
    mid.tracks.append(track)
    mid.save(path)


def stingers() -> dict[str, tuple[int, list, list]]:
    D = 62
    return {
        "era_up": (96, [
            (60, [(0, 0.5, D - 12, 100), (0.5, 0.5, D - 5, 100), (1, 0.5, D, 105), (1.5, 2.5, D + 4, 112)]),
            (61, [(1.5, 2.5, D - 12, 90), (1.5, 2.5, D - 8, 88), (1.5, 2.5, D - 5, 88)]),
            (48, [(1.5, 3, D - 24, 80), (1.5, 3, D - 12, 70)]),
            (47, [(0, 0.25, D - 24, 70), (0.25, 0.25, D - 24, 80), (1.5, 0.5, D - 24, 110)]),
        ], [(1.5, 49, 100)]),
        "research_done": (110, [
            (9, [(0, 0.5, 74, 90), (0.5, 0.5, 78, 90), (1, 0.5, 81, 95), (1.5, 1.5, 86, 100)]),
            (8, [(1.5, 1.5, 74, 70), (1.5, 1.5, 78, 70)]),
        ], []),
        "victory": (88, [
            (61, [(0, 1, 60, 100), (1, 0.5, 64, 100), (1.5, 0.5, 67, 105), (2, 1, 72, 110), (3, 0.5, 67, 100), (3.5, 3.5, 72, 115)]),
            (56, [(2, 1, 76, 90), (3.5, 3.5, 79, 100)]),
            (48, [(0, 7, 48, 80), (0, 7, 55, 75), (3.5, 3.5, 64, 70)]),
            (52, [(3.5, 3.5, 60, 70), (3.5, 3.5, 64, 70), (3.5, 3.5, 67, 70)]),
        ], [(0, 49, 90), (3.5, 49, 110), (3.5, 36, 100)]),
        "defeat": (60, [
            (42, [(0, 1.5, 50, 90), (1.5, 1.5, 48, 85), (3, 3, 45, 85)]),
            (52, [(0, 6, 57, 60), (0, 6, 62, 55), (3, 3, 60, 55)]),
            (48, [(0, 6, 38, 70)]),
        ], [(0, 41, 80), (3, 41, 70)]),
        "war_declared": (100, [
            (60, [(1, 1.5, 50, 105), (2.5, 2.5, 50, 110)]),
            (57, [(2.5, 2.5, 38, 90)]),
        ], [(0, 35, 110), (0.5, 41, 90), (1, 41, 100), (1.5, 43, 100), (2.5, 36, 120), (2.5, 49, 90)]),
        "peace": (80, [
            (46, [(i * 0.25, 2, p, 70) for i, p in enumerate([62, 66, 69, 74, 78, 81])]),
            (73, [(1.5, 1, 78, 80), (2.5, 2, 74, 85)]),
        ], []),
        "settlement_founded": (100, [
            (60, [(0, 0.5, 57, 95), (0.5, 1.5, 62, 100)]),
            (24, [(0.5, 1.5, 50, 80), (0.5, 1.5, 57, 75), (0.5, 1.5, 62, 75), (0.5, 1.5, 66, 75)]),
        ], [(0, 36, 100), (0.5, 41, 80)]),
        "alert_danger": (90, [
            (60, [(0, 1.2, 45, 112), (1.3, 1.8, 45, 118)]),
            (57, [(0, 1.2, 33, 95), (1.3, 1.8, 33, 100)]),
        ], [(0, 36, 110), (1.3, 36, 115)]),
        "alert_notice": (100, [
            (14, [(0, 1.5, 74, 70)]),
        ], []),
    }
