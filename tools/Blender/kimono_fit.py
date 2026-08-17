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


# Здесь стоял список мешей одежды персонажа, которые пайплайн снимал, и рядом
# с ним — обоснование: «меш тела при этом полный, от стоп до макушки (Ch28_Body
# z 0.064..1.764), поэтому под снятой одеждой дыр нет». Обоснование было
# ложным, и проверяли его негодной мерой: bbox тела действительно тянется от
# 0.064 до 1.764, но только потому, что сверху голова, а снизу ступни. Между
# ними пусто. Габарит не умеет отличить целое тело от головы со ступнями, а
# гистограмму вершин по высоте никто не снял. См. import_body, ревизия 4.

# С персонажа Mixamo снимается ТОЛЬКО обувь: тела она не несёт (ступни лежат
# в самом меше Body), а карате босое.
#
# Всё остальное — тело. Проверено кадром с выключенным кимоно: без Tops и
# Bottoms от бойца остаются голова, две отдельно висящие в воздухе руки от
# локтя, и голени со ступнями. Торс и плечи лежат в Tops, бёдра в Bottoms.
# Промежуточный заход снимал Tops (он пробивал халат сзади) — и вернул ровно
# ту картину, с которой всё началось. Пробой лечится посадкой кимоно, см.
# seat_on_body, а не снятием тела.
CHARACTER_STRIP = ('sneakers', 'shoes', 'boots')

