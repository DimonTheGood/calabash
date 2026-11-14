"""Winter manga-horror village (village_manga_prompt.md): 8 house types + environment, one Blender run.

The approved single-house kit (scripts/build_house_manga.py: primitives, manga tone shader, stone /
timber walls, tiled roof with the snow heightfield, props) is read and executed here with two small
patches (optional roof hole, patch position); that file itself is not modified.
House 1 (apothecary) is appended unchanged from house_01.blend; the other 7 are built parametrically.

Run:  blender -b --factory-startup -P scripts/build_village.py [-- <output dir, default: the folder above scripts/>]
      [--norender] [--only=overview,road,low,sheet]
Writes village.blend, fbx/SM_House_<Type>.fbx, fbx/Village_Props.fbx, fbx/Village_Environment.fbx,
renders/village_{overview,road,low}.png, renders/village_houses_sheet.png, renders/village_kit_<type>.png

Asset rules as before: metres, house front = -Y, pivot at ground centre, crookedness baked into
vertices; village placement uses plain object transforms (houses and props are linked duplicates).
"""
import bpy, math, random, sys, os, glob
import numpy as np
from mathutils import Vector as V, Matrix

_here = os.path.dirname(os.path.abspath(__file__))
_src = open(os.path.join(_here, "build_house_manga.py")).read()
_lib = _src[:_src.index("mats = make_materials()")]
for _old, _new in [
    ("    hole = sgn < 0\n    hu, hv, hru, hrv = HOLE\n",
     "    hole = HOLE is not None and sgn == HOLE_SIDE\n    hu, hv, hru, hrv = HOLE or (0.0, 0.0, 1.0, 1.0)\n"),
    ("ub = -2.55 + k * 0.31", "ub = PATCH_U + k * 0.31"),
]:
    assert _lib.count(_old) == 1, _old
    _lib = _lib.replace(_old, _new)
exec(compile(_lib, "build_house_manga.py", "exec"), globals())
_post = _src[_src.index("PAPER = 1.0"):_src.index("def finish(")]
exec(compile(_post, "build_house_manga.py", "exec"), globals())

RX, RY = 1920, 1080
NIGHT = "--night" in argv
ONLY = next((a.split("=", 1)[1].split(",") for a in argv if a.startswith("--only=")), None)
FBXDIR = os.path.join(OUT, "fbx")
MDIR = os.path.join(RDIR, "_passes")
HOLE_SIDE, PATCH_U = -1, -2.55
CHIMS = []

# =====================================================================
# kit extensions
# =====================================================================
def in_chimney(p, m=0.06):
    return any(c[0] - m < p.x < c[1] + m and c[2] - m < p.y < c[3] + m for c in CHIMS)

class xform:
    """Transforms every vertex created inside the block (GEO and SNOW) by matrix M."""
    def __init__(s, M):
        s.M = M
    def __enter__(s):
        s.mark = [{m: len(v[0]) for m, v in d.items()} for d in (GEO, SNOW)]
    def __exit__(s, *a):
        for d, mk in zip((GEO, SNOW), s.mark):
            for m, (vs, fs) in d.items():
                for i in range(mk.get(m, 0), len(vs)):
                    vs[i] = s.M @ V(vs[i])

def T(x, y, z=0.0, yaw=0.0, rx=0.0, ry=0.0):
    return (Matrix.Translation((x, y, z)) @ Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(rx, 4, "X")
            @ Matrix.Rotation(ry, 4, "Y"))

def setup(name, hw, hd, storeys, jet=(0.0, 0.0), pitch=1.4, lean=(0.0, 0.0), sag=0.25, settle=0.0,
          hole=None, hole_side=-1, bare=None, seed=1):
    global HW, HD, H0, ZT, HWT, HDT, PITCH, A_, CA, SA, ZR, EAVE, GOV, LEAN_X, LEAN_Y, SAG, SETTLE
    global HOLE, HOLE_SIDE, SNOW_BARE, ZB0, ZB1, LEVELS, ZSAG0, LEAN0, PATCH_U, rng, J, JS, H1, NAME
    NAME = name
    rng = random.Random(seed)
    HW, HD = hw, hd
    LEVELS, z = [], 0.0
    for k, (kind, h) in enumerate(storeys):
        LEVELS.append((kind, z, z + h, hw + jet[0] * k, hd + jet[1] * k)); z += h
    H0, ZT = LEVELS[0][2], z
    HWT, HDT = LEVELS[-1][3], LEVELS[-1][4]
    JS, J, H1 = HWT - HW, HDT - HD, ZT - H0
    PITCH = pitch; A_ = math.atan(pitch); CA, SA = math.cos(A_), math.sin(A_)
    ZR = ZT + HWT * pitch
    EAVE, GOV = HWT + 0.45, HDT + 0.35
    LEAN_X, LEAN_Y = lean; SAG, SETTLE = sag, settle
    ZSAG0 = LEVELS[-1][1] if len(LEVELS) > 1 else max(0.0, ZT - 1.5)
    LEAN0 = H0 if len(LEVELS) > 1 else 0.0
    HOLE, HOLE_SIDE, PATCH_U = hole, hole_side, -GOV + 0.9
    SNOW_BARE = bare or {-1: [], 1: []}
    ZB0, ZB1 = H0 + 0.24, ZT - 0.22
    CHIMS.clear(); GEO.clear(); SNOW.clear()

def warp(p):
    """Ridge sags over the top storey, everything above the ground storey leans, the house settles."""
    x, y, z = p
    wx = max(0.0, 1 - abs(x) / EAVE)
    wy = max(0.0, 1 - (y / GOV) ** 2)
    wz = min(1.0, max(0.0, (z - ZSAG0) / max(ZT - ZSAG0, 0.5)))
    z -= SAG * wx * wy * wz
    up = max(z - LEAN0, 0.0)
    return V((x + LEAN_X * up + SETTLE * z, y + LEAN_Y * up, z))

def frame(side, hx, hy, c=(0.0, 0.0)):
    O, ex, ey, ez, h = wall_frame(side, hx, hy)
    return (O + V((c[0], c[1], 0.0)), ex, ey, ez, h)

def stone_wall2(side, ops=(), hx=None, hy=None, c=(0.0, 0.0), z0=0.0, z1=None, gable=False, miss=0.05):
    """Coursed stone wall; with gable=True (front/back) the courses continue up under the roof line."""
    hx = HW if hx is None else hx; hy = HD if hy is None else hy; z1 = H0 if z1 is None else z1
    W = frame(side, hx, hy, c); half = W[4]
    ext = 0.07 if side in "fb" else 0.0
    zend = ZR - 0.12 if gable else z1
    z, row = z0, 0
    while z < zend - 0.02:
        za = min(z + R(0.30, 0.42), zend)
        if zend - za < 0.2:
            za = zend
        lo, hi = -half - ext, half + ext
        if gable and za > z1 + 0.02:
            lim = (ZR - za) / PITCH - 0.03
            if lim < 0.12:
                break
            lo, hi = max(lo, -lim), min(hi, lim)
        blocks = [iv for iv in (op.blocked(z, za) for op in ops) if iv]
        for a, b in free_intervals(lo, hi, blocks):
            u, first = a, True
            while b - u > 0.1:
                if first and abs(a - lo) < 1e-6:
                    w = 0.82 if row % 2 else 0.5
                else:
                    w = R(0.45, 0.85)
                if b - (u + w) < 0.3:
                    w = b - u
                if abs(b - hi) < 1e-6 and b - (u + w) < 0.95 and b - u > 0.95:
                    w = b - u - (0.5 if row % 2 else 0.82)
                corner = abs(u - lo) < 1e-6 or abs(u + w - hi) < 1e-6
                d = R(0.05, 0.08) if corner else R(0.03, 0.075)
                if corner or rng.random() > miss:
                    wpanel(W, u + GAP / 2, u + w - GAP / 2, z + GAP / 2, za - GAP / 2, 0, d, "M_Stone", jit=0.02)
                u += w; first = False
        z = za; row += 1
    return W

def jambs(W, uc, hw, z0, z1):
    z, k = z0, 0
    while z < z1 - 0.05:
        za = min(z + 0.43, z1)
        if z1 - za < 0.15:
            za = z1
        for s in (-1, 1):
            wj = 0.3 if (k + (s > 0)) % 2 else 0.22
            u0, u1 = (uc - hw - wj, uc - hw) if s < 0 else (uc + hw, uc + hw + wj)
            wpanel(W, u0 + GAP / 2, u1 - GAP / 2, z + GAP / 2, za - GAP / 2, 0, R(0.08, 0.095), "M_Stone", jit=0.01)
        z = za; k += 1

