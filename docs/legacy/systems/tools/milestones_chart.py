#!/usr/bin/env python3
"""Pace chart (stage 6 PR T): minute of each milestone for every civilization, before and after a change.

Usage:
    python3 docs/systems/tools/milestones_chart.py before.csv after.csv out.png [--minutes 15]

Both files come from `CivSim.Headless --batch N --milestones pace.csv`. One row per milestone; each dot is one civilization that
existed from the start; the dashed line is the goal from ADR 0026 rule 2; the right column says how many reached it at all.
"""
import csv
import sys

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402

MILESTONES = [
    ("first_technology", "First technology", 1),
    ("first_village", "First village", 3),
    ("era_1", "Era 1 (Neolithic)", 5),
    ("five_settlements", "Five settlements", None),
    ("first_contact", "First contact", None),
    ("first_war", "First war", None),
]
BEFORE, AFTER = "#2a78d6", "#eb6834"  # categorical slots 1 and 2 of the reference palette
INK, MUTED, GRID = "#1a1a19", "#6b6a63", "#e4e3dd"


def load(path):
    with open(path, newline="", encoding="utf-8") as f:
        return [r for r in csv.DictReader(f) if r["from_start"] == "1"]


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    minutes = 15
    if "--minutes" in sys.argv:
        minutes = int(sys.argv[sys.argv.index("--minutes") + 1])
    before, after, out = load(args[0]), load(args[1]), args[2]

    fig, ax = plt.subplots(figsize=(10, 6.2), dpi=120)
    rows = len(MILESTONES)
    for i, (key, label, goal) in enumerate(MILESTONES):
        y = rows - 1 - i
        for rows_, colour, dy in ((before, BEFORE, 0.17), (after, AFTER, -0.17)):
            values = sorted(float(r[key]) for r in rows_ if r[key])
            # Spread equal minutes a little so every civilization shows.
            jitter = [((k % 5) - 2) * 0.035 for k in range(len(values))]
            ax.scatter(values, [y + dy + j for j in jitter], s=22, color=colour, edgecolors="white", linewidths=0.8, zorder=3)
            if values:
                median = values[len(values) // 2]
                ax.plot([median, median], [y + dy - 0.13, y + dy + 0.13], color=INK, linewidth=2, zorder=4)
            ax.text(minutes + 0.35, y + dy, f"{len(values)}/{len(rows_)}", va="center", fontsize=9, color=MUTED)
        if goal is not None:
            ax.plot([goal, goal], [y - 0.42, y + 0.42], color=INK, linestyle=(0, (3, 3)), linewidth=1, zorder=2)
            ax.text(goal, y + 0.45, f"goal ≤ {goal} min", ha="center", va="bottom", fontsize=8, color=MUTED)

    ax.set_yticks(range(rows))
    ax.set_yticklabels([m[1] for m in reversed(MILESTONES)], fontsize=10, color=INK)
    ax.set_xlim(0, minutes + 1.6)
    ax.set_ylim(-0.6, rows - 0.3)
    ax.set_xticks(range(0, minutes + 1))
    ax.set_xlabel("minute of the game at x1 (dots: one people each; black tick: median; right: how many reached it)", fontsize=9, color=MUTED)
    ax.grid(axis="x", color=GRID, linewidth=0.8)
    ax.set_axisbelow(True)
    for side in ("top", "right", "left"):
        ax.spines[side].set_visible(False)
    ax.spines["bottom"].set_color(GRID)
    ax.tick_params(colors=MUTED, length=0)
    ax.scatter([], [], s=22, color=BEFORE, label="before")
    ax.scatter([], [], s=22, color=AFTER, label="after")
    ax.legend(loc="upper center", bbox_to_anchor=(0.5, 1.08), ncol=2, frameon=False, fontsize=9)
    ax.set_title("When each people reaches its milestones", fontsize=12, color=INK, loc="left", pad=22)
    fig.tight_layout()
    fig.savefig(out)
    print(f"wrote {out}")


if __name__ == "__main__":
    main()
