"""Manga-horror medieval house #01 (poor apothecary, 2 storeys) as a clean low-poly mesh.
v2 "grim": stronger lean and sag, roof hole with bare battens, fallen plaster, damp streaks,
missing stones and rubble, boarded / lit windows with a silhouette, skull over the door, noose,
crows, firewood with a chopping stump, dry grass; low moonlight, night sky with moon, ground fog.
Backup of the clean v1: scripts/build_house_manga_v1_clean.py, house_01_v1_clean.blend.

Run:  blender -b --factory-startup -P scripts/build_house_manga.py [-- <output dir, default: the folder above scripts/>] [--norender]
Writes house_01.blend, house_01.fbx (house + props, for Unity) and renders/house_01_*.png.

Style (village_manga_prompt.md): achromatic only, shapes instead of textures (separate stones,
beams, tiles), Freestyle ink lines, stepped tone shading: white / screentone dots / hatching / black.
Asset rules: metres, front faces -Y, pivot at ground centre, no object transforms (lean and ridge
sag are baked into the vertices), flat-shaded quads/ngons, box-projected UVs.
"""
import bpy, math, random, sys, os
import numpy as np
from mathutils import Vector as V

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = next((a for a in argv if not a.startswith("--")), os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
NORENDER = "--norender" in argv
RDIR = os.path.join(OUT, "renders")
RX, RY = 1600, 1200

bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene
SC.unit_settings.system = "METRIC"
SC.unit_settings.scale_length = 1.0
rng = random.Random(11)
def R(a, b):
    return rng.uniform(a, b)

X, Y, Z = V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))

# =====================================================================
# geometry accumulator: material -> (verts, faces); one GEO per output object
# =====================================================================
GEO = {}
def acc(mat):
    return GEO.setdefault(mat, ([], []))

FACES6 = {"bottom": (0, 3, 2, 1), "top": (4, 5, 6, 7), "front": (0, 1, 5, 4),
          "right": (1, 2, 6, 5), "back": (2, 3, 7, 6), "left": (3, 0, 4, 7)}

def hexa(c, mat, skip=()):
    """8 corners: z0 layer (x0y0, x1y0, x1y1, x0y1) then z1 layer, in a right-handed frame."""
    vs, fs = acc(mat); b = len(vs)
    vs.extend(V(p) for p in c)
    for k, f in FACES6.items():
        if k not in skip:
            fs.append(tuple(b + i for i in f))

def fbox(O, ex, ey, ez, x0, x1, y0, y1, z0, z1, mat, skip=(), jit=0.0):
    """Box in the frame (O, ex, ey, ez); ez = ex x ey. jit roughens the z1 face only."""
    c = []
    for z in (z0, z1):
        for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
            p = O + ex * x + ey * y + ez * z
            if jit and z == z1:
                p = p + ex * R(-jit, jit) + ey * R(-jit, jit) + ez * R(-jit, jit) * 0.6
            c.append(p)
    hexa(c, mat, skip)

def box(x0, x1, y0, y1, z0, z1, mat, skip=()):
    fbox(V((0, 0, 0)), X, Y, Z, x0, x1, y0, y1, z0, z1, mat, skip)

def seg(p0, p1, w, mat, h=None, up=Z):
    """Square-ish beam between two world points."""
    p0, p1 = V(p0), V(p1)
    ex = (p1 - p0); L = ex.length; ex.normalize()
    ey = up.cross(ex)
    if ey.length < 1e-4:
        ey = X.cross(ex)
    ey.normalize(); ez = ex.cross(ey)
    h = w if h is None else h
    fbox(p0, ex, ey, ez, 0, L, -w / 2, w / 2, -h / 2, h / 2, mat)

