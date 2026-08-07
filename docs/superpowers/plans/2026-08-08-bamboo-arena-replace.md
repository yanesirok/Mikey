# Замена арены на BambooArena — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Полностью заменить арену боя в FightSandbox на присланную сцену BambooArena из `Assets/Fight/NewArena/BambooArena_scene.zip`, сохранив геймплей и контракт `Arena/Timber`.

**Architecture:** Сцена из архива становится FightSandbox: распаковываем `Assets/BambooArena/**`, содержимое их сцены пишем поверх `FightSandbox.unity` (GUID наш), геймплей переносим одноразовым Editor-скриптом, окружение собираем под корень `Arena` с настилом на y=0. Старые арены и их генераторы удаляем.

**Tech Stack:** Unity 6000.3.18f1, URP, unity-cli (сборки/тесты/eval — только через навык unity-cli), gltfast (уже в проекте), git.

**Spec:** `docs/superpowers/specs/2026-08-08-bamboo-arena-replace-design.md`

## Global Constraints

- Проект — Unity **6000.3.18f1**; архив собран в **6000.5.7f1**. Гейт после импорта: ошибки по 13 мешам-.asset или сцене → **СТОП**, доложить пользователю (вопрос апгрейда), дальше не идти.
- **Не трогать**: `Assets/Scenes/SampleScene.unity` (App-сцена, тот же GUID у архивной сцены!), `Assets/Settings/**`, `ProjectSettings/**`. Из архива их **не распаковывать**.
- Рантайм-контракт: `GameObject.Find("Arena/Timber")` (FightBootstrap.cs:77, FootIK.cs:89) — корень сцены `Arena`, прямой ребёнок `Timber` с BoxCollider, **верх коробки ровно y=0**. Бой идёт вдоль X на линии z=0, `Mikey.Fight.FightRules.ArenaHalfWidth = 3` — настил обязан покрывать x ∈ [−3, 3].
- Все Editor-методы, вызываемые через unity-cli eval, — **public** (eval не видит internal).
- Работа с Unity только через навык **unity-cli** (не сырой Unity.exe).
- Коммиты — как в репо: `feat:`/`chore:`/`fix:` + русское описание. В конце каждого коммита: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- Это ассет-таск: вместо юнит-тестов у каждого таска свой проверяемый гейт (лог импорта, компиляция, лог диагностики, скриншот). TDD-цикл «тест → красный → зелёный» здесь заменяется «гейт до → действие → гейт после».

---

### Task 1: Распаковка архива и импорт ассетов

**Files:**
- Create: `Assets/BambooArena/**` (из архива, со всеми .meta), `Assets/BambooArena/HANDOFF.md`, `Assets/BambooArena/BambooArenaProfile.asset` (+ .meta с новым GUID), `Assets/Scenes/FightSandboxOld.unity` (+ .meta, временная копия)
- Modify: `Assets/Scenes/FightSandbox.unity` (перезапись содержимым архивной сцены + перевязка волюм-профиля)
- Delete: `Assets/Fight/NewArena/BambooArena_scene.zip` (+ `.meta`, если Unity успел создать)

**Interfaces:**
- Produces: сцена `Assets/Scenes/FightSandbox.unity` = арена из архива (корни: `Water Reflection Probe`, `Main Camera`, `Key Sun`, `Global Volume`, `WaterReflection`, `Fill Light`, префаб-инстанс `BambooArena`); копия старого геймплея в `Assets/Scenes/FightSandboxOld.unity` (корни: `Main Camera`, `Directional Light`, `TouchControls`, `FillLight`, `PostProcessing`, `FightRound`, `Player`, `Enemy`, `Arena`). Task 2 читает обе.

- [ ] **Step 1: Убедиться, что редактор не держит FightSandbox**

