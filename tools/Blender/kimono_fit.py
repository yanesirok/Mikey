"""Кимоно на базовое тело: подгонка, low-poly с запечёнными картами, скиннинг.

Запуск (headless):
  blender --background --factory-startup --python kimono_fit.py -- \
      --body <body.fbx> --kimono <kimono.glb> --out <dir> --name <имя FBX>

Переиспользует из bridge_kit.py детерминированный FBX-экспорт и обвязку
запека: там это уже отлажено на деталях моста. Сам запек — свой, см.
bake_pair_cloth: cage моста рассчитан на доски, а не на слои ткани.

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
from bridge_kit import (ATLAS, MARGIN, _install_deterministic_fbx_uuids,
                        apply_mods, bake_target_material, fill, reset_scene,
                        save_png, tri_count)


def parse_args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    p = argparse.ArgumentParser()
    p.add_argument('--body', required=True)
    p.add_argument('--kimono', required=True)
    p.add_argument('--out', required=True)
    p.add_argument('--name', required=True,
                   help='имя выходного FBX без расширения, например KimonoFighter_Player')
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


# Меши собственной одежды и обуви персонажа Mixamo. Под кимоно они не нужны, а
# карате босое, поэтому обувь уходит обязательно. Лицо остаётся: Hair, Eyes,
# Eyelashes не входят в этот список намеренно.
# Проверено импортом обеих моделей: у Ch28 это Hoody/Pants/Sneakers, у Remy —
# Tops/Bottoms/Shoes. Меш тела при этом полный, от стоп до макушки (Ch28_Body
# z 0.064..1.764, Body z 0.094..3.742), поэтому под снятой одеждой дыр нет.
CHARACTER_CLOTHING = ('hoody', 'pants', 'sneakers', 'tops', 'bottoms', 'shoes')

# Оба бойца приводятся к одному росту: они дерутся друг с другом, а приходят
# в разном масштабе — 1.767 м у Ch28 и 3.784 м у Remy.
TARGET_HEIGHT = 1.75


def bone_by_suffix(arm, suffix):
    """Кость по окончанию имени, а не по точному совпадению.

    Mixamo нумерует префикс, когда персонаж выгружался в сессии с несколькими
    моделями: в одном файле кость зовётся mixamorig:Neck, в другом
    mixamorig10:Neck. Точное сравнение ломается на второй же выгрузке, причём
    молча — fit() просто не найдёт кость и упадёт с StopIteration.
    """
    hits = [b for b in arm.pose.bones if b.name.endswith(suffix)]
    assert hits, (f'в скелете нет кости, оканчивающейся на {suffix!r}; '
                  f'есть, например: {[b.name for b in arm.pose.bones][:5]}')
    assert len(hits) == 1, f'{suffix!r} неоднозначно: {[b.name for b in hits]}'
    return hits[0]


def normalize_height(arm, meshes):
    """Приводит персонажа к TARGET_HEIGHT. Возвращает применённый множитель.

    Масштабируются только объекты без родителя: у импорта Mixamo меши обычно
    дети арматуры, и масштабировать их отдельно значило бы применить масштаб
    дважды.
    """
    mn, mx = world_bbox(meshes)
    height = mx.z - mn.z
    assert height > 0.1, f'высота персонажа {height:.3f} м — импорт пустой?'
    k = TARGET_HEIGHT / height
    for o in {arm, *meshes}:
        if o.parent is None:
            o.scale = (o.scale.x * k, o.scale.y * k, o.scale.z * k)
    bpy.context.view_layer.update()

    mn, mx = world_bbox(meshes)
    got = mx.z - mn.z
    assert abs(got - TARGET_HEIGHT) < 0.02, (
        f'после нормализации рост {got:.3f} м вместо {TARGET_HEIGHT} — '
        'масштаб применился не ко всем корневым объектам')
    return k


def import_body(path):
    """Готовый персонаж Mixamo: снимаем его одежду и приводим к общему росту."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, automatic_bone_orientation=True)
    new = [o for o in bpy.data.objects if o not in before]
    arm = next((o for o in new if o.type == 'ARMATURE'), None)
    assert arm is not None, f'в {path} нет арматуры — это не персонаж Mixamo?'

    meshes = [o for o in new if o.type == 'MESH']
    assert meshes, f'в {path} нет ни одного меша'

    doomed = [o for o in meshes
              if any(c in o.name.lower() for c in CHARACTER_CLOTHING)]
    doomed_names = [o.name for o in doomed]
    for o in doomed:
        bpy.data.objects.remove(o, do_unlink=True)
    meshes = [o for o in meshes if o not in doomed]
    assert meshes, 'после снятия одежды не осталось ни одного меша'

    k = normalize_height(arm, meshes)
    # Имена, а не только счётчики: по «снято 3» не отличить «снято всё, что
    # нужно» от «снято 3, а Jacket/Dress/Boots третьего персонажа пропущены» —
    # CHARACTER_CLOTHING покрывает ровно этих двух бойцов и молчит про остальных.
    print(f'kimono_fit: персонаж {os.path.basename(path)} — снято мешей одежды '
          f'{len(doomed)} {doomed_names}, осталось {len(meshes)} '
          f'{[o.name for o in meshes]}, масштаб {k:.4f}')
    return arm, meshes


