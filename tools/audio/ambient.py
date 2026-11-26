"""Ambient loops of the game, synthesized (see synth.py). Each returns stereo float audio that loops without a seam."""
from __future__ import annotations

import numpy as np

from synth import (RATE, add_at, bandpass, click_burst, colored, env_ad, highpass, lowpass, make_loop, modal,
                   moving_band, normalize, pan, seconds, smooth_random, t_axis, white)

LOOP = 48.0      # seconds of each loop
OVERLAP = 4.0    # cross-fade at the seam


def _stereo_bed(rng, length, maker) -> np.ndarray:
    """Two slightly different takes of the same bed, one per ear, so it sounds wide."""
    return np.stack([maker(rng), maker(rng)], axis=1)


def wind(rng, length, lo, hi, width, rate=0.15) -> np.ndarray:
    def one(r):
        n = colored(r, length, 1.0)
        centre = smooth_random(r, length, rate, lo, hi)
        level = smooth_random(r, length, rate * 1.3, 0.35, 1.0)
        return moving_band(n, centre, width) * level
    return _stereo_bed(rng, length, one)


# ---------------------------------------------------------------- creatures


def bird_species(rng) -> dict:
    return {
        "base": rng.uniform(2200, 5200),
        "sweep": rng.uniform(-0.45, 0.55),
        "note": rng.uniform(0.04, 0.16),
        "gap": rng.uniform(0.03, 0.12),
        "notes": int(rng.integers(2, 7)),
        "trill": rng.uniform(0, 1) < 0.35,
        "vib": rng.uniform(0, 60),
        "fall": rng.uniform(0.85, 1.12),
    }


def bird_call(rng, sp) -> np.ndarray:
    parts = []
    f0 = sp["base"] * rng.uniform(0.95, 1.05)
    for i in range(sp["notes"]):
        d = sp["note"] * rng.uniform(0.8, 1.2)
        t = t_axis(d)
        k = t / d
        sweep = 1 + sp["sweep"] * (np.sin(np.pi * k) if i % 2 == 0 else k)
        freq = f0 * sweep
        if sp["trill"]:
            freq = freq * (1 + 0.06 * np.sin(2 * np.pi * sp["vib"] * 4 * t))
        phase = 2 * np.pi * np.cumsum(freq) / RATE
        tone = np.sin(phase) + 0.18 * np.sin(2 * phase)
        envelope = np.sin(np.pi * k) ** 1.5
        parts.append(tone * envelope)
        parts.append(np.zeros(seconds(sp["gap"] * rng.uniform(0.7, 1.3))))
        f0 *= sp["fall"]
    return np.concatenate(parts)


def crickets(rng, length, carrier=4600.0, chirps_per_s=2.6, pulses=3, level=1.0) -> np.ndarray:
    t = t_axis(length)
    out = np.zeros_like(t)
    period = 1 / chirps_per_s
    start = rng.uniform(0, period)
    while start < length:
        for p in range(pulses):
            s = start + p * 0.035
            n = seconds(0.022)
            i = seconds(s)
            if i + n >= len(t):
                break
            seg = t[i:i + n] - s
            out[i:i + n] += np.sin(2 * np.pi * carrier * seg) * np.sin(np.pi * seg / 0.022)
        start += period * rng.uniform(0.92, 1.08)
    return out * level


def frog(rng) -> np.ndarray:
    d = rng.uniform(0.18, 0.4)
    t = t_axis(d)
    f = rng.uniform(70, 140)
    buzz = np.sign(np.sin(2 * np.pi * f * t)) * 0.6 + np.sin(2 * np.pi * f * 2 * t) * 0.4
    body = bandpass(buzz, rng.uniform(300, 500), rng.uniform(900, 1400))
    return body * np.sin(np.pi * t / d) ** 0.7


def gull(rng) -> np.ndarray:
    d = rng.uniform(0.25, 0.45)
    t = t_axis(d)
    k = t / d
    freq = rng.uniform(1100, 1500) * (1 + 0.5 * np.sin(np.pi * k) - 0.35 * k)
    phase = 2 * np.pi * np.cumsum(freq) / RATE
    tone = np.sin(phase) + 0.5 * np.sin(2 * phase) + 0.25 * np.sin(3 * phase)
    return bandpass(tone, 800, 5000) * np.sin(np.pi * k) ** 0.8


# ---------------------------------------------------------------- settlement and battle


def wood_chop(rng) -> np.ndarray:
    hit = modal([rng.uniform(180, 260), rng.uniform(420, 560), rng.uniform(900, 1200)], [0.05, 0.03, 0.015], [1, 0.6, 0.3], 0.25, rng)
    return hit + click_burst(rng, 0.25, 0.012, 800, 5000) * 0.8


def hammer_anvil(rng) -> np.ndarray:
    base = rng.uniform(900, 1300)
    ring = modal([base, base * 2.76, base * 5.4, base * 8.9], [0.35, 0.22, 0.12, 0.06], [1, 0.5, 0.25, 0.1], 0.6, rng)
    return ring * 0.6 + click_burst(rng, 0.6, 0.008, 1500, 9000) * 0.7


