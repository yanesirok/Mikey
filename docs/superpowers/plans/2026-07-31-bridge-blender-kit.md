# Blender-кит моста: план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Перила и сваи моста из запечённого в Blender кита деталей — тёсаные силуэты, рельеф волокна в картах нормалей, верёвочные вязки — вместо боксов и шестигранных трубок.

**Architecture:** `Tools/Blender/bridge_kit.py` headless строит high-poly → low-poly кит из 8 деталей, печёт атласы нормалей и маски (AO в G), экспортирует FBX; результат коммитится. `Assets/Editor/BridgeKit.cs` читает детали из FBX; `BambooArena.BuildBridge` штампует их в новый бейк `M_ArenaBridgeKit.mesh` со своим материалом — вся расстановка (кривая, наклоны, тона) остаётся в C#. Спека: `docs/superpowers/specs/2026-07-31-bridge-blender-kit-design.md`.

**Tech Stack:** Blender 5.1 (`C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`), Python/bpy/numpy, Unity (редактор проекта), C#.

## Global Constraints

- Композиция ограды неизменна: 8 столбов мимо x = 0, `postTop 0.39`, `railTop 0.36`, `lowerRail 0.19`, порожек `0.08`, 7 длин порожка на сторону, ближняя сторона — только порожек. Все значения уже в `BuildFarRailing`/`BuildRailing` — не менять.
- Потолки трисов: кит ≤ **40 000**, арена целиком ≤ **150 000** (поднимается со 115 000).
- Не трогать: палитру, грейд, туман, воду, рощу, камеру, бой, `FightRules`, доски настила, `NailHead`, стрингеры, `Blades`-мох у свай.
- Blender-скрипт детерминирован: python `random.seed(20260731)`, Cycles CPU, `seed = 0`. Два прогона → побайтно одинаковые файлы.
- Выход Blender коммитится: `Assets/Fight/Arena/BridgeKit/BridgeKit.fbx`, `T_BridgeKit_N.png`, `T_BridgeKit_Mask.png`. Сборка Unity никогда не запускает Blender.
- Один новый материал `M_ArenaBridgeKit` = один дополнительный дро-кол. Шейдер `Mikey/Arena` не редактируется: окклюзия уже читается из G-канала `_MaskMap` (`Arena.shader:182`), нормали — вручную `rgb*2-1` (`Arena.shader:167`), поэтому оба PNG импортируются как обычные текстуры с **выключенным sRGB**, ни в коем случае не как Unity NormalMap (та перекодирует каналы и сломает ручное декодирование).
- Вершинный цвет = тинт × сценовая AO (`BakeOcclusion` умножает RGB) — кит проходит через `BakeOcclusion` так же, как timber.

## Известные точки кода (для исполнителя без контекста)

| Что | Где |
|---|---|
| Вся арена, класс `Bake` (`Push/Tri/Box/Tube/ToMesh`) | `Assets/Editor/BambooArena.cs` (Bake: строка ~2405) |
| Мост: `BuildBridge` / `BuildRailing` / `BuildFarRailing` / `Beam` | `BambooArena.cs:273/460/522/425` |
| Сваи (трубки, насадка, вязки) | `BambooArena.cs:366-410` |
| Сохранение мешей/материалов | `AddMesh`/`SaveMesh`/`ArenaMaterial`/`TimberMaterial`, `BambooArena.cs:2083-2150` |
| Сценовая AO | `BakeOcclusion(Mesh, Mesh[])`, `BambooArena.cs:1689` — умножает `colors[i]`, alpha ставит 1 |
| Бюджет и лог трисов | `BambooArena.cs:234-267` (потолок 115000 — строка 257) |
| Пересборка арены | меню `Mikey/Rebuild Arena` или `Unity.exe -batchmode -quit -projectPath . -executeMethod FightSceneSetup.RebuildArena` |
| Кадр | `Unity.exe -batchmode -quit -projectPath . -executeMethod FightCapture.Shoot -captureOut Temp/fight_capture.png -captureSize 1920x1080` (или меню `Mikey/Capture Fight Screenshot`) |
| Сборка плеера | `-executeMethod FightBuild.Build` (меню `Mikey/Build Fight Player`) |

Unity-редактор у пользователя обычно открыт — batch-команды требуют его закрыть; меню-пункты равнозначны.

---

### Task 1: Blender-харнесс + первая деталь (Post)

**Files:**
- Create: `Tools/Blender/bridge_kit.py`
- Create: `Tools/Blender/build_bridge_kit.ps1`

**Interfaces:**
- Produces: `Assets/Fight/Arena/BridgeKit/BridgeKit.fbx` (объекты-детали по именам), `T_BridgeKit_N.png` (тангент-нормали, фон 0.5/0.5/1), `T_BridgeKit_Mask.png` (G = AO, A = 0.5). Имя детали этой задачи: `Post`. Реестр `PARTS` и хелперы (`box_part`, `clone_high`, `subdiv_displace`, `uv_into_rect`, `bake_pair`) — их используют задачи 2–3.
- Consumes: ничего.

