"""Calabash characters in the manga-ink style of the village kit: faceless black figures with
pin-point white eyes, long hunched bodies, colour only for fire (orange), water (blue) and blood (red).
Source: docs/characters (style bible, protagonist, NPC roster, endings, the fire).

Run:  blender -b --factory-startup -P characters/scripts/build_characters.py -- [out dir] [--norender]
          [--only=zhdan0,shadow,...] [--corridor]
  default out dir: the folder above scripts/ (characters/)
  * default run: builds every character, saves characters.blend, fbx/SM_Char_<id>.fbx and renders a
    transparent 3/4 ortho cut-out per character to renders/raw/<id>.png (same scale for all)
  * --corridor: builds a dark stone passage with the Shadow standing in the far doorway (low camera,
    light from outside behind it) -> renders/raw/corridor.png
Then run compose_sheets.py (system python3 with Pillow) for the cards, the line-up and the 64 px test.

Asset rules: metres, front faces -Y, pivot at the feet, no object transforms, flat-shaded faces,
box UVs. Each character = SM_Char_<id> (inked) + SM_Char_<id>_Glow (eyes / embers, no ink lines).
These are static concept meshes (no rig): pose and silhouette reference for the 2D sprites.
"""
import bpy, math, random, sys, os, zlib
from mathutils import Vector as V

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = next((a for a in argv if not a.startswith("--")), os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
NORENDER = "--norender" in argv
CORRIDOR = "--corridor" in argv
ONLY = next((a.split("=", 1)[1].split(",") for a in argv if a.startswith("--only=")), None)
RAW = os.path.join(OUT, "renders", "raw")
RX, RY = (1600, 1200) if CORRIDOR else (1000, 1500)

bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene
SC.unit_settings.system = "METRIC"
rng = random.Random(7)
def R(a, b):
    return rng.uniform(a, b)

X, Y, Z = V((1, 0, 0)), V((0, 1, 0)), V((0, 0, 1))

# =====================================================================
# geometry accumulator: material -> (verts, faces). GLOW = no ink lines (eyes, embers)
# =====================================================================
GEO, GLOW = {}, {}
CUR = [GEO]
def acc(mat):
    return CUR[0].setdefault(mat, ([], []))

class glow_layer:
    def __enter__(self):
        self.prev = CUR[0]; CUR[0] = GLOW
    def __exit__(self, *a):
        CUR[0] = self.prev

FACES6 = {"bottom": (0, 3, 2, 1), "top": (4, 5, 6, 7), "front": (0, 1, 5, 4),
          "right": (1, 2, 6, 5), "back": (2, 3, 7, 6), "left": (3, 0, 4, 7)}

def hexa(c, mat, skip=()):
    vs, fs = acc(mat); b = len(vs)
    vs.extend(V(p) for p in c)
    for k, f in FACES6.items():
        if k not in skip:
            fs.append(tuple(b + i for i in f))

def fbox(O, ex, ey, ez, x0, x1, y0, y1, z0, z1, mat, skip=()):
    c = []
    for z in (z0, z1):
        for x, y in ((x0, y0), (x1, y0), (x1, y1), (x0, y1)):
            c.append(O + ex * x + ey * y + ez * z)
    hexa(c, mat, skip)

def box(x0, x1, y0, y1, z0, z1, mat, skip=()):
    fbox(V((0, 0, 0)), X, Y, Z, x0, x1, y0, y1, z0, z1, mat, skip)

def seg(p0, p1, w, mat, h=None, up=Z):
    p0, p1 = V(p0), V(p1)
    ex = p1 - p0; L = ex.length
    if L < 1e-5:
        return
    ex.normalize()
    ey = up.cross(ex)
    if ey.length < 1e-3:
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

def sheet(rows, mat):
    """Open strip surface through polylines of equal length (capes, aprons)."""
    vs, fs = acc(mat); b = len(vs); n = len(rows[0])
    for r in rows:
        vs.extend(V(p) for p in r)
    for k in range(len(rows) - 1):
        for i in range(n - 1):
            a0 = b + k * n + i
            fs.append((a0, a0 + 1, a0 + 1 + n, a0 + n))

def prism(O, ex, ey, ez, pts, z0, z1, mat):
    loft([[O + ex * x + ey * y + ez * z for x, y in pts] for z in (z0, z1)], mat)

def ngon(pts, mat):
    vs, fs = acc(mat); b = len(vs)
    vs.extend(V(p) for p in pts)
    fs.append(tuple(range(b, b + len(pts))))

def perp(t, ref=X):
    u = ref - t * ref.dot(t)
    if u.length < 1e-3:
        u = Y - t * Y.dot(t)
    return u.normalized()

def tube(pts, rad, mat, n=8, caps=(True, True), ref=X, jit=0.0, twist=0.0):
    """Sweep rings along a polyline; rad items: r or (r_along_ref, r_other)."""
    pts = [V(p) for p in pts]; secs = []; u = None
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        u = perp(t, ref if u is None else u)
        v = t.cross(u)
        r = rad[i]; rx, ry = (r, r) if not isinstance(r, tuple) else r
        ring = []
        for k in range(n):
            a = twist + 2 * math.pi * k / n
            j = 1 + R(-jit, jit) if jit else 1
            ring.append(p + u * (rx * math.cos(a) * j) + v * (ry * math.sin(a) * j))
        secs.append(ring)
    loft(secs, mat, *caps)

def blob(c, r, mat, n=8, k=5, sq=(1, 1, 1), jit=0.0):
    c = V(c); pts, rad = [], []
    for i in range(k + 1):
        ph = math.pi * (0.06 + 0.88 * i / k)
        pts.append(c + V((0, 0, -r * sq[2] * math.cos(ph))))
        rad.append((r * sq[0] * math.sin(ph), r * sq[1] * math.sin(ph)))
    tube(pts, rad, mat, n=n, jit=jit)

def ring_pts(c, rx, ry, n, z=None, a0=0.0):
    c = V(c)
    return [c + V((rx * math.cos(a0 + 2 * math.pi * k / n), ry * math.sin(a0 + 2 * math.pi * k / n), 0)) for k in range(n)]

def cone(base, axis, r, L, mat, n=4):
    """Pointed spike (crown points, hair, tines)."""
    axis = V(axis).normalized(); u = perp(axis, X); v = axis.cross(u)
    vs, fs = acc(mat); b = len(vs); a = R(0, 1)
    for k in range(n):
        t = a + 2 * math.pi * k / n
        vs.append(V(base) + u * r * math.cos(t) + v * r * math.sin(t))
    vs.append(V(base) + axis * L)
    fs.append(tuple(b + k for k in reversed(range(n))))
    for k in range(n):
        fs.append((b + k, b + (k + 1) % n, b + n))

def lerp(a, b, t):
    return V(a) + (V(b) - V(a)) * t

def skirt(levels, mat, n=14, jag=0.0, cap_top=True, seed=None):
    """levels: [(centre, (rx, ry)), ...] top to bottom. jag > 0 gives a torn zig-zag hem."""
    rings = []
    for li, (c, r) in enumerate(levels):
        c = V(c); ring = []
        last = li == len(levels) - 1
        for k in range(n):
            a = 2 * math.pi * k / n
            rr = 1 + (R(-0.06, 0.06) if last and jag else 0)
            z = c.z + ((jag * (1.0 if k % 2 else 0.15) * R(0.4, 1.0)) if last and jag else 0)
            ring.append(V((c.x + r[0] * math.cos(a) * rr, c.y + r[1] * math.sin(a) * rr, z)))
        rings.append(ring)
    loft(rings, mat, cap_top, False)

def splat(c, normal, size, mat):
    """Flat blood splash glued to a surface."""
    normal = V(normal).normalized(); u = perp(normal, Z); v = normal.cross(u)
    n = 13; pts = []; r = size * 0.8
    for k in range(n):
        a = 2 * math.pi * k / n
        r = 0.6 * r + 0.4 * size * R(0.45, 1.15)
        pts.append((r * math.cos(a) * 1.15, r * math.sin(a) * 0.8))
    prism(V(c), u, v, normal, pts, 0.0, 0.004, mat)
    down = -Z - normal * (-Z).dot(normal)
    down = down.normalized() if down.length > 1e-3 else -v
    side = down.cross(normal).normalized()
    for _ in range(rng.randint(1, 3)):                           # drips running down the leather
        x0 = R(-0.6, 0.6) * size; L = size * R(1.2, 3.6); w = size * R(0.08, 0.15)
        prism(V(c) + normal * 0.001, side, -down, normal,
              [(x0 - w, 0.2 * size), (x0 + w, 0.2 * size), (x0 + w * 0.6, -L), (x0 + w * 1.1, -L - w * 1.4),
               (x0, -L - w * 2.4), (x0 - w * 1.1, -L - w * 1.4), (x0 - w * 0.6, -L)], 0.0, 0.004, mat)

def flame(base, up, h, w, mat="M_Ember", n=5):
    """Ragged fire tongue (manga: sharp, not soft). Goes to the glow layer."""
    up = V(up).normalized(); side = perp(up, X); bend = R(-0.35, 0.35)
    pts = [V(base) + up * h * t + side * bend * h * t * t for t in (0, 0.3, 0.6, 0.85, 1.0)]
    with glow_layer():
        tube(pts, [w, w * 0.85, w * 0.55, w * 0.28, w * 0.04], mat, n=n, jit=0.2)

def cracks(a, b, ra, rb, count, w=0.007, mat="M_Ember", spread=0.7, f0=0.0, f1=1.0):
    """Glowing fissures wandering over a limb between axis points a -> b."""
    a, b = V(a), V(b); t = (b - a).normalized(); u = perp(t, X); v = t.cross(u)
    with glow_layer():
        for _ in range(count):
            th = R(0, 2 * math.pi); f = R(f0, f1 - 0.2); L = R(0.2, 0.55)
            pts = []
            for k in range(6):
                ff = min(f1, f + L * k / 5); th += R(-spread, spread)
                r = (ra + (rb - ra) * ff) * 1.06
                pts.append(a + (b - a) * ff + (u * math.cos(th) + v * math.sin(th)) * r)
            for k in range(5):
                seg(pts[k], pts[k + 1], w, mat, h=w * 0.7)
            if R(0, 1) < 0.5:                                  # side branch
                k = rng.randrange(1, 5); th2 = th + R(-1.2, 1.2)
                q = a + (b - a) * min(f1, f + L * k / 5 + 0.08) + (u * math.cos(th2) + v * math.sin(th2)) * ra * 1.06
                seg(pts[k], q, w * 0.7, mat, h=w * 0.5)

def smoke(c, n, size, rise, mat="M_Ink"):
    """Fat black manga smoke: lumpy clumps climbing and drifting back."""
    c = V(c)
    for i in range(n):
        f = i / max(n - 1, 1)
        sz = size * (0.7 + 1.1 * f)
        p = c + V((0.12 * math.sin(f * 5.0) + R(-0.04, 0.04), 0.06 + 0.3 * f, rise * f))
        blob(p, sz * R(0.85, 1.1), mat, n=8, k=4, sq=(1.1, 0.85, 0.75), jit=0.22)
        if f > 0.3:
            blob(p + V((R(-1, 1) * sz * 0.7, R(-0.3, 0.3) * sz, R(-0.2, 0.4) * sz)), sz * R(0.5, 0.75), mat, n=7, k=3,
                 jit=0.25)

# =====================================================================
# skeleton + body
# =====================================================================
DEF = dict(leg=0.88, crouch=0.0, torso=0.52, hunch=0.05, lean_x=0.0, sh_w=0.2, sh_fwd=0.0, sh_drop=0.03,
           hips=(0.15, 0.1), waist=(0.14, 0.09), chest=(0.17, 0.11), hip_w=0.15, foot_sep=0.11, step=0.0,
           neck=0.09, neck_r=0.045, head_r=0.09, head_d=1.1, head_h=0.23, head_fwd=0.15, head_side=0.0,
           upper=0.3, fore=0.27, arm_r=0.042, thigh_r=0.07, calf_r=0.05, foot_w=0.045, foot_l=0.17,
           hand=0.1, finger=0.08, palm_w=0.075, finger_r=0.009, arm_out=0.07,
           hands=None, poles=None, hdir=None, curl=None, nohands=(), eyes=True, eye_r=0.011, eye_gap=0.032,
           eye_mat="M_Glow", eye_z=0.0, head=True, m=None)
MDEF = dict(torso="M_Dark", upper="M_Dark", fore="M_Dark", hand="M_Black", thigh="M_Black", shin="M_Black",
            foot="M_Ink", head="M_Ink", neck="M_Ink")

def side_mat(v, s):
    return v[0 if s < 0 else 1] if isinstance(v, tuple) else v

def ik(S, W, a, b, pole):
    S, W, pole = V(S), V(W), V(pole)
    d = W - S; L = max(min(d.length, (a + b) * 0.995), abs(a - b) + 1e-3)
    dn = d.normalized()
    ca = max(-1.0, min(1.0, (a * a + L * L - b * b) / (2 * a * L))); sa = math.sqrt(1 - ca * ca)
    p = perp(dn, pole.normalized())
    return S + dn * a * ca + p * a * sa, S + dn * L

def skeleton(**kw):
    P = dict(DEF); P.update(kw)
    m = dict(MDEF); m.update(kw.get("m") or {}); P["m"] = m
    J = {}
    hz = P["leg"] - P["crouch"]
    spine = []
    for i in range(5):
        f = i / 4
        spine.append(V((P["lean_x"] * f, -P["hunch"] * f * f, hz + P["torso"] * f)))
    J["spine"] = spine; top = spine[-1]
    ln = (P["leg"] - 0.085) / 2
    for s in (-1, 1):
        J["sh", s] = top + V((s * P["sh_w"], -P["hunch"] * 0.25 + P["sh_fwd"], -P["sh_drop"]))
        hip = V((s * P["hip_w"] * 0.55, 0, hz))
        ank = V((s * P["foot_sep"], -s * P["step"], 0.085))
        knee, ank = ik(hip, ank, ln, ln, (0, -1, 0))
        J["hip", s], J["knee", s], J["ank", s] = hip, knee, ank
        ua, fa = P["upper"], P["fore"]
        W = (P["hands"] or {}).get(s)
        if W is None:
            W = J["sh", s] + V((s * P["arm_out"], -0.03, -(ua + fa) * 0.97))
        pole = (P["poles"] or {}).get(s, (s * 0.3, 1.0, 0.0))
        E, Wc = ik(J["sh", s], W, ua, fa, pole)
        J["elb", s], J["wr", s] = E, Wc
        hd = (P["hdir"] or {}).get(s)
        J["hd", s] = V(hd).normalized() if hd is not None else (Wc - E).normalized()
    J["neck_b"] = top + V((0, -P["hunch"] * 0.25 - 0.01, -0.02))
    a = V((P["head_side"], -P["head_fwd"], 1)).normalized()
    J["head_a"] = a
    J["head_b"] = J["neck_b"] + a * P["neck"]
    J["head_c"] = J["head_b"] + a * P["head_h"] * 0.5
    J["head_t"] = J["head_b"] + a * P["head_h"]
    J["fwd"] = X.cross(a).normalized()
    return P, J

def eyes(J, P, c=None, rx=None, ry=None, mat=None, r=None, gap=None, dz=None):
    c = J["head_c"] if c is None else c
    rx = P["head_r"] if rx is None else rx
    ry = P["head_r"] * P["head_d"] if ry is None else ry
    r = P["eye_r"] if r is None else r
    gap = P["eye_gap"] if gap is None else gap
    dz = P["eye_z"] if dz is None else dz
    a, f = J["head_a"], J["fwd"]
    with glow_layer():
        for s in (-1, 1):
            x = s * gap
            p = c + X * x + a * dz + f * (ry * math.sqrt(max(0.05, 1 - (x / rx) ** 2)) + 0.002)
            ex = X; ey = f; ez = ex.cross(ey)
            fbox(p, ex, ey, ez, -r, r, 0.0, r * 0.6, -r * 0.8, r * 0.8, mat or P["eye_mat"])

def face_patch(J, c, rx, ry, wx, wz, mat="M_Void", n=12, dz=0.0):
    """Dark face opening following the front curve of a hood / head."""
    a, f = J["head_a"], J["fwd"]; pts = []
    for k in range(n):
        t = 2 * math.pi * k / n
        x, z = wx * math.cos(t), wz * math.sin(t)
        pts.append(c + X * x + a * (z + dz) + f * (ry * math.sqrt(max(0.05, 1 - (x / rx) ** 2)) + 0.004))
    ngon(pts, mat)

def hand(J, P, s, curl=None, mat=None):
    Wr, hd = J["wr", s], J["hd", s]
    curl = (P["curl"] or {}).get(s, 0.35) if curl is None else curl
    mat = mat or side_mat(P["m"]["hand"], s)
    across = perp(hd, Y); pn = hd.cross(across)
    if pn.dot(V((-s, 0, 0))) < 0:
        across = -across; pn = -pn
    pl, w = P["hand"] * 0.45, P["palm_w"]; t = w * 0.32
    fbox(Wr - hd * 0.01, across, pn, hd, -w / 2, w / 2, -t / 2, t / 2, 0, pl, mat)
    fl, fr = P["finger"], P["finger_r"]
    for i, k in enumerate((0.85, 1.0, 0.95, 0.78)):
        b = Wr + hd * pl + across * (-w / 2 + w * (i + 0.5) / 4) * 0.9
        dd = (hd + across * (i - 1.5) * 0.12).normalized(); L = fl * k
        mid = b + dd * L * 0.5 + pn * L * 0.12 * curl
        tip = mid + (dd * (1 - curl) + pn * curl * 1.2).normalized() * L * 0.5
        tube([b, mid, tip], [fr, fr * 0.85, fr * 0.45], mat, n=4)
    ts = -1 if across.y > 0 else 1
    b = Wr + hd * pl * 0.3 + across * ts * w * 0.5
    d = (hd * 0.5 + across * ts * 0.6 + pn * 0.35).normalized()
    tube([b, b + d * fl * 0.35, b + (d + pn * 0.6 * curl).normalized() * fl * 0.62], [fr * 1.1, fr, fr * 0.5], mat, n=4)

def body(P, J, legs=True, arms=True, torso=True, head=True):
    m = P["m"]
    sp = J["spine"]
    if legs:
        for s in (-1, 1):
            hip, knee, ank = J["hip", s], J["knee", s], J["ank", s]
            tube([hip, lerp(hip, knee, 0.5), knee], [P["thigh_r"], P["thigh_r"] * 0.85, P["calf_r"] * 1.12],
                 side_mat(m["thigh"], s))
            tube([knee, lerp(knee, ank, 0.4), ank], [P["calf_r"] * 1.12, P["calf_r"], P["calf_r"] * 0.62],
                 side_mat(m["shin"], s))
            toe = V((s * math.sin(math.radians(10)), -math.cos(math.radians(10)), 0))
            tube([ank + V((0, 0.035, -0.03)), ank + toe * P["foot_l"] * 0.5 + V((0, 0, -0.045)),
                  ank + toe * P["foot_l"] + V((0, 0, -0.06))],
                 [(P["foot_w"], 0.04), (P["foot_w"], 0.032), (P["foot_w"] * 0.8, 0.022)], side_mat(m["foot"], s), n=6)
    if torso:
        hx, wx, cx = P["hips"], P["waist"], P["chest"]
        rad = [hx, tuple(lerp(V((*hx, 0)), V((*wx, 0)), 0.6)[:2]), wx, cx, (P["sh_w"] * 0.8, cx[1] * 0.85)]
        tube(sp, rad, m["torso"], n=10)
        L, Rr = J["sh", 1], J["sh", -1]
        tube([Rr, sp[-1] + V((0, 0, -P["sh_drop"])), L], [(cx[1] * 0.72, P["arm_r"] * 1.25)] * 3, m["torso"], n=8)
    if arms:
        for s in (-1, 1):
            S, E, Wr = J["sh", s], J["elb", s], J["wr", s]
            blob(S, P["arm_r"] * 1.35, side_mat(m["upper"], s), n=8, k=4)
            tube([S, lerp(S, E, 0.5), E], [P["arm_r"] * 1.15, P["arm_r"], P["arm_r"] * 0.85], side_mat(m["upper"], s))
            tube([E, lerp(E, Wr, 0.5), Wr], [P["arm_r"] * 0.85, P["arm_r"] * 0.78, P["arm_r"] * 0.6], side_mat(m["fore"], s))
            if s not in P["nohands"]:
                hand(J, P, s)
    if head:
        tube([J["neck_b"] - V((0, 0, 0.03)), J["head_b"] + J["head_a"] * 0.04], [P["neck_r"], P["neck_r"] * 0.9],
             m["neck"], n=8)
        if P["head"]:
            hb, a, hh, hr, hd = J["head_b"], J["head_a"], P["head_h"], P["head_r"], P["head_r"] * P["head_d"]
            prof = [(0.0, 0.55), (0.15, 0.86), (0.4, 1.0), (0.7, 0.96), (0.9, 0.72), (1.0, 0.3)]
            tube([hb + a * hh * f for f, _ in prof], [(hr * k, hd * k) for _, k in prof], m["head"], n=12)
            if P["eyes"]:
                eyes(J, P)

def hood(P, J, mat, scale=1.32, drape=None, tip_back=0.06, tip_up=0.04, face=True, face_mat="M_Void",
         want_eyes=True, n=12, low=0.1):
    a, hb, hh, hr = J["head_a"], J["head_b"], P["head_h"], P["head_r"]
    hd = hr * P["head_d"]
    dr = drape or (P["sh_w"] * 0.9, P["chest"][1] * 1.15)
    pts = [J["neck_b"] - V((0, 0, low)), J["neck_b"] + V((0, 0, 0.02)), hb + a * hh * 0.3, hb + a * hh * 0.62,
           hb + a * hh * 0.92 + V((0, tip_back * 0.5, 0)), hb + a * (hh + tip_up) + V((0, tip_back, 0))]
    rad = [dr, (hr * 1.5, hd * 1.4), (hr * scale, hd * scale), (hr * scale * 0.97, hd * scale),
           (hr * scale * 0.72, hd * scale * 0.8), (0.02, 0.02)]
    tube(pts, rad, mat, n=n)
    c = hb + a * hh * 0.47
    if face:
        face_patch(J, c, hr * scale * 0.97, hd * scale * 0.98, hr * 0.78, hh * 0.3, face_mat)
    if want_eyes:
        eyes(J, P, c=c, rx=hr * scale, ry=hd * scale * 0.98 + 0.006)

# =====================================================================
# props
# =====================================================================
def rope_ring(c, rx, ry, r, mat, n=14):
    pts = ring_pts(c, rx, ry, n)
    tube(pts + [pts[0], pts[1]], [r] * (n + 2), mat, n=4, caps=(False, False))

def bread_slice(O, ex, ey, mat="M_Bread", s=1.0):
    ez = ex.cross(ey)
    pts = [(-0.055, 0), (-0.02, -0.004), (0.03, 0.002), (0.055, 0), (0.06, 0.055), (0.045, 0.082), (0.0, 0.095),
           (-0.045, 0.085), (-0.062, 0.058)]
    prism(O, ex, ey, ez, [(x * s, y * s) for x, y in pts], -0.012 * s, 0.012 * s, mat)

def pitchfork(A, B, tines=0.32):
    A, B = V(A), V(B); d = (B - A).normalized()
    tube([A, B], [0.017, 0.016], "M_Wood", n=6)
    side = perp(d, X)
    seg(B - side * 0.11, B + side * 0.11, 0.022, "M_Steel")
    for k in (-1, 0, 1):
        b = B + side * 0.1 * k
        tube([b, b + d * tines * 0.6 + side * 0.02 * k, b + d * tines + side * 0.035 * k], [0.008, 0.007, 0.002],
             "M_Steel", n=4)

def spear(base, top, blade=0.32):
    base, top = V(base), V(top); d = (top - base).normalized()
    tube([base, top], [0.018, 0.016], "M_Wood", n=6)
    u = perp(d, X); ez = u.cross(d)
    prism(top, u, d, ez, [(0, -0.02), (0.045, 0.1), (0.0, blade), (-0.045, 0.1)], -0.006, 0.006, "M_Steel")
    tube([top - d * 0.06, top + d * 0.02], [0.024, 0.02], "M_Iron", n=6)

def shield(c, normal, r):
    c, normal = V(c), V(normal).normalized()
    tube([c - normal * 0.03, c], [r, r], "M_Wood", n=16)
    tube([c + normal * 0.002, c + normal * 0.01], [r * 1.02, r * 1.02], "M_Iron", n=16)
    blob(c + normal * 0.0, r * 0.22, "M_Iron", n=8, k=4, sq=(1, 1, 1))
    u = perp(normal, Z); v = normal.cross(u)
    d1 = (u + v).normalized()
    fbox(c + normal * 0.011, d1, normal.cross(d1).normalized(), normal, -r * 0.95, r * 0.95, -0.05, 0.05, 0, 0.004,
         "M_Ink")

def bucket(O, ez, r0=0.12, r1=0.155, h=0.28, mat="M_Water"):
    O, ez = V(O), V(ez).normalized()
    tube([O, O + ez * h], [r0, r1], mat, n=12, caps=(True, False), ref=X)
    for f in (0.15, 0.85):
        rr = r0 + (r1 - r0) * f + 0.006
        tube([O + ez * (h * f - 0.012), O + ez * (h * f + 0.012)], [rr, rr], "M_Iron", n=12, caps=(False, False))
    tube([O + ez * (h * 0.8), O + ez * (h * 0.8 + 0.005)], [r1 * 0.95, r1 * 0.95], "M_WaterDeep", n=12)
    u = perp(ez, X)
    arc = [O + ez * h + u * r1 * math.cos(math.pi * k / 8) + ez * r1 * 0.9 * math.sin(math.pi * k / 8) for k in range(9)]
    tube(arc, [0.006] * 9, "M_Iron", n=4)

def axe(top, bottom):
    top, bottom = V(top), V(bottom); d = (bottom - top).normalized()
    tube([top, bottom], [0.024, 0.026], "M_Wood", n=6)
    side = perp(d, X); ez = side.cross(d)
    O = bottom - d * 0.05
    blade = [(0, -0.02), (0.06, -0.05), (0.26, -0.13), (0.3, 0.05), (0.27, 0.24), (0.07, 0.1), (0, 0.1)]
    prism(O, side, d * -1, ez, blade, -0.012, 0.012, "M_Steel")
    prism(O + ez * 0.0125, side, d * -1, ez, [(0.255, -0.11), (0.3, 0.05), (0.27, 0.23), (0.22, 0.12), (0.24, 0.0)],
          0, 0.003, "M_BloodDark")

# =====================================================================
# characters
# =====================================================================
ZH = dict(leg=0.93, crouch=0.04, torso=0.58, hunch=0.17, sh_w=0.19, sh_fwd=-0.03, sh_drop=0.05,
          hips=(0.13, 0.09), waist=(0.12, 0.08), chest=(0.155, 0.1), hip_w=0.13, foot_sep=0.13, step=0.06,
          neck=0.1, neck_r=0.035, head_r=0.085, head_h=0.23, head_fwd=0.55, upper=0.31, fore=0.29, arm_r=0.034,
          thigh_r=0.058, calf_r=0.04, hand=0.13, finger=0.11, palm_w=0.088, finger_r=0.0105)

def zhdan_hair(J, P, mat="M_Ink", n=9):
    t, a = J["head_t"], J["head_a"]
    for k in range(n):
        ang = 2 * math.pi * k / n
        b = t - a * 0.05 + V((math.cos(ang) * 0.055, math.sin(ang) * 0.05 + 0.03, 0))
        d = V((math.cos(ang) * 0.45 + R(-0.2, 0.2), 0.9 + math.sin(ang) * 0.3, R(-0.45, 0.05))).normalized()
        cone(b, d, 0.032, R(0.06, 0.11), mat)

def char_zhdan0():
    """Stage 0 'Ash': a starved peasant, burlap shirt, rope belt, one short trouser leg, sooty hands, bread."""
    P, J = skeleton(**ZH, hands={1: None}, curl={-1: 0.25, 1: 0.8},
                    m=dict(torso="M_Cloth", upper="M_Cloth", fore="M_Cloth", hand="M_Soot", thigh="M_Dark",
                           shin=("M_Skin", "M_Dark"), foot="M_Wood", head="M_Black", neck="M_Skin"))
    cs = J["spine"][3]
    P, J = skeleton(**ZH, hands={1: cs + V((0.06, -0.21, -0.06))}, poles={1: (1, 0.4, -1)},
                    hdir={1: (-0.5, -0.3, 0.6)}, curl={-1: 0.25, 1: 0.75},
                    m=dict(torso="M_Cloth", upper="M_Cloth", fore="M_Cloth", hand="M_Soot", thigh="M_Dark",
                           shin=("M_Skin", "M_Dark"), foot="M_Wood", head="M_Black", neck="M_Skin"))
    body(P, J)
    sp = J["spine"]
    skirt([(sp[1] + V((0, 0, 0.02)), (0.14, 0.095)), (V((0, sp[0].y + 0.01, 0.74)), (0.19, 0.13))], "M_Cloth", jag=0.06)
    rope_ring(sp[1] + V((0, 0, 0.0)), 0.135, 0.092, 0.011, "M_Wood")
    seg(sp[1] + V((0.06, -0.08, 0)), sp[1] + V((0.09, -0.1, -0.2)), 0.012, "M_Wood")     # belt end
    k, a = J["knee", -1], J["ank", -1]                                                      # short trouser leg
    tube([k, lerp(k, a, 0.38)], [ZH["calf_r"] * 1.45, ZH["calf_r"] * 1.35], "M_Dark", caps=(True, False))
    for s in (-1, 1):                                                                       # bast shoe straps
        a = J["ank", s]
        for h in (0.03, 0.08):
            rope_ring(a + V((0, 0, h)), ZH["calf_r"] * 0.8, ZH["calf_r"] * 0.8, 0.006, "M_Wood", n=8)
    zhdan_hair(J, P)
    Wr, hd = J["wr", 1], J["hd", 1]
    bread_slice(Wr + hd * 0.09 + V((-0.02, -0.03, 0.02)), X, (Z * 0.9 - Y * 0.3).normalized(), s=1.15)
    return P, J

def char_zhdan2():
    """Stage 2 'Brand': hands and feet charred black, shirt burnt off the chest, glowing cracks, ash hair."""
    P, J = skeleton(**dict(ZH, hunch=0.14, head_fwd=0.45, step=0.12), curl={-1: 0.7, 1: 0.65},
                    hands={-1: None, 1: None}, arm_out=0.1,
                    m=dict(torso="M_Char", upper=("M_Cloth", "M_Char"), fore="M_Char", hand="M_Ink", thigh="M_Dark",
                           shin="M_Char", foot="M_Ink", head="M_Black", neck="M_Char"),
                    eye_mat="M_EmberHot", eye_r=0.012)
    body(P, J)
    sp = J["spine"]
    skirt([(sp[2] + V((0, 0, 0.04)), (0.13, 0.088)), (sp[1], (0.135, 0.092)),
           (V((0, sp[0].y + 0.01, 0.76)), (0.18, 0.125))], "M_Cloth", jag=0.08)
    rope_ring(sp[1], 0.14, 0.096, 0.011, "M_Wood")
    S, E = J["sh", -1], J["elb", -1]                                   # one sleeve stub left, torn
    tube([S, lerp(S, E, 0.55)], [ZH["arm_r"] * 1.3, ZH["arm_r"] * 1.25], "M_Cloth", jit=0.12, caps=(False, False))
    for s in (-1, 1):                                                   # trousers burnt off below the knee
        k, a = J["knee", s], J["ank", s]
        tube([k + V((0, 0, 0.02)), lerp(k, a, 0.18)], [ZH["calf_r"] * 1.4, ZH["calf_r"] * 1.45], "M_Dark", jit=0.15,
             caps=(True, False))
        cracks(J["elb", s], J["wr", s], ZH["arm_r"] * 0.85, ZH["arm_r"] * 0.6, 5)
        cracks(J["knee", s], J["ank", s], ZH["calf_r"] * 1.1, ZH["calf_r"] * 0.6, 3)
        if s == 1:
            cracks(J["sh", s], J["elb", s], ZH["arm_r"] * 1.1, ZH["arm_r"] * 0.85, 3)
        with glow_layer():                                              # ears: burnt stubs with a dull ember
            c = J["head_c"] + X * s * ZH["head_r"] * 0.98 + J["head_a"] * 0.0
        blob(c, 0.017, "M_Ink", n=5, k=3)
    cracks(J["neck_b"] - V((0, 0, 0.03)), J["head_b"], ZH["neck_r"], ZH["neck_r"] * 0.9, 4, w=0.006)
    cracks(sp[2], sp[4], 0.13, 0.15, 7, w=0.008, spread=0.5)
    # ash instead of hair: a grey crust with sparks blowing off the back of the head
    t, a = J["head_t"], J["head_a"]
    blob(t - a * 0.06 + V((0, 0.015, 0)), 0.088, "M_Ash", n=9, k=4, sq=(1.0, 1.15, 0.55), jit=0.12)
    with glow_layer():
        for _ in range(14):
            p = t + V((R(-0.15, 0.15), R(0.08, 0.45), R(-0.2, 0.15)))
            q = R(0.004, 0.009)
            box(p.x - q, p.x + q, p.y - q, p.y + q, p.z - q, p.z + q, "M_EmberHot")
    smoke(J["head_c"] + J["fwd"] * 0.13 - J["head_a"] * 0.07, 3, 0.03, 0.25)
    return P, J

def char_zhdan4():
    """Stage 4 'Pyre': charcoal body, glowing furnace ribcage, skull with fire in the sockets, black smoke.
    Only the hands are still human: they reach out to take something."""
    cs = None
    P, J = skeleton(**dict(ZH, hunch=0.2, head_fwd=0.6), m=dict(torso="M_Ink", upper="M_Ink", fore="M_Char",
                    hand="M_Char", thigh="M_Ink", shin="M_Ink", foot="M_Ink", head="M_Bone", neck="M_Ink"),
                    eyes=False)
    sh = {s: J["sh", s] for s in (-1, 1)}
    P, J = skeleton(**dict(ZH, hunch=0.2, head_fwd=0.6), m=dict(torso="M_Ink", upper="M_Ink", fore="M_Char",
                    hand="M_Char", thigh="M_Ink", shin="M_Ink", foot="M_Ink", head="M_Bone", neck="M_Ink"),
                    eyes=False, hands={s: sh[s] + V((s * 0.09, -0.5, -0.12)) for s in (-1, 1)},
                    poles={s: (s * 1, 0.2, -1) for s in (-1, 1)}, curl={-1: 0.3, 1: 0.45})
    body(P, J)
    sp = J["spine"]
    skirt([(sp[1], (0.13, 0.09)), (V((0, sp[0].y, 0.7)), (0.175, 0.125))], "M_Black", jag=0.1)
    # furnace in the chest: glowing plate under black rib bars
    c = sp[3] + V((0, -0.002, 0)); hx, hy = ZH["chest"]
    pts = []
    for k in range(14):
        t = 2 * math.pi * k / 14
        x, z = 0.125 * math.cos(t), 0.17 * math.sin(t)
        pts.append(c + V((x, -hy * math.sqrt(max(0.05, 1 - (x / hx) ** 2)) - 0.006 - 0.03 * (z / 0.15), z)))
    with glow_layer():
        ngon(pts, "M_Ember")
        ngon([lerp(c + V((0, -hy - 0.04, 0)), p, 0.5) + V((0, -0.002, 0)) for p in pts], "M_EmberHot")
    for k in range(6):
        z = -0.13 + 0.05 * k
        arc = []
        for i in range(7):
            x = -0.14 + 0.28 * i / 6
            arc.append(c + V((x, -hy * math.sqrt(max(0.05, 1 - (x / (hx * 1.02)) ** 2)) - 0.012 - 0.025 * (z / 0.15),
                              z - 0.02 * abs(x) / 0.12)))
        tube(arc, [0.0095] * 7, "M_Ink", n=4)
    seg(c + V((0, -hy - 0.03, -0.14)), c + V((0, -hy - 0.012, 0.15)), 0.024, "M_Ink")      # sternum
    for s in (-1, 1):
        cracks(J["sh", s], J["elb", s], ZH["arm_r"], ZH["arm_r"] * 0.8, 6)
        cracks(J["elb", s], J["wr", s], ZH["arm_r"] * 0.85, ZH["arm_r"] * 0.6, 6)
        cracks(J["hip", s], J["knee", s], ZH["thigh_r"], ZH["calf_r"], 6)
        cracks(J["knee", s], J["ank", s], ZH["calf_r"], ZH["calf_r"] * 0.6, 5)
        for k in range(4):
            b = J["sh", s] + V((-s * R(0.0, 0.12), R(0.02, 0.08), R(-0.03, 0.03)))
            flame(b, (s * R(0.1, 0.4), R(0.9, 1.6), 1), R(0.08, 0.2) * (0.6 if s < 0 else 1.0), R(0.015, 0.025),
                  ("M_Ember", "M_EmberDeep", "M_EmberHot")[k % 3])
    cracks(sp[0], sp[2], 0.13, 0.12, 8, w=0.008)
    # skull: dark sockets with fire, open jaw with fire
    c, a, f = J["head_c"], J["head_a"], J["fwd"]
    hr, hd = ZH["head_r"], ZH["head_r"] * DEF["head_d"]
    for s in (-1, 1):
        face_patch_c = c + X * s * 0.034 + a * 0.012
        pts = []
        for k in range(8):
            t = 2 * math.pi * k / 8
            x, z = s * 0.034 + 0.024 * math.cos(t), 0.012 + 0.02 * math.sin(t)
            pts.append(c + X * x + a * z + f * (hd * math.sqrt(max(0.05, 1 - (x / hr) ** 2)) + 0.003))
        ngon(pts, "M_Void")
        with glow_layer():
            p = c + X * s * 0.034 + a * 0.008 + f * (hd * 0.93 + 0.006)
            box(p.x - 0.008, p.x + 0.008, p.y - 0.004, p.y + 0.004, p.z - 0.01, p.z + 0.01, "M_EmberHot")
    jaw = []
    for k in range(8):
        t = 2 * math.pi * k / 8
        x, z = 0.035 * math.cos(t), -0.055 + 0.022 * math.sin(t)
        jaw.append(c + X * x + a * z + f * (hd * 0.86 * math.sqrt(max(0.05, 1 - (x / hr) ** 2)) + 0.004))
    ngon(jaw, "M_Void")
    with glow_layer():
        ngon([lerp(c + a * -0.055 + f * hd * 0.9, p, 0.6) + f * 0.002 for p in jaw], "M_Ember")
    for k in range(6):                                                   # fire licking off the back of the skull
        b = J["head_t"] - a * R(0.02, 0.07) + V((R(-0.05, 0.05), R(0.03, 0.07), 0))
        flame(b, (R(-0.3, 0.3), R(1.2, 2.0), 1), R(0.1, 0.24), R(0.02, 0.035), ("M_Ember", "M_EmberHot", "M_EmberDeep")[k % 3])
    smoke(J["head_t"] + V((0.02, 0.16, 0.1)), 13, 0.085, 0.6)
    smoke(J["sh", 1] + V((0.0, 0.16, 0.12)), 6, 0.055, 0.35)
    return P, J

def char_shadow():
    """The Shadow: how the Fire shows itself in the world. Too tall, broad flat head, arms to the knees."""
    base = dict(leg=1.5, crouch=0.04, torso=0.85, hunch=0.12, sh_w=0.3, sh_drop=0.0, hips=(0.13, 0.08), waist=(0.105, 0.07),
                chest=(0.25, 0.11), hip_w=0.14, foot_sep=0.13, neck=0.13, neck_r=0.034, head_r=0.13, head_d=0.82,
                head_h=0.25, head_fwd=0.22, upper=0.62, fore=0.6, arm_r=0.03, thigh_r=0.055, calf_r=0.036,
                foot_w=0.035, foot_l=0.2, hand=0.17, finger=0.21, palm_w=0.07, finger_r=0.0075,
                eye_r=0.0085, eye_gap=0.046, eye_z=0.01,
                m=dict(torso="M_Ink", upper="M_Ink", fore="M_Ink", hand="M_Ink", thigh="M_Ink", shin="M_Ink",
                       foot="M_Ink", head="M_Ink", neck="M_Ink"))
    P, J = skeleton(**base)
    sh = {s: J["sh", s] for s in (-1, 1)}
    P, J = skeleton(**base, hands={-1: sh[-1] + V((-0.14, -0.1, -1.13)), 1: sh[1] + V((0.2, -0.16, -1.1))},
                    poles={-1: (-0.4, 1, 0.3), 1: (0.5, 1, 0.2)}, curl={-1: 0.2, 1: 0.35})
    body(P, J)
    return P, J

def char_korney():
    """Executioner Korney: huge, square, never runs. Pointed hood with two slits, bloody apron, axe."""
    base = dict(leg=0.98, torso=0.75, hunch=0.03, sh_w=0.4, sh_drop=0.0, hips=(0.3, 0.22), waist=(0.33, 0.25),
                chest=(0.38, 0.26), hip_w=0.28, foot_sep=0.22, neck=0.06, neck_r=0.12, head_r=0.13, head_h=0.28,
                head_fwd=0.05, upper=0.38, fore=0.36, arm_r=0.085, thigh_r=0.13, calf_r=0.1, foot_w=0.07, foot_l=0.26,
                hand=0.16, finger=0.1, palm_w=0.14, finger_r=0.02, arm_out=0.06, eyes=False, head=False,
                m=dict(torso="M_Black", upper="M_Black", fore="M_Black", hand="M_Ink", thigh="M_Ink", shin="M_Ink",
                       foot="M_Ink", neck="M_Black"), curl={-1: 0.9, 1: 0.8})
    P, J = skeleton(**base)
    sh = J["sh", -1]
    P, J = skeleton(**base, hands={-1: sh + V((-0.09, -0.08, -0.71))}, hdir={-1: (0, -1, -0.3)})
    body(P, J)
    sp = J["spine"]
    # pointed executioner's hood down over the shoulders
    a, hb = J["head_a"], J["head_b"]
    pts = [sp[4] - V((0, 0, 0.2)), sp[4] + V((0, -0.01, 0.03)), hb + a * 0.08, hb + a * 0.2, hb + a * 0.33,
           hb + a * 0.5 + V((0, 0.03, 0))]
    rad = [(0.42, 0.29), (0.3, 0.24), (0.16, 0.15), (0.15, 0.145), (0.1, 0.1), (0.012, 0.012)]
    tube(pts, rad, "M_Black", n=14)
    for s in (-1, 1):                                                   # eye slits: you never see the eyes
        c = hb + a * 0.19 + X * s * 0.055
        y = -0.145 * math.sqrt(max(0.05, 1 - (0.055 / 0.155) ** 2)) - 0.004
        ngon([c + V((-0.035, y, -0.008)), c + V((0.035, y, -0.004 * s)), c + V((0.03, y, 0.01)),
              c + V((-0.03, y, 0.012))], "M_Void")
    # leather apron with dried blood
    rows = []
    for z in (sp[3].z + 0.05, sp[2].z, sp[1].z, sp[0].z, 0.75, 0.52):
        row = []
        w = 0.24 if z > sp[2].z else 0.3
        for i in range(7):
            x = -w + 2 * w * i / 6
            rx, ry = (0.34, 0.25) if z < sp[2].z else (0.36, 0.26)
            depth = ry * math.sqrt(max(0.05, 1 - (x / (rx * 1.05)) ** 2)) + 0.025
            if z < sp[0].z:
                depth = 0.27 + (sp[0].z - z) * 0.18
            row.append(V((x, -depth, z + (R(-0.03, 0.01) if z < 0.6 else 0))))
        rows.append(row)
    sheet(rows, "M_Dark")
    for c, sz in ((V((0.08, -0.3, 1.15)), 0.06), (V((-0.12, -0.31, 0.95)), 0.05), (V((0.05, -0.36, 0.72)), 0.07),
                  (V((-0.04, -0.29, 1.4)), 0.035)):
        splat(c, (0, -1, -0.15), sz, "M_BloodDark")
    rope_ring(sp[1] + V((0, 0, 0.03)), 0.345, 0.265, 0.025, "M_Ink", n=16)
    Wr = J["wr", -1]
    axe(Wr + V((0, -0.06, 0.12)), Wr + V((-0.08, -0.18, -0.85)))
    return P, J

def char_king():
    """The King: the heaviest figure in the game, but human. Black crown with white hatching, ermine
    collar, open mantle to the ground, sceptre."""
    base = dict(leg=0.9, torso=0.62, hunch=0.0, sh_w=0.3, sh_drop=0.02, hips=(0.27, 0.22), waist=(0.3, 0.28),
                chest=(0.3, 0.22), hip_w=0.24, foot_sep=0.15, neck=0.05, neck_r=0.08, head_r=0.11, head_h=0.26,
                head_fwd=0.05, upper=0.33, fore=0.3, arm_r=0.07, thigh_r=0.11, calf_r=0.08, foot_w=0.055,
                hand=0.11, finger=0.08, palm_w=0.09, finger_r=0.014, eye_gap=0.038,
                m=dict(torso="M_Dark", upper="M_Black", fore="M_Black", hand="M_Ink", thigh="M_Ink", shin="M_Ink",
                       foot="M_Ink", head="M_Ink", neck="M_Ink"), curl={-1: 0.85, 1: 0.5})
    P, J = skeleton(**base)
    sh = J["sh", -1]
    P, J = skeleton(**base, hands={-1: sh + V((-0.1, -0.3, -0.36))}, poles={-1: (-1, 0.5, -1)},
                    hdir={-1: (0, -0.3, -1)})
    body(P, J)
    sp = J["spine"]
    skirt([(sp[1], (0.3, 0.28)), (V((0, 0, 0.5)), (0.36, 0.32)), (V((0, 0.02, 0.01)), (0.42, 0.38))], "M_Black", n=16)
    # mantle: open strip around the back from shoulder to shoulder, trailing on the ground
    rows = []
    for z, rx, ry, cy in ((sp[4].z + 0.02, 0.33, 0.25, 0.0), (sp[3].z, 0.4, 0.31, 0.02), (1.0, 0.5, 0.42, 0.08),
                          (0.45, 0.58, 0.52, 0.16), (0.01, 0.64, 0.62, 0.24)):
        row = []
        for i in range(15):
            ang = math.radians(-55 + 290 * i / 14)          # open at the front (-Y = -90 deg)
            row.append(V((rx * math.cos(ang), cy + ry * math.sin(ang), z)))
        rows.append(row)
    sheet(rows, "M_Ink")
    # ermine collar
    c = sp[4] + V((0, 0.0, 0.0))
    pts = ring_pts(c, 0.3, 0.22, 16)
    tube(pts + pts[:2], [0.075] * 18, "M_Pale", n=8, caps=(False, False))
    for k in range(16):
        ang = 2 * math.pi * (k + 0.5) / 16
        p = c + V((0.37 * math.cos(ang), 0.29 * math.sin(ang), R(-0.03, 0.04)))
        q = V((math.cos(ang), math.sin(ang), 0))
        seg(p - Z * 0.025, p + Z * 0.025 + q * 0.01, 0.014, "M_Ink")
    # beard
    a, f = J["head_a"], J["fwd"]
    b0 = J["head_c"] - a * 0.06 + f * 0.07
    tube([b0, b0 - Z * 0.12 + f * 0.04, b0 - Z * 0.26 + f * 0.03], [(0.08, 0.04), (0.075, 0.035), (0.02, 0.01)],
         "M_Dark", n=8, jit=0.12)
    # black crown: band + spikes
    t = J["head_t"] - a * 0.06
    tube([t, t + a * 0.06], [(0.118, 0.13), (0.125, 0.137)], "M_Crown", n=14)
    for k in range(9):
        ang = 2 * math.pi * k / 9 + 0.2
        b = t + a * 0.055 + V((0.118 * math.cos(ang), 0.13 * math.sin(ang), 0))
        cone(b, a + V((math.cos(ang), math.sin(ang), 0)) * 0.18, 0.03, 0.17 if k % 2 else 0.12, "M_Crown")
    # sceptre
    Wr = J["wr", -1]
    tube([Wr + V((0, -0.05, -0.2)), Wr + V((0, -0.05, 0.62))], [0.016, 0.014], "M_Crown", n=6)
    blob(Wr + V((0, -0.05, 0.68)), 0.055, "M_Crown", n=8, k=4)
    seg(Wr + V((0, -0.05, 0.72)), Wr + V((0, -0.05, 0.84)), 0.018, "M_Crown")
    seg(Wr + V((-0.045, -0.05, 0.79)), Wr + V((0.045, -0.05, 0.79)), 0.018, "M_Crown")
    return P, J

PEASANT = dict(leg=0.82, crouch=0.06, torso=0.5, hunch=0.14, sh_w=0.25, hips=(0.17, 0.12), waist=(0.18, 0.13),
               chest=(0.22, 0.14), hip_w=0.17, foot_sep=0.17, step=0.1, neck=0.05, neck_r=0.06, head_r=0.095, head_h=0.22,
               head_fwd=0.35, upper=0.3, fore=0.28, arm_r=0.05, thigh_r=0.08, calf_r=0.06, hand=0.1, finger=0.075,
               palm_w=0.085, finger_r=0.011)

def felt_cap(J, mat="M_Black"):
    t, a = J["head_t"], J["head_a"]
    blob(t - a * 0.05, 0.11, mat, n=10, k=4, sq=(1, 1.05, 0.55), jit=0.06)
    tube([t - a * 0.085, t - a * 0.06], [(0.13, 0.14), (0.125, 0.135)], mat, n=10)

def char_peasant():
    """Enemy: peasant. Shoves in a crowd, hits with whatever he has: pitchfork (grey)."""
    A = V((-0.22, 0.3, 0.72)); B = V((0.18, -0.85, 1.72))
    P, J = skeleton(**PEASANT, hands={-1: lerp(A, B, 0.33), 1: lerp(A, B, 0.68)},
                    poles={-1: (-1, 0.4, -1), 1: (1, 0.2, -1)}, hdir={-1: (0.2, -0.4, -1), 1: (-0.3, -0.2, -1)},
                    curl={-1: 0.9, 1: 0.9},
                    m=dict(torso="M_Dark", upper="M_Dark", fore="M_Dark", hand="M_Black", thigh="M_Black", shin="M_Linen",
                           foot="M_Wood", head="M_Ink"))
    body(P, J)
    sp = J["spine"]
    skirt([(sp[1], (0.185, 0.135)), (V((0, sp[0].y + 0.02, 0.5)), (0.27, 0.22))], "M_Dark", jag=0.05, n=14)
    rope_ring(sp[1] + V((0, -0.005, 0)), 0.19, 0.14, 0.013, "M_Wood")
    felt_cap(J)
    pitchfork(A, B)
    return P, J

def char_ragman():
    """Enemy: peasant with a wet rag. Arms hang low; the rag is the only blue thing on him."""
    P, J = skeleton(**dict(PEASANT, hunch=0.2, head_fwd=0.45, upper=0.33, fore=0.32, arm_out=0.1, step=0.04),
                    curl={-1: 0.85, 1: 0.4}, hdir={-1: (0, -0.2, -1)},
                    m=dict(torso="M_Cloth", upper="M_Cloth", fore="M_Skin", hand="M_Black", thigh="M_Black",
                           shin="M_Black", foot="M_Wood", head="M_Ink"))
    body(P, J)
    sp = J["spine"]
    skirt([(sp[1], (0.185, 0.135)), (V((0, sp[0].y, 0.56)), (0.25, 0.2))], "M_Cloth", jag=0.07)
    rope_ring(sp[1], 0.19, 0.14, 0.013, "M_Wood")
    for s in (-1, 1):                                                   # rolled-up sleeves
        E = J["elb", s]
        tube([E + V((0, 0, 0.04)), E - V((0, 0, 0.03))], [0.06, 0.06], "M_Cloth", n=8)
    Wr = J["wr", -1]
    top = Wr + V((0, -0.04, -0.05))
    pts = [top, top + V((0.02, -0.03, -0.18)), top + V((-0.03, -0.01, -0.36)), top + V((0.01, 0.03, -0.52)),
           top + V((0.04, 0.02, -0.62))]
    tube(pts, [(0.06, 0.018), (0.07, 0.016), (0.065, 0.015), (0.05, 0.013), (0.03, 0.01)], "M_Water", n=6, jit=0.15,
         ref=Y)
    for k in range(4):
        p = pts[-1] + V((R(-0.03, 0.03), R(-0.02, 0.02), -0.04 - 0.07 * k))
        cone(p + Z * 0.012, (0, 0, -1), 0.006, 0.02, "M_Water")
    blob(V((pts[-1].x + 0.03, pts[-1].y, 0.004)), 0.13, "M_WaterDeep", n=10, k=2, sq=(1.2, 0.8, 0.03))
    return P, J

GUARD = dict(leg=0.95, torso=0.56, hunch=0.0, sh_w=0.23, hips=(0.15, 0.11), waist=(0.15, 0.11), chest=(0.2, 0.14),
             hip_w=0.15, foot_sep=0.13, neck=0.08, neck_r=0.05, head_r=0.09, head_h=0.23, head_fwd=0.05, upper=0.32,
             fore=0.29, arm_r=0.048, thigh_r=0.075, calf_r=0.055, hand=0.1, finger=0.075, palm_w=0.08, finger_r=0.011)

def kettle_helmet(J):
    t, a = J["head_t"], J["head_a"]
    blob(t - a * 0.07, 0.112, "M_Steel", n=12, k=4, sq=(1, 1.05, 0.85))
    tube([t - a * 0.105, t - a * 0.09], [(0.2, 0.21), (0.19, 0.2)], "M_Steel", n=14)
    seg(t - a * 0.11 + V((0, 0, 0)), t + a * 0.04, 0.012, "M_Iron")

def guard_body(extra_m=None, **kw):
    m = dict(torso="M_Cloth", upper="M_Cloth", fore="M_Cloth", hand="M_Ink", thigh="M_Black", shin="M_Ink",
             foot="M_Ink", head="M_Ink")
    m.update(extra_m or {})
    P, J = skeleton(**dict(GUARD, **kw), m=m)
    body(P, J)
    sp = J["spine"]
    skirt([(sp[1], (0.16, 0.12)), (V((0, 0.0, 0.55)), (0.24, 0.2))], "M_Cloth", n=14)
    for z in (sp[2].z, sp[3].z):                                         # quilted gambeson ridges
        rope_ring(V((0, sp[2].y, z)), 0.165 if z == sp[2].z else 0.205, 0.125 if z == sp[2].z else 0.145, 0.008,
                  "M_Black", n=12)
    rope_ring(sp[1] + V((0, 0, 0.01)), 0.17, 0.125, 0.016, "M_Iron")
    kettle_helmet(J)
    return P, J

def char_guard():
    """Enemy: town guard. Straight, tall, helmet, holds the line: spear and shield (grey)."""
    sh = skeleton(**GUARD)[1]
    P, J = guard_body(hands={-1: sh["sh", -1] + V((-0.1, -0.15, -0.36)), 1: sh["sh", 1] + V((0.06, -0.38, -0.3))},
                      poles={-1: (-1, 0.5, -1), 1: (1, 0.0, -1)}, hdir={-1: (0, -0.3, -1), 1: (-0.3, -1, 0)},
                      curl={-1: 0.9, 1: 0.8})
    Wr = J["wr", -1]
    spear(V((Wr.x - 0.02, Wr.y - 0.05, 0.0)), V((Wr.x - 0.02, Wr.y - 0.06, 2.3)))
    shield(J["wr", 1] + V((0.02, -0.07, 0.03)), (-0.25, -1, 0.05), 0.31)
    return P, J

def char_bucketguard():
    """Enemy: guard with a bucket. No shield; closes in and throws water (the bucket is blue)."""
    sh = skeleton(**GUARD)[1]
    O = V((0.0, -0.42, 0.84)); ez = V((0, -0.35, 1)).normalized()
    P, J = guard_body(hands={-1: O + V((-0.17, 0.03, 0.2)), 1: O + V((0.17, 0.03, 0.2))},
                      poles={-1: (-1, 0.3, -1), 1: (1, 0.3, -1)}, hdir={-1: (0.3, -0.2, -1), 1: (-0.3, -0.2, -1)},
                      curl={-1: 0.85, 1: 0.85}, hunch=0.05)
    bucket(O, ez)
    rim = O + ez * 0.25 + V((0, -0.05, 0))
    for k in range(9):                                                   # water slopping over the rim
        p = rim + V((R(-0.13, 0.13), R(-0.2, -0.05), R(0.02, 0.18)))
        blob(p, R(0.008, 0.018), "M_Water", n=5, k=3)
    return P, J

def char_archer():
    """Enemy: archer. Thin, keeps distance, bow drawn in profile (grey)."""
    base = dict(leg=0.92, torso=0.55, hunch=0.06, sh_w=0.19, hips=(0.13, 0.09), waist=(0.12, 0.085),
                chest=(0.16, 0.11), hip_w=0.13, foot_sep=0.15, step=0.12, neck=0.08, neck_r=0.04, head_r=0.085,
                head_h=0.22, head_fwd=0.15, upper=0.31, fore=0.28, arm_r=0.038, thigh_r=0.065, calf_r=0.045,
                hand=0.1, finger=0.08, palm_w=0.075, finger_r=0.01,
                m=dict(torso="M_Black", upper="M_Black", fore="M_Dark", hand="M_Ink", thigh="M_Ink", shin="M_Ink",
                       foot="M_Ink", head="M_Ink"))
    sh = skeleton(**base)[1]
    D = V((0.35, -1, 0.02)).normalized()
    bowhand = sh["sh", 1] + D * 0.56 + V((0, 0, 0.0))
    pull = sh["sh", -1] + V((0.12, -0.12, 0.12))
    P, J = skeleton(**base, hands={1: bowhand, -1: pull}, poles={1: (1, 0.3, -1), -1: (-1, 1, 0)},
                    hdir={1: (0, 0, 1), -1: (0.3, -0.2, 0.6)}, curl={1: 0.95, -1: 0.7})
    body(P, J)
    hood(P, J, "M_Black", drape=(0.26, 0.17), tip_back=0.12, tip_up=0.03, low=0.16)
    sp = J["spine"]
    skirt([(sp[1], (0.13, 0.09)), (V((0, 0.0, 0.62)), (0.2, 0.15))], "M_Black", jag=0.06)
    Wb = J["wr", 1] + J["hd", 1] * 0.04
    arc = []
    for k in range(11):
        t = -1 + 2 * k / 10
        arc.append(Wb + Z * 0.72 * t - D * 0.2 * t * t)
    tube(arc, [0.012 + 0.008 * (1 - abs((k - 5) / 5)) for k in range(11)], "M_Wood", n=5)
    nock = J["wr", -1] + V((0.01, 0.01, 0.03))
    seg(arc[0], nock, 0.004, "M_Ink"); seg(arc[-1], nock, 0.004, "M_Ink")
    tip = nock + (Wb - nock).normalized() * 0.82
    seg(nock, tip, 0.008, "M_Wood")
    cone(tip, tip - nock, 0.016, 0.06, "M_Steel")
    # quiver on the back
    q0, q1 = sp[1] + V((-0.12, 0.13, 0.0)), sp[4] + V((0.12, 0.15, 0.08))
    tube([q0, q1], [0.05, 0.055], "M_Dark", n=8)
    for k in range(4):
        p = q1 + V((R(-0.03, 0.03), R(-0.02, 0.02), 0))
        d = (q1 - q0).normalized()
        seg(p, p + d * 0.14, 0.006, "M_Wood")
        seg(p + d * 0.1, p + d * 0.16 + V((0.02, 0, 0)), 0.02, "M_Linen", h=0.003)
    return P, J

def char_gavrila():
    """Victim #1, baker Gavrila: a barrel in an apron, flour-white face (black dot eyes), hands black
    from the oven - the mirror of Zhdan. Holds out a loaf."""
    base = dict(leg=0.78, torso=0.6, hunch=0.03, sh_w=0.27, hips=(0.3, 0.26), waist=(0.37, 0.34), chest=(0.32, 0.26),
                hip_w=0.24, foot_sep=0.16, neck=0.03, neck_r=0.09, head_r=0.11, head_h=0.24, head_fwd=0.1,
                upper=0.28, fore=0.26, arm_r=0.065, thigh_r=0.11, calf_r=0.075, hand=0.1, finger=0.07, palm_w=0.09,
                finger_r=0.014, eye_mat="M_Void", eye_r=0.019, eye_gap=0.043, eye_z=0.01,
                m=dict(torso="M_Linen", upper="M_Linen", fore="M_Pale", hand="M_Ink", thigh="M_Dark", shin="M_Dark",
                       foot="M_Black", head="M_Pale", neck="M_Pale"), curl={1: 0.6, -1: 0.4})
    sh = skeleton(**base)[1]
    P, J = skeleton(**base, hands={1: sh["sh", 1] + V((0.1, -0.48, -0.2))}, poles={1: (1, 0.3, -1)},
                    hdir={1: (-0.2, -0.6, 0.3)})
    body(P, J)
    sp = J["spine"]
    rows = []                                                            # apron hugging the belly
    for z, hw, (rx, ry), flat in ((sp[3].z + 0.06, 0.12, (0.3, 0.24), None), (sp[2].z + 0.04, 0.19, (0.36, 0.32), None),
                                  (sp[1].z, 0.22, (0.37, 0.34), None), (sp[0].z, 0.23, (0.32, 0.29), 0.33),
                                  (0.6, 0.235, (0.32, 0.29), 0.35), (0.44, 0.24, (0.32, 0.29), 0.36)):
        row = []
        for i in range(7):
            x = -hw + 2 * hw * i / 6
            depth = ry * math.sqrt(max(0.05, 1 - (x / rx) ** 2)) + 0.018
            if flat:
                depth = max(depth, flat - 0.06 * (x / hw) ** 2)
            row.append(V((x, -depth, z + (R(-0.015, 0.0) if z < 0.5 else 0))))
        rows.append(row)
    sheet(rows, "M_Pale")
    t, a = J["head_t"], J["head_a"]                                       # soft baker's cap, slumped
    tube([t - a * 0.08, t - a * 0.05], [(0.112, 0.122), (0.115, 0.125)], "M_Pale", n=12)
    blob(t + a * 0.0 + V((0.03, 0.02, 0)), 0.135, "M_Pale", n=12, k=5, sq=(1.05, 1.0, 0.55), jit=0.08)
    Wr, hd = J["wr", 1], J["hd", 1]
    blob(Wr + hd * 0.08 + V((0, -0.04, 0.05)), 0.11, "M_Bread", n=10, k=5, sq=(1.15, 0.9, 0.62))
    for k in range(3):
        c = Wr + hd * 0.08 + V((-0.06 + 0.06 * k, -0.135, 0.09))
        seg(c - V((0.02, 0, 0.012)), c + V((0.02, 0, 0.012)), 0.008, "M_Black")
    # a smudge of soot on the forearm, flour dust on the trousers
    return P, J

def char_agafya():
    """Victim #5, widow Agafya: all in black, shawl over the head, crosses herself and whispers."""
    base = dict(leg=0.8, torso=0.5, hunch=0.18, sh_w=0.17, hips=(0.14, 0.1), waist=(0.12, 0.09), chest=(0.15, 0.1),
                hip_w=0.13, foot_sep=0.09, neck=0.07, neck_r=0.035, head_r=0.08, head_h=0.21, head_fwd=0.5,
                upper=0.27, fore=0.25, arm_r=0.035, thigh_r=0.065, calf_r=0.045, hand=0.09, finger=0.09,
                palm_w=0.065, finger_r=0.008, eyes=False,
                m=dict(torso="M_Ink", upper="M_Ink", fore="M_Ink", hand="M_Skin", thigh="M_Ink", shin="M_Ink",
                       foot="M_Ink", head="M_Ink"))
    P0, J0 = skeleton(**base)
    P, J = skeleton(**base, hands={-1: J0["head_c"] + J0["fwd"] * 0.16 + V((0.0, 0, 0.05)),
                                   1: J0["spine"][3] + V((-0.03, -0.17, 0.02))},
                    poles={-1: (-1, 0.0, -1), 1: (1, 0.2, -1)}, hdir={-1: (0.2, 0.1, 1), 1: (-1, -0.2, 0.3)},
                    curl={-1: 0.3, 1: 0.8})
    body(P, J)
    sp = J["spine"]
    skirt([(sp[1], (0.13, 0.095)), (V((0, 0.0, 0.45)), (0.22, 0.2)), (V((0, 0.02, 0.0)), (0.27, 0.25))], "M_Ink",
          jag=0.03, n=16)
    hood(P, J, "M_Black", scale=1.38, drape=(0.25, 0.18), tip_back=0.02, tip_up=0.01, low=0.24)
    rows = []                                                             # shawl tail down the back
    for z, w, d in ((sp[4].z, 0.2, 0.14), (sp[3].z - 0.05, 0.26, 0.17), (sp[2].z - 0.1, 0.22, 0.19)):
        row = []
        for i in range(9):
            ang = math.pi * (0.1 + 0.8 * i / 8)
            row.append(V((w * math.cos(ang), sp[3].y + d * math.sin(ang) + 0.02,
                          z - (0.12 if (i % 2 and z < sp[3].z - 0.1) else 0))))
        rows.append(row)
    sheet(rows, "M_Black")
    return P, J

def char_nikodim():
    """Victim #8, priest Nikodim: tall cassock, kamilavka, grey beard, cross, censer. Not afraid."""
    base = dict(leg=0.93, torso=0.57, hunch=0.0, sh_w=0.21, hips=(0.18, 0.13), waist=(0.17, 0.12), chest=(0.19, 0.13),
                hip_w=0.15, foot_sep=0.11, neck=0.07, neck_r=0.045, head_r=0.09, head_h=0.24, head_fwd=0.03,
                upper=0.31, fore=0.28, arm_r=0.05, thigh_r=0.075, calf_r=0.055, hand=0.1, finger=0.08,
                palm_w=0.075, finger_r=0.01, eye_r=0.012,
                m=dict(torso="M_Ink", upper="M_Ink", fore="M_Ink", hand="M_Skin", thigh="M_Ink", shin="M_Ink",
                       foot="M_Ink", head="M_Ink"), curl={-1: 0.85, 1: 0.6})
    sh = skeleton(**base)[1]
    P, J = skeleton(**base, hands={-1: sh["sh", -1] + V((-0.03, -0.36, -0.38))}, poles={-1: (-1, 0.3, -1)},
                    hdir={-1: (0, -0.3, -1)})
    body(P, J)
    sp = J["spine"]
    skirt([(sp[2], (0.17, 0.12)), (V((0, 0, 0.7)), (0.22, 0.17)), (V((0, 0.01, 0.0)), (0.3, 0.26))], "M_Ink", n=16)
    for s in (-1, 1):                                                     # wide bell sleeves
        E, Wr = J["elb", s], J["wr", s]
        tube([E, lerp(E, Wr, 0.6), Wr + (Wr - E).normalized() * 0.03], [0.055, 0.075, 0.1], "M_Ink", n=10,
             caps=(True, False))
    a, f = J["head_a"], J["fwd"]
    t = J["head_t"]
    tube([t - a * 0.05, t + a * 0.13], [(0.098, 0.105), (0.112, 0.118)], "M_Black", n=12)    # kamilavka
    tube([t + a * 0.13, t + a * 0.15], [(0.125, 0.13), (0.125, 0.13)], "M_Black", n=12)
    tube([t - a * 0.04 + V((0, 0.03, 0)), t - a * 0.2 + V((0, 0.08, 0)), t - a * 0.34 + V((0, 0.1, 0))],
         [(0.095, 0.07), (0.1, 0.06), (0.09, 0.03)], "M_Linen", n=8, jit=0.1)                  # hair down the back
    b0 = J["head_c"] - a * 0.055 + f * 0.065
    tube([b0, b0 - Z * 0.14 + f * 0.035, b0 - Z * 0.32 + f * 0.04], [(0.08, 0.045), (0.075, 0.04), (0.025, 0.012)],
         "M_Linen", n=8, jit=0.1)
    cc = sp[3] + V((0, -0.17, -0.12))                                      # pectoral cross
    seg(cc + V((0, 0, -0.07)), cc + V((0, 0, 0.07)), 0.022, "M_Steel", h=0.01)
    seg(cc + V((-0.045, 0, 0.025)), cc + V((0.045, 0, 0.025)), 0.022, "M_Steel", h=0.01)
    Wr = J["wr", -1]                                                       # censer on chains
    top = Wr + V((0, -0.03, -0.06)); bot = top + V((0.0, -0.04, -0.45))
    for k in range(10):
        p, q = lerp(top, bot, k / 10), lerp(top, bot, (k + 1) / 10)
        seg(p, q, 0.009 if k % 2 else 0.004, "M_Steel", h=0.004 if k % 2 else 0.009)
    blob(bot + V((0, 0, -0.06)), 0.065, "M_Steel", n=10, k=4, sq=(1, 1, 0.85))
    cone(bot - V((0, 0, 0.02)), (0, 0, 1), 0.05, 0.06, "M_Steel", n=8)
    for k in range(5):                                                     # pale incense smoke
        blob(bot + V((0.03 * k + R(-0.02, 0.02), 0.03 * k, 0.04 + 0.1 * k)), 0.028 + 0.012 * k, "M_Pale", n=7, k=3,
             jit=0.2)
    return P, J

def char_efim():
    """Victim #18, blind Efim: bent beggar in rags, bandage over the eyes (no eye dots: he alone does not
    see the fire), staff, one hand reaching out."""
    base = dict(leg=0.82, crouch=0.08, torso=0.5, hunch=0.32, sh_w=0.19, hips=(0.14, 0.1), waist=(0.13, 0.09),
                chest=(0.16, 0.11), hip_w=0.14, foot_sep=0.13, step=0.1, neck=0.08, neck_r=0.035, head_r=0.085,
                head_h=0.22, head_fwd=0.8, upper=0.29, fore=0.27, arm_r=0.036, thigh_r=0.065, calf_r=0.042,
                hand=0.1, finger=0.09, palm_w=0.072, finger_r=0.009, eyes=False,
                m=dict(torso="M_Cloth", upper="M_Cloth", fore="M_Skin", hand="M_Skin", thigh="M_Dark", shin="M_Skin",
                       foot="M_Linen", head="M_Black"))
    sh = skeleton(**base)[1]
    st0, st1 = V((0.38, -0.5, 0.0)), V((0.27, -0.3, 1.38))
    P, J = skeleton(**base, hands={-1: sh["sh", -1] + V((0.04, -0.5, -0.08)), 1: lerp(st0, st1, 0.8)},
                    poles={-1: (-1, 0.2, -1), 1: (1, 0.4, -1)}, hdir={-1: (0, -1, -0.15), 1: (-0.3, -0.3, -1)},
                    curl={-1: 0.15, 1: 0.9})
    body(P, J)
    sp = J["spine"]
    skirt([(sp[1], (0.14, 0.1)), (V((0, sp[0].y, 0.5)), (0.22, 0.17)), (V((0, 0.02, 0.22)), (0.25, 0.2))], "M_Cloth",
          jag=0.13, n=14)
    rows = []                                                             # ragged shoulder cape
    for z, rx, ry in ((sp[4].z + 0.03, 0.2, 0.13), (sp[3].z - 0.04, 0.27, 0.19), (sp[2].z - 0.06, 0.3, 0.22)):
        row = []
        for i in range(13):
            ang = 2 * math.pi * i / 12
            jag = -0.1 * R(0.3, 1) if (i % 2 and z < sp[3].z - 0.05) else 0
            row.append(V((rx * math.cos(ang), sp[3].y + ry * math.sin(ang), z + jag)))
        rows.append(row)
    sheet(rows, "M_Dark")
    a, c = J["head_a"], J["head_c"]                                        # bandage
    tube([c + a * 0.0, c + a * 0.035], [(0.092, 0.1), (0.091, 0.099)], "M_Pale", n=12)
    tail = c + J["fwd"] * -0.09 + a * 0.02
    for s in (-1, 1):
        seg(tail, tail + V((s * 0.04, 0.1, -0.14)), 0.03, "M_Pale", h=0.006)
    tube([st0, lerp(st0, st1, 0.5) + V((0.02, 0, 0)), st1 + (st1 - st0).normalized() * 0.1], [0.018, 0.016, 0.02],
         "M_Wood", n=6)
    return P, J

def char_limbo():
    """Limbo: the figure by the scales. Pale robe, empty hood - no face, no eyes, no judgement. Waits."""
    base = dict(leg=1.15, torso=0.75, hunch=0.0, sh_w=0.2, hips=(0.15, 0.11), waist=(0.13, 0.1), chest=(0.18, 0.12),
                hip_w=0.14, foot_sep=0.1, neck=0.12, neck_r=0.035, head_r=0.085, head_h=0.24, head_fwd=0.12,
                upper=0.36, fore=0.33, arm_r=0.04, eyes=False, nohands=(-1, 1), head=False,
                m=dict(torso="M_Pale", upper="M_Pale", fore="M_Pale", thigh="M_Pale", shin="M_Pale", foot="M_Pale",
                       neck="M_Pale"))
    sp0 = skeleton(**base)[1]["spine"]
    meet = sp0[2] + V((0, -0.22, 0.05))
    P, J = skeleton(**base, hands={-1: meet + V((-0.03, 0, 0)), 1: meet + V((0.03, 0, 0))},
                    poles={-1: (-1, 0.3, -1), 1: (1, 0.3, -1)})
    body(P, J, legs=False)
    sp = J["spine"]
    skirt([(sp[2], (0.16, 0.12)), (V((0, 0, 0.8)), (0.24, 0.2)), (V((0, 0.03, 0.0)), (0.36, 0.33))], "M_Pale", n=16)
    for s in (-1, 1):
        E, Wr = J["elb", s], J["wr", s]
        tube([E, lerp(E, Wr, 0.5), Wr + (Wr - E).normalized() * 0.05], [0.06, 0.085, 0.11], "M_Pale", n=10,
             caps=(True, False))
    hood(P, J, "M_Pale", scale=1.45, drape=(0.25, 0.17), tip_back=0.08, tip_up=0.03, want_eyes=False, low=0.2)
    return P, J

CHARS = [  # id, builder, label, subtitle
    ("zhdan0", char_zhdan0, "Ждан · стадия 0", "«Пепел». Крестьянин, который хотел хлеба"),
    ("zhdan2", char_zhdan2, "Ждан · стадия 2", "«Головня». Кисти обуглены, трещины тлеют"),
    ("zhdan4", char_zhdan4, "Ждан · стадия 4", "«Костёр». Печь в груди, огонь в глазницах"),
    ("shadow", char_shadow, "Тень", "Огонь в мире: тень, которая шевелится не в такт"),
    ("korney", char_korney, "Палач Корней", "Не бегает — идёт. Всегда смотрит"),
    ("king", char_king, "Король", "Стена, а не финал. Чёрная корона"),
    ("peasant", char_peasant, "Крестьянин", "Враг: прёт толпой, вилы"),
    ("ragman", char_ragman, "Крестьянин с мокрой тряпкой", "Враг: голубое — бить первым"),
    ("guard", char_guard, "Стража", "Враг: строй, копьё и щит"),
    ("bucketguard", char_bucketguard, "Стража с ведром", "Враг: выплёскивает воду"),
    ("archer", char_archer, "Лучник", "Враг: держит дистанцию"),
    ("gavrila", char_gavrila, "Пекарь Гаврила", "Жертва №1: «Жданушка»"),
    ("agafya", char_agafya, "Вдова Агафья", "Жертва №5: крестится, шепчет"),
    ("nikodim", char_nikodim, "Поп Никодим", "Жертва №8: не боится"),
    ("efim", char_efim, "Слепой Ефим", "Жертва №18: не видит огня"),
    ("limbo", char_limbo, "Фигура Лимбо", "Без лица. Ждёт ответа"),
]

# =====================================================================
# materials: manga tone shader (same logic as the village kit) + flat emission for glow
# =====================================================================
TONES = {   # display value, screentone in light
    "M_Ink": (0.035, 0.0), "M_Black": (0.13, 0.0), "M_Char": (0.2, 0.0), "M_Dark": (0.27, 0.0), "M_Soot": (0.16, 0.3),
    "M_Skin": (0.42, 0.0), "M_Cloth": (0.47, 0.3), "M_Wood": (0.36, 0.0), "M_Steel": (0.6, 0.32), "M_Iron": (0.1, 0.0),
    "M_Linen": (0.66, 0.0), "M_Bread": (0.66, 0.34), "M_Ash": (0.72, 0.36), "M_Bone": (0.78, 0.3), "M_Pale": (0.9, 0.0),
    "M_Crown": (0.05, 0.0), "M_Void": (0.0, 0.0),
    "M_Stone": (0.36, 0.0), "M_Plank": (0.3, 0.0), "M_Timber": (0.08, 0.0), "M_Snow": (0.98, 0.0),
}
COLORED = {"M_Water": "#7FB2C4", "M_WaterDeep": "#3F6E7E", "M_Blood": "#A3121C", "M_BloodDark": "#5E0A10"}
EMIT = {"M_Glow": "#FFFFFF", "M_Ember": "#FF7A18", "M_EmberHot": "#FFD166", "M_EmberDeep": "#C24E00"}
INVERT = {"M_Crown": 1.0}        # black with white hatching where lit
BOOST = {"M_Snow": 1.4, "M_Pale": 1.1}
ALLM = list(TONES) + list(COLORED) + list(EMIT)

def lin(v):
    return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4

def hexcol(h):
    return tuple(lin(int(h[i:i + 2], 16) / 255) for i in (1, 3, 5))

TO_LIGHT = (V((0.05, 0.82, 0.57)) if CORRIDOR else V((0.55, -0.62, 0.58))).normalized()
T2, T3 = 0.22, 0.5
DOT_CELL, HATCH_CELL, HATCH_W = 5.0, 4.5, 1.4

def tone_group():
    g = bpy.data.node_groups.new("NG_MangaTone", "ShaderNodeTree")
    g.interface.new_socket("Base", in_out="INPUT", socket_type="NodeSocketColor")
    g.interface.new_socket("Screen", in_out="INPUT", socket_type="NodeSocketFloat")
    g.interface.new_socket("Boost", in_out="INPUT", socket_type="NodeSocketFloat").default_value = 1.0
    g.interface.new_socket("Invert", in_out="INPUT", socket_type="NodeSocketFloat")
    g.interface.new_socket("Shader", in_out="OUTPUT", socket_type="NodeSocketShader")
    N, L = g.nodes, g.links
    gi, go = N.new("NodeGroupInput"), N.new("NodeGroupOutput")

    def link(x, sock):
        if isinstance(x, (int, float, tuple)):
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

    geo = N.new("ShaderNodeNewGeometry")
    ndl = VM("DOT_PRODUCT", geo.outputs["Normal"], tuple(TO_LIGHT), out=1)
    dif = N.new("ShaderNodeBsdfDiffuse"); dif.inputs["Color"].default_value = (1, 1, 1, 1)
    s2r = N.new("ShaderNodeShaderToRGB"); L.new(dif.outputs[0], s2r.inputs[0])
    bw = N.new("ShaderNodeRGBToBW"); L.new(s2r.outputs["Color"], bw.inputs[0])
    ao = N.new("ShaderNodeAmbientOcclusion"); ao.inputs["Distance"].default_value = 0.6 if CORRIDOR else 0.18
    aoo = ao.outputs["AO"]
    aw = 0.55 if CORRIDOR else 0.0          # Eevee horizon-scan AO is blotchy at character scale
    s = M("MULTIPLY", M("MULTIPLY", ndl, M("ADD", M("MULTIPLY", aoo, aw), 1 - aw)), gi.outputs["Boost"])
    facing = M("GREATER_THAN", ndl, 0.2)
    lit = M("MULTIPLY", facing, M("GREATER_THAN", M("SUBTRACT", bw.outputs[0], M("MULTIPLY", ndl, 0.43)), 0.0))
    form = M("SUBTRACT", 1.0, facing)
    cast = M("SUBTRACT", M("SUBTRACT", 1.0, lit), form, clamp=True)

    tc = N.new("ShaderNodeTexCoord")
    P = VM("MULTIPLY", tc.outputs["Window"], (RX, RY, 0.0))

    def rotated(angle):
        r = N.new("ShaderNodeVectorRotate"); r.rotation_type = "Z_AXIS"
        r.inputs["Angle"].default_value = math.radians(angle)
        L.new(P, r.inputs["Vector"])
        return r.outputs[0]

    q = VM("FRACTION", VM("MULTIPLY", rotated(45), (1 / DOT_CELL, 1 / DOT_CELL, 0)))
    d = VM("LENGTH", VM("SUBTRACT", q, (0.5, 0.5, 0.0)), out=1)
    mr = N.new("ShaderNodeMapRange"); mr.clamp = True
    L.new(s, mr.inputs["Value"])
    mr.inputs["From Min"].default_value = T2; mr.inputs["From Max"].default_value = T3
    mr.inputs["To Min"].default_value = 0.5; mr.inputs["To Max"].default_value = 0.17
    dots = M("ADD", M("MULTIPLY", M("SUBTRACT", mr.outputs[0], d), DOT_CELL), 0.5, clamp=True)
    sheet_ = M("MULTIPLY", M("ADD", M("MULTIPLY", M("SUBTRACT", gi.outputs["Screen"], d), DOT_CELL), 0.5, clamp=True),
               M("GREATER_THAN", gi.outputs["Screen"], 0.01))
    dots = M("MAXIMUM", dots, sheet_)

    def hatch(angle):
        sep = N.new("ShaderNodeSeparateXYZ"); L.new(rotated(angle), sep.inputs[0])
        f = M("FRACT", M("DIVIDE", sep.outputs[0], HATCH_CELL))
        px = M("MULTIPLY", M("ABSOLUTE", M("SUBTRACT", f, 0.5)), HATCH_CELL)
        return M("SUBTRACT", HATCH_W / 2 + 0.5, px, clamp=True)

    h1 = hatch(-35)
    hx = M("MAXIMUM", h1, hatch(55))
    g2, g3 = M("GREATER_THAN", s, T2), M("GREATER_THAN", s, T3)
    ink_lit = M("ADD", M("MULTIPLY", M("SUBTRACT", 1.0, g2), h1),
                M("ADD", M("MULTIPLY", M("SUBTRACT", g2, g3), dots), M("MULTIPLY", g3, sheet_)))
    a1, a2 = (M("LESS_THAN", aoo, 0.62), M("LESS_THAN", aoo, 0.33)) if CORRIDOR else \
        (M("LESS_THAN", aoo, 0.12), M("LESS_THAN", aoo, 0.04))
    ink_form = M("MAXIMUM", M("MAXIMUM", h1, sheet_), M("MAXIMUM", M("MULTIPLY", a1, hx), a2))
    ink = M("ADD", M("ADD", M("MULTIPLY", lit, ink_lit), M("MULTIPLY", form, ink_form)), cast)
    k = M("SUBTRACT", 1.0, ink, clamp=True)
    # inverted (black metal): white hatching on the lit faces only
    white = M("MULTIPLY", M("MULTIPLY", lit, M("MAXIMUM", M("MULTIPLY", g2, h1), M("MULTIPLY", g3, hx))), 0.85)
    tone = VM("SCALE", gi.outputs["Base"], None)
    tone.node.inputs[3].default_value = 1.0
    sc = tone.node
    L.new(M("ADD", M("MULTIPLY", k, M("SUBTRACT", 1.0, gi.outputs["Invert"])), 0.0), sc.inputs[3])
    addw = VM("ADD", tone, None)
    cw = N.new("ShaderNodeCombineXYZ")
    wv = M("MULTIPLY", white, gi.outputs["Invert"])
    for i in range(3):
        L.new(wv, cw.inputs[i])
    L.new(cw.outputs[0], addw.node.inputs[1])
    inv_base = VM("SCALE", gi.outputs["Base"], None)
    L.new(gi.outputs["Invert"], inv_base.node.inputs[3])
    total = VM("ADD", addw, inv_base)
    em = N.new("ShaderNodeEmission"); L.new(total, em.inputs["Color"])
    L.new(em.outputs[0], go.inputs["Shader"])
    return g

def make_materials():
    g = tone_group()
    mats = {}
    for name in ALLM:
        m = bpy.data.materials.new(name); m.use_nodes = True
        nt = m.node_tree; nt.nodes.clear()
        if name in TONES:
            lv = lin(TONES[name][0]); col = (lv, lv, lv); screen = TONES[name][1]
        elif name in COLORED:
            col = hexcol(COLORED[name]); screen = 0.0
        else:
            col = hexcol(EMIT[name]); screen = 0.0
        m.diffuse_color = (*col, 1.0)
        out_e = nt.nodes.new("ShaderNodeOutputMaterial"); out_e.target = "EEVEE"
        if name in EMIT:
            em = nt.nodes.new("ShaderNodeEmission"); em.inputs["Color"].default_value = (*col, 1.0)
            nt.links.new(em.outputs[0], out_e.inputs["Surface"])
        else:
            grp = nt.nodes.new("ShaderNodeGroup"); grp.node_tree = g
            grp.inputs["Base"].default_value = (*col, 1.0)
            grp.inputs["Screen"].default_value = screen
            grp.inputs["Boost"].default_value = BOOST.get(name, 1.0)
            grp.inputs["Invert"].default_value = INVERT.get(name, 0.0)
            nt.links.new(grp.outputs[0], out_e.inputs["Surface"])
        out_c = nt.nodes.new("ShaderNodeOutputMaterial"); out_c.target = "CYCLES"
        pb = nt.nodes.new("ShaderNodeBsdfPrincipled")
        pb.inputs["Base Color"].default_value = (*col, 1.0); pb.inputs["Roughness"].default_value = 0.9
        if name in EMIT:
            pb.inputs["Emission Color"].default_value = (*col, 1.0); pb.inputs["Emission Strength"].default_value = 1.0
        nt.links.new(pb.outputs[0], out_c.inputs["Surface"])
        out_e.is_active_output = True
        mats[name] = m
    return mats

# =====================================================================
# mesh output
# =====================================================================
def collection(name, parent=None):
    c = bpy.data.collections.new(name); (parent or SC.collection).children.link(c)
    return c

def emit(name, coll, mats, src):
    verts, faces, mids, order = [], [], [], [m for m in ALLM if m in src]
    if not order:
        return None
    for mi, m in enumerate(order):
        vs, fs = src[m]; b = len(verts)
        verts.extend(vs); faces.extend(tuple(b + i for i in f) for f in fs); mids.extend([mi] * len(fs))
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    for m in order:
        me.materials.append(mats[m])
    me.polygons.foreach_set("material_index", mids)
    me.validate(clean_customdata=False)
    uv = me.uv_layers.new(name="UVMap")
    for p in me.polygons:
        nx, ny, nz = (abs(c) for c in p.normal)
        for li in p.loop_indices:
            co = me.vertices[me.loops[li].vertex_index].co
            uv.data[li].uv = (co.x, co.y) if nz >= max(nx, ny) else ((co.y, co.z) if nx >= ny else (co.x, co.z))
    me.update()
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    src.clear()
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    print(f"[mesh] {name}: {len(me.vertices)} verts, {tris} tris")
    return ob

mats = make_materials()
C_CHARS = collection("COL_Characters")
C_NOINK = collection("COL_NoInk")
C_SET = collection("COL_Set")
C_CAMS = collection("COL_Cameras")

built = {}
for cid, fn, label, sub in CHARS:
    if CORRIDOR and cid != "shadow":
        continue
    if ONLY and cid not in ONLY and not CORRIDOR:
        continue
    rng.seed(zlib.crc32(cid.encode()))
    GEO.clear(); GLOW.clear(); CUR[0] = GEO
    P, J = fn()
    cc = collection(f"CH_{cid}", C_CHARS)
    ob = emit(f"SM_Char_{cid}", cc, mats, GEO)
    gl = emit(f"SM_Char_{cid}_Glow", C_NOINK, mats, GLOW)
    bb = [V(c) for c in ob.bound_box]
    print(f"[size] {cid}: height {max(v.z for v in bb):.2f} m, width {max(v.x for v in bb) - min(v.x for v in bb):.2f} m")
    built[cid] = (ob, gl)

# =====================================================================
# corridor set (--corridor): stone passage, the Shadow in the far doorway against the snow outside
# =====================================================================
def build_corridor():
    rng.seed(31)
    W, L, H, DOOR = 1.3, 10.6, 3.6, (0.78, 3.15)
    for k in range(13):                                                  # floor planks with gaps
        x0 = -W + k * (2 * W / 13)
        y = 0.0
        while y < L:
            ln = R(1.4, 3.2); y1 = min(L, y + ln)
            box(x0 + 0.012, x0 + 2 * W / 13 - 0.012, y + 0.01, y1 - 0.01, -0.06, R(-0.012, 0.0), "M_Plank")
            y = y1
    box(-W - 0.6, W + 0.6, -0.6, L + 0.1, -0.3, -0.055, "M_Ink")           # backing: floor, walls, end wall
    for sd in (-1, 1):
        box(min(sd * (W + 0.28), sd * (W + 0.6)), max(sd * (W + 0.28), sd * (W + 0.6)), -0.6, L + 0.6, -0.3, H + 0.2,
            "M_Ink")
        box(min(sd * DOOR[0], sd * (W + 0.6)), max(sd * DOOR[0], sd * (W + 0.6)), L + 0.43, L + 0.6, -0.3, H + 0.2,
            "M_Ink")
    box(-DOOR[0], DOOR[0], L + 0.43, L + 0.6, DOOR[1], H + 0.2, "M_Ink")
    for side in (-1, 1):                                                 # stone walls
        z = 0.0
        while z < H:
            hgt = R(0.24, 0.36); y = R(-0.3, 0.0)
            while y < L:
                ln = R(0.35, 0.75); y1 = min(L, y + ln); dep = R(0.0, 0.05)
                xa = side * (W + 0.3); xb = side * (W - dep)
                box(min(xa, xb), max(xa, xb), y + 0.02, y1 - 0.02, z + 0.02, min(H, z + hgt) - 0.02, "M_Stone")
                y = y1
            z += hgt
    end = L                                                              # end wall with the doorway
    z = 0.0
    while z < H:
        hgt = R(0.24, 0.36)
        for xa, xb in ((-W - 0.3, -DOOR[0]), (DOOR[0], W + 0.3)) if z < DOOR[1] else ((-W - 0.3, W + 0.3),):
            x = xa
            while x < xb:
                ln = R(0.3, 0.6); x1 = min(xb, x + ln)
                box(x + 0.02, x1 - 0.02, end + R(-0.04, 0.0), end + 0.45, z + 0.02, min(H, z + hgt) - 0.02, "M_Stone")
                x = x1
        z += hgt
    for s in (-1, 1):                                                    # door posts and lintel
        box(s * DOOR[0] - 0.09, s * DOOR[0] + 0.09, end - 0.08, end + 0.46, 0.0, DOOR[1], "M_Timber")
    box(-DOOR[0] - 0.2, DOOR[0] + 0.2, end - 0.1, end + 0.46, DOOR[1], DOOR[1] + 0.22, "M_Timber")
    box(-W - 0.3, W + 0.3, -0.5, L + 0.5, H, H + 0.12, "M_Plank")      # ceiling
    y = 0.6
    while y < L:
        box(-W - 0.3, W + 0.3, y - 0.11, y + 0.11, H - 0.24, H, "M_Wood")
        y += 1.15
    box(-W - 0.3, W + 0.3, -0.6, -0.5, -0.1, H + 0.2, "M_Stone")        # wall behind the camera
    # hanging chain with a hook, a fallen stool, a barrel
    top = V((0.25, 5.2, H - 0.24))
    for k in range(18):
        p, q = top - Z * (0.075 * k), top - Z * (0.075 * (k + 1))
        if k % 2:
            seg(p, q, 0.035, "M_Iron", h=0.008)
        else:
            seg(p, q, 0.008, "M_Iron", h=0.035)
    hk = top - Z * 1.35
    tube([hk, hk - Z * 0.12, hk - Z * 0.2 + Y * 0.06, hk - Z * 0.14 + Y * 0.12], [0.012] * 4, "M_Iron", n=4)
    tube([V((-0.9, 3.4, 0.0)), V((-0.9, 3.4, 0.8))], [0.26, 0.28], "M_Wood", n=12)
    for z in (0.1, 0.7):
        tube([V((-0.9, 3.4, z)), V((-0.9, 3.4, z + 0.04))], [0.285, 0.285], "M_Iron", n=12)
    seg(V((0.7, 2.6, 0.04)), V((1.05, 2.9, 0.04)), 0.28, "M_Wood", h=0.05)
    for p in ((0.75, 2.5), (1.1, 2.9), (0.9, 3.2)):
        seg(V((p[0], p[1], 0.04)), V((p[0] + 0.1, p[1] - 0.25, 0.4)), 0.04, "M_Wood")
    emit("SM_Corridor", C_SET, mats, GEO)
    # outside: snow and two dead trees (no ink on the snow so there is no horizon line)
    box(-40, 40, L + 0.45, 80, -0.1, 0.0, "M_Snow", skip=("bottom",))
    emit("SM_OutsideSnow", C_NOINK, mats, GEO)
    for x, y, h in ((-3.0, 22.0, 6.0), (4.5, 30.0, 7.5)):
        p = V((x, y, 0))
        tube([p, p + V((0.2, 0, h * 0.5)), p + V((-0.1, 0, h))], [0.14, 0.09, 0.02], "M_Ink", n=6)
        for k in range(5):
            b = p + V((0.1, 0, h * (0.4 + 0.11 * k)))
            d = V((R(-1, 1), R(-0.3, 0.3), R(0.5, 1.0))).normalized()
            tube([b, b + d * h * 0.25], [0.04, 0.01], "M_Ink", n=4)
    emit("SM_OutsideTrees", C_SET, mats, GEO)

if CORRIDOR:
    build_corridor()
    sob, sgl = built["shadow"]
    for o in (sob, sgl):                                                 # mesh data moved into place, no transform
        if o:
            o.data.transform(__import__("mathutils").Matrix.Translation((0.05, 9.75, 0.0)))

# =====================================================================
# light, render settings, Freestyle
# =====================================================================
sun = bpy.data.lights.new("LGT_Sun", "SUN")
sun.energy = math.pi * 0.95
sun.angle = math.radians(0.3)
sun_ob = bpy.data.objects.new("LGT_Sun", sun); SC.collection.objects.link(sun_ob)
sun_ob.rotation_euler = (-TO_LIGHT).to_track_quat("-Z", "Y").to_euler()
world = bpy.data.worlds.new("WRL"); SC.world = world; world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (0, 0, 0, 1)

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
fs.crease_angle = math.radians(125)
fs.use_culling = True
for ls in list(fs.linesets):
    fs.linesets.remove(ls)

def ink_style(name, thick, wobble):
    st = bpy.data.linestyles.new(name)
    st.color = (0, 0, 0); st.thickness = thick; st.thickness_position = "CENTER"; st.caps = "ROUND"
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
ls.linestyle = ink_style("INK_Contour", 4.6, 1.0)
ls2 = fs.linesets.new("LS_Inner")
ls2.select_by_visibility = True; ls2.visibility = "VISIBLE"; ls2.select_by_edge_types = True
ls2.select_silhouette = ls2.select_border = ls2.select_external_contour = False
ls2.select_crease = True
ls2.linestyle = ink_style("INK_Inner", 2.0, 0.6)
for l in (ls, ls2):
    l.select_by_collection = True; l.collection = C_NOINK; l.collection_negation = "EXCLUSIVE"

def camera(name, pos, tgt, ortho=None, lens=50):
    cd = bpy.data.cameras.new(name); cd.clip_end = 300; cd.lens = lens
    if ortho:
        cd.type = "ORTHO"; cd.ortho_scale = ortho
    co = bpy.data.objects.new(name, cd); C_CAMS.objects.link(co)
    co.location = pos
    co.rotation_euler = (V(tgt) - V(pos)).to_track_quat("-Z", "Y").to_euler()
    return co

YAW, ELEV, FRAME_H, FRAME_C = math.radians(28), math.radians(4), 3.3, 1.55
d = V((-math.sin(YAW) * math.cos(ELEV), -math.cos(YAW) * math.cos(ELEV), math.sin(ELEV)))
cam_card = camera("CAM_Card", V((0, 0, FRAME_C)) + d * 30, (0, 0, FRAME_C), ortho=FRAME_H)
cam_corr = camera("CAM_Corridor", (0.42, 2.3, 1.02), (0.05, 10.0, 1.78), lens=30)

# =====================================================================
# save, export, render
# =====================================================================
os.makedirs(RAW, exist_ok=True)
os.makedirs(os.path.join(OUT, "fbx"), exist_ok=True)
blend = os.path.join(OUT, "corridor.blend" if CORRIDOR else "characters.blend")
bpy.ops.wm.save_as_mainfile(filepath=blend)
print("[saved]", blend)

if not CORRIDOR:
    for cid, (ob, gl) in built.items():
        for o in bpy.context.view_layer.objects:
            o.select_set(o in (ob, gl))
        path = os.path.join(OUT, "fbx", f"SM_Char_{cid}.fbx")
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                                 apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                                 bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                                 use_triangles=True)
    print("[saved] fbx/SM_Char_*.fbx")

if not NORENDER:
    if CORRIDOR:
        SC.camera = cam_corr
        SC.render.filepath = os.path.join(RAW, "corridor.png")
        bpy.ops.render.render(write_still=True)
        print("[render] corridor")
    else:
        SC.camera = cam_card
        allobs = [o for pair in built.values() for o in pair if o]
        for cid, (ob, gl) in built.items():
            for o in allobs:
                o.hide_render = o not in (ob, gl)
            SC.render.filepath = os.path.join(RAW, f"{cid}.png")
            bpy.ops.render.render(write_still=True)
            print("[render]", cid)
