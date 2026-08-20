"""Кит моста: high-poly -> low-poly запекание перил и свай арены.

Запуск (headless):
  blender --background --factory-startup --python bridge_kit.py -- --out <dir>

Детерминирован: фиксированный python-сид, Cycles CPU с seed=0. Два прогона
обязаны давать побайтно одинаковые файлы — диффы в git осмысленные.

Договорённости с Unity-стороной (Assets/Editor/BridgeKit.cs):
  - линейные детали лежат длинной осью по X, столбы и сваи — по Y;
  - C# масштабирует деталь по её баундам под свои константы, поэтому здесь
    номинальные размеры, а не источник истины;
  - UV каждой детали живут в своём прямоугольнике общего атласа: albedo в
    Unity — тайловая карта дерева по этим же UV, поэтому проекция кубическая
    (u вдоль волокна), а не smart_project со случайной ориентацией островов.
"""
import argparse
import datetime
import hashlib
import os
import random
import sys

import bpy
import numpy as np

# Два источника недетерминизма в стоковом FBX-экспортёре Blender, оба вне
# нашего кода — правим их monkeypatch'ем перед экспортом:
#
# 1) CreationTimeStamp/CreationTime в заголовке FBX — реальный datetime.now().
#    Замораживаем "сейчас" на постоянную дату.
class _FrozenDateTime(datetime.datetime):
    @classmethod
    def now(cls, tz=None):
        return cls(2026, 7, 31, 0, 0, 0)


datetime.datetime = _FrozenDateTime


# 2) UUID узлов (Document/Model/Geometry/...) — io_scene_fbx.fbx_utils
#    считает их через встроенный hash(key) от имени объекта. hash() строк
#    рандомизирован на процесс; PYTHONHASHSEED из окружения не помогает —
#    Blender стартует Python с ignore_environment=1 и его не читает. Патчим
#    только шаг hash(key) на стабильный по байтам md5, остальную логику
#    (сокращение UUID, разрешение коллизий) оставляем как в оригинале.
def _install_deterministic_fbx_uuids():
    import io_scene_fbx.fbx_utils as fbx_utils

    def _key_to_uuid(uuids, key):
        if isinstance(key, int) and 0 <= key < 2**63:
            uuid = key
        else:
            uuid = int.from_bytes(
                hashlib.md5(repr(key).encode('utf-8')).digest()[:8], 'little')
            if uuid >= 2**63:
                uuid //= 2
        if uuid > int(1e9):
            t_uuid = uuid % int(1e9)
            if t_uuid not in uuids:
                uuid = t_uuid
        if uuid in uuids:
            inc = 1 if uuid < 2**62 else -1
            while uuid in uuids:
                uuid += inc
                if 0 > uuid >= 2**63:
                    raise ValueError(f'Unable to generate an UUID for key {key!r}')
        return fbx_utils.UUID(uuid)

    fbx_utils._key_to_uuid = _key_to_uuid


SEED = 20260731
ATLAS = 2048
SMOOTHNESS = 0.5      # альфа маски; ровный отклик, как у M_ArenaWood
AO_SAMPLES = 64
MARGIN = 4            # пиксели выпуска запекания за остров

# имя -> (builder, потолок трисов low-poly, прямоугольник атласа (u0, v0, du, dv))
PARTS = {}


def register(name, cap, rect):
    def deco(fn):
        PARTS[name] = (fn, cap, rect)
        return fn
    return deco


# ---------------------------------------------------------------- хелперы

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.seed = 0
    sc.cycles.use_animated_seed = False
    sc.cycles.samples = AO_SAMPLES
    sc.render.bake.use_clear = False
    sc.render.bake.margin = MARGIN


def apply_mods(o):
    bpy.context.view_layer.objects.active = o
    for m in list(o.modifiers):
        bpy.ops.object.modifier_apply(modifier=m.name)


def box_part(name, sx, sy, sz, bevel=0.008, segs=2):
    """Брус: куб с фаской. Низкополигональная основа детали."""
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    o.scale = (sx, sy, sz)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    b = o.modifiers.new('bevel', 'BEVEL')
    b.width = bevel
    b.segments = segs
    b.limit_method = 'ANGLE'
    apply_mods(o)
    return o