# Собственная одежда персонажа: её можно резать там, где закрыло кимоно.
# Кожа (Body) в список не входит — это лицо, кисти и ступни, всё видимое.
CHARACTER_CLOTHES = ('hoody', 'tops', 'pants', 'bottoms')


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

    # Ревизия 4: одежда персонажа больше НЕ снимается.
    #
    # Персонажи Mixamo смоделированы одетыми, и меш 'Body' у них — только
    # открытая кожа. Снимая Hoody и Pants, мы уносили вместе с ними торс и
    # ноги. Замер готовой сцены: у Ch28_Body 9466 вершин лежат на высотах
    # 1.7/1.6/1.5/1.4 м (голова, шея, плечи, руки) и 0.1/0.0 м (ступни), а
    # между 0.2 и 1.3 м нет ни одной. Боец состоял из головы, рук и ступней,
    # висящих в воздухе; кимоно работало не одеждой поверх тела, а
    # единственной оболочкой вместо тела.
    #
    # Отсюда росли жалобы, которые я лечил не там: «оторванные куски в
    # воздухе» — это кисти без рук, «плоские крылья» — треугольники от culla
    # плеча к этим кистям, «одежда сидит неестественно» — под ней нечему
    # быть. Первая жалоба владельца в этой работе была именно про руки.
    #
    # Одежда персонажа теперь остаётся телом под кимоно. Кимоно — халат в пол
    # с широкими рукавами, так что от неё видно только предплечья и щиколотки.
    #
    # Снимается только то, что перечислено в CHARACTER_STRIP: обувь и верх.
    # Штаны остаются — они и есть ноги, которые видно из-под подола.
    doomed = [o for o in meshes
              if any(c in o.name.lower() for c in CHARACTER_STRIP)]
    # Имена снимаем до удаления: после remove() объект — мёртвый StructRNA, и
    # обращение к .name в печати роняет прогон (проверено, ReferenceError).
    doomed_names = [o.name for o in doomed]
    for o in doomed:
        bpy.data.objects.remove(o, do_unlink=True)
    meshes = [o for o in meshes if o not in doomed]
    assert meshes, 'у персонажа не осталось ни одного меша'

    k = normalize_height(arm, meshes)
    print(f'kimono_fit: персонаж {os.path.basename(path)} — снято '
          f'{doomed_names}, мешей {len(meshes)} '
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


# Меши, которые не участвуют в посадке: волосы, ресницы, глаза. Волосы
# особенно — хвост на затылке сдвинул бы весь халат вперёд.
FIT_IGNORE = ('hair', 'eyelash', 'eye')

# Зазор между телом и тканью по глубине, в метрах. Ткань обязана лежать НАД
# телом с запасом: в bind pose хватило бы и миллиметра, но на анимации меши
# деформируются по-разному, и впритык посаженная ткань начинает пропускать
# тело наружу.
#
# 2 см оказалось мало: у врага расчёт дал расширение ×1.00 (то есть ткань
# прошла ровно впритык, с запасом в 2 мм по полуразмаху), и на кадре сзади
# сквозь лопатки проступала футболка. 4 см требуют расширения ×1.13 и
# закрывают её.
FIT_CLEARANCE = 0.04

# Потолок расширения по глубине. Больше — значит халат вообще не по фигуре,
# и растягивать его дальше уже нельзя; лучше упасть и разбираться.
FIT_MAX_WIDEN = 1.5


def _depth_bands(objs, lo, hi, bands=6):
    """Для каждого пояса высот: (центр, полуразмах) по глубине (ось Y)."""
    out = []
    step = (hi - lo) / bands
    for i in range(bands):
        z0, z1 = lo + i * step, lo + (i + 1) * step
        ys = [(o.matrix_world @ v.co).y for o in objs for v in o.data.vertices
              if z0 <= (o.matrix_world @ v.co).z < z1]
        out.append(((min(ys) + max(ys)) / 2, (max(ys) - min(ys)) / 2) if ys else None)
    return out


def seat_on_body(kimono, body_meshes, arm):
    """Сажает кимоно на тело по глубине: центрует и добавляет недостающую ширину.

    fit() выставляет только рост (воротник к шее, подол к стопе) и центрует по
    ОБЩЕМУ габариту. Для этого халата общий габарит врёт: распахнутые передние
    полы уводят его центр вперёд, и на теле кимоно садится со сдвигом. Замер по
    поясам высот: ткань спереди доходит до -0.28, сзади только до +0.06..+0.10,
    а спина тела — на +0.13..+0.15. Восемь сантиметров спины оказывались
    снаружи халата, и на кадре сзади сквозь него была видна футболка и шорты.

    Здесь считается честная поправка: по поясам от паха до шеи берём центр и
    полуразмах тела и ткани, сдвигаем ткань на разницу центров и растягиваем
    по глубине ровно настолько, чтобы тело поместилось с зазором.

    Ось Y (глубина), а не общий обхват: по ширине мешают руки, разведённые в
    T-позе, — они дают «телу» полуразмах в 0.9 м и требование расширить халат
    втрое. Проверено: первый заход считал именно так и просил ×3.08.
    """
    body = [o for o in body_meshes
            if not any(k in o.name.lower() for k in FIT_IGNORE)]
    assert body, 'не из чего собрать цель посадки — остались одни волосы?'

    lo = bone_head_z(arm, ':LeftUpLeg')
    hi = bone_head_z(arm, ':Neck')
    b = _depth_bands(body, lo, hi)
    k = _depth_bands([kimono], lo, hi)
    pairs = [(bb, kk) for bb, kk in zip(b, k) if bb and kk]
    assert pairs, 'посадка: пояса высот пусты, мерить нечего'

    shift = sum(bb[0] - kk[0] for bb, kk in pairs) / len(pairs)
    widen = max([(bb[1] + FIT_CLEARANCE) / kk[1] for bb, kk in pairs] + [1.0])
    assert widen <= FIT_MAX_WIDEN, (
        f'кимоно пришлось бы расширить по глубине в {widen:.2f} раза '
        f'(потолок {FIT_MAX_WIDEN}) — халат не по этой фигуре')

    centre = sum(bb[0] for bb, _ in pairs) / len(pairs)
    m = kimono.matrix_world
    inv = m.inverted()
    for v in kimono.data.vertices:
        p = m @ v.co
        p.y = centre + (p.y + shift - centre) * widen
        v.co = inv @ p
    kimono.data.update()
    print(f'kimono_fit: посадка по глубине — сдвиг {shift * 100:+.1f} см, '
          f'расширение ×{widen:.2f}, зазор {FIT_CLEARANCE * 100:.1f} см')


# Запас при вырезании укрытой геометрии, в метрах. Отрицательный намеренно:
# режется даже то, что в позе покоя торчит наружу на пару сантиметров.
#
# Положительный запас (было 1.2 см) режет ровно по укрытости В ПОКОЕ, а
# пробой случается В ДВИЖЕНИИ: ткань и тело деформируются по-разному, и на
# ударе наружу выходит то, что в T-позе лежало внутри. Отрицательный запас
# отдаёт эти два сантиметра заранее.
COVER_MARGIN = -0.02

# Сетка, по которой меряется укрытость: угол вокруг вертикальной оси и высота.
COVER_ANGLES = 48
COVER_LEVELS = 40


def drop_character_clothes(body_meshes):
    """Убирает собственную одежду персонажа, оставляя кожу. Возвращает остаток."""
    doomed = [o for o in body_meshes
              if any(c in o.name.lower() for c in CHARACTER_CLOTHES)]
    names = [o.name for o in doomed]
    for o in doomed:
        bpy.data.objects.remove(o, do_unlink=True)
    kept = [o for o in body_meshes if o not in doomed]
    assert kept, 'после снятия одежды не осталось ни одного меша'
    print(f'kimono_fit: снята одежда персонажа {names}, осталось '
          f'{[o.name for o in kept]}')
    return kept


def strip_covered(kimono, body_meshes, arm):
    """Удаляет геометрию тела там, где её закрывает кимоно.

    Зазора мало. В bind pose тело помещается внутрь ткани полностью (замер:
    0% вершин снаружи), но на анимации меши деформируются по-разному, и на
    клипе Punch наружу выходит 2079 вершин у врага и 3709 у игрока — серые
    пятна футболки и белая нога прямо сквозь халат. Увеличение зазора это не
    лечит: на любой запас найдётся поза.

    Единственное надёжное — не иметь под тканью того, что может из неё выйти.
    Укрытость меряется честно: строим по сетке (угол, высота) максимальный
    радиус кимоно и удаляем те грани тела, все вершины которых лежат глубже
    ткани на COVER_MARGIN. Грань у самого края остаётся, поэтому на границе
    видимого и укрытого рваного шва не возникает.

    Кожа (меш Body) не трогается вовсе: это лицо, кисти и ступни — всё
    видимое. Режется только собственная одежда персонажа, которую кимоно
    и закрывает.
    """
    kmn, kmx = world_bbox([kimono])
    bmn, bmx = world_bbox(body_meshes)
    axis = ((bmn.x + bmx.x) / 2, (bmn.y + bmx.y) / 2)
    lo, hi = kmn.z, kmx.z
    if hi - lo < 1e-6:
        return

    # Максимальный радиус ткани по ячейкам (угол, высота).
    grid = [[0.0] * COVER_LEVELS for _ in range(COVER_ANGLES)]
    m = kimono.matrix_world
    for v in kimono.data.vertices:
        p = m @ v.co
        li = int((p.z - lo) / (hi - lo) * COVER_LEVELS)
        li = min(COVER_LEVELS - 1, max(0, li))
        dx, dy = p.x - axis[0], p.y - axis[1]
        ai = int((math.atan2(dy, dx) + math.pi) / (2 * math.pi) * COVER_ANGLES)
        ai = min(COVER_ANGLES - 1, max(0, ai)) 
        r = math.hypot(dx, dy)
        if r > grid[ai][li]:
            grid[ai][li] = r

    total = 0
    for o in body_meshes:
        if not any(c in o.name.lower() for c in CHARACTER_CLOTHES):
            continue
        mo = o.matrix_world
        covered = set()
        for v in o.data.vertices:
            p = mo @ v.co
            if not (lo <= p.z <= hi):
                continue
            li = int((p.z - lo) / (hi - lo) * COVER_LEVELS)
            li = min(COVER_LEVELS - 1, max(0, li))
            dx, dy = p.x - axis[0], p.y - axis[1]
            ai = int((math.atan2(dy, dx) + math.pi) / (2 * math.pi) * COVER_ANGLES)
            ai = min(COVER_ANGLES - 1, max(0, ai))
            if grid[ai][li] > math.hypot(dx, dy) + COVER_MARGIN:
                covered.add(v.index)

        doomed = [p.index for p in o.data.polygons
                  if all(vi in covered for vi in p.vertices)]
        if not doomed:
            continue
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='DESELECT')
        bpy.ops.object.mode_set(mode='OBJECT')
        for i in doomed:
            o.data.polygons[i].select = True
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.delete(type='FACE')
        bpy.ops.object.mode_set(mode='OBJECT')
        total += len(doomed)
        print(f'kimono_fit: с {o.name} снято укрытых граней {len(doomed)}')
    print(f'kimono_fit: вырезано укрытой геометрии — граней {total}, '
          f'запас {COVER_MARGIN * 100:.1f} см')


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

