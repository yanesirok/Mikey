# Кимоно-бойцы — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить офисного `Ch15_nonPBR` на бойца в кимоно — тело с Mixamo, кимоно из `kimono.glb`, сшитое в Blender, — и закрыть два анимационных долга: настоящий удар ногой и безоружный блок.

**Architecture:** Готового бесплатного бойца в ги не существует, поэтому тело и кимоно берутся из разных мест и сшиваются headless-скриптом Blender. Скрипт переиспользует из `bridge_kit.py` детерминированный FBX-экспорт и запек high→low: у кимоно 305k трисов и ноль текстур, весь вид держится на складках, поэтому децимация возможна только вместе с запеком нормалей. Unity-сторона не меняет ни одного контракта — тот же Humanoid-аватар, тот же `Fighter.controller`, те же имена состояний.

**Tech Stack:** Blender 5.1 (bpy, Cycles CPU), Unity 6000.3.18f1 (Humanoid retarget, Mecanim), Unity CLI 1.0.0-beta.3, NUnit EditMode, Mixamo (Adobe-аккаунт).

**Spec:** `docs/superpowers/specs/2026-08-15-kimono-fighters-design.md`

## Global Constraints

- Blender: `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`, всегда `--background --factory-startup`.
- Unity: 6000.3.18f1. Тесты — только через Unity CLI: `unity test --mode EditMode`, не сырой `Unity.exe`.
- **Все клипы Mixamo — строго In Place и Without Skin.** Root motion не используется: `Fighter.cs:59` двигает бойца через `transform.position`.
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

### Task 2: Забрать тело и клипы с Mixamo

Работа в браузере под Adobe-аккаунтом пользователя. Персонаж выбирается по лицу и телосложению: из-под кимоно наружу торчат только голова, кисти и стопы, одежда самой модели роли не играет.

**Files:**
- Create: `Assets/Fight/character/mixamo/Fighter_Body.fbx`
- Create: `Assets/Fight/animations/mixamo/*.fbx` — девять клипов
- Create: `Assets/Fight/animations/mixamo/MANIFEST.md`
- Create: `tools/check_mixamo_assets.py`

**Interfaces:**
- Consumes: ничего.
- Produces: `Assets/Fight/character/mixamo/Fighter_Body.fbx` — Humanoid-тело в T-позе со скелетом `mixamorig:*`; девять клипов с именами файлов ровно `Idle.fbx`, `Walk.fbx`, `Punch.fbx`, `PunchB.fbx`, `Kick.fbx`, `Hit.fbx`, `BlockHit.fbx`, `Blocking.fbx`, `Death.fbx` — имена файлов равны именам состояний `Fighter.controller`, Task 6 опирается на это соответствие.

- [ ] **Step 1: Написать падающую проверку**

Создать `tools/check_mixamo_assets.py`:

```python
"""Проверка комплектности ассетов Mixamo.

Клипы качаются Without Skin, поэтому меша в них быть не должно: FBX без
скина не содержит объектов Geometry. Тело, наоборот, обязано его содержать.
In Place здесь не проверяется — это делает C#-тест RootMotion_StaysInPlace,
которому доступны разобранные Unity кривые RootT.
"""
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BODY = os.path.join(ROOT, 'Assets/Fight/character/mixamo/Fighter_Body.fbx')
CLIPS = os.path.join(ROOT, 'Assets/Fight/animations/mixamo')
STATES = ['Idle', 'Walk', 'Punch', 'PunchB', 'Kick', 'Hit', 'BlockHit',
          'Blocking', 'Death']

fails = []


def has_geometry(path):
    with open(path, 'rb') as f:
        return b'Geometry' in f.read()


if not os.path.isfile(BODY):
    fails.append(f'нет тела: {BODY}')
elif not has_geometry(BODY):
    fails.append('тело скачано без скина — нужен меш')

for state in STATES:
    p = os.path.join(CLIPS, state + '.fbx')
    if not os.path.isfile(p):
        fails.append(f'нет клипа: {state}.fbx')
    elif has_geometry(p):
        fails.append(f'{state}.fbx скачан со скином — нужен Without Skin')

manifest = os.path.join(CLIPS, 'MANIFEST.md')
if not os.path.isfile(manifest):
    fails.append('нет MANIFEST.md')
else:
    text = open(manifest, encoding='utf-8').read()
    for state in STATES:
        if state not in text:
            fails.append(f'MANIFEST.md не описывает {state}')

if fails:
    print('\n'.join('FAIL ' + f for f in fails))
    sys.exit(1)
print('MIXAMO_OK')
```

