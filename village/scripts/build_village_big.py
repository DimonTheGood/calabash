"""Большая деревня: расширяет village.blend примерно в 5 раз — новые улицы, десятки домов, лес, заборы, пропсы.

Запуск из корня репо:
    blender -b village/village.blend --factory-startup -P village/scripts/build_village_big.py
    blender -b village/village_big.blend --factory-startup -P village/scripts/export_village_map.py

Первая команда пишет village/village_big.blend (village.blend не меняется), вторая — village/fbx/Village_Map.fbx.

Что делает:
  - старое ядро (главная дорога, 8 домов, колодец, кладбище, заборы) остаётся как есть;
  - дальние «туманные» дома и внешнее кольцо деревьев убираются: на их месте теперь улицы;
  - новые улицы: северная и южная вдоль деревни, западная и восточная поперёк, переулки к главной дороге
    и дальний хутор на севере. Улицы — утоптанный снег с колеями, как у главной дороги;
  - вдоль улиц ставятся дома из того же набора (8 типов, linked-копии с крышами в снегу) фасадом к дороге,
    без пересечений с дорогами, старыми домами и друг другом;
  - у части домов — бочки, ящики, дрова, пни; вдоль улиц — заборы с разрывами;
  - вокруг — лес: деревья не ставятся на дороги и дома.
Всё детерминировано (сиды), повторный запуск даёт ту же деревню.
"""
import bpy
import bmesh
import math
import os
import random
from mathutils import Vector as V, Matrix

R = random.Random(42)
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "village_big.blend")


def road_y(x):                                       # главная дорога из build_village.py
    return 1.4 * math.sin(x / 17.0)


def coll(name):
    return bpy.data.collections[name]


C_HOUSES, C_PROPS, C_NATURE, C_GROUND = coll("COL_Houses"), coll("COL_Props"), coll("COL_Nature"), coll("COL_Ground")

# ------------------------------------------------------------------ убрать то, что мешает новым улицам
removed = 0
for ob in list(C_HOUSES.objects):
    if "_Far_" in ob.name:
        bpy.data.objects.remove(ob); removed += 1
for ob in list(C_NATURE.objects):
    if ob.name.startswith("SM_Tree_") and ob.data.name.startswith("SM_Tree_"):
        n = int(ob.name.split("_")[-1]) if ob.name.split("_")[-1].isdigit() else 0
        if n > 15:                                   # внешнее кольцо (внутренние 15 — у домов, остаются)
            bpy.data.objects.remove(ob); removed += 1
print(f"[big] убрано объектов: {removed}")


def world_aabb(ob, pad=0.0):
    bb = [ob.matrix_world @ V(c) for c in ob.bound_box]
    return (min(v.x for v in bb) - pad, max(v.x for v in bb) + pad, min(v.y for v in bb) - pad, max(v.y for v in bb) + pad)


# ------------------------------------------------------------------ препятствия старого ядра
OLD_BOXES = [world_aabb(o, 1.0) for o in C_HOUSES.objects]
OLD_BOXES += [world_aabb(o, 0.8) for o in C_PROPS.objects if o.name.startswith(("SM_Well", "SM_Scarecrow", "SM_Graveyard"))]
OLD_BOXES += [(22.5, 34.5, 6.0, 15.8), (20.5, 36.5, -24.0, -9.0), (-20.0, -6.0, 9.5, 21.0),   # заборы ядра
              (-47.3, -45.9, -28.0, 28.0)]                                                     # частокол
OLD_TREES = [(o.matrix_world.translation.x, o.matrix_world.translation.y) for o in C_NATURE.objects if o.name.startswith("SM_Tree_")]


