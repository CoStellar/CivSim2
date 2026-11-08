"""Charts and a short report from a long run of the viewer (--long-run 4h).

Usage: python3 docs/systems/tools/long_run_report.py RUN_DIR
RUN_DIR is one folder from long_runs/ (stats.csv, turns.csv, events.csv, log.txt, summary.txt).
Writes RUN_DIR/charts/*.png and RUN_DIR/charts/raport.md (in Polish, for the project owner). Needs matplotlib.
"""
import csv
import os
import sys

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt

INK, MUTED, GRID, SURFACE = '#0b0b0b', '#52514e', '#dddcd6', '#fcfcfb'
SERIES = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100', '#e87ba4', '#008300', '#6250d6', '#8a8984']

RUN = sys.argv[1] if len(sys.argv) > 1 else sys.exit(__doc__)
OUT = os.path.join(RUN, 'charts')
os.makedirs(OUT, exist_ok=True)


def read(name):
    path = os.path.join(RUN, name)
    if not os.path.exists(path):
        return []
    with open(path, newline='', encoding='utf-8') as f:
        # A run still going (or cut off) may end with a half-written line: skip incomplete rows.
        return [r for r in csv.DictReader(f) if None not in r.values() and '' not in r.values()]


def col(rows, key, kind=float):
    return [kind(r[key]) for r in rows]


def style(ax, title, unit):
    ax.set_title(title, loc='left', color=INK, fontsize=10)
    ax.set_ylabel(unit, color=MUTED, fontsize=8)
    ax.grid(axis='y', color=GRID, linewidth=0.6)
    ax.tick_params(colors=MUTED, labelsize=8)
    ax.set_facecolor(SURFACE)
    for side in ('top', 'right'):
        ax.spines[side].set_visible(False)
    for side in ('left', 'bottom'):
        ax.spines[side].set_color(GRID)


def save(fig, name):
    fig.tight_layout()
    fig.savefig(os.path.join(OUT, name), dpi=130, facecolor=SURFACE)
    plt.close(fig)


def slope_per_hour(hours, values):
    """Least-squares slope (units per hour) over the second half of the run: the start fills caches, the rest should be flat."""
    pts = [(h, v) for h, v in zip(hours, values) if h >= hours[-1] / 2]
    if len(pts) < 3:
        return 0.0
    n = len(pts)
    mx = sum(p[0] for p in pts) / n
    my = sum(p[1] for p in pts) / n
    den = sum((p[0] - mx) ** 2 for p in pts)
    return 0.0 if den == 0 else sum((p[0] - mx) * (p[1] - my) for p in pts) / den


stats = read('stats.csv')
turns = read('turns.csv')
if len(stats) < 2:
    sys.exit('stats.csv has fewer than two rows: the run was too short.')
hours = [float(r['elapsed_s']) / 3600 for r in stats]
xlabel = 'godziny od startu'

# 1. Memory: everything in MB on one axis.
fig, ax = plt.subplots(figsize=(10, 4.8))
memory = [('working_set_mb', 'cały proces (RAM)'), ('private_mb', 'pamięć prywatna'), ('gc_heap_mb', 'obiekty .NET (GC)'),
          ('godot_static_mb', 'Godot'), ('video_mb', 'pamięć grafiki')]
for i, (key, label) in enumerate(memory):
    ys = col(stats, key)
    ax.plot(hours, ys, color=SERIES[i], linewidth=2 if i == 0 else 1.4, label=label)
    ax.annotate(f'{ys[-1]:.0f}', (hours[-1], ys[-1]), xytext=(4, 0), textcoords='offset points', color=MUTED, fontsize=8, va='center')
style(ax, 'Pamięć w czasie', 'MB')
ax.set_xlabel(xlabel, color=MUTED, fontsize=8)
ax.set_ylim(bottom=0)
ax.legend(fontsize=8, frameon=False, loc='upper left')
save(fig, '1_pamiec.png')

