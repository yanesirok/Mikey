# Плывущие облака на карте — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить на обе карты слой из семи плывущих облаков на трёх полосах глубины и оживить четыре существующих облака рамки, не тронув их композицию.

**Architecture:** Плывущий слой — фиксированный пул из семи элементов разметки на экран; положение каждого есть чистая функция времени с заворачиванием, без аллокаций и без случайности. Чистая математика живёт в `MapWindMath`, таблица полос и статическая раскладка — в `MapWindLayout`, владение элементами — в `MapWindLayer`. Единственный драйвер 30 Гц остаётся один: `MapAmbientController` только зовёт `MapWindLayer.Tick`.

**Tech Stack:** Unity 6000.3.18f1, UI Toolkit (UXML/USS), C#, NUnit EditMode, `unity` CLI поверх живого редактора.

**Spec:** `docs/superpowers/specs/2026-08-23-map-wind-clouds-design.md`

## Global Constraints

- За кадр пишутся **только** `translate`, `scale`, `rotate`, `opacity`. `left`/`top`/`width`/`height`/`margin`/`padding`/`font-size` не анимируются никогда.
- Единственный планировщик 30 Гц — `MapAmbientController`. `schedule.Execute` в новых файлах не появляется.
- Каждый анимируемый элемент получает `usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor`.
- Инлайн-стиль снимается только в `StyleKeyword.Null`, никогда записью литерального нуля. Инлайн переживает повторный вход на экран.
- `MapCloudLayout` — единственный источник истины для раскладки четырёх облаков рамки; плывущий слой в него не залезает.
- `MapPanZoomController.ApplyCanvasTransform` остаётся единственным писателем трансформа канваса.
- XML-комментарии в UXML **не могут содержать `--`**. Имена классов проекта содержат. Нарушение молча ломает загрузку всего UXML, и тесты становятся пустыми, а не красными.
- `:not()` в USS не поддерживается этой версией редактора.
- **Каждая задача прогоняет полный EditMode-набор**, а не только свои сборки: `unity command run_tests --mode EditMode --format json`. Отфильтрованного прогона недостаточно — в прошлый раз регрессия пережила три ревью именно из-за фильтров.
- **Никогда не трогать и не индексировать** `Assets/UI/Map/MapMarkerLayout.cs` и `Assets/UI/Map/Tests/MapMarkerLayoutTests.cs` — владелец правит их параллельно, они намеренно не в коммите.
- **Никогда `git add -A` или `git add .`** — индексировать только явно названные пути и проверять `git diff --cached --stat` перед каждым коммитом.
- Работать через навык `unity-cli`, а не через сырой `Unity.exe`. `eval` не видит `internal`-члены: всё, что зовётся из CLI, должно быть `public`.

## Структура файлов

| Файл | Ответственность |
|---|---|
| `Assets/UI/Map/MapWindMath.cs` (новый) | Чистая математика без `UnityEngine`: доля пути, смещение, краевое затухание, покачивание, набухание, крен, прозрачность |
| `Assets/UI/Map/MapWindLayout.cs` (новый) | Таблица семи полос; статическая раскладка `width`/`height`/`top`/`left` |
| `Assets/UI/Map/MapWindLayer.cs` (новый) | Владеет элементами одного экрана: `Bind`, `EnsureLayout`, `Tick`, `Reset`, `SetVisible` |
| `Assets/UI/Map/MapAmbientController.cs` | Только зовёт `MapWindLayer` |
| `Assets/UI/Map/MapAmbientMath.cs` | Константы и математика оживления рамки |
| `Assets/UI/MikeyApp.uxml` | 7 элементов × 2 экрана |
| `Assets/UI/Map/Map.uss` | `.map-wind-layer`, `.map-wind`, 4 класса текстур |

Порядок задач обязателен: задача 4 привязывается к элементам, которые создаёт задача 3.

---

### Task 1: MapWindMath — чистая математика полос

**Files:**
- Create: `Assets/UI/Map/MapWindMath.cs`
- Test: `Assets/UI/Map/Tests/MapWindMathTests.cs`

**Interfaces:**
- Consumes: `MapAmbientMath.Wave(float timeSeconds, float periodSeconds, float phase01) -> float` — уже существует, диапазон `[-1, 1]`, на вырожденном вводе даёт 0.
- Produces:
  - `MapWindMath.LaneProgress(float timeSeconds, float crossSeconds, float phase01) -> float` в `[0, 1)`
  - `MapWindMath.LaneOffsetX(float progress01, float canvasWidth, float cloudWidth) -> float`
  - `MapWindMath.EdgeFade(float progress01) -> float` в `[0, 1]`
  - `MapWindMath.Bob(float timeSeconds, float crossSeconds, float phase01, float canvasHeight) -> float`
  - `MapWindMath.Swell(float timeSeconds, float crossSeconds, float phase01) -> float` в `[1, 1.05]`
  - `MapWindMath.RollDegrees(float timeSeconds, float crossSeconds, float phase01) -> float`
  - `MapWindMath.Opacity(float restOpacity, float progress01, float timeSeconds, float crossSeconds, float phase01, float settle01) -> float`
  - `MapWindMath.Frac(float value) -> float`
  - Константы `FadeEdge`, `BobAmplitude`, `BobPeriodRatio`, `SwellAmplitude`, `SwellPeriodRatio`, `RollAmplitudeDegrees`, `RollPeriodRatio`, `OpacityAmplitude`, `OpacityPeriodRatio`

- [ ] **Шаг 1: написать падающий тест**

Создать `Assets/UI/Map/Tests/MapWindMathTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Математика ветровых полос. Класс чист и детерминирован, поэтому здесь
    /// настоящие тесты значений, а не проверки по тексту исходника.
    /// </summary>
    public class MapWindMathTests
    {
        private const float Tolerance = 0.0005f;

        [Test]
        public void Frac_HandlesNegativeAndWholeInput()
        {
            Assert.AreEqual(0.25f, MapWindMath.Frac(3.25f), Tolerance);
            Assert.AreEqual(0.75f, MapWindMath.Frac(-3.25f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Frac(4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Frac(float.NaN), Tolerance);
        }

        [Test]
        public void Frac_NeverReturnsOne()
        {
            // Округление при большом отрицательном вводе умеет вернуть ровно 1,
            // а единица в доле пути означала бы облако за правым краем в момент,
            // который код считает стартом полосы.
            for (int i = 1; i <= 200; i++)
            {
                float value = MapWindMath.Frac(-i * 0.9999f);
                Assert.GreaterOrEqual(value, 0f);
                Assert.Less(value, 1f);
            }
        }

        [Test]
        public void LaneProgress_WrapsOncePerCrossing()
        {
            Assert.AreEqual(0f, MapWindMath.LaneProgress(0f, 100f, 0f), Tolerance);
            Assert.AreEqual(0.5f, MapWindMath.LaneProgress(50f, 100f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneProgress(100f, 100f, 0f), Tolerance);
            Assert.AreEqual(0.25f, MapWindMath.LaneProgress(125f, 100f, 0f), Tolerance);
        }

        [Test]
        public void LaneProgress_AppliesPhase()
        {
            Assert.AreEqual(0.4f, MapWindMath.LaneProgress(0f, 100f, 0.4f), Tolerance);
            Assert.AreEqual(0.1f, MapWindMath.LaneProgress(70f, 100f, 0.4f), Tolerance);
        }

        [Test]
        public void LaneProgress_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.LaneProgress(10f, 0f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneProgress(10f, -5f, 0.4f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneProgress(float.NaN, 100f, 0.4f), Tolerance);
        }

        [Test]
        public void LaneOffsetX_ClearsBothEdgesAtTheWrapPoint()
        {
            const float canvas = 1000f;
            const float cloud = 340f;

            float atStart = MapWindMath.LaneOffsetX(0f, canvas, cloud);
            Assert.AreEqual(-cloud, atStart, Tolerance,
                "В нуле пути облако обязано быть целиком за левым краем.");
            Assert.LessOrEqual(atStart + cloud, 0f + Tolerance);

            float atEnd = MapWindMath.LaneOffsetX(1f, canvas, cloud);
            Assert.GreaterOrEqual(atEnd, canvas - Tolerance,
                "В конце пути облако обязано быть целиком за правым краем.");
        }

        [Test]
        public void LaneOffsetX_IsMonotonic()
        {
            float previous = float.NegativeInfinity;
            for (int i = 0; i <= 50; i++)
            {
                float value = MapWindMath.LaneOffsetX(i / 50f, 1000f, 340f);
                Assert.Greater(value, previous);
                previous = value;
            }
        }

        [Test]
        public void LaneOffsetX_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(0.5f, 0f, 340f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(0.5f, 1000f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.LaneOffsetX(float.NaN, 1000f, 340f), Tolerance);
        }

        [Test]
        public void EdgeFade_IsZeroAtBothEndsAndOneInTheMiddle()
        {
            Assert.AreEqual(0f, MapWindMath.EdgeFade(0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.EdgeFade(1f), Tolerance);
            Assert.AreEqual(1f, MapWindMath.EdgeFade(0.5f), Tolerance);
            Assert.AreEqual(1f, MapWindMath.EdgeFade(MapWindMath.FadeEdge), Tolerance);
            Assert.AreEqual(1f, MapWindMath.EdgeFade(1f - MapWindMath.FadeEdge), Tolerance);
        }

        [Test]
        public void EdgeFade_RisesMonotonicallyOnBothSlopes()
        {
            float previous = -1f;
            for (int i = 0; i <= 20; i++)
            {
                float value = MapWindMath.EdgeFade(i * MapWindMath.FadeEdge / 20f);
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }

            previous = 2f;
            for (int i = 0; i <= 20; i++)
            {
                float value = MapWindMath.EdgeFade(1f - i * MapWindMath.FadeEdge / 20f);
                Assert.LessOrEqual(value, previous);
                previous = value;
            }
        }

        [Test]
        public void Swell_StaysInsideItsDeclaredRange()
        {
            for (int i = 0; i <= 400; i++)
            {
                float value = MapWindMath.Swell(i * 0.5f, 140f, 0.55f);
                Assert.GreaterOrEqual(value, 1f - Tolerance);
                Assert.LessOrEqual(value, 1f + MapWindMath.SwellAmplitude + Tolerance);
            }
        }

        /// <summary>
        /// Главный тест против возврата к MapAmbientMath.Breath: она не берёт
        /// фазу, и три облака дальней полосы, делящие один crossSeconds,
        /// набухали бы строем — ровно то, от чего уходит весь дизайн.
        /// </summary>
        [Test]
        public void Swell_IsPhaseAware()
        {
            float a = MapWindMath.Swell(7f, 200f, 0.00f);
            float b = MapWindMath.Swell(7f, 200f, 0.41f);
            float c = MapWindMath.Swell(7f, 200f, 0.73f);

            // NUnit 3 не имеет перегрузки AreNotEqual с допуском — только AreEqual.
            Assert.Greater(System.Math.Abs(a - b), Tolerance);
            Assert.Greater(System.Math.Abs(b - c), Tolerance);
            Assert.Greater(System.Math.Abs(a - c), Tolerance);
        }

        [Test]
        public void Swell_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(1f, MapWindMath.Swell(7f, 0f, 0.4f), Tolerance);
            Assert.AreEqual(1f, MapWindMath.Swell(float.NaN, 140f, 0.4f), Tolerance);
        }

        [Test]
        public void RollDegrees_StaysInsideItsAmplitude()
        {
            for (int i = 0; i <= 400; i++)
            {
                float value = MapWindMath.RollDegrees(i * 0.5f, 140f, 0.55f);
                Assert.GreaterOrEqual(value, -MapWindMath.RollAmplitudeDegrees - Tolerance);
                Assert.LessOrEqual(value, MapWindMath.RollAmplitudeDegrees + Tolerance);
            }
        }

        [Test]
        public void Bob_StaysInsideItsAmplitude()
        {
            const float height = 800f;
            for (int i = 0; i <= 400; i++)
            {
                float value = MapWindMath.Bob(i * 0.5f, 140f, 0.55f, height);
                Assert.LessOrEqual(System.Math.Abs(value), height * MapWindMath.BobAmplitude + Tolerance);
            }
        }

        [Test]
        public void Bob_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.Bob(7f, 140f, 0.4f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Bob(7f, 140f, 0.4f, float.NaN), Tolerance);
        }

        [Test]
        public void Opacity_NeverExceedsTheLaneRest()
        {
            for (int i = 0; i <= 400; i++)
            {
                float t = i * 0.5f;
                float progress = MapWindMath.LaneProgress(t, 140f, 0.55f);
                float value = MapWindMath.Opacity(0.24f, progress, t, 140f, 0.55f, 1f);
                Assert.GreaterOrEqual(value, 0f);
                Assert.LessOrEqual(value, 0.24f + Tolerance,
                    "Прозрачность множительна: дальняя полоса не должна уметь выйти на передний план.");
            }
        }

        [Test]
        public void Opacity_IsZeroAtBothLaneEnds()
        {
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, 0f, 0f, 140f, 0f, 1f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Opacity(0.24f, 1f, 0f, 140f, 0f, 1f), Tolerance);
        }

        [Test]
        public void Opacity_ScalesWithSettle()
        {
            float full = MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.55f, 1f);
            float half = MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.55f, 0.5f);
            float none = MapWindMath.Opacity(0.24f, 0.5f, 7f, 140f, 0.55f, 0f);

            Assert.AreEqual(full * 0.5f, half, Tolerance);
            Assert.AreEqual(0f, none, Tolerance);
        }

        [Test]
        public void Opacity_IsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindMath.Opacity(float.NaN, 0.5f, 7f, 140f, 0.4f, 1f), Tolerance);
            Assert.AreEqual(0f, MapWindMath.Opacity(-1f, 0.5f, 7f, 140f, 0.4f, 1f), Tolerance);
        }
    }
}
```

