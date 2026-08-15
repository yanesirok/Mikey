"""Базовое тело бойца: MPFB2 + скелет mixamo_unity.

Запуск (headless):
  blender --background --python make_body.py -- --out <file.fbx>

БЕЗ --factory-startup и без read_factory_settings: и то и другое выключает
аддоны, то есть убивает MPFB прямо в этой сессии. Сцену чистим руками.

Скелет берётся именно mixamo_unity: его 64 кости несут префикс mixamorig:,
на который опирается kimono_fit.py — fit() ищет mixamorig:Neck и
mixamorig:LeftToeBase, strip_covered() отбирает вершины по именам костей.
Возьми другой риг — и оба сломаются молча.
"""
import argparse
import os
import sys

import bpy


def parse_args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument('--out', required=True)
    return p.parse_args(argv)


def rigs_dir():
    ext = os.path.join(os.path.dirname(bpy.utils.user_resource('EXTENSIONS')),
                       'extensions', 'blender_org', 'mpfb', 'data', 'rigs', 'standard')
    assert os.path.isdir(ext), f'MPFB2 не установлен: нет {ext}'
    return ext


def main():
    a = parse_args()
    os.makedirs(os.path.dirname(os.path.abspath(a.out)), exist_ok=True)

    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)

    assert hasattr(bpy.ops, 'mpfb'), 'аддон MPFB2 не включён в этой сессии Blender'
    bpy.ops.mpfb.create_human()

    mesh = next((o for o in bpy.data.objects if o.type == 'MESH'), None)
    assert mesh is not None, 'MPFB не создал меш'

    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    bpy.ops.mpfb.load_rig(filepath=os.path.join(rigs_dir(), 'rig.mixamo_unity.json'))

    arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
    assert arm is not None, 'скелет не создан'
    bones = {b.name for b in arm.data.bones}
    for want in ('mixamorig:Hips', 'mixamorig:Neck', 'mixamorig:LeftToeBase'):
        assert want in bones, f'в скелете нет {want} — загрузился не тот риг'

    # T-поза, рост человека: если тут не так, дальше сломается подгонка кимоно.
    assert 1.5 < mesh.dimensions.z < 2.0, f'рост {mesh.dimensions.z:.3f} м вне человеческого'
    assert mesh.dimensions.x > mesh.dimensions.y, 'руки не разведены — это не T-поза'

    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    # use_mesh_modifiers применяет маску 'Hide helpers' — вспомогательная
    # геометрия MPFB для примерки одежды в экспорт не уезжает.
    bpy.ops.export_scene.fbx(
        filepath=os.path.abspath(a.out), use_selection=True,
        object_types={'MESH', 'ARMATURE'}, add_leaf_bones=False,
        bake_anim=False, apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True, axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=True)

    print(f'make_body: {len(mesh.data.vertices)} вершин, {len(bones)} костей, '
          f'рост {mesh.dimensions.z:.3f} м -> {os.path.abspath(a.out)}')


if __name__ == '__main__':
    main()
