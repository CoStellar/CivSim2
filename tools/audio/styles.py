"""Music styles by terrain and era (Patryk, 2026-11-24): the terrain gives the colour (mountains sound Nordic, the
desert Middle Eastern...), the era how primitive it is (stone age: drums, voices and bone flutes; middle ages: a full band).

GM programs are 0-based; drum notes are of the GM percussion kit.
"""
from __future__ import annotations

import random

from music import R4, Style

R3 = [[1, 2], [2, 1], [1.5, 0.5, 1], [0.5, 0.5, 2], [3], [1, 1, 1], [0.5, 0.5, 0.5, 0.5, 1]]
ERAS = ["stone_age", "neolithic", "bronze_age", "iron_age", "middle_ages"]
REGIONS = ["mountains", "forest", "steppe", "desert", "wetland", "coast"]

# Per terrain: modes, tempo range, beats per bar, tonic roots, and per era (0 stone … 4 middle ages) the instruments.
# melody: programs taking turns; drone/harmony/arp/counter: one program or None.
REGION = {
    "mountains": dict(
        modes=["aeolian", "dorian", "penta_minor"], tempo=(60, 74), beats=4, roots=[50, 52, 53, 55],
        melody=[[53, 79], [77, 53], [60, 77], [110, 60, 73], [110, 74, 60]],
        drone=[52, 52, 43, 42, 42],
        harmony=[None, None, 89, 48, 52],
        arp=[None, None, None, 46, 46],
        counter=[None, None, None, 60, 71],
        drums=[{116: "x.......x.......", 45: "........o......."},
               {116: "x.......x.......", 45: "....o.......o..."},
               {116: "x.......x...o...", 41: "....o.......o..."},
               {116: "x.......x.o.....", 41: "....o.......o...", 45: "..........-....."},
               {116: "x.......x.o.....", 41: "....o.......o...", 54: "....-.......-..."}],
        light={116: "x...............", 45: "........-......."},
    ),
    "forest": dict(
        modes=["dorian", "mixolydian", "penta_major"], tempo=(80, 98), beats=3, roots=[60, 62, 64],
        melody=[[79, 53], [75, 78], [78, 73], [73, 110], [74, 110, 109]],
        drone=[89, 89, 42, 42, 42],
        harmony=[None, None, None, 48, 48],
        arp=[None, 108, 46, 46, 24],
        counter=[None, None, None, None, 15],
        drums=[{45: "x.....o.....", 76: "......-....."},
               {45: "x.....o.....", 76: "...-.....-.."},
               {45: "x..o..x.....", 41: "......o.....", 76: "...-.....-.."},
               {45: "x..o..x..o..", 41: "......o....."},
               {45: "x..o..x..o..", 41: "......o.....", 54: "...-.....-.."}],
        light={45: "x...........", 76: "......-....."},
    ),
    "steppe": dict(
        modes=["penta_minor", "dorian", "mixolydian"], tempo=(96, 112), beats=4, roots=[50, 52, 55],
        melody=[[53, 77], [77, 53], [110, 77], [110, 106], [110, 106, 73]],
        drone=[53, 53, 92, 42, 42],
        harmony=[None, None, None, 92, 48],
        arp=[None, None, 106, 106, 106],
        counter=[None, None, None, None, 71],
        drums=[{41: "x.......x.......", 75: "..-...-...-...-."},
               {45: "x.xx....x.xx....", 41: "x.......x......."},
               {45: "x.xx..x.x.xx..x.", 41: "x.......x......."},
               {45: "x.xx..x.x.xx..x.", 41: "x.......x.......", 70: "-.-.-.-.-.-.-.-."},
               {45: "x.xx..x.x.xx..x.", 41: "x.......x.......", 54: "....-.......-..."}],
        light={45: "x.xx....x.xx....", 41: "x..............."},
    ),
    "desert": dict(
        modes=["phrygian_dom", "aeolian", "phrygian_dom"], tempo=(76, 94), beats=4, roots=[57, 60, 62],
        melody=[[53, 79], [73, 53], [68, 111], [111, 104], [104, 73, 68]],
        drone=[53, 53, 43, 42, 42],
        harmony=[None, None, 48, 48, 48],
        arp=[None, None, 46, 104, 107],
        counter=[None, None, None, 73, 111],
        drums=[{39: "x.....x...x.....", 64: "x.......x......."},
               {64: "x..-x.-.x..-x.-.", 39: "....x.......x..."},
               {60: "x..-x.-.x..-x.-.", 61: "...o.....o...o..", 54: "..-...-...-...-."},
               {60: "x..-x.-.x..-x.-.", 61: "...o.....o...o..", 64: "x.......x.......", 54: "..-...-...-...-."},
               {60: "x..-x.-.x..-x.-.", 61: "...o.....o...o..", 64: "x.......x.......", 54: "..-...-...-...-."}],
        light={64: "x.......x.......", 60: "....-.......-..."},
    ),
    "wetland": dict(
        modes=["penta_major", "mixolydian", "penta_minor"], tempo=(100, 116), beats=4, roots=[60, 62, 65],
        melody=[[79, 53], [75, 79], [79, 12], [73, 12], [74, 73, 11]],
        drone=[None, 89, 89, 42, 42],
        harmony=[None, None, 89, 48, 48],
        arp=[115, 108, 12, 12, 12],
        counter=[None, None, None, 46, 46],
        drums=[{77: "x..x..x...x.x...", 76: "..-...-...-...-."},
               {63: "x..x..x...x.x...", 70: "-.-.-.-.-.-.-.-.", 77: "......x.......x."},
               {63: "x..x..x...x.x...", 64: "x.......x.......", 70: "-.-.-.-.-.-.-.-."},
               {63: "x..x..x...x.x...", 64: "x.......x.......", 70: "-.-.-.-.-.-.-.-.", 76: "..-...-...-...-."},
               {63: "x..x..x...x.x...", 64: "x.......x.......", 70: "-.-.-.-.-.-.-.-.", 54: "....-.......-..."}],
        light={63: "x.......x.......", 70: "-...-...-...-..."},
    ),
    "coast": dict(
        modes=["ionian", "mixolydian", "dorian"], tempo=(66, 84), beats=3, roots=[60, 62, 64],
        melody=[[53, 79], [79, 75], [73, 75], [73, 71], [110, 21, 74]],
        drone=[89, 89, 95, 42, 42],
        harmony=[None, None, 89, 48, 48],
        arp=[None, 108, 46, 46, 24],
        counter=[None, None, None, None, 110],
        drums=[{64: "x...........", 70: "....-...-..."},
               {64: "x.....o.....", 70: "....-...-..."},
               {64: "x.....o.....", 63: "...-.....-..", 70: "....-...-..."},
               {45: "x.....o.....", 63: "...-.....-..", 70: "....-...-..."},
               {45: "x.....o.....", 54: "...-.....-..", 63: "......o....."}],
        light={64: "x...........", 70: "......-....."},
    ),
}

