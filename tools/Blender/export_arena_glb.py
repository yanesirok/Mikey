"""Экспорт арены в GLB для Unity. Запуск:
& "C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe" -b tools/Blender/bamboo_grove.blend -P tools/Blender/export_arena_glb.py

В .blend, кроме финальной сцены, живут скрытые исходники (скалы по 1.3 млн трис, травяные
LOD'ы, старая арена) — экспортируем только видимую восьмёрку из таблицы handoff §1.
FogDomain — Cycles-объём, в Unity стал бы 130-метровым кубом (handoff §2) — не входит.
"""
import bpy
import os

WANTED = {
    "Merged_fern_02", "BambooLeaves", "BambooWall", "Merged_boulder_01",
    "Terrain", "Bridge", "Water", "Backdrop",
}

for o in list(bpy.data.objects):
    if o.name not in WANTED:
        bpy.data.objects.remove(o, do_unlink=True)

missing = WANTED - {o.name for o in bpy.data.objects}
if missing:
    raise SystemExit(f"MISSING {sorted(missing)}")

out = os.path.normpath(os.path.join(
    os.path.dirname(bpy.data.filepath), "..", "..",
    "Assets", "Fight", "NewArena", "BambooGrove.glb"))

bpy.ops.export_scene.gltf(
    filepath=out,
    export_format='GLB',
    export_yup=True,               # Blender Z-up -> Unity Y-up (handoff §2)
    export_apply=True,
    export_texcoords=True,
    export_normals=True,
    export_materials='EXPORT',
    export_vertex_color='MATERIAL',  # пригодятся для ветра
    export_cameras=False,
    export_lights=False,
)
print("EXPORTED", out, os.path.getsize(out), "bytes")
for o in bpy.data.objects:
    print("OBJ", o.name)