- [ ] **Step 1: Написать `Tools/Blender/build_bridge_kit.ps1`**

```powershell
# Прогоняет Blender headless и падает, если скрипт упал.
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$out = Join-Path $PSScriptRoot "..\..\Assets\Fight\Arena\BridgeKit"
& $blender --background --factory-startup --python (Join-Path $PSScriptRoot "bridge_kit.py") -- --out $out
exit $LASTEXITCODE
```

- [ ] **Step 2: Написать каркас `Tools/Blender/bridge_kit.py` с деталью Post**

Полный файл этой задачи (задачи 2–3 только добавляют функции-детали в `PARTS`):

```python
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
import os
import random
import sys

import bpy
import numpy as np

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
    hi = clone_high(low, [
        ('CLOUDS', 0.35, 0.0030),    # волокно
        ('VORONOI', 0.16, 0.0045),   # следы топора — гранёные плоскости
    ])
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

    if total > 2500:
        print(f'bridge_kit: FAIL — кит целиком {total} трисов (потолок 2500)')
        sys.exit(1)
    print(f'bridge_kit: OK — {len(lows)} деталей, {total} трисов, атлас {ATLAS}')


main()
```

- [ ] **Step 3: Прогнать и убедиться, что выход появился**

Run: `powershell -ExecutionPolicy Bypass -File Tools/Blender/build_bridge_kit.ps1`
Expected: exit 0, в консоли `bridge_kit: Post ... tris` и `bridge_kit: OK — 1 деталей ...`; в `Assets/Fight/Arena/BridgeKit/` лежат `BridgeKit.fbx`, `T_BridgeKit_N.png`, `T_BridgeKit_Mask.png`. Если Blender 5.1 переименовал параметр API (`cage_extrusion`, `foreach_set` и т.п.) — чинить по сообщению об ошибке, суть шага не меняется.

- [ ] **Step 4: Проверка детерминизма**

Run: сохранить хеши (`Get-FileHash Assets/Fight/Arena/BridgeKit/*`), прогнать ps1 второй раз, сравнить.
Expected: хеши совпали. Если AO-карта дрожит — зафиксировать потоки: `sc.render.threads_mode = 'FIXED'; sc.render.threads = 1` в `reset_scene()`, повторить.

- [ ] **Step 5: Commit**

```bash
git add Tools/Blender/bridge_kit.py Tools/Blender/build_bridge_kit.ps1 "Assets/Fight/Arena/BridgeKit"
git commit -m "feat: Blender-харнесс кита моста и первая деталь — столб"
```

---

### Task 2: Брусовые детали — PostEnd, Rail1m, LowerRail1m, Sill1m, PileBeam

**Files:**
- Modify: `Tools/Blender/bridge_kit.py` (секция «детали»)

**Interfaces:**
- Consumes: хелперы и реестр из задачи 1.
- Produces: детали `PostEnd`, `Rail1m`, `LowerRail1m`, `Sill1m`, `PileBeam` в том же FBX/атласе. Линейные — длинной осью по X.

- [ ] **Step 1: Добавить пять builder-функций после `post()`**

```python
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
        ('CLOUDS', 0.4, 0.0035),
        ('VORONOI', 0.2, 0.0050),
    ])
    return low, hi


@register('Rail1m', cap=180, rect=(0.50, 0.0, 0.25, 0.5))
def rail():
    """Поручень: верх затёрт ладонями до скругления — фаска сверху крупнее."""
    low = box_part('Rail1m', 1.0, 0.06, 0.08, bevel=0.012, segs=3)
    hi = clone_high(low, [
        ('CLOUDS', 0.5, 0.0020),
    ])
    # затёртость: у high верхние рёбра осаживаются к центру сечения
    for v in hi.data.vertices:
        if v.co.y > 0.02:
            v.co.y -= 0.006 * (abs(v.co.z) / 0.04) ** 2
    return low, hi


@register('LowerRail1m', cap=120, rect=(0.75, 0.0, 0.25, 0.5))
def lower_rail():
    """Круглая жердь со снятой корой: сучки — буграми displace."""
    # 6 граней, не 8: subsurf в cyl_part удваивает их, а 12 после удвоения — потолок cap 120
    low = cyl_part('LowerRail1m', 0.0275, 0.0275, 1.0, sides=6)
    low.rotation_euler = (0.0, 0.0, 1.5707963)   # ось Y -> X: жердь лежит вдоль моста
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=False)
    hi = clone_high(low, [
        ('CLOUDS', 0.3, 0.0022),
        ('STUCCI', 0.12, 0.0030),   # сучки
    ])
    return low, hi


@register('Sill1m', cap=90, rect=(0.00, 0.5, 0.25, 0.5))
def sill():
    """Порожек: грубая фаска, торцы со следом пилы (рельефом high-poly)."""
    low = box_part('Sill1m', 1.0, 0.08, 0.14, bevel=0.010, segs=1)
    hi = clone_high(low, [
        ('CLOUDS', 0.45, 0.0030),
        ('WOOD', 0.08, 0.0025),     # кольца пилы на торцах
    ])
    return low, hi


@register('PileBeam', cap=150, rect=(0.25, 0.5, 0.25, 0.5))
def pile_beam():
    """Насадка на пару свай, с врубками по посадочным местам (±0.62 от центра
    в мировых Z, здесь это ±0.62 по X при длине 1.45)."""
    low = box_part('PileBeam', 1.45, 0.10, 0.20, bevel=0.010, segs=2)
    hi = clone_high(low, [
        ('CLOUDS', 0.4, 0.0030),
    ])
    # врубки: нижняя грань high осаживается над посадочными местами свай
    for v in hi.data.vertices:
        if v.co.y < -0.02 and (abs(v.co.x - 0.62) < 0.09 or abs(v.co.x + 0.62) < 0.09):
            v.co.y += 0.012
    return low, hi
```

