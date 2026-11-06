#!/usr/bin/env python3
"""Generates the placeholder art of stage 5 (GDD 12.3) in the CivSim 2 palette.

The viewer loads every picture by name from art/export/<layer>/ first (Patryk's pixel art, stage 13) and falls back
to art/placeholder/<layer>/ (these files), so drawing a real picture under the same name replaces a placeholder
without any code change. Run from the repository root:

    python3 art/placeholder/generate.py

Rules kept here (art/README.md): palette colours only, no semi-transparent pixels, light from the top left,
outlines in a darker shade of the fill, civilization colour in the reserved magentas #FF00FF, #C000C0, #800080.
Output is deterministic (fixed seeds), so running it again gives the same files.
"""

import json
import os
import random
import re

from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "art", "placeholder")
W, H = 48, 42

CIV = [(255, 0, 255), (192, 0, 192), (128, 0, 128)]  # light, mid, dark civilization colour


def load_palette():
    cols = []
    with open(os.path.join(ROOT, "art", "palette", "civsim2_palette.gpl"), encoding="utf-8") as f:
        for line in f:
            m = re.match(r"\s*(\d+)\s+(\d+)\s+(\d+)", line)
            if m:
                cols.append(tuple(int(v) for v in m.groups()))
    return cols


PAL = load_palette()


def hexrgb(s):
    s = s.lstrip("#")
    return tuple(int(s[i:i + 2], 16) for i in (0, 2, 4))


def lum(c):
    return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]


def nearest(c):
    return min(PAL, key=lambda p: sum((a - b) ** 2 for a, b in zip(p, c)))


def shade(c, step):
    """Palette colour about `step` steps lighter (>0) or darker (<0) than c, keeping the hue close."""
    c = nearest(c)
    if step == 0:
        return c
    target = lum(c) + 28 * step
    cands = [p for p in PAL if (lum(p) > lum(c)) == (step > 0) and p != c]
    if not cands:
        return c
    return min(cands, key=lambda p: abs(lum(p) - target) * 1.0 + 0.6 * sum(abs(a - b) for a, b in zip(p, c)))


def hex_mask():
    tpl = Image.open(os.path.join(ROOT, "art", "templates", "hex_template_48x42.png")).convert("RGBA")
    return [[tpl.getpixel((x, y))[3] > 0 for x in range(W)] for y in range(H)]


MASK = hex_mask()


def inside(x, y, margin=0):
    if not (0 <= x < W and 0 <= y < H):
        return False
    if not MASK[y][x]:
        return False
    if margin:
        for dx, dy in ((margin, 0), (-margin, 0), (0, margin), (0, -margin)):
            xx, yy = x + dx, y + dy
            if not (0 <= xx < W and 0 <= yy < H) or not MASK[yy][xx]:
                return False
    return True


class Canvas:
    def __init__(self, w, h):
        self.w, self.h = w, h
        self.im = Image.new("RGBA", (w, h), (0, 0, 0, 0))

    def px(self, x, y, c):
        if 0 <= x < self.w and 0 <= y < self.h:
            self.im.putpixel((x, y), tuple(c) + (255,))

    def rect(self, x0, y0, x1, y1, c):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.px(x, y, c)

    def line(self, x0, y0, x1, y1, c, width=1):
        n = max(abs(x1 - x0), abs(y1 - y0), 1)
        for i in range(n + 1):
            x = round(x0 + (x1 - x0) * i / n)
            y = round(y0 + (y1 - y0) * i / n)
            r = width // 2
            for dy in range(-r, width - r):
                for dx in range(-r, width - r):
                    self.px(x + dx, y + dy, c)

    def disc(self, cx, cy, r, c):
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                    self.px(x, y, c)

    def outline(self, c):
        """Darker outline around everything drawn so far (on transparent neighbours)."""
        src = self.im.copy()
        for y in range(self.h):
            for x in range(self.w):
                if src.getpixel((x, y))[3]:
                    continue
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    xx, yy = x + dx, y + dy
                    if 0 <= xx < self.w and 0 <= yy < self.h and src.getpixel((xx, yy))[3]:
                        self.px(x, y, c)
                        break

    def save(self, layer, name):
        d = os.path.join(OUT, layer)
        os.makedirs(d, exist_ok=True)
        self.im.save(os.path.join(d, name + ".png"))


# ---------------------------------------------------------------- biomes (48×42, 3 variants)

