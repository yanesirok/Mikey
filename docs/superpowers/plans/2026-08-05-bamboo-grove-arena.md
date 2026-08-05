# Bamboo Grove Arena Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить процедурную арену в FightSandbox готовой бамбуковой рощей из Blender по `Assets/Fight/NewArena/UNITY_HANDOFF (2).md` и спеке `docs/superpowers/specs/2026-08-05-bamboo-grove-arena-design.md`.

**Architecture:** Headless-Blender экспортирует GLB из .blend; пакет com.unity.cloud.gltfast импортирует его как ассет; идемпотентный editor-скрипт `NewArenaScene.cs` собирает сцену (материалы, свет, туман, фиксированная камера handoff, DoF, вода на существующем Water.shader) и выравнивает мост на пол бойцов y=0/z=0.

**Tech Stack:** Unity 6000.3.18f1, URP 17.3.0, Blender 5.1 (`C:\Program Files\Blender Foundation\Blender 5.1\blender.exe`), com.unity.cloud.gltfast.

## Global Constraints

- Все операции с редактором Unity (запуск методов, тесты, скриншоты) — через навык **unity-cli**, не сырой Unity.exe (память пользователя `use-unity-cli`).
- Цифры handoff копируются точно: key-свет X40/Y52 int 3.0 цвет (1.0, 0.78, 0.52); fill X18/Y−208 int 0.4 цвет (0.82, 0.87, 1.0); туман Exponential 0.0125; камера (−0.68, 2.60, −3.219)+дельта, поворот (5, 0.5, 0), FOV 45.7, клипы 0.05/500; DoF Bokeh фокус 6.18 апертура 2.8; cutoff листьев 0.35.
- `fern_02` — **Opaque** (листья геометрией; clip сломал бы early-Z на 36% треугольников). `M_ArenaLeafCard` — **Alpha Clip**, никогда blend.
- `bamboo_grove.blend` (190 МБ) не попадает ни в Assets, ни в git.
- Старая арена (`Assets/Fight/Arena/`, `BambooArena.cs`, `FightSceneSetup.RebuildArena`) не удаляется и не правится.
- Коммиты — после каждой задачи, сообщения в стиле репо (русские, `feat:`/`docs:`/`chore:`).

---

### Task 1: Переезд исходников из Assets + экспорт GLB

**Files:**
- Move: `Assets/Fight/NewArena/bamboo_grove.blend` → `tools/Blender/bamboo_grove.blend`
- Delete: `Assets/Fight/NewArena/unity.zip`, `Assets/Fight/NewArena/unity/` (после переноса png), их `.meta`
- Move: `Assets/Fight/NewArena/unity/unity/*.png` (5 шт.) → `Assets/Fight/NewArena/Textures/`
- Create: `tools/Blender/export_arena_glb.py`
- Modify: `.gitignore` (создать, если нет)
- Generate: `Assets/Fight/NewArena/BambooGrove.glb`

**Interfaces:**
- Produces: `Assets/Fight/NewArena/BambooGrove.glb` с объектами `Merged_fern_02`, `BambooLeaves`, `BambooWall`, `Merged_boulder_01`, `Terrain`, `Bridge`, `Water`, `Backdrop` (без `FogDomain`); текстуры `Assets/Fight/NewArena/Textures/{bamboo_bark,bamboo_leaf,fern,ground,rock}_albedo.png`.

- [ ] **Step 1: Перенести файлы**

```bash
mkdir -p tools/Blender Assets/Fight/NewArena/Textures
mv "Assets/Fight/NewArena/bamboo_grove.blend" tools/Blender/
mv Assets/Fight/NewArena/unity/unity/*.png Assets/Fight/NewArena/Textures/
rm -rf Assets/Fight/NewArena/unity "Assets/Fight/NewArena/unity.zip"
rm -f "Assets/Fight/NewArena/bamboo_grove.blend.meta" "Assets/Fight/NewArena/unity.zip.meta" "Assets/Fight/NewArena/unity.meta"
echo "tools/Blender/bamboo_grove.blend" >> .gitignore
```