PROGRESSIONS = {
    "aeolian": ([[0, 5, 6, 0], [0, 3, 4, 0], [0, 5, 2, 6], [0, 6, 5, 4]], [[3, 4, 2, 5], [5, 6, 4, 4], [5, 3, 6, 0]]),
    "dorian": ([[0, 6, 3, 0], [0, 3, 6, 4], [0, 2, 3, 6], [0, 3, 0, 6]], [[3, 4, 6, 0], [5, 3, 6, 4], [2, 3, 4, 6]]),
    "mixolydian": ([[0, 6, 3, 0], [0, 3, 6, 0], [0, 4, 6, 3]], [[3, 6, 4, 0], [5, 6, 3, 4]]),
    "ionian": ([[0, 3, 4, 0], [0, 5, 3, 4], [0, 3, 5, 4]], [[5, 3, 4, 0], [3, 4, 5, 4]]),
    "phrygian_dom": ([[0, 1, 0, 6], [0, 1, 6, 0], [0, 6, 1, 0]], [[3, 1, 0, 6], [5, 6, 1, 0], [3, 4, 1, 0]]),
    "penta_minor": ([[0, 0, 3, 0], [0, 2, 0, 3], [0, 3, 2, 0]], [[2, 3, 1, 0], [3, 3, 2, 0], [1, 2, 3, 0]]),
    "penta_major": ([[0, 3, 1, 0], [0, 2, 3, 0], [0, 4, 3, 0]], [[1, 3, 2, 4], [3, 1, 4, 0], [2, 4, 3, 0]]),
}


