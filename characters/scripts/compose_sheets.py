"""Turns the transparent Blender cut-outs (renders/raw/*.png) into the presentation images:
  renders/char_<id>.png             character card: night fog, halftone, mist at the feet, eye bloom
  renders/characters_lineup.png     everyone at one scale with 1 m / 2 m marks
  renders/characters_silhouettes_64px.png   style-bible test: Zhdan = 64 px tall (shown 3x, nearest)
  renders/corridor.png              the Shadow in the doorway (from build_characters.py --corridor)
Colour rules from docs/characters/00-style-bible.md: the frame is achromatic; orange only from embers
(and Zhdan's smouldering outline), blue only from water, red only from blood.

Run with system python3 (numpy + Pillow):  python3 characters/scripts/compose_sheets.py [characters dir]
"""
import os, sys, math
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RAW = os.path.join(ROOT, "renders", "raw")
OUTD = os.path.join(ROOT, "renders")
FONT_B = "/usr/share/fonts/truetype/noto/NotoSerif-Bold.ttf"
FONT_R = "/usr/share/fonts/truetype/noto/NotoSerif-Regular.ttf"

CHARS = [
    ("zhdan0", "Ждан · стадия 0", "«Пепел». Крестьянин, который хотел хлеба"),
    ("zhdan2", "Ждан · стадия 2", "«Головня». Кисти обуглены, трещины тлеют"),
    ("zhdan4", "Ждан · стадия 4", "«Костёр». Печь в груди, огонь в глазницах"),
    ("shadow", "Тень", "Огонь в мире: тень, что шевелится не в такт"),
    ("limbo", "Фигура Лимбо", "Без лица. Без суждения. Ждёт ответа"),
    ("korney", "Палач Корней", "Не бегает — идёт. Глаз не видно, но он смотрит"),
    ("king", "Король", "Стена, а не финал. Чёрная корона"),
    ("peasant", "Крестьянин", "Враг: прёт толпой, бьёт вилами"),
    ("guard", "Стража", "Враг: строй, копьё и щит"),
    ("bucketguard", "Стража с ведром", "Враг: выплёскивает воду — бить первым"),
    ("archer", "Лучник", "Враг: держит дистанцию"),
    ("gavrila", "Пекарь Гаврила", "Жертва №1. «Жданушка»"),
    ("agafya", "Вдова Агафья", "Жертва №5. Крестится, шепчет"),
    ("nikodim", "Поп Никодим", "Жертва №8. Не боится"),
    ("efim", "Слепой Ефим", "Жертва №18. Не видит огня"),
]
SHORT = {"zhdan0": "Ждан · 0", "zhdan2": "Ждан · 2", "zhdan4": "Ждан · 4", "ragman": "С тряпкой",
         "bucketguard": "С ведром", "korney": "Палач Корней", "limbo": "Лимбо"}
FRAME_H, FRAME_C = 3.3, 1.55          # ortho frame of the cut-outs (metres), as in build_characters.py
EMBER = np.array([0xFF, 0x7A, 0x18]) / 255
EMBER_HOT = np.array([0xFF, 0xD1, 0x66]) / 255
EMBER_DEEP = np.array([0xC2, 0x4E, 0x00]) / 255
PAPER = 0.94

def load(path):
    return np.asarray(Image.open(path).convert("RGBA"), np.float32) / 255.0

def save(a, path):
    Image.fromarray((np.clip(a, 0, 1) * 255 + 0.5).astype(np.uint8)).save(path)
    print("[saved]", os.path.relpath(path, ROOT))

def box_blur(a, r, passes=3):
    r = max(1, int(r))
    for _ in range(passes):
        for ax in (0, 1):
            c = np.cumsum(np.pad(a, [(r + 1, r) if i == ax else (0, 0) for i in range(a.ndim)], mode="edge"),
                          axis=ax, dtype=np.float64)
            n = a.shape[ax]
            a = ((np.take(c, np.arange(2 * r + 1, 2 * r + 1 + n), axis=ax) -
                  np.take(c, np.arange(0, n), axis=ax)) / (2 * r + 1)).astype(np.float32)
    return a