def arch_frame(W, uc, hw, spring):
    """Open stone arch (jambs + voussoirs), e.g. the gate passage."""
    O, ex, ey, ez, _ = W
    jambs(W, uc, hw, 0.0, spring)
    n = 11
    for i in range(n):
        t0 = math.pi * i / n + 0.01; t1 = math.pi * (i + 1) / n - 0.01
        ro = hw + (0.4 if i == n // 2 else R(0.28, 0.33)); ri = hw + 0.01
        pts = [(uc + ri * math.cos(t0), spring + ri * math.sin(t0)), (uc + ro * math.cos(t0), spring + ro * math.sin(t0)),
               (uc + ro * math.cos(t1), spring + ro * math.sin(t1)), (uc + ri * math.cos(t1), spring + ri * math.sin(t1))]
        prism(O, ex, ey, ez, pts, 0.0, R(0.09, 0.11), "M_Stone")
    return Arch(uc, hw, spring, 0.33)

class Lancet:
    def __init__(s, uc, hw, zb, spring, band=0.3):
        s.uc, s.hw, s.zb, s.spring, s.band = uc, hw, zb, spring, band
    def blocked(s, z0, z1):
        if z1 <= s.zb - 0.16:
            return None
        if z0 < s.spring:
            return (s.uc - s.hw - s.band, s.uc + s.hw + s.band)
        Ro = 1.6 * s.hw + s.band; dz = z0 - s.spring
        if dz >= Ro:
            return None
        h = math.sqrt(Ro * Ro - dz * dz) - 0.6 * s.hw + 0.03
        return (s.uc - h, s.uc + h) if h > 0 else None

def lancet_pts(uc, hw, zb, spring, n=6):
    r = 1.6 * hw; th = math.acos(0.6 * hw / r)
    right = [(uc - 0.6 * hw + r * math.cos(t), spring + r * math.sin(t)) for t in [th * k / n for k in range(n + 1)]]
    left = [(2 * uc - x, z) for x, z in reversed(right)]
    return [(uc - hw, zb), (uc + hw, zb)] + right + left[1:]

def lancet_window(W, uc, hw, zb, spring, kind="mullion"):
    O, ex, ey, ez, _ = W
    prism(O, ex, ey, ez, lancet_pts(uc, hw, zb, spring), 0.0, 0.012, "M_Void")
    jambs(W, uc, hw, zb, spring)
    r_in = 1.6 * hw + 0.01; th = math.acos(0.6 * hw / r_in); n = 4
    for s in (-1, 1):
        cx = uc - s * 0.6 * hw
        for i in range(n):
            ro = r_in + R(0.24, 0.3)
            t0, t1 = th * i / n + 0.012, th * (i + 1) / n - 0.012
            P = lambda r, t: (cx + s * r * math.cos(t), spring + r * math.sin(t))
            prism(O, ex, ey, ez, [P(r_in, t0), P(ro, t0), P(ro, t1), P(r_in, t1)], 0.0, R(0.085, 0.1), "M_Stone")
    wpanel(W, uc - hw - 0.14, uc + hw + 0.14, zb - 0.13, zb, 0.0, 0.13, "M_Stone", jit=0.008)
    snow_ledge(W, uc - hw - 0.14, uc + hw + 0.14, zb, 0.0, 0.13, 0.07)
    if kind == "mullion":
        wpanel(W, uc - 0.035, uc + 0.035, zb, spring + 0.75 * hw, 0.012, 0.075, "M_Stone")
    elif kind == "bars":
        for k in (-1, 1):
            wpanel(W, uc + k * hw * 0.45 - 0.018, uc + k * hw * 0.45 + 0.018, zb, spring + 0.4 * hw, 0.02, 0.055, "M_Iron")
    elif kind == "boarded":
        wbeam(W, (uc - hw - 0.12, zb + 0.3), (uc + hw + 0.14, zb + 0.6), w=0.17, d=0.04, mat="M_Wood", z0=0.09)
        wbeam(W, (uc - hw - 0.1, spring - 0.1), (uc + hw + 0.1, spring - 0.25), w=0.16, d=0.04, mat="M_Wood", z0=0.135)
    return Lancet(uc, hw, zb, spring)

def leaf(W, hu, z0, w, h, ang, dirn, tilt=0.0):
    """Plank door leaf hinged at wall coordinate hu; dirn=+1 extends to the right when closed;
    ang swings it outward, tilt (<0) makes the free end drop (torn hinge)."""
    O, ex, ey, ez, _ = W
    e1 = ex * (dirn * math.cos(ang)) + ez * math.sin(ang); e2 = V(ey)
    if tilt:
        e1, e2 = e1 * math.cos(tilt) + e2 * math.sin(tilt), -e1 * math.sin(tilt) + e2 * math.cos(tilt)
    e3 = e1.cross(e2)
    org = wp(W, hu, z0 + 0.02, 0.035)
    fbox(org, e1, e2, e3, 0.0, w, 0.0, h - 0.04, -0.03, 0.03, "M_Wood")
    for s in (-1, 1):
        for zz in (0.35, h - 0.45):
            fbox(org, e1, e2, e3, 0.06, w - 0.06, zz - 0.06, zz + 0.06, s * 0.03, s * 0.055, "M_Timber",
                 skip=() if s > 0 else ())
        k = 1
        while k * 0.23 < w - 0.1:
            fbox(org, e1, e2, e3, k * 0.23 - 0.008, k * 0.23 + 0.008, 0.03, h - 0.08, s * 0.03, s * 0.036, "M_Iron")
            k += 1

def door_leaves(W, uc, hw, z0, h, style):
    if style in ("closed", "plague"):
        wpanel(W, uc - hw, uc + hw, z0, z0 + h, 0.0, 0.03, "M_Wood")
        k = 1
        while uc - hw + k * 0.23 < uc + hw - 0.08:
            u = uc - hw + k * 0.23
            wpanel(W, u - 0.009, u + 0.009, z0 + 0.03, z0 + h - 0.03, 0.03, 0.036, "M_Iron"); k += 1
        for zz in (z0 + 0.4, z0 + h - 0.45):
            wpanel(W, uc - hw + 0.04, uc + hw - 0.04, zz - 0.04, zz + 0.04, 0.03, 0.05, "M_Iron")
        if style == "plague":
            plague_marks(W, uc, hw, z0 + h * 0.5, 0.05)
    else:
        wpanel(W, uc - hw, uc + hw, z0, z0 + h, -0.02, 0.006, "M_Void")
        if style == "open2glow":                       # forge glow deep inside
            wpanel(W, uc - hw * 0.75, uc + hw * 0.1, z0 + 0.35, z0 + 1.05, 0.006, 0.012, "M_Glow")
            wpanel(W, uc - hw * 0.5, uc - hw * 0.15, z0 + 0.35, z0 + 0.75, 0.012, 0.018, "M_Void")   # anvil block
        leaf(W, uc - hw, z0, hw, h, 1.75, 1)
        if style in ("open2", "open2glow"):
            leaf(W, uc + hw, z0, hw, h, 1.35, -1)
        else:                                          # "broken": right leaf hangs from one hinge
            leaf(W, uc + hw, z0, hw, h, 0.3, -1, tilt=-0.13)

def painted_x(W, uc, zc, s, out, mat, w=0.08):
    O, ex, ey, ez, _ = W
    for a in (1, -1):
        p0, p1 = V((uc - s, zc - a * s)), V((uc + s, zc + a * s))
        d = (p1 - p0).normalized(); n = V((-d.y, d.x)) * w / 2
        pts = [tuple(p0 - n), tuple(p0 + n * 0.7), tuple(p1 + n), tuple(p1 - n * 0.8)]
        prism(O, ex, ey, ez, pts, out, out + 0.004, mat)
        lo = p0 if p0.y < p1.y else p1                 # paint drips under the low end
        for k in range(2):
            u = lo.x + R(-0.04, 0.04); L = R(0.12, 0.35)
            prism(O, ex, ey, ez, [(u - 0.012, lo.y), (u + 0.012, lo.y), (u + 0.006, lo.y - L), (u - 0.006, lo.y - L)],
                  out, out + 0.004, mat)

def plague_marks(W, uc, hw, zc, out):
    """Door nailed shut with planks and a pale painted cross."""
    wbeam(W, (uc - hw - 0.15, zc - 0.45), (uc + hw + 0.15, zc + 0.25), w=0.18, d=0.045, mat="M_Wood", z0=out + 0.03)
    wbeam(W, (uc - hw - 0.12, zc + 0.35), (uc + hw + 0.18, zc - 0.2), w=0.17, d=0.045, mat="M_Wood", z0=out + 0.08)
    painted_x(W, uc, zc + 0.05, min(hw * 0.8, 0.45), out + 0.13, "M_Plaster", w=0.09)

def rect_door(W, uc, hw, h, style="closed"):
    jambs(W, uc, hw, 0.0, h)
    wpanel(W, uc - hw - 0.34, uc + hw + 0.34, h, h + 0.3, 0.0, 0.13, "M_Timber", jit=0.01)
    snow_ledge(W, uc - hw - 0.34, uc + hw + 0.34, h + 0.3, 0.0, 0.13, 0.06)
    door_leaves(W, uc, hw, 0.0, h, style)
    return Rect(uc - hw - 0.31, uc + hw + 0.31, 0.0, h + 0.31)

def buttress(W, u, h, d=0.75, w=0.32):
    O, ex, ey, ez, _ = W
    z, zst = 0.0, h * 0.55
    while z < h - 0.05:
        za = min(z + R(0.32, 0.4), h)
        if h - za < 0.15:
            za = h
        dd = d if z < zst else d * 0.62
        wpanel(W, u - w + GAP / 2, u + w - GAP / 2, z + GAP / 2, za - GAP / 2, 0.0, dd, "M_Stone", jit=0.015)
        z = za
    org = wp(W, u - w - 0.02, 0.0, 0.0)
    prism(org, ez, ey, ex, [(0.0, h), (d * 0.62 + 0.05, h), (d * 0.62 + 0.05, h + 0.1), (0.0, h + 0.1 + d * 0.7)],
          0.0, 2 * w + 0.04, "M_Stone")
    with snow_layer():
        prism(org, ez, ey, ex, [(0.0, h + 0.1 + d * 0.7), (d * 0.62 + 0.05, h + 0.1), (d * 0.62 + 0.07, h + 0.16),
                                (0.0, h + 0.18 + d * 0.7)], 0.02, 2 * w + 0.02, "M_Snow")

def gable(side, window):
    W = wall_frame(side, HWT, HDT)
    hg = ZR - ZT
    zc = ZT + min(1.7, 0.47 * hg); xc = (ZR - zc) / PITCH - 0.08
    wbeam(W, (-xc, zc), (xc, zc), w=0.17, d=0.08)
    if window and hg > 2.7:
        for s in (-1, 1):
            wbeam(W, (s * 0.55, ZT + 0.02), (s * 0.55, zc - 0.08), w=0.15, d=0.08)
            wbeam(W, (s * 0.68, ZT + 0.1), (s * (xc - 0.1), zc - 0.1), w=0.14, d=0.07)
        zs, zh = ZT + 0.45, min(ZT + 1.42, zc - 0.2)
        wbeam(W, (-0.47, zs - 0.06), (0.47, zs - 0.06), w=0.12, d=0.075)
        wbeam(W, (-0.47, zh + 0.06), (0.47, zh + 0.06), w=0.12, d=0.075)
        timber_window(W, 0.0, 0.34, zs, zh, None, window if isinstance(window, str) else None)
    else:
        wbeam(W, (0, ZT + 0.02), (0, zc - 0.08), w=0.17, d=0.08)
        for s in (-1, 1):
            wbeam(W, (s * 0.15, ZT + 0.1), (s * (xc - 0.1), zc - 0.1), w=0.14, d=0.07)
            wbeam(W, (s * (HWT - 0.5), ZT + 0.1), (s * 0.2, zc - 0.1), w=0.13, d=0.07, z0=0.004)
    if ZR - zc > 0.7:
        wbeam(W, (0, zc + 0.08), (0, ZR - 0.25), w=0.16, d=0.08)
        top = min(zc + 1.05, ZR - 0.35)
        for s in (-1, 1):
            wbeam(W, (s * (xc - 0.3), zc + 0.1), (s * 0.08, top), w=0.12, d=0.07)
    return W

def timber_wall2(side, lv, wins=(), door=None):
    """Half-timbered wall of storey lv with automatic post / brace layout around windows and a door.
    wins: [(u, shutters, state)], door: (u, half width, height, style)."""
    kind, z0, z1, hx, hy = LEVELS[lv]
    W = frame(side, hx, hy); half = W[4]
    zb0, zb1 = z0 + 0.24, z1 - 0.22
    c = half - 0.09
    spans = [(u - 0.65, u + 0.65, ("win", sh, st)) for u, sh, st in wins]
    if door:
        u, dw, dh, style = door
        spans.append((u - dw - 0.1, u + dw + 0.1, ("door", dw, dh, style)))
    spans.sort(key=lambda s: s[0])
    bays, cur = [], -c
    def fill(a, b):
        n = max(1, int(round((b - a) / 1.35)))
        for i in range(n):
            bays.append((a + (b - a) * i / n, a + (b - a) * (i + 1) / n, (rng.choice(["x", "d", "d", "p"]),
                                                                          rng.choice((-1, 1)))))
    for a, b, what in spans:
        a, b = max(a, -c), min(b, c)
        if a - cur < 0.45:
            a = cur
        else:
            fill(cur, a)
        bays.append((a, b, what)); cur = b
    if c - cur < 0.45 and bays and bays[-1][2][0] in ("win", "door"):
        a, b, what = bays.pop(); bays.append((a, c, what))
    elif c - cur > 0.02:
        fill(cur, c)
    doors = [(a, b) for a, b, w in bays if w[0] == "door"]
    for a, b in free_intervals(-half - 0.03, half + 0.03, doors):
        wbeam(W, (a, z0 + 0.12), (b, z0 + 0.12), w=0.24, d=0.1)
        if z0 > 0.5:
            snow_ledge(W, a, b, z0 + 0.24, 0.0, 0.11, 0.06)
    wbeam(W, (-half - 0.03, z1 - 0.11), (half + 0.03, z1 - 0.11), w=0.22, d=0.09)
    posts = sorted({round(a, 3) for a, b, w in bays} | {round(bays[-1][1], 3)})
    for u in posts:
        lo = z0 if any(abs(u - a) < 0.01 or abs(u - b) < 0.01 for a, b in doors) else zb0
        wbeam(W, (u, lo), (u, zb1), w=0.18, d=0.085)
    for a, b, what in bays:
        a0, b0 = a + 0.09, b - 0.09; k = what[0]
        if k == "x":
            wbeam(W, (a0, zb0), (b0, zb1), w=0.15, d=0.07)
            wbeam(W, (a0, zb1), (b0, zb0), w=0.15, d=0.075, z0=0.004)
        elif k == "d":
            if what[1] > 0:
                wbeam(W, (a0, zb0), (b0, zb1), w=0.15, d=0.07)
            else:
                wbeam(W, (a0, zb1), (b0, zb0), w=0.15, d=0.07)
        elif k == "p":
            wbeam(W, (a0, (zb0 + zb1) / 2), (b0, (zb0 + zb1) / 2 + R(-0.05, 0.05)), w=0.14, d=0.07)
        elif k == "win":
            cc = (a + b) / 2; ww = min(0.42, (b - a) / 2 - 0.25)
            zs, zh = z0 + 1.0, min(z0 + 2.12, zb1 - 0.12)
            wbeam(W, (a0, zs - 0.07), (b0, zs - 0.07), w=0.14, d=0.075)
            wbeam(W, (a0, zh + 0.07), (b0, zh + 0.07), w=0.14, d=0.075)
            for s in (-1, 1):
                wbeam(W, (cc + s * (ww + 0.07), zs), (cc + s * (ww + 0.07), zh), w=0.14, d=0.075)
            wbeam(W, (a0, zb0), (b0, zs - 0.14), w=0.12, d=0.065)
            wbeam(W, (a0, zs - 0.14), (b0, zb0), w=0.12, d=0.07, z0=0.004)
            timber_window(W, cc, ww, zs, zh, what[1], what[2])
        elif k == "door":
            _, dw, dh, style = what; uc = (a + b) / 2
            wbeam(W, (a0 - 0.05, z0 + dh + 0.08), (b0 + 0.05, z0 + dh + 0.08), w=0.16, d=0.08)
            door_leaves(W, uc, dw, z0, dh, style)
            if zb1 - (z0 + dh + 0.16) > 0.5:
                wbeam(W, (a0, z0 + dh + 0.16), (b0, zb1), w=0.12, d=0.065)
                wbeam(W, (a0, zb1), (b0, z0 + dh + 0.16), w=0.12, d=0.07, z0=0.004)
    return W

def board_wall(side, z0, z1, holes=(), gable=False, miss=0.05):
    """Barn cladding: vertical planks (some missing), rails; gable planks follow the roof line.
    holes: [(u0, u1, z0, z1)]."""
    W = frame(side, HW, HD); O, ex, ey, ez, half = W
    def top(u):
        return ZR - abs(u) * PITCH - 0.05
    hi_all = ZR if gable else z1
    u = -half - 0.05
    while u < half + 0.05:
        u1 = min(u + R(0.2, 0.3), half + 0.05)
        if u1 - u < 0.08:
            break
        if rng.random() >= miss:
            ua, ub = u + 0.012, u1 - 0.012; out = R(0.03, 0.05)
            cuts = [(h[2], h[3]) for h in holes if h[0] < ub and h[1] > ua]
            for za, zb in free_intervals(z0, hi_all, cuts):
                to_top = zb >= hi_all - 1e-6
                if to_top and gable:
                    if za > min(top(ua), top(ub)) - 0.1:
                        continue
                    pts = [(ua, za), (ub, za), (ub, top(ub))] + ([(0.0, top(0.0))] if ua < 0 < ub else []) + [(ua, top(ua))]
                else:
                    pts = [(ua, za), (ub, za), (ub, zb), (ua, zb)]
                prism(O, ex, ey, ez, pts, 0.0, out, "M_Wood")
        u = u1
    for zr in (z0 + 0.35, (z0 + z1) / 2, z1 - 0.25):
        cuts = [(h[0] - 0.05, h[1] + 0.05) for h in holes if h[2] < zr < h[3]]
        for a, b in free_intervals(-half - 0.06, half + 0.06, cuts):
            wpanel(W, a, b, zr - 0.07, zr + 0.07, 0.05, 0.1, "M_Timber", jit=0.01)
            snow_ledge(W, a, b, zr + 0.07, 0.05, 0.12, 0.04)
    return W

def cores(passage=None):
    n = len(LEVELS)
    for i, (kind, z0, z1, hx, hy) in enumerate(LEVELS):
        mat = "M_Plaster" if kind == "timber" else "M_Mortar"
        if i == n - 1:
            prof = [(-hx, z0), (hx, z0), (hx, z1), (0.0, ZR - 0.06), (-hx, z1)]
            loft([[V((x, -hy + 2 * hy * k / 8, z)) for x, z in prof] for k in range(9)], mat)
        elif i == 0 and passage:
            pw, ps = passage
            box(-hx, -pw, -hy, hy, z0, z1, mat, skip=("bottom",))
            box(pw, hx, -hy, hy, z0, z1, mat, skip=("bottom",))
            pts = [(pw, ps)] + [(pw * math.cos(math.pi * k / 12), ps + pw * math.sin(math.pi * k / 12))
                                for k in range(1, 12)] + [(-pw, ps), (-pw, z1), (pw, z1)]
            prism(V((0, -hy, 0)), X, Z, Y, pts, 0.0, 2 * hy, mat)
        else:
            box(-hx, hx, -hy, hy, z0, z1, mat, skip=("bottom",) if i == 0 else ())

def joists2(lv):
    _, z0, _, hx, hy = LEVELS[lv]; _, _, _, lx, ly = LEVELS[lv - 1]
    if hy - ly > 0.08:
        for s in (-1, 1):
            for u in np.arange(-lx + 0.3, lx - 0.29, 0.6):
                y0, y1 = sorted((s * ly, s * (hy + 0.07)))
                box(u - 0.08, u + 0.08, y0, y1, z0 - 0.2, z0 + 0.02, "M_Timber")
    if hx - lx > 0.19:
        for s in (-1, 1):
            for v in np.arange(-ly + 0.3, ly - 0.29, 0.6):
                x0, x1 = sorted((s * lx, s * (hx + 0.07)))
                box(x0, x1, v - 0.08, v + 0.08, z0 - 0.2, z0 + 0.02, "M_Timber")

def wall_chimney(side, u):
    """Stone base + leaning brick stack against an eave wall ('l' or 'r'); returns the wall opening."""
    sx = -1 if side == "l" else 1
    yc = -u if side == "l" else u
    def xr(d0, d1):
        a, b = sx * (HW + d0), sx * (HW + d1)
        return (min(a, b), max(a, b))
    y0, y1 = yc - 0.38, yc + 0.38
    CHIMS.append(xr(0.08, 0.68) + (y0, y1))
    zch = ZR - 0.55
    z, i = 0.0, 0
    while z < H0 + 0.35:
        ins = 0.03 if i % 2 else 0.0
        a, b = xr(-0.02, 0.78 - ins + R(-0.02, 0.02))
        box(a, b, y0 - 0.1 + ins, y1 + 0.1 - ins, z, z + 0.38 - GAP, "M_Stone", skip=("bottom",))
        z += 0.38; i += 1
    a, b = xr(-0.02, 0.81)
    box(a, b, y0 - 0.13, y1 + 0.13, z - GAP, z + 0.08, "M_Stone"); z += 0.08
    i = 0
    while z < zch:
        sh = 0.04 * max(0.0, z - H0 - 0.4)
        sy = y0 + (y1 - y0) * (0.42 if i % 2 else 0.6); ins = 0.012 * (i % 2)
        top = z > zch - 0.8
        a, b = xr(0.08 + sh, 0.68 - ins + sh)
        if not (top and rng.random() < 0.3):
            box(a, b, y0, sy - 0.012, z, z + 0.195, "M_Brick", skip=("bottom",))
        if not (top and rng.random() < 0.3):
            box(a, b, sy + 0.012, y1, z, z + 0.195, "M_Brick", skip=("bottom",))
        z += 0.22; i += 1
    sh = 0.04 * max(0.0, z - H0 - 0.4)
    a, b = xr(0.01 + sh, 0.75 + sh); box(a, b, y0 - 0.07, y1 + 0.07, z, z + 0.12, "M_Stone")
    a, b = xr(0.22 + sh, 0.54 + sh); box(a, b, y0 + 0.2, y1 - 0.2, z + 0.12, z + 0.32, "M_Brick", skip=("bottom",))
    a, b = xr(0.27 + sh, 0.49 + sh); box(a, b, y0 + 0.25, y1 - 0.25, z + 0.32, z + 0.325, "M_Void", skip=("bottom",))
    with snow_layer():
        a, b = xr(0.03 + sh, 0.73 + sh); box(a, b, y0 - 0.06, y1 + 0.05, z + 0.12, z + 0.2, "M_Snow", skip=("bottom",))
    return Rect(u - 0.5, u + 0.5, 0.0, H0 + 1.0)

def roof_chimney(x, y, hgt=1.0):
    s = 0.31
    CHIMS.append((x - s, x + s, y - s, y + s))
    z = ZR - (abs(x) + s) * PITCH - 0.35
    ztop = ZR - max(abs(x) - s, 0.0) * PITCH + hgt
    i = 0
    while z < ztop:
        sy = y - s + 2 * s * (0.42 if i % 2 else 0.6); ins = 0.012 * (i % 2)
        top = z > ztop - 0.6
        if not (top and rng.random() < 0.3):
            box(x - s + ins, x + s, y - s, sy - 0.012, z, z + 0.195, "M_Brick", skip=("bottom",))
        if not (top and rng.random() < 0.3):
            box(x - s + ins, x + s, sy + 0.012, y + s, z, z + 0.195, "M_Brick", skip=("bottom",))
        z += 0.22; i += 1
    box(x - s - 0.07, x + s + 0.07, y - s - 0.07, y + s + 0.07, z, z + 0.12, "M_Stone")
    box(x - 0.13, x + 0.13, y - 0.13, y + 0.13, z + 0.12, z + 0.3, "M_Brick", skip=("bottom",))
    box(x - 0.08, x + 0.08, y - 0.08, y + 0.08, z + 0.3, z + 0.305, "M_Void", skip=("bottom",))
    with snow_layer():
        box(x - s - 0.05, x + s + 0.04, y - s - 0.06, y + s + 0.05, z + 0.12, z + 0.2, "M_Snow", skip=("bottom",))

def roof():
    roof_side(1); roof_side(-1); ridge_cap(); icicles(1); icicles(-1)

def drift_run(W, a, b, hmul=1.0):
    m = max(2, int((b - a) / 0.35)); ph = R(0, 6.3); secs = []
    for k in range(m + 1):
        u = a + (b - a) * k / m
        e = max(0.06, min(1.0, (u - a) / 0.6, (b - u) / 0.6))
        h = (0.42 + 0.15 * math.sin(1.7 * u + ph) + 0.06 * math.sin(4.1 * u)) * e * hmul
        D = (0.9 + 0.25 * math.sin(1.1 * u + 2 * ph)) * (0.35 + 0.65 * e) * hmul
        prof = [(-0.06, 0.0), (D, 0.0), (D * 0.55, h * 0.33), (D * 0.22, h * 0.8), (0.0, h)]
        secs.append([wp(W, u, z, o) for o, z in prof])
    loft(secs, "M_Snow")

def drifts2(gaps=None, hmul=1.0):
    gaps = gaps or {}
    with snow_layer():
        for side in "fblr":
            W = frame(side, HW, HD); half = W[4]
            for a, b in free_intervals(-half - 0.45, half + 0.45, gaps.get(side, [])):
                if b - a > 0.5:
                    drift_run(W, a, b, hmul)

def footprints(path):
    with snow_layer():
        k = 0
        for (x0, y0), (x1, y1) in zip(path, path[1:]):
            L = math.hypot(x1 - x0, y1 - y0); fx, fy = (x1 - x0) / L, (y1 - y0) / L; sx, sy = -fy, fx
            for i in range(int(L / 0.36)):
                side = 0.11 if k % 2 else -0.11; k += 1
                cx = x0 + fx * 0.36 * i + sx * side + R(-0.02, 0.02); cy = y0 + fy * 0.36 * i + sy * side
                pts = [(cx + fx * 0.14 * math.cos(t) + sx * 0.06 * math.sin(t), cy + fy * 0.14 * math.cos(t) + sy * 0.06 * math.sin(t))
                       for t in [2 * math.pi * q / 8 for q in range(8)]]
                prism(V((0, 0, 0)), X, Y, Z, pts, 0.002, 0.005, "M_SnowShade")

def rubble2(counts=(4, 4, 4, 3), clear=None):
    clear = clear or {}
    for side, n in zip("flrb", counts):
        W = frame(side, HW, HD); half = W[4]; k = tries = 0
        while k < n and tries < 60:
            tries += 1
            u = R(-half + 0.2, half - 0.2)
            if any(a < u < b for a, b in clear.get(side, [])):
                continue
            t = R(0, math.pi); c = wp(W, u, 0.0, R(0.12, 0.6))
            a = V((math.cos(t), math.sin(t), 0)); b = Z.cross(a); sz = R(0.1, 0.2)
            fbox(c, a, b, Z, -sz, sz, -sz * 0.7, sz * 0.7, 0.0, sz * R(0.6, 0.9), "M_Stone", skip=("bottom",), jit=0.02)
            k += 1

def awning(side, u0, u1, zw, zo, depth):
    """Lean-to plank roof on posts against a wall, snow on top."""
    W = frame(side, HW, HD); O, ex, ey, ez, _ = W
    for u in (u0 + 0.1, (u0 + u1) / 2, u1 - 0.1):
        seg(wp(W, u, -0.2, depth - 0.1), wp(W, u + R(-0.05, 0.05), zo - 0.05, depth - 0.1 + R(-0.06, 0.06)), 0.15, "M_Timber")
    seg(wp(W, u0 - 0.1, zo - 0.1, depth - 0.1), wp(W, u1 + 0.1, zo - 0.08, depth - 0.1), 0.15, "M_Timber")
    seg(wp(W, u0, zw - 0.1, 0.08), wp(W, u1, zw - 0.1, 0.08), 0.13, "M_Timber")
    for u in np.arange(u0 + 0.15, u1, 0.8):
        seg(wp(W, u, zw - 0.04, 0.05), wp(W, u, zo + 0.02, depth + 0.2), 0.09, "M_Timber")
    pt, pl = wp(W, u0, zw + 0.03, 0.0), wp(W, u0, zo + 0.07, depth + 0.3)
    es = (pl - pt); L = es.length; es.normalize()
    A, B = es, ex
    if A.cross(B).z < 0:
        pass
    C = A.cross(B)
    flip = C.z < 0
    def board(s0, s1, a0, a1, w0, w1, mat):
        if flip:                                       # keep a right-handed frame with the normal up
            fbox(pt, ex, es, ex.cross(es), a0, a1, s0, s1, w0, w1, mat)
        else:
            fbox(pt, es, ex, C, s0, s1, a0, a1, w0, w1, mat)
    u = 0.0
    while u < u1 - u0:
        w = R(0.24, 0.32)
        if rng.random() > 0.08:
            board(0.0, L - R(0.0, 0.25), u + 0.01, min(u + w, u1 - u0) - 0.01, 0.0, 0.04, "M_Wood")
        u += w
    with snow_layer():
        u = 0.0
        while u < u1 - u0:
            w = R(0.5, 1.1); u2 = min(u + w, u1 - u0)
            board(R(0.0, 0.25), L - R(0.05, 0.35), u + 0.04, u2 - 0.04, 0.04, 0.04 + R(0.07, 0.12), "M_Snow")
            u = u2 + R(0.0, 0.25)

def anvil(M):
    with xform(M):
        pts = [(0.26 * math.cos(k * math.pi / 5), 0.26 * math.sin(k * math.pi / 5)) for k in range(10)]
        prism(V((0, 0, 0)), X, Y, Z, pts, 0.0, 0.55, "M_Wood")
        box(-0.12, 0.12, -0.09, 0.09, 0.55, 0.68, "M_Iron")
        box(-0.07, 0.07, -0.06, 0.06, 0.68, 0.76, "M_Iron")
        box(-0.24, 0.2, -0.1, 0.1, 0.76, 0.88, "M_Iron")
        hexa([(0.2, -0.08, 0.8), (0.46, -0.01, 0.86), (0.46, 0.01, 0.86), (0.2, 0.08, 0.8),
              (0.2, -0.08, 0.88), (0.46, -0.01, 0.88), (0.46, 0.01, 0.88), (0.2, 0.08, 0.88)], "M_Iron")
        seg((-0.1, 0.0, 0.9), (0.15, 0.25, 0.93), 0.04, "M_Wood")                 # hammer handle
        box(-0.16, -0.04, -0.04, 0.04, 0.88, 0.97, "M_Iron")
        with snow_layer():
            box(-0.22, 0.18, -0.1, 0.1, 0.88, 0.93, "M_Snow", skip=("bottom",))

def anvil_sign(W, u):
    O, ex, ey, ez, _ = W
    zb = 2.62
    wpanel(W, u - 0.13, u + 0.13, zb - 0.2, zb + 0.12, 0.0, 0.05, "M_Iron")
    fbox(O, ex, ey, ez, u - 0.025, u + 0.025, zb - 0.03, zb + 0.03, 0.0, 0.95, "M_Iron")
    seg(wp(W, u, zb - 0.55, 0.03), wp(W, u, zb - 0.02, 0.62), 0.035, "M_Iron")
    seg(wp(W, u, zb - 0.02, 0.3), wp(W, u, zb - 0.3, 0.3), 0.02, "M_Iron")
    seg(wp(W, u, zb - 0.02, 0.85), wp(W, u, zb - 0.22, 0.86), 0.02, "M_Iron")
    org = wp(W, u, zb - 0.3, 0.0)
    pts = [(0.12, -0.04), (0.3, 0.0), (0.92, 0.0), (0.92, -0.12), (0.76, -0.17), (0.72, -0.31), (0.86, -0.42),
           (0.34, -0.42), (0.48, -0.31), (0.46, -0.17), (0.3, -0.12)]
    prism(org, ez, ey, ex, pts, -0.025, 0.025, "M_Iron")

def portcullis(W, pw, ps, zbot, out):
    for u in np.arange(-pw + 0.18, pw - 0.1, 0.3):
        top = ps + math.sqrt(max(pw * pw - u * u, 0.0)) - 0.03
        if top - zbot > 0.12:
            wpanel(W, u - 0.035, u + 0.035, zbot - 0.28, top, out - 0.04, out + 0.04, "M_Iron", skip=())
    for zz in (zbot + 0.08, zbot + 0.45, zbot + 0.82, zbot + 1.2):
        hw_ = pw if zz < ps else math.sqrt(max(pw * pw - (zz - ps) ** 2, 0.0)) - 0.04
        if hw_ > 0.15:
            wpanel(W, -hw_, hw_, zz - 0.04, zz + 0.04, out - 0.06, out + 0.06, "M_Iron", skip=())

def cage(x, y, zbeam, ztop, zbot, r=0.33):
    """Gibbet cage hanging from a beam, bones inside."""
    seg((x, y - 0.0, zbeam), (x, y, ztop + 0.05), 0.035, "M_Iron")
    seg((x, y, ztop + 0.05), (x, y, ztop + 0.25), 0.05, "M_Iron")
    for k in range(8):
        a = 2 * math.pi * k / 8
        seg((x + r * math.cos(a), y + r * math.sin(a), zbot), (x + r * 0.92 * math.cos(a), y + r * 0.92 * math.sin(a), ztop),
            0.03, "M_Iron")
    for zz, rr in ((zbot, r), ((zbot + ztop) / 2, r * 1.04), (ztop, r * 0.92)):
        pts = [V((x + rr * math.cos(2 * math.pi * k / 10), y + rr * math.sin(2 * math.pi * k / 10), zz)) for k in range(11)]
        for a, b in zip(pts, pts[1:]):
            seg(a, b, 0.035, "M_Iron")
    seg((x - r * 0.9, y, ztop), (x + r * 0.9, y, ztop + 0.25), 0.03, "M_Iron")
    seg((x + r * 0.9, y, ztop), (x - r * 0.9, y, ztop + 0.25), 0.03, "M_Iron")
    box(x - 0.1, x + 0.1, y - 0.11, y + 0.09, zbot + 0.1, zbot + 0.3, "M_Plaster")           # skull slumped down
    box(x - 0.03, x + 0.03, y - 0.115, y - 0.11, zbot + 0.2, zbot + 0.24, "M_Void")
    for a, b in (((x - 0.05, y + 0.05, zbot + 0.12), (x - 0.25, y - 0.55, zbot - 0.2)),     # arm through the bars
                 ((x + 0.12, y, zbot + 0.05), (x + 0.2, y + 0.1, zbot + 0.6)),
                 ((x - 0.15, y + 0.1, zbot + 0.05), (x + 0.05, y - 0.15, zbot + 0.08))):
        seg(a, b, 0.045, "M_Plaster")
    with snow_layer():
        pts = [(x + r * 0.92 * math.cos(2 * math.pi * k / 10), y + r * 0.92 * math.sin(2 * math.pi * k / 10)) for k in range(10)]
        prism(V((0, 0, 0)), X, Y, Z, pts, ztop + 0.25, ztop + 0.3, "M_Snow")

def pyramid_roof(c, hb, zb, h, over=0.4, n=7):
    cx, cy = c
    def sq(r, z):
        return [V((cx - r, cy - r, z)), V((cx + r, cy - r, z)), V((cx + r, cy + r, z)), V((cx - r, cy + r, z))]
    r0 = hb + over
    for i in range(n):
        za, zb_ = zb + h * i / n, zb + h * (i + 1) / n
        ra, rb = r0 * (1 - i / n) + 0.07, r0 * (1 - (i + 1) / n) + 0.02
        loft([sq(ra, za), sq(ra, za + 0.05), sq(max(rb, 0.04), zb_ + 0.03)], "M_RoofTile")
        if i < 2:
            with snow_layer():
                loft([sq(ra + 0.01, za + 0.05), sq(ra - 0.05, za + 0.12), sq(ra - 0.12 - (r0 / n) * 0.3, za + 0.12 + h / n * 0.35)],
                     "M_Snow")

def bell_tower(cy, th, zs=7.8, zb=10.0, roof_h=3.1, tilt=7.0):
    """Square stone tower in front of the chapel, leaning (sheared, the base stays on the ground)."""
    M = Matrix.Identity(4); M[0][2] = math.tan(math.radians(tilt))
    with xform(M):
        box(-th, th, cy - th, cy + th, 0.0, zs, "M_Mortar", skip=("bottom",))
        for side in "fblr":
            W = frame(side, th, th, (0.0, cy)); ops = []
            if side == "f":
                ops.append(Arch(0.0, 0.62, 1.85, 0.31)); arch_door(W, 0.0, 0.62, 1.85)
                ops.append(lancet_window(W, 0.0, 0.24, 4.6, 5.2, "bars"))
                skull(W, 0.0, 2.95, 0.1)
            elif side in "lr":
                ops.append(stone_window(W, 0.0, 0.09, 5.4, 0.5, "slit"))
            stone_wall2(side, ops, hx=th, hy=th, c=(0.0, cy), z1=zs)
            if side != "b":
                with snow_layer():
                    for a, b in free_intervals(-th - 0.4, th + 0.4, [(-0.85, 0.85)] if side == "f" else []):
                        drift_run(W, a, b)
        box(-th - 0.16, th + 0.16, cy - th - 0.16, cy + th + 0.16, zs, zs + 0.24, "M_Stone")
        with snow_layer():
            for (x0, x1, y0, y1) in ((-th - 0.15, th + 0.15, cy - th - 0.15, cy - th + 0.02),
                                     (-th - 0.15, th + 0.15, cy + th - 0.02, cy + th + 0.15),
                                     (-th - 0.15, -th + 0.02, cy - th, cy + th), (th - 0.02, th + 0.15, cy - th, cy + th)):
                box(x0, x1, y0, y1, zs + 0.24, zs + 0.24 + R(0.06, 0.11), "M_Snow", skip=("bottom",))
        p = th - 0.1
        for sx in (-1, 1):
            for sy in (-1, 1):
                box(sx * p - 0.13, sx * p + 0.13, cy + sy * p - 0.13, cy + sy * p + 0.13, zs + 0.24, zb, "M_Timber")
        for (a, b) in (((-p, -p), (p, -p)), ((p, -p), (p, p)), ((p, p), (-p, p)), ((-p, p), (-p, -p))):
            seg((a[0], cy + a[1], zs + 1.05), (b[0], cy + b[1], zs + 1.05), 0.12, "M_Timber")     # rail
            seg((a[0], cy + a[1], zb - 0.05), (b[0], cy + b[1], zb - 0.05), 0.2, "M_Timber")      # top plate
            seg((a[0], cy + a[1], zs + 1.1), (b[0] * 0.5 + a[0] * 0.5, cy + (b[1] + a[1]) / 2, zb - 0.15), 0.1, "M_Timber")
        seg((-p, cy, zb - 0.3), (p, cy, zb - 0.3), 0.17, "M_Timber")                                # headstock
        hz = zb - 0.38
        with xform(Matrix.Translation((0, cy, hz)) @ Matrix.Rotation(math.radians(14), 4, "X")
                   @ Matrix.Translation((0, -cy, -hz))):
            prof = [(0.06, 0.0), (0.24, -0.04), (0.3, -0.25), (0.34, -0.55), (0.46, -0.8), (0.47, -0.86)]
            loft([ring(r, hz + dz, 12, 0.0, cy) for r, dz in prof], "M_Iron")
            seg((0.0, cy, hz - 0.6), (0.0, cy + 0.02, hz - 0.95), 0.06, "M_Iron")                  # clapper
        seg((0.0, cy + 0.2, hz - 0.8), (0.1, cy + 0.25, zs + 0.3), 0.03, "M_Wood")                # bell rope
        pyramid_roof((0.0, cy), th, zb + 0.08, roof_h)
        za = zb + 0.08 + roof_h
        seg((0.0, cy, za - 0.1), (0.04, cy, za + 1.25), 0.08, "M_Iron")
        cr = Matrix.Rotation(math.radians(-17), 4, "Y")
        seg(V((0.02, cy, za + 0.85)) + cr @ V((-0.42, 0, 0)), V((0.02, cy, za + 0.85)) + cr @ V((0.42, 0, 0)), 0.07, "M_Iron")

def finish_house(name, perches=(), props=(), ridge_ys=(-0.45, 0.5)):
    ob = emit(f"SM_House_{name}", C_HOUSES, mats, deform=warp)
    sn = emit(f"SM_House_{name}_Snow", C_HOUSES, mats, deform=warp, geo=SNOW)
    zr = ZR + ROOF_T / CA + 0.3
    P = [warp(V((0.0, f * GOV, zr))) for f in ridge_ys] + [warp(V(p)) for p in perches]
    return dict(name=name, ob=ob, snow=sn, front=HDT + 0.4, perches=P, props=list(props))

# =====================================================================
# the houses
# =====================================================================
def house_apothecary():
    """House 1 = the approved house_01, appended unchanged (materials remapped to the village ones)."""
    setup("Apothecary", 2.4, 3.0, [("stone", 3.0), ("timber", 2.9)], jet=(0.15, 0.45), pitch=1.5,
          lean=(0.065, -0.025), sag=0.42, settle=0.012)
    with bpy.data.libraries.load(os.path.join(OUT, "house_01.blend"), link=False) as (src, dst):
        dst.objects = ["SM_House_01", "SM_House_01_Snow"]
    ob, sn = sorted(dst.objects, key=lambda o: len(o.name))
    for o, nm in ((ob, "SM_House_Apothecary"), (sn, "SM_House_Apothecary_Snow")):
        for i, m in enumerate(o.data.materials):
            o.data.materials[i] = mats[m.name.split(".")[0]]
        o.name = o.data.name = nm
        C_HOUSES.objects.link(o)
    for m in list(bpy.data.materials):
        if m.users == 0 and m not in mats.values():
            bpy.data.materials.remove(m)
    for g in list(bpy.data.node_groups):
        if g.users == 0 and "." in g.name:
            bpy.data.node_groups.remove(g)
    zr = ZR + 0.585
    P = [warp(V(p)) for p in ((0.0, -1.7, zr), (0.0, 2.2, zr), (2.12, -3.67, 2.65))]
    props = [("Barrel", (1.6, -3.55, 0), 0.0), ("CrateA", (-2.05, -3.45, 0), 0.0), ("CrateB", (-1.95, -3.5, 0.6), 0.0),
             ("Firewood", (HW + 0.04, 1.55, 0), 0.0), ("Stump", (3.1, -2.7, 0), 0.0)]
    return dict(name="Apothecary", ob=ob, snow=sn, front=HDT + 0.4, perches=P, props=props)

def house_smithy():
    setup("Smithy", 3.0, 4.0, [("stone", 3.0), ("timber", 2.6)], jet=(0.15, 0.4), pitch=1.0,
          lean=(-0.035, 0.02), sag=0.32, settle=-0.006,
          bare={-1: [(1.6, 1.1, 0.75, 0.6)], 1: [(-2.2, 0.9, 0.8, 0.6), (2.4, 2.4, 0.5, 0.4)]}, seed=21)
    chim = wall_chimney("l", 1.3)
    cores()
    W = frame("f", HW, HD)
    stone_wall2("f", [rect_door(W, -0.7, 1.1, 2.3, "open2glow"), stone_window(W, 1.95, 0.32, 1.5, 0.4, "bars")])
    anvil_sign(W, 1.08)
    W = frame("r", HW, HD)
    stone_wall2("r", [stone_window(W, -1.6, 0.36, 1.45, 0.42, "cross")])
    awning("r", -3.3, 1.2, 2.65, 2.0, 2.5)
    anvil(T(HW + 1.1, 0.5, 0.0, yaw=0.4))
    W = frame("l", HW, HD)
    stone_wall2("l", [chim, stone_window(W, -2.1, 0.34, 1.5, 0.4, "boarded")])
    W = frame("b", HW, HD)
    stone_wall2("b", [stone_window(W, 0.9, 0.34, 1.5, 0.4, "boarded")])
    joists2(1)
    Wf = timber_wall2("f", 1, wins=[(-1.5, "open", None), (1.5, None, "boarded")])
    crumble(Wf, 0.1, LEVELS[1][1] + 0.85, 0.35, 0.3)
    Wb = timber_wall2("b", 1, wins=[(0.0, "broken", None)])
    crumble(Wb, -2.2, LEVELS[1][1] + 1.4, 0.4, 0.45)
    timber_wall2("l", 1, wins=[(-2.4, None, None)])
    Wr = timber_wall2("r", 1, wins=[(-1.7, "askew", None), (1.9, None, "boarded")])
    crumble(Wr, 0.2, LEVELS[1][1] + 1.3, 0.45, 0.4)
    gable("f", True); gable("b", False)
    roof()
    drifts2({"f": [(-1.95, 0.55)], "r": [(-3.2, 1.0)]})
    footprints([(-0.7, -HD - 0.3), (-0.5, -HD - 2.2), (0.3, -HD - 4.2)])
    rubble2((2, 3, 1, 3), {"f": [(-2.0, 0.6)], "r": [(-3.5, 1.5)]})
    return finish_house("Smithy", props=[("Barrel", (HW + 2.0, -0.4, 0), 0.3), ("CrateA", (HW + 0.6, -2.6, 0), 0.2),
                                         ("Stump", (-2.4, -HD - 1.4, 0), 0.0)])

def house_hut():
    setup("Hut", 2.3, 2.9, [("stone", 2.3)], pitch=1.25, lean=(0.025, 0.0), sag=0.28, settle=0.01,
          bare={-1: [(0.6, 0.9, 0.8, 0.5)], 1: [(-1.3, 1.5, 0.6, 0.5)]}, seed=31)
    roof_chimney(0.95, 1.2, hgt=0.7)
    cores()
    W = frame("f", HW, HD)
    ops = [Arch(-0.55, 0.45, 1.25, 0.31), stone_window(W, 1.25, 0.3, 1.3, 0.34, "cross"), Rect(-0.24, 0.24, ZT + 0.85, ZT + 1.35)]
    arch_door(W, -0.55, 0.45, 1.25)
    wpanel(W, -0.18, 0.18, ZT + 0.9, ZT + 1.3, 0.0, 0.012, "M_Void")
    shutter(W, 1.25 - 0.3 - 0.16 - 0.32, 1.3 + 0.34 + 0.02, 0.32, 0.72, side=-1, rot=-0.06)
    stone_wall2("f", ops, gable=True)
    stone_wall2("b", [], gable=True)
    W = frame("l", HW, HD); stone_wall2("l", [stone_window(W, 0.6, 0.26, 1.3, 0.32, "bars")])
    W = frame("r", HW, HD); stone_wall2("r", [stone_window(W, -0.8, 0.28, 1.3, 0.32, "boarded")])
    roof()
    drifts2({"f": [(-1.15, 0.05)]}, hmul=1.15)
    footprints([(-0.55, -HD - 0.3), (-0.2, -HD - 2.0), (-0.8, -HD - 4.0)])
    rubble2((2, 2, 2, 2), {"f": [(-1.2, 0.1)]})
    return finish_house("Hut", props=[("Firewood", (HW + 0.04, 1.0, 0), 0.0), ("CrateA", (-1.7, -HD - 0.6, 0), 0.3)])

def house_gatehouse():
    setup("Gatehouse", 3.6, 2.4, [("stone", 3.9), ("timber", 3.0)], jet=(0.15, 0.4), pitch=1.05,
          lean=(0.04, -0.02), sag=0.36, settle=0.006, hole=(1.0, 1.5, 0.5, 0.42), hole_side=1,
          bare={-1: [(1.0, 1.2, 0.7, 0.5)], 1: [(1.6, 2.6, 0.5, 0.4)]}, seed=41)
    PW, PS = 1.45, 1.9
    cores(passage=(PW, PS))
    for side in "fb":
        W = frame(side, HW, HD)
        ops = [arch_frame(W, 0.0, PW, PS), stone_window(W, -2.6, 0.09, 2.5, 0.5, "slit"),
               stone_window(W, 2.6, 0.09, 2.5, 0.5, "slit")]
        stone_wall2(side, ops)
    portcullis(frame("f", HW, HD), PW, PS, 2.6, -0.3)
    for side in "lr":
        W = frame(side, HW, HD); stone_wall2(side, [stone_window(W, 0.0, 0.09, 2.4, 0.55, "slit")])
    joists2(1)
    Wf = timber_wall2("f", 1, wins=[(-2.1, "broken", None), (2.1, None, "boarded")])
    crumble(Wf, 0.0, LEVELS[1][1] + 1.6, 0.5, 0.45)
    Wb = timber_wall2("b", 1, wins=[(-1.9, None, None), (1.9, "open", None)])
    crumble(Wb, 0.3, LEVELS[1][1] + 0.8, 0.4, 0.3, laths=False)
    timber_wall2("l", 1, wins=[(0.0, None, "boarded")])
    timber_wall2("r", 1, wins=[(0.0, "askew", None)])
    gable("f", True); gable("b", False)
    seg((-2.45, -HD + 0.3, 4.15), (-2.45, -HD - 1.45, 4.15), 0.17, "M_Timber")
    seg((-2.45, -HD, 3.3), (-2.45, -HD - 0.85, 4.1), 0.12, "M_Timber")
    cage(-2.45, -HD - 1.2, 4.1, 2.95, 1.55)
    roof()
    drifts2({"f": [(-PW - 0.3, PW + 0.3)], "b": [(-PW - 0.3, PW + 0.3)]})
    rubble2((2, 3, 3, 2), {"f": [(-1.9, 1.9)], "b": [(-1.9, 1.9)]})
    return finish_house("Gatehouse", perches=[(-2.45, -HD - 1.2, 3.25)],
                        props=[("Barrel", (2.6, -HD - 0.7, 0), 0.0)])

def house_tall():
    setup("Tall", 2.2, 2.6, [("stone", 3.0), ("timber", 2.7), ("timber", 2.5)], jet=(0.2, 0.38), pitch=1.6,
          lean=(0.075, -0.035), sag=0.42, settle=0.008, hole=(0.9, 2.2, 0.42, 0.38), hole_side=1,
          bare={-1: [(-1.6, 1.4, 0.6, 0.5)], 1: [(-1.2, 3.0, 0.5, 0.45)]}, seed=51)
    roof_chimney(-1.0, -1.5, hgt=0.9)
    cores()
    W = frame("f", HW, HD)
    ops = [Arch(-0.75, 0.5, 1.65, 0.31), stone_window(W, 1.15, 0.32, 1.5, 0.4, "bars")]
    arch_door(W, -0.75, 0.5, 1.65)
    stone_wall2("f", ops)
    W = frame("l", HW, HD); stone_wall2("l", [stone_window(W, 0.8, 0.34, 1.5, 0.4, "boarded")])
    W = frame("r", HW, HD); stone_wall2("r", [stone_window(W, -0.4, 0.36, 1.5, 0.42, "cross")])
    shutter(W, -0.4 + 0.36 + 0.16 + 0.4, 1.5 + 0.42 + 0.02, 0.4, 0.88, side=1, rot=0.55)
    stone_wall2("b", [])
    joists2(1); joists2(2)
    Wf = timber_wall2("f", 1, wins=[(-1.15, "askew", None), (1.15, "open", None)])
    crumble(Wf, 0.0, LEVELS[1][1] + 1.7, 0.3, 0.35)
    Wb = timber_wall2("b", 1, wins=[(0.0, None, "boarded")])
    crumble(Wb, -1.6, LEVELS[1][1] + 0.9, 0.4, 0.4)
    Wl = timber_wall2("l", 1, wins=[(-1.2, "broken", None), (1.4, None, "boarded")])
    Wr = timber_wall2("r", 1, wins=[(0.0, None, None)])
    crumble(Wr, -2.0, LEVELS[1][1] + 1.6, 0.4, 0.5)
    Wf2 = timber_wall2("f", 2, wins=[(-1.1, None, "boarded"), (1.25, "broken", None)])
    crumble(Wf2, 0.1, LEVELS[2][1] + 0.7, 0.35, 0.3, laths=False)
    timber_wall2("b", 2, wins=[(-0.9, "open", None)])
    Wl2 = timber_wall2("l", 2, wins=[(0.3, None, None)])
    crumble(Wl2, 2.2, LEVELS[2][1] + 1.3, 0.45, 0.5)
    timber_wall2("r", 2, wins=[(-1.0, None, "boarded"), (1.3, "askew", None)])
    gable("f", True); gable("b", False)
    roof()
    drifts2({"f": [(-1.45, -0.05)]})
    footprints([(-0.75, -HD - 0.3), (-0.3, -HD - 2.0), (0.6, -HD - 4.0)])
    rubble2((3, 3, 3, 3), {"f": [(-1.5, 0.0)]})
    noose(1.55, -LEVELS[2][4] + 0.02, LEVELS[2][1] - 0.1, LEVELS[1][1] + 0.4)
    return finish_house("Tall", props=[("Barrel", (1.3, -HD - 0.6, 0), 0.0), ("CrateB", (-1.9, -HD - 0.5, 0), 0.5)])

def house_boarded():
    setup("Boarded", 2.5, 3.0, [("stone", 2.8), ("timber", 2.7)], jet=(0.12, 0.35), pitch=1.45,
          lean=(-0.05, -0.02), sag=0.52, settle=-0.01, hole=(-0.5, 2.3, 0.95, 0.8), hole_side=1,
          bare={-1: [(1.2, 1.6, 0.7, 0.5)], 1: []}, seed=61)
    roof_chimney(-0.95, 1.1, hgt=0.4)
    cores()
    W = frame("f", HW, HD)
    ops = [Arch(-0.7, 0.52, 1.6, 0.31), stone_window(W, 1.3, 0.34, 1.45, 0.4, "boarded")]
    arch_door(W, -0.7, 0.52, 1.6)
    plague_marks(W, -0.7, 0.52, 1.15, 0.05)
    stone_wall2("f", ops)
    W = frame("l", HW, HD)
    stone_wall2("l", [stone_window(W, -1.2, 0.34, 1.45, 0.4, "boarded"), stone_window(W, 1.3, 0.34, 1.45, 0.4, "boarded")])
    W = frame("r", HW, HD); stone_wall2("r", [stone_window(W, 0.0, 0.36, 1.45, 0.4, "boarded")])
    W = frame("b", HW, HD); stone_wall2("b", [stone_window(W, -0.8, 0.34, 1.45, 0.4, "boarded")])
    joists2(1)
    Wf = timber_wall2("f", 1, wins=[(-1.3, "broken", "boarded"), (1.3, None, "boarded")])
    painted_x(Wf, 0.0, LEVELS[1][1] + 1.45, 0.42, 0.004, "M_Void", w=0.11)
    crumble(Wf, 1.9, LEVELS[1][1] + 0.6, 0.3, 0.25, laths=False)
    Wb = timber_wall2("b", 1, wins=[(0.4, "askew", "boarded")])
    crumble(Wb, -1.7, LEVELS[1][1] + 1.2, 0.55, 0.6)
    Wl = timber_wall2("l", 1, wins=[(-1.0, None, "boarded"), (1.5, None, "boarded")])
    crumble(Wl, 0.25, LEVELS[1][1] + 1.0, 0.45, 0.55)
    Wr = timber_wall2("r", 1, wins=[(0.0, "broken", "boarded")])
    crumble(Wr, 2.0, LEVELS[1][1] + 1.5, 0.5, 0.5); crumble(Wr, -2.0, LEVELS[1][1] + 0.7, 0.4, 0.3, laths=False)
    gable("f", "boarded"); gable("b", False)
    crumble(wall_frame("f", HWT, HDT), 1.1, ZT + 0.5, 0.3, 0.25)
    roof()
    drifts2({}, hmul=1.25)                              # nobody has dug the door out
    rubble2((5, 4, 4, 4))
    return finish_house("Boarded", props=[("CrateA", (1.9, -HD - 0.9, 0), 0.6)])

def house_chapel():
    setup("Chapel", 3.0, 5.0, [("stone", 5.0)], pitch=1.6, lean=(0.0, 0.0), sag=0.24, settle=0.004,
          hole=(2.6, 3.2, 0.7, 0.55), hole_side=-1, bare={-1: [(-2.5, 1.0, 0.8, 0.6)], 1: [(0.5, 2.0, 0.9, 0.6)]}, seed=71)
    TH, CY = 1.25, -HD - 1.12
    CHIMS.append((-TH - 0.3, TH + 1.1, CY - TH - 0.2, CY + TH + 0.15))
    cores()
    for side in "lr":
        W = frame(side, HW, HD)
        stone_wall2(side, [lancet_window(W, u, 0.4, 2.0, 3.55, "boarded" if (side, u) == ("r", 3.1) else "mullion")
                           for u in (-3.1, 0.0, 3.1)])
        for u in (-4.45, -1.55, 1.55, 4.45):
            buttress(W, u, 4.2)
    W = frame("b", HW, HD); stone_wall2("b", [lancet_window(W, 0.0, 0.55, 2.6, 4.7)], gable=True)
    stone_wall2("f", [], gable=True)
    bell_tower(CY, TH)
    roof()
    drifts2({"f": [(-TH - 0.3, TH + 0.3)]})
    rubble2((0, 4, 4, 3))
    zc = 10.08 + 3.1 + 0.85 + 0.4 * math.sin(math.radians(17))
    return finish_house("Chapel", perches=[(0.02 + 0.38 + math.tan(math.radians(7.0)) * zc, CY, zc + 0.03)],
                        ridge_ys=(-0.2, 0.55))

def house_barn():
    setup("Barn", 3.6, 4.6, [("boards", 3.4)], pitch=1.15, lean=(0.05, 0.0), sag=0.48, settle=0.012,
          hole=(1.5, 2.4, 0.8, 0.62), hole_side=-1,
          bare={-1: [(-2.8, 1.2, 0.7, 0.5)], 1: [(0.0, 1.6, 1.0, 0.6), (3.2, 2.8, 0.5, 0.4)]}, seed=81)
    cores()
    DOOR, HAY = (-1.4, 1.4, 0.0, 2.9), (-0.55, 0.55, 4.0, 5.0)
    for side in "fblr":
        stone_wall2(side, [Rect(-1.55, 1.55, 0.0, 3.0)] if side == "f" else [], z1=0.45, miss=0.03)
    W = board_wall("f", 0.45, ZT, holes=[DOOR, HAY], gable=True)
    board_wall("b", 0.45, ZT, gable=True, miss=0.07)
    Wl = board_wall("l", 0.45, ZT, holes=[(-0.4, 0.4, 1.4, 2.1)], miss=0.06)
    wpanel(Wl, -0.4, 0.4, 1.4, 2.1, 0.0, 0.01, "M_Void")
    Wr = board_wall("r", 0.45, ZT, holes=[(0.9, 2.0, 0.45, 2.3)], miss=0.04)
    door_leaves(Wr, 1.45, 0.55, 0.45, 1.85, "closed")
    for sx in (-1, 1):
        for sy in (-1, 1):
            x, y = sx * (HW + 0.04), sy * (HD + 0.04)
            box(x - 0.12, x + 0.12, y - 0.12, y + 0.12, 0.4, ZT + 0.05, "M_Timber")
    wpanel(W, -1.4, 1.4, 0.0, 2.9, -0.05, 0.005, "M_Void")
    for s in (-1, 1):
        wpanel(W, s * 1.47 - 0.1, s * 1.47 + 0.1, 0.0, 3.0, 0.0, 0.14, "M_Timber", jit=0.01)
    wpanel(W, -1.65, 1.65, 2.9, 3.12, 0.0, 0.15, "M_Timber", jit=0.01)
    snow_ledge(W, -1.65, 1.65, 3.12, 0.0, 0.15, 0.06)
    leaf(W, -1.4, 0.0, 1.4, 2.85, 1.95, 1)
    leaf(W, 1.4, 0.0, 1.4, 2.85, 0.32, -1, tilt=-0.1)
    wpanel(W, -0.55, 0.55, 4.0, 5.0, -0.05, 0.005, "M_Void")
    for s in (-1, 1):
        wpanel(W, s * 0.6 - 0.07, s * 0.6 + 0.07, 3.95, 5.05, 0.0, 0.12, "M_Timber")
    wpanel(W, -0.7, 0.7, 3.88, 4.0, 0.0, 0.13, "M_Timber"); snow_ledge(W, -0.7, 0.7, 4.0, 0.0, 0.13, 0.05)
    leaf(W, -0.55, 4.0, 1.1, 1.0, 1.3, 1)
    skull(W, 0.0, 3.4, 0.16)
    seg((0.0, -HD + 0.6, ZR - 0.55), (0.0, -HD - 1.25, ZR - 0.55), 0.18, "M_Timber")                # hoist beam
    seg((0.0, -HD - 1.05, ZR - 0.65), (0.03, -HD - 1.05, 3.4), 0.03, "M_Wood")
    for a, b in (((0.03, -HD - 1.05, 3.4), (0.03, -HD - 1.05, 3.15)), ((0.03, -HD - 1.05, 3.15), (0.03, -HD - 1.22, 3.08)),
                 ((0.03, -HD - 1.22, 3.08), (0.03, -HD - 1.3, 3.2))):
        seg(a, b, 0.035, "M_Iron")
    roof()
    drifts2({"f": [(-1.6, 1.6)], "r": [(0.8, 2.1)]})
    footprints([(0.3, -HD - 0.3), (0.7, -HD - 2.0), (0.2, -HD - 4.0)])
    rubble2((2, 3, 3, 3), {"f": [(-1.8, 1.8)]})
    return finish_house("Barn", perches=[(0.0, -HD - 1.15, ZR - 0.46)],
                        props=[("CrateA", (2.6, -HD - 0.7, 0), 0.1), ("CrateB", (2.55, -HD - 0.65, 0.6), 0.5),
                               ("Barrel", (-2.7, -HD - 0.6, 0), 0.0), ("Barrel", (-3.3, -HD - 0.9, 0), 0.0)])

# =====================================================================
# environment pieces (world space or prop space)
# =====================================================================
def road_y(x):
    return 1.4 * math.sin(x / 17.0)

def road_hw(x):
    return 1.9 if x > -40.0 else 1.35

def build_road(x0=-86.0, x1=33.0):
    """Trampled snow ribbon with ruts (no ink), returns cobble positions for the inked stones."""
    def ribbon(off0, off1, z, mat, jit, step=0.7):
        vs, fs = acc(mat); b0 = len(vs)
        xs = np.arange(x0, x1 + 0.01, step)
        for x in xs:
            y, k = road_y(x), road_hw(x) / 1.9
            vs.append(V((x, y + off0 * k + R(-jit, jit), z))); vs.append(V((x, y + off1 * k + R(-jit, jit), z)))
        for i in range(len(xs) - 1):
            fs.append((b0 + 2 * i, b0 + 2 * i + 2, b0 + 2 * i + 3, b0 + 2 * i + 1))
    ribbon(-1.9, 1.9, 0.004, "M_SnowShade", 0.18)
    for o in (-0.68, 0.62):
        ribbon(o - 0.12, o + 0.12, 0.008, "M_Grime", 0.04, step=0.5)

def build_cobbles(x0=-86.0, x1=32.0):
    x = x0
    while x < x1:
        if rng.random() < 0.55:                        # broken stretches: patches of old paving
            for _ in range(rng.randint(5, 14)):
                cx = x + R(0.0, 2.5); cy = road_y(cx) + R(-1.5, 1.5) * road_hw(cx) / 1.9
                t = R(0, math.pi); a = V((math.cos(t), math.sin(t), 0)); b = Z.cross(a); s = R(0.13, 0.24)
                fbox(V((cx, cy, 0)), a, b, Z, -s, s, -s * 0.7, s * 0.7, 0.0, R(0.03, 0.06), "M_Stone", skip=("bottom",), jit=0.02)
        x += R(2.0, 4.0)

def fence(pts, seed=0, step=1.8, h=1.15, miss=0.15):
    fr = random.Random(seed)
    posts = []
    for (xa, ya), (xb, yb) in zip(pts, pts[1:]):
        n = max(1, round(math.hypot(xb - xa, yb - ya) / step))
        posts += [V((xa + (xb - xa) * i / n, ya + (yb - ya) * i / n, 0)) for i in range(n)]
    posts.append(V((pts[-1][0], pts[-1][1], 0)))
    tops = []
    for p in posts:
        if fr.random() < 0.08:
            tops.append(None); continue
        top = p + V((fr.uniform(-0.13, 0.13), fr.uniform(-0.13, 0.13), h * fr.uniform(0.85, 1.08)))
        seg(p - V((0, 0, 0.2)), top, 0.12, "M_Timber")
        seg(top - V((0, 0, 0.01)), top + V((0, 0, 0.07)), 0.13, "M_Snow")
        tops.append(top)
    for k in range(len(posts) - 1):
        a, b = tops[k], tops[k + 1]
        if a is None or b is None:
            continue
        for frac in (0.42, 0.82):
            pa, pb = posts[k].lerp(a, frac), posts[k + 1].lerp(b, frac)
            r = fr.random()
            if r < miss:
                continue
            if r < miss + 0.1:                          # snapped rail hanging from one post
                pb = pa + (pb - pa) * 0.6 - V((0, 0, frac * h * 0.8))
                seg(pa, pb, 0.07, "M_Wood", h=0.11)
                continue
            seg(pa, pb, 0.07, "M_Wood", h=0.11)
            seg(pa.lerp(pb, 0.06) + V((0, 0, 0.075)), pa.lerp(pb, 0.94) + V((0, 0, 0.075)), 0.065, "M_Snow", h=0.045)
    return [t + V((0, 0, 0.07)) for t in tops if t is not None]

def palisade(x, y0, y1, seed):
    """Sharpened stakes either side of the gate, broken and leaning."""
    fr = random.Random(seed)
    y = y0; d = 1 if y1 > y0 else -1
    while (y1 - y) * d > 0:
        r = fr.uniform(0.11, 0.15)
        if fr.random() < 0.1:
            y += d * fr.uniform(0.5, 1.4); continue
        h = fr.uniform(2.2, 3.2) * (0.45 if fr.random() < 0.1 else 1.0)
        lx, ly = fr.uniform(-0.15, 0.15), fr.uniform(-0.08, 0.08)
        cx = x + fr.uniform(-0.08, 0.08)
        rings = [ring(r, -0.3, 6, cx, y), ring(r * 0.95, h, 6, cx + lx, y + ly), ring(0.015, h + 0.45, 6, cx + lx * 1.15, y + ly * 1.15)]
        loft(rings, "M_Timber")
        y += d * (2 * r + 0.03)
    return None

def build_tree(seed, height=7.0, claw=1.0):
    tr = random.Random(seed)
    def basis(d, prev):
        a = prev - d * prev.dot(d)
        if a.length < 1e-4:
            a = d.orthogonal()
        a.normalize()
        return a, d.cross(a)
    def limb(p, d, L, r0, depth):
        n = max(3, int(L / 0.45))
        cur, dd = V(p), d.normalized()
        a, _ = basis(dd, dd.orthogonal())
        rings, kids = [], []
        for i in range(n + 1):
            t = i / n
            r = r0 * (1 - 0.82 * t) + 0.008
            a, b = basis(dd, a)
            rings.append([cur + (a * math.cos(2 * math.pi * k / 5) + b * math.sin(2 * math.pi * k / 5)) * r for k in range(5)])
            if i == n:
                break
            wob = 0.3 if depth else 0.12
            dd = (dd + V((tr.uniform(-wob, wob), tr.uniform(-wob, wob), tr.uniform(-wob * 0.4, wob * 0.5)))).normalized()
            if depth >= 2 and t > 0.4:
                dd = (dd + V((0, 0, -0.2 * claw))).normalized()
            cur = cur + dd * (L / n)
            if depth < 3 and i >= (1 if depth else 3) and tr.random() < (0.5 if depth == 0 else 0.42):
                kids.append((V(cur), V(dd), r))
        loft(rings, "M_Timber", cap0=True, cap1=True)
        for q, kd, r in kids:
            side = V((tr.uniform(-1, 1), tr.uniform(-1, 1), tr.uniform(0.1, 0.7) if depth == 0 else tr.uniform(-0.3, 0.6)))
            limb(q, (kd * 0.4 + side.normalized()).normalized(), L * tr.uniform(0.42, 0.62), r * 0.85, depth + 1)
    limb(V((0, 0, -0.3)), V((tr.uniform(-0.12, 0.12), tr.uniform(-0.12, 0.12), 1)), height, 0.3, 0)
    for k in range(3):
        a = 2 * math.pi * k / 3 + tr.uniform(-0.4, 0.4)
        limb(V((0, 0, 0.35)), V((math.cos(a), math.sin(a), -0.45)), 1.3, 0.16, 2)

def build_well():
    Rin, Rout = 0.72, 1.0
    for c in range(3):
        z0, off, n = c * 0.3, (c % 2) * 0.5, 9
        for k in range(n):
            a0 = 2 * math.pi * (k + off) / n + 0.02; a1 = 2 * math.pi * (k + 1 + off) / n - 0.02
            ro = Rout + R(-0.02, 0.03)
            arc = [a0 + (a1 - a0) * q / 3 for q in range(4)]
            pts = [(Rin * math.cos(a), Rin * math.sin(a)) for a in arc] + [(ro * math.cos(a), ro * math.sin(a)) for a in reversed(arc)]
            prism(V((0, 0, 0)), X, Y, Z, pts, z0 + 0.015, z0 + 0.3 - 0.015, "M_Stone")
    for k in range(8):
        a0 = 2 * math.pi * k / 8 + 0.02; a1 = 2 * math.pi * (k + 1) / 8 - 0.02
        arc = [a0 + (a1 - a0) * q / 3 for q in range(4)]
        pts = [(0.68 * math.cos(a), 0.68 * math.sin(a)) for a in arc] + [(1.06 * math.cos(a), 1.06 * math.sin(a)) for a in reversed(arc)]
        prism(V((0, 0, 0)), X, Y, Z, pts, 0.9, 1.0, "M_Stone")
        if rng.random() < 0.8:
            pts = [(0.72 * math.cos(a), 0.72 * math.sin(a)) for a in arc] + [(1.02 * math.cos(a), 1.02 * math.sin(a)) for a in reversed(arc)]
            prism(V((0, 0, 0)), X, Y, Z, pts, 1.0, 1.0 + R(0.05, 0.1), "M_Snow")
    prism(V((0, 0, 0)), X, Y, Z, [(0.7 * math.cos(2 * math.pi * k / 12), 0.7 * math.sin(2 * math.pi * k / 12)) for k in range(12)],
          0.55, 0.57, "M_Void")
    for s in (-1, 1):
        seg((s * 1.12, 0, -0.2), (s * 1.1, 0.02, 2.72), 0.17, "M_Timber")
    circ = [(0.1 * math.cos(2 * math.pi * k / 8), 0.1 * math.sin(2 * math.pi * k / 8)) for k in range(8)]
    prism(V((-1.05, 0, 1.8)), Y, Z, X, circ, 0.0, 2.1, "M_Wood")
    prism(V((-0.3, 0, 1.8)), Y, Z, X, [(x * 1.4, y * 1.4) for x, y in circ], 0.0, 0.55, "M_Wood")
    seg((1.15, 0, 1.8), (1.4, 0, 1.8), 0.05, "M_Iron"); seg((1.4, 0, 1.8), (1.4, 0, 1.45), 0.05, "M_Iron")
    seg((0.0, 0.13, 1.8), (0.02, 0.13, 1.08), 0.025, "M_Wood")
    loft([ring(0.17, 0.72, 10, 0.02, 0.13), ring(0.2, 1.02, 10, 0.02, 0.13)], "M_Wood")
    loft([ring(0.205, 0.95, 10, 0.02, 0.13), ring(0.21, 0.99, 10, 0.02, 0.13)], "M_Iron", cap0=False, cap1=False)
    seg((-0.18, 0.13, 1.0), (0.02, 0.13, 1.1), 0.02, "M_Iron"); seg((0.02, 0.13, 1.1), (0.22, 0.13, 1.0), 0.02, "M_Iron")
    seg((-1.45, 0, 2.78), (1.45, 0, 2.78), 0.14, "M_Timber")
    for s in (-1, 1):
        prism(V((-1.45, 0, 0)), Y, Z, X, [(0, 2.84), (s * 0.95, 2.24), (s * 0.95, 2.3), (0, 2.91)], 0.0, 2.9, "M_Wood")
        prism(V((-1.4, 0, 0)), Y, Z, X, [(s * 0.03, 2.92), (s * 0.9, 2.36), (s * 0.9, 2.43), (s * 0.03, 3.0)], 0.0, 2.75, "M_Snow")

def build_scarecrow():
    seg((0, 0, -0.3), (0.06, 0, 2.35), 0.13, "M_Timber")
    seg((-0.95, 0, 1.72), (0.95, 0, 1.82), 0.1, "M_Timber")
    fbox(V((0.08, 0, 2.38)), X, Y, Z, -0.17, 0.17, -0.16, 0.16, 0.0, 0.38, "M_Plaster", jit=0.04)
    for s in (-1, 1):
        box(0.08 + s * 0.075 - 0.04, 0.08 + s * 0.075 + 0.04, -0.175, -0.165, 2.6, 2.66, "M_Void")
    for k in range(5):
        box(0.0 + k * 0.04, 0.012 + k * 0.04, -0.18, -0.165, 2.45, 2.5, "M_Iron")
    prism(V((0, 0, 0)), X, Y, Z, [(0.08 + 0.34 * math.cos(2 * math.pi * k / 10), 0.33 * math.sin(2 * math.pi * k / 10)) for k in range(10)],
          2.72, 2.76, "M_Wood")
    loft([ring(0.19, 2.76, 8, 0.1, 0.0), ring(0.15, 2.95, 8, 0.14, 0.02), ring(0.07, 3.02, 8, 0.2, 0.05)], "M_Wood")
    coat = [(-0.32, 1.86), (0.34, 1.86), (0.38, 1.3), (0.3, 0.92), (0.22, 1.05), (0.14, 0.8), (0.03, 1.0),
            (-0.08, 0.78), (-0.17, 1.02), (-0.27, 0.86), (-0.36, 1.22)]
    prism(V((0, 0, 0)), X, Z, Y, coat, -0.15, 0.15, "M_Wood")
    for s in (-1, 1):
        rag = [(0.38, 1.84), (0.9, 1.88), (0.86, 1.5), (0.76, 1.63), (0.64, 1.3), (0.5, 1.55), (0.4, 1.4)]
        prism(V((0, 0, 0)), X, Z, Y, [(s * x, z) for x, z in rag], -0.07, 0.07, "M_Wood")
    for x in (-0.6, 0.62):
        seg((x, -0.09, 1.7), (x, 0.09, 1.86), 0.03, "M_Timber")
    prism(V((0, 0, 0)), X, Y, Z, [(0.08 + 0.3 * math.cos(2 * math.pi * k / 10), 0.29 * math.sin(2 * math.pi * k / 10)) for k in range(10)],
          2.76, 2.8, "M_Snow")
    box(-0.3, 0.3, -0.12, 0.12, 1.86, 1.91, "M_Snow")

def build_graveyard(cx, cy, cols=5, rows=3):
    gr = random.Random(7)
    pts = []
    for i in range(cols):
        for j in range(rows):
            if gr.random() < 0.12:
                continue
            x, y = cx + i * 2.1 + gr.uniform(-0.35, 0.35), cy + j * 2.5 + gr.uniform(-0.3, 0.3)
            yaw = gr.uniform(-0.15, 0.15)
            with xform(T(x, y, 0, yaw)):
                if (i, j) == (2, 0):                       # fresh open grave, earth heap and a shovel
                    box(-0.45, 0.45, 0.2, 2.0, -0.01, 0.006, "M_Void", skip=("bottom",))
                    loft([[V((0.7 + 0.5 * math.cos(t), 1.1 + 0.95 * math.sin(t), z)) for t in [2 * math.pi * k / 10 for k in range(10)]]
                          for z in (0.0,)] + [[V((0.75 + 0.3 * math.cos(t), 1.1 + 0.6 * math.sin(t), 0.35)) for t in
                                               [2 * math.pi * k / 10 for k in range(10)]]], "M_Grime")
                    seg((0.8, 1.2, 0.2), (1.05, 1.05, 1.35), 0.04, "M_Wood")
                    box(0.62, 0.95, 1.18, 1.22, -0.1, 0.25, "M_Iron")
                else:
                    loft([[V((0.45 * math.cos(t), 1.0 + 0.95 * math.sin(t), 0.0)) for t in [2 * math.pi * k / 10 for k in range(10)]],
                          [V((0.35 * math.cos(t), 1.0 + 0.8 * math.sin(t), 0.18)) for t in [2 * math.pi * k / 10 for k in range(10)]],
                          [V((0.18 * math.cos(t), 1.0 + 0.55 * math.sin(t), 0.27)) for t in [2 * math.pi * k / 10 for k in range(10)]]],
                         "M_Snow")
                with xform(Matrix.Rotation(gr.uniform(-0.25, 0.25), 4, "Y") @ Matrix.Rotation(gr.uniform(-0.2, 0.12), 4, "X")):
                    if gr.random() < 0.55:
                        seg((0, 0, -0.3), (0, 0, 1.25), 0.1, "M_Wood")
                        seg((-0.36, 0, 0.92), (0.36, 0, 0.92), 0.09, "M_Wood")
                        seg((-0.33, 0, 0.98), (0.33, 0, 0.98), 0.07, "M_Snow", h=0.04)
                        pts.append(V((x, y, 1.3)))
                    else:
                        top = gr.uniform(0.65, 0.95)
                        prof = [(-0.3, -0.2), (0.3, -0.2), (0.3, top)] + [(0.3 * math.cos(math.pi * k / 6), top + 0.3 * math.sin(math.pi * k / 6))
                                                                       for k in range(1, 6)] + [(-0.3, top)]
                        if gr.random() < 0.3:                  # broken headstone
                            prof = [(-0.3, -0.2), (0.3, -0.2), (0.3, top * 0.6), (0.05, top * 0.45), (-0.1, top * 0.7), (-0.3, top * 0.55)]
                        prism(V((0, 0, 0)), X, Z, Y, prof, -0.08, 0.08, "M_Stone")
    return pts

def build_grass(zones, n, blocked):
    vs, fs = acc("M_Wood")
    k = tries = 0
    while k < n and tries < n * 30:
        tries += 1
        x0, x1, y0, y1 = rng.choice(zones)
        x, y = R(x0, x1), R(y0, y1)
        if abs(y - road_y(x)) < road_hw(x) + 0.3 or any(a < x < b and c < y < d for a, b, c, d in blocked):
            continue
        for _ in range(rng.randint(4, 8)):
            a = R(0, 2 * math.pi); h = R(0.15, 0.42); lean = R(0.15, 0.45)
            bx, by = x + 0.07 * math.cos(a), y + 0.07 * math.sin(a)
            dx, dy = math.cos(a), math.sin(a); px, py = -dy * 0.02, dx * 0.02
            b0 = len(vs)
            vs.extend([V((bx - px, by - py, 0)), V((bx + px, by + py, 0)), V((bx + dx * lean * h, by + dy * lean * h, h))])
            fs.append((b0, b0 + 1, b0 + 2))
        k += 1

# =====================================================================
# scene
# =====================================================================
mats = make_materials()
C_HOUSES, C_PROPS, C_NATURE, C_LIGHTS, C_GROUND, C_CAMS, C_KIT = (collection(n) for n in
    ("COL_Houses", "COL_Props", "COL_Nature", "COL_Lights", "COL_Ground", "COL_Cameras", "COL_Kit"))

BUILDERS = [house_apothecary, house_smithy, house_hut, house_gatehouse, house_tall, house_boarded,
            house_chapel, house_barn]
HOUSES = {}
for fn in BUILDERS:
    rec = fn()
    HOUSES[rec["name"]] = rec

# props (prototypes at the origin, moved to the kit row later)
rng = random.Random(5)
PROTO = {}
def proto(name, coll, builder, *a):
    GEO.clear(); SNOW.clear()
    builder(*a)
    PROTO[name] = emit(f"SM_{name}", coll, mats)
proto("Barrel", C_PROPS, build_barrel)
proto("CrateA", C_PROPS, build_crate)
proto("CrateB", C_PROPS, build_crate, 0.22)
proto("Firewood", C_PROPS, build_firewood)
proto("Stump", C_PROPS, build_stump)
proto("Crow", C_PROPS, build_crow)
proto("Well", C_PROPS, build_well)
proto("Scarecrow", C_PROPS, build_scarecrow)
for i, (sd, h) in enumerate(((3, 7.5), (11, 6.0), (23, 8.5))):
    proto(f"Tree_{'ABC'[i]}", C_NATURE, build_tree, sd, h)

COUNT = {}
def place(src, M, coll=None, base=None):
    base = base or src.name
    COUNT[base] = COUNT.get(base, 0) + 1
    ob = bpy.data.objects.new(f"{base}_{COUNT[base]:02d}", src.data)
    (coll or src.users_collection[0]).objects.link(ob)
    ob.matrix_world = M
    return ob

# ---------- layout ----------
PLACES = [("Gatehouse", -46.0, 0), ("Apothecary", -29.0, 1), ("Tall", -24.0, -1), ("Smithy", -11.0, 1),
          ("Boarded", -5.0, -1), ("Hut", 8.0, 1), ("Barn", 15.0, -1), ("Chapel", 40.0, 0)]
lr = random.Random(9)
BLOCK, PERCH = [], []
for name, x, side in PLACES:
    rec = HOUSES[name]
    if side == 0:
        yaw = math.pi / 2 if x < 0 else -math.pi / 2
        loc = (x, road_y(x), 0.0)
    else:
        yaw = (0.0 if side > 0 else math.pi) + lr.uniform(-0.05, 0.05)
        loc = (x, road_y(x) + side * (3.6 + rec["front"]), 0.0)
    M = T(*loc, yaw=yaw)
    rec["M"] = M
    for o in (rec["ob"], rec["snow"]):
        o.matrix_world = M
    bb = [M @ V(c) for c in rec["ob"].bound_box]
    rec["bb"] = (min(v.x for v in bb), max(v.x for v in bb), min(v.y for v in bb), max(v.y for v in bb))
    BLOCK.append((rec["bb"][0] - 1.0, rec["bb"][1] + 1.0, rec["bb"][2] - 1.0, rec["bb"][3] + 1.0))
    PERCH += [(M @ p, yaw + lr.uniform(-2, 2)) for p in rec["perches"]]
    for pname, (px, py, pz), rz in rec["props"]:
        place(PROTO[pname], M @ T(px, py, pz, rz), C_PROPS, f"SM_{pname}")
    print(f"[place] {name}: loc {tuple(round(c, 1) for c in loc)} bbox {tuple(round(c, 1) for c in rec['bb'])}")

# far houses swallowed by the fog
for k, (name, x, y, yaw) in enumerate((("Hut", -12.0, 31.0, 0.35), ("Boarded", 21.0, 34.0, 2.9),
                                       ("Barn", -60.0, 21.0, 1.75), ("Tall", 5.0, -36.0, 3.0),
                                       ("Hut", 30.0, -38.0, 2.6), ("Smithy", -36.0, 30.0, -0.3))):
    rec = HOUSES[name]
    for o in (rec["ob"], rec["snow"]):
        place(o, T(x, y, 0, yaw), C_HOUSES, o.name + "_Far")
    BLOCK.append((x - 6, x + 6, y - 6, y + 6))

# ---------- environment meshes ----------
rng = random.Random(13)
GEO.clear()
box(-1700, 500, -600, 600, -0.2, 0.0, "M_Ground", skip=("bottom",))
emit("SM_Ground", C_GROUND, mats)
build_road()
emit("SM_Road", C_GROUND, mats)
build_cobbles()
emit("SM_RoadStones", C_NATURE, mats)

def runs(side, x0, x1):
    """Fence polylines along the road, broken where houses stand and at random gaps."""
    out, cur, x = [], [], x0
    while x <= x1:
        y = road_y(x) + side * 2.7
        blocked = any(a - 0.5 < x < b + 0.5 and c - 0.5 < y < d + 0.5 for a, b, c, d in BLOCK[:8]) or rng.random() < 0.06
        if blocked:
            if len(cur) > 1:
                out.append(cur)
            cur = []
        else:
            cur.append((x, y))
        x += 1.8
    if len(cur) > 1:
        out.append(cur)
    return out
fence_tops = []
for side in (1, -1):
    for k, pl in enumerate(runs(side, -40.0, 30.0)):
        fence_tops += fence(pl, seed=100 + 10 * k + side)
fence_tops += fence([(22.5, 6.0), (22.5, 15.8), (34.5, 15.8), (34.5, 5.5)], seed=300)
fence_tops += fence([(20.5, -9.0), (36.0, -9.5), (36.5, -24.0)], seed=301, miss=0.3)
fence_tops += fence([(-20.0, 9.5), (-20.0, 20.0), (-6.0, 21.0)], seed=302, miss=0.25)
for s in (-1, 1):
    palisade(-46.6, road_y(-46) + s * 4.1, road_y(-46) + s * 27.0, 400 + s)
emit("SM_Fences", C_PROPS, mats)

grave_tops = build_graveyard(23.6, 7.0)
emit("SM_Graveyard", C_PROPS, mats)

build_grass([(-44, 32, -24, 24), (-44, 32, -6, 6)], 220, BLOCK[:8] + [(22.5, 34.5, 6, 16)])
emit("SM_GrassTufts", C_NATURE, mats)

# well, scarecrow, trees
place(PROTO["Well"], T(3.5, road_y(3.5) - 6.2, 0, 0.3), C_PROPS, "SM_Well")
sc = place(PROTO["Scarecrow"], T(28.0, -16.0, 0, 0.5, rx=0.06, ry=-0.08), C_PROPS, "SM_Scarecrow")
PERCH.append((sc.matrix_world @ V((0.75, 0.0, 1.88)), 0.4))
place(PROTO["Barrel"], T(4.9, road_y(3.5) - 7.0, 0, 0.0), C_PROPS, "SM_Barrel")
place(PROTO["Stump"], T(-17.5, -6.5, 0, 0.0), C_PROPS, "SM_Stump")
trees, tries = [], 0
tr_rng = random.Random(17)
def tree_ok(x, y, rad):
    if abs(y - road_y(x)) < 4.5 or any(a - 2 < x < b + 2 and c - 2 < y < d + 2 for a, b, c, d in BLOCK):
        return False
    if 21 < x < 37 and 4 < y < 17 or 19 < x < 38 and -25 < y < -8 or -48 < x < -45 or 31 < x < 47 and -6 < y < 6:
        return False
    return all(math.hypot(x - tx, y - ty) > rad for tx, ty in trees)
for zone, n, rad in (((-62, 52, -30, 30), 15, 8.0), ((-110, 100, -95, 95), 70, 9.0)):
    k = 0
    while k < n and tries < 6000:
        tries += 1
        x, y = tr_rng.uniform(zone[0], zone[1]), tr_rng.uniform(zone[2], zone[3])
        if n == 70 and abs(x) < 64 and abs(y) < 32:
            continue
        if not tree_ok(x, y, rad):
            continue
        trees.append((x, y)); k += 1
        src = PROTO[f"Tree_{tr_rng.choice('ABC')}"]
        s = tr_rng.uniform(0.85, 1.3)
        place(src, T(x, y, 0, tr_rng.uniform(0, 6.3)) @ Matrix.Scale(s, 4), C_NATURE, "SM_Tree")

# crows
cr = random.Random(21)
spots = PERCH + [(p, cr.uniform(0, 6.3)) for p in cr.sample(fence_tops, 7)] + [(p, cr.uniform(0, 6.3)) for p in grave_tops[:3]]
for p, rz in spots:
    place(PROTO["Crow"], T(p.x, p.y, p.z, rz), C_PROPS, "SM_Crow")

# ---------- kit lineup (comparison sheet) and prop row ----------
ORDER = ["Apothecary", "Smithy", "Hut", "Gatehouse", "Tall", "Boarded", "Chapel", "Barn"]
KITY = 0.0
for i, name in enumerate(ORDER):
    rec = HOUSES[name]
    rec["kit_x"] = -1250.0 + 60.0 * i
    for o in (rec["ob"], rec["snow"]):
        place(o, T(rec["kit_x"], KITY, 0), C_KIT, o.name + "_Kit")
for i, (name, ob) in enumerate(PROTO.items()):
    ob.location = (-1250.0 + 8.0 * i, -90.0, 0.0)
    for c in list(ob.users_collection):
        c.objects.unlink(ob)
    C_KIT.objects.link(ob)

# =====================================================================
# light, render settings, Freestyle
# =====================================================================
sun = bpy.data.lights.new("LGT_Sun", "SUN")
sun.energy = math.pi * 0.95
sun.angle = math.radians(0.3)
sun_ob = bpy.data.objects.new("LGT_Sun", sun); C_LIGHTS.objects.link(sun_ob)
sun_ob.rotation_euler = (-TO_LIGHT).to_track_quat("-Z", "Y").to_euler()
world = bpy.data.worlds.new("WRL_Paper"); SC.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0.0, 0.0, 0.0, 1)

