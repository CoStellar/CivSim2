"""Showcase of every building (and the settlement centres) on a piece of map, each hex in another people's colour,
drawn the way the game draws them: grass tile, the building in its places, the people's colour in place of the
magentas, a soft shadow. Run from the repo root: python3 -I showcase.py <out_dir>."""
import json
import sys

from PIL import Image, ImageDraw

OUT = sys.argv[1]
ART = "art/placeholder"
CIVS = [(0.95, 0.85, 0.25), (0.85, 0.25, 0.25), (0.3, 0.55, 0.95), (0.6, 0.35, 0.85),
        (0.3, 0.8, 0.45), (0.95, 0.55, 0.2), (0.4, 0.85, 0.85), (0.9, 0.45, 0.7)]


def tint(im, civ):
    im = im.copy()
    px = im.load()
    c = tuple(v * 255 for v in civ)
    light = tuple(v + (255 - v) * 0.25 for v in c)
    dark = tuple(v * 0.6 for v in c)
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a > 128 and g < 5 and abs(r - b) < 5 and r > 100:
                col = light if r > 230 else c if r > 150 else dark
                px[x, y] = tuple(int(v) for v in col) + (255,)
    return im


def shadow(base, box):
    x0, y0, x1, y1 = box
    layer = Image.new("RGBA", base.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    w, h = x1 - x0, y1 - y0
    cx, cy = x0 + w * 0.56, y1 - h * 0.06
    d.ellipse((cx - w * 0.48, cy - h * 0.12, cx + w * 0.48, cy + h * 0.12), fill=(0, 0, 0, 66))
    base.alpha_composite(layer)


def fit(im, box):
    x0, y0, x1, y1 = box
    k = min((x1 - x0) / im.width, (y1 - y0) / im.height)
    w, h = max(1, round(im.width * k)), max(1, round(im.height * k))
    return im.resize((w, h), Image.NEAREST), (x0 + (x1 - x0 - w) // 2, y1 - h)


d = json.load(open("data/sim/game.json"))
era = {t["key"]: t["era"] for t in d["technologies"]}
order = ["stone_age", "neolithic", "bronze_age", "iron_age", "middle_ages"]
items = sorted(d["buildings"], key=lambda b: order.index(era.get(b["tech"], "stone_age")))
items = [("building", b["key"], b.get("size", 4)) for b in items] + [("settlement", l["key"], 4) for l in d["settlementLevels"]]

cols = 8
rows = (len(items) + cols - 1) // cols
W = 72 * cols + 24
H = 84 * rows + 42
img = Image.new("RGBA", (W, H), (24, 22, 20, 255))
grass = [Image.open(f"{ART}/biome/biome_grassland_0{v}.png").convert("RGBA") for v in (1, 2, 3)]
water = Image.open(f"{ART}/biome/biome_coast_01.png").convert("RGBA")
font = None
for k, (kind, key, size) in enumerate(items):
    col, row = k % cols, k // cols
    x, y = col * 72, row * 84 + (42 if col % 2 else 0)
    tile = water if key in ("fishing_hut", "raft_landing", "harbour", "shipyard") else grass[k % 3]
    img.alpha_composite(tile, (x, y))
    civ = CIVS[k % len(CIVS)]
    pic = tint(Image.open(f"{ART}/buildings/{kind}_{key}.png").convert("RGBA"), civ)
    cx, cy = x + 48, y + 42
    if size == 4:
        k2 = 0.86 if kind == "building" else 0.88
        box = (round(cx - 48 * k2), round(cy - 42 * k2), round(cx + 48 * k2), round(cy + 42 * k2))
    elif size == 2:
        box = (cx - 32 + 3, cy - 28 + 3, cx + 30 - 3, cy - 2 - 3)
    else:
        box = (cx - 32 + 3, cy - 28 + 3, cx - 2 - 3, cy - 2 - 3)
    if size < 4 or kind == "settlement":
        shadow(img, box)
    im2, at = fit(pic, box)
    img.alpha_composite(im2, at)
img = img.resize((img.width * 2, img.height * 2), Image.NEAREST)
img.save(f"{OUT}/showcase_all.png")

# the same few buildings in every people's colour
pick = ["hut", "potter", "bronze_smith", "barracks", "guildhall", "farm", "castle"]
pick_items = [b for b in d["buildings"] if b["key"] in pick]
cell = 100
sheet = Image.new("RGBA", (cell * len(CIVS) + 8, cell * len(pick_items) + 8), (24, 22, 20, 255))
for r_, b in enumerate(pick_items):
    for c_, civ in enumerate(CIVS):
        x, y = c_ * cell + 4, r_ * cell + 4
        sheet.alpha_composite(grass[(r_ + c_) % 3], (x + 2, y + 8))
        pic = tint(Image.open(f"{ART}/buildings/building_{b['key']}.png").convert("RGBA"), civ)
        size = b.get("size", 4)
        box = (x + 8, y + 14, x + 92, y + 86) if size == 4 else (x + 14, y + 22, x + 86, y + 80)
        shadow(sheet, box)
        im2, at = fit(pic, box)
        sheet.alpha_composite(im2, at)
sheet = sheet.resize((sheet.width * 2, sheet.height * 2), Image.NEAREST)
sheet.save(f"{OUT}/showcase_colours.png")
print("ok", len(items))