- [ ] **Step 2: Прогнать и убедиться, что падает**

```bash
python tools/check_mixamo_assets.py
```

Ожидание: FAIL, десять строк — нет тела, нет девяти клипов, нет манифеста.

- [ ] **Step 3: Выбрать персонажа**

Открыть `https://www.mixamo.com/#/?type=Character` в браузере. Отобрать 3–5 кандидатов атлетичного сложения и **показать их пользователю до скачивания** — выбор лица за ним, не за исполнителем.

Скачать выбранного: Format `FBX Binary (.fbx)`, Pose `T-pose`. Положить в `Assets/Fight/character/mixamo/Fighter_Body.fbx`.

- [ ] **Step 4: Скачать девять клипов**

Для каждого — вкладка Animations, поиск по кандидатам из таблицы, экспорт с настройками: Format `FBX Binary (.fbx)`, Skin **Without Skin**, FPS `30`, Keyframe Reduction `none`, галка **In Place** включена везде, где она есть.

| Файл | Что ищем | Кандидаты в поиске |
|---|---|---|
| `Idle.fbx` | боевая стойка, петля | Fighting Idle, Boxing Idle |
| `Walk.fbx` | шаг в стойке, петля | Fighting Walk, Strafe |
| `Punch.fbx` | прямой рукой | Cross Punch, Jab |
| `PunchB.fbx` | второй удар рукой, визуально отличимый | Hook Punch, Uppercut |
| `Kick.fbx` | удар ногой | Mma Kick, Roundhouse Kick, Side Kick |
| `Hit.fbx` | реакция на пропущенный удар | Head Hit, Hit Reaction |
| `BlockHit.fbx` | приём удара в блок | Center Block, Body Block |
| `Blocking.fbx` | безоружная защитная стойка, петля | Blocking, Guard Idle |
| `Death.fbx` | падение назад | Falling Back Death, Dying |

Если у клипа нет галки In Place — брать другой клип из кандидатов, а не «поправим потом»: Task 6 такой клип завалит.

- [ ] **Step 5: Записать MANIFEST.md**

Создать `Assets/Fight/animations/mixamo/MANIFEST.md`. Mixamo в maintenance-режиме и Adobe его не развивает — без этого файла через месяц нельзя понять, что откуда взялось.

```markdown
# Ассеты Mixamo

Скачано: 2026-08-15. Настройки экспорта одинаковы для всех клипов:
FBX Binary, Without Skin, 30 fps, Keyframe Reduction none, In Place.

Тело: FBX Binary, T-pose, со скином.

| Файл | Имя на Mixamo |
|---|---|
| `character/mixamo/Fighter_Body.fbx` | <имя персонажа> |
| `Idle.fbx` | <имя клипа> |
| `Walk.fbx` | <имя клипа> |
| `Punch.fbx` | <имя клипа> |
| `PunchB.fbx` | <имя клипа> |
| `Kick.fbx` | <имя клипа> |
| `Hit.fbx` | <имя клипа> |
| `BlockHit.fbx` | <имя клипа> |
| `Blocking.fbx` | <имя клипа> |
| `Death.fbx` | <имя клипа> |

Удары Mixamo — MMA и кикбоксинг, не шотокан. Хрестоматийного yoko geri
здесь нет; для pose-анализа источником остаются собственные мокапы
`Assets/Fight/animations/video_*_BoyFBX.fbx`.
```