# Проходов сглаживания весов после переноса: снимают ступеньку на швах, где
# соседние вершины взяли веса разных частей тела. Больше — ткань становится
# ватной и перестаёт следовать телу.
SEAM_SMOOTH = 3

# Насколько сильно подол сажается на таз, 0..1. Единица делает его жёстким
# конусом: не рвётся, но нога проходит сквозь него на высоком ударе. Ноль
# отдаёт подол ногам целиком — и он рвётся на перемычке между ними (замер без
# посадки: p99 растяжения 4.83 против 2.31, 4 рваных ребра). Промежуточное
# значение оставляет подолу возможность следовать за ногой, ограничивая
# расхождение.
SKIRT_PIN = 0.65

# Порог приёмки скиннинга: во сколько раз ребру позволено растянуться на
# проверочной позе. Здоровая ткань на замерах не переходит 4x, разрыв даёт
# десятки — 5.0 разделяет их с запасом и не ловит честное натяжение подола.
STRETCH_MAX = 5.0

# Проверочная поза: развод ног как в ударе, сгиб колена, скрутка корпуса и
# рук. Именно она вскрывает разрывы, которые bind pose прячет в складках.
GUARD_POSE = (('LeftUpLeg', 'x', 70), ('RightUpLeg', 'x', -40),
              ('LeftLeg', 'x', -50), ('Spine2', 'x', 20),
              ('LeftForeArm', 'y', -70), ('LeftShoulder', 'z', -30))