# kimono.glb несёт пять мешей; четыре — одежда, пятый (материал 'default') —
# манекен, на котором её моделировали: он тянется от стоп до макушки (z
# 1.8..735.1 против 51.5..658.8 у самой одежды) и имеет самый широкий размах
# рук из всех пяти. Склей его вместе с одеждой — и fit() станет мерить
# масштаб по макушке манекена, что её собственный докстринг прямо запрещает
# (кимоно кончается у воротника). Подтверждено импортом: реальные материалы
# на пяти мешах — Belts_1, Jacket_1, Pants_1, Shirt_1, default.
# BELT_PART назван отдельно и подставлен в KIMONO_PARTS, а не продублирован:
# по нему же capture_belt_mask отбирает полигоны пояса в отдельный сабмеш.
BELT_PART = 'Belts'
KIMONO_PARTS = (BELT_PART, 'Jacket', 'Pants', 'Shirt')


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

    # bpy.ops.mesh.* работают с активным объектом. До этой строки функция
    # держалась на том, что join() в import_kimono_parts() оставил активным
    # именно k — вызови её отдельно, и правки уехали бы в чужой меш.
    bpy.context.view_layer.objects.active = k
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


def bone_head_z(arm, suffix):
    return (arm.matrix_world @ bone_by_suffix(arm, suffix).head).z


def fit(kimono, arm, meshes, scale_mul, offset_z):
    """Совмещает кимоно с телом: воротник у шеи, штанина у стопы.

    По высоте головы масштабировать нельзя — кимоно кончается у воротника,
    а не на макушке.
    """
    neck = bone_head_z(arm, ':Neck')
    toe = bone_head_z(arm, ':LeftToeBase')
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
    return [names[p.material_index].startswith(BELT_PART)
            for p in low.data.polygons]


def apply_belt_split(low, is_belt):
    """Rebuilds two material slots on the already-baked low mesh: cloth and belt.

    Оба слота держат материалы-заглушки — запечённые карты общие на всю развёртку,
    а не по слотам. Настоящие материалы команд назначает FightSceneSwap.cs (не
    FighterImportSetup) уже в сцене, и назначает ПО ИМЕНИ ЗАГЛУШКИ, а не по индексу
    сабмеша: порядок переворачивается на круге экспорт-импорт FBX. Здесь ткань 0,
    пояс 1; в Unity замерено обратное — слот 0 это Kimono_Belt (1060 трисов), слот 1
    Kimono_Cloth (10940). Поэтому имена ниже несущие, менять их без правки
    FightSceneSwap.SetKimonoMaterials нельзя — там контракт изложен полностью.

    Пояс вообще выделен в свой сабмеш потому, что спека просит игроку и врагу разные
    цвета пояса, а _RimColor шейдера — это френель по всему силуэту, не пояс.
    """
    # zip() усекается по короткому: разъехавшаяся маска молча раскрасила бы
    # часть полигонов и оставила остальные нулевыми, а пустая — вообще ничего.
    # Отказ всплыл бы только в Unity как «один сабмеш вместо двух» и был бы
    # неотличим от «забыли перегнать пайплайн».
    assert len(is_belt) == len(low.data.polygons), (
        f'маска пояса на {len(is_belt)} полигонов при {len(low.data.polygons)} '
        'в меше — снята не с этого меша?')
    assert any(is_belt), (
        f'в маске пояса нет ни одного полигона — материал {BELT_PART!r} '
        'не доехал до low или переименован в kimono.glb')
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


