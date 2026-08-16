# Кимоно на готовые тела Mixamo — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Одеть в кимоно двух готовых персонажей Mixamo — `Ch28_nonPBR` игроку, `Remy` врагу — вместо безликого тела, генерируемого MPFB2, и убрать вырезание геометрии под тканью, из-за которого у бойцов отваливались кисти.

**Architecture:** Тело перестаёт генерироваться и начинает приходить готовым файлом. `kimono_fit.py` учится принимать персонажа Mixamo: снимать его собственную одежду целыми мешами, нормализовать рост и искать кости по суффиксу имени, потому что префикс у разных выгрузок различается. Шаг `strip_covered` удаляется целиком. Пайплайн гоняется дважды, по разу на бойца. Unity-сторона получает две модели вместо одной.

**Tech Stack:** Blender 5.1 (bpy, Cycles CPU), Unity 6000.3.18f1 (Humanoid retarget, Mecanim), Unity CLI 1.0.0-beta.3, NUnit EditMode.

**Spec:** `docs/superpowers/specs/2026-08-15-kimono-fighters-design.md`, действует **ревизия 2** от 2026-08-16. Ревизия 1 — исторический раздел, её источник тела отменён.

## Global Constraints

- **Unity Editor сейчас ОТКРЫТ** (пользователь смотрел арену) и держит project lock. `unity test` и `FightCapture` при этом не работают. Первый шаг любой задачи, которой нужен headless-прогон, — закрыть редактор.
- Blender: `C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`, `--background --factory-startup`. MPFB2 больше не нужен ни одному скрипту этого плана.
- Целевая высота обоих бойцов — **1.75 м**. Множитель считается как отношение целевой высоты к фактической, не зашивается константой.
- Кости ищутся **по суффиксу имени**, не точным совпадением: у `Ch28_nonPBR` префикс `mixamorig10:`, у `Remy` — `mixamorig:`.
- Бюджет кимоно — **12 000 треугольников**, атлас **2048²**.
- Root motion не используется и остаётся выключенным; это стережёт тест `Fighters_DoNotApplyRootMotion`.
- Контракт аниматора не меняется: состояния Idle, Walk, Punch, PunchB, Kick, Hit, BlockHit, Blocking, Death.
- Арт-бинарники в этом репозитории лежат непоследовательно. После каждого коммита с бинарниками сверяться **`git ls-files <путь>`**, а не `git status`.
- В рабочем дереве лежат **незакоммиченные изменения пользователя** в `tools/Blender/bridge_kit.py` и `Assets/Pose/**`, а также неотслеживаемый рантайм-слой `Assets/Fight/*.cs`. В индекс их не добавлять. Только свои файлы поимённо, никаких `git add -A` и никаких каталогов.
- Старые `Ch15_nonPBR`, `make_body.py`, `build_body.ps1`, `check_body.py` **не удалять** — сносятся отдельным коммитом после того, как сцена отстоится.

---

### Task 1: Зафиксировать осиротевшую работу прерванной волны

В рабочем дереве лежат несохранённые правки от агента, которого прервали между редактированием файлов и коммитом. Работа настоящая и нужная, но **непроверенная** — пайплайн после неё ни разу не запускался.

Задача существует по одной причине: пять последующих задач будут править те же файлы, и если оставить эти изменения болтаться, они уедут в чужой коммит. В этой сессии так уже произошло однажды.

**Files:**
- Commit as-is: `tools/Blender/kimono_fit.py`, `tools/Blender/make_body.py`, `Assets/Editor/FighterImportSetup.cs`, `Assets/Editor/FightSceneSwap.cs`, `Assets/Editor/FightCapture.cs`

**Interfaces:**
- Consumes: ничего.
- Produces: чистое рабочее дерево по этим пяти файлам. В `kimono_fit.py` появляются `KIMONO_PARTS`, `import_kimono_parts`, `weld_seams_and_fix_normals`, `capture_belt_mask`, `apply_belt_split`, `check_ao_content`, `check_normal_content`, `UV_COVERAGE_MIN`. В `FighterImportSetup.cs` — `SetUpAoMap()` и материалы пояса. В `FightSceneSwap.cs` — `SetMaterials(root, childName, Material[])`.