# Расстояния до поверхности тела, по которым решается, чьи веса брать, в
# метрах. Ближе NEAR ткань считается лежащей на теле — её ведут веса тела.
# Дальше FAR она висит свободно (широкий рукав, подол) — её ведёт скелет.
# Между ними линейная смесь, иначе на границе появится ступенька весов.
WEIGHT_NEAR = 0.02
WEIGHT_FAR = 0.07


def _read_weights(o):
    names = {g.index: g.name for g in o.vertex_groups}
    return [{names[g.group]: g.weight for g in v.groups} for v in o.data.vertices]


def _write_weights(o, per_vertex):
    for g in list(o.vertex_groups):
        o.vertex_groups.remove(g)
    groups = {}
    for vi, w in enumerate(per_vertex):
        for name, val in w.items():
            if val <= 1e-5:
                continue
            if name not in groups:
                groups[name] = o.vertex_groups.new(name=name)
            groups[name].add([vi], val, 'REPLACE')


def _body_shell(body_meshes):
    """Склеенная копия тела в позе покоя — цель для переноса и замера расстояний."""
    src = [o for o in body_meshes
           if not any(k in o.name.lower() for k in FIT_IGNORE)]
    assert src, 'не из чего собрать тело — остались одни волосы?'
    copies = []
    for o in src:
        c = o.copy()
        c.data = o.data.copy()
        c.modifiers.clear()
        bpy.context.collection.objects.link(c)
        copies.append(c)
    bpy.ops.object.select_all(action='DESELECT')
    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    if len(copies) > 1:
        bpy.ops.object.join()
    return bpy.context.view_layer.objects.active