- [ ] **Шаг 2: прогнать и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter Mikey.UI.Map.Tests --filter_type assembly --format json
```

Ожидание: ошибка компиляции — `MapWindMath` не существует.

- [ ] **Шаг 3: написать реализацию**

Создать `Assets/UI/Map/MapWindMath.cs`:

```csharp
namespace Mikey.UI.Map
{
    /// <summary>
    /// Чистая математика ветровых полос — намеренно без UnityEngine, чтобы
    /// гоняться прямо в EditMode, как MapAmbientMath и MapPanZoomMath.
    ///
    /// <para>
    /// Небезопасный ввод (NaN, бесконечность, неположительный период или
    /// размер) везде даёт покой, а не рывок: значение, попавшее на элемент
    /// разметки как NaN, ломает его отрисовку до конца сессии.
    /// </para>
    /// </summary>
    public static class MapWindMath
    {
        /// <summary>Доля пути на каждом конце полосы, на которой облако гасится. Страховка на случай зума и пана, когда видимая область не совпадает с канвасом.</summary>
        public const float FadeEdge = 0.06f;

        /// <summary>Амплитуда покачивания как доля высоты канваса.</summary>
        public const float BobAmplitude = 0.018f;

        /// <summary>Период покачивания как доля периода пересечения полосы.</summary>
        public const float BobPeriodRatio = 0.382f;

        /// <summary>Амплитуда набухания как добавка к масштабу.</summary>
        public const float SwellAmplitude = 0.05f;

        public const float SwellPeriodRatio = 0.236f;

        public const float RollAmplitudeDegrees = 1.6f;

        public const float RollPeriodRatio = 0.146f;

        /// <summary>Относительная амплитуда пульсации прозрачности — множитель, а не слагаемое.</summary>
        public const float OpacityAmplitude = 0.18f;

        public const float OpacityPeriodRatio = 0.191f;

        /// <summary>
        /// Дробная часть в <c>[0, 1)</c>, корректная для отрицательного ввода.
        /// Единица не возвращается никогда: при большом отрицательном вводе
        /// вычитание умеет округлиться ровно до 1, а единица в доле пути
        /// означала бы облако за правым краем в момент, который остальной код
        /// считает стартом полосы.
        /// </summary>
        public static float Frac(float value)
        {
            if (!IsFinite(value))
                return 0f;

            float result = value - (float)System.Math.Floor(value);
            if (result < 0f || result >= 1f)
                return 0f;
            return result;
        }

        /// <summary>Доля пройденного пути по своей полосе в <c>[0, 1)</c>.</summary>
        public static float LaneProgress(float timeSeconds, float crossSeconds, float phase01)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(crossSeconds) || crossSeconds <= 0f)
                return 0f;