SC.render.engine = "BLENDER_EEVEE_NEXT"
SC.eevee.taa_render_samples = 16
SC.render.filter_size = 0.8
SC.render.film_transparent = True
SC.render.resolution_x, SC.render.resolution_y = RX, RY
SC.view_settings.view_transform = "Standard"
SC.view_settings.look = "None"
SC.render.image_settings.file_format = "PNG"
SC.render.image_settings.color_mode = "RGBA"
SC.render.use_freestyle = True
SC.render.line_thickness_mode = "ABSOLUTE"
vl = SC.view_layers[0]
fs = vl.freestyle_settings
fs.crease_angle = math.radians(130)
fs.use_culling = True
for ls in list(fs.linesets):
    fs.linesets.remove(ls)
def ink_style(name, thick, wobble):
    st = bpy.data.linestyles.new(name)
    st.color = (0, 0, 0); st.thickness = thick; st.thickness_position = "CENTER"; st.caps = "ROUND"
    sm = st.geometry_modifiers.new("Sampling", "SAMPLING"); sm.sampling = 2.0
    pn = st.geometry_modifiers.new("Wobble", "PERLIN_NOISE_1D"); pn.frequency = 12.0; pn.amplitude = wobble; pn.octaves = 2
    tn = st.thickness_modifiers.new("Pressure", "NOISE"); tn.amplitude = thick * 0.35; tn.period = 35.0; tn.blend = "SUBTRACT"
    return st
