"""Кимоно на базовое тело: подгонка, low-poly с запечёнными картами, скиннинг.

Запуск (headless):
  blender --background --factory-startup --python kimono_fit.py -- \
      --body <body.fbx> --kimono <kimono.glb> --out <dir>

Переиспользует из bridge_kit.py запек high -> low и детерминированный
FBX-экспорт: там это уже отлажено на деталях моста.

У кимоно 305k трисов и ни одной текстуры — весь вид держится на складках.
Поэтому децимация идёт только в паре с запеком нормалей; без него от
кимоно остаётся чёрный мешок.

Подгонка скана на чужое тело не бывает точной с первого раза, поэтому
масштаб выведен из костей, но оставлены ручки --scale-mul и --offset-z.
"""
import argparse
import math
import os
import sys

import bpy
import mathutils

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bridge_kit import (ATLAS, apply_mods, bake_pair, fill, reset_scene,
                        save_png, tri_count)


def parse_args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument('--body', required=True)
    p.add_argument('--kimono', required=True)
    p.add_argument('--out', required=True)
    p.add_argument('--scale-mul', type=float, default=1.0,
                   help='ручная поправка к масштабу, выведенному из костей')
    p.add_argument('--offset-z', type=float, default=0.0,
                   help='ручная поправка высадки по вертикали, в метрах')
    p.add_argument('--tris', type=int, default=12000)
    return p.parse_args(argv)


def world_bbox(objs):
    cs = [o.matrix_world @ mathutils.Vector(c)
          for o in objs for c in o.bound_box]
    mn = mathutils.Vector((min(c.x for c in cs), min(c.y for c in cs),
                           min(c.z for c in cs)))
    mx = mathutils.Vector((max(c.x for c in cs), max(c.y for c in cs),
                           max(c.z for c in cs)))
    return mn, mx


def import_body(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=True)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next(o for o in new if o.type == 'ARMATURE')
    meshes = [o for o in new if o.type == 'MESH']
    assert meshes, 'в теле нет ни одного меша — скачано Without Skin?'
    return arm, meshes


def import_kimono(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    assert new, 'в glb нет мешей'
    bpy.ops.object.select_all(action='DESELECT')
    for o in new:
        o.select_set(True)
    bpy.context.view_layer.objects.active = new[0]
    bpy.ops.object.join()
    k = bpy.context.view_layer.objects.active
    k.name = 'Kimono_high'

    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')

    # 5 склеенных объектов делят швы не по общим вершинам, а по
    # задвоенным — join() не сваривает их. 408 несвязанных островов
    # вершин на выходе не дают smart_project ни одной крупной развёртки:
    # 98% атласа уходит в межостровные поля. Порог 0.05 — это ~0.1 мм в
    # мировых единицах при масштабе кимоно 0.0019, спаивает только честные
    # дубли шва, не трогает реальные складки, где ткань просто соприкасается.
    bpy.ops.mesh.remove_doubles(threshold=0.05)

    # Куски несут не согласованное между собой направление нормалей
    # (обычный дефект сканов из нескольких частей) — bake 'selected to
    # active' кастует луч вдоль нормали low-poly, и там, где она смотрит
    # внутрь, луч уходит от high-poly и текстель остаётся фоновой заливкой.
    # Пересчитываем нормали наружу по геометрии.
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    return k


def upright(o):
    """glb хранит Z-up внутри Y-up-формата, и импортёр честно кладёт его на
    спину. Ставим по самой длинной оси, а не по вере в экспортёра."""
    mn, mx = world_bbox([o])
    size = mx - mn
    if size.y > size.z:
        o.rotation_euler.x += math.radians(90)
        bpy.context.view_layer.update()


def bone_head_z(arm, name):
    return (arm.matrix_world @ arm.pose.bones[name].head).z


def fit(kimono, arm, meshes, scale_mul, offset_z):
    """Совмещает кимоно с телом: воротник у шеи, штанина у стопы.

    По высоте головы масштабировать нельзя — кимоно кончается у воротника,
    а не на макушке.
    """
    neck = bone_head_z(arm, 'mixamorig:Neck')
    toe = bone_head_z(arm, 'mixamorig:LeftToeBase')
    kmn, kmx = world_bbox([kimono])
    s = (neck - toe) / (kmx.z - kmn.z) * scale_mul
    kimono.scale = (s, s, s)
    bpy.context.view_layer.update()

    bmn, bmx = world_bbox(meshes)
    kmn, kmx = world_bbox([kimono])
    kimono.location.x += (bmn.x + bmx.x) / 2 - (kmn.x + kmx.x) / 2
    kimono.location.y += (bmn.y + bmx.y) / 2 - (kmn.y + kmx.y) / 2
    kimono.location.z += toe - kmn.z + offset_z
    bpy.context.view_layer.update()
    return s


def make_low(high, target_tris):
    low = high.copy()
    low.data = high.data.copy()
    low.name = 'Kimono_low'
    bpy.context.collection.objects.link(low)
    n = tri_count(low)
    mod = low.modifiers.new('decimate', 'DECIMATE')
    mod.ratio = min(1.0, target_tris / n)
    apply_mods(low)

    # Штатная UV из glb не годится: децимация её рвёт, и ни один материал
    # на неё всё равно не ссылается. Разворачиваем заново — цель запека
    # обязана иметь чистую развёртку, источник в UV не нуждается вовсе.
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.004)
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.pack_islands(margin=0.004, rotate=True)
    bpy.ops.object.mode_set(mode='OBJECT')
    return low, n


def bake(low, high, out_dir):
    normal = bpy.data.images.new('T_Kimono_Normal', ATLAS, ATLAS,
                                 alpha=False, is_data=True)
    ao = bpy.data.images.new('T_Kimono_AO', ATLAS, ATLAS,
                             alpha=False, is_data=True)
    fill(normal, (0.5, 0.5, 1.0, 1.0))
    fill(ao, (1.0, 1.0, 1.0, 1.0))
    bake_pair(low, high, normal, ao)
    save_png(normal, os.path.join(out_dir, 'T_Kimono_Normal.png'))
    save_png(ao, os.path.join(out_dir, 'T_Kimono_AO.png'))


def main():
    a = parse_args()
    os.makedirs(a.out, exist_ok=True)
    reset_scene()

    arm, body_meshes = import_body(a.body)
    kimono = import_kimono(a.kimono)
    upright(kimono)
    scale = fit(kimono, arm, body_meshes, a.scale_mul, a.offset_z)

    bmn, bmx = world_bbox(body_meshes)
    kmn, kmx = world_bbox([kimono])
    assert kmn.z >= bmn.z - 0.05, 'кимоно провалилось ниже стоп'
    assert kmx.z <= bmx.z + 0.01, 'кимоно выше макушки — масштаб не тот'

    low, high_tris = make_low(kimono, a.tris)
    got = tri_count(low)
    assert got <= a.tris * 1.05, f'low-poly {got} трисов при бюджете {a.tris}'

    bake(low, kimono, a.out)
    print(f'kimono_fit: подгонка scale={scale:.4f}, '
          f'{high_tris} -> {got} трисов, карты в {a.out}')


if __name__ == '__main__':
    main()
