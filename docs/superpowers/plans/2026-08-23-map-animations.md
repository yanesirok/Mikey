# Карта: слой анимаций — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** оживить экраны карты (`map`, `mapOkinawa`) слоем анимаций — атмосфера, камера, маркеры, отклик на касание, подача панелей, церемонии — не добавив нагрузки на телефон.

**Architecture:** один 30-герцовый драйвер (`MapAmbientController`) на оба экрана карты плюс отдельный контроллер одноразовых сцен (`MapCeremonyController`); вся считаемая математика вынесена в чистые статические классы (`MapAmbientMath`, дополнения к `MapPanZoomMath`) и покрыта настоящими юнит-тестами. Анимируются исключительно `translate`, `scale`, `rotate`, `opacity` и цвет — ни один анимационный путь не трогает `left/top/width/height`, потому что это проход лэйаута каждый кадр. Трансформацию `.pan-canvas` по-прежнему пишет только `MapPanZoomController`; ambient отдаёт ему аддитивное смещение и множитель зума.

**Tech Stack:** Unity 6000.3.18f1, C#, UI Toolkit (UXML/USS), NUnit EditMode-тесты, unity-cli через подключённый редактор.

**Spec:** `docs/superpowers/specs/2026-08-23-map-animations-design.md`

## Global Constraints

- Unity **6000.3.18f1**, UI Toolkit. Целевая платформа — Android, ландшафтная ориентация.
- **Анимируем только `translate`, `scale`, `rotate`, `opacity` и цвет.** Ни один анимационный путь не пишет `style.left`, `style.top`, `style.width`, `style.height`, `margin`, `padding`, `font-size`. Это стержень всего дизайна: перечисленное дёргает лэйаут каждый кадр.
- `MapCloudLayout.Apply` остаётся единственным, кто пишет облакам `left/top/width/height/rotate` — раскладка при показе и ресайзе. Дрейф и параллакс кладутся поверх через `translate`.
- Всем анимируемым элементам при привязке ставится `usageHints = UsageHints.DynamicTransform` (плюс `DynamicColor`, где меняется цвет или прозрачность).
- Ambient тикает на **30 Гц** (`schedule.Execute(...).Every(33)`). Потолок одновременно ДВИЖУЩИХСЯ ambient-элементов — четырнадцать: четыре облака, до девяти маркеров уровня и камера. Заблокированные маркеры не дышат вовсе, поэтому фактическое число на сегодня — около семи.
- Никаких новых полноэкранных полупрозрачных слоёв: на карте уже лежит арт, скрим экрана, четыре облака с альфой и скрим канваса. Исключение — оверлей ink-wash, который существует только на время церемонии.
- Ambient полностью останавливается, когда активный экран не `map` и не `mapOkinawa`, когда включена настройка «меньше движения» и когда `MapCloudTransitionController.IsTransitioning` или `MapCeremonyController.IsPlaying`.
- Инерция и резинка **не** отключаются настройкой «меньше движения»: это отклик на палец, а не декор.
- Новый код карты — в сборке `Mikey.UI.Map`, настройка движения — в `Mikey.UI.Settings`. Циклов сборок это не создаёт: `Mikey.UI.Settings` ссылается только на `Mikey.UI.Audio`.
- Тесты — EditMode, NUnit, рядом с кодом: `Assets/UI/Map/Tests/`, `Assets/UI/Settings/Tests/`. Стиль копируется с существующих файлов в этих папках.
- Все контроллеры карты живут на корневом GameObject `UI` в `Assets/Scenes/SampleScene.unity` — новые вешаются туда же.
- **Никогда не оставлять проект в несобирающемся состоянии между шагами.** Тип создаётся ДО того, как его кто-то использует; ссылка сборки добавляется ДО кода, которому она нужна. После каждого файла с новыми типами — `AssetDatabase.Refresh()` и проверка компиляции. Это не педантизм: пока проект не компилируется, HTTP-сервер конвейера не поднимается, и редактор перестаёт отвечать CLI совсем.
- **Перед коммитом гнать ПОЛНЫЙ прогон EditMode, а не только сборки своей задачи.** `unity command run_tests --mode EditMode --format json` без фильтра, затем опрос `test_status` до `completed`. В проекте есть архитектурные тесты-сторожа, живущие в чужих сборках (например `Mikey.UI.Progression.Tests.PlayerPrefsKeyRegressionTests`, запрещающий писать `PlayerPrefs` из любого файла, кроме выделенных классов-хранилищ). Прогон только своих сборок их не видит: именно так регрессия из задачи 1 прожила три ревью.
- **Персистентность — только через класс-хранилище.** Прямой вызов `PlayerPrefs.Set*` разрешён исключительно файлам из белого списка в `PlayerPrefsKeyRegressionTests`. Новая настройка получает пару «интерфейс + PlayerPrefs-реализация» по образцу `IAudioSettingsStorage`/`PlayerPrefsAudioSettingsStorage`, а сам store делегирует ей и добавляется в белый список.
- Коммит после каждой задачи.

### Среда: редактор Unity уже открыт

- Тесты гонять **через подключённый редактор**: `unity command run_tests …`. Синтаксис `unity test --project …` использовать нельзя — он берёт блокировку проекта, которую держит открытый редактор.
- Длинные прогоны асинхронны: вызов CLI отваливается по таймауту в 30 секунд, это не ошибка. Опрашивать `unity command test_status --format json` до `completed`. Прервать — `unity command cancel_tests`. Повторный `run_tests` во время идущего прогона отменяет предыдущий.
- После создания или удаления файлов дёргать `unity --json cmd eval 'UnityEditor.AssetDatabase.Refresh(); return "ok";'`, иначе `.meta` не создадутся и сборка не увидит новый код.
- Методы, вызываемые через `unity … cmd eval`, должны быть `public` — eval не видит `internal`.
- **Если `unity pipeline list` показывает `Server Reachable: false`** — не жди и не заводи фоновых наблюдателей. Почти всегда причина в собственном незавершённом изменении: посмотри Editor.log (`%LOCALAPPDATA%\Unity\Editor\Editor.log`) на `error CS`. Сломанная компиляция роняет сервер, и редактор не восстановится сам, пока код в дереве не станет собираемым. Если код собираемый, а сервер молчит — редактору нужен фокус окна, чтобы запустить перекомпиляцию; это уже требует человека, отчитайся статусом BLOCKED.

### Отклонение от спеки

Спека называет три новых файла кода. В плане их четыре: добавлен `MapNodeFeedback` — маленький статический помощник для отказа locked-маркера и волны от тапа. Класть эти два одноразовых эффекта в ambient-драйвер (он про непрерывное движение) или в контроллер церемоний (он про редкие сцены) означало бы смешать ответственности; отдельный файл на 60 строк дешевле.

---

## Task 1: Настройка «меньше движения» и каркас драйвера

Ставим фундамент: настройка, ссылки сборок, живой драйвер, который правильно стартует и останавливается, но пока ничего не двигает. Отдельной задачей — потому что все последующие анимации садятся на этот каркас, и его ошибки иначе размажутся по всему плану.

**Files:**
- Create: `Assets/UI/Settings/IMotionSettings.cs`
- Create: `Assets/UI/Settings/MotionSettingsStore.cs`
- Create: `Assets/UI/Settings/Tests/MotionSettingsStoreTests.cs`
- Create: `Assets/UI/Map/MapAmbientController.cs`
- Create: `Assets/UI/Map/Tests/MapAmbientControllerSourceTests.cs`
- Modify: `Assets/UI/Map/Mikey.UI.Map.asmdef` (добавить ссылку `Mikey.UI.Settings`)
- Modify: `Assets/UI/MikeyApp.uxml` (строка тумблера в `shared-settings-modal`, около строки 1250)
- Modify: `Assets/UI/Settings/Settings.uss` (стиль строки тумблера)
- Modify: `Assets/UI/Settings/SettingsModalController.cs` (проводка тумблера)
- Modify: `Assets/UI/Map/Tests/MapControllersSceneTests.cs` (проверка компонента в сцене)
- Modify: `Assets/Scenes/SampleScene.unity` (компоненты на GameObject `UI`)

**Interfaces:**
- Consumes: `ScreenManager.ScreenChanged` (событие `Action<string>` из `IScreenNavigator`), `MapCloudTransitionController.IsTransitioning`.
- Produces:
  - `Mikey.UI.Settings.IMotionSettings` — `bool ReducedMotion { get; set; }`, `event Action Changed`.
  - `Mikey.UI.Settings.MotionSettingsStore : MonoBehaviour, IMotionSettings` — хранение в `PlayerPrefs` под ключом `Mikey.Settings.ReducedMotion`.
  - `Mikey.UI.Map.MapAmbientController : MonoBehaviour` — публичных членов пока нет; тик приватный.

- [ ] **Step 1: Написать падающий тест настройки**

Создать `Assets/UI/Settings/Tests/MotionSettingsStoreTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Mikey.UI.Settings.Tests
{
    public class MotionSettingsStoreTests
    {
        private const string Key = "Mikey.Settings.ReducedMotion";

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(Key);

        [Test]
        public void DefaultsToFullMotion()
        {
            PlayerPrefs.DeleteKey(Key);
            var go = new GameObject("motion");
            var store = go.AddComponent<MotionSettingsStore>();
            Assert.IsFalse(store.ReducedMotion, "Движение по умолчанию включено полностью.");
            Object.DestroyImmediate(go);
        }

        [Test]
        public void PersistsAndRaisesChangedOnlyOnRealChange()
        {
            // Свой сброс, а не расчёт на TearDown соседа: тест обязан проходить
            // и когда его гоняют в одиночку по фильтру, первым в свежем процессе.
            PlayerPrefs.DeleteKey(Key);

            var go = new GameObject("motion");
            var store = go.AddComponent<MotionSettingsStore>();

            int raised = 0;
            store.Changed += () => raised++;

            store.ReducedMotion = true;
            store.ReducedMotion = true;

            Assert.AreEqual(1, raised, "Повторная запись того же значения не должна оповещать.");
            Assert.AreEqual(1, PlayerPrefs.GetInt(Key, 0), "Значение должно пережить перезапуск.");
            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

```bash
unity command run_tests --mode EditMode --filter "MotionSettingsStoreTests" --filter_type testName --format json
```

Ожидается: провал компиляции — типа `MotionSettingsStore` не существует.

- [ ] **Step 3: Написать интерфейс настройки**

Создать `Assets/UI/Settings/IMotionSettings.cs`:

```csharp
using System;

namespace Mikey.UI.Settings
{
    /// <summary>
    /// Единственная настройка движения: «меньше движения» выключает
    /// декоративный ambient-слой карты целиком и сокращает каскады до
    /// простого проявления. Прямой отклик на палец (инерция пана, резинка
    /// на границах) она НЕ трогает — это управление, а не декор.
    /// Форма зеркалит <see cref="Mikey.UI.Audio.IAudioSettings"/>, чтобы
    /// общий Settings-модал читал обе настройки одинаково.
    /// </summary>
    public interface IMotionSettings
    {
        /// <summary>Поднимается только при настоящей смене значения, включая загрузку из хранилища.</summary>
        event Action Changed;

        /// <summary>true — декоративное движение выключено. По умолчанию false.</summary>
        bool ReducedMotion { get; set; }
    }
}
```

- [ ] **Step 4: Написать хранилище**

Создать `Assets/UI/Settings/MotionSettingsStore.cs`:

```csharp
using System;
using UnityEngine;

namespace Mikey.UI.Settings
{
    /// <summary>
    /// Хранение <see cref="IMotionSettings"/> в PlayerPrefs. Живёт на том же
    /// GameObject, что и SettingsModalController, который достаёт её через
    /// GetComponent — ровно как уже сделано для громкостей.
    /// </summary>
    public sealed class MotionSettingsStore : MonoBehaviour, IMotionSettings
    {
        public const string ReducedMotionKey = "Mikey.Settings.ReducedMotion";

        private bool _reducedMotion;
        private bool _loaded;

        public event Action Changed;

        public bool ReducedMotion
        {
            get
            {
                EnsureLoaded();
                return _reducedMotion;
            }
            set
            {
                EnsureLoaded();
                if (_reducedMotion == value)
                    return;
                _reducedMotion = value;
                PlayerPrefs.SetInt(ReducedMotionKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        private void Awake() => EnsureLoaded();

        private void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            _reducedMotion = PlayerPrefs.GetInt(ReducedMotionKey, 0) != 0;
        }
    }
}
```

- [ ] **Step 5: Обновить ассеты и прогнать тест**

```bash
unity --json cmd eval 'UnityEditor.AssetDatabase.Refresh(); return "ok";'
```

```bash
unity command run_tests --mode EditMode --filter "MotionSettingsStoreTests" --filter_type testName --format json
```

Ожидается: оба теста PASS.

- [ ] **Step 6: Добавить тумблер в разметку настроек**

В `Assets/UI/MikeyApp.uxml`, внутри `shared-settings-modal`, сразу после строки со слайдером `shared-settings-trainer` и до кнопки `shared-settings-close`, вставить:

```xml
                <!-- Одна настройка движения: выключает декоративный ambient-слой
                     карты целиком (см. MapAmbientController). Отклик на палец —
                     инерция пана и резинка — ею сознательно не управляется. -->
                <ui:VisualElement class="setting">
                    <ui:Label text="Reduced motion" class="setting__label" />
                    <ui:Toggle name="shared-settings-reduced-motion" class="setting__toggle" />
                </ui:VisualElement>
```

- [ ] **Step 7: Добавить стиль строки тумблера**

В конец `Assets/UI/Settings/Settings.uss` дописать:

```css
/* Тумблер в строке настройки. Галка сама по себе крупнее минимального
   тач-таргета за счёт padding самой строки (".setting"), поэтому отдельный
   .tap-target здесь не нужен — в отличие от кнопок в HUD. */
.setting__toggle {
    flex-shrink: 0;
    margin-left: 24px;
}
.setting__toggle .unity-toggle__checkmark {
    width: 34px;
    height: 34px;
}
```

- [ ] **Step 8: Провести тумблер в контроллере настроек**

В `Assets/UI/Settings/SettingsModalController.cs`:

добавить поля рядом с существующими слайдерами:

```csharp
        private Toggle _reducedMotionToggle;
        private IMotionSettings _motionSettings;
        private EventCallback<ChangeEvent<bool>> _reducedMotionChangedCallback;
```

в методе привязки, рядом со строкой `_closeButton = root.Q<Button>("shared-settings-close");`, добавить:

```csharp
            _reducedMotionToggle = root.Q<Toggle>("shared-settings-reduced-motion");
```

сразу после трёх вызовов `WireSlider`, добавить:

```csharp
            _motionSettings = GetComponent<IMotionSettings>();
            if (_reducedMotionToggle != null && _motionSettings != null)
            {
                _reducedMotionToggle.value = _motionSettings.ReducedMotion;
                _reducedMotionChangedCallback = evt => _motionSettings.ReducedMotion = evt.newValue;
                _reducedMotionToggle.RegisterValueChangedCallback(_reducedMotionChangedCallback);
            }
```

в блоке отписки (там же, где `_closeButton.clicked -= Close;`), добавить:

```csharp
                if (_reducedMotionToggle != null && _reducedMotionChangedCallback != null)
                    _reducedMotionToggle.UnregisterValueChangedCallback(_reducedMotionChangedCallback);
```

и в сбросе ссылок, рядом с `_closeButton = null;`:

```csharp
            _reducedMotionToggle = null;
            _reducedMotionChangedCallback = null;
            _motionSettings = null;
```

- [ ] **Step 9: Написать падающий тест каркаса драйвера**

Создать `Assets/UI/Map/Tests/MapAmbientControllerSourceTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Контракт ambient-драйвера карты, читаемый по тексту исходника — по той
    /// же причине, что и остальные source-тесты контроллеров карты: корутины и
    /// планировщик MonoBehaviour в EditMode не гоняются.
    ///
    /// Главный тест здесь — <see cref="NeverWritesLayoutProperties"/>: весь
    /// дизайн держится на том, что анимация не трогает геометрию, и нарушение
    /// этого правила должно ломать сборку, а не всплывать как просадка кадров
    /// на телефоне.
    /// </summary>
    public class MapAmbientControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapAmbientController.cs";

        /// <remarks>
        /// Сканирование ТЕКСТА, а не синтаксического дерева: намеренно грубо,
        /// зато не требует парсера и ловит нарушение в любой форме записи.
        /// Обратная сторона — запрещённые имена нельзя упоминать даже в
        /// комментариях проверяемого файла, иначе он уронит сам себя.
        /// </remarks>
        [Test]
        public void NeverWritesLayoutProperties()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("style.left", source);
            StringAssert.DoesNotContain("style.top", source);
            StringAssert.DoesNotContain("style.width", source);
            StringAssert.DoesNotContain("style.height", source);
            StringAssert.DoesNotContain("style.margin", source);
            StringAssert.DoesNotContain("style.padding", source);
            StringAssert.DoesNotContain("style.fontSize", source);
        }

        [Test]
        public void TicksAtThirtyHertz()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("Every(TickIntervalMs)", source);
            StringAssert.Contains("TickIntervalMs = 33", source);
        }

        [Test]
        public void StopsOutsideMapScreens()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("JapanScreenId", source);
            StringAssert.Contains("OkinawaScreenId", source);
        }

        [Test]
        public void RespectsReducedMotionAndTransition()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ReducedMotion", source);
            StringAssert.Contains("MapCloudTransitionController.IsTransitioning", source);
        }

        [Test]
        public void ThrottlesRenderingWhileOnTheMap()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("OnDemandRendering.renderFrameInterval", source);
        }
    }
}
```

- [ ] **Step 10: Запустить тест и убедиться, что он падает**

```bash
unity command run_tests --mode EditMode --filter "MapAmbientControllerSourceTests" --filter_type testName --format json
```

Ожидается: провал — файла `MapAmbientController.cs` нет.

- [ ] **Step 11: Добавить ссылку сборки**

В `Assets/UI/Map/Mikey.UI.Map.asmdef` в массив `references` добавить `"Mikey.UI.Settings"`, чтобы получилось:

```json
    "references": [
        "Mikey.UI.SafeArea",
        "Mikey.UI.Progression",
        "Mikey.UI.Settings",
        "Unity.InputSystem"
    ],