def noise(h, w, cell, seed):
    g = np.random.default_rng(seed)
    small = g.random((max(2, h // cell + 2), max(2, w // cell + 2))).astype(np.float32)
    im = Image.fromarray(small, mode="F").resize((w + 2 * cell, h + 2 * cell), Image.BICUBIC)
    return np.asarray(im, np.float32)[cell:cell + h, cell:cell + w]

def halftone(v, cell=6.0, ang=45):
    h, w = v.shape
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    c, s = math.cos(math.radians(ang)), math.sin(math.radians(ang))
    u, t = (xx * c - yy * s) / cell, (xx * s + yy * c) / cell
    d = np.hypot(u - np.floor(u) - 0.5, t - np.floor(t) - 0.5) * cell
    r = cell * np.sqrt(np.clip(1 - v, 0, 1) / math.pi)
    return 1 - np.clip(r - d + 0.5, 0, 1)

def snowfall(h, w, seed, n=900, big=30):
    g = np.random.default_rng(seed)
    m = np.zeros((h, w), np.float32)
    x0, y0 = g.uniform(-40, w, n), g.uniform(-40, h, n)
    L, th = g.uniform(2, 14, n), g.choice([1, 1, 1, 2], n)
    ang = np.radians(28 + g.normal(0, 6, n))
    for i in range(n):
        t = np.linspace(0, L[i], int(L[i]) + 1)
        xs = (x0[i] + np.cos(ang[i]) * t).astype(int); ys = (y0[i] + np.sin(ang[i]) * t).astype(int)
        for o in range(int(th[i])):
            ok = (xs >= 0) & (xs < w) & (ys + o >= 0) & (ys + o < h)
            m[ys[ok] + o, xs[ok]] = 1.0
    yy, xx = np.mgrid[0:h, 0:w]
    for _ in range(big):
        cx, cy, r = g.uniform(0, w), g.uniform(0, h), g.uniform(1.8, 4.5)
        xa, xb, ya, yb = int(max(cx - r - 2, 0)), int(min(cx + r + 2, w)), int(max(cy - r - 2, 0)), int(min(cy + r + 2, h))
        if xa < xb and ya < yb:
            sy, sx = yy[ya:yb, xa:xb], xx[ya:yb, xa:xb]
            m[ya:yb, xa:xb] = np.maximum(m[ya:yb, xa:xb], np.clip(r - np.hypot(sx - cx, sy - cy) + 0.5, 0, 1))
    return m

def dead_tree(draw, x, y, h, col, seed, width=6):
    g = np.random.default_rng(seed)
    def branch(x0, y0, ang, L, w, depth):
        x1, y1 = x0 + math.cos(ang) * L, y0 - math.sin(ang) * L
        draw.line([(x0, y0), (x1, y1)], fill=col, width=max(1, int(w)))
        if depth > 0:
            for _ in range(2 if depth > 2 else g.integers(1, 3)):
                branch(x1, y1, ang + g.uniform(-0.75, 0.75), L * g.uniform(0.55, 0.75), w * 0.62, depth - 1)
    branch(x, y, math.pi / 2 + g.uniform(-0.08, 0.08), h * 0.42, width, 5)

def night_bg(h, w, ground_y, seed, centers=None):
    """Night fog: a pale glow at the horizon behind the figures, darkness above and in front."""
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    centers = centers or [w / 2]
    glow = np.zeros((h, w), np.float32)
    for cx in centers:
        glow = np.maximum(glow, np.exp(-((xx - cx) / (0.5 * w / max(1, len(centers) ** 0.5))) ** 2))
    vert = np.exp(-((yy - ground_y) / (0.38 * h)) ** 2)
    v = 0.07 + 0.5 * glow * vert + 0.1 * vert
    v = np.where(yy > ground_y, v * (1 - 0.6 * np.clip((yy - ground_y) / (h - ground_y + 1), 0, 1)) + 0.04, v)
    v += (noise(h, w, 90, seed) - 0.5) * 0.16 + (noise(h, w, 28, seed + 1) - 0.5) * 0.07
    v = np.clip(v, 0, 1)
    # far dead trees, swallowed by fog
    im = Image.fromarray((v * 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    g = np.random.default_rng(seed + 7)
    for k in range(0):                     # (trees read as staffs next to the figures: off)
        x = g.uniform(0, w)
        dead_tree(d, x, ground_y + g.uniform(-6, 4), g.uniform(0.3, 0.55) * h, int(255 * g.uniform(0.1, 0.2)),
                  seed + k, width=g.uniform(4, 8))
    v = np.asarray(im, np.float32) / 255
    v = 0.5 * halftone(v) + 0.5 * v
    return v

def finish(rgb, seed, vign=0.6, grain=0.025):
    h, w = rgb.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    r = np.hypot((xx - w / 2) / (w / 2), (yy - h * 0.45) / (h / 2))
    rgb = rgb * (1 - vign * np.clip(r - 0.35, 0, 1) ** 1.6)[..., None]
    rgb = rgb + ((np.random.default_rng(seed).random((h, w)) - 0.5) * grain)[..., None]
    return np.clip(rgb, 0, 1)

def masks(fig):
    rgb, al = fig[..., :3], fig[..., 3]
    eyes = ((rgb.min(-1) > 0.985) & (al > 0.5)).astype(np.float32)
    ember = ((rgb[..., 0] > 0.7) & (rgb[..., 0] - rgb[..., 2] > 0.35) & (al > 0.5)).astype(np.float32)
    return eyes, ember

def smoulder_outline(fig, seed, width=5):
    """Zhdan's outline: the only coloured contour in the game, glowing unevenly (pulse)."""
    al = (fig[..., 3] > 0.5).astype(np.uint8) * 255
    er = np.asarray(Image.fromarray(al).filter(ImageFilter.MinFilter(2 * width + 1)), np.float32) / 255
    band = np.clip(al / 255.0 - er, 0, 1)
    h, w = band.shape
    n = noise(h, w, 18, seed)
    t = np.clip((n - 0.25) * 1.8, 0, 1)
    col = EMBER_DEEP * (1 - t[..., None]) + EMBER * t[..., None]
    col = np.where((t > 0.8)[..., None], EMBER_HOT, col)
    return band, col

def compose_figure(bg, fig, seed, zhdan=False, ox=0, oy=0, mist_y=None):
    """Composite one cut-out (any scale) onto a background at (ox, oy)."""
    out = bg.copy()
    h, w = fig.shape[:2]
    sub = out[oy:oy + h, ox:ox + w]
    al = fig[..., 3:4]
    # ground shadow under the feet
    cols = np.where(fig[..., 3].max(0) > 0.5)[0]
    rows = np.where(fig[..., 3].max(1) > 0.5)[0]
    if len(cols):
        fy = rows.max(); fx = (cols.min() + cols.max()) / 2; fw = (cols.max() - cols.min()) * 0.55
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        sh = np.exp(-(((xx - fx) / max(fw, 10)) ** 2 + ((yy - fy) / (0.018 * h + 4)) ** 2) * 2.0)
        sub *= (1 - 0.75 * sh)[..., None]
    rgb = fig[..., :3]
    if zhdan:
        band, col = smoulder_outline(fig, seed, width=max(2, int(round(5 * h / 1500))))
        rgb = rgb * (1 - band[..., None]) + col * band[..., None]
    sub[:] = rgb * al + sub * (1 - al)
    eyes, ember = masks(fig)
    if zhdan:
        ember = np.maximum(ember, band * 0.7)
    s = h / 1500
    bloom = np.clip(box_blur(eyes, 4 * s + 1) * 4, 0, 1) * 0.55 + np.clip(box_blur(eyes, 16 * s + 1) * 8, 0, 1) * 0.25
    sub[:] = sub + bloom[..., None] * (1 - sub)
    eb = box_blur(ember, 10 * s + 1) * 2.2 + box_blur(ember, 40 * s + 1) * 1.6
    ecol = box_blur(ember[..., None] * EMBER, 10 * s + 1) / (box_blur(ember, 10 * s + 1)[..., None] + 1e-4)
    ecol = np.where(ember.sum() > 0, ecol, 0)
    sub[:] = sub + np.clip(eb, 0, 1)[..., None] * 0.5 * np.clip(ecol + EMBER * 0.2, 0, 1) * (1 - 0.4 * al)
    # mist over the feet
    if mist_y is not None and len(cols):
        yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
        fy = rows.max()
        m = np.clip(1 - np.abs(yy - fy + 0.015 * h) / (0.05 * h + 6), 0, 1) ** 1.3
        m *= np.clip(noise(h, w, int(40 * s) + 4, seed + 3) * 1.4 - 0.2, 0, 1)
        sub[:] = sub * (1 - 0.6 * m[..., None]) + 0.36 * 0.6 * m[..., None]
    return out

def label(img, xy, text, size, fill, bold=True, anchor="la"):
    d = ImageDraw.Draw(img)
    d.text(xy, text, font=ImageFont.truetype(FONT_B if bold else FONT_R, size), fill=fill, anchor=anchor)

def report(name, a):
    rgb = a[..., :3]
    ch = rgb.max(-1) - rgb.min(-1)
    colored = ch > 0.08
    orange = colored & (rgb[..., 0] > rgb[..., 1]) & (rgb[..., 1] > rgb[..., 2] - 0.02) & (rgb[..., 1] > 0.15 * rgb[..., 0])
    blue = colored & (rgb[..., 2] >= rgb[..., 0])
    red = colored & ~orange & ~blue
    print(f"[check] {name}: coloured {colored.mean() * 100:.2f}% (orange {orange.mean() * 100:.2f}, "
          f"blue {blue.mean() * 100:.2f}, red {red.mean() * 100:.2f}), mean {rgb.mean():.2f}")

def card(cid, name, sub, fig, seed):
    H, W = fig.shape[:2]
    band = 210
    foot = int(H * (1 - (0 - (FRAME_C - FRAME_H / 2)) / FRAME_H))
    bg = night_bg(H + band, W, foot, seed)
    bg = np.repeat(bg[..., None], 3, axis=2)
    out = compose_figure(bg, fig, seed, zhdan=cid.startswith("zhdan"), mist_y=foot)
    snow = snowfall(H + band, W, seed + 11)
    out = out * (1 - 0.75 * snow[..., None]) + 0.92 * 0.75 * snow[..., None]
    out = finish(out, seed)
    out[H:] = out[H:] * 0.25 + 0.03
    im = Image.fromarray((out * 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    d.line([(60, H + 34), (W - 60, H + 34)], fill=(110, 110, 110), width=2)
    label(im, (60, H + 58), name, 56, (240, 240, 240))
    label(im, (60, H + 138), sub, 31, (165, 165, 165), bold=False)
    a = np.asarray(im, np.float32) / 255
    path = os.path.join(OUTD, f"char_{cid}.png")
    im.save(path); print("[saved]", os.path.relpath(path, ROOT)); report(f"char_{cid}", a)

def crop_cols(fig, margin):
    cols = np.where(fig[..., 3].max(0) > 0.02)[0]
    a, b = max(0, cols.min() - margin), min(fig.shape[1], cols.max() + margin + 1)
    return fig[:, a:b]

def scaled(fig, k):
    h, w = fig.shape[:2]
    im = Image.fromarray((fig * 255 + 0.5).astype(np.uint8), "RGBA").resize((max(1, int(w * k)), int(h * k)),
                                                                             Image.LANCZOS)
    return np.asarray(im, np.float32) / 255

def lineup(figs):
    k = 0.5
    rows = [["zhdan0", "zhdan2", "zhdan4", "shadow", "limbo", "korney", "king"],
            ["peasant", "guard", "bucketguard", "archer", "gavrila", "agafya", "nikodim", "efim"]]
    names = {c: n for c, n, _ in CHARS}
    gap, left, top, row_h, lab = 26, 150, 150, int(1500 * k), 70
    scaled_rows = [[(c, scaled(crop_cols(figs[c], 20), k)) for c in r if c in figs] for r in rows]
    W = max(left + sum(f.shape[1] + gap for _, f in r) + 60 for r in scaled_rows)
    H = top + len(rows) * (row_h + lab) + 40
    canvas = np.zeros((H, W, 3), np.float32)
    ppm = row_h / FRAME_H
    for ri, r in enumerate(scaled_rows):
        y0 = top + ri * (row_h + lab)
        foot = int(row_h * (1 - (0 - (FRAME_C - FRAME_H / 2)) / FRAME_H))
        width = sum(f.shape[1] + gap for _, f in r)
        x = left + (W - left - 60 - width) // 2
        centers = []
        xx = x
        for _, f in r:
            centers.append(xx + f.shape[1] / 2); xx += f.shape[1] + gap
        bg = np.repeat(night_bg(row_h + lab, W, foot, 40 + ri, centers=[W / 2])[..., None], 3, axis=2)
        for ci, (c, f) in enumerate(r):
            bg = compose_figure(bg, f, 50 + ci, zhdan=c.startswith("zhdan"), ox=x, oy=0, mist_y=foot)
            x += f.shape[1] + gap
        snow = snowfall(row_h + lab, W, 60 + ri, n=1500, big=40)
        bg = bg * (1 - 0.7 * snow[..., None]) + 0.92 * 0.7 * snow[..., None]
        bg = finish(bg, 70 + ri, vign=0.35)
        canvas[y0:y0 + row_h + lab] = bg
        canvas[y0 + row_h:y0 + row_h + lab] = canvas[y0 + row_h:y0 + row_h + lab] * 0.2 + 0.03
    canvas[:top] = 0.03
    im = Image.fromarray((canvas * 255).astype(np.uint8))
    d = ImageDraw.Draw(im)
    label(im, (left, 50), "CALABASH — персонажи", 60, (240, 240, 240))
    label(im, (W - 60, 72), "один масштаб · манга-тушь · цвет только у огня, воды и крови", 28, (150, 150, 150),
          bold=False, anchor="ra")
    for ri, r in enumerate(scaled_rows):
        y0 = top + ri * (row_h + lab)
        foot = y0 + int(row_h * (1 - (0 - (FRAME_C - FRAME_H / 2)) / FRAME_H))
        for m in (1, 2):
            y = foot - int(m * ppm)
            for xd in range(40, W - 40, 28):
                d.line([(xd, y), (xd + 12, y)], fill=(120, 120, 120), width=1)
            label(im, (40, y - 34), f"{m} м", 24, (150, 150, 150), bold=False)
        width = sum(f.shape[1] + gap for _, f in r)
        x = left + (W - left - 60 - width) // 2
        for c, f in r:
            label(im, (x + f.shape[1] // 2, y0 + row_h + 22), SHORT.get(c, names[c]), 24, (225, 225, 225), anchor="ma")
            x += f.shape[1] + gap
    path = os.path.join(OUTD, "characters_lineup.png")
    im.save(path); print("[saved]", os.path.relpath(path, ROOT))
    report("characters_lineup", np.asarray(im, np.float32) / 255)

def silhouettes(figs):
    """64 px test: Zhdan (1.80 m) is 64 px tall; everyone at the same scale, pure black on paper."""
    k = 64 / (1.80 / FRAME_H * 1500)
    order = [c for c, _, _ in CHARS if c in figs]
    tiles = []
    for c in order:
        a = crop_cols(figs[c], 10)[..., 3]
        im = Image.fromarray((a * 255).astype(np.uint8)).resize((max(1, int(a.shape[1] * k)), int(a.shape[0] * k)),
                                                                Image.LANCZOS)
        tiles.append(np.asarray(im, np.float32) / 255)
    h = tiles[0].shape[0]; gap = 8
    W = sum(t.shape[1] + gap for t in tiles) + gap
    strip = np.full((h + 8, W), PAPER, np.float32)
    x = gap
    for t in tiles:
        strip[4:4 + h, x:x + t.shape[1]] = PAPER * (1 - t) + 0.04 * t
        x += t.shape[1] + gap
    big = np.repeat(np.repeat(strip, 3, 0), 3, 1)
    lab = 90
    out = np.full((big.shape[0] + lab + 70, big.shape[1]), PAPER, np.float32)
    out[70:70 + big.shape[0]] = big
    im = Image.fromarray((out * 255).astype(np.uint8)).convert("RGB")
    label(im, (24, 18), "Проверка силуэта: Ждан = 64 px (показано ×3, без сглаживания)", 30, (20, 20, 20))
    names = {c: n for c, n, _ in CHARS}
    x = gap
    for c, t in zip(order, tiles):
        cx = (x + t.shape[1] / 2) * 3
        label(im, (cx, 70 + big.shape[0] + 12), SHORT.get(c, names[c].split(" ")[0]).split(" ")[-1 if c.startswith("zhdan") else 0] if not c.startswith("zhdan") else SHORT[c].replace("Ждан · ", "Ждан "), 18, (40, 40, 40), bold=False, anchor="ma")
        x += t.shape[1] + gap
    path = os.path.join(OUTD, "characters_silhouettes_64px.png")
    im.save(path); print("[saved]", os.path.relpath(path, ROOT))
    Image.fromarray((strip * 255).astype(np.uint8)).save(os.path.join(OUTD, "characters_silhouettes_64px_1x.png"))

def corridor():
    p = os.path.join(RAW, "corridor.png")
    if not os.path.exists(p):
        return
    fig = load(p)
    h, w = fig.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    sky = 0.8 + 0.12 * np.clip(yy / h, 0, 1) + (noise(h, w, 60, 5) - 0.5) * 0.1
    sky = np.repeat(sky[..., None], 3, axis=2)
    al = fig[..., 3:4]
    out = fig[..., :3] * al + sky * (1 - al)
    outside = 1 - np.clip(box_blur(fig[..., 3], 2), 0, 1)
    bright = (out.mean(-1) > 0.85).astype(np.float32)
    spill = np.clip(box_blur(bright, 40) * 1.6, 0, 1) * 0.3 + np.clip(box_blur(bright, 120) * 1.4, 0, 1) * 0.18
    out = out + spill[..., None] * (1 - out)
    dark = np.clip(box_blur(bright, 160) * 3.0, 0, 1)                   # the passage falls into darkness
    out = out * (0.3 + 0.7 * dark)[..., None]
    eyes, _ = masks(fig)
    bloom = np.clip(box_blur(eyes, 3) * 4, 0, 1) * 0.6 + np.clip(box_blur(eyes, 12) * 10, 0, 1) * 0.3
    out = out + bloom[..., None] * (1 - out)
    snow = snowfall(h, w, 9, n=2600, big=50)
    near = np.clip(box_blur(outside, 30) * 2.5, 0, 1)                  # snow blows in a little past the door
    sm = snow * near
    out = out * (1 - 0.6 * sm[..., None]) + 0.5 * 0.6 * sm[..., None] * (1 - outside[..., None]) + \
        0.97 * 0.6 * sm[..., None] * outside[..., None]
    out = finish(out, 3, vign=0.85, grain=0.03)
    path = os.path.join(OUTD, "corridor.png")
    save(out, path); report("corridor", out)

EYE_VARIANTS = [
    ("now", "1 · Сейчас", "прямоугольные точки"),
    ("round", "2 · Круглые точки", "мельче и мягче"),
    ("slits", "3 · Щели", "косые, к переносице"),
    ("sockets", "4 · Провалы", "тёмные глазницы, искра в глубине"),
    ("void", "5 · Пустые глазницы", "без света вообще"),
    ("uneven", "6 · Разные", "один больше и ниже"),
    ("vertical", "7 · Вертикальные", "узкие, как прорези"),
    ("drips", "8 · Подтёки", "свет стекает по лицу"),
    ("stare", "9 · Немигающий взгляд", "белые круги, точка-зрачок"),
]

def eyes_sheet():
    """Nine eye styles on the Shadow's head (renders/raw/eyes_<key>.png -> renders/eyes_variants.png)."""
    tiles = []
    for i, (key, name, sub) in enumerate(EYE_VARIANTS):
        p = os.path.join(RAW, f"eyes_{key}.png")
        if not os.path.exists(p):
            return
        fig = load(p)
        h, w = fig.shape[:2]
        bg = np.repeat(night_bg(h, w, int(h * 0.98), 300 + i)[..., None], 3, axis=2) * 0.8 + 0.08
        al = fig[..., 3:4]
        out = fig[..., :3] * al + bg * (1 - al)
        eyes, _ = masks(fig)
        bloom = np.clip(box_blur(eyes, 5) * 3, 0, 1) * 0.55 + np.clip(box_blur(eyes, 22) * 6, 0, 1) * 0.3
        out = out + bloom[..., None] * (1 - out)
        out = finish(out, 400 + i, vign=0.7, grain=0.02)
        im = Image.fromarray((out * 255).astype(np.uint8)).resize((600, 600), Image.LANCZOS)
        tiles.append((im, name, sub))
    pad, lab, top = 16, 92, 110
    W = 3 * 600 + 4 * pad; H = top + 3 * (600 + lab) + pad
    sheet = Image.new("RGB", (W, H), (8, 8, 8))
    label(sheet, (pad + 8, 30), "Глаза: варианты на одной модели (Тень)", 46, (240, 240, 240))
    for k, (im, name, sub) in enumerate(tiles):
        x = pad + (k % 3) * (600 + pad); y = top + (k // 3) * (600 + lab)
        sheet.paste(im, (x, y))
        label(sheet, (x + 6, y + 610), name, 32, (235, 235, 235))
        label(sheet, (x + 6, y + 652), sub, 24, (150, 150, 150), bold=False)
    path = os.path.join(OUTD, "eyes_variants.png")
    sheet.save(path); print("[saved]", os.path.relpath(path, ROOT))

if __name__ == "__main__":
    only = sys.argv[2].split(",") if len(sys.argv) > 2 else None
    figs = {}
    for i, (cid, name, sub) in enumerate(CHARS):
        p = os.path.join(RAW, f"{cid}.png")
        if os.path.exists(p):
            figs[cid] = load(p)
            if not only or cid in only:
                card(cid, name, sub, figs[cid], 100 + i)
    if figs and not only:
        lineup(figs)
        silhouettes(figs)
    if not only or "corridor" in only:
        corridor()
    if not only or "eyes" in only:
        eyes_sheet()