def biome_tile(key, color, variant):
    rnd = random.Random(f"{key}-{variant}")
    base = nearest(color)
    light, dark = shade(base, 1), shade(base, -1)
    c = Canvas(W, H)
    for y in range(H):
        for x in range(W):
            if MASK[y][x]:
                c.px(x, y, base)

    def spots(n, col, size=1):
        for _ in range(n):
            x, y = rnd.randrange(3, W - 3), rnd.randrange(3, H - 3)
            if inside(x, y, 2):
                c.rect(x, y, x + size - 1, y + size - 1, col)

    water = key in ("deep_ocean", "shallow_sea", "coast", "lake")
    if water:
        for _ in range(7):
            x, y = rnd.randrange(6, W - 10), rnd.randrange(5, H - 5)
            if inside(x, y, 3) and inside(x + 4, y, 3):
                c.line(x, y, x + 3, y, light)
                c.px(x + 4, y - 1, light)
    elif key in ("deciduous_forest", "taiga", "jungle"):
        crown = shade(base, 1 if key != "taiga" else 0)
        n = {"jungle": 16, "deciduous_forest": 11, "taiga": 12}[key]
        for _ in range(n):
            x, y = rnd.randrange(5, W - 5), rnd.randrange(5, H - 5)
            if not inside(x, y, 3):
                continue
            if key == "taiga":  # small firs
                c.px(x, y - 2, crown)
                c.line(x - 1, y - 1, x + 1, y - 1, crown)
                c.line(x - 2, y, x + 2, y, dark)
                c.px(x, y + 1, shade(base, -2))
            else:
                c.rect(x - 1, y - 1, x + 1, y, crown)
                c.px(x - 1, y - 1, shade(crown, 1))
                c.px(x + 1, y + 1, dark)
                c.px(x, y + 1, dark)
    elif key in ("grassland", "steppe", "savanna"):
        for _ in range(14 if key == "grassland" else 10):
            x, y = rnd.randrange(4, W - 4), rnd.randrange(4, H - 4)
            if inside(x, y, 2):
                c.px(x, y, light)
                c.px(x - 1, y + 1, dark if key == "steppe" else light)
                c.px(x + 1, y + 1, light)
        if key == "savanna":
            for _ in range(3):
                x, y = rnd.randrange(8, W - 8), rnd.randrange(8, H - 8)
                if inside(x, y, 4):
                    tree = nearest((57, 99, 37))
                    c.rect(x - 2, y - 1, x + 2, y - 1, tree)
                    c.px(x, y, shade(tree, -1))
                    c.px(x, y + 1, shade(tree, -1))
    elif key == "desert":
        spots(18, light)
        spots(8, dark)
        for _ in range(2):  # dune line
            x, y = rnd.randrange(8, W - 16), rnd.randrange(10, H - 10)
            if inside(x, y, 3):
                c.line(x, y, x + 5, y - 2, light)
                c.line(x + 5, y - 2, x + 9, y, dark)
    elif key in ("tundra", "ice"):
        spots(16, light)
        spots(8 if key == "tundra" else 4, dark)
    elif key == "swamp":
        pool = nearest((19, 103, 157))
        for _ in range(4):
            x, y = rnd.randrange(8, W - 8), rnd.randrange(8, H - 8)
            if inside(x, y, 4):
                c.rect(x - 2, y, x + 2, y + 1, pool)
                c.px(x - 2, y, shade(pool, 1))
        spots(10, light)
    else:
        spots(12, light)
        spots(8, dark)
    # Light from the top left: a lighter rim along the upper-left edges, darker along the lower right.
    for y in range(H):
        for x in range(W):
            if not MASK[y][x]:
                continue
            ul = (x - 1 < 0 or not MASK[y][x - 1]) or (y - 1 < 0 or not MASK[y - 1][x])
            lr = (x + 1 >= W or not MASK[y][x + 1]) or (y + 1 >= H or not MASK[y + 1][x])
            if ul and y < H // 2:
                c.px(x, y, light)
            elif lr and y >= H // 2:
                c.px(x, y, dark)
    c.save("biome", f"biome_{key}_{variant:02d}")


# ---------------------------------------------------------------- terrain: relief, features, rivers (48×42)