# 2. Program performance: small multiples, one measure each.
fig, axes = plt.subplots(3, 2, figsize=(11, 8.5), sharex=True)
panels = [
    (['cpu_pct'], ['procesor'], 'Procesor (cały komputer)', '%'),
    (['turns_per_s'], ['tury na sekundę'], 'Tempo symulacji', 'tury/s'),
    (['turn_ms_avg', 'turn_ms_p95'], ['średnio', '95% tur krótszych niż'], 'Czas tury', 'ms'),
    (['fps'], ['klatki na sekundę'], 'Płynność okna', 'FPS'),
    (['client_ms_avg', 'frame_gap_max_ms'], ['praca okna na klatkę', 'najdłuższa przerwa między klatkami'], 'Klatka okna', 'ms'),
    (['backlog_max'], ['nieodebrane wiadomości'], 'Zaległość okna (limit 256)', 'wiadomości'),
]
for ax, (keys, labels, title, unit) in zip(axes.flat, panels):
    for i, (key, label) in enumerate(zip(keys, labels)):
        ax.plot(hours, col(stats, key), color=SERIES[i], linewidth=1.3, label=label)
    style(ax, title, unit)
    ax.set_ylim(bottom=0)
    if len(keys) > 1:
        ax.legend(fontsize=7, frameon=False)
for ax in axes[-1]:
    ax.set_xlabel(xlabel, color=MUTED, fontsize=8)
save(fig, '2_wydajnosc.png')

# 3. The simulation.
fig, axes = plt.subplots(3, 2, figsize=(11, 8.5), sharex=True)
panels = [
    (['people'], ['ludzie'], 'Ludność świata', 'ludzie'),
    (['settlements'], ['zamieszkane osady'], 'Osady', 'osady'),
    (['civs_alive'], ['żywe cywilizacje'], 'Cywilizacje', 'cywilizacje'),
    (['wars', 'trade', 'non_aggression', 'alliances'], ['wojny', 'handel', 'nieagresja', 'sojusze'], 'Wojny i traktaty w mocy', 'liczba'),
    (['stability_avg'], ['średnia stabilność'], 'Stabilność', '0–100'),
    (['era_max'], ['najdalsza era'], 'Najdalsza era (0 = epoka kamienia)', 'era'),
]
for ax, (keys, labels, title, unit) in zip(axes.flat, panels):
    for i, (key, label) in enumerate(zip(keys, labels)):
        ax.plot(hours, col(stats, key), color=SERIES[i], linewidth=1.3, label=label)
    style(ax, title, unit)
    ax.set_ylim(bottom=0)
    if len(keys) > 1:
        ax.legend(fontsize=7, frameon=False)
for ax in axes[-1]:
    ax.set_xlabel(xlabel, color=MUTED, fontsize=8)
save(fig, '3_symulacja.png')

# 4. Where the turn time goes: phases averaged in 50 slices of the run (7 largest, the rest as "other").
phase_keys = [k for k in (turns[0].keys() if turns else []) if k.endswith('_ms') and k != 'turn_ms']
names = {'world_ms': 'świat', 'perception_ms': 'percepcja', 'decisions_ms': 'decyzje AI', 'commands_ms': 'komendy',
         'movement_ms': 'ruch', 'combat_ms': 'walka', 'economy_ms': 'ekonomia', 'population_ms': 'ludność',
         'state_ms': 'państwo', 'science_ms': 'nauka', 'record_ms': 'zapis'}
