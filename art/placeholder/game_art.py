"""Placeholder art of the new game at twice the size (decisions G1-G6 of Patryk, 2026-11-23, docs/grafika-plan.md).

One style for everything, so the pieces sit together on the map:
  * pixel density 2x: a hex is 96x84, one of its four places 32x28;
  * 3/4 view from the side, light from the top left: a lighter left/top, a darker right/bottom, three tones per material;
  * a 1-pixel outline in the darkest brown (OUTLINE) around every object, never around ground tiles;
  * civilization colour only in the reserved magentas (CIV), on flags, roofs' ridges and tunics;
  * roofs by the era of the building (thatch, reed, red tile, slate, dark slate), so an era reads at a glance;
  * units stand on their own: a group of 2-3 figures with an outline (the game draws a soft shadow under them).

Drawn from the data (data/sim/game.json): every building, unit, good and settlement level gets a picture under its key.
"""

import json
import math
import os
import random

S = 2                      # pixel density against the 48x42 hex of the stage 5 placeholders
HW, HH = 48 * S, 42 * S    # hex tile
PW, PH = 16 * S, 14 * S    # one of the four places of a hex

# Filled by generate.py (shared helpers: palette, Canvas, nearest, shade).
G = {}


def P(rgb):
    return G["nearest"](rgb)


def sh(c, step):
    return G["shade"](c, step)


OUTLINE = (29, 23, 16)
CIV = [(255, 0, 255), (192, 0, 192), (128, 0, 128)]


def tones(rgb):
    base = P(rgb)
    return sh(base, 1), base, sh(base, -1)


# Materials (light, base, dark).
WOOD = None
THATCH = None
STONE = None
MUD = None
ROOFS = {}


def init_materials():
    global WOOD, THATCH, STONE, MUD, ROOFS, HIDE, SKIN, IRON, WATER, BRICK
    WOOD = tones((138, 86, 45))
    THATCH = tones((228, 191, 68))
    STONE = tones((147, 138, 130))
    MUD = tones((204, 126, 87))
    BRICK = tones((165, 77, 6))
    HIDE = tones((170, 101, 39))
    SKIN = P((204, 126, 87))
    IRON = tones((120, 121, 121))
    WATER = tones((25, 119, 208))
    ROOFS = {
        "stone_age": HIDE,
        "neolithic": THATCH,
        "bronze_age": tones((187, 110, 70)),
        "iron_age": tones((101, 99, 98)),
        "middle_ages": tones((79, 75, 69)),
    }


def canvas(w, h):
    return G["Canvas"](w, h)


# ------------------------------------------------------------------------------------------------ hex mask at 2x