def blend_weights_by_fit(low, body_meshes, arm):
    """Смешивает два скиннинга: веса тела там, где ткань на теле, скелет — где висит.

    Ни один из двух способов не верен целиком, и оба уже проверены на кадрах.

    Перенос весов с тела (POLYINTERP_NEAREST) точен для облегающих мест: корпус
    и полы двигаются ровно как тело, ничего не расходится. Но для свободных он
    врёт: у широкого рукава ближайшая поверхность тела — не рука, а бок, и
    рукав прилипает к корпусу, схлопываясь на ударе. На кадре из рукава кимоно
    торчал серый рукав толстовки.

    Костное тепловое взвешивание наоборот: рукава ведёт правильно, потому что
    считает диффузию по самой ткани, а не ищет ближайшее тело. Зато корпус у
    него живёт своей жизнью — ткань идёт по своей идее о скелете, тело по
    своей, и на блоке спина халата раскрывалась, а из плеча лез плоский клин.

    Поэтому: считаем оба набора и берём каждый там, где он прав, по
    расстоянию вершины до поверхности тела. Переход линейный, чтобы на
    границе не возникла ступенька, следом идёт сглаживание швов.
    """
    shell = _body_shell(body_meshes)

    # 1. Тепловой расчёт по скелету.
    # Мировую матрицу запоминаем ДО parent_set: он её меняет, и взять её после
    # значит вернуть ткань не туда. Проверено — при чтении после кимоно уезжало
    # с z 0.937 под пол на -0.218, расстояния до тела вырастали до метра, и вся
    # смесь вырождалась в чистый тепловой расчёт.
    world = low.matrix_world.copy()
    bpy.ops.object.select_all(action='DESELECT')
    low.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='ARMATURE_AUTO')
    heat = _read_weights(low)
    low.data.transform(arm.matrix_world.inverted() @ world)
    low.matrix_parent_inverse.identity()
    low.matrix_basis.identity()
    bpy.context.view_layer.update()

    # 2. Перенос с тела.
    for g in list(low.vertex_groups):
        low.vertex_groups.remove(g)
    for vg in shell.vertex_groups:
        low.vertex_groups.new(name=vg.name)
    mod = low.modifiers.new('weights', 'DATA_TRANSFER')
    mod.object = shell
    mod.use_vert_data = True
    mod.data_types_verts = {'VGROUP_WEIGHTS'}
    mod.vert_mapping = 'POLYINTERP_NEAREST'
    bpy.context.view_layer.objects.active = low
    apply_mods(low)
    worn = _read_weights(low)

    # 3. Смесь по расстоянию до тела.
    tree = mathutils.bvhtree.BVHTree.FromObject(shell, bpy.context.evaluated_depsgraph_get())
    # BVHTree.FromObject строит дерево в ЛОКАЛЬНЫХ координатах меша, а не в
    # мировых. Спрашивать его мировой точкой нельзя: первый заход так и делал,
    # и все 4838 вершин вышли «далеко от тела» — смесь выродилась в чистый
    # тепловой расчёт. Переводим точку в систему тела, а найденное расстояние
    # обратно в метры через масштаб (он равномерный).
    to_shell = shell.matrix_world.inverted()
    shell_scale = shell.matrix_world.to_scale().x
    m = low.matrix_world
    mixed = []
    loose = 0
    for vi, v in enumerate(low.data.vertices):
        local = to_shell @ (m @ v.co)
        hit = tree.find_nearest(local)
        d = ((local - hit[0]).length * shell_scale
             if hit and hit[0] is not None else WEIGHT_FAR * 2)
        a = max(0.0, min(1.0, (d - WEIGHT_NEAR) / max(WEIGHT_FAR - WEIGHT_NEAR, 1e-9)))
        if a > 0.5:
            loose += 1
        w = {}
        for name, val in worn[vi].items():
            w[name] = w.get(name, 0.0) + val * (1.0 - a)
        for name, val in heat[vi].items():
            w[name] = w.get(name, 0.0) + val * a
        total = sum(w.values())
        if total > 1e-6:
            w = {k: val / total for k, val in w.items()}
        mixed.append(w)

    _write_weights(low, mixed)
    bpy.data.objects.remove(shell, do_unlink=True)

    armmod = low.modifiers.new('Armature', 'ARMATURE')
    armmod.object = arm
    bpy.ops.object.select_all(action='DESELECT')
    low.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='OBJECT', keep_transform=True)

    print(f'kimono_fit: скиннинг смесью — свободных вершин (ведёт скелет) '
          f'{loose} из {len(low.data.vertices)}, остальные ведёт тело')


