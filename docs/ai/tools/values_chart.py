"""Draws docs/ai/img/wartosci_przed_po.png: the AI values rework (PR B) before and after.

Usage: python3 docs/ai/tools/values_chart.py BEFORE_DIR AFTER_DIR
Each dir holds stats7.csv, stats23.csv, stats42.csv and why7.csv, why23.csv, why42.csv from 1000-turn runs on the standard
map (CivSim.Headless --stats --decisions). Needs matplotlib.
"""
import collections
import csv
import os
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

BEFORE, AFTER = sys.argv[1], sys.argv[2]
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'img', 'wartosci_przed_po.png')
SEEDS = (7, 23, 42)
COLORS = {7: '#2a6fdb', 23: '#3aa676', 42: '#d9822b'}


def stats(folder, seed):
    return list(csv.DictReader(open(os.path.join(folder, f'stats{seed}.csv'))))


def switches(folder, seed):
    last, kinds = {}, collections.Counter()
    for r in csv.DictReader(open(os.path.join(folder, f'why{seed}.csv'))):
        if r['action'] != 'SetRecipe':
            continue
        pair = {last.get(r['settlement']), r['target']}
        last[r['settlement']] = r['target']
        if '-1' in pair:
            kinds['praca ↔ brak receptury'] += 1
        elif pair == {'1', '5'}:
            kinds['odzież ↔ chleb'] += 1
        else:
            kinds['inne (także pierwszy wybór)'] += 1
    return kinds


fig, (a, b, c) = plt.subplots(3, 1, figsize=(12, 13))
for seed in SEEDS:
    for folder, style, tag in ((BEFORE, '--', 'przed'), (AFTER, '-', 'po')):
        st = stats(folder, seed)
        turns = [int(r['turn']) for r in st]
        a.plot(turns, [int(r['people']) / 1000 for r in st], style, color=COLORS[seed], label=f'seed {seed} {tag}')
        b.plot(turns, [float(r['techs_avg']) for r in st], style, color=COLORS[seed], label=f'seed {seed} {tag}')
a.set_title('Ludność świata (tys.): linia przerywana przed PR B, ciągła po')
a.set_xlabel('krok świata'); a.legend(ncol=3, fontsize=8); a.grid(alpha=.3)
b.set_title('Średnio znanych technologii na cywilizację')
b.set_xlabel('krok świata'); b.legend(ncol=3, fontsize=8); b.grid(alpha=.3)

labels = ['praca ↔ brak receptury', 'odzież ↔ chleb', 'inne (także pierwszy wybór)']
width = 0.13
for i, seed in enumerate(SEEDS):
    for j, (folder, tag, alpha) in enumerate(((BEFORE, 'przed', .45), (AFTER, 'po', 1))):
        k = switches(folder, seed)
        xs = [x + (i * 2 + j - 2.5) * width for x in range(len(labels))]
        c.bar(xs, [k[l] for l in labels], width, color=COLORS[seed], alpha=alpha, label=f'seed {seed} {tag}')
c.set_xticks(range(len(labels))); c.set_xticklabels(labels)
c.set_title('Zmiany receptur w 1000 krokach (skarbnik)'); c.legend(ncol=3, fontsize=8); c.grid(alpha=.3, axis='y')
plt.tight_layout()
plt.savefig(OUT, dpi=130)
print(OUT)