def hex_mask():
    # The 48x42 template doubled pixel for pixel: it tiles (columns every 72 px, rows every 84, odd columns 42 down)
    # exactly as the 48x42 one does at half the size.
    from PIL import Image
    here = os.path.dirname(os.path.abspath(__file__))
    tpl = Image.open(os.path.join(here, "..", "templates", "hex_template_48x42.png")).convert("RGBA")
    return [[tpl.getpixel((x // S, y // S))[3] > 0 for x in range(HW)] for y in range(HH)]


MASK = hex_mask()


def inside(x, y, margin=0):
    if not (0 <= x < HW and 0 <= y < HH) or not MASK[y][x]:
        return False
    for dx, dy in ((margin, 0), (-margin, 0), (0, margin), (0, -margin)):
        xx, yy = x + dx, y + dy
        if not (0 <= xx < HW and 0 <= yy < HH) or not MASK[yy][xx]:
            return False
    return True


# ------------------------------------------------------------------------------------------------ drawing pieces

def _h(x, y, seed=0):
    """A fixed pseudo-random number 0-1 for a pixel: patterns that look hand-made but never change."""
    v = (x * 73856093) ^ (y * 19349663) ^ (seed * 83492791)
    v = (v ^ (v >> 13)) * 1274126177 & 0xFFFFFFFF
    return ((v ^ (v >> 16)) & 0xFFFF) / 0xFFFF


def _mark(c, x, y):
    """Keep this pixel out of the texture pass (signs, flags: they stay clean)."""
    if not hasattr(c, "protect"):
        c.protect = set()
    c.protect.add((x, y))


def box(c, x0, y0, x1, y1, t, moss=True):
    """A wall in 3/4 view, drawn in its material: courses of cut stone, rows of brick, plaster with cracks or upright
    planks. Lit on the left, in shadow on the right, darker at the foot and right under the eaves (depth), and green moss
    creeping up the foot of stone and brick walls."""
    mat = "stone" if t is STONE else "wood" if t is WOOD else "mud" if t is MUD else "brick" if t is BRICK else "plain"
    r = ramp(t[1])
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            ly, lx = y - y0, x - x0
            col = r[2]
            if mat == "stone":
                row = ly // 3
                off = (row % 2) * 3 + int(_h(row, 0, 5) * 3)
                if ly % 3 == 2:
                    col = r[1]  # mortar bed
                elif (lx + off) % 6 == 0:
                    col = r[1]  # joint
                elif (lx + off) % 6 == 1 and ly % 3 == 0:
                    col = r[3]  # lit edge of the block
                elif _h(x, y, 1) < 0.12:
                    col = r[1]
            elif mat == "brick":
                row = ly // 2
                if ly % 2 == 1:
                    col = P((147, 138, 130))  # light mortar
                elif (lx + (row % 2) * 2) % 4 == 0:
                    col = P((147, 138, 130))
                elif _h(x, y, 2) < 0.2:
                    col = r[3]
            elif mat == "wood":
                if lx % 4 == 3:
                    col = r[0]  # gap between planks
                elif lx % 4 == 0:
                    col = r[3]
                elif _h(lx // 4, ly // 2, 3) < 0.25:
                    col = r[1]  # grain
            elif mat == "mud":
                if _h(x, y, 4) < 0.08:
                    col = r[3]
                elif _h(x // 2, y, 6) < 0.05:
                    col = r[1]
            # depth: light from the left, shadow on the right, at the foot and under the eaves
            if x <= x0 + 1:
                col = tone(col, 1)
            elif x >= x1 - 2:
                col = tone(col, -1)
            if y == y0:
                col = tone(col, -2)
            elif y >= y1 - 1:
                col = tone(col, -1)
            c.px(x, y, col)
    if moss and mat in ("stone", "brick"):
        green = ramp((79, 132, 47))
        for x in range(x0, x1 + 1):
            hgt = int(_h(x, y1, 9) * 4) - 1
            for k in range(max(0, hgt)):
                c.px(x, y1 - k, green[2] if k == 0 else green[3] if _h(x, k, 8) < 0.5 else green[1])


def gable(c, x0, x1, base_y, height, t, ridge_civ=False):
    """A pitched roof over x0..x1 whose eaves stand at base_y, drawn in its covering: stitched hides, straw thatch in
    strokes, rows of clay tiles or slates. Lit left slope, shaded right slope, a light ridge, a dark eaves line."""
    kind = "hide" if t is HIDE else "thatch" if t is THATCH else "tile" if t is ROOFS.get("bronze_age") else "slate"
    r = ramp(t[1])
    mid = (x0 + x1) / 2
    for k in range(height + 1):
        y = base_y - k
        half = (x1 - x0) / 2 * (1 - k / (height + 1)) + 1
        l, rr = round(mid - half), round(mid + half)
        for x in range(l, rr + 1):
            col = r[2]
            if kind == "thatch":
                col = r[3] if _h(x, y // 2, 11) < 0.3 else r[1] if _h(x, y // 2, 12) < 0.25 else r[2]
            elif kind == "tile":
                if k % 3 == 0:
                    col = r[1]
                elif (x + (k // 3) * 2) % 4 == 0:
                    col = r[1]
                elif k % 3 == 2:
                    col = r[3]
            elif kind == "slate":
                if k % 2 == 0:
                    col = r[1]
                elif (x + k) % 5 == 0:
                    col = r[1]
            elif kind == "hide":
                if (x + k) % 7 == 0:
                    col = r[3]
            col = tone(col, 1) if x <= mid else tone(col, -1)
            if k == height:
                col = r[4]
            c.px(x, y, col)
    # the eaves painted in the people's colour (2026-11-23, Patryk): every roof says whose it is, from afar too
    c.line(x0 - 1, base_y, x1 + 1, base_y, CIV[2])
    c.line(x0, base_y - 1, x1, base_y - 1, CIV[1])
    for xx in range(x0 - 1, x1 + 2):
        _mark(c, xx, base_y)
        _mark(c, xx, base_y - 1)
    if ridge_civ:
        c.px(round(mid), base_y - height, CIV[0])
        c.px(round(mid) + 1, base_y - height, CIV[1])


def flat_roof(c, x0, x1, y, t):
    c.rect(x0 - 1, y - 2, x1 + 1, y, t[1])
    c.line(x0 - 1, y - 2, x1 + 1, y - 2, t[0])
    c.line(x0 - 1, y, x1 + 1, y, t[2])


def door(c, x, y, h=5):
    c.rect(x, y - h + 1, x + 2, y, P((47, 46, 45)))
    c.px(x, y - h + 1, P((79, 75, 69)))


def flag(c, x, y, h=8):
    c.line(x, y, x, y - h, P((47, 46, 45)))
    c.rect(x + 1, y - h, x + 4, y - h + 2, CIV[1])
    c.line(x + 1, y - h, x + 4, y - h, CIV[0])
    for yy in range(y - h, y + 1):
        for xx in range(x, x + 5):
            _mark(c, xx, yy)


def tent(c, x, y, w, h, t):
    for k in range(h):
        half = round(w / 2 * (1 - k / h))
        band = k == h // 3  # a painted band in the people's colour round the tent
        c.line(x - half, y - k, x, y - k, CIV[0] if band else t[0])
        c.line(x + 1, y - k, x + half, y - k, CIV[1] if band else t[2])
        if band:
            for xx in range(x - half, x + half + 1):
                _mark(c, xx, y - k)
    c.line(x, y - 3, x, y, P((47, 46, 45)))
    c.px(x, y - h, P((47, 46, 45)))


def fire(c, x, y):
    c.rect(x - 2, y, x + 2, y, STONE[2])
    c.rect(x - 1, y - 3, x + 1, y - 1, P((236, 39, 30)))
    c.px(x, y - 4, P((234, 210, 32)))
    c.px(x, y - 2, P((234, 210, 32)))


def person(c, x, y, tunic=CIV, item=None, head=None):
    """A figure 13 px tall standing at (x, y): head, tunic in the civ colour, legs, and what it carries."""
    c.rect(x - 1, y - 12, x + 1, y - 10, SKIN)
    if head == "helmet":
        c.rect(x - 2, y - 13, x + 2, y - 12, IRON[0])
        c.px(x + 2, y - 12, IRON[2])
        c.rect(x - 1, y - 15, x + 1, y - 14, CIV[0])  # plume, like the old CivSim knight's
        c.px(x - 2, y - 15, CIV[1])
    elif head == "hood":
        c.rect(x - 2, y - 13, x + 2, y - 12, HIDE[1])
    c.rect(x - 2, y - 9, x + 2, y - 4, tunic[1])
    c.line(x - 2, y - 9, x - 2, y - 4, tunic[0])
    c.line(x + 2, y - 9, x + 2, y - 4, tunic[2])
    c.rect(x - 2, y - 3, x - 1, y, P((86, 44, 24)))
    c.rect(x + 1, y - 3, x + 2, y, P((86, 44, 24)))
    hand = (x + 3, y - 7)
    if item == "spear":
        c.line(x + 3, y - 15, x + 3, y, WOOD[1])
        c.rect(x + 2, y - 17, x + 4, y - 16, IRON[0])
    elif item == "club":
        c.line(x + 3, y - 12, x + 3, y - 5, WOOD[1])
        c.rect(x + 2, y - 14, x + 4, y - 12, STONE[1])
    elif item == "sword":
        c.line(x + 4, y - 14, x + 4, y - 6, IRON[0])
        c.line(x + 3, y - 7, x + 5, y - 7, P((223, 143, 19)))
        c.rect(x - 6, y - 10, x - 3, y - 3, IRON[1])  # shield
        c.line(x - 6, y - 10, x - 6, y - 3, IRON[0])
        c.rect(x - 5, y - 8, x - 4, y - 5, CIV[1])
    elif item == "bow":
        for k in range(-5, 6):
            c.px(x + 4 + (1 if abs(k) < 3 else 0), y - 8 + k, WOOD[1])
        c.line(x + 4, y - 13, x + 4, y - 3, P((228, 241, 247)))
    elif item == "crossbow":
        c.line(x + 2, y - 8, x + 7, y - 8, WOOD[1])
        c.line(x + 6, y - 11, x + 6, y - 5, IRON[0])
    elif item == "bundle":
        c.rect(x - 6, y - 11, x - 3, y - 6, HIDE[1])
        c.px(x - 6, y - 11, HIDE[0])
    elif item == "staff":
        c.line(x + 3, y - 15, x + 3, y, WOOD[0])
    elif item == "pick":
        c.line(x + 3, y - 11, x + 3, y - 3, WOOD[1])
        c.line(x + 1, y - 12, x + 6, y - 11, IRON[0])
    elif item == "rod":
        c.line(x + 3, y - 14, x + 3, y - 2, WOOD[0])
        c.rect(x + 2, y - 15, x + 4, y - 14, CIV[0])
    elif item == "sack":
        c.rect(x - 6, y - 10, x - 3, y - 4, THATCH[1])
        c.px(x - 6, y - 10, THATCH[0])
    elif item == "fork":
        c.line(x + 3, y - 14, x + 3, y, WOOD[1])
        c.line(x + 2, y - 15, x + 2, y - 13, IRON[0])
        c.line(x + 4, y - 15, x + 4, y - 13, IRON[0])
    return hand


def horse(c, x, y, rider_item="spear", armoured=False):
    body = tones((138, 86, 45))
    c.rect(x - 7, y - 9, x + 5, y - 5, body[1])
    c.line(x - 7, y - 9, x + 5, y - 9, body[0])
    c.rect(x + 5, y - 13, x + 8, y - 8, body[1])  # neck and head
    c.px(x + 8, y - 13, body[2])
    for lx in (x - 6, x - 3, x + 1, x + 4):
        c.line(lx, y - 4, lx, y, body[2])
    c.line(x - 8, y - 8, x - 9, y - 4, P((47, 46, 45)))  # tail
    if armoured:
        c.rect(x - 7, y - 8, x + 5, y - 6, CIV[1])
    if rider_item is None:
        return
    # rider
    c.rect(x - 2, y - 17, x, y - 15, SKIN)
    c.rect(x - 3, y - 14, x + 1, y - 9, CIV[1])
    c.line(x - 3, y - 14, x - 3, y - 9, CIV[0])
    if armoured:
        c.rect(x - 2, y - 18, x, y - 17, IRON[0])
    if rider_item == "spear":
        c.line(x + 2, y - 19, x + 2, y - 8, WOOD[1])
        c.rect(x + 1, y - 21, x + 3, y - 20, IRON[0])
    elif rider_item == "lance":
        c.line(x - 1, y - 13, x + 9, y - 17, WOOD[0])
        c.px(x + 10, y - 18, IRON[0])


def boat(c, x, y, w, mast=True, sails=1, war=False):
    hull = WOOD
    for k in range(5):
        c.line(x - w + k, y - k, x + w - k, y - k, hull[1] if k else hull[2])
    c.line(x - w + 4, y - 4, x + w - 4, y - 4, hull[0])
    if war:
        for k in range(-w + 6, w - 4, 4):
            c.px(x + k, y - 2, CIV[0])
    if mast:
        c.line(x, y - 4, x, y - 22, WOOD[2])
        for s in range(sails):
            top = y - 21 + s * 9
            c.rect(x + 1, top, x + 9, top + 7, P((242, 194, 121)) if not war else CIV[1])
            c.line(x + 1, top, x + 9, top, P((251, 249, 251)) if not war else CIV[0])


# ------------------------------------------------------------------------------------------------ signs of trades

def sign(c, kind, x, y):
    """A small mark (about 7x7) of the building's trade, at its top-left corner (x, y)."""
    if kind == "grain":
        c.line(x + 3, y + 7, x + 3, y + 1, THATCH[2])
        for k in (1, 3, 5):
            c.px(x + 2, y + k, THATCH[0])
            c.px(x + 4, y + k, THATCH[0])
    elif kind == "axe":
        c.line(x + 1, y + 7, x + 5, y + 1, WOOD[1])
        c.rect(x + 4, y, x + 6, y + 2, IRON[0])
    elif kind == "pot":
        c.rect(x + 1, y + 2, x + 5, y + 6, MUD[1])
        c.line(x + 2, y + 1, x + 4, y + 1, MUD[2])
        c.px(x + 1, y + 3, MUD[0])
    elif kind == "hammer":
        c.line(x + 2, y + 7, x + 4, y + 2, WOOD[1])
        c.rect(x + 2, y, x + 6, y + 2, IRON[1])
    elif kind == "anvil":
        c.rect(x, y + 2, x + 6, y + 3, IRON[1])
        c.rect(x + 2, y + 4, x + 4, y + 6, IRON[2])
        c.line(x, y + 2, x + 6, y + 2, IRON[0])
    elif kind == "flame":
        c.rect(x + 2, y + 3, x + 4, y + 6, P((236, 39, 30)))
        c.px(x + 3, y + 2, P((223, 143, 19)))
        c.px(x + 3, y + 5, P((234, 210, 32)))
    elif kind == "leaf":
        c.rect(x + 2, y + 2, x + 5, y + 4, P((79, 132, 47)))
        c.px(x + 1, y + 5, P((51, 88, 33)))
        c.px(x + 3, y + 2, P((123, 161, 84)))
    elif kind == "needle":
        c.line(x + 1, y + 6, x + 6, y + 1, IRON[0])
        c.line(x + 1, y + 2, x + 4, y + 5, P((183, 10, 9)))
    elif kind == "spool":
        c.rect(x + 1, y + 1, x + 5, y + 6, P((242, 194, 121)))
        c.line(x + 1, y + 3, x + 5, y + 3, P((26, 74, 208)))
        c.line(x + 1, y + 5, x + 5, y + 5, P((26, 74, 208)))
    elif kind == "block":
        c.rect(x + 1, y + 2, x + 6, y + 6, STONE[1])
        c.line(x + 1, y + 2, x + 6, y + 2, STONE[0])
    elif kind == "brick":
        for k in range(3):
            c.rect(x + (k % 2), y + 1 + k * 2, x + 5 + (k % 2), y + 2 + k * 2, P((183, 10, 9)) if k % 2 else P((165, 77, 6)))
    elif kind == "coin":
        c.disc(x + 3.5, y + 3.5, 3, P((223, 143, 19)))
        c.px(x + 3, y + 3, P((234, 210, 32)))
    elif kind == "book":
        c.rect(x, y + 2, x + 6, y + 6, P((251, 249, 251)))
        c.line(x + 3, y + 2, x + 3, y + 6, P((147, 138, 130)))
        c.line(x, y + 6, x + 6, y + 6, P((122, 45, 2)))
    elif kind == "shield":
        c.rect(x + 1, y + 1, x + 5, y + 5, IRON[1])
        c.px(x + 3, y + 6, IRON[1])
        c.rect(x + 2, y + 2, x + 4, y + 4, P((183, 10, 9)))
    elif kind == "swords":
        c.line(x, y + 6, x + 6, y, IRON[0])
        c.line(x, y, x + 6, y + 6, IRON[0])
    elif kind == "horse":
        c.rect(x + 1, y + 3, x + 5, y + 5, WOOD[1])
        c.rect(x + 5, y + 1, x + 6, y + 3, WOOD[1])
        c.px(x + 1, y + 6, WOOD[2])
        c.px(x + 5, y + 6, WOOD[2])
    elif kind == "sheep":
        c.rect(x + 1, y + 2, x + 5, y + 5, P((251, 249, 251)))
        c.px(x + 6, y + 3, P((47, 46, 45)))
        c.px(x + 2, y + 6, P((47, 46, 45)))
        c.px(x + 4, y + 6, P((47, 46, 45)))
    elif kind == "drop":
        c.disc(x + 3, y + 4.5, 2.2, WATER[1])
        c.px(x + 3, y + 1, WATER[1])
        c.px(x + 2, y + 4, WATER[0])
    elif kind == "scroll":
        c.rect(x + 1, y + 1, x + 5, y + 6, P((242, 194, 121)))
        c.line(x + 2, y + 3, x + 4, y + 3, P((47, 46, 45)))
        c.line(x + 2, y + 5, x + 4, y + 5, P((47, 46, 45)))
    elif kind == "wheel":
        c.disc(x + 3.5, y + 3.5, 3.2, WOOD[1])
        c.disc(x + 3.5, y + 3.5, 1.5, WOOD[2])
    elif kind == "saw":
        c.rect(x, y + 3, x + 6, y + 4, IRON[0])
        for k in range(0, 7, 2):
            c.px(x + k, y + 5, IRON[1])
    elif kind == "lime":
        c.rect(x + 1, y + 2, x + 5, y + 6, P((251, 249, 251)))
        c.line(x + 1, y + 2, x + 5, y + 2, P((228, 241, 247)))
    elif kind == "crate":
        c.rect(x, y + 1, x + 6, y + 6, WOOD[1])
        c.line(x, y + 1, x + 6, y + 6, WOOD[2])
    elif kind == "scales":
        c.line(x + 3, y + 1, x + 3, y + 6, P((223, 143, 19)))
        c.line(x, y + 2, x + 6, y + 2, P((223, 143, 19)))
        c.px(x, y + 4, P((223, 143, 19)))
        c.px(x + 6, y + 4, P((223, 143, 19)))
    elif kind == "tower":
        c.rect(x + 1, y + 2, x + 5, y + 6, STONE[1])
        for k in (1, 3, 5):
            c.px(x + k, y + 1, STONE[0])
    elif kind == "bow":
        for k in range(7):
            c.px(x + 2 + (1 if 1 < k < 5 else 0), y + k, WOOD[1])
        c.line(x + 2, y, x + 2, y + 6, P((228, 241, 247)))
    elif kind == "gear":
        c.disc(x + 3.5, y + 3.5, 3, IRON[1])
        c.disc(x + 3.5, y + 3.5, 1, IRON[2])


def board(c, kind, x, y, k=1):
    """The trade's sign on a light board with a dark frame (k = 2: drawn twice as big), its top-left corner at (x, y).
    Clean: the texture pass leaves it alone, so the sign stands out from the building (Patryk, 2026-11-23)."""
    from PIL import Image
    t = canvas(11, 11)
    t.rect(0, 0, 10, 10, P(OUTLINE))
    t.rect(1, 1, 9, 9, P((242, 194, 121)))
    t.line(1, 1, 9, 1, P((251, 249, 251)))
    t.line(1, 9, 9, 9, P((229, 168, 98)))
    sign(t, kind, 2, 2)
    im = t.im.resize((11 * k, 11 * k), Image.NEAREST) if k > 1 else t.im
    c.im.alpha_composite(im, (x, y))
    for yy in range(y, y + 11 * k):
        for xx in range(x, x + 11 * k):
            _mark(c, xx, yy)


# Every building: (sign, wall material, roof kind) by key; its size and era come from the data.
BUILDINGS = {
    "gatherers_hut": ("grain", "wood", "hide"), "woodcutter": ("axe", "wood", "hide"), "hut": (None, "wood", "hide"),
    "storehouse": ("crate", "wood", "hide"), "hearth": ("flame", "stone", "none"), "toolmaker": ("hammer", "wood", "hide"),
    "hunters_lodge": ("bow", "wood", "hide"), "clay_pit": ("pot", "mud", "none"), "herbalist": ("leaf", "wood", "hide"),
    "tailor": ("needle", "wood", "hide"), "potter": ("pot", "mud", "era"), "granary": ("grain", "mud", "era"),
    "stone_house": (None, "stone", "era"), "mason": ("block", "stone", "era"), "weaver": ("spool", "mud", "era"),
    "trading_post": ("scales", "wood", "era"), "bronze_smith": ("anvil", "stone", "era"), "charcoal_burner": ("flame", "mud", "none"),
    "armourer": ("shield", "stone", "era"), "school": ("book", "mud", "era"), "stable": ("horse", "wood", "era"),
    "mint": ("coin", "brick", "era"), "barracks": ("swords", "stone", "era"), "blacksmith": ("anvil", "stone", "era"),
    "weaponsmith": ("swords", "stone", "era"), "builders_yard": ("hammer", "wood", "era"), "province_office": ("scroll", "stone", "era"),
    "siege_workshop": ("wheel", "wood", "era"), "bathhouse": ("drop", "brick", "era"), "academy": ("book", "stone", "era"),
    "brickworks": ("brick", "brick", "era"), "sheepfold": ("sheep", "wood", "era"), "cloth_weaver": ("spool", "stone", "era"),
    "steelworks": ("gear", "stone", "era"), "mill": ("wheel", "stone", "era"), "guildhall": ("scales", "brick", "era"),
    "university": ("book", "brick", "era"), "sawmill": ("saw", "wood", "era"), "lime_kiln": ("lime", "stone", "none"),
}


def wall_tones(kind):
    return {"wood": WOOD, "stone": STONE, "mud": MUD, "brick": BRICK}[kind]


def small_building(key, era, places, rnd):
    """A building on one or two places of a hex: a house in 3/4 view with the sign of its trade."""
    w = PW * places
    c = canvas(w, PH)
    signk, wall, roof = BUILDINGS.get(key, (None, "wood", "era"))
    wt = wall_tones(wall)
    rt = HIDE if roof == "hide" else ROOFS[era]
    x0, x1 = 3, w - 4
    eaves = 12
    if roof == "hide" and places == 1:
        tent(c, w // 2, PH - 3, w - 8, 16, HIDE)
        if signk:
            board(c, signk, w // 2 + 2, PH - 13)
        c.outline(P(OUTLINE))
        return c
    if key in ("charcoal_burner", "lime_kiln"):
        # a kiln: a round mound with smoke and a door
        for k in range(10):
            half = round(10 * (1 - (k / 11) ** 2))
            c.line(w // 2 - half, PH - 3 - k, w // 2, PH - 3 - k, wt[0])
            c.line(w // 2 + 1, PH - 3 - k, w // 2 + half, PH - 3 - k, wt[2])
        door(c, w // 2 - 1, PH - 3, 4)
        for sx, sy in ((w // 2 + 1, PH - 15), (w // 2 + 2, PH - 18), (w // 2 + 1, PH - 21)):
            c.px(sx, sy, P((147, 138, 130)))
        board(c, signk, 1, 1)
        c.outline(P(OUTLINE))
        return c
    if key == "hearth":
        c.disc(w // 2, PH - 7, 7, STONE[1])
        c.disc(w // 2 - 1, PH - 8, 6, STONE[0])
        c.disc(w // 2, PH - 7, 4, STONE[2])
        fire(c, w // 2, PH - 6)
        c.outline(P(OUTLINE))
        return c
    if key == "clay_pit":
        c.rect(6, PH - 10, w - 7, PH - 3, MUD[2])
        c.rect(8, PH - 9, w - 9, PH - 4, MUD[1])
        for k in range(4):
            c.rect(10 + k * 12, PH - 14, 15 + k * 12, PH - 11, MUD[0])
        flag(c, 4, PH - 4, 10)
        c.outline(P(OUTLINE))
        return c
    smoky = key in ("toolmaker", "potter", "bronze_smith", "armourer", "mint", "blacksmith", "weaponsmith", "brickworks",
                    "steelworks", "bathhouse", "cloth_weaver")
    if places == 1:
        box(c, x0, eaves + 1, x1, PH - 3, wt)
        if roof == "none":
            flat_roof(c, x0, x1, eaves + 1, STONE)
        else:
            gable(c, x0, x1, eaves + 1, 10, rt)
        door_frame(c, x0 + 3, PH - 3, 7)
        if signk:
            board(c, signk, x1 - 11, eaves + 3)
        else:
            window(c, x1 - 8, eaves + 5)
        if smoky:
            chimney(c, x0 + 7, eaves - 3)
        if roof != "none":
            pennant(c, (x0 + x1) // 2, eaves - 9)
    else:
        # two volumes: the hall and a lower annex with a lean-to roof (a varied outline, depth between them)
        split = x1 - 15
        box(c, split + 1, eaves + 3, x1, PH - 3, wt)
        lean_to(c, split + 1, x1, eaves + 3, 6, rt)
        box(c, x0, eaves + 1, split, PH - 3, wt)
        if roof == "none":
            flat_roof(c, x0, split, eaves + 1, STONE)
        else:
            gable(c, x0, split, eaves + 1, 11, rt)
        c.line(split + 1, eaves + 3, split + 1, PH - 3, tone(wt[1], -2))  # the hall's shadow on the annex
        door_frame(c, x0 + 4, PH - 3, 8)
        window(c, x0 + 12, eaves + 5)
        banner(c, split - 8, eaves + 3)
        if signk:
            board(c, signk, split + 3, eaves + 5)
        if smoky:
            chimney(c, x0 + 9, eaves - 4)
        if key in ("barracks", "province_office", "guildhall", "university", "academy"):
            flag(c, split - 2, eaves - 6, 10)
    c.outline(P(OUTLINE))
    return c


def door_frame(c, x, y, h):
    """A door painted in the people's colour (2026-11-23, Patryk: colours of the civilization on the buildings) in a
    wooden frame, with a dark plank line, a brass latch and a dark threshold."""
    c.rect(x - 1, y - h, x + 3, y, WOOD[2])
    c.rect(x, y - h + 1, x + 2, y, CIV[1])
    c.line(x, y - h + 1, x + 2, y - h + 1, CIV[0])
    c.line(x + 1, y - h + 2, x + 1, y, CIV[2])
    c.px(x + 2, y - h // 2, P((223, 143, 19)))  # the latch
    c.line(x - 1, y, x + 3, y, P((47, 46, 45)))
    for yy in range(y - h, y + 1):
        for xx in range(x - 1, x + 4):
            _mark(c, xx, yy)


def pennant(c, x, y):
    """A pennant on the apex of a roof, in the people's colour."""
    c.line(x, y, x, y - 6, P((47, 46, 45)))
    c.line(x + 1, y - 6, x + 5, y - 6, CIV[0])
    c.line(x + 1, y - 5, x + 4, y - 5, CIV[1])
    c.line(x + 1, y - 4, x + 2, y - 4, CIV[2])
    for yy in range(y - 6, y + 1):
        for xx in range(x, x + 6):
            _mark(c, xx, yy)


def banner(c, x, y, h=6):
    """A cloth banner hanging from the eaves on a hall's wall, in the people's colour, with a pale border."""
    c.rect(x, y, x + 3, y + h - 2, CIV[1])
    c.line(x, y, x, y + h - 2, CIV[0])
    c.line(x + 3, y, x + 3, y + h - 2, CIV[2])
    c.px(x + 1, y + h - 1, CIV[1])
    c.px(x + 2, y + h - 1, CIV[1])
    c.line(x - 1, y - 1, x + 4, y - 1, WOOD[2])
    for yy in range(y - 1, y + h):
        for xx in range(x - 1, x + 5):
            _mark(c, xx, yy)


def window(c, x, y):
    """A small window: dark opening, a lit sill, a frame cross."""
    c.rect(x, y, x + 3, y + 3, P((29, 23, 16)))
    c.px(x + 1, y + 1, P((63, 167, 201)))
    c.line(x, y + 4, x + 3, y + 4, P((242, 194, 121)))
    c.line(x - 1, y - 1, x + 4, y - 1, WOOD[2])


def chimney(c, x, y):
    """A chimney with a puff of smoke: the workshop is at work."""
    r = ramp((147, 138, 130))
    c.rect(x, y, x + 2, y + 5, r[2])
    c.line(x, y, x, y + 5, r[3])
    c.line(x - 1, y, x + 3, y, r[1])
    for k, (dx, dy) in enumerate(((1, -2), (2, -4), (1, -6))):
        c.px(x + dx, y + dy, P((169, 162, 160)) if k else P((147, 138, 130)))


def lean_to(c, x0, x1, base_y, height, t):
    """A single-slope roof falling to the right, over an annex."""
    r = ramp(t[1])
    for k in range(height):
        y = base_y - k
        right = x1 - round(k * (x1 - x0) / (height * 2.2))
        for x in range(x0, right + 1):
            col = r[2] if (x + k) % 4 else r[1]
            c.px(x, y, tone(col, -1) if x > (x0 + right) / 2 else col)
    c.line(x0, base_y, x1 + 1, base_y, r[0])


def hex_building(key, era, rnd):
    """A building on the whole hex: a small scene inside the hex mask."""
    c = canvas(HW, HH)
    cx, cy = HW // 2, HH // 2
    rt = ROOFS[era]

    def mini_house(x, y, w=12, roof=rt, wall=WOOD):
        box(c, x, y - 8, x + w, y, wall)
        gable(c, x, x + w, y - 8, 6, roof)
        door(c, x + 2, y, 4)

    if key in ("farm", "manor"):
        for y in range(16, HH - 12):
            for x in range(10, HW - 10):
                if inside(x, y, 4):
                    stripe = (y // 4) % 2 == 0
                    c.px(x, y, THATCH[1] if stripe else THATCH[2])
                    if (x * 7 + y * 3) % 13 == 0:
                        c.px(x, y, THATCH[0] if stripe else P((138, 86, 45)))
        if key == "manor":
            box(c, cx - 14, cy - 2, cx + 14, cy + 14, STONE)
            gable(c, cx - 14, cx + 14, cy - 2, 12, rt)
            door(c, cx - 2, cy + 14, 6)
            for k in (-9, 7):
                c.rect(cx + k, cy + 3, cx + k + 2, cy + 6, P((47, 46, 45)))
            flag(c, cx + 16, cy - 2, 14)
        else:
            mini_house(cx + 10, cy + 20, 14, THATCH)
            flag(c, cx - 22, cy + 14, 14)
    elif key == "pasture":
        for x in range(12, HW - 12, 6):
            c.line(x, 22, x, 26, WOOD[1])
            c.line(x, HH - 20, x, HH - 16, WOOD[1])
        c.line(12, 23, HW - 13, 23, WOOD[0])
        c.line(12, HH - 19, HW - 13, HH - 19, WOOD[0])
        for x, y in ((30, 44), (50, 40), (62, 52), (40, 56)):
            c.rect(x, y, x + 7, y + 3, P((251, 249, 251)) if (x // 10) % 2 else WOOD[1])
            c.px(x + 8, y, P((47, 46, 45)))
            c.px(x + 1, y + 4, P((47, 46, 45)))
            c.px(x + 6, y + 4, P((47, 46, 45)))
    elif key in ("quarry", "copper_mine", "tin_mine", "iron_mine"):
        # Step 3 (2026-11-23): rocks shaded as bodies, like the old CivSim stone.
        if key == "quarry":
            boulder(c, cx - 8, cy + 6, 20, 16, (147, 138, 130), seed=3)
            boulder(c, cx + 16, cy + 14, 11, 9, (147, 138, 130), seed=5)
            for x, y in ((cx - 20, cy + 22), (cx + 2, cy + 24)):
                box(c, x, y, x + 8, y + 5, STONE)
                c.line(x, y, x + 8, y, STONE[0])
        else:
            ore = {"copper_mine": (201, 113, 21), "tin_mine": (185, 216, 230), "iron_mine": (105, 56, 41)}[key]
            boulder(c, cx, cy + 6, 28, 22, (113, 111, 108), seed=len(key))
            for k, (x, y) in enumerate(((cx - 14, cy - 4), (cx + 10, cy - 2), (cx - 4, cy - 10), (cx + 16, cy + 6), (cx - 18, cy + 8))):
                boulder(c, x, y, 2.5, 2, ore, seed=k, flat_bottom=False)
            c.rect(cx - 6, cy + 12, cx + 6, cy + 24, P((29, 23, 16)))  # the shaft
            for x in (cx - 7, cx + 7):
                c.line(x, cy + 11, x, cy + 24, WOOD[1])
            c.line(cx - 7, cy + 11, cx + 7, cy + 11, WOOD[0])
            c.rect(cx + 14, cy + 20, cx + 22, cy + 24, WOOD[1])  # a cart
            boulder(c, cx + 18, cy + 18, 4, 2.5, ore, seed=9, flat_bottom=False)
        flag(c, cx - 26, cy + 18, 12)
    elif key in ("fishing_hut", "raft_landing", "harbour", "shipyard"):
        for y in range(cy + 6, HH):
            for x in range(HW):
                if inside(x, y, 1):
                    c.px(x, y, WATER[1] if (x + y * 3) % 13 else WATER[0])
        c.rect(cx - 2, cy + 4, cx + 26, cy + 8, WOOD[1])  # pier
        c.line(cx - 2, cy + 4, cx + 26, cy + 4, WOOD[0])
        if key == "fishing_hut":
            mini_house(cx - 26, cy + 4, 14, ROOFS["neolithic"])
            for k in range(5):
                c.line(cx + 4 + k * 3, cy - 6, cx + 4 + k * 3, cy + 2, P((147, 138, 130)))  # net
        elif key == "raft_landing":
            mini_house(cx - 26, cy + 4, 14, ROOFS["neolithic"])
            for k in range(2):
                c.rect(cx + 2 + k * 12, cy + 12, cx + 11 + k * 12, cy + 15, WOOD[2])
                c.line(cx + 2 + k * 12, cy + 12, cx + 11 + k * 12, cy + 12, WOOD[0])
        elif key == "harbour":
            box(c, cx - 30, cy - 8, cx - 8, cy + 4, STONE)
            gable(c, cx - 30, cx - 8, cy - 8, 8, rt)
            boat(c, cx + 14, cy + 22, 12, sails=1)
        else:
            boat(c, cx + 2, cy + 2, 18, mast=True, sails=2)
            c.line(cx - 22, cy + 6, cx + 26, cy + 6, WOOD[2])
            box(c, cx - 34, cy - 10, cx - 18, cy + 4, WOOD)
            gable(c, cx - 34, cx - 18, cy - 10, 7, rt)
        flag(c, cx + 26, cy + 4, 14)
    elif key in ("great_storehouse",):
        box(c, cx - 24, cy - 4, cx + 24, cy + 18, WOOD)
        gable(c, cx - 24, cx + 24, cy - 4, 16, rt)
        door(c, cx - 4, cy + 18, 9)
        c.rect(cx - 3, cy + 10, cx + 3, cy + 18, P((47, 46, 45)))
        for x in (cx - 18, cx + 10):
            sign(c, "crate", x, cy + 6)
    elif key == "castle":
        wall = STONE
        for y in range(cy - 8, cy + 24):
            for x in range(cx - 30, cx + 31):
                edge = x in (cx - 30, cx + 30) or y in (cy - 8, cy + 23)
                if edge or (cx - 28 <= x <= cx + 28 and cy + 18 <= y):
                    c.px(x, y, wall[1] if x > cx - 28 else wall[0])
        box(c, cx - 30, cy + 10, cx + 30, cy + 24, STONE)
        for tx in (cx - 34, cx + 26):
            box(c, tx, cy - 6, tx + 8, cy + 24, STONE)
            for k in range(0, 9, 3):
                c.rect(tx + k, cy - 9, tx + k + 1, cy - 7, STONE[0])
        box(c, cx - 9, cy - 22, cx + 9, cy + 12, STONE)  # keep
        for k in range(0, 19, 4):
            c.rect(cx - 9 + k, cy - 25, cx - 8 + k, cy - 23, STONE[0])
        c.rect(cx - 4, cy + 16, cx + 4, cy + 24, P((47, 46, 45)))  # gate
        flag(c, cx, cy - 25, 12)
        for bx in (cx - 7, cx + 4):
            banner(c, bx, cy - 14, 9)  # the people's banners on the keep
    else:
        mini_house(cx - 8, cy + 10, 16)
    c.outline(P(OUTLINE))
    return c


HEX_SIGNS = {"farm": "grain", "manor": "grain", "pasture": "horse", "quarry": "block", "copper_mine": "hammer", "tin_mine": "hammer",
             "iron_mine": "hammer", "fishing_hut": "drop", "raft_landing": "drop", "harbour": "crate", "shipyard": "wheel",
             "great_storehouse": "crate", "castle": "shield"}

# ------------------------------------------------------------------------------------------------ settlement centres

def centre(level):
    c = canvas(HW, HH)
    cx, cy = HW // 2, HH // 2 + 4
    if level == "camp":
        tent(c, cx - 14, cy + 6, 22, 18, HIDE)
        tent(c, cx + 12, cy + 12, 18, 14, HIDE)
        tent(c, cx + 2, cy - 6, 16, 12, HIDE)
        fire(c, cx - 1, cy + 16)
        flag(c, cx + 22, cy + 4, 16)
    elif level == "village":
        for x, y, w in ((cx - 24, cy + 2, 14), (cx + 4, cy - 4, 16), (cx - 6, cy + 16, 14), (cx + 16, cy + 14, 12)):
            box(c, x, y - 9, x + w, y, MUD)
            gable(c, x, x + w, y - 9, 7, ROOFS["neolithic"])
            door(c, x + 3, y, 4)
        fire(c, cx - 8, cy + 4)
        flag(c, cx + 24, cy - 6, 16)
    else:
        # Step 3 (2026-11-23): the old CivSim castle: round towers with conical roofs, battlements, a gate, the hall behind.
        roof = (183, 10, 9) if level == "city" else (187, 110, 70)
        stone = (147, 138, 130)
        if level == "city":
            stone_wall(c, cx - 30, cx + 30, cy + 2, cy + 22, stone)
            for tx in (cx - 36, cx + 26):
                tower(c, tx, tx + 10, cy - 12, cy + 22, stone, roof)
            tower(c, cx - 7, cx + 7, cy - 26, cy + 2, stone, roof)  # keep
            c.rect(cx - 4, cy + 12, cx + 4, cy + 22, P((86, 44, 24)))  # gate
            c.line(cx - 4, cy + 12, cx + 4, cy + 12, P((29, 23, 16)))
        else:
            stone_wall(c, cx - 22, cx + 22, cy + 6, cy + 22, stone)
            for tx in (cx - 27, cx + 19):
                tower(c, tx, tx + 8, cy - 4, cy + 22, stone, roof)
            box(c, cx - 9, cy - 10, cx + 9, cy + 6, STONE)  # the hall
            gable(c, cx - 9, cx + 9, cy - 10, 9, tones(roof))
            c.rect(cx - 3, cy + 14, cx + 3, cy + 22, P((86, 44, 24)))
    c.outline(P(OUTLINE))
    return c


# ------------------------------------------------------------------------------------------------ units (64x64)

UNIT_FIGURES = {
    "settlers": [("bundle", None), ("staff", None), ("sack", None)],
    "scouts": [("staff", "hood"), ("bow", "hood")],
    "prospectors": [("pick", None), ("pick", "hood")],
    "surveyors": [("rod", None), ("staff", None)],
    "porters": [("sack", None), ("bundle", None)],
    "levy": [("fork", None), ("club", None), ("fork", None)],
    "hunters": [("spear", "hood"), ("bow", "hood")],
    "warriors": [("club", None), ("club", None), ("spear", None)],
    "archers": [("bow", None), ("bow", None), ("bow", None)],
    "spearmen": [("spear", "helmet"), ("spear", "helmet"), ("spear", "helmet")],
    "swordsmen": [("sword", "helmet"), ("sword", "helmet"), ("sword", "helmet")],
    "crossbowmen": [("crossbow", "helmet"), ("crossbow", "helmet"), ("crossbow", "helmet")],
}


def grow(c, most=2.0, margin=3):
    """Scales what is drawn on the canvas (nearest neighbour) up to `most` times, keeping it inside the canvas, standing on
    the bottom: units get figures big enough to read while the pixels stay square."""
    from PIL import Image
    box_ = c.im.getbbox()
    if box_ is None:
        return
    part = c.im.crop(box_)
    k = min(most, (c.w - 2 * margin) / part.width, (c.h - 2 * margin) / part.height)
    k = max(1, int(k))
    part = part.resize((part.width * k, part.height * k), Image.NEAREST)
    c.im = Image.new("RGBA", (c.w, c.h), (0, 0, 0, 0))
    c.im.alpha_composite(part, ((c.w - part.width) // 2, c.h - margin - part.height))


def unit(key):
    c = canvas(64, 64)
    base = 52
    if key in UNIT_FIGURES:
        figs = UNIT_FIGURES[key]
        spots = [(22, base - 6), (40, base - 4), (31, base + 4)][:len(figs)]
        for (item, head), (x, y) in sorted(zip(figs, spots), key=lambda f: f[1][1]):
            person(c, x, y, CIV, item, head)
    elif key == "caravan":
        c.rect(14, base - 12, 34, base - 4, WOOD[1])  # cart
        c.line(14, base - 12, 34, base - 12, WOOD[0])
        c.rect(16, base - 17, 32, base - 13, THATCH[1])
        c.disc(18, base - 2, 3, WOOD[2])
        c.disc(30, base - 2, 3, WOOD[2])
        c.rect(36, base - 10, 46, base - 6, P((138, 86, 45)))  # ox
        c.rect(46, base - 13, 49, base - 8, P((138, 86, 45)))
        for lx in (37, 40, 43, 45):
            c.line(lx, base - 5, lx, base - 2, P((105, 56, 41)))
        person(c, 52, base + 2, CIV, "staff")
    elif key == "horsemen":
        horse(c, 26, base, "spear")
        horse(c, 36, base + 5, "spear")
    elif key == "knights":
        horse(c, 27, base, "lance", armoured=True)
        horse(c, 32, base + 5, "lance", armoured=True)
    elif key == "catapult":
        c.rect(20, base - 7, 40, base - 4, WOOD[1])
        c.line(20, base - 7, 40, base - 7, WOOD[0])
        c.line(24, base - 7, 36, base - 21, WOOD[2])
        c.line(25, base - 7, 37, base - 21, WOOD[1])
        c.rect(34, base - 24, 38, base - 21, STONE[1])
        c.disc(23, base - 2, 2.5, WOOD[2])
        c.disc(37, base - 2, 2.5, WOOD[2])
        person(c, 44, base + 1, CIV, "club", "helmet")
    elif key in ("transport", "trade_ship", "warship"):
        boat(c, 32, base, 22, sails=1 if key == "transport" else 2, war=key == "warship")
        if key == "trade_ship":
            for x in (20, 26):
                sign(c, "crate", x, base - 12)
        if key == "transport":
            for x in (20, 26, 40):
                c.rect(x, base - 10, x + 2, base - 7, CIV[1])
                c.px(x + 1, base - 11, SKIN)
    grow(c)
    c.outline(P(OUTLINE))
    return c


# ------------------------------------------------------------------------------------------------ goods (32x32)

def good(key):
    c = canvas(32, 32)
    g = key
    if g == "food":
        c.disc(13, 18, 7, P((183, 10, 9)))
        c.disc(11, 16, 2, P((236, 39, 30)))
        c.line(14, 11, 16, 7, WOOD[1])
        c.rect(17, 7, 21, 9, P((79, 132, 47)))
        sign(c, "grain", 19, 14)
    elif g == "wood":
        for k, y in enumerate((20, 15, 10)):
            c.rect(6 + k * 2, y, 25 - k * 2, y + 4, WOOD[1])
            c.line(6 + k * 2, y, 25 - k * 2, y, WOOD[0])
            c.disc(6 + k * 2, y + 2, 2, THATCH[1])
    elif g in ("stone", "clay", "bricks", "mortar", "charcoal", "copper", "tin", "iron", "bronze"):
        col = {"stone": (147, 138, 130), "clay": (204, 126, 87), "bricks": (183, 10, 9), "mortar": (228, 241, 247),
               "charcoal": (62, 60, 58), "copper": (201, 113, 21), "tin": (185, 216, 230), "iron": (105, 56, 41),
               "bronze": (223, 143, 19)}[g]
        t = tones(col)
        if g in ("copper", "tin", "iron", "bronze"):  # bars
            for k, y in enumerate((20, 14)):
                c.rect(7 + k * 4, y, 24 - k * 4, y + 5, t[1])
                c.line(7 + k * 4, y, 24 - k * 4, y, t[0])
                c.line(24 - k * 4, y, 24 - k * 4, y + 5, t[2])
        elif g == "bricks":
            for row in range(3):
                for k in range(2):
                    x = 6 + k * 10 + (row % 2) * 5
                    c.rect(x, 12 + row * 5, x + 8, 15 + row * 5, t[1])
                    c.line(x, 12 + row * 5, x + 8, 12 + row * 5, t[0])
        else:  # a heap of lumps, shaded like the old CivSim stone and filling the icon
            boulder(c, 12, 20, 9, 8, col, seed=1)
            boulder(c, 21, 21, 8, 7, col, seed=2)
            boulder(c, 16, 13, 8, 7, col, seed=3)
    elif g == "hides":
        c.rect(8, 9, 24, 23, HIDE[1])
        for x, y in ((7, 9), (24, 9), (7, 23), (24, 23)):
            c.px(x, y, HIDE[0])
        c.line(8, 9, 24, 9, HIDE[0])
    elif g in ("tools",):
        sign_big(c, "hammer")
    elif g in ("clothing", "cloth", "textiles", "wool"):
        col = {"clothing": CIV[1], "cloth": (26, 74, 208), "textiles": (242, 194, 121), "wool": (251, 249, 251)}[g]
        t = tones(col) if g != "clothing" else (CIV[0], CIV[1], CIV[2])
        if g == "wool":
            for x, y in ((12, 16), (19, 16), (15, 11), (16, 20)):
                c.disc(x, y, 5, t[1])
            c.disc(13, 13, 2, t[0])
        elif g == "clothing":
            c.rect(10, 10, 22, 24, t[1])
            c.rect(6, 10, 9, 16, t[1])
            c.rect(23, 10, 26, 16, t[1])
            c.line(10, 10, 22, 10, t[0])
        else:
            c.rect(8, 10, 24, 22, t[1])
            c.disc(8, 16, 4, t[2])
            c.line(8, 10, 24, 10, t[0])
    elif g == "pottery":
        c.disc(16, 19, 7, MUD[1])
        c.rect(13, 9, 19, 13, MUD[1])
        c.line(12, 9, 20, 9, MUD[2])
        c.disc(13, 17, 2, MUD[0])
    elif g == "horses":
        horse(c, 16, 28, rider_item=None)
    elif g == "coins":
        for x, y in ((12, 19), (19, 17), (15, 13)):
            c.disc(x, y, 5, P((223, 143, 19)))
            c.disc(x - 1, y - 1, 2, P((234, 210, 32)))
    elif g == "planks":
        for k in range(4):
            c.rect(6, 10 + k * 4, 26, 12 + k * 4, WOOD[1] if k % 2 else WOOD[0])
    elif g.endswith("_weapons"):
        metal = {"stone": STONE, "bronze": tones((223, 143, 19)), "iron": IRON, "steel": tones((185, 216, 230))}[g.split("_")[0]]
        c.line(8, 25, 24, 7, WOOD[1], 2)
        c.rect(21, 5, 26, 10, metal[1])
        c.line(10, 6, 24, 24, metal[0], 2)
        c.line(9, 20, 13, 24, P((86, 44, 24)), 2)
    elif g.endswith("_armour"):
        metal = {"hide": HIDE, "bronze": tones((223, 143, 19)), "iron": IRON, "steel": tones((185, 216, 230))}[g.split("_")[0]]
        c.rect(9, 8, 23, 25, metal[1])
        c.line(9, 8, 23, 8, metal[0])
        c.line(9, 8, 9, 25, metal[0])
        c.line(23, 8, 23, 25, metal[2])
        c.rect(14, 8, 18, 10, metal[2])  # the neck
    c.outline(P(OUTLINE))
    return c


def sign_big(c, kind):
    if kind == "hammer":
        c.line(10, 26, 18, 12, WOOD[1], 2)
        c.rect(13, 6, 25, 11, IRON[1])
        c.line(13, 6, 25, 6, IRON[0])


# ------------------------------------------------------------------------------------------------ hex tiles at 2x

# Grounds closer to the old CivSim grass (Patryk, 2026-11-23): deeper greens than the data's map colours.
OLD_GROUND = {"grassland": (79, 132, 47)}


def biome_tile(key, color, variant):
    rnd = random.Random(f"{key}-{variant}-2x")
    base = P(OLD_GROUND.get(key, color))
    light, dark = sh(base, 1), sh(base, -1)
    c = canvas(HW, HH)
    for y in range(HH):
        for x in range(HW):
            if MASK[y][x]:
                c.px(x, y, base)

    def speckle(n, col):
        for _ in range(n):
            x, y = rnd.randrange(2, HW - 2), rnd.randrange(2, HH - 2)
            if inside(x, y, 2):
                c.px(x, y, col)

    water = key in ("deep_ocean", "shallow_sea", "coast", "lake")
    if water:
        for _ in range(16):
            x, y = rnd.randrange(10, HW - 16), rnd.randrange(8, HH - 8)
            if inside(x, y, 4) and inside(x + 8, y, 4):
                c.line(x, y, x + 5, y, light)
                c.line(x + 6, y - 1, x + 8, y - 1, light)
        speckle(30, dark)
    elif key in ("deciduous_forest", "taiga", "jungle"):
        speckle(60, dark)
        n = {"jungle": 26, "deciduous_forest": 18, "taiga": 20}[key]
        trees = sorted((rnd.randrange(8, HW - 8), rnd.randrange(10, HH - 6)) for _ in range(n))
        for x, y in sorted(trees, key=lambda t: t[1]):
            if not inside(x, y, 5):
                continue
            if key == "taiga":
                fir = P((45, 77, 29))
                for k in range(7):
                    half = k // 2 + 1
                    c.line(x - half, y - 7 + k, x, y - 7 + k, sh(fir, 1))
                    c.line(x + 1, y - 7 + k, x + half, y - 7 + k, fir)
                c.px(x, y + 1, P((86, 44, 24)))
            else:
                crown = P((57, 99, 37)) if key == "deciduous_forest" else P((51, 88, 33))
                c.disc(x, y - 3, 3.2, crown)
                c.disc(x - 1, y - 4, 1.4, sh(crown, 1))
                c.px(x + 2, y - 1, sh(crown, -1))
                c.line(x, y, x, y + 1, P((86, 44, 24)))
    elif key in ("grassland", "steppe", "savanna"):
        speckle(50, dark)
        for _ in range(40 if key == "grassland" else 28):
            x, y = rnd.randrange(4, HW - 4), rnd.randrange(4, HH - 4)
            if inside(x, y, 3):
                c.px(x, y, light)
                c.px(x - 1, y + 1, light)
                c.px(x + 1, y + 1, light if key != "steppe" else dark)
        if key == "savanna":
            for _ in range(4):
                x, y = rnd.randrange(14, HW - 14), rnd.randrange(14, HH - 10)
                if inside(x, y, 8):
                    tree = P((57, 99, 37))
                    c.rect(x - 4, y - 3, x + 4, y - 2, tree)
                    c.line(x - 3, y - 4, x + 3, y - 4, sh(tree, 1))
                    c.line(x, y - 1, x, y + 2, P((86, 44, 24)))
    elif key == "desert":
        speckle(50, light)
        speckle(25, dark)
        for _ in range(4):
            x, y = rnd.randrange(14, HW - 30), rnd.randrange(16, HH - 14)
            if inside(x, y, 6):
                c.line(x, y, x + 10, y - 3, light)
                c.line(x + 10, y - 3, x + 18, y, dark)
    elif key in ("tundra", "ice"):
        speckle(50, light)
        speckle(20 if key == "tundra" else 10, dark)
    elif key == "swamp":
        pool = P((19, 103, 157))
        for _ in range(7):
            x, y = rnd.randrange(14, HW - 14), rnd.randrange(14, HH - 12)
            if inside(x, y, 6):
                c.rect(x - 4, y, x + 4, y + 2, pool)
                c.line(x - 4, y, x - 1, y, sh(pool, 1))
                c.px(x + 5, y - 2, P((79, 132, 47)))
        speckle(40, light)
    else:
        speckle(40, light)
        speckle(30, dark)
    for y in range(HH):
        for x in range(HW):
            if not MASK[y][x]:
                continue
            ul = (x - 1 < 0 or not MASK[y][x - 1]) or (y - 1 < 0 or not MASK[y - 1][x])
            lr = (x + 1 >= HW or not MASK[y][x + 1]) or (y + 1 >= HH or not MASK[y + 1][x])
            if ul and y < HH // 2:
                c.px(x, y, light)
            elif lr and y >= HH // 2:
                c.px(x, y, dark)
    c.save("biome", f"biome_{key}_{variant:02d}")


def relief(kind, variant):
    rnd = random.Random(f"{kind}-{variant}-2x")
    c = canvas(HW, HH)
    if kind == "hills":
        for i in range(3):
            cx = 22 + i * 26 + rnd.randrange(-4, 5)
            cy = 56 + rnd.randrange(-6, 5) - (i % 2) * 10
            boulder(c, cx, cy, 13 + rnd.randrange(0, 4), 9, (138, 86, 45), seed=i + variant * 3)
    else:
        rock, snow = P((113, 111, 108)), P((251, 249, 251))
        peaks = [(32, 64, 20), (62, 66, 24)] if variant == 1 else [(26, 64, 18), (50, 66, 26), (72, 64, 16)]
        for px_, base_y, half in peaks:
            top = base_y - int(half * 1.6)
            for y in range(top, base_y + 1):
                w = int((y - top) / 1.6)
                for x in range(px_ - w, px_ + w + 1):
                    if not inside(x, y, 2):
                        continue
                    col = sh(rock, 1) if x <= px_ else sh(rock, -1)
                    if y < top + 8:
                        col = snow if x <= px_ else P((169, 162, 160))
                    c.px(x, y, col)
    c.outline(P((62, 60, 58)))
    c.save("terrain", f"terrain_{kind}_{variant:02d}")


def feature(key):
    c = canvas(HW, HH)
    if key == "oasis":
        water = P((63, 167, 201))
        c.disc(42, 50, 12, water)
        c.disc(38, 46, 4, sh(water, 1))
        palm = P((51, 88, 33))
        c.line(60, 56, 62, 32, P((122, 45, 2)), 3)
        for dx, dy in ((-10, 4), (10, 4), (-8, -2), (8, -2)):
            c.line(62, 30, 62 + dx, 30 + dy, palm, 3)
    elif key == "reef":
        coral, rnd = P((240, 154, 168)), random.Random("reef-2x")
        for _ in range(18):
            x, y = rnd.randrange(20, HW - 20), rnd.randrange(20, HH - 20)
            if inside(x, y, 8):
                c.rect(x, y, x + 2, y + 2, coral)
                c.px(x, y, sh(coral, 1))
    else:
        ice, rnd = P((228, 241, 247)), random.Random("ice-2x")
        for _ in range(10):
            x, y = rnd.randrange(16, HW - 24), rnd.randrange(16, HH - 20)
            if inside(x, y, 8):
                c.rect(x, y, x + 10, y + 4, ice)
                c.line(x, y, x + 10, y, P((251, 249, 251)))
    c.outline(P((62, 60, 58)) if key != "reef" else P((105, 56, 41)))
    c.save("terrain", f"terrain_{key}_01")


CORNERS = [(HW - 1, HH // 2), (HW * 3 // 4, HH - 1), (HW // 4, HH - 1), (0, HH // 2), (HW // 4, 0), (HW * 3 // 4 - 1, 0)]
EDGES = {"n": (4, 5), "ne": (5, 0), "se": (0, 1), "s": (1, 2), "sw": (2, 3), "nw": (3, 4)}


def river(edge, width_name):
    c = canvas(HW, HH)
    a, b = EDGES[edge]
    (x0, y0), (x1, y1) = CORNERS[a], CORNERS[b]
    width = 4 if width_name == "stream" else 8
    water = P((25, 119, 208))
    cx, cy = HW / 2, HH / 2
    inset = 0.05 if width_name == "stream" else 0.08
    ix0, iy0 = round(x0 + (cx - x0) * inset), round(y0 + (cy - y0) * inset)
    ix1, iy1 = round(x1 + (cx - x1) * inset), round(y1 + (cy - y1) * inset)
    c.line(ix0, iy0, ix1, iy1, sh(water, -1), width + 2)  # a darker bank so rivers read on any biome
    c.line(ix0, iy0, ix1, iy1, water, width)
    c.line(ix0, iy0, ix1, iy1, sh(water, 1), max(1, width // 4))
    c.save("terrain", f"terrain_river_{edge}_{width_name}")


def grove(variant):
    """Trees on open land (data: trees.groveChance), drawn over the biome: round crowns lit from the top left, an outline,
    in the same style as the forest tiles; they stand near the edges, so the building places stay readable."""
    rnd = random.Random(f"grove-{variant}")
    c = canvas(HW, HH)
    spots = [(20, 26), (74, 24), (14, 56), (80, 58), (34, 72), (62, 74), (48, 14)]
    rnd.shuffle(spots)
    crown = P((57, 99, 37))
    for x, y in sorted(spots[:4 + variant], key=lambda t: t[1]):
        x += rnd.randrange(-3, 4)
        y += rnd.randrange(-3, 4)
        c.line(x, y - 1, x, y + 3, P((86, 44, 24)), 2)
        c.disc(x, y - 6, 5.5, crown)
        c.disc(x - 2, y - 8, 2.2, sh(crown, 1))
        c.disc(x + 2, y - 4, 2, sh(crown, -1))
    c.outline(P(OUTLINE))
    c.save("terrain", f"terrain_grove_{variant:02d}")


# ------------------------------------------------------------------------------------------------ everything

def generate(root):
    init_materials()
    with open(os.path.join(root, "data", "sim", "game.json"), encoding="utf-8") as f:
        d = json.load(f)
    with open(os.path.join(root, "data", "biomes.json"), encoding="utf-8") as f:
        biomes = json.load(f)["biomes"]
    era = {t["key"]: t["era"] for t in d["technologies"]}
    for b in biomes:
        color = tuple(int(b["color"].lstrip("#")[i:i + 2], 16) for i in (0, 2, 4))
        for v in (1, 2, 3):
            biome_tile(b["key"], color, v)
    for kind in ("hills", "mountains"):
        for v in (1, 2):
            relief(kind, v)
    for key in ("oasis", "reef", "ice_field"):
        feature(key)
    for edge in EDGES:
        for width in ("stream", "river"):
            river(edge, width)
    for v in (1, 2, 3):
        grove(v)
    for b in d["buildings"]:
        rnd = random.Random(b["key"])
        e = era.get(b["tech"], "stone_age")
        size = b.get("size", 4)
        pic = hex_building(b["key"], e, rnd) if size == 4 else small_building(b["key"], e, 1 if size == 1 else 2, rnd)
        if size == 4 and b["key"] in HEX_SIGNS:
            board(pic, HEX_SIGNS[b["key"]], HW // 2 - 11, 4, 2)
            pic.outline(P(OUTLINE))
        texturize(pic.im, seed=sum(map(ord, b["key"])), strength=0.35, protect=getattr(pic, "protect", set()))
        pic.save("buildings", f"building_{b['key']}")
    for level in d["settlementLevels"]:
        centre(level["key"]).save("buildings", f"settlement_{level['key']}")
    for u in d["units"]:
        unit(u["key"]).save("units", f"unit_{u['key']}")
    for g in d["goods"]:
        good(g["key"]).save("goods", f"good_{g['key']}")
    finish(root)
    generate_works()  # after finish: walls carry their own stone and wood texture


# Patryk's own icons from the first CivSim (art/legacy/map_visuals, 32x32), used as they are for these goods.
OLD_ICONS = {"food": "wheat", "wood": "wood_log", "stone": "stone", "iron": "iron", "hides": "cow"}

GRASS = ("grassland", "savanna", "steppe", "deciduous_forest", "jungle", "taiga", "swamp", "tundra")


def finish(root):
    """The look of the first CivSim over everything drawn above (Patryk, 2026-11-23): a texture on every surface,
    upright grass strokes on grounds, and Patryk's old icons for the goods they show."""
    from PIL import Image
    out = os.path.join(root, "art", "placeholder")
    for layer in ("biome", "terrain", "units", "goods"):  # buildings get their own texture with clean signs
        folder = os.path.join(out, layer)
        for name in sorted(os.listdir(folder)):
            if not name.endswith(".png") or (layer == "units" and name.endswith("_idle_01.png")):
                continue  # the legacy units keep their look
            path = os.path.join(folder, name)
            im = Image.open(path).convert("RGBA")
            key = name[:-4]
            grass = layer == "biome" and any(key.startswith(f"biome_{g}_") for g in GRASS)
            texturize(im, seed=sum(map(ord, key)), strength=1.3 if layer in ("biome", "units") else 1.0, strokes=grass)
            im.save(path)
    for good_key, old in OLD_ICONS.items():
        src = os.path.join(root, "art", "legacy", "map_visuals", old + ".png")
        if os.path.exists(src):
            Image.open(src).convert("RGBA").save(os.path.join(out, "goods", f"good_{good_key}.png"))


# ------------------------------------------------------------------------------------------------ texture (old CivSim look)

def _hash(x, y, seed):
    h = (x * 374761393 + y * 668265263 + seed * 2246822519) & 0xFFFFFFFF
    h = ((h ^ (h >> 13)) * 1274126177) & 0xFFFFFFFF
    return (h ^ (h >> 16)) / 0xFFFFFFFF


def _noise(x, y, seed, cell):
    """Value noise in cells of `cell` pixels, bilinear: blotches like the old CivSim grass and stone."""
    gx, gy = x / cell, y / cell
    x0, y0 = int(gx), int(gy)
    fx, fy = gx - x0, gy - y0
    a, b = _hash(x0, y0, seed), _hash(x0 + 1, y0, seed)
    c, d = _hash(x0, y0 + 1, seed), _hash(x0 + 1, y0 + 1, seed)
    top, bot = a + (b - a) * fx, c + (d - c) * fx
    return top + (bot - top) * fy


def texturize(im, seed=1, strength=1.0, outline=OUTLINE, strokes=False, protect=frozenset()):
    """The old CivSim texture over flat colours: every pixel of a surface takes a lighter or darker palette tone by a mix of
    blotchy and fine noise, wood in horizontal grain, so grass, stone and wood read as materials. The outline, the
    civilization magentas and tiny details (lone pixels) stay as drawn."""
    w, h = im.size
    src = im.copy()
    px = src.load()
    out = im.load()
    keep = {tuple(P(outline))}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a < 128 or (r, g, b) in keep or (g < 5 and abs(r - b) < 5 and r > 100) or (x, y) in protect:
                continue
            # same-colour neighbours: only surfaces get a texture, not single-pixel details
            same = sum(1 for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))
                       if 0 <= x + dx < w and 0 <= y + dy < h and px[x + dx, y + dy][:3] == (r, g, b))
            if same < 3:
                continue
            woody = r > g > b and r - b > 60 and r < 200  # browns: grain along the board
            if strokes:  # grass: short upright strokes, as in the old CivSim grass
                n = 0.55 * _noise(x * 3, y, seed, 4) + 0.45 * _hash(x, y // 2, seed + 7)
            else:
                n = 0.6 * _noise(x, y * (4 if woody else 1), seed, 3) + 0.4 * _hash(x, y, seed + 7)
            step = 0
            if n > 1 - 0.22 * strength:
                step = 1
            elif n < 0.24 * strength:
                step = -1
            if n < 0.08 * strength:
                step = -2
            if step:
                out[x, y] = tuple(tone((r, g, b), step)) + (255,)
    return im


_TONES = {}


def tone(c, step):
    """A palette colour `step` steps lighter or darker of the same hue (the old CivSim ramps: greens stay green, stone
    stays grey), falling back to the plain shade."""
    key = (tuple(c), step)
    if key in _TONES:
        return _TONES[key]
    import colorsys
    h0, l0, s0 = colorsys.rgb_to_hls(*(v / 255 for v in c))
    best, best_d = None, 9.0
    for p in G["palette"]:
        h, l, sat = colorsys.rgb_to_hls(*(v / 255 for v in p))
        dl = l - l0
        if (dl > 0.03) != (step > 0) or abs(dl) < 0.03:
            continue
        dh = min(abs(h - h0), 1 - abs(h - h0)) if s0 > 0.12 and sat > 0.12 else (0 if s0 <= 0.12 and sat <= 0.15 else 1)
        d = dh * 4 + abs(sat - s0) + abs(abs(dl) - 0.07 * abs(step)) * 3
        if d < best_d:
            best, best_d = p, d
    result = best if best is not None and best_d < 1.2 else sh(c, step)
    _TONES[key] = result
    return result

# ------------------------------------------------------------------------------------------------ volume (step 3: shapes with light)

LIGHT = (-0.55, -0.65, 0.52)  # from the top left, a little from the front


def ramp(rgb):
    """Four tones of one material, dark to highlight, from the palette (the old CivSim stone and grass ramps)."""
    base = P(rgb)
    return [tone(base, -2), tone(base, -1), base, tone(base, 1), tone(base, 2)]


def lit(r, nx, ny, nz):
    """The tone of a surface whose normal is (nx, ny, nz) under LIGHT, from a five-tone ramp."""
    d = nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2]
    return r[0] if d < -0.1 else r[1] if d < 0.25 else r[2] if d < 0.6 else r[3] if d < 0.85 else r[4]


def boulder(c, cx, cy, rx, ry, rgb, seed=1, flat_bottom=True):
    """A rounded rock shaded as a body: the old CivSim stone. Its rim wobbles a little, so no two look alike."""
    r = ramp(rgb)
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            u, v = (x - cx) / rx, (y - cy) / ry
            ang = math.atan2(v, u)
            wobble = 1 + 0.08 * math.sin(ang * 3 + seed) + 0.05 * math.sin(ang * 5 + seed * 2)
            d2 = (u * u + v * v) / (wobble * wobble)
            if d2 > 1 or (flat_bottom and v > 0.75):
                continue
            nz = math.sqrt(max(0.0, 1 - d2))
            c.px(x, y, lit(r, u / wobble, v / wobble, nz))
    # a crack or two
    k = random.Random(seed)
    for _ in range(2):
        x0 = int(cx + k.uniform(-0.4, 0.3) * rx)
        y0 = int(cy + k.uniform(-0.5, 0.0) * ry)
        for t in range(int(ry * 0.6)):
            c.px(x0 + (t // 3) * (1 if seed % 2 else -1), y0 + t, r[0])


def cone(c, cx, base_y, half, height, rgb):
    """A conical roof shaded round: lit left, dark right, a civ-coloured flag on top."""
    r = ramp(rgb)
    for k in range(height):
        y = base_y - k
        w = half * (1 - k / height)
        for x in range(int(cx - w), int(cx + w) + 1):
            u = (x - cx) / max(1.0, w)
            c.px(x, y, lit(r, u, -0.3, math.sqrt(max(0.0, 1 - u * u))))
    flag(c, int(cx), int(base_y - height), 6)


def tower(c, x0, x1, top, bottom, rgb, roof_rgb):
    """A round stone tower: shaded across like a cylinder, battlements or a cone on top, a slit window."""
    r = ramp(rgb)
    cx = (x0 + x1) / 2
    for x in range(x0, x1 + 1):
        u = (x - cx) / max(1.0, (x1 - x0) / 2)
        col = lit(r, u, 0, math.sqrt(max(0.0, 1 - u * u)))
        c.line(x, top, x, bottom, col)
    c.rect(int(cx), top + 4, int(cx) + 1, top + 7, P((29, 23, 16)))
    if roof_rgb is None:
        for x in range(x0, x1 + 1, 3):
            c.rect(x, top - 3, min(x + 1, x1), top - 1, r[3])
    else:
        cone(c, cx, top - 1, (x1 - x0) / 2 + 2, max(6, (x1 - x0)), roof_rgb)


def stone_wall(c, x0, x1, top, bottom, rgb):
    r = ramp(rgb)
    c.rect(x0, top, x1, bottom, r[2])
    c.line(x0, top, x1, top, r[3])
    c.line(x0, bottom, x1, bottom, r[1])
    for x in range(x0, x1 + 1, 4):  # battlements
        c.rect(x, top - 3, min(x + 1, x1), top - 1, r[3])
    for y in range(top + 3, bottom, 4):  # courses of stone
        for x in range(x0 + (y // 4 % 2) * 3, x1, 6):
            c.px(x, y, r[1])


# ------------------------------------------------------------------------------------------------ walls along hex edges

EDGE_W, EDGE_H = 48, 16  # one hex edge (24 world units long) at 2x, the wall standing on its line


def wall_edge():
    """A stone wall segment along one hex edge, seen from above at a slant: courses of cut stone with mortar lines,
    battlements on the outer side, lit from the top left. Drawn by the game only on the outside edges of a walled
    area, like a border (Patryk, 2026-11-23)."""
    c = canvas(EDGE_W, EDGE_H)
    r = ramp((147, 138, 130))
    top, bottom = 5, EDGE_H - 2
    c.rect(0, top, EDGE_W - 1, bottom, r[2])
    c.line(0, top, EDGE_W - 1, top, r[3])
    c.line(0, bottom, EDGE_W - 1, bottom, r[0])
    for row, y in enumerate(range(top + 1, bottom, 3)):  # courses of stone: mortar lines and staggered joints
        c.line(0, y + 2, EDGE_W - 1, y + 2, r[1])
        for x in range((row % 2) * 4, EDGE_W, 8):
            c.line(x, y, x, y + 1, r[1])
            c.px(x + 1, y, r[3])
    for x in range(1, EDGE_W - 2, 6):  # battlements
        c.rect(x, 1, x + 3, top - 1, r[2])
        c.line(x, 1, x + 3, 1, r[4])
        c.line(x + 3, 1, x + 3, top - 1, r[1])
    c.outline(P(OUTLINE))
    return c


def wall_tower():
    """A round tower where two wall segments meet (and at the ends of a wall)."""
    c = canvas(20, 20)
    r = ramp((147, 138, 130))
    for y in range(20):
        for x in range(20):
            u, v = (x - 9.5) / 8.5, (y - 9.5) / 8.5
            if u * u + v * v <= 1:
                c.px(x, y, lit(r, u, v, math.sqrt(max(0.0, 1 - u * u - v * v))))
    for a in range(0, 360, 45):  # merlons round the top
        x = round(9.5 + 6 * math.cos(math.radians(a)))
        y = round(9.5 + 6 * math.sin(math.radians(a)))
        c.px(x, y, r[4])
    c.disc(9.5, 9.5, 2.5, r[0])
    c.outline(P(OUTLINE))
    return c


def palisade_edge():
    """A palisade segment along one hex edge: sharpened logs side by side, each lit on its left, bark lines."""
    c = canvas(EDGE_W, EDGE_H)
    r = ramp((138, 86, 45))
    for x in range(0, EDGE_W - 1, 4):
        top = 2 if (x // 4) % 2 == 0 else 3
        for y in range(top + 2, EDGE_H - 1):
            c.px(x, y, r[3])
            c.px(x + 1, y, r[2])
            c.px(x + 2, y, r[1])
        c.px(x + 1, top, r[3])
        c.line(x, top + 1, x + 2, top + 1, r[2])
        for y in range(top + 4, EDGE_H - 2, 4):  # bark
            c.px(x + 1, y, r[0])
    c.line(0, EDGE_H - 5, EDGE_W - 1, EDGE_H - 5, r[0])  # the binding rope
    c.outline(P(OUTLINE))
    return c


def generate_works():
    wall_edge().save("works", "work_wall_edge")
    wall_tower().save("works", "work_wall_tower")
    palisade_edge().save("works", "work_palisade_edge")


# ------------------------------------------------------------------------------------------------ interface frames (textured)

def _frame(name, w, h, base_rgb, margin, pattern, raised=True, stitch=None):
    """A 9-slice frame of the interface in a material (Patryk, 2026-11-23: the whole interface textured): a dark outline,
    a bevel (lit top-left when raised, sunk otherwise) and a material pattern whose period divides the centre, so it
    repeats seamlessly when the game tiles the centre."""
    c = canvas(w, h)
    r = ramp(base_rgb)
    for y in range(h):
        for x in range(w):
            col = r[2]
            if pattern == "stone":
                row = y // 3
                if y % 3 == 2 or (x + (row % 2) * 3) % 6 == 0:
                    col = r[1]
                elif (x + (row % 2) * 3) % 6 == 1 and y % 3 == 0:
                    col = r[3]
            elif pattern == "leather":
                if (x * 3 + y * 5) % 7 == 0:
                    col = r[1]
                elif (x * 5 + y) % 11 == 0:
                    col = r[3]
            elif pattern == "wood":
                if x % 6 == 5:
                    col = r[0]
                elif (x // 6 + y) % 5 == 0:
                    col = r[1]
            elif pattern == "parchment":
                if (x * 7 + y * 3) % 9 == 0:
                    col = r[3]
            c.px(x, y, col)
    lit, dark = (r[4], r[0]) if raised else (r[0], r[3])
    c.line(1, 1, w - 2, 1, lit)
    c.line(1, 1, 1, h - 2, lit)
    c.line(1, h - 2, w - 2, h - 2, dark)
    c.line(w - 2, 1, w - 2, h - 2, dark)
    if margin >= 4:
        c.line(2, 2, w - 3, 2, r[3])
        c.line(2, h - 3, w - 3, h - 3, r[1])
    if stitch is not None:
        for x in range(4, w - 4, 2):
            c.px(x, 3, stitch)
            c.px(x, h - 4, stitch)
        for y in range(4, h - 4, 2):
            c.px(3, y, stitch)
            c.px(w - 4, y, stitch)
    c.rect(0, 0, w - 1, 0, P(OUTLINE))
    c.rect(0, h - 1, w - 1, h - 1, P(OUTLINE))
    c.rect(0, 0, 0, h - 1, P(OUTLINE))
    c.rect(w - 1, 0, w - 1, h - 1, P(OUTLINE))
    for x, y in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)):
        c.im.putpixel((x, y), (0, 0, 0, 0))
    c.save("ui", name)


def generate_ui():
    stone, leather, wood = (79, 75, 69), (86, 44, 24), (122, 45, 2)
    _frame("frame_panel", 24, 24, (62, 60, 58), 6, "stone")
    _frame("frame_bar", 24, 24, leather, 6, "leather", stitch=P((170, 101, 39)))
    _frame("frame_header", 24, 16, (105, 56, 41), 4, "leather")
    _frame("button_normal", 12, 12, stone, 3, "stone")
    _frame("button_hover", 12, 12, (101, 99, 98), 3, "stone")
    _frame("button_pressed", 12, 12, wood, 3, "wood", raised=False)
    _frame("button_disabled", 12, 12, (47, 46, 45), 3, "stone")
    _frame("tab_selected", 12, 12, wood, 3, "wood")
    _frame("tab_unselected", 12, 12, (62, 60, 58), 3, "stone")
    _frame("tooltip", 12, 12, (242, 194, 121), 3, "parchment")