ls = fs.linesets.new("LS_Contour")
ls.select_by_visibility = True; ls.visibility = "VISIBLE"; ls.select_by_edge_types = True
ls.select_silhouette = ls.select_border = ls.select_external_contour = True; ls.select_crease = False
ls.linestyle = ink_style("INK_Contour", 4.0, 1.2)
ls2 = fs.linesets.new("LS_Inner")
ls2.select_by_visibility = True; ls2.visibility = "VISIBLE"; ls2.select_by_edge_types = True
ls2.select_silhouette = ls2.select_border = ls2.select_external_contour = False; ls2.select_crease = True
ls2.linestyle = ink_style("INK_Inner", 1.8, 0.8)
for l in (ls, ls2):
    l.select_by_collection = True; l.collection = C_GROUND; l.collection_negation = "EXCLUSIVE"

# depth + position passes -> EXR (used for the depth / ground fog in post)
vl.use_pass_z = True
try:
    vl.use_pass_position = True
except Exception:
    pass
SC.use_nodes = True
nt = SC.node_tree
rl = next(n for n in nt.nodes if n.type == "R_LAYERS")
fo = nt.nodes.new("CompositorNodeOutputFile")
fo.base_path = MDIR
fo.format.file_format = "OPEN_EXR"; fo.format.color_depth = "32"
fo.file_slots[0].path = "depth_"
nt.links.new(rl.outputs["Depth"], fo.inputs[0])
HAS_POS = "Position" in rl.outputs
if HAS_POS:
    fo.file_slots.new("pos_")
    nt.links.new(rl.outputs["Position"], fo.inputs[1])
