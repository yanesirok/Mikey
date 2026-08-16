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
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from bridge_kit import (ATLAS, _install_deterministic_fbx_uuids, apply_mods,
                        bake_pair, fill, reset_scene, save_png, tri_count)


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
    arm = next((o for o in new if o.type == 'ARMATURE'), None)
    assert arm is not None, f'в {path} нет арматуры — тело экспортировано без скелета?'
    meshes = [o for o in new if o.type == 'MESH']
    assert meshes, 'в теле нет ни одного меша — скачано Without Skin?'
    return arm, meshes


# kimono.glb несёт пять мешей; четыре — одежда, пятый (материал 'default') —
# манекен, на котором её моделировали: он тянется от стоп до макушки (z
# 1.8..735.1 против 51.5..658.8 у самой одежды) и имеет самый широкий размах
# рук из всех пяти. Склей его вместе с одеждой — и fit() станет мерить
# масштаб по макушке манекена, что её собственный докстринг прямо запрещает
# (кимоно кончается у воротника). Подтверждено импортом: реальные материалы
# на пяти мешах — Belts_1, Jacket_1, Pants_1, Shirt_1, default.
KIMONO_PARTS = ('Belts', 'Jacket', 'Pants', 'Shirt')


def import_kimono_parts(path):
    """Импортирует kimono.glb и склеивает только четыре части одежды, отбросив манекен.

    Падает громко, а не молча, если частей одежды оказалось не четыре — регресс к
    сценарию 'манекен снова в склейке' обязан ронять прогон, а не проходить тихо.
    """
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    assert new, 'в glb нет мешей'

    kept = [o for o in new if any(
        slot.material and slot.material.name.startswith(KIMONO_PARTS)
        for slot in o.material_slots)]
    for o in new:
        if o not in kept:
            bpy.data.objects.remove(o, do_unlink=True)
    assert len(kept) == 4, (
        f'ожидалось 4 части кимоно ({", ".join(KIMONO_PARTS)}), найдено {len(kept)}: '
        f'{[o.name for o in kept]} — проверь материалы в kimono.glb')

    bpy.ops.object.select_all(action='DESELECT')
    for o in kept:
        o.select_set(True)
    bpy.context.view_layer.objects.active = kept[0]
    bpy.ops.object.join()
    k = bpy.context.view_layer.objects.active
    k.name = 'Kimono_high'
    return k


def weld_seams_and_fix_normals(k):
    """Сваривает швы, которые join() оставляет как задвоенные вершины, и приводит
    нормали к единому направлению наружу."""
    # Порог сварки выводим из собственного bbox меша, тем же приёмом,
    # каким fit() выводит масштаб из костей, а не числом: на этом шаге
    # объект ещё не отмасштабирован (upright()/fit() позже), так что
    # world_bbox тут — это местные единицы исходного файла. Абсолютное
    # число было бы завязано на то, в каких единицах кимоно приехало на
    # этот раз, и молча ломалось бы при переэкспорте в других единицах:
    # либо не сваривало бы швы, либо съедало настоящие складки.
    mn, mx = world_bbox([k])
    weld_threshold = (mx - mn).length * 5e-5

    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')

    # 4 склеенных объекта делят швы не по общим вершинам, а по
    # задвоенным — join() не сваривает их. Несвязанные острова
    # вершин на выходе не дают smart_project ни одной крупной развёртки:
    # почти весь атлас уходит в межостровные поля. weld_threshold спаивает
    # только честные дубли шва, не трогает реальные складки, где ткань
    # просто соприкасается.
    bpy.ops.mesh.remove_doubles(threshold=weld_threshold)

    # Куски несут не согласованное между собой направление нормалей
    # (обычный дефект сканов из нескольких частей) — bake 'selected to
    # active' кастует луч вдоль нормали low-poly, и там, где она смотрит
    # внутрь, луч уходит от high-poly и текстель остаётся фоновой заливкой.
    # Пересчитываем нормали наружу по геометрии.
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')


def import_kimono(path):
    """Импорт кимоно целиком: отбор частей одежды, склейка, сварка швов, нормали."""
    k = import_kimono_parts(path)
    weld_seams_and_fix_normals(k)
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


def capture_belt_mask(low):
    """Per-face 'is this the belt' marker, read while low still carries the four
    original per-part slots (Belts_1/Jacket_1/Pants_1/Shirt_1) that join() preserved
    and decimate did not disturb. bake() wipes material slots down to one bake target
    (bridge_kit.bake_target_material), so the split has to be captured before bake()
    runs and reapplied after — see apply_belt_split().
    """
    names = [m.name if m else '' for m in low.data.materials]
    return [names[p.material_index].startswith('Belts') for p in low.data.polygons]