def cyl_part(name, r_top, r_bot, h, sides=12):
    """Бревно: цилиндр с сужением кверху, ось Y."""
    bpy.ops.mesh.primitive_cylinder_add(vertices=sides, radius=r_bot, depth=h)
    o = bpy.context.active_object
    o.name = name
    o.data.name = name
    # ось цилиндра у Blender — Z; повернуть на Y и вморозить
    o.rotation_euler = (-1.5707963, 0.0, 0.0)
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    # горизонтальные разрезы, чтобы сужение и изгиб было чем нести
    sub = o.modifiers.new('cuts', 'SUBSURF')
    sub.subdivision_type = 'SIMPLE'
    sub.levels = 1
    apply_mods(o)
    # сузить верх: верхняя половина вершин стягивается к оси
    for v in o.data.vertices:
        t = (v.co.y / h) + 0.5           # 0 внизу, 1 наверху
        k = 1.0 + (r_top / r_bot - 1.0) * max(0.0, t)
        v.co.x *= k
        v.co.z *= k
    return o


def clone_high(low, layers):
    """High-poly: копия low + subdiv + процедурный рельеф.

    layers: [(тип текстуры, noise_scale, сила смещения), ...]
    """
    bpy.ops.object.select_all(action='DESELECT')
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.duplicate()
    hi = bpy.context.active_object
    hi.name = low.name + '_hi'
    sub = hi.modifiers.new('sub', 'SUBSURF')
    sub.subdivision_type = 'SIMPLE'
    sub.levels = 5
    for i, (kind, scale, strength) in enumerate(layers):
        tex = bpy.data.textures.new(f'{hi.name}_t{i}', type=kind)
        tex.noise_scale = scale
        d = hi.modifiers.new(f'disp{i}', 'DISPLACE')
        d.texture = tex
        d.strength = strength
        d.mid_level = 0.5
    apply_mods(hi)
    return hi


def uv_into_rect(o, rect):
    """Кубическая проекция + нормировка в прямоугольник атласа.

    Кубическая, а не smart_project: у smart_project острова ложатся под
    случайными углами, а albedo в Unity — тайловое дерево по этим UV, и
    волокно обязано идти вдоль детали. На круглых деталях перед и зад
    зеркалятся в одни тексели — на 4 экранных пикселях это не видно.
    """
    bpy.ops.object.select_all(action='DESELECT')
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.cube_project(cube_size=1.0)
    bpy.ops.object.mode_set(mode='OBJECT')
    uvs = o.data.uv_layers[0].data
    us = [l.uv[0] for l in uvs]
    vs = [l.uv[1] for l in uvs]
    u0, v0 = min(us), min(vs)
    du = max(1e-6, max(us) - u0)
    dv = max(1e-6, max(vs) - v0)
    ru, rv, rdu, rdv = rect
    pad = MARGIN / ATLAS
    for l in uvs:
        l.uv[0] = ru + pad + (l.uv[0] - u0) / du * (rdu - 2 * pad)
        l.uv[1] = rv + pad + (l.uv[1] - v0) / dv * (rdv - 2 * pad)


def bake_target_material(o, img):
    mat = bpy.data.materials.new(o.name + '_bake')
    mat.use_nodes = True
    node = mat.node_tree.nodes.new('ShaderNodeTexImage')
    node.image = img
    mat.node_tree.nodes.active = node
    o.data.materials.clear()
    o.data.materials.append(mat)


def bake_pair(low, high, normal_img, ao_img):
    """Печёт high -> low обе карты. Остальные детали скрыты от лучей."""
    for other in bpy.data.objects:
        other.hide_render = other not in (low, high)
    bpy.ops.object.select_all(action='DESELECT')
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low

    bake_target_material(low, normal_img)
    bpy.ops.object.bake(type='NORMAL', use_selected_to_active=True,
                        cage_extrusion=0.02, max_ray_distance=0.06,
                        margin=MARGIN, use_clear=False)
    bake_target_material(low, ao_img)
    bpy.ops.object.bake(type='AO', use_selected_to_active=True,
                        cage_extrusion=0.02, max_ray_distance=0.06,
                        margin=MARGIN, use_clear=False)
    low.data.materials.clear()


def fill(img, rgba):
    px = np.tile(np.array(rgba, dtype=np.float32), ATLAS * ATLAS)
    img.pixels.foreach_set(px)


def save_png(img, path):
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()


def tri_count(o):
    o.data.calc_loop_triangles()
    return len(o.data.loop_triangles)


# ---------------------------------------------------------------- детали

@register('Post', cap=350, rect=(0.00, 0.0, 0.25, 0.5))
def post():
    """Промежуточный столб 9x9, тёсаный. Скруглённая макушка — фаской покрупнее
    сверху не выйдет из box_part, поэтому макушку осаживает displace high-poly,
    а low несёт только общую фаску."""
    low = box_part('Post', 0.09, 0.74, 0.09, bevel=0.010, segs=2)
    # Силы подняты в ~3.5 раза против первой версии: при 106 px/м тёс глубиной 4 мм — это
    # полпикселя, деталировка пеклась честно и не читалась с игровой дистанции вовсе.
    hi = clone_high(low, [
        ('CLOUDS', 0.35, 0.010),    # волокно
        ('VORONOI', 0.16, 0.016),   # следы топора — гранёные плоскости
    ])
    return low, hi