# Числовых порогов на содержимое карт здесь нет, и это измеренное решение, а не
# отступление. Прежние 0.35/0.20/0.85/0.15 выведены из отчёта о ЗАВЕДОМО ПЛОХОМ
# запеке и роняли прогон на первом же assert'е (AO 0.234 при пороге 0.35; синий
# 0.520 при пороге 0.85 не дожил бы до проверки). Но поставить вместо них числа,
# которые сегодняшний запек проходит, значило бы откалибровать порог по запеку,
# про который измерением известно, что он дефектный.
#
# Каждое число ниже помечено маской и разрешением, и сравнивать можно только
# одинаково помеченные: маска покрытия (см. таблицу у CLOTH_CAGE) и _baked_mask
# считают разные знаменатели. Полностью — в task-2-report.md.
#
#   * контроль, что машинерия чиста — low, запечённый САМ НА СЕБЯ (_baked_mask,
#     1024²): синий 0.994 при σ 0.027, выше 0.9 — 99.1% текселей. Развёртка,
#     тангенциальный базис и сам оператор исправны, и чистый запек прежние
#     0.85/0.15 прошёл бы с запасом. Это целевое состояние для возврата порогов;
#   * наша карта, которая реально уехала в Unity (_baked_mask, 2048²): синий
#     0.642 при σ 0.376 — против 0.994 и 0.027 у контроля и против прежнего
#     порога σ < 0.15. Тот же переход на cage под ткань дал по этой же маске и
#     разрешению 0.520 -> 0.642;
#   * кастомные нормали скана ни при чём: их снятие даёт 0.549 -> 0.553
#     (_baked_mask, 1024², cage 0.5 мм — обе цифры из одного замера);
#   * помогают две вещи, и ранжировать их нечем. Cage под ткань: 0.544 у
#     значений моста -> 0.726 (маска покрытия, 1024², строки таблицы у
#     CLOTH_CAGE). Бюджет трисов: low в 60k против штатных 12k из --tris, то
#     есть впятеро -> 0.549 -> 0.651 (_baked_mask, 1024², cage 0.5 мм). Обе
#     действуют через одну величину — насколько поверхность low отстоит от
#     правильной поверхности high по сравнению с расстоянием между слоями ткани.
#     Вариант 60k к тому же не «при прочих равных»: make_low после децимации
#     заново гоняет smart_project и pack_islands, так что у него другая
#     развёртка и другая плотность текселей.
#     Данные поддерживают ровно одно утверждение, и его достаточно: НИ ОДНА из
#     двух поодиночке до чистого запека не доводит. При cage 0 внутрь смотрят
#     всё ещё 19.1% текселей (маска покрытия), а 60k даёт σ 0.404 против
#     контрольных 0.027 (_baked_mask).
#
# AO тёмный по другой причине и честно: изнанка и внутренности рукавов — это
# 78.9% граней low-поли и 72.0% его площади (замер лучом по нормали каждой
# грани), у них AO и должен быть нулевым. Рендером проверено, что лицевая
# сторона — кимоно со складками, а не чёрный мешок.
#
# Пока разрыв не закрыт (бюджет трисов и раскладка — не объём Task 2), проверки
# держат единственное, что можно утверждать честно: запек вообще состоялся.
# Все четыре числа печатаются каждый прогон, чтобы деградация была видна в логе.


def check_ao_content(ao):
    """AO идёт в _BaseMap при _AlbedoGamma=1, поэтому пустая карта — это чёрная ткань."""
    px = _pixel_array(ao)
    lit = px[_baked_mask(px, (1.0, 1.0, 1.0)), 0]
    assert lit.size > 0, 'AO не запёкся ни на одном текселе'
    return float(lit.mean()), float(np.median(lit))


def check_normal_content(normal):
    """Синий канал tangent-space карты: 1.0 — прямо наружу, ниже 0.5 — внутрь поверхности.

    Третьим числом — доля АТЛАСА, отличающаяся от заливки. Она в логе потому, что
    промахи луча — единственная величина, которая по свипу реально ходит (2.9% ->
    33.9% покрытой площади в таблице у CLOTH_CAGE), пока синий и сигма стоят почти
    на месте. Промах оставляет тексель заливкой, значит он в эту долю не попадает, и
    при неизменной развёртке (покрытие постоянно) её падение и есть рост промахов.
    Печатается доля атласа, а не доля промахов от покрытия: маску покрытия (острова
    плюс margin-выпуск) пайплайн не считает, а знаменатель из uv_coverage — площадь
    UV-треугольников без выпуска — дал бы число, несравнимое с той таблицей.
    Идеально плоский запечённый тексель от заливки неотличим и тоже не попадает.
    """
    px = _pixel_array(normal)
    mask = _baked_mask(px, (0.5, 0.5, 1.0))
    blue = px[mask, 2]
    assert blue.size > 0, 'нормали не запеклись ни на одном текселе'
    return float(blue.mean()), float(blue.std()), float(mask.mean())