Загрузить навык unity-cli. Если Unity Editor запущен с проектом и активная сцена — FightSandbox, открыть через unity-cli другую сцену (например `Assets/Scenes/PoseReview.unity`), чтобы перезапись файла сцены не конфликтовала. Если редактор не запущен — ничего не делать (запустим на Step 6).

- [ ] **Step 2: Сохранить копию старой сцены**

```bash
cd /c/Users/user/Mikey
cp Assets/Scenes/FightSandbox.unity Assets/Scenes/FightSandboxOld.unity
OLDGUID=$(python -c "import uuid; print(uuid.uuid4().hex)")
cat > Assets/Scenes/FightSandboxOld.unity.meta <<EOF
fileFormatVersion: 2
guid: $OLDGUID
DefaultImporter:
  externalObjects: {}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
EOF
```

- [ ] **Step 3: Распаковать ассеты арены и сцену**

```bash
cd /c/Users/user/Mikey
unzip -o Assets/Fight/NewArena/BambooArena_scene.zip "Assets/BambooArena/*" -d .
unzip -p Assets/Fight/NewArena/BambooArena_scene.zip HANDOFF.md > Assets/BambooArena/HANDOFF.md
# Сцена архива → поверх FightSandbox.unity; наш FightSandbox.unity.meta НЕ трогаем (GUID остаётся)
unzip -p Assets/Fight/NewArena/BambooArena_scene.zip Assets/Scenes/SampleScene.unity > Assets/Scenes/FightSandbox.unity
```

Проверка: `ls Assets/BambooArena` → папки Materials, Meshes, Models, Scripts, Shaders, Textures + HANDOFF.md. `Assets/Settings` и `Assets/Scenes/SampleScene.unity` не изменились (`git status -- Assets/Settings Assets/Scenes/SampleScene.unity` пуст).

- [ ] **Step 4: Волюм-профиль арены под новым GUID**

Архивный профиль несёт GUID нашего `SampleSceneProfile.asset` (используется App-сценой) — извлекаем под новым именем и GUID, перевязываем сцену:

```bash
cd /c/Users/user/Mikey
unzip -p Assets/Fight/NewArena/BambooArena_scene.zip Assets/Settings/SampleSceneProfile.asset > Assets/BambooArena/BambooArenaProfile.asset
PROFGUID=$(python -c "import uuid; print(uuid.uuid4().hex)")
cat > Assets/BambooArena/BambooArenaProfile.asset.meta <<EOF
fileFormatVersion: 2
guid: $PROFGUID
NativeFormatImporter:
  externalObjects: {}
  mainObjectFileID: 11400000
  userData: 
  assetBundleName: 
  assetBundleVariant: 
EOF
# Перевязка Global Volume новой сцены (ровно одно вхождение старого GUID)
grep -c 10fc4df2da32a41aaa32d77bc913491c Assets/Scenes/FightSandbox.unity   # ожидается: 1
sed -i "s/10fc4df2da32a41aaa32d77bc913491c/$PROFGUID/" Assets/Scenes/FightSandbox.unity
```

Сверить шапку meta с `Assets/Settings/SampleSceneProfile.asset.meta` — структура должна совпадать (кроме guid).

- [ ] **Step 5: Удалить zip**

```bash
rm Assets/Fight/NewArena/BambooArena_scene.zip
rm -f Assets/Fight/NewArena/BambooArena_scene.zip.meta
```

- [ ] **Step 6: Импорт и гейт версии**

Через unity-cli: запустить/подключиться к редактору, дождаться рефреша/импорта, прочитать лог редактора. Критерий провала: ошибки импорта по `Assets/BambooArena/Meshes/*.asset` (13 мешей), `Assets/BambooArena/Models/BambooArena.glb`, `Assets/Scenes/FightSandbox.unity`, ошибки компиляции `PlanarReflection.cs` или `RiverWater.shader`. Ворнинги типа KHR_materials_specular — допустимы.

**Если ошибки импорта мешей/сцены есть — СТОП: не коммитить, доложить пользователю (вопрос апгрейда до 6000.5.7f1).**

