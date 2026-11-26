"""Short effects of the game, synthesized (see synth.py)."""
from __future__ import annotations

import numpy as np

from synth import (RATE, add_at, bandpass, click_burst, colored, env_ad, fade, highpass, lowpass, modal, normalize,
                   pan, seconds, t_axis, white)


def _stereo(x):
    return np.stack([x, x], axis=1)


def ui_click(rng):
    x = modal([1650, 3900, 6100], [0.012, 0.006, 0.003], [1, 0.4, 0.2], 0.06, rng)
    x += click_burst(rng, 0.06, 0.002, 2000, 9000) * 0.5
    return _stereo(x)


def _swish(rng, up: bool, d=0.22):
    n = seconds(d)
    k = np.linspace(0, 1, n)
    centre = 900 + 3500 * (k if up else 1 - k)
    from synth import moving_band
    x = moving_band(white(rng, d), centre, 0.6) * np.sin(np.pi * k) ** 1.5
    return x


def ui_open(rng):
    return _stereo(_swish(rng, True))


def ui_close(rng):
    return _stereo(_swish(rng, False, 0.18))


def ui_error(rng):
    out = np.zeros(seconds(0.3))
    for s in (0.0, 0.12):
        hit = modal([180, 410], [0.05, 0.03], [1, 0.4], 0.12, rng) + click_burst(rng, 0.12, 0.006, 300, 2000) * 0.4
        add_at(out, hit, s, wrap=False)
    return _stereo(out)


def build_place(rng):
    d = 0.6
    t = t_axis(d)
    f = 110 * np.exp(-t * 3)
    body = np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-t / 0.08)
    wood = modal([240, 520, 1100], [0.06, 0.04, 0.02], [0.8, 0.5, 0.3], d, rng)
    dust = lowpass(highpass(white(rng, d), 800), 5000) * env_ad(seconds(d), 0.02, 0.15) * 0.25
    return _stereo(body + wood + dust)


def _bell(rng, f, d=1.4, amp=1.0):
    return modal([f, f * 2.0, f * 2.76, f * 4.07, f * 5.4], [d, d * 0.6, d * 0.4, d * 0.25, d * 0.15], [1, 0.5, 0.45, 0.25, 0.12], d * 1.6, rng) * amp


def build_done(rng):
    out = np.zeros(seconds(1.6))
    for i, s in enumerate((0.0, 0.18, 0.36)):
        knock = modal([300, 640, 1300], [0.05, 0.03, 0.015], [1, 0.6, 0.3], 0.2, rng) + click_burst(rng, 0.2, 0.006, 900, 6000) * 0.5
        add_at(out, knock * (0.7 + 0.15 * i), s, wrap=False)
    add_at(out, _bell(rng, 1318, 0.8, 0.6), 0.55, wrap=False)
    return _stereo(out)


def unit_order(rng):
    out = np.zeros((seconds(0.45), 2))
    for i, s in enumerate((0.0, 0.17)):
        step = lowpass(white(rng, 0.12), 900) * env_ad(seconds(0.12), 0.004, 0.03)
        step += click_burst(rng, 0.12, 0.01, 1500, 6000) * 0.3
        add_at(out, pan(step, -0.3 if i == 0 else 0.3), s, wrap=False)
    rustle = bandpass(white(rng, 0.3), 2000, 7000) * env_ad(seconds(0.3), 0.05, 0.08) * 0.15
    add_at(out, pan(rustle, 0), 0.05, wrap=False)
    return out


def unit_attack(rng):
    d = 0.7
    n = seconds(d)
    k = np.linspace(0, 1, n)
    from synth import moving_band
    scrape = moving_band(white(rng, d), 2500 + 4000 * k, 0.3) * np.sin(np.pi * np.clip(k * 1.4, 0, 1)) ** 2
    ring = modal([2900, 4300, 6800], [0.25, 0.18, 0.1], [0.4, 0.3, 0.15], d, rng)
    ring[: seconds(0.25)] = 0
    return _stereo(scrape * 0.8 + ring)


def unit_trained(rng):
    d = 0.5
    t = t_axis(d)
    f = 150 * np.exp(-t * 2.5)
    drum = np.sin(2 * np.pi * np.cumsum(f) / RATE) * np.exp(-t / 0.15)
    skin = bandpass(white(rng, d), 200, 1200) * np.exp(-t / 0.04) * 0.4
    out = np.zeros(seconds(0.8))
    add_at(out, drum + skin, 0.0, wrap=False)
    add_at(out, (drum + skin) * 0.8, 0.22, wrap=False)
    return _stereo(out)


def caravan_sent(rng):
    out = np.zeros(seconds(1.2))
    for s in (0.0, 0.32):
        low, high = _bell(rng, 620, 0.35, 0.6), _bell(rng, 930, 0.25, 0.3)
        add_at(out, low, s, wrap=False)
        add_at(out, high, s, wrap=False)
    return _stereo(out)


def tear_down(rng):
    out = np.zeros((seconds(1.2), 2))
    for i in range(14):
        s = rng.uniform(0, 0.7)
        chunk = lowpass(white(rng, 0.2), rng.uniform(600, 2500)) * env_ad(seconds(0.2), 0.002, rng.uniform(0.02, 0.06))
        add_at(out, pan(chunk * rng.uniform(0.3, 1.0), rng.uniform(-0.6, 0.6)), s, wrap=False)
    rumble = lowpass(colored(rng, 1.2, 2.0), 200) * env_ad(seconds(1.2), 0.05, 0.4)
    return out + _stereo(rumble * 0.6)


EFFECTS = {
    "ui_click": ui_click,
    "ui_open": ui_open,
    "ui_close": ui_close,
    "ui_error": ui_error,
    "build_place": build_place,
    "build_done": build_done,
    "unit_order": unit_order,
    "unit_attack": unit_attack,
    "unit_trained": unit_trained,
    "caravan_sent": caravan_sent,
    "tear_down": tear_down,
}


def render(name: str, seed: int):
    rng = np.random.default_rng(seed)
    x = EFFECTS[name](rng)
    return fade(normalize(x, 0.8), 0.0, min(0.03, len(x) / RATE / 4))
