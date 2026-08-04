# Щедрый зачёт приседаний + без голоса — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Приседания перестают терять повторы (лечение, уже доказанное на пуш-апе), песочница уровня 0 молчит, каждый CSV — одна сессия.

**Architecture:** `SquatAnalyzer` получает дефолты пуш-апа (α=1, счётчик с дебаунсом 2, `ResetDownStreak` на невалидных кадрах) — пороги 160°/100° не трогаем (глубина пользователя 74–99°, пороги не виноваты). Красной фазой служит новый корпус-тест: склейка сегодняшних записей со старой конфигурацией даёт 11, с новой — 16. Голос и очистка буфера — механические правки песочницы/контроллера.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-05-squat-recall-no-voice-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; при падениях смотреть `Temp/pose_tests.xml`):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Temp\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Если корпус приседаний даёт НЕ 16 с новыми дефолтами — BLOCKED с фактическим числом, ожидание не подгонять (расхождение C# с эталонным реплеем разбирает контролёр).
- Исходник записи: `C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_012743.csv`.
- Пороги углов приседания (160/100/0.3) и наклона корпуса (50°) НЕ меняются; тесты пуш-апа/RepCounter/остальных техник остаются зелёными без правок.
- Новые файлы получат `.meta` при прогоне — добавлять в коммит; посторонние изменённые файлы (арена) не трогать.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Щедрые дефолты SquatAnalyzer + корпус

**Files:**
- Modify: `Assets/Pose/SquatAnalyzer.cs` (ctor ~строки 47-56; ветка невалидного кадра ~78-86; докстрока класса)
- Modify: `Assets/Pose/Tests/SquatAnalyzerTests.cs` (последовательности с двумя кадрами низа)
- Create: `Assets/Pose/Tests/Recordings/squats_mixed_sessions.csv` (копия исходника из Global Constraints)
- Test: `Assets/Pose/Tests/SquatRecordingTests.cs` (новый)

**Interfaces:**
- Consumes: `RepCounter(float upThresholdDeg = 140f, float downThresholdDeg = 105f, double minRepSeconds = 0.3, int downDebounceFrames = 1)`, `RepCounter.ResetDownStreak()`, `CsvPoseFrames.Load(string)`, `LegTestFrames.Squat(...)` — всё уже в кодовой базе.
- Produces: `SquatAnalyzer(RepCounter counter = null, float minVisibility = 0.6f, float maxTorsoLeanDeg = 50f, float smoothingAlpha = 1f)` — дефолтный счётчик `new RepCounter(upThresholdDeg: 160f, downThresholdDeg: 100f, minRepSeconds: 0.3, downDebounceFrames: 2)`.

- [ ] **Step 1: Корпус и адаптация тестов (красная фаза)**

1. Скопировать запись:

```powershell
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_012743.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\squats_mixed_sessions.csv"
```

2. Создать `Assets/Pose/Tests/SquatRecordingTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Characterization corpus for squats: a real on-device capture (2026-08-05) that glues
    /// several sessions together (the recording buffer wasn't cleared between exercises back
    /// then), so 16 is what the lenient configuration finds — not user ground truth. Guards
    /// against scoring regressions, nothing more.
    /// </summary>
    public class SquatRecordingTests
    {
        [Test]
        public void MixedSessionsRecording_CountsSixteen()
        {
            var analyzer = new SquatAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/squats_mixed_sessions.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(16, analyzer.Reps);
        }
    }
}
```

3. В `Assets/Pose/Tests/SquatAnalyzerTests.cs` дать низу два кадра (дебаунс), сохранив ассерты:
   - `CountsCleanRep`: последовательность `175@0.0, 95@1.0, 95@1.5, 175@2.0` (было три кадра);
   - `TorsoLeanAtBottomIsTalliedButStillCounts`: `175@0.0, 95(lean:60)@1.0, 95(lean:60)@1.5, 175@2.0`;
   - `ResetClearsSet`: `175@0.0, 95@1.0, 95@1.5, 175@2.0`, затем `Reset()`;
   - `ShallowSquatDoesNotCount`, `ThresholdJitterDoesNotProducePhantomReps`, `LowVisibilityPausesCountingAndReportsNotVisible`, `RegisteredInCatalog` — НЕ менять (совместимы с обеими конфигурациями; джиттер-тест с дебаунсом обретает второй смысл).

- [ ] **Step 2: Прогнать — красная фаза**

Run: команда тестов. Expected: `exit=6`, упал ровно один тест — `MixedSessionsRecording_CountsSixteen` (старая конфигурация даёт 11, не 16). Адаптированные `SquatAnalyzerTests` зелёные и со старыми дефолтами.

- [ ] **Step 3: Реализация**

`Assets/Pose/SquatAnalyzer.cs`:

1. Конструктор:

```csharp
        public SquatAnalyzer(RepCounter counter = null, float minVisibility = 0.6f,
            float maxTorsoLeanDeg = 50f, float smoothingAlpha = 1f)
        {
            // Дефолт: без сглаживания (α=1) + дебаунс низа — та же пара, что вылечила пуш-ап:
            // на реальном fps устройства EMA не успевала довести угол до порога (повторы
            // терялись), а от одиночных шумовых кадров защищает дебаунс.
            _counter = counter ?? new RepCounter(upThresholdDeg: 160f, downThresholdDeg: 100f,
                minRepSeconds: 0.3, downDebounceFrames: 2);
            _minVisibility = minVisibility;
            _maxTorsoLeanDeg = maxTorsoLeanDeg;
            _smoothingAlpha = smoothingAlpha;
        }
```

2. В ветке невалидного кадра (`if (_lastVis < _minVisibility)`) перед `Changed?.Invoke()` добавить:

```csharp
                _counter.ResetDownStreak();
```

3. В докстроке класса дополнить: «Smoothing is off by default (α = 1); the bottom phase is debounced (2 consecutive below-threshold frames) — the pair that fixed push-up recall on real device fps.»

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`; корпус приседаний = 16; корпус пуш-апа (5/4/0) и все остальные — без изменений.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/SquatAnalyzer.cs Assets/Pose/Tests/SquatAnalyzerTests.cs Assets/Pose/Tests/SquatRecordingTests.cs* Assets/Pose/Tests/Recordings/squats_mixed_sessions.csv*
git commit -m "feat: щедрый зачёт приседаний — дебаунс вместо EMA, корпус-характеризация 16"
```

---

### Task 2: Без голоса + буфер записи по сессиям

**Files:**
- Modify: `Assets/Pose/DevSandbox/ExerciseSandbox.cs` (поля ~33-38; Awake ~41-49; Update ~54-79 целиком; OnDestroy ~81)
- Modify: `Assets/Pose/PoseController.cs` (`SelectExercise` ~67-75)

**Interfaces:**
- Consumes: ничего нового. `AndroidVoice` класс ОСТАЁТСЯ в проекте (не удалять файл) — его перестаёт использовать песочница.

- [ ] **Step 1: Внести правки**

`Assets/Pose/DevSandbox/ExerciseSandbox.cs` — удалить озвучку целиком:

1. Удалить поля `_voice`, `_lastSpokenCue`, `_lastSpeakTime` и константу `MinSpeakInterval`.
2. В `Awake()` удалить строку `_voice = new AndroidVoice();`.
3. Удалить метод `Update()` целиком (после удаления голосового блока в нём не остаётся ничего).
4. Удалить метод `OnDestroy()` целиком (он только диспоузил голос).
5. В докстроке класса, если упоминается озвучка, — убрать упоминание.

`Assets/Pose/PoseController.cs` — в `SelectExercise`, после проверки на null аргумента, добавить:

```csharp
            // Уровень 0 проверяет, а не учит: каждый CSV — одна сессия одного упражнения,
            // иначе записи склеиваются и разбор с устройства теряет граунд-трус.
            _recording.Clear();
```

- [ ] **Step 2: Прогнать тесты (компиляция + регрессия).** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 3: Commit**

```powershell
git add Assets/Pose/DevSandbox/ExerciseSandbox.cs Assets/Pose/PoseController.cs
git commit -m "feat: уровень 0 молчит — голосовые подсказки убраны; запись чистится по сессиям"
```

---

### Task 3: Пересборка и установка

**Files:** без изменений кода.

- [ ] **Step 1: Сборка** (Editor закрыт)

```powershell
unity build "C:\Users\user\Mikey" --target Android --execute-method Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid --no-banner; "exit=$LASTEXITCODE"
```

Expected: `exit=0`, свежий mtime `Builds/ExerciseSandbox.apk` (несвежий — не устанавливать, эскалировать).

- [ ] **Step 2: Установка**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\user\Mikey\Builds\ExerciseSandbox.apk"
```

Expected: `Success`.

- [ ] **Step 3: Пользовательская проверка** — 8 приседаний сбоку → счёт 7–8; голоса нет; каждый выход по Back даёт отдельный CSV своей сессии.