def relief(kind, variant):
    rnd = random.Random(f"{kind}-{variant}")
    c = Canvas(W, H)
    if kind == "hills":
        body = nearest((138, 86, 45))
        for i in range(3):
            cx = 12 + i * 12 + rnd.randrange(-2, 3)
            cy = 26 + rnd.randrange(-4, 3) - (i % 2) * 5
            r = 6 + rnd.randrange(0, 3)
            for y in range(cy - r, cy + 1):
                for x in range(cx - r, cx + r + 1):
                    if (x - cx) ** 2 + ((y - cy) * 1.6) ** 2 <= r * r and inside(x, y, 1):
                        c.px(x, y, shade(body, 1) if x < cx - 1 else body if x < cx + 2 else shade(body, -1))
    else:
        rock = nearest((113, 111, 108))
        snow = nearest((251, 249, 251))
        peaks = [(16, 31, 10), (31, 33, 12)] if variant == 1 else [(13, 32, 9), (25, 33, 13), (36, 32, 8)]
        for px_, base_y, half in peaks:
            top = base_y - int(half * 1.6)
            for y in range(top, base_y + 1):
                w = int((y - top) / 1.6)
                for x in range(px_ - w, px_ + w + 1):
                    if not inside(x, y, 1):
                        continue
                    col = shade(rock, 1) if x <= px_ else shade(rock, -1)
                    if y < top + 4:
                        col = snow if x <= px_ else nearest((169, 162, 160))
                    c.px(x, y, col)
    c.outline(shade(nearest((62, 60, 58)), 0))
    c.save("terrain", f"terrain_{kind}_{variant:02d}")


def feature(key):
    c = Canvas(W, H)
    if key == "oasis":
        water = nearest((63, 167, 201))
        c.disc(21, 25, 6, water)
        c.disc(19, 23, 2, shade(water, 1))
        palm = nearest((51, 88, 33))
        c.line(30, 28, 31, 16, nearest((122, 45, 2)), 2)
        for dx, dy in ((-5, 2), (5, 2), (-4, -1), (4, -1)):
            c.line(31, 15, 31 + dx, 15 + dy, palm, 2)
    elif key == "reef":
        coral = nearest((240, 154, 168))
        rnd = random.Random("reef")
        for _ in range(9):
            x, y = rnd.randrange(10, W - 10), rnd.randrange(10, H - 10)
            if inside(x, y, 4):
                c.rect(x, y, x + 1, y + 1, coral)
                c.px(x, y, shade(coral, 1))
    else:  # ice field
        ice = nearest((228, 241, 247))
        rnd = random.Random("ice")
        for _ in range(6):
            x, y = rnd.randrange(8, W - 12), rnd.randrange(8, H - 10)
            if inside(x, y, 4):
                c.rect(x, y, x + 5, y + 2, ice)
                c.line(x, y, x + 5, y, nearest((251, 249, 251)))
    c.outline(shade(nearest((62, 60, 58)), 0) if key != "reef" else nearest((105, 56, 41)))
    c.save("terrain", f"terrain_{key}_01")


# Corners of the hex in tile pixels (flat top), clockwise from east, like HexLayout.Corners in the viewer.
CORNERS = [(47, 21), (36, 41), (12, 41), (0, 21), (12, 0), (35, 0)]
EDGES = {"n": (4, 5), "ne": (5, 0), "se": (0, 1), "s": (1, 2), "sw": (2, 3), "nw": (3, 4)}


def river(edge, width_name):
    c = Canvas(W, H)
    a, b = EDGES[edge]
    (x0, y0), (x1, y1) = CORNERS[a], CORNERS[b]
    width = 2 if width_name == "stream" else 4
    water = nearest((25, 119, 208))
    # Draw just inside the edge so the neighbouring cell's half of the river meets it.
    cx, cy = 24, 21
    ix0, iy0 = round(x0 + (cx - x0) * 0.06), round(y0 + (cy - y0) * 0.06)
    ix1, iy1 = round(x1 + (cx - x1) * 0.06), round(y1 + (cy - y1) * 0.06)
    c.line(ix0, iy0, ix1, iy1, water, width)
    if width > 2:
        c.line(ix0, iy0, ix1, iy1, shade(water, 1), 1)
    c.save("terrain", f"terrain_river_{edge}_{width_name}")


# ---------------------------------------------------------------- objects: deposits (24×24)

def deposit(key, color):
    c = Canvas(24, 24)
    fill = nearest(color)
    c.disc(11.5, 11.5, 8.5, shade(fill, -1))
    c.disc(11.5, 11.5, 7.0, fill)
    c.disc(9, 9, 2.5, shade(fill, 1))
    # A tiny mark by kind of good: food animals a dot pair, stone and ores a square, plants a stalk.
    mark = shade(fill, -2)
    if key in ("game", "cattle", "sheep", "horses", "fish"):
        c.rect(10, 13, 11, 14, mark)
        c.rect(14, 13, 15, 14, mark)
    elif key in ("wild_grain",):
        c.line(12, 16, 12, 9, mark)
        c.px(11, 10, mark)
        c.px(13, 11, mark)
    else:
        c.rect(11, 12, 14, 15, mark)
    c.outline(nearest((29, 23, 16)))
    c.save("objects", f"deposit_{key}")


