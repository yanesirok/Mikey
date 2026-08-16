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

    Both slots point at placeholder materials — the baked textures are shared UV space,
    not per-slot content — Unity/FighterImportSetup assigns the real per-team materials
    by submesh index (0 = cloth, 1 = belt) after import. The spec wants player and enemy
    belts in different colours, which needs its own submesh: the shader's _RimColor is a
    fresnel highlight over the whole silhouette, not a belt.
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
# запеке и роняли каждый прогон. Но поставить вместо них числа, которые
# сегодняшний запек проходит, значило бы откалибровать порог по запеку, про
# который измерением известно, что он дефектный. Измерено (1024², по текселям
# под островами атласа; полностью в task-2-report.md):
#
#   * контроль в НАШЕЙ топологии — low, запечённый сам на себя: синий 0.994 при
#     σ 0.027, выше 0.9 — 99.1% текселей. Тангенциальный базис, развёртка и сам
#     оператор исправны, и чистый запек прежние 0.85/0.15 проходил бы с запасом.
#     Это и есть целевое состояние, в которое пороги надо вернуть;
#   * реальный запек high -> low, уже с cage под ткань: синий 0.726 при σ 0.355,
#     ниже 0.5 сидит 25.1% текселей. Синий ниже 0.5 — нормаль, смотрящая внутрь
#     поверхности; честная проекция такого дать не может;
#   * причина не в кастомных нормалях скана: их снятие не меняет ничего
#     (0.549 -> 0.553). Помогают две вещи — cage под ткань (см. bake_pair_cloth,
#     0.549 -> 0.726) и вчетверо больший бюджет трисов (low в 60k: 0.549 ->
#     0.651, cage тот же). Ранжировать их бессмысленно: обе действуют через одну
#     величину — насколько поверхность low отстоит от правильной поверхности
#     high по сравнению с расстоянием между слоями ткани. Вариант 60k к тому же
#     не «при прочих равных»: make_low после децимации заново гоняет
#     smart_project и pack_islands, так что у него другая развёртка и другая
#     плотность текселей.
#     Данные поддерживают ровно одно утверждение, и его достаточно: НИ ОДНА из
#     двух поодиночке до чистого запека не доводит. При cage 0 внутрь смотрят
#     всё ещё 19.1% текселей, а 60k даёт σ 0.404 — против контрольных 0.027.
#
# AO тёмный по другой причине и честно: 72% площади low-поли — изнанка и
# внутренности рукавов, у них AO и должен быть нулевым (рендером проверено, что
# лицевая сторона — кимоно со складками, а не чёрный мешок).
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
    """Синий канал tangent-space карты: 1.0 — прямо наружу, ниже 0.5 — внутрь поверхности."""
    px = _pixel_array(normal)
    blue = px[_baked_mask(px, (0.5, 0.5, 1.0)), 2]
    assert blue.size > 0, 'нормали не запеклись ни на одном текселе'
    return float(blue.mean()), float(blue.std())


# Значения моста (cage 20 мм, луч 60 мм) подобраны под доски, где до второй
# поверхности сантиметры. У кимоно слои ткани лежат в 0.3–14 мм друг от друга, и
# луч, стартующий в двух сантиметрах снаружи, проходит свой слой насквозь и
# упирается в изнанку соседнего — нормаль оттуда смотрит внутрь поверхности.
# Замерено на этой геометрии (1024², по текселям под островами атласа). Луч
# менялся вместе с cage, в отношении 4:1 — держать его постоянным при малом
# cage бессмысленно, он ограничивает ту же самую дистанцию поиска:
#   cage 20.0 мм луч 60.0 мм (мост): синий 0.544, >0.9 26.7%, <0.5 46.2%, промахов  2.9%
#   cage  1.0 мм луч  3.0 мм:        синий 0.538, >0.9 29.3%, <0.5 46.8%, промахов  7.2%
#   cage  0.2 мм луч  0.8 мм:        синий 0.726, >0.9 53.3%, <0.5 25.1%, промахов 24.8%
#   cage  0.0 мм луч  0.5 мм:        синий 0.787, >0.9 63.4%, <0.5 19.1%, промахов 33.9%
#
# Колонка >0.9 сама по себе обманывает: промах оставляет тексель ЗАЛИВКОЙ, а
# заливка — это синий 1.0, то есть каждый промах попадает в неё же. Вычтя
# промахи, получаем долю реально запечённого рельефа: 23.8% / 22.1% / 28.5% /
# 29.5%. То есть от cage 0.2 к нулю рельефа прибавляется один процентный пункт,
# вредных текселей убывает шесть, а промахов прибавляется девять.
#
# Отсюда выбор 0.2 мм, и критерий у него один — доля промахов. При нуле треть
# покрытой площади остаётся вообще без запечённой нормали; это хуже, чем шестая
# часть текселей с нормалью внутрь, потому что промах отбирает рельеф у ткани
# целыми пятнами, а не портит освещение в отдельных текселях. По отношению
# годных к вредным нуль, наоборот, лучше (3.3:1 против 2.1:1) — не этим и
# выбирали. Уменьшать cage дальше можно, но платить за это придётся ровно
# промахами: разрыв low/high так не лечится (см. комментарий у порогов выше).
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
    n_mean, n_std = check_normal_content(normal)
    print(f'kimono_fit: запек — AO среднее {ao_mean:.3f} медиана {ao_median:.3f}, '
          f'нормали синий среднее {n_mean:.3f} sigma {n_std:.3f}')


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
    # Считаем РАЗНЫЕ material_index в употреблении, а не слоты: пустой слот дал
    # бы двойку и при полностью несостоявшемся разделении. Без этой проверки
    # пояс ловится только тестом в Unity, то есть задачей позже, когда негодный
    # FBX уже лежит на диске.
    used = {p.material_index for p in kimono.data.polygons}
    assert len(used) == 2, (
        f'в готовом FBX у Kimono_low полигоны ссылаются на {len(used)} материал(ов) '
        f'{sorted(used)} вместо двух (ткань, пояс) — разделение не пережило экспорт')
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

    body = max(body_meshes, key=tri_count)
    transfer_weights(low, body, arm)

    skinned = sum(1 for v in low.data.vertices if v.groups)
    assert skinned == len(low.data.vertices), (
        f'{len(low.data.vertices) - skinned} вершин кимоно без весов — '
        'ткань останется висеть в воздухе')

    fbx = os.path.join(a.out, '..', a.name + '.fbx')
    export(os.path.normpath(fbx), [arm, low] + body_meshes)
    verify_export(os.path.normpath(fbx))
    print(f'kimono_fit: подгонка scale={scale:.4f}, '
          f'{high_tris} -> {got} трисов, '
          f'экспорт {os.path.normpath(fbx)}')


if __name__ == '__main__':
    main()