- [ ] **Step 7: Commit**

```bash
git add -A Assets/BambooArena Assets/Scenes/FightSandbox.unity Assets/Scenes/FightSandboxOld.unity Assets/Scenes/FightSandboxOld.unity.meta Assets/Fight/NewArena
git commit -m "feat: ассеты и сцена BambooArena из архива — профиль волюма отвязан от App-сцены

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Сборка FightSandbox — геймплей в новую арену

**Files:**
- Create: `Assets/Editor/ArenaSceneAssembly.cs` (одноразовый, удаляется в Task 3)
- Modify: `Assets/Scenes/FightSandbox.unity` (через редактор)
- Delete: `Assets/Scenes/FightSandboxOld.unity` (+ .meta, внутри скрипта через AssetDatabase)

**Interfaces:**
- Consumes: обе сцены из Task 1; `Mikey.Fight.FightRules.ArenaHalfWidth` (const float 3); `PlanarReflection` (глобальный неймспейс, поле `public float waterLevel`).
- Produces: `FightSandbox.unity` с корнями `Arena` (риг: BambooArena, WaterReflection, Water Reflection Probe, Key Sun, Fill Light, Main Camera, Timber), `Global Volume`, `TouchControls`, `FightRound`, `Player`, `Enemy`. Контракт `Arena/Timber` восстановлен.

- [ ] **Step 1: Написать скрипт сборки**

Создать `Assets/Editor/ArenaSceneAssembly.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mikey.EditorTools
{
    /// <summary>
    /// Одноразовая сборка FightSandbox из присланной сцены BambooArena: переносит геймплей из
    /// копии старой сцены, собирает окружение под корень "Arena", кладёт настил моста на y=0 и
    /// восстанавливает контракт Arena/Timber (FightBootstrap и FootIK рейкастят его коллайдер).
    /// public, потому что вызывается через unity-cli eval.
    /// </summary>
    public static class ArenaSceneAssembly
    {
        private const string NewScenePath = "Assets/Scenes/FightSandbox.unity";
        private const string OldScenePath = "Assets/Scenes/FightSandboxOld.unity";

        private static readonly string[] GameplayRoots = { "TouchControls", "FightRound", "Player", "Enemy" };
        private static readonly string[] ArenaRoots =
            { "BambooArena", "WaterReflection", "Water Reflection Probe", "Key Sun", "Fill Light", "Main Camera" };

        public static void Assemble()
        {
            Scene arena = EditorSceneManager.OpenScene(NewScenePath, OpenSceneMode.Single);
            Scene old = EditorSceneManager.OpenScene(OldScenePath, OpenSceneMode.Additive);

            foreach (string name in GameplayRoots)
            {
                GameObject go = old.GetRootGameObjects().FirstOrDefault(g => g.name == name);
                if (go == null) { Debug.LogError($"ArenaSceneAssembly: в старой сцене нет корня '{name}'"); return; }
                SceneManager.MoveGameObjectToScene(go, arena);
            }
            EditorSceneManager.CloseScene(old, true);

            var rig = new GameObject("Arena");
            SceneManager.MoveGameObjectToScene(rig, arena);
            foreach (string name in ArenaRoots)
            {
                GameObject go = arena.GetRootGameObjects().FirstOrDefault(g => g.name == name);
                if (go == null) { Debug.LogError($"ArenaSceneAssembly: в сцене арены нет корня '{name}'"); return; }
                go.transform.SetParent(rig.transform, true);
            }

            // Авторская камера смотрит в −Z, наши бойцы — лицом к +Z-камере: разворот всего рига.
            rig.transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            MeshRenderer[] bridge = BridgeRenderers(rig.transform);
            if (bridge.Length == 0)
            {
                string names = string.Join(", ", rig.GetComponentsInChildren<Transform>(true).Select(t => t.name).Distinct());
                Debug.LogError("ArenaSceneAssembly: не нашёл рендереры моста. Дети рига: " + names);
                return;
            }
            Bounds b = bridge[0].bounds;
            foreach (MeshRenderer r in bridge) b.Encapsulate(r.bounds);

            float deckTop = DeckTopY(bridge, b);
            rig.transform.position -= new Vector3(b.center.x, deckTop, b.center.z);
            Debug.Log($"[assembly] bridge bounds {b.size:F2}, deckTop {deckTop:F3}, rig at {rig.transform.position:F3}");

            // Контракт Arena/Timber — тот же рецепт, что был у старого билдера:
            // плоская коробка по габаритам настила, верх ровно y=0.
            var timber = new GameObject("Timber");
            timber.transform.SetParent(rig.transform);
            timber.transform.position = Vector3.zero;
            var box = timber.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, -0.05f, 0f);
            box.size = new Vector3(b.size.x, 0.1f, b.size.z);
            if (b.size.x * 0.5f < Mikey.Fight.FightRules.ArenaHalfWidth)
                Debug.LogError($"ArenaSceneAssembly: настил короче арены боя — {b.size.x:F2}/2 < {Mikey.Fight.FightRules.ArenaHalfWidth}");

            // Вода уехала вместе с ригом — зеркальная плоскость отражения едет следом.
            var refl = rig.GetComponentInChildren<PlanarReflection>(true);
            if (refl == null) Debug.LogError("ArenaSceneAssembly: PlanarReflection не найден");
            else { refl.waterLevel += rig.transform.position.y; Debug.Log($"[assembly] waterLevel -> {refl.waterLevel:F3}"); }

            Camera cam = rig.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.gameObject.name == "Main Camera");
            Debug.Log(cam != null
                ? $"[assembly] camera at {cam.transform.position:F3} rot {cam.transform.rotation.eulerAngles:F1}"
                : "[assembly] ВНИМАНИЕ: Main Camera не найдена в риге");

            EditorSceneManager.MarkSceneDirty(arena);
            EditorSceneManager.SaveScene(arena);
            AssetDatabase.DeleteAsset(OldScenePath);
            Debug.Log("[assembly] DONE");
        }

        private static MeshRenderer[] BridgeRenderers(Transform rig)
        {
            return rig.GetComponentsInChildren<MeshRenderer>(true).Where(r =>
            {
                var mf = r.GetComponent<MeshFilter>();
                string mesh = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "";
                return r.name.ToLowerInvariant().Contains("bridge") || mesh.ToLowerInvariant().Contains("bridge");
            }).ToArray();
        }

        /// <summary>Высота ходовой поверхности настила: bounds.max.y — это верх перил, поэтому
        /// вешаем временные MeshCollider'ы на мост и бьём лучом сверху по центру пролёта
        /// (перила идут по краям, центр дорожки сверху открыт).</summary>
        private static float DeckTopY(MeshRenderer[] bridge, Bounds b)
        {
            var temps = new List<MeshCollider>();
            foreach (MeshRenderer r in bridge)
                if (r.GetComponent<Collider>() == null)
                    temps.Add(r.gameObject.AddComponent<MeshCollider>());
            Physics.SyncTransforms();

            var ray = new Ray(new Vector3(b.center.x, b.max.y + 1f, b.center.z), Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, b.size.y + 2f);
            float y = hits.Where(h => temps.Contains(h.collider))
                          .OrderByDescending(h => h.point.y)
                          .Select(h => h.point.y)
                          .DefaultIfEmpty(b.max.y)   // fallback: верх bounds, дальше правится по скриншоту
                          .First();

            foreach (MeshCollider c in temps) Object.DestroyImmediate(c);
            return y;
        }
    }
}
```

- [ ] **Step 2: Гейт до — компиляция**

unity-cli: рефреш, лог без ошибок компиляции нового скрипта.

- [ ] **Step 3: Запустить сборку**

unity-cli eval: `Mikey.EditorTools.ArenaSceneAssembly.Assemble()`. В логе должны появиться строки `[assembly] bridge bounds …`, `[assembly] waterLevel -> …`, `[assembly] camera at …`, `[assembly] DONE` и ни одного `ArenaSceneAssembly:`-error. Если `не нашёл рендереры моста` — взять из лога список детей рига, поправить фильтр в `BridgeRenderers` под реальное имя и повторить (сцена пересобирается идемпотентно только из git-состояния: перед повтором `git checkout -- Assets/Scenes/FightSandbox.unity` и восстановить FightSandboxOld из коммита Task 1, если скрипт успел его удалить).

- [ ] **Step 4: Гейт после — скриншот**

unity-cli: скриншот Game view. Проверить: мост по центру кадра, бамбуковая роща, вода с отражением (не чёрная и не «терраса сквозь воду»), туман, бойцы стоят на настиле (не висят и не утоплены). Кадр по композиции соответствует HANDOFF (камера смотрит вдоль канала на мост). Если бойцы висят/утоплены — deckTop посчитан по перилам/канату: смотреть `[assembly] deckTop` в логе, поправить точку луча в `DeckTopY` (сместить XZ от центра) и повторить по процедуре из Step 3.

- [ ] **Step 5: Commit**

```bash
git add Assets/Editor/ArenaSceneAssembly.cs Assets/Scenes/FightSandbox.unity
git rm -q Assets/Scenes/FightSandboxOld.unity Assets/Scenes/FightSandboxOld.unity.meta 2>/dev/null || git add -A Assets/Scenes
git commit -m "feat: FightSandbox собран на BambooArena — геймплей перенесён, настил на y=0, Arena/Timber восстановлен

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Чистка старых арен