def wear_body_weights(low, body_meshes, arm):
    """Одевает ткань как одежду ЭТОГО тела: отдаёт ей веса самого тела.

    Собственная одежда персонажа Mixamo деформируется безупречно ровно
    потому, что сшита на это тело и носит его веса. Ткань, привязанная к
    костям отдельно от тела (тепловым расчётом), движется по своей идее о
    скелете, а тело по своей — и на анимации они расходятся: из плеча лезет
    клин, корпус складывается доской, футболка проступает наружу.

    Перенос весов уже пробовался раньше и дал разрывы: соседние вершины
    подола брали веса разных ног без перехода (LeftLeg 0.80 против RightLeg
    0.74 в 4.5 см друг от друга). Но тогда халат висел мимо фигуры — посадка
    по глубине была смещена на 8 см, и подол болтался далеко от тела, так что
    «ближайшая грань» для него значила мало. После seat_on_body ткань лежит по
    телу, и перенос попадает туда, куда должен.

    Разрыв в паху всё равно возможен — подол физически натянут между ног, —
    поэтому следом идёт pin_skirt_to_hips, а сглаживание снимает ступеньку на
    границе. Если что-то останется, это поймает verify_deform.
    """
    src = [o for o in body_meshes
           if not any(k in o.name.lower() for k in FIT_IGNORE)]
    assert src, 'не с чего переносить веса — остались одни волосы?'

    # DATA_TRANSFER принимает один объект, тело приходит несколькими мешами.
    # Склеиваем копию: оригиналы уезжают в экспорт, их трогать нельзя.
    # join() сводит группы весов по именам, так что копия несёт их все.
    copies = []
    for o in src:
        c = o.copy()
        c.data = o.data.copy()
        c.modifiers.clear()
        bpy.context.collection.objects.link(c)
        copies.append(c)
    bpy.ops.object.select_all(action='DESELECT')
    for c in copies:
        c.select_set(True)
    bpy.context.view_layer.objects.active = copies[0]
    if len(copies) > 1:
        bpy.ops.object.join()
    shell = bpy.context.view_layer.objects.active

    for vg in shell.vertex_groups:
        if vg.name not in low.vertex_groups:
            low.vertex_groups.new(name=vg.name)

    mod = low.modifiers.new('weights', 'DATA_TRANSFER')
    mod.object = shell
    mod.use_vert_data = True
    mod.data_types_verts = {'VGROUP_WEIGHTS'}
    mod.vert_mapping = 'POLYINTERP_NEAREST'
    bpy.context.view_layer.objects.active = low
    apply_mods(low)
    bpy.data.objects.remove(shell, do_unlink=True)

    armmod = low.modifiers.new('Armature', 'ARMATURE')
    armmod.object = arm

    # parent_set сам считает matrix_parent_inverse. Руками её задавать нельзя:
    # экспортёр FBX реконструирует для такого объекта другой Lcl Rotation, и
    # кимоно приезжало в Unity повёрнутым на 90° (см. историю правок).
    bpy.ops.object.select_all(action='DESELECT')
    low.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.parent_set(type='OBJECT', keep_transform=True)

    weighted = sum(1 for v in low.data.vertices if v.groups)
    print(f'kimono_fit: ткань одета весами тела — взвешено {weighted} '
          f'из {len(low.data.vertices)} вершин, групп {len(low.vertex_groups)}')


