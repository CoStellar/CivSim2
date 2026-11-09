"""Draws docs/ai/img/badania_przed_po.png: technologies and science before and after a change.

Usage: python3 docs/ai/tools/research_chart.py RUNS_DIR
RUNS_DIR holds before7.csv, before23.csv, before42.csv, after7.csv, after23.csv and after42.csv: --stats files
(CivSim.Headless --stats, standard map, 1000 turns). Needs matplotlib.
"""
import csv
import os
import sys

import matplotlib

matplotlib.use('Agg')
import matplotlib.pyplot as plt

RUNS = sys.argv[1] if len(sys.argv) > 1 else '.'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'badania_przed_po.png')
SEEDS = (7, 23, 42)
COLORS = {7: '#2a6fdb', 23: '#e8833a', 42: '#3aa676'}


def load(name):
    with open(os.path.join(RUNS, name), newline='') as f:
        return list(csv.DictReader(f))


fig, (techs, science) = plt.subplots(1, 2, figsize=(15, 5.5))
for seed in SEEDS:
    for when, style in (('before', '--'), ('after', '-')):
        rows = load(f'{when}{seed}.csv')
        turns = [int(r['turn']) for r in rows]
        label = f'seed {seed}, {"przed" if when == "before" else "po"}'
        techs.plot(turns, [float(r['techs_avg']) for r in rows], style, color=COLORS[seed], label=label)
        science.plot(turns, [float(r['science_per_turn_avg']) for r in rows], style, color=COLORS[seed], label=label)

for ax, title, ylabel in (
        (techs, 'Technologie: średnio na żywą cywilizację (z 14 er 1–2)', 'technologie'),
        (science, 'Nauka: punkty na krok, średnio na cywilizację', 'punkty nauki')):
    ax.set_title(title, fontsize=10)
    ax.set_xlabel('tura')
    ax.set_ylabel(ylabel)
    ax.spines[['top', 'right']].set_visible(False)
    ax.legend(frameon=False, fontsize=7)

fig.suptitle('Etap 4F, PR 3: wymogi ludzi i nauka od myślicieli, mapa standard, 1000 tur (przerywana = przed, ciągła = po)', fontsize=11)
plt.tight_layout()
plt.savefig(OUT, dpi=130)
print(OUT)