```

Цикла это не создаёт: `Mikey.UI.Settings` ссылается только на `Mikey.UI.Audio`.

- [ ] **Step 12: Написать каркас драйвера**

Создать `Assets/UI/Map/MapAmbientController.cs`:

```csharp
using System.Collections;
using Mikey.UI.SafeArea;
using Mikey.UI.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Единственный драйвер непрерывного («ambient») движения на обоих экранах
    /// карты: дрейф и параллакс облаков, дыхание бумаги, Ken Burns в простое,
    /// дыхание маркеров.
    ///
    /// <para>
    /// Тикает на 30 Гц, а не по кадру: у ambient-движения периоды в десятки
    /// секунд, разница с 60 Гц невидима, а работы вдвое меньше. По той же
    /// причине на экранах карты поднимается
    /// <see cref="OnDemandRendering.renderFrameInterval"/> — карта статичный
    /// экран, ей незачем полная частота.
    /// </para>
    ///
    /// <para>
    /// Пишет ИСКЛЮЧИТЕЛЬНО transform-свойства и прозрачность. Запись любого
    /// геометрического свойства здесь означала бы полный проход лэйаута
    /// каждый тик — см. MapAmbientControllerSourceTests, который это
    /// стережёт.
    ///
    /// ВНИМАНИЕ при правке этого файла: страж сканирует ТЕКСТ исходника, а не
    /// синтаксическое дерево, поэтому запрещённые имена свойств нельзя
    /// упоминать даже в комментарии — файл уронит сам себя.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapAmbientController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;
        private const int TickIntervalMs = 33;
        private const int MapRenderFrameInterval = 2;

        private const string JapanScreenId = "map";
        private const string OkinawaScreenId = "mapOkinawa";

        private VisualElement _root;
        private IScreenNavigator _navigator;
        private IMotionSettings _motion;
        private IVisualElementScheduledItem _tick;

        private Coroutine _bindRoutine;
        private bool _bound;
        private bool _onMapScreen;
        private float _elapsedSeconds;

        private void OnEnable()
        {
            if (_bound)
                return;
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }

            StopTicking();

            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            if (_motion != null)
                _motion.Changed -= OnMotionSettingsChanged;

            _root = null;
            _motion = null;
            _bound = false;
            _onMapScreen = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[MapAmbientController] UIDocument root unavailable; ambient not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;
            _motion = GetComponent<IMotionSettings>();
            if (_motion != null)
                _motion.Changed += OnMotionSettingsChanged;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
            {
                _navigator.ScreenChanged += OnScreenChanged;
                OnScreenChanged(_navigator.CurrentScreen);
            }

            _bound = true;
            _bindRoutine = null;
        }

        private void OnScreenChanged(string screenId)
        {
            _onMapScreen = screenId == JapanScreenId || screenId == OkinawaScreenId;
            if (_onMapScreen)
                StartTicking();
            else
                StopTicking();
        }

        /// <summary>
        /// Настройка «меньше движения» переключается из модала, который
        /// открывается ПОВЕРХ карты и экран не меняет — значит ScreenChanged не
        /// придёт, и реакция обязана идти от самой настройки. Без этой подписки
        /// выключение движения ловилось бы следующим тиком, а обратное
        /// включение не ловилось бы никогда: тик к тому моменту уже остановлен,
        /// и ambient молчал бы до следующего входа на экран карты.
        /// </summary>
        private void OnMotionSettingsChanged()
        {
            if (!_onMapScreen)
                return;

            if (_motion != null && _motion.ReducedMotion)
                StopTicking();
            else
                StartTicking();
        }

        private void StartTicking()
        {
            if (_root == null || _tick != null)
                return;
            if (_motion != null && _motion.ReducedMotion)
                return;

            _elapsedSeconds = 0f;
            OnDemandRendering.renderFrameInterval = MapRenderFrameInterval;
            _tick = _root.schedule.Execute(Tick).Every(TickIntervalMs);
        }

        private void StopTicking()
        {
            if (_tick != null)
            {
                _tick.Pause();
                _tick = null;
            }
            OnDemandRendering.renderFrameInterval = 1;
        }

        /// <summary>
        /// Один шаг ambient-движения. Пока только копит время — конкретные
        /// каналы (облака, камера, маркеры) добавляются следующими задачами
        /// плана и каждый живёт в своём приватном методе.
        /// </summary>
        private void Tick()
        {
            if (!_bound || !_onMapScreen)
                return;
            if (_motion != null && _motion.ReducedMotion)
            {
                StopTicking();
                return;
            }
            if (MapCloudTransitionController.IsTransitioning)
                return;

            _elapsedSeconds += TickIntervalMs / 1000f;
        }
    }
}
```

- [ ] **Step 13: Добавить компоненты в сцену**

```bash
unity --json cmd eval 'var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); if (scene.path != "Assets/Scenes/SampleScene.unity") return "WRONG SCENE: " + scene.path; var ui = UnityEngine.GameObject.Find("UI"); if (ui == null) return "NO UI OBJECT"; if (ui.GetComponent<Mikey.UI.Settings.MotionSettingsStore>() == null) ui.AddComponent<Mikey.UI.Settings.MotionSettingsStore>(); if (ui.GetComponent<Mikey.UI.Map.MapAmbientController>() == null) ui.AddComponent<Mikey.UI.Map.MapAmbientController>(); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene); return "ok";'
```

Если вернулось `WRONG SCENE`, открыть `Assets/Scenes/SampleScene.unity` в редакторе вручную и повторить — команда сознательно не открывает сцену сама, чтобы не потерять несохранённые правки.

- [ ] **Step 14: Дописать проверку сцены**

В `Assets/UI/Map/Tests/MapControllersSceneTests.cs` добавить тест рядом с существующими:

```csharp
        [Test]
        public void UiGameObject_HasMapAmbientController()
        {
            GameObject ui = OpenUiGameObject();
            Assert.IsNotNull(ui.GetComponent<MapAmbientController>(),
                "UI GameObject must have a MapAmbientController for the map's ambient motion to run in a real build.");
        }
```

- [ ] **Step 15: Прогнать всю сборку тестов карты и настроек**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

Опрашивать до `completed`:

```bash
unity command test_status --format json
```

Ожидается: все PASS.

- [ ] **Step 16: Визуальная проверка**

Войти на экран карты в редакторе и убедиться, что ничего не сломалось: карта показывается, пан и зум работают, тумблер «Reduced motion» в настройках переключается и переживает перезапуск редактора. Движения пока нет — это ожидаемо.

- [ ] **Step 17: Коммит**

```bash
git add Assets/UI/Settings Assets/UI/Map Assets/UI/MikeyApp.uxml Assets/Scenes/SampleScene.unity && git commit -m "feat(map): каркас ambient-драйвера и настройка «меньше движения»"
```

---

> **Поправка после исполнения (см. ревью задачи 4).** Образец `MotionSettingsStore`
> выше пишет `PlayerPrefs` напрямую, что нарушает архитектурное правило проекта:
> прямая запись разрешена только классам-хранилищам из белого списка в
> `PlayerPrefsKeyRegressionTests`. Правильная форма — пара
> `IMotionSettingsStorage` + `PlayerPrefsMotionSettingsStorage` по образцу
> `IAudioSettingsStorage`/`PlayerPrefsAudioSettingsStorage`, store делегирует ей,
> новый файл хранилища добавляется в белый список. Исправлено отдельным заходом.

## Task 2: Дрейф облаков

Первая настоящая анимация и заодно фундамент математики.

**Files:**
- Create: `Assets/UI/Map/MapAmbientMath.cs`
- Create: `Assets/UI/Map/Tests/MapAmbientMathTests.cs`
- Modify: `Assets/UI/Map/MapAmbientController.cs`

**Interfaces:**
- Consumes: `MapCloudLayout.JapanRest`, `MapCloudLayout.OkinawaRest` (прозрачность покоя каждого облака).
- Produces:
  - `MapAmbientMath.Wave(float timeSeconds, float periodSeconds, float phase01) -> float` в `[-1, 1]`.
  - `MapAmbientMath.Breath(float timeSeconds, float periodSeconds, float amplitude) -> float` в `[1, 1 + amplitude]`.
  - `MapAmbientMath.CloudDrift(int index, float timeSeconds, float canvasWidth, float canvasHeight, out float offsetX, out float offsetY, out float opacityDelta)`.
  - `MapAmbientMath.CloudCount = 4`.

- [ ] **Step 1: Написать падающие тесты математики**

Создать `Assets/UI/Map/Tests/MapAmbientMathTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    public class MapAmbientMathTests
    {
        private const float Tolerance = 0.0005f;

        [Test]
        public void Wave_StartsAtZeroAndPeaksAtQuarterPeriod()
        {
            Assert.AreEqual(0f, MapAmbientMath.Wave(0f, 20f, 0f), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.Wave(5f, 20f, 0f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.Wave(10f, 20f, 0f), Tolerance);
            Assert.AreEqual(-1f, MapAmbientMath.Wave(15f, 20f, 0f), Tolerance);
        }

        [Test]
        public void Wave_RepeatsEveryPeriod()
        {
            Assert.AreEqual(MapAmbientMath.Wave(3f, 20f, 0f), MapAmbientMath.Wave(23f, 20f, 0f), Tolerance);
        }

        [Test]
        public void Wave_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapAmbientMath.Wave(1f, 0f, 0f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.Wave(1f, -5f, 0f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.Wave(float.NaN, 20f, 0f), Tolerance);
        }

        [Test]
        public void Breath_StaysInsideOneToOnePlusAmplitude()
        {
            for (int i = 0; i <= 40; i++)
            {
                float value = MapAmbientMath.Breath(i * 0.25f, 3.2f, 0.035f);
                Assert.GreaterOrEqual(value, 1f - Tolerance);
                Assert.LessOrEqual(value, 1.035f + Tolerance);
            }
        }

        [Test]
        public void Breath_StartsAtRest()
        {
            Assert.AreEqual(1f, MapAmbientMath.Breath(0f, 3.2f, 0.035f), Tolerance);
        }

        [Test]
        public void CloudDrift_StaysInsideItsAmplitudeBudget()
        {
            for (int index = 0; index < MapAmbientMath.CloudCount; index++)
            {
                for (int step = 0; step <= 100; step++)
                {
                    MapAmbientMath.CloudDrift(index, step * 0.5f, 1000f, 500f,
                        out float dx, out float dy, out float dOpacity);

                    Assert.LessOrEqual(System.Math.Abs(dx), 1000f * MapAmbientMath.DriftAmplitudeX + Tolerance);
                    Assert.LessOrEqual(System.Math.Abs(dy), 500f * MapAmbientMath.DriftAmplitudeY + Tolerance);
                    Assert.LessOrEqual(System.Math.Abs(dOpacity), MapAmbientMath.DriftOpacityAmplitude + Tolerance);
                }
            }
        }

        [Test]
        public void CloudDrift_GivesEachCloudItsOwnMotion()
        {
            MapAmbientMath.CloudDrift(0, 7f, 1000f, 500f, out float x0, out _, out _);
            MapAmbientMath.CloudDrift(1, 7f, 1000f, 500f, out float x1, out _, out _);
            Assert.AreNotEqual(x0, x1, "Облака не должны ходить синхронно — иначе это читается как единый слайд.");
        }

        [Test]
        public void CloudDrift_IsSafeOnOutOfRangeIndex()
        {
            MapAmbientMath.CloudDrift(-1, 7f, 1000f, 500f, out float dx, out float dy, out float dOpacity);
            Assert.AreEqual(0f, dx, Tolerance);
            Assert.AreEqual(0f, dy, Tolerance);
            Assert.AreEqual(0f, dOpacity, Tolerance);
        }
    }
}
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

Ожидается: провал компиляции — класса `MapAmbientMath` нет.

- [ ] **Step 3: Написать математику**

Создать `Assets/UI/Map/MapAmbientMath.cs`:

```csharp
namespace Mikey.UI.Map
{
    /// <summary>
    /// Чистая математика непрерывного движения карты, свободная от
    /// UnityEngine и UI Toolkit — ровно по тем же причинам, что и
    /// <see cref="MapPanZoomMath"/>: всё считаемое должно проверяться
    /// настоящим EditMode-тестом, а не чтением текста контроллера.
    ///
    /// <para>
    /// Все амплитуды заданы долями от размера канваса, а не пикселями: карта
    /// живёт на телефонах с очень разной плотностью, и движение «на 12
    /// пикселей» на разных экранах читалось бы по-разному.
    /// </para>
    /// </summary>
    public static class MapAmbientMath
    {
        /// <summary>Число декоративных облаков на экран — совпадает с числом элементов в MapCloudLayout.</summary>
        public const int CloudCount = 4;

        /// <summary>Доля ширины канваса, на которую облако уходит от своей раскладки по горизонтали.</summary>
        public const float DriftAmplitudeX = 0.012f;

        /// <summary>Доля высоты канваса по вертикали — вдвое меньше горизонтальной: небо читается как боковой снос, а не как качели.</summary>
        public const float DriftAmplitudeY = 0.006f;

        /// <summary>На сколько прозрачность облака уходит от своего значения покоя из MapCloudLayout.</summary>
        public const float DriftOpacityAmplitude = 0.04f;

        /// <summary>
        /// Периоды дрейфа по облакам. Взаимно непериодичны специально: с
        /// кратными периодами композиция возвращалась бы в одну и ту же точку
        /// заметным циклом, и небо читалось бы как зацикленная гифка.
        /// </summary>
        public static readonly float[] CloudDriftPeriodsSeconds = { 26f, 31f, 37f, 43f };

        /// <summary>Фазы, чтобы облака не стартовали из одной точки и не шли строем.</summary>
        public static readonly float[] CloudDriftPhases = { 0f, 0.37f, 0.61f, 0.83f };

        /// <summary>Единичная синусоида в [-1, 1]. Небезопасный ввод (нулевой/отрицательный период, NaN) даёт 0 — покой, а не рывок.</summary>
        public static float Wave(float timeSeconds, float periodSeconds, float phase01)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(periodSeconds) || periodSeconds <= 0f)
                return 0f;

            float phase = IsFinite(phase01) ? phase01 : 0f;
            double angle = (timeSeconds / periodSeconds + phase) * 2.0 * System.Math.PI;
            return (float)System.Math.Sin(angle);
        }

        /// <summary>
        /// Множитель «дыхания» в [1, 1 + amplitude], равный ровно 1 в нуле
        /// времени: анимация обязана стартовать из состояния покоя, иначе
        /// первый кадр после показа экрана даёт скачок.
        /// </summary>
        public static float Breath(float timeSeconds, float periodSeconds, float amplitude)
        {
            if (!IsFinite(amplitude))
                return 1f;
            return 1f + amplitude * 0.5f * (1f - (float)System.Math.Cos(WaveAngle(timeSeconds, periodSeconds)));
        }

        /// <summary>Смещение и добавка к прозрачности одного облака относительно его раскладки покоя.</summary>
        public static void CloudDrift(int index, float timeSeconds, float canvasWidth, float canvasHeight,
            out float offsetX, out float offsetY, out float opacityDelta)
        {
            offsetX = 0f;
            offsetY = 0f;
            opacityDelta = 0f;

            if (index < 0 || index >= CloudCount)
                return;
            if (!IsFinite(canvasWidth) || !IsFinite(canvasHeight) || canvasWidth <= 0f || canvasHeight <= 0f)
                return;

            float period = CloudDriftPeriodsSeconds[index];
            float phase = CloudDriftPhases[index];

            offsetX = canvasWidth * DriftAmplitudeX * Wave(timeSeconds, period, phase);
            // Вертикаль идёт своим, более длинным периодом — иначе облако
            // ходило бы по прямой под 45 градусов вместо неспешной петли.
            offsetY = canvasHeight * DriftAmplitudeY * Wave(timeSeconds, period * 1.618f, phase);
            opacityDelta = DriftOpacityAmplitude * Wave(timeSeconds, period * 0.77f, phase);
        }

        private static double WaveAngle(float timeSeconds, float periodSeconds)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(periodSeconds) || periodSeconds <= 0f)
                return 0.0;
            return timeSeconds / periodSeconds * 2.0 * System.Math.PI;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
```

- [ ] **Step 4: Прогнать тесты математики**

```bash
unity --json cmd eval 'UnityEditor.AssetDatabase.Refresh(); return "ok";'
```

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

Ожидается: все PASS.

- [ ] **Step 5: Подключить дрейф в драйвере**

В `Assets/UI/Map/MapAmbientController.cs` добавить поля:

```csharp
        private readonly VisualElement[] _clouds = new VisualElement[MapAmbientMath.CloudCount];
        private readonly float[] _cloudRestOpacity = new float[MapAmbientMath.CloudCount];
        private VisualElement _canvas;
```

добавить метод разрешения элементов текущего экрана и вызывать его из `OnScreenChanged` до `StartTicking()`:

```csharp
        /// <summary>
        /// Забирает элементы того экрана, который сейчас показан. Имена
        /// отличаются между экранами ("map-cloud-*" против "okinawa-cloud-*"),
        /// а прозрачность покоя берётся из соответствующего пресета
        /// MapCloudLayout — контроллер её не выдумывает.
        /// </summary>
        private void ResolveScreenElements(string screenId)
        {
            bool japan = screenId == JapanScreenId;
            string prefix = japan ? "map-cloud-" : "okinawa-cloud-";
            _canvas = _root?.Q<VisualElement>(japan ? "map-canvas" : "okinawa-canvas");

            string[] suffixes = { "right-01", "left-01", "left-02", "bottom-01" };
            MapCloudPreset preset = japan ? MapCloudLayout.JapanRest : MapCloudLayout.OkinawaRest;
            float[] restOpacity =
            {
                preset.Right1.Opacity,
                preset.Left1.Opacity,
                preset.Left2.Opacity,
                preset.Bottom1.Opacity,
            };

            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                _clouds[i] = _root?.Q<VisualElement>(prefix + suffixes[i]);
                _cloudRestOpacity[i] = restOpacity[i];
                if (_clouds[i] != null)
                    _clouds[i].usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor;
            }
        }
```

и в `Tick()`, после накопления времени, вызывать:

```csharp
            TickClouds();
```

```csharp
        private void TickClouds()
        {
            float width = _canvas?.resolvedStyle.width ?? 0f;
            float height = _canvas?.resolvedStyle.height ?? 0f;
            if (width <= 0f || height <= 0f)
                return;

            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                VisualElement cloud = _clouds[i];
                if (cloud == null)
                    continue;

                MapAmbientMath.CloudDrift(i, _elapsedSeconds, width, height,
                    out float dx, out float dy, out float dOpacity);

                cloud.style.translate = new Translate(dx, dy);
                // Клампим: у правого облака прозрачность покоя ровно 1.00
                // (см. MapCloudLayout), и без ограничения верхняя половина
                // синуса упиралась бы в потолок — облако умело бы только
                // темнеть, но не светлеть, то есть дышало бы вполсилы.
                cloud.style.opacity = Mathf.Clamp01(_cloudRestOpacity[i] + dOpacity);
            }
        }
```

- [ ] **Step 6: Прогнать тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

Ожидается: все PASS, включая `NeverWritesLayoutProperties`.

- [ ] **Step 7: Визуальная проверка**

Войти на карту и посмотреть минуту. Облака должны еле заметно плыть, каждое по-своему; композиция не должна ощущаться зацикленной. Если движение видно сознательно — уменьшить `DriftAmplitudeX` до 0.008. Переключить «Reduced motion» и убедиться, что движение останавливается, а облака остаются в своей раскладке покоя.