print("[passes] position pass:", HAS_POS)

def camera(name, pos, tgt, lens):
    cd = bpy.data.cameras.new(name); cd.lens = lens; cd.clip_start = 0.1; cd.clip_end = 2000
    co = bpy.data.objects.new(name, cd); C_CAMS.objects.link(co)
    co.location = pos
    co.rotation_euler = (V(tgt) - V(pos)).to_track_quat("-Z", "Y").to_euler()
    return co

# shot: camera, fog (start, depth scale, fog height, max, sky), ground-haze strength
SHOTS = {
    "overview": (camera("CAM_Overview", (-60.0, -56.0, 36.0), (3.0, 4.0, 0.0), 27), (85.0, 80.0, 14.0, 0.85, 0.45), 0.3),
    "road": (camera("CAM_Road", (-37.5, road_y(-37.5) - 0.35, 1.65), (30.0, road_y(30.0) + 0.6, 4.3), 30),
             (14.0, 38.0, 10.0, 0.88, 0.5), 1.0),
    "low": (camera("CAM_Low", (-28.8, road_y(-28.8) + 1.3, 0.35), (-23.2, -8.0, 8.8), 18), (22.0, 40.0, 12.0, 0.88, 0.45), 0.6),
}
KITCAMS = {}
dirv = V((-15.5, -19.0, 2.6)).normalized()
for name in ORDER:
    rec = HOUSES[name]
    tgt = V((rec["kit_x"], KITY - (1.0 if name == "Chapel" else 0.0), 6.2))
    KITCAMS[name] = camera(f"CAM_Kit_{name}", tgt + dirv * 42.0, tgt, 50)