**Files:**
- Move (git mv, GUID сохраняются): `Assets/Fight/Arena/Character.shader` (+ .meta) → `Assets/Fight/character/`; `Assets/Fight/Arena/M_BlobShadow.mat` (+ .meta) → `Assets/Fight/character/`
- Delete: `Assets/Fight/Arena/**` (остальное), `Assets/Fight/NewArena/**` (докоммитить уже сделанные удаления), `Assets/Editor/ArenaRefImporter.cs`, `ArenaTextures.cs`, `BambooArena.cs`, `BambooCrownBake.cs`, `BridgeKit.cs`, `FightSceneSetup.cs`, `GrassBuilder.cs`, `NewArenaScene.cs`, `WaterProbe.cs`, `ArenaSceneAssembly.cs` (одноразовый из Task 2) — все с .meta

**Interfaces:**
- Consumes: сцена из Task 2 (единственный потребитель `M_BlobShadow.mat` и, через материалы бойцов, `Character.shader`).
- Produces: компилирующийся проект без старых арен. Остаются `Assets/Editor/FightBuild.cs`, `FightCapture.cs`, `PlayModeStartScene.cs` — они старые арены не трогают (проверено грепом).

Обоснование расширения списка против спеки: делит-лист замкнут по ссылкам компиляции — `FightSceneSetup.cs` вызывает `BambooArena.Build()` (пересборщик старой арены, с новой сценой ему делать нечего), `BridgeKit`/`ArenaTextures`/`BambooCrownBake`/`GrassBuilder`/`NewArenaScene` — его же семейство генераторов. Всё остаётся в истории git.