            float phase = IsFinite(phase01) ? phase01 : 0f;
            return Frac(timeSeconds / crossSeconds + phase);
        }

        /// <summary>
        /// Горизонтальное смещение относительно левого края канваса. Путь
        /// длиной <c>canvasWidth + cloudWidth</c>: в нуле облако целиком за
        /// левым краем, в единице — целиком за правым, поэтому прыжок
        /// заворачивания происходит вне кадра и не виден в принципе.
        /// </summary>
        public static float LaneOffsetX(float progress01, float canvasWidth, float cloudWidth)
        {
            if (!IsFinite(progress01) || !IsFinite(canvasWidth) || !IsFinite(cloudWidth))
                return 0f;
            if (canvasWidth <= 0f || cloudWidth <= 0f)
                return 0f;

            return -cloudWidth + Clamp01(progress01) * (canvasWidth + cloudWidth);
        }

        /// <summary>Множитель прозрачности на концах полосы: 0 на обоих концах, 1 между <see cref="FadeEdge"/> и <c>1 - FadeEdge</c>.</summary>
        public static float EdgeFade(float progress01)
        {
            if (!IsFinite(progress01))
                return 0f;

            float u = Clamp01(progress01);
            return Clamp01(u / FadeEdge) * Clamp01((1f - u) / FadeEdge);
        }

        /// <summary>Вертикальное покачивание в пикселях.</summary>
        public static float Bob(float timeSeconds, float crossSeconds, float phase01, float canvasHeight)
        {
            if (!IsFinite(canvasHeight) || canvasHeight <= 0f)
                return 0f;

            return canvasHeight * BobAmplitude
                * MapAmbientMath.Wave(timeSeconds, crossSeconds * BobPeriodRatio, phase01);
        }

        /// <summary>
        /// Множитель масштаба в <c>[1, 1 + SwellAmplitude]</c>.
        ///
        /// <para>
        /// Намеренно НЕ через <see cref="MapAmbientMath.Breath"/>: та не берёт
        /// фазу, а три облака одной полосы делят один период пересечения — на
        /// Breath они набухали бы синхронно, то есть строем. Скачка на первом
        /// кадре бояться нечего: до показа экрана этих облаков не было, прыгать
        /// не от чего, а ввод в кадр несёт отдельный множитель прозрачности.
        /// </para>
        /// </summary>
        public static float Swell(float timeSeconds, float crossSeconds, float phase01)
        {
            if (!IsFinite(timeSeconds) || !IsFinite(crossSeconds) || crossSeconds <= 0f)
                return 1f;

            return 1f + SwellAmplitude * 0.5f
                * (1f + MapAmbientMath.Wave(timeSeconds, crossSeconds * SwellPeriodRatio, phase01));
        }

        /// <summary>Крен в градусах.</summary>
        public static float RollDegrees(float timeSeconds, float crossSeconds, float phase01)
        {
            return RollAmplitudeDegrees
                * MapAmbientMath.Wave(timeSeconds, crossSeconds * RollPeriodRatio, phase01);
        }

        /// <summary>
        /// Итоговая прозрачность: покой полосы, приглушённый краевым
        /// затуханием, пульсацией и множителем ввода в кадр. Всё множительно,
        /// поэтому результат никогда не превышает покой полосы и дальняя
        /// полоса не умеет выйти на передний план.
        /// </summary>
        public static float Opacity(float restOpacity, float progress01, float timeSeconds, float crossSeconds, float phase01, float settle01)
        {
            if (!IsFinite(restOpacity) || restOpacity <= 0f)
                return 0f;

            float pulse = 1f + OpacityAmplitude
                * MapAmbientMath.Wave(timeSeconds, crossSeconds * OpacityPeriodRatio, phase01);
            float settle = IsFinite(settle01) ? Clamp01(settle01) : 0f;

            return Clamp01(restOpacity * EdgeFade(progress01) * pulse * settle);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
```

- [ ] **Шаг 4: прогнать и убедиться, что проходит**

```bash
unity command run_tests --mode EditMode --format json
```

Ожидание: весь набор зелёный, включая новые `MapWindMathTests`.

- [ ] **Шаг 5: коммит**

```bash
git add Assets/UI/Map/MapWindMath.cs Assets/UI/Map/MapWindMath.cs.meta Assets/UI/Map/Tests/MapWindMathTests.cs Assets/UI/Map/Tests/MapWindMathTests.cs.meta
git commit -m "feat(map): математика ветровых полос"
```

---

### Task 2: MapWindLayout — таблица полос и статическая раскладка

**Files:**
- Create: `Assets/UI/Map/MapWindLayout.cs`
- Test: `Assets/UI/Map/Tests/MapWindLayoutTests.cs`

**Interfaces:**
- Consumes: `MapWindMath` из задачи 1 (только для тестов бюджета — сам layout его не зовёт).
- Produces:
  - `struct WindCloud` с полями `WidthFraction`, `TopFraction`, `Phase`, `CrossSeconds`, `RestOpacity`, `ParallaxFactor`, `TextureClass` (все `public readonly float`, кроме последнего — `public readonly string`)
  - `MapWindLayout.Clouds` — `static readonly WindCloud[]` длиной 7
  - `MapWindLayout.HeightFraction(float widthFraction, float canvasWidth, float canvasHeight) -> float`
  - `MapWindLayout.Apply(VisualElement element, WindCloud cloud, float canvasWidth, float canvasHeight)`
  - Константы `SourceAspect`, `AreaBudgetScreens`, `BudgetCanvasAspect`, `FarCrossSeconds`, `MidCrossSeconds`, `NearCrossSeconds`

- [ ] **Шаг 1: написать падающий тест**

Создать `Assets/UI/Map/Tests/MapWindLayoutTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Таблица ветровых полос. Главные тесты здесь — упорядоченность по
    /// глубине и потолок площади: первое отвечает за то, что глубина вообще
    /// читается, второе за то, что слой не съест кадровый бюджет карты.
    /// </summary>
    public class MapWindLayoutTests
    {
        private const float Tolerance = 0.0005f;

        [Test]
        public void TableHasSevenClouds()
        {
            Assert.AreEqual(7, MapWindLayout.Clouds.Length);
        }

        [Test]
        public void EveryPhaseIsInsideTheLane()
        {
            foreach (WindCloud cloud in MapWindLayout.Clouds)
            {
                Assert.GreaterOrEqual(cloud.Phase, 0f);
                Assert.Less(cloud.Phase, 1f);
            }
        }

        [Test]
        public void EveryPhaseIsDistinct()
        {
            // Совпавшие фазы означали бы два облака, идущие вплотную друг за
            // другом весь сеанс — дыра в одном месте полосы и сгусток в другом.
            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                for (int j = i + 1; j < MapWindLayout.Clouds.Length; j++)
                {
                    // NUnit 3 не имеет перегрузки AreNotEqual с допуском — только AreEqual.
                    Assert.That(MapWindLayout.Clouds[i].Phase,
                        Is.Not.EqualTo(MapWindLayout.Clouds[j].Phase).Within(Tolerance),
                        $"Облака {i} и {j} стартуют из одной точки полосы.");
                }
            }
        }

        [Test]
        public void BandsAreOrderedByDepth()
        {
            // Дальнее обязано двигаться МЕНЬШЕ карты, ближнее БОЛЬШЕ. Именно
            // отсутствие этого разброса (1.04..1.12 у рамки) и делало небо
            // плоским.
            float far = FindByCross(MapWindLayout.FarCrossSeconds).ParallaxFactor;
            float mid = FindByCross(MapWindLayout.MidCrossSeconds).ParallaxFactor;
            float near = FindByCross(MapWindLayout.NearCrossSeconds).ParallaxFactor;

            Assert.Less(far, mid);
            Assert.Less(mid, near);
            Assert.Less(far, 1f, "Дальняя полоса обязана отставать от карты.");
            Assert.Greater(near, 1f, "Ближняя полоса обязана обгонять карту.");
        }

        [Test]
        public void FartherBandsAreSlowerAndSmaller()
        {
            WindCloud far = FindByCross(MapWindLayout.FarCrossSeconds);
            WindCloud mid = FindByCross(MapWindLayout.MidCrossSeconds);
            WindCloud near = FindByCross(MapWindLayout.NearCrossSeconds);

            Assert.Greater(MapWindLayout.FarCrossSeconds, MapWindLayout.MidCrossSeconds);
            Assert.Greater(MapWindLayout.MidCrossSeconds, MapWindLayout.NearCrossSeconds);
            Assert.Less(far.WidthFraction, mid.WidthFraction);
            Assert.Less(mid.WidthFraction, near.WidthFraction);
            Assert.Less(far.RestOpacity, mid.RestOpacity);
        }

        [Test]
        public void HeightFractionPreservesTheSourceAspect()
        {
            // Ширина 0.5 канваса на квадратном канвасе даёт высоту 0.5/2.525.
            float height = MapWindLayout.HeightFraction(0.5f, 1000f, 1000f);
            Assert.AreEqual(0.5f / MapWindLayout.SourceAspect, height, Tolerance);
        }

        [Test]
        public void HeightFractionIsSafeOnDegenerateInput()
        {
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 1000f, 0f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(0.5f, 0f, 1000f), Tolerance);
            Assert.AreEqual(0f, MapWindLayout.HeightFraction(float.NaN, 1000f, 1000f), Tolerance);
        }

        /// <summary>
        /// Потолок стоимости слоя. GPU растеризует и смешивает весь квад, а не
        /// только видимую тушь, поэтому цена считается по площади
        /// прямоугольников. Рамка стоит около 2.0 экранов; ветер обязан
        /// остаться дешевле неё.
        /// </summary>
        [Test]
        public void TotalQuadAreaStaysInsideTheBudget()
        {
            float total = 0f;
            foreach (WindCloud cloud in MapWindLayout.Clouds)
            {
                float height = MapWindLayout.HeightFraction(
                    cloud.WidthFraction, MapWindLayout.BudgetCanvasAspect, 1f);
                total += cloud.WidthFraction * height;
            }

            Assert.Greater(total, 0f, "Тест обязан был что-то посчитать.");
            Assert.LessOrEqual(total, MapWindLayout.AreaBudgetScreens,
                $"Суммарная площадь ветровых квадов {total:F3} экрана превышает потолок " +
                $"{MapWindLayout.AreaBudgetScreens}. Это прямая плата кадром на телефоне.");
        }

        [Test]
        public void EveryTextureClassIsOneOfTheFourKnownOnes()
        {
            string[] allowed =
            {
                "map-wind--left-01",
                "map-wind--left-02",
                "map-wind--right-01",
                "map-wind--bottom-01",
            };

            foreach (WindCloud cloud in MapWindLayout.Clouds)
                CollectionAssert.Contains(allowed, cloud.TextureClass);
        }

        private static WindCloud FindByCross(float crossSeconds)
        {
            foreach (WindCloud cloud in MapWindLayout.Clouds)
            {
                if (System.Math.Abs(cloud.CrossSeconds - crossSeconds) < Tolerance)
                    return cloud;
            }

            Assert.Fail($"В таблице нет облака с периодом пересечения {crossSeconds}.");
            return default;
        }
    }
}
```

- [ ] **Шаг 2: прогнать и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter Mikey.UI.Map.Tests --filter_type assembly --format json
```

Ожидание: ошибка компиляции — `MapWindLayout` и `WindCloud` не существуют.

- [ ] **Шаг 3: написать реализацию**

Создать `Assets/UI/Map/MapWindLayout.cs`:

```csharp
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>Одна ветровая полоса: где облако идёт, как быстро, насколько крупно и бледно.</summary>
    public readonly struct WindCloud
    {
        /// <summary>Ширина элемента в долях ширины канваса.</summary>
        public readonly float WidthFraction;

        /// <summary>Верх прямоугольника в долях высоты канваса.</summary>
        public readonly float TopFraction;

        /// <summary>Стартовая доля пути в полосе, <c>[0, 1)</c>.</summary>
        public readonly float Phase;

        /// <summary>Секунд на полное пересечение полосы.</summary>
        public readonly float CrossSeconds;

        /// <summary>Прозрачность полосы в середине пути, до пульсации и краевого затухания.</summary>
        public readonly float RestOpacity;

        /// <summary>Множитель глубины: меньше единицы — отстаёт от карты, больше — обгоняет.</summary>
        public readonly float ParallaxFactor;

        /// <summary>Класс USS, дающий этому облаку текстуру.</summary>
        public readonly string TextureClass;

        public WindCloud(float widthFraction, float topFraction, float phase, float crossSeconds,
            float restOpacity, float parallaxFactor, string textureClass)
        {
            WidthFraction = widthFraction;
            TopFraction = topFraction;
            Phase = phase;
            CrossSeconds = crossSeconds;
            RestOpacity = restOpacity;
            ParallaxFactor = parallaxFactor;
            TextureClass = textureClass;
        }
    }

    /// <summary>
    /// Единственный источник истины для семи плывущих облаков. В отличие от
    /// <see cref="MapCloudLayout"/>, координаты здесь нормализованы к КАНВАСУ,
    /// а не к исходному изображению карты: плывущее облако не часть рисунка
    /// карты и не обязано совпадать с её кадрированием.
    ///
    /// <para>
    /// <b>Направление ветра — слева направо.</b> Не произвольный выбор: слева
    /// стоят left1/left2 рамки, справа right1, и вход и выход облака
    /// происходят там, где тушь рамки наиболее плотная.
    /// </para>
    ///
    /// <para>
    /// Этот класс — ЕДИНСТВЕННОЕ место, которому разрешено писать
    /// <c>width</c>/<c>height</c>/<c>top</c>/<c>left</c> плывущих облаков, и
    /// делает это только при показе экрана и при смене размера канваса. Тик
    /// (<see cref="MapWindLayer"/>) к геометрии не прикасается вовсе.
    /// </para>
    /// </summary>
    public static class MapWindLayout
    {
        /// <summary>Пропорция облачных PNG: 2376 x 941. Зафиксирована тестом MapCloudAssetsTests.</summary>
        public const float SourceAspect = 2376f / 941f;

        public const float FarCrossSeconds = 200f;
        public const float MidCrossSeconds = 140f;
        public const float NearCrossSeconds = 95f;

        public const float FarWidthFraction = 0.34f;
        public const float MidWidthFraction = 0.52f;
        public const float NearWidthFraction = 0.78f;

        public const float FarRestOpacity = 0.14f;
        public const float MidRestOpacity = 0.24f;

        /// <summary>Ближнее облако крупное и потому НАМЕРЕННО бледнее среднего: оно проходит близко к глазу и не должно спорить с картой за внимание.</summary>
        public const float NearRestOpacity = 0.18f;

        public const float FarParallax = 0.40f;
        public const float MidParallax = 0.75f;
        public const float NearParallax = 1.30f;

        /// <summary>Потолок суммарной площади ветровых квадов в долях экрана. Рамка стоит около 2.0 — ветер обязан быть дешевле.</summary>
        public const float AreaBudgetScreens = 1.8f;

        /// <summary>Пропорция альбомного телефона, на которой считается бюджет площади.</summary>
        public const float BudgetCanvasAspect = 2.17f;

        public static readonly WindCloud[] Clouds =
        {
            new WindCloud(FarWidthFraction, 0.04f, 0.00f, FarCrossSeconds, FarRestOpacity, FarParallax, "map-wind--left-01"),
            new WindCloud(FarWidthFraction, 0.19f, 0.41f, FarCrossSeconds, FarRestOpacity, FarParallax, "map-wind--right-01"),
            new WindCloud(FarWidthFraction, 0.11f, 0.73f, FarCrossSeconds, FarRestOpacity, FarParallax, "map-wind--left-02"),
            new WindCloud(MidWidthFraction, 0.26f, 0.17f, MidCrossSeconds, MidRestOpacity, MidParallax, "map-wind--right-01"),
            new WindCloud(MidWidthFraction, 0.44f, 0.55f, MidCrossSeconds, MidRestOpacity, MidParallax, "map-wind--left-01"),
            new WindCloud(MidWidthFraction, 0.35f, 0.88f, MidCrossSeconds, MidRestOpacity, MidParallax, "map-wind--bottom-01"),
            new WindCloud(NearWidthFraction, 0.30f, 0.31f, NearCrossSeconds, NearRestOpacity, NearParallax, "map-wind--left-01"),
        };

        /// <summary>Высота элемента в долях высоты канваса, сохраняющая пропорции исходного PNG.</summary>
        public static float HeightFraction(float widthFraction, float canvasWidth, float canvasHeight)
        {
            if (!IsFinite(widthFraction) || !IsFinite(canvasWidth) || !IsFinite(canvasHeight))
                return 0f;
            if (canvasWidth <= 0f || canvasHeight <= 0f)
                return 0f;

            return widthFraction * (canvasWidth / canvasHeight) / SourceAspect;
        }

        /// <summary>
        /// Пишет геометрию одного облака. <c>left</c> всегда 0: весь путь по
        /// горизонтали несёт <c>translate</c> из тика, иначе движение стоило бы
        /// полного прохода раскладки каждый кадр.
        /// </summary>
        public static void Apply(VisualElement element, WindCloud cloud, float canvasWidth, float canvasHeight)
        {
            if (element == null)
                return;

            float height = HeightFraction(cloud.WidthFraction, canvasWidth, canvasHeight);

            element.style.width = new Length(cloud.WidthFraction * 100f, LengthUnit.Percent);
            element.style.height = new Length(height * 100f, LengthUnit.Percent);
            element.style.top = new Length(cloud.TopFraction * 100f, LengthUnit.Percent);
            element.style.left = new Length(0f, LengthUnit.Percent);
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
```

- [ ] **Шаг 4: прогнать и убедиться, что проходит**

```bash
unity command run_tests --mode EditMode --format json
```

Ожидание: весь набор зелёный. Тест `TotalQuadAreaStaysInsideTheBudget` должен насчитать около 1.518.

- [ ] **Шаг 5: коммит**

```bash
git add Assets/UI/Map/MapWindLayout.cs Assets/UI/Map/MapWindLayout.cs.meta Assets/UI/Map/Tests/MapWindLayoutTests.cs Assets/UI/Map/Tests/MapWindLayoutTests.cs.meta
git commit -m "feat(map): таблица ветровых полос и статическая раскладка"
```

---

### Task 3: разметка и стили — семь элементов на каждом экране

**Files:**
- Modify: `Assets/UI/MikeyApp.uxml` (вставка после `pan-canvas-scrim` на обоих экранах)
- Modify: `Assets/UI/Map/Map.uss` (добавить блок перед комментарием `/* ---------- Map Pass 3B decorative cloud overlay`)
- Test: `Assets/UI/Map/Tests/MapWindLayerUxmlTests.cs`

**Interfaces:**
- Consumes: имена классов текстур из `MapWindLayout.Clouds[i].TextureClass` (задача 2).
- Produces: элементы `map-wind-layer` / `okinawa-wind-layer`, внутри них `map-wind-0` … `map-wind-6` и `okinawa-wind-0` … `okinawa-wind-6`. Задача 4 привязывается именно к этим именам.

**Осторожно:** XML-комментарии не могут содержать `--`, а все классы полос
его содержат. В комментариях писать «класс текстуры» словами, не именем
класса. Нарушение молча ломает загрузку всего UXML.

- [ ] **Шаг 1: написать падающий тест**

Создать `Assets/UI/Map/Tests/MapWindLayerUxmlTests.cs`:

```csharp
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Разметка и стили плывущего слоя. Ключевой контракт — порядок
    /// объявления: слой обязан идти ПОСЛЕ арта карты и ДО первого маркера,
    /// иначе облако начнёт проходить перед единственным интерактивным
    /// элементом экрана.
    /// </summary>
    public class MapWindLayerUxmlTests
    {
        private const string UxmlPath = "Assets/UI/MikeyApp.uxml";
        private const string UssPath = "Assets/UI/Map/Map.uss";

        [Test]
        public void BothScreensDeclareSevenWindClouds()
        {
            string uxml = File.ReadAllText(UxmlPath);

            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                StringAssert.Contains($"name=\"map-wind-{i}\"", uxml);
                StringAssert.Contains($"name=\"okinawa-wind-{i}\"", uxml);
            }

            StringAssert.Contains("name=\"map-wind-layer\"", uxml);
            StringAssert.Contains("name=\"okinawa-wind-layer\"", uxml);
        }

        [Test]
        public void EveryWindCloudCarriesTheTextureClassItsLaneDeclares()
        {
            string uxml = File.ReadAllText(UxmlPath);

            for (int i = 0; i < MapWindLayout.Clouds.Length; i++)
            {
                string expected = MapWindLayout.Clouds[i].TextureClass;
                AssertElementHasClass(uxml, $"map-wind-{i}", expected);
                AssertElementHasClass(uxml, $"okinawa-wind-{i}", expected);
            }
        }

        [Test]
        public void WindLayerIsDeclaredAfterTheArtAndBeforeTheFirstMarker()
        {
            string uxml = File.ReadAllText(UxmlPath);

            int japanArt = uxml.IndexOf("class=\"map-canvas-art\"", System.StringComparison.Ordinal);
            int japanWind = uxml.IndexOf("name=\"map-wind-layer\"", System.StringComparison.Ordinal);
            int japanMarker = uxml.IndexOf("name=\"chapter-node-okinawa\"", System.StringComparison.Ordinal);

            Assert.Greater(japanArt, -1);
            Assert.Greater(japanWind, japanArt, "Ветер обязан краситься поверх арта карты.");
            Assert.Less(japanWind, japanMarker, "Ветер обязан краситься ПОД маркерами.");

            int okiArt = uxml.IndexOf("class=\"okinawa-canvas-art\"", System.StringComparison.Ordinal);
            int okiWind = uxml.IndexOf("name=\"okinawa-wind-layer\"", System.StringComparison.Ordinal);
            int okiMarker = uxml.IndexOf("name=\"level-node-0\"", System.StringComparison.Ordinal);

            Assert.Greater(okiArt, -1);
            Assert.Greater(okiWind, okiArt);
            Assert.Less(okiWind, okiMarker);
        }

        [Test]
        public void FramingCloudLayerStillPaintsAboveTheWind()
        {
            string uxml = File.ReadAllText(UxmlPath);

            int japanWind = uxml.IndexOf("name=\"map-wind-layer\"", System.StringComparison.Ordinal);
            int japanFrame = uxml.IndexOf("name=\"map-cloud-layer\"", System.StringComparison.Ordinal);
            Assert.Less(japanWind, japanFrame, "Рамка маскирует край карты и обязана оставаться сверху.");

            int okiWind = uxml.IndexOf("name=\"okinawa-wind-layer\"", System.StringComparison.Ordinal);
            int okiFrame = uxml.IndexOf("name=\"okinawa-cloud-layer\"", System.StringComparison.Ordinal);
            Assert.Less(okiWind, okiFrame);
        }

        [Test]
        public void EveryWindElementIgnoresPicking()
        {
            string uxml = File.ReadAllText(UxmlPath);

            foreach (Match match in Regex.Matches(uxml, @"<ui:VisualElement[^>]*name=""(?:map|okinawa)-wind[^""]*""[^>]*/?>"))
            {
                StringAssert.Contains("picking-mode=\"Ignore\"", match.Value,
                    $"Декоративное облако не должно перехватывать тап: {match.Value}");
            }
        }

        [Test]
        public void WindRestsInvisibleSoResetCannotFlashItOpaque()
        {
            // Тик снимает инлайн в StyleKeyword.Null, а Null отдаёт значение
            // обратно USS. Без opacity:0 здесь сброшенное облако вспыхнуло бы
            // полностью непрозрачным.
            string block = ExtractRuleBlock(File.ReadAllText(UssPath), ".map-wind");
            Assert.IsNotNull(block, "Ожидалось правило '.map-wind' в Map.uss.");
            StringAssert.Contains("opacity: 0", block);
        }

        [Test]
        public void WindTextureClassesPointAtTheSameFilesAsTheFramingOnes()
        {
            string uss = File.ReadAllText(UssPath);
            string[] suffixes = { "left-01", "left-02", "right-01", "bottom-01" };

            foreach (string suffix in suffixes)
            {
                string wind = ExtractRuleBlock(uss, $".map-wind--{suffix}");
                string frame = ExtractRuleBlock(uss, $".map-cloud--{suffix}");

                Assert.IsNotNull(wind, $"Ожидалось правило '.map-wind--{suffix}'.");
                Assert.IsNotNull(frame, $"Ожидалось правило '.map-cloud--{suffix}'.");

                string windUrl = ExtractUrl(wind);
                string frameUrl = ExtractUrl(frame);
                Assert.AreEqual(frameUrl, windUrl,
                    $"'.map-wind--{suffix}' и '.map-cloud--{suffix}' обязаны указывать на один файл.");
            }
        }

        /// <summary>
        /// У ветра не должно быть CSS-переходов ВООБЩЕ: всё его движение
        /// считает тик, и переход поверх посчитанного значения дал бы борьбу
        /// двух источников за одно свойство. Разбираются именно БЛОКИ по
        /// селектору, а не весь файл подстрокой — иначе тест спотыкался бы о
        /// переходы соседних правил.
        /// </summary>
        [Test]
        public void WindDeclaresNoCssTransitionsAtAll()
        {
            string uss = File.ReadAllText(UssPath);
            string withoutComments = Regex.Replace(uss, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            int checkedBlocks = 0;

            foreach (Match block in Regex.Matches(withoutComments, @"([^{}]+)\{([^{}]*)\}"))
            {
                if (!block.Groups[1].Value.Contains("map-wind"))
                    continue;

                checkedBlocks++;
                StringAssert.DoesNotContain("transition", block.Groups[2].Value,
                    $"Правило \"{block.Groups[1].Value.Trim()}\" объявляет CSS-переход — " +
                    "движение ветра целиком считает MapWindLayer.Tick.");
            }

            Assert.GreaterOrEqual(checkedBlocks, 6,
                "Ожидались правила слоя, базовое и четыре класса текстур — тест ничего не проверил.");
        }

        private static void AssertElementHasClass(string uxml, string elementName, string className)
        {
            Match match = Regex.Match(uxml, $@"<ui:VisualElement[^>]*name=""{Regex.Escape(elementName)}""[^>]*>");
            Assert.IsTrue(match.Success, $"Не найден элемент '{elementName}' в разметке.");
            StringAssert.Contains(className, match.Value,
                $"Элемент '{elementName}' обязан нести класс текстуры '{className}' из своей полосы.");
        }

        private static string ExtractRuleBlock(string uss, string selector)
        {
            Match match = Regex.Match(uss, Regex.Escape(selector) + @"\s*\{([^}]*)\}");
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string ExtractUrl(string block)
        {
            Match match = Regex.Match(block, @"url\(""([^""]+)""\)");
            Assert.IsTrue(match.Success, $"В блоке нет background-image: {block}");
            return match.Groups[1].Value;
        }
    }
}
```

