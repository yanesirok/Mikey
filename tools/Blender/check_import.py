"""Проверка: bridge_kit импортируется, не запуская сборку моста."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bridge_kit

for name in ('_install_deterministic_fbx_uuids', 'bake_pair', 'fill',
             'save_png', 'tri_count', 'apply_mods', 'reset_scene',
             'bake_target_material', 'MARGIN'):
    assert hasattr(bridge_kit, name), f'bridge_kit не отдаёт {name}'
assert bridge_kit.ATLAS == 2048
print('IMPORT_OK')