- [ ] **Step 8: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): дрейф декоративных облаков"
```

---

## Task 3: Параллакс облаков

Даёт карте глубину: облака идут не вместе с картой, а чуть быстрее неё.

**Files:**
- Modify: `Assets/UI/Map/MapAmbientMath.cs`
- Modify: `Assets/UI/Map/Tests/MapAmbientMathTests.cs`
- Modify: `Assets/UI/Map/MapPanZoomController.cs` (публичное чтение текущего пана)
- Modify: `Assets/UI/Map/MapAmbientController.cs`

**Interfaces:**
- Consumes: `MapAmbientMath.CloudDrift` из задачи 2.
- Produces:
  - `MapAmbientMath.ParallaxOffset(float pan, float factor) -> float`.
  - `MapAmbientMath.CloudParallaxFactors` — `float[4]`.
  - `MapPanZoomController.CurrentPanX -> float`, `MapPanZoomController.CurrentPanY -> float`.

- [ ] **Step 1: Написать падающие тесты параллакса**

Дописать в `Assets/UI/Map/Tests/MapAmbientMathTests.cs`:

```csharp
        [Test]
        public void ParallaxOffset_IsZeroWhenFactorIsOne()
        {
            Assert.AreEqual(0f, MapAmbientMath.ParallaxOffset(250f, 1f), Tolerance);
        }

        [Test]
        public void ParallaxOffset_MovesWithThePanForFactorsAboveOne()
        {
            float offset = MapAmbientMath.ParallaxOffset(100f, 1.1f);
            Assert.Greater(offset, 0f, "Ближнее облако должно уходить в ту же сторону, что и пан, но дальше.");
            Assert.AreEqual(10f, offset, Tolerance);
        }

        [Test]
        public void ParallaxOffset_IsCapped()
        {
            float offset = MapAmbientMath.ParallaxOffset(100000f, 1.12f);
            Assert.AreEqual(MapAmbientMath.MaxParallaxOffsetPixels, offset, Tolerance);
            Assert.AreEqual(-MapAmbientMath.MaxParallaxOffsetPixels, MapAmbientMath.ParallaxOffset(-100000f, 1.12f), Tolerance);
        }

        [Test]
        public void ParallaxFactors_AreDefinedForEveryCloudAndAboveOne()
        {
            Assert.AreEqual(MapAmbientMath.CloudCount, MapAmbientMath.CloudParallaxFactors.Length);
            foreach (float factor in MapAmbientMath.CloudParallaxFactors)
                Assert.Greater(factor, 1f);
        }
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

Ожидается: провал компиляции — `ParallaxOffset` не существует.

- [ ] **Step 3: Дописать математику**

В `Assets/UI/Map/MapAmbientMath.cs` добавить:

```csharp
        /// <summary>
        /// Насколько быстрее карты движется каждое облако. Больше единицы —
        /// облако «ближе к камере». Порядок совпадает с порядком в
        /// <see cref="CloudDriftPeriodsSeconds"/>.
        /// </summary>
        public static readonly float[] CloudParallaxFactors = { 1.04f, 1.06f, 1.10f, 1.12f };

        /// <summary>
        /// Потолок параллакс-смещения. Без него на максимальном зуме, где пан
        /// исчисляется сотнями пикселей, облака уехали бы из композиции
        /// целиком — а они часть рисунка карты, а не свободный слой.
        /// </summary>
        public const float MaxParallaxOffsetPixels = 40f;

        /// <summary>Смещение облака относительно карты при данном пане и его множителе глубины.</summary>
        public static float ParallaxOffset(float pan, float factor)
        {
            if (!IsFinite(pan) || !IsFinite(factor))
                return 0f;

            float offset = pan * (factor - 1f);
            if (offset > MaxParallaxOffsetPixels)
                return MaxParallaxOffsetPixels;
            if (offset < -MaxParallaxOffsetPixels)
                return -MaxParallaxOffsetPixels;
            return offset;
        }
```

- [ ] **Step 4: Прогнать тесты математики**

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

Ожидается: все PASS.

- [ ] **Step 5: Открыть текущий пан на чтение**

В `Assets/UI/Map/MapPanZoomController.cs`, рядом со свойством `CurrentZoom`, добавить:

```csharp
        /// <summary>Текущий горизонтальный пан в пикселях — читается ambient-слоем для параллакса облаков (см. MapAmbientController).</summary>
        public float CurrentPanX => _panX;

        /// <summary>Текущий вертикальный пан в пикселях — см. <see cref="CurrentPanX"/>.</summary>
        public float CurrentPanY => _panY;
```

- [ ] **Step 6: Сложить дрейф и параллакс в одну запись**

В `Assets/UI/Map/MapAmbientController.cs` добавить поле и разрешение контроллера пана:

```csharp
        private MapPanZoomController _panZoom;
```

в `ResolveScreenElements` дописать (в конец метода):

```csharp
            _panZoom = null;
            foreach (MapPanZoomController candidate in GetComponents<MapPanZoomController>())
            {
                if (candidate.ScreenId == screenId)
                {
                    _panZoom = candidate;
                    break;
                }
            }
```

и заменить тело цикла в `TickClouds` на версию, складывающую оба вклада в одно `translate`:

```csharp
            float panX = _panZoom?.CurrentPanX ?? 0f;
            float panY = _panZoom?.CurrentPanY ?? 0f;

            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                VisualElement cloud = _clouds[i];
                if (cloud == null)
                    continue;

                MapAmbientMath.CloudDrift(i, _elapsedSeconds, width, height,
                    out float dx, out float dy, out float dOpacity);

                float factor = MapAmbientMath.CloudParallaxFactors[i];
                dx += MapAmbientMath.ParallaxOffset(panX, factor);
                dy += MapAmbientMath.ParallaxOffset(panY, factor);

                cloud.style.translate = new Translate(dx, dy);
                // Клампим: у правого облака прозрачность покоя ровно 1.00
                // (см. MapCloudLayout), и без ограничения верхняя половина
                // синуса упиралась бы в потолок — облако умело бы только
                // темнеть, но не светлеть, то есть дышало бы вполсилы.
                cloud.style.opacity = Mathf.Clamp01(_cloudRestOpacity[i] + dOpacity);
            }
```

- [ ] **Step 7: Прогнать тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

Ожидается: все PASS.

- [ ] **Step 8: Визуальная проверка**

Пройтись пальцем по карте влево-вправо. Облака должны заметно опережать карту, создавая ощущение слоёв. Если облака начинают вылезать из композиции на максимальном зуме — уменьшить `MaxParallaxOffsetPixels` до 28.