- [ ] **Шаг 2: прогнать и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter Mikey.UI.Map.Tests --filter_type assembly --format json
```

Ожидание: падают все тесты этого файла — элементов и правил ещё нет.

- [ ] **Шаг 3: добавить стили**

В `Assets/UI/Map/Map.uss`, непосредственно ПЕРЕД комментарием
`/* ---------- Map Pass 3B decorative cloud overlay (corrected architecture) ----------`,
вставить:

```css
/* ---------- плывущий слой облаков ----------
   Семь элементов на экран, ребёнок той же ".pan-canvas", что арт, маркеры и
   рамка — значит, слой панится и зумится вместе с картой и клипуется
   существующим overflow:hidden у ".pan-stage". Своего клипа слой не заводит:
   двойной клип уже ломал композицию рамки.

   Объявляется в разметке ПОСЛЕ арта и ДО первого маркера, поэтому красится
   над картой, но под пинами. Облако, проходящее перед пином, задевало бы
   единственный интерактивный элемент экрана ради красоты.

   CSS-переходов здесь нет НИ ОДНОГО и быть не должно: всё движение считает
   тик MapWindLayer, а переход поверх посчитанного значения дал бы борьбу
   двух источников за одно свойство.

   opacity:0 — это состояние покоя. Тик снимает свой инлайн в
   StyleKeyword.Null, а Null отдаёт значение обратно сюда; без нуля здесь
   сброшенное облако вспыхнуло бы полностью непрозрачным. */
.map-wind-layer {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
}
.map-wind {
    position: absolute;
    opacity: 0;
    -unity-background-scale-mode: stretch;
}
.map-wind--left-01 {
    background-image: url("/Assets/UI/Media/Images/Map/Clouds/cloud_left_01.png");
}
.map-wind--left-02 {
    background-image: url("/Assets/UI/Media/Images/Map/Clouds/cloud_left_02.png");
}
.map-wind--right-01 {
    background-image: url("/Assets/UI/Media/Images/Map/Clouds/cloud_right_01.png");
}
.map-wind--bottom-01 {
    background-image: url("/Assets/UI/Media/Images/Map/Clouds/cloud_bottom_01.png");
}
```

- [ ] **Шаг 4: добавить разметку экрана Японии**

В `Assets/UI/MikeyApp.uxml` сразу после строки
`<ui:VisualElement class="pan-canvas-scrim" picking-mode="Ignore" />`
внутри `<ui:VisualElement name="map-canvas" class="pan-canvas">` вставить:

```xml
                        <!-- Плывущий слой: семь облаков на трёх полосах
                             глубины, см. MapWindLayout. Объявлен здесь, а не
                             рядом со слоем рамки внизу, намеренно: тут он
                             красится над артом карты, но ПОД маркерами.
                             Размер и высоту пишет MapWindLayout.Apply при
                             показе и на смене размера; путь, крен, набухание и
                             прозрачность считает MapWindLayer.Tick. Классы
                             текстур переиспользуют те же четыре PNG, что и
                             рамка. -->
                        <ui:VisualElement name="map-wind-layer" class="map-wind-layer" picking-mode="Ignore">
                            <ui:VisualElement name="map-wind-0" class="map-wind map-wind--left-01" picking-mode="Ignore" />
                            <ui:VisualElement name="map-wind-1" class="map-wind map-wind--right-01" picking-mode="Ignore" />
                            <ui:VisualElement name="map-wind-2" class="map-wind map-wind--left-02" picking-mode="Ignore" />
                            <ui:VisualElement name="map-wind-3" class="map-wind map-wind--right-01" picking-mode="Ignore" />
                            <ui:VisualElement name="map-wind-4" class="map-wind map-wind--left-01" picking-mode="Ignore" />
                            <ui:VisualElement name="map-wind-5" class="map-wind map-wind--bottom-01" picking-mode="Ignore" />
                            <ui:VisualElement name="map-wind-6" class="map-wind map-wind--left-01" picking-mode="Ignore" />
                        </ui:VisualElement>
```

- [ ] **Шаг 5: добавить разметку экрана Окинавы**

Тот же блок с префиксом `okinawa-`, сразу после
`<ui:VisualElement class="pan-canvas-scrim" picking-mode="Ignore" />`
внутри `<ui:VisualElement name="okinawa-canvas" class="pan-canvas">`:

```xml
                        <!-- Плывущий слой Окинавы. Полосы те же, что на
                             Японии, см. MapWindLayout: композиция полос от
                             экрана не зависит. -->
                        <ui:VisualElement name="okinawa-wind-layer" class="map-wind-layer" picking-mode="Ignore">
                            <ui:VisualElement name="okinawa-wind-0" class="map-wind map-wind--left-01" picking-mode="Ignore" />
                            <ui:VisualElement name="okinawa-wind-1" class="map-wind map-wind--right-01" picking-mode="Ignore" />
                            <ui:VisualElement name="okinawa-wind-2" class="map-wind map-wind--left-02" picking-mode="Ignore" />
                            <ui:VisualElement name="okinawa-wind-3" class="map-wind map-wind--right-01" picking-mode="Ignore" />
                            <ui:VisualElement name="okinawa-wind-4" class="map-wind map-wind--left-01" picking-mode="Ignore" />
                            <ui:VisualElement name="okinawa-wind-5" class="map-wind map-wind--bottom-01" picking-mode="Ignore" />
                            <ui:VisualElement name="okinawa-wind-6" class="map-wind map-wind--left-01" picking-mode="Ignore" />
                        </ui:VisualElement>
