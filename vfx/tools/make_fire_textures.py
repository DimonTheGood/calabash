"""Собирает текстуры огня vfx/Textures из бесплатных CC0-ассетов (см. vfx/Textures/LICENSE.txt).

    python3 vfx/tools/make_fire_textures.py <папка с распакованными архивами>

В папке должны лежать распакованные архивы:
  kenney_particlePack/            (kenney_particlePack.zip)
  Flame03-flipbooks/              (Flame03-flipbooks.zip)
  WispySmoke01-flipbooks/         (WispySmoke01-flipbooks.zip)

Что делает:
  - спрайты Kenney: белый цвет, альфа по яркости, 256 px, атласы;
  - флипбуки Unity: каждый кадр уменьшен и отодвинут от краёв тайла, основание пламени гаснет мягко;
  - у пламени альфа «в манга-стиле»: плотный силуэт с чётким краем вместо полупрозрачной дымки
    (дымку манга-фильтр рвёт на пятна с прямыми краями);
  - у каждого тайла края прозрачные, поэтому у частиц нигде не видно квадрата.
Нужен Pillow и numpy.
"""
import os
import sys

import numpy as np
from PIL import Image

SRC = sys.argv[1] if len(sys.argv) > 1 else "."
REFLOOR = "--refloor" in sys.argv          # только срезать слабую альфу у готовых текстур (исходники не нужны)
SMALL_ONLY = "--small" in sys.argv         # только пересобрать T_SmallFlame01 из готового T_Flame03 (исходники не нужны)
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Textures")
K = os.path.join(SRC, "kenney_particlePack", "PNG")


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def edge_fade(tw, th, m):
    x = np.linspace(0, 1, tw)
    y = np.linspace(0, 1, th)
    fx = smooth(0, 1, np.clip(np.minimum(x, 1 - x) / m, 0, 1))
    fy = smooth(0, 1, np.clip(np.minimum(y, 1 - y) / m, 0, 1))
    return np.outer(fy, fx)


ALPHA_FLOOR = 14   # слабее этого альфа обнуляется: манга-фильтр иначе вытягивает дымку в видимый квадрат частицы


def floor_alpha(arr):
    arr = arr.astype(float)
    a = arr[..., 3]
    arr[..., 3] = np.clip((a - ALPHA_FLOOR) * 255.0 / (255 - ALPHA_FLOOR), 0, 255)
    return arr


def save(arr, name):
    path = os.path.join(OUT, name)
    Image.fromarray(np.clip(floor_alpha(arr), 0, 255).astype(np.uint8), "RGBA").save(path, optimize=True)
    print(f"{name}: {os.path.getsize(path) // 1024} KB")


def kenney(name, tile, cel=None, rotated=False):
    p = os.path.join(K, "Rotated", name + ".png") if rotated else os.path.join(K, name + ".png")
    a = np.array(Image.open(p).convert("L").resize((tile, tile), Image.LANCZOS)).astype(float) / 255
    if cel:
        a = smooth(cel[0], cel[1], a)
    out = np.zeros((tile, tile, 4))
    out[..., :3] = 255
    out[..., 3] = a * 255
    return out


def atlas(name, sprites, cols, rows, tile=256, cel=None):
    out = np.zeros((rows * tile, cols * tile, 4))
    out[..., :3] = 255
    fade = edge_fade(tile, tile, 0.12)
    for i, s in enumerate(sprites):
        t = kenney(s, tile, cel)
        t[..., 3] *= fade
        r, c = divmod(i, cols)
        out[r * tile:(r + 1) * tile, c * tile:(c + 1) * tile] = t
    save(out, name)


def flipbook(src, name, tiles, size, shrink=0.8, cel=None, base_fade=True):
    im = Image.open(os.path.join(SRC, src)).convert("RGBA")
    W, H = im.size
    tx, ty = tiles
    tw, th = W // tx, H // ty
    nw, nh = int(tw * shrink), int(th * shrink)
    fade = edge_fade(tw, th, 0.08)
    out = np.zeros((H, W, 4))
    for j in range(ty):
        for i in range(tx):
            fr = np.array(im.crop((i * tw, j * th, (i + 1) * tw, (j + 1) * th)).resize((nw, nh), Image.LANCZOS)).astype(float)
            a = fr[..., 3] / 255
            if cel:
                a = smooth(cel[0], cel[1], a)
            if base_fade:
                yy = np.linspace(0, 1, nh)[:, None]
                a = a * np.clip((1 - yy) / 0.18, 0, 1) ** 1.5
            fr[..., 3] = a * 255
            tile = np.zeros((th, tw, 4))
            ox, oy = (tw - nw) // 2, (th - nh) // 2
            tile[oy:oy + nh, ox:ox + nw] = fr
            tile[..., 3] *= fade
            out[j * th:(j + 1) * th, i * tw:(i + 1) * tw] = tile
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), "RGBA")
    if img.size != size:
        img = img.resize(size, Image.LANCZOS)
    save(np.array(img).astype(float), name)


