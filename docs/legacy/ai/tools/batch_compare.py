"""Draws docs/ai/img/100_gier_po_poprawkach.png: the stage 4g batch before and after the AI fixes, game by game medians.

Usage: python3 docs/ai/tools/batch_compare.py before.csv after.csv
Both come from: CivSim.Headless --seed 1 --turns 1000 --batch 100 --batch-out games.csv --jobs 4. Needs matplotlib.
"""
import csv
import os
import statistics
import sys

import matplotlib

matplotlib.use('Agg')
import matplotlib.pyplot as plt

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', '100_gier_po_poprawkach.png')
BLUE, ORANGE = '#2a78d6', '#eb6834'
INK, MUTED, SURFACE = '#0b0b0b', '#52514e', '#fcfcfb'

before = list(csv.DictReader(open(sys.argv[1], encoding='utf-8')))
after = list(csv.DictReader(open(sys.argv[2], encoding='utf-8')))

# (column, label, per game: mean or share of games with at least one)
ROWS = [
    ('village_hunger', 'Głodujące wioski (3 kroki z rzędu)', 'mean'),
    ('wars', 'Wojny', 'mean'),
    ('wars_revenge', 'Wojny o odwet', 'mean'),
    ('wars_loot', 'Wojny o łupy', 'mean'),
    ('wars_land', 'Wojny o ziemię', 'mean'),
    ('pacts', 'Pakty o nieagresji', 'mean'),
    ('alliances', 'Sojusze obronne', 'mean'),
    ('allies_joined', 'gry: sojusznik przystąpił do wojny', 'share'),
    ('conquests', 'gry: osada zdobyta na wojnie', 'share'),
    ('wars_loot', 'gry: wojna o łupy', 'share'),
]


def value(rows, col, kind):
    v = [int(r[col]) for r in rows]
    return statistics.mean(v) if kind == 'mean' else 100 * sum(1 for x in v if x > 0) / len(v)


fig, axes = plt.subplots(1, 2, figsize=(13, 6.4), facecolor=SURFACE, gridspec_kw={'width_ratios': [1.2, 1]})
for ax, kind, title in [(axes[0], 'mean', 'Średnio na grę'), (axes[1], 'share', 'Ile gier (na 100) to miało')]:
    rows = [r for r in ROWS if r[2] == kind]
    labels = [r[1].replace('gry: ', '') for r in rows]
    b = [value(before, r[0], kind) for r in rows]
    a = [value(after, r[0], kind) for r in rows]
    y = range(len(rows))
    ax.set_facecolor(SURFACE)
    ax.barh([i - 0.2 for i in y], b, height=0.38, color=BLUE, label='przed poprawkami')
    ax.barh([i + 0.2 for i in y], a, height=0.38, color=ORANGE, label='po poprawkach')
    for i in y:
        ax.text(b[i], i - 0.2, f' {b[i]:.0f}', va='center', fontsize=9, color=MUTED)
        ax.text(a[i], i + 0.2, f' {a[i]:.0f}', va='center', fontsize=9, color=INK)
    ax.set_yticks(list(y))
    ax.set_yticklabels(labels, fontsize=10, color=INK)
    ax.invert_yaxis()
    ax.set_title(title, fontsize=12, color=INK, loc='left')
    ax.tick_params(axis='x', colors=MUTED)
    for side in ('top', 'right'):
        ax.spines[side].set_visible(False)
    if kind == 'share':
        ax.set_xlim(0, 110)
    else:
        ax.set_xscale('symlog', linthresh=10)
        ax.set_xticks([0, 5, 10, 50, 100, 300])
        ax.set_xticklabels(['0', '5', '10', '50', '100', '300'])
axes[0].legend(loc='lower right', frameon=False)
fig.suptitle('100 gier AI kontra AI przed poprawkami i po nich (1000 kroków, mapa standard)', fontsize=13, color=INK, x=0.02, ha='left')
fig.tight_layout()
fig.savefig(OUT, dpi=110, facecolor=SURFACE)
print(OUT)
