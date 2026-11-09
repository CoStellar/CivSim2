"""Draws docs/ai/img/stabilnosc_3_seedy.png: stability of stage 4f PR 3 in three 1000-turn games.

Usage: python3 docs/ai/tools/stability_chart.py RUN_DIR
RUN_DIR holds st_7.csv, st_23.csv, st_42.csv (CivSim.Headless --stats FILE --stats-every 1). Needs matplotlib.
"""
import csv
import os
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

RUN = sys.argv[1]
SEEDS = [7, 23, 42]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'stabilnosc_3_seedy.png')
INK, MUTED, GRID = '#2b2b2a', '#6b6a64', '#dddcd6'
LINE, BARS, LIMIT = '#2a78d6', '#eb6834', '#8a8984'

fig, axes = plt.subplots(len(SEEDS), 1, figsize=(10, 8.5), sharey=True)
for ax, seed in zip(axes, SEEDS):
    rows = list(csv.DictReader(open(os.path.join(RUN, f'st_{seed}.csv'))))
    years = [int(r['year']) for r in rows]
    stability = [int(r['stability_avg']) for r in rows]
    unrest = [int(r['unrest_civs']) for r in rows]
    alive = [int(r['alive']) for r in rows]
    ax.plot(years, stability, color=LINE, linewidth=1.4, label='średnia stabilność żywych cywilizacji')
    ax.axhline(30, color=LIMIT, linestyle='--', linewidth=1, label='próg niepokojów (30)')
    ax2 = ax.twinx()
    ax2.fill_between(years, unrest, step='post', color=BARS, alpha=0.35, linewidth=0, label='cywilizacje w niepokojach')
    ax2.set_ylim(0, max(alive) + 1)
    ax2.set_ylabel('cywilizacje w niepokojach', color=MUTED, fontsize=9)
    ax2.tick_params(colors=MUTED, labelsize=8)
    ax.set_ylim(0, 100)
    ax.set_xlim(0, max(years))
    ax.set_title(f'seed {seed}', loc='left', color=INK, fontsize=10)
    ax.set_ylabel('stabilność 0–100', color=MUTED, fontsize=9)
    ax.grid(axis='y', color=GRID, linewidth=0.6)
    ax.tick_params(colors=MUTED, labelsize=8)
    for spine in ax.spines.values():
        spine.set_color(GRID)
    for spine in ax2.spines.values():
        spine.set_color(GRID)
    if seed == SEEDS[0]:
        h1, l1 = ax.get_legend_handles_labels()
        h2, l2 = ax2.get_legend_handles_labels()
        ax.legend(h1 + h2, l1 + l2, loc='lower right', fontsize=8, frameon=False)
axes[-1].set_xlabel('rok gry', color=MUTED, fontsize=9)
fig.suptitle('Stabilność w 3 grach (etap 4f PR 3): 1000 kroków świata, mapa standard, 12 cywilizacji', color=INK, fontsize=11)
fig.tight_layout()
fig.savefig(OUT, dpi=150)
print(OUT)