- [ ] **Step 9: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): параллакс облаков относительно карты"
```

---

## Task 4: Ambient-камера — дыхание бумаги и Ken Burns

Самое дешёвое движение из всех: одна трансформация на весь канвас. Здесь же вводится правило «единственный писатель», без которого ambient и пан дрались бы за одну трансформацию.

**Files:**
- Modify: `Assets/UI/Map/MapAmbientMath.cs`
- Modify: `Assets/UI/Map/Tests/MapAmbientMathTests.cs`
- Modify: `Assets/UI/Map/MapPanZoomController.cs`
- Create: `Assets/UI/Map/Tests/MapPanZoomControllerAmbientSourceTests.cs`
- Modify: `Assets/UI/Map/MapAmbientController.cs`

**Interfaces:**
- Consumes: `MapAmbientMath.Breath`, `MapAmbientMath.Wave`.
- Produces:
  - `MapAmbientMath.PaperBreathPeriodSeconds = 24f`, `PaperBreathAmplitude = 0.006f`.
  - `MapAmbientMath.IdleDelaySeconds = 5f`, `KenBurnsFadeSeconds = 0.4f`.
  - `MapAmbientMath.ApproachWeight(float current, float target, float deltaSeconds, float fadeSeconds) -> float`.
  - `MapAmbientMath.KenBurns(float timeSeconds, float viewportWidth, float viewportHeight, out float panX, out float panY, out float zoomDelta)`.
  - `MapPanZoomController.SetAmbientOffset(float panX, float panY, float zoomMultiplier)`.
  - `MapPanZoomController.SecondsSinceLastInput -> float`.

- [ ] **Step 1: Написать падающие тесты**

Дописать в `Assets/UI/Map/Tests/MapAmbientMathTests.cs`:

```csharp
        [Test]
        public void ApproachWeight_MovesTowardTargetAndArrives()
        {
            float w = MapAmbientMath.ApproachWeight(0f, 1f, 0.2f, 0.4f);
            Assert.AreEqual(0.5f, w, Tolerance);

            w = MapAmbientMath.ApproachWeight(w, 1f, 0.2f, 0.4f);
            Assert.AreEqual(1f, w, Tolerance);
        }

        [Test]
        public void ApproachWeight_NeverLeavesZeroOne()
        {
            Assert.AreEqual(0f, MapAmbientMath.ApproachWeight(0f, 0f, 1f, 0.4f), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.ApproachWeight(1f, 1f, 1f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.ApproachWeight(0.1f, 0f, 10f, 0.4f), Tolerance);
        }

        [Test]
        public void ApproachWeight_IsSafeOnZeroFade()
        {
            Assert.AreEqual(1f, MapAmbientMath.ApproachWeight(0f, 1f, 0.016f, 0f), Tolerance);
        }

        [Test]
        public void KenBurns_StartsAtRestAndStaysInsideItsBudget()
        {
            MapAmbientMath.KenBurns(0f, 1000f, 500f, out float x0, out float y0, out float z0);
            Assert.AreEqual(0f, x0, Tolerance);
            Assert.AreEqual(0f, y0, Tolerance);
            Assert.AreEqual(0f, z0, Tolerance);

            for (int step = 0; step <= 200; step++)
            {
                MapAmbientMath.KenBurns(step * 0.5f, 1000f, 500f, out float x, out float y, out float z);
                Assert.LessOrEqual(System.Math.Abs(x), 1000f * MapAmbientMath.KenBurnsPanAmplitude + Tolerance);
                Assert.LessOrEqual(System.Math.Abs(y), 500f * MapAmbientMath.KenBurnsPanAmplitude + Tolerance);
                Assert.LessOrEqual(System.Math.Abs(z), MapAmbientMath.KenBurnsZoomAmplitude + Tolerance);
            }
        }

        [Test]
        public void PaperBreath_IsAtMostSixPromille()
        {
            Assert.AreEqual(0.006f, MapAmbientMath.PaperBreathAmplitude, Tolerance);
            for (int step = 0; step <= 100; step++)
            {
                float v = MapAmbientMath.Breath(step * 0.5f, MapAmbientMath.PaperBreathPeriodSeconds, MapAmbientMath.PaperBreathAmplitude);
                Assert.GreaterOrEqual(v, 1f - Tolerance);
                Assert.LessOrEqual(v, 1.006f + Tolerance);
            }
        }
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

Ожидается: провал компиляции.

- [ ] **Step 3: Дописать математику камеры**

В `Assets/UI/Map/MapAmbientMath.cs` добавить:

```csharp
        /// <summary>Период «дыхания бумаги» — очень длинный специально: это должно чувствоваться телом, а не читаться глазом.</summary>
        public const float PaperBreathPeriodSeconds = 24f;

        /// <summary>Амплитуда дыхания бумаги как множитель зума: 1.000 - 1.006.</summary>
        public const float PaperBreathAmplitude = 0.006f;

        /// <summary>Сколько секунд без ввода до включения Ken Burns.</summary>
        public const float IdleDelaySeconds = 5f;

        /// <summary>За сколько Ken Burns набирает полную силу и гаснет при касании. Гашение через вес, а не мгновенным нулём — иначе на касании был бы рывок.</summary>
        public const float KenBurnsFadeSeconds = 0.4f;

        /// <summary>Период дрейфа камеры в простое.</summary>
        public const float KenBurnsPeriodSeconds = 40f;

        /// <summary>Доля вьюпорта, на которую камера уходит в простое.</summary>
        public const float KenBurnsPanAmplitude = 0.008f;

        /// <summary>Добавка к множителю зума в простое.</summary>
        public const float KenBurnsZoomAmplitude = 0.015f;

        /// <summary>
        /// Плавно ведёт вес эффекта к цели со скоростью «полный ход за
        /// <paramref name="fadeSeconds"/>». Используется, чтобы Ken Burns
        /// затухал при касании за 0.4 с, а не обрывался кадром.
        /// </summary>
        public static float ApproachWeight(float current, float target, float deltaSeconds, float fadeSeconds)
        {
            float from = IsFinite(current) ? Clamp01(current) : 0f;
            float to = IsFinite(target) ? Clamp01(target) : 0f;
            if (!IsFinite(deltaSeconds) || deltaSeconds <= 0f)
                return from;
            if (!IsFinite(fadeSeconds) || fadeSeconds <= 0f)
                return to;

            float step = deltaSeconds / fadeSeconds;
            if (to > from)
                return from + step >= to ? to : from + step;
            return from - step <= to ? to : from - step;
        }

        /// <summary>
        /// Смещение камеры в простое. Горизонталь и вертикаль идут разными
        /// периодами, поэтому траектория — медленная петля, а не движение
        /// по прямой туда-обратно. В нуле времени всё ровно по нулям, чтобы
        /// включение эффекта не давало скачка.
        /// </summary>
        public static void KenBurns(float timeSeconds, float viewportWidth, float viewportHeight,
            out float panX, out float panY, out float zoomDelta)
        {
            panX = 0f;
            panY = 0f;
            zoomDelta = 0f;

            if (!IsFinite(viewportWidth) || !IsFinite(viewportHeight) || viewportWidth <= 0f || viewportHeight <= 0f)
                return;

            panX = viewportWidth * KenBurnsPanAmplitude * Wave(timeSeconds, KenBurnsPeriodSeconds, 0f);
            panY = viewportHeight * KenBurnsPanAmplitude * Wave(timeSeconds, KenBurnsPeriodSeconds * 1.618f, 0f);
            zoomDelta = KenBurnsZoomAmplitude * Wave(timeSeconds, KenBurnsPeriodSeconds * 1.31f, 0f);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
```

- [ ] **Step 4: Прогнать тесты математики**

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

Ожидается: все PASS.

- [ ] **Step 5: Написать падающий тест «единственного писателя»**

Создать `Assets/UI/Map/Tests/MapPanZoomControllerAmbientSourceTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Трансформацию ".pan-canvas" пишет ровно один класс —
    /// MapPanZoomController. Ambient-слой отдаёт ему аддитивное смещение и
    /// множитель зума через SetAmbientOffset, а не пишет канвасу сам: два
    /// писателя одной трансформации дрались бы за неё каждый кадр.
    /// </summary>
    public class MapPanZoomControllerAmbientSourceTests
    {
        private const string ControllerPath = "Assets/UI/Map/MapPanZoomController.cs";
        private const string AmbientPath = "Assets/UI/Map/MapAmbientController.cs";

        [Test]
        public void PanZoomControllerAcceptsAnAmbientOffset()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("public void SetAmbientOffset(", source);
            StringAssert.Contains("public float SecondsSinceLastInput", source);
        }

        [Test]
        public void PanZoomControllerFunnelsEveryWriteThroughOneMethod()
        {
            string source = File.ReadAllText(ControllerPath);
            StringAssert.Contains("private void ApplyCanvasTransform()", source);
            Assert.AreEqual(1, CountOccurrences(source, "_canvas.transform.position ="),
                "Позиция канваса должна писаться ровно в одном месте.");
            Assert.AreEqual(1, CountOccurrences(source, "_canvas.transform.scale ="),
                "Масштаб канваса должен писаться ровно в одном месте.");
        }

        [Test]
        public void AmbientControllerNeverTouchesTheCanvasTransformItself()
        {
            string source = File.ReadAllText(AmbientPath);
            StringAssert.DoesNotContain("_canvas.transform", source);
            StringAssert.Contains("SetAmbientOffset(", source);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            int index = haystack.IndexOf(needle, System.StringComparison.Ordinal);
            while (index >= 0)
            {
                count++;
                index = haystack.IndexOf(needle, index + needle.Length, System.StringComparison.Ordinal);
            }
            return count;
        }
    }
}
```

- [ ] **Step 6: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapPanZoomControllerAmbientSourceTests" --filter_type testName --format json
```

Ожидается: провал — `SetAmbientOffset` не существует.

- [ ] **Step 7: Свести запись трансформации в один метод**

В `Assets/UI/Map/MapPanZoomController.cs` добавить поля рядом с `_zoom`:

```csharp
        private float _ambientPanX;
        private float _ambientPanY;
        private float _ambientZoomMultiplier = 1f;
        private float _lastInputTime;
```

заменить `SetPan` и `ApplyZoom` на версии, которые считают своё состояние и делегируют запись:

```csharp
        private void SetPan(float x, float y)
        {
            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;

            _panX = MapPanZoomMath.ClampPan(x, _zoom, viewportWidth);
            _panY = MapPanZoomMath.ClampPan(y, _zoom, viewportHeight);

            ApplyCanvasTransform();
        }

        private void ApplyZoom() => ApplyCanvasTransform();

        /// <summary>
        /// ЕДИНСТВЕННОЕ место, которое пишет трансформацию канваса. Логический
        /// пан/зум (управление игрока) и ambient-добавка (дыхание бумаги, Ken
        /// Burns) складываются здесь, а не спорят за transform: ambient
        /// сознательно не клампится вместе с паном, его амплитуда заведомо
        /// меньше процента и вылезти за край карты не может.
        /// </summary>
        private void ApplyCanvasTransform()
        {
            if (_canvas == null)
                return;

            _canvas.transform.position = new Vector3(_panX + _ambientPanX, _panY + _ambientPanY, 0f);
            float scale = _zoom * _ambientZoomMultiplier;
            _canvas.transform.scale = new Vector3(scale, scale, 1f);
        }

        /// <summary>
        /// Ambient-добавка к камере: смещение в пикселях и множитель зума.
        /// Вызывается MapAmbientController на каждом его тике; логический
        /// пан/зум при этом не меняется, поэтому ни клампы, ни сохранённая
        /// рамка перехода между экранами не съезжают.
        /// </summary>
        public void SetAmbientOffset(float panX, float panY, float zoomMultiplier)
        {
            _ambientPanX = float.IsNaN(panX) ? 0f : panX;
            _ambientPanY = float.IsNaN(panY) ? 0f : panY;
            _ambientZoomMultiplier = float.IsNaN(zoomMultiplier) || zoomMultiplier <= 0f ? 1f : zoomMultiplier;
            ApplyCanvasTransform();
        }

        /// <summary>Сколько секунд прошло с последнего действия игрока — ambient включает Ken Burns только в простое.</summary>
        public float SecondsSinceLastInput => Time.unscaledTime - _lastInputTime;

        /// <summary>
        /// Отмечает «игрок только что действовал». Вызывается со ВСЕХ путей
        /// реального ввода, включая продолжение уже идущего жеста — а не только
        /// его начало. Иначе непрерывный пан или пинч длиннее IdleDelaySeconds
        /// пересёк бы порог простоя прямо под пальцем, и Ken Burns начал бы
        /// уводить камеру поверх активного жеста игрока.
        /// </summary>
        private void MarkInput() => _lastInputTime = Time.unscaledTime;
```

Вызвать `MarkInput()` из всех путей реального ввода:

- в начале `OnPointerDown`;
- в начале `OnWheel`;
- в ветке старта пинча в `Update()` **и в ветке его продолжения** — там, где применяется `SetZoom(_pinchStartZoom * ratio)`;
- в `OnPointerMove` — **безусловно на каждом кадре перетаскивания**, а не только при пересечении порога.

Отдельно вызвать `MarkInput()` в `ResetTransform()`. Без этого счётчик простоя считается от запуска приложения, а не от входа на экран: `_lastInputTime` по умолчанию равен нулю, и к моменту, когда игрок доберётся до карты через заставку и меню, порог простоя давно пройден — Ken Burns включился бы сразу при появлении экрана, поверх вводного зума.

- [ ] **Step 8: Подключить камеру в драйвере**

В `Assets/UI/Map/MapAmbientController.cs` добавить поле:

```csharp
        private float _kenBurnsWeight;
```

и метод, вызываемый из `Tick()` сразу после `TickClouds()`:

```csharp
            TickCamera();
```

```csharp
        /// <summary>
        /// Дыхание бумаги идёт всегда, Ken Burns — только в простое и с
        /// плавным набором/гашением веса. Обе добавки уезжают в
        /// MapPanZoomController одним вызовом: канвас пишет только он.
        /// </summary>
        private void TickCamera()
        {
            if (_panZoom == null)
                return;

            float width = _canvas?.resolvedStyle.width ?? 0f;
            float height = _canvas?.resolvedStyle.height ?? 0f;

            float target = _panZoom.SecondsSinceLastInput >= MapAmbientMath.IdleDelaySeconds ? 1f : 0f;
            _kenBurnsWeight = MapAmbientMath.ApproachWeight(
                _kenBurnsWeight, target, TickIntervalMs / 1000f, MapAmbientMath.KenBurnsFadeSeconds);

            MapAmbientMath.KenBurns(_elapsedSeconds, width, height,
                out float kenPanX, out float kenPanY, out float kenZoom);

            float breath = MapAmbientMath.Breath(
                _elapsedSeconds, MapAmbientMath.PaperBreathPeriodSeconds, MapAmbientMath.PaperBreathAmplitude);

            _panZoom.SetAmbientOffset(
                kenPanX * _kenBurnsWeight,
                kenPanY * _kenBurnsWeight,
                breath + kenZoom * _kenBurnsWeight);
        }
```

и в `StopTicking()` перед сбросом добавить возврат камеры в покой:

```csharp
            _panZoom?.SetAmbientOffset(0f, 0f, 1f);
```

- [ ] **Step 9: Прогнать все тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

Ожидается: все PASS. Особое внимание — существующие тесты пан/зума и перехода между экранами: рефакторинг записи трансформации не должен их тронуть.

- [ ] **Step 10: Визуальная проверка**

Войти на карту и не трогать её десять секунд: камера должна почти незаметно поплыть. Коснуться — движение должно погаснуть плавно, без рывка. Проверить, что переход Япония→Окинава по-прежнему бесшовный, а пан и зум не «уплывают» после отпускания.

- [ ] **Step 11: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): дыхание бумаги и Ken Burns в простое"
```

---

## Task 5: Инерция пана

Самый весомый пункт всего плана по вкладу в ощущение: резкий стоп читается как прототип сильнее, чем полное отсутствие анимации.

**Files:**
- Modify: `Assets/UI/Map/MapAmbientMath.cs`
- Modify: `Assets/UI/Map/Tests/MapAmbientMathTests.cs`
- Modify: `Assets/UI/Map/MapPanZoomController.cs`

**Interfaces:**
- Produces:
  - `MapAmbientMath.DecayVelocity(float velocity, float deltaSeconds) -> float`.
  - `MapAmbientMath.BlendVelocity(float previous, float sample) -> float`.
  - `MapAmbientMath.IsInertiaFinished(float velocityX, float velocityY) -> bool`.

- [ ] **Step 1: Написать падающие тесты**

Дописать в `Assets/UI/Map/Tests/MapAmbientMathTests.cs`:

```csharp
        [Test]
        public void DecayVelocity_LosesTheSameFractionEverySecond()
        {
            float afterOne = MapAmbientMath.DecayVelocity(1000f, 1f);
            Assert.AreEqual(1000f * MapAmbientMath.InertiaRemainingPerSecond, afterOne, 0.01f);

            float afterTwo = MapAmbientMath.DecayVelocity(afterOne, 1f);
            Assert.AreEqual(1000f * MapAmbientMath.InertiaRemainingPerSecond * MapAmbientMath.InertiaRemainingPerSecond, afterTwo, 0.01f);
        }

        [Test]
        public void DecayVelocity_IsIndependentOfStepSize()
        {
            float oneStep = MapAmbientMath.DecayVelocity(1000f, 0.5f);
            float twoSteps = MapAmbientMath.DecayVelocity(MapAmbientMath.DecayVelocity(1000f, 0.25f), 0.25f);
            Assert.AreEqual(oneStep, twoSteps, 0.01f);
        }

        [Test]
        public void IsInertiaFinished_TripsBelowTheStopSpeed()
        {
            Assert.IsTrue(MapAmbientMath.IsInertiaFinished(0f, 0f));
            Assert.IsTrue(MapAmbientMath.IsInertiaFinished(10f, 10f));
            Assert.IsFalse(MapAmbientMath.IsInertiaFinished(500f, 0f));
        }

        [Test]
        public void BlendVelocity_FollowsTheLatestSampleButSmoothsSpikes()
        {
            float blended = MapAmbientMath.BlendVelocity(0f, 1000f);
            Assert.Greater(blended, 0f);
            Assert.Less(blended, 1000f, "Одиночный выброс не должен целиком становиться скоростью броска.");
        }
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

- [ ] **Step 3: Дописать математику инерции**

В `Assets/UI/Map/MapAmbientMath.cs` добавить:

```csharp
        /// <summary>Какая доля скорости броска остаётся через секунду. Подобрано так, чтобы карта доезжала примерно за полсекунды и не «ехала вечно».</summary>
        public const float InertiaRemainingPerSecond = 0.06f;

        /// <summary>Ниже этой скорости (пикселей в секунду) инерция считается законченной и гасится, чтобы карта не дрожала на околонулевых значениях.</summary>
        public const float InertiaStopSpeedPixelsPerSecond = 40f;

        /// <summary>Вес последнего замера в сглаживании скорости пальца.</summary>
        public const float VelocitySampleWeight = 0.6f;

        /// <summary>Затухание скорости за произвольный шаг времени. Экспонента, а не вычитание, — иначе результат зависел бы от частоты кадров.</summary>
        public static float DecayVelocity(float velocity, float deltaSeconds)
        {
            if (!IsFinite(velocity))
                return 0f;
            if (!IsFinite(deltaSeconds) || deltaSeconds <= 0f)
                return velocity;
            return velocity * (float)System.Math.Pow(InertiaRemainingPerSecond, deltaSeconds);
        }

        /// <summary>
        /// Сглаживание скорости пальца: одиночный дёрганый замер не должен
        /// целиком становиться скоростью броска — на тач-экране такие выбросы
        /// обычны и дают «выстрел» карты вместо броска.
        /// </summary>
        public static float BlendVelocity(float previous, float sample)
        {
            float prev = IsFinite(previous) ? previous : 0f;
            float next = IsFinite(sample) ? sample : 0f;
            return prev * (1f - VelocitySampleWeight) + next * VelocitySampleWeight;
        }

        /// <summary>Достаточно ли скорость упала, чтобы остановиться.</summary>
        public static bool IsInertiaFinished(float velocityX, float velocityY)
        {
            if (!IsFinite(velocityX) || !IsFinite(velocityY))
                return true;
            double speed = System.Math.Sqrt(velocityX * (double)velocityX + velocityY * (double)velocityY);
            return speed < InertiaStopSpeedPixelsPerSecond;
        }
```

- [ ] **Step 4: Прогнать тесты математики**

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

Ожидается: все PASS.

- [ ] **Step 5: Копить скорость и доезжать после отпускания**

В `Assets/UI/Map/MapPanZoomController.cs` добавить поля:

```csharp
        private float _velocityX;
        private float _velocityY;
        private Vector2 _lastMovePosition;
        private float _lastMoveTime;
        private bool _inertiaActive;
```

в `OnPointerMove`, сразу после `SetPan(...)`, добавить накопление скорости:

```csharp
            float now = Time.unscaledTime;
            float dt = now - _lastMoveTime;
            if (dt > 0.001f && dt < 0.25f)
            {
                Vector2 step = current - _lastMovePosition;
                _velocityX = MapAmbientMath.BlendVelocity(_velocityX, step.x / dt);
                _velocityY = MapAmbientMath.BlendVelocity(_velocityY, step.y / dt);
            }
            _lastMovePosition = current;
            _lastMoveTime = now;
```

добавить помощник, гасящий инерцию:

```csharp
        /// <summary>
        /// Гасит инерцию НАСМЕРТЬ. Вызывается отовсюду, где камера ставится
        /// заново дискретно, а не движется пальцем: новое касание, старт пинча,
        /// сброс при входе на экран и перенос вида при межэкранном переходе.
        ///
        /// <para>
        /// Проверки в Update() инерцию только ПРИОСТАНАВЛИВАЮТ, а не гасят.
        /// Поэтому бросок карты прямо перед переходом оставил бы скорость
        /// взведённой, и на первом же кадре после перехода камера поехала бы
        /// поверх свежепоставленной рамки — движение, которого игрок не просил.
        /// </para>
        /// </summary>
        private void StopInertia()
        {
            _velocityX = 0f;
            _velocityY = 0f;
            _inertiaActive = false;
        }
```

в `OnPointerDown` погасить инерцию и начать замер заново:

```csharp
            StopInertia();
            _lastMovePosition = evt.position;
            _lastMoveTime = Time.unscaledTime;
```

Вызвать `StopInertia()` также из **ветки старта пинча** в `Update()` (второй палец посреди перетаскивания — это тоже новый жест), из `ResetTransform()`, из `SetViewToSourceFocalPoint()` и в начале `AnimateViewToSourceFocalPoint()`. Последние три — пути, которыми `MapCloudTransitionController` ставит камеру назначения при переходе Япония↔Окинава.

Заодно вызвать его из `OnWheel`: зум колесом должен обрывать доезд, как и касание.

в `EndDrag()` — запустить инерцию, если бросок был настоящим:

```csharp
            if (_dragging && !MapAmbientMath.IsInertiaFinished(_velocityX, _velocityY))
                _inertiaActive = true;
```

(строку поставить в самое начало метода, до сброса `_dragging`).

В `Update()`, сразу после проверки `if (!_bound || MapCloudTransitionController.IsTransitioning) return;`, добавить шаг инерции:

```csharp
            if (_inertiaActive && !_pointerDown && !_isPinching)
            {
                float dt = Time.unscaledDeltaTime;
                SetPan(_panX + _velocityX * dt, _panY + _velocityY * dt);
                _velocityX = MapAmbientMath.DecayVelocity(_velocityX, dt);
                _velocityY = MapAmbientMath.DecayVelocity(_velocityY, dt);
                if (MapAmbientMath.IsInertiaFinished(_velocityX, _velocityY))
                    _inertiaActive = false;
            }
```

- [ ] **Step 6: Прогнать тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

- [ ] **Step 7: Визуальная проверка**

Бросить карту пальцем: она должна доехать и мягко остановиться, а не встать колом. Проверить, что бросок в упёртую границу не даёт дрожания, что новое касание мгновенно перехватывает управление, и что после доезда Ken Burns включается как обычно.

- [ ] **Step 8: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): инерция пана после броска"
```

---

## Task 6: Резинка на границах

**Files:**
- Modify: `Assets/UI/Map/MapPanZoomMath.cs`
- Modify: `Assets/UI/Map/Tests/MapPanZoomMathTests.cs`
- Modify: `Assets/UI/Map/MapPanZoomController.cs`

**Interfaces:**
- Produces:
  - `MapPanZoomMath.RubberBand(float overshoot, float viewportDimension) -> float`.
  - `MapPanZoomMath.MaxRubberBandFraction = 0.12f`.

- [ ] **Step 1: Написать падающие тесты**

Дописать в `Assets/UI/Map/Tests/MapPanZoomMathTests.cs`:

```csharp
        [Test]
        public void RubberBand_IsZeroWithoutOvershoot()
        {
            Assert.AreEqual(0f, MapPanZoomMath.RubberBand(0f, 1000f), 0.0005f);
        }

        [Test]
        public void RubberBand_ResistsMoreTheFurtherYouPull()
        {
            float small = MapPanZoomMath.RubberBand(50f, 1000f);
            float large = MapPanZoomMath.RubberBand(500f, 1000f);

            Assert.Less(small, 50f, "Резинка обязана отдавать меньше, чем в неё тянут.");
            Assert.Greater(large, small);
            Assert.Less(large / 500f, small / 50f, "Сопротивление должно расти с натяжением.");
        }

        [Test]
        public void RubberBand_IsCappedAndSymmetric()
        {
            float cap = 1000f * MapPanZoomMath.MaxRubberBandFraction;
            Assert.AreEqual(cap, MapPanZoomMath.RubberBand(100000f, 1000f), 0.0005f);
            Assert.AreEqual(-cap, MapPanZoomMath.RubberBand(-100000f, 1000f), 0.0005f);
        }

        [Test]
        public void RubberBand_IsSafeOnDegenerateViewport()
        {
            Assert.AreEqual(0f, MapPanZoomMath.RubberBand(100f, 0f), 0.0005f);
            Assert.AreEqual(0f, MapPanZoomMath.RubberBand(float.NaN, 1000f), 0.0005f);
        }
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapPanZoomMathTests" --filter_type testName --format json
```

- [ ] **Step 3: Дописать математику резинки**

В `Assets/UI/Map/MapPanZoomMath.cs` добавить:

```csharp
        /// <summary>Дальше этой доли вьюпорта карту не оттянуть ни при каком усилии.</summary>
        public const float MaxRubberBandFraction = 0.12f;

        /// <summary>Жёсткость резинки. Меньше — туже.</summary>
        public const float RubberBandCoefficient = 0.55f;

        /// <summary>
        /// Насколько карта реально уезжает за свою границу, когда игрок тянет
        /// её на <paramref name="overshoot"/> пикселей дальше допустимого.
        /// Отдача убывает с натяжением, поэтому край ощущается упругим, а не
        /// как стена и не как свободный ход.
        /// </summary>
        public static float RubberBand(float overshoot, float viewportDimension)
        {
            if (!IsFinite(overshoot) || !IsFinite(viewportDimension) || viewportDimension <= 0f)
                return 0f;

            float sign = overshoot < 0f ? -1f : 1f;
            float magnitude = overshoot * sign;

            float damped = (1f - 1f / (magnitude * RubberBandCoefficient / viewportDimension + 1f)) * viewportDimension;
            float cap = viewportDimension * MaxRubberBandFraction;
            return sign * (damped > cap ? cap : damped);
        }
```

- [ ] **Step 4: Прогнать тесты математики**

```bash
unity command run_tests --mode EditMode --filter "MapPanZoomMathTests" --filter_type testName --format json
```

Ожидается: все PASS.

- [ ] **Step 5: Разрешить оттяжку только на прямом перетаскивании**

В `Assets/UI/Map/MapPanZoomController.cs` добавить поля:

```csharp
        private float _rubberBandX;
        private float _rubberBandY;
        private Coroutine _rubberBandRoutine;
```

добавить метод оттяжки и вызывать его из `OnPointerMove` вместо прямого `SetPan`:

```csharp
        /// <summary>
        /// Пан во время прямого перетаскивания: за границей карта продолжает
        /// идти, но с сопротивлением. Оттяжка живёт ОТДЕЛЬНО от логического
        /// пана (_panX/_panY остаются законно заклампленными), поэтому ни
        /// зум, ни переход между экранами, ни инерция не наследуют
        /// «нелегальную» позицию.
        /// </summary>
        private void SetPanWithRubberBand(float x, float y)
        {
            float viewportWidth = _viewport?.resolvedStyle.width ?? 0f;
            float viewportHeight = _viewport?.resolvedStyle.height ?? 0f;

            float maxX = MapPanZoomMath.MaxPanForZoom(_zoom, viewportWidth);
            float maxY = MapPanZoomMath.MaxPanForZoom(_zoom, viewportHeight);

            _rubberBandX = MapPanZoomMath.RubberBand(Overshoot(x, maxX), viewportWidth);
            _rubberBandY = MapPanZoomMath.RubberBand(Overshoot(y, maxY), viewportHeight);

            SetPan(x, y);
        }

        private static float Overshoot(float value, float limit)
        {
            if (value > limit)
                return value - limit;
            if (value < -limit)
                return value + limit;
            return 0f;
        }
```

в `ApplyCanvasTransform()` добавить оттяжку к позиции:

```csharp
            _canvas.transform.position = new Vector3(
                _panX + _ambientPanX + _rubberBandX,
                _panY + _ambientPanY + _rubberBandY,
                0f);
```

в `EndDrag()` — вернуть карту, если её оттянули:

```csharp
            if (_rubberBandX != 0f || _rubberBandY != 0f)
            {
                if (_rubberBandRoutine != null)
                    StopCoroutine(_rubberBandRoutine);
                _rubberBandRoutine = StartCoroutine(ReleaseRubberBand());
            }
```

и добавить корутину возврата:

```csharp
        private const float RubberBandReleaseSeconds = 0.28f;

        private IEnumerator ReleaseRubberBand()
        {
            float startX = _rubberBandX;
            float startY = _rubberBandY;
            float elapsed = 0f;

            while (elapsed < RubberBandReleaseSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = MapPanZoomMath.EaseOutCubic(elapsed / RubberBandReleaseSeconds);
                _rubberBandX = Mathf.LerpUnclamped(startX, 0f, t);
                _rubberBandY = Mathf.LerpUnclamped(startY, 0f, t);
                ApplyCanvasTransform();
                yield return null;
            }

            _rubberBandX = 0f;
            _rubberBandY = 0f;
            ApplyCanvasTransform();
            _rubberBandRoutine = null;
        }
```

Вынести гашение оттяжки в помощник по образцу `StopInertia()`:

```csharp
        /// <summary>Гасит оттяжку и останавливает её возврат. Единственное место, обнуляющее эти поля.</summary>
        private void StopRubberBand()
        {
            if (_rubberBandRoutine != null)
            {
                StopCoroutine(_rubberBandRoutine);
                _rubberBandRoutine = null;
            }
            _rubberBandX = 0f;
            _rubberBandY = 0f;
        }
```

Вызвать его из `OnDisable()`, `OnPointerDown`, ветки старта пинча, `ResetTransform()` и `SetViewToSourceFocalPoint()`. Естественное завершение `ReleaseRubberBand()` тоже провести через него, чтобы обнуление жило ровно в одном месте — как у инерции.

> **НЕ вызывать его из `AnimateViewToSourceFocalPoint`**, хотя `StopInertia()` там стоит и соблазн симметрии велик. Разница существенная. Инерция пишет в тот же `_panX`/`_panY`, что и анимация перехода, поэтому непогашенная инерция реально дралась бы с ней за поле — гашение там несущее. Оттяжка же живёт в собственных `_rubberBandX`/`_rubberBandY`, которых эта корутина не касается вовсе: оставленная в покое, она просто доедет до нуля своим чередом, пока пан анимируется. А синхронное обнуление даёт видимый скачок до 12% вьюпорта на первом же кадре — причём на ИСХОДНОМ экране, который игрок ещё видит (фаза approach идёт до подмены). `SetViewToSourceFocalPoint` — другое дело: он работает на ещё не показанном экране назначения, там скачок невидим.

- [ ] **Step 6: Прогнать тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

- [ ] **Step 7: Визуальная проверка**

Дотянуть карту до края и продолжить тянуть: она должна идти с сопротивлением и вернуться, когда палец отпущен. Убедиться, что фон за краем карты при этом не обнажается больше, чем на оттяжку, и что зум после возврата не съехал.

- [ ] **Step 8: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): резинка на границах карты"
```

---

## Task 7: Двойной тап с перелётом

**Files:**
- Modify: `Assets/UI/Map/MapPanZoomMath.cs`
- Modify: `Assets/UI/Map/Tests/MapPanZoomMathTests.cs`
- Modify: `Assets/UI/Map/MapPanZoomController.cs`

**Interfaces:**
- Produces: `MapPanZoomMath.EaseOutBack(float t) -> float`.

- [ ] **Step 1: Написать падающие тесты**

Дописать в `Assets/UI/Map/Tests/MapPanZoomMathTests.cs`:

```csharp
        [Test]
        public void EaseOutBack_StartsAtZeroAndEndsAtOne()
        {
            Assert.AreEqual(0f, MapPanZoomMath.EaseOutBack(0f), 0.0005f);
            Assert.AreEqual(1f, MapPanZoomMath.EaseOutBack(1f), 0.0005f);
        }

        [Test]
        public void EaseOutBack_ActuallyOvershoots()
        {
            float peak = 0f;
            for (int i = 0; i <= 100; i++)
                peak = System.Math.Max(peak, MapPanZoomMath.EaseOutBack(i / 100f));

            Assert.Greater(peak, 1f, "Без перелёта это обычный ease-out, а перелёт здесь и есть смысл.");
            Assert.Less(peak, 1.2f, "Перелёт должен читаться как упругость, а не как промах.");
        }

        [Test]
        public void EaseOutBack_ClampsItsInput()
        {
            Assert.AreEqual(0f, MapPanZoomMath.EaseOutBack(-3f), 0.0005f);
            Assert.AreEqual(1f, MapPanZoomMath.EaseOutBack(4f), 0.0005f);
            Assert.AreEqual(0f, MapPanZoomMath.EaseOutBack(float.NaN), 0.0005f);
        }
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapPanZoomMathTests" --filter_type testName --format json
```

- [ ] **Step 3: Дописать кривую с перелётом**

В `Assets/UI/Map/MapPanZoomMath.cs` добавить:

```csharp
        /// <summary>Величина перелёта в <see cref="EaseOutBack"/> — канонический коэффициент Пеннера, даёт около 10% промаха сверху.</summary>
        public const float BackOvershoot = 1.70158f;

        /// <summary>
        /// Кривая с упругим перелётом: доезжает чуть дальше цели и
        /// возвращается. В отличие от <see cref="EaseOutCubic"/> (вход на
        /// экран, всегда из мёртвой точки) применяется там, где движение
        /// должно ощущаться как отклик на действие игрока — двойной тап,
        /// появление маркеров, раскрытие свитка.
        /// </summary>
        public static float EaseOutBack(float t)
        {
            float clamped = Clamp(IsFinite(t) ? t : 0f, 0f, 1f);
            float inv = clamped - 1f;
            return 1f + (BackOvershoot + 1f) * inv * inv * inv + BackOvershoot * inv * inv;
        }