Угловые скобки заменить настоящими именами — файл без них бесполезен.

- [ ] **Step 6: Прогнать проверку и убедиться, что проходит**

```bash
python tools/check_mixamo_assets.py
```

Ожидание: PASS, в выводе `MIXAMO_OK`.

- [ ] **Step 7: Коммит и сверка, что бинарники реально легли**

```bash
git add Assets/Fight/character/mixamo Assets/Fight/animations/mixamo tools/check_mixamo_assets.py
git commit -m "assets: тело и девять клипов с Mixamo, In Place без скина

Kick и Blocking закрывают долги спеки 2026-07-11: удар ногой вместо
Punch_Cross и безоружный блок вместо щитовой стойки UAL2."
git ls-files Assets/Fight/character/mixamo Assets/Fight/animations/mixamo
```

Ожидание: `git ls-files` перечисляет тело, девять клипов и манифест. Если список пуст или неполон — бинарники не легли, и это надо чинить сейчас: в этом репозитории арт регулярно остаётся вне git, и `git status` этого не показывает.

---

### Task 3: `kimono_fit.py` — подгонка и low-poly с запечёнными картами

Первая половина скрипта: поставить кимоно на тело, сделать из 305k низкополигональную версию и перенести на неё складки запеком. Скиннинга здесь ещё нет — он в Task 4.

**Files:**
- Create: `tools/Blender/kimono_fit.py`
- Create: `tools/Blender/build_kimono.ps1`

**Interfaces:**
- Consumes: `bridge_kit` из Task 1 — `_install_deterministic_fbx_uuids()`, `bake_pair`, `fill`, `save_png`, `tri_count`, `apply_mods`, `reset_scene`, `ATLAS`; тело `Fighter_Body.fbx` из Task 2.
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
    --body (Join-Path $root "Assets\Fight\character\mixamo\Fighter_Body.fbx") `
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

В `tools/Blender/kimono_fit.py` добавить перед `main()`:

```python
# Кости, чью геометрию закрывает ткань, и кости, которые остаются наружу.
# KEEP проверяется первым: mixamorig:ForeArm содержит и Arm, и — по смыслу —
# запястье, но кисть обязана уцелеть.
COVERED = ('Spine', 'Chest', 'Arm', 'Shoulder', 'UpLeg', 'Leg')
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

### Task 5: Импорт бойца в Unity как Humanoid

**Files:**
- Create: `Assets/Fight/Tests/FighterModelTests.cs`
- Create: `Assets/Fight/character/M_Player_Kimono.mat`, `Assets/Fight/character/M_Enemy_Kimono.mat`
- Modify: `Assets/Fight/character/KimonoFighter.fbx.meta` (через инспектор импорта)

**Interfaces:**
- Consumes: `Assets/Fight/character/KimonoFighter.fbx` из Task 4.
- Produces: константа пути `FighterModelTests.ModelPath = "Assets/Fight/character/KimonoFighter.fbx"`; два материала на `Character.shader` — светлое ги игрока и тёмное ги врага.

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
    }
}
```

- [ ] **Step 2: Прогнать тест и убедиться, что падает**

```bash
unity test --mode EditMode --filter FighterModelTests --output test-results.xml
```

Ожидание: FAIL — `Model_ImportsAsHumanoid` валится на `is not in the project` либо на `animationType`, потому что Unity по умолчанию импортирует FBX как Generic.

- [ ] **Step 3: Выставить импорт**

Выделить `Assets/Fight/character/KimonoFighter.fbx` в Project. Вкладка Rig: `Animation Type` → **Humanoid**, `Avatar Definition` → **Create From This Model**. Apply.

- [ ] **Step 4: Прогнать тест и убедиться, что проходит**

```bash
unity test --mode EditMode --filter FighterModelTests --output test-results.xml
```

Ожидание: PASS, три теста.

- [ ] **Step 5: Сделать материалы**

Создать два материала на существующем `Assets/Fight/character/Character.shader`. Цвета взяты от тех, что сцена уже использует для тонировки тел (игрок — индиго, враг — тёмно-багровый, спека 2026-07-11), но сдвинуты в сторону ткани: белое ги читается на закатной арене, а пояс несёт опознавательный цвет.

- `M_Player_Kimono.mat` — ги `#E8E4DA`, пояс `#3B4A9E` (индиго игрока);
- `M_Enemy_Kimono.mat` — ги `#2A2A30`, пояс `#7A1F28` (багровый врага).