# ------------------------------------------------------------------ улицы
def wobble(pts, amp, seed, step=2.0):
    """Ломаная → плотная кривая с лёгким изгибом (деревенская улица не по линейке)."""
    rr = random.Random(seed)
    ph1, ph2 = rr.uniform(0, 6.3), rr.uniform(0, 6.3)
    out = []
    for (xa, ya), (xb, yb) in zip(pts, pts[1:]):
        L = math.hypot(xb - xa, yb - ya)
        n = max(1, int(L / step))
        nx, ny = -(yb - ya) / L, (xb - xa) / L
        for i in range(n):
            t = i / n
            s = len(out) * step
            w = amp * (math.sin(s / 23.0 + ph1) * 0.7 + math.sin(s / 9.0 + ph2) * 0.3)
            edge = min(1.0, t * L / 8.0, (1 - t) * L / 8.0) if len(pts) == 2 else 1.0
            out.append((xa + (xb - xa) * t + nx * w * edge, ya + (yb - ya) * t + ny * w * edge))
    out.append(pts[-1])
    return out


def main_road_pt(x):
    return (x, road_y(x))


# (имя, точки, полуширина, ставить дома)
STREETS = [
    ("North", wobble([(-72.0, 41.0), (-20.0, 44.0), (30.0, 41.0), (66.0, 43.0)], 1.6, 1), 1.7, True),
    ("South", wobble([(-72.0, -40.0), (-15.0, -43.0), (25.0, -40.0), (66.0, -42.0)], 1.6, 2), 1.7, True),
    ("West", wobble([(-72.0, -40.0), (-68.0, road_y(-68.0)), (-72.0, 41.0)], 1.2, 3), 1.5, True),
    ("East", wobble([(66.0, -42.0), (62.0, 0.0), (66.0, 43.0)], 1.2, 4), 1.5, True),
    ("LaneNW", wobble([main_road_pt(-38.0), (-38.5, 41.5)], 0.6, 5), 1.25, True),
    ("LaneNE", wobble([main_road_pt(17.5), (17.0, 41.5)], 0.6, 6), 1.25, True),
    ("LaneS", wobble([main_road_pt(-15.0), (-15.5, -42.5)], 0.6, 7), 1.25, True),
    ("LaneFar", wobble([(-8.0, 43.5), (-6.0, 78.0)], 0.8, 9), 1.25, True),
    ("Hamlet", wobble([(-46.0, 76.0), (-6.0, 79.0), (40.0, 75.0)], 1.4, 10), 1.5, True),
    ("WestOut", wobble([(-86.0, road_y(-86.0)), (-112.0, 2.0)], 0.8, 11), 1.4, True),
]
ROAD_SEGS = []
for name, pts, hw, _ in STREETS:
    for a, b in zip(pts, pts[1:]):
        ROAD_SEGS.append((a, b, hw))
for x in range(-86, 33, 2):                           # главная дорога ядра
    ROAD_SEGS.append(((x, road_y(x)), (x + 2, road_y(x + 2)), 1.9))


def seg_dist(p, a, b):
    ax, ay = a; bx, by = b; px, py = p
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    t = 0.0 if L2 < 1e-9 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / L2))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def road_clear(p, margin):
    return all(seg_dist(p, a, b) > hw + margin for a, b, hw in ROAD_SEGS)


# ------------------------------------------------------------------ меш улиц (как build_road: снег + колеи)
def ribbon(bm, pts, off0, off1, z, mi, jit, rr):
    verts = []
    for i, (x, y) in enumerate(pts):
        a = pts[max(0, i - 1)]; b = pts[min(len(pts) - 1, i + 1)]
        tx, ty = b[0] - a[0], b[1] - a[1]
        L = math.hypot(tx, ty) or 1.0
        nx, ny = -ty / L, tx / L
        o0 = off0 + rr.uniform(-jit, jit); o1 = off1 + rr.uniform(-jit, jit)
        verts.append((bm.verts.new((x + nx * o0, y + ny * o0, z)), bm.verts.new((x + nx * o1, y + ny * o1, z))))
    for (a0, a1), (b0, b1) in zip(verts, verts[1:]):
        f = bm.faces.new((a0, b0, b1, a1))
        f.material_index = mi


def resample(pts, step):
    out = [pts[0]]
    acc = 0.0
    for a, b in zip(pts, pts[1:]):
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        d = step - acc
        while d < L:
            t = d / L
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
            d += step
        acc = L - (d - step)
    out.append(pts[-1])
    return out


mesh = bpy.data.meshes.new("SM_Streets")
road_mats = [bpy.data.materials["M_SnowShade"], bpy.data.materials["M_Grime"]]
for m in road_mats:
    mesh.materials.append(m)