- [ ] **Step 2: Прогнать**

Run: `powershell -ExecutionPolicy Bypass -File Tools/Blender/build_bridge_kit.ps1`
Expected: exit 0, `bridge_kit: OK — 6 деталей ...`, каждый под своим cap.

- [ ] **Step 3: Commit**

```bash
git add Tools/Blender/bridge_kit.py "Assets/Fight/Arena/BridgeKit"
git commit -m "feat: брусовые детали кита — столбы, поручень, жердь, порожек, насадка"
```

---

### Task 3: Круглые детали — Pile и Lashing

**Files:**
- Modify: `Tools/Blender/bridge_kit.py` (секция «детали»)

**Interfaces:**
- Consumes: хелперы из задачи 1.
- Produces: `Pile` (бревно, ось Y, комель шире), `Lashing` (низкополигональная обечайка, витки верёвки живут в картах). После этой задачи кит полон — 8 деталей.

- [ ] **Step 1: Добавить две builder-функции**

```python
@register('Pile', cap=380, rect=(0.50, 0.5, 0.25, 0.5))
def pile():
    """Свая: комель (низ) шире вершины, кольцевые трещины у головы."""
    low = cyl_part('Pile', r_top=0.075, r_bot=0.095, h=1.6, sides=12)
    hi = clone_high(low, [
        ('CLOUDS', 0.35, 0.0040),     # кора снята, но бревно живое
        ('MUSGRAVE', 0.10, 0.0055),   # продольные трещины
    ])
    # кольцевые трещины у головы: верхняя четверть high пережимается волной
    import math
    for v in hi.data.vertices:
        if v.co.y > 0.4:
            v.co.x *= 1.0 - 0.02 * (0.5 + 0.5 * math.sin(v.co.y * 55.0))
            v.co.z *= 1.0 - 0.02 * (0.5 + 0.5 * math.sin(v.co.y * 55.0))
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
    d.strength = 0.0025
    d.mid_level = 0.5
    apply_mods(hi)
    return low, hi
```

- [ ] **Step 2: Прогнать полный кит**

Run: `powershell -ExecutionPolicy Bypass -File Tools/Blender/build_bridge_kit.ps1`
Expected: exit 0, `bridge_kit: OK — 8 деталей, N трисов` с N ≤ 2500. Открыть `T_BridgeKit_N.png` — в прямоугольнике Lashing должны читаться диагональные витки; если прямоугольник плоский-синий, high не попал в кейдж — поднять `cage_extrusion` до 0.03.

- [ ] **Step 3: Повторить проверку детерминизма (два прогона, хеши)**

Expected: побайтно одинаково.

- [ ] **Step 4: Commit**

```bash
git add Tools/Blender/bridge_kit.py "Assets/Fight/Arena/BridgeKit"
git commit -m "feat: свая и верёвочная вязка — кит моста полон, 8 деталей"
```

---

### Task 4: Unity-загрузчик кита

**Files:**
- Create: `Assets/Editor/BridgeKit.cs`

**Interfaces:**
- Consumes: `Assets/Fight/Arena/BridgeKit/BridgeKit.fbx` и два PNG из задач 1–3.
- Produces (для задачи 5):
  - `BridgeKit.Part { Vector3[] Positions; Vector3[] Normals; Vector2[] Uvs; int[] Triangles; Bounds Bounds; }`
  - `BridgeKit.Part Get(string name)` — null + `Debug.LogError` при отсутствии кита или детали;
  - `BridgeKit.Reset()` — сброс кэша (вызывается в начале сборки арены);
  - `BridgeKit.EnsureImportSettings()` — FBX readable, текстуры линейные;
  - константы `BridgeKit.NormalPath`, `BridgeKit.MaskPath`;
  - меню `Mikey/Verify Bridge Kit` — прогоняемая проверка задачи.