```

- [ ] **Step 4: Прогнать тесты математики**

```bash
unity command run_tests --mode EditMode --filter "MapPanZoomMathTests" --filter_type testName --format json
```

Ожидается: все PASS.

- [ ] **Step 5: Распознать двойной тап и сыграть зум**

В `Assets/UI/Map/MapPanZoomController.cs` добавить константы и поля:

```csharp
        private const float DoubleTapMaxSeconds = 0.3f;
        private const float DoubleTapMaxDistancePixels = 40f;
        private const float DoubleTapZoomFactor = 1.6f;
        private const float DoubleTapDurationSeconds = 0.34f;

        private float _lastTapTime = -1f;
        private Vector2 _lastTapPosition;
        private Coroutine _doubleTapRoutine;
```

в `OnPointerUp`, до вызова `EndDrag()`, добавить распознавание:

```csharp
            // Тап по кнопке (маркер главы или уровня) не участвует в
            // распознавании — и обязан ОБНУЛИТЬ ожидание, а не просто быть
            // пропущенным. Иначе последовательность «фон -> маркер -> фон»
            // за DoubleTapMaxSeconds спарит третий тап с первым: тап по
            // маркеру окажется для автомата невидимым, и наезд запустится
            // там, где игрок его не просил.
            if (evt.target is Button)
            {
                _lastTapTime = -1f;
            }
            else if (!_dragging)
            {
                Vector2 position = evt.position;
                bool isDoubleTap = _lastTapTime > 0f
                    && Time.unscaledTime - _lastTapTime <= DoubleTapMaxSeconds
                    && Vector2.Distance(position, _lastTapPosition) <= DoubleTapMaxDistancePixels;

                if (isDoubleTap)
                {
                    _lastTapTime = -1f;
                    if (_doubleTapRoutine != null)
                        StopCoroutine(_doubleTapRoutine);
                    _doubleTapRoutine = StartCoroutine(PlayDoubleTapZoom());
                }
                else
                {
                    _lastTapTime = Time.unscaledTime;
                    _lastTapPosition = position;
                }
            }
```

и добавить корутину:

```csharp
        /// <summary>
        /// Зум по двойному тапу: доезжает чуть дальше цели и возвращается
        /// (см. MapPanZoomMath.EaseOutBack). Пивот — центр канваса, тот же,
        /// что у колеса и пинча: зум «в точку тапа» здесь не нужен, карта и
        /// так центрируется на интересном объекте паном.
        /// </summary>
        private IEnumerator PlayDoubleTapZoom()
        {
            CancelIntroZoomAnimation();
            _lastInputTime = Time.unscaledTime;

            float startZoom = _zoom;
            float targetZoom = MapPanZoomMath.ClampZoom(startZoom * DoubleTapZoomFactor);
            if (Mathf.Approximately(startZoom, targetZoom))
            {
                _doubleTapRoutine = null;
                yield break;
            }

            // У потолка зума упругость выключается. EaseOutBack проскакивает
            // цель примерно на 10%, а SetZoom тут же срезает превышение
            // клампом — вместо упругой отдачи читается удар в стену. Пружине
            // не во что упираться на пределе, поэтому доводим ease-out без
            // перелёта. Случай не редкий: это второй двойной тап подряд.
            bool targetIsAtCeiling = Mathf.Approximately(targetZoom, MapPanZoomMath.MaxZoom);

            float elapsed = 0f;
            while (elapsed < DoubleTapDurationSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = elapsed / DoubleTapDurationSeconds;
                float t = targetIsAtCeiling
                    ? MapPanZoomMath.EaseOutCubic(progress)
                    : MapPanZoomMath.EaseOutBack(progress);
                SetZoom(Mathf.LerpUnclamped(startZoom, targetZoom, t));
                yield return null;
            }

            SetZoom(targetZoom);
            _doubleTapRoutine = null;
        }
```

В `OnDisable()` добавить остановку:

```csharp
            if (_doubleTapRoutine != null)
            {
                StopCoroutine(_doubleTapRoutine);
                _doubleTapRoutine = null;
            }
```

- [ ] **Step 6: Прогнать тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

- [ ] **Step 7: Визуальная проверка**

Двойной тап по карте должен давать упругий наезд. Одиночный тап по маркеру должен по-прежнему открывать панель и не запускать зум. На максимальном зуме двойной тап не должен дёргать картинку.

- [ ] **Step 8: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): зум по двойному тапу с перелётом"
```

---

## Task 8: Дыхание маркеров, тень и усиленная цель

Здесь вводится обёртка `__breath` вокруг иконки. Это не косметика структуры, а необходимость: ambient пишет масштаб инлайном каждый тик, а инлайн-стиль перебивает USS — если бы дыхание и состояние выбора писали в один и тот же `scale`, состояние выбора просто не было бы видно.

**Files:**
- Modify: `Assets/UI/MikeyApp.uxml` (три узла главы, девять узлов уровня — `level-node-0`..`level-node-8`)
- Modify: `Assets/UI/Map/Map.uss`
- Modify: `Assets/UI/Map/MapAmbientController.cs`
- Modify: существующие структурные тесты, если они падают на новой разметке

**Interfaces:**
- Consumes: `MapAmbientMath.Breath`, `MapAmbientMath.MarkerBreathPeriodSeconds`, `MarkerBreathAmplitude`, `FocusBreathMultiplier`.
- Produces: разметочный контракт — у каждого узла маркера есть потомки с классами `chapter-node__shadow` / `level-node__shadow` и `chapter-node__breath` / `level-node__breath`, внутри последнего лежит иконка.

- [ ] **Step 1: Дописать константы дыхания и прогнать тест**

В `Assets/UI/Map/MapAmbientMath.cs` добавить:

```csharp
        /// <summary>Период дыхания маркера — заметно быстрее неба: маркер живой объект, а не погода.</summary>
        public const float MarkerBreathPeriodSeconds = 3.2f;

        /// <summary>Амплитуда дыхания маркера как добавка к масштабу.</summary>
        public const float MarkerBreathAmplitude = 0.025f;

        /// <summary>
        /// Во сколько раз сильнее дышит ЕДИНСТВЕННАЯ текущая цель. Остальные
        /// разблокированные маркеры дышат обычной амплитудой, locked не дышат
        /// вовсе.
        ///
        /// <para>
        /// Произведение с <see cref="MarkerBreathAmplitude"/> обязано укладываться
        /// в правило композиции (ambient не больше 4%): 0.025 x 1.4 = 0.035.
        /// Отсюда и амплитуда 0.025, а не 0.035 — с ней усиленная цель давала бы
        /// 0.049 и правило нарушалось. Не поднимать одно, не пересчитав другое.
        /// </para>
        /// </summary>
        public const float FocusBreathMultiplier = 1.4f;
```

дописать в `Assets/UI/Map/Tests/MapAmbientMathTests.cs`:

```csharp
        [Test]
        public void MarkerBreath_RespectsTheAmbientCompositionRule()
        {
            Assert.GreaterOrEqual(MapAmbientMath.MarkerBreathPeriodSeconds, 3f,
                "Правило композиции: ambient не короче трёх секунд.");
            Assert.LessOrEqual(MapAmbientMath.MarkerBreathAmplitude * MapAmbientMath.FocusBreathMultiplier, 0.04f + Tolerance,
                "Правило композиции: ambient не больше четырёх процентов, включая усиленную цель.");
        }
```

```bash
unity command run_tests --mode EditMode --filter "MapAmbientMathTests" --filter_type testName --format json
```

Ожидается: PASS.

- [ ] **Step 2: Перестроить разметку узлов главы**

В `Assets/UI/MikeyApp.uxml` заменить каждый из трёх узлов главы на структуру с тенью и обёрткой дыхания. Для Окинавы:

```xml
                        <ui:Button name="chapter-node-okinawa" class="chapter-node chapter-node--okinawa tap-target-lg">
                            <!-- Тень объявлена первой, чтобы красилась под всем
                                 остальным. Она absolute, поэтому колоночную
                                 раскладку узла не трогает и привязка кончика
                                 пина остаётся прежней. -->
                            <ui:VisualElement class="chapter-node__shadow" picking-mode="Ignore" />
                            <ui:Label text="OKINAWA" class="chapter-node__label" picking-mode="Ignore" />
                            <!-- Обёртка дыхания: ambient пишет scale ИМЕННО ей,
                                 а состояние выбора живёт на самой иконке. Два
                                 вложенных transform перемножаются сами, и
                                 инлайн-стиль ambient не затирает USS выбора. -->
                            <ui:VisualElement class="chapter-node__breath" picking-mode="Ignore">
                                <ui:VisualElement class="chapter-node__icon chapter-node__icon--okinawa" picking-mode="Ignore" />
                            </ui:VisualElement>
                        </ui:Button>
```

Для Фукуоки и Хиросимы — то же самое со своими именами, текстами меток и классами иконок (`chapter-node__icon--fukuoka`, `chapter-node__icon--hiroshima`), сохранив на них класс `chapter-node--locked`.

- [ ] **Step 3: Перестроить разметку узлов уровня**

Каждый из девяти `level-node-N` (`level-node-0`..`level-node-8`, глава Окинава содержит девять миссий LVL 0-8 — см. `MapMarkerLayout.Missions` и `OkinawaMapController.LevelCount`) в `Assets/UI/MikeyApp.uxml` привести к той же форме:

```xml
                        <ui:Button name="level-node-0" class="level-node level-node--0 tap-target-lg">
                            <ui:VisualElement class="level-node__shadow" picking-mode="Ignore" />
                            <ui:Label text="LVL 0" class="level-node__label" picking-mode="Ignore" />
                            <ui:VisualElement class="level-node__breath" picking-mode="Ignore">
                                <ui:VisualElement class="level-node__icon" picking-mode="Ignore" />
                            </ui:VisualElement>
                        </ui:Button>
```

Класс конкретной иконки на узлах уровня проставляется в рантайме `OkinawaMapController` из `MapMarkerLayout.Missions` — в разметке остаётся только базовый `level-node__icon`, как и было.

- [ ] **Step 4: Добавить стили тени и обёртки**

В `Assets/UI/Map/Map.uss`, сразу после блока `.chapter-node__icon`, добавить:

```css
/* Обёртка дыхания. Собственных визуальных свойств не имеет: существует
   ровно для того, чтобы ambient-слой писал непрерывный масштаб СЮДА, а
   USS-состояния (выбран/заблокирован) остались на иконке. Инлайн-стиль
   перебивает USS, поэтому делить одно свойство между ними нельзя. */
.chapter-node__breath {
    align-items: center;
    justify-content: center;
}

/* Тень под кончиком пина: отрывает маркер от бумаги. Эллипс, а не
   изображение — та же техника, что у ореола в топбаре, где блюра нет. */
.chapter-node__shadow {
    position: absolute;
    left: 50%;
    bottom: -5px;
    translate: -50% 0;
    width: 46px;
    height: 12px;
    border-radius: 50%;
    /* Цвет НЕПРОЗРАЧНЫЙ намеренно: видимой альфой владеет только inline
       opacity, которую пишет ambient. UI Toolkit перемножает opacity на
       альфу цвета, поэтому rgba(...,0.35) вместе с opacity 0.35 дал бы
       реальные 0.12 — тень была бы втрое бледнее задуманного, а её дыхание
       практически неразличимо. */
    background-color: rgb(0, 0, 0);
}
```

и симметрично после `.level-node__icon`:

```css
.level-node__breath {
    align-items: center;
    justify-content: center;
}
.level-node__shadow {
    position: absolute;
    left: 50%;
    bottom: -4px;
    translate: -50% 0;
    width: 36px;
    height: 10px;
    border-radius: 50%;
    /* Цвет НЕПРОЗРАЧНЫЙ намеренно: видимой альфой владеет только inline
       opacity, которую пишет ambient. UI Toolkit перемножает opacity на
       альфу цвета, поэтому rgba(...,0.35) вместе с opacity 0.35 дал бы
       реальные 0.12 — тень была бы втрое бледнее задуманного, а её дыхание
       практически неразличимо. */
    background-color: rgb(0, 0, 0);
}
```

- [ ] **Step 5: Прогнать структурные тесты и починить те, что упали**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

Существующие `JapanMapScreenUxmlTests`, `OkinawaMapScreenUxmlTests` и `MapMechanicsAndMarkersUnchangedTests` могут утверждать прежний порядок потомков узла. Там, где тест проверяет, что иконка — прямой потомок узла, поправить его на новую структуру и оставить осмысленное утверждение: иконка лежит внутри обёртки дыхания, метка по-прежнему объявлена раньше иконки, тень объявлена первой. Тесты, проверяющие координаты, классы блокировки и логику маркеров, трогать не нужно — они не зависят от вложенности.

- [ ] **Step 6: Оживить маркеры в драйвере**

В `Assets/UI/Map/MapAmbientController.cs` добавить поля:

```csharp
        private readonly System.Collections.Generic.List<VisualElement> _markerBreaths = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<VisualElement> _markerShadows = new System.Collections.Generic.List<VisualElement>();
        private readonly System.Collections.Generic.List<bool> _markerAlive = new System.Collections.Generic.List<bool>();
        private int _focusMarkerIndex = -1;
```

в конец `ResolveScreenElements` добавить сбор маркеров:

```csharp
            _markerBreaths.Clear();
            _markerShadows.Clear();
            _markerAlive.Clear();
            _focusMarkerIndex = -1;

            string nodeClass = japan ? "chapter-node" : "level-node";
            var nodes = _root?.Query<VisualElement>(className: nodeClass).ToList();
            if (nodes == null)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                VisualElement node = nodes[i];
                VisualElement breath = node.Q<VisualElement>(className: nodeClass + "__breath");
                VisualElement shadow = node.Q<VisualElement>(className: nodeClass + "__shadow");

                if (breath != null)
                    breath.usageHints = UsageHints.DynamicTransform;
                if (shadow != null)
                    shadow.usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor;

                _markerBreaths.Add(breath);
                _markerShadows.Add(shadow);

                // Состояние блокировки читается из класса, а не из отдельного
                // API контроллеров: класс уже есть, он единственный источник
                // правды для внешнего вида, и ambient не заводит вторую копию
                // этого знания. Снимок берётся здесь, при смене экрана, а не
                // на каждом тике — блокировки за время показа экрана не
                // меняются, а тик обязан оставаться дешёвым.
                bool alive = !node.ClassListContains(nodeClass + "--locked");
                _markerAlive.Add(alive);
                if (alive)
                    _focusMarkerIndex = i;
            }
```

и метод, вызываемый из `Tick()` после `TickCamera()`:

```csharp
            TickMarkers();
```