- [ ] **Step 1: Прочитать, что именно фиксируешь**

```bash
git diff --stat tools/Blender/kimono_fit.py tools/Blender/make_body.py Assets/Editor/FighterImportSetup.cs Assets/Editor/FightSceneSwap.cs Assets/Editor/FightCapture.cs
git diff tools/Blender/kimono_fit.py
```

Ожидание: около 235 добавленных строк на пять файлов. Содержательно там: фильтр манекена из `kimono.glb` по имени материала, разделение пояса в отдельный сабмеш, проверки содержимого запечённых карт, снятие sRGB с AO-карты, материалы пояса, и починка в `FightCapture` — отражение воды теперь рендерится внутри цикла прогрева, а не один раз до него.

Если увидишь что-то, чего в этом описании нет, — **не коммить**, сообщи мне.

- [ ] **Step 2: Проверить, что Python хотя бы разбирается**

```bash
python -c "import ast,io; ast.parse(io.open('tools/Blender/kimono_fit.py',encoding='utf-8').read()); print('SYNTAX_OK')"
python -c "import ast,io; ast.parse(io.open('tools/Blender/make_body.py',encoding='utf-8').read()); print('SYNTAX_OK')"
```

Ожидание: две строки `SYNTAX_OK`. Это дешёвый нижний гейт, а не проверка правильности — прогон пайплайна делает Task 2.

- [ ] **Step 3: Проверить, что C# компилируется**

Закрыть Unity Editor, если открыт (он держит project lock), затем:

```bash
unity test --mode EditMode --filter FighterModelTests --output test-results.xml
```

`timeout: 600000`. Ожидание: тесты запускаются, то есть проект собрался. Сами результаты тестов сейчас не показательны — модель ещё старая; важно только отсутствие ошибок компиляции.

Если компиляция падает — сообщи мне с текстом ошибки, не чини вслепую.

- [ ] **Step 4: Коммит**

```bash
git add tools/Blender/kimono_fit.py tools/Blender/make_body.py Assets/Editor/FighterImportSetup.cs Assets/Editor/FightSceneSwap.cs Assets/Editor/FightCapture.cs
git status --short
git diff --cached --stat
git commit -m "chore: зафиксирована работа прерванной волны правок, непроверенная

Агент внёс правки и был прерван до коммита. Содержимое: фильтр манекена
из kimono.glb по имени материала, пояс отдельным сабмешем со своим
материалом, проверки содержимого запечённых карт, снятие sRGB с AO,
и починка FightCapture — отражение воды рендерится внутри цикла
прогрева, иначе в воде навсегда застревает первый кадр с раздавленными
тенями.

Пайплайн после этих правок не запускался ни разу. Проверка — следующей
задачей, которая всё равно правит те же файлы."
```

Перед коммитом убедиться по `git status --short`, что в индексе ровно пять файлов и что `bridge_kit.py` там нет.

---

### Task 2: Тела Mixamo вместо генерации, без вырезания под тканью

Ядро плана. `kimono_fit.py` учится принимать готового персонажа, `strip_covered` уходит, пайплайн гоняется дважды.

**Files:**
- Modify: `tools/Blender/kimono_fit.py`
- Modify: `tools/Blender/build_kimono.ps1`

**Interfaces:**
- Consumes: `Assets/Fight/NewChar3d/Ch28_nonPBR.fbx`, `Assets/Fight/NewChar3d/Remy.fbx`, `Assets/Characters/Clothes/kimono.glb`.
- Produces: `Assets/Fight/character/KimonoFighter_Player.fbx` и `Assets/Fight/character/KimonoFighter_Enemy.fbx`. Обе модели содержат меши персонажа (тело, волосы, глаза, ресницы) и `Kimono_low` с двумя сабмешами — ткань, затем пояс. Функции `bone_by_suffix(arm, suffix)`, `normalize_height(arm, meshes) -> float`; `strip_covered` и `COVERED`/`KEEP` удалены.

- [ ] **Step 1: Заменить `import_body` и добавить две вспомогательные функции**

