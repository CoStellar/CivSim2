"""Agency charts from the playtest harness CSV (tools/CivSim.Playtest). Usage: agency_chart.py playtest.csv out_dir"""
import csv, sys, os, statistics as st, collections
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt

src, out = sys.argv[1], sys.argv[2]
os.makedirs(out, exist_ok=True)
rows = list(csv.DictReader(open(src)))
by = {(int(r["seed"]), int(r["civ"]), r["style"], int(r["minute"])): r for r in rows}
styles = ["ai", "absent", "absent_research", "absent_land", "absent_war", "tech_cheapest", "tech_dearest", "farm_rush", "settle_now", "warlord", "random"]
styles = [s for s in styles if any(k[2] == s for k in by)]
keys = sorted({(k[0], k[1]) for k in by})
last = max(k[3] for k in by)

def val(s, c, style, m, f):
    r = by.get((s, c, style, m))
    return None if r is None else float(r[f])

summary = []
for style in styles:
    ratios, techs, sets, vill, alive = [], [], [], 0, 0
    for (s, c) in keys:
        a = val(s, c, "ai", last, "people"); p = val(s, c, style, last, "people")
        if a is None or p is None: continue
        ratios.append((p + 1) / (a + 1))
        techs.append(val(s, c, style, last, "techs") - val(s, c, "ai", last, "techs"))
        sets.append(val(s, c, style, last, "settlements"))
        vill += 1 if val(s, c, style, last, "villages") > 0 else 0
        alive += 1
    summary.append((style, ratios, techs, sets, vill, alive))

# 1. people after the last minute relative to the AI on the same seed and people
fig, ax = plt.subplots(figsize=(11, 5.5))
data = [x[1] for x in summary]
ax.boxplot(data, showfliers=False)
ax.set_xticks(range(1, len(styles) + 1), styles, rotation=30, ha="right")
ax.axhline(1, color="#888", lw=1, ls="--")
ax.set_yscale("log")
ax.set_ylabel(f"ludzie po {last} min / ludzie przy AI (ten sam lud i seed)")
ax.set_title("Czy styl gracza zmienia wynik? (1 = tak samo jak AI; mediana i rozrzut 50 ludów)")
for i, x in enumerate(summary, 1):
    ax.text(i, ax.get_ylim()[1] * 0.85, f"{st.median(x[1]):.2f}", ha="center", fontsize=9)
fig.tight_layout(); fig.savefig(os.path.join(out, "1_sprawczosc_ludzie.png"), dpi=130); plt.close(fig)

# 2. villages and techs
fig, axs = plt.subplots(1, 2, figsize=(12, 5))
axs[0].bar(styles, [100 * x[4] / max(1, x[5]) for x in summary], color="#4e79a7")
axs[0].set_ylabel(f"% ludów z wioską po {last} min"); axs[0].set_title("Osiedlenie (wioska)")
axs[0].tick_params(axis="x", rotation=40)
axs[1].bar(styles, [st.mean(x[2]) for x in summary], color="#f28e2b")
axs[1].axhline(0, color="#888", lw=1)
axs[1].set_ylabel("technologie: różnica do AI (średnio)"); axs[1].set_title("Technologie")
axs[1].tick_params(axis="x", rotation=40)
for a in axs:
    for lab in a.get_xticklabels(): lab.set_ha("right")
fig.tight_layout(); fig.savefig(os.path.join(out, "2_wioski_technologie.png"), dpi=130); plt.close(fig)

# 3. median people per minute
fig, ax = plt.subplots(figsize=(10, 5.5))
for style in ["ai", "absent", "absent_land", "absent_research", "farm_rush", "settle_now", "warlord", "random"]:
    if style not in styles: continue
    ys = [st.median([val(s, c, style, m, "people") for (s, c) in keys if val(s, c, style, m, "people") is not None]) for m in range(1, last + 1)]
    ax.plot(range(1, last + 1), ys, label=style, lw=2.2 if style in ("ai", "absent") else 1.3)
ax.set_xlabel("minuta partii (x1)"); ax.set_ylabel("ludzie ludu gracza (mediana 50 ludów)")
ax.set_title("Ludność minuta po minucie według stylu gracza"); ax.legend(ncol=2)
fig.tight_layout(); fig.savefig(os.path.join(out, "3_ludnosc_w_czasie.png"), dpi=130); plt.close(fig)

print("style | ludzie/AI mediana | lud lepszy niż AI | wioska | tech-AI | osady mediana")
for style, r, t, s_, v, n in summary:
    print(f"{style} | {st.median(r):.2f} | {sum(1 for x in r if x > 1.1)}/{n} | {v}/{n} | {st.mean(t):+.2f} | {st.median(s_):.0f}")