def voice_babble(rng, length, voices=7) -> np.ndarray:
    """Distant talking: pitched buzz through vowel-like filters, syllable by syllable. Kept low and blurred."""
    total = np.zeros((seconds(length), 2))
    vowels = [(700, 1200), (500, 1700), (350, 2200), (450, 900), (600, 1000), (300, 800)]
    for _ in range(voices):
        pitch = rng.uniform(95, 230)
        t = 0.0
        while t < length:
            if rng.uniform() < 0.25:
                t += rng.uniform(0.4, 1.6)  # pause between phrases
                continue
            d = rng.uniform(0.1, 0.24)
            tt = t_axis(d)
            f = pitch * (1 + 0.08 * np.sin(2 * np.pi * rng.uniform(2, 5) * tt)) * rng.uniform(0.9, 1.1)
            ph = 2 * np.pi * np.cumsum(f) / RATE
            src = sum(np.sin(k * ph) / k for k in range(1, 12))
            f1, f2 = vowels[int(rng.integers(len(vowels)))]
            syl = bandpass(src, f1 * 0.8, f1 * 1.25) + 0.5 * bandpass(src, f2 * 0.85, f2 * 1.2)
            syl *= np.sin(np.pi * tt / d) ** 0.6
            add_at(total, pan(syl, rng.uniform(-0.8, 0.8)), t)
            t += d * rng.uniform(0.9, 1.3)
    out = np.stack([lowpass(total[:, 0], 2200), lowpass(total[:, 1], 2200)], axis=1)
    return out


def fire(rng, length) -> np.ndarray:
    rumble = lowpass(colored(rng, length, 2.0), 300) * 0.5
    out = np.stack([rumble, rumble], axis=1)
    n = int(length * 9)
    for _ in range(n):
        c = click_burst(rng, 0.05, rng.uniform(0.002, 0.008), 1500, 8000) * rng.uniform(0.2, 1.0)
        add_at(out, pan(c, rng.uniform(-0.3, 0.3)), rng.uniform(0, length))
    return out


def metal_clash(rng) -> np.ndarray:
    base = rng.uniform(1400, 2600)
    ring = modal([base, base * 1.48, base * 2.31, base * 3.7, base * 5.1], [0.18, 0.14, 0.1, 0.06, 0.04], [1, 0.8, 0.6, 0.4, 0.2], 0.5, rng, 0.02)
    return ring * 0.5 + click_burst(rng, 0.5, 0.02, 2000, 10000)


def thud(rng) -> np.ndarray:
    d = 0.35
    t = t_axis(d)
    f = rng.uniform(55, 90) * np.exp(-t * 4)
    body = np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-t / 0.09)
    return body + lowpass(white(rng, d), 800) * np.exp(-t / 0.03) * 0.5


def war_cry(rng, length) -> np.ndarray:
    """A crowd shouting in the distance: many voices, louder and higher than babble, swelling."""
    crowd = voice_babble(rng, length, voices=16) * 2.2
    swell = smooth_random(rng, length, 0.25, 0.25, 1.0)
    return crowd * swell[:, None]


# ---------------------------------------------------------------- the loops


def amb_forest(rng) -> np.ndarray:
    L = LOOP + OVERLAP
    out = wind(rng, L, 300, 1100, 0.9) * 0.25
    out += np.stack([highpass(colored(rng, L, 1.0), 2500)] * 2, axis=1) * smooth_random(rng, L, 0.2, 0.0, 0.05)[:, None]
    species = [bird_species(rng) for _ in range(6)]
    for sp in species:
        position = rng.uniform(-0.9, 0.9)
        distance = rng.uniform(0.25, 1.0)
        t = rng.uniform(0, 6)
        while t < L:
            call = bird_call(rng, sp)
            add_at(out, pan(call * distance * 0.35, position), t, wrap=False)
            t += rng.uniform(2.5, 11)
    return make_loop(out, OVERLAP)


def amb_grassland(rng) -> np.ndarray:
    L = LOOP + OVERLAP
    out = wind(rng, L, 250, 900, 1.0) * 0.32
    for _ in range(5):
        c = crickets(rng, L, rng.uniform(3800, 5400), rng.uniform(1.6, 3.2), int(rng.integers(2, 5)), rng.uniform(0.02, 0.05))
        out += pan(c, rng.uniform(-0.8, 0.8))
    for sp in [bird_species(rng) for _ in range(2)]:
        t = rng.uniform(0, 10)
        while t < L:
            add_at(out, pan(bird_call(rng, sp) * 0.18, rng.uniform(-0.9, 0.9)), t, wrap=False)
            t += rng.uniform(8, 20)
    return make_loop(out, OVERLAP)