```csharp
        /// <summary>
        /// Дышат только разблокированные маркеры, и ровно один — текущая цель —
        /// дышит сильнее остальных. Заблокированные стоят абсолютно
        /// неподвижно: контраст сам ведёт взгляд, и никакие стрелки-указатели
        /// поверх карты не нужны.
        /// </summary>
        private void TickMarkers()
        {
            for (int i = 0; i < _markerBreaths.Count; i++)
            {
                VisualElement breath = _markerBreaths[i];
                if (breath == null)
                    continue;

                if (!_markerAlive[i])
                {
                    // Заблокированные приведены в покой один раз при смене
                    // экрана (см. ResolveScreenElements) — писать им что-либо
                    // каждый тик незачем, они не меняются.
                    continue;
                }

                float amplitude = MapAmbientMath.MarkerBreathAmplitude
                    * (i == _focusMarkerIndex ? MapAmbientMath.FocusBreathMultiplier : 1f);
                float scale = MapAmbientMath.Breath(_elapsedSeconds, MapAmbientMath.MarkerBreathPeriodSeconds, amplitude);
                breath.style.scale = new Scale(new Vector2(scale, scale));

                VisualElement shadow = _markerShadows[i];
                if (shadow == null)
                    continue;

                // Тень идёт в противофазе: маркер поднимается — тень
                // поджимается и бледнеет. Иначе это читается как рост
                // объекта, а не как отрыв от поверхности. Обе формулы живут в
                // MapAmbientMath, потому что встроенные сюда они не покрывались
                // бы ни одним тестом — именно так сюда и попала ошибка с
                // перемножением альфы.
                float shadowScale = MapAmbientMath.MarkerShadowScale(scale);
                shadow.style.scale = new Scale(new Vector2(shadowScale, shadowScale));
                shadow.style.opacity = MapAmbientMath.MarkerShadowOpacity(scale);
            }
        }
```

> **Приводить в покой при смене экрана надо ВСЕ маркеры, а не только
> заблокированные.** Цвет тени непрозрачен, видимой альфой владеет только inline
> `opacity` — а у живого маркера её никто не задаёт до первого тика. Один кадр
> после показа экрана его тень нарисуется сплошным чёрным. Раньше это случайно
> компенсировалось альфой, вшитой в цвет; после её удаления компенсации нет.
> Живым тик перепишет значение сразу же, так что лишней работы это не создаёт.

- [ ] **Step 7: Прогнать все тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

Ожидается: все PASS.

- [ ] **Step 8: Визуальная проверка**

На японской карте Окинава должна еле заметно дышать вместе с тенью, а Фукуока и Хиросима — стоять мёртво. Взгляд должен сам идти к живому маркеру. На Окинаве дышит только доступный уровень. Если дыхание читается как пульсация — снизить `MarkerBreathAmplitude` до 0.025.

- [ ] **Step 9: Коммит**

```bash
git add Assets/UI/Map Assets/UI/MikeyApp.uxml && git commit -m "feat(map): дыхание маркеров, тень под пином, усиленная текущая цель"
```

---

## Task 9: Каскад появления маркеров

**Files:**
- Modify: `Assets/UI/Map/Map.uss`
- Modify: `Assets/UI/Map/JapanMapController.cs`
- Modify: `Assets/UI/Map/OkinawaMapController.cs`
- Create: `Assets/UI/Map/MapNodeFeedback.cs`
- Create: `Assets/UI/Map/Tests/MapNodeFeedbackSourceTests.cs`

**Interfaces:**
- Produces:
  - `MapNodeFeedback.PlayEntranceCascade(IReadOnlyList<VisualElement> nodes, bool reducedMotion)` — расставляет задержку по индексу и снимает стартовое состояние.
  - USS-классы `map-node--enter`, `map-node--reduced`.

- [ ] **Step 1: Написать падающий тест помощника**

Создать `Assets/UI/Map/Tests/MapNodeFeedbackSourceTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// MapNodeFeedback — одноразовые эффекты маркера (появление каскадом,
    /// отказ у заблокированного, волна от тапа). Отдельно от ambient-драйвера
    /// сознательно: тот про непрерывное движение, эти три — про реакцию на
    /// событие.
    /// </summary>
    public class MapNodeFeedbackSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapNodeFeedback.cs";

        /// <remarks>
        /// Сканирование ТЕКСТА, а не синтаксического дерева: намеренно грубо,
        /// зато не требует парсера и ловит нарушение в любой форме записи.
        /// Обратная сторона — запрещённые имена нельзя упоминать даже в
        /// комментариях проверяемого файла, иначе он уронит сам себя.
        /// </remarks>
        [Test]
        public void NeverWritesLayoutProperties()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("style.left", source);
            StringAssert.DoesNotContain("style.top", source);
            StringAssert.DoesNotContain("style.width", source);
            StringAssert.DoesNotContain("style.height", source);
            StringAssert.DoesNotContain("style.margin", source);
            StringAssert.DoesNotContain("style.padding", source);
            StringAssert.DoesNotContain("style.fontSize", source);
        }

        [Test]
        public void CascadeStaggersByIndex()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("CascadeStepMs", source);
            StringAssert.Contains("transitionDelay", source);
        }

        [Test]
        public void CascadeCollapsesUnderReducedMotion()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("reducedMotion", source);
            StringAssert.Contains("ReducedClass", source);
        }
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapNodeFeedbackSourceTests" --filter_type testName --format json
```

- [ ] **Step 3: Добавить стили каскада**

В `Assets/UI/Map/Map.uss` добавить:

```css
/* ---------- появление маркеров каскадом ----------
   Состояние покоя описано на обёртке дыхания, а не на самом узле: у узла
   translate занят привязкой кончика пина к точке карты (см. ".chapter-node"),
   и трогать его нельзя ни при каких обстоятельствах.

   "ease-out-back" даёт лёгкий перелёт — маркер как будто становится на
   место, а не выплывает. Если версия редактора не понимает эту функцию
   (в консоли будет предупреждение разбора USS), заменить на "ease-out":
   каскад останется, пропадёт только упругость. */
.chapter-node__breath,
.level-node__breath {
    transition-property: translate, opacity, scale;
    transition-duration: 0.32s;
    transition-timing-function: ease-out-back;
}

/* Стартовое состояние каскада: ставится без перехода, снимается следующим
   кадром — тогда переход и проигрывается. */
.map-node--enter .chapter-node__breath,
.map-node--enter .level-node__breath {
    translate: 0 -14px;
    opacity: 0;
    scale: 0.92;
}

/* "Меньше движения": каскад схлопывается в короткое проявление без
   смещения и без перелёта. */
.map-node--reduced .chapter-node__breath,
.map-node--reduced .level-node__breath {
    transition-duration: 0.15s;
    transition-timing-function: ease-out;
}
.map-node--reduced.map-node--enter .chapter-node__breath,
.map-node--reduced.map-node--enter .level-node__breath {
    translate: 0 0;
    scale: 1;
}
```

- [ ] **Step 4: Написать помощника**

Создать `Assets/UI/Map/MapNodeFeedback.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Одноразовые эффекты маркера: появление каскадом, отказ у
    /// заблокированного, волна от тапа. Отдельно от MapAmbientController
    /// (непрерывное движение) и MapCeremonyController (редкие сцены) —
    /// это короткая реакция на действие игрока.
    ///
    /// <para>
    /// Пишет только transform и прозрачность, как и весь остальной
    /// анимационный код карты — см. MapNodeFeedbackSourceTests.
    /// </para>
    /// </summary>
    public static class MapNodeFeedback
    {
        /// <summary>Класс стартового состояния каскада (см. Map.uss ".map-node--enter").</summary>
        public const string EnterClass = "map-node--enter";

        /// <summary>Класс укороченного каскада при включённой настройке «меньше движения».</summary>
        public const string ReducedClass = "map-node--reduced";

        /// <summary>Задержка между соседними маркерами в каскаде.</summary>
        public const int CascadeStepMs = 70;

        /// <summary>
        /// Проигрывает появление маркеров: ставит стартовое состояние, даёт
        /// каждому свою задержку по порядку и снимает состояние следующим
        /// кадром, чтобы переход действительно проигрался (изменение стиля в
        /// том же кадре, что и добавление класса, переход не запускает).
        /// </summary>
        public static void PlayEntranceCascade(IReadOnlyList<VisualElement> nodes, bool reducedMotion)
        {
            if (nodes == null || nodes.Count == 0)
                return;

            for (int i = 0; i < nodes.Count; i++)
            {
                VisualElement node = nodes[i];
                if (node == null)
                    continue;

                VisualElement breath = FindBreath(node);
                if (breath != null)
                {
                    breath.style.transitionDelay = new List<TimeValue>
                    {
                        new TimeValue(reducedMotion ? 0 : i * CascadeStepMs, TimeUnit.Millisecond),
                    };
                }

                node.EnableInClassList(ReducedClass, reducedMotion);
                node.AddToClassList(EnterClass);
            }

            VisualElement first = nodes[0];
            first?.schedule.Execute(() =>
            {
                foreach (VisualElement node in nodes)
                    node?.RemoveFromClassList(EnterClass);
            }).ExecuteLater(0);
        }

        private static VisualElement FindBreath(VisualElement node)
        {
            VisualElement breath = node.Q<VisualElement>(className: "chapter-node__breath");
            return breath ?? node.Q<VisualElement>(className: "level-node__breath");
        }
    }
}
```

- [ ] **Step 5: Позвать каскад при входе на экран**

В `Assets/UI/Map/JapanMapController.cs`, в обработчике `OnScreenChanged` для своего экрана (там, где уже вызывается `ResetToDefaultState()`), добавить:

```csharp
            var motion = GetComponent<Mikey.UI.Settings.IMotionSettings>();
            MapNodeFeedback.PlayEntranceCascade(
                new[] { _okinawaNode, _fukuokaNode, _hiroshimaNode },
                motion != null && motion.ReducedMotion);
```

Порядок массива — юг→север, тот же, что в `MapMarkerLayout.Chapters`: каскад должен идти оттуда, где игрок сейчас, туда, куда он пойдёт.

В `Assets/UI/Map/OkinawaMapController.cs`, в аналогичном месте:

```csharp
            var motion = GetComponent<Mikey.UI.Settings.IMotionSettings>();
            MapNodeFeedback.PlayEntranceCascade(_levelNodes, motion != null && motion.ReducedMotion);
```

- [ ] **Step 6: Прогнать тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

- [ ] **Step 7: Проверить разбор USS**

```bash
unity --json cmd eval 'UnityEditor.AssetDatabase.ImportAsset("Assets/UI/Map/Map.uss", UnityEditor.ImportAssetOptions.ForceUpdate); return "reimported";'
```

Открыть консоль редактора и убедиться, что предупреждений разбора USS нет. Если `ease-out-back` не распознан — заменить на `ease-out` во всех трёх местах блока каскада.

- [ ] **Step 8: Визуальная проверка**

Войти на карту: маркеры должны становиться на места один за другим, снизу вверх, с лёгким перелётом. Уйти на другой экран и вернуться — каскад должен играть снова. Включить «Reduced motion» — маркеры должны просто проявляться.

- [ ] **Step 9: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): каскадное появление маркеров при входе на экран"
```

---

> ### ПЕРЕСМОТР ПОДХОДА ПОСЛЕ РЕВЬЮ
>
> Каскад выше, построенный на USS-переходе с индивидуальной `transition-delay`
> и снятием класса «следующим кадром», **не работает** — и это арифметика, а не
> тонкость движка. Задержка действует в обе стороны. Для маркера с задержкой
> 70 мс переход В стартовое состояние ставится в очередь с этой задержкой, а
> класс снимается примерно через 16 мс, то есть до её истечения: элемент никуда
> не успевает уйти и обратно ехать ему неоткуда. Каскад проиграется только у
> нулевого маркера, у которого задержки нет.
>
> Попытка починить это переносом стагтера в поштучное снятие класса упирается в
> следующую проблему: применение стартового состояния само по себе анимируется,
> и маркеры сперва видимо съёжатся, а потом вернутся.
>
> **Каскад переводится на численный привод в уже существующем тике.** Драйвер
> ambient тикает 30 раз в секунду, владеет элементами маркеров и умеет писать
> трансформации — всё, что нужно, уже есть. Для каждого маркера прогресс входа
> считается как `clamp01((t - i * CascadeStepSeconds) / CascadeDurationSeconds)`,
> прогоняется через `MapPanZoomMath.EaseOutBack` и превращается в масштаб,
> смещение и прозрачность. Никаких классов, никаких отложенных вызовов, никакой
> зависимости от семантики переходов.
>
> Это, кроме надёжности, даёт главное: формулу входа можно покрыть юнит-тестом,
> как и все остальные формулы этого слоя. USS-переход не покрывается ничем — и
> именно поэтому его поломку не поймал ни один из 499 зелёных тестов.
>
> Под «меньше движения» вход схлопывается в проявление одной прозрачностью за
> 0.15 с без смещения и перелёта.
>
> Взаимодействие с дыханием: вход — это МНОЖИТЕЛЬ поверх дыхания, а не смена
> владельца. Итоговый масштаб равен произведению дыхания на масштаб входа,
> который сам стремится к единице. Тогда границы передачи владения не существует
> вовсе, а значит неоткуда взяться и разрыву: при завершении входа множитель
> просто становится единицей. Вариант «сначала владеет каскад, потом дыхание»
> даёт скачок, потому что фаза дыхания не привязана к моменту завершения входа и
> в этот миг равна чему угодно.
>
> **Обязательно: дискретный привод должен доводить до точных значений покоя.**
> Прогресс считается на тиках по 33 мс против длительности 320 мс, поэтому в
> единицу он попадает не ровно, а перепрыгивает её. Если писать прозрачность
> только пока прогресс меньше единицы, значения 1 она не получит НИКОГДА и
> маркер навсегда застынет полупрозрачным — около 90% при обычном движении и
> около 78% при «меньше движения», где длительность короче. Переход через стили
> доезжал бы до цели сам; численный привод обязан это сделать явно. На первом же
> тике, где прогресс достиг единицы, выставить точные значения покоя:
> прозрачность 1, смещение 0, множитель входа 1.
>
> Значение настройки «меньше движения» защёлкивается на момент начала входа и не
> перечитывается каждый тик: иначе переключение настройки посреди входа
> пересчитает прогресс против другой длительности и маркер прыгнет назад.

## Task 10: Приподнимание выбранного и отказ у заблокированного

**Files:**
- Modify: `Assets/UI/Map/Map.uss`
- Modify: `Assets/UI/Map/MapNodeFeedback.cs`
- Modify: `Assets/UI/Map/JapanMapController.cs`
- Modify: `Assets/UI/Map/OkinawaMapController.cs`
- Modify: `Assets/UI/Map/Tests/MapNodeFeedbackSourceTests.cs`

**Interfaces:**
- Produces: `MapNodeFeedback.PlayRefusal(VisualElement node)`.

- [ ] **Step 1: Дописать падающий тест**

В `Assets/UI/Map/Tests/MapNodeFeedbackSourceTests.cs` добавить:

```csharp
        [Test]
        public void RefusalShakesAndSettlesBackToRest()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public static void PlayRefusal(", source);
            StringAssert.Contains("RefusalOffsets", source);
            StringAssert.Contains("RefusalStepMs", source);
        }
```

```bash
unity command run_tests --mode EditMode --filter "MapNodeFeedbackSourceTests" --filter_type testName --format json
```

Ожидается: провал.

- [ ] **Step 2: Обновить стиль выбранного маркера**

В `Assets/UI/Map/Map.uss` заменить существующее правило

```css
.chapter-node--selected .chapter-node__icon {
    scale: 1.08;
}
```

на

```css
/* Выбранный маркер приподнимается, а его тень расходится и бледнеет —
   вместе это читается как «поднялся над картой». Живёт на иконке, а не на
   обёртке дыхания: ту непрерывно пишет ambient инлайном, и USS там не
   победит. */
.chapter-node__icon,
.level-node__icon {
    transition-property: translate, scale;
    transition-duration: 0.18s;
    transition-timing-function: ease-out;
}
.chapter-node--selected .chapter-node__icon {
    translate: 0 -6px;
    scale: 1.10;
}
```

и симметрично заменить `.level-node--selected .level-node__icon { scale: 1.1; }` на:

```css
.level-node--selected .level-node__icon {
    translate: 0 -6px;
    scale: 1.10;
}
```

- [ ] **Step 3: Написать отказ**

В `Assets/UI/Map/MapNodeFeedback.cs` добавить:

```csharp
        /// <summary>Шаг между кадрами дрожи отказа.</summary>
        public const int RefusalStepMs = 70;

        /// <summary>
        /// Кадры дрожи в пикселях. Затухающие и заканчиваются нулём: маркер
        /// обязан вернуться ровно на своё место, иначе кончик пина уедет с
        /// точки на карте.
        /// </summary>
        public static readonly float[] RefusalOffsets = { 6f, -6f, 3f, 0f };

        /// <summary>
        /// Короткая дрожь при тапе по заблокированному маркеру. До этого тап
        /// по locked не давал вообще ничего, и это читалось как «кнопка
        /// сломана», а не «сюда пока нельзя».
        ///
        /// <para>
        /// Пишется на обёртку дыхания, а не на узел (у него translate занят
        /// привязкой пина) и не на иконку (там живёт состояние выбора).
        /// Ambient в этот момент маркер не трогает — заблокированные не
        /// дышат, — поэтому конфликта записи нет.
        /// </para>
        /// </summary>
        public static void PlayRefusal(VisualElement node)
        {
            VisualElement breath = node == null ? null : FindBreath(node);
            if (breath == null)
                return;

            for (int i = 0; i < RefusalOffsets.Length; i++)
            {
                float offset = RefusalOffsets[i];
                breath.schedule
                    .Execute(() => breath.style.translate = new Translate(offset, 0f))
                    .ExecuteLater(i * RefusalStepMs);
            }
        }
```

- [ ] **Step 4: Позвать отказ из контроллеров**

В `Assets/UI/Map/JapanMapController.cs`, в `ToggleChapter`, перед тем как показать панель заблокированной главы, добавить:

```csharp
            if (node != null && node.ClassListContains(LockedNodeClass))
                MapNodeFeedback.PlayRefusal(node);
```

В `Assets/UI/Map/OkinawaMapController.cs`, в обработчике клика по уровню, там же где вычисляется `IsLevelLocked(index)`:

```csharp
            if (IsLevelLocked(index) && _levelNodes[index] != null)
                MapNodeFeedback.PlayRefusal(_levelNodes[index]);
```

- [ ] **Step 5: Прогнать тесты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

- [ ] **Step 6: Визуальная проверка**

Тап по Окинаве: маркер приподнимается, тень расходится, панель выезжает. Тап по Фукуоке: короткая дрожь, маркер возвращается ровно на своё место — проверить это отдельно, кончик пина не должен съехать. Убедиться, что дыхание живого маркера продолжается и после выбора.

- [ ] **Step 7: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): приподнимание выбранного маркера и отказ у заблокированного"
```

