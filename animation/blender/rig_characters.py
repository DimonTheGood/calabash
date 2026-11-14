"""Calabash: rigs and animates the characters for Unity.

The characters are built by the same code as characters/scripts/build_characters.py (that file is only
read, never changed): its functions are loaded, every character is built again, and the joint positions
the builder computes (skeleton() -> J) become the bones. So the rig matches every body exactly.

    blender -b --factory-startup -P animation/blender/rig_characters.py -- [out dir] [--only=hero,peasant] [--preview=dir] [--fp-arms]

Output (default out dir: animation/):
  fbx/SK_Char_<id>.fbx   armature "Rig" + skinned mesh "Body" + all clips (one FBX take per clip)
  fbx/SM_Zhdan_FPArms.fbx  Zhdan's arms for the first-person view: upper arm, forearm, fist per side,
                           each piece hangs along -Z from its joint at the origin (a hanging arm)

Characters: "hero" = Zhdan stage 0 without the bread, both hands free and clenched (the player),
plus the NPCs from the roster. Front faces -Y, metres, pivot at the feet.
Clips are made procedurally: poses are written as rotations around armature axes (X = sideways,
Y = front-back, Z = up) applied on top of each bone's rest orientation.
"""
import bpy, math, os, sys, zlib
from mathutils import Vector as V, Euler, Quaternion
from mathutils.kdtree import KDTree

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
SRC = os.path.join(ROOT, "characters", "scripts", "build_characters.py")

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = next((a for a in argv if not a.startswith("--")), os.path.join(ROOT, "animation"))
FP_ONLY = "--fp-arms" in argv
ONLY = next((a.split("=", 1)[1].split(",") for a in argv if a.startswith("--only=")), None)
PREVIEW = next((a.split("=", 1)[1] for a in argv if a.startswith("--preview=")), None)
FPS = 30

# ---------------------------------------------------------------------------------------------------
# load the character builder's functions (everything before its main loop)
# ---------------------------------------------------------------------------------------------------
code = open(SRC, encoding="utf-8").read()
code = code[:code.index("\nmats = make_materials()")]
saved_argv = sys.argv
sys.argv = [sys.argv[0], "--", OUT, "--norender"]
B = {"__name__": "build_characters", "__file__": SRC}
exec(compile(code, SRC, "exec"), B)
sys.argv = saved_argv

SC = bpy.context.scene
SC.render.fps = FPS
GEO, GLOW, BODY, CLOTH = B["GEO"], B["GLOW"], B["BODY"], B["CLOTH"]
lerp, tube, blob, hand, layer = B["lerp"], B["tube"], B["blob"], B["hand"], B["layer"]
mats = B["make_materials"]()


def char_hero():
    """Zhdan, stage 0, as the player: same body and clothes as char_zhdan0, but no bread,
    both arms hanging a little away from the body (clean skinning) and fists clenched."""
    ZH = B["ZH"]
    m = dict(torso="M_Cloth", upper="M_Cloth", fore="M_Cloth", hand="M_Soot", thigh="M_Dark",
             shin=("M_Skin", "M_Dark"), foot="M_Wood", head="M_Black", neck="M_Skin")
    P, J = B["skeleton"](**ZH, arm_out=0.11, curl={-1: 0.9, 1: 0.9}, m=m)
    B["body"](P, J)
    sp = J["spine"]
    B["skirt"]([(sp[1] + V((0, 0, 0.02)), (0.14, 0.095)), (V((0, sp[0].y + 0.01, 0.74)), (0.19, 0.13))], "M_Cloth", jag=0.06)
    B["rope_ring"](sp[1], 0.135, 0.092, 0.011, "M_Wood")
    B["seg"](sp[1] + V((0.06, -0.08, 0)), sp[1] + V((0.09, -0.1, -0.2)), 0.012, "M_Wood")
    k, a = J["knee", -1], J["ank", -1]
    tube([k, lerp(k, a, 0.38)], [ZH["calf_r"] * 1.45, ZH["calf_r"] * 1.35], "M_Dark", caps=(True, False))
    for s in (-1, 1):
        a = J["ank", s]
        for h in (0.03, 0.08):
            B["rope_ring"](a + V((0, 0, h)), ZH["calf_r"] * 0.8, ZH["calf_r"] * 0.8, 0.006, "M_Wood", n=8)
    B["zhdan_hair"](J, P)
    return P, J


BUILDERS = {"hero": char_hero}
for cid, fn, _label, _sub in B["CHARS"]:
    if not cid.startswith("zhdan"):
        BUILDERS[cid] = fn

# ---------------------------------------------------------------------------------------------------
# rig
# ---------------------------------------------------------------------------------------------------
SIDES = ((1, "L"), (-1, "R"))       # +X is the character's left (it faces -Y)