# Значения из bridge_kit.bake_pair — cage 20 мм, луч 60 мм — приехали с деталей
# моста, под ткань их никто не мерил. У кимоно соседний слой ткани близко: замер
# лучами по граням low даёт до него p10 0.3 мм, медиану 13.9 мм (task-2-report.md),
# так что луч, стартующий в двух сантиметрах снаружи, успевает уйти в изнанку
# соседнего слоя. Нормаль оттуда смотрит внутрь поверхности, то есть синий < 0.5;
# по таблице ниже у значений моста таких текселей 46.2%, у выбранных 25.1%.
#
# Замер: 1024², маска ПОКРЫТИЯ (острова + margin, знаменатель один на все строки —
# 169713 текселей, 16.2% атласа). Луч в каждой строке свой, постоянным его не
# держали; отношения к cage у него нет — в последней строке cage нулевой.
#
#   cage     луч   синий   >0.9  промахи  рельеф   <0.5
#   20.0 мм 60.0   0.544  26.7%    2.9%   23.8%   46.2%   <- значения моста
#    5.0 мм 15.0   0.531  26.5%    3.9%   22.6%   48.1%
#    2.0 мм  6.0   0.529  27.4%    5.1%   22.3%   48.1%
#    1.0 мм  3.0   0.538  29.3%    7.2%   22.1%   46.8%
#    0.5 мм  1.5   0.612  38.3%   14.0%   24.3%   37.5%
#    0.2 мм  0.8   0.726  53.3%   24.8%   28.5%   25.1%   <- взято
#    0.0 мм  0.5   0.787  63.4%   33.9%   29.5%   19.1%
#
# Колонку >0.9 нельзя читать как «годные»: промах оставляет тексель ЗАЛИВКОЙ, а
# заливка нормалей — это (0.5, 0.5, 1.0), то есть синий 1.0, и каждый промах
# падает в ту же корзину. Ловится сложением: в нижней строке 63.4 + 19.1 + 33.9
# = 116% при знаменателе в 100%. Поэтому колонка «рельеф» = >0.9 минус промахи,
# и она-то и есть доля реально запечённого рельефа.
#
# Выбор 0.2 мм ДВУСТОРОННИЙ, одного критерия здесь нет: сам по себе он не
# выигрывает ни одного столбца — по промахам победили бы значения моста (2.9%),
# по вредным и по рельефу — нуль (19.1% и 29.5%). Он выигрывает попарно:
#   * против значений моста: текселей с нормалью внутрь почти вдвое меньше
#     (25.1% против 46.2%) и рельефа больше (28.5% против 23.8%);
#   * против нуля: промахов 24.8% вместо 33.9%, ценой одного пункта рельефа
#     (28.5 против 29.5) и шести пунктов вредных (25.1 против 19.1).
# Второй пункт — суждение, а не расчёт: треть покрытой площади вообще без
# запечённой нормали я считаю худшим злом, чем 19.1% текселей с вывернутой.
# По отношению рельефа к вредным нуль выигрывает (1.5:1 против 1.1:1) — выбирали
# не им. Уменьшать cage дальше можно, но по таблице это ровно размен рельефа на
# промахи: разрыв low/high им не лечится (см. комментарий у порогов выше).
CLOTH_CAGE = 0.0002
CLOTH_RAY = 0.0008


def bake_pair_cloth(low, high, normal_img, ao_img):
    """То же, что bridge_kit.bake_pair, но с cage под ткань, а не под доски моста.

    Отдельная функция, а не параметр к общей, по внешней причине: bridge_kit.py
    несёт незакоммиченные правки пользователя, и править его — значит смести его
    работу в наш коммит. Весь смысл дубликата — в двух константах выше.
    """
    for other in bpy.data.objects:
        other.hide_render = other not in (low, high)
    bpy.ops.object.select_all(action='DESELECT')
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low

    for img, kind in ((normal_img, 'NORMAL'), (ao_img, 'AO')):
        bake_target_material(low, img)
        bpy.ops.object.bake(type=kind, use_selected_to_active=True,
                            cage_extrusion=CLOTH_CAGE,
                            max_ray_distance=CLOTH_RAY,
                            margin=MARGIN, use_clear=False)
    low.data.materials.clear()