- [ ] **Step 2: Написать экспорт-скрипт**

`tools/Blender/export_arena_glb.py`:

```python
"""Экспорт арены в GLB для Unity. Запуск:
& "C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe" -b tools/Blender/bamboo_grove.blend -P tools/Blender/export_arena_glb.py
"""
import bpy
import os

# FogDomain — Cycles-объём; в Unity он стал бы 130-метровым кубом (handoff §2).
fog = bpy.data.objects.get("FogDomain")
if fog is not None:
    bpy.data.objects.remove(fog, do_unlink=True)

out = os.path.normpath(os.path.join(
    os.path.dirname(bpy.data.filepath), "..", "..",
    "Assets", "Fight", "NewArena", "BambooGrove.glb"))

bpy.ops.export_scene.gltf(
    filepath=out,
    export_format='GLB',
    export_yup=True,               # Blender Z-up -> Unity Y-up (handoff §2)
    export_apply=True,
    export_texcoords=True,
    export_normals=True,
    export_materials='EXPORT',
    export_vertex_color='MATERIAL',  # пригодятся для ветра
    export_cameras=False,
    export_lights=False,
)
print("EXPORTED", out, os.path.getsize(out), "bytes")
for o in bpy.data.objects:
    print("OBJ", o.name)
```

- [ ] **Step 3: Запустить экспорт и проверить**

Run (PowerShell): `& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b tools/Blender/bamboo_grove.blend -P tools/Blender/export_arena_glb.py`
Expected: строка `EXPORTED ... bytes` (файл > 5 МБ), в списке OBJ есть Bridge/Water/BambooWall/Terrain, **нет** FogDomain. Если Blender 5.1 не открывает .blend — см. риск в спеке: поставить версию из заголовка файла.

- [ ] **Step 4: Commit**

```bash
git add -A "Assets/Fight/NewArena" tools/Blender/export_arena_glb.py .gitignore
git commit -m "chore: исходники новой арены — blend вне Assets, GLB-экспорт, запечённые альбедо"
```

(GLB коммитится: это теперь исходник арены для Unity.)

---

### Task 2: Референс-рендер из .blend (фоном)

**Files:**
- Create: `tools/Blender/render_reference.py`
- Generate: `docs/superpowers/specs/refs/2026-08-05-bamboo-grove-ref.png`

**Interfaces:**
- Produces: референс-кадр 1920×1080, с которым Task 6 сверяет сцену (композиция, туман, цвет горизонта).

- [ ] **Step 1: Написать рендер-скрипт**

`tools/Blender/render_reference.py`:

```python
"""Референс-кадр той же сцены: 128 сэмплов Cycles хватает для сравнения на глаз."""
import bpy
import os

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 128
scene.render.resolution_x = 1920
scene.render.resolution_y = 1080
scene.render.resolution_percentage = 100
scene.render.filepath = os.path.normpath(os.path.join(
    os.path.dirname(bpy.data.filepath), "..", "..",
    "docs", "superpowers", "specs", "refs", "2026-08-05-bamboo-grove-ref.png"))
bpy.ops.render.render(write_still=True)
print("RENDERED", scene.render.filepath)
```

- [ ] **Step 2: Запустить фоном** (не ждать: Cycles на CPU может идти десятки минут; продолжать Task 3, результат нужен только в Task 6)

Run (PowerShell, background): `& "C:\Program Files\Blender Foundation\Blender 5.1\blender.exe" -b tools/Blender/bamboo_grove.blend -P tools/Blender/render_reference.py`

- [ ] **Step 3: Commit (когда файл готов, можно вместе с Task 6)**

```bash
git add tools/Blender/render_reference.py docs/superpowers/specs/refs/2026-08-05-bamboo-grove-ref.png
git commit -m "docs: референс-кадр бамбуковой рощи из Blender"
```