В `tools/Blender/kimono_fit.py` заменить существующую `import_body` (сейчас это строки 55–63) на:

```python
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
    for o in doomed:
        bpy.data.objects.remove(o, do_unlink=True)
    meshes = [o for o in meshes if o not in doomed]
    assert meshes, 'после снятия одежды не осталось ни одного меша'

    k = normalize_height(arm, meshes)
    print(f'kimono_fit: персонаж {os.path.basename(path)} — снято мешей одежды '
          f'{len(doomed)}, осталось {len(meshes)}, масштаб {k:.4f}')
    return arm, meshes
```

- [ ] **Step 2: Перевести `fit` на поиск по суффиксу**

В `tools/Blender/kimono_fit.py` функция `bone_head_z` и её вызовы в `fit` используют точные имена. Заменить `bone_head_z` на:

```python
def bone_head_z(arm, suffix):
    return (arm.matrix_world @ bone_by_suffix(arm, suffix).head).z
```

и в `fit` заменить два вызова так, чтобы они передавали суффикс, а не полное имя: `bone_head_z(arm, ':Neck')` и `bone_head_z(arm, ':LeftToeBase')`.

- [ ] **Step 3: Удалить `strip_covered`**

Удалить из `tools/Blender/kimono_fit.py` целиком: константы `COVERED` и `KEEP` вместе с их комментарием, и функцию `strip_covered`.

В `main()` удалить три строки — вызов и два assert'а:

```python
    removed = strip_covered(body)
    assert removed > 0, 'ни одна вершина тела не вырезана — имена костей не mixamorig?'
    assert tri_count(body) > 0, 'вырезано всё тело — COVERED слишком широк'
```

и убрать `вырезано {removed} вершин тела, ` из финального `print`.

Строка `body = max(body_meshes, key=tri_count)` **остаётся** — с этого меша `transfer_weights` берёт веса для кимоно.

- [ ] **Step 4: Сделать имя выходного файла параметром**

В `parse_args` добавить:

```python
    p.add_argument('--name', required=True,
                   help='имя выходного FBX без расширения, например KimonoFighter_Player')
```

В `main()` заменить строку сборки пути:

```python
    fbx = os.path.join(a.out, '..', a.name + '.fbx')
```

- [ ] **Step 5: Прогнать на первом персонаже**

```bash
"/c/Program Files/Blender Foundation/Blender 5.1/blender.exe" --background --factory-startup --python tools/Blender/kimono_fit.py -- --body Assets/Fight/NewChar3d/Ch28_nonPBR.fbx --kimono Assets/Characters/Clothes/kimono.glb --out Assets/Fight/character/kimono --name KimonoFighter_Player
```

`timeout: 600000`. Прогон занимает минуты — это Cycles-запек.

Ожидание: exit 0, и в выводе строка про снятые меши одежды (у Ch28 их три) и масштаб около 0.990.

Отдельно важно: прогон обязан пройти проверки `check_ao_content` и `check_normal_content`, добавленные в Task 1. До удаления манекена запек был негодным — у карты AO медиана засвеченных текселей 0.0196, то есть половина ткани чистый чёрный. Если эти проверки падают и после фильтра манекена, значит причина запека была не только в нём — сообщи мне с числами, не поднимай порог.

Assert'ы `fit()` могут упасть: кимоно теперь садится на другое тело. Ровно для этого есть ручки `--scale-mul` и `--offset-z` — подбери, впиши подобранное в `build_kimono.ps1` умолчанием, опиши в отчёте что и почему.

- [ ] **Step 6: Проверить главный признак — кимоно уже тела**