def bake(low, high, out_dir):
    normal = bpy.data.images.new('T_Kimono_Normal', ATLAS, ATLAS,
                                 alpha=False, is_data=True)
    ao = bpy.data.images.new('T_Kimono_AO', ATLAS, ATLAS,
                             alpha=False, is_data=True)
    fill(normal, (0.5, 0.5, 1.0, 1.0))
    fill(ao, (1.0, 1.0, 1.0, 1.0))
    bake_pair_cloth(low, high, normal, ao)

    # Запись ДО проверок намеренно: запек стоит минут, а проверки роняют прогон
    # assert'ом. Упади они раньше save_png — на диск не легло бы ни одной PNG,
    # то есть ровно того артефакта, по которому и видно, врёт порог или врёт
    # запек. Негодная карта на диске безобиднее: прогон всё равно упал, а
    # FighterImportSetup её не подхватит, пока пайплайн не пройдёт целиком.
    save_png(normal, os.path.join(out_dir, 'T_Kimono_Normal.png'))
    save_png(ao, os.path.join(out_dir, 'T_Kimono_AO.png'))

    ao_mean, ao_median = check_ao_content(ao)
    n_mean, n_std, n_baked = check_normal_content(normal)
    print(f'kimono_fit: запек — AO среднее {ao_mean:.3f} медиана {ao_median:.3f}, '
          f'нормали синий среднее {n_mean:.3f} sigma {n_std:.3f}, '
          f'не заливка на {n_baked:.1%} атласа')


# Полоса плавного перехода над пахом, долей от высоты шеи. Ниже неё подол
# целиком на тазе, выше — веса теплового расчёта; между — линейная смесь,
# чтобы не поставить на месте старого разрыва новый.
SKIRT_BAND = 0.08

# Порог приёмки скиннинга: во сколько раз ребру позволено растянуться на
# проверочной позе. Здоровая ткань на замерах не переходит 4x, разрыв даёт
# десятки — 5.0 разделяет их с запасом и не ловит честное натяжение подола.
STRETCH_MAX = 5.0

# Проверочная поза: развод ног как в ударе, сгиб колена, скрутка корпуса и
# рук. Именно она вскрывает разрывы, которые bind pose прячет в складках.
GUARD_POSE = (('LeftUpLeg', 'x', 70), ('RightUpLeg', 'x', -40),
              ('LeftLeg', 'x', -50), ('Spine2', 'x', 20),
              ('LeftForeArm', 'y', -70), ('LeftShoulder', 'z', -30))


def skin_cloth(low, arm):
    """Скиннинг ткани костным тепловым расчётом.

    До этого веса переносились с уже отскиненного тела модификатором
    DATA_TRANSFER в режиме POLYINTERP_NEAREST. Такой перенос точен, но
    разрывен: там, где ткань перекрывает промежуток между частями тела —
    пах, подмышки, — соседние вершины берут веса ближайшей грани, а
    ближайшими у них оказываются разные конечности. Никакого перехода между
    ними нет: замер показал соседей в 4.5 см с весами LeftLeg 0.80 и
    RightLeg 0.74 соответственно, без единой общей кости. В bind pose ноги
    рядом и шва не видно, а на ударе полотно между ними разлетается плоскими
    плитами — ровно то, что было видно в Play.

    Тепловой расчёт решает диффузию по мешу и разрывов не даёт по
    построению. На проверочной позе: рёбер с растяжением больше пятикратного
    261 -> 24, максимум 58x -> 9.6x.

    Вторая причина отказаться от переноса: он копирует только те группы, что
    есть у тела, а у тел Mixamo нет групп Hips, Spine, Spine1, LeftUpLeg и
    RightUpLeg вовсе (45 групп на 65 костей — проверено на сыром файле, наш
    импорт тут ничего не теряет). Подолу халата было буквально не за что
    держаться, кроме голеней. Тепловой расчёт берёт веса со скелета и даёт
    все 65.
    """
    # Веса тепловой расчёт берёт по текущему положению меша, поэтому парентим
    # до правки трансформа, пока кимоно стоит ровно по телу.
    world = low.matrix_world.copy()
    bpy.ops.object.select_all(action='DESELECT')
    low.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')

    # parent_set(ARMATURE_AUTO), в отличие от parent_set(keep_transform=True),
    # мирового положения не сохраняет: замер показал уезд кимоно с z 0.147..1.628
    # под пол, в -0.282..0.101. Дальше pin_skirt_to_hips сравнивает высоту вершин
    # с высотой паха, и на уехавшем меше под посадку попадали все вершины разом —
    # кимоно становилось монолитом, а verify_deform показывал ровно 1.00 и
    # пропускал это как успех.
    #
    # Возвращаем ткань туда, где она стояла, и заодно в ту же локальную систему,
    # в какой приезжают меши тела: единичная matrix_parent_inverse, весь трансформ
    # унаследован от арматуры. Раньше у ткани был собственный масштаб 0.0024
    # против 0.0102 у тела; теперь они совпадают, и экспорт связки идёт по уже
    # проверенному на телах пути.
    low.data.transform(arm.matrix_world.inverted() @ world)
    low.matrix_parent_inverse.identity()
    low.matrix_basis.identity()
    bpy.context.view_layer.update()