- [ ] **Step 1: Переезд живых файлов**

```bash
cd /c/Users/user/Mikey
git mv Assets/Fight/Arena/Character.shader Assets/Fight/character/Character.shader
git mv Assets/Fight/Arena/Character.shader.meta Assets/Fight/character/Character.shader.meta
git mv Assets/Fight/Arena/M_BlobShadow.mat Assets/Fight/character/M_BlobShadow.mat
git mv Assets/Fight/Arena/M_BlobShadow.mat.meta Assets/Fight/character/M_BlobShadow.mat.meta
```

- [ ] **Step 2: Снос**

```bash
git rm -r -q Assets/Fight/Arena Assets/Fight/Arena.meta
git rm -r -q Assets/Fight/NewArena Assets/Fight/NewArena.meta   # докоммичивает и уже удалённые файлы
for f in ArenaRefImporter ArenaTextures BambooArena BambooCrownBake BridgeKit FightSceneSetup GrassBuilder NewArenaScene WaterProbe ArenaSceneAssembly; do
  git rm -q "Assets/Editor/$f.cs" "Assets/Editor/$f.cs.meta"
done
```

Примечание: `ArenaSceneAssembly.cs.meta` мог не попасть в коммит Task 2 — тогда для него `rm` вместо `git rm`.