```bash
cat > /tmp/check_span.py <<'PY'
import bpy, mathutils, sys
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=sys.argv[-1], automatic_bone_orientation=True)
def span(o):
    cs=[o.matrix_world @ mathutils.Vector(c) for c in o.bound_box]
    return max(c.x for c in cs) - min(c.x for c in cs)
meshes={o.name: span(o) for o in bpy.data.objects if o.type=='MESH'}
for n,s in sorted(meshes.items()): print(f'SPAN {n:<16} {s:.3f}')
kim=[s for n,s in meshes.items() if 'Kimono' in n]
body=[s for n,s in meshes.items() if 'Kimono' not in n]
assert kim, 'в файле нет меша кимоно'
print('SPAN_CHECK', 'OK' if max(kim) < max(body) else 'FAIL')
PY
"/c/Program Files/Blender Foundation/Blender 5.1/blender.exe" --background --factory-startup --python /tmp/check_span.py -- Assets/Fight/character/KimonoFighter_Player.fbx 2>&1 | grep SPAN
```

Ожидание: `SPAN_CHECK OK`. Размах меша кимоно обязан быть **меньше** размаха тела — рукав не может быть шире руки, которую он покрывает. До починки манекена было наоборот, 1.066 против 1.051, и это само по себе было признаком дефекта.

Если `FAIL` — манекен всё ещё попадает в склейку; проверь, что `KIMONO_PARTS` действительно отфильтровал пятый меш.

- [ ] **Step 7: Прогнать на втором персонаже**

```bash
"/c/Program Files/Blender Foundation/Blender 5.1/blender.exe" --background --factory-startup --python tools/Blender/kimono_fit.py -- --body Assets/Fight/NewChar3d/Remy.fbx --kimono Assets/Characters/Clothes/kimono.glb --out Assets/Fight/character/kimono --name KimonoFighter_Enemy
```

Ожидание: exit 0, снято три меша одежды, масштаб около 0.462 — Remy приходит вдвое выше нужного. Повторить проверку из Step 6 для `KimonoFighter_Enemy.fbx`.

- [ ] **Step 8: Обновить обёртку на два прогона**

Заменить `tools/Blender/build_kimono.ps1` целиком:

```powershell
# Прогоняет Blender headless по разу на бойца и падает, если любой прогон упал.
# Карты кимоно у обоих одинаковы: рост нормализован к общему, геометрия ткани
# та же, поэтому второй прогон перезапишет их тем же содержимым.
$blender = "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe"
$root = Join-Path $PSScriptRoot "..\.."
$out = Join-Path $root "Assets\Fight\character\kimono"
$kimono = Join-Path $root "Assets\Characters\Clothes\kimono.glb"
$script = Join-Path $PSScriptRoot "kimono_fit.py"

$fighters = @(
    @{ Body = "Ch28_nonPBR.fbx"; Name = "KimonoFighter_Player" },
    @{ Body = "Remy.fbx";        Name = "KimonoFighter_Enemy"  }
)

foreach ($f in $fighters) {
    $body = Join-Path $root ("Assets\Fight\NewChar3d\" + $f.Body)
    & $blender --background --factory-startup --python $script -- `
        --body $body --kimono $kimono --out $out --name $f.Name @args
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
exit 0
```

- [ ] **Step 9: Прогнать обёртку целиком**

```bash
powershell -ExecutionPolicy Bypass -File tools/Blender/build_kimono.ps1
```

`timeout: 600000`. Ожидание: exit 0 и оба FBX на месте.

- [ ] **Step 10: Коммит**

```bash
git add tools/Blender/kimono_fit.py tools/Blender/build_kimono.ps1 Assets/Fight/character/KimonoFighter_Player.fbx Assets/Fight/character/KimonoFighter_Enemy.fbx Assets/Fight/character/kimono
git status --short
git diff --cached --stat
git commit -m "feat: кимоно на готовые тела Mixamo, вырезание под тканью убрано

Тело больше не генерируется, а приходит готовым: Ch28_nonPBR игроку,
Remy врагу. У обоих есть лицо, глаза, волосы и текстуры — безликие
головы закрываются без единой строки кода.

Три несовместимости сняты: своя одежда снимается целыми мешами (тело
под ней полное), рост нормализуется к 1.75 м (приходят 1.767 и 3.784),
кости ищутся по суффиксу имени (префикс различается между выгрузками —
mixamorig10: против mixamorig:).