SC.camera = SHOTS["road"][0]

# =====================================================================
# save, export
# =====================================================================
os.makedirs(RDIR, exist_ok=True); os.makedirs(FBXDIR, exist_ok=True); os.makedirs(MDIR, exist_ok=True)
blend = os.path.join(OUT, "village.blend")
if not NIGHT:
    bpy.ops.wm.save_as_mainfile(filepath=blend)
print("[saved]", blend)

def export(path, obs):
    if NIGHT:
        return
    for o in bpy.context.view_layer.objects:
        o.select_set(o in obs)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", use_mesh_modifiers=True,
                             add_leaf_bones=False, use_triangles=True)
    print("[saved]", path)
for name, rec in HOUSES.items():
    saved = [(o, o.matrix_world.copy()) for o in (rec["ob"], rec["snow"])]
    for o, _ in saved:
        o.matrix_world = Matrix.Identity(4)
    export(os.path.join(FBXDIR, f"SM_House_{name}.fbx"), [rec["ob"], rec["snow"]])
    for o, m in saved:
        o.matrix_world = m
saved = [(o, o.matrix_world.copy()) for o in PROTO.values()]
for o, _ in saved:
    o.matrix_world = Matrix.Identity(4)
export(os.path.join(FBXDIR, "Village_Props.fbx"), list(PROTO.values()))
for o, m in saved:
    o.matrix_world = m