В обоих подключить `T_Kimono_Normal.png` как normal map и `T_Kimono_AO.png` как AO. Базовый цвет плоский: в `kimono.glb` текстур нет вовсе, весь рельеф идёт из запечённой нормали.

`T_Kimono_Normal.png` в импортере должен стоять `Texture Type` → **Normal map**, иначе рельеф будет читаться как цветной шум.

- [ ] **Step 6: Коммит**

```bash
git add Assets/Fight/Tests/FighterModelTests.cs Assets/Fight/character
git commit -m "feat: боец импортируется как Humanoid, материалы ги игрока и врага

Тесты закрывают ровно тот отказ, который проект уже ловил: аватар,
переживший round trip через Blender, но приехавший сплющенным."
```

---

### Task 6: Перевесить `Fighter.controller` на клипы Mixamo

**Files:**
- Modify: `Assets/Fight/Fighter.controller`
- Modify: `Assets/Fight/Tests/FighterModelTests.cs` (добавить класс `FighterClipsTests`)

**Interfaces:**
- Consumes: девять клипов из Task 2, имена файлов которых равны именам состояний.
- Produces: `Fighter.controller`, у которого ни одно состояние не ссылается на UAL1/UAL2.

- [ ] **Step 1: Написать падающий тест**

Дописать в `Assets/Fight/Tests/FighterModelTests.cs` второй класс — **внутрь `namespace Mikey.Fight.Tests`**, то есть после закрывающей скобки класса `FighterModelTests` и перед закрывающей скобкой namespace. Блок `using` в шапке файла уже даёт всё нужное: `System.Linq`, `UnityEditor` (там же `AnimationUtility` и `AssetDatabase`), `UnityEngine`.

```csharp
    /// <summary>The controller is the contract between Fighter.cs and the art. A state with a
    /// null motion plays the bind pose and looks like a frozen fighter, not like an error — so
    /// it has to fail here rather than in someone's play session.</summary>
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

        [Test]
        public void EveryState_HasMotion()
        {
            foreach (var s in States())
                Assert.IsNotNull(s.motion, "state " + s.name + " has no motion");
        }

        /// <summary>Fighter.cs drives position itself, so a clip that carries root motion walks
        /// the fighter out of the spot the code put them in. Mixamo's In Place export is the fix;
        /// this asserts the export setting was actually used.</summary>
        [Test]
        public void EveryClip_StaysInPlace()
        {
            foreach (var s in States())
            {
                var clip = s.motion as AnimationClip;
                if (clip == null)
                    continue;
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (binding.propertyName != "RootT.x" && binding.propertyName != "RootT.z")
                        continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    float min = float.MaxValue, max = float.MinValue;
                    foreach (var key in curve.keys)
                    {
                        min = Mathf.Min(min, key.value);
                        max = Mathf.Max(max, key.value);
                    }
                    Assert.Less(max - min, 0.15f,
                        clip.name + " drifts " + (max - min) + " m on " + binding.propertyName);
                }
            }
        }

        /// <summary>Blocking used to borrow UAL2's shield stance because the CC0 pack had no
        /// unarmed block, and Kick used to play a punch. Both are paid off by the Mixamo clips;
        /// this keeps them paid.</summary>
        [Test]
        public void NoState_StillUsesTheCC0Stopgaps()
        {
            foreach (var s in States())
            {
                var path = AssetDatabase.GetAssetPath(s.motion);
                Assert.IsFalse(path.Contains("UAL1_Standard") || path.Contains("UAL2_Standard"),
                    "state " + s.name + " still plays a CC0 stopgap: " + path);
                if (s.name == "Kick")
                    Assert.IsFalse(s.motion.name.Contains("Punch"),
                        "Kick still plays a punch");
            }
        }
    }
```