bm = bmesh.new()
rr = random.Random(77)
for name, pts, hw, _ in STREETS:
    p = resample(pts, 0.7)
    ribbon(bm, p, -hw, hw, 0.006, 0, 0.15, rr)
    k = hw / 1.9
    for o in (-0.66, 0.6):
        ribbon(bm, resample(pts, 0.5), (o - 0.12) * k, (o + 0.12) * k, 0.010, 1, 0.04, rr)
bm.to_mesh(mesh); bm.free()
streets = bpy.data.objects.new("SM_Streets", mesh)
C_GROUND.objects.link(streets)


# ------------------------------------------------------------------ дома вдоль улиц
KIT = {}
for ob in coll("COL_Kit").objects:
    if ob.name.startswith("SM_House_") and "_Snow_" not in ob.name:
        t = ob.name[len("SM_House_"):].split("_Kit")[0]
        snow = bpy.data.objects.get(f"SM_House_{t}_Snow_Kit_01")
        xs, ys = [], []
        for src in (ob, snow):
            if src is None:
                continue
            for c in src.bound_box:
                xs.append(c[0]); ys.append(c[1])
        KIT[t] = dict(house=ob.data, snow=snow.data if snow else None, x0=min(xs), x1=max(xs), y0=min(ys), y1=max(ys))
print("[big] типы домов:", {k: (round(v["x1"] - v["x0"], 1), round(v["y1"] - v["y0"], 1)) for k, v in KIT.items()})

WEIGHTS = [("Hut", 5), ("Boarded", 4), ("Tall", 3), ("Smithy", 2), ("Barn", 2), ("Apothecary", 2), ("Gatehouse", 0.4), ("Chapel", 0.3)]
PLACED = []                                           # (corners, center) повёрнутых прямоугольников


def pick_type():
    tot = sum(w for _, w in WEIGHTS)
    r = R.uniform(0, tot)
    for t, w in WEIGHTS:
        r -= w
        if r <= 0:
            return t
    return "Hut"


def rect_world(M, x0, x1, y0, y1, pad):
    return [(M @ V((x, y, 0))).to_2d() for x, y in ((x0 - pad, y0 - pad), (x1 + pad, y0 - pad), (x1 + pad, y1 + pad), (x0 - pad, y1 + pad))]


def sat(a, b):
    for poly in (a, b):
        for i in range(4):
            p, q = poly[i], poly[(i + 1) % 4]
            n = V((-(q.y - p.y), q.x - p.x))
            pa = [n.dot(v) for v in a]; pb = [n.dot(v) for v in b]
            if max(pa) < min(pb) or max(pb) < min(pa):
                return False
    return True


def box_poly(b):
    x0, x1, y0, y1 = b
    return [V((x0, y0)), V((x1, y0)), V((x1, y1)), V((x0, y1))]


OLD_POLYS = [box_poly(b) for b in OLD_BOXES]


def fits(poly):
    for q in OLD_POLYS:
        if sat(poly, q):
            return False
    for q in PLACED:
        if sat(poly, q):
            return False
    # дорога: углы, середины сторон и центр
    pts = list(poly) + [(poly[i] + poly[(i + 1) % 4]) / 2 for i in range(4)] + [sum(poly, V((0, 0))) / 4]
    if not all(road_clear((p.x, p.y), 0.4) for p in pts):
        return False
    for tx, ty in OLD_TREES:
        if min(abs(tx - p.x) + abs(ty - p.y) for p in pts) < 1.0 or (min(p.x for p in poly) < tx < max(p.x for p in poly) and min(p.y for p in poly) < ty < max(p.y for p in poly)):
            return False
    return True


COUNT = {}


def place(data, M, collection, base):
    COUNT[base] = COUNT.get(base, 0) + 1
    ob = bpy.data.objects.new(f"{base}_Big_{COUNT[base]:02d}", data)
    collection.objects.link(ob)
    ob.matrix_world = M
    return ob