def bone_defs(P, J):
    sp = J["spine"]
    d = [("Root", V((0, 0, 0)), V((0, 0, 0.25)), None),
         ("Hips", sp[0], sp[1], "Root"),
         ("Spine", sp[1], sp[2], "Hips"),
         ("Spine1", sp[2], sp[3], "Spine"),
         ("Chest", sp[3], sp[4], "Spine1"),
         ("Neck", J["neck_b"], J["head_b"], "Chest"),
         ("Head", J["head_b"], J["head_t"], "Neck")]
    for s, x in SIDES:
        foot_tip = J["ank", s] + V((s * 0.03, -P["foot_l"] * 0.8, -0.06))
        d += [(f"Shoulder.{x}", sp[4] + V((s * 0.04, 0, -P["sh_drop"])), J["sh", s], "Chest"),
              (f"UpperArm.{x}", J["sh", s], J["elb", s], f"Shoulder.{x}"),
              (f"Forearm.{x}", J["elb", s], J["wr", s], f"UpperArm.{x}"),
              (f"Hand.{x}", J["wr", s], J["wr", s] + J["hd", s] * P["hand"] * 0.9, f"Forearm.{x}"),
              (f"Thigh.{x}", J["hip", s], J["knee", s], "Hips"),
              (f"Shin.{x}", J["knee", s], J["ank", s], f"Thigh.{x}"),
              (f"Foot.{x}", J["ank", s], foot_tip, f"Shin.{x}")]
    return d


def make_rig(coll, P, J):
    ad = bpy.data.armatures.new("Rig")
    rig = bpy.data.objects.new("Rig", ad)
    coll.objects.link(rig)
    vl = bpy.context.view_layer
    for o in vl.objects:
        o.select_set(False)
    vl.objects.active = rig
    rig.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for name, h, t, parent in bone_defs(P, J):
        eb = ad.edit_bones.new(name)
        eb.head, eb.tail = V(h), V(t)
        if (eb.tail - eb.head).length < 0.01:
            eb.tail = eb.head + V((0, 0, 0.05))
        eb.roll = 0.0
        if parent:
            eb.parent = ad.edit_bones[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    ad.bones["Root"].use_deform = False
    return rig


def segment_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (a + ab * t - p).length


def vertex_weights(ob):
    names = {g.index: g.name for g in ob.vertex_groups}
    return [{names[g.group]: g.weight for g in v.groups if g.weight > 1e-3} for v in ob.data.vertices]


def set_weights(ob, weights):
    for w in weights:
        for name in w:
            if ob.vertex_groups.get(name) is None:
                ob.vertex_groups.new(name=name)
    groups = {g.name: g for g in ob.vertex_groups}
    for i, w in enumerate(weights):
        for name, val in w.items():
            groups[name].add([i], val, "REPLACE")


def top(w, n=4):
    items = sorted(w.items(), key=lambda kv: -kv[1])[:n]
    s = sum(v for _, v in items) or 1.0
    return {k: v / s for k, v in items}


def loose_parts(me):
    parent = list(range(len(me.vertices)))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for e in me.edges:
        a, b = find(e.vertices[0]), find(e.vertices[1])
        if a != b:
            parent[a] = b
    parts = {}
    for i in range(len(me.vertices)):
        parts.setdefault(find(i), []).append(i)
    return list(parts.values())


def clusters(me, parts, touch=0.012):
    """Loose parts that touch each other are one prop (pitchfork shaft + tines, hair spikes)."""
    owner = {}
    kd = KDTree(len(me.vertices))
    for pi, part in enumerate(parts):
        for i in part:
            kd.insert(me.vertices[i].co, i); owner[i] = pi
    kd.balance()
    parent = list(range(len(parts)))
    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]; i = parent[i]
        return i
    for pi, part in enumerate(parts):
        for i in part[::2]:
            for co, j, dist in kd.find_range(me.vertices[i].co, touch):
                a, b = find(pi), find(owner[j])
                if a != b:
                    parent[a] = b
    groups = {}
    for pi, part in enumerate(parts):
        groups.setdefault(find(pi), []).extend(part)
    return list(groups.values())


LEG_BONES = ("Thigh", "Shin", "Foot")
CLOTH_LEG = 0.3        # how much cloth below the waist follows the legs