- [ ] **Step 2: Прогнать тест и убедиться, что падает**

```bash
unity test --mode EditMode --filter FighterClipsTests --output test-results.xml
```

Ожидание: FAIL на `NoState_StillUsesTheCC0Stopgaps` — `Blocking` играет `Idle_Shield_Loop` из UAL2, `Kick` играет `Punch_Cross`.

- [ ] **Step 3: Перевесить состояния**

Открыть `Assets/Fight/Fighter.controller` в окне Animator. Для каждого из девяти состояний в поле Motion выставить одноимённый клип из `Assets/Fight/animations/mixamo/`: Idle → `Idle.fbx`, Walk → `Walk.fbx`, и так далее по всем девяти.

Параметры, переходы и их условия не трогать: контракт `Fighter.cs` не меняется.

- [ ] **Step 4: Прогнать тесты и убедиться, что проходят**

```bash
unity test --mode EditMode --filter FighterClipsTests --output test-results.xml
```

Ожидание: PASS, три теста. Если валится `EveryClip_StaysInPlace` — соответствующий клип скачан без галки In Place, вернуться к Task 2 Step 4 и перекачать именно его.

- [ ] **Step 5: Коммит**

```bash
git add Assets/Fight/Fighter.controller Assets/Fight/Tests/FighterModelTests.cs
git commit -m "feat: контроллер бойца на клипах Mixamo

Kick перестал быть Punch_Cross, Blocking перестал быть щитовой стойкой
UAL2 — оба долга спеки 2026-07-11 закрыты. Тест StaysInPlace держит
экспортную настройку In Place: Fighter.cs двигает бойца сам."
```

---

### Task 7: Свап в сцене и проверка на живой игре

**Files:**
- Modify: `Assets/Scenes/FightSandbox.unity`

**Interfaces:**
- Consumes: всё предыдущее.
- Produces: сцена, в которой оба бойца — `KimonoFighter`.

- [ ] **Step 1: Заменить модели бойцов**

В `Assets/Scenes/FightSandbox.unity` у обоих бойцов заменить меш-иерархию `Ch15_nonPBR` на `KimonoFighter`. Компоненты `Fighter`, `PlayerFighterInput`, `EnemyFighterAI`, `FootIK`, `Animator` и их настройки сохранить как есть. Материалы: игроку `M_Player_Kimono`, врагу `M_Enemy_Kimono`.

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

- [ ] **Step 5: Прогнать весь набор тестов**

```bash
unity test --mode EditMode --output test-results.xml
```

Ожидание: PASS целиком, включая старые `FightRulesTests`.

- [ ] **Step 6: Коммит**

```bash
git add Assets/Scenes/FightSandbox.unity
git commit -m "feat: бойцы в кимоно в FightSandbox

Ch15 и его материалы пока на месте — сносить их отдельным коммитом,
когда сцена отстоится."
git ls-files Assets/Scenes/FightSandbox.unity
```

---

## Что остаётся после плана

Снос `Ch15_nonPBR.fbx`, его текстур и материалов `M_Player_Ch15_body*` / `M_Enemy_Ch15_body*` — отдельным коммитом после того, как сцена отстоится. Спека прямо требует не удалять их раньше.

Вне рамок целиком: второе отдельное тело для врага, `the-enigmatic-master`, камера-слежение, полоски HP, спецприёмы, замена собственных мокапов для pose-анализа.