- [ ] **Step 1: Написать `Assets/Editor/BridgeKit.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Детали моста, запечённые Blender-скриптом Tools/Blender/bridge_kit.py: восемь именованных
/// мешей в одном FBX плюс атласы нормалей и маски. Этот класс только читает их; вся
/// расстановка — позиции, кривая провиса, наклоны, тона — остаётся в BambooArena, потому что
/// выверена против конкретной камеры и правится там одной константой.
///
/// Договорённость по осям: линейные детали лежат длинной осью по X, столбы и сваи — по Y.
/// BambooArena масштабирует деталь по её баундам под свои размеры, поэтому здесь нет ни
/// одного номинального размера — источник истины один, и он в C#.
/// </summary>
public static class BridgeKit
{
    public const string Dir = "Assets/Fight/Arena/BridgeKit/";
    public const string FbxPath = Dir + "BridgeKit.fbx";
    public const string NormalPath = Dir + "T_BridgeKit_N.png";
    public const string MaskPath = Dir + "T_BridgeKit_Mask.png";

    public static readonly string[] Required =
        { "Post", "PostEnd", "Rail1m", "LowerRail1m", "Sill1m", "Pile", "PileBeam", "Lashing" };

    public sealed class Part
    {
        public Vector3[] Positions;
        public Vector3[] Normals;
        public Vector2[] Uvs;
        public int[] Triangles;
        public Bounds Bounds;
    }

    private static Dictionary<string, Part> _parts;

    public static Part Get(string name)
    {
        if (_parts == null)
            Load();
        if (_parts != null && _parts.TryGetValue(name, out Part part))
            return part;
        Debug.LogError($"BridgeKit: деталь '{name}' не найдена в {FbxPath} — " +
                       "прогони Tools/Blender/build_bridge_kit.ps1 и закоммить результат.");
        return null;
    }

    /// <summary>Сбрасывает кэш, чтобы пересборка арены увидела переэкспортированный FBX.</summary>
    public static void Reset() => _parts = null;

    private static void Load()
    {
        EnsureImportSettings();
        List<Mesh> meshes = AssetDatabase.LoadAllAssetsAtPath(FbxPath).OfType<Mesh>().ToList();
        if (meshes.Count == 0)
        {
            Debug.LogError($"BridgeKit: {FbxPath} отсутствует или пуст — " +
                           "прогони Tools/Blender/build_bridge_kit.ps1.");
            return;
        }
        _parts = new Dictionary<string, Part>();
        foreach (Mesh m in meshes)
            _parts[m.name] = new Part
            {
                Positions = m.vertices,
                Normals = m.normals,
                Uvs = m.uv,
                Triangles = m.triangles,
                Bounds = m.bounds,
            };
    }

    /// <summary>
    /// FBX должен быть readable (BambooArena читает вершины в редакторе), материалы из него не
    /// нужны. Оба PNG — данные, не цвет: шейдер декодирует нормали вручную (rgb*2-1) и читает
    /// AO из G-канала, поэтому sRGB выключен и тип текстуры Default — Unity NormalMap
    /// перекодировал бы каналы под UnpackNormal, которого в Arena.shader нет.
    /// </summary>
    public static void EnsureImportSettings()
    {
        if (AssetImporter.GetAtPath(FbxPath) is ModelImporter model &&
            (!model.isReadable || model.materialImportMode != ModelImporterMaterialImportMode.None))
        {
            model.isReadable = true;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importAnimation = false;
            model.SaveAndReimport();
        }
        foreach (string path in new[] { NormalPath, MaskPath })
            if (AssetImporter.GetAtPath(path) is TextureImporter tex &&
                (tex.sRGBTexture || tex.textureType != TextureImporterType.Default))
            {
                tex.sRGBTexture = false;
                tex.textureType = TextureImporterType.Default;
                tex.SaveAndReimport();
            }
    }

    [MenuItem("Mikey/Verify Bridge Kit")]
    public static void Verify()
    {
        Reset();
        int total = 0;
        foreach (string name in Required)
        {
            Part p = Get(name);
            if (p == null)
                return; // ошибка уже в консоли
            int tris = p.Triangles.Length / 3;
            total += tris;
            Vector3 s = p.Bounds.size;
            // Договорённость по осям, на которой держится масштабирование в BambooArena.
            // Вязка приземистая — у неё длинной оси нет, её не проверяем.
            bool linear = name.EndsWith("1m") || name == "PileBeam";
            bool tall = name == "Post" || name == "PostEnd" || name == "Pile";
            bool axisOk = linear ? s.x >= s.y && s.x >= s.z
                                 : !tall || (s.y >= s.x && s.y >= s.z);
            if (!axisOk)
                Debug.LogError($"BridgeKit: у '{name}' длинная ось не там — баунды {s}. " +
                               "Линейные детали лежат по X, столбы и сваи — по Y.");
            Debug.Log($"BridgeKit: {name} — {tris} трисов, баунды {s}.");
        }
        if (total > 2500)
            Debug.LogError($"BridgeKit: кит целиком {total} трисов (потолок 2500).");
        else
            Debug.Log($"BridgeKit: OK — {Required.Length} деталей, {total} трисов.");
    }
}
```