@register('PostEnd', cap=400, rect=(0.25, 0.0, 0.25, 0.5))
def post_end():
    """Начальный столб 14x14, макушка на два ската."""
    low = box_part('PostEnd', 0.14, 0.74, 0.14, bevel=0.012, segs=2)
    # два ската: верхние вершины разъезжаются по высоте знаком X
    top = max(v.co.y for v in low.data.vertices)
    for v in low.data.vertices:
        if v.co.y > top - 0.02:
            v.co.y -= 0.02 + 0.025 * abs(v.co.x) / 0.07  # конёк в центре, скаты к краям
    hi = clone_high(low, [
        ('CLOUDS', 0.4, 0.012),
        ('VORONOI', 0.2, 0.018),
    ])
    return low, hi


@register('Rail1m', cap=180, rect=(0.50, 0.0, 0.25, 0.5))
def rail():
    """Поручень: верх затёрт ладонями до скругления — фаска сверху крупнее."""
    low = box_part('Rail1m', 1.0, 0.06, 0.08, bevel=0.012, segs=2)
    hi = clone_high(low, [
        ('CLOUDS', 0.5, 0.007),
    ])
    # затёртость: у high верхние рёбра осаживаются к центру сечения
    for v in hi.data.vertices:
        if v.co.y > 0.02:
            v.co.y -= 0.020 * (abs(v.co.z) / 0.04) ** 2
    return low, hi


@register('LowerRail1m', cap=120, rect=(0.75, 0.0, 0.25, 0.5))
def lower_rail():
    """Круглая жердь со снятой корой: сучки — буграми displace."""
    # 6 граней, не 8: subsurf в cyl_part удваивает их, а 12 после удвоения — потолок cap 120
    low = cyl_part('LowerRail1m', 0.0275, 0.0275, 1.0, sides=6)
    low.rotation_euler = (0.0, 0.0, 1.5707963)   # ось Y -> X: жердь лежит вдоль моста
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    hi = clone_high(low, [
        ('CLOUDS', 0.3, 0.008),
        ('STUCCI', 0.12, 0.010),   # сучки
    ])
    return low, hi


@register('Sill1m', cap=90, rect=(0.00, 0.5, 0.25, 0.5))
def sill():
    """Порожек: грубая фаска, торцы со следом пилы (рельефом high-poly)."""
    low = box_part('Sill1m', 1.0, 0.08, 0.14, bevel=0.010, segs=1)
    hi = clone_high(low, [
        ('CLOUDS', 0.45, 0.010),
        ('WOOD', 0.08, 0.008),      # кольца пилы на торцах
    ])
    return low, hi


@register('PileBeam', cap=150, rect=(0.25, 0.5, 0.25, 0.5))
def pile_beam():
    """Насадка на пару свай, с врубками по посадочным местам (±0.62 от центра
    в мировых Z, здесь это ±0.62 по X при длине 1.45)."""
    low = box_part('PileBeam', 1.45, 0.10, 0.20, bevel=0.010, segs=2)
    hi = clone_high(low, [
        ('CLOUDS', 0.4, 0.010),
    ])
    # врубки: нижняя грань high осаживается над посадочными местами свай
    for v in hi.data.vertices:
        if v.co.y < -0.02 and (abs(v.co.x - 0.62) < 0.09 or abs(v.co.x + 0.62) < 0.09):
            v.co.y += 0.012
    return low, hi


@register('Pile', cap=380, rect=(0.50, 0.5, 0.25, 0.5))
def pile():
    """Свая: комель (низ) шире вершины, кольцевые трещины у головы."""
    low = cyl_part('Pile', r_top=0.075, r_bot=0.095, h=1.6, sides=12)
    hi = clone_high(low, [
        ('CLOUDS', 0.35, 0.014),      # кора снята, но бревно живое
        ('MUSGRAVE', 0.10, 0.019),    # продольные трещины
    ])
    # кольцевые трещины у головы: верхняя четверть high пережимается волной
    import math
    for v in hi.data.vertices:
        if v.co.y > 0.4:
            v.co.x *= 1.0 - 0.035 * (0.5 + 0.5 * math.sin(v.co.y * 55.0))
            v.co.z *= 1.0 - 0.035 * (0.5 + 0.5 * math.sin(v.co.y * 55.0))
    return low, hi