strip_covered удалён, а не починен: именно он превращал ошибку в
размере кимоно в видимую дыру между рукавом и кистью."
git ls-files Assets/Fight/character
```

Ожидание: `git ls-files` показывает оба новых FBX.

---

### Task 3: Импорт двух бойцов в Unity и материалы

**Files:**
- Modify: `Assets/Editor/FighterImportSetup.cs`
- Modify: `Assets/Fight/Tests/FighterModelTests.cs`

**Interfaces:**
- Consumes: `Assets/Fight/character/KimonoFighter_Player.fbx`, `KimonoFighter_Enemy.fbx` из Task 2.
- Produces: обе модели импортированы как Humanoid с аватаром из самой модели; материалы `M_Player_Kimono`, `M_Enemy_Kimono`, `M_Player_Belt`, `M_Enemy_Belt` на `Character.shader`. Материал кожи больше не создаётся — тело приходит со своими материалами.

- [ ] **Step 1: Обновить тесты под две модели**

В `Assets/Fight/Tests/FighterModelTests.cs` класс `FighterModelTests` сейчас проверяет одну модель по константе `ModelPath`. Заменить константу на массив и прогнать все проверки по обеим моделям:

```csharp
        public const string PlayerModelPath = "Assets/Fight/character/KimonoFighter_Player.fbx";
        public const string EnemyModelPath = "Assets/Fight/character/KimonoFighter_Enemy.fbx";

        static readonly string[] Models = { PlayerModelPath, EnemyModelPath };

        [Test]
        public void Models_ImportAsHumanoid()
        {
            foreach (var path in Models)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.IsNotNull(importer, path + " is not in the project");
                Assert.AreEqual(ModelImporterAnimationType.Human, importer.animationType, path);
            }
        }

        [Test]
        public void Models_AvatarsAreValidHumans()
        {
            foreach (var path in Models)
            {
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
                Assert.IsNotNull(avatar, path + " carries no avatar");
                Assert.IsTrue(avatar.isValid, path + " avatar is invalid");
                Assert.IsTrue(avatar.isHuman, path + " avatar is not human");
            }
        }

        /// <summary>Both fighters are normalised to the same height in Blender because they fight
        /// each other — one arriving at 3.78 m and the other at 1.77 m would look absurd.</summary>
        [Test]
        public void Models_AreTheSameHumanHeight()
        {
            var heights = new System.Collections.Generic.List<float>();
            foreach (var path in Models)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(go, path + " is not in the project");
                var instance = Object.Instantiate(go);
                try
                {
                    var renderers = instance.GetComponentsInChildren<Renderer>();
                    Assert.IsNotEmpty(renderers, path + " has no renderers");
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers)
                        bounds.Encapsulate(r.bounds);
                    heights.Add(bounds.size.y);
                }
                finally
                {
                    Object.DestroyImmediate(instance);
                }
            }
            foreach (var h in heights)
                Assert.That(h, Is.InRange(1.6f, 1.95f), "fighter is " + h + " m tall");
            Assert.That(Mathf.Abs(heights[0] - heights[1]), Is.LessThan(0.1f),
                "fighters differ in height by " + Mathf.Abs(heights[0] - heights[1]) + " m");
        }

        /// <summary>The cloth mesh carries two submeshes — cloth then belt — so the belt can take
        /// its own colour instead of borrowing the rim, which is a silhouette effect and cannot
        /// represent a belt at all.</summary>
        [Test]
        public void Models_KimonoHasClothAndBeltSubmeshes()
        {
            foreach (var path in Models)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var kimono = go.GetComponentsInChildren<SkinnedMeshRenderer>()
                    .FirstOrDefault(s => s.name.Contains("Kimono"));
                Assert.IsNotNull(kimono, path + " has no Kimono mesh");
                Assert.AreEqual(2, kimono.sharedMesh.subMeshCount,
                    path + " kimono should be cloth + belt");
            }
        }