---

### Task 3: Пакет gltfast + импорт GLB

**Files:**
- Modify: `Packages/manifest.json`
- Generate: `Packages/packages-lock.json` (редактор перепишет), `Assets/Fight/NewArena/BambooGrove.glb.meta`

**Interfaces:**
- Produces: GLB как импортированный ассет — `AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fight/NewArena/BambooGrove.glb")` возвращает префаб с иерархией объектов Blender; текстуры GLB — sub-assets.

- [ ] **Step 1: Узнать актуальную версию пакета**

Run: `curl -s https://packages.unity.com/com.unity.cloud.gltfast | python -c "import json,sys; print(json.load(sys.stdin)['dist-tags']['latest'])"` (или распарсить jq/строкой). Если сеть недоступна — взять `6.13.0`.

- [ ] **Step 2: Добавить в manifest**

В `Packages/manifest.json` в `"dependencies"` добавить строку (версию из Step 1):

```json
"com.unity.cloud.gltfast": "6.13.0",
```

- [ ] **Step 3: Дать редактору разрешить пакет и импортировать GLB**

Через **unity-cli**: убедиться, что редактор Mikey запущен/перезапущен и проект скомпилировался. Проверить: в `Packages/packages-lock.json` появился `com.unity.cloud.gltfast`; `Assets/Fight/NewArena/BambooGrove.glb.meta` существует; в консоли нет ошибок импорта.

- [ ] **Step 4: Проверить иерархию импорта**

Через unity-cli выполнить в редакторе C#:

```csharp
var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Fight/NewArena/BambooGrove.glb");
foreach (var t in go.GetComponentsInChildren<Transform>(true))
    UnityEngine.Debug.Log($"[glb] {t.name}");
```

Expected: в логе Bridge, Water, Terrain, BambooWall, BambooLeaves, Merged_fern_02, Merged_boulder_01, Backdrop.

- [ ] **Step 5: Commit**

```bash
git add Packages/manifest.json Packages/packages-lock.json "Assets/Fight/NewArena/BambooGrove.glb.meta"
git commit -m "feat: gltfast — GLB рощи импортируется как ассет"
```

---

### Task 4: NewArenaScene.EnsureMaterials — материалы по таблице handoff

**Files:**
- Create: `Assets/Editor/NewArenaScene.cs` (в этой задаче — константы + материалы; Build добавит Task 5)
- Generate: `Assets/Fight/NewArena/M_Grove{Fern,LeafCard,Bamboo,Ground,Rock}.mat`

**Interfaces:**
- Produces: `internal static Dictionary<string, Material> NewArenaScene.EnsureMaterials()` — ключ = имя материала в GLB (`fern_02`, `M_ArenaLeafCard`, `M_ArenaBamboo`, `Bank`, `boulder_01`), значение = наш URP/Lit-ассет с запечённым альбедо. `weathered_planks` и `BackdropMat` в словаре **нет** — им остаются материалы gltfast (их текстуры не запекались и приезжают в GLB).
- Consumes: текстуры `Assets/Fight/NewArena/Textures/*_albedo.png` из Task 1.

- [ ] **Step 1: Создать файл со скелетом и материалами**

`Assets/Editor/NewArenaScene.cs`:

```csharp
using System.Collections.Generic;
using Mikey.Fight;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Сборка боевой сцены на арене «бамбуковая роща» из Blender — спека 2026-08-05, цифры из
/// Assets/Fight/NewArena/UNITY_HANDOFF (2).md. Идемпотентна: повторный запуск пересоздаёт
/// всё, чем владеет, и не плодит дублей. Старый билдер (FightSceneSetup.RebuildArena) не
/// трогается — это его замена на новую сцену, не правка.
/// </summary>
public static class NewArenaScene
{
    private const string ScenePath = "Assets/Scenes/FightSandbox.unity";
    private const string GlbPath = "Assets/Fight/NewArena/BambooGrove.glb";
    private const string TexDir = "Assets/Fight/NewArena/Textures";
    private const string MatDir = "Assets/Fight/NewArena";

    /// <summary>Материалы по таблице handoff §4. Ключ — имя материала в GLB. Планки моста и
    /// задник в словаре отсутствуют намеренно: их текстуры не запекались и живут в GLB,
    /// материалы gltfast для них правильные как есть.</summary>
    internal static Dictionary<string, Material> EnsureMaterials()
    {
        var map = new Dictionary<string, Material>
        {
            // Ферн Opaque принципиально: листья геометрией, альфа 99.8% непрозрачна,
            // clip сломал бы early-Z на 36% треугольников сцены (handoff §4).
            ["fern_02"] = LitMaterial("M_GroveFern", "fern_albedo", clip: false),
            ["M_ArenaLeafCard"] = LitMaterial("M_GroveLeafCard", "bamboo_leaf_albedo", clip: true),
            ["M_ArenaBamboo"] = LitMaterial("M_GroveBamboo", "bamboo_bark_albedo", clip: false),
            ["Bank"] = LitMaterial("M_GroveGround", "ground_albedo", clip: false),
            ["boulder_01"] = LitMaterial("M_GroveRock", "rock_albedo", clip: false),
        };
        AssetDatabase.SaveAssets();
        return map;
    }

    private static Material LitMaterial(string name, string albedo, bool clip)
    {
        string path = $"{MatDir}/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{albedo}.png");
        if (tex == null)
            Debug.LogError($"NewArenaScene: нет текстуры {TexDir}/{albedo}.png");
        mat.SetTexture("_BaseMap", tex);
        // Roughness из glTF в URP/Lit не переложить (другая упаковка каналов); листва и
        // камень матовые, плоского значения достаточно.
        mat.SetFloat("_Smoothness", 0.25f);
        mat.SetFloat("_AlphaClip", clip ? 1f : 0f);
        if (clip)
        {
            // Клип, не blend: blend не пишет глубину — ломает сортировку карт листьев и
            // теневой проход (handoff §4).
            mat.SetFloat("_Cutoff", 0.35f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            mat.SetFloat("_Cull", (float)CullMode.Off); // карты видны с обеих сторон
        }
        else
        {
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = -1;
        }
        return mat;
    }
}
```

- [ ] **Step 2: Скомпилировать и выполнить**

Через unity-cli: дождаться компиляции без ошибок, выполнить в редакторе `NewArenaScene.EnsureMaterials()` и залогировать `map.Count`.
Expected: 5; в `Assets/Fight/NewArena/` появились 5 .mat; у M_GroveLeafCard в инспекторе Alpha Clipping on, Threshold 0.35.

- [ ] **Step 3: Commit**

```bash
git add Assets/Editor/NewArenaScene.cs Assets/Fight/NewArena/*.mat Assets/Fight/NewArena/*.mat.meta Assets/Fight/NewArena/Textures.meta Assets/Fight/NewArena/Textures
git commit -m "feat: материалы рощи — URP/Lit c запечёнными альбедо, ферн Opaque, листья clip 0.35"
```

---

### Task 5: NewArenaScene.Build — сборка сцены

**Files:**
- Modify: `Assets/Editor/NewArenaScene.cs` (добавить Build и всё ниже)
- Modify (скриптом): `Assets/Scenes/FightSandbox.unity`
- Generate: `Assets/Fight/NewArena/GrovePost.asset` (VolumeProfile с DoF)

**Interfaces:**
- Consumes: `EnsureMaterials()` из Task 4; префаб GLB из Task 3; `Assets/Fight/Arena/M_ArenaWater.mat` + компонент `Mikey.Fight.WaterReflection` (существующие); константы `Mikey.Fight.FightRules`.
- Produces: пункт меню **Mikey/Build Bamboo Grove** (и метод `NewArenaScene.Build()` для -executeMethod); в сцене корень `Arena` с дочерним `Timber` (BoxCollider, верх на y=0) — путь `Arena/Timber` рейкастит диагностика FightBootstrap.