PROPS = {n: bpy.data.objects[f"SM_{n}"].data for n in ("Barrel", "CrateA", "CrateB", "Firewood", "Stump")}
HOUSE_FRONTS = []                                     # для заборов: участки, где стоят дома
houses_new = 0
for name, pts, hw, build in STREETS:
    if not build:
        continue
    dense = resample(pts, 0.5)
    L = 0.5 * (len(dense) - 1)
    for side in (1, -1):
        s = R.uniform(3.0, 7.0)
        while s < L - 3.0:
            i = int(s / 0.5)
            a, b = dense[max(0, i - 2)], dense[min(len(dense) - 1, i + 2)]
            tx, ty = b[0] - a[0], b[1] - a[1]
            tl = math.hypot(tx, ty) or 1.0
            tx, ty = tx / tl, ty / tl
            nx, ny = -ty * side, tx * side             # от дороги к дому
            t = pick_type()
            k = KIT[t]
            yaw = math.atan2(-nx, ny)                  # фасад (−Y дома) смотрит на дорогу
            yaw += R.uniform(-0.06, 0.06)
            gap = R.uniform(1.6, 3.4)
            d = hw + gap - k["y0"]
            cx, cy = dense[i][0] + nx * d, dense[i][1] + ny * d
            M = Matrix.Translation((cx, cy, 0.0)) @ Matrix.Rotation(yaw, 4, "Z")
            poly = rect_world(M, k["x0"], k["x1"], k["y0"], k["y1"], 0.9)
            width = k["x1"] - k["x0"]
            if fits(poly):
                PLACED.append(poly)
                place(k["house"], M, C_HOUSES, f"SM_House_{t}")
                if k["snow"] is not None:
                    place(k["snow"], M, C_HOUSES, f"SM_House_{t}_Snow")
                houses_new += 1
                HOUSE_FRONTS.append((name, side, s - width / 2 - 1.0, s + width / 2 + 1.0))
                # пропсы у фасада
                if R.random() < 0.55:
                    for _ in range(R.randint(1, 3)):
                        pn = R.choice(list(PROPS))
                        px = R.choice((k["x0"] + 0.6, k["x1"] - 0.6)) + R.uniform(-0.3, 0.3)
                        py = k["y0"] - R.uniform(0.5, 1.1)
                        place(PROPS[pn], M @ Matrix.Translation((px, py, 0)) @ Matrix.Rotation(R.uniform(0, 6.3), 4, "Z"), C_PROPS, f"SM_{pn}")
                s += width + R.uniform(2.5, 7.0)
            else:
                s += 1.5
print(f"[big] новых домов: {houses_new}")


# ------------------------------------------------------------------ заборы вдоль улиц (где нет домов)
fmesh = bpy.data.meshes.new("SM_StreetFences")
fmesh.materials.append(bpy.data.materials["M_Wood"])
fmesh.materials.append(bpy.data.materials["M_Snow"])
bm = bmesh.new()


def beam(p0, p1, w, h, z0, mi, tilt=0.0):
    d = (p1 - p0)
    L = d.length
    if L < 1e-4:
        return
    d.normalize()
    n = V((-d.y, d.x, 0.0)) * (w / 2)
    up = V((0, 0, h))
    base = [p0 + n, p1 + n, p1 - n, p0 - n]
    vs = [bm.verts.new(v + V((0, 0, z0))) for v in base] + [bm.verts.new(v + V((0, 0, z0)) + up) for v in base]
    for f in ((0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)):
        bm.faces.new([vs[i] for i in f]).material_index = mi


