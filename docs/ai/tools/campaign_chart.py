"""Draws docs/ai/img/kampanie_3_seedy.png: war campaigns of stage 4e (PR 3-5) in three 1000-turn games.

Usage: python3 docs/ai/tools/campaign_chart.py RUN_DIR
RUN_DIR holds camp_7.csv, camp_23.csv, camp_42.csv (CivSim.Headless --campaigns) and log_7.txt, log_23.txt, log_42.txt
(the chronicle printed by CivSim.Headless without --quiet). Needs matplotlib.
"""
import csv
import os
import re
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

RUN = sys.argv[1]
SEEDS = [7, 23, 42]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'kampanie_3_seedy.png')
INK, MUTED, GRID = '#2b2b2a', '#6b6a64', '#dddcd6'
SERIES = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#6250d6', '#8a8984']

# How campaigns ended, from the chronicle lines.
OUTCOMES = [
    ('wygrana bitwa (łupy, ucieczka z obozu)', r' beat .* at .*fell in'),
    ('zdobyta osada', r' took .* from .*fell in'),
    ('przegrana bitwa (rozbici albo obrońcy się utrzymali)', r'(routed the warriors|held .* against)'),
    ('zawrócili przed silnym celem (reguła do PR 4)', r'saw how strong'),
    ('dowódca odwołał wyprawę (od PR 5)', r'called its warriors back'),
    ('głód złamał wyprawę', r'Hunger broke'),
]

rows = {s: list(csv.DictReader(open(os.path.join(RUN, f'camp_{s}.csv')))) for s in SEEDS}
counts = {}
for s in SEEDS:
    text = open(os.path.join(RUN, f'log_{s}.txt'), encoding='utf-8').read()
    counts[s] = [len(re.findall(p, text)) for _, p in OUTCOMES]

plt.rcParams.update({'font.size': 10, 'axes.edgecolor': GRID, 'axes.labelcolor': INK, 'xtick.color': MUTED, 'ytick.color': MUTED})
fig, axes = plt.subplots(2, 2, figsize=(13, 9))
fig.suptitle('Kampanie wojenne w 3 grach po 1000 tur (etap 4e PR 3–5: armie, tragarze, głód, bitwy w rundach, dowódca)', color=INK, fontsize=13)

# (1) Outcomes per seed, stacked.
a = axes[0][0]
left = [0] * len(SEEDS)
for k, (label, _) in enumerate(OUTCOMES):
    vals = [counts[s][k] for s in SEEDS]
    a.barh([f'seed {s}' for s in SEEDS], vals, left=left, color=SERIES[k], edgecolor='white', linewidth=2, label=label, height=0.55)
    left = [l + v for l, v in zip(left, vals)]
for i, s in enumerate(SEEDS):
    a.text(left[i] + 0.5, i, f'{len(rows[s])} kampanii', va='center', color=INK, fontsize=9)
a.invert_yaxis()
a.set_title('Jak kończyły się wyprawy (zdarzenia w kronice)', color=INK, loc='left')
a.legend(frameon=False, fontsize=8, loc='lower right')
a.set_xlim(0, max(left) * 1.35)

def hist(ax, key, bins, title, xlabel):
    vals = [int(r[key]) for s in SEEDS for r in rows[s]]
    ax.hist(vals, bins=bins, color=SERIES[0], edgecolor='white', linewidth=2)
    ax.set_title(title, color=INK, loc='left')
    ax.set_xlabel(xlabel)
    ax.set_ylabel('kampanie')
    ax.grid(axis='y', color=GRID, lw=0.6)
    ax.set_axisbelow(True)

hist(axes[0][1], 'warriors', range(0, 55, 5), 'Wielkość armii (limit wodza w erach 1–2: 50)', 'wojownicy na wyprawie')
hist(axes[1][0], 'settlements', range(1, 16), 'Z ilu osad zebrano armię', 'osady (do 6 pól od osady wyjścia)')

# (4) Months out vs hungry months.
a = axes[1][1]
months = [int(r['months']) for s in SEEDS for r in rows[s]]
hungry = [int(r['hungry_months']) for s in SEEDS for r in rows[s]]
a.scatter(months, hungry, s=36, color=SERIES[1], edgecolor='white', linewidth=1.5, alpha=0.85)
top = max(months + [1])
a.plot([0, top], [0, top], color=MUTED, lw=1, ls='--')
a.text(top * 0.55, top * 0.62, 'cała wyprawa o głodzie', color=MUTED, fontsize=8, rotation=35)
a.set_title('Miesiące wyprawy i miesiące głodu', color=INK, loc='left')
a.set_xlabel('miesiące wyprawy')
a.set_ylabel('miesiące bez jedzenia')
a.grid(color=GRID, lw=0.6)
a.set_axisbelow(True)

for ax in axes.flat:
    for side in ('top', 'right'):
        ax.spines[side].set_visible(False)
fig.tight_layout(rect=(0, 0, 1, 0.95))
fig.savefig(OUT, dpi=110, facecolor='white')
print(OUT)
