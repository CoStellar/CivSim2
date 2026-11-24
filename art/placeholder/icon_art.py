"""Icons and portraits of the interface (Patryk, 2026-11-24), in the style of the goods icons (game_art.py, rules in
docs/grafika-plan.md, chapter 8): palette colours only, a 1 px outline in the darkest brown, light from the top left,
the old CivSim texture on surfaces (none in the flat pack), the people's colour only in the three magentas.

- technologies: tech_<key>.png, 32x32, the thing the technology is about (layer techs/);
- branches of the tree: ui/category_<key>.png, 16x16;
- eras: ui/era_<key>.png, 32x32;
- events: ui/event_<key>.png, 32x32;
- veteran choices: ui/veteran_<key>.png, 16x16;
- window buttons: ui/window_<name>.png, 16x16;
- the advisor in the alert strip and the "Strangers" alert: ui/advisor.png and ui/alert_11.png (round badges like the alerts);
- portraits of advisors and thinkers: portraits/advisor_<field>_<nn>.png and portraits/thinker_<rarity>_<nn>.png, 64x92,
  each drawn with its own random looks (skin, hair, beard, age, headwear), clothes in the people's colour shaded with the
  three magentas, the advisor holding the thing of his field, the thinker dressed and equipped by rarity;
- emblems of peoples: emblems/emblem_<era>_<motif>.png, 40x40: a painted board (stone age), a carved stone (neolithic), a
  wooden shield with a bronze rim (bronze age), a banner (iron age) and a heraldic shield (middle ages), each with the
  people's motif in its colour.
"""

import math
import random

import game_art as ga
from game_art import P, tones, ramp, tone, canvas, person, horse, boat, boulder, OUTLINE, CIV, _mark

INK = (29, 23, 16)


def M():
    return ga  # materials are set by init_materials() before drawing


def finish(c, strength=0.8, outline=True):
    if outline:
        c.outline(P(OUTLINE))
    ga.texturize(c.im, seed=sum(c.im.tobytes()[::97]) % 1000, strength=strength)
    return c


def poly(c, pts, col):
    """Fills a polygon (scanline, even-odd)."""
    ys = [p[1] for p in pts]
    for y in range(int(min(ys)), int(max(ys)) + 1):
        xs = []
        for i in range(len(pts)):
            (x0, y0), (x1, y1) = pts[i], pts[(i + 1) % len(pts)]
            if (y0 <= y + 0.5 < y1) or (y1 <= y + 0.5 < y0):
                xs.append(x0 + (y + 0.5 - y0) * (x1 - x0) / (y1 - y0))
        xs.sort()
        for a, b in zip(xs[::2], xs[1::2]):
            for x in range(int(round(a)), int(round(b))):
                c.px(x, y, col)


def shaded_disc(c, cx, cy, r, rgb):
    rr = ramp(rgb)
    for y in range(int(cy - r) - 1, int(cy + r) + 2):
        for x in range(int(cx - r) - 1, int(cx + r) + 2):
            u, v = (x - cx) / r, (y - cy) / r
            d = u * u + v * v
            if d <= 1:
                c.px(x, y, ga.lit(rr, u, v, math.sqrt(1 - d)))


def civ_px(c, x, y, k):
    c.px(x, y, CIV[k])
    _mark(c, x, y)


# ------------------------------------------------------------------------------------------------ small things at 32 px

def wheat(c, x, y, h=12, col=None):
    t = ga.THATCH if col is None else tones(col)
    c.line(x, y, x, y - h, t[2])
    for k in range(0, 6, 2):
        c.px(x - 1, y - h + 1 + k, t[0])
        c.px(x + 1, y - h + 2 + k, t[1])
    c.px(x, y - h - 1, t[0])


def stick(c, x0, y0, x1, y1, w=1):
    c.line(x0, y0, x1, y1, ga.WOOD[1], w)


def blade(c, x0, y0, x1, y1, metal, w=2):
    c.line(x0, y0, x1, y1, metal[1], w)
    c.line(x0, y0, x1, y1, metal[0], 1)


def axe(c, x, y, metal, flint=False):
    stick(c, x, y, x + 12, y - 16, 2)
    hx, hy = x + 10, y - 17
    if flint:
        poly(c, [(hx - 2, hy - 3), (hx + 6, hy - 1), (hx + 4, hy + 5), (hx - 3, hy + 3)], (47, 46, 45))
        c.px(hx + 1, hy, P((101, 99, 98)))
    else:
        poly(c, [(hx - 1, hy - 3), (hx + 6, hy - 5), (hx + 7, hy + 4), (hx - 1, hy + 2)], metal[1])
        c.line(hx + 6, hy - 4, hx + 7, hy + 3, metal[0])


def hammer(c, x, y, metal):
    stick(c, x, y, x + 9, y - 14, 2)
    c.rect(x + 4, y - 20, x + 16, y - 15, metal[1])
    c.line(x + 4, y - 20, x + 16, y - 20, metal[0])
    c.line(x + 16, y - 20, x + 16, y - 15, metal[2])


def sword(c, x, y, metal, length=18):
    blade(c, x, y, x + length, y - length, metal)
    c.line(x + 1, y - 6, x + 6, y - 1, P((223, 143, 19)), 2)
    c.line(x - 2, y + 2, x + 1, y - 1, ga.WOOD[2], 2)


def spear(c, x, y, metal, length=22, up=True):
    stick(c, x, y, x + length * 0.7, y - length * 0.7)
    tx, ty = int(x + length * 0.7), int(y - length * 0.7)
    poly(c, [(tx - 1, ty + 1), (tx + 4, ty - 4), (tx + 1, ty + 2)], metal[1])
    c.line(tx, ty, tx + 3, ty - 3, metal[0])


def bow(c, x, y, h=22):
    for k in range(h + 1):
        t = k / h
        c.px(int(x + 6 * math.sin(math.pi * t)), y - k, ga.WOOD[1])
        c.px(int(x + 6 * math.sin(math.pi * t)) + 1, y - k, ga.WOOD[2])
    c.line(x, y, x, y - h, P((228, 241, 247)))


def arrow(c, x0, y0, x1, y1):
    c.line(x0, y0, x1, y1, ga.WOOD[0])
    c.px(x1, y1, ga.IRON[0])
    c.px(x1 - 1, y1, ga.IRON[1])
    c.px(x0, y0, P((251, 249, 251)))
    c.px(x0 + 1, y0, P((251, 249, 251)))


def round_shield(c, cx, cy, r, face, rim=None, boss=None):
    shaded_disc(c, cx, cy, r, face)
    if rim is not None:
        rr = ramp(rim)
        for y in range(int(cy - r) - 1, int(cy + r) + 2):
            for x in range(int(cx - r) - 1, int(cx + r) + 2):
                d = math.hypot(x - cx, y - cy)
                if r - 1.6 < d <= r:
                    c.px(x, y, rr[3] if (x - cx) + (y - cy) < 0 else rr[1])
    if boss is not None:
        shaded_disc(c, cx, cy, max(1.5, r / 4), boss)


def house(c, x0, x1, base, wall_h, roof, wall=None, door=True):
    t = wall or ga.WOOD
    c.rect(x0, base - wall_h, x1, base, t[1])
    c.line(x0, base - wall_h, x0, base, t[0])
    c.line(x1, base - wall_h, x1, base, t[2])
    mid = (x0 + x1) / 2
    h = (x1 - x0) // 2 + 2
    for k in range(h):
        half = (x1 - x0) / 2 + 2 - k
        l, r = int(mid - half), int(mid + half)
        c.line(l, base - wall_h - k, int(mid), base - wall_h - k, roof[0])
        c.line(int(mid) + 1, base - wall_h - k, r, base - wall_h - k, roof[2] if k else roof[1])
    if door:
        dx = int(mid)
        c.rect(dx - 1, base - 4, dx + 1, base, CIV[1])
        for yy in range(base - 4, base + 1):
            for xx in range(dx - 1, dx + 2):
                _mark(c, xx, yy)


def scroll(c, x0, y0, x1, y1, lines=True):
    paper = tones((242, 194, 121))
    c.rect(x0, y0, x1, y1, paper[1])
    c.line(x0, y0, x1, y0, paper[0])
    c.rect(x0 - 1, y0 - 1, x0 + 1, y1 + 1, paper[2])
    c.rect(x1 - 1, y0 - 1, x1 + 1, y1 + 1, paper[2])
    if lines:
        for y in range(y0 + 3, y1 - 1, 3):
            c.line(x0 + 3, y, x1 - 4 - (y % 4), y, P(INK))


def book(c, x0, y0, x1, y1, cover=(122, 45, 2)):
    t = tones(cover)
    c.rect(x0, y0, x1, y1, t[1])
    c.line(x0, y0, x1, y0, t[0])
    c.rect(x0 + 2, y1 - 2, x1, y1, P((251, 249, 251)))
    c.line(x0, y0, x0, y1, t[2])
    c.rect(x0 + 4, y0 + 3, x1 - 3, y0 + 4, P((223, 143, 19)))


def tablet(c, x0, y0, x1, y1, rgb=(147, 138, 130), marks=True):
    t = ramp(rgb)
    c.rect(x0, y0 + 2, x1, y1, t[2])
    c.disc((x0 + x1) / 2, y0 + 3, (x1 - x0) / 2, t[2])
    c.line(x0, y0 + 3, x0, y1, t[3])
    c.line(x1, y0 + 3, x1, y1, t[1])
    if marks:
        for y in range(y0 + 4, y1 - 1, 3):
            for x in range(x0 + 2, x1 - 1, 2):
                if (x * 7 + y * 3) % 5:
                    c.px(x, y, t[0])


def wheel(c, cx, cy, r):
    w = ga.WOOD
    for a in range(0, 360, 4):
        for rr in (r, r - 1):
            c.px(int(cx + rr * math.cos(math.radians(a))), int(cy + rr * math.sin(math.radians(a))), w[1] if a < 180 else w[2])
    for a in range(0, 360, 60):
        c.line(cx, cy, int(cx + (r - 1) * math.cos(math.radians(a))), int(cy + (r - 1) * math.sin(math.radians(a))), w[0])
    c.disc(cx, cy, 1.5, ga.IRON[1])