def apply_belt_split(low, is_belt):
    """Rebuilds two material slots on the already-baked low mesh: cloth and belt.

    Both slots point at placeholder materials — the baked textures are shared UV space,
    not per-slot content — Unity/FighterImportSetup assigns the real per-team materials
    by submesh index (0 = cloth, 1 = belt) after import. The spec wants player and enemy
    belts in different colours, which needs its own submesh: the shader's _RimColor is a
    fresnel highlight over the whole silhouette, not a belt.
    """
    low.data.materials.clear()
    low.data.materials.append(bpy.data.materials.new('Kimono_Cloth'))
    low.data.materials.append(bpy.data.materials.new('Kimono_Belt'))
    for belt, poly in zip(is_belt, low.data.polygons):
        poly.material_index = 1 if belt else 0


# Опорные точки измерены этой же функцией на реальном пайплайне (не
# взяты из отчёта буквально: там 1.8%/8.7% — доля незалитых ПИКСЕЛЕЙ на
# готовом PNG, раздутая margin-выпуском запека вокруг мелких островов;
# здесь же точная площадь UV-треугольников, без выпуска):
#   без сварки швов (408 островов)              -> 1.9%
#   со сваркой + pack_islands (текущий пайплайн) -> 3.7%
# 2.5% лежит между ними: заведомо ловит регресс к развалу развёртки и
# не трогает текущее рабочее состояние.
UV_COVERAGE_MIN = 0.025


def uv_coverage(obj):
    """Доля площади атласа 0..1, покрытая UV-треугольниками объекта.

    Интегрирование по loop_triangles в UV-пространстве — тем же приёмом,
    которым в диагностике был найден провал развёртки на 1.8% (см.
    task-3-report.md). Наложение островов друг на друга не вычитается —
    цель не точная площадь, а грубый страж регрессии.
    """
    obj.data.calc_loop_triangles()
    uv = obj.data.uv_layers.active.data
    total = 0.0
    for tri in obj.data.loop_triangles:
        a, b, c = (uv[li].uv for li in tri.loops)
        total += abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) / 2
    return total


