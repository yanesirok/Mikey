"""Референс-кадр той же сцены: 128 сэмплов Cycles хватает для сравнения на глаз."""
import bpy
import os

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 128
scene.render.resolution_x = 1920
scene.render.resolution_y = 1080
scene.render.resolution_percentage = 100
scene.render.filepath = os.path.normpath(os.path.join(
    os.path.dirname(bpy.data.filepath), "..", "..",
    "docs", "superpowers", "specs", "refs", "2026-08-05-bamboo-grove-ref.png"))
bpy.ops.render.render(write_still=True)
print("RENDERED", scene.render.filepath)