def flame(c, cx, base, h=10, wide=1.0):
    for k in range(h):
        w = 4.5 * wide * math.sin(math.pi * (0.18 + 0.82 * k / h)) * (0.85 + 0.15 * math.sin(k * 1.3))
        col = P((236, 39, 30)) if k < h * 0.5 else P((223, 143, 19))
        c.line(int(cx - w), base - k, int(cx + w), base - k, col)
    c.disc(cx, base - 3, 1.8, P((234, 210, 32)))


def logs(c, cx, base):
    c.line(cx - 7, base, cx + 7, base - 2, ga.WOOD[2], 2)
    c.line(cx - 7, base - 2, cx + 7, base, ga.WOOD[1], 2)


def leaf(c, cx, cy, ang, length=6, col=(79, 132, 47)):
    t = tones(col)
    for k in range(length):
        x = cx + k * math.cos(ang)
        y = cy + k * math.sin(ang)
        w = math.sin(math.pi * k / length) * 2
        c.disc(x, y, w, t[1])
    c.line(cx, cy, int(cx + length * math.cos(ang)), int(cy + length * math.sin(ang)), t[2])


def animal(c, x, y, kind):
    """Side view standing on y: cow, sheep, deer, horse."""
    if kind == "horse":
        horse(c, x, y, rider_item=None)
        return
    body = {"cow": tones((138, 86, 45)), "sheep": tones((251, 249, 251)), "deer": tones((170, 101, 39))}[kind]
    if kind == "sheep":
        for bx in range(x - 6, x + 5, 3):
            c.disc(bx, y - 7, 3, body[1])
        c.disc(x - 3, y - 9, 2, body[0])
        c.rect(x + 5, y - 10, x + 7, y - 7, P((47, 46, 45)))
        legs = P((47, 46, 45))
    else:
        c.rect(x - 7, y - 9, x + 4, y - 5, body[1])
        c.line(x - 7, y - 9, x + 4, y - 9, body[0])
        c.rect(x + 4, y - 12, x + 7, y - 8, body[1])
        if kind == "cow":
            c.rect(x - 4, y - 8, x - 2, y - 6, P((251, 249, 251)))
            c.px(x + 5, y - 13, P((251, 249, 251)))
        else:
            c.line(x + 5, y - 13, x + 4, y - 17, body[2])
            c.line(x + 7, y - 13, x + 8, y - 17, body[2])
            c.px(x + 3, y - 16, body[2])
            c.px(x + 9, y - 16, body[2])
        legs = body[2]
    for lx in (x - 6, x - 3, x + 1, x + 3):
        c.line(lx, y - 4, lx, y, legs)


def fish(c, cx, cy, col=(63, 167, 201)):
    t = tones(col)
    for x in range(-6, 5):
        h = int(3 * math.cos(x / 6 * math.pi / 2))
        c.line(cx + x, cy - h, cx + x, cy + h, t[1] if x < 0 else t[2])
    c.line(cx - 6, cy - 2, cx - 6, cy + 2, t[0])
    poly(c, [(cx + 4, cy), (cx + 8, cy - 4), (cx + 8, cy + 4)], t[1])
    c.px(cx - 4, cy - 1, P(INK))


