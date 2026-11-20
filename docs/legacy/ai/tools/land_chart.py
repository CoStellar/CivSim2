"""Draws docs/ai/img/ziemia_przed_po.png: how far people live from their start, and settlements (camps and villages).

Usage: python3 docs/ai/tools/land_chart.py RUNS_DIR
RUNS_DIR holds before7.csv, before23.csv, before42.csv, after7.csv, after23.csv and after42.csv: --stats files
(CivSim.Headless --stats, standard map, 1000 turns) from before and after a change. Needs the settlements and
spread_cells columns (added in PR C; for an older build, add them to its HeadlessRun.cs first). Needs matplotlib.
"""
import csv
import os
import sys

import matplotlib

matplotlib.use('Agg')
import matplotlib.pyplot as plt

RUNS = sys.argv[1] if len(sys.argv) > 1 else '.'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'ziemia_przed_po.png')
SEEDS = (7, 23, 42)
COLORS = {7: '#2a6fdb', 23: '#e8833a', 42: '#3aa676'}


def load(name):
    with open(os.path.join(RUNS, name), newline='') as f:
        return list(csv.DictReader(f))


def column(rows, key):
    return [int(r[key]) for r in rows] if rows and key in rows[0] else None


fig, (dist, sets, vill) = plt.subplots(1, 3, figsize=(17, 5.5))
for seed in SEEDS:
    for when, style in (('before', '--'), ('after', '-')):
        rows = load(f'{when}{seed}.csv')
        turns = column(rows, 'turn')
        label = f'seed {seed}, {"przed" if when == "before" else "po"}'
        spread = column(rows, 'spread_cells')
        if spread is not None:
            dist.plot(turns, spread, style, color=COLORS[seed], label=label)
        lived = column(rows, 'settlements')
        if lived is not None:
            sets.plot(turns, lived, style, color=COLORS[seed], label=label)
        villages = column(rows, 'villages')
        if villages is not None:
            vill.plot(turns, villages, style, color=COLORS[seed], label=label)

for ax, title, ylabel in (
        (dist, 'Jak daleko od startu żyją ludzie (średnia na osobę)', 'pola'),
        (sets, 'Zamieszkane osady (obozy i wioski z ludźmi)', 'liczba osad'),
        (vill, 'Wioski (osady rolnicze)', 'liczba wiosek')):
    ax.set_title(title, fontsize=10)
    ax.set_xlabel('tura')
    ax.set_ylabel(ylabel)
    ax.spines[['top', 'right']].set_visible(False)
    ax.legend(frameon=False, fontsize=7)

fig.suptitle('PR C: ziemia i migracje, mapa standard, 1000 tur (linia przerywana = przed, ciągła = po)', fontsize=11)
plt.tight_layout()
plt.savefig(OUT, dpi=130)
print(OUT)