---

## Task 11: Волна от тапа и звук печати

**Files:**
- Modify: `Assets/UI/MikeyApp.uxml` (элемент волны в каждом узле маркера)
- Modify: `Assets/UI/Map/Map.uss`
- Modify: `Assets/UI/Map/MapNodeFeedback.cs`
- Modify: `Assets/UI/Map/JapanMapController.cs`
- Modify: `Assets/UI/Map/OkinawaMapController.cs`
- Modify: `Assets/UI/Map/Mikey.UI.Map.asmdef` (ссылка `Mikey.UI.Audio`)
- Modify: `Assets/UI/Audio/AudioController.cs`

**Interfaces:**
- Produces:
  - `MapNodeFeedback.PlayRipple(VisualElement node)`.
  - `AudioController.PlaySealStamp()`.

- [ ] **Step 1: Добавить элемент волны в каждый узел**

В `Assets/UI/MikeyApp.uxml` в каждый из трёх узлов главы и девяти узлов уровня (`level-node-0`..`level-node-8`) добавить кольцо первым потомком, до тени:

```xml
                            <ui:VisualElement class="chapter-node__ripple" picking-mode="Ignore" />
```

(в узлах уровня — `level-node__ripple`).

- [ ] **Step 2: Добавить стиль волны**

В `Assets/UI/Map/Map.uss` добавить:

```css
/* Волна от тапа: кольцо расходится от кончика пина и гаснет. В покое
   прозрачность 0 и масштаб мал, поэтому элемент ничего не стоит и ничего
   не перехватывает (picking-mode="Ignore" в разметке). */
.chapter-node__ripple,
.level-node__ripple {
    position: absolute;
    left: 50%;
    bottom: -6px;
    translate: -50% 0;
    width: 64px;
    height: 64px;
    border-radius: 50%;
    border-width: 2px;
    border-color: #C62828;
    opacity: 0;
    scale: 0.4;
    transition-property: opacity, scale;
    transition-duration: 0.5s;
    transition-timing-function: ease-out;
}
.chapter-node--rippling .chapter-node__ripple,
.level-node--rippling .level-node__ripple {
    opacity: 0;
    scale: 2.2;
}
```

Начальный всплеск прозрачности ставится из кода (см. следующий шаг), потому что переход от 0 к 0 через промежуточный пик одним правилом USS не описывается.

- [ ] **Step 3: Написать волну**

В `Assets/UI/Map/MapNodeFeedback.cs` добавить:

```csharp
        /// <summary>Пиковая прозрачность кольца в начале волны.</summary>
        public const float RipplePeakOpacity = 0.45f;

        /// <summary>
        /// Волна от тапа по маркеру. Кольцо мгновенно ставится в начальное
        /// состояние (виден, мал), после чего класс запускает переход в
        /// «большой и прозрачный». Возврат в исходное состояние делается
        /// через отложенное снятие класса, а не второй анимацией — иначе на
        /// быстрых повторных тапах кольца накладывались бы друг на друга.
        /// </summary>
        public static void PlayRipple(VisualElement node)
        {
            if (node == null)
                return;

            bool chapter = node.ClassListContains("chapter-node");
            string rippleClass = chapter ? "chapter-node__ripple" : "level-node__ripple";
            string activeClass = chapter ? "chapter-node--rippling" : "level-node--rippling";

            VisualElement ripple = node.Q<VisualElement>(className: rippleClass);
            if (ripple == null)
                return;

            node.RemoveFromClassList(activeClass);
            ripple.style.opacity = RipplePeakOpacity;
            ripple.style.scale = new Scale(new Vector2(0.4f, 0.4f));

            ripple.schedule.Execute(() => node.AddToClassList(activeClass)).ExecuteLater(0);
            ripple.schedule.Execute(() => node.RemoveFromClassList(activeClass)).ExecuteLater(520);
        }
```

Дописать в начало файла `using UnityEngine;` для типа `Vector2`.

- [ ] **Step 4: Добавить звук печати**

В `Assets/UI/Audio/AudioController.cs` рядом с полем клипа клика добавить:

```csharp
        [Tooltip("Глухой удар печати — выбор главы на карте. Пока клип не назначен, метод молча ничего не делает.")]
        [SerializeField] private AudioClip sealStampClip;
```

и метод рядом с `PlayUiClick`:

```csharp
        /// <summary>Играет удар печати на текущей громкости эффектов. Безопасно вызывать без назначенного клипа.</summary>
        public void PlaySealStamp()
        {
            if (_sfxSource != null && sealStampClip != null)
                _sfxSource.PlayOneShot(sealStampClip, _settings.SfxVolume);
        }
```

Клипа для печати в проекте сейчас нет (в `Assets/UI/Audio/SFX/` лежит только `ui_button_press.mp3`), поэтому поле остаётся пустым, а вызовы безвредны. Назначение реального звука — отдельная задача владельца по контенту; код к ней уже готов.

- [ ] **Step 5: Добавить ссылку на сборку звука**

В `Assets/UI/Map/Mikey.UI.Map.asmdef` в `references` добавить `"Mikey.UI.Audio"`.

- [ ] **Step 6: Позвать волну и звук из контроллеров**

В `Assets/UI/Map/JapanMapController.cs`, в `SelectChapter`, сразу после `node.AddToClassList(SelectedNodeClass);`:

```csharp
            MapNodeFeedback.PlayRipple(node);
            GetComponent<Mikey.UI.Audio.AudioController>()?.PlaySealStamp();
```

В `Assets/UI/Map/OkinawaMapController.cs`, там же где ставится `SelectedNodeClass`:

```csharp
            MapNodeFeedback.PlayRipple(_levelNodes[index]);
            GetComponent<Mikey.UI.Audio.AudioController>()?.PlaySealStamp();
```

- [ ] **Step 7: Прогнать тесты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

- [ ] **Step 8: Визуальная проверка**

Тап по доступному маркеру: от кончика пина расходится и гаснет красное кольцо. Быстрые повторные тапы не должны наслаивать кольца друг на друга. Звука пока нет — это ожидаемо и записано в шаге 4.

- [ ] **Step 9: Коммит**

```bash
git add Assets/UI/Map Assets/UI/Audio Assets/UI/MikeyApp.uxml && git commit -m "feat(map): волна от тапа по маркеру и точка подключения звука печати"
```

---

## Task 12: Свиток для попапа уровня

**Files:**
- Modify: `Assets/UI/MikeyApp.uxml` (перестройка `level-panel`)
- Modify: `Assets/UI/Map/Map.uss`
- Modify: `Assets/UI/Map/OkinawaMapController.cs`
- Create: `Assets/UI/Map/Tests/MapScrollPanelUxmlTests.cs`

**Interfaces:**
- Produces: разметочный контракт свитка — `level-panel` содержит `level-panel-paper` с классом `scroll-panel__paper`, внутри которого лежит весь контент и валик `scroll-panel__rod`.

- [ ] **Step 1: Написать падающий тест разметки**

Создать `Assets/UI/Map/Tests/MapScrollPanelUxmlTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Свиток попапа уровня. Ключевой контракт — раскрытие идёт СМЕЩЕНИЕМ
    /// бумаги внутри контейнера с overflow:hidden, а не анимацией высоты:
    /// высота — это лэйаут каждый кадр, ровно то, чего весь дизайн избегает.
    /// Вариант «scaleY от нуля» тоже отвергнут — он сплющивал бы текст.
    /// </summary>
    public class MapScrollPanelUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        [Test]
        public void LevelPanelWrapsItsContentInPaper()
        {
            string uxml = File.ReadAllText(UxmlPath);
            StringAssert.Contains("name=\"level-panel-paper\"", uxml);
            StringAssert.Contains("scroll-panel__paper", uxml);
            StringAssert.Contains("scroll-panel__rod", uxml);
        }

        [Test]
        public void ScrollOpensByTranslateNotHeight()
        {
            string uss = File.ReadAllText(UssPath);
            StringAssert.Contains(".scroll-panel__paper", uss);
            StringAssert.Contains("transition-property: translate", uss);
        }

        [Test]
        public void ScrollPanelClipsItsPaper()
        {
            string uss = File.ReadAllText(UssPath);
            StringAssert.Contains(".detail-panel--scroll", uss);
            StringAssert.Contains("overflow: hidden", uss);
        }
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapScrollPanelUxmlTests" --filter_type testName --format json
```

- [ ] **Step 3: Перестроить разметку попапа уровня**

В `Assets/UI/MikeyApp.uxml` обернуть содержимое `level-panel` в бумагу и добавить валик в её низ:

```xml
                <!-- Попап уровня раскрывается как свиток: контейнер стоит на
                     месте и обрезает, а «бумага» внутри съезжает вниз из-под
                     верхнего края вместе с валиком. Высота при этом не
                     анимируется никогда — это был бы полный проход лэйаута
                     каждый кадр. -->
                <ui:VisualElement name="level-panel" class="detail-panel detail-panel--scroll" picking-mode="Ignore">
                    <ui:VisualElement name="level-panel-paper" class="scroll-panel__paper" picking-mode="Ignore">
                        <ui:VisualElement class="detail-panel__content">
                            <ui:Label name="level-panel-eyebrow" text="LEVEL" class="detail-panel__eyebrow" />
                            <ui:Label name="level-panel-title" text="LVL 0" class="detail-panel__title" />
                            <ui:Label name="level-panel-subtitle" text="ASSESSMENT" class="detail-panel__subtitle" />
                            <ui:Label name="level-panel-desc" text="Measure your starting ability and learn how Mikey evaluates your movement." class="detail-panel__desc" />
                            <ui:Button name="level-panel-cta" class="detail-panel__cta tap-target-lg">
                                <ui:Label name="level-panel-cta-text" text="BEGIN" class="detail-panel__cta-text" picking-mode="Ignore" />
                            </ui:Button>
                        </ui:VisualElement>
                        <ui:VisualElement class="scroll-panel__rod" picking-mode="Ignore" />
                    </ui:VisualElement>
                </ui:VisualElement>
```

Остальные потомки `level-panel`, которые были в нём до правки (метки и мета-строки), перенести внутрь `detail-panel__content` без изменения имён — имена читает `OkinawaMapController` и они должны остаться прежними.

- [ ] **Step 4: Добавить стили свитка**

В `Assets/UI/Map/Map.uss` добавить:

```css
/* ---------- свиток (попап уровня) ----------
   Внешний контейнер НЕ ездит: он стоит на месте и обрезает. Едет бумага
   внутри. Поэтому здесь снимается выездной translate базовой панели. */
.detail-panel--scroll {
    translate: 0 0;
    overflow: hidden;
    transition-property: opacity;
    transition-duration: 0.2s;
    background-color: rgba(0, 0, 0, 0);
    border-left-width: 0;
}
.detail-panel--scroll .detail-panel__content {
    flex-grow: 1;
}

/* Бумага: закрытая полностью выведена за верхний край собственного
   контейнера, открытая стоит на месте. Перелёт "ease-out-back" читается как
   разворачивание, а не как выезд ящика. Если версия редактора не понимает
   эту функцию (предупреждение разбора USS в консоли) — заменить на
   "ease-out". */
.scroll-panel__paper {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background-color: var(--base-2);
    border-left-width: 1px;
    border-left-color: var(--line);
    translate: 0 -100%;
    transition-property: translate;
    transition-duration: 0.42s;
    transition-timing-function: ease-out-back;
}
.detail-panel--open .scroll-panel__paper {
    translate: 0 0;
}
/* Закрытие короче и без перелёта: свиток сворачивается собранно. */
.detail-panel--scroll:not(.detail-panel--open) .scroll-panel__paper {
    transition-duration: 0.26s;
    transition-timing-function: ease-in;
}

/* Валик внизу бумаги. Едет вместе с ней, поэтому собственной анимации не
   имеет — одна анимируемая сущность вместо двух. */
.scroll-panel__rod {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 0;
    height: 14px;
    border-radius: 7px;
    background-color: #2A2118;
    border-top-width: 1px;
    border-top-color: rgba(237, 230, 216, 0.18);
}

/* Каскад содержимого: проявляется после того, как бумага в основном
   развернулась. */
.scroll-panel__paper .detail-panel__eyebrow,
.scroll-panel__paper .detail-panel__title,
.scroll-panel__paper .detail-panel__subtitle,
.scroll-panel__paper .detail-panel__desc,
.scroll-panel__paper .detail-panel__cta {
    opacity: 0;
    translate: 0 8px;
    transition-property: opacity, translate;
    transition-duration: 0.22s;
    transition-timing-function: ease-out;
}
.detail-panel--revealed .detail-panel__eyebrow {
    opacity: 1;
    translate: 0 0;
    transition-delay: 0.20s;
}
.detail-panel--revealed .detail-panel__title {
    opacity: 1;
    translate: 0 0;
    transition-delay: 0.24s;
}
.detail-panel--revealed .detail-panel__subtitle {
    opacity: 1;
    translate: 0 0;
    transition-delay: 0.28s;
}
.detail-panel--revealed .detail-panel__desc {
    opacity: 1;
    translate: 0 0;
    transition-delay: 0.32s;
}
.detail-panel--revealed .detail-panel__cta {
    opacity: 1;
    translate: 0 0;
    transition-delay: 0.36s;
}
```

- [ ] **Step 5: Управлять классом проявления из контроллера**

В `Assets/UI/Map/OkinawaMapController.cs` добавить константу рядом с существующими:

```csharp
        private const string PanelRevealedClass = "detail-panel--revealed";
```

в месте, где панель открывается (`_panel.AddToClassList(PanelOpenClass);`), добавить следующей строкой:

```csharp
            _panel.AddToClassList(PanelRevealedClass);
```

и в месте закрытия (`_panel.RemoveFromClassList(PanelOpenClass);`):

```csharp
            _panel.RemoveFromClassList(PanelRevealedClass);
```

- [ ] **Step 6: Прогнать тесты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

Ожидается: все PASS, включая существующие `OkinawaMapScreenUxmlTests` — если они утверждают, что метки лежат прямо в `level-panel`, поправить их на новую вложенность.

- [ ] **Step 7: Проверить разбор USS и поведение при закрытой панели**

```bash
unity --json cmd eval 'UnityEditor.AssetDatabase.ImportAsset("Assets/UI/Map/Map.uss", UnityEditor.ImportAssetOptions.ForceUpdate); return "reimported";'
```

Убедиться в консоли, что предупреждений разбора нет. Отдельно проверить, что закрытый свиток не перехватывает касания: контейнер теперь всегда на экране, и его `picking-mode` должен оставаться `Ignore`, пока панель закрыта.

- [ ] **Step 8: Визуальная проверка**

Тап по доступному уровню: бумага разворачивается сверху вниз с валиком по нижнему краю, слегка перелетает и встаёт, следом по одному проявляются строки. Закрытие — быстрее и без перелёта. Текст в процессе не должен сплющиваться ни на кадр.

- [ ] **Step 9: Коммит**

```bash
git add Assets/UI/Map Assets/UI/MikeyApp.uxml && git commit -m "feat(map): попап уровня раскрывается свитком"
```

---

## Task 13: Панель главы — пружина, каскад, уход карты вглубь

**Files:**
- Modify: `Assets/UI/Map/Map.uss`
- Modify: `Assets/UI/Map/JapanMapController.cs`

**Interfaces:**
- Consumes: класс `detail-panel--revealed` из задачи 12 (тот же механизм каскада).
- Produces: USS-класс `pan-stage--pushed`.

- [ ] **Step 1: Добавить пружину и уход карты**

В `Assets/UI/Map/Map.uss` изменить базовую панель, добавив перелёт (правило `.detail-panel` уже существует — заменить в нём только функцию сглаживания):

```css
    transition-timing-function: ease-out-back;
```

и добавить новый блок:

```css
/* Карта уходит вглубь, пока открыта панель главы: масштаб на самой сцене
   (одна трансформация на весь экран, не на каждом элементе) плюс более
   плотный скрим. Без этого панель читается как наклейка поверх картинки, а
   не как слой над сценой. */
.pan-stage {
    transition-property: scale;
    transition-duration: 0.34s;
    transition-timing-function: ease-out;
}
.pan-stage--pushed {
    scale: 0.985;
}
.pan-canvas-scrim {
    transition-property: background-color;
    transition-duration: 0.34s;
    transition-timing-function: ease-out;
}
.pan-stage--pushed .pan-canvas-scrim {
    background-color: rgba(8, 10, 12, 0.32);
}
```

- [ ] **Step 2: Переключать состояние из контроллера**

В `Assets/UI/Map/JapanMapController.cs` добавить константу и поле:

```csharp
        private const string PanelRevealedClass = "detail-panel--revealed";
        private const string StagePushedClass = "pan-stage--pushed";

        private VisualElement _stage;
```

в привязке добавить получение сцены рядом с остальными элементами:

```csharp
            _stage = root.Q<VisualElement>("map-stage");
```

в месте открытия панели (`_panel.AddToClassList(PanelOpenClass);`) добавить:

```csharp
            _panel.AddToClassList(PanelRevealedClass);
            _stage?.AddToClassList(StagePushedClass);
```

в месте закрытия (`_panel.RemoveFromClassList(PanelOpenClass);`):

```csharp
            _panel.RemoveFromClassList(PanelRevealedClass);
            _stage?.RemoveFromClassList(StagePushedClass);
```

- [ ] **Step 3: Прогнать тесты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

- [ ] **Step 4: Визуальная проверка**

Тап по Окинаве: панель выезжает с лёгким перелётом, карта позади чуть отступает и темнеет, строки панели проявляются по очереди. Закрыть панель — карта возвращается. Убедиться, что масштаб сцены не ломает пан и зум: подвигать карту при открытой панели.

- [ ] **Step 5: Коммит**

```bash
git add Assets/UI/Map && git commit -m "feat(map): пружина и каскад в панели главы, уход карты вглубь"
```

---

## Task 14: Церемонии

Последняя задача. Две церемонии из четырёх подключаются к настоящим событиям, две — поставляются с публичным входом и отладочным триггером, потому что источника события для них в прогрессии пока не существует.

**Files:**
- Create: `Assets/UI/Map/MapCeremonyController.cs`
- Create: `Assets/UI/Map/Tests/MapCeremonyControllerSourceTests.cs`
- Modify: `Assets/UI/MikeyApp.uxml` (оверлей ink-wash на обоих экранах карты)
- Modify: `Assets/UI/Map/Map.uss`
- Modify: `Assets/UI/Map/MapAmbientController.cs` (уступать церемонии)
- Modify: `Assets/UI/Map/MapCloudTransitionController.cs` (клякса в момент подмены экранов)
- Modify: `Assets/UI/Map/Tests/MapControllersSceneTests.cs`
- Modify: `Assets/Scenes/SampleScene.unity`