def skin(rig, body, extras, knee_z):
    """Body: Blender's bone-heat weights. Cloth: weights copied from the nearest body surface.
    Gear and glow (props, belts, hair, eyes): every loose part follows one bone rigidly."""
    vl = bpy.context.view_layer
    for o in vl.objects:
        o.select_set(False)
    body.select_set(True); rig.select_set(True); vl.objects.active = rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")

    bones = [(b.name, b.head_local.copy(), b.tail_local.copy()) for b in rig.data.bones if b.use_deform]
    bw = vertex_weights(body)
    missing = 0
    for i, v in enumerate(body.data.vertices):              # bone heat leaves some islands empty
        if not bw[i]:
            missing += 1
            bw[i] = {min(bones, key=lambda b: segment_dist(v.co, b[1], b[2]))[0]: 1.0}
    if missing:
        set_weights(body, bw)
        print(f"[rig]   {missing} body verts weighted by distance")

    kd = KDTree(len(body.data.vertices))
    for i, v in enumerate(body.data.vertices):
        kd.insert(v.co, i)
    kd.balance()

    for ob, kind in extras:
        me = ob.data
        if kind == "cloth":
            ws = []
            for v in me.vertices:
                acc = {}
                for co, idx, dist in kd.find_n(v.co, 6):
                    k = 1.0 / (dist + 0.01) ** 2
                    for name, val in bw[idx].items():
                        if name.startswith(LEG_BONES):        # skirts and aprons mostly follow the hips
                            acc["Hips"] = acc.get("Hips", 0.0) + val * k * (1 - CLOTH_LEG)
                            val *= CLOTH_LEG
                        acc[name] = acc.get(name, 0.0) + val * k
                ws.append(top(acc))
        else:
            ws = [None] * len(me.vertices)
            for part in clusters(me, loose_parts(me)):
                acc = {}
                low = max(me.vertices[i].co.z for i in part) < knee_z + 0.05   # straps, shoes: may follow the legs
                for i in part:
                    co, idx, dist = kd.find(me.vertices[i].co)
                    k = 1.0 / (dist + 0.02) ** 2              # the part follows the bone that holds it
                    for name, val in bw[idx].items():
                        if not low and name.startswith(LEG_BONES):
                            continue
                        acc[name] = acc.get(name, 0.0) + val * k
                bone = max(acc, key=acc.get) if acc else "Hips"
                for i in part:
                    ws[i] = {bone: 1.0}
        set_weights(ob, ws)

    parts = [body] + [o for o, _ in extras]
    with bpy.context.temp_override(active_object=body, selected_editable_objects=parts, selected_objects=parts):
        bpy.ops.object.join()
    body.name = "Body"
    body.data.name = "Body"
    return body


def build_character(cid):
    B["rng"].seed(zlib.crc32(cid.encode()))
    for d in (GEO, GLOW, BODY, CLOTH):
        d.clear()
    B["CUR"][0] = GEO; B["NO_EYES"][0] = False; B["CUR_CID"][0] = cid if cid != "hero" else "zhdan0"
    P, J = BUILDERS[cid]()
    coll = B["collection"](f"SK_{cid}")
    body = B["emit_smooth"](f"{cid}_Body", coll, mats, BODY)
    extras = []
    gear = B["emit"](f"{cid}_Gear", coll, mats, GEO)
    if gear is not None:
        gear.data.shade_smooth(); gear.data.set_sharp_from_angle(angle=math.radians(50))
        extras.append((gear, "rigid"))
    cl = B["emit"](f"{cid}_Cloth", coll, mats, CLOTH)
    if cl is not None:
        sub = cl.modifiers.new("Subsurf", "SUBSURF"); sub.levels = sub.render_levels = 1
        B["evaluated"](cl).shade_smooth()
        extras.append((cl, "cloth"))
    gl = B["emit"](f"{cid}_Glow", coll, mats, GLOW)
    if gl is not None:
        extras.append((gl, "rigid"))
    rig = make_rig(coll, P, J)
    mesh = skin(rig, body, extras, (J["knee", 1].z + J["knee", -1].z) / 2)
    tris = sum(len(p.vertices) - 2 for p in mesh.data.polygons)
    print(f"[rig] {cid}: {len(rig.data.bones)} bones, {tris} tris")
    return P, J, rig, mesh, coll


# ---------------------------------------------------------------------------------------------------
# poses and clips
# ---------------------------------------------------------------------------------------------------
def E(x=0.0, y=0.0, z=0.0):
    """Rotation around armature axes, degrees, X applied first.
    x: for a bone pointing down (legs, hanging arms) negative = swing forward; for a bone pointing up
       (spine, neck, head) positive = lean forward. y: sideways. z: + = turn to the character's left."""
    return Euler((math.radians(x), math.radians(y), math.radians(z)), "XYZ").to_quaternion()


def ease(t):
    t = max(0.0, min(1.0, t))
    return t * t * (3 - 2 * t)


def mix(a, b, t):
    return a + (b - a) * t


def span(t, a, b):
    return ease((t - a) / (b - a)) if b > a else float(t >= b)


class Pose(dict):
    def __init__(self):
        super().__init__(); self.loc = {}

    def rot(self, bone, q):
        self[bone] = q @ self.get(bone, Quaternion())
        return self

    def move(self, bone, d):
        self.loc[bone] = self.loc.get(bone, V()) + V(d)
        return self


def arm_free(rig_info, x):
    return x not in rig_info["posed"]


def sx(s):
    return "L" if s > 0 else "R"