- [ ] **Step 2: Прогнать проверку**

Run: в Unity меню `Mikey/Verify Bridge Kit` (или batch `-executeMethod BridgeKit.Verify`).
Expected: в консоли восемь строк `BridgeKit: <имя> — ...` без ошибок и `BridgeKit: OK`. Если имена мешей из FBX пришли с суффиксами Blender — поправить имена объектов в `bridge_kit.py` (объект и его `data` должны называться именем детали), переэкспортировать.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/BridgeKit.cs Assets/Editor/BridgeKit.cs.meta
git commit -m "feat: загрузчик Blender-кита моста с проверкой Mikey/Verify Bridge Kit"
```

---

### Task 5: Штамповка кита в BambooArena

**Files:**
- Modify: `Assets/Editor/BambooArena.cs` — `Build()`, `BuildBridge`, `BuildRailing`, `BuildFarRailing`, бюджетный блок; новые `Stamp`/`StampBeam`/`FitTrs`/`PileTint`/`BridgeKitMaterial`; удаление замещённого кода.

**Interfaces:**
- Consumes: `BridgeKit.Get/Reset/NormalPath/MaskPath` из задачи 4; существующие `Bake`, `ArenaMaterial`, `AddMesh`, `BakeOcclusion`, `DeckHeight`, константы ограды.
- Produces: `Assets/Fight/Arena/M_ArenaBridgeKit.mesh`, `M_ArenaBridgeKit.mat`; объект `BridgeKit` под корнем `Arena`.

- [ ] **Step 1: Новые хелперы рядом с `Beam` (после строки ~438)**

```csharp
    /// <summary>Матрица, ставящая деталь кита центром баундов в centre и растягивающая её
    /// по-осево под target (в осях детали — до поворота rot).</summary>
    private static Matrix4x4 FitTrs(BridgeKit.Part part, Vector3 target, Vector3 centre,
                                    Quaternion rot)
    {
        Vector3 s = part.Bounds.size;
        var scale = new Vector3(target.x / s.x, target.y / s.y, target.z / s.z);
        return Matrix4x4.TRS(centre, rot, scale) * Matrix4x4.Translate(-part.Bounds.center);
    }

    /// <summary>Штампует деталь кита в бейк. Тинт — функцией от мировой точки, потому что
    /// свая одним мешем проходит три среды (воздух, мокрая полоса, под водой) и цвет обязан
    /// смениться посреди детали. topTint — как у Bake.Box: осветление граней, глядящих в небо,
    /// затёртый верх поручня.</summary>
    private static void Stamp(Bake bake, BridgeKit.Part part, Matrix4x4 trs,
                              System.Func<Vector3, Color> tint, float topTint = 0f)
    {
        if (part == null)
            return; // кита нет — ошибка уже в консоли, сборка не валится каскадом
        Matrix4x4 nrm = trs.inverse.transpose;
        int firstIndex = -1;
        for (int i = 0; i < part.Positions.Length; i++)
        {
            Vector3 p = trs.MultiplyPoint3x4(part.Positions[i]);
            Vector3 n = nrm.MultiplyVector(part.Normals[i]).normalized;
            Color c = tint(p);
            if (topTint > 0f)
            {
                float lift = topTint * Mathf.Clamp01(n.y);
                c = new Color(c.r + lift, c.g + lift, c.b + lift, c.a);
            }
            int idx = bake.Push(p, n, part.Uvs[i], Vector2.zero, c);
            if (firstIndex < 0)
                firstIndex = idx;
        }
        for (int t = 0; t < part.Triangles.Length; t += 3)
            bake.Tri(firstIndex + part.Triangles[t], firstIndex + part.Triangles[t + 1],
                     firstIndex + part.Triangles[t + 2]);
    }

    /// <summary>Как Beam, но деталью кита: хорда между двумя точками кривой настила.</summary>
    private static void StampBeam(Bake bake, BridgeKit.Part part, float x0, float x1,
                                  float above, float z, Vector2 section, Color colour,
                                  float topTint = 0f)
    {
        float y0 = DeckHeight(x0) + above, y1 = DeckHeight(x1) + above;
        float run = x1 - x0, rise = y1 - y0;
        Stamp(bake, part,
              FitTrs(part, new Vector3(Mathf.Sqrt(run * run + rise * rise), section.x, section.y),
                     new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, z),
                     Quaternion.Euler(0f, 0f, Mathf.Atan2(rise, run) * Mathf.Rad2Deg)),
              _ => colour, topTint);
    }

    /// <summary>Свая одним мешем: над водой — цвет балки, 15 см мокрой полосы над зеркалом,
    /// ниже — темнее и зеленее. Ступенька-сдвиг подводной части (имитация преломления у
    /// трёхсегментной трубы) не переносится: на цельном меше она стоила бы разреза, а на
    /// грациозном угле камеры это было 2 пикселя.</summary>
    private static Color PileTint(float y)
    {
        if (y < WaterY) return new Color(0.16f, 0.24f, 0.2f);
        if (y < WaterY + 0.15f) return new Color(0.3f, 0.32f, 0.32f);
        return new Color(0.5f, 0.48f, 0.44f);
    }