# ---------------------------------------------------------------- objects: settlements (48×42, 3 levels × 3 states)

def tent(c, x, y, s, cloth, flag):
    for i in range(s):
        c.line(x - i, y - s + i, x + i, y - s + i, cloth if i < s - 1 else shade(cloth, -1))
    for i in range(s):
        c.px(x - i, y - s + i, shade(cloth, 1))
    c.px(x, y - s - 1, nearest((47, 46, 45)))
    if flag:
        c.rect(x + 1, y - s - 2, x + 3, y - s - 1, CIV[0])
        c.px(x + 3, y - s - 1, CIV[1])


def house(c, x, y, roof, wall, ruined=False):
    c.rect(x - 4, y - 4, x + 4, y, wall)
    c.rect(x - 4, y - 4, x - 4, y, shade(wall, 1))
    c.rect(x - 1, y - 2, x + 1, y, nearest((47, 46, 45)))
    if ruined:
        c.rect(x + 1, y - 4, x + 4, y - 3, (0, 0, 0))
        return
    for i in range(4):
        c.line(x - 5 + i, y - 5 - i, x + 5 - i, y - 5 - i, roof[1] if i else roof[2])
    c.line(x - 5, y - 5, x - 2, y - 8, roof[0])


def settlement(level, state):
    c = Canvas(W, H)
    abandoned, ruins = state == "abandoned", state == "ruins"
    grey = [nearest((147, 138, 130)), nearest((113, 111, 108)), nearest((79, 75, 69))]
    roof = grey if abandoned else CIV
    wall = nearest((204, 126, 87)) if not abandoned else nearest((147, 138, 130))
    if ruins:
        stone = nearest((120, 121, 121))
        rnd = random.Random(f"ruins-{level}")
        n = {"camp": 5, "village": 9, "town": 14}[level]
        for _ in range(n):
            x, y = rnd.randrange(12, 36), rnd.randrange(14, 32)
            c.rect(x, y, x + rnd.randrange(1, 3), y + 1, stone)
            c.px(x, y, shade(stone, 1))
        c.outline(nearest((47, 46, 45)))
        c.save("objects", f"settlement_{level}_{state}")
        return
    if level == "camp":
        cloth = nearest((170, 101, 39)) if not abandoned else nearest((147, 138, 130))
        tent(c, 17, 30, 8, cloth, not abandoned)
        tent(c, 30, 32, 7, cloth, False)
        fire = nearest((236, 39, 30)) if not abandoned else nearest((79, 75, 69))
        c.rect(24, 33, 25, 34, fire)
    elif level == "village":
        for x, y in ((15, 29), (27, 27), (33, 34), (20, 36)):
            house(c, x, y, roof, wall, abandoned and x == 27)
    else:
        wallc = nearest((101, 99, 98))
        for y in range(12, 37):
            for x in range(8, 41):
                if inside(x, y, 2) and not inside(x, y, 5):
                    c.px(x, y, wallc if not abandoned or (x + y) % 5 else (0, 0, 0))
        for x, y in ((17, 22), (29, 21), (14, 32), (24, 31), (34, 31)):
            house(c, x, y, roof, wall, abandoned and x in (29, 24))
    c.outline(nearest((29, 23, 16)))
    c.save("objects", f"settlement_{level}_{state}")