def amb_wetland(rng) -> np.ndarray:
    L = LOOP + OVERLAP
    out = wind(rng, L, 200, 700, 1.0) * 0.18
    out += np.stack([highpass(colored(rng, L, 0.5), 4000)] * 2, axis=1) * 0.02
    for _ in range(4):
        c = crickets(rng, L, rng.uniform(5000, 7000), rng.uniform(4, 8), int(rng.integers(3, 6)), rng.uniform(0.015, 0.03))
        out += pan(c, rng.uniform(-0.9, 0.9))
    for _ in range(5):
        position = rng.uniform(-0.9, 0.9)
        t = rng.uniform(0, 3)
        while t < L:
            for k in range(int(rng.integers(2, 6))):
                add_at(out, pan(frog(rng) * rng.uniform(0.15, 0.35), position), t + k * rng.uniform(0.35, 0.6), wrap=False)
            t += rng.uniform(4, 12)
    for sp in [bird_species(rng) for _ in range(3)]:
        t = rng.uniform(0, 10)
        while t < L:
            add_at(out, pan(bird_call(rng, sp) * 0.2, rng.uniform(-0.9, 0.9)), t, wrap=False)
            t += rng.uniform(6, 16)
    return make_loop(out, OVERLAP)


def amb_desert(rng) -> np.ndarray:
    L = LOOP + OVERLAP
    out = wind(rng, L, 400, 1600, 0.7, rate=0.1) * 0.55
    sand = np.stack([highpass(colored(rng, L, 0.6), 3000), highpass(colored(rng, L, 0.6), 3000)], axis=1)
    out += sand * smooth_random(rng, L, 0.15, 0.0, 0.08)[:, None]
    return make_loop(out, OVERLAP)


def amb_cold(rng) -> np.ndarray:
    L = LOOP + OVERLAP
    out = wind(rng, L, 250, 900, 0.35, rate=0.12) * 0.8
    out += wind(rng, L, 80, 250, 1.0, rate=0.1) * 0.35
    return make_loop(out, OVERLAP)


def amb_sea(rng) -> np.ndarray:
    L = LOOP + OVERLAP
    n = seconds(L)
    out = np.zeros((n, 2))
    t = 0.0
    while t < L:
        period = rng.uniform(5.5, 9.0)
        d = period * 1.4
        tt = t_axis(d)
        k = tt / d
        shape = np.sin(np.pi * k) ** 2 * (1 + 0.6 * np.sin(np.pi * np.clip(k * 1.6, 0, 1)))
        body = lowpass(colored(rng, d, 1.6), 1400) * shape
        foam = highpass(white(rng, d), 2500) * np.clip(np.sin(np.pi * (k - 0.35) / 0.5), 0, 1) ** 3 * 0.08
        add_at(out, pan(body + foam, rng.uniform(-0.5, 0.5)), t)
        t += period
    out += wind(rng, L, 300, 900, 1.0) * 0.08
    for _ in range(int(rng.integers(3, 6))):
        add_at(out, pan(gull(rng) * 0.12, rng.uniform(-0.9, 0.9)), rng.uniform(0, L - 1))
    return make_loop(out, OVERLAP)


def amb_settlement(rng) -> np.ndarray:
    L = LOOP + OVERLAP
    out = fire(rng, L) * 0.35
    out += voice_babble(rng, L, voices=8) * 0.9
    out += wind(rng, L, 300, 900, 1.0) * 0.06
    t = rng.uniform(0, 2)
    pos = rng.uniform(-0.8, 0.8)
    while t < L:  # someone chopping wood now and then
        for k in range(int(rng.integers(3, 8))):
            add_at(out, pan(wood_chop(rng) * 0.4, pos), t + k * rng.uniform(0.9, 1.3), wrap=False)
        t += rng.uniform(9, 16)
        pos = rng.uniform(-0.8, 0.8)
    t = rng.uniform(3, 10)
    while t < L:  # a smith or a stone-worker
        for k in range(int(rng.integers(2, 5))):
            add_at(out, pan(hammer_anvil(rng) * 0.12, 0.6), t + k * 0.55, wrap=False)
        t += rng.uniform(10, 20)
    return make_loop(out, OVERLAP)


def amb_battle(rng) -> np.ndarray:
    L = LOOP + OVERLAP
    out = war_cry(rng, L) * 0.8
    out += wind(rng, L, 80, 300, 1.0) * 0.2
    t = 0.0
    while t < L:
        if rng.uniform() < 0.7:
            add_at(out, pan(metal_clash(rng) * rng.uniform(0.08, 0.3), rng.uniform(-0.9, 0.9)), t)
        else:
            add_at(out, pan(thud(rng) * rng.uniform(0.2, 0.5), rng.uniform(-0.7, 0.7)), t)
        t += rng.exponential(0.28)
    return make_loop(out, OVERLAP)


LOOPS = {
    "amb_forest": amb_forest,
    "amb_grassland": amb_grassland,
    "amb_wetland": amb_wetland,
    "amb_desert": amb_desert,
    "amb_cold": amb_cold,
    "amb_sea": amb_sea,
    "amb_settlement": amb_settlement,
    "amb_battle": amb_battle,
}


def render(name: str, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    return normalize(LOOPS[name](rng), 0.7)