```

- [ ] **Step 2: Материал кита рядом с `TimberMaterial` (~строка 2150)**

```csharp
    /// <summary>Тот же дровяной отклик, что у M_ArenaWood, но нормали и маска — из атласов
    /// кита: форма и волокно запечены с high-poly, окклюзия щелей — в G маски. Albedo остаётся
    /// общей картой дерева: UV кита кладут волокно вдоль деталей, а тон разводят вершинные
    /// цвета — печь третий атлас не за чем.</summary>
    private static Material BridgeKitMaterial(ArenaTextures.Surface wood)
    {
        BridgeKit.EnsureImportSettings();
        Material mat = ArenaMaterial("M_ArenaBridgeKit", CullMode.Back, 0f, 1f, 0f, 1f, 0.55f);
        mat.SetTexture("_BaseMap", wood.Albedo);
        mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BridgeKit.NormalPath));
        mat.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BridgeKit.MaskPath));
        // 1.0, не 0.55 как у настила: там карта — синтетическое волокно поверх плоских досок
        // и на полной силе делала вельвет; здесь карта несёт саму форму — фаски, витки, врубки.
        mat.SetFloat("_BumpScale", 1f);
        mat.SetColor("_RimColor", Srgb(0.72f, 0.78f, 0.82f));
        mat.SetFloat("_RimStrength", 0.35f);
        mat.SetFloat("_RimPower", 3.5f);
        mat.EnableKeyword("_NORMALMAP");
        return mat;
    }
```

- [ ] **Step 3: Сваи — заменить трубки штамповкой (в `BuildBridge`, блок строк ~366-410)**

Сигнатура меняется: `private static void BuildBridge(Bake bake, Bake kit, Bake foliage)`. Цикл по сваям становится:

```csharp
        BridgeKit.Part pilePart = BridgeKit.Get("Pile");
        BridgeKit.Part beamPart = BridgeKit.Get("PileBeam");
        BridgeKit.Part lashPart = BridgeKit.Get("Lashing");
        foreach (float x in PileX)
        {
            // Насадка, связывающая пару свай, — прежняя высота и прежний тон.
            Stamp(kit, beamPart,
                  FitTrs(beamPart, new Vector3(1.45f, 0.1f, 0.2f),
                         new Vector3(x, DeckHeight(x) - 0.255f, 0f),
                         Quaternion.Euler(0f, 90f, 0f)),
                  _ => beam * 0.94f);

            for (int i = -1; i <= 1; i += 2)
            {
                var head = new Vector3(x, DeckHeight(x) - 0.2f, i * 0.62f);
                var foot = new Vector3(x + i * 0.12f, WaterY - 0.85f, i * 0.86f);
                Vector3 axis = head - foot;
                Quaternion lean = Quaternion.FromToRotation(Vector3.up, axis.normalized);
                Stamp(kit, pilePart,
                      FitTrs(pilePart, new Vector3(0.16f, axis.magnitude, 0.16f),
                             (head + foot) * 0.5f, lean),
                      p => PileTint(p.y));
                // Вязка на голове сваи — тот же стык нагрузки, что держали трубки.
                Stamp(kit, lashPart,
                      FitTrs(lashPart, new Vector3(0.185f, 0.11f, 0.185f),
                             head - Vector3.up * 0.0125f, lean),
                      _ => rope);
                Vector3 damp = Vector3.Lerp(foot, head,
                    Mathf.InverseLerp(foot.y, head.y, WaterY + 0.15f));
                if (Random.value < 0.75f)
                    Blades(foliage, damp + new Vector3(Random.Range(-0.05f, 0.05f), 0f, i * 0.06f),
                           Random.Range(0.08f, 0.16f), 1.4f, 0.5f, 5,
                           Srgb(0.16f, 0.22f, 0.11f) * Random.Range(0.8f, 1.2f), Random.value, 0f, 1f);
            }
        }

        BuildRailing(kit, plankPale);
        BuildAbutments(bake);