if REFLOOR:
    for n in ("T_Flame03_16x4.png", "T_SmallFlame01_16x4.png", "T_Flames_3x2.png", "T_Burst_3x2.png", "T_Sparks_4x2.png"):
        save(np.array(Image.open(os.path.join(OUT, n)).convert("RGBA")).astype(float), n)
    sys.exit(0)

def small_from_flame():
    """Мелкое пламя кулаков и поджога — из чистого T_Flame03: кадры меньше, сдвинуты по фазе, желтее.
    Исходник SmallFlame01_16x4.tga не ложится на сетку 16×4 (кадры резались вертикальными полосами,
    на врагах это давало языки огня с прямыми краями), поэтому его больше не берём."""
    src = np.array(Image.open(os.path.join(OUT, "T_Flame03_16x4.png")).convert("RGBA")).astype(float)
    H, W = src.shape[:2]
    tx, ty = 16, 4
    tw, th = W // tx, H // ty
    k = 0.62
    nw, nh = int(tw * k), int(th * k)
    out = np.zeros_like(src)
    n = tx * ty
    for f in range(n):
        g = (f + 23) % n                                   # другая фаза: кулаки и враги не мигают в такт телу
        i, j = g % tx, g // tx
        fr = Image.fromarray(src[j * th:(j + 1) * th, i * tw:(i + 1) * tw].astype(np.uint8), "RGBA")
        fr = fr.transpose(Image.FLIP_LEFT_RIGHT)             # зеркально: не копия тела
        fr = np.array(fr.resize((nw, nh), Image.LANCZOS)).astype(float)
        fr[..., 1] = np.minimum(255, fr[..., 1] * 1.08)    # чуть желтее
        tile = np.zeros((th, tw, 4))
        ox, oy = (tw - nw) // 2, th - nh - 2               # основание внизу тайла, край тайла прозрачный
        tile[oy:oy + nh, ox:ox + nw] = fr
        oi, oj = f % tx, f // tx
        out[oj * th:(oj + 1) * th, oi * tw:(oi + 1) * tw] = tile
    save(out, "T_SmallFlame01_16x4.png")


if SMALL_ONLY:
    small_from_flame()
    sys.exit(0)

FLAME_CEL = (0.12, 0.32)     # плотный силуэт пламени
atlas("T_Flames_3x2.png", [f"flame_0{i}" for i in range(1, 7)], 3, 2, cel=(0.15, 0.4))
atlas("T_Sparks_4x2.png", [f"spark_0{i}" for i in range(1, 8)] + ["light_01"], 4, 2)
atlas("T_Burst_3x2.png", [f"muzzle_0{i}" for i in range(1, 6)] + ["fire_01"], 3, 2, cel=(0.12, 0.35))
atlas("T_Scorch_2x2.png", ["scorch_01", "scorch_02", "scorch_03", "fire_02"], 2, 2)
atlas("T_Rings_2x2.png", ["circle_02", "circle_03", "circle_05", "flare_01"], 2, 2)

tr = kenney("trace_04_rotated", 512, rotated=True)[128:384]
tr = np.array(Image.fromarray(tr.astype(np.uint8), "RGBA").resize((512, 128), Image.LANCZOS)).astype(float)
save(tr, "T_Trail.png")

flipbook("Flame03-flipbooks/Flame03_16x4.tga", "T_Flame03_16x4.png", (16, 4), (1024, 512), cel=FLAME_CEL)
small_from_flame()
flipbook("WispySmoke01-flipbooks/WispySmoke01_8x8.tga", "T_WispySmoke01_8x8.png", (8, 8), (1024, 1024), shrink=0.9, base_fade=False)