- [ ] **Step 1: Дописать Build и помощников в NewArenaScene.cs**

Добавить `using UnityEditor.SceneManagement; using UnityEngine.SceneManagement; using UnityEngine.Rendering.Universal;` и следующие члены класса:

```csharp
    // Камера из handoff §7. К позиции прибавляется дельта выравнивания арены — композиция
    // кадра не меняется, но пол боя ложится на y=0/z=0 из FightRules.
    private static readonly Vector3 HandoffCamPos = new Vector3(-0.68f, 2.60f, -3.219f);
    private static readonly Vector3 HandoffCamEuler = new Vector3(5.0f, 0.5f, 0f);
    private const float HandoffFov = 45.7f; // вертикальный

    // Тёплый бледно-кремовый горизонт (handoff §5.1): туман, фон камеры и амбиент — одна
    // семья. Значение стартовое, сверяется с референс-рендером в конце.
    private static readonly Color FogColor = new Color(0.93f, 0.88f, 0.78f);
    private const float FogDensity = 0.0125f; // 12% на 10 м, 27% на 25 м, 43% на 45 м

    [MenuItem("Mikey/Build Bamboo Grove")]
    public static void Build()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath);

        // Тот же список владения, что у FightSceneSetup.RebuildArena: бойцы, контроллер и
        // тач-управление не трогаются, окружение пересоздаётся целиком.
        var owned = new HashSet<string>
        {
            "Arena", "Water", "Terrain", "Grass", "FX_Mist", "FX_Leaves", "FX_Motes",
            "FX_Drips", "FX_Steam", "FX_Embers", "PostProcessing",
        };
        foreach (GameObject root in scene.GetRootGameObjects())
            if (owned.Contains(root.name))
                Object.DestroyImmediate(root);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
        if (prefab == null)
        {
            Debug.LogError($"NewArenaScene: нет {GlbPath} — сначала экспорт из Blender.");
            return;
        }
        var arena = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        arena.name = "Arena"; // связь с GLB остаётся: переэкспорт из Blender подтянется сам

        Vector3 shift = AlignToFightLine(arena);
        ApplyMaterialsAndLayers(arena);
        AddDeckCollider(arena);
        SetupWater(arena);
        SetupLights();
        SetupAtmosphere();
        SetupCamera(shift);
        CreatePostVolume();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"NewArenaScene: роща собрана, сдвиг арены {shift}.");
    }

    /// <summary>Верх настила — на y=0 (пол бойцов), середина пролёта — на x=0, ось боя — на
    /// z=0. Возвращает применённый сдвиг: на него же смещается камера.</summary>
    private static Vector3 AlignToFightLine(GameObject arena)
    {
        Transform bridge = FindDeep(arena.transform, "Bridge");
        if (bridge == null)
        {
            Debug.LogError("NewArenaScene: в GLB нет объекта Bridge — выравнивание пропущено.");
            return Vector3.zero;
        }

        var renderers = bridge.GetComponentsInChildren<MeshRenderer>();
        Bounds b = renderers[0].bounds;
        foreach (MeshRenderer r in renderers)
            b.Encapsulate(r.bounds);

        // Верх настила — рейкаст в середину моста, а не bounds.max.y: максимум AABB — перила.
        float deckY = b.center.y;
        var mf = bridge.GetComponentInChildren<MeshFilter>();
        var temp = bridge.gameObject.AddComponent<MeshCollider>();
        temp.sharedMesh = mf.sharedMesh;
        var ray = new Ray(new Vector3(b.center.x, b.max.y + 1f, b.center.z), Vector3.down);
        if (temp.Raycast(ray, out RaycastHit hit, b.size.y + 2f))
            deckY = hit.point.y;
        else
            Debug.LogWarning("NewArenaScene: рейкаст в настил промахнулся, беру центр bounds.");
        Object.DestroyImmediate(temp);

        var shift = new Vector3(-b.center.x, -deckY, -b.center.z);
        arena.transform.position += shift;
        return shift;
    }

    /// <summary>Наши материалы взамен gltfast-овских (по имени), нормали переносятся из
    /// заменяемого материала. Слои для отражения воды: мост — Reflected, роща — ReflectedFar
    /// (см. WaterReflection: near-слой рендерится всегда, far отключается на мобильном тире).</summary>
    private static void ApplyMaterialsAndLayers(GameObject arena)
    {
        Dictionary<string, Material> mats = EnsureMaterials();

        foreach (MeshRenderer r in arena.GetComponentsInChildren<MeshRenderer>())
        {
            Material[] shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                if (shared[i] == null || !mats.TryGetValue(shared[i].name, out Material own))
                    continue;
                Texture normal = NormalOf(shared[i]);
                if (normal != null)
                {
                    own.SetTexture("_BumpMap", normal);
                    own.EnableKeyword("_NORMALMAP");
                }
                shared[i] = own;
            }
            r.sharedMaterials = shared;
        }

        int near = LayerMask.NameToLayer(Mikey.Fight.WaterReflection.LayerName);
        int far = LayerMask.NameToLayer(Mikey.Fight.WaterReflection.FarLayerName);
        if (near < 0 || far < 0)
        {
            Debug.LogWarning("NewArenaScene: нет слоёв Reflected/ReflectedFar — вода без отражения.");
            return;
        }
        foreach (Transform t in arena.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = far;
        Transform bridge = FindDeep(arena.transform, "Bridge");
        if (bridge != null)
            foreach (Transform t in bridge.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = near;
    }

    /// <summary>Нормали не запекались и приезжают в GLB — снимаем с материала, который
    /// заменяем. Имя свойства зависит от шейдера gltfast, поэтому перебор.</summary>
    private static Texture NormalOf(Material m)
    {
        foreach (string prop in new[] { "_BumpMap", "normalTexture", "_NormalTexture" })
            if (m.HasProperty(prop) && m.GetTexture(prop) != null)
                return m.GetTexture(prop);
        return null;
    }

    /// <summary>BoxCollider по настилу на объекте Arena/Timber — именно этот путь рейкастит
    /// диагностика FightBootstrap.LogDeckProfile.</summary>
    private static void AddDeckCollider(GameObject arena)
    {
        Transform bridge = FindDeep(arena.transform, "Bridge");
        if (bridge == null)
            return;
        var renderers = bridge.GetComponentsInChildren<MeshRenderer>();
        Bounds b = renderers[0].bounds;
        foreach (MeshRenderer r in renderers)
            b.Encapsulate(r.bounds);

        var timber = new GameObject("Timber");
        timber.transform.SetParent(arena.transform);
        timber.transform.position = Vector3.zero; // мировой ноль: настил уже выровнен на y=0
        var box = timber.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, -0.05f, 0f); // верх коробки — ровно y=0
        box.size = new Vector3(b.size.x, 0.1f, b.size.z);
    }

    /// <summary>Вода из GLB получает боевой водный шейдер с планарным отражением — «better»-
    /// вариант из handoff §5.4, уже написанный для старой арены.</summary>
    private static void SetupWater(GameObject arena)
    {
        Transform water = FindDeep(arena.transform, "Water");
        if (water == null)
        {
            Debug.LogWarning("NewArenaScene: в GLB нет Water.");
            return;
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Fight/Arena/M_ArenaWater.mat");
        // Рендерер может висеть на дочернем узле (glTF: node -> mesh-объект) — ищем вглубь,
        // и WaterReflection ставим туда же: ему нужен MeshRenderer на своём объекте.
        MeshRenderer mr = water.GetComponentInChildren<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.gameObject.layer = 0; // вода — зеркало, в отражённые слои не входит
        if (mr.GetComponent<Mikey.Fight.WaterReflection>() == null)
            mr.gameObject.AddComponent<Mikey.Fight.WaterReflection>();
    }

    private static void SetupLights()
    {
        // 40° элевации — то, что кладёт тени листвы в кадр (handoff §5.2).
        Light key = EnsureLight("Directional Light");
        key.transform.rotation = Quaternion.Euler(40f, 52f, 0f);
        key.color = new Color(1.0f, 0.78f, 0.52f); // тёплые ~3200 K
        key.intensity = 3.0f;
        key.shadows = LightShadows.Soft;

        // Без fill мост — силуэт: он поднимает поверхности, обращённые к камере.
        Light fill = EnsureLight("FillLight");
        fill.transform.rotation = Quaternion.Euler(18f, -208f, 0f);
        fill.color = new Color(0.82f, 0.87f, 1.0f);
        fill.intensity = 0.4f;
        fill.shadows = LightShadows.None;
    }

    private static Light EnsureLight(string name)
    {
        GameObject go = GameObject.Find(name);
        if (go == null)
            go = new GameObject(name);
        Light light = go.GetComponent<Light>();
        if (light == null)
            light = go.AddComponent<Light>();
        light.type = LightType.Directional;
        return light;
    }

    private static void SetupAtmosphere()
    {
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = FogDensity;
        RenderSettings.fogColor = FogColor;
        // Не чёрный: иначе затенённая роща проваливается в сплошную темноту. ~20:1 к key.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = FogColor * 0.15f;
    }

    private static void SetupCamera(Vector3 shift)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("NewArenaScene: нет камеры с тегом MainCamera.");
            return;
        }

        // Кадр фиксированный по handoff §7 — слежение уходит со сцены. Код FightCamera
        // остаётся: вернём отдельной итерацией, если статичный кадр не устроит.
        FightCamera follow = cam.GetComponent<FightCamera>();
        if (follow != null)
            Object.DestroyImmediate(follow);

        cam.transform.SetPositionAndRotation(HandoffCamPos + shift, Quaternion.Euler(HandoffCamEuler));
        cam.fieldOfView = HandoffFov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 500f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = FogColor;

        UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        data.renderPostProcessing = true;
    }

    private static void CreatePostVolume()
    {
        var go = new GameObject("PostProcessing");
        Volume volume = go.AddComponent<Volume>();
        volume.isGlobal = true;

        const string path = "Assets/Fight/NewArena/GrovePost.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet(out DepthOfField dof))
            dof = profile.Add<DepthOfField>(true);
        dof.mode.Override(DepthOfFieldMode.Bokeh);
        dof.focusDistance.Override(6.18f); // мост; f/2.8 из handoff §7
        dof.aperture.Override(2.8f);
        dof.focalLength.Override(24f);
        EditorUtility.SetDirty(profile);
        volume.sharedProfile = profile;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        Transform exact = null;
        Transform prefix = null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
                exact ??= t;
            else if (t.name.StartsWith(name))
                prefix ??= t; // glTF-импорт любит суффиксы к именам узлов
        }
        return exact != null ? exact : prefix;
    }
```

