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
    # load_rig вешает арматуру, но не грузит веса — тело один раз уехало в
    # kimono_fit.py с 64 пустыми Deformer/Cluster (кости на месте, а с ними
    # не связана ни одна вершина). Проверки выше это пропускают: они видят
    # только имена костей в скелете, а не то, привязан ли к ним меш. Indexes
    # и Weights — свойства бинарного FBX, которые реально несут номера вершин
    # и веса каждого кластера; без load_weights их в файле попросту нет
    # (проверено grep'ом по байтам). Не убирать эту проверку как избыточную
    # рядом с REQUIRED — она ловит другой дефект.
    for prop in (b'Indexes', b'Weights'):
        if prop not in data:
            fails.append(f'в теле нет скиннинга — нет свойства {prop.decode()}')
    size_mb = len(data) / 1024 / 1024
    if size_mb > 25:
        fails.append(f'тело весит {size_mb:.1f} МБ — похоже, уехали хелперы MPFB')

if fails:
    print('\n'.join('FAIL ' + f for f in fails))
    sys.exit(1)
print('BODY_OK')