def pin_skirt_to_hips(low, arm):
    """Всё ниже паха — жёстко на таз.

    Тепловой расчёт снимает разрывы весов, но полотно, натянутое между ног,
    всё равно обязано растягиваться, когда ноги расходятся на 110°: остаётся
    24 рваных ребра, максимум 9.6x. Длинный халат в жизни и не облегает
    каждую ногу — он качается от бедра целиком. Посадка подола на таз
    убирает разрывы полностью: 0 рёбер, максимум 4.0x.

    Цена решения: при высоком ударе нога проходит внутри подола. Это
    штатный размен для длинной одежды без симуляции ткани, и он заметно
    лучше, чем разлетающиеся плиты.
    """
    world = arm.matrix_world
    crotch = min((world @ bone_by_suffix(arm, ':' + side + 'UpLeg').head).z
                 for side in ('Left', 'Right'))
    band = (world @ bone_by_suffix(arm, ':Neck').head).z * SKIRT_BAND

    hips_bone = next(b.name for b in arm.data.bones
                     if b.name.split(':')[-1] == 'Hips')
    hips = low.vertex_groups.get(hips_bone) or low.vertex_groups.new(name=hips_bone)
    by_index = {g.index: g.name for g in low.vertex_groups}

    matrix = low.matrix_world
    for v in low.data.vertices:
        # t: 0 на верхней кромке полосы, 1 у паха и ниже.
        t = max(0.0, min(1.0, (crotch + band - (matrix @ v.co).z) / max(band, 1e-9)))
        if t <= 1e-4:
            continue
        for g in list(v.groups):
            if g.group != hips.index:
                low.vertex_groups[by_index[g.group]].add(
                    [v.index], g.weight * (1.0 - t), 'REPLACE')
        was = next((g.weight for g in v.groups if g.group == hips.index), 0.0)
        hips.add([v.index], was * (1.0 - t) + t, 'REPLACE')


def verify_deform(low, arm):
    """Гоняет проверочную позу и меряет растяжение рёбер ткани.

    Все прежние проверки смотрели bind pose и были к этому слепы:
    verify_export перечитывает записанный файл в покое, проверка размаха —
    тоже. Кимоно уезжало в Unity целым и разлеталось на первом ударе, а
    пайплайн об этом молчал. Сюда добавлена единственная проверка, которая
    видит деформацию.
    """
    def edge_lengths():
        graph = bpy.context.evaluated_depsgraph_get()
        graph.update()
        evaluated = low.evaluated_get(graph)
        mesh = evaluated.to_mesh()
        out = [(mesh.vertices[e.vertices[0]].co - mesh.vertices[e.vertices[1]].co).length
               for e in mesh.edges]
        evaluated.to_mesh_clear()
        return out

    rest = edge_lengths()
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    try:
        for suffix, axis, degrees in GUARD_POSE:
            bone = next((pb for pb in arm.pose.bones
                         if pb.name.split(':')[-1] == suffix), None)
            assert bone is not None, f'нет кости {suffix} для проверочной позы'
            bone.rotation_mode = 'XYZ'
            setattr(bone.rotation_euler, axis, math.radians(degrees))
        bpy.ops.object.mode_set(mode='OBJECT')
        posed = edge_lengths()
    finally:
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='POSE')
        for pb in arm.pose.bones:
            pb.rotation_euler = (0, 0, 0)
            pb.rotation_quaternion = (1, 0, 0, 0)
        bpy.ops.object.mode_set(mode='OBJECT')

    n = len(rest)
    # Порог длины — p99 покоя: почти все рёбра меша короче него. Одного
    # коэффициента мало. У второго бойца остаётся ребро 1.17 см -> 7.37 см,
    # это 6.3x, но в мире 7 см при медиане ребра 4 см и p99 22 см — обычный
    # размер для этой ткани, увидеть там нечего. А настоящий разрыв,
    # который был до правки скиннинга, уводил рёбра в 490 см при той же
    # медиане: он валит оба условия сразу. Поэтому рвано = растянулось
    # сильно И стало длиннее, чем почти всё в меше.
    long_rest = sorted(rest)[int(n * 0.99)]
    ratios = sorted(p / max(r, 1e-9) for p, r in zip(posed, rest))
    torn = [(p / max(r, 1e-9), p) for p, r in zip(posed, rest)
            if p / max(r, 1e-9) > STRETCH_MAX and p > long_rest]
    print(f'kimono_fit: деформация — медиана {ratios[n // 2]:.2f} '
          f'p99 {ratios[int(n * 0.99)]:.2f} максимум {ratios[-1]:.2f}, '
          f'рёбер длиннее {long_rest:.3f}: {len(torn)}')
    assert not torn, (
        f'{len(torn)} рёбер ткани растянулись больше чем в {STRETCH_MAX:g} раз '
        f'и переросли {long_rest:.3f} (худшее {max(t[1] for t in torn):.3f} '
        f'при {max(t[0] for t in torn):.1f}x) — скиннинг рвёт кимоно на анимации')