- [ ] **Step 2: Скомпилировать и запустить сборку**

Через unity-cli: дождаться компиляции, выполнить `NewArenaScene.Build()`.
Expected в консоли: `NewArenaScene: роща собрана, сдвиг арены (...)`, без ошибок. Допустимые предупреждения — про промах рейкаста (тогда проверить выравнивание глазами на скриншоте).

Затем выполнить `NewArenaScene.Build()` **второй раз** — идемпотентность из спеки §6: в сцене по-прежнему один корень `Arena`, один `PostProcessing`, один `FillLight` (проверить логом количество объектов — см. Step 3).

- [ ] **Step 3: Проверить структуру сцены**

Через unity-cli выполнить:

```csharp
var timber = GameObject.Find("Arena/Timber");
var cam = Camera.main;
Debug.Log($"[check] timber={(timber != null && timber.GetComponent<Collider>() != null)} " +
          $"cam={cam.transform.position} fov={cam.fieldOfView} " +
          $"fightCam={(cam.GetComponent<Mikey.Fight.FightCamera>() == null ? "снята" : "ОСТАЛАСЬ")} " +
          $"fog={RenderSettings.fogMode}/{RenderSettings.fogDensity}");
```

Expected: timber=True, fov=45.7, fightCam=снята, fog=Exponential/0.0125.