env = [o for o in bpy.data.objects if o.name in ("SM_Road", "SM_RoadStones", "SM_Fences", "SM_Graveyard", "SM_GrassTufts")]
export(os.path.join(FBXDIR, "Village_Environment.fbx"), env)
for name, rec in HOUSES.items():
    me = rec["ob"].data; sn = rec["snow"].data
    print(f"[stats] {name}: {sum(len(p.vertices) - 2 for p in me.polygons)} tris (+ snow "
          f"{sum(len(p.vertices) - 2 for p in sn.polygons)})")

# =====================================================================
# render + post (sky, depth/ground fog, veil, blizzard)
# =====================================================================
FOGV = 0.88
def load_exr(prefix):
    fl = sorted(glob.glob(os.path.join(MDIR, prefix + "*.exr")))
    if not fl:
        return None
    im = bpy.data.images.load(fl[-1], check_existing=False); w, h = im.size
    a = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(a); bpy.data.images.remove(im)
    return a.reshape(h, w, 4)

def shift_filter(a, r, op):
    out = a.copy()
    for ax in (0, 1):
        src = out.copy()
        for k in range(1, r + 1):
            out = op(out, np.roll(src, k, ax)); out = op(out, np.roll(src, -k, ax))
    return out

def finish2(path, seed, fog, haze_k):
    im = bpy.data.images.load(path, check_existing=False)
    w, h = im.size
    a = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(a); a = a.reshape(h, w, 4)
    al = a[..., 3:4]
    rgb = a[..., :3] * al + winter_sky(h, w)[..., None] * (1 - al)
    if fog:
        f0, fd, fh, fmax, fsky = fog
        d = load_exr("depth_")[..., 0]
        bg = d > 1500.0
        dm = shift_filter(np.where(bg, 1e9, d), 3, np.minimum)
        f = 1 - np.exp(-np.maximum(dm - f0, 0) / fd)
        pos = load_exr("pos_") if HAS_POS else None
        if pos is not None:
            hz = shift_filter(np.where(bg, -1e9, pos[..., 2]), 3, np.maximum)
            f *= np.clip(1.25 - hz / fh, 0.4, 1.0)
        f = np.where(dm > 1e8, fsky, np.minimum(f, fmax))[..., None]
        print(f"[fog] {os.path.basename(path)}: depth range {float(d[~bg].min()) if (~bg).any() else 0:.1f}.."
              f"{float(d[~bg].max()) if (~bg).any() else 0:.1f}, bg {float(bg.mean()):.2f}, mean fog {float(f.mean()):.2f}")
        rgb = rgb * (1 - f) + FOGV * f
    rgb = rgb * 0.86 + 0.58 * 0.14
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    top = 0.22 * h + 12 * np.sin(xx / 95.0) + 7 * np.sin(xx / 37.0)
    haze = (haze_k * 0.8 * np.clip((top - yy) / top, 0, 1) ** 1.2)[..., None]
    rgb = rgb * (1 - haze) + 0.97 * haze
    m = (0.9 * snowfall(h, w, seed))[..., None]
    rgb = rgb * (1 - m) + 0.97 * m
    a[..., :3] = rgb; a[..., 3] = 1.0
    im.pixels.foreach_set(a.ravel()); im.filepath_raw = path; im.file_format = "PNG"; im.save()
    bpy.data.images.remove(im)
    chroma = float(np.max(np.abs(rgb - rgb.mean(-1, keepdims=True))))
    lum = rgb.mean(-1)
    print(f"[check] {os.path.basename(path)}: max chroma {chroma:.4f}, black {np.mean(lum < 0.1):.2f}, "
          f"white {np.mean(lum > 0.9):.2f}, mid {np.mean((lum >= 0.1) & (lum <= 0.9)):.2f}")
    return a