# --- locomotion -----------------------------------------------------------------------------------
def gait(info, t, amp, knee_amp, lean, bob, arm_amp, elbow, bounce=0.0):
    p = Pose(); ph = 2 * math.pi * t
    for s, x in SIDES:
        f = ph + (0 if s > 0 else math.pi)
        a = amp * math.sin(f)
        knee = 6 + knee_amp * max(0.0, math.cos(f)) ** 1.4
        p.rot(f"Thigh.{x}", E(x=-a))
        p.rot(f"Shin.{x}", E(x=knee))
        p.rot(f"Foot.{x}", E(x=(a - knee) * 0.6))
        k = 1.0 if arm_free(info, x) else 0.2
        p.rot(f"UpperArm.{x}", E(x=a * arm_amp * k, y=-s * 7 * k))
        p.rot(f"Forearm.{x}", E(x=-(elbow + 0.3 * elbow * math.sin(f)) * k))
    p.rot("Hips", E(z=-6 * math.sin(ph)))
    p.rot("Spine", E(x=lean * 0.6))
    p.rot("Spine1", E(x=lean * 0.4, z=9 * math.sin(ph)))
    p.rot("Head", E(x=-lean * 0.5, z=-3 * math.sin(ph)))
    p.move("Hips", (0, 0, bob * math.cos(2 * ph) - bob - bounce))
    return p


def clip_idle(info, t):
    p = Pose(); b = math.sin(2 * math.pi * t)
    p.rot("Spine1", E(x=1.2 * b)).rot("Chest", E(x=1.5 * b)).rot("Head", E(x=-1.0 * b, z=2 * math.sin(math.pi * t)))
    for s, x in SIDES:
        if arm_free(info, x):
            p.rot(f"UpperArm.{x}", E(x=-2 * b, y=-s * 3))
            p.rot(f"Forearm.{x}", E(x=-8 - 2 * b))
    p.move("Hips", (0, 0, 0.004 * b))
    return p


def clip_walk(info, t):
    return gait(info, t, 24, 36, 4, 0.018, 0.8, 16)


def clip_run(info, t):
    return gait(info, t, 42, 75, 14, 0.035, 1.1, 80, bounce=0.03)


def guard_arms(p, info, k=1.0, raise_=0.0):
    """Hero fighting guard: fists up in front of the chest."""
    for s, x in SIDES:
        p.rot(f"UpperArm.{x}", E(x=(-32 - raise_) * k, y=s * 14 * k))
        p.rot(f"Forearm.{x}", E(x=-105 * k))
    return p


def clip_hero_idle(info, t):
    p = clip_idle(info, t)
    for s, x in SIDES:
        p.pop(f"UpperArm.{x}", None); p.pop(f"Forearm.{x}", None)
    guard_arms(p, info, 0.85, 3 * math.sin(2 * math.pi * t))
    return p


def clip_hero_run(info, t):
    p = clip_run(info, t)
    for s, x in SIDES:                                 # pump the fists, elbows bent
        f = 2 * math.pi * t + (0 if s > 0 else math.pi)
        p[f"UpperArm.{x}"] = E(x=44 * math.sin(f) - 10, y=s * 6)
        p[f"Forearm.{x}"] = E(x=-95 - 15 * math.sin(f))
    return p


def clip_jump(info, t):
    p = Pose(); k = ease(min(1.0, t * 2))
    for s, x in SIDES:
        p.rot(f"Thigh.{x}", E(x=-(35 if s > 0 else 15) * k)).rot(f"Shin.{x}", E(x=(70 if s > 0 else 40) * k))
        p.rot(f"Foot.{x}", E(x=20 * k))
        p.rot(f"UpperArm.{x}", E(x=-30 * k, y=-s * 25 * k)).rot(f"Forearm.{x}", E(x=-60 * k))
    p.rot("Spine1", E(x=-6 * k))
    return p


def clip_fall(info, t):
    p = Pose(); w = math.sin(2 * math.pi * t)
    for s, x in SIDES:
        p.rot(f"Thigh.{x}", E(x=-20 - 8 * s * w)).rot(f"Shin.{x}", E(x=35 + 6 * s * w))
        p.rot(f"UpperArm.{x}", E(x=-10, y=-s * (55 + 6 * w))).rot(f"Forearm.{x}", E(x=-35))
    p.rot("Spine1", E(x=4)).rot("Head", E(x=-6))
    return p


def clip_slide(info, t):
    """Feet first: right leg straight out in front, left folded under, leaning back, left hand down."""
    p = Pose(); w = 0.5 * math.sin(2 * math.pi * t)
    p.move("Hips", (0, 0.05, -0.42))
    p.rot("Hips", E(x=-28))
    p.rot("Thigh.R", E(x=-62)).rot("Shin.R", E(x=8)).rot("Foot.R", E(x=-15))
    p.rot("Thigh.L", E(x=-10, y=-12)).rot("Shin.L", E(x=105)).rot("Foot.L", E(x=-30))
    p.rot("Spine", E(x=6)).rot("Spine1", E(x=10 + w)).rot("Head", E(x=14))
    p.rot("UpperArm.L", E(x=30, y=-25)).rot("Forearm.L", E(x=-10))
    p.rot("UpperArm.R", E(x=-45, y=10)).rot("Forearm.R", E(x=-80))
    return p