if turns:
    totals = {k: sum(float(r[k]) for r in turns) for k in phase_keys}
    top = sorted(phase_keys, key=lambda k: -totals[k])[:7]
    rest = [k for k in phase_keys if k not in top]
    slices = 50
    size = max(1, len(turns) // slices)
    xs, layers = [], {k: [] for k in top + ['other']}
    for start in range(0, len(turns), size):
        part = turns[start:start + size]
        xs.append(float(part[-1]['elapsed_s']) / 3600)
        for k in top:
            layers[k].append(sum(float(r[k]) for r in part) / len(part))
        layers['other'].append(sum(float(r[k]) for r in part for k in rest) / len(part))
    fig, ax = plt.subplots(figsize=(10, 4.8))
    ax.stackplot(xs, [layers[k] for k in top + ['other']], colors=SERIES[:len(top) + 1],
                 labels=[names.get(k, k) for k in top] + ['pozostałe'], edgecolor=SURFACE, linewidth=0.5)
    style(ax, 'Na co idzie czas tury (średnio, fazy)', 'ms')
    ax.set_xlabel(xlabel, color=MUTED, fontsize=8)
    ax.legend(fontsize=8, frameon=False, loc='upper left', ncol=4)
    save(fig, '4_fazy_tury.png')

# 5. Events of the whole run by kind.
kinds = [('ev_war', 'wojny'), ('ev_peace', 'pokoje'), ('ev_treaty', 'traktaty'), ('ev_secession', 'secesje'),
         ('ev_coup', 'zamachy stanu'), ('ev_died_out', 'wymarłe cywilizacje'), ('ev_era', 'nowe ery'),
         ('ev_settlement', 'nowe osady'), ('ev_unrest', 'niepokoje i uspokojenia'), ('ev_epidemic', 'epidemie'),
         ('ev_nature', 'susze, pożary, powodzie'), ('ev_other', 'inne')]
last = stats[-1]
values = [int(last[k]) for k, _ in kinds]
fig, ax = plt.subplots(figsize=(10, 4.8))
ax.barh([label for _, label in kinds][::-1], values[::-1], color=SERIES[0], height=0.6)
for y, v in enumerate(values[::-1]):
    ax.annotate(f'{v:,}'.replace(',', ' '), (v, y), xytext=(4, 0), textcoords='offset points', color=MUTED, fontsize=8, va='center')
ax.set_xscale('symlog')
style(ax, 'Zdarzenia w kronice przez cały bieg (skala logarytmiczna)', '')
ax.grid(axis='x', color=GRID, linewidth=0.6)
ax.grid(axis='y', visible=False)
save(fig, '5_zdarzenia.png')

# Report.
ws = col(stats, 'working_set_mb')
growth = slope_per_hour(hours, ws)
heap_growth = slope_per_hour(hours, col(stats, 'gc_heap_mb'))
turn_ms = [float(r['turn_ms']) for r in turns] or [0]
turn_sorted = sorted(turn_ms)
busy = [r for r in stats if float(r['fps']) > 0]
fps = sum(float(r['fps']) for r in busy) / len(busy) if busy else 0
frames = sum(int(r['frames']) for r in stats)
slow = sum(int(r['slow_frames']) for r in stats)
behind = sum(1 for r in stats if int(r['clients_behind']) > 0)
log_lines = open(os.path.join(RUN, 'log.txt'), encoding='utf-8').read().splitlines() if os.path.exists(os.path.join(RUN, 'log.txt')) else []
errors = [l for l in log_lines if 'error' in l.lower()]
slow_turns = [l for l in log_lines if 'slow turn' in l]
verdict = ('bieg za krótki, żeby ocenić trend (potrzeba co najmniej godziny)' if hours[-1] < 1 else
           'stabilna (rośnie mniej niż 50 MB na godzinę w drugiej połowie biegu)' if growth < 50
           else f'**rośnie o {growth:.0f} MB na godzinę** w drugiej połowie biegu: możliwy wyciek, do sprawdzenia')
people_text = f"{int(stats[-1]['people']):,}".replace(',', ' ')
report = f"""# Raport długiego biegu

Folder: `{os.path.basename(os.path.abspath(RUN))}`

| Co | Wynik |
| --- | --- |
| Czas biegu | {hours[-1]:.2f} h |
| Tury | {stats[-1]['turn']} (rok {stats[-1]['year']}) |
| Czas tury | średnio {sum(turn_ms) / len(turn_ms):.1f} ms, 95% tur poniżej {turn_sorted[int(len(turn_sorted) * 0.95) - 1 if len(turn_sorted) > 1 else 0]:.1f} ms, najdłuższa {turn_sorted[-1]:.0f} ms |
| Pamięć procesu | start {ws[0]:.0f} MB, koniec {ws[-1]:.0f} MB, szczyt {max(ws):.0f} MB |
| Pamięć w drugiej połowie | {verdict}; obiekty .NET {heap_growth:+.0f} MB/h |
| Procesor | średnio {sum(col(stats, 'cpu_pct')) / len(stats):.0f}% całego komputera |
| Okno | średnio {fps:.1f} FPS, {slow} z {frames} klatek wolniejszych niż 30 FPS |
| Okno nie nadążało | w {behind} z {len(stats)} pomiarów |
| Cywilizacje na końcu | {stats[-1]['civs_alive']} żywych z {stats[-1]['civs_total']} |
| Ludność na końcu | {people_text} |
| Wolne tury (ponad 1 s) | {len(slow_turns)} |
| Błędy w logu | {len(errors)} |

Wykresy: `1_pamiec.png`, `2_wydajnosc.png`, `3_symulacja.png`, `4_fazy_tury.png`, `5_zdarzenia.png`.
"""
with open(os.path.join(OUT, 'raport.md'), 'w', encoding='utf-8') as f:
    f.write(report)
print(report)
print('charts in', OUT)