@register('Lashing', cap=220, rect=(0.75, 0.5, 0.25, 0.5))
def lashing():
    """Вязка: low — бочкообразная обечайка, витки верёвки печёт high-poly
    из настоящих торов. Это то, что AAA и делает: геометрия витков на 4
    экранных пикселях — расход, а нормали читаются."""
    low = cyl_part('Lashing', r_top=0.085, r_bot=0.085, h=0.11, sides=10)
    # лёгкая бочка: середина чуть шире, чтобы силуэт не был идеальным цилиндром
    for v in low.data.vertices:
        k = 1.0 + 0.06 * (1.0 - (abs(v.co.y) / 0.055) ** 2)
        v.co.x *= k
        v.co.z *= k

    # high: 5 витков-торов вокруг той же оси + узел
    coils = []
    for i in range(5):
        y = -0.044 + i * 0.022
        bpy.ops.mesh.primitive_torus_add(major_radius=0.085, minor_radius=0.011,
                                         major_segments=48, minor_segments=12,
                                         location=(0.0, 0.0, 0.0))
        t = bpy.context.active_object
        t.rotation_euler = (-1.5707963, 0.0, 0.35 * i)
        t.location = (0.0, y, 0.0)
        coils.append(t)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.016, segments=16, ring_count=8,
                                         location=(0.088, 0.0, 0.02))
    coils.append(bpy.context.active_object)
    bpy.ops.object.select_all(action='DESELECT')
    for c in coils:
        c.select_set(True)
    bpy.context.view_layer.objects.active = coils[0]
    bpy.ops.object.join()
    hi = bpy.context.active_object
    hi.name = 'Lashing_hi'
    tex = bpy.data.textures.new('rope_fibre', type='CLOUDS')
    tex.noise_scale = 0.02
    d = hi.modifiers.new('fibre', 'DISPLACE')
    d.texture = tex
    # x2, не x3.5 как у дерева: волокно глубже 5 мм разорвало бы 11-мм жилу витка
    d.strength = 0.005
    d.mid_level = 0.5
    apply_mods(hi)
    return low, hi


# ---------------------------------------------------------------- сборка

def main():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    parser = argparse.ArgumentParser(add_help=False)
    parser.add_argument('--out', required=True)
    out_dir = os.path.abspath(parser.parse_args(argv).out)
    os.makedirs(out_dir, exist_ok=True)

    random.seed(SEED)
    reset_scene()
    _install_deterministic_fbx_uuids()

    normal_img = bpy.data.images.new('kit_n', ATLAS, ATLAS, alpha=False)
    normal_img.colorspace_settings.name = 'Non-Color'
    fill(normal_img, (0.5, 0.5, 1.0, 1.0))
    ao_img = bpy.data.images.new('kit_ao', ATLAS, ATLAS, alpha=False)
    ao_img.colorspace_settings.name = 'Non-Color'
    fill(ao_img, (1.0, 1.0, 1.0, 1.0))

    lows, total = [], 0
    for name, (fn, cap, rect) in PARTS.items():
        low, hi = fn()
        uv_into_rect(low, rect)
        bake_pair(low, hi, normal_img, ao_img)
        bpy.data.objects.remove(hi, do_unlink=True)
        tris = tri_count(low)
        total += tris
        print(f'bridge_kit: {name} {tris} tris (cap {cap})')
        if tris > cap:
            print(f'bridge_kit: FAIL — {name} над потолком')
            sys.exit(1)
        lows.append(low)

    # Потолок 2500 держим в синхроне с BridgeKit.Verify() в Assets/Editor/BridgeKit.cs.
    if total > 2500:
        print(f'bridge_kit: FAIL — кит целиком {total} трисов (потолок 2500)')
        sys.exit(1)

    save_png(normal_img, os.path.join(out_dir, 'T_BridgeKit_N.png'))

    # маска Arena.shader: G — окклюзия, A — гладкость, R/B не читаются
    ao_px = np.zeros(ATLAS * ATLAS * 4, dtype=np.float32)
    ao_img.pixels.foreach_get(ao_px)
    ao_px = ao_px.reshape(-1, 4)
    mask = np.zeros_like(ao_px)
    mask[:, 1] = ao_px[:, 0]
    mask[:, 3] = SMOOTHNESS
    mask_img = bpy.data.images.new('kit_mask', ATLAS, ATLAS, alpha=True)
    mask_img.colorspace_settings.name = 'Non-Color'
    mask_img.pixels.foreach_set(mask.ravel())
    save_png(mask_img, os.path.join(out_dir, 'T_BridgeKit_Mask.png'))

    bpy.ops.object.select_all(action='DESELECT')
    for o in lows:
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(out_dir, 'BridgeKit.fbx'),
        use_selection=True, object_types={'MESH'},
        apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
        axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True)

    print(f'bridge_kit: OK — {len(lows)} деталей, {total} трисов, атлас {ATLAS}')


if __name__ == '__main__':
    main()