def clip_dash(info, t):
    p = Pose(); w = math.sin(2 * math.pi * t)
    p.rot("Spine", E(x=14)).rot("Spine1", E(x=12)).rot("Head", E(x=-14))
    p.rot("Thigh.L", E(x=-45)).rot("Shin.L", E(x=60)).rot("Thigh.R", E(x=25)).rot("Shin.R", E(x=50))
    for s, x in SIDES:
        p.rot(f"UpperArm.{x}", E(x=55 + 4 * w, y=-s * 18)).rot(f"Forearm.{x}", E(x=-25))
    return p


def clip_slam(info, t):
    """Cannonball dive: knees up, both fists raised over the head."""
    p = Pose(); w = math.sin(2 * math.pi * t)
    for s, x in SIDES:
        p.rot(f"Thigh.{x}", E(x=-70, y=-s * 8)).rot(f"Shin.{x}", E(x=100)).rot(f"Foot.{x}", E(x=-10))
        p.rot(f"UpperArm.{x}", E(x=-160 + 4 * w, y=s * 12)).rot(f"Forearm.{x}", E(x=-35))
    p.rot("Spine1", E(x=10)).rot("Head", E(x=-10))
    return p


# --- hero attacks (upper body; the animator scrubs them in sync with ZhdanCombat) -----------------
# Each attack clip: 0..0.3 windup, 0.3..0.5 strike, 0.5..1 recovery back to the guard.
def attack(t, windup, strike):
    """0 = guard, -1 = full windup, 1 = full strike, back to 0 at the end."""
    if t < 0.3:
        return -windup * span(t, 0.0, 0.3)
    if t < 0.5:
        return mix(-windup, 1.0, span(t, 0.3, 0.42))
    return 1.0 - span(t, 0.55, 1.0)


def clip_jab(s):
    x = sx(s)
    def fn(info, t):
        p = guard_arms(Pose(), info)
        e = attack(t, 0.3, 1.0)
        k = max(0.0, e); w = max(0.0, -e)
        p[f"UpperArm.{x}"] = E(x=mix(-32, -88, k) + 12 * w, y=s * mix(14, -6, k))
        p[f"Forearm.{x}"] = E(x=mix(-105, -4, k) - 10 * w)
        p.rot("Spine1", E(z=-s * 16 * k + s * 6 * w)).rot("Chest", E(z=-s * 8 * k))
        return p
    return fn


def clip_hook(info, t):
    s, x = -1, "R"
    p = guard_arms(Pose(), info)
    e = attack(t, 1.0, 1.0)
    k = max(0.0, e); w = max(0.0, -e)
    p[f"UpperArm.{x}"] = E(x=mix(-32, -82, max(k, w)), z=mix(0, 60, w) + mix(0, -15, k), y=s * 14 * (1 - max(k, w)))
    p[f"Forearm.{x}"] = E(x=mix(-105, -20, max(k, w)), z=-s * 85 * max(k, w))
    p.rot("Spine1", E(z=s * 28 * w - s * 34 * k, x=6 * k)).rot("Chest", E(z=-s * 10 * k))
    return p


def clip_heavy(info, t):
    """0..0.3 = charge (the animator holds it by the charge level), then a straight right."""
    s, x = -1, "R"
    p = guard_arms(Pose(), info)
    e = attack(t, 1.0, 1.0)
    k = max(0.0, e); w = max(0.0, -e)
    p[f"UpperArm.{x}"] = E(x=mix(-32, -90, k) + 55 * w, y=s * (14 - 20 * w) * (1 - k))
    p[f"Forearm.{x}"] = E(x=mix(-105, -2, k) - 20 * w)
    p.rot("Spine", E(x=-6 * w + 10 * k)).rot("Spine1", E(z=s * 32 * w - s * 40 * k)).rot("Chest", E(z=-s * 12 * k))
    p.rot("UpperArm.L", E(x=-25 * w))
    return p


def clip_dash_strike(info, t):
    p = guard_arms(Pose(), info)
    e = attack(t, 0.2, 1.0); k = max(0.0, e)
    for s, x in SIDES:
        p[f"UpperArm.{x}"] = E(x=mix(-32, -86, k), y=s * mix(14, -10, k))
        p[f"Forearm.{x}"] = E(x=mix(-105, -6, k))
    p.rot("Spine1", E(x=14 * k)).rot("Head", E(x=-10 * k))
    return p


def clip_uppercut(info, t):
    s, x = -1, "R"
    p = guard_arms(Pose(), info)
    e = attack(t, 1.0, 1.0)
    k = max(0.0, e); w = max(0.0, -e)
    p[f"UpperArm.{x}"] = E(x=mix(-32, -150, k) + 40 * w, y=s * 14 * (1 - k))
    p[f"Forearm.{x}"] = E(x=mix(-105, -50, k) - 10 * w)
    p.rot("Spine1", E(x=12 * w - 12 * k, z=s * 14 * w - s * 18 * k))
    return p


