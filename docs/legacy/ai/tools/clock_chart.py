"""Draws docs/ai/img/zegar_rytmy.png: the world clock and civilization rhythms (ADR 0018), and people before/after.

Usage: python3 docs/ai/tools/clock_chart.py BEFORE_DIR AFTER_DIR
Each dir holds stats7.csv, stats23.csv, stats42.csv from 1000-turn runs on the standard map (CivSim.Headless --stats).
Needs matplotlib.
"""
import csv
import os
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

BEFORE, AFTER = sys.argv[1], sys.argv[2]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'zegar_rytmy.png')

fig, (a, b) = plt.subplots(2, 1, figsize=(12, 9), gridspec_kw={'height_ratios': [1, 1.2]})

# (1) The rule itself: who computes in which year. Same algorithm as WorldClock.Begin.
civs = [('Plemię A: epoka kamienia (co 50 lat)', [(0, 600)]),
        ('Plemię B: neolit (co 25 lat)', [(0, 300)]),
        ('Plemię C: neolit, od roku 75 epoka brązu (co 20 lat)', [(0, 300), (900, 240)])]
colors = ['#8c6d46', '#3aa676', '#2a6fdb']
world = set()
for row, (label, rhythms) in enumerate(civs):
    month, nxt = 0, 0
    while month < 2400:
        rhythm = [r for start, r in rhythms if start <= month][-1]
        end = (month // rhythm + 1) * rhythm
        a.barh(row, (end - month) / 12, left=month / 12, height=0.55, color=colors[row], edgecolor='white', linewidth=2)
        a.text((month + end) / 24, row, f'{(end - month) // 12}', ha='center', va='center', color='white', fontsize=8)
        world.add(month)
        month = end
for m in sorted(world):
    a.axvline(m / 12, color='#bbb', lw=0.6, zorder=0)
a.set_yticks(range(len(civs)))
a.set_yticklabels([c[0] for c in civs], fontsize=9)
a.invert_yaxis()
a.set_xlim(0, 200)
a.set_xlabel('rok gry (szare linie: lata, w których świat coś liczy)')
a.set_title('Każdy liczy tylko w swoim kroku, za tyle lat, ile minęło (liczby = długość kroku w latach)', fontsize=10)
a.spines[['top', 'right']].set_visible(False)

# (2) People over time, before and after, three seeds.
cols = {7: '#2a6fdb', 23: '#e8833a', 42: '#3aa676'}
for seed, col in cols.items():
    for d, style, tag in ((BEFORE, '--', 'przed'), (AFTER, '-', 'po')):
        rows = list(csv.DictReader(open(f'{d}/stats{seed}.csv')))
        b.plot([int(r['year']) for r in rows], [int(r['people']) for r in rows], style, color=col, lw=1.4, label=f'seed {seed}, {tag}')
    first = next((int(r['year']) for r in csv.DictReader(open(f'{BEFORE}/stats{seed}.csv')) if r['era_max'] != '0'), None)
    if first:
        b.axvline(first, color=col, lw=0.6, ls=':')
b.set_xlabel('rok gry (kropkowane linie: pierwsza cywilizacja wchodzi w neolit)')
b.set_ylabel('ludzie na świecie')
b.set_title('Ludność w 1000 krokach: przed zmianą (przerywana) i po (ciągła). Do neolitu linie są identyczne.', fontsize=10)
b.legend(frameon=False, fontsize=8, ncol=3)
b.spines[['top', 'right']].set_visible(False)
plt.tight_layout()
plt.savefig(OUT, dpi=130)
print(OUT)
