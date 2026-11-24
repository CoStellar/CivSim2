"""Small sound synthesis kit for the game's ambient loops and effects (numpy + scipy, no samples).

Everything here is made from noise and sine waves, so the sounds belong to the project (no licence to follow).
"""
from __future__ import annotations

import numpy as np
from scipy import signal
from scipy.io import wavfile

RATE = 44100


def seconds(n: float) -> int:
    return int(round(n * RATE))


def t_axis(length: float) -> np.ndarray:
    return np.arange(seconds(length)) / RATE


# ---------------------------------------------------------------- noise


def white(rng: np.random.Generator, length: float) -> np.ndarray:
    return rng.standard_normal(seconds(length))


def colored(rng: np.random.Generator, length: float, slope: float) -> np.ndarray:
    """Noise whose power falls by `slope` per octave (1 = pink, 2 = brown)."""
    n = seconds(length)
    spec = np.fft.rfft(rng.standard_normal(n))
    f = np.fft.rfftfreq(n, 1 / RATE)
    f[0] = f[1]
    spec *= f ** (-slope / 2)
    out = np.fft.irfft(spec, n)
    return out / (np.abs(out).max() + 1e-9)


def smooth_random(rng: np.random.Generator, length: float, rate_hz: float, lo: float, hi: float) -> np.ndarray:
    """A slowly wandering control curve between lo and hi (about `rate_hz` changes per second)."""
    n = seconds(length)
    points = max(4, int(length * rate_hz) + 4)
    knots = rng.uniform(lo, hi, points)
    x = np.linspace(0, points - 1, n)
    xi = np.floor(x).astype(int)
    frac = x - xi
    frac = frac * frac * (3 - 2 * frac)
    xi2 = np.minimum(xi + 1, points - 1)
    return knots[xi] * (1 - frac) + knots[xi2] * frac


# ---------------------------------------------------------------- filters


def lowpass(x: np.ndarray, cutoff: float, order: int = 2) -> np.ndarray:
    sos = signal.butter(order, cutoff, "low", fs=RATE, output="sos")
    return signal.sosfilt(sos, x)


def highpass(x: np.ndarray, cutoff: float, order: int = 2) -> np.ndarray:
    sos = signal.butter(order, cutoff, "high", fs=RATE, output="sos")
    return signal.sosfilt(sos, x)


def bandpass(x: np.ndarray, lo: float, hi: float, order: int = 2) -> np.ndarray:
    sos = signal.butter(order, [lo, hi], "band", fs=RATE, output="sos")
    return signal.sosfilt(sos, x)


def moving_band(x: np.ndarray, center: np.ndarray, width_oct: float) -> np.ndarray:
    """Band-pass whose centre follows `center` (Hz per sample); width in octaves. Done frame by frame in the STFT."""
    nper = 2048
    f, _, z = signal.stft(x, RATE, nperseg=nper)
    frames = z.shape[1]
    idx = np.clip((np.arange(frames) * (nper // 2)), 0, len(center) - 1)
    c = center[idx]
    ff = np.maximum(f, 1.0)[:, None]
    gain = np.exp(-0.5 * (np.log2(ff / c[None, :]) / width_oct) ** 2)
    _, y = signal.istft(z * gain, RATE, nperseg=nper)
    y = y[: len(x)]
    if len(y) < len(x):
        y = np.pad(y, (0, len(x) - len(y)))
    return y


# ---------------------------------------------------------------- envelopes and helpers


def env_ad(n: int, attack: float, decay: float) -> np.ndarray:
    a = min(n, seconds(attack))
    out = np.zeros(n)
    if a > 0:
        out[:a] = np.linspace(0, 1, a)
    rest = n - a
    if rest > 0:
        out[a:] = np.exp(-np.arange(rest) / (RATE * max(decay, 1e-4)))
    return out


def fade(x: np.ndarray, fin: float, fout: float) -> np.ndarray:
    y = x.copy()
    a, b = seconds(fin), seconds(fout)
    if a:
        y[:a] *= np.linspace(0, 1, a)[:, None] if y.ndim == 2 else np.linspace(0, 1, a)
    if b:
        y[-b:] *= np.linspace(1, 0, b)[:, None] if y.ndim == 2 else np.linspace(1, 0, b)
    return y


def pan(mono: np.ndarray, position: float) -> np.ndarray:
    """Stereo from mono; position −1 left … 1 right (equal power)."""
    angle = (position + 1) * np.pi / 4
    return np.stack([mono * np.cos(angle), mono * np.sin(angle)], axis=1)


def add_at(buffer: np.ndarray, sound: np.ndarray, start: float, wrap: bool = True) -> None:
    """Mixes `sound` into `buffer` from `start` seconds; with wrap the tail continues at the start (seamless loops)."""
    s = seconds(start)
    n = len(sound)
    total = len(buffer)
    if s >= total:
        return
    end = min(total, s + n)
    buffer[s:end] += sound[: end - s]
    if wrap and s + n > total:
        rest = sound[end - s:]
        buffer[: len(rest)] += rest[:total]


def modal(freqs: list[float], decays: list[float], amps: list[float], length: float, rng=None, detune: float = 0.0) -> np.ndarray:
    """Struck object: decaying sine partials (wood, metal, bells)."""
    t = t_axis(length)
    out = np.zeros_like(t)
    for f, d, a in zip(freqs, decays, amps):
        if rng is not None and detune:
            f *= 1 + rng.uniform(-detune, detune)
        out += a * np.sin(2 * np.pi * f * t + (rng.uniform(0, 6.28) if rng is not None else 0)) * np.exp(-t / d)
    return out


def click_burst(rng: np.random.Generator, length: float, decay: float, lo: float, hi: float) -> np.ndarray:
    n = seconds(length)
    return bandpass(rng.standard_normal(n), lo, hi) * env_ad(n, 0.0005, decay)


def normalize(x: np.ndarray, peak: float = 0.89) -> np.ndarray:
    m = np.abs(x).max()
    return x if m == 0 else x * (peak / m)


def make_loop(x: np.ndarray, overlap: float) -> np.ndarray:
    """Cuts `overlap` seconds off the end and cross-fades them into the start, so the file loops without a seam."""
    o = seconds(overlap)
    body = x[:-o].copy()
    tail = x[-o:]
    ramp = np.linspace(0, 1, o)
    if x.ndim == 2:
        ramp = ramp[:, None]
    body[:o] = body[:o] * ramp + tail * (1 - ramp)
    return body


def write_wav(path: str, x: np.ndarray) -> None:
    if x.ndim == 1:
        x = np.stack([x, x], axis=1)
    wavfile.write(path, RATE, (np.clip(x, -1, 1) * 32767).astype(np.int16))


def read_wav(path: str) -> np.ndarray:
    rate, data = wavfile.read(path)
    assert rate == RATE, path
    x = data.astype(np.float64) / 32768
    if x.ndim == 1:
        x = np.stack([x, x], axis=1)
    return x
