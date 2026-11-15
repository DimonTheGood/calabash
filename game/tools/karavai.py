"""Calabash: каравай для концовки «рай» — круглый, с куполом, косой по краю, листьями сверху и солонкой.

    blender -b --factory-startup -P game/tools/karavai.py -- [out.fbx] [--preview=file.png]

Метры, низ на z=0, центр в начале координат. Материалы: M_Crust (корка), M_Glaze (верх, светлее),
M_Dough (украшения), M_Salt (солонка). В Unity их задаёт GameSetup.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector as V

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = next((a for a in argv if not a.startswith("--")), os.path.join(HERE, "..", "Models", "SM_Karavai.fbx"))
PREVIEW = next((a.split("=", 1)[1] for a in argv if a.startswith("--preview=")), None)

bpy.ops.wm.read_factory_settings(use_empty=True)
SC = bpy.context.scene
COLORS = {"M_Crust": (0.32, 0.15, 0.05), "M_Glaze": (0.55, 0.3, 0.1), "M_Dough": (0.8, 0.62, 0.38), "M_Salt": (0.92, 0.92, 0.9)}
MATS = {}
for n, c in COLORS.items():
    m = bpy.data.materials.new(n); m.diffuse_color = (*c, 1); MATS[n] = m

R, H = 0.25, 0.15            # радиус и высота купола


def obj_from_bm(name, bm, mat):
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(MATS[mat])
    ob = bpy.data.objects.new(name, me); SC.collection.objects.link(ob)
    for p in me.polygons:
        p.use_smooth = True
    return ob


def dome():
    """Профиль вращения: плоское дно, крутой бок, купол; верх — глазурь."""
    prof = [(0.0, 0.0), (R * 0.92, 0.0), (R, 0.02), (R * 1.01, 0.05), (R * 0.97, 0.085), (R * 0.88, 0.11),
            (R * 0.72, 0.135), (R * 0.5, 0.148), (R * 0.25, H * 1.02), (0.0, H * 1.03)]
    bm = bmesh.new(); n = 48
    rings = []
    for (r, z) in prof:
        ring = [bm.verts.new((r * math.cos(2 * math.pi * k / n), r * math.sin(2 * math.pi * k / n), z)) for k in range(n)]
        rings.append(ring)
    for a, b in zip(rings, rings[1:]):
        for k in range(n):
            bm.faces.new((a[k], a[(k + 1) % n], b[(k + 1) % n], b[k]))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    ob = obj_from_bm("Dome", bm, "M_Crust")
    ob.data.materials.append(MATS["M_Glaze"])
    for p in ob.data.polygons:
        if p.center.z > 0.1:
            p.material_index = 1
    # трещинки на корке: лёгкий шум по куполу
    tex = bpy.data.textures.new("Bumps", "CLOUDS"); tex.noise_scale = 0.05
    d = ob.modifiers.new("Bumps", "DISPLACE"); d.texture = tex; d.strength = 0.006
    sub = ob.modifiers.new("Sub", "SUBSURF"); sub.levels = sub.render_levels = 1
    sub.show_viewport = True
    ob.modifiers.move(1, 0)
    return ob


def strand(name, pts, r, mat, closed=True):
    cd = bpy.data.curves.new(name, "CURVE"); cd.dimensions = "3D"
    cd.bevel_depth = r; cd.bevel_resolution = 3; cd.use_fill_caps = True
    sp = cd.splines.new("POLY"); sp.points.add(len(pts) - 1)
    for p, q in zip(sp.points, pts):
        p.co = (*q, 1)
    sp.use_cyclic_u = closed
    ob = bpy.data.objects.new(name, cd); SC.collection.objects.link(ob)
    cd.materials.append(MATS[mat])
    return ob


def braid():
    """Коса из двух жгутов вокруг основания."""
    obs = []
    n, k, a, z0, Rb = 240, 22, 0.017, 0.035, R * 1.03
    for i, ph in enumerate((0.0, math.pi)):
        pts = []
        for j in range(n):
            t = 2 * math.pi * j / n
            w = math.cos(k * t + ph)
            rr = Rb + a * w
            pts.append((rr * math.cos(t), rr * math.sin(t), z0 + a * math.sin(k * t + ph)))
        obs.append(strand(f"Braid{i}", pts, 0.019, "M_Dough"))
    return obs


def leaves():
    """Листья и колоски из теста сверху, по кругу, и солонка в центре."""
    obs = []
    for i in range(6):
        t = 2 * math.pi * i / 6
        c = V((math.cos(t) * R * 0.5, math.sin(t) * R * 0.5, 0.0))
        bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=1, location=(0, 0, 0))
        lf = bpy.context.active_object; lf.name = f"Leaf{i}"
        lf.scale = (0.055, 0.02, 0.008)
        lf.rotation_euler = (0, 0.32, t)
        z = 0.143
        lf.location = (c.x, c.y, z)
        lf.data.materials.append(MATS["M_Dough"])
        for pp in lf.data.polygons:
            pp.use_smooth = True
        obs.append(lf)
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=0.045, depth=0.03, location=(0, 0, H + 0.02))
    salt = bpy.context.active_object; salt.name = "Salt"; salt.data.materials.append(MATS["M_Salt"])
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=0.04, location=(0, 0, H + 0.035))
    heap = bpy.context.active_object; heap.scale = (1, 1, 0.35); heap.data.materials.append(MATS["M_Salt"])
    for o in (salt, heap):
        for p in o.data.polygons:
            p.use_smooth = True
    return obs + [salt, heap]


parts = [dome()] + braid() + leaves()
for o in parts:
    o.select_set(True)
bpy.context.view_layer.objects.active = parts[0]
bpy.ops.object.convert(target="MESH")
bpy.ops.object.join()
ob = bpy.context.active_object; ob.name = "SM_Karavai"; ob.data.name = "SM_Karavai"
bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
print("tris", sum(len(p.vertices) - 2 for p in ob.data.polygons))

os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=os.path.abspath(OUT), use_selection=True, object_types={"MESH"}, apply_unit_scale=True,
                         apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y", bake_space_transform=True,
                         mesh_smooth_type="FACE", use_triangles=True)
print("[fbx]", OUT)

if PREVIEW:
    SC.render.engine = "BLENDER_WORKBENCH"
    sh = SC.display.shading; sh.light = "STUDIO"; sh.color_type = "MATERIAL"; sh.show_cavity = True
    SC.world = bpy.data.worlds.new("W"); SC.world.color = (0.9, 0.9, 0.9)
    SC.render.resolution_x, SC.render.resolution_y = 640, 420
    cd = bpy.data.cameras.new("C"); cd.lens = 60
    cam = bpy.data.objects.new("C", cd); SC.collection.objects.link(cam); SC.camera = cam
    cam.location = (0.0, -0.95, 0.55)
    cam.rotation_euler = (V((0, 0, 0.08)) - cam.location).to_track_quat("-Z", "Y").to_euler()
    SC.render.filepath = os.path.abspath(PREVIEW)
    bpy.ops.render.render(write_still=True)
