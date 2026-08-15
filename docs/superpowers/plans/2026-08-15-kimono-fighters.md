# Кимоно-бойцы — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить офисного `Ch15_nonPBR` на бойца в кимоно — тело генерируется MPFB2, кимоно из `kimono.glb` шьётся в Blender — и закрыть два анимационных долга настоящим карате из собственных мокапов: ёко гери вместо удара рукой и аге-укэ вместо щитовой стойки.

**Architecture:** Готового бесплатного бойца в ги не существует, поэтому тело и кимоно берутся из разных мест и сшиваются headless-скриптом Blender. Тело генерируется MPFB2 прямо в Blender со скелетом `mixamo_unity`; клипы берутся из уже лежащих в репозитории мокапов. Скрипт кимоно переиспользует из `bridge_kit.py` детерминированный FBX-экспорт и запек high→low: у кимоно 305k трисов и ноль текстур, весь вид держится на складках, поэтому децимация возможна только вместе с запеком нормалей. Unity-сторона не меняет ни одного контракта — тот же Humanoid-аватар, тот же `Fighter.controller`, те же имена состояний.

**Tech Stack:** Blender 5.1 (bpy, Cycles CPU, аддон MPFB2), Unity 6000.3.18f1 (Humanoid retarget, Mecanim), Unity CLI 1.0.0-beta.3, NUnit EditMode.

**Spec:** `docs/superpowers/specs/2026-08-15-kimono-fighters-design.md` (ревизия от 2026-08-15 — Mixamo мёртв, источник ассетов заменён)

## Global Constraints