# --- reactions and NPC clips -----------------------------------------------------------------------
def clip_hit(info, t):
    p = Pose(); k = span(t, 0.0, 0.25) * (1 - span(t, 0.35, 1.0))
    p.rot("Spine", E(x=-10 * k)).rot("Spine1", E(x=-12 * k)).rot("Head", E(x=-20 * k))
    for s, x in SIDES:
        if arm_free(info, x):
            p.rot(f"UpperArm.{x}", E(x=-20 * k, y=-s * 25 * k)).rot(f"Forearm.{x}", E(x=-40 * k))
        p.rot(f"Thigh.{x}", E(x=-8 * k)).rot(f"Shin.{x}", E(x=14 * k))
    p.move("Hips", (0, 0.03 * k, -0.03 * k))
    return p


def clip_death(info, t):
    """Falls on the back: the whole body tips over around the feet, knees buckle on the way."""
    p = Pose()
    fall = span(t, 0.12, 0.8)
    buckle = span(t, 0.0, 0.25) * (1 - span(t, 0.6, 0.9))
    p.rot("Root", E(x=-86 * fall))
    p.move("Root", (0, 0.0, 0.11 * span(t, 0.6, 0.85)))
    p.rot("Spine1", E(x=12 * buckle - 4 * fall)).rot("Head", E(x=-25 * buckle + 10 * fall))
    for s, x in SIDES:
        p.rot(f"Thigh.{x}", E(x=-25 * buckle)).rot(f"Shin.{x}", E(x=45 * buckle + 6 * fall))
        if arm_free(info, x):
            p.rot(f"UpperArm.{x}", E(x=-20 * buckle, y=-s * 50 * fall)).rot(f"Forearm.{x}", E(x=-30 * buckle - 10))
    return p


def clip_getup(info, t):
    return clip_death(info, 1.0 - t)


def clip_talk(info, t):
    p = clip_idle(info, t); ph = 2 * math.pi * t
    p.rot("Head", E(x=5 * math.sin(3 * ph), z=6 * math.sin(ph)))
    p.rot("Spine1", E(z=4 * math.sin(ph)))
    for s, x in SIDES:
        if not arm_free(info, x):
            continue
        g = 0.5 + 0.5 * math.sin(ph + (0 if s < 0 else 2.0))       # the free hand gestures
        p[f"UpperArm.{x}"] = E(x=-25 - 20 * g, y=s * 8)
        p[f"Forearm.{x}"] = E(x=-60 - 30 * g, z=-s * 15 * math.sin(2 * ph))
        break
    return p


def clip_pray(info, t):
    """Bows and crosses herself with the free hand (Agafya), others just bow."""
    p = clip_idle(info, t); ph = 2 * math.pi * t
    bow = 0.5 + 0.5 * math.sin(ph)
    p.rot("Spine1", E(x=10 * bow)).rot("Chest", E(x=6 * bow)).rot("Head", E(x=18 * bow))
    for s, x in SIDES:
        if not arm_free(info, x):
            continue
        c = (t * 4) % 1.0                                   # forehead, chest, shoulder, shoulder
        p[f"UpperArm.{x}"] = E(x=-30, y=s * 20 + 10 * math.sin(2 * math.pi * c))
        p[f"Forearm.{x}"] = E(x=-125 + 25 * math.sin(2 * math.pi * c), z=-s * 10)
        break
    return p


def clip_cower(info, t):
    p = Pose(); w = math.sin(2 * math.pi * t * 3) * 1.5
    p.move("Hips", (0, 0.06, -0.22))
    p.rot("Spine", E(x=14)).rot("Spine1", E(x=16 + w)).rot("Chest", E(x=8)).rot("Head", E(x=22))
    for s, x in SIDES:
        p.rot(f"Thigh.{x}", E(x=-38)).rot(f"Shin.{x}", E(x=70)).rot(f"Foot.{x}", E(x=-30))
        if arm_free(info, x):
            p.rot(f"UpperArm.{x}", E(x=-120 + w, y=s * 18)).rot(f"Forearm.{x}", E(x=-115))
    return p


def clip_npc_run(info, t):
    return gait(info, t, 34, 60, 10, 0.028, 1.0, 55, bounce=0.02)


COMMON = [("Idle", 60, clip_idle, True), ("Walk", 32, clip_walk, True), ("Run", 20, clip_npc_run, True),
          ("Hit", 12, clip_hit, False), ("Death", 36, clip_death, False), ("GetUp", 36, clip_getup, False)]