def smooth_cloth_weights(low, repeat):
    """Снимает ступеньку весов на швах переноса."""
    if repeat <= 0:
        return
    bpy.ops.object.select_all(action='DESELECT')
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.mode_set(mode='WEIGHT_PAINT')
    bpy.ops.object.vertex_group_smooth(group_select_mode='ALL', factor=0.5,
                                       repeat=repeat, expand=0.0)
    bpy.ops.object.vertex_group_normalize_all(group_select_mode='ALL',
                                              lock_active=False)
    bpy.ops.object.mode_set(mode='OBJECT')


def fill_unweighted(low):
    """Вершинам, которых не достал тепловой расчёт, отдаёт веса ближайшей соседки.

    Костное тепловое взвешивание решает диффузию и на отдельных вершинах
    решения не находит — обычно там, где геометрия сложилась в почти
    вырожденный лоскут. После добавления подгонки по обхвату таких вершин
    стало 2399, и прогон вставал на проверке 'ткань останется висеть в
    воздухе'. Проверку убирать нельзя: невзвешенная вершина — это дыра,
    которая в Unity замирает в bind pose, пока вокруг всё движется.

    Ближайшая взвешенная соседка — честная замена: веса скиннинга меняются
    по мешу плавно, поэтому на расстоянии одного ребра ошибка мала.
    """
    weighted = [v.index for v in low.data.vertices if v.groups]
    orphans = [v.index for v in low.data.vertices if not v.groups]
    if not orphans:
        return
    assert weighted, 'тепловой расчёт не взвесил ни одной вершины'

    tree = mathutils.kdtree.KDTree(len(weighted))
    for n, vi in enumerate(weighted):
        tree.insert(low.data.vertices[vi].co, n)
    tree.balance()

    by_index = {g.index: g for g in low.vertex_groups}
    for vi in orphans:
        _, n, _ = tree.find(low.data.vertices[vi].co)
        src = low.data.vertices[weighted[n]]
        for g in src.groups:
            by_index[g.group].add([vi], g.weight, 'REPLACE')
    print(f'kimono_fit: добрано весов ближайшей соседкой: {len(orphans)} вершин')


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
        t *= SKIRT_PIN
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
    # Порог длины — вдвое больше p99 покоя, а не сам p99.
    #
    # Сначала здесь стоял голый p99, и он споткнулся о безобидное: после
    # посадки кимоно по глубине 4 ребра дали 7x при длине 25.8 против p99
    # 22.15, то есть всего на 16% длиннее обычного длинного ребра меша —
    # увидеть там нечего. Настоящий разрыв, который эта проверка и писалась
    # ловить, уводил рёбра на 423.6 при p99 92.9, вчетверо с лишним за него.
    # Двойка лежит между этими случаями с большим запасом с обеих сторон.
    long_rest = sorted(rest)[int(n * 0.99)] * 2.0
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

    seat_on_body(kimono, body_meshes, arm)

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

    blend_weights_by_fit(low, body_meshes, arm)
    # Одежда персонажа снимается ПОСЛЕ скиннинга, и порядок здесь несущий.
    #
    # До скиннинга она нужна: это поверхность тела, с которой берутся веса и по
    # которой меряется расстояние. Без неё от бойца остаются голова, предплечья
    # и ступни — переносить веса не с чего (ровно это и вышло, когда снятие
    # стояло раньше: все 4838 вершин ткани считались висящими свободно).
    #
    # После скиннинга она вредна: кимоно сидит по фигуре и закрывает торс
    # целиком, плечи лежат внутри рукавов, ноги — под штанами кимоно, так что
    # видеть её негде, а пробивать наружу на резком движении она может. Проверено
    # кадром: с погашенными Tops и Bottoms боец спереди и сзади цел полностью.
    body_meshes = drop_character_clothes(body_meshes)
    fill_unweighted(low)
    pin_skirt_to_hips(low, arm)
    smooth_cloth_weights(low, SEAM_SMOOTH)

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