```

Удалить из этого класса старые `Model_ImportsAsHumanoid`, `Model_AvatarIsValidHuman`, `Model_IsHumanHeight`, `Model_HasSeparateBodyAndClothMeshes` и константу `ModelPath` — их заменяют четыре теста выше. Тесты `Materials_ExistOnTheCharacterShader`, `KimonoMaterials_CarryTheBakedMaps` и `NormalMap_IsImportedAsNormalMap` оставить, но в первом убрать `M_Fighter_Skin` из списка и добавить `M_Player_Belt` и `M_Enemy_Belt`.

- [ ] **Step 2: Прогнать тесты и убедиться, что падают**

Закрыть Unity Editor, если открыт.

```bash
unity test --mode EditMode --filter FighterModelTests --output test-results.xml
```

`timeout: 600000`. Ожидание: FAIL — моделей по новым путям в проекте ещё нет как Humanoid, материалов пояса нет.

- [ ] **Step 3: Обновить `FighterImportSetup`**

В `Assets/Editor/FighterImportSetup.cs`:

```csharp
        const string PlayerModelPath = CharacterDir + "/KimonoFighter_Player.fbx";
        const string EnemyModelPath = CharacterDir + "/KimonoFighter_Enemy.fbx";
```

Существующую константу `ModelPath` удалить. `SetUpModel()` принимает путь параметром и вызывается дважды:

```csharp
        static void SetUpModel(string modelPath)
        {
            var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
                throw new System.IO.FileNotFoundException(modelPath + " is not in the project");

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;   // clips come from the mocap files, not from here
            importer.SaveAndReimport();
        }
```

и в `Run()`:

```csharp
            SetUpModel(PlayerModelPath);
            SetUpModel(EnemyModelPath);
```

Создание `M_Fighter_Skin` удалить целиком — и сам вызов, и строки, задающие ему цвет и гладкость. Тело приходит с собственными материалами Mixamo и текстурами лица; переопределять их плоским цветом значило бы вернуть ровно ту безликость, из-за которой мы меняли источник тела.

Создание `M_Player_Kimono`, `M_Enemy_Kimono`, `M_Player_Belt`, `M_Enemy_Belt` и вызов `SetUpAoMap()` оставить как есть.

- [ ] **Step 4: Выполнить скрипт headless**

Через навык `unity-cli` выполнить `Mikey.FightEditor.FighterImportSetup.Run`. Ожидание: `FighterImportSetup: done` и exit 0.

- [ ] **Step 5: Прогнать тесты и убедиться, что проходят**

```bash
unity test --mode EditMode --filter FighterModelTests --output test-results.xml
```

Ожидание: PASS, семь тестов.

- [ ] **Step 6: Коммит**

```bash
git add Assets/Editor/FighterImportSetup.cs Assets/Fight/Tests/FighterModelTests.cs Assets/Fight/character
git status --short
git diff --cached --stat
git commit -m "feat: два бойца импортируются как Humanoid, материал кожи больше не нужен

Тела приходят с Mixamo со своими материалами и текстурами лица, поэтому
M_Fighter_Skin удалён: переопределять их плоским цветом было бы шагом
назад ровно к тому, из-за чего головы читались болванками.

Тест на одинаковый рост обоих бойцов закрепляет нормализацию: приходят
1.767 и 3.784 м."
```

---

### Task 4: Разные модели в сцене, кадры и полный прогон

**Files:**
- Modify: `Assets/Editor/FightSceneSwap.cs`
- Modify: `Assets/Scenes/FightSandbox.unity`

**Interfaces:**
- Consumes: обе модели и все четыре материала из Task 3.
- Produces: сцена, в которой у игрока и врага разные модели.

- [ ] **Step 1: Развести модели по бойцам**

В `Assets/Editor/FightSceneSwap.cs` сейчас грузится одна модель и один аватар на обоих бойцов. Развести по бойцам: модель и аватар выбираются тем же признаком `isPlayer`, каким уже выбирается материал кимоно.

```csharp
        const string PlayerModelPath = CharacterDir + "/KimonoFighter_Player.fbx";
        const string EnemyModelPath = CharacterDir + "/KimonoFighter_Enemy.fbx";
```

Загрузка модели и аватара переезжает внутрь цикла по бойцам, потому что теперь зависит от того, кто это:

```csharp
                var modelPath = isPlayer ? PlayerModelPath : EnemyModelPath;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (model == null)
                    throw new System.IO.FileNotFoundException(modelPath + " is not in the project");
                var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath)
                    .OfType<Avatar>().FirstOrDefault();
                if (avatar == null)
                    throw new System.InvalidOperationException(modelPath + " carries no avatar");