```

Удаляются: `bake.Box` насадки, три `bake.Tube` сегментов сваи, `bake.Tube` вязки, локальная функция `At`, локали `submerged` и `wet` (их значения теперь живут в `PileTint`). Мох `Blades` остаётся — позиция `damp` считается лерпом, как в удалённом `At`.

- [ ] **Step 4: Порожек и ограда — штамповка вместо `Beam`/`Box`/`Tube`**

`BuildRailing(Bake kit, Color pale)`: тело то же, но `Beam(...)` в цикле порожка заменяется на

```csharp
        BridgeKit.Part sillPart = BridgeKit.Get("Sill1m");
        foreach (int side in new[] { -1, 1 })
            for (int s = 0; s < sillLengths; s++)
                StampBeam(kit, sillPart,
                          Mathf.Lerp(-HalfLength, HalfLength, s / (float)sillLengths),
                          Mathf.Lerp(-HalfLength, HalfLength, (s + 1) / (float)sillLengths),
                          sillHeight * 0.5f, side * sillZ,
                          new Vector2(sillHeight, sillWidth), pale * 0.95f, topTint: 0.08f);
```

`BuildFarRailing(Bake kit, Color pale, float sillZ)`: константы и весь комментарий класса остаются. Столбы:

```csharp
        BridgeKit.Part postPart = BridgeKit.Get("Post");
        BridgeKit.Part postEndPart = BridgeKit.Get("PostEnd");
        BridgeKit.Part lashPart = BridgeKit.Get("Lashing");
        for (int i = 0; i < posts; i++)
        {
            float x = PostX(i);
            bool end = i == 0 || i == posts - 1;
            // Начальные столбы толще — 14 см против 9: мост объявляет себя с торцов.
            float thick = (end ? 0.14f : postSection) * Random.Range(0.92f, 1.08f);
            Color tone = timber * Random.Range(0.94f, 1.06f);
            Stamp(kit, end ? postEndPart : postPart,
                  FitTrs(end ? postEndPart : postPart,
                         new Vector3(thick, postTop - postFoot, thick),
                         new Vector3(x, DeckHeight(x) + (postTop + postFoot) * 0.5f, sillZ),
                         Quaternion.Euler(Random.Range(-0.6f, 0.6f), 0f, Random.Range(-0.6f, 0.6f))),
                  _ => tone);
            // Вязка у подошвы, где столб проходит сквозь порожек, — 8 штук, по числу столбов.
            Stamp(kit, lashPart,
                  FitTrs(lashPart, new Vector3(thick + 0.06f, 0.09f, thick + 0.06f),
                         new Vector3(x, DeckHeight(x) + 0.115f, sillZ), Quaternion.identity),
                  _ => rope);
        }
```

Пролёты (`rope` придётся передать параметром или поднять цвета моста в статические поля класса — поднять поля: `plankPale/plankMid/plankDark/beam/nail/rope` из локалей `BuildBridge` в `private static readonly` поля с теми же значениями и комментарием, локали удалить):

```csharp
        BridgeKit.Part railPart = BridgeKit.Get("Rail1m");
        BridgeKit.Part lowerPart = BridgeKit.Get("LowerRail1m");
        for (int i = 0; i < posts - 1; i++)
        {
            float a = PostX(i), b = PostX(i + 1);
            StampBeam(kit, railPart,
                      a - (i == 0 ? overhang : 0f), b + (i == posts - 2 ? overhang : 0f),
                      railTop - railHeight * 0.5f, sillZ, new Vector2(railHeight, railWidth),
                      timber * Random.Range(0.96f, 1.04f), 0.18f);
            StampBeam(kit, lowerPart, a, b, lowerRail, sillZ,
                      new Vector2(lowerRadius * 2f, lowerRadius * 2f),
                      timber * Random.Range(0.92f, 1.02f));
        }
```

После замены `Beam(...)` больше нигде не вызывается (проверить `grep "Beam("` — если остались вызовы вне ограды, оставить хелпер; если нет — удалить `Beam` целиком).

- [ ] **Step 5: `Build()` — новый бейк, AO, материал, бюджет**

В `Build()` (строки ~115-268):

```csharp
        BridgeKit.Reset();               // переэкспортированный FBX подхватывается без перезапуска
        var bridgeKit = new Bake();      // рядом с var timber = new Bake();
        BuildBridge(timber, bridgeKit, foliage);
        // Штамповка не тянет Random так, как тянули трубки, поэтому пересев: иначе любая
        // правка кита перемешивала бы рощу. Роща переложится один раз — на этом коммите.
        Random.InitState(Seed + 1);
        BuildProps(timber, foliage);
        // ... остальные Build* без изменений
        Mesh kitMesh = bridgeKit.ToMesh("ArenaBridgeKit");
        BakeOcclusion(timberMesh, new[] { timberMesh, bambooMesh, kitMesh });
        BakeOcclusion(kitMesh, new[] { timberMesh, bambooMesh, kitMesh });
        kitMesh.RecalculateTangents();   // рядом с timberMesh.RecalculateTangents()
        GameObject kitGo = AddMesh(root, "BridgeKit", kitMesh, BridgeKitMaterial(wood),
                                   castShadows: true);
        // Кит — мост: отражается всегда, вместе с настилом.
        if (reflected >= 0)
            kitGo.layer = reflected;