NPC_CLIPS = COMMON + [("Talk", 90, clip_talk, True), ("Pray", 120, clip_pray, True), ("Cower", 45, clip_cower, True)]
HERO_CLIPS = [("Idle", 60, clip_hero_idle, True), ("Walk", 30, clip_walk, True), ("Run", 14, clip_hero_run, True),
              ("Jump", 12, clip_jump, False), ("Fall", 30, clip_fall, True), ("Slide", 30, clip_slide, True),
              ("Dash", 12, clip_dash, True), ("Slam", 20, clip_slam, True), ("Hit", 12, clip_hit, False),
              ("Jab_L", 12, clip_jab(1), False), ("Jab_R", 12, clip_jab(-1), False), ("Hook_R", 16, clip_hook, False),
              ("Heavy", 20, clip_heavy, False), ("DashStrike", 14, clip_dash_strike, False),
              ("Uppercut", 16, clip_uppercut, False)]


def make_action(rig, info, name, frames, fn):
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    rig.animation_data_create()
    rig.animation_data.action = act
    rest = {pb.name: pb.bone.matrix_local.to_quaternion() for pb in rig.pose.bones}
    prev = {}
    for f in range(frames + 1):
        pose = fn(info, f / frames)
        for pb in rig.pose.bones:
            Bq = rest[pb.name]
            q = Bq.inverted() @ pose.get(pb.name, Quaternion()) @ Bq
            q.normalize()
            if pb.name in prev and prev[pb.name].dot(q) < 0:
                q.negate()
            prev[pb.name] = q
            pb.rotation_mode = "QUATERNION"
            pb.rotation_quaternion = q
            pb.location = Bq.inverted() @ pose.loc.get(pb.name, V())
            pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
            pb.keyframe_insert("location", frame=f, group=pb.name)
    return act


def animate(rig, info, clips):
    acts = [make_action(rig, info, name, frames, fn) for name, frames, fn, _loop in clips]
    rig.animation_data.action = acts[0]
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion(); pb.location = V()
    return acts


# ---------------------------------------------------------------------------------------------------
# first-person arms
# ---------------------------------------------------------------------------------------------------
def knuckles(J, P, s, curl, mat):
    """Fills the gaps between the round finger tubes of a clenched fist (hand() leaves see-through slits
    between the knuckles and the curled fingers): a bar along the knuckles and one along the folded fingers."""
    Wr, hd = J["wr", s], J["hd", s]
    across = B["perp"](hd, B["Y"]); pn = hd.cross(across)
    if pn.dot(V((-s, 0, 0))) < 0:
        across = -across; pn = -pn
    pl, w, fl, fr = P["hand"] * 0.45, P["palm_w"], P["finger"], P["finger_r"]
    rows = []
    for i, k in enumerate((0.85, 1.0, 0.95, 0.78)):
        b = Wr + hd * pl + across * (-w / 2 + w * (i + 0.5) / 4) * 0.9
        L = fl * k
        rows.append((b, b + hd * L * 0.5 + pn * L * 0.12 * curl))
    for j, r in ((0, 1.0), (1, 0.9)):
        tube([rows[0][j], rows[1][j], rows[2][j], rows[3][j]], [fr * r] * 4, mat, n=8)
    tube([(a + b) / 2 for a, b in rows], [fr * 1.1] * 4, mat, n=8)


def build_fp_arms(P, coll):
    """Zhdan's arms for the first-person view, cut into rigid pieces for a 2-bone IK in Unity.
    Each piece hangs along -Z from its joint at the origin; the front of the arm faces -Y."""
    m = P["m"]; ar, ua, fa = P["arm_r"], P["upper"], P["fore"]
    old = B["BODY_TRIS"]; B["BODY_TRIS"] = 3500
    obs = []
    O = V((0, 0, 0))
    for s, x in SIDES:
        E1 = V((0, 0, -ua))
        with layer(BODY):
            tube([lerp(O, E1, f) + V((0, -ar * o, 0)) for f, o in ((0.0, 0.0), (0.3, 0.12), (0.6, 0.15), (1.0, 0.0))],
                 [ar * 1.22, ar * 1.1, ar * 1.0, ar * 0.8], B["side_mat"](m["upper"], s), n=12)
            blob(E1, ar * 0.86, B["side_mat"](m["upper"], s), n=12, k=6)
        obs.append(B["emit_smooth"](f"FP_UpperArm.{x}", coll, mats, BODY))
        W = V((0, 0, -fa))
        with layer(BODY):
            tube([lerp(O, W, f) + V((s * ar * o, 0, 0)) for f, o in ((0.0, 0.0), (0.22, 0.18), (0.55, 0.1), (1.0, 0.0))],
                 [ar * 0.82, ar * 0.92, ar * 0.72, ar * 0.55], B["side_mat"](m["fore"], s), n=12)
        obs.append(B["emit_smooth"](f"FP_Forearm.{x}", coll, mats, BODY))
        J = {("wr", s): V((0, 0, 0)), ("hd", s): V((0, 0, -1))}
        PF = dict(P); PF["spread"] = 0.03; PF["finger_r"] = P["finger_r"] * 1.35      # a clenched fist
        with layer(BODY):
            hand(J, PF, s, curl=1.75)
            knuckles(J, PF, s, 1.75, B["side_mat"](m["hand"], s))
            blob(V((0, 0, -0.012)), ar * 0.74, B["side_mat"](m["hand"], s), n=12, k=6)     # closes the palm/wrist gap
        obs.append(B["emit_smooth"](f"FP_Fist.{x}", coll, mats, BODY))
    B["BODY_TRIS"] = old
    for o in obs:
        o.data.name = o.name
    return obs