```

Материал кожи больше не назначается: из сигнатуры `Swap` убрать параметр `skin`, из её тела — соответствующий вызов `SetMaterial`, и из `Run` — загрузку `M_Fighter_Skin`.

После этого метод `SetMaterial` (единственного числа) может остаться без вызовов — проверь и удали, если так: он был обёрткой над `SetMaterials` ровно для кожи.

Назначение материалов кимоно и пояса через `SetMaterials(root, "Kimono_low", new[] { kimono, belt })` оставить как есть — оно уже написано и учитывает два сабмеша.

- [ ] **Step 2: Выполнить свап headless**

Через навык `unity-cli` выполнить `Mikey.FightEditor.FightSceneSwap.Run`, затем проверить по файлу сцены:

```bash
grep -c "$(sed -n 's/^guid: //p' Assets/Fight/character/KimonoFighter_Player.fbx.meta)" Assets/Scenes/FightSandbox.unity
grep -c "$(sed -n 's/^guid: //p' Assets/Fight/character/KimonoFighter_Enemy.fbx.meta)" Assets/Scenes/FightSandbox.unity
```

Ожидание: оба больше нуля. Обе модели должны присутствовать — если одна из них ноль, свап поставил одну модель обоим.

- [ ] **Step 3: Прогнать весь набор тестов**

```bash
unity test --mode EditMode --output test-results.xml
```

`timeout: 600000`. Ожидание: PASS по всем нашим наборам — `FighterModelTests`, `FighterClipsTests`, `FightSceneTests`, `FightRulesTests`.

Три теста в `Assets/Pose/Tests/StatCalculatorTests.cs` падают и будут падать: это незакоммиченная работа пользователя по удалению mae geri, она была грязной до начала работы. Не чинить, не трогать, назвать в отчёте.

- [ ] **Step 4: Снять два кадра**

Закрыть Unity Editor — `FightCapture` требует, чтобы редактор не держал project lock, и запускается **без** `-nographics`.

Общий кадр:

```bash
unity run . --format json --timeout 600 -- -executeMethod FightCapture.Shoot   -captureOut issues/mixamo_fighters.png -captureSize 1920x1080
```

Кадр с наложенной позой ёко гери — `FightSceneSwap.ShootKickPose` уже существует и делает это сам через `clip.SampleAnimation`:

```bash
unity run . --format json --timeout 600 -- -executeMethod Mikey.FightEditor.FightSceneSwap.ShootKickPose   -captureOut issues/mixamo_fighters_kick.png -captureSize 1920x1080
```

Если `Shoot` сохранит файл внутрь `Assets/`, перенести его оттуда: попав в `Assets/`, PNG становится ассетом проекта и Unity начнёт его импортировать.

Кадры смотрит координатор. Выносить по ним вердикт не надо; в отчёте только подтвердить, что оба сняты и лежат по этим путям.

- [ ] **Step 5: Коммит**

```bash
git add Assets/Editor/FightSceneSwap.cs Assets/Scenes/FightSandbox.unity
git status --short
git diff --cached --stat
git commit -m "feat: у игрока и врага разные персонажи в сцене

Бойцы читаются как разные люди, а не как один боец в двух поясах."
```

Кадры в `issues/` не коммитить.

---

## Что остаётся после плана

Снос `Ch15_nonPBR.fbx` с материалами, а также `make_body.py`, `build_body.ps1` и `check_body.py` — отдельным коммитом после того, как сцена отстоится на новых бойцах.

Три вопроса пользователя, не входящие в объём: `kimono.glb` не отслеживается git, поэтому пайплайн живёт только на этой машине; около 51 МБ неиспользуемых бинарников уехало в LFS; рантайм-слой `Assets/Fight/*.cs` вне git, из-за чего ветку не соберёт никто, кроме этой машины.

Плотность упаковки атласа — отдельная задача, к ней возвращаться после того, как запек перемерен на кимоно без манекена.