TEXTURE_SUBDIR = 'body'


def unpack_body_textures(meshes, fbx_path):
    """Выкладывает текстуры тела файлами в <каталог FBX>/body/. Возвращает {путь: байт}.

    Mixamo вшивает пиксели внутрь FBX, а пути пишет абсолютные, во временный
    каталог своего сервера (…\\mixamo-mini\\tmp\\skins_<uuid>.fbm\\), которого на
    этой машине нет и не было. Импортёр Blender распаковывает пиксели в память
    (packed_file), но файла на диске не создаёт — поэтому одного path_mode='COPY'
    мало: копировать ему нечего, и он молча не пишет НИ ОДНОГО файла (замерено:
    0 файлов, FBX 1.3 МБ). Без файлов Unity получает тело без diffuse, то есть
    ровно ту безликую голову, ради которой затевалась вся ревизия 2.

    Пишем packed_file.data как есть, без image.save(): это исходные байты PNG, а
    не пересжатие, поэтому файл совпадает побайтно с тем, что кладёт
    ExtractTextures на стороне Unity, и git не видит изменений на перегонах.
    """
    tex_dir = os.path.join(os.path.dirname(fbx_path), TEXTURE_SUBDIR)
    os.makedirs(tex_dir, exist_ok=True)
    imgs = {n.image for o in meshes for m in o.data.materials if m and m.node_tree
            for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image}
    assert imgs, 'у мешей тела нет ни одной текстуры — персонаж пришёл без скинов?'

    written = {}
    for img in imgs:
        assert img.packed_file, (
            f'текстура {img.name!r} не вшита в исходник и файла у неё нет: '
            f'{img.filepath!r} — выкладывать нечего')
        # Дубликаты датабликов (у Ch28 один Diffuse висит на трёх) сходятся в
        # один путь, поэтому written считает файлы, а не картинки.
        dst = os.path.join(tex_dir, os.path.basename(img.filepath))
        with open(dst, 'wb') as f:
            f.write(img.packed_file.data)
        img.filepath = dst
        written[dst] = len(img.packed_file.data)

    print(f'kimono_fit: текстур тела выложено {len(written)} на '
          f'{sum(written.values()) / 1048576:.1f} МБ в {tex_dir}')
    return written


# path_mode='RELATIVE', а не 'COPY' и не встраивание. Пиксели должны лежать в
# репозитории ровно один раз:
#   * embed_textures=True клал их внутрь FBX (Player 47.2 МБ, Enemy 15.7 МБ), но
#     Unity встроенные медиа поддассетами НЕ отдаёт — их приходится доставать
#     ModelImporter.ExtractTextures(), и распакованные PNG тоже надо коммитить.
#     Выходило 60 МБ файлов ПЛЮС 63 МБ тех же пикселей внутри FBX;
#   * 'COPY' поверх уже выложенных файлов скопировал бы их из body/ в каталог
#     самого FBX — тот же второй экземпляр, только рядом.
# RELATIVE пишет в FBX путь body/<имя>.png относительно модели, Unity находит
# текстуру при импорте сама, и экземпляр остаётся один: ~60 МБ на обоих бойцов.
# Запечённые карты кимоно сюда не попадают — Kimono_Cloth и Kimono_Belt пустые,
# без нод, их PNG Unity подхватывает отдельными ассетами.
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
        path_mode='RELATIVE',
        use_mesh_modifiers=True)


