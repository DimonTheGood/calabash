"""Экспорт собранной деревни из village.blend одним FBX с мировыми позициями — для Unity.

Запуск из корня репо:
    blender -b village/village.blend --factory-startup -P village/scripts/export_village_map.py

Результат: village/fbx/Village_Map.fbx (дома, снег на крышах, пропсы, природа, земля, дорога).
Образцы домов (COL_Kit), камеры и свет не экспортируются. Оси под Unity: Y вверх, метры.
"""
import os
import bpy

COLLECTIONS = ["COL_Ground", "COL_Houses", "COL_Props", "COL_Nature"]
OUT = os.path.join(os.path.dirname(bpy.data.filepath), "fbx", "Village_Map.fbx")

bpy.ops.object.select_all(action="DESELECT")
count = 0
for name in COLLECTIONS:
    coll = bpy.data.collections.get(name)
    if coll is None:
        print(f"[export] нет коллекции {name}")
        continue
    for obj in coll.all_objects:
        if obj.type == "MESH" and not obj.hide_render:
            obj.hide_set(False)
            obj.select_set(True)
            count += 1

os.makedirs(os.path.dirname(OUT), exist_ok=True)
bpy.ops.export_scene.fbx(
    filepath=OUT,
    use_selection=True,
    object_types={"MESH"},
    use_mesh_modifiers=True,
    mesh_smooth_type="FACE",
    apply_unit_scale=True,
    apply_scale_options="FBX_SCALE_ALL",
    axis_forward="-Z",
    axis_up="Y",
    bake_space_transform=True,
    add_leaf_bones=False,
    bake_anim=False,
    path_mode="AUTO",
)
print(f"[export] {count} объектов → {OUT} ({os.path.getsize(OUT) / 1e6:.1f} МБ)")