```

- [ ] **Шаг 6: прогнать и убедиться, что проходит**

```bash
unity command run_tests --mode EditMode --format json
```

Ожидание: весь набор зелёный. Если тесты СОСЕДНИХ файлов вдруг стали
пустыми вместо красных — значит, в комментарий попало `--` и UXML перестал
загружаться целиком. Проверить оба вставленных комментария.

- [ ] **Шаг 7: коммит**

```bash
git add Assets/UI/MikeyApp.uxml Assets/UI/Map/Map.uss Assets/UI/Map/Tests/MapWindLayerUxmlTests.cs Assets/UI/Map/Tests/MapWindLayerUxmlTests.cs.meta
git commit -m "feat(map): разметка и стили плывущего слоя облаков"
```

---

### Task 4: MapWindLayer — владение элементами, раскладка и тик

**Files:**
- Create: `Assets/UI/Map/MapWindLayer.cs`
- Test: `Assets/UI/Map/Tests/MapWindLayerSourceTests.cs`

**Interfaces:**
- Consumes: `MapWindMath` (задача 1), `MapWindLayout.Clouds` и `MapWindLayout.Apply` (задача 2), имена элементов из задачи 3, а также существующие `MapAmbientMath.ParallaxOffset(float pan, float factor) -> float` и `MapPanZoomMath.EaseOutCubic(float t) -> float`.
- Produces:
  - `MapWindLayer.Bind(VisualElement root, string prefix)` — `prefix` это `"map-wind-"` или `"okinawa-wind-"`
  - `MapWindLayer.Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)`
  - `MapWindLayer.Reset()`
  - `MapWindLayer.SetVisible(bool visible)`
  - `MapWindLayer.SettleSeconds` — `public const float`

- [ ] **Шаг 1: написать падающий тест**

Создать `Assets/UI/Map/Tests/MapWindLayerSourceTests.cs`:

```csharp
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Mikey.UI.Map.Tests
{
    /// <summary>
    /// Контракт владельца плывущих облаков, читаемый по тексту исходника — по
    /// той же причине, что и остальные source-тесты карты: планировщик и
    /// раскладка UI Toolkit в EditMode не гоняются.
    ///
    /// Главное здесь — разделение писателей. Геометрию пишет ТОЛЬКО
    /// MapWindLayout.Apply и только при смене размера; тик не смеет коснуться
    /// раскладки ни разу.
    /// </summary>
    public class MapWindLayerSourceTests
    {
        private const string SourcePath = "Assets/UI/Map/MapWindLayer.cs";

        [Test]
        public void TickNeverWritesLayoutProperties()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath),
                "public void Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)");

            StringAssert.DoesNotContain("style.left", body);
            StringAssert.DoesNotContain("style.top", body);
            StringAssert.DoesNotContain("style.width", body);
            StringAssert.DoesNotContain("style.height", body);
            StringAssert.DoesNotContain("style.margin", body);
            StringAssert.DoesNotContain("style.padding", body);
        }

        [Test]
        public void TickDelegatesLayoutToAGuardedHelper()
        {
            string source = File.ReadAllText(SourcePath);
            string tick = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source,
                "public void Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)");
            StringAssert.Contains("EnsureLayout(canvasWidth, canvasHeight)", tick);

            string ensure = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(source,
                "private void EnsureLayout(float canvasWidth, float canvasHeight)");
            StringAssert.Contains("_laidOutWidth", ensure);
            StringAssert.Contains("_laidOutHeight", ensure);
            StringAssert.Contains("return", ensure);
        }

        [Test]
        public void ResetClearsInlineToNullNeverToLiteralZero()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void Reset()");

            StringAssert.Contains("style.translate = StyleKeyword.Null", body);
            StringAssert.Contains("style.scale = StyleKeyword.Null", body);
            StringAssert.Contains("style.rotate = StyleKeyword.Null", body);
            StringAssert.Contains("style.opacity = StyleKeyword.Null", body);

            // Считаем именно записи в style, а не любые "= 0f" в теле: Reset
            // законно обнуляет ещё и запомненный размер раскладки.
            int styleWrites = Regex.Matches(body, @"style\.\w+\s*=").Count;
            int nullWrites = Regex.Matches(body, @"style\.\w+\s*=\s*StyleKeyword\.Null").Count;
            Assert.AreEqual(4, styleWrites);
            Assert.AreEqual(styleWrites, nullWrites,
                "Каждое снятие инлайна обязано идти в Null, а не в литеральный ноль.");
        }

        [Test]
        public void ResetAlsoDropsTheLaidOutSizeSoTheNextEntryRelaysOut()
        {
            // Инлайн-геометрия живёт на элементах разметки и переживает уход с
            // экрана. Если Reset не сбросит запомненный размер, повторный вход
            // при изменившемся экране не переразложит слой.
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void Reset()");
            StringAssert.Contains("_laidOutWidth", body);
        }

        [Test]
        public void EveryCloudGetsDynamicUsageHints()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void Bind(VisualElement root, string prefix)");
            StringAssert.Contains("UsageHints.DynamicTransform | UsageHints.DynamicColor", body);
        }

        [Test]
        public void DoesNotStartASecondScheduler()
        {
            string source = File.ReadAllText(SourcePath);
            StringAssert.DoesNotContain("schedule.Execute", source,
                "Единственный планировщик 30 Гц — MapAmbientController.");
        }

        [Test]
        public void SetVisibleTogglesDisplayNotOpacity()
        {
            // display:none вырезает семь прозрачных квадов из цепочки
            // отрисовки; opacity:0 оставил бы их в ней целиком.
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "public void SetVisible(bool visible)");
            StringAssert.Contains("DisplayStyle.None", body);
            StringAssert.Contains("DisplayStyle.Flex", body);
        }

        [Test]
        public void IsTheOnlyWriterOfWindTransforms()
        {
            string source = File.ReadAllText(SourcePath);

            Assert.AreEqual(2, Regex.Matches(source, @"style\.translate\s*=").Count,
                "translate плывущих облаков пишут ровно два места: Tick и Reset.");
            Assert.AreEqual(2, Regex.Matches(source, @"style\.scale\s*=").Count);
            Assert.AreEqual(2, Regex.Matches(source, @"style\.rotate\s*=").Count);
            Assert.AreEqual(2, Regex.Matches(source, @"style\.opacity\s*=").Count);
        }

        [Test]
        public void UsesTheSharedParallaxWithItsCeiling()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath),
                "public void Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)");
            StringAssert.Contains("MapAmbientMath.ParallaxOffset", body,
                "Потолок MaxParallaxOffsetPixels обязан действовать и на ветер.");
        }
    }
}
```

- [ ] **Шаг 2: прогнать и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter Mikey.UI.Map.Tests --filter_type assembly --format json
```

Ожидание: ошибка компиляции — `MapWindLayer` не существует.

- [ ] **Шаг 3: написать реализацию**

Создать `Assets/UI/Map/MapWindLayer.cs`:

```csharp
using UnityEngine;
using UnityEngine.UIElements;

namespace Mikey.UI.Map
{
    /// <summary>
    /// Владелец семи плывущих облаков ОДНОГО экрана. Не MonoBehaviour: своего
    /// жизненного цикла у слоя нет, его целиком ведёт
    /// <see cref="MapAmbientController"/> — единственный планировщик 30 Гц во
    /// всей карте.
    ///
    /// <para>
    /// <b>Разделение писателей.</b> Геометрию (<c>width</c>/<c>height</c>/
    /// <c>top</c>/<c>left</c>) пишет только <see cref="MapWindLayout.Apply"/>
    /// и только через <see cref="EnsureLayout"/>, то есть лишь когда размер
    /// канваса реально изменился. Трансформ и прозрачность пишут только
    /// <see cref="Tick"/> и <see cref="Reset"/>. Пересечения нет ни в одном
    /// свойстве.
    /// </para>
    /// </summary>
    public sealed class MapWindLayer
    {
        /// <summary>За сколько секунд слой проявляется после показа экрана. Множитель идёт ТОЛЬКО на прозрачность: на положение он слепил бы все семь облаков в одну точку их полос.</summary>
        public const float SettleSeconds = 1.5f;

        private readonly VisualElement[] _clouds = new VisualElement[MapWindLayout.Clouds.Length];
        private VisualElement _layer;
        private float _laidOutWidth;
        private float _laidOutHeight;
        private bool _visible = true;

        /// <summary>Забирает элементы одного экрана. <paramref name="prefix"/> — "map-wind-" или "okinawa-wind-".</summary>
        public void Bind(VisualElement root, string prefix)
        {
            _layer = root?.Q<VisualElement>(prefix + "layer");
            _laidOutWidth = 0f;
            _laidOutHeight = 0f;
            _visible = true;

            for (int i = 0; i < _clouds.Length; i++)
            {
                _clouds[i] = root?.Q<VisualElement>(prefix + i.ToString());
                if (_clouds[i] != null)
                    _clouds[i].usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor;
            }
        }

        /// <summary>Один шаг движения слоя.</summary>
        public void Tick(float timeSeconds, float canvasWidth, float canvasHeight, float panX, float panY)
        {
            if (canvasWidth <= 0f || canvasHeight <= 0f)
                return;

            EnsureLayout(canvasWidth, canvasHeight);

            float settle = MapPanZoomMath.EaseOutCubic(timeSeconds / SettleSeconds);

            for (int i = 0; i < _clouds.Length; i++)
            {
                VisualElement cloud = _clouds[i];
                if (cloud == null)
                    continue;

                WindCloud lane = MapWindLayout.Clouds[i];
                float progress = MapWindMath.LaneProgress(timeSeconds, lane.CrossSeconds, lane.Phase);
                float cloudWidth = canvasWidth * lane.WidthFraction;

                float x = MapWindMath.LaneOffsetX(progress, canvasWidth, cloudWidth)
                    + MapAmbientMath.ParallaxOffset(panX, lane.ParallaxFactor);
                float y = MapWindMath.Bob(timeSeconds, lane.CrossSeconds, lane.Phase, canvasHeight)
                    + MapAmbientMath.ParallaxOffset(panY, lane.ParallaxFactor);

                float swell = MapWindMath.Swell(timeSeconds, lane.CrossSeconds, lane.Phase);
                float roll = MapWindMath.RollDegrees(timeSeconds, lane.CrossSeconds, lane.Phase);

                cloud.style.translate = new Translate(x, y);
                cloud.style.scale = new Scale(new Vector2(swell, swell));
                cloud.style.rotate = new Rotate(new Angle(roll, AngleUnit.Degree));
                cloud.style.opacity = MapWindMath.Opacity(
                    lane.RestOpacity, progress, timeSeconds, lane.CrossSeconds, lane.Phase, settle);
            }
        }

        /// <summary>
        /// Перекладывает слой, только если размер канваса действительно
        /// изменился. Без этой проверки раскладка шла бы каждый кадр — ровно
        /// та цена, которой весь дизайн избегает.
        /// </summary>
        private void EnsureLayout(float canvasWidth, float canvasHeight)
        {
            if (canvasWidth == _laidOutWidth && canvasHeight == _laidOutHeight)
                return;

            for (int i = 0; i < _clouds.Length; i++)
                MapWindLayout.Apply(_clouds[i], MapWindLayout.Clouds[i], canvasWidth, canvasHeight);

            _laidOutWidth = canvasWidth;
            _laidOutHeight = canvasHeight;
        }

        /// <summary>
        /// Снимает всё, что написал тик. Прозрачность уходит в Null, а не в
        /// литеральный ноль: покой задан правилом ".map-wind" в USS, и Null
        /// возвращает элемент именно к нему.
        /// </summary>
        public void Reset()
        {
            for (int i = 0; i < _clouds.Length; i++)
            {
                VisualElement cloud = _clouds[i];
                if (cloud == null)
                    continue;

                cloud.style.translate = StyleKeyword.Null;
                cloud.style.scale = StyleKeyword.Null;
                cloud.style.rotate = StyleKeyword.Null;
                cloud.style.opacity = StyleKeyword.Null;
            }

            _laidOutWidth = 0f;
            _laidOutHeight = 0f;
        }

        /// <summary>
        /// Показывает или прячет слой целиком. Через display, а не через
        /// прозрачность: прозрачный элемент всё равно отдаёт геометрию в
        /// цепочку отрисовки, и «меньше движения» тогда не экономило бы ничего.
        /// </summary>
        public void SetVisible(bool visible)
        {
            if (_layer == null || visible == _visible)
                return;

            _visible = visible;
            _layer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
```

- [ ] **Шаг 4: прогнать и убедиться, что проходит**

```bash
unity command run_tests --mode EditMode --format json
```

Ожидание: весь набор зелёный.

- [ ] **Шаг 5: коммит**

```bash
git add Assets/UI/Map/MapWindLayer.cs Assets/UI/Map/MapWindLayer.cs.meta Assets/UI/Map/Tests/MapWindLayerSourceTests.cs Assets/UI/Map/Tests/MapWindLayerSourceTests.cs.meta
git commit -m "feat(map): владелец плывущего слоя облаков"
```

---

### Task 5: подключение слоя к драйверу и к «меньше движения»