- [ ] **Step 4: Скриншот**

Через unity-cli выполнить меню `Mikey/Capture Fight Screenshot` (FightCapture), посмотреть картинку: мост в нижней части кадра, бойцы стоят на настиле (не тонут, не парят), роща в тумане, вода с отражением. Кривые места записать — правки в Task 6.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/NewArenaScene.cs Assets/Fight/NewArena/GrovePost.asset Assets/Fight/NewArena/GrovePost.asset.meta Assets/Scenes/FightSandbox.unity
git commit -m "feat: сцена бамбуковой рощи — GLB, свет и туман по handoff, фиксированная камера, DoF"
```

---

### Task 6: Сверка с референсом, тесты, доводка

**Files:**
- Modify: `Assets/Editor/NewArenaScene.cs` (только константы FogColor/ambient по референсу, если разошлись)
- Modify (скриптом): `Assets/Scenes/FightSandbox.unity`

**Interfaces:**
- Consumes: референс из Task 2, скриншот из Task 5.

- [ ] **Step 1: Дождаться референс-рендера** (Task 2, фоновый процесс) и открыть оба кадра рядом.

- [ ] **Step 2: Сверить и поправить цвета**

Сравнить: цвет горизонта/тумана, общая теплота, плотность дымки на дальних планах. Если FogColor разошёлся — снять цвет пипеткой с горизонта референса, поправить константу `FogColor`, перезапустить `NewArenaScene.Build()` через unity-cli, снять новый скриншот.

- [ ] **Step 3: Прогнать существующие тесты**

Через unity-cli запустить edit-mode тесты проекта (Mikey.Fight.Tests).
Expected: все зелёные — билдер не трогает ни FightRules, ни рантайм-код, падения быть не должно.

- [ ] **Step 4: Короткий прогон боя**

Через unity-cli войти в Play Mode в FightSandbox на ~10 секунд, выйти, прочитать лог.
Expected: `[diag deck] N samples...` с ненулевым N (коллайдер Timber виден рейкасту), `[diag cam] fov=45.7`, бойцы двигаются (diag-строки с меняющимся x), ошибок нет.

- [ ] **Step 5: Финальный commit**

```bash
git add -A Assets/Editor/NewArenaScene.cs Assets/Scenes/FightSandbox.unity docs/superpowers/specs/refs/2026-08-05-bamboo-grove-ref.png tools/Blender/render_reference.py
git commit -m "feat: доводка рощи по референсу — туман и амбиент сведены с Blender-кадром"
```

---

## Заметки для исполнителя

- Ферны/листья/камни делят один UV-атлас — их альбедо только запечённые, из `Textures/`, не из GLB.
- Если Water.shader на новом меше воды выглядит сломанным (он рассчитан на вершинные данные старого билдера — пена/берега), допустимый откат в рамках плана: URP/Lit, Smoothness 0.95, базовый цвет (0.004, 0.011, 0.008) — «simple»-вариант handoff §5.4. Отражение тогда уйдёт; зафиксировать как известный долг.
- Новый мост наверняка не повторяет кривую `FightRules.DeckHeight` (провис 0.18). Ноги бойцов у краёв могут отходить от досок на сантиметры — это принятый долг первой фазы (спека, «интеграция боя — минимум»); лечится позже подгонкой BridgeSag под новую геометрию.
- Пан камеры не нужен: кадр FOV 45.7 на глубине моста покрывает ±4.6 м, бойцы ходят в ±3 (ArenaHalfWidth).