- [ ] **Step 3: Гейт — компиляция и сцена**

unity-cli: рефреш; лог без ошибок компиляции; открыть `FightSandbox.unity` — в консоли нет missing-script/missing-reference ошибок (розовых материалов на бойцах нет: Character.shader переехал с GUID).

- [ ] **Step 4: Commit**

```bash
git add -A Assets/Fight
git commit -m "chore: снос обеих старых арен и их генераторов — живые Character.shader и M_BlobShadow переехали в character/

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Финальная проверка в плей-моде

**Files:** нет изменений (только запуск и логи; коммит только если пришлось что-то чинить).

**Interfaces:**
- Consumes: собранный FightSandbox из Task 3.

- [ ] **Step 1: Плей-смоук**

unity-cli: открыть `Assets/Scenes/FightSandbox.unity`, войти в Play Mode (сцена не в Build Settings — PlayModeStartScene пустит её напрямую), подождать ~5 секунд, снять скриншот, выйти из Play.

- [ ] **Step 2: Проверить логи**

Обязательные признаки успеха в консоли:
- `[diag deck] N samples across the arena…` с N > 0 (FightBootstrap дострелялся до коллайдера Timber);
- **нет** `[diag deck] no collider on Arena/Timber`;
- **нет** `FootIK: no collider on Arena/Timber`;
- нет NullReference/Missing-ошибок.

На скриншоте: бойцы стоят на настиле моста, роща и вода на месте.

- [ ] **Step 3: Отчёт**

Свести пользователю: скриншот, строку `[diag deck]`, итоговую позицию камеры из лога сборки. Известные унаследованные ограничения (из HANDOFF): камера — латеральный ход только x ±6 относительно арены; отражение — самая дорогая часть кадра (scale 0.385), первый кандидат на срез на слабом девайсе; бойцы в отражение не попадают (маска слоёв арены) — осознанно, ради цены кадра.

---

## Self-Review (выполнен)

1. **Покрытие спеки:** импорт (Task 1), профиль под новым GUID (Task 1 Step 4), запрет на Settings/ProjectSettings/SampleScene (Global Constraints + проверка в Task 1 Step 3), сборка рига с разворотом и настилом на y=0 (Task 2), waterLevel вслед за ригом (Task 2 — уточнение спеки: у PlanarReflection есть публичное поле, код не меняем), чистка (Task 3, список расширен по замыканию компиляции — обосновано в таске), проверка (Task 2 Step 4 + Task 4). Гейт версии — Task 1 Step 6.
2. **Плейсхолдеры:** нет; весь код приведён полностью.
3. **Согласованность типов:** `Assemble()` — public static в `Mikey.EditorTools`; `FightRules.ArenaHalfWidth` — `Mikey.Fight`, const 3f; `PlanarReflection.waterLevel` — public float (проверено по исходнику из архива).

Отклонение от спеки, внесённое планом: контракт `Arena/Timber` (не был замечен на этапе спеки — найден при планировании в FightBootstrap/FootIK) — корень рига называется `Arena`, а не `ArenaRig`, и в него добавляется `Timber` с плоским BoxCollider по рецепту старого билдера. Спеке не противоречит, дополняет «геймплей не трогаем».