**Files:**
- Modify: `Assets/UI/Map/MapAmbientController.cs`
- Modify: `Assets/UI/Map/Tests/MapAmbientControllerSourceTests.cs`

**Interfaces:**
- Consumes: `MapWindLayer.Bind/Tick/Reset/SetVisible` из задачи 4.
- Produces: ничего для последующих задач — задача 6 трогает только рамку.

- [ ] **Шаг 1: написать падающий тест**

Дописать в `Assets/UI/Map/Tests/MapAmbientControllerSourceTests.cs` перед
закрывающей скобкой класса:

```csharp
        [Test]
        public void BindsTheWindLayerForTheScreenItResolved()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void ResolveScreenElements(string screenId)");

            StringAssert.Contains("_wind.Bind(", body);
            StringAssert.Contains("\"map-wind-\"", body);
            StringAssert.Contains("\"okinawa-wind-\"", body);
        }

        [Test]
        public void TicksTheWindLayerOnlyWhenMotionIsAllowed()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void Tick()");

            int visibility = body.IndexOf("_wind.SetVisible(!liveReducedMotion)", System.StringComparison.Ordinal);
            int reducedBranch = body.IndexOf("if (liveReducedMotion)", System.StringComparison.Ordinal);
            int windTick = body.IndexOf("TickWind()", System.StringComparison.Ordinal);

            Assert.Greater(visibility, -1, "Видимость слоя обязана следовать за настройкой.");
            Assert.Greater(reducedBranch, -1);
            Assert.Greater(windTick, reducedBranch,
                "Ветер обязан тикать ПОСЛЕ ветки раннего выхода по «меньше движения».");
            Assert.Less(visibility, reducedBranch,
                "Слой обязан прятаться до раннего выхода, иначе включённая настройка оставит его на экране.");
        }

        [Test]
        public void ReturnsTheWindLayerToRestWhenTickingStops()
        {
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void StopTicking()");
            StringAssert.Contains("_wind.Reset()", body);
        }
```

- [ ] **Шаг 2: прогнать и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter Mikey.UI.Map.Tests --filter_type assembly --format json
```

Ожидание: три новых теста красные.

- [ ] **Шаг 3: подключить слой**

В `Assets/UI/Map/MapAmbientController.cs`:

Рядом с объявлением `private readonly VisualElement[] _clouds` добавить поле:

```csharp
        /// <summary>Плывущий слой текущего экрана. Своего планировщика у него нет — его тикает Tick ниже.</summary>
        private readonly MapWindLayer _wind = new MapWindLayer();
```

В конце `ResolveScreenElements(string screenId)`, после цикла привязки
облаков рамки, добавить:

```csharp
            _wind.Bind(_root, japan ? "map-wind-" : "okinawa-wind-");
```

В `StopTicking()`, рядом с `ResetCloudDrift();`, добавить:

```csharp
            _wind.Reset();
```

В `Tick()` — сразу после строки
`bool liveReducedMotion = _motion != null && _motion.ReducedMotion;`
добавить:

```csharp
            _wind.SetVisible(!liveReducedMotion);
```

и в самом конце `Tick()`, после `TickCamera();`, добавить:

```csharp
            TickWind();
```

Затем добавить сам метод рядом с `TickClouds()`:

```csharp
        /// <summary>
        /// Двигает плывущий слой. Размер канваса и пан читаются ровно так же,
        /// как в <see cref="TickClouds"/> — слой живёт в том же
        /// трансформированном канвасе, что и рамка.
        /// </summary>
        private void TickWind()
        {
            float width = _canvas?.resolvedStyle.width ?? 0f;
            float height = _canvas?.resolvedStyle.height ?? 0f;
            if (width <= 0f || height <= 0f)
                return;

            _wind.Tick(_elapsedSeconds, width, height,
                _panZoom?.CurrentPanX ?? 0f, _panZoom?.CurrentPanY ?? 0f);
        }
