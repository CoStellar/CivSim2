"""Draws docs/ai/img/fachy_przed_po.png: people and settlements before and after a change, and skilled people over time.

Usage: python3 docs/ai/tools/skills_chart.py RUNS_DIR
RUNS_DIR holds before7.csv, before23.csv, before42.csv, after7.csv, after23.csv and after42.csv: --stats files
(CivSim.Headless --stats, standard map, 1000 turns). The "after" files need the skilled_<skill> columns (stage 4F).
Needs matplotlib.
"""
import csv
import os
import sys

import matplotlib

matplotlib.use('Agg')
import matplotlib.pyplot as plt

RUNS = sys.argv[1] if len(sys.argv) > 1 else '.'
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'fachy_przed_po.png')
SEEDS = (7, 23, 42)
COLORS = {7: '#2a6fdb', 23: '#e8833a', 42: '#3aa676'}
NAMES = {'hunter': 'myśliwi', 'fisher': 'rybacy', 'farmer': 'rolnicy', 'herder': 'pasterze', 'knapper': 'krzemieniarze',
         'tanner': 'garbarze', 'potter': 'garncarze', 'weaver': 'tkacze', 'mason': 'kamieniarze', 'baker': 'piekarze'}


def load(name):
    with open(os.path.join(RUNS, name), newline='') as f:
        return list(csv.DictReader(f))


def column(rows, key):
    return [int(float(r[key])) for r in rows]


fig, axes = plt.subplots(2, 2, figsize=(15, 10))
people, camps, producers, artisans = axes[0][0], axes[0][1], axes[1][0], axes[1][1]
for seed in SEEDS:
    for when, style in (('before', '--'), ('after', '-')):
        rows = load(f'{when}{seed}.csv')
        turns = column(rows, 'turn')
        label = f'seed {seed}, {"przed" if when == "before" else "po"}'
        people.plot(turns, column(rows, 'people'), style, color=COLORS[seed], label=label)
        camps.plot(turns, [s - v for s, v in zip(column(rows, 'settlements'), column(rows, 'villages'))], style, color=COLORS[seed], label=label)

after = load('after23.csv')
turns = column(after, 'turn')
for key, name in NAMES.items():
    if f'skilled_{key}' not in after[0]:
        continue
    ax = producers if key in ('hunter', 'fisher', 'farmer', 'herder') else artisans
    ax.plot(turns, column(after, f'skilled_{key}'), label=name)

for ax, title, ylabel in (
        (people, 'Ludność świata', 'ludzie'),
        (camps, 'Obozy (zamieszkane osady, które nie są wioskami)', 'liczba obozów'),
        (producers, 'Seed 23, po: fachowcy wśród wytwórców', 'fachowcy'),
        (artisans, 'Seed 23, po: fachowcy wśród rzemieślników', 'fachowcy')):
    ax.set_title(title, fontsize=10)
    ax.set_xlabel('tura')
    ax.set_ylabel(ylabel)
    ax.spines[['top', 'right']].set_visible(False)
    ax.legend(frameon=False, fontsize=7)

fig.suptitle('Etap 4F, PR 2: nauka fachu, mapa standard, 1000 tur (linia przerywana = przed, ciągła = po)', fontsize=11)
plt.tight_layout()
plt.savefig(OUT, dpi=130)
print(OUT)