- Blender: `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`.
- **`--factory-startup` нельзя в скриптах, которым нужен MPFB2** — он выключает аддоны. То же касается `bpy.ops.wm.read_factory_settings()`: он убивает MPFB прямо в запущенной сессии. Скрипт генерации тела чистит сцену вручную (`bpy.data.objects.remove`). Скрипты, которым MPFB не нужен (`bridge_kit.py`, `kimono_fit.py`), запускаются с `--factory-startup` как раньше.
- MPFB2 установлен и включён (`bl_ext.blender_org.mpfb`). Определения скелетов лежат в `%APPDATA%\Blender Foundation\Blender\5.1\extensions\blender_org\mpfb\data\rigs\standard\`.
- Unity: 6000.3.18f1. Тесты — только через Unity CLI: `unity test --mode EditMode`, не сырой `Unity.exe`.
- **Root motion не используется:** `Fighter.cs:59` двигает бойца через `transform.position`. Клипы — мокап с видео, они несут смещение корня, поэтому гасится оно настройкой импорта Root Transform Position (XZ) → Bake Into Pose.
- Бюджет кимоно — **12 000 треугольников**, атлас **2048²**.
- Детерминизм: два прогона Blender-скрипта дают побайтно одинаковый FBX. Конвенция репо, зафиксирована в шапке `bridge_kit.py`.
- Контракт аниматора не меняется: `float MoveSpeed`; триггеры `Punch`/`PunchB`/`Kick`/`Hit`/`BlockHit`; `bool Blocking`, `bool Dead`. Состояния: Idle, Walk, Punch, PunchB, Kick, Hit, BlockHit, Blocking, Death.
- Арт-бинарники в этом репозитории лежат непоследовательно: `Assets/BambooArena/**` в git, `Ch15_nonPBR.fbx` и весь `Assets/Characters/**` — нет, при том что `.gitignore` их не исключает. Новые ассеты коммитим, но после каждого коммита с бинарниками сверяемся **`git ls-files <путь>`**, а не `git status`.
- Старый `Ch15_nonPBR` и его материалы не удаляются, пока сцена не поднимется на новом бойце.

---

### Task 1: Сделать `bridge_kit.py` импортируемым

Файл заканчивается голым вызовом `main()`. Пока это так, `import bridge_kit` запускает сборку моста целиком — переиспользовать оттуда запек и детерминированный экспорт невозможно. Blender при запуске через `--python` выставляет `__name__ == '__main__'`, поэтому headless-прогон моста от правки не меняется.

**Files:**
- Modify: `tools/Blender/bridge_kit.py` (последняя строка)

**Interfaces:**
- Consumes: ничего.
- Produces: импортируемый модуль `bridge_kit` со свободными от побочных эффектов `_install_deterministic_fbx_uuids()`, `bake_pair(low, high, normal_img, ao_img)`, `fill(img, rgba)`, `save_png(img, path)`, `tri_count(o) -> int`, `apply_mods(o)`, `reset_scene()`, константами `ATLAS = 2048`, `MARGIN = 4`, `AO_SAMPLES = 64`.

- [ ] **Step 1: Написать падающую проверку**

Создать `tools/Blender/check_import.py`:

```python
"""Проверка: bridge_kit импортируется, не запуская сборку моста."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge_kit

for name in ('_install_deterministic_fbx_uuids', 'bake_pair', 'fill',
             'save_png', 'tri_count', 'apply_mods', 'reset_scene'):
    assert hasattr(bridge_kit, name), f'bridge_kit не отдаёт {name}'
assert bridge_kit.ATLAS == 2048
print('IMPORT_OK')
```

- [ ] **Step 2: Прогнать и убедиться, что падает**

```bash
"/c/Program Files/Blender Foundation/Blender 5.1/blender.exe" --background --factory-startup --python tools/Blender/check_import.py
```

Ожидание: FAIL. `main()` вызывается на импорте, `argparse` не находит обязательный `--out` и валит процесс с `SystemExit: 2`. Строки `IMPORT_OK` в выводе нет.

- [ ] **Step 3: Добавить guard**

В `tools/Blender/bridge_kit.py` последнюю строку `main()` заменить на:

```python
if __name__ == '__main__':
    main()
```

- [ ] **Step 4: Прогнать и убедиться, что проходит**

```bash
"/c/Program Files/Blender Foundation/Blender 5.1/blender.exe" --background --factory-startup --python tools/Blender/check_import.py
```

Ожидание: PASS, в выводе `IMPORT_OK`.

- [ ] **Step 5: Убедиться, что сама сборка моста не сломалась**

```bash
powershell -ExecutionPolicy Bypass -File tools/Blender/build_bridge_kit.ps1
```

Ожидание: exit 0 и строка `bridge_kit: OK — N деталей, M трисов, атлас 2048`. Прогон занимает несколько минут — это Cycles-запек AO на 64 сэмплах, так и должно быть.

- [ ] **Step 6: Коммит**

```bash
git add tools/Blender/bridge_kit.py tools/Blender/check_import.py
git commit -m "refactor: bridge_kit импортируем — main() под guard

Нужен для kimono_fit.py: запек high->low и детерминированный FBX
переиспользуются, а не пишутся второй раз."
```

---

### Task 2: Сгенерировать базовое тело в Blender (MPFB2)

Mixamo мёртв (см. спеку, раздел «Смена источника ассетов»), поэтому тело не скачивается, а генерируется. Рецепт проверен на живом Blender 5.1 до написания плана: `create_human` даёт меш 19158 вершин со 152 вертексными группами, `load_rig` вешает арматуру на 64 кости с префиксом `mixamorig:`, меш приезжает уже со скиннингом и модификатором `Armature`, рост 1.659 м в T-позе.

Тело базовое, без одежды — так и задумано: ткань садится на него чисто, а всё закрытое кимоно скрипт Task 4 всё равно вырезает.

**Files:**
- Create: `tools/Blender/make_body.py`
- Create: `tools/Blender/build_body.ps1`

**Interfaces:**
- Consumes: ничего.
- Produces: `Assets/Fight/character/body/Fighter_Body.fbx` — Humanoid-совместимое тело в T-позе, скелет `mixamorig:*` из `rig.mixamo_unity.json`. Task 3 читает этот путь.

- [ ] **Step 1: Написать падающую проверку**

Создать `tools/check_body.py`:

```python
"""Проверка сгенерированного тела: файл есть, в нём меш и скелет mixamorig.

FBX бинарный, поэтому проверяем по именам костей в байтах: они лежат там
как обычные строки. Так проверка не зависит ни от Blender, ни от Unity.
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BODY = os.path.join(ROOT, 'Assets/Fight/character/body/Fighter_Body.fbx')

# Кости, на которые опирается kimono_fit.py: fit() ищет Neck и LeftToeBase,
# strip_covered() отбирает по Spine/UpLeg/Shoulder и щадит Head/Hand/Foot.
REQUIRED = [b'mixamorig:Hips', b'mixamorig:Neck', b'mixamorig:LeftToeBase',
            b'mixamorig:Spine', b'mixamorig:LeftUpLeg', b'mixamorig:LeftShoulder',
            b'mixamorig:Head', b'mixamorig:LeftHand', b'mixamorig:LeftFoot']

fails = []
if not os.path.isfile(BODY):
    fails.append(f'нет тела: {BODY}')
else:
    data = open(BODY, 'rb').read()
    if b'Geometry' not in data:
        fails.append('в теле нет меша')
    for bone in REQUIRED:
        if bone not in data:
            fails.append(f'нет кости {bone.decode()}')
    size_mb = len(data) / 1024 / 1024
    if size_mb > 25:
        fails.append(f'тело весит {size_mb:.1f} МБ — похоже, уехали хелперы MPFB')

if fails:
    print('\n'.join('FAIL ' + f for f in fails))
    sys.exit(1)
print('BODY_OK')
```

- [ ] **Step 2: Прогнать и убедиться, что падает**

```bash
python tools/check_body.py
```

Ожидание: FAIL, одна строка — нет тела.

- [ ] **Step 3: Написать генератор**

Создать `tools/Blender/make_body.py`:

```python
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
```

- [ ] **Step 4: Написать обёртку запуска**

Создать `tools/Blender/build_body.ps1`:

```powershell
# Прогоняет Blender headless и падает, если скрипт упал.
# Без --factory-startup: он выключает MPFB2, без которого генерировать нечего.
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$root = Join-Path $PSScriptRoot "..\.."
$out = Join-Path $root "Assets\Fight\character\body\Fighter_Body.fbx"
& $blender --background --python (Join-Path $PSScriptRoot "make_body.py") -- --out $out
exit $LASTEXITCODE
```

- [ ] **Step 5: Прогнать генератор**

```bash
powershell -ExecutionPolicy Bypass -File tools/Blender/build_body.ps1
```

Ожидание: exit 0 и строка вида `make_body: 19158 вершин, 64 костей, рост 1.659 м -> ...`.

Если падает на `аддон MPFB2 не включён` — проверь, что в команде нет `--factory-startup`.

- [ ] **Step 6: Прогнать проверку и убедиться, что проходит**

```bash
python tools/check_body.py
```

Ожидание: PASS, в выводе `BODY_OK`.

- [ ] **Step 7: Коммит и сверка, что бинарник реально лёг**

```bash
git add tools/Blender/make_body.py tools/Blender/build_body.ps1 tools/check_body.py Assets/Fight/character/body
git commit -m "feat: базовое тело бойца генерируется MPFB2, скелет mixamo_unity

Mixamo не грузится ни у кого (три.js не поставляется, js/three*.js отдают
404), поэтому тело не качается, а генерируется на месте. rig.mixamo_unity
даёт кости с префиксом mixamorig:, на который опирается kimono_fit.py."
git ls-files Assets/Fight/character/body
```

Ожидание: `git ls-files` показывает `Fighter_Body.fbx`. Пустой вывод означает, что бинарник не лёг — в этом репозитории арт регулярно остаётся вне git, и `git status` этого не показывает.

---

### Task 3: `kimono_fit.py` — подгонка и low-poly с запечёнными картами

Первая половина скрипта: поставить кимоно на тело, сделать из 305k низкополигональную версию и перенести на неё складки запеком. Скиннинга здесь ещё нет — он в Task 4.

**Files:**
- Create: `tools/Blender/kimono_fit.py`
- Create: `tools/Blender/build_kimono.ps1`

**Interfaces:**
- Consumes: `bridge_kit` из Task 1 — `bake_pair`, `fill`, `save_png`, `tri_count`, `apply_mods`, `reset_scene`, `ATLAS`; тело `Assets/Fight/character/body/Fighter_Body.fbx` из Task 2.
- Produces: функции `parse_args()`, `world_bbox(objs) -> (Vector, Vector)`, `import_body(path) -> (armature, [mesh])`, `import_kimono(path) -> object`, `upright(o)`, `fit(kimono, arm, meshes, scale_mul, offset_z) -> float`, `make_low(high, target_tris) -> (object, int)`, `bake(low, high, out_dir)`. Task 4 достраивает этот же файл функциями скиннинга и экспорта.

- [ ] **Step 1: Написать скрипт с самопроверками**

Создать `tools/Blender/kimono_fit.py`:

```python
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
```

- [ ] **Step 2: Написать обёртку запуска**

Создать `tools/Blender/build_kimono.ps1` по образцу `build_bridge_kit.ps1`:

```powershell
# Прогоняет Blender headless и падает, если скрипт упал.
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$root = Join-Path $PSScriptRoot "..\.."
$out = Join-Path $root "Assets\Fight\character\kimono"
& $blender --background --factory-startup --python (Join-Path $PSScriptRoot "kimono_fit.py") -- `
    --body (Join-Path $root "Assets\Fight\character\body\Fighter_Body.fbx") `
    --kimono (Join-Path $root "Assets\Characters\Clothes\kimono.glb") `
    --out $out @args
exit $LASTEXITCODE
```

- [ ] **Step 3: Прогнать**

```bash
powershell -ExecutionPolicy Bypass -File tools/Blender/build_kimono.ps1
```

Ожидание: exit 0, строка `kimono_fit: подгонка scale=..., 305224 -> ~12000 трисов, карты в ...`, и два файла `T_Kimono_Normal.png`, `T_Kimono_AO.png` в `Assets/Fight/character/kimono/`.

Если падает на `assert` о масштабе или провале ниже стоп — это ровно то, для чего оставлены ручки: подобрать `--scale-mul` / `--offset-z` и записать подобранные значения в `build_kimono.ps1` как значения по умолчанию. Скан на чужом теле с первого раза не садится, и это не баг скрипта.

- [ ] **Step 4: Посмотреть глазами**

Открыть `Assets/Fight/character/kimono/T_Kimono_Normal.png`. Ожидание: узнаваемые складки ткани, а не ровная сиреневая заливка. Ровная заливка означает, что запек не сел — тогда причина в развёртке, и `island_margin` в `make_low` надо поднять.

- [ ] **Step 5: Коммит**

```bash
git add tools/Blender/kimono_fit.py tools/Blender/build_kimono.ps1
git commit -m "feat: kimono_fit — подгонка кимоно по костям и low-poly с запеком

305k трисов и ноль текстур: децимация возможна только вместе с запеком
нормалей, иначе от кимоно остаётся чёрный мешок. Запек и детерминизм
переиспользуются из bridge_kit."
```

---

### Task 4: Скиннинг кимоно и экспорт бойца

Вторая половина скрипта: перенести веса с тела на кимоно, вырезать закрытое тканью тело и выгрузить готовый FBX.

**Files:**
- Modify: `tools/Blender/kimono_fit.py` (добавить три функции и достроить `main()`)

**Interfaces:**
- Consumes: всё из Task 3.
- Produces: `Assets/Fight/character/KimonoFighter.fbx` — Humanoid-совместимый FBX со скелетом `mixamorig:*`, мешами тела (голова, шея, кисти, стопы) и low-poly кимоно.

- [ ] **Step 1: Дописать скиннинг, чистку и экспорт**

В `tools/Blender/kimono_fit.py` расширить импорт из `bridge_kit` — `_install_deterministic_fbx_uuids` нужен здесь впервые, в Task 3 его не было намеренно:

```python
from bridge_kit import (ATLAS, _install_deterministic_fbx_uuids, apply_mods,
                        bake_pair, fill, reset_scene, save_png, tri_count)
```

Затем добавить перед `main()`:

```python
# Кости, чью геометрию закрывает ткань, и кости, которые остаются наружу.
# KEEP проверяется первым: mixamorig:ForeArm содержит и Arm, и — по смыслу —
# запястье, но кисть обязана уцелеть.
# Buttock и Breast есть именно в скелете mixamo_unity (64 кости против 52 у
# обычного mixamo); без них ягодицы и грудь остались бы под тканью целыми.
# Hips — таз и пах, ровно под поясом кимоно: 218 вершин тела доминантно
# весят на него, и без этой строки они переживают вырезание.
COVERED = ('Hips', 'Spine', 'Chest', 'Arm', 'Shoulder', 'UpLeg', 'Leg',
           'Buttock', 'Breast')
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
    low.parent = arm
    low.matrix_parent_inverse = arm.matrix_world.inverted()


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
        name = name_of.get(max(w, key=lambda k: w[k]), '')
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
```

- [ ] **Step 2: Достроить `main()`**

Заменить в `main()` блок после `bake(low, kimono, a.out)` на:

```python
    bake(low, kimono, a.out)

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
    print(f'kimono_fit: подгонка scale={scale:.4f}, '
          f'{high_tris} -> {got} трисов, вырезано {removed} вершин тела, '
          f'экспорт {os.path.normpath(fbx)}')
```

Старую строку `print(f'kimono_fit: подгонка ...')` из Task 3 удалить — её заменяет эта.

- [ ] **Step 3: Прогнать**

```bash
powershell -ExecutionPolicy Bypass -File tools/Blender/build_kimono.ps1
```

Ожидание: exit 0, в выводе `вырезано N вершин тела` при N > 0 и путь до `KimonoFighter.fbx`.

Если падает на `ни одна вершина тела не вырезана` — у скачанного тела имена костей не `mixamorig:*`; посмотреть настоящие имена и поправить `COVERED`/`KEEP`.

- [ ] **Step 4: Проверить детерминизм**

```bash
powershell -ExecutionPolicy Bypass -File tools/Blender/build_kimono.ps1
sha256sum Assets/Fight/character/KimonoFighter.fbx > /tmp/h1
powershell -ExecutionPolicy Bypass -File tools/Blender/build_kimono.ps1
sha256sum Assets/Fight/character/KimonoFighter.fbx > /tmp/h2
diff /tmp/h1 /tmp/h2 && echo DETERMINISTIC_OK
```

Ожидание: `DETERMINISTIC_OK`. Иначе `_install_deterministic_fbx_uuids()` не отработал — проверить, что он вызывается до `export_scene.fbx`, а не после.

- [ ] **Step 5: Коммит**

```bash
git add tools/Blender/kimono_fit.py Assets/Fight/character/KimonoFighter.fbx Assets/Fight/character/kimono
git commit -m "feat: кимоно отскинено на тело, боец выгружен в FBX

Веса переносятся с тела (Data Transfer), а не автоматические: тело уже
отскинено правильно. Закрытое тканью тело вырезается — иначе на ударе
ногой оно пробивает штанину."
git ls-files Assets/Fight/character
```

Ожидание: `git ls-files` показывает `KimonoFighter.fbx` и обе карты.

---

### Task 5: Импорт бойца как Humanoid и материалы

Unity ассет ещё ни разу не импортировала — `KimonoFighter.fbx.meta` не существует. Редактор не запущен, а кликать в инспекторе исполнитель не может, поэтому и настройка импорта, и материалы делаются кодом, одним одноразовым Editor-скриптом, и запускаются headless через Unity CLI.

Две вещи, которые пришлось уточнить против первой редакции плана:

- **У `Character.shader` нет входа для AO.** Его свойства: `_BaseMap`, `_BumpMap`, `_BaseColor`, `_AlbedoGamma`, `_BumpScale`, `_Smoothness`, `_SpecStrength`, `_RimColor`, `_RimPower`, `_RimStrength`. Поэтому запечённый AO идёт в `_BaseMap`, а цвет ги задаётся через `_BaseColor`. Для одноцветной ткани без текстуры это и есть правильный albedo: AO, помноженный на тинт, даёт складкам глубину, и шейдер трогать не нужно. `_AlbedoGamma` при этом выставляется в 1: его дефолт 0.45 существует, чтобы вытягивать почти чёрный диффуз старого персонажа, а карту AO он бы просто пересветил.
- **Мешей у бойца два, а не один** — тело и кимоно. Из-под ткани видны голова, шея, кисти и стопы, им нужен свой материал кожи. Кожа у игрока и врага одна и та же, различаются только ги и пояс, поэтому материалов три, а не четыре.

**Files:**
- Create: `Assets/Editor/FighterImportSetup.cs`
- Create: `Assets/Fight/Tests/FighterModelTests.cs`
- Создаются скриптом: `Assets/Fight/character/M_Fighter_Skin.mat`, `M_Player_Kimono.mat`, `M_Enemy_Kimono.mat`

**Interfaces:**
- Consumes: `Assets/Fight/character/KimonoFighter.fbx` из Task 4, карты `Assets/Fight/character/kimono/T_Kimono_Normal.png` и `T_Kimono_AO.png` из Task 3, шейдер `Assets/Fight/character/Character.shader`.
- Produces: константа `FighterModelTests.ModelPath = "Assets/Fight/character/KimonoFighter.fbx"`; три материала по путям выше; модель, импортированная как Humanoid с аватаром из самой модели. Task 7 назначает эти материалы в сцене.

- [ ] **Step 1: Написать падающий тест**

Создать `Assets/Fight/Tests/FighterModelTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Mikey.Fight.Tests
{
    /// <summary>The fighter is retargeted mocap, so a model that imports as anything but
    /// Humanoid silently plays nothing at all. These are asset tests, not logic tests: they
    /// fail when an import setting is lost, which is exactly how the previous fighter broke.</summary>
    public class FighterModelTests
    {
        public const string ModelPath = "Assets/Fight/character/KimonoFighter.fbx";

        [Test]
        public void Model_ImportsAsHumanoid()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.IsNotNull(importer, ModelPath + " is not in the project");
            Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType);
        }

        [Test]
        public void Model_AvatarIsValidHuman()
        {
            var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Avatar>().FirstOrDefault();
            Assert.IsNotNull(avatar, "model carries no avatar");
            Assert.IsTrue(avatar.isValid, "avatar is invalid");
            Assert.IsTrue(avatar.isHuman, "avatar is not human");
        }

        /// <summary>A squashed avatar is the failure this project already hit once: the editor
        /// preview looked right and the running game showed a flattened fighter. Height is the
        /// cheapest signal that the rig scale survived the Blender round trip.</summary>
        [Test]
        public void Model_IsHumanHeight()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(go);
            var instance = Object.Instantiate(go);
            try
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                Assert.IsNotEmpty(renderers, "model has no renderers");
                var bounds = renderers[0].bounds;
                foreach (var r in renderers)
                    bounds.Encapsulate(r.bounds);
                Assert.That(bounds.size.y, Is.InRange(1.6f, 1.95f),
                    "fighter is " + bounds.size.y + " m tall");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>Body and cloth are separate meshes and must not share a material: the body
        /// shows only where the kimono does not cover it — head, neck, hands, feet — so one
        /// material for both would paint bare skin in gi colours.</summary>
        [Test]
        public void Model_HasSeparateBodyAndClothMeshes()
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            Assert.IsNotNull(go);
            var skins = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            Assert.AreEqual(2, skins.Length,
                "expected body and kimono, got " + string.Join(", ", skins.Select(s => s.name)));
        }

        [Test]
        public void Materials_ExistOnTheCharacterShader()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Fight/character/Character.shader");
            Assert.IsNotNull(shader, "Character.shader is missing");

            foreach (var path in new[]
                     {
                         "Assets/Fight/character/M_Fighter_Skin.mat",
                         "Assets/Fight/character/M_Player_Kimono.mat",
                         "Assets/Fight/character/M_Enemy_Kimono.mat",
                     })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(mat, path + " is missing");
                Assert.AreEqual(shader, mat.shader, path + " is on the wrong shader");
            }
        }

        /// <summary>The kimono has no albedo texture at all — its five materials are flat
        /// colours — so the baked AO doubles as the base map and the normal map carries the
        /// folds. Losing either reduces the garment to a flat silhouette, which is the whole
        /// failure this asset pipeline exists to avoid.</summary>
        [Test]
        public void KimonoMaterials_CarryTheBakedMaps()
        {
            foreach (var path in new[]
                     {
                         "Assets/Fight/character/M_Player_Kimono.mat",
                         "Assets/Fight/character/M_Enemy_Kimono.mat",
                     })
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                Assert.IsNotNull(mat, path + " is missing");
                Assert.IsNotNull(mat.GetTexture("_BumpMap"), path + " has no normal map");
                Assert.IsNotNull(mat.GetTexture("_BaseMap"), path + " has no base map");
            }
        }

        /// <summary>A normal map imported as a plain colour texture reads as coloured noise on
        /// the surface instead of relief — a silent, purely visual failure that no other test
        /// here would catch.</summary>
        [Test]
        public void NormalMap_IsImportedAsNormalMap()
        {
            var importer = AssetImporter.GetAtPath(
                "Assets/Fight/character/kimono/T_Kimono_Normal.png") as TextureImporter;
            Assert.IsNotNull(importer, "normal map is not in the project");
            Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType);
        }
    }
}
```

- [ ] **Step 2: Прогнать тесты и убедиться, что падают**

Прогон тестов запускает редактор, а он же импортирует ассеты — до этого шага `KimonoFighter.fbx.meta` в проекте нет вовсе.

```bash
unity test --mode EditMode --filter FighterModelTests --output test-results.xml
```

Ожидание: FAIL. `Model_ImportsAsHumanoid` валится на `animationType` — Unity импортирует FBX как Generic по умолчанию. Материалов не существует, поэтому `Materials_ExistOnTheCharacterShader` и `KimonoMaterials_CarryTheBakedMaps` тоже падают.

Если тест не находит модель вовсе — значит редактор ещё не импортировал ассет; повторный прогон после первого импорта это лечит.

- [ ] **Step 3: Написать Editor-скрипт настройки**

Создать `Assets/Editor/FighterImportSetup.cs`:

```csharp
using UnityEditor;
using UnityEngine;

namespace Mikey.FightEditor
{
    /// <summary>One-shot setup for the kimono fighter: import settings and materials.
    ///
    /// This exists as a script rather than as inspector clicks because the settings have to be
    /// reproducible — the fighter's FBX is regenerated by a Blender script whenever the body or
    /// the garment changes, and a regenerated asset comes back as Generic with no materials.
    /// Re-running this method restores the whole setup.
    /// </summary>
    public static class FighterImportSetup
    {
        const string CharacterDir = "Assets/Fight/character";
        const string ModelPath = CharacterDir + "/KimonoFighter.fbx";
        const string ShaderPath = CharacterDir + "/Character.shader";
        const string NormalPath = CharacterDir + "/kimono/T_Kimono_Normal.png";
        const string AoPath = CharacterDir + "/kimono/T_Kimono_AO.png";

        [MenuItem("Mikey/Setup Kimono Fighter")]
        public static void Run()
        {
            SetUpModel();
            SetUpNormalMap();
            CreateMaterials();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("FighterImportSetup: done");
        }

        static void SetUpModel()
        {
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null)
                throw new System.IO.FileNotFoundException(ModelPath + " is not in the project");

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;   // clips come from the mocap files, not from here
            importer.SaveAndReimport();
        }

        /// <summary>Without this the relief reads as coloured noise rather than folds.</summary>
        static void SetUpNormalMap()
        {
            var importer = AssetImporter.GetAtPath(NormalPath) as TextureImporter;
            if (importer == null)
                throw new System.IO.FileNotFoundException(NormalPath + " is not in the project");
            if (importer.textureType == TextureImporterType.NormalMap)
                return;

            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        static void CreateMaterials()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
                throw new System.IO.FileNotFoundException(ShaderPath + " is not in the project");

            var normal = AssetDatabase.LoadAssetAtPath<Texture>(NormalPath);
            var ao = AssetDatabase.LoadAssetAtPath<Texture>(AoPath);

            // Skin is shared: player and enemy are the same body and differ only in cloth.
            var skin = Material(shader, CharacterDir + "/M_Fighter_Skin.mat");
            skin.SetColor("_BaseColor", new Color(0.78f, 0.60f, 0.48f));
            skin.SetFloat("_AlbedoGamma", 1f);
            skin.SetFloat("_Smoothness", 0.22f);

            Kimono(shader, CharacterDir + "/M_Player_Kimono.mat", normal, ao,
                   new Color(0.91f, 0.89f, 0.85f), new Color(0.23f, 0.29f, 0.62f));
            Kimono(shader, CharacterDir + "/M_Enemy_Kimono.mat", normal, ao,
                   new Color(0.16f, 0.16f, 0.19f), new Color(0.48f, 0.12f, 0.16f));
        }

        /// <summary>The kimono has no albedo texture — all five of its source materials are flat
        /// colours — so the baked AO serves as the base map and the tint supplies the colour.
        /// _AlbedoGamma is forced to 1: its 0.45 default exists to lift the near-black diffuse of
        /// the previous character and would wash an AO map out.
        ///
        /// The belt is a separate submesh in the source garment, but the export merges the cloth
        /// into one mesh, so the belt colour is carried as the rim rather than as a second
        /// material — one draw call instead of two on a mobile target.
        /// </summary>
        static void Kimono(Shader shader, string path, Texture normal, Texture ao,
                           Color cloth, Color accent)
        {
            var mat = Material(shader, path);
            mat.SetTexture("_BaseMap", ao);
            mat.SetTexture("_BumpMap", normal);
            mat.SetColor("_BaseColor", cloth);
            mat.SetColor("_RimColor", accent);
            mat.SetFloat("_AlbedoGamma", 1f);
            mat.SetFloat("_BumpScale", 1.4f);
            mat.SetFloat("_Smoothness", 0.12f);   // cotton, not silk
        }

        static Material Material(Shader shader, string path)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            return mat;
        }
    }
}
```

- [ ] **Step 4: Выполнить скрипт headless**

Скрипт надо выполнить в редакторе, не открывая его руками. Используй навык `unity-cli` — он знает, как запустить проект в batch-режиме и выполнить статический метод; целевой метод здесь `Mikey.FightEditor.FighterImportSetup.Run`, он `public static` намеренно, потому что из CLI не видно ни `internal`, ни приватных членов.

Ожидание: в логе редактора строка `FighterImportSetup: done` и exit 0.

- [ ] **Step 5: Прогнать тесты и убедиться, что проходят**

```bash
unity test --mode EditMode --filter FighterModelTests --output test-results.xml
```

Ожидание: PASS, семь тестов.

Если валится `Model_HasSeparateBodyAndClothMeshes` с числом, отличным от 2, — значит Blender-экспорт слил меши или наоборот оставил лишний; это дефект Task 4, о нём надо сообщить, а не подгонять тест под факт.

- [ ] **Step 6: Коммит**

```bash
git add Assets/Editor/FighterImportSetup.cs Assets/Editor/FighterImportSetup.cs.meta Assets/Fight/Tests/FighterModelTests.cs Assets/Fight/Tests/FighterModelTests.cs.meta Assets/Fight/character Assets/Fight/character.meta
git commit -m "feat: боец импортируется как Humanoid, материалы кожи и ги

Настройка сделана скриптом, а не кликами: FBX бойца пересобирается
Blender-скриптом при каждой правке тела или ткани и возвращается
Generic'ом без материалов, так что настройку нужно уметь повторить.

AO уходит в _BaseMap, а не в отдельный слот: у Character.shader входа
для AO нет, а у кимоно нет альбедо-текстуры вовсе — все пять исходных
материалов плоские цвета. AO под тинтом и есть правильный albedo.

Тесты закрывают ровно тот отказ, который проект уже ловил: аватар,
переживший round trip через Blender, но приехавший сплющенным."
git ls-files Assets/Fight/character
```

Ожидание: `git ls-files` показывает три `.mat`, их `.meta`, модель и карты.

---

### Task 6: Пересобрать `Fighter.controller` на клипах из репозитория

Не «перевесить», а «проставить заново»: все семь GUID клипов в контроллере сейчас висячие, ни один не разрешается ни в один `.meta` проекта. Контроллер не играет ничего. Внешних загрузок не требуется — карате-техники лежат в собственных мокапах, недостающие бытовые движения в CC0-наборе Quaternius.

**Files:**
- Create: `Assets/Editor/FighterClipSetup.cs`
- Modify: `Assets/Fight/Fighter.controller`
- Modify: `Assets/Fight/Tests/FighterModelTests.cs` (добавить класс `FighterClipsTests`)
- Modify: настройки импорта клипов в `Assets/Fight/animations/*.fbx.meta` и `Assets/Characters/Karate/UAL1_Standard.fbx.meta` (правит скрипт, не руками)

**Interfaces:**
- Consumes: собственные мокапы `Assets/Fight/animations/video_*_BoyFBX.fbx` (клипы `FightIdle`, `OiZuki`, `Uraken_Swing`, `YokoGeri_High`, `AgeUke`, `Knockdown_GetUp`) и CC0-набор `Assets/Characters/Karate/UAL1_Standard.fbx` (клипы `Armature|Walk_Loop`, `Armature|Hit_Chest` — префикс у них настоящий, пак оставлен как его записал экспортёр).
- Produces: `Fighter.controller`, у которого каждое состояние имеет non-null motion, ни одно не ссылается на UAL2, а `Kick` играет ёко гери.

- [ ] **Step 1: Написать падающий тест**

Дописать в `Assets/Fight/Tests/FighterModelTests.cs` второй класс — **внутрь `namespace Mikey.Fight.Tests`**, то есть после закрывающей скобки класса `FighterModelTests` и перед закрывающей скобкой namespace. Блок `using` в шапке файла уже даёт всё нужное: `System.Linq`, `UnityEditor` (там же `AnimationUtility` и `AssetDatabase`), `UnityEngine`.

```csharp
    /// <summary>The controller is the contract between Fighter.cs and the art. A state with a
    /// null motion plays the bind pose and looks like a frozen fighter, not like an error — so
    /// it has to fail here rather than in someone's play session. Every clip reference in this
    /// controller was dangling when these tests were written; that is the failure they lock out.</summary>
    public class FighterClipsTests
    {
        const string ControllerPath = "Assets/Fight/Fighter.controller";

        static UnityEditor.Animations.AnimatorState[] States()
        {
            var ac = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(
                ControllerPath);
            Assert.IsNotNull(ac, ControllerPath + " is missing");
            return ac.layers
                .SelectMany(l => l.stateMachine.states)
                .Select(c => c.state)
                .ToArray();
        }

        static UnityEditor.Animations.AnimatorState State(string name)
        {
            var s = States().FirstOrDefault(x => x.name == name);
            Assert.IsNotNull(s, "no state named " + name);
            return s;
        }

        [Test]
        public void EveryState_HasMotion()
        {
            foreach (var s in States())
                Assert.IsNotNull(s.motion, "state " + s.name + " has no motion");
        }

        /// <summary>Kick used to play Punch_Cross and Blocking used to borrow UAL2's shield
        /// stance, because the CC0 pack had neither a kick nor an unarmed block. Both are paid
        /// off by the project's own karate mocap. UAL1 is deliberately still allowed — Walk and
        /// Hit legitimately come from it; UAL2 was only ever the weapon-stance stopgap.</summary>
        [Test]
        public void NoState_StillUsesTheWeaponStopgaps()
        {
            foreach (var s in States())
            {
                var path = AssetDatabase.GetAssetPath(s.motion);
                Assert.IsFalse(path.Contains("UAL2_Standard"),
                    "state " + s.name + " still plays a weapon-pack stopgap: " + path);
            }
        }

        /// <summary>Asserted on the asset path and clip name rather than on a generic "not a
        /// punch" check: every technique here is a named karate move, so the test can say which
        /// one it expects. Yoko geri is the kick the project kept — spec 2026-07-29 drops mae
        /// geri, so a Kick that plays MaeGeri is a regression, not a near miss.</summary>
        [Test]
        public void Kick_PlaysYokoGeri()
        {
            var motion = State("Kick").motion;
            Assert.IsNotNull(motion, "Kick has no motion");
            Assert.IsTrue(motion.name.Contains("YokoGeri"),
                "Kick plays " + motion.name + " instead of yoko geri");
        }

        [Test]
        public void Blocking_PlaysAgeUke()
        {
            var motion = State("Blocking").motion;
            Assert.IsNotNull(motion, "Blocking has no motion");
            Assert.IsTrue(motion.name.Contains("AgeUke"),
                "Blocking plays " + motion.name + " instead of age uke");
        }
    }
```

- [ ] **Step 2: Прогнать тест и убедиться, что падает**

```bash
unity test --mode EditMode --filter FighterClipsTests --output test-results.xml
```

Ожидание: FAIL, и падает почти всё. `EveryState_HasMotion` валится первым — ссылки на клипы висячие, `motion` равен null у каждого состояния. `Kick_PlaysYokoGeri` и `Blocking_PlaysAgeUke` валятся по той же причине.

- [ ] **Step 3: Написать Editor-скрипт настройки**

Настройки импорта клипов и поле Motion у состояний — обычно работа мышью в инспекторе и окне Animator. Здесь это делается кодом по той же причине, что и в Task 5: настройку нужно уметь повторить, а мокапы и контроллер переживут не одну переразметку.

Создать `Assets/Editor/FighterClipSetup.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Mikey.FightEditor
{
    /// <summary>Wires the fighter's animator states to the project's own karate mocap.
    ///
    /// Done in code rather than by hand because the clips need two import settings that are
    /// easy to lose and invisible when lost: without Bake Into Pose on the root's XZ the
    /// fighter walks away from the position Fighter.cs holds for them, and without Loop Time
    /// the three looping states play once and freeze.
    /// </summary>
    public static class FighterClipSetup
    {
        const string ControllerPath = "Assets/Fight/Fighter.controller";
        const string Mocap = "Assets/Fight/animations/";
        const string Ual1 = "Assets/Characters/Karate/UAL1_Standard.fbx";

        /// <summary>state name -> (model asset, clip name inside it).
        ///
        /// The two UAL1 clips carry an "Armature|" prefix and the mocap clips do not: the mocap
        /// files were renamed at import time, the CC0 pack was left as its exporter wrote it.
        /// These are the names of the actual AnimationClip assets — verified against the
        /// importers' own clip lists — and a mismatch here throws rather than silently skipping.
        /// </summary>
        static readonly (string State, string Model, string Clip)[] Wiring =
        {
            ("Idle",     Mocap + "video_2026-08-06_08-08-32_BoyFBX.fbx", "FightIdle"),
            ("Walk",     Ual1,                                           "Armature|Walk_Loop"),
            ("Punch",    Mocap + "video_2026-08-06_08-08-18_BoyFBX.fbx", "OiZuki"),
            ("PunchB",   Mocap + "video_2026-08-06_08-08-25_BoyFBX.fbx", "Uraken_Swing"),
            ("Kick",     Mocap + "video_2026-08-06_08-08-14_BoyFBX.fbx", "YokoGeri_High"),
            ("Hit",      Ual1,                                           "Armature|Hit_Chest"),
            ("BlockHit", Mocap + "video_2026-08-06_08-08-22_BoyFBX.fbx", "AgeUke"),
            ("Blocking", Mocap + "video_2026-08-06_08-08-22_BoyFBX.fbx", "AgeUke"),
            ("Death",    Mocap + "video_2026-08-06_08-08-28_BoyFBX.fbx", "Knockdown_GetUp"),
        };

        /// <summary>Only these three are states the fighter can sit in; the rest fire once on a
        /// trigger and hand control back.</summary>
        static readonly HashSet<string> Looping = new HashSet<string>
        {
            "FightIdle", "Armature|Walk_Loop", "AgeUke",
        };

        [MenuItem("Mikey/Setup Fighter Clips")]
        public static void Run()
        {
            foreach (var model in Wiring.Select(w => w.Model).Distinct())
                SetUpClips(model);

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                throw new System.IO.FileNotFoundException(ControllerPath + " is not in the project");

            var states = controller.layers
                .SelectMany(l => l.stateMachine.states)
                .Select(c => c.state)
                .ToDictionary(s => s.name);

            foreach (var (stateName, model, clipName) in Wiring)
            {
                if (!states.TryGetValue(stateName, out var state))
                    throw new System.InvalidOperationException(
                        "controller has no state named " + stateName);

                var clip = AssetDatabase.LoadAllAssetsAtPath(model)
                    .OfType<AnimationClip>()
                    .FirstOrDefault(c => c.name == clipName);
                if (clip == null)
                    throw new System.InvalidOperationException(
                        "no clip named " + clipName + " inside " + model);

                state.motion = clip;
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("FighterClipSetup: done");
        }

        /// <summary>clipAnimations is empty until something writes it — until then the importer
        /// serves defaultClipAnimations, which cannot be edited in place. Copying, editing and
        /// assigning back is the supported way to change per-clip settings.</summary>
        static void SetUpClips(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
                throw new System.IO.FileNotFoundException(modelPath + " is not in the project");

            var clips = importer.clipAnimations.Length > 0
                ? importer.clipAnimations
                : importer.defaultClipAnimations;

            var wanted = new HashSet<string>(
                Wiring.Where(w => w.Model == modelPath).Select(w => w.Clip));

            foreach (var clip in clips)
            {
                if (!wanted.Contains(clip.name))
                    continue;
                // Fighter.cs drives position itself; root translation in the clip fights it.
                clip.lockRootPositionXZ = true;
                clip.keepOriginalPositionXZ = true;
                clip.loopTime = Looping.Contains(clip.name);
            }

            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }
    }
}
```

- [ ] **Step 4: Выполнить скрипт headless**

Выполнить `Mikey.FightEditor.FighterClipSetup.Run` в редакторе, не открывая его руками, — так же, как в Task 5, через навык `unity-cli`. Метод `public static` намеренно: из CLI не видно ни `internal`, ни приватных членов.

Ожидание: в логе строка `FighterClipSetup: done` и exit 0.

Скрипт проставляет вот эту раскладку, и она же — источник истины для ревью:

| Состояние | Клип | Файл |
|---|---|---|
| Idle | `FightIdle` | `Assets/Fight/animations/video_2026-08-06_08-08-32_BoyFBX.fbx` |
| Walk | `Armature\|Walk_Loop` | `Assets/Characters/Karate/UAL1_Standard.fbx` |
| Punch | `OiZuki` | `Assets/Fight/animations/video_2026-08-06_08-08-18_BoyFBX.fbx` |
| PunchB | `Uraken_Swing` | `Assets/Fight/animations/video_2026-08-06_08-08-25_BoyFBX.fbx` |
| Kick | `YokoGeri_High` | `Assets/Fight/animations/video_2026-08-06_08-08-14_BoyFBX.fbx` |
| Hit | `Armature\|Hit_Chest` | `Assets/Characters/Karate/UAL1_Standard.fbx` |
| BlockHit | `AgeUke` | `Assets/Fight/animations/video_2026-08-06_08-08-22_BoyFBX.fbx` |
| Blocking | `AgeUke` | `Assets/Fight/animations/video_2026-08-06_08-08-22_BoyFBX.fbx` |
| Death | `Knockdown_GetUp` | `Assets/Fight/animations/video_2026-08-06_08-08-28_BoyFBX.fbx` |

`MaeGeri_High` и `MaeGeri_Mid` не использовать: проект выпиливает маэ гери, спека 2026-07-29 оставляет гибкость только по ёко гери.

Параметры, переходы и их условия не трогать: контракт `Fighter.cs` не меняется.

- [ ] **Step 5: Прогнать тесты и убедиться, что проходят**

```bash
unity test --mode EditMode --filter FighterClipsTests --output test-results.xml
```

Ожидание: PASS, четыре теста. Если какой-то состояние не находится или клип не найден, `Run` бросит исключение с именем — проверь, что имя клипа в `Wiring` совпадает с именем внутри модели, иначе `SetUpClips` его просто не найдёт и молча пропустит.

- [ ] **Step 6: Коммит**

```bash
git add Assets/Editor/FighterClipSetup.cs Assets/Editor/FighterClipSetup.cs.meta Assets/Fight/Fighter.controller Assets/Fight/Tests/FighterModelTests.cs Assets/Fight/animations Assets/Characters/Karate
git commit -m "feat: контроллер бойца пересобран на своих карате-мокапах

Все семь ссылок на клипы были висячими — контроллер не играл ничего.
Kick получил настоящее ёко гери вместо удара рукой, Blocking и BlockHit —
настоящий аге-укэ вместо щитовой стойки и парирования мечом из UAL2.
Оба долга спеки 2026-07-11 закрыты, причём карате настоящим, а не MMA.

Клипы — мокап с видео и несут смещение корня, поэтому импортируются с
Root Transform Position (XZ) = Bake Into Pose: Fighter.cs двигает бойца сам."
```

---

### Task 7: Свап в сцене и проверка на живой игре

**Files:**
- Modify: `Assets/Scenes/FightSandbox.unity`
- Modify: `Assets/Fight/Tests/FighterModelTests.cs` (добавить класс `FightSceneTests`)

**Interfaces:**
- Consumes: всё предыдущее.
- Produces: сцена, в которой оба бойца — `KimonoFighter`.

- [ ] **Step 1: Заменить модели бойцов**

В `Assets/Scenes/FightSandbox.unity` у обоих бойцов заменить меш-иерархию `Ch15_nonPBR` на `KimonoFighter`. Компоненты `Fighter`, `PlayerFighterInput`, `EnemyFighterAI`, `FootIK`, `Animator` и их настройки сохранить как есть.

Материалов у каждого бойца два, потому что мешей два. На меш тела — общий `M_Fighter_Skin` у обоих: из-под ткани видны только голова, шея, кисти и стопы, и они у игрока и врага одинаковые. На меш кимоно — `M_Player_Kimono` игроку и `M_Enemy_Kimono` врагу. Различаются бойцы только этим.

- [ ] **Step 2: Запустить сцену и прочитать диагностику рига**

Запустить `FightSandbox` в редакторе. `FightBootstrap` уже логирует `[diag rig]` — он написан ровно под ловлю сплющенного бойца.

Ожидание в консоли, для обоих бойцов:

```
[diag rig] <имя> humanScale=1.0xx avatar(valid=True, human=True) bodyHeight=1.7x ...
```

`humanScale` заметно меньше или больше единицы, либо `bodyHeight` вне 1.6–1.95 — риг приехал не в том масштабе, возвращаться к Task 4 Step 3 и смотреть `apply_scale_options` в экспорте.

- [ ] **Step 3: Проверить пробойность ткани на ударе ногой**

В запущенной сцене вызвать удар ногой у обоих бойцов. `Kick` — самая нагруженная поза для штанины и подмышки.

Ожидание: тело не торчит сквозь ткань. Если торчит — `Data Transfer` дал артефакт в этой зоне; лечится ручной подкраской весов в двух местах, штанина и подмышка, и повторным прогоном `build_kimono.ps1`.

- [ ] **Step 4: Перепроверить отражение в воде**

`WaterReflection` держит константу scale `0.296` и честно отражает бойцов. Смена модели её задевает.

Ожидание: отражение бойцов в воде на месте и не разъехалось по вертикали относительно самих бойцов. Если разъехалось — подобрать константу заново и записать новое значение с комментарием, откуда оно.

- [ ] **Step 5: Закрепить тестом, что root motion остаётся выключенным**

Это то, на чём держится вся конструкция движения. `Fighter.cs` двигает бойца сам, через `transform.position`, и поэтому клипы могут нести какое угодно смещение корня — аниматор его не применяет. Проверено: у обоих бойцов в сцене `m_ApplyRootMotion: 0`, и ни один скрипт в `Assets/Fight` не трогает `applyRootMotion`, `deltaPosition` или `OnAnimatorMove`. Включи кто-нибудь Apply Root Motion — и бойцы поедут по арене, потому что мокап снят с живого человека и смещение в клипах настоящее, до 0.8 м.

Заметь: проверять это чтением кривых `RootT` у клипов нельзя. Настройка Bake Into Pose не переписывает кривые собственных мокапов проекта ни через типизированный API, ни через `SerializedObject`, ни с `ForceUpdate` — это установлено экспериментально. Единственный надёжный признак живёт в сцене.

Дописать в `Assets/Fight/Tests/FighterModelTests.cs`, внутрь `namespace Mikey.Fight.Tests`:

```csharp
    /// <summary>Fighter.cs owns the fighters' positions and moves them by writing
    /// transform.position directly. That only works while the Animator is not also moving them.
    /// The mocap clips carry real captured translation — up to 0.8 m — so the moment someone
    /// ticks Apply Root Motion, both fighters start sliding around the arena and the arena's
    /// bridge-deck height logic stops lining up with where they actually are.</summary>
    public class FightSceneTests
    {
        const string ScenePath = "Assets/Scenes/FightSandbox.unity";

        [Test]
        public void Fighters_DoNotApplyRootMotion()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                ScenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var animators = scene.GetRootGameObjects()
                    .SelectMany(go => go.GetComponentsInChildren<Animator>(true))
                    .Where(a => a.GetComponent<Fighter>() != null)
                    .ToArray();

                Assert.IsNotEmpty(animators, "no fighters found in " + ScenePath);
                foreach (var animator in animators)
                    Assert.IsFalse(animator.applyRootMotion,
                        animator.name + " applies root motion; Fighter.cs already owns position");
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
```

- [ ] **Step 6: Прогнать весь набор тестов**

```bash
unity test --mode EditMode --output test-results.xml
```

Ожидание: PASS целиком, включая старые `FightRulesTests`.

- [ ] **Step 7: Коммит**

```bash
git add Assets/Scenes/FightSandbox.unity Assets/Fight/Tests/FighterModelTests.cs
git commit -m "feat: бойцы в кимоно в FightSandbox

Ch15 и его материалы пока на месте — сносить их отдельным коммитом,
когда сцена отстоится."
git ls-files Assets/Scenes/FightSandbox.unity
```

---

## Что остаётся после плана

Снос `Ch15_nonPBR.fbx`, его текстур и материалов `M_Player_Ch15_body*` / `M_Enemy_Ch15_body*` — отдельным коммитом после того, как сцена отстоится. Спека прямо требует не удалять их раньше.

Вне рамок целиком: второе отдельное тело для врага, `the-enigmatic-master`, камера-слежение, полоски HP, спецприёмы, замена собственных мокапов для pose-анализа.