def newell(pts):
    n = V((0, 0, 0))
    for i in range(len(pts)):
        a, b = pts[i], pts[(i + 1) % len(pts)]
        n += V(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
    return n

def loft(sections, mat, cap0=True, cap1=True):
    """Closed rings swept along their order; orientation is fixed automatically."""
    secs = [[V(p) for p in s] for s in sections]
    c0 = sum(secs[0], V()) / len(secs[0]); c1 = sum(secs[1], V()) / len(secs[1])
    if newell(secs[0]).dot(c1 - c0) < 0:
        secs = [list(reversed(s)) for s in secs]
    vs, fs = acc(mat); b = len(vs); n = len(secs[0])
    for s in secs:
        vs.extend(s)
    for k in range(len(secs) - 1):
        for i in range(n):
            j = (i + 1) % n
            a0, a1 = b + k * n + i, b + k * n + j
            fs.append((a0, a1, a1 + n, a0 + n))
    if cap0:
        fs.append(tuple(b + i for i in reversed(range(n))))
    if cap1:
        fs.append(tuple(b + (len(secs) - 1) * n + i for i in range(n)))

def prism(O, ex, ey, ez, pts, z0, z1, mat):
    loft([[O + ex * x + ey * y + ez * z for x, y in pts] for z in (z0, z1)], mat)

# =====================================================================
# house dimensions (metres). Front = -Y. Ridge runs along Y: the gable faces the street.
# =====================================================================
HW, HD, H0 = 2.4, 3.0, 3.0          # ground floor half width / half depth / height (stone)
J, JS = 0.45, 0.15                  # jetty of the timber storey: front+back / sides
HWT, HDT = HW + JS, HD + J
H1 = 2.9; ZT = H0 + H1              # top of the timber storey (eaves line)
PITCH = 1.5                         # ~56 degrees
A_ = math.atan(PITCH); CA, SA = math.cos(A_), math.sin(A_)
ZR = ZT + HWT * PITCH               # ridge (roof underside)
EAVE, GOV, ROOF_T = HWT + 0.45, HDT + 0.35, 0.16
LEAN_X, LEAN_Y, SAG = 0.065, -0.025, 0.42   # ~3.7 deg lean of the upper part, deep ridge sag
SETTLE = 0.012                               # the whole house sinks ~0.7 deg toward +X (rotten footing)
CHIM = (-HW - 0.68, -HW - 0.08, 0.72, 1.48)  # brick stack footprint x0 x1 y0 y1 (left wall)
ZCH = ZR - 0.55

def warp(p):
    """Bake the 'living' crookedness: ridge sags in the middle, everything above H0 leans."""
    x, y, z = p
    wx = max(0.0, 1 - abs(x) / EAVE)
    wy = max(0.0, 1 - (y / GOV) ** 2)
    wz = min(1.0, max(0.0, (z - H0) / (ZT - H0)))
    z -= SAG * wx * wy * wz
    up = max(z - H0, 0.0)
    return V((x + LEAN_X * up + SETTLE * z, y + LEAN_Y * up, z))

# ---------- walls ----------
def wall_frame(side, hx, hy):
    """origin, ex (along the wall, viewer's right), ey (up), ez (outward), half length."""
    return {"f": (V((0, -hy, 0)), X, Z, -Y, hx), "b": (V((0, hy, 0)), -X, Z, Y, hx),
            "l": (V((-hx, 0, 0)), -Y, Z, -X, hy), "r": (V((hx, 0, 0)), Y, Z, X, hy)}[side]

def wp(W, u, z, out=0.0):
    O, ex, ey, ez, _ = W
    return O + ex * u + ey * z + ez * out

def wbeam(W, a, b, w=0.18, d=0.08, mat="M_Timber", z0=0.0, ext=0.0, jit=0.012):
    O, ex, ey, ez, _ = W
    a = (a[0] + R(-jit, jit), a[1] + R(-jit, jit)); b = (b[0] + R(-jit, jit), b[1] + R(-jit, jit))
    pa, pb = wp(W, *a), wp(W, *b)
    u = pb - pa; L = u.length; u.normalize()
    v = ez.cross(u)
    fbox(pa, u, v, ez, -ext, L + ext, -w / 2, w / 2, z0, z0 + d * R(0.88, 1.12), mat, skip=("bottom",))

def wpanel(W, u0, u1, z0, z1, o0, o1, mat, skip=("bottom",), jit=0.0):
    O, ex, ey, ez, _ = W
    fbox(O, ex, ey, ez, u0, u1, z0, z1, o0, o1, mat, skip, jit)

def shutter(W, hu, hz, sw, sh, side, rot=0.0, off=0.1):
    """Plank shutter hinged at (hu, hz) = its top outer corner; side=-1 opens to the left."""
    O, ex, ey, ez, _ = W
    t = rot
    e1 = ex * math.cos(t) + ey * math.sin(t); e2 = -ex * math.sin(t) + ey * math.cos(t)
    org = wp(W, hu, hz, off)
    x0, x1 = (0.0, sw) if side < 0 else (-sw, 0.0)
    fbox(org, e1, e2, ez, x0, x1, -sh, 0, 0, 0.045, "M_Wood", skip=("bottom",))
    for k in range(1, 3):                         # plank joints
        xc = x0 + sw * k / 3
        fbox(org, e1, e2, ez, xc - 0.008, xc + 0.008, -sh + 0.02, -0.02, 0.045, 0.05, "M_Iron", skip=("bottom",))
    for zz in (-0.18, -sh + 0.18):                # battens
        fbox(org, e1, e2, ez, x0 + 0.03, x1 - 0.03, zz - 0.05, zz + 0.05, 0.045, 0.075, "M_Timber", skip=("bottom",))

# ---------- stone ground floor ----------
class Rect:
    def __init__(s, u0, u1, z0, z1):
        s.u0, s.u1, s.z0, s.z1 = u0, u1, z0, z1
    def blocked(s, z0, z1):
        return (s.u0, s.u1) if (z1 > s.z0 and z0 < s.z1) else None

class Arch:
    def __init__(s, uc, hw, spring, band):
        s.uc, s.hw, s.spring, s.band = uc, hw, spring, band
    def blocked(s, z0, z1):
        r = s.hw + s.band
        if z0 < s.spring:
            return (s.uc - r, s.uc + r)
        dz = z0 - s.spring
        if dz >= r:
            return None
        h = math.sqrt(r * r - dz * dz) + 0.03
        return (s.uc - h, s.uc + h)

def free_intervals(lo, hi, blocks):
    out, cur = [], lo
    for a, b in sorted(blocks):
        if a > cur:
            out.append((cur, min(a, hi)))
        cur = max(cur, b)
    if cur < hi:
        out.append((cur, hi))
    return [(a, b) for a, b in out if b - a > 0.1]

GAP = 0.035
def stone_wall(side, openings):
    W = wall_frame(side, HW, HD); half = W[4]
    ext = 0.07 if side in "fb" else 0.0           # front/back stones wrap the corners
    lo, hi = -half - ext, half + ext
    z, row = 0.0, 0
    while z < H0 - 0.02:
        z1 = min(z + R(0.30, 0.42), H0)
        if H0 - z1 < 0.2:
            z1 = H0
        blocks = [iv for iv in (op.blocked(z, z1) for op in openings) if iv]
        for a, b in free_intervals(lo, hi, blocks):
            u, first = a, True
            while b - u > 0.1:
                if first and abs(a - lo) < 1e-6:      # corner quoins alternate long / short
                    w = 0.82 if row % 2 else 0.5
                else:
                    w = R(0.45, 0.85)
                if b - (u + w) < 0.3:
                    w = b - u
                if abs(b - hi) < 1e-6 and b - (u + w) < 0.95 and b - u > 0.95:
                    w = b - u - (0.5 if row % 2 else 0.82)
                corner = abs(u - lo) < 1e-6 or abs(u + w - hi) < 1e-6
                d = R(0.05, 0.08) if corner else R(0.03, 0.075)
                if corner or rng.random() > 0.05:          # ~5% of the stones have fallen out
                    wpanel(W, u + GAP / 2, u + w - GAP / 2, z + GAP / 2, z1 - GAP / 2, 0, d, "M_Stone", jit=0.02)
                u += w; first = False
        z = z1; row += 1
    return W

def arch_door(W, uc, hw, spring):
    pts = [(uc - hw, 0), (uc + hw, 0), (uc + hw, spring)]
    for i in range(1, 8):
        t = math.pi * i / 8
        pts.append((uc + hw * math.cos(t), spring + hw * math.sin(t)))
    pts.append((uc - hw, spring))
    O, ex, ey, ez, _ = W
    prism(O, ex, ey, ez, pts, 0.0, 0.025, "M_Wood")
    for k in (1, 2, 3):                                # plank joints, clipped by the arch
        u = uc - hw + 2 * hw * k / 4
        top = spring + math.sqrt(max(hw * hw - (u - uc) ** 2, 0)) - 0.04
        wpanel(W, u - 0.011, u + 0.011, 0.03, top, 0.025, 0.033, "M_Iron")
    for zz in (0.45, 1.4):                             # iron straps + ring handle
        wpanel(W, uc - hw + 0.04, uc + hw - 0.04, zz - 0.04, zz + 0.04, 0.025, 0.045, "M_Iron")
    wpanel(W, uc + hw - 0.24, uc + hw - 0.16, 0.95, 1.07, 0.025, 0.07, "M_Iron")
    # jamb stones
    z, k = 0.0, 0
    while z < spring - 0.05:
        z1 = min(z + 0.43, spring)
        for s in (-1, 1):
            wj = 0.3 if (k + (s > 0)) % 2 else 0.22
            u0, u1 = (uc - hw - wj, uc - hw) if s < 0 else (uc + hw, uc + hw + wj)
            wpanel(W, u0 + GAP / 2, u1 - GAP / 2, z + GAP / 2, z1 - GAP / 2, 0, R(0.08, 0.095), "M_Stone", jit=0.01)
        z = z1; k += 1
    # voussoirs
    n = 9
    for i in range(n):
        t0 = math.pi * i / n + 0.012; t1 = math.pi * (i + 1) / n - 0.012
        ro = hw + (0.36 if i == n // 2 else R(0.26, 0.31))
        ri = hw + 0.01
        pts = [(uc + ri * math.cos(t0), spring + ri * math.sin(t0)), (uc + ro * math.cos(t0), spring + ro * math.sin(t0)),
               (uc + ro * math.cos(t1), spring + ro * math.sin(t1)), (uc + ri * math.cos(t1), spring + ri * math.sin(t1))]
        prism(O, ex, ey, ez, pts, 0.0, R(0.085, 0.1), "M_Stone")
    # threshold step
    wpanel(W, uc - hw - 0.2, uc + hw + 0.2, 0.0, 0.14, 0.0, 0.42, "M_Stone", skip=("bottom",))

def stone_window(W, uc, hw, zc, hh, kind="cross"):
    wpanel(W, uc - hw, uc + hw, zc - hh, zc + hh, 0.0, 0.012, "M_Void")
    wpanel(W, uc - hw - 0.14, uc + hw + 0.14, zc - hh - 0.13, zc - hh, 0.0, 0.13, "M_Stone", jit=0.008)   # sill
    wpanel(W, uc - hw - 0.17, uc + hw + 0.17, zc + hh, zc + hh + 0.2, 0.0, 0.09, "M_Stone", jit=0.008)   # lintel
    for s in (-1, 1):
        u0, u1 = (uc - hw - 0.15, uc - hw) if s < 0 else (uc + hw, uc + hw + 0.15)
        wpanel(W, u0, u1, zc - hh, zc - 0.01, 0.0, 0.085, "M_Stone", jit=0.008)
        wpanel(W, u0, u1, zc + 0.01, zc + hh, 0.0, 0.085, "M_Stone", jit=0.008)
    if kind == "cross":
        wpanel(W, uc - 0.025, uc + 0.025, zc - hh, zc + hh, 0.012, 0.05, "M_Timber")
        wpanel(W, uc - hw, uc + hw, zc - 0.025 + 0.12, zc + 0.025 + 0.12, 0.012, 0.05, "M_Timber")
    elif kind == "bars":
        for k in (-1, 0, 1):
            u = uc + k * hw * 0.5
            wpanel(W, u - 0.018, u + 0.018, zc - hh, zc + hh, 0.02, 0.055, "M_Iron")
    elif kind == "boarded":
        wbeam(W, (uc - hw - 0.12, zc - hh + 0.15), (uc + hw + 0.14, zc - hh + 0.42), w=0.17, d=0.04, mat="M_Wood", z0=0.09)
        wbeam(W, (uc - hw - 0.1, zc + 0.12), (uc + hw + 0.1, zc + 0.02), w=0.16, d=0.04, mat="M_Wood", z0=0.135)
        wbeam(W, (uc - hw - 0.08, zc + hh - 0.05), (uc + hw + 0.12, zc - 0.25), w=0.15, d=0.04, mat="M_Wood", z0=0.18)
    return Rect(uc - hw - 0.2, uc + hw + 0.2, zc - hh - 0.15, zc + hh + 0.22)

# ---------- timber storey ----------
ZB0, ZB1 = H0 + 0.24, ZT - 0.22     # clear height between sill plate and top plate
def grime(W, u, ztop, length, w):
    """Damp streak running down the plaster (tone sheet)."""
    O, ex, ey, ez, _ = W
    pts = [(u - w, ztop), (u + w, ztop), (u + w * 0.35 + R(-0.03, 0.03), ztop - length),
           (u - w * 0.25, ztop - length * R(0.6, 0.9))]
    prism(O, ex, ey, ez, pts, 0.002, 0.005, "M_Grime")

def crumble(W, uc, zc, rx, rz, laths=True):
    """Plaster fallen off: a dark jagged hole with the wattle laths showing."""
    O, ex, ey, ez, _ = W
    raw = [R(0.6, 1.05) for _ in range(18)]                     # irregular, not star-like
    rad = [(raw[k - 1] + 2 * raw[k] + raw[(k + 1) % 18]) / 4 + R(-0.08, 0.08) for k in range(18)]
    pts = [(uc + rx * r * math.cos(2 * math.pi * k / 18), zc + rz * r * math.sin(2 * math.pi * k / 18))
           for k, r in enumerate(rad)]
    prism(O, ex, ey, ez, pts, 0.002, 0.007, "M_Mortar")
    if laths:
        zz = zc - rz * 0.55
        while zz < zc + rz * 0.6:
            half = rx * 0.68 * math.sqrt(max(0.0, 1 - ((zz - zc) / rz) ** 2))
            if half > 0.06:
                wpanel(W, uc - half + R(-0.04, 0.04), uc + half + R(-0.04, 0.04), zz - 0.022, zz + 0.022,
                       0.007, 0.02, "M_Wood")
            zz += 0.11

def figure(W, u, zs):
    """Black silhouette standing behind a lit window, head tilted."""
    O, ex, ey, ez, _ = W
    body = [(u - 0.25, zs), (u + 0.25, zs), (u + 0.23, zs + 0.4), (u + 0.12, zs + 0.54),
            (u - 0.12, zs + 0.54), (u - 0.23, zs + 0.4)]
    neck = [(u - 0.05, zs + 0.5), (u + 0.05, zs + 0.5), (u + 0.07, zs + 0.64), (u - 0.03, zs + 0.64)]
    head = []
    for k in range(10):
        t = 2 * math.pi * k / 10
        hz = zs + 0.74 + 0.135 * math.sin(t)
        head.append((u + 0.1 * math.cos(t) + 0.35 * (hz - zs - 0.6), hz))
    for pts in (body, neck, head):
        prism(O, ex, ey, ez, pts, 0.012, 0.02, "M_Void")

def timber_window(W, c, ww, zs, zh, shutters=None, state=None):
    lit = state == "lit"
    wpanel(W, c - ww, c + ww, zs, zh, 0.0, 0.012, "M_Glow" if lit else "M_Void")
    if lit:
        figure(W, c + 0.13, zs)
    wbeam(W, (c, zs), (c, zh), w=0.05, d=0.05, z0=0.012, jit=0.0)
    wbeam(W, (c - ww, zs + (zh - zs) * 0.62), (c + ww, zs + (zh - zs) * 0.62), w=0.05, d=0.05, z0=0.012, jit=0.0)
    if state == "boarded":
        wbeam(W, (c - ww - 0.15, zs + 0.2), (c + ww + 0.12, zs + 0.38), w=0.17, d=0.04, mat="M_Wood", z0=0.09)
        wbeam(W, (c - ww - 0.1, zs + 0.74), (c + ww + 0.15, zs + 0.6), w=0.16, d=0.04, mat="M_Wood", z0=0.135)
        wbeam(W, (c - ww - 0.12, zh - 0.05), (c + ww + 0.1, zs + 0.45), w=0.15, d=0.04, mat="M_Wood", z0=0.18)
    if zs > H0 + 0.6:                                  # damp streaks under the sill
        for du in (-0.6, 0.3):
            ztop = zs - 0.14
            grime(W, c + du * ww, ztop, min(R(0.45, 0.85), ztop - H0 - 0.15), R(0.07, 0.12))
    sw, sh = ww + 0.02, zh - zs + 0.06
    if shutters:
        shutter(W, c - ww - 0.1 - sw, zh + 0.03, sw, sh, side=-1, rot=-0.07 if shutters == "broken" else 0.0)
        if shutters == "open":
            shutter(W, c + ww + 0.1 + sw, zh + 0.03, sw, sh, side=1)
        elif shutters == "askew":                     # hangs from its last hinge
            shutter(W, c + ww + 0.1 + sw, zh + 0.03, sw, sh, side=1, rot=0.55)
        else:                                         # "broken": the right leaf is gone, hinge straps left
            for zz in (zh - 0.15, zs + 0.15):
                wpanel(W, c + ww + 0.07, c + ww + 0.3, zz - 0.025, zz + 0.025, 0.075, 0.095, "M_Iron")

def timber_wall(side, posts, bays):
    W = wall_frame(side, HWT, HDT); half = W[4]
    wbeam(W, (-half - 0.03, H0 + 0.12), (half + 0.03, H0 + 0.12), w=0.24, d=0.1)        # sill plate
    wbeam(W, (-half - 0.03, ZT - 0.11), (half + 0.03, ZT - 0.11), w=0.22, d=0.09)        # top plate
    for u in posts:
        wbeam(W, (u, ZB0), (u, ZB1), w=0.18, d=0.085)
    for (a, b, kind, *arg) in bays:
        a0, b0 = a + 0.09, b - 0.09
        if kind == "x":
            wbeam(W, (a0, ZB0), (b0, ZB1), w=0.15, d=0.07)
            wbeam(W, (a0, ZB1), (b0, ZB0), w=0.15, d=0.075, z0=0.004)
        elif kind == "d":                         # single brace, arg = +1 rises to the right
            if arg[0] > 0:
                wbeam(W, (a0, ZB0), (b0, ZB1), w=0.15, d=0.07)
            else:
                wbeam(W, (a0, ZB1), (b0, ZB0), w=0.15, d=0.07)
        elif kind == "win":
            c = (a + b) / 2; ww = min(0.42, (b - a) / 2 - 0.25)
            zs, zh = H0 + 1.0, H0 + 2.12
            wbeam(W, (a0, zs - 0.07), (b0, zs - 0.07), w=0.14, d=0.075)
            wbeam(W, (a0, zh + 0.07), (b0, zh + 0.07), w=0.14, d=0.075)
            for s in (-1, 1):
                wbeam(W, (c + s * (ww + 0.07), zs), (c + s * (ww + 0.07), zh), w=0.14, d=0.075)
            wbeam(W, (a0, ZB0), (b0, zs - 0.14), w=0.12, d=0.065)          # low cross under the sill
            wbeam(W, (a0, zs - 0.14), (b0, ZB0), w=0.12, d=0.07, z0=0.004)
            timber_window(W, c, ww, zs, zh, *(list(arg) + [None, None])[:2])
    return W

def gable(side, window):
    W = wall_frame(side, HWT, HDT)
    zc = ZT + 1.7; xc = (ZR - zc) / PITCH - 0.08
    wbeam(W, (-xc, zc), (xc, zc), w=0.17, d=0.08)                            # collar
    if window:
        for s in (-1, 1):
            wbeam(W, (s * 0.55, ZT + 0.02), (s * 0.55, zc - 0.08), w=0.15, d=0.08)
            wbeam(W, (s * 0.68, ZT + 0.1), (s * (xc - 0.1), zc - 0.1), w=0.14, d=0.07)
        zs, zh = ZT + 0.45, ZT + 1.42
        wbeam(W, (-0.47, zs - 0.06), (0.47, zs - 0.06), w=0.12, d=0.075)
        wbeam(W, (-0.47, zh + 0.06), (0.47, zh + 0.06), w=0.12, d=0.075)
        timber_window(W, 0.0, 0.34, zs, zh)
    else:
        wbeam(W, (0, ZT + 0.02), (0, zc - 0.08), w=0.17, d=0.08)
        for s in (-1, 1):
            wbeam(W, (s * 0.15, ZT + 0.1), (s * (xc - 0.1), zc - 0.1), w=0.14, d=0.07)
            wbeam(W, (s * (HWT - 0.5), ZT + 0.1), (s * 0.2, zc - 0.1), w=0.13, d=0.07, z0=0.004)
    wbeam(W, (0, zc + 0.08), (0, ZR - 0.25), w=0.16, d=0.08)                 # king post
    for s in (-1, 1):
        wbeam(W, (s * (xc - 0.3), zc + 0.1), (s * 0.08, zc + 1.05), w=0.12, d=0.07)

# ---------- roof ----------
def in_chimney(p, m=0.06):
    return CHIM[0] - m < p.x < CHIM[1] + m and CHIM[2] - m < p.y < CHIM[3] + m

HOLE = (1.3, 2.9, 0.62, 0.55)        # left slope (u = -y there): centre u, v and half sizes of the hole
def roof_side(sgn):
    n = V((sgn * SA, 0, CA)); up = V((-sgn * CA, 0, SA)); ex = up.cross(n)
    O = V((sgn * EAVE, 0, ZR - EAVE * PITCH))
    LS = EAVE / CA - 0.006            # both slabs stop just short of the ridge line (no shared edges)
    # slab as a subdivided solid so the sag bends it: profile in (v, w), swept along u
    NV, NU = 5, 12
    prof = [(LS * i / NV, 0.0) for i in range(NV + 1)] + [(LS * i / NV, ROOF_T) for i in reversed(range(NV + 1))]
    secs = []
    for k in range(NU + 1):
        u = -GOV + 2 * GOV * k / NU
        secs.append([O + ex * u + up * v + n * w for v, w in prof])
    loft(secs, "M_Timber")
    hole = sgn < 0
    hu, hv, hru, hrv = HOLE
    # tile rows (overlapping, lower edge lifted -> a dark step under every row)
    ROW, TL, TT, LIFT = 0.42, 0.56, 0.035, 0.06
    i = 0
    while True:
        v0 = -0.06 + i * ROW
        if v0 > LS - 0.25:
            break
        v1 = min(v0 + TL, LS + 0.02)
        u = -GOV - R(0.0, 0.08) - (0.22 if i % 2 else 0.0)
        while u < GOV:
            w = R(0.36, 0.48)
            u0, u1 = max(u, -GOV - R(0.0, 0.07)), min(u + w - 0.03, GOV + R(-0.02, 0.07))
            u += w
            if u1 - u0 < 0.12:
                continue
            uc, vc = (u0 + u1) / 2, (v0 + v1) / 2
            centre = O + ex * uc + up * vc
            if in_chimney(centre) or rng.random() < 0.022:       # chimney hole, slipped tiles
                continue
            if hole and ((uc - hu) / hru) ** 2 + ((vc - hv) / hrv) ** 2 < 1.0 + R(-0.35, 0.3):
                continue
            dv = R(-0.035, 0.035); lift = LIFT + R(-0.02, 0.03)
            c = []
            for wl, wh in ((ROOF_T + lift, ROOF_T), (ROOF_T + lift + TT, ROOF_T + TT)):
                for uu, vv, ww in ((u0, v0 + dv, wl), (u1, v0 - dv, wl), (u1, v1, wh), (u0, v1, wh)):
                    c.append(O + ex * uu + up * vv + n * ww)
            hexa(c, "M_RoofTile", skip=("back",) if i == 0 else ("back", "bottom"))
        i += 1
    if hole:                                           # dark hole, bare battens and a rafter
        fbox(O, ex, up, n, hu - hru - 0.1, hu + hru + 0.1, hv - hrv - 0.1, hv + hrv + 0.1,
             ROOF_T + 0.001, ROOF_T + 0.004, "M_Void", skip=("bottom",))
        for k, vb in enumerate((hv - 0.42, hv, hv + 0.42)):
            u1 = hu + hru + (R(-0.55, -0.3) if k == 1 else 0.15)   # one batten snapped
            fbox(O, ex, up, n, hu - hru - 0.15, u1, vb - 0.03, vb + 0.03, ROOF_T + 0.004, ROOF_T + 0.045,
                 "M_Wood", skip=("bottom",))
        fbox(O, ex, up, n, hu - 0.25, hu - 0.15, hv - hrv - 0.2, hv + hrv + 0.2, ROOF_T + 0.004, ROOF_T + 0.08,
             "M_Timber", skip=("bottom",))
        for k in range(3):                             # board patch nailed over the tiles near the eave
            ub = -2.55 + k * 0.31 + R(-0.03, 0.03)
            fbox(O, ex, up, n, ub, ub + 0.27, 0.3 + R(0, 0.12), 1.5 + R(-0.12, 0.12),
                 ROOF_T + 0.135, ROOF_T + 0.17, "M_Wood")

def ridge_cap():
    zc = ZR + ROOF_T / CA + 0.02
    a = math.sqrt(0.5)
    ey = V((a, 0, a)); ez = Y.cross(ey)
    n = 11; Lp = 2 * (GOV + 0.08) / n
    for k in range(n):
        if k == 7:                                     # one ridge piece is gone
            continue
        y0 = -GOV - 0.08 + k * Lp
        O = V((R(-0.02, 0.02), y0, zc + R(-0.02, 0.02)))
        fbox(O, Y, ey, ez, 0.0, Lp - 0.025, -0.15, 0.15, -0.15, 0.15, "M_RoofTile")

def chimney():
    x0, x1, y0, y1 = CHIM[0] - 0.1, -HW + 0.02, CHIM[2] - 0.1, CHIM[3] + 0.1
    z, i = 0.0, 0                                     # stone base up to the jetty
    while z < H0 + 0.35:
        h = 0.38
        ins = 0.03 if i % 2 else 0.0
        box(x0 + ins + R(-0.02, 0.02), x1, y0 + ins, y1 - ins, z, z + h - GAP, "M_Stone", skip=("bottom",))
        z += h; i += 1
    box(x0 - 0.03, x1, y0 - 0.03, y1 + 0.03, z - GAP, z + 0.08, "M_Stone")   # weathering slab
    z += 0.08
    bx0, bx1, by0, by1 = CHIM
    i = 0
    while z < ZCH:                                      # brick courses, the stack leans off the wall
        h = 0.22
        sh = 0.04 * max(0.0, z - H0 - 0.4)
        sy = by0 + (by1 - by0) * (0.42 if i % 2 else 0.6)
        ins = 0.012 * (i % 2)
        top = z > ZCH - 0.8
        if not (top and rng.random() < 0.3):
            box(bx0 + ins - sh, bx1 - sh, by0, sy - 0.012, z, z + h - 0.025, "M_Brick", skip=("bottom",))
        if not (top and rng.random() < 0.3):
            box(bx0 + ins - sh, bx1 - sh, sy + 0.012, by1, z, z + h - 0.025, "M_Brick", skip=("bottom",))
        z += h; i += 1
    sh = 0.04 * (z - H0 - 0.4)
    box(bx0 - 0.07 - sh, bx1 + 0.07 - sh, by0 - 0.07, by1 + 0.07, z, z + 0.12, "M_Stone")       # cap
    box(bx0 + 0.14 - sh, bx1 - 0.14 - sh, by0 + 0.2, by1 - 0.2, z + 0.12, z + 0.32, "M_Brick", skip=("bottom",))
    box(bx0 + 0.19 - sh, bx1 - 0.19 - sh, by0 + 0.25, by1 - 0.25, z + 0.32, z + 0.325, "M_Void", skip=("bottom",))

def sign(W, u):
    """Apothecary sign hanging from one chain, the other one snapped."""
    O, ex, ey, ez, _ = W
    zb = 2.62
    wpanel(W, u - 0.13, u + 0.13, zb - 0.2, zb + 0.12, 0.0, 0.05, "M_Iron")               # wall plate
    fbox(O, ex, ey, ez, u - 0.025, u + 0.025, zb - 0.03, zb + 0.03, 0.0, 0.92, "M_Iron")  # arm
    seg(wp(W, u, zb - 0.55, 0.03), wp(W, u, zb - 0.02, 0.62), 0.035, "M_Iron")           # strut
    seg(wp(W, u, zb - 0.02, 0.36), wp(W, u, zb - 0.2, 0.36), 0.02, "M_Iron")             # chain
    seg(wp(W, u, zb - 0.02, 0.78), wp(W, u, zb - 0.1, 0.79), 0.02, "M_Iron")             # snapped stub
    t = 0.32
    a_out = ez * math.cos(t) - ey * math.sin(t); a_up = ey * math.cos(t) + ez * math.sin(t)
    P = wp(W, u, zb - 0.2, 0.36)
    fbox(P, ex, a_up, a_out, u * 0 - 0.025, 0.025, -0.52, 0.0, -0.06, 0.48, "M_Wood")
    for s in (1, -1):                                  # bottle relief on both faces
        org = P + ex * (0.025 * s)
        e1, e2 = (a_up, a_out) if s > 0 else (a_out, a_up)
        body = [(-0.32 + 0.11 * math.sin(k * math.pi / 5), 0.21 + 0.11 * math.cos(k * math.pi / 5)) for k in range(10)]
        neck = [(-0.22, 0.175), (-0.22, 0.245), (-0.1, 0.245), (-0.1, 0.175)]
        for pts in (body, neck):
            pp = pts if s > 0 else [(b, a) for a, b in pts]
            prism(org, e1, e2, ex * s, pp, 0.0, 0.018, "M_Plaster")

def joists():
    for s in (-1, 1):
        for u in np.arange(-2.1, 2.11, 0.6):
            yy0, yy1 = sorted((s * HD, s * (HDT + 0.07)))
            box(u - 0.08, u + 0.08, yy0, yy1, H0 - 0.2, H0 + 0.02, "M_Timber")

def skull(W, u, z, out):
    """Horned animal skull nailed over the door."""
    O, ex, ey, ez, _ = W
    wpanel(W, u - 0.1, u + 0.1, z + 0.02, z + 0.2, out, out + 0.16, "M_Plaster", jit=0.012)       # cranium
    lo, hi = out + 0.03, out + 0.13
    c = [(u - 0.07, z - 0.22, lo), (u + 0.07, z - 0.22, lo), (u + 0.08, z + 0.05, lo), (u - 0.08, z + 0.05, lo),
         (u - 0.045, z - 0.2, hi), (u + 0.045, z - 0.2, hi), (u + 0.07, z + 0.05, hi), (u - 0.07, z + 0.05, hi)]
    hexa([O + ex * a + ey * b + ez * d for a, b, d in c], "M_Plaster")                       # snout
    for s in (-1, 1):
        wpanel(W, u + s * 0.03 - 0.025, u + s * 0.03 + 0.025 + s * 0.02, z + 0.08, z + 0.14,
               out + 0.16, out + 0.166, "M_Void")                                               # eye sockets
        p0, p1, p2 = wp(W, u + s * 0.08, z + 0.17, out + 0.08), wp(W, u + s * 0.27, z + 0.3, out + 0.05), \
            wp(W, u + s * 0.35, z + 0.2, out + 0.02)
        seg(p0, p1, 0.045, "M_Stone"); seg(p1, p2, 0.03, "M_Stone")                              # horns
    wpanel(W, u - 0.03, u + 0.03, z - 0.2, z - 0.17, out + 0.13, out + 0.135, "M_Void")         # nostrils
    wpanel(W, u - 0.015, u + 0.015, z + 0.1, z + 0.13, out + 0.16, out + 0.2, "M_Iron")         # nail

def noose(x, y, ztop, zl):
    seg((x, y, ztop), (x + 0.02, y, zl + 0.14), 0.03, "M_Wood")
    box(x - 0.02, x + 0.06, y - 0.035, y + 0.035, zl + 0.1, zl + 0.24, "M_Wood")
    pts = [V((x + 0.02 + 0.13 * math.sin(2 * math.pi * k / 10), y, zl + 0.13 * math.cos(2 * math.pi * k / 10)))
           for k in range(11)]
    for a, b in zip(pts, pts[1:]):
        seg(a, b, 0.028, "M_Wood")

def rubble():
    for side, n in (("f", 4), ("l", 5), ("r", 4), ("b", 3)):
        W = wall_frame(side, HW, HD); half = W[4]
        k = 0
        while k < n:
            u = R(-half + 0.2, half - 0.2)
            if side == "f" and -1.9 < u < -0.1:        # keep the doorstep clear
                continue
            t = R(0, math.pi)
            c = wp(W, u, 0.0, R(0.12, 0.6))
            a = V((math.cos(t), math.sin(t), 0)); b = Z.cross(a)
            sz = R(0.1, 0.2)
            fbox(c, a, b, Z, -sz, sz, -sz * 0.7, sz * 0.7, 0.0, sz * R(0.6, 0.9), "M_Stone", skip=("bottom",), jit=0.02)
            k += 1

def build_house():
    # cores: mortar block (ground) and plaster block with the attic (one swept solid)
    box(-HW, HW, -HD, HD, 0.0, H0, "M_Mortar", skip=("bottom", "top"))
    za = ZR - 0.06
    prof = [(-HWT, H0), (HWT, H0), (HWT, ZT), (0.0, za), (-HWT, ZT)]
    loft([[V((x, -HDT + 2 * HDT * k / 8, z)) for x, z in prof] for k in range(9)], "M_Plaster")
    # ground floor
    W = wall_frame("f", HW, HD)
    ops = [Arch(-1.0, 0.55, 1.72, 0.31)]
    arch_door(W, -1.0, 0.55, 1.72)
    skull(W, -1.0, 2.46, 0.1)
    ops.append(stone_window(W, 1.25, 0.38, 1.5, 0.45, "bars"))
    stone_wall("f", ops)
    sign(W, 2.12)
    W = wall_frame("b", HW, HD)
    stone_wall("b", [stone_window(W, -0.9, 0.36, 1.5, 0.42, "boarded")])
    W = wall_frame("l", HW, HD)
    stone_wall("l", [stone_window(W, 1.55, 0.36, 1.5, 0.42, "boarded"),
                     Rect(-CHIM[3] - 0.12, -CHIM[2] + 0.12, 0, H0 + 1)])
    W = wall_frame("r", HW, HD)
    stone_wall("r", [stone_window(W, 0.0, 0.4, 1.5, 0.45, "cross")])
    shutter(W, -0.4 - 0.16 - 0.42, 1.5 + 0.45 + 0.02, 0.42, 0.94, side=-1, rot=-0.05)
    shutter(W, 0.4 + 0.16 + 0.42, 1.5 + 0.45 + 0.02, 0.42, 0.94, side=1, rot=0.5)
    rubble()
    joists()
    # timber storey: fallen plaster, damp, a lit window with someone standing in it
    Wf = timber_wall("f", [-2.46, -1.9, -0.6, 0.6, 1.9, 2.46],
                     [(-2.46, -1.9, "d", 1), (-1.9, -0.6, "win", "broken", "lit"), (-0.6, 0.6, "x"),
                      (0.6, 1.9, "win", "askew", "boarded"), (1.9, 2.46, "d", -1)])
    crumble(Wf, 0.05, H0 + 0.75, 0.42, 0.36)
    crumble(Wf, 2.18, H0 + 2.1, 0.2, 0.32, laths=False)
    Wb = timber_wall("b", [-2.46, -1.2, 1.2, 2.46],
                     [(-2.46, -1.2, "x"), (-1.2, 1.2, "win"), (1.2, 2.46, "x")])
    crumble(Wb, -1.85, H0 + 1.0, 0.4, 0.5)
    crumble(Wb, 0.4, H0 + 0.55, 0.35, 0.22, laths=False)
    Wl = timber_wall("l", [-3.36, -1.75, -0.45, 1.3, 3.36],
                     [(-3.36, -1.75, "x"), (-0.45, 1.3, "win", "broken"), (1.3, 3.36, "d", 1)])
    crumble(Wl, 2.45, H0 + 1.7, 0.5, 0.48)
    crumble(Wl, -2.6, H0 + 0.6, 0.35, 0.25, laths=False)
    Wr = timber_wall("r", [-3.36, -1.15, 1.15, 3.36],
                     [(-3.36, -1.15, "d", 1), (-1.15, 1.15, "win", None, "boarded"), (1.15, 3.36, "d", -1)])
    crumble(Wr, 2.2, H0 + 1.4, 0.55, 0.5)
    crumble(Wr, -2.0, H0 + 2.2, 0.4, 0.25, laths=False)
    gable("f", True)
    gable("b", False)
    crumble(wall_frame("f", HWT, HDT), -1.05, ZT + 0.55, 0.32, 0.26)
    crumble(wall_frame("b", HWT, HDT), 0.7, ZT + 0.9, 0.3, 0.3, laths=False)
    roof_side(1); roof_side(-1); ridge_cap()
    chimney()
    noose(-2.1, -HDT + 0.02, H0 - 0.1, 1.5)

# ---------- props (separate reusable meshes, pivot at their base centre) ----------
def ring(r, z, n=12, cx=0.0, cy=0.0):
    return [(cx + r * math.cos(2 * math.pi * k / n), cy + r * math.sin(2 * math.pi * k / n), z) for k in range(n)]

def build_barrel():
    prof = [(0.27, 0.0), (0.31, 0.12), (0.335, 0.3), (0.34, 0.45), (0.335, 0.6), (0.31, 0.78), (0.27, 0.9)]
    loft([ring(r, z) for r, z in prof], "M_Wood")
    for za, zb in ((0.1, 0.17), (0.73, 0.8)):
        loft([ring(0.318, za), ring(0.33, za + 0.01), ring(0.33, zb - 0.01), ring(0.318, zb)], "M_Iron",
             cap0=False, cap1=False)
    loft([ring(0.24, 0.9), ring(0.24, 0.93)], "M_Timber", cap0=False)

def build_crate(s=0.3):
    box(-s + 0.02, s - 0.02, -s + 0.02, s - 0.02, 0.0, 2 * s - 0.02, "M_Wood")
    b = 0.07
    for x in (-1, 1):                                  # vertical edge battens
        for y in (-1, 1):
            box(x * s - b / 2 - 0.01, x * s + b / 2 - 0.01, y * s - b / 2 - 0.006, y * s + b / 2 + 0.006,
                0.006, 2 * s - 0.006, "M_Timber")
    for z in (0.0, 2 * s - b):                         # horizontal edge battens
        for y in (-1, 1):
            box(-s, s, y * s - b / 2, y * s + b / 2, z, z + b, "M_Timber")
        for x in (-1, 1):
            box(x * s - b / 2, x * s + b / 2, -s, s, z + 0.006, z + b - 0.006, "M_Timber")
    for y in (-1, 1):                                  # diagonal brace on the long faces
        seg((-s + 0.05, y * (s + 0.01), b), (s - 0.05, y * (s + 0.01), 2 * s - b), 0.03, "M_Timber", h=0.07)

def build_crow():
    m = "M_Iron"
    hexa([(-0.13, -0.055, 0.07), (0.1, -0.06, 0.08), (0.1, 0.06, 0.08), (-0.13, 0.055, 0.07),
          (-0.13, -0.04, 0.17), (0.1, -0.05, 0.2), (0.1, 0.05, 0.2), (-0.13, 0.04, 0.17)], m)        # body
    box(0.08, 0.19, -0.042, 0.042, 0.16, 0.26, m)                                                  # head
    hexa([(0.19, -0.025, 0.19), (0.3, -0.004, 0.2), (0.3, 0.004, 0.2), (0.19, 0.025, 0.19),
          (0.19, -0.022, 0.235), (0.3, -0.003, 0.205), (0.3, 0.003, 0.205), (0.19, 0.022, 0.235)], m)  # beak
    hexa([(-0.34, -0.06, 0.05), (-0.12, -0.04, 0.09), (-0.12, 0.04, 0.09), (-0.34, 0.06, 0.05),
          (-0.34, -0.06, 0.08), (-0.12, -0.04, 0.12), (-0.12, 0.04, 0.12), (-0.34, 0.06, 0.08)], m)     # tail
    for s in (-1, 1):                                                                              # folded wings
        hexa([(-0.2, s * 0.05, 0.1), (0.06, s * 0.06, 0.11), (0.06, s * 0.075, 0.11), (-0.2, s * 0.065, 0.1),
              (-0.2, s * 0.05, 0.16), (0.06, s * 0.06, 0.19), (0.06, s * 0.075, 0.19), (-0.2, s * 0.065, 0.16)][::1]
             if s > 0 else
             [(-0.2, s * 0.065, 0.1), (0.06, s * 0.075, 0.11), (0.06, s * 0.06, 0.11), (-0.2, s * 0.05, 0.1),
              (-0.2, s * 0.065, 0.16), (0.06, s * 0.075, 0.19), (0.06, s * 0.06, 0.19), (-0.2, s * 0.05, 0.16)], m)
        seg((0.0, s * 0.03, 0.0), (0.0, s * 0.03, 0.08), 0.015, m)                                 # legs

def build_firewood():
    """Log stack against a wall (logs along Y, local X points away from the wall)."""
    rows = [(0.09, [0.12, 0.31, 0.5, 0.69]), (0.25, [0.21, 0.4, 0.59]), (0.41, [0.3, 0.5])]
    for cz, xs in rows:
        for cx in xs:
            r = R(0.075, 0.095); a0 = R(0, 1)
            pts = [(cx + r * math.cos(a0 + k * math.pi / 4), cz + r * math.sin(a0 + k * math.pi / 4)) for k in range(8)]
            prism(V((0, 0, 0)), X, Z, Y, pts, R(-0.55, -0.45), R(0.45, 0.55), "M_Wood")

def build_stump():
    """Chopping stump with an axe in it and a dark stain on top."""
    pts = [(0.26 * math.cos(k * math.pi / 5) + R(-0.02, 0.02), 0.26 * math.sin(k * math.pi / 5)) for k in range(10)]
    prism(V((0, 0, 0)), X, Y, Z, pts, 0.0, 0.45, "M_Wood")
    stain = [(0.03 + 0.13 * R(0.5, 1.0) * math.cos(k * math.pi / 6), -0.02 + 0.11 * R(0.5, 1.0) * math.sin(k * math.pi / 6))
             for k in range(12)]
    prism(V((0, 0, 0)), X, Y, Z, stain, 0.45, 0.454, "M_Void")
    box(-0.13, 0.07, -0.012, 0.012, 0.37, 0.52, "M_Iron")                         # blade, sunk in the wood
    seg((0.0, 0.0, 0.5), (0.36, 0.08, 0.86), 0.04, "M_Wood")                      # handle

def build_grass(n=34):
    """Dry grass tufts around the house: single triangles, both sides render."""
    vs, fs = acc("M_Wood")
    k = 0
    while k < n:
        x, y = R(-4.8, 4.8), R(-5.2, 5.0)
        if abs(x) < HW + 0.3 and abs(y) < HD + 0.3:
            continue
        if (-1.9 < x < 0.0 and -4.6 < y < -3.0) or (x < -2.3 and 0.4 < y < 1.8):   # doorstep, chimney
            continue
        for _ in range(rng.randint(4, 7)):
            a = R(0, 2 * math.pi); h = R(0.22, 0.5); lean = R(0.15, 0.45)
            bx, by = x + 0.06 * math.cos(a), y + 0.06 * math.sin(a)
            dx, dy = math.cos(a), math.sin(a)
            px, py = -dy * 0.02, dx * 0.02
            b0 = len(vs)
            vs.extend([V((bx - px, by - py, 0)), V((bx + px, by + py, 0)), V((bx + dx * lean * h, by + dy * lean * h, h))])
            fs.append((b0, b0 + 1, b0 + 2))
        k += 1

# =====================================================================
# materials: manga tone shader (EEVEE) + plain Principled grey (FBX / Unity)
# =====================================================================
TONES = {   # (display sRGB value, screentone dot radius in the lit band): white, 2-3 greys, black
    "M_Plaster": (0.95, 0.0), "M_Stone": (0.88, 0.0), "M_Brick": (0.7, 0.0), "M_RoofTile": (0.9, 0.32),
    "M_Wood": (0.38, 0.0), "M_Grime": (0.62, 0.38), "M_Mortar": (0.14, 0.0), "M_Timber": (0.06, 0.0),
    "M_Iron": (0.03, 0.0), "M_Void": (0.0, 0.0), "M_Glow": (0.85, 0.0), "M_Ground": (1.0, 0.0),
}
BOOST = {"M_Ground": 1.7}      # the ground reads white unless something casts a shadow on it
def lin(v):
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4

TO_LIGHT = V((-0.75, -0.5, 0.55)).normalized()    # low moonlight from front-left: long shadows
T2, T3 = 0.22, 0.5                                # lit faces by N.L*AO: hatch | dots | white
DOT_CELL, HATCH_CELL, HATCH_W = 6.0, 5.0, 1.5

def tone_group():
    g = bpy.data.node_groups.new("NG_MangaTone", "ShaderNodeTree")
    g.interface.new_socket("Base", in_out="INPUT", socket_type="NodeSocketFloat")
    g.interface.new_socket("Screen", in_out="INPUT", socket_type="NodeSocketFloat")
    g.interface.new_socket("Boost", in_out="INPUT", socket_type="NodeSocketFloat").default_value = 1.0
    g.interface.new_socket("Shader", in_out="OUTPUT", socket_type="NodeSocketShader")
    N, L = g.nodes, g.links
    gi, go = N.new("NodeGroupInput"), N.new("NodeGroupOutput")

    def link(x, sock):
        if isinstance(x, (int, float)):
            sock.default_value = x
        elif isinstance(x, tuple):
            sock.default_value = x
        else:
            L.new(x, sock)

    def M(op, a, b=None, clamp=False):
        n = N.new("ShaderNodeMath"); n.operation = op; n.use_clamp = clamp
        link(a, n.inputs[0])
        if b is not None:
            link(b, n.inputs[1])
        return n.outputs[0]

    def VM(op, a, b=None, out=0):
        n = N.new("ShaderNodeVectorMath"); n.operation = op
        link(a, n.inputs[0])
        if b is not None:
            link(b, n.inputs[1])
        return n.outputs[out]

    # manga light logic: faces turned to the sun get white/dots by N.L, faces turned away get
    # hatching (cross-hatch / black in crevices by AO), cast shadows are solid black.
    geo = N.new("ShaderNodeNewGeometry")
    ndl = VM("DOT_PRODUCT", geo.outputs["Normal"], tuple(TO_LIGHT), out=1)
    dif = N.new("ShaderNodeBsdfDiffuse"); dif.inputs["Color"].default_value = (1, 1, 1, 1)
    s2r = N.new("ShaderNodeShaderToRGB"); L.new(dif.outputs[0], s2r.inputs[0])
    bw = N.new("ShaderNodeRGBToBW"); L.new(s2r.outputs["Color"], bw.inputs[0])   # sun only, with shadows
    ao = N.new("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.9
    aoo = ao.outputs["AO"]
    s = M("MULTIPLY", M("MULTIPLY", ndl, M("ADD", M("MULTIPLY", aoo, 0.55), 0.45)), gi.outputs["Boost"])
    facing = M("GREATER_THAN", ndl, 0.2)   # grazing faces count as turned away (no patchy roofs)
    lit = M("MULTIPLY", facing, M("GREATER_THAN", M("SUBTRACT", bw.outputs[0], M("MULTIPLY", ndl, 0.43)), 0.0))
    form = M("SUBTRACT", 1.0, facing)
    cast = M("SUBTRACT", M("SUBTRACT", 1.0, lit), form, clamp=True)

    # screen-space pixel coordinates
    tc = N.new("ShaderNodeTexCoord")
    P = VM("MULTIPLY", tc.outputs["Window"], (RX, RY, 0.0))

    def rotated(angle):
        r = N.new("ShaderNodeVectorRotate"); r.rotation_type = "Z_AXIS"
        r.inputs["Angle"].default_value = math.radians(angle)
        L.new(P, r.inputs["Vector"])
        return r.outputs[0]

    # screentone: 45 deg dot grid, dots grow as the light drops
    q = VM("FRACTION", VM("MULTIPLY", rotated(45), (1 / DOT_CELL, 1 / DOT_CELL, 0)))
    d = VM("LENGTH", VM("SUBTRACT", q, (0.5, 0.5, 0.0)), out=1)
    mr = N.new("ShaderNodeMapRange"); mr.clamp = True
    L.new(s, mr.inputs["Value"])
    mr.inputs["From Min"].default_value = T2; mr.inputs["From Max"].default_value = T3
    mr.inputs["To Min"].default_value = 0.5; mr.inputs["To Max"].default_value = 0.17
    dots = M("ADD", M("MULTIPLY", M("SUBTRACT", mr.outputs[0], d), DOT_CELL), 0.5, clamp=True)
    # printed tone sheet (fixed dot size) for materials that read as grey even in full light
    sheet = M("MULTIPLY", M("ADD", M("MULTIPLY", M("SUBTRACT", gi.outputs["Screen"], d), DOT_CELL), 0.5, clamp=True),
              M("GREATER_THAN", gi.outputs["Screen"], 0.01))
    dots = M("MAXIMUM", dots, sheet)

    def hatch(angle):
        sep = N.new("ShaderNodeSeparateXYZ"); L.new(rotated(angle), sep.inputs[0])
        f = M("FRACT", M("DIVIDE", sep.outputs[0], HATCH_CELL))
        px = M("MULTIPLY", M("ABSOLUTE", M("SUBTRACT", f, 0.5)), HATCH_CELL)
        return M("SUBTRACT", HATCH_W / 2 + 0.5, px, clamp=True)

    h1 = hatch(-35)
    hx = M("MAXIMUM", h1, hatch(55))
    g2, g3 = M("GREATER_THAN", s, T2), M("GREATER_THAN", s, T3)
    ink_lit = M("ADD", M("MULTIPLY", M("SUBTRACT", 1.0, g2), h1),
                M("ADD", M("MULTIPLY", M("SUBTRACT", g2, g3), dots), M("MULTIPLY", g3, sheet)))
    a1, a2 = M("LESS_THAN", aoo, 0.62), M("LESS_THAN", aoo, 0.33)
    ink_form = M("MAXIMUM", M("MAXIMUM", h1, sheet), M("MAXIMUM", M("MULTIPLY", a1, hx), a2))
    ink = M("ADD", M("ADD", M("MULTIPLY", lit, ink_lit), M("MULTIPLY", form, ink_form)), cast)
    tone = M("MULTIPLY", gi.outputs["Base"], M("SUBTRACT", 1.0, ink, clamp=True))
    cc = N.new("ShaderNodeCombineColor")
    for i in range(3):
        L.new(tone, cc.inputs[i])
    em = N.new("ShaderNodeEmission"); L.new(cc.outputs[0], em.inputs["Color"])
    L.new(em.outputs[0], go.inputs["Shader"])
    return g

def make_materials():
    g = tone_group()
    mats = {}
    for name, (v, screen) in TONES.items():
        m = bpy.data.materials.new(name); m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        lv = lin(v)
        m.diffuse_color = (lv, lv, lv, 1.0)
        out_e = nt.nodes.new("ShaderNodeOutputMaterial"); out_e.target = "EEVEE"; out_e.location = (300, 100)
        grp = nt.nodes.new("ShaderNodeGroup"); grp.node_tree = g; grp.inputs["Base"].default_value = lv
        grp.inputs["Screen"].default_value = screen
        grp.inputs["Boost"].default_value = BOOST.get(name, 1.0)
        if name == "M_Glow":                       # dim window light: flat, no tone shading
            em = nt.nodes.new("ShaderNodeEmission"); em.inputs["Color"].default_value = (lv, lv, lv, 1.0)
            nt.links.new(em.outputs[0], out_e.inputs["Surface"])
        else:
            nt.links.new(grp.outputs[0], out_e.inputs["Surface"])
        out_c = nt.nodes.new("ShaderNodeOutputMaterial"); out_c.target = "CYCLES"; out_c.location = (300, -200)
        pb = nt.nodes.new("ShaderNodeBsdfPrincipled"); pb.location = (0, -200)
        pb.inputs["Base Color"].default_value = (lv, lv, lv, 1.0); pb.inputs["Roughness"].default_value = 0.9
        nt.links.new(pb.outputs[0], out_c.inputs["Surface"])
        out_e.is_active_output = True
        mats[name] = m
    return mats

# =====================================================================
# mesh output
# =====================================================================
def collection(name):
    c = bpy.data.collections.new(name); SC.collection.children.link(c)
    return c

def emit(name, coll, mats, deform=None, loc=(0, 0, 0)):
    global GEO
    verts, faces, mids, order = [], [], [], [m for m in TONES if m in GEO]
    for mi, m in enumerate(order):
        vs, fs = GEO[m]; b = len(verts)
        verts.extend(vs); faces.extend(tuple(b + i for i in f) for f in fs); mids.extend([mi] * len(fs))
    if deform:
        verts = [deform(v) for v in verts]
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.validate(clean_customdata=False)
    for m in order:
        me.materials.append(mats[m])
    me.polygons.foreach_set("material_index", mids)
    # box-projected UVs (1 UV unit = 1 m) so textures / lightmap UVs can be added in Unity
    uv = me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        nx, ny, nz = (abs(c) for c in p.normal)
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (co.x, co.y) if nz >= max(nx, ny) else ((co.y, co.z) if nx >= ny else (co.x, co.z))
    me.update()
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob); ob.location = loc
    GEO = {}
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print(f"[mesh] {name}: {len(me.vertices)} verts, {len(me.polygons)} faces, {tris} tris")
    return ob

mats = make_materials()
C_HOUSES, C_PROPS, C_NATURE, C_LIGHTS, C_PREVIEW = (collection(n) for n in
    ("COL_Houses", "COL_Props", "COL_Nature", "COL_Lights", "COL_Preview"))

build_house()
house = emit("SM_House_01", C_HOUSES, mats, deform=warp)
build_barrel()
barrel = emit("SM_Barrel_01", C_PROPS, mats, loc=(1.6, -3.55, 0))
build_crate()
crate = emit("SM_Crate_01", C_PROPS, mats, loc=(-2.05, -3.45, 0))
build_crate(0.22)
crate2 = emit("SM_Crate_02", C_PROPS, mats, loc=(-2.05, -3.45, 0.6))
bb = [house.matrix_world @ V(c) for c in house.bound_box]
print("[bbox] house min", tuple(round(min(v[i] for v in bb), 2) for i in range(3)),
      "max", tuple(round(max(v[i] for v in bb), 2) for i in range(3)))

# props mesh data is local; nudge the small crate off-axis for a lived-in look (still unrotated objects)
crate2.location = (-1.95, -3.5, 0.6)
build_firewood()
emit("SM_Firewood_01", C_PROPS, mats, loc=(HW + 0.04, 1.55, 0))
build_stump()
emit("SM_Stump_01", C_PROPS, mats, loc=(3.1, -2.7, 0))
build_grass()
emit("SM_GrassTufts_01", C_NATURE, mats)
build_crow()
crow = emit("SM_Crow_01", C_PROPS, mats)
ztop = ZR + ROOF_T / CA + 0.02 + 0.2
for k, (p, rz) in enumerate([((0.0, -1.7, ztop), -1.4), ((0.0, 2.2, ztop), 2.6),
                             ((2.12, -3.67, 2.65), -1.9)]):
    ob = crow if k == 0 else bpy.data.objects.new(f"SM_Crow_{k + 1:02d}", crow.data)
    if k:
        C_PROPS.objects.link(ob)
    ob.location = warp(V(p)); ob.rotation_euler.z = rz

# ground (preview only)
GEO = {}
box(-150, 150, -150, 150, -0.2, 0.0, "M_Ground", skip=("bottom",))
emit("SM_PreviewGround", C_PREVIEW, mats)

# =====================================================================
# light, world, render settings
# =====================================================================
sun = bpy.data.lights.new("LGT_Sun", "SUN")
sun.energy = math.pi * 0.95                      # white diffuse facing the sun -> 0.95
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
fs = SC.view_layers[0].freestyle_settings
fs.crease_angle = math.radians(130)
for ls in list(fs.linesets):
    fs.linesets.remove(ls)

def ink_style(name, thick, wobble):
    st = bpy.data.linestyles.new(name)
    st.color = (0, 0, 0); st.thickness = thick; st.thickness_position = "CENTER"
    st.caps = "ROUND"
    sm = st.geometry_modifiers.new("Sampling", "SAMPLING"); sm.sampling = 2.0
    pn = st.geometry_modifiers.new("Wobble", "PERLIN_NOISE_1D")
    pn.frequency = 12.0; pn.amplitude = wobble; pn.octaves = 2
    tn = st.thickness_modifiers.new("Pressure", "NOISE")
    tn.amplitude = thick * 0.35; tn.period = 35.0; tn.blend = "SUBTRACT"
    return st

ls = fs.linesets.new("LS_Contour")
ls.select_by_visibility = True; ls.visibility = "VISIBLE"; ls.select_by_edge_types = True
ls.select_silhouette = ls.select_border = ls.select_external_contour = True
ls.select_crease = False
ls.linestyle = ink_style("INK_Contour", 4.6, 1.3)
ls2 = fs.linesets.new("LS_Inner")
ls2.select_by_visibility = True; ls2.visibility = "VISIBLE"; ls2.select_by_edge_types = True
ls2.select_silhouette = ls2.select_border = ls2.select_external_contour = False
ls2.select_crease = True
ls2.linestyle = ink_style("INK_Inner", 2.0, 0.8)
for l in (ls, ls2):                      # no ink on the preview ground (no horizon line through the house)
    l.select_by_collection = True; l.collection = C_PREVIEW; l.collection_negation = "EXCLUSIVE"

# cameras (preview)
VIEWS = {
    "front": ((0.3, -25.0, 3.6), (0.2, 0, 4.7)),
    "three_quarter": ((-15.5, -19.0, 6.5), (0.0, 0.0, 4.4)),
    "side": ((-25.0, -0.6, 3.6), (0, 0, 4.7)),
    "back": ((15.5, 19.0, 6.5), (0.0, 0.0, 4.4)),
}
cams = {}
for key, (pos, tgt) in VIEWS.items():
    cd = bpy.data.cameras.new(f"CAM_{key}"); cd.lens = 50; cd.clip_end = 1000
    co = bpy.data.objects.new(f"CAM_{key}", cd); C_PREVIEW.objects.link(co)
    co.location = pos
    co.rotation_euler = (V(tgt) - V(pos)).to_track_quat("-Z", "Y").to_euler()
    cams[key] = co
SC.camera = cams["three_quarter"]

# =====================================================================
# save, export, render
# =====================================================================
os.makedirs(RDIR, exist_ok=True)
blend = os.path.join(OUT, "house_01.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
print("[saved]", blend)

for o in bpy.context.view_layer.objects:
    o.select_set(any(c in (C_HOUSES, C_PROPS, C_NATURE) for c in o.users_collection))
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "house_01.fbx"), use_selection=True,
                         object_types={"MESH"}, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False,
                         use_triangles=True)
print("[saved]", os.path.join(OUT, "house_01.fbx"))

PAPER = 1.0
def night_sky(h, w):
    """Screentone night sky (darker to the top), a white moon with an ink ring, cloud streaks."""
    srng = random.Random(5)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)        # row 0 = bottom
    c = math.sqrt(0.5)
    qx, qy = (xx * c - yy * c) / DOT_CELL, (xx * c + yy * c) / DOT_CELL
    d = np.hypot(qx - np.floor(qx) - 0.5, qy - np.floor(qy) - 0.5)
    r = 0.12 + 0.34 * np.clip((yy / h - 0.4) / 0.6, 0, 1)
    v = 1 - np.clip((r - d) * DOT_CELL + 0.5, 0, 1)
    mx, my, mr = 0.15 * w, 0.82 * h, 0.07 * h
    dm = np.hypot(xx - mx, yy - my)
    v[dm < mr] = 1.0
    v[(dm >= mr) & (dm < mr + 3.5)] = 0.0
    clouds = [(my - 0.25 * mr, mx + 0.06 * w, 0.26 * w, 11.0)] + \
        [(hy * h, srng.uniform(0.0, 1.0) * w, srng.uniform(0.14, 0.34) * w, srng.uniform(7, 16))
         for hy in (0.62, 0.71, 0.9, 0.95)]
    for cy, cx, L, th in clouds:
        wob = 3.0 * np.sin(xx / srng.uniform(30, 70) + srng.uniform(0, 6))
        e = ((xx - cx) / (L / 2)) ** 2 + ((yy - cy - wob) / th) ** 2
        e2 = ((xx - cx) / (L / 2 + 3)) ** 2 + ((yy - cy - wob) / (th + 2.5)) ** 2
        v[e < 1] = 1.0
        v[(e2 < 1) & (e >= 1) & (yy < cy + wob)] = 0.0       # ink under the cloud
    return v