def only_layers(names):
    """Render only the given collections (less Freestyle work and memory per shot)."""
    for lc in vl.layer_collection.children:
        lc.exclude = lc.name not in names

def shoot(fname, cam, fog, haze_k, seed):
    SC.camera = cam
    path = os.path.join(RDIR, fname)
    SC.render.filepath = path
    for f in glob.glob(os.path.join(MDIR, "*.exr")):
        os.remove(f)
    bpy.ops.render.render(write_still=True)
    return finish2(path, seed, fog, haze_k)

def save_png(arr, path):
    h, w = arr.shape[:2]
    im = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    im.pixels.foreach_set(arr.ravel()); im.filepath_raw = path; im.file_format = "PNG"; im.save()
    bpy.data.images.remove(im)
    print("[saved]", path)

if not NORENDER and not NIGHT:
    only_layers({"COL_Houses", "COL_Props", "COL_Nature", "COL_Lights", "COL_Ground", "COL_Cameras"})
    for i, (key, (cam, fog, hk)) in enumerate(SHOTS.items()):
        if ONLY and key not in ONLY:
            continue
        shoot(f"village_{key}.png", cam, fog, hk, seed=11 + i)
    if not ONLY or "sheet" in ONLY:
        only_layers({"COL_Kit", "COL_Lights", "COL_Ground", "COL_Cameras"})
        tw = RX // 2
        sheet = np.ones((2 * RY, 4 * tw, 4), np.float32)
        for i, name in enumerate(ORDER):
            a = shoot(f"village_kit_{name.lower()}.png", KITCAMS[name], None, 1.0, seed=31 + i)
            tile = a[:, RX // 4: RX // 4 + tw].copy()
            r, c = i // 4, i % 4
            sheet[(1 - r) * RY:(2 - r) * RY, c * tw:(c + 1) * tw] = tile
        for c in range(1, 4):
            sheet[:, c * tw - 2:c * tw + 2, :3] = 0.0
        sheet[RY - 2:RY + 2, :, :3] = 0.0
        save_png(sheet, os.path.join(RDIR, "village_houses_sheet.png"))

# =====================================================================
# night screenshot mode (--night): murk, dark fog, whirling blizzard, glowing windows
# =====================================================================
def to_srgb(x):
    x = np.clip(x, 0.0, 1.0)
    return np.where(x <= 0.0031308, 12.92 * x, 1.055 * np.power(x, 1 / 2.4) - 0.055)

def box_blur(a, r):
    for ax in (0, 1):
        for _ in range(3):
            c = np.cumsum(np.pad(a, [(r + 1, r) if i == ax else (0, 0) for i in range(2)], mode="edge"), axis=ax)
            a = (np.take(c, range(2 * r + 1, c.shape[ax]), axis=ax) - np.take(c, range(0, c.shape[ax] - 2 * r - 1), axis=ax)) / (2 * r + 1)
    return a

def night_sky(h, w, seed):
    g = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    c = math.sqrt(0.5)
    qx, qy = (xx * c - yy * c) / DOT_CELL, (xx * c + yy * c) / DOT_CELL
    d = np.hypot(qx - np.floor(qx) - 0.5, qy - np.floor(qy) - 0.5)
    low = np.clip(1 - (yy / h - 0.3) / 0.7, 0, 1) ** 1.6              # murky light low on the horizon
    clouds = box_blur(g.random((h // 8, w // 8)).astype(np.float32), 3)
    clouds = np.kron(clouds, np.ones((8, 8), np.float32))[:h, :w]
    r = 0.04 + 0.3 * low * (0.6 + 0.8 * (clouds - clouds.mean()) / (clouds.std() * 3 + 1e-6))
    return 0.03 + 0.5 * np.clip((np.clip(r, 0, 0.45) - d) * DOT_CELL + 0.5, 0, 1)

def draw_lines(m, pts_list, val=1.0):
    h, w = m.shape
    for xs, ys, th in pts_list:
        xi, yi = xs.astype(int), ys.astype(int)
        for o in range(th):
            ok = (xi >= 0) & (xi < w) & (yi + o >= 0) & (yi + o < h)
            m[yi[ok] + o, xi[ok]] = val

def vortex(h, w, cx, cy, seed, squash=2.2):
    """Ragged spiral mist + broken spiral speed lines around (cx, cy) (pixels, row 0 = bottom)."""
    g = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    dx, dy = xx - cx, (yy - cy) * squash
    r = np.hypot(dx, dy) + 1.0; th = np.arctan2(dy, dx)
    noise = box_blur(g.random((h // 6 + 1, w // 6 + 1)).astype(np.float32), 4)
    noise = np.kron((noise - noise.mean()) / (noise.std() + 1e-6), np.ones((6, 6), np.float32))[:h, :w]
    s = th + 2.2 * np.log(r) + 0.5 * noise
    band = np.clip(0.5 + 0.5 * np.sin(2 * s) + 0.25 * noise, 0, 1)
    fall = np.clip((r - 0.04 * w) / (0.12 * w), 0, 1) * np.exp(-r / (0.45 * w))
    mist = band ** 2 * fall
    streak = np.zeros((h, w), np.float32)
    for _ in range(0):
        r0 = w * g.uniform(0.12, 0.95) ** 0.8; t0 = g.uniform(0, 2 * math.pi); L = g.uniform(0.25, 0.9)
        n = int(L * r0 / 1.2) + 2
        t = np.linspace(0, L, n)
        rr = r0 * np.exp(-0.35 * t)
        tt = t0 + t
        xs, ys = cx + rr * np.cos(tt), cy + rr * np.sin(tt) / squash
        val = g.uniform(0.35, 1.0); th_ = int(g.choice([1, 1, 1, 2, 2, 3]))
        xi, yi = xs.astype(int), ys.astype(int)
        fade = np.sin(np.linspace(0, math.pi, n)) * val            # tapered both ends, like a brush stroke
        for o in range(th_):
            ok = (xi >= 0) & (xi < w) & (yi + o >= 0) & (yi + o < h)
            streak[yi[ok] + o, xi[ok]] = np.maximum(streak[yi[ok] + o, xi[ok]], fade[ok])
    fl = np.zeros((h, w), np.float32)
    for _ in range(60):
        fx, fy, fr = g.uniform(0, w), g.uniform(0, h), g.uniform(2.0, 5.5)
        xa, xb, ya, yb = int(max(fx - fr - 2, 0)), int(min(fx + fr + 2, w)), int(max(fy - fr - 2, 0)), int(min(fy + fr + 2, h))
        if xa < xb and ya < yb:
            sy, sx = np.mgrid[ya:yb, xa:xb]
            fl[ya:yb, xa:xb] = np.maximum(fl[ya:yb, xa:xb], np.clip(fr - np.hypot(sx - fx, sy - fy) + 0.5, 0, 1))
    return mist, streak, fl, noise

def finish_night(path, out, seed, fog, centre):
    im = bpy.data.images.load(path, check_existing=False); w, h = im.size
    a = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(a); a = a.reshape(h, w, 4); bpy.data.images.remove(im)
    disp, al = a[..., :3], a[..., 3:4]
    glow = ((disp.min(-1) > 0.996) & (al[..., 0] > 0.99)).astype(np.float32)   # only the glow material renders pure white
    scene = 0.6 * np.power(disp, 1.7)                                   # night: everything sinks, ink stays black
    scene = np.maximum(scene, glow[..., None] * 0.95)
    rgb = scene * al + night_sky(h, w, seed)[..., None] * (1 - al)
    f0, fd, fh, fmax, fsky = fog
    d = load_exr("depth_")[..., 0]; bg = d > 1500.0
    dm = shift_filter(np.where(bg, 1e9, d), 3, np.minimum)
    f = 1 - np.exp(-np.maximum(dm - f0, 0) / fd)
    pos = load_exr("pos_")
    hz = shift_filter(np.where(bg, -1e9, pos[..., 2]), 3, np.maximum)
    f *= np.clip(1.25 - hz / fh, 0.45, 1.0)
    f = np.where(dm > 1e8, fsky, np.minimum(f, fmax))[..., None]
    rgb = rgb * (1 - f) + 0.08 * f                                      # black murk eats the distance
    mist, streak, fl, noise = vortex(h, w, centre[0] * w, centre[1] * h, seed)
    ground = np.where(dm > 1e8, 0.0, np.clip(1 - hz / 1.8, 0, 1) * np.clip((dm - 5.0) / 20.0, 0, 1))
    ground = box_blur(ground.astype(np.float32), 5) * np.clip(0.55 + 0.45 * noise, 0, 1)
    ground = (0.45 * ground)[..., None]
    rgb = rgb * (1 - ground) + 0.3 * ground                             # pale mist crawling over the snow
    bloom = np.clip(box_blur(glow, 12) * 3.0, 0, 1) * 0.6 + np.clip(box_blur(glow, 55) * 6.0, 0, 1) * 0.35
    rgb = rgb + bloom[..., None] * (1 - rgb)                            # window / forge light bleeding into the dark
    mist = (0.3 * np.clip(0.5 + 0.5 * noise, 0, 1) ** 2)[..., None]   # drifting murk patches, no rings
    rgb = rgb * (1 - mist) + 0.4 * mist
    m = (0.6 * np.maximum(snowfall(h, w, seed + 7), snowfall(h, w, seed + 8)) + fl)[..., None].clip(0, 1)
    rgb = rgb * (1 - m) + 0.8 * m
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    v = ((xx / w - 0.5) ** 2 * 1.2 + (yy / h - 0.5) ** 2) / 0.5
    rgb = rgb * (1 - 0.7 * np.clip(v, 0, 1) ** 1.4)[..., None]           # heavy vignette
    out_a = np.concatenate([np.clip(rgb, 0, 1), np.ones((h, w, 1), np.float32)], -1)
    save_png(out_a, out)
    lum = rgb.mean(-1)
    print(f"[check] {os.path.basename(out)}: glow px {int(glow.sum())}, max chroma "
          f"{float(np.max(np.abs(rgb - rgb.mean(-1, keepdims=True)))):.4f}, black {np.mean(lum < 0.1):.2f}, mean {float(lum.mean()):.2f}")

if NIGHT and not NORENDER:
    mats["M_Glow"].node_tree.nodes["Emission"].inputs["Color"].default_value = (1.0, 1.0, 1.0, 1.0)
    only_layers({"COL_Houses", "COL_Props", "COL_Nature", "COL_Lights", "COL_Ground", "COL_Cameras"})
    NIGHTSHOTS = {"road": ((10.0, 26.0, 11.0, 0.95, 0.7), (0.52, 0.68)),
                  "overview": ((50.0, 45.0, 16.0, 0.95, 0.7), (0.56, 0.58)),
                  "low": ((20.0, 35.0, 12.0, 0.9, 0.55), (0.5, 0.75))}
    for i, (key, (fog, centre)) in enumerate(NIGHTSHOTS.items()):
        if (ONLY or ["road", "overview"]) and key not in (ONLY or ["road", "overview"]):
            continue
        SC.camera = SHOTS[key][0]
        exr = os.path.join(MDIR, f"night_{key}.png")
        SC.render.filepath = exr
        for fpath in glob.glob(os.path.join(MDIR, "depth_*.exr")) + glob.glob(os.path.join(MDIR, "pos_*.exr")):
            os.remove(fpath)
        bpy.ops.render.render(write_still=True)
        finish_night(exr, os.path.join(RDIR, f"village_night_{key}.png"), 41 + i, fog, centre)
print("[done]")