**Interfaces:**
- Produces:
  - `MapCeremonyController.IsPlaying` (static bool) — ambient его проверяет так же, как уже проверяет `MapCloudTransitionController.IsTransitioning`.
  - `MapCeremonyController.PlayMapEntryInkWash()`.
  - `MapCeremonyController.PlayTransitionBlot()`.
  - `MapCeremonyController.PlayChapterUnlock(string chapterId)`.
  - `MapCeremonyController.PlayLevelCompleteStamp(int index)`.

- [ ] **Step 1: Написать падающий тест**

Создать `Assets/UI/Map/Tests/MapCeremonyControllerSourceTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Контроллер одноразовых сцен карты. Две из них подключены к реальным
    /// событиям (вход на карту, переход между главами), две ждут события,
    /// которого в прогрессии пока нет: состояние блокировки глав захардкожено
    /// в MapMarkerLayout.Chapters, а TutorialProgressState про главы карты
    /// ничего не знает. Поэтому у них публичный вход и отладочный триггер, а
    /// не имитация автоматической работы.
    /// </summary>
    public class MapCeremonyControllerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapCeremonyController.cs";
        private const string AmbientPath = "Assets/UI/Map/MapAmbientController.cs";

        /// <remarks>
        /// Сканирование ТЕКСТА, а не синтаксического дерева: намеренно грубо,
        /// зато не требует парсера и ловит нарушение в любой форме записи.
        /// Обратная сторона — запрещённые имена нельзя упоминать даже в
        /// комментариях проверяемого файла, иначе он уронит сам себя.
        /// </remarks>
        [Test]
        public void NeverWritesLayoutProperties()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("style.left", source);
            StringAssert.DoesNotContain("style.top", source);
            StringAssert.DoesNotContain("style.width", source);
            StringAssert.DoesNotContain("style.height", source);
            StringAssert.DoesNotContain("style.margin", source);
            StringAssert.DoesNotContain("style.padding", source);
            StringAssert.DoesNotContain("style.fontSize", source);
        }

        [Test]
        public void ExposesEveryCeremonyEntryPoint()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("public void PlayMapEntryInkWash()", source);
            StringAssert.Contains("public void PlayTransitionBlot()", source);
            StringAssert.Contains("public void PlayChapterUnlock(string chapterId)", source);
            StringAssert.Contains("public void PlayLevelCompleteStamp(int index)", source);
        }

        [Test]
        public void UnwiredCeremoniesCarryADebugTrigger()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.Contains("ContextMenu", source);
        }

        [Test]
        public void AmbientYieldsWhileACeremonyPlays()
        {
            string source = File.ReadAllText(AmbientPath);
            StringAssert.Contains("MapCeremonyController.IsPlaying", source);
        }
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter "MapCeremonyControllerSourceTests" --filter_type testName --format json
```

- [ ] **Step 3: Добавить оверлей ink-wash в разметку**

В `Assets/UI/MikeyApp.uxml` на обоих экранах карты, сразу после существующего `map-transition-overlay` (и его аналога на Окинаве), добавить:

```xml
                <!-- Проявление карты при входе: три чернильных пятна расходятся
                     и гаснут. Существует только на время церемонии — в покое
                     полностью прозрачен и не участвует ни в отрисовке, ни в
                     попадании, поэтому постоянного полноэкранного слоя это не
                     добавляет. -->
                <ui:VisualElement name="map-inkwash" class="map-inkwash" picking-mode="Ignore">
                    <ui:VisualElement class="map-inkwash__blot map-inkwash__blot--a" picking-mode="Ignore" />
                    <ui:VisualElement class="map-inkwash__blot map-inkwash__blot--b" picking-mode="Ignore" />
                    <ui:VisualElement class="map-inkwash__blot map-inkwash__blot--c" picking-mode="Ignore" />
                </ui:VisualElement>
```

На экране Окинавы дать элементу имя `okinawa-inkwash`, классы те же.

- [ ] **Step 4: Добавить стили церемоний**

В `Assets/UI/Map/Map.uss` добавить:

```css
/* ---------- ink-wash при входе на карту ----------
   В покое полностью прозрачен: слоя как такового нет. Пятна расходятся
   масштабом и гаснут прозрачностью — ни одного геометрического свойства. */
.map-inkwash {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    opacity: 0;
}
.map-inkwash--playing {
    opacity: 1;
}
.map-inkwash__blot {
    position: absolute;
    border-radius: 50%;
    background-color: var(--base);
    scale: 1;
    opacity: 1;
    transition-property: scale, opacity;
    transition-duration: 0.6s;
    transition-timing-function: ease-out;
}
.map-inkwash--dissolving .map-inkwash__blot {
    scale: 1.8;
    opacity: 0;
}
.map-inkwash__blot--a {
    left: -20%;
    top: -30%;
    width: 90%;
    height: 140%;
}
.map-inkwash__blot--b {
    left: 30%;
    top: -20%;
    width: 90%;
    height: 140%;
}
.map-inkwash__blot--c {
    left: 10%;
    top: 10%;
    width: 90%;
    height: 130%;
}

/* ---------- печать церемонии ----------
   Одна и та же печать используется и для разблокировки главы, и для штампа
   «пройдено» на маркере уровня — разница только в том, кто её вызвал. */
.map-seal {
    position: absolute;
    left: 50%;
    top: 50%;
    translate: -50% -50%;
    width: 220px;
    height: 220px;
    border-radius: 50%;
    border-width: 6px;
    border-color: #C62828;
    opacity: 0;
    scale: 1.7;
    transition-property: scale, opacity;
    transition-duration: 0.3s;
    transition-timing-function: ease-out-back;
}
.map-seal--stamped {
    opacity: 1;
    scale: 1;
}
```

- [ ] **Step 5: Написать контроллер церемоний**

Создать `Assets/UI/Map/MapCeremonyController.cs`:

```csharp
using System.Collections;
using Mikey.UI.SafeArea;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Одноразовые сцены карты. Отделены от MapAmbientController сознательно:
    /// у них противоположная природа. Ambient крутится вечно и обязан быть
    /// незаметным; церемония играет один раз и обязана быть заметной.
    ///
    /// <para>
    /// <b>Что подключено к настоящим событиям:</b> проявление карты при входе
    /// (ScreenChanged).
    /// </para>
    ///
    /// <para>
    /// <b>Что ждёт события:</b> разблокировка главы и штамп «пройдено».
    /// Источника у них сегодня нет — состояние блокировки глав захардкожено в
    /// <see cref="MapMarkerLayout.Chapters"/>, а
    /// <see cref="Mikey.UI.Progression.TutorialProgressState"/> про главы
    /// карты ничего не знает. Обе поставляются готовыми, с публичным входом и
    /// отладочным триггером в инспекторе: подключение к реальному событию —
    /// отдельная задача, когда прогрессия начнёт его отдавать.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MapCeremonyController : MonoBehaviour
    {
        private const int MaxRootResolveFrames = 30;

        private const string JapanScreenId = "map";
        private const string OkinawaScreenId = "mapOkinawa";

        private const string InkWashPlayingClass = "map-inkwash--playing";
        private const string InkWashDissolvingClass = "map-inkwash--dissolving";
        private const string SealStampedClass = "map-seal--stamped";

        private const float InkWashSeconds = 0.6f;
        private const float CloudPartSeconds = 0.7f;
        private const float SealSeconds = 0.3f;
        private const float CloudReturnSeconds = 0.9f;

        /// <summary>
        /// Истина на всё время любой церемонии. Ambient проверяет этот флаг и
        /// уступает — иначе он и церемония писали бы облакам одно и то же
        /// свойство. Тот же приём, что уже применён в
        /// MapCloudTransitionController.IsTransitioning.
        /// </summary>
        public static bool IsPlaying { get; private set; }

        private VisualElement _root;
        private IScreenNavigator _navigator;
        private Coroutine _bindRoutine;
        private Coroutine _ceremonyRoutine;
        private bool _bound;
        private bool _inkWashPlayed;

        private void OnEnable()
        {
            if (_bound)
                return;
            _bindRoutine = StartCoroutine(BindWhenReady());
        }

        private void OnDisable()
        {
            if (_bindRoutine != null)
            {
                StopCoroutine(_bindRoutine);
                _bindRoutine = null;
            }
            if (_ceremonyRoutine != null)
            {
                StopCoroutine(_ceremonyRoutine);
                _ceremonyRoutine = null;
            }
            if (_navigator != null)
            {
                _navigator.ScreenChanged -= OnScreenChanged;
                _navigator = null;
            }

            IsPlaying = false;
            _root = null;
            _bound = false;
        }

        private IEnumerator BindWhenReady()
        {
            var document = GetComponent<UIDocument>();

            int frames = 0;
            while (document.rootVisualElement == null)
            {
                if (++frames > MaxRootResolveFrames)
                {
                    Debug.LogError("[MapCeremonyController] UIDocument root unavailable; ceremonies not bound.", this);
                    _bindRoutine = null;
                    yield break;
                }
                yield return null;
            }

            _root = document.rootVisualElement;

            _navigator = GetComponent<IScreenNavigator>();
            if (_navigator != null)
                _navigator.ScreenChanged += OnScreenChanged;

            _bound = true;
            _bindRoutine = null;
        }

        private void OnScreenChanged(string screenId)
        {
            if (screenId != JapanScreenId || _inkWashPlayed)
                return;
            _inkWashPlayed = true;
            PlayMapEntryInkWash();
        }

        /// <summary>Проявление карты чернильным размывом — играет один раз за сессию, при первом входе на мировую карту.</summary>
        public void PlayMapEntryInkWash()
        {
            VisualElement inkWash = _root?.Q<VisualElement>("map-inkwash");
            if (inkWash == null)
                return;

            StartCeremony(PlayInkWashRoutine(inkWash));
        }

        /// <summary>
        /// Сцена разблокировки главы: облака расходятся от её маркера, печать
        /// впечатывается, облака возвращаются. Реального триггера сегодня нет
        /// (см. описание класса) — вызывается вручную и из отладочного пункта
        /// контекстного меню.
        /// </summary>
        public void PlayChapterUnlock(string chapterId)
        {
            if (!MapMarkerLayout.TryGetChapterFocalPoint(chapterId, out float sourceX, out float sourceY))
            {
                Debug.LogWarning($"[MapCeremonyController] Unknown chapter '{chapterId}'; unlock ceremony skipped.", this);
                return;
            }

            StartCeremony(PlayChapterUnlockRoutine(sourceX, sourceY));
        }

        /// <summary>Штамп «пройдено» на маркере уровня. Реального триггера сегодня нет — см. описание класса.</summary>
        public void PlayLevelCompleteStamp(int index)
        {
            VisualElement node = _root?.Q<VisualElement>($"level-node-{index}");
            if (node == null)
                return;

            StartCeremony(PlaySealRoutine(node));
        }

        [ContextMenu("Debug: Play chapter unlock (Fukuoka)")]
        public void DebugPlayChapterUnlock() => PlayChapterUnlock(MapMarkerLayout.FukuokaChapterId);

        [ContextMenu("Debug: Play level complete stamp (LVL 1)")]
        public void DebugPlayLevelStamp() => PlayLevelCompleteStamp(1);

        private void StartCeremony(IEnumerator routine)
        {
            if (IsPlaying || !_bound)
                return;
            _ceremonyRoutine = StartCoroutine(RunCeremony(routine));
        }

        private IEnumerator RunCeremony(IEnumerator routine)
        {
            IsPlaying = true;
            yield return routine;
            IsPlaying = false;
            _ceremonyRoutine = null;
        }

        private IEnumerator PlayInkWashRoutine(VisualElement inkWash)
        {
            inkWash.RemoveFromClassList(InkWashDissolvingClass);
            inkWash.AddToClassList(InkWashPlayingClass);
            yield return null;

            inkWash.AddToClassList(InkWashDissolvingClass);
            yield return new WaitForSecondsRealtime(InkWashSeconds);

            inkWash.RemoveFromClassList(InkWashPlayingClass);
            inkWash.RemoveFromClassList(InkWashDissolvingClass);
        }

        private IEnumerator PlayChapterUnlockRoutine(float sourceX, float sourceY)
        {
            VisualElement layer = _root?.Q<VisualElement>("map-cloud-layer");
            if (layer == null)
                yield break;

            // Облака расходятся от точки главы. Пишется translate и
            // прозрачность слоя целиком — раскладка отдельных облаков
            // (MapCloudLayout) при этом не трогается вообще.
            float directionX = (sourceX - 0.5f) < 0f ? -1f : 1f;
            float elapsed = 0f;
            while (elapsed < CloudPartSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = MapPanZoomMath.EaseOutCubic(elapsed / CloudPartSeconds);
                layer.style.translate = new Translate(directionX * 0.08f * 1000f * t, 0f);
                layer.style.opacity = 1f - 0.25f * t;
                yield return null;
            }

            VisualElement node = _root.Q<VisualElement>("chapter-node-fukuoka");
            yield return PlaySealRoutine(node);

            elapsed = 0f;
            while (elapsed < CloudReturnSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = MapPanZoomMath.EaseInOutCubic(elapsed / CloudReturnSeconds);
                layer.style.translate = new Translate(directionX * 0.08f * 1000f * (1f - t), 0f);
                layer.style.opacity = 0.75f + 0.25f * t;
                yield return null;
            }

            layer.style.translate = new Translate(0f, 0f);
            layer.style.opacity = 1f;
        }

        private IEnumerator PlaySealRoutine(VisualElement anchor)
        {
            if (anchor == null)
                yield break;

            var seal = new VisualElement();
            seal.AddToClassList("map-seal");
            seal.pickingMode = PickingMode.Ignore;
            seal.usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor;
            anchor.Add(seal);

            yield return null;
            seal.AddToClassList(SealStampedClass);
            GetComponent<Mikey.UI.Audio.AudioController>()?.PlaySealStamp();

            yield return new WaitForSecondsRealtime(SealSeconds);
            yield return new WaitForSecondsRealtime(0.6f);

            seal.RemoveFromHierarchy();
        }
    }
}
```

- [ ] **Step 6: Добавить кляксу на переходе между главами**

В `Assets/UI/Map/MapCeremonyController.cs` добавить метод рядом с `PlayMapEntryInkWash`:

```csharp
        /// <summary>
        /// Короткая чернильная клякса в момент подмены экранов на переходе
        /// Япония-Окинава. Сам переход — честная кинематографичная камера
        /// (см. MapCloudTransitionController), и накрывать её целиком нечем;
        /// клякса лишь маскирует кадр подмены. Живёт 0.3 с, поэтому не
        /// поднимает IsPlaying надолго и ambient не успевает застыть.
        /// </summary>
        public void PlayTransitionBlot()
        {
            VisualElement inkWash = _root?.Q<VisualElement>("map-inkwash");
            if (inkWash == null)
                return;

            StartCeremony(PlayBlotRoutine(inkWash));
        }

        private IEnumerator PlayBlotRoutine(VisualElement inkWash)
        {
            inkWash.RemoveFromClassList(InkWashDissolvingClass);
            inkWash.AddToClassList(InkWashPlayingClass);
            yield return null;

            inkWash.AddToClassList(InkWashDissolvingClass);
            yield return new WaitForSecondsRealtime(TransitionBlotSeconds);

            inkWash.RemoveFromClassList(InkWashPlayingClass);
            inkWash.RemoveFromClassList(InkWashDissolvingClass);
        }
```

и константу рядом с `InkWashSeconds`:

```csharp
        private const float TransitionBlotSeconds = 0.3f;
```

В `Assets/UI/Map/Map.uss` добавить укороченный вариант размыва — иначе клякса играла бы 0.6 с и отставала бы от подмены:

```css
.map-inkwash--fast .map-inkwash__blot {
    transition-duration: 0.3s;
}
```

и в `PlayBlotRoutine` перед `AddToClassList(InkWashPlayingClass)` дописать `inkWash.AddToClassList("map-inkwash--fast");`, а в конце — `inkWash.RemoveFromClassList("map-inkwash--fast");`.

В `Assets/UI/Map/MapCloudTransitionController.cs`, в момент подмены экранов (сразу перед вызовом `Show()` экрана назначения), добавить:

```csharp
            GetComponent<MapCeremonyController>()?.PlayTransitionBlot();
```

- [ ] **Step 7: Заставить ambient уступать церемонии**

В `Assets/UI/Map/MapAmbientController.cs`, в `Tick()`, заменить проверку перехода на:

```csharp
            if (MapCloudTransitionController.IsTransitioning || MapCeremonyController.IsPlaying)
                return;
```

- [ ] **Step 8: Добавить компонент в сцену и в тест сцены**

```bash
unity --json cmd eval 'var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene(); if (scene.path != "Assets/Scenes/SampleScene.unity") return "WRONG SCENE: " + scene.path; var ui = UnityEngine.GameObject.Find("UI"); if (ui == null) return "NO UI OBJECT"; if (ui.GetComponent<Mikey.UI.Map.MapCeremonyController>() == null) ui.AddComponent<Mikey.UI.Map.MapCeremonyController>(); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene); UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene); return "ok";'
```

В `Assets/UI/Map/Tests/MapControllersSceneTests.cs` добавить:

```csharp
        [Test]
        public void UiGameObject_HasMapCeremonyController()
        {
            GameObject ui = OpenUiGameObject();
            Assert.IsNotNull(ui.GetComponent<MapCeremonyController>(),
                "UI GameObject must have a MapCeremonyController for the map's one-shot ceremonies to run in a real build.");
        }
```

- [ ] **Step 9: Прогнать все тесты карты**

```bash
unity command run_tests --mode EditMode --filter "Mikey.UI.Map.Tests" --filter_type assembly --format json
```

Ожидается: все PASS.

- [ ] **Step 10: Визуальная проверка**

Первый вход на карту должен давать чернильное проявление, повторные — нет. Через контекстное меню компонента `MapCeremonyController` в инспекторе запустить обе отладочные церемонии: облака должны расходиться и возвращаться в исходную композицию без смещения, печать — впечатываться с отскоком. Убедиться, что ambient во время церемонии молчит и корректно возобновляется после.

- [ ] **Step 11: Коммит**

```bash
git add Assets/UI/Map Assets/UI/MikeyApp.uxml Assets/Scenes/SampleScene.unity && git commit -m "feat(map): церемонии — проявление карты, разблокировка главы, штамп уровня"
```

---

## Что сознательно не входит в план

- **Магнит камеры к маркеру и тинт времени суток.** Помечены в спеке как опциональные. Браться за них имеет смысл после того, как основной слой отсмотрен на устройстве: магнит может конфликтовать с инерцией, а тинт меняет художественный вид карты и требует отдельного решения владельца.
- **Подключение церемоний разблокировки и прохождения к прогрессии.** Событий сегодня не существует; задача 14 поставляет готовые сцены и точки входа.
- **Звуковой клип печати.** Код готов и безопасен без клипа; сам звук — контентная задача владельца.
- **Хаптика, тропа между главами, погода, анимации HUD.** Выведены за объём ещё на этапе дизайна.