def finish(path):
    im = bpy.data.images.load(path, check_existing=False)
    w, h = im.size
    a = np.empty(w * h * 4, np.float32); im.pixels.foreach_get(a); a = a.reshape(h, w, 4)
    al = a[..., 3:4]
    sky = night_sky(h, w)[..., None]
    rgb = a[..., :3] * al + sky * (1 - al)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    fog_top = 0.2 * h + 10 * np.sin(xx / 95.0) + 6 * np.sin(xx / 37.0)
    fog = (0.85 * np.clip((fog_top - yy) / fog_top, 0, 1) ** 1.3)[..., None]   # ground fog
    rgb = rgb * (1 - fog) + fog
    a[..., :3] = rgb; a[..., 3] = 1.0
    im.pixels.foreach_set(a.ravel()); im.filepath_raw = path; im.file_format = "PNG"; im.save()
    chroma = float(np.max(np.abs(rgb - rgb.mean(-1, keepdims=True))))
    lum = rgb.mean(-1)
    print(f"[check] {os.path.basename(path)}: max chroma {chroma:.4f}, black {np.mean(lum < 0.1):.2f}, "
          f"white {np.mean(lum > 0.9):.2f}, mid {np.mean((lum >= 0.1) & (lum <= 0.9)):.2f}")
    return a

if not NORENDER:
    shots = {}
    for key, cam in cams.items():
        SC.camera = cam
        path = os.path.join(RDIR, f"house_01_{key}.png")
        SC.render.filepath = path
        bpy.ops.render.render(write_still=True)
        shots[key] = finish(path)
    # 2x2 contact sheet at half size: front | three_quarter / side | back (rows stored bottom-up)
    hh, ww = RY // 2, RX // 2
    sheet = np.ones((RY, RX, 4), np.float32)
    def half(a):
        return a.reshape(hh, 2, ww, 2, 4).mean(axis=(1, 3))
    sheet[hh:, :ww] = half(shots["front"]); sheet[hh:, ww:] = half(shots["three_quarter"])
    sheet[:hh, :ww] = half(shots["side"]); sheet[:hh, ww:] = half(shots["back"])
    im = bpy.data.images.new("house_01_sheet", RX, RY)
    im.pixels.foreach_set(sheet.ravel())
    im.filepath_raw = os.path.join(RDIR, "house_01_sheet.png"); im.file_format = "PNG"; im.save()
    print("[saved] renders/house_01_*.png")