def _pixel_array(img):
    """RGBA float32, one row per texel."""
    px = np.empty(img.size[0] * img.size[1] * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    return px.reshape(-1, 4)


def _baked_mask(px, background):
    """Texels the bake actually touched, as opposed to the untouched background fill.

    bake() pre-fills both maps with a flat colour before baking (white for AO, "pointing
    straight out" for the normal map); islands and their margin dilation are the only
    pixels the bake operator overwrites, so anything still close to the fill is
    background that no ray ever hit. The threshold has to clear 1/255 (~0.004): these
    images are 8-bit-quantized even in memory (float_buffer defaults to False), so an
    exact-fill midpoint like 0.5 comes back as ~0.498/0.502, not bit-identical — too
    tight an epsilon (verified: 1e-4) classifies every background texel as "baked".
    """
    bg = np.array(background, dtype=np.float32)
    return np.any(np.abs(px[:, :3] - bg) > 0.01, axis=1)


# Пороги измерены на реальном негодном запеке (найден финальным ревью ветки, C2,
# коммит перед починкой C1, где в склейку кимоно уезжал манекен): по текселям,
# которые бейк реально тронул, AO — среднее 0.24, медиана 0.02 (половина ткани чистый
# чёрный); синий канал нормалей — среднее 0.59 при σ 0.37 (шум, а не складки). Пороги
# ниже заведомо ловят оба этих состояния; после починки C1 должны пройти (см. отчёт).
AO_MEAN_MIN = 0.35
AO_MEDIAN_MIN = 0.20
NORMAL_BLUE_MEAN_MIN = 0.85
NORMAL_BLUE_STD_MAX = 0.15


def check_ao_content(ao):
    """AO обязан заметно отличаться от чёрного там, где он реально запечён — он идёт в
    _BaseMap при _AlbedoGamma=1, и чёрный AO там означает albedo ~0, то есть чёрную ткань."""
    px = _pixel_array(ao)
    lit = px[_baked_mask(px, (1.0, 1.0, 1.0)), 0]
    assert lit.size > 0, 'AO не запёкся ни на одном текселе'
    mean, median = float(lit.mean()), float(np.median(lit))
    assert mean > AO_MEAN_MIN and median > AO_MEDIAN_MIN, (
        f'AO негодный: среднее {mean:.3f}, медиана {median:.3f} по запечённым текселям '
        f'(нужно среднее > {AO_MEAN_MIN}, медиана > {AO_MEDIAN_MIN}) — '
        'похоже, в запек попала лишняя геометрия')
    return mean, median


def check_normal_content(normal):
    """Синий канал tangent-space карты обязан группироваться у 1 (смотрит прямо наружу)
    — иначе поверхность на экране читается как цветной шум, а не как рельеф ткани."""
    px = _pixel_array(normal)
    blue = px[_baked_mask(px, (0.5, 0.5, 1.0)), 2]
    assert blue.size > 0, 'нормали не запеклись ни на одном текселе'
    mean, std = float(blue.mean()), float(blue.std())
    assert mean > NORMAL_BLUE_MEAN_MIN and std < NORMAL_BLUE_STD_MAX, (
        f'карта нормалей — шум: синий канал среднее {mean:.3f}, σ {std:.3f} '
        f'(нужно среднее > {NORMAL_BLUE_MEAN_MIN}, σ < {NORMAL_BLUE_STD_MAX}) — '
        'похоже, в запек попала лишняя геометрия')
    return mean, std


def bake(low, high, out_dir):
    normal = bpy.data.images.new('T_Kimono_Normal', ATLAS, ATLAS,
                                 alpha=False, is_data=True)
    ao = bpy.data.images.new('T_Kimono_AO', ATLAS, ATLAS,
                             alpha=False, is_data=True)
    fill(normal, (0.5, 0.5, 1.0, 1.0))
    fill(ao, (1.0, 1.0, 1.0, 1.0))
    bake_pair(low, high, normal, ao)

    ao_mean, ao_median = check_ao_content(ao)
    n_mean, n_std = check_normal_content(normal)
    print(f'kimono_fit: запек — AO среднее {ao_mean:.3f} медиана {ao_median:.3f}, '
          f'нормали синий среднее {n_mean:.3f} sigma {n_std:.3f}')

    save_png(normal, os.path.join(out_dir, 'T_Kimono_Normal.png'))
    save_png(ao, os.path.join(out_dir, 'T_Kimono_AO.png'))


# Кости, чью геометрию закрывает ткань, и кости, которые остаются наружу.
# KEEP проверяется первым: mixamorig:ForeArm содержит и Arm, и — по смыслу —
# запястье, но кисть обязана уцелеть.
# Buttock и Breast есть именно в скелете mixamo_unity (64 кости против 52 у
# обычного mixamo); без них ягодицы и грудь остались бы под тканью целыми.
# Hips — 218 из 13380 вершин доминантно весят на неё: геометрия таза и паха,
# ровно там, где сходятся пояс куртки и верх штанов. Без Hips в COVERED эта
# геометрия переживала вырезание и уезжала внутрь готового FBX.
COVERED = ('Spine', 'Chest', 'Arm', 'Shoulder', 'UpLeg', 'Leg',
           'Buttock', 'Breast', 'Hips')
KEEP = ('Head', 'Neck', 'Hand', 'Foot', 'Toe')


def transfer_weights(low, body, arm):
    """Веса берём с уже отскиненного тела, а не автоматические.

    Тело отскинено правильно и кимоно лежит ровно по нему, поэтому перенос
    ближайшей гранью точнее, чем parent_set(ARMATURE_AUTO), и не требует
    ручной покраски.
    """
    for vg in body.vertex_groups:
        if vg.name not in low.vertex_groups:
            low.vertex_groups.new(name=vg.name)
    mod = low.modifiers.new('weights', 'DATA_TRANSFER')
    mod.object = body
    mod.use_vert_data = True
    mod.data_types_verts = {'VGROUP_WEIGHTS'}
    mod.vert_mapping = 'POLYINTERP_NEAREST'
    apply_mods(low)

    armmod = low.modifiers.new('Armature', 'ARMATURE')
    armmod.object = arm

    # Раунд правок 2: было low.parent = arm; low.matrix_parent_inverse =
    # arm.matrix_world.inverted() — математически эквивалентно (обнуляет
    # матрицу родителя), но экспортёр FBX реконструирует Lcl Rotation для
    # low иначе, чем для parent_set(): body_meshes приезжают из FBX, где
    # парентинг сделан Блендером, и переживают экспорт верно; low с ручной
    # matrix_parent_inverse — нет (при реимпорте получал произвольный лишний
    # поворот в 90°, ширина/высота кимоно уезжали в глубину — см.
    # коммит-сообщение и task-4-report.md). parent_set(keep_transform=True)
    # — тот же результат в сцене (мировое положение low не меняется), но
    # matrix_parent_inverse считает сам Blender, и с ней экспорт стабилен.
    bpy.ops.object.select_all(action='DESELECT')
    low.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='OBJECT', keep_transform=True)