def field():
    c = Canvas(W, H)
    crop = nearest((234, 210, 32))
    soil = nearest((122, 45, 2))
    for y in range(8, 35):
        for x in range(6, 42):
            if inside(x, y, 3):
                c.px(x, y, crop if (y // 2) % 2 == 0 else soil)
                if (y // 2) % 2 == 0 and x % 7 == 0:
                    c.px(x, y, shade(crop, 1))
    c.save("objects", "improvement_field_01")


# ---------------------------------------------------------------- units (32×32, civilization colour)

def person(c, x, y, tunic, carry=None):
    skin = nearest((204, 126, 87))
    c.rect(x - 1, y - 11, x + 1, y - 9, skin)
    c.rect(x - 2, y - 8, x + 2, y - 3, tunic[1])
    c.rect(x - 2, y - 8, x - 2, y - 3, tunic[0])
    c.rect(x + 2, y - 8, x + 2, y - 3, tunic[2])
    c.rect(x - 2, y - 2, x - 1, y, nearest((86, 44, 24)))
    c.rect(x + 1, y - 2, x + 2, y, nearest((86, 44, 24)))
    if carry == "bundle":
        c.rect(x + 3, y - 10, x + 6, y - 6, nearest((170, 101, 39)))
        c.px(x + 3, y - 10, nearest((229, 168, 98)))
    elif carry == "spear":
        c.line(x + 4, y - 14, x + 4, y, nearest((122, 45, 2)))
        c.rect(x + 3, y - 16, x + 5, y - 15, nearest((169, 162, 160)))


def unit(name):
    c = Canvas(32, 32)
    if name == "migrants":
        person(c, 9, 29, CIV, "bundle")
        person(c, 20, 30, CIV, "bundle")
        person(c, 15, 25, CIV)
    else:
        person(c, 14, 29, CIV, "spear")
    c.outline(nearest((29, 23, 16)))
    c.save("units", f"unit_{name}_idle_01")


# ---------------------------------------------------------------- event markers (24×24)

def event(key):
    c = Canvas(24, 24)
    if key == "extraction_reduction":  # cracked, dry ground
        sun = nearest((223, 143, 19))
        c.disc(11.5, 11.5, 6, sun)
        c.disc(10, 10, 2, nearest((234, 210, 32)))
        for a, b in ((11, 1), (11, 21), (1, 11), (21, 11), (4, 4), (18, 18), (4, 18), (18, 4)):
            c.px(a, b, sun)
    elif key in ("movement_restriction", "movement_slowdown"):  # flood waves
        water = nearest((26, 74, 208))
        for y in (7, 12, 17):
            for x in range(3, 21):
                c.px(x, y + (1 if (x // 3) % 2 else 0), water)
            c.line(3, y - 1, 6, y - 1, nearest((63, 167, 201)))
        if key == "movement_restriction":
            c.line(4, 4, 19, 19, nearest((183, 10, 9)), 2)
    elif key == "damage":  # fire: a flame, red outside, orange, yellow core
        for col, top, half in ((nearest((236, 39, 30)), 3, 8), (nearest((223, 143, 19)), 8, 5), (nearest((234, 210, 32)), 13, 2)):
            for y in range(top, 21):
                w = round(half * min(1.0, (y - top + 1) / (21 - top) * 1.6))
                c.line(12 - w, y, 12 + w, y, col)
    elif key == "depopulation":  # sickness: a green drop
        sick = nearest((79, 132, 47))
        c.disc(11.5, 14, 6, sick)
        c.line(11, 3, 11, 9, sick, 3)
        c.disc(9.5, 12, 1.5, nearest((123, 161, 84)))
    else:  # mood drop: grey cloud
        cloud = nearest((124, 137, 142))
        c.disc(8, 12, 4, cloud)
        c.disc(14, 10, 5, cloud)
        c.disc(16, 14, 3, cloud)
    c.outline(nearest((29, 23, 16)))
    c.save("effects", f"event_{key}")


def main():
    with open(os.path.join(ROOT, "data", "biomes.json"), encoding="utf-8") as f:
        biomes = json.load(f)["biomes"]
    with open(os.path.join(ROOT, "data", "resources.json"), encoding="utf-8") as f:
        deposits = json.load(f)["deposits"]
    with open(os.path.join(ROOT, "data", "effects.json"), encoding="utf-8") as f:
        effects = json.load(f)["effects"]
    for b in biomes:
        for v in (1, 2, 3):
            biome_tile(b["key"], hexrgb(b["color"]), v)
    for kind in ("hills", "mountains"):
        for v in (1, 2):
            relief(kind, v)
    for key in ("oasis", "reef", "ice_field"):
        feature(key)
    for edge in EDGES:
        for width in ("stream", "river"):
            river(edge, width)
    for d in deposits:
        deposit(d["key"], hexrgb(d["color"]))
    for level in ("camp", "village", "town"):
        for state in ("active", "abandoned", "ruins"):
            settlement(level, state)
    field()
    for name in ("migrants", "scout"):
        unit(name)
    for e in effects:
        if e["target"] in ("cell", "settlement"):
            event(e["key"])
    print("placeholders written to", os.path.relpath(OUT, ROOT))


if __name__ == "__main__":
    main()
