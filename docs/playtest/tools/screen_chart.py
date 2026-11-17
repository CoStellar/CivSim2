"""What the player's screen shows, minute by minute (a --transcript file of tools/CivSim.Playtest). Usage: screen_chart.py transcript.txt own_civ_name out.png"""
import re, sys, collections
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
path, own, out = sys.argv[1], sys.argv[2], sys.argv[3]
cats = [("przywódcy (śmierć, następca)", r"died at|leads them now|New leaders"),
        ("osadnicy giną w drodze", r"starved on the way"),
        ("osadnicy wyruszają / zakładają", r"settlers"),
        ("ważne: technologia, kontakt, wojna, wioska, era", r"Minute \d+:| learned |met |declared war|made peace|beat back|took over"),
        ("reszta", r".")]
per = collections.defaultdict(collections.Counter)
own_n = tot = 0
for line in open(path):
    m = re.match(r"\[(\d+):(\d+)\] CHRONICLE (.*)", line)
    if not m: continue
    minute, text = int(m.group(1)), m.group(3)
    tot += 1; own_n += own in text
    for name, pat in cats:
        if re.search(pat, text):
            per[minute][name] += 1; break
mins = sorted(per)
fig, ax = plt.subplots(figsize=(10, 5.5))
bottom = [0] * len(mins)
colors = ["#bab0ac", "#e15759", "#f28e2b", "#59a14f", "#76b7b2"]
for (name, _), col in zip(cats, colors):
    ys = [per[m][name] for m in mins]
    ax.bar([m + 1 for m in mins], ys, bottom=bottom, label=name, color=col)
    bottom = [b + y for b, y in zip(bottom, ys)]
ax.set_xlabel("minuta partii"); ax.set_ylabel("wpisy w kronice gracza na minutę")
ax.set_title(f"Co gracz czyta w kronice: {tot} wpisów w 15 min, {100*own_n//max(1,tot)}% o jego ludzie")
ax.legend(fontsize=9); fig.tight_layout(); fig.savefig(out, dpi=130)
print(tot, own_n, {k: sum(per[m][k] for m in mins) for k, _ in cats})