def strip_covered(body):
    """Тело под тканью удаляем: иначе на ударе ногой оно пробьёт штанину."""
    import bmesh
    name_of = {g.index: g.name for g in body.vertex_groups}
    bm = bmesh.new()
    bm.from_mesh(body.data)
    layer = bm.verts.layers.deform.verify()
    doomed = []
    for v in bm.verts:
        w = v[layer]
        if not w:
            continue
        name = name_of.get(max(w.keys(), key=lambda k: w[k]), '')
        if any(k in name for k in KEEP):
            continue
        if any(c in name for c in COVERED):
            doomed.append(v)
    bmesh.ops.delete(bm, geom=doomed, context='VERTS')
    bm.to_mesh(body.data)
    body.data.update()
    bm.free()
    return len(doomed)


def export(path, objs):
    _install_deterministic_fbx_uuids()
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True,
        object_types={'MESH', 'ARMATURE'}, add_leaf_bones=False,
        bake_anim=False, apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True, axis_forward='-Z', axis_up='Y',
        use_mesh_modifiers=True)


def verify_export(path):
    """Единственная проверка, которая читает сам записанный FBX, а не
    состояние сцены до export_scene.fbx.

    Раунд правок 2: экспортёр Blender однажды тихо испортил геометрию
    кимоно ровно на шаге записи — причина в transfer_weights() (ручной
    matrix_parent_inverse, см. комментарий там), а все прежние assert'ы
    смотрели только на сцену до export() и потому прошли, пока на диск
    ложился негодный файл. Эта проверка перечитывает path тем же
    импортёром, каким его позже прочтёт Unity.
    """
    reset_scene()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    kimono = next((o for o in new if o.name == 'Kimono_low'), None)
    assert kimono is not None, f'в {path} нет меша Kimono_low — экспорт переименовал его?'
    body = [o for o in new if o is not kimono]
    bmn, bmx = world_bbox(body)
    kmn, kmx = world_bbox([kimono])
    height = bmx.z - bmn.z
    assert 1.5 < height < 2.0, (
        f'в готовом FBX тело {height:.3f} м вне человеческого роста')
    assert kmn.z >= bmn.z - 0.05, (
        f'в готовом FBX низ кимоно {kmn.z:.3f} ниже стоп {bmn.z:.3f} больше чем на 5 см')
    assert kmx.z - bmn.z >= 0.5 * height, (
        f'в готовом FBX верх кимоно на {kmx.z - bmn.z:.3f} м от стоп — не доезжает '
        f'до плеч (нужно хотя бы {0.5 * height:.3f} м)')
    combined = max(bmx.z, kmx.z) - min(bmn.z, kmn.z)
    assert combined < height + 0.1, (
        f'в готовом FBX кимоно и тело не совпадают по высоте — общий охват '
        f'{combined:.3f} м при росте тела {height:.3f} м')


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

    coverage = uv_coverage(low)
    assert coverage >= UV_COVERAGE_MIN, (
        f'атлас заполнен на {coverage:.1%} — развёртка развалилась '
        f'(ниже порога {UV_COVERAGE_MIN:.0%}, см. UV_COVERAGE_MIN)')
    print(f'kimono_fit: атлас заполнен на {coverage:.1%}')

    is_belt = capture_belt_mask(low)
    bake(low, kimono, a.out)
    apply_belt_split(low, is_belt)

    # High-poly больше не нужен. Удаляем сразу: в экспорт по
    # object_types={'MESH'} он уехал бы молча, и вместо 12k в Unity
    # приехало бы 305k.
    bpy.data.objects.remove(kimono, do_unlink=True)

    body = max(body_meshes, key=tri_count)
    transfer_weights(low, body, arm)
    removed = strip_covered(body)
    assert removed > 0, 'ни одна вершина тела не вырезана — имена костей не mixamorig?'
    assert tri_count(body) > 0, 'вырезано всё тело — COVERED слишком широк'

    skinned = sum(1 for v in low.data.vertices if v.groups)
    assert skinned == len(low.data.vertices), (
        f'{len(low.data.vertices) - skinned} вершин кимоно без весов — '
        'ткань останется висеть в воздухе')

    fbx = os.path.join(a.out, '..', 'KimonoFighter.fbx')
    export(os.path.normpath(fbx), [arm, low] + body_meshes)
    verify_export(os.path.normpath(fbx))
    print(f'kimono_fit: подгонка scale={scale:.4f}, '
          f'{high_tris} -> {got} трисов, вырезано {removed} вершин тела, '
          f'экспорт {os.path.normpath(fbx)}')


if __name__ == '__main__':
    main()