def style(region: str, era: int, seed: int) -> Style:
    """A style for one piece: the terrain's colour, the era's instruments, the rest picked by the seed."""
    r = random.Random(seed)
    g = REGION[region]
    mode = r.choice(g["modes"])
    lo, hi = g["tempo"]
    tempo = int(r.uniform(lo, hi) * (0.9 if era == 0 else 1.0) + era * 2)
    prog, bridge = PROGRESSIONS[mode]
    melody = list(g["melody"][era])
    r.shuffle(melody)
    beats = g["beats"]
    return Style(
        name=f"{region}_{ERAS[era]}", root=r.choice(g["roots"]), mode=mode, tempo=tempo, beats=beats,
        melody=melody, harmony=g["harmony"][era], arp=g["arp"][era], bass=g["drone"][era], counter=g["counter"][era],
        progressions=prog, bridge=bridge, rhythms=R3 if beats == 3 else R4,
        arp_pattern=r.choice([[0, 1, 2, 1, 3, 2, 1, 2], [0, 2, 1, 3, 2, 4, 3, 1], [0, 1, 2, 3, 2, 1], [0, 2, 4, 2]]),
        arp_step=0.5,
        drums=g["drums"][era], drums_light=g["light"],
        chord_bars=2 if era == 0 or tempo > 104 else 1,
        harmony_vel=36 + era * 2, arp_vel=54 + era * 2, bass_vel=56 + era * 2, melody_vel=80 + era,
        rest_chance=[0.28, 0.2, 0.14, 0.1, 0.08][era],
    )


def menu_style() -> Style:
    return Style("menu", 62, "dorian", 76, 4, [60, 73], 48, 46, 42, 52,
                 [[0, 6, 3, 0], [0, 3, 6, 4], [0, 2, 3, 6]], [[3, 4, 6, 0], [5, 3, 6, 4]], R4,
                 [0, 1, 2, 3, 2, 1, 0, 1], 0.5,
                 {116: "x.......o.......", 45: "........-......."}, {116: "o..............."},
                 chord_bars=1, harmony_vel=48, melody_vel=84)


def tracks(per: int = 3) -> list[tuple[str, Style]]:
    """Every piece of the game: menu, then each terrain × era × number."""
    out = [("menu", menu_style())]
    for e in range(len(ERAS)):
        for region in REGIONS:
            for k in range(1, per + 1):
                seed = 1000 * (e + 1) + 100 * REGIONS.index(region) + k
                out.append((f"{ERAS[e]}_{region}_{k}", style(region, e, seed)))
    return out


def drum_keys() -> set[int]:
    """Drum notes the music uses."""
    keys = set()
    for _, s in tracks():
        keys |= set(s.drums) | set(s.drums_light)
    return keys


def programs() -> set[tuple[int, int]]:
    """Bank and program of every instrument the music and the stingers use (for the trimmed sound font)."""
    used = {(128, 0)}
    for _, s in tracks():
        for p in [*s.melody, s.harmony, s.arp, s.bass, s.counter]:
            if p is not None:
                used.add((0, p))
    return used