```

- [ ] **Шаг 4: прогнать и убедиться, что проходит**

```bash
unity command run_tests --mode EditMode --format json
```

Ожидание: весь набор зелёный. В частности, `NeverWritesLayoutProperties`
для `MapAmbientController.cs` обязан остаться зелёным — новый код не пишет
геометрию.

- [ ] **Шаг 5: коммит**

```bash
git add Assets/UI/Map/MapAmbientController.cs Assets/UI/Map/Tests/MapAmbientControllerSourceTests.cs
git commit -m "feat(map): подключить плывущий слой к ambient-драйверу"
```

---

### Task 6: оживить рамку — амплитуды, составная траектория, набухание и крен

**Files:**
- Modify: `Assets/UI/Map/MapAmbientMath.cs`
- Modify: `Assets/UI/Map/MapAmbientController.cs`
- Modify: `Assets/UI/Map/Tests/MapAmbientMathTests.cs`

**Interfaces:**
- Consumes: `MapPanZoomMath.EaseOutCubic(float t) -> float` (уже существует), `MapCloudLayout.JapanRest`/`OkinawaRest` для базового угла.
- Produces: `MapAmbientMath.Compound`, `MapAmbientMath.DriftSettle`, `MapAmbientMath.FrameSwell`, `MapAmbientMath.FrameRollDegrees`.

- [ ] **Шаг 1: написать падающий тест**

Дописать в `Assets/UI/Map/Tests/MapAmbientMathTests.cs` перед закрывающей
скобкой класса:

```csharp
        [Test]
        public void FrameDriftIsFinallyAboveThePerceptionFloor()
        {
            // Прежние 0.012/0.006 давали размах ниже порога различения — это и
            // была вся причина, по которой небо читалось мёртвым.
            Assert.GreaterOrEqual(MapAmbientMath.DriftAmplitudeX, 0.03f);
            Assert.GreaterOrEqual(MapAmbientMath.DriftAmplitudeY, 0.014f);
            Assert.Less(MapAmbientMath.DriftAmplitudeY, MapAmbientMath.DriftAmplitudeX,
                "Небо обязано читаться как боковой снос, а не как качели.");
        }

        [Test]
        public void Compound_StaysInsideTheUnitRange()
        {
            for (int i = 0; i <= 2000; i++)
            {
                float value = MapAmbientMath.Compound(i * 0.1f, 26f, 0.37f);
                Assert.GreaterOrEqual(value, -1f - Tolerance,
                    "Без нормировки сумма двух синусоид выносит рамку за её амплитуду.");
                Assert.LessOrEqual(value, 1f + Tolerance);
            }
        }

        [Test]
        public void Compound_DoesNotRepeatWithinTheBasePeriod()
        {
            // Одна синусоида вернулась бы в ту же точку ровно через период.
            // Составная — не должна, иначе небо снова читается зацикленной гифкой.
            float atStart = MapAmbientMath.Compound(0.5f, 26f, 0.37f);
            float aPeriodLater = MapAmbientMath.Compound(26.5f, 26f, 0.37f);
            // NUnit 3 не имеет перегрузки AreNotEqual с допуском — только AreEqual.
            Assert.Greater(System.Math.Abs(atStart - aPeriodLater), Tolerance);
        }

        [Test]
        public void DriftSettle_RisesFromRestToFull()
        {
            Assert.AreEqual(0f, MapAmbientMath.DriftSettle(0f), Tolerance,
                "Вход обязан начинаться из покоя: при ненулевых фазах смещение в нуле времени иначе НЕ ноль.");
            Assert.AreEqual(1f, MapAmbientMath.DriftSettle(MapAmbientMath.DriftSettleSeconds), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.DriftSettle(99f), Tolerance);

            float previous = -1f;
            for (int i = 0; i <= 30; i++)
            {
                float value = MapAmbientMath.DriftSettle(i * MapAmbientMath.DriftSettleSeconds / 30f);
                Assert.GreaterOrEqual(value, previous);
                previous = value;
            }
        }

        [Test]
        public void CloudDrift_StartsAtExactRestForEveryCloud()
        {
            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                MapAmbientMath.CloudDrift(i, 0f, 1000f, 500f,
                    out float dx, out float dy, out float dOpacity);

                Assert.AreEqual(0f, dx, Tolerance, $"Облако {i} прыгает по горизонтали на первом кадре.");
                Assert.AreEqual(0f, dy, Tolerance, $"Облако {i} прыгает по вертикали на первом кадре.");
                Assert.AreEqual(0f, dOpacity, Tolerance, $"Облако {i} прыгает прозрачностью на первом кадре.");
            }
        }

        [Test]
        public void CloudDrift_StaysInsideItsDeclaredAmplitude()
        {
            const float width = 1000f;
            const float height = 500f;

            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                for (int step = 0; step <= 2000; step++)
                {
                    MapAmbientMath.CloudDrift(i, step * 0.1f, width, height,
                        out float dx, out float dy, out float dOpacity);

                    Assert.LessOrEqual(System.Math.Abs(dx), width * MapAmbientMath.DriftAmplitudeX + Tolerance);
                    Assert.LessOrEqual(System.Math.Abs(dy), height * MapAmbientMath.DriftAmplitudeY + Tolerance);
                    Assert.LessOrEqual(System.Math.Abs(dOpacity), MapAmbientMath.DriftOpacityAmplitude + Tolerance);
                }
            }
        }

        [Test]
        public void FrameSwell_StartsAtRestAndStaysInsideItsAmplitude()
        {
            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                Assert.AreEqual(1f, MapAmbientMath.FrameSwell(i, 0f), Tolerance,
                    "Экран уже показан: любой масштаб кроме единицы на первом кадре — видимый скачок.");

                for (int step = 0; step <= 1000; step++)
                {
                    float value = MapAmbientMath.FrameSwell(i, step * 0.2f);
                    Assert.GreaterOrEqual(value, 1f - Tolerance);
                    Assert.LessOrEqual(value, 1f + MapAmbientMath.FrameSwellAmplitude + Tolerance);
                }
            }
        }

        [Test]
        public void FrameRoll_StartsAtRestAndStaysInsideItsAmplitude()
        {
            for (int i = 0; i < MapAmbientMath.CloudCount; i++)
            {
                Assert.AreEqual(0f, MapAmbientMath.FrameRollDegrees(i, 0f), Tolerance);

                for (int step = 0; step <= 1000; step++)
                {
                    float value = MapAmbientMath.FrameRollDegrees(i, step * 0.2f);
                    Assert.LessOrEqual(System.Math.Abs(value),
                        MapAmbientMath.FrameRollAmplitudeDegrees + Tolerance);
                }
            }
        }

        [Test]
        public void FrameHelpersAreSafeOnOutOfRangeIndex()
        {
            Assert.AreEqual(1f, MapAmbientMath.FrameSwell(-1, 5f), Tolerance);
            Assert.AreEqual(1f, MapAmbientMath.FrameSwell(MapAmbientMath.CloudCount, 5f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.FrameRollDegrees(-1, 5f), Tolerance);
            Assert.AreEqual(0f, MapAmbientMath.FrameRollDegrees(MapAmbientMath.CloudCount, 5f), Tolerance);
        }
```

Дописать в `Assets/UI/Map/Tests/MapAmbientControllerSourceTests.cs`:

```csharp
        [Test]
        public void FrameRollAddsToThePresetAngleInsteadOfReplacingIt()
        {
            // bottom1 повёрнуто на -180 градусов. Запись голого крена
            // перевернула бы его обратно.
            string body = MapPanZoomControllerAmbientSourceTests.ExtractMethodBody(
                File.ReadAllText(SourcePath), "private void TickClouds()");

            StringAssert.Contains("_cloudRestRotation[i]", body,
                "Крен обязан складываться с углом из пресета.");
            StringAssert.Contains("FrameRollDegrees", body);
        }
```

- [ ] **Шаг 2: прогнать и убедиться, что падает**

```bash
unity command run_tests --mode EditMode --filter Mikey.UI.Map.Tests --filter_type assembly --format json
```

Ожидание: новые тесты красные — константы и методы не существуют.

- [ ] **Шаг 3: правки в MapAmbientMath**

Заменить три существующие константы:

```csharp
        /// <summary>Доля ширины канваса, на которую облако рамки уходит от своей раскладки по горизонтали. 0.035, а не прежние 0.012: ниже примерно трёх процентов движение просто не различается глазом, и небо читается мёртвым.</summary>
        public const float DriftAmplitudeX = 0.035f;

        /// <summary>Доля высоты канваса по вертикали — вдвое меньше горизонтальной: небо читается как боковой снос, а не как качели.</summary>
        public const float DriftAmplitudeY = 0.016f;

        /// <summary>На сколько прозрачность облака уходит от своего значения покоя из MapCloudLayout.</summary>
        public const float DriftOpacityAmplitude = 0.07f;
```

Добавить новые константы и методы рядом с `CloudDrift`:

```csharp
        /// <summary>Период второй синусоиды составной траектории как доля основного периода.</summary>
        public const float CompoundRatio = 0.377f;

        /// <summary>Вес второй синусоиды.</summary>
        public const float CompoundWeight = 0.42f;

        /// <summary>
        /// Составная траектория: вторая синусоида с несоизмеримым периодом
        /// поверх первой. Одна синусоида возвращается в ту же точку ровно
        /// через период, и небо читается зацикленной гифкой.
        ///
        /// <para>
        /// Нормировка на <c>1 + CompoundWeight</c> обязательна: без неё сумма
        /// двух синусоид выходит за заявленную амплитуду, и рамка уезжает
        /// дальше, чем разрешено композицией.
        /// </para>
        /// </summary>
        public static float Compound(float timeSeconds, float periodSeconds, float phase01)
        {
            float primary = Wave(timeSeconds, periodSeconds, phase01);
            float secondary = Wave(timeSeconds, periodSeconds * CompoundRatio, phase01);
            return (primary + CompoundWeight * secondary) / (1f + CompoundWeight);
        }

        /// <summary>За сколько секунд движение рамки набирает полную амплитуду после показа экрана.</summary>
        public const float DriftSettleSeconds = 1.5f;

        /// <summary>
        /// Множитель ввода рамки в движение, 0 в нуле времени и 1 после
        /// <see cref="DriftSettleSeconds"/>.
        ///
        /// <para>
        /// Нужен потому, что фазы <see cref="CloudDriftPhases"/> ненулевые: при
        /// t = 0 синусоида уже не в нуле, и для фазы 0.37 это около 0.73
        /// амплитуды. На прежних 0.012 ширины это были невидимые 0.9%; на
        /// нынешних 0.035 — заметный скачок на первом же кадре после показа
        /// экрана. Отказаться от фаз нельзя: без них четыре облака стартуют
        /// строем.
        /// </para>
        /// </summary>
        public static float DriftSettle(float timeSeconds)
        {
            if (!IsFinite(timeSeconds) || timeSeconds <= 0f)
                return 0f;
            if (timeSeconds >= DriftSettleSeconds)
                return 1f;

            return MapPanZoomMath.EaseOutCubic(timeSeconds / DriftSettleSeconds);
        }

        /// <summary>Амплитуда набухания облака рамки как добавка к масштабу.</summary>
        public const float FrameSwellAmplitude = 0.04f;

        /// <summary>Период набухания рамки как доля периода дрейфа своего облака.</summary>
        public const float FrameSwellPeriodRatio = 0.618f;

        /// <summary>Амплитуда крена рамки в градусах.</summary>
        public const float FrameRollAmplitudeDegrees = 1.5f;

        public const float FrameRollPeriodRatio = 0.347f;

        /// <summary>
        /// Множитель масштаба облака рамки. Через <see cref="Breath"/>, а не
        /// через фазированную форму: экран уже показан, облака уже стоят на
        /// местах, и любой масштаб кроме единицы на первом кадре был бы
        /// видимым скачком. Разные периоды у четырёх облаков расфазируют их
        /// сами, стартовав из общего покоя.
        /// </summary>
        public static float FrameSwell(int index, float timeSeconds)
        {
            if (index < 0 || index >= CloudCount)
                return 1f;

            return Breath(timeSeconds, CloudDriftPeriodsSeconds[index] * FrameSwellPeriodRatio, FrameSwellAmplitude);
        }

        /// <summary>
        /// Крен облака рамки в градусах. Фаза здесь намеренно нулевая — по той
        /// же причине, что и у <see cref="FrameSwell"/>: старт обязан быть из
        /// покоя.
        /// </summary>
        public static float FrameRollDegrees(int index, float timeSeconds)
        {
            if (index < 0 || index >= CloudCount)
                return 0f;

            return FrameRollAmplitudeDegrees
                * Wave(timeSeconds, CloudDriftPeriodsSeconds[index] * FrameRollPeriodRatio, 0f);
        }
```

Заменить тело `CloudDrift` (сохранив сигнатуру и ранние выходы):

```csharp
            float period = CloudDriftPeriodsSeconds[index];
            float phase = CloudDriftPhases[index];
            float settle = DriftSettle(timeSeconds);

            offsetX = canvasWidth * DriftAmplitudeX * Compound(timeSeconds, period, phase) * settle;
            // Вертикаль идёт своим, более длинным периодом — иначе облако
            // ходило бы по прямой под 45 градусов вместо неспешной петли.
            offsetY = canvasHeight * DriftAmplitudeY * Compound(timeSeconds, period * 1.618f, phase) * settle;
            opacityDelta = DriftOpacityAmplitude * Wave(timeSeconds, period * 0.77f, phase) * settle;
```

- [ ] **Шаг 4: правки в MapAmbientController**

Рядом с `private readonly float[] _cloudRestOpacity` добавить:

```csharp
        /// <summary>Угол покоя облака рамки из пресета. Крен складывается с ним: bottom1 повёрнуто на -180 градусов, и запись голого крена перевернула бы его обратно.</summary>
        private readonly float[] _cloudRestRotation = new float[MapAmbientMath.CloudCount];
```

В `ResolveScreenElements`, рядом с массивом `restOpacity`, добавить:

```csharp
            float[] restRotation =
            {
                preset.Right1.RotationDegrees,
                preset.Left1.RotationDegrees,
                preset.Left2.RotationDegrees,
                preset.Bottom1.RotationDegrees,
            };
```

и внутри цикла привязки, рядом с `_cloudRestOpacity[i] = restOpacity[i];`:

```csharp
                _cloudRestRotation[i] = restRotation[i];
```

В `ResetCloudDrift()`, внутри цикла, после строки с прозрачностью добавить:

```csharp
                cloud.style.scale = StyleKeyword.Null;
                cloud.style.rotate = new Rotate(new Angle(_cloudRestRotation[i], AngleUnit.Degree));
```

(угол возвращается ЗНАЧЕНИЕМ, а не в Null: покой угла — тоже инлайн, его
пишет `MapCloudLayout.Apply` из пресета, и Null стёр бы вместе с креном ещё
и раскладку — ровно та же причина, что уже записана для прозрачности.)

В `TickClouds()`, после записи `cloud.style.translate`, добавить:

```csharp
                float swell = MapAmbientMath.FrameSwell(i, _elapsedSeconds);
                cloud.style.scale = new Scale(new Vector2(swell, swell));
                cloud.style.rotate = new Rotate(new Angle(
                    _cloudRestRotation[i] + MapAmbientMath.FrameRollDegrees(i, _elapsedSeconds),
                    AngleUnit.Degree));
```

- [ ] **Шаг 5: прогнать и убедиться, что проходит**

```bash
unity command run_tests --mode EditMode --format json
```

Ожидание: весь набор зелёный.

- [ ] **Шаг 6: коммит**

```bash
git add Assets/UI/Map/MapAmbientMath.cs Assets/UI/Map/MapAmbientController.cs Assets/UI/Map/Tests/MapAmbientMathTests.cs Assets/UI/Map/Tests/MapAmbientControllerSourceTests.cs
git commit -m "feat(map): оживить облака рамки объёмом, креном и составной траекторией"
```

---

## Проверка глазами после задачи 6

Ни один кадр этой работы не видел человек. Порядок осмотра:

1. **Войти на Японию и не трогать ничего минуту.** Облака должны идти
   слева направо на трёх разных скоростях. Если все семь идут с одной
   скоростью — не подхватилась таблица полос.
2. **Смотреть на первую секунду после входа.** Ни вспышки, ни рывка:
   рамка вводится смещением за полторы секунды, ветер — прозрачностью.
3. **Панить карту до упора вправо и следить за краями.** Дальние облака
   должны отставать от карты, ближнее — обгонять. Голого края карты
   появиться не должно нигде.
4. **Стоять три минуты и следить за одним дальним облаком.** В момент
   заворачивания не должно быть видно ни прыжка, ни мигания.
5. **Включить «меньше движения» в настройках.** Плывущие облака должны
   исчезнуть целиком, рамка — замереть. Выключить: слой возвращается.