def water(c, x0, x1, y):
    w = tones((25, 119, 208))
    for x in range(x0, x1 + 1):
        c.px(x, y + (1 if (x // 3) % 2 else 0), w[0])
        c.px(x, y + 1 + (1 if (x // 3) % 2 else 0), w[1])


def ground(c, x0, x1, y, col=(79, 132, 47)):
    t = tones(col)
    c.rect(x0, y, x1, y + 2, t[1])
    c.line(x0, y, x1, y, t[0])


def rope(c, x0, y0, x1, y1):
    n = max(abs(x1 - x0), abs(y1 - y0))
    for i in range(n + 1):
        x = round(x0 + (x1 - x0) * i / n)
        y = round(y0 + (y1 - y0) * i / n)
        c.px(x, y, P((228, 191, 68)) if i % 2 else P((170, 101, 39)))


def mortar_bowl(c, cx, base):
    t = tones((147, 138, 130))
    for k in range(6):
        c.line(cx - 7 + k, base - k, cx + 7 - k, base - k, t[1] if k else t[2])
    c.line(cx - 7, base - 5, cx + 7, base - 5, t[0])


# ------------------------------------------------------------------------------------------------ technologies

def tech(key):
    c = canvas(32, 32)
    W, I, S, H = ga.WOOD, ga.IRON, ga.STONE, ga.HIDE
    bronze = tones((223, 143, 19))
    steel = tones((185, 216, 230))
    copper = tones((201, 113, 21))
    if key == "foraging":  # a basket of berries and a leaf
        t = tones((170, 101, 39))
        for k in range(8):
            c.line(8 + k // 2, 18 + k, 24 - k // 2, 18 + k, t[1] if k % 2 else t[0])
        for x, y in ((11, 16), (15, 14), (19, 16), (14, 18), (18, 18), (22, 17)):
            shaded_disc(c, x, y, 2.4, (183, 10, 9))
        leaf(c, 21, 12, -0.6, 7)
    elif key == "dwellings":
        house(c, 8, 24, 28, 7, ga.ROOFS["neolithic"], wall=ga.MUD)
    elif key == "stockpiling":  # a heap of sacks and logs
        for x, y in ((9, 26), (17, 26), (13, 19)):
            c.rect(x - 4, y - 6, x + 4, y, ga.THATCH[1])
            c.line(x - 4, y - 6, x + 4, y - 6, ga.THATCH[0])
            c.px(x, y - 7, P((86, 44, 24)))
        for y in (27, 23):
            c.rect(20, y - 3, 29, y, W[1])
            c.disc(20, y - 1.5, 1.8, ga.THATCH[1])
    elif key == "carrying":  # a figure with a bundle on a pole
        person(c, 14, 29, CIV, "bundle")
        stick(c, 6, 14, 24, 12)
        c.rect(22, 13, 27, 20, H[1])
    elif key == "scouting":  # an eye over a hill
        boulder(c, 16, 26, 13, 6, (79, 132, 47), seed=4)
        t = tones((251, 249, 251))
        poly(c, [(5, 12), (16, 5), (27, 12), (16, 18)], t[1])
        shaded_disc(c, 16, 11.5, 4, (19, 103, 157))
        c.disc(16, 11.5, 1.4, P(INK))
        c.px(15, 10, P((251, 249, 251)))
    elif key == "migration":  # footsteps and a staff walker
        person(c, 12, 28, CIV, "staff")
        for x, y in ((20, 28), (24, 24), (22, 20), (26, 16)):
            c.rect(x, y, x + 1, y + 2, P((86, 44, 24)))
    elif key == "fire":
        logs(c, 16, 28)
        flame(c, 16, 26, 20, 1.7)
        c.disc(16, 22, 2.5, P((234, 210, 32)))
    elif key == "stone_tools":  # a hand axe and a flint scraper
        poly(c, [(4, 27), (8, 9), (15, 4), (18, 24)], P((101, 99, 98)))
        poly(c, [(7, 23), (10, 10), (14, 7)], P((147, 138, 130)))
        poly(c, [(18, 28), (22, 16), (29, 20), (28, 28)], P((47, 46, 45)))
        c.px(22, 19, P((120, 121, 121)))
    elif key == "hunting":
        animal(c, 13, 29, "deer")
        arrow(c, 30, 4, 20, 14)
    elif key == "shelters":
        ga.tent(c, 16, 28, 22, 18, H)
    elif key == "herbal_lore":  # herbs over a stone mortar
        for a, x in ((-2.0, 12), (-1.6, 16), (-1.1, 20)):
            leaf(c, x, 22, a, 9)
        mortar_bowl(c, 16, 28)
    elif key == "sewing":  # needle and thread over hide
        c.rect(6, 12, 26, 26, H[1])
        c.line(6, 12, 26, 12, H[0])
        for x in range(8, 25, 3):
            c.px(x, 19, P((242, 194, 121)))
        c.line(9, 26, 26, 6, I[0])
        c.px(26, 6, P((251, 249, 251)))
    elif key == "flint_weapons":  # a flint spear point and knife
        stick(c, 3, 29, 20, 12, 2)
        poly(c, [(17, 13), (27, 3), (23, 15)], P((47, 46, 45)))
        c.px(22, 9, P((120, 121, 121)))
        poly(c, [(17, 29), (29, 15), (26, 25)], P((47, 46, 45)))
        c.px(24, 17, P((101, 99, 98)))
    elif key == "agriculture":
        ground(c, 3, 29, 27, (122, 45, 2))
        for x in range(6, 28, 4):
            wheat(c, x, 26, 12 + (x % 3))
    elif key == "pottery":
        c.disc(16, 20, 8, ga.MUD[1])
        c.rect(12, 8, 20, 12, ga.MUD[1])
        c.line(11, 8, 21, 8, ga.MUD[2])
        c.line(9, 19, 23, 19, P((105, 56, 41)))
        c.disc(13, 17, 2, ga.MUD[0])
    elif key == "animal_husbandry":
        stick(c, 3, 29, 3, 15)
        stick(c, 29, 29, 29, 15)
        c.line(3, 18, 29, 18, W[1])
        c.line(3, 24, 29, 24, W[1])
        animal(c, 15, 29, "cow")
    elif key == "masonry":  # stacked cut stones and a chisel
        for row, (y, n) in enumerate(((27, 3), (21, 2))):
            for k in range(n):
                x = 4 + k * 8 + row * 4
                c.rect(x, y - 5, x + 7, y, S[1])
                c.line(x, y - 5, x + 7, y - 5, S[0])
                c.line(x + 7, y - 5, x + 7, y, S[2])
        c.line(22, 14, 28, 4, I[1], 2)
        c.rect(19, 13, 22, 16, W[1])
    elif key == "weaving":  # a loom with cloth
        stick(c, 6, 28, 6, 6, 2)
        stick(c, 26, 28, 26, 6, 2)
        c.line(5, 7, 27, 7, W[0], 2)
        cl = tones((242, 194, 121))
        c.rect(8, 9, 24, 20, cl[1])
        for x in range(9, 24, 2):
            c.line(x, 9, x, 20, cl[2])
        for x in range(9, 24, 2):
            c.line(x, 21, x, 26, P((228, 241, 247)))
    elif key == "irrigation":
        ground(c, 2, 30, 22, (79, 132, 47))
        w = tones((25, 119, 208))
        c.rect(2, 24, 30, 27, w[1])
        c.line(2, 24, 30, 24, w[0])
        c.rect(2, 23, 30, 23, P((105, 56, 41)))
        c.rect(2, 28, 30, 28, P((105, 56, 41)))
        for x in (7, 14, 21, 27):
            wheat(c, x, 21, 11)
    elif key == "barter":  # two hands swapping: pot for hide
        c.disc(10, 19, 5, ga.MUD[1])
        c.rect(8, 12, 12, 14, ga.MUD[1])
        c.rect(18, 14, 28, 24, H[1])
        c.line(18, 14, 28, 14, H[0])
        c.line(9, 6, 23, 6, P((251, 249, 251)))
        c.px(22, 5, P((251, 249, 251)))
        c.px(22, 7, P((251, 249, 251)))
        c.line(9, 28, 23, 28, P((251, 249, 251)))
        c.px(10, 27, P((251, 249, 251)))
        c.px(10, 29, P((251, 249, 251)))
    elif key == "palisade":
        for x in range(3, 29, 4):
            c.rect(x, 9, x + 3, 28, W[1])
            c.line(x, 9, x, 28, W[0])
            poly(c, [(x, 9), (x + 2, 5), (x + 4, 9)], W[1])
        c.line(3, 15, 30, 15, W[2])
        c.line(3, 23, 30, 23, W[2])
    elif key == "prospecting":  # a pick over a rock with ore
        boulder(c, 17, 25, 11, 7, (120, 121, 121), seed=7)
        for x, y in ((12, 23), (19, 22), (22, 26), (15, 27)):
            c.rect(x, y, x + 1, y + 1, copper[1])
        c.line(6, 4, 18, 18, W[1], 2)
        c.line(3, 9, 12, 2, I[0], 2)
    elif key == "copper_working":
        for k, y in enumerate((25, 19)):
            c.rect(6 + k * 4, y, 25 - k * 4, y + 5, copper[1])
            c.line(6 + k * 4, y, 25 - k * 4, y, copper[0])
            c.line(25 - k * 4, y, 25 - k * 4, y + 5, copper[2])
        hammer(c, 14, 18, I)
    elif key == "fishing":
        water(c, 2, 30, 25)
        fish(c, 14, 15)
        c.line(22, 2, 28, 8, W[1])
        c.line(28, 8, 28, 13, P((228, 241, 247)))
        c.px(27, 13, I[0])
    elif key == "rafts":
        water(c, 1, 31, 27)
        for x in range(6, 26, 3):
            c.rect(x, 21, x + 2, 25, W[1])
            c.line(x, 21, x + 2, 21, W[0])
        c.line(6, 23, 27, 23, P((228, 191, 68)))
        person(c, 15, 21, CIV, "staff")
    elif key == "polished_stone":
        poly(c, [(8, 26), (12, 8), (22, 6), (24, 24)], P((101, 99, 98)))
        poly(c, [(11, 22), (14, 10), (19, 9)], P((147, 138, 130)))
        c.px(15, 12, P((251, 249, 251)))
        c.px(16, 11, P((251, 249, 251)))
    elif key == "hide_shields":
        round_shield(c, 16, 16, 12, (170, 101, 39), rim=(105, 56, 41), boss=(138, 86, 45))
        for a in range(0, 360, 45):
            c.px(int(16 + 9 * math.cos(math.radians(a))), int(16 + 9 * math.sin(math.radians(a))), P((242, 194, 121)))
    elif key == "hafted_weapons":  # a stone head bound to a haft
        stick(c, 6, 29, 22, 6, 2)
        boulder(c, 22, 8, 6, 4, (120, 121, 121), seed=3, flat_bottom=False)
        rope(c, 18, 9, 22, 12)
        rope(c, 19, 8, 23, 11)
    elif key == "bronze_working":
        for k, y in enumerate((25, 19)):
            c.rect(6 + k * 4, y, 25 - k * 4, y + 5, bronze[1])
            c.line(6 + k * 4, y, 25 - k * 4, y, bronze[0])
            c.line(25 - k * 4, y, 25 - k * 4, y + 5, bronze[2])
        flame(c, 16, 15, 9)
    elif key == "bronze_weapons":
        sword(c, 5, 27, bronze, 19)
        spear(c, 12, 30, bronze, 22)
    elif key == "the_wheel":
        wheel(c, 16, 16, 12)
    elif key == "writing":  # a clay tablet with a stylus
        tablet(c, 6, 4, 22, 28, (204, 126, 87))
        c.line(20, 26, 29, 10, W[1], 2)
    elif key == "horseback_riding":
        horse(c, 15, 30, rider_item="spear")
    elif key == "sailing":
        water(c, 1, 31, 28)
        boat(c, 14, 27, 12, sails=1)
    elif key == "currency":
        for x, y in ((11, 22), (20, 20), (15, 14)):
            shaded_disc(c, x, y, 6, (223, 143, 19))
            c.line(x - 1, y - 2, x - 1, y + 2, bronze[2])
    elif key == "code_of_laws":  # a stone stele with lines
        tablet(c, 8, 2, 24, 29, (169, 162, 160))
        for y in range(9, 26, 3):
            c.line(11, y, 21, y, P((79, 75, 69)))
    elif key == "archery":
        bow(c, 8, 28, 24)
        arrow(c, 4, 16, 28, 16)
    elif key == "barracks":  # crossed spears over a shield
        spear(c, 3, 29, I, 30)
        c.line(29, 29, 8, 8, W[1])
        poly(c, [(6, 9), (9, 4), (10, 9)], I[1])
        round_shield(c, 16, 19, 8, CIV[1], rim=(105, 56, 41))
    elif key == "stone_walls":
        for row in range(4):
            for k in range(4):
                x = 1 + k * 8 - (row % 2) * 4
                c.rect(max(1, x), 12 + row * 5, min(30, x + 7), 16 + row * 5, S[1])
                c.line(max(1, x), 12 + row * 5, min(30, x + 7), 12 + row * 5, S[0])
        for x in range(1, 30, 6):
            c.rect(x, 7, x + 3, 11, S[1])
    elif key == "bronze_age_arms":  # helmet and armour of bronze
        c.rect(9, 15, 23, 29, bronze[1])
        c.line(9, 15, 9, 29, bronze[0])
        c.line(23, 15, 23, 29, bronze[2])
        c.disc(16, 9, 6, bronze[1])
        c.rect(10, 9, 22, 12, bronze[1])
        c.rect(13, 10, 19, 12, P(INK))
        c.line(16, 1, 16, 4, CIV[0])
    elif key == "bronze_tools":  # a bronze axe and a chisel
        stick(c, 4, 29, 18, 7, 2)
        poly(c, [(14, 6), (24, 2), (26, 13), (16, 12)], bronze[1])
        c.line(24, 3, 26, 12, bronze[0])
        c.line(20, 29, 28, 17, bronze[1], 2)
        c.rect(26, 14, 29, 18, W[1])
    elif key == "iron_working":  # anvil with a glowing bar
        c.rect(6, 17, 26, 20, I[1])
        c.line(6, 17, 26, 17, I[0])
        c.rect(12, 21, 20, 25, I[2])
        c.rect(9, 26, 23, 28, I[1])
        c.rect(10, 13, 22, 15, P((236, 39, 30)))
        c.line(10, 13, 22, 13, P((234, 210, 32)))
    elif key == "iron_weapons":
        sword(c, 5, 27, I, 20)
        axe(c, 12, 30, I)
    elif key == "mathematics":  # a compass over a triangle
        poly(c, [(4, 28), (28, 28), (16, 10)], P((242, 194, 121)))
        c.line(4, 28, 16, 10, P(INK))
        c.line(16, 10, 28, 28, P(INK))
        c.line(4, 28, 28, 28, P(INK))
        c.line(16, 3, 9, 22, bronze[1], 2)
        c.line(16, 3, 23, 22, bronze[1], 2)
        c.disc(16, 3, 2, bronze[0])
    elif key == "construction":  # a crane lifting a stone
        stick(c, 8, 29, 8, 4, 2)
        stick(c, 8, 5, 27, 5, 2)
        c.line(8, 14, 16, 5, W[2])
        c.line(25, 6, 25, 17, P((228, 191, 68)))
        c.rect(21, 18, 29, 24, S[1])
        c.line(21, 18, 29, 18, S[0])
    elif key == "civil_service":  # a scroll with a seal
        scroll(c, 5, 8, 26, 24)
        shaded_disc(c, 22, 23, 4, (183, 10, 9))
        c.px(21, 22, P((236, 39, 30)))
    elif key == "roads":
        t = tones((147, 138, 130))
        poly(c, [(12, 4), (20, 4), (30, 30), (2, 30)], t[2])
        for y in range(6, 30, 4):
            half = 4 + (y - 4) * 0.5
            for x in range(int(16 - half), int(16 + half), 4):
                c.rect(x + (y // 4 % 2) * 2, y, x + 2 + (y // 4 % 2) * 2, y + 2, t[1])
    elif key == "engineering":  # an arch of stone
        for a in range(0, 181, 3):
            for r in range(8, 13):
                x = 16 + r * math.cos(math.radians(a))
                y = 22 - r * math.sin(math.radians(a))
                c.px(int(x), int(y), S[1] if (a // 18) % 2 else S[0])
        c.rect(3, 22, 8, 29, S[1])
        c.rect(24, 22, 29, 29, S[1])
        c.rect(14, 8, 18, 11, S[0])
    elif key == "medicine":  # a bowl of herbs and a bandage cross
        c.rect(12, 3, 20, 19, P((251, 249, 251)))
        c.rect(5, 7, 27, 15, P((251, 249, 251)))
        c.rect(14, 5, 18, 17, P((183, 10, 9)))
        c.rect(7, 9, 25, 13, P((183, 10, 9)))
        mortar_bowl(c, 16, 29)
        leaf(c, 12, 23, -1.9, 5)
        leaf(c, 20, 23, -1.2, 5)
    elif key == "philosophy":  # an oil lamp and a scroll
        scroll(c, 3, 17, 18, 28)
        t = tones((204, 126, 87))
        poly(c, [(17, 22), (29, 22), (26, 27), (19, 27)], t[1])
        c.line(17, 22, 29, 22, t[0])
        flame(c, 27, 21, 7)
    elif key == "shipbuilding":  # a hull on its frame
        for k in range(6):
            c.line(4 + k, 22 - k, 28 - k, 22 - k, W[1] if k % 2 else W[0])
        for x in range(8, 26, 4):
            c.line(x, 16, x, 6, W[2])
        c.line(6, 26, 26, 26, W[2])
        hammer(c, 18, 29, I)
    elif key == "brick_making":
        for row in range(3):
            for k in range(2):
                x = 4 + k * 12 + (row % 2) * 5
                c.rect(x, 9 + row * 6, x + 10, 13 + row * 6, ga.BRICK[1])
                c.line(x, 9 + row * 6, x + 10, 9 + row * 6, ga.BRICK[0])
        flame(c, 26, 29, 6)
    elif key == "shepherding":
        animal(c, 13, 29, "sheep")
        c.line(26, 29, 26, 8, W[1])
        for a in range(0, 200, 20):
            c.px(int(23 + 3 * math.cos(math.radians(a))), int(8 - 3 * math.sin(math.radians(a))), W[1])
    elif key == "iron_age_arms":
        c.rect(9, 12, 23, 28, I[1])
        c.line(9, 12, 9, 28, I[0])
        c.line(23, 12, 23, 28, I[2])
        for y in range(14, 28, 3):
            c.line(10, y, 22, y, I[2])
        c.rect(14, 12, 18, 14, I[2])
        round_shield(c, 24, 22, 6, CIV[1], rim=(120, 121, 121), boss=(120, 121, 121))
    elif key == "iron_tools":
        hammer(c, 4, 28, I)
        c.line(14, 30, 26, 10, W[1], 2)
        poly(c, [(22, 10), (30, 6), (29, 12)], I[1])
    elif key == "feudalism":  # a crown over a castle tower
        c.rect(10, 14, 22, 29, S[1])
        c.line(10, 14, 10, 29, S[0])
        for x in range(10, 23, 4):
            c.rect(x, 11, x + 1, 13, S[1])
        c.rect(14, 22, 17, 29, P(INK))
        gold = tones((223, 143, 19))
        c.rect(9, 6, 23, 9, gold[1])
        for x in (9, 13, 16, 19, 23):
            c.rect(x, 3, x, 6, gold[0])
        c.px(16, 7, P((183, 10, 9)))
    elif key == "chivalry":
        horse(c, 15, 30, rider_item="lance", armoured=True)
    elif key == "steel":
        for k, y in enumerate((25, 19)):
            c.rect(6 + k * 4, y, 25 - k * 4, y + 5, steel[1])
            c.line(6 + k * 4, y, 25 - k * 4, y, steel[0])
            c.line(25 - k * 4, y, 25 - k * 4, y + 5, steel[2])
        sword(c, 12, 16, steel, 14)
    elif key == "castles":
        c.rect(5, 14, 27, 29, S[1])
        c.line(5, 14, 27, 14, S[0])
        for x in (3, 23):
            c.rect(x, 8, x + 6, 29, S[1])
            c.line(x, 8, x, 29, S[0])
            for xx in range(x, x + 7, 3):
                c.rect(xx, 5, xx + 1, 7, S[1])
        c.rect(13, 21, 19, 29, P(INK))
        ga.flag(c, 16, 13, 8)
    elif key == "heavy_plough":
        ground(c, 1, 31, 26, (122, 45, 2))
        for x in range(2, 31, 4):
            c.px(x, 26, P((86, 44, 24)))
        c.line(4, 14, 22, 22, W[1], 2)
        poly(c, [(20, 21), (28, 25), (24, 28), (18, 25)], I[1])
        c.line(8, 10, 12, 18, W[1], 2)
    elif key == "crossbows":
        c.line(6, 26, 24, 8, W[1], 3)
        for k in range(-9, 10):
            c.px(int(19 + k * 0.7) + (0 if abs(k) > 6 else 1), int(13 + k * 0.7) - (0 if abs(k) > 6 else 1), I[0])
        c.line(13, 7, 25, 19, I[1])
        arrow(c, 8, 22, 26, 6)
    elif key == "guilds":  # a hanging sign with a hammer
        c.line(4, 4, 28, 4, I[1], 2)
        c.line(8, 4, 8, 9, I[0])
        c.line(24, 4, 24, 9, I[0])
        c.rect(5, 9, 27, 27, W[1])
        c.line(5, 9, 27, 9, W[0])
        c.rect(7, 11, 25, 25, P((242, 194, 121)))
        hammer(c, 9, 25, I)
    elif key == "mills":  # windmill sails over a mill
        c.rect(11, 18, 21, 29, ga.MUD[1])
        poly(c, [(10, 18), (16, 12), (22, 18)], ga.ROOFS["iron_age"][1])
        for a in (45, 135, 225, 315):
            x1 = 16 + 14 * math.cos(math.radians(a))
            y1 = 14 + 14 * math.sin(math.radians(a))
            c.line(16, 14, int(x1), int(y1), W[2], 1)
            poly(c, [(16 + 4 * math.cos(math.radians(a)), 14 + 4 * math.sin(math.radians(a))), (x1, y1),
                     (x1 + 3 * math.cos(math.radians(a + 90)), y1 + 3 * math.sin(math.radians(a + 90)))], P((251, 249, 251)))
        c.disc(16, 14, 1.5, W[0])
    elif key == "university":  # an open book with a mortarboard-like cap
        t = tones((251, 249, 251))
        poly(c, [(3, 14), (15, 17), (15, 29), (3, 26)], t[1])
        poly(c, [(17, 17), (29, 14), (29, 26), (17, 29)], t[2])
        for y in range(18, 26, 2):
            c.line(5, y - 1, 13, y + 1, P((147, 138, 130)))
            c.line(19, y + 1, 27, y - 1, P((147, 138, 130)))
        poly(c, [(6, 7), (16, 3), (26, 7), (16, 11)], P((47, 46, 45)))
        c.line(24, 7, 25, 13, P((223, 143, 19)))
    elif key == "medieval_arms":  # a heater shield and sword of steel
        sword(c, 3, 29, steel, 22)
        poly(c, [(13, 6), (29, 6), (29, 18), (21, 28), (13, 18)], CIV[1])
        for y in range(6, 29):
            for x in range(13, 30):
                if c.im.getpixel((x, y))[:3] == CIV[1]:
                    _mark(c, x, y)
        c.line(13, 6, 29, 6, steel[0])
        c.line(21, 7, 21, 26, P((223, 143, 19)))
    elif key == "crop_rotation":  # three strips: grain, green, fallow
        for k, col in enumerate(((228, 191, 68), (79, 132, 47), (122, 45, 2))):
            poly(c, [(2 + k * 10, 28), (11 + k * 10, 28), (15 + k * 6, 10), (9 + k * 6, 10)], tones(col)[1])
        for x, y in ((6, 26), (10, 20), (13, 14)):
            wheat(c, x, y, 5)
        c.line(14, 4, 22, 4, P((251, 249, 251)))
        c.line(20, 2, 22, 4, P((251, 249, 251)))
        c.line(20, 6, 22, 4, P((251, 249, 251)))
    elif key == "longbow":
        bow(c, 6, 30, 28)
        arrow(c, 3, 26, 28, 4)
        arrow(c, 9, 29, 30, 10)
    elif key == "sawmills":  # a saw blade over planks
        for k in range(4):
            c.rect(3, 18 + k * 3, 28, 19 + k * 3, W[1] if k % 2 else W[0])
        shaded_disc(c, 18, 11, 8, (185, 216, 230))
        for a in range(0, 360, 30):
            c.px(int(18 + 9 * math.cos(math.radians(a))), int(11 + 9 * math.sin(math.radians(a))), I[0])
        c.disc(18, 11, 2, I[2])
    elif key == "lime_mortar":
        mortar_bowl(c, 13, 29)
        t = tones((228, 241, 247))
        c.disc(13, 22, 5, t[1])
        c.rect(18, 7, 21, 20, W[1])
        poly(c, [(20, 18), (29, 24), (27, 27), (19, 21)], I[1])
    elif key == "trebuchet":
        c.rect(5, 24, 27, 27, W[1])
        c.line(9, 24, 15, 10, W[2], 2)
        c.line(21, 24, 15, 10, W[2], 2)
        c.line(4, 4, 26, 16, W[1], 2)
        c.rect(22, 15, 28, 21, S[1])
        rope(c, 4, 4, 6, 12)
        shaded_disc(c, 6, 13, 2.5, (147, 138, 130))
    else:
        raise KeyError(key)
    return finish(c)


# ------------------------------------------------------------------------------------------------ branches, eras, events

CATEGORY_RGB = {"basics": (147, 138, 130), "food": (79, 132, 47), "industry": (165, 77, 6), "building": (120, 121, 121),
                "trade": (223, 143, 19), "military": (183, 10, 9), "governance": (75, 46, 107), "science": (19, 103, 157)}


def category(key):
    """A branch of the research tree: 16x16, a small mark."""
    c = canvas(16, 16)
    W, I, S = ga.WOOD, ga.IRON, ga.STONE
    if key == "basics":  # a hand axe
        poly(c, [(4, 14), (6, 4), (11, 2), (12, 12)], P((101, 99, 98)))
        c.px(8, 5, P((169, 162, 160)))
    elif key == "food":
        for x in (5, 8, 11):
            wheat(c, x, 14, 9)
    elif key == "industry":
        stick(c, 3, 14, 9, 6, 2)
        c.rect(6, 2, 14, 5, I[1])
        c.line(6, 2, 14, 2, I[0])
    elif key == "building":
        for row in range(3):
            for k in range(2):
                x = 1 + k * 7 - (row % 2) * 3
                c.rect(max(1, x), 5 + row * 3, min(14, x + 6), 6 + row * 3, S[1])
                c.line(max(1, x), 5 + row * 3, min(14, x + 6), 5 + row * 3, S[0])
    elif key == "trade":
        for x, y in ((5, 11), (11, 11), (8, 5)):
            shaded_disc(c, x, y, 3.4, (223, 143, 19))
            c.px(x, y, P((165, 77, 6)))
    elif key == "military":
        c.line(2, 14, 13, 3, I[0], 2)
        c.line(2, 3, 13, 14, I[0], 2)
        c.line(3, 11, 5, 13, W[1], 2)
        c.line(11, 13, 13, 11, W[1], 2)
    elif key == "governance":  # a crown
        gold = tones((223, 143, 19))
        c.rect(2, 8, 13, 12, gold[1])
        for x in (2, 5, 8, 11, 13):
            c.rect(x, 4, x, 8, gold[0])
        c.px(7, 10, P((183, 10, 9)))
    elif key == "science":
        book(c, 2, 4, 13, 13, (19, 103, 157))
    return finish(c, 0.5)


def era(key):
    """An era's mark beside its name: 32x32."""
    c = canvas(32, 32)
    W, I, S = ga.WOOD, ga.IRON, ga.STONE
    if key == "stone_age":  # a hand axe and a fire
        poly(c, [(6, 28), (9, 8), (16, 3), (19, 26)], P((101, 99, 98)))
        poly(c, [(9, 24), (11, 10), (15, 6)], P((147, 138, 130)))
        logs(c, 24, 29)
        flame(c, 24, 27, 10)
    elif key == "neolithic":  # a pot and a sheaf
        c.disc(12, 21, 8, ga.MUD[1])
        c.rect(8, 9, 16, 13, ga.MUD[1])
        c.line(7, 9, 17, 9, ga.MUD[2])
        c.disc(9, 18, 2, ga.MUD[0])
        for x in (22, 25, 28):
            wheat(c, x, 29, 16)
        c.line(21, 22, 29, 22, P((170, 101, 39)))
    elif key == "bronze_age":
        round_shield(c, 16, 17, 13, (223, 143, 19), rim=(165, 77, 6), boss=(234, 210, 32))
        for a in range(0, 360, 30):
            c.px(int(16 + 8 * math.cos(math.radians(a))), int(17 + 8 * math.sin(math.radians(a))), P((165, 77, 6)))
    elif key == "iron_age":  # an iron helmet with cheek guards
        I2 = tones((120, 121, 121))
        c.disc(16, 15, 11, I2[1])
        c.rect(5, 15, 27, 18, I2[1])
        c.rect(7, 18, 12, 27, I2[1])
        c.rect(20, 18, 25, 27, I2[1])
        c.line(16, 4, 16, 18, I2[0])
        c.line(6, 15, 26, 15, I2[2])
        c.disc(12, 9, 2, I2[0])
    elif key == "middle_ages":  # a castle keep with a banner
        c.rect(8, 12, 24, 29, S[1])
        c.line(8, 12, 8, 29, S[0])
        c.line(24, 12, 24, 29, S[2])
        for x in range(8, 25, 4):
            c.rect(x, 8, x + 2, 11, S[1])
        c.rect(14, 21, 18, 29, P(INK))
        c.disc(16, 21, 2, P(INK))
        c.rect(11, 15, 12, 17, P(INK))
        c.rect(20, 15, 21, 17, P(INK))
        ga.flag(c, 16, 8, 7)
    return finish(c)


def event(key):
    """A world event in the advisor's panel: 32x32."""
    c = canvas(32, 32)
    if key == "drought":  # a sun over cracked earth
        shaded_disc(c, 16, 11, 7, (234, 210, 32))
        for a in range(0, 360, 45):
            c.line(int(16 + 9 * math.cos(math.radians(a))), int(11 + 9 * math.sin(math.radians(a))),
                   int(16 + 12 * math.cos(math.radians(a))), int(11 + 12 * math.sin(math.radians(a))), P((223, 143, 19)))
        t = tones((201, 113, 21))
        c.rect(2, 24, 29, 29, t[1])
        c.line(2, 24, 29, 24, t[0])
        for x0, x1 in ((6, 10), (14, 12), (20, 24), (26, 27)):
            c.line(x0, 25, x1, 29, P((105, 56, 41)))
    elif key == "hard_winter":  # a snowflake over snow
        ice = P((19, 103, 157))
        for a in range(0, 180, 30):
            dx, dy = 11 * math.cos(math.radians(a)), 11 * math.sin(math.radians(a))
            c.line(int(16 - dx), int(14 - dy), int(16 + dx), int(14 + dy), ice, 2)
        c.disc(16, 14, 2, P((228, 241, 247)))
        snow = tones((228, 241, 247))
        c.rect(2, 27, 29, 29, snow[1])
    elif key == "epidemic":  # a skull
        b = tones((228, 241, 247))
        c.disc(16, 14, 9, b[1])
        c.rect(11, 20, 21, 27, b[1])
        c.disc(12, 14, 2.5, P(INK))
        c.disc(20, 14, 2.5, P(INK))
        c.rect(15, 18, 17, 20, P(INK))
        for x in (13, 16, 19):
            c.line(x, 23, x, 27, P((147, 138, 130)))
    elif key == "fire":  # a burning house
        house(c, 6, 22, 29, 8, tones((228, 191, 68)), door=False)
        flame(c, 13, 20, 12)
        flame(c, 22, 22, 15)
    elif key == "flood":  # waves over a roof
        house(c, 9, 23, 22, 5, ga.ROOFS["neolithic"], door=False)
        w = tones((25, 119, 208))
        for k, y in enumerate((19, 23, 27)):
            for x in range(1, 31):
                c.px(x, y + (1 if ((x + k * 2) // 3) % 2 else 0), w[0])
            c.rect(1, y + 2, 30, y + 3, w[1])
    return finish(c)


def veteran(key):
    """A veteran choice: 16x16."""
    c = canvas(16, 16)
    I = ga.IRON
    if key == "hardened":  # a fist-like sword with a spark
        c.line(3, 13, 12, 4, I[0], 2)
        c.line(2, 10, 6, 14, P((223, 143, 19)), 2)
        c.px(13, 2, P((234, 210, 32)))
        c.px(14, 4, P((234, 210, 32)))
    elif key == "shield_wall":
        for x in (1, 6, 11):
            c.rect(x, 4, x + 4, 13, CIV[1])
            c.line(x, 4, x + 4, 4, CIV[0])
            c.px(x + 2, 8, I[1])
            for yy in range(4, 14):
                for xx in range(x, x + 5):
                    _mark(c, xx, yy)
    elif key == "forced_march":  # a boot with speed lines
        t = tones((105, 56, 41))
        c.rect(7, 3, 11, 11, t[1])
        c.rect(7, 11, 15, 13, t[1])
        c.line(7, 13, 15, 13, t[2])
        for y in (5, 8, 11):
            c.line(1, y, 4, y, P((251, 249, 251)))
    return finish(c, 0.4)


def window_icon(name):
    """A window button of the top bar: 16x16."""
    c = canvas(16, 16)
    W, S = ga.WOOD, ga.STONE
    if name == "settlements":
        house(c, 3, 12, 14, 5, ga.ROOFS["bronze_age"], wall=ga.MUD)
    elif name == "research":
        book(c, 2, 4, 13, 13, (19, 103, 157))
    elif name == "civilizations":
        ga.flag(c, 4, 14, 12)
        c.rect(5, 2, 12, 7, CIV[1])
        c.line(5, 2, 12, 2, CIV[0])
        for yy in range(2, 8):
            for xx in range(5, 13):
                _mark(c, xx, yy)
    elif name == "thinkers":
        tones_ = tones((204, 126, 87))
        c.disc(8, 5, 3, tones_[1])
        c.rect(3, 9, 12, 14, CIV[1])
        c.line(3, 9, 12, 9, CIV[0])
        for yy in range(9, 15):
            for xx in range(3, 13):
                _mark(c, xx, yy)
        c.px(12, 2, P((234, 210, 32)))
    elif name == "summary":  # a chart on parchment
        t = tones((242, 194, 121))
        c.rect(2, 2, 13, 13, t[1])
        for x, h in ((4, 3), (7, 6), (10, 9)):
            c.rect(x, 12 - h, x + 1, 12, P((19, 103, 157)))
    elif name == "units":
        person(c, 8, 15, CIV, "spear")
    return finish(c, 0.4)


# ------------------------------------------------------------------------------------------------ portraits (64x92)

SKINS = [(242, 194, 121), (229, 168, 98), (204, 126, 87), (187, 110, 70), (170, 101, 39), (138, 86, 45), (105, 56, 41)]
HAIRS = [(29, 23, 16), (69, 31, 7), (95, 36, 5), (148, 64, 16), (228, 191, 68), (201, 113, 21), (47, 46, 45)]
GREY_HAIRS = [(169, 162, 160), (147, 138, 130), (228, 241, 247)]
EYES = [(29, 23, 16), (19, 103, 157), (51, 88, 33), (95, 36, 5)]

FIELD_RGB = {"economy": (223, 143, 19), "growth": (79, 132, 47), "research": (19, 103, 157), "expansion": (183, 10, 9)}
RARITY_RGB = {"common": (147, 138, 130), "gifted": (79, 132, 47), "visionary": (19, 103, 157), "rare": (75, 46, 107),
              "legendary": (223, 143, 19)}
RARITY_RANK = {"common": 0, "gifted": 1, "visionary": 2, "rare": 3, "legendary": 4}


def _body(c, cx, top, rank, rnd, trim, under):
    """Shoulders and clothes in the people's colour, shaded with the three magentas: lit on the left, the middle tone,
    shadow on the right, darker folds; an undershirt at the neck, a belt, and trims of finer cloth or gold by rank."""
    bottom = 89
    for y in range(top, bottom + 1):
        k_ = (y - top)
        half = min(26, 13 + k_ * 1.6) if k_ < 9 else min(27, 26 + (k_ - 9) // 12)
        l, r = int(cx - half), int(cx + half)
        for x in range(l, r + 1):
            u = (x - l) / max(1, r - l)
            k = 0 if u < 0.25 else 1 if u < 0.7 else 2
            if k_ < 3 and k == 1 and u < 0.45:
                k = 0  # light on the top of the shoulders
            if (x - cx) in (-10, 9) and k_ > 10:
                k = min(2, k + 1)  # where the arms meet the body
            if (x - l) % 11 == 6 and k_ > 14 and k < 2:
                k += 1
            civ_px(c, x, y, k)
    # the undershirt: a V at the neck
    ut = tones(under)
    for k in range(9):
        c.line(cx - 5 + k // 2, top + k, cx + 5 - k // 2, top + k, ut[1] if k < 6 else ut[2])
    if trim is not None:
        t = tones(trim)
        for k in range(10):
            c.px(cx - 6 + k // 2, top + k, t[0])
            c.px(cx + 6 - k // 2, top + k, t[1])
        c.line(cx, top + 10, cx, bottom, t[1])
        c.line(cx + 1, top + 10, cx + 1, bottom, t[2])
    belt = tones((223, 143, 19) if rank >= 3 else (86, 44, 24))
    c.line(cx - 22, 80, cx + 22, 80, belt[1])
    c.line(cx - 22, 81, cx + 22, 81, belt[2])
    if rank >= 1:
        c.rect(cx - 2, 79, cx + 2, 82, tones((223, 143, 19))[0] if rank >= 2 else belt[0])


def _hand(c, x, y, skin):
    sk = ramp(skin)
    c.rect(x - 2, y - 2, x + 2, y + 2, sk[2])
    c.line(x - 2, y - 2, x + 2, y - 2, sk[3])
    c.line(x + 2, y - 1, x + 2, y + 2, sk[1])


def _back_hair(c, cx, cy, hair, hair_style, female):
    if hair_style == "bald":
        return
    hr = ramp(hair)
    down = 24 if female or hair_style in ("long", "braid") else 4
    for y in range(cy - 17, cy + down):
        for x in range(cx - 15, cx + 16):
            u, v = (x - cx) / 14.5, (y - cy + 3) / 16.0
            if (u * u + v * v <= 1) or (y >= cy and abs(x - cx) <= 14 - (y - cy) // 4 and down > 4):
                c.px(x, y, hr[1] if x > cx + 4 else hr[2] if (x + y) % 4 else hr[3])


def _head(c, rnd, cx, cy, skin, hair, eye, age, beard, hair_style, female):
    """A face 23x29: an oval lit from the top left, eyes with whites, brows, a shaded nose, a mouth; lines with age."""
    sk = ramp(skin)
    c.rect(cx - 5, cy + 12, cx + 5, cy + 19, sk[1])  # neck
    c.line(cx - 5, cy + 13, cx + 5, cy + 13, sk[0])
    for y in range(cy - 14, cy + 15):
        for x in range(cx - 11, cx + 12):
            u, v = (x - cx) / 11.2, (y - cy) / 14.5
            jaw = 1.0 if v < 0.3 else 1.0 - (v - 0.3) * 0.35
            d = (u / jaw) ** 2 + v * v
            if d <= 1:
                c.px(x, y, ga.lit(sk, u, v, math.sqrt(max(0.0, 1 - d))))
    c.rect(cx - 13, cy - 2, cx - 11, cy + 3, sk[2])  # ears
    c.rect(cx + 11, cy - 2, cx + 13, cy + 3, sk[1])
    hr = ramp(hair)
    if hair_style != "bald":  # hair over the forehead
        part = rnd.choice([-3, 0, 4])
        for y in range(cy - 16, cy - 6):
            for x in range(cx - 12, cx + 13):
                u, v = (x - cx) / 12.5, (y - cy + 6) / 10.5
                fringe = y < cy - 9 + (2 if (x - cx - part) % 7 < 3 else 0)
                if u * u + v * v <= 1 and (fringe or abs(x - cx) > 9):
                    c.px(x, y, hr[3] if (x - cx - part) % 6 == 0 and x < cx else hr[2] if x < cx + 4 else hr[1])
        if hair_style == "topknot":
            c.disc(cx + 1, cy - 17, 4, hr[2])
            c.px(cx - 1, cy - 19, hr[3])
    else:
        c.line(cx - 5, cy - 12, cx - 2, cy - 12, sk[4])
    brow = hr[0] if hair_style != "bald" else sk[0]
    c.line(cx - 7, cy - 4, cx - 2, cy - 4 - (1 if female else 0), brow)
    c.line(cx + 2, cy - 4 - (1 if female else 0), cx + 7, cy - 4, brow)
    for ex in (cx - 5, cx + 4):
        c.rect(ex - 1, cy - 1, ex + 2, cy, P((251, 249, 251)))
        c.rect(ex, cy - 1, ex + 1, cy, P(eye))
        c.px(ex + 1, cy - 1, P(INK))
        if female:
            c.line(ex - 1, cy - 2, ex + 2, cy - 2, P(INK))
    c.line(cx, cy, cx + 1, cy + 5, sk[1])  # nose
    c.px(cx - 1, cy + 6, sk[0])
    c.px(cx + 1, cy + 6, sk[0])
    c.px(cx + 2, cy + 4, sk[0])
    lips = P((183, 10, 9)) if female else sk[0]
    c.line(cx - 3, cy + 9, cx + 3, cy + 9, lips)
    c.line(cx - 2, cy + 10, cx + 2, cy + 10, sk[1] if not female else P((122, 45, 2)))
    c.px(cx - 7, cy + 4, sk[3])  # cheek light
    if age >= 1:
        c.px(cx - 7, cy + 1, sk[1])
        c.px(cx + 7, cy + 1, sk[1])
    if age >= 2:
        c.line(cx - 6, cy - 8, cx + 6, cy - 8, sk[1])
        c.line(cx - 5, cy + 6, cx - 4, cy + 9, sk[1])
        c.line(cx + 5, cy + 6, cx + 4, cy + 9, sk[1])
    if age >= 3:
        c.line(cx - 5, cy - 10, cx + 5, cy - 10, sk[1])
        c.px(cx - 8, cy - 1, sk[0])
        c.px(cx + 8, cy - 1, sk[0])
        c.line(cx - 3, cy + 12, cx + 3, cy + 12, sk[1])
    if beard in ("short", "full", "long") and not female:
        length = {"short": 4, "full": 10, "long": 20}[beard]
        for y in range(cy + 5, cy + 12 + length):
            half = 11 - max(0, (y - cy - 10)) * (0.55 if beard != "long" else 0.35)
            for x in range(int(cx - half), int(cx + half) + 1):
                if y < cy + 11 and abs(x - cx) < 4 and y > cy + 7:
                    continue  # the mouth stays clear
                if y < cy + 8 and abs(x - cx) < 6:
                    continue
                c.px(x, y, hr[2] if (x + y) % 3 else hr[1] if x > cx else hr[3])
        c.line(cx - 3, cy + 9, cx + 3, cy + 9, P((105, 56, 41)))
    elif beard == "moustache" and not female:
        c.line(cx - 5, cy + 7, cx + 5, cy + 7, hr[1])
        c.line(cx - 4, cy + 8, cx - 3, cy + 8, hr[1])
        c.line(cx + 3, cy + 8, cx + 4, cy + 8, hr[1])
    if female and rnd.random() < 0.5:  # earrings
        c.px(cx - 12, cy + 4, P((223, 143, 19)))
        c.px(cx + 12, cy + 4, P((223, 143, 19)))


def _headwear(c, kind, cx, top, rank):
    gold = tones((223, 143, 19))
    if kind == "hood":
        for y in range(top - 3, top + 12):
            for x in range(cx - 15, cx + 16):
                u, v = (x - cx) / 15.0, (y - top - 4) / 10.0
                if u * u + v * v <= 1 and not ((x - cx) ** 2 / 81 + (y - top - 8) ** 2 / 36 < 1 and y > top + 2):
                    civ_px(c, x, y, 0 if x < cx - 4 else 1 if x < cx + 5 else 2)
    elif kind == "band":
        c.line(cx - 10, top + 5, cx + 10, top + 5, CIV[1])
        c.line(cx - 10, top + 6, cx + 10, top + 6, CIV[2])
        for x in range(cx - 10, cx + 11):
            _mark(c, x, top + 5)
            _mark(c, x, top + 6)
    elif kind == "cap":
        for y in range(top - 2, top + 5):
            half = 10 if y > top else 8
            for x in range(cx - half, cx + half + 1):
                civ_px(c, x, y, 0 if x < cx - 3 else 1 if x < cx + 5 else 2)
        c.line(cx - 10, top + 5, cx + 10, top + 5, P((251, 249, 251)) if rank >= 2 else P((86, 44, 24)))
    elif kind == "laurel":
        g = tones((79, 132, 47))
        for x in range(cx - 10, cx + 11, 2):
            c.px(x, top + 4 + (1 if x % 4 else 0), g[1])
            c.px(x + 1, top + 3 + (1 if x % 4 else 0), g[0])
    elif kind == "circlet":
        c.line(cx - 10, top + 5, cx + 10, top + 5, gold[1])
        c.line(cx - 10, top + 6, cx + 10, top + 6, gold[2])
        c.rect(cx - 1, top + 2, cx + 1, top + 4, gold[0])
        c.px(cx, top + 3, P((19, 103, 157)))
    elif kind == "tall_hat":
        for y in range(top - 12, top + 5):
            half = 6 + (y - top + 12) // 4
            for x in range(cx - half, cx + half + 1):
                civ_px(c, x, y, 0 if x < cx - 2 else 1 if x < cx + 4 else 2)
        c.line(cx - 10, top + 4, cx + 10, top + 4, gold[1])
        c.px(cx, top - 13, gold[0])


def _item(c, kind, x, y):
    """What the advisor or thinker holds, at hand height (x, y): the left hand side of the picture."""
    W, I = ga.WOOD, ga.IRON
    gold = tones((223, 143, 19))
    if kind == "purse":  # economy: a purse of coins
        t = tones((138, 86, 45))
        c.disc(x, y + 2, 6, t[1])
        c.rect(x - 3, y - 6, x + 3, y - 3, t[1])
        c.line(x - 3, y - 3, x + 3, y - 3, gold[1])
        for cx_, cy_ in ((x + 5, y - 5), (x + 7, y - 1)):
            shaded_disc(c, cx_, cy_, 2.5, (223, 143, 19))
    elif kind == "scales":
        c.line(x, y - 10, x, y + 6, gold[1])
        c.line(x - 9, y - 8, x + 9, y - 8, gold[1])
        for sx in (x - 8, x + 8):
            c.line(sx, y - 8, sx - 3, y - 2, gold[2])
            c.line(sx, y - 8, sx + 3, y - 2, gold[2])
            c.line(sx - 4, y - 1, sx + 4, y - 1, gold[1])
    elif kind == "sheaf":  # growth
        for dx in range(-5, 6, 2):
            wheat(c, x + dx, y + 8, 18 - abs(dx))
        c.line(x - 5, y + 2, x + 5, y + 2, P((170, 101, 39)))
    elif kind == "basket":
        t = tones((170, 101, 39))
        for k in range(9):
            c.line(x - 8 + k // 3, y + k - 2, x + 8 - k // 3, y + k - 2, t[1] if k % 2 else t[0])
        for bx in (x - 4, x, x + 4):
            shaded_disc(c, bx, y - 3, 2.6, (183, 10, 9))
    elif kind == "scroll":  # research
        scroll(c, x - 9, y - 6, x + 9, y + 6)
    elif kind == "book":
        book(c, x - 8, y - 6, x + 8, y + 7, (19, 103, 157))
    elif kind == "map":  # expansion: a map and a staff
        t = tones((242, 194, 121))
        c.rect(x - 10, y - 7, x + 8, y + 7, t[1])
        c.line(x - 10, y - 7, x + 8, y - 7, t[0])
        c.line(x - 7, y + 4, x - 2, y - 3, P((183, 10, 9)))
        c.line(x - 2, y - 3, x + 5, y + 1, P((183, 10, 9)))
        c.px(x + 5, y + 1, P(INK))
        c.disc(x - 6, y - 2, 2, P((79, 132, 47)))
    elif kind == "compass":
        shaded_disc(c, x, y, 7, (223, 143, 19))
        c.disc(x, y, 5, P((242, 194, 121)))
        c.line(x, y - 4, x, y + 4, P((183, 10, 9)))
        c.line(x - 4, y, x + 4, y, P(INK))
    elif kind == "tablet":
        tablet(c, x - 7, y - 9, x + 7, y + 8, (204, 126, 87))
    elif kind == "staff":
        c.line(x + 6, y - 40, x + 6, y + 20, W[1], 2)
    elif kind == "lamp":
        t = tones((223, 143, 19))
        poly(c, [(x - 7, y), (x + 6, y), (x + 3, y + 5), (x - 4, y + 5)], t[1])
        c.line(x - 7, y, x + 6, y, t[0])
        flame(c, x + 6, y - 1, 6)
    elif kind == "orb":
        shaded_disc(c, x, y, 6, (63, 167, 201))
        c.px(x - 2, y - 3, P((251, 249, 251)))
        c.rect(x - 4, y + 6, x + 4, y + 7, gold[1])
    elif kind == "astrolabe":
        for a in range(0, 360, 6):
            c.px(int(x + 7 * math.cos(math.radians(a))), int(y + 7 * math.sin(math.radians(a))), gold[1])
        c.line(x - 7, y, x + 7, y, gold[2])
        c.line(x - 4, y - 5, x + 4, y + 5, gold[0])
        c.disc(x, y, 1.5, gold[0])
    elif kind == "quill":
        c.line(x - 4, y + 8, x + 6, y - 10, P((251, 249, 251)), 2)
        c.line(x - 4, y + 8, x + 6, y - 10, P((169, 162, 160)))
        c.px(x - 5, y + 9, P(INK))


FIELD_ITEMS = {"economy": ["purse", "scales"], "growth": ["sheaf", "basket"], "research": ["scroll", "book", "quill"],
               "expansion": ["map", "compass"]}


def portrait(rnd, rank, items, frame_rgb, thinker):
    """A bust 64 wide and 92 tall on a dark arch in the frame's colour (the field of an advisor, the rarity of a thinker).
    rank 0..4 makes the person richer and (often) older: finer clothes, gold trims, a cloak, jewellery, better things in
    hand. Everything else is drawn at random: skin, hair and its style, eyes, beard, age, headwear."""
    c = canvas(64, 92)
    fr = ramp(frame_rgb)
    back = ramp((47, 46, 45))
    for y in range(92):
        for x in range(64):
            if not (y >= 18 or (x - 32) ** 2 + (y - 18) ** 2 <= 31 ** 2):
                continue
            edge = y >= 90 or x <= 1 or x >= 62 or (y < 18 and (x - 32) ** 2 + (y - 18) ** 2 > 29 ** 2)
            if edge:
                c.px(x, y, fr[3] if x + y < 70 else fr[1])
            else:
                c.px(x, y, back[2] if (x - 10) + y > 50 else back[3])
    female = rnd.random() < 0.4
    age = min(3, max(0, int(rnd.random() * 3) + (rank >= 3) + (1 if rank == 4 and rnd.random() < 0.7 else 0)))
    skin = rnd.choice(SKINS)
    hair = rnd.choice(GREY_HAIRS) if age >= 3 or (age == 2 and rnd.random() < 0.4) else rnd.choice(HAIRS)
    eye = rnd.choice(EYES)
    hair_style = rnd.choice(["short", "long", "braid", "topknot"] + ([] if female else ["bald", "short"]))
    beard = None if female else rnd.choice([None, "short", "full", "moustache"] + (["long", "long"] if age >= 2 else []))
    cx, cy = 32 + rnd.randint(-1, 1), 34
    if rank >= 2:  # a cloak behind the shoulders
        cl = tones(rnd.choice([(14, 63, 107), (75, 46, 107), (105, 56, 41), (47, 46, 45), (51, 88, 33)]))
        for y in range(cy + 18, 90):
            half = min(30, 16 + (y - cy - 18) * 2)
            c.line(cx - half, y, cx - half + 5, y, cl[1])
            c.line(cx + half - 5, y, cx + half, y, cl[2])
    _back_hair(c, cx, cy, hair, hair_style, female)
    trim = (223, 143, 19) if rank >= 3 else (242, 194, 121) if rank >= 1 else None
    under = (251, 249, 251) if rank >= 2 else rnd.choice([(242, 194, 121), (170, 101, 39), (169, 162, 160)])
    _body(c, cx, cy + 19, rank, rnd, trim, under)
    if rank >= 3:
        shaded_disc(c, cx - 14, cy + 22, 2.5, (223, 143, 19))  # a brooch holding the cloak
    _head(c, rnd, cx, cy, skin, hair, eye, age, beard, hair_style, female)
    choices = [None, "hood", "band", "cap"][: 2 + rank] if rank < 3 else [None, "laurel", "circlet", "tall_hat", "cap"]
    wear = rnd.choice(choices)
    if rank == 4 and wear is None:
        wear = rnd.choice(["circlet", "laurel"])
    if wear is not None:
        _headwear(c, wear, cx, cy - 17, rank)
    if rank >= 3 and rnd.random() < 0.7:  # a chain with a medallion
        g = tones((223, 143, 19))
        for k in range(-9, 10):
            c.px(cx + k, cy + 21 + int(0.07 * k * k), g[1])
        shaded_disc(c, cx, cy + 28, 3, (223, 143, 19))
    # what is in hand, at the lower front, held by a hand
    for i, it in enumerate(items):
        x = 17 + i * 28 + rnd.randint(-1, 1)
        y = 72 - (8 if it in ("sheaf", "staff") else 0)
        _item(c, it, x, y)
        _hand(c, x + (6 if it != "staff" else 6), y + 6 if it != "staff" else y, skin)
    c.outline(P(OUTLINE))
    ga.texturize(c.im, seed=rnd.randint(0, 999), strength=0.45)
    return c


def advisor_portraits(field, count):
    out = []
    for n in range(count):
        rnd = random.Random(f"advisor-{field}-{n}")
        rank = rnd.choice([0, 1, 1, 2])
        items = [rnd.choice(FIELD_ITEMS[field])]
        out.append(portrait(rnd, rank, items, FIELD_RGB[field], False))
    return out


THINKER_ITEMS = {0: ["tablet", "scroll", None], 1: ["scroll", "quill", "tablet"], 2: ["book", "scroll", "astrolabe"],
                 3: ["book", "astrolabe", "lamp"], 4: ["orb", "astrolabe", "book", "lamp"]}


def thinker_portraits(rarity, count):
    out = []
    rank = RARITY_RANK[rarity]
    for n in range(count):
        rnd = random.Random(f"thinker-{rarity}-{n}")
        first = rnd.choice(THINKER_ITEMS[rank])
        items = [first] if first else []
        if rank >= 3 and rnd.random() < 0.6:
            items.append(rnd.choice(["staff", "quill", "lamp"]))
        out.append(portrait(rnd, rank, items, RARITY_RGB[rarity], True))
    return out


# ------------------------------------------------------------------------------------------------ emblems (40x40)

MOTIFS = ["deer", "sun", "tree", "wave", "mountain", "bird", "hand", "spiral"]


def _motif(c, kind, cx, cy, s, era_key):
    """The people's sign in its colour, shaded with the three magentas; s scales it (about 1.0 = 16 px)."""
    def P_(x, y, k=1):
        civ_px(c, int(round(cx + x * s)), int(round(cy + y * s)), k)

    def L(x0, y0, x1, y1, k=1):
        n = int(max(abs(x1 - x0), abs(y1 - y0)) * s) + 1
        for i in range(n + 1):
            P_(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n, k)

    def D(x, y, r, k=1):
        rr = r * s
        for yy in range(int(-rr) - 1, int(rr) + 2):
            for xx in range(int(-rr) - 1, int(rr) + 2):
                if xx * xx + yy * yy <= rr * rr:
                    civ_px(c, int(round(cx + x * s)) + xx, int(round(cy + y * s)) + yy, 0 if xx + yy < -rr * 0.5 else k)

    if kind == "deer":
        for y in range(-1, 3):
            L(-5, y, 3, y, 1 if y < 2 else 2)
        L(3, -1, 5, -5, 1)
        L(5, -5, 6, -5, 1)
        for x in (-5, -3, 1, 3):
            L(x, 3, x, 7, 2)
        L(4, -6, 2, -9, 0)
        L(6, -6, 8, -9, 0)
        L(3, -8, 1, -8, 0)
    elif kind == "sun":
        D(0, 0, 4)
        for a in range(0, 360, 45):
            L(5.5 * math.cos(math.radians(a)), 5.5 * math.sin(math.radians(a)),
              8 * math.cos(math.radians(a)), 8 * math.sin(math.radians(a)), 1 if a < 180 else 2)
    elif kind == "tree":
        L(0, 8, 0, -2, 2)
        L(-1, 8, -1, -2, 1)
        D(0, -4, 5)
        D(-4, -1, 3, 2)
        D(4, -1, 3, 2)
    elif kind == "wave":
        for row, k in ((-4, 0), (1, 1), (6, 2)):
            for x in range(-8, 8):
                L(x, row + 2 * math.sin(x * 0.8), x + 1, row + 2 * math.sin((x + 1) * 0.8), k)
                L(x, row + 1 + 2 * math.sin(x * 0.8), x + 1, row + 1 + 2 * math.sin((x + 1) * 0.8), k)
    elif kind == "mountain":
        for y in range(0, 12):
            half = y * 0.75
            for x in range(int(-half), int(half) + 1):
                P_(x, -6 + y, 0 if x < 0 else 2 if x > half * 0.5 else 1)
        L(-2, -3, 2, -3, 0)
    elif kind == "bird":
        for dy in (0, 1):
            L(-8, -2 + dy, -3, 1 + dy, 0 if dy == 0 else 1)
            L(-3, 1 + dy, 0, -1 + dy, 1)
            L(0, -1 + dy, 3, 1 + dy, 1)
            L(3, 1 + dy, 8, -2 + dy, 2)
        D(0, 1, 1.8)
        L(0, 3, -1, 6, 2)
        L(0, 3, 1, 6, 2)
    elif kind == "hand":
        for y in range(0, 7):
            L(-4, y, 4, y, 1 if y < 5 else 2)
        for k, fx in enumerate((-4, -2, 0, 2)):
            L(fx, 0, fx, -6 + abs(k - 1.5), 1)
        L(4, 2, 7, -1, 2)
    elif kind == "spiral":
        for t in range(0, 420):
            a = t * 0.03
            r = 0.6 + a * 0.62
            P_(r * math.cos(a), r * math.sin(a), 0 if math.sin(a) < -0.3 else 1 if math.cos(a) < 0.5 else 2)


def emblem(era_key, motif):
    c = canvas(40, 40)
    W = ga.WOOD
    # 2026-11-24 (Patryk): the Stone Age carves its sign in a stone; the Neolithic paints it on a board of planks.
    if era_key == "neolithic":  # a board of planks with the sign painted on, hung on a cord
        t = ramp((138, 86, 45))
        for x in range(5, 35):
            for y in range(9, 36):
                c.px(x, y, t[0] if x % 8 == 4 else t[3] if (x + y * 3) % 17 == 0 else t[2] if x < 20 else t[1])
        rope(c, 8, 9, 20, 2)
        rope(c, 32, 9, 20, 2)
        _motif(c, motif, 20, 22, 1.25, era_key)
    elif era_key == "stone_age":  # a standing stone with the sign carved and painted
        boulder(c, 20, 24, 14, 16, (147, 138, 130), seed=sum(map(ord, motif)), flat_bottom=True)
        _motif(c, motif, 20, 22, 1.2, era_key)
    elif era_key == "bronze_age":  # a round wooden shield with a bronze rim and boss
        round_shield(c, 20, 20, 18, (138, 86, 45), rim=(201, 113, 21))
        for x in range(4, 37):
            if x % 7 == 0:
                c.line(x, 4, x, 36, ramp((138, 86, 45))[1])
        rr = ramp((201, 113, 21))
        for y in range(1, 40):
            for x in range(1, 40):
                d = math.hypot(x - 20, y - 20)
                if 16.4 < d <= 18:
                    c.px(x, y, rr[3] if (x - 20) + (y - 20) < 0 else rr[1])
                elif d > 18:
                    c.im.putpixel((x, y), (0, 0, 0, 0))
        _motif(c, motif, 20, 20, 1.3, era_key)
        for a in range(0, 360, 45):
            c.px(int(20 + 15 * math.cos(math.radians(a))), int(20 + 15 * math.sin(math.radians(a))), rr[4])
    elif era_key == "iron_age":  # a cloth banner on a crossbar
        c.line(4, 4, 36, 4, W[1], 2)
        c.line(20, 1, 20, 4, W[2])
        cloth = tones((242, 194, 121))
        poly(c, [(7, 6), (33, 6), (33, 34), (20, 28), (7, 34)], cloth[1])
        for y in range(6, 35):
            c.px(7, y, cloth[0])
            c.px(32, y, cloth[2])
        for y in range(6, 34, 4):
            c.line(8, y, 31, y, cloth[1])
        _motif(c, motif, 20, 17, 1.15, era_key)
        c.px(4, 4, ga.IRON[0])
        c.px(36, 4, ga.IRON[0])
    elif era_key == "middle_ages":  # a heater shield: the field parted, the sign on it, a gold edge
        gold = ramp((223, 143, 19))
        pts = [(4, 3), (36, 3), (36, 20), (20, 38), (4, 20)]
        poly(c, pts, gold[2])
        inner = [(6, 5), (34, 5), (34, 19), (20, 35), (6, 19)]
        field = tones((251, 249, 251))
        poly(c, inner, field[1])
        poly(c, [(6, 5), (20, 5), (20, 35), (6, 19)], field[0])
        for y in range(5, 11):  # a chief in the people's colour
            for x in range(6, 35):
                if c.im.getpixel((x, y))[3]:
                    civ_px(c, x, y, 2 if x > 28 else 1)
        _motif(c, motif, 20, 21, 1.05, era_key)
        c.line(4, 3, 36, 3, gold[3])
    c.outline(P(OUTLINE))
    ga.texturize(c.im, seed=sum(map(ord, era_key + motif)), strength=0.5)
    return c


# ------------------------------------------------------------------------------------------------ badges and everything

def advisor_badge():
    """The advisor in the alert strip: a parchment badge like the alerts, with a hooded head and a scroll."""
    def mark(m, x, y):
        sk = P((204, 126, 87))
        m.rect(x + 2, y + 1, x + 4, y + 3, sk)
        m.rect(x + 1, y, x + 5, y, CIV[1])
        m.px(x + 1, y + 1, CIV[1])
        m.px(x + 5, y + 1, CIV[1])
        m.rect(x + 1, y + 4, x + 5, y + 6, CIV[1])
        m.px(x + 6, y + 5, P((242, 194, 121)))
    return ga.badge_with(mark, "blue")


def strangers_badge():
    """Alert 11, "Strangers": foreign scouts saw us. Footprints on an amber badge."""
    def mark(m, x, y):
        foot = P((86, 44, 24))
        for fx, fy in ((x, y + 4), (x + 4, y)):
            m.rect(fx, fy, fx + 1, fy + 2, foot)
            m.px(fx, fy - 1, foot)
            m.px(fx + 1, fy + 3, foot)
    return ga.badge_with(mark, "amber")


ADVISOR_FIELDS = ["economy", "growth", "research", "expansion"]
RARITIES = ["common", "gifted", "visionary", "rare", "legendary"]
WINDOWS = ["settlements", "research", "civilizations", "thinkers", "summary", "units"]
PORTRAITS_PER_KIND = 12


def generate(d):
    """Every icon, portrait and emblem of this module, saved through the canvas (placeholders or the flat pack)."""
    for t in d["technologies"]:
        tech(t["key"]).save("techs", f"tech_{t['key']}")
    for k in d["techCategories"]:
        category(k).save("ui", f"category_{k}")
    for e in d["eras"]:
        era(e["key"]).save("ui", f"era_{e['key']}")
    for e in d["events"]["pool"]:
        event(e["key"]).save("ui", f"event_{e['key']}")
    for v in d["veterans"]["options"]:
        veteran(v["key"]).save("ui", f"veteran_{v['key']}")
    for w in WINDOWS:
        window_icon(w).save("ui", f"window_{w}")
    advisor_badge().save("ui", "advisor")
    strangers_badge().save("ui", "alert_11")
    for f in d["advisors"]["fields"]:
        for n, pic in enumerate(advisor_portraits(f, PORTRAITS_PER_KIND)):
            pic.save("portraits", f"advisor_{f}_{n + 1:02d}")
    for r in d["thinkers"]["rarities"]:
        for n, pic in enumerate(thinker_portraits(r["key"], PORTRAITS_PER_KIND)):
            pic.save("portraits", f"thinker_{r['key']}_{n + 1:02d}")
    for e in d["eras"]:
        for m in MOTIFS:
            emblem(e["key"], m).save("emblems", f"emblem_{e['key']}_{m}")
