"""Draws docs/ai/img/ludnosc_przed_po.png: people and their yearly growth before and after a change.

Usage: python3 docs/ai/tools/population_chart.py RUNS_DIR
RUNS_DIR holds before7.csv, before23.csv, before42.csv, after7.csv, after23.csv and after42.csv: --stats files
(CivSim.Headless --stats, standard map, 1000 turns). Needs matplotlib.
"""
import csv
import math
import os
import sys

import matplotlib

matplotlib.use('Agg')
import matplotlib.pyplot as plt

RUNS = sys.argv[1] if len(sys.argv) > 1 else '.'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'ludnosc_przed_po.png')
SEEDS = (7, 23, 42)
COLORS = {7: '#2a6fdb', 23: '#e8833a', 42: '#3aa676'}
WINDOW = 20  # samples in the moving growth window


def load(name):
    with open(os.path.join(RUNS, name), newline='') as f:
        return list(csv.DictReader(f))


fig, (people, growth) = plt.subplots(1, 2, figsize=(15, 5.5))
for seed in SEEDS:
    for when, style in (('before', '--'), ('after', '-')):
        rows = load(f'{when}{seed}.csv')
        turns = [int(r['turn']) for r in rows]
        years = [float(r['year']) for r in rows]
        total = [int(r['people']) for r in rows]
        label = f'seed {seed}, {"przed" if when == "before" else "po"}'
        people.plot(turns, total, style, color=COLORS[seed], label=label)
        xs, ys = [], []
        for k in range(WINDOW, len(rows)):
            a, b = total[k - WINDOW], total[k]
            span = years[k] - years[k - WINDOW]
            if a > 0 and b > 0 and span > 0:
                xs.append(turns[k])
                ys.append(1000 * (math.exp(math.log(b / a) / span) - 1))
        growth.plot(xs, ys, style, color=COLORS[seed], label=label)

people.set_title('Ludzie na całej planecie', fontsize=10)
people.set_ylabel('ludzie')
growth.set_title(f'Przyrost: promile na rok (średnia z {WINDOW} pomiarów)', fontsize=10)
growth.set_ylabel('‰ na rok')
growth.axhline(0, color='#888', linewidth=0.6)
for ax in (people, growth):
    ax.set_xlabel('tura')
    ax.spines[['top', 'right']].set_visible(False)
    ax.legend(frameon=False, fontsize=7)

fig.suptitle('Etap 4F, PR 4: urodzenia, śmiertelność dzieci i zgony dorosłych, mapa standard, 1000 tur (przerywana = przed, ciągła = po)', fontsize=11)
plt.tight_layout()
plt.savefig(OUT, dpi=130)
print(OUT)
