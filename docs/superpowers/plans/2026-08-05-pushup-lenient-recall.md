# Щедрый зачёт пуш-апа — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Настоящие отжимания перестают теряться (5 из 5–6 на записи с граунд-трусом), удержание планки не даёт фантомов; корпус растёт до трёх записей 5/4/0.

**Architecture:** Дебаунс входа в фазу «низ» в `RepCounter` (новый параметр, дефолт 1 — приседания не затронуты; пуш-ап передаёт 2 + сброс серии на невалидных кадрах) вместо EMA-сглаживания (дефолт α → 1.0). Щедрые дефолты оценщика: `minVisibility` 0.5, `positionMinDeg` 120. Числа корпуса 5/4/0 получены реплеем ровно этой конфигурации — реализация обязана их воспроизвести.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-04-pushup-lenient-recall-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; при падениях смотреть `Temp/pose_tests.xml`):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Temp\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Если корпус даёт НЕ 5/4/0 — статус BLOCKED с фактическими числами; пороги и ожидания НЕ подгонять (это признак расхождения C# с эталонным реплеем — разбирает контролёр).
- Исходник новой записи: `C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_205917.csv`.
- Поведение `SquatAnalyzer` не меняется (дефолт дебаунса 1); его тесты должны остаться зелёными без правок.
- Новые файлы получат `.meta` при прогоне — добавлять в коммит; посторонние изменённые файлы (арена) не трогать.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Дебаунс в RepCounter

**Files:**
- Modify: `Assets/Pose/RepCounter.cs` (ctor ~строки 30-40; `Update` ~53-75; `Reset` ~78-84)
- Test: `Assets/Pose/Tests/RepCounterTests.cs`

**Interfaces:**
- Produces: `RepCounter(float upThresholdDeg = 140f, float downThresholdDeg = 105f, double minRepSeconds = 0.3, int downDebounceFrames = 1)`; `public void ResetDownStreak()` (сбрасывает серию подряд идущих «низких» кадров — анализатор зовёт её на невалидных кадрах, чтобы серия не переживала провалы трекинга). Поведение при `downDebounceFrames = 1` бит-в-бит прежнее.

- [ ] **Step 1: Написать падающие тесты**

В `Assets/Pose/Tests/RepCounterTests.cs` добавить:

```csharp
        [Test]
        public void SingleFrameSpikeDoesNotOpenDown_WithDebounce()
        {
            var counter = new RepCounter(downDebounceFrames: 2);
            counter.Update(170f, 0.0);
            counter.Update(100f, 0.5);                 // одиночный шумовой скачок
            Assert.IsFalse(counter.Update(170f, 1.0)); // Down не открылся — не повтор
            Assert.AreEqual(0, counter.Reps);
            counter.Update(100f, 1.5);
            counter.Update(100f, 2.0);                 // два подряд — Down открыт
            Assert.IsTrue(counter.Update(170f, 2.5));
            Assert.AreEqual(1, counter.Reps);
        }

        [Test]
        public void MidBandFrameBreaksTheDebounceStreak()
        {
            var counter = new RepCounter(downDebounceFrames: 2);
            counter.Update(170f, 0.0);
            counter.Update(100f, 0.5);
            counter.Update(120f, 1.0);                 // середина диапазона — серия сброшена
            counter.Update(100f, 1.5);
            counter.Update(170f, 2.0);                 // Down так и не открылся
            Assert.AreEqual(0, counter.Reps);
        }

        [Test]
        public void ResetDownStreakBreaksTheSeries()
        {
            var counter = new RepCounter(downDebounceFrames: 2);
            counter.Update(170f, 0.0);
            counter.Update(100f, 0.5);
            counter.ResetDownStreak();                 // провал трекинга между кадрами
            counter.Update(100f, 1.0);                 // серия начата заново — это 1-й, не 2-й
            counter.Update(170f, 1.5);
            Assert.AreEqual(0, counter.Reps);
        }
```

- [ ] **Step 2: Прогнать — падают** (нет параметра/метода — ошибка компиляции, exit=1). Run: команда тестов.

- [ ] **Step 3: Реализация**

`Assets/Pose/RepCounter.cs`:

```csharp
        private readonly float _upThresholdDeg;
        private readonly float _downThresholdDeg;
        private readonly double _minRepSeconds;
        private readonly int _downDebounceFrames;

        private double _downEnterTime;
        private int _belowStreak;

        /// <param name="upThresholdDeg">Angle at/above which the movement counts as at the top.</param>
        /// <param name="downThresholdDeg">Angle at/below which the rep counts as deep (bottom).</param>
        /// <param name="minRepSeconds">Minimum time from reaching the bottom to returning to the top.</param>
        /// <param name="downDebounceFrames">Consecutive below-threshold updates required to enter the
        /// bottom phase. 1 = прежнее поведение; больше — защита от одиночных шумовых кадров
        /// (низкий fps без сглаживания). Провал трекинга рвёт серию через <see cref="ResetDownStreak"/>.</param>
        public RepCounter(float upThresholdDeg = 140f, float downThresholdDeg = 105f, double minRepSeconds = 0.3, int downDebounceFrames = 1)
        {
            _upThresholdDeg = upThresholdDeg;
            _downThresholdDeg = downThresholdDeg;
            _minRepSeconds = minRepSeconds;
            _downDebounceFrames = downDebounceFrames;
        }
```

`Update` (замена целиком тела):

```csharp
        public bool Update(float angleDeg, double timeSeconds)
        {
            if (angleDeg >= _upThresholdDeg)
            {
                bool longEnough = (timeSeconds - _downEnterTime) >= _minRepSeconds;
                bool completed = Phase == RepPhase.Down && longEnough;
                if (completed)
                    Reps++;
                Phase = RepPhase.Up;
                _belowStreak = 0;
                return completed;
            }

            if (angleDeg <= _downThresholdDeg)
            {
                _belowStreak++;
                if (Phase == RepPhase.Up && _belowStreak >= _downDebounceFrames)
                {
                    Phase = RepPhase.Down;
                    _downEnterTime = timeSeconds;
                }
            }
            else
            {
                _belowStreak = 0;
            }

            return false;
        }

        /// <summary>Рвёт серию «низких» кадров — вызывается на невалидных (невидимых) кадрах,
        /// чтобы серия дебаунса не переживала провалы трекинга.</summary>
        public void ResetDownStreak()
        {
            _belowStreak = 0;
        }
```

`Reset()` — добавить `_belowStreak = 0;`.

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0` (существующие RepCounter/Squat-тесты не тронуты — дефолт 1).

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/RepCounter.cs Assets/Pose/Tests/RepCounterTests.cs
git commit -m "feat: дебаунс низа в RepCounter — защита от одиночных шумовых кадров"
```

---

### Task 2: Щедрые дефолты пуш-апа + адаптация тестов

**Files:**
- Modify: `Assets/Pose/PushUpAnalyzer.cs` (ctor ~52-58; ветка невалидного кадра ~78-84; докстрока класса ~5-17)
- Modify: `Assets/Pose/PushUpFormEvaluator.cs` (ctor-дефолты)
- Test: `Assets/Pose/Tests/PushUpAnalyzerTests.cs`, `Assets/Pose/Tests/PushUpFormEvaluatorTests.cs`

**Interfaces:**
- Consumes: `RepCounter(..., downDebounceFrames)` и `ResetDownStreak()` из Task 1.
- Produces: `PushUpAnalyzer(RepCounter counter = null, PushUpFormEvaluator evaluator = null, float smoothingAlpha = 1f, float wristBelowHipMin = 0f)` — дефолтный счётчик `new RepCounter(downDebounceFrames: 2)`; `PushUpFormEvaluator(float minVisibility = 0.5f, float straightMinDeg = 160f, float positionMinDeg = 120f)`. Task 3 полагается на эти дефолты.

- [ ] **Step 1: Адаптировать тесты (красная фаза)**

`Assets/Pose/Tests/PushUpAnalyzerTests.cs`:

1. Хелпер `Rep` — четыре кадра (дебаунсу нужно два кадра низа):

```csharp
        // One rep at the given hip offset; the bottom is held two frames because the
        // default counter debounces the Down phase (2 consecutive below-threshold frames).
        private static void Rep(PushUpAnalyzer a, float hipOffset, ref double t)
        {
            a.ProcessFrame(PoseTestFrames.Build(170f, hipOffset, 1f, t)); t += 0.5;
            a.ProcessFrame(PoseTestFrames.Build(80f, hipOffset, 1f, t)); t += 0.5;
            a.ProcessFrame(PoseTestFrames.Build(80f, hipOffset, 1f, t)); t += 0.5;
            a.ProcessFrame(PoseTestFrames.Build(170f, hipOffset, 1f, t)); t += 0.5;
        }
```

2. `BentRep_CountsButTalliedAsNoRep` — тоже два кадра низа:

```csharp
            a.ProcessFrame(PoseTestFrames.Build(170f, 0f, 1f, 0.0));
            a.ProcessFrame(PoseTestFrames.Build(80f, 0.06f, 1f, 0.5));
            a.ProcessFrame(PoseTestFrames.Build(80f, 0.06f, 1f, 1.0));
            a.ProcessFrame(PoseTestFrames.Build(170f, 0f, 1f, 1.5));
```

3. `OutOfPosition_DoesNotCount` — `Rep(a, 0.12f, ref t)` → `Rep(a, 0.16f, ref t)` (при новом пороге 120° смещение 0.12 даёт угол 128.7° — уже NotStraight-планка; 0.16 даёт 114.8° — по-прежнему «не позиция»).
4. Докстроку класса поправить: «Smoothing is disabled (alpha = 1) so...» → «Smoothing is off by default; the bottom is held two frames to satisfy the counter's debounce.»

`Assets/Pose/Tests/PushUpFormEvaluatorTests.cs`:

5. `BentBody_IsNotAPushUpPosition` — `hipOffset: 0.12f` → `hipOffset: 0.16f` (та же геометрия: 0.12 теперь валидная согнутая планка, 0.16 — не позиция).

- [ ] **Step 2: Прогнать — переходный зелёный.** Run: команда тестов. Expected: `exit=0`. Это сознательно НЕ красная фаза: адаптированные значения (4-кадровый Rep, offset 0.16, два кадра низа) выбраны совместимыми и со старыми, и с новыми дефолтами, чтобы правки тестов и правки дефолтов были разделимы. Поведенческий «красный→зелёный» гейт этой работы — корпус-тесты Task 3 (5/4/0), которые без новых дефолтов дают 3/2/0. Если этот прогон НЕ зелёный — правки Step 1 сделаны с ошибкой, чинить их, не дефолты.

- [ ] **Step 3: Реализация**

`Assets/Pose/PushUpAnalyzer.cs`:

1. Конструктор:

```csharp
        public PushUpAnalyzer(RepCounter counter = null, PushUpFormEvaluator evaluator = null,
            float smoothingAlpha = 1f, float wristBelowHipMin = 0f)
        {
            // Дефолт: без сглаживания (α=1) + дебаунс низа. На реальном fps устройства (6–15
            // кадров/с с провалами) EMA не успевала довести угол до порога — повторы терялись;
            // от одиночных шумовых кадров вместо неё защищает дебаунс.
            _counter = counter ?? new RepCounter(downDebounceFrames: 2);
            _evaluator = evaluator ?? new PushUpFormEvaluator();
            _smoothingAlpha = smoothingAlpha;
            _wristBelowHipMin = wristBelowHipMin;
        }
```

2. В ветке невалидного кадра (`if (!assessment.PostureValid)`) перед `Changed?.Invoke()` добавить:

```csharp
                _counter.ResetDownStreak();
```

3. В докстроке класса заменить пункт про Smoothing: «**Debounce** — the bottom phase opens only after consecutive below-threshold frames (see <see cref="RepCounter"/>), so a single noisy frame while holding a plank cannot start a phantom rep; smoothing is off by default (α = 1).»

`Assets/Pose/PushUpFormEvaluator.cs` — дефолты конструктора:

```csharp
        public PushUpFormEvaluator(float minVisibility = 0.5f, float straightMinDeg = 160f, float positionMinDeg = 120f)
```

(и в `<param>`-докстроках значения не упоминаются — правок не требуют).

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`, включая нетронутые Squat/RepCounter-тесты.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/PushUpAnalyzer.cs Assets/Pose/PushUpFormEvaluator.cs Assets/Pose/Tests/PushUpAnalyzerTests.cs Assets/Pose/Tests/PushUpFormEvaluatorTests.cs
git commit -m "feat: щедрый зачёт пуш-апа — дебаунс вместо EMA, видимость 0.5, позиция 120°"
```

---

### Task 3: Корпус — третья запись и новые ожидания

**Files:**
- Create: `Assets/Pose/Tests/Recordings/pushups_with_plank_holds.csv` (копия `pose_rec_205917.csv`, путь в Global Constraints)
- Modify: `Assets/Pose/Tests/PushUpRecordingTests.cs`

**Interfaces:**
- Consumes: дефолтный `PushUpAnalyzer` из Task 2; `CsvPoseFrames.Load`.

- [ ] **Step 1: Скопировать запись и обновить тесты**

```powershell
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_205917.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\pushups_with_plank_holds.csv"
```

В `Assets/Pose/Tests/PushUpRecordingTests.cs` заменить оба тестовых метода и добавить третий (docstring класса дополнить перечнем: real_pushups — характеризация, pushups_with_plank_holds — граунд-трус пользователя, walking_noise — ноль):

```csharp
        [Test]
        public void RealRecording_CountsFour()
        {
            // 4 — характеризация текущей конфигурации, не граунд-трус: прежние «2» были
            // находкой старой (терявшей повторы) логики, а не правдой. Пользователь в той
            // сессии жаловался, что счёт не шёл вовсе.
            Assert.AreEqual(4, Replay("Pose/Tests/Recordings/real_pushups.csv"));
        }

        [Test]
        public void PlankHoldsRecording_CountsFive()
        {
            // Граунд-трус пользователя: 5–6 отжиманий с ошибками формы + удержания планки.
            Assert.AreEqual(5, Replay("Pose/Tests/Recordings/pushups_with_plank_holds.csv"));
        }

        [Test]
        public void WalkingRecording_CountsNothing()
        {
            Assert.AreEqual(0, Replay("Pose/Tests/Recordings/walking_noise.csv"));
        }
```

- [ ] **Step 2: Прогнать**

Run: команда тестов. Expected: `exit=0` — корпус 5/4/0. Любое другое число = BLOCKED (Global Constraints), без подгонок.

- [ ] **Step 3: Commit**

```powershell
git add Assets/Pose/Tests/Recordings/pushups_with_plank_holds.csv* Assets/Pose/Tests/PushUpRecordingTests.cs
git commit -m "test: корпус пуш-апа 5/4/0 — граунд-трус с планками, характеризация старой записи"
```

---

### Task 4: Пересборка и установка

**Files:** без изменений кода.

- [ ] **Step 1: Сборка** (Editor закрыт; unity build сам ждёт до конца)

```powershell
unity build "C:\Users\user\Mikey" --target Android --execute-method Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid --no-banner; "exit=$LASTEXITCODE"
```

Expected: `exit=0`, mtime `Builds/ExerciseSandbox.apk` свежее старта сборки (несвежий = не устанавливать, эскалировать).

- [ ] **Step 2: Установка**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\user\Mikey\Builds\ExerciseSandbox.apk"
```

Expected: `Success`.

- [ ] **Step 3: Пользовательская проверка** — 5–6 честных отжиманий → счёт 5–6; удержание планки — счёт стоит; ходьба — стоит.