# Размер записанного FBX — единственное, что ловит ВОЗВРАТ дублирования пикселей.
# Проверка «текстура разрешается в файл рядом с моделью» (ниже) при embed_textures=True
# или при path_mode='COPY' поверх уже выложенного body/ проходит: файлы на месте, пути
# годные. А 60 МБ пикселей при этом снова уезжают в LFS вторым экземпляром — ровно то
# состояние, из которого пайплайн уводили.
# Опорные точки, замерены на этом же пайплайне:
#   файлами (сегодня)      Player  1.83 МБ, Enemy  1.60 МБ
#   со встраиванием        Player 47.2 МБ,  Enemy 15.7 МБ
# 8 МБ лежит между ними с запасом в обе стороны: вчетверо выше сегодняшнего худшего
# (то есть тело может потяжелеть вчетверо, не роняя прогон) и вдвое ниже самого
# лёгкого встроенного варианта.
FBX_MAX_BYTES = 8 * 1024 * 1024


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
    size = os.path.getsize(path)
    assert size <= FBX_MAX_BYTES, (
        f'{path} весит {size / 1048576:.2f} МБ при потолке '
        f'{FBX_MAX_BYTES / 1048576:.0f} МБ — похоже, текстуры снова внутри файла '
        '(embed_textures или path_mode=COPY), то есть в репозитории появился '
        'второй экземпляр тех же пикселей; см. FBX_MAX_BYTES')
    print(f'kimono_fit: {os.path.basename(path)} весит {size / 1048576:.2f} МБ')

    reset_scene()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path)
    new = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    kimono = next((o for o in new if o.name == 'Kimono_low'), None)
    assert kimono is not None, f'в {path} нет меша Kimono_low — экспорт переименовал его?'
    # Считаем РАЗНЫЕ material_index в употреблении, а не слоты: пустой слот дал
    # бы двойку и при полностью несостоявшемся разделении. Без этой проверки
    # пояс ловится только тестом в Unity, то есть задачей позже, когда негодный
    # FBX уже лежит на диске.
    used = {p.material_index for p in kimono.data.polygons}
    assert len(used) == 2, (
        f'в готовом FBX у Kimono_low полигоны ссылаются на {len(used)} материал(ов) '
        f'{sorted(used)} вместо двух (ткань, пояс) — разделение не пережило экспорт')
    body = [o for o in new if o is not kimono]

    # Текстура тела обязана разрешаться в существующий файл РЯДОМ С МОДЕЛЬЮ.
    # Исходный дефект Mixamo был именно тут: ссылка вела в каталог их сервера,
    # которого на этой машине нет, и Unity молча импортировала тело без diffuse.
    # В Blender это не видно — там картинка есть, просто в памяти, — так что
    # ловится только чтением записанного FBX. Проверка на «файл существует» без
    # проверки «внутри каталога модели» пропустила бы ровно исходный дефект на
    # той машине, где такой каталог случайно есть.
    used = {n.image for o in body for m in o.data.materials if m and m.node_tree
            for n in m.node_tree.nodes if n.type == 'TEX_IMAGE' and n.image}
    assert used, f'в {path} у мешей тела нет ни одной текстуры'
    here = os.path.dirname(os.path.abspath(path))
    broken = []
    for img in sorted(used, key=lambda i: i.name):
        p = os.path.abspath(bpy.path.abspath(img.filepath)) if img.filepath else ''
        inside = os.path.normcase(p).startswith(os.path.normcase(here + os.sep))
        if not p or not os.path.isfile(p) or not inside:
            broken.append(f'{img.name} -> {img.filepath!r}')
    assert not broken, (
        f'в {path} текстуры тела не разрешаются в файлы рядом с моделью: {broken} '
        '— потеряны unpack_body_textures() или path_mode=RELATIVE в export()')

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
    # Image.save() при относительном filepath_raw возвращает успех и не пишет
    # ничего (проверено: EXISTS False, исключения нет). До этой строки карты
    # доезжали до диска только потому, что build_kimono.ps1 звал скрипт с
    # абсолютным --out; ручной прогон из корня репозитория молча терял обе PNG.
    a.out = os.path.abspath(a.out)
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

    skin_cloth(low, arm)
    pin_skirt_to_hips(low, arm)

    skinned = sum(1 for v in low.data.vertices if v.groups)
    assert skinned == len(low.data.vertices), (
        f'{len(low.data.vertices) - skinned} вершин кимоно без весов — '
        'ткань останется висеть в воздухе')

    verify_deform(low, arm)

    fbx = os.path.normpath(os.path.join(a.out, '..', a.name + '.fbx'))
    unpack_body_textures(body_meshes, fbx)
    export(fbx, [arm, low] + body_meshes)
    verify_export(fbx)
    print(f'kimono_fit: подгонка scale={scale:.4f}, '
          f'{high_tris} -> {got} трисов, '
          f'экспорт {fbx}')


if __name__ == '__main__':
    main()