# ---------------------------------------------------------------------------------------------------
# export and preview
# ---------------------------------------------------------------------------------------------------
def select_only(obs):
    vl = bpy.context.view_layer
    for o in vl.objects:
        o.select_set(o in obs)
    vl.objects.active = obs[0]


def export_skinned(path, rig, mesh):
    select_only([rig, mesh])
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
                             axis_forward="-Z", axis_up="Y", bake_space_transform=False,
                             mesh_smooth_type="FACE", use_triangles=True, add_leaf_bones=False,
                             primary_bone_axis="Y", secondary_bone_axis="X", use_armature_deform_only=False,
                             bake_anim=True, bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
                             bake_anim_use_all_actions=True, bake_anim_force_startend_keying=True,
                             bake_anim_step=1.0, bake_anim_simplify_factor=0.0)


def export_static(path, obs):
    select_only(obs)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, mesh_smooth_type="FACE", add_leaf_bones=False,
                             use_triangles=True)


def preview(cid, rig, mesh, acts, folder):
    """Side-view workbench frames of a few clips, to check the motion without Unity."""
    os.makedirs(folder, exist_ok=True)
    cam_d = bpy.data.cameras.new("PreviewCam"); cam_d.type = "ORTHO"; cam_d.ortho_scale = 2.6
    cam = bpy.data.objects.new("PreviewCam", cam_d); SC.collection.objects.link(cam)
    cam.location = (4.0, -2.2, 1.0)
    cam.rotation_euler = (math.radians(88), 0, math.radians(61))
    SC.camera = cam
    SC.render.engine = "BLENDER_WORKBENCH"
    SC.display.shading.light = "STUDIO"; SC.display.shading.color_type = "MATERIAL"
    SC.render.resolution_x, SC.render.resolution_y = 300, 360
    SC.render.film_transparent = False
    names = {"Walk", "Run", "Jab_R", "Hook_R", "Slide", "Death", "Talk", "Heavy", "Uppercut"}
    for act in acts:
        if act.name.split(".")[0] not in names:
            continue
        rig.animation_data.action = act
        end = int(act.frame_range[1])
        for i, f in enumerate((0, end // 4, end // 2, 3 * end // 4)):
            SC.frame_set(f)
            SC.render.filepath = os.path.join(folder, f"{cid}_{act.name}_{i}.png")
            bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)


os.makedirs(os.path.join(OUT, "fbx"), exist_ok=True)
if FP_ONLY:                                          # only the first-person arms: no rigging, little memory
    B["rng"].seed(zlib.crc32(b"hero"))
    B["CUR"][0] = GEO; B["NO_EYES"][0] = False; B["CUR_CID"][0] = "zhdan0"
    P, _ = BUILDERS["hero"]()
    for d in (GEO, GLOW, BODY, CLOTH):
        d.clear()
    fp = build_fp_arms(P, B["collection"]("SM_FPArms"))
    export_static(os.path.join(OUT, "fbx", "SM_Zhdan_FPArms.fbx"), fp)
    print("[fbx] SM_Zhdan_FPArms.fbx")
for cid in ([] if FP_ONLY else BUILDERS):
    if ONLY and cid not in ONLY:
        continue
    P, J, rig, mesh, coll = build_character(cid)
    info = {"posed": {sx(s) for s, v in (P.get("hands") or {}).items() if v is not None}}
    acts = animate(rig, info, HERO_CLIPS if cid == "hero" else NPC_CLIPS)
    export_skinned(os.path.join(OUT, "fbx", f"SK_Char_{cid}.fbx"), rig, mesh)
    print(f"[fbx] SK_Char_{cid}.fbx: {len(acts)} clips, posed arms {sorted(info['posed'])}")
    if PREVIEW:
        preview(cid, rig, mesh, acts, PREVIEW)
    if cid == "hero":
        fp = build_fp_arms(P, coll)
        export_static(os.path.join(OUT, "fbx", "SM_Zhdan_FPArms.fbx"), fp)
        print("[fbx] SM_Zhdan_FPArms.fbx")
    for o in list(coll.objects):                     # free memory before the next character
        bpy.data.objects.remove(o)
    for a in list(bpy.data.actions):
        bpy.data.actions.remove(a)
    for me in list(bpy.data.meshes):
        if me.users == 0:
            bpy.data.meshes.remove(me)
    for ad in list(bpy.data.armatures):
        if ad.users == 0:
            bpy.data.armatures.remove(ad)
print("[done]")