```

Бюджетный блок: `int kitTris = kitMesh.triangles.Length / 3;` входит в `arenaTris` и в `Debug.Log`; потолок меняется — `if (arenaTris > 150000)` с текстом `(max 150000)` и правкой комментария (арена подросла на кит сознательно, спека 2026-07-31); новый трипвайр:

```csharp
        if (kitTris > 40000)
            Debug.LogError($"BambooArena: bridge kit over budget — {kitTris} tris (max 40000).");
```

- [ ] **Step 6: Пересобрать арену и прочитать лог**

Run: меню `Mikey/Rebuild Arena` (или batch `-executeMethod FightSceneSetup.RebuildArena`).
Expected: в консоли ни одной ошибки; строка трисов содержит kit; `M_ArenaBridgeKit.mesh` и `M_ArenaBridgeKit.mat` появились в `Assets/Fight/Arena/`; инварианты (`layout invariants`, берег/вода) молчат — пересев рощи их не сломал; в сцене у объекта `Arena/BridgeKit` слой отражения и материал с двумя атласами.

- [ ] **Step 7: Commit**

```bash
git add Assets/Editor/BambooArena.cs "Assets/Fight/Arena"
git commit -m "feat: перила и сваи штампуются из Blender-кита — боксы и трубки ушли"
```

---

### Task 6: Проверка целиком

**Files:**
- Modify: `docs/superpowers/specs/2026-07-31-bridge-blender-kit-design.md` (строка статуса)

**Interfaces:**
- Consumes: всё собранное; `FightCapture.Shoot`, `FightBuild.Build`, тесты проекта.

- [ ] **Step 1: Кадр и осмотр**

Run: `Unity.exe -batchmode -quit -projectPath . -executeMethod FightCapture.Shoot -captureOut Temp/fight_kit.png -captureSize 1920x1080` (Unity закрыт; либо меню `Mikey/Capture Fight Screenshot`).
Expected/осмотр кадра (инструментом Read по PNG):
- ограда: столбы и поручень читаются тёсаными (фаски ловят свет), нижняя жердь круглая, вязки у подошв видны как верёвка;
- сваи: круглые, комель шире, мокрая полоса и подводное затемнение на месте;
- тональный коридор: настил и перила остаются в 100–125 против неба ~219 (замер регионов кадра как в спеке 29.07 — усреднение пикселей по прямоугольникам настила/перил/неба); бойцы и небо не сдвинулись;
- роща переложилась (ожидаемо после пересева) — берег нигде не выходит из воды у схода моста.

- [ ] **Step 2: Тесты и плеер**

Run: `Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Temp/kit_tests.xml`; затем `Unity.exe -batchmode -quit -projectPath . -executeMethod FightBuild.Build -buildOut Build/Fight/Mikey.exe`.
Expected: все тесты зелёные (в т.ч. `FightRulesTests` — кривая настила не тронута), сборка `FightBuild: succeeded`.

- [ ] **Step 3: Спека — статус**

В `2026-07-31-bridge-blender-kit-design.md` строка `**Статус:**` → `дизайн согласован, реализовано`. Если по ходу выяснились отклонения (имена параметров Blender API, фактические трисы) — дописать одним абзацем «Как вышло на деле» в конец спеки.

- [ ] **Step 4: Commit**

```bash
git add docs/superpowers/specs/2026-07-31-bridge-blender-kit-design.md
git commit -m "docs: кит моста реализован — кадр, тесты и плеер проверены"
```

---

## Заметки исполнителю

- **Blender API дрейфует.** Скрипт писан по API 4.x; в 5.1 отдельные именованные аргументы (`cage_extrusion`, `apply_scale_options`, `foreach_set`) могли переехать. Ошибка скажет куда; смысл шага не меняется. Проверяй `blender --version` и мануал соответствующей версии.
- **Random-дисциплина в BambooArena.** Любое добавление/удаление вызова `Random.*` до конца `BuildBridge` меняет всё после него. Пересев `Random.InitState(Seed + 1)` после `BuildBridge` — сознательная развязка; не добавляй других пересевов.
- **Не «улучшай» композицию.** Высоты, чётность столбов, отсутствие балясин, одинокий порожек на ближней стороне — это решения против конкретной камеры, задокументированные в спеках 27-29.07. Кит меняет качество членов, не их расстановку.
