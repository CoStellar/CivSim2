"""Draws docs/ai/img/100_gier.png and prints the numbers of the stage 4g report from a batch CSV.

Usage: python3 docs/ai/tools/batch_report.py games.csv
games.csv comes from: CivSim.Headless --seed 1 --turns 1000 --batch 100 --batch-out games.csv --jobs 4. Needs matplotlib.
"""
import csv
import os
import statistics
import sys

import matplotlib

matplotlib.use('Agg')
import matplotlib.pyplot as plt

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', '100_gier.png')
BLUE, ORANGE, AQUA = '#2a78d6', '#eb6834', '#1baf7a'
INK, MUTED, SURFACE = '#0b0b0b', '#52514e', '#fcfcfb'
ERAS = ['epoka kamienia', 'neolit', 'brąz']

rows = list(csv.DictReader(open(sys.argv[1], encoding='utf-8')))
n = len(rows)


def col(name):
    return [int(r[name]) for r in rows]


def describe(name, label):
    v = col(name)
    q = statistics.quantiles(v, n=10) if len(v) > 1 else [v[0]] * 9
    print(f"{label}: średnio {statistics.mean(v):.1f}, mediana {statistics.median(v):g}, "
          f"10% gier ≤ {q[0]:g}, 90% gier ≤ {q[8]:g}, min {min(v)}, max {max(v)}, zero w {sum(1 for x in v if x == 0)} grach")


print(f"gier: {n}")
for name, label in [('years', 'lata gry'), ('wars', 'wojny'), ('wars_revenge', 'wojny o odwet'), ('wars_land', 'wojny o ziemię'),
                    ('wars_loot', 'wojny o łupy'), ('wars_breaking_treaty', 'wojny łamiące umowę'), ('peaces', 'pokoje'),
                    ('trade', 'umowy handlowe'), ('pacts', 'pakty'), ('alliances', 'sojusze'), ('allies_joined', 'sojusznik przystąpił'),
                    ('conquests', 'podbite osady'), ('died_out', 'wymarłe cywilizacje'), ('alive_end', 'żywe na końcu'),
                    ('secessions', 'secesje'), ('coups', 'przewroty'), ('government_changes', 'zmiany ustroju'),
                    ('stability_avg', 'średnia stabilność'), ('unrest_percent', '% lat w niepokojach'),
                    ('hungry_settlement_years', 'lata głodu osad'), ('hunger_without_reaction', 'głód bez reakcji (obozy)'), ('village_hunger', 'głód 3 kroki w wioskach'),
                    ('settlements', 'osady na końcu'), ('people', 'ludzie na końcu')]:
    describe(name, label)
eras = col('max_era')
for e in range(max(eras) + 1):
    print(f"najdalsza era {ERAS[e] if e < len(ERAS) else e}: {sum(1 for x in eras if x == e)} gier")
neo = [int(r['era_years'].split(';')[1]) for r in rows if len(r['era_years'].split(';')) > 1]
if neo:
    print(f"pierwszy lud w neolicie: mediana roku {statistics.median(neo):g}, od {min(neo)} do {max(neo)}")
ms = [float(r['avg_turn_ms']) for r in rows]
print(f"czas tury (4 gry naraz): średnio {statistics.mean(ms):.1f} ms, max średniej {max(ms):.1f} ms")

plt.rcParams.update({'font.size': 9, 'axes.edgecolor': MUTED, 'axes.labelcolor': INK, 'xtick.color': MUTED, 'ytick.color': MUTED,
                     'axes.spines.top': False, 'axes.spines.right': False, 'figure.facecolor': SURFACE, 'axes.facecolor': SURFACE})
fig, axes = plt.subplots(2, 3, figsize=(15, 8.5))
fig.suptitle(f'{n} gier AI kontra AI (mapa standard, 12 cywilizacji, 1000 kroków świata)', fontsize=12, color=INK)


def hist(ax, values, title, xlabel, color=BLUE, bins=None):
    top = max(values) if values else 0
    step = max(1, (top + 2) // 20)
    bins = bins or [x - 0.5 * (step == 1) for x in range(0, top + 2 * step, step)]
    ax.hist(values, bins=bins, color=color, edgecolor=SURFACE, linewidth=2)
    ax.set_title(title, fontsize=10, color=INK)
    ax.set_xlabel(xlabel)
    ax.set_ylabel('liczba gier')
    m = statistics.median(values)
    ax.axvline(m, color=INK, linewidth=1, linestyle='--')
    ax.text(m, ax.get_ylim()[1] * 0.95, f' mediana {m:g}', color=INK, va='top', fontsize=8)


# Wars by goal: stacked bars per game, games sorted by the number of wars.
ax = axes[0][0]
order = sorted(range(n), key=lambda k: int(rows[k]['wars']))
xs = range(n)
rev = [int(rows[k]['wars_revenge']) for k in order]
land = [int(rows[k]['wars_land']) for k in order]
loot = [int(rows[k]['wars_loot']) for k in order]
ax.bar(xs, rev, color=BLUE, width=1.0, edgecolor=SURFACE, linewidth=0.5, label='o odwet')
ax.bar(xs, land, bottom=rev, color=ORANGE, width=1.0, edgecolor=SURFACE, linewidth=0.5, label='o ziemię')
ax.bar(xs, loot, bottom=[a + b for a, b in zip(rev, land)], color=AQUA, width=1.0, edgecolor=SURFACE, linewidth=0.5, label='o łupy')
ax.set_title('Wojny w każdej grze (gry od najspokojniejszej)', fontsize=10, color=INK)
ax.set_xlabel('gry')
ax.set_ylabel('wojny')
ax.set_xticks([])
ax.legend(frameon=False)

hist(axes[0][1], col('died_out'), 'Ile cywilizacji wymarło', 'wymarłe cywilizacje w grze')
hist(axes[0][2], col('alive_end'), 'Ile ludów żyje na końcu (12 na starcie + secesje)', 'żywe cywilizacje na końcu gry')
hist(axes[1][0], col('government_changes'), 'Zmiany ustroju (strateg i przewroty)', 'zmiany w grze')
hist(axes[1][1], col('unrest_percent'), 'Niepokoje', '% lat cywilizacji w niepokojach')
hist(axes[1][2], col('hunger_without_reaction'), 'Głód bez reakcji (obozy)', 'obóz głoduje 3 własne kroki w tym samym miejscu (razy w grze)')
fig.tight_layout(rect=(0, 0, 1, 0.96))
fig.savefig(OUT, dpi=110)
print(OUT)