fr = random.Random(55)
fence_runs = 0
for name, pts, hw, build in STREETS:
    if not build or name.startswith("Lane"):
        continue
    dense = resample(pts, 0.5)
    L = 0.5 * (len(dense) - 1)
    for side in (1, -1):
        blocked = [(a, b) for n, sd, a, b in HOUSE_FRONTS if n == name and sd == side]
        s, run = 2.0, []
        while s < L - 2.0:
            i = int(s / 0.5)
            a, b = dense[max(0, i - 1)], dense[min(len(dense) - 1, i + 1)]
            tx, ty = b[0] - a[0], b[1] - a[1]
            tl = math.hypot(tx, ty) or 1.0
            nx, ny = -ty / tl * side, tx / tl * side
            p = V((dense[i][0] + nx * (hw + 1.0), dense[i][1] + ny * (hw + 1.0), 0.0))
            free = not any(a0 < s < b0 for a0, b0 in blocked) and fr.random() > 0.04 and road_clear((p.x, p.y), 0.3) \
                and not any(sat(box_poly((p.x - 0.3, p.x + 0.3, p.y - 0.3, p.y + 0.3)), q) for q in OLD_POLYS)
            if free:
                run.append(p)
            elif run:
                if len(run) > 1:
                    fence_runs += 1
                    for q0, q1 in zip(run, run[1:]):
                        if fr.random() < 0.12:
                            continue                   # выпавшие пролёты
                        dd = (q1 - q0).normalized() * 0.07
                        beam(q0 - dd, q0 + dd, 0.14, fr.uniform(1.0, 1.3), 0.0, 0)                            # столб
                        for zr in (0.45, 0.9):
                            beam(q0, q1, 0.06, 0.09, zr + fr.uniform(-0.05, 0.05), 0)
                        beam(q0, q1, 0.09, 0.05, 0.99, 1)                                               # снег на жерди
                run = []
            s += 1.8
bm.to_mesh(fmesh); bm.free()
place(fmesh, Matrix.Identity(4), C_PROPS, "SM_StreetFences")
print(f"[big] заборов: {fence_runs}")


# ------------------------------------------------------------------ лес
TREES = [bpy.data.objects[f"SM_Tree_{c}"].data for c in "ABC"]
trees = list(OLD_TREES)
tr = random.Random(17)
added = 0


def tree_ok(x, y, rad):
    if not road_clear((x, y), 3.0):
        return False
    pt = box_poly((x - 1.2, x + 1.2, y - 1.2, y + 1.2))
    if any(sat(pt, q) for q in OLD_POLYS) or any(sat(pt, q) for q in PLACED):
        return False
    return all(math.hypot(x - tx, y - ty) > rad for tx, ty in trees)


for zone, n, rad in (((-100, 95, -65, 100), 90, 7.5),          # между улицами и у хутора
                     ((-170, 150, -130, 165), 200, 9.0)):      # лес вокруг
    k = tries = 0
    while k < n and tries < 20000:
        tries += 1
        x, y = tr.uniform(zone[0], zone[1]), tr.uniform(zone[2], zone[3])
        if n == 200 and -95 < x < 90 and -60 < y < 95 and tr.random() < 0.85:
            continue                                   # внешний лес гуще по краям
        if not tree_ok(x, y, rad):
            continue
        trees.append((x, y)); k += 1; added += 1
        s = tr.uniform(0.85, 1.35)
        place(tr.choice(TREES), Matrix.Translation((x, y, 0)) @ Matrix.Rotation(tr.uniform(0, 6.3), 4, "Z") @ Matrix.Scale(s, 4), C_NATURE, "SM_Tree")
print(f"[big] деревьев добавлено: {added}, всего {len(trees)}")

# вороны на некоторых новых крышах
crow = bpy.data.objects["SM_Crow"].data
cr = random.Random(21)
for ob in cr.sample([o for o in C_HOUSES.objects if "_Big_" in o.name and "_Snow" not in o.name], 10):
    bb = [ob.matrix_world @ V(c) for c in ob.bound_box]
    top = max(v.z for v in bb)
    c = sum(bb, V()) / 8
    place(crow, Matrix.Translation((c.x + cr.uniform(-1, 1), c.y + cr.uniform(-0.5, 0.5), top - 0.05)) @ Matrix.Rotation(cr.uniform(0, 6.3), 4, "Z"), C_PROPS, "SM_Crow")

xs = [p[0] for _, pts, _, _ in STREETS for p in pts]; ys = [p[1] for _, pts, _, _ in STREETS for p in pts]
print(f"[big] улицы: x {min(xs):.0f}…{max(xs):.0f}, y {min(ys):.0f}…{max(ys):.0f}; домов всего: "
      f"{len([o for o in C_HOUSES.objects if '_Snow' not in o.name])}")
bpy.ops.wm.save_as_mainfile(filepath=os.path.abspath(OUT), compress=True)
print(f"[big] → {os.path.abspath(OUT)}")
