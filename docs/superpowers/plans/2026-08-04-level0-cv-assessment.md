# Уровень 0: CV-оценка техник и стартовые статы — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Шесть новых упражнений (приседания, wall-sit, медленный yoko-geri, mae geri gedan/chudan/jodan) поверх существующего pose-пайплайна + расчёт стартовых статов Сила/Выносливость/Гибкость/Баланс.

**Architecture:** Каждая техника — малый чистый C#-класс, реализующий существующий `IExerciseAnalyzer`, зарегистрированный в `ExerciseCatalog` (mae geri — один класс, три записи). Общие примитивы: `RepCounter` (переименованный `PushUpRepCounter`), `HoldTimer` (удержание с грейс-периодом), `LegLiftCycle` (цикл подъёма ноги — примитив, выделившийся при детализации: махи не ложатся на семантику `RepCounter`), `KickHeightZone` (классификатор высоты). Скоринг мягкий, как у пуш-апа: полноамплитудный повтор засчитывается, огрех формы → `NoReps`.

**Tech Stack:** Unity 6000.3.18f1, C# (asmdef `Mikey.Pose`), NUnit EditMode-тесты (asmdef `Mikey.Pose.Tests`), PlayerPrefs + JsonUtility для хранения результатов.

**Спека:** `docs/superpowers/specs/2026-08-04-level0-cv-assessment-design.md`

## Global Constraints

- **Запуск EditMode-тестов** (Unity Editor с этим проектом должен быть ЗАКРЫТ, иначе batchmode не получит лок; выход 0 = все прошли, 2 = есть упавшие):

  ```powershell
  & "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\user\Mikey" -runTests -testPlatform EditMode -testFilter "Mikey.Pose.Tests" -testResults "C:\Users\user\Mikey\Temp\pose_tests.xml" -logFile "C:\Users\user\Mikey\Temp\pose_tests.log" | Out-Null; "exit=$LASTEXITCODE"
  ```

  Ниже по тексту это «команда тестов». При падениях смотреть `Temp/pose_tests.xml` (атрибуты `result="Failed"`).
- **.meta-файлы:** новые `.cs` получают `.meta` во время первого прогона тестов (Unity их генерирует). После прогона добавлять их в коммит (`git add Assets/Pose/...meta`). При переименовании файлов переносить `.meta` через `git mv` — иначе Unity потеряет GUID.
- Вся логика анализаторов — детерминированный C# без UnityEngine (кроме `Level0Results`, которому нужны PlayerPrefs/JsonUtility).
- Русские строки-подсказки (cues) — файлы в UTF-8, как существующие.
- Координаты image-space: Y растёт ВНИЗ (меньший Y = выше в кадре).
- Пороги — параметры конструктора с дефолтами (стартовые значения из спеки); никаких конфиг-файлов.
- **Заделы под Strict-режим** (спека, раздел «Профили Lenient/Strict»): пороги уже параметры конструкторов — будущий строгий режим просто передаст свои. Флаг «форма блокирует счёт» сейчас НЕ добавляется (мёртвый код без потребителя); его введёт спека точного режима — переделки анализаторов это не потребует.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Переименование PushUpRepCounter → RepCounter

Счётчик уже чистый гистерезис по скалярному сигналу — в нём нет ничего пуш-ап-специфичного. Переименовываем, чтобы приседания использовали его без вранья в имени. Поведение не меняется, существующие тесты должны пройти без правок логики.

**Files:**
- Rename: `Assets/Pose/PushUpRepCounter.cs` → `Assets/Pose/RepCounter.cs` (+ `.meta`)
- Rename: `Assets/Pose/Tests/PushUpRepCounterTests.cs` → `Assets/Pose/Tests/RepCounterTests.cs` (+ `.meta`)
- Modify: `Assets/Pose/PushUpAnalyzer.cs:7,20,52,54`

**Interfaces:**
- Produces: `public sealed class RepCounter` — прежний API: `RepCounter(float upThresholdDeg = 140f, float downThresholdDeg = 105f, double minRepSeconds = 0.3)`, `bool Update(float angleDeg, double timeSeconds)` (true ровно на кадре завершения повтора), `int Reps`, `RepPhase Phase`, `void Reset()`. `RepPhase { Unknown, Up, Down }` остаётся в том же файле.

- [ ] **Step 1: git mv файлов и их .meta**

```powershell
git mv Assets/Pose/PushUpRepCounter.cs Assets/Pose/RepCounter.cs
git mv Assets/Pose/PushUpRepCounter.cs.meta Assets/Pose/RepCounter.cs.meta
git mv Assets/Pose/Tests/PushUpRepCounterTests.cs Assets/Pose/Tests/RepCounterTests.cs
git mv Assets/Pose/Tests/PushUpRepCounterTests.cs.meta Assets/Pose/Tests/RepCounterTests.cs.meta
```

- [ ] **Step 2: Переименовать класс и обобщить докстроки**

В `Assets/Pose/RepCounter.cs`: `class PushUpRepCounter` → `class RepCounter`, конструктор — так же. Параметр `Update(float elbowAngleDeg, ...)` → `Update(float angleDeg, ...)` (оба потребителя — угол в градусах, где большой угол = верх/отдых). Шапку класса заменить на:

```csharp
/// <summary>
/// Pure motion detector: counts one full-range rep from a smoothed scalar signal
/// (a joint angle in degrees where large = top/rest, small = bottom), using two
/// thresholds for hysteresis. A rep is the transition Down→Up, but only if the
/// descent-to-ascent took at least <c>minRepSeconds</c> — this rejects the sub-frame
/// threshold flicker that noisy landmarks produce.
///
/// Used by push-ups (elbow angle) and squats (knee angle). Detects movement only;
/// visibility/posture gating is the caller's job.
/// </summary>
```

Докстроку `RepPhase` поправить: «The two ends of a rep cycle (top/bottom), plus the initial unknown state.»

- [ ] **Step 3: Обновить ссылки**

`Assets/Pose/PushUpAnalyzer.cs`: строки 7 (`<see cref="PushUpRepCounter"/>` → `RepCounter`), 20, 52, 54 — тип поля, параметра и `new`. `Assets/Pose/Tests/RepCounterTests.cs`: класс `PushUpRepCounterTests` → `RepCounterTests`, все `PushUpRepCounter` → `RepCounter` (9 мест). Проверить, что упоминаний не осталось:

```powershell
git grep -n "PushUpRepCounter" -- Assets
```

Expected: пусто.

- [ ] **Step 4: Прогнать тесты**

Run: команда тестов. Expected: `exit=0` (все существующие тесты зелёные без правок логики).

- [ ] **Step 5: Commit**

```powershell
git add -A Assets/Pose
git commit -m "refactor: PushUpRepCounter -> RepCounter — гистерезисный счётчик общий для пуш-апа и приседаний"
```

---

### Task 2: HoldTimer — удержание позы с грейс-периодом

**Files:**
- Create: `Assets/Pose/HoldTimer.cs`
- Test: `Assets/Pose/Tests/HoldTimerTests.cs`

**Interfaces:**
- Produces: `public sealed class HoldTimer` — `HoldTimer(double graceSeconds = 1.0)`, `void Update(bool inPose, double timeSeconds)`, `double CurrentSeconds` (текущее непрерывное удержание; разрывы ≤ grace прозрачно «сшиваются», время разрыва входит в счёт), `double BestSeconds` (лучшее непрерывное за сессию), `void Reset()`. Используется wall-sit (Task 5).

- [ ] **Step 1: Написать падающие тесты**

`Assets/Pose/Tests/HoldTimerTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class HoldTimerTests
    {
        // Кадры в позе идут не реже грейса (в реале это 15–30 fps): пауза между
        // true-кадрами длиннее грейса означает потерю данных и рвёт удержание.

        private static void Hold(HoldTimer t, double from, double to)
        {
            for (double s = from; s <= to + 1e-9; s += 0.5)
                t.Update(true, s);
        }

        [Test]
        public void AccumulatesWhileInPose()
        {
            var t = new HoldTimer();
            Hold(t, 0.0, 2.5);
            Assert.AreEqual(2.5, t.CurrentSeconds, 1e-6);
            Assert.AreEqual(2.5, t.BestSeconds, 1e-6);
        }

        [Test]
        public void ShortGapWithinGraceDoesNotBreakTheHold()
        {
            var t = new HoldTimer(graceSeconds: 1.0);
            Hold(t, 0.0, 2.0);
            t.Update(false, 2.4);          // моргнул трекер
            Hold(t, 2.8, 4.0);             // разрыв 0.8 с ≤ grace — удержание продолжается
            Assert.AreEqual(4.0, t.CurrentSeconds, 1e-6);
        }

        [Test]
        public void LongGapBreaksTheHoldButKeepsBest()
        {
            var t = new HoldTimer(graceSeconds: 1.0);
            Hold(t, 0.0, 3.0);
            t.Update(false, 5.0);          // разрыв больше grace
            Assert.AreEqual(0.0, t.CurrentSeconds, 1e-6);
            Assert.AreEqual(3.0, t.BestSeconds, 1e-6);
            Hold(t, 6.0, 7.5);             // новое удержание с нуля
            Assert.AreEqual(1.5, t.CurrentSeconds, 1e-6);
            Assert.AreEqual(3.0, t.BestSeconds, 1e-6);
        }

        [Test]
        public void SparseInPoseFramesBeyondGraceStartFresh()
        {
            var t = new HoldTimer(graceSeconds: 1.0);
            Hold(t, 0.0, 2.0);
            // ни одного Update(false,·): просто длинная пауза между true-кадрами
            t.Update(true, 10.0);
            Assert.AreEqual(0.0, t.CurrentSeconds, 1e-6);
            t.Update(true, 11.0);
            Assert.AreEqual(1.0, t.CurrentSeconds, 1e-6);
        }

        [Test]
        public void ResetClearsEverything()
        {
            var t = new HoldTimer();
            t.Update(true, 0.0);
            t.Update(true, 5.0);
            t.Reset();
            Assert.AreEqual(0.0, t.CurrentSeconds, 1e-6);
            Assert.AreEqual(0.0, t.BestSeconds, 1e-6);
        }
    }
}
```

- [ ] **Step 2: Прогнать — убедиться, что падают**

Run: команда тестов. Expected: `exit=2`, в `Temp/pose_tests.log` — ошибка компиляции `HoldTimer` not found (это и есть «красный»: файла ещё нет).

- [ ] **Step 3: Реализация**

`Assets/Pose/HoldTimer.cs`:

```csharp
namespace Mikey.Pose
{
    /// <summary>
    /// Accumulates how long a pose predicate stays true. Brief dropouts (tracker blink,
    /// occlusion) up to <c>graceSeconds</c> are bridged transparently — the hold continues
    /// and the gap itself counts into the time. Longer gaps break the hold: the current
    /// time resets, the best time is kept. Engine-free and EditMode-testable.
    /// </summary>
    public sealed class HoldTimer
    {
        private readonly double _graceSeconds;
        private double _holdStart = double.NaN;
        private double _lastInPose = double.NaN;

        /// <summary>Continuous hold so far, seconds (grace-bridged gaps included).</summary>
        public double CurrentSeconds { get; private set; }

        /// <summary>Longest continuous hold this session, seconds.</summary>
        public double BestSeconds { get; private set; }

        public HoldTimer(double graceSeconds = 1.0) => _graceSeconds = graceSeconds;

        public void Update(bool inPose, double timeSeconds)
        {
            if (inPose)
            {
                bool broken = double.IsNaN(_lastInPose) || timeSeconds - _lastInPose > _graceSeconds;
                if (broken)
                {
                    _holdStart = timeSeconds;
                    CurrentSeconds = 0;
                }
                _lastInPose = timeSeconds;
                CurrentSeconds = timeSeconds - _holdStart;
                if (CurrentSeconds > BestSeconds)
                    BestSeconds = CurrentSeconds;
            }
            else if (!double.IsNaN(_lastInPose) && timeSeconds - _lastInPose > _graceSeconds)
            {
                CurrentSeconds = 0;
                _lastInPose = double.NaN;
            }
        }

        public void Reset()
        {
            _holdStart = double.NaN;
            _lastInPose = double.NaN;
            CurrentSeconds = 0;
            BestSeconds = 0;
        }
    }
}
```

- [ ] **Step 4: Прогнать — зелёные**

Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/HoldTimer.cs Assets/Pose/HoldTimer.cs.meta Assets/Pose/Tests/HoldTimerTests.cs Assets/Pose/Tests/HoldTimerTests.cs.meta
git commit -m "feat: HoldTimer — удержание позы с грейс-периодом для wall-sit"
```

---

### Task 3: LegLiftCycle и KickHeightZone — примитивы махов

**Files:**
- Create: `Assets/Pose/LegLiftCycle.cs`
- Create: `Assets/Pose/KickHeightZone.cs`
- Test: `Assets/Pose/Tests/LegLiftCycleTests.cs`
- Test: `Assets/Pose/Tests/KickHeightZoneTests.cs`

**Interfaces:**
- Produces:
  - `public enum LiftPhase { Grounded, Lifted }`
  - `public sealed class LegLiftCycle` — `LegLiftCycle(float liftedAt = 1.0f, float groundedAt = 0.25f, double minLiftSeconds = 0.2)`, `bool Update(float lift01, double timeSeconds)` (true ровно на кадре возврата ноги на пол после достаточно долгого подъёма), `LiftPhase Phase`, `double LiftedSeconds` (длительность текущего подъёма; после завершения — последнего), `void Reset()`. Сигнал `lift01`: 0 = стопа на полу, 1 = на высоте колена опорной ноги (считает вызывающий).
  - `public enum KickZone { None = 0, Gedan = 1, Chudan = 2, Jodan = 3 }`
  - `public static class KickHeightZone` — `static KickZone Classify(float ankleY, float hipY, float shoulderY)` (image-space Y того же кадра; вызывать только для уже поднятой ноги).
- Используется mae geri (Task 6) и yoko-geri (Task 7).

- [ ] **Step 1: Написать падающие тесты**

`Assets/Pose/Tests/KickHeightZoneTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class KickHeightZoneTests
    {
        // Y растёт вниз: hip 0.5, shoulder 0.2.
        [TestCase(0.65f, KickZone.Gedan)]   // ниже бедра
        [TestCase(0.35f, KickZone.Chudan)]  // между бедром и плечом
        [TestCase(0.50f, KickZone.Chudan)]  // ровно на бедре — уже chudan
        [TestCase(0.20f, KickZone.Jodan)]   // ровно на плече — jodan
        [TestCase(0.10f, KickZone.Jodan)]   // выше плеча
        public void ClassifiesByHeight(float ankleY, KickZone expected)
        {
            Assert.AreEqual(expected, KickHeightZone.Classify(ankleY, hipY: 0.5f, shoulderY: 0.2f));
        }
    }
}
```

`Assets/Pose/Tests/LegLiftCycleTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class LegLiftCycleTests
    {
        [Test]
        public void CompletesCycleOnReturnToGround()
        {
            var c = new LegLiftCycle();
            Assert.IsFalse(c.Update(0.0f, 0.0));
            Assert.IsFalse(c.Update(1.2f, 0.5));           // поднялась
            Assert.AreEqual(LiftPhase.Lifted, c.Phase);
            Assert.IsFalse(c.Update(1.5f, 1.0));
            Assert.IsTrue(c.Update(0.1f, 1.5));            // вернулась — цикл завершён
            Assert.AreEqual(LiftPhase.Grounded, c.Phase);
            Assert.AreEqual(1.0, c.LiftedSeconds, 1e-6);   // подъём длился с 0.5 до 1.5
        }

        [Test]
        public void LiftedSecondsMeasuresFromLiftStart()
        {
            var c = new LegLiftCycle();
            c.Update(0.0f, 0.0);
            c.Update(1.2f, 1.0);                            // старт подъёма
            c.Update(1.4f, 2.0);
            Assert.AreEqual(1.0, c.LiftedSeconds, 1e-6);
            c.Update(0.1f, 3.5);
            Assert.AreEqual(2.5, c.LiftedSeconds, 1e-6);    // длительность завершённого
        }

        [Test]
        public void TooShortLiftDoesNotComplete()
        {
            var c = new LegLiftCycle(minLiftSeconds: 0.2);
            c.Update(0.0f, 0.0);
            c.Update(1.2f, 0.05);
            Assert.IsFalse(c.Update(0.1f, 0.1));            // дрожание, не мах
        }

        [Test]
        public void HysteresisHoldsPhaseBetweenThresholds()
        {
            var c = new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f);
            c.Update(0.0f, 0.0);
            c.Update(0.7f, 0.5);                            // между порогами — всё ещё на полу
            Assert.AreEqual(LiftPhase.Grounded, c.Phase);
            c.Update(1.1f, 1.0);
            c.Update(0.7f, 1.5);                            // между порогами — всё ещё поднята
            Assert.AreEqual(LiftPhase.Lifted, c.Phase);
        }

        [Test]
        public void ResetReturnsToGrounded()
        {
            var c = new LegLiftCycle();
            c.Update(0.0f, 0.0);
            c.Update(1.2f, 1.0);
            c.Reset();
            Assert.AreEqual(LiftPhase.Grounded, c.Phase);
            Assert.AreEqual(0.0, c.LiftedSeconds, 1e-6);
        }
    }
}
```

- [ ] **Step 2: Прогнать — падают** (ошибка компиляции: типы не существуют). Run: команда тестов. Expected: `exit=2`.

- [ ] **Step 3: Реализация**

`Assets/Pose/KickHeightZone.cs`:

```csharp
namespace Mikey.Pose
{
    /// <summary>Karate height levels a kick can reach, ordered so bigger = higher.</summary>
    public enum KickZone
    {
        None = 0,
        Gedan = 1,
        Chudan = 2,
        Jodan = 3,
    }

    /// <summary>
    /// Classifies the kicking ankle's height against the same frame's hip and shoulder
    /// (image-space Y, down-positive) — robust to the player moving in frame and needs
    /// no per-height calibration. The caller decides whether the leg is lifted at all;
    /// a lifted ankle below the hip is Gedan.
    /// </summary>
    public static class KickHeightZone
    {
        public static KickZone Classify(float ankleY, float hipY, float shoulderY) =>
            ankleY <= shoulderY ? KickZone.Jodan
            : ankleY <= hipY ? KickZone.Chudan
            : KickZone.Gedan;
    }
}
```

`Assets/Pose/LegLiftCycle.cs`:

```csharp
namespace Mikey.Pose
{
    /// <summary>Where the kicking foot is in the lift cycle.</summary>
    public enum LiftPhase
    {
        Grounded,
        Lifted,
    }

    /// <summary>
    /// Pure detector of one leg-lift cycle (kick, slow raise): foot leaves the floor,
    /// peaks, returns. Feeds on a normalized lift signal (0 = foot at floor level,
    /// 1 = at the support knee's height) with two thresholds for hysteresis; a cycle
    /// shorter than <c>minLiftSeconds</c> is treated as landmark jitter and dropped.
    /// The caller samples what it needs (peak zone, knee bend) while Phase is Lifted.
    /// Engine-free and EditMode-testable.
    /// </summary>
    public sealed class LegLiftCycle
    {
        private readonly float _liftedAt;
        private readonly float _groundedAt;
        private readonly double _minLiftSeconds;
        private double _liftStart;

        public LiftPhase Phase { get; private set; } = LiftPhase.Grounded;

        /// <summary>Duration of the current lift while Lifted; of the last completed one after.</summary>
        public double LiftedSeconds { get; private set; }

        public LegLiftCycle(float liftedAt = 1.0f, float groundedAt = 0.25f, double minLiftSeconds = 0.2)
        {
            _liftedAt = liftedAt;
            _groundedAt = groundedAt;
            _minLiftSeconds = minLiftSeconds;
        }

        /// <summary>Returns true exactly on the frame a long-enough lift returns to the ground.</summary>
        public bool Update(float lift01, double timeSeconds)
        {
            if (Phase == LiftPhase.Grounded)
            {
                if (lift01 >= _liftedAt)
                {
                    Phase = LiftPhase.Lifted;
                    _liftStart = timeSeconds;
                    LiftedSeconds = 0;
                }
                return false;
            }

            LiftedSeconds = timeSeconds - _liftStart;
            if (lift01 > _groundedAt)
                return false;

            Phase = LiftPhase.Grounded;
            return LiftedSeconds >= _minLiftSeconds;
        }

        public void Reset()
        {
            Phase = LiftPhase.Grounded;
            LiftedSeconds = 0;
            _liftStart = 0;
        }
    }
}
```

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/LegLiftCycle.cs* Assets/Pose/KickHeightZone.cs* Assets/Pose/Tests/LegLiftCycleTests.cs* Assets/Pose/Tests/KickHeightZoneTests.cs*
git commit -m "feat: LegLiftCycle и KickHeightZone — примитивы цикла маха и высотных зон"
```

---

### Task 4: SquatAnalyzer + синтетические кадры ног

**Files:**
- Create: `Assets/Pose/SquatAnalyzer.cs`
- Create: `Assets/Pose/Tests/LegTestFrames.cs`
- Test: `Assets/Pose/Tests/SquatAnalyzerTests.cs`
- Modify: `Assets/Pose/ExerciseCatalog.cs:28-33`

**Interfaces:**
- Consumes: `RepCounter` (Task 1), `IExerciseAnalyzer`, `PoseFrame`, `PoseMath.AngleDeg3D`.
- Produces:
  - `public sealed class SquatAnalyzer : IExerciseAnalyzer` — `SquatAnalyzer(RepCounter counter = null, float minVisibility = 0.6f, float maxTorsoLeanDeg = 50f, float smoothingAlpha = 0.6f)`; дефолтный counter = `new RepCounter(upThresholdDeg: 160f, downThresholdDeg: 100f, minRepSeconds: 0.3)`. `Id == "squat"`.
  - `internal static class LegTestFrames` (в Tests) — `PoseFrame Squat(float kneeAngleDeg, float torsoLeanDeg = 0f, float visibility = 1f, double timestamp = 0)`; в Task 5/6 добавятся `WallSit(...)` и `Kick(...)`.

- [ ] **Step 1: Написать билдер кадров и падающие тесты**

`Assets/Pose/Tests/LegTestFrames.cs`:

```csharp
using System;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Synthetic side-on lower-body frames for squat/wall-sit/kick scoring tests.
    /// Same idea as <see cref="PoseTestFrames"/> (which stays push-up-specific):
    /// place landmarks so a requested joint angle or ankle height comes out exactly.
    /// Both body sides get identical coordinates so side-selection ties resolve left.
    /// </summary>
    internal static class LegTestFrames
    {
        private const double Deg2Rad = Math.PI / 180.0;

        /// <summary>
        /// Standing/squatting figure: vertical shank (ankle→knee), thigh rotated to
        /// realize <paramref name="kneeAngleDeg"/>, torso tilted from vertical by
        /// <paramref name="torsoLeanDeg"/>.
        /// </summary>
        public static PoseFrame Squat(float kneeAngleDeg, float torsoLeanDeg = 0f, float visibility = 1f, double timestamp = 0)
        {
            var lm = Blank(visibility);

            float ax = 0.5f, ay = 0.9f;                     // ankle
            float kx = 0.5f, ky = 0.7f;                     // knee (shank vertical)
            double phi = (180.0 - kneeAngleDeg) * Deg2Rad;  // 0 = thigh straight up
            float hx = kx + 0.25f * (float)Math.Sin(phi);
            float hy = ky - 0.25f * (float)Math.Cos(phi);
            double lean = torsoLeanDeg * Deg2Rad;
            float sx = hx + 0.3f * (float)Math.Sin(lean);
            float sy = hy - 0.3f * (float)Math.Cos(lean);

            SetBoth(lm, PoseLandmarkType.LeftAnkle, PoseLandmarkType.RightAnkle, ax, ay, visibility);
            SetBoth(lm, PoseLandmarkType.LeftKnee, PoseLandmarkType.RightKnee, kx, ky, visibility);
            SetBoth(lm, PoseLandmarkType.LeftHip, PoseLandmarkType.RightHip, hx, hy, visibility);
            SetBoth(lm, PoseLandmarkType.LeftShoulder, PoseLandmarkType.RightShoulder, sx, sy, visibility);
            return new PoseFrame(lm, timestamp);
        }

        internal static PoseLandmark[] Blank(float visibility)
        {
            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < lm.Length; i++)
                lm[i] = new PoseLandmark(0f, 0f, 0f, visibility);
            return lm;
        }

        internal static void SetBoth(PoseLandmark[] lm, PoseLandmarkType left, PoseLandmarkType right, float x, float y, float vis)
        {
            lm[(int)left] = new PoseLandmark(x, y, 0f, vis);
            lm[(int)right] = new PoseLandmark(x, y, 0f, vis);
        }
    }
}
```

`Assets/Pose/Tests/SquatAnalyzerTests.cs` (везде `smoothingAlpha: 1f`, чтобы тесты не зависели от EMA-инерции):

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class SquatAnalyzerTests
    {
        private static SquatAnalyzer NewAnalyzer() => new SquatAnalyzer(smoothingAlpha: 1f);

        private static void Feed(SquatAnalyzer a, float kneeAngleDeg, double t, float lean = 0f, float vis = 1f)
            => a.ProcessFrame(LegTestFrames.Squat(kneeAngleDeg, lean, vis, t));

        [Test]
        public void CountsCleanRep()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 1.0);     // глубокий сед
            Feed(a, 175f, 2.0);    // встал
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void ShallowSquatDoesNotCount()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 120f, 1.0);    // недосед
            Feed(a, 175f, 2.0);
            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void ThresholdJitterDoesNotProducePhantomReps()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 0.05);    // «повтор» за 0.1 c — дрожание сигнала, не движение
            Feed(a, 175f, 0.10);
            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void TorsoLeanAtBottomIsTalliedButStillCounts()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 1.0, lean: 60f);   // сед с сильным завалом корпуса
            Feed(a, 175f, 2.0);
            Assert.AreEqual(1, a.Reps);      // мягкий скоринг: повтор идёт
            Assert.AreEqual(1, a.NoReps);    // но огрех зафиксирован
        }

        [Test]
        public void LowVisibilityPausesCountingAndReportsNotVisible()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 1.0, vis: 0.3f);   // трекинг потерян в нижней точке
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Feed(a, 175f, 2.0);
            Assert.AreEqual(0, a.Reps);      // низ не был увиден достоверно
        }

        [Test]
        public void ResetClearsSet()
        {
            var a = NewAnalyzer();
            Feed(a, 175f, 0.0);
            Feed(a, 95f, 1.0);
            Feed(a, 175f, 2.0);
            a.Reset();
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void RegisteredInCatalog()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("squat"));
        }
    }
}
```

- [ ] **Step 2: Прогнать — падают** (нет `SquatAnalyzer`). Run: команда тестов. Expected: `exit=2`.

- [ ] **Step 3: Реализация**

`Assets/Pose/SquatAnalyzer.cs`:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores squats from a side-on view: the smoothed 3D knee angle drives the shared
    /// <see cref="RepCounter"/> (stand ≥ 160° → depth ≤ 100° → stand = one rep). Lenient
    /// policy, mirroring push-ups: every full-range rep counts; a heavy torso lean at the
    /// bottom is tallied in <see cref="NoReps"/> but does not block the count. A shallow
    /// squat simply never completes the counter's cycle. Engine-free.
    /// </summary>
    public sealed class SquatAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private readonly RepCounter _counter;
        private readonly float _minVisibility;
        private readonly float _maxTorsoLeanDeg;
        private readonly float _smoothingAlpha;

        private float _smoothedKnee = float.NaN;
        private float _lastLean = float.NaN;
        private float _lastVis;
        private bool _leanFaultThisRep;

        public string Id => "squat";
        public string DisplayName => "Squats";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public string DebugInfo =>
            $"knee {(float.IsNaN(_smoothedKnee) ? "--" : _smoothedKnee.ToString("0"))}°  " +
            $"lean {(float.IsNaN(_lastLean) ? "--" : _lastLean.ToString("0"))}°  " +
            $"phase {_counter.Phase}  vis {_lastVis:0.00}";

        public event Action Changed;

        public SquatAnalyzer(RepCounter counter = null, float minVisibility = 0.6f,
            float maxTorsoLeanDeg = 50f, float smoothingAlpha = 0.6f)
        {
            _counter = counter ?? new RepCounter(upThresholdDeg: 160f, downThresholdDeg: 100f, minRepSeconds: 0.3);
            _minVisibility = minVisibility;
            _maxTorsoLeanDeg = maxTorsoLeanDeg;
            _smoothingAlpha = smoothingAlpha;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = Math.Min(
                frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle),
                frame.Get(PoseLandmarkType.LeftShoulder).Visibility);
            float rightVis = Math.Min(
                frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle),
                frame.Get(PoseLandmarkType.RightShoulder).Visibility);
            bool useLeft = leftVis >= rightVis;
            _lastVis = useLeft ? leftVis : rightVis;

            if (_lastVis < _minVisibility)
            {
                // Drop the smoothing baseline so a resumed set starts clean.
                _smoothedKnee = float.NaN;
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            PoseLandmark hip = frame.Get(useLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark knee = frame.Get(useLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
            PoseLandmark ankle = frame.Get(useLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            PoseLandmark shoulder = frame.Get(useLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);

            float kneeAngle = PoseMath.AngleDeg3D(hip, knee, ankle);
            _smoothedKnee = float.IsNaN(_smoothedKnee)
                ? kneeAngle
                : _smoothedKnee + _smoothingAlpha * (kneeAngle - _smoothedKnee);

            // Torso lean from vertical, degrees (image-space; shoulder is above the hip).
            _lastLean = (float)(Math.Atan2(Math.Abs(shoulder.X - hip.X), Math.Max(1e-6f, hip.Y - shoulder.Y)) * 180.0 / Math.PI);

            RepPhase prevPhase = _counter.Phase;
            bool completed = _counter.Update(_smoothedKnee, frame.TimestampSeconds);

            if (prevPhase != RepPhase.Down && _counter.Phase == RepPhase.Down)
                _leanFaultThisRep = false;
            bool leanFault = _counter.Phase == RepPhase.Down && _lastLean > _maxTorsoLeanDeg;
            if (leanFault)
                _leanFaultThisRep = true;

            FormState = leanFault ? ExerciseFormState.BadForm : ExerciseFormState.GoodForm;
            Cue = leanFault ? "Спину прямее" : string.Empty;

            if (completed)
            {
                Reps++;
                if (_leanFaultThisRep)
                    NoReps++;
                _leanFaultThisRep = false;
            }

            Changed?.Invoke();
        }

        public void Reset()
        {
            _counter.Reset();
            Reps = 0;
            NoReps = 0;
            _smoothedKnee = float.NaN;
            _lastLean = float.NaN;
            _lastVis = 0f;
            _leanFaultThisRep = false;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
```

В `Assets/Pose/ExerciseCatalog.cs` добавить после записи pushup:

```csharp
new ExerciseDescriptor("squat", "Squats", () => new SquatAnalyzer()),
```

(комментарий-пример `// new ExerciseDescriptor("squat", ...)` удалить — он сбылся).

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/SquatAnalyzer.cs* Assets/Pose/Tests/LegTestFrames.cs* Assets/Pose/Tests/SquatAnalyzerTests.cs* Assets/Pose/ExerciseCatalog.cs
git commit -m "feat: SquatAnalyzer — приседания на общем RepCounter, мягкий скоринг"
```

---

### Task 5: WallSitAnalyzer

**Files:**
- Create: `Assets/Pose/WallSitAnalyzer.cs`
- Modify: `Assets/Pose/Tests/LegTestFrames.cs` (добавить `WallSit`)
- Test: `Assets/Pose/Tests/WallSitAnalyzerTests.cs`
- Modify: `Assets/Pose/ExerciseCatalog.cs`

**Interfaces:**
- Consumes: `HoldTimer` (Task 2), `LegTestFrames.Blank/SetBoth` (Task 4).
- Produces: `public sealed class WallSitAnalyzer : IExerciseAnalyzer` — `WallSitAnalyzer(HoldTimer timer = null, float minVisibility = 0.6f, float minAngleDeg = 70f, float maxAngleDeg = 120f)`; `Id == "wallsit"`; `int Reps => (int)BestHoldSeconds`; `double BestHoldSeconds`, `double CurrentHoldSeconds` — их читает `Level0Results.Absorb` (Task 8).

- [ ] **Step 1: Добавить билдер и падающие тесты**

В `Assets/Pose/Tests/LegTestFrames.cs` добавить метод:

```csharp
        /// <summary>
        /// Wall-sit figure: vertical shank, thigh rotated to realize the knee angle,
        /// torso rotated about the hip to realize the hip angle (90/90 = ideal seat).
        /// </summary>
        public static PoseFrame WallSit(float kneeAngleDeg = 90f, float hipAngleDeg = 90f, float visibility = 1f, double timestamp = 0)
        {
            var lm = Blank(visibility);

            float ax = 0.6f, ay = 0.9f;
            float kx = 0.6f, ky = 0.7f;
            double phi = (180.0 - kneeAngleDeg) * Deg2Rad;
            float hx = kx - 0.25f * (float)Math.Sin(phi);   // бедро уходит назад (влево)
            float hy = ky - 0.25f * (float)Math.Cos(phi);

            // Торс: повернуть направление бедро→колено на hipAngle, чтобы интериорный
            // угол в бедре (плечо–бедро–колено) вышел ровно заданным.
            float thigh = (float)Math.Sqrt((kx - hx) * (kx - hx) + (ky - hy) * (ky - hy));
            float tx = (kx - hx) / thigh, ty = (ky - hy) / thigh;
            double a = hipAngleDeg * Deg2Rad;
            float ux = tx * (float)Math.Cos(a) + ty * (float)Math.Sin(a);
            float uy = -tx * (float)Math.Sin(a) + ty * (float)Math.Cos(a);
            float sx = hx + 0.3f * ux, sy = hy + 0.3f * uy;

            SetBoth(lm, PoseLandmarkType.LeftAnkle, PoseLandmarkType.RightAnkle, ax, ay, visibility);
            SetBoth(lm, PoseLandmarkType.LeftKnee, PoseLandmarkType.RightKnee, kx, ky, visibility);
            SetBoth(lm, PoseLandmarkType.LeftHip, PoseLandmarkType.RightHip, hx, hy, visibility);
            SetBoth(lm, PoseLandmarkType.LeftShoulder, PoseLandmarkType.RightShoulder, sx, sy, visibility);
            return new PoseFrame(lm, timestamp);
        }
```

`Assets/Pose/Tests/WallSitAnalyzerTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class WallSitAnalyzerTests
    {
        // Кадры в позе — раз в 0.5 c: паузы длиннее грейса HoldTimer рвут удержание.
        private static void Sit(WallSitAnalyzer a, double from, double to)
        {
            for (double t = from; t <= to + 1e-9; t += 0.5)
                a.ProcessFrame(LegTestFrames.WallSit(timestamp: t));
        }

        [Test]
        public void AccumulatesSecondsWhileSeated()
        {
            var a = new WallSitAnalyzer();
            Sit(a, 0.0, 5.0);
            Assert.AreEqual(5, a.Reps);
            Assert.AreEqual(5.0, a.BestHoldSeconds, 1e-6);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void TooHighSeatGivesLowerCueAndStopsTimer()
        {
            var a = new WallSitAnalyzer();
            Sit(a, 0.0, 3.0);
            a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg: 150f, timestamp: 3.5));   // встал слишком высоко
            Assert.AreEqual(ExerciseFormState.BadForm, a.FormState);
            Assert.AreEqual("Ниже", a.Cue);
            a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg: 150f, timestamp: 10.0));
            Assert.AreEqual(3, a.Reps);                     // лучший результат остался 3 с
        }

        [Test]
        public void TooDeepSeatGivesHigherCue()
        {
            var a = new WallSitAnalyzer();
            a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg: 55f, timestamp: 0.0));
            Assert.AreEqual("Выше", a.Cue);
        }

        [Test]
        public void TrackerBlinkWithinGraceKeepsTheHold()
        {
            var a = new WallSitAnalyzer();
            Sit(a, 0.0, 4.0);
            a.ProcessFrame(LegTestFrames.WallSit(visibility: 0.2f, timestamp: 4.5));     // моргнул
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            a.ProcessFrame(LegTestFrames.WallSit(timestamp: 5.0));                       // разрыв 1.0 c ≤ grace
            Assert.AreEqual(5, a.Reps);                     // удержание не прервалось
        }

        [Test]
        public void ResetClearsBest()
        {
            var a = new WallSitAnalyzer();
            Sit(a, 0.0, 5.0);
            a.Reset();
            Assert.AreEqual(0, a.Reps);
        }

        [Test]
        public void RegisteredInCatalog()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("wallsit"));
        }
    }
}
```

- [ ] **Step 2: Прогнать — падают.** Run: команда тестов. Expected: `exit=2`.

- [ ] **Step 3: Реализация**

`Assets/Pose/WallSitAnalyzer.cs`:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores a wall-sit hold from a side-on view: both the knee angle (hip–knee–ankle)
    /// and the hip angle (shoulder–hip–knee) must sit in a lenient window around 90°.
    /// The result is the longest continuous hold (via <see cref="HoldTimer"/>, tracker
    /// blinks bridged), surfaced through <see cref="Reps"/> as whole seconds because the
    /// HUD contract has no time field. No <see cref="NoReps"/> for a hold — a drifted
    /// seat just pauses the timer with a corrective cue. Engine-free.
    /// </summary>
    public sealed class WallSitAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private readonly HoldTimer _timer;
        private readonly float _minVisibility;
        private readonly float _minAngleDeg;
        private readonly float _maxAngleDeg;

        private float _lastKnee = float.NaN;
        private float _lastHip = float.NaN;
        private float _lastVis;

        public string Id => "wallsit";
        public string DisplayName => "Wall-sit (сек)";
        public int Reps => (int)_timer.BestSeconds;
        public int NoReps => 0;
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public double BestHoldSeconds => _timer.BestSeconds;
        public double CurrentHoldSeconds => _timer.CurrentSeconds;

        public string DebugInfo =>
            $"knee {(float.IsNaN(_lastKnee) ? "--" : _lastKnee.ToString("0"))}°  " +
            $"hip {(float.IsNaN(_lastHip) ? "--" : _lastHip.ToString("0"))}°  " +
            $"hold {_timer.CurrentSeconds:0.0}s  best {_timer.BestSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public WallSitAnalyzer(HoldTimer timer = null, float minVisibility = 0.6f,
            float minAngleDeg = 70f, float maxAngleDeg = 120f)
        {
            _timer = timer ?? new HoldTimer(graceSeconds: 1.0);
            _minVisibility = minVisibility;
            _minAngleDeg = minAngleDeg;
            _maxAngleDeg = maxAngleDeg;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = Math.Min(
                frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle),
                frame.Get(PoseLandmarkType.LeftShoulder).Visibility);
            float rightVis = Math.Min(
                frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle),
                frame.Get(PoseLandmarkType.RightShoulder).Visibility);
            bool useLeft = leftVis >= rightVis;
            _lastVis = useLeft ? leftVis : rightVis;

            if (_lastVis < _minVisibility)
            {
                // Не помечаем "не в позе": HoldTimer сам сошьёт короткий провал грейсом.
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            PoseLandmark shoulder = frame.Get(useLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(useLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark knee = frame.Get(useLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
            PoseLandmark ankle = frame.Get(useLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);

            _lastKnee = PoseMath.AngleDeg3D(hip, knee, ankle);
            _lastHip = PoseMath.AngleDeg3D(shoulder, hip, knee);

            bool inPose = _lastKnee >= _minAngleDeg && _lastKnee <= _maxAngleDeg
                       && _lastHip >= _minAngleDeg && _lastHip <= _maxAngleDeg;
            _timer.Update(inPose, frame.TimestampSeconds);

            if (inPose)
            {
                FormState = ExerciseFormState.GoodForm;
                Cue = string.Empty;
            }
            else
            {
                FormState = ExerciseFormState.BadForm;
                Cue = _lastKnee > _maxAngleDeg || _lastHip > _maxAngleDeg ? "Ниже" : "Выше";
            }

            Changed?.Invoke();
        }

        public void Reset()
        {
            _timer.Reset();
            _lastKnee = float.NaN;
            _lastHip = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
```

В `ExerciseCatalog` добавить: `new ExerciseDescriptor("wallsit", "Wall-sit (сек)", () => new WallSitAnalyzer()),`

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/WallSitAnalyzer.cs* Assets/Pose/Tests/WallSitAnalyzerTests.cs* Assets/Pose/Tests/LegTestFrames.cs Assets/Pose/ExerciseCatalog.cs
git commit -m "feat: WallSitAnalyzer — удержание стульчика на HoldTimer, секунды через Reps"
```

---

### Task 6: MaeGeriAnalyzer — прямой удар на три высоты

**Files:**
- Create: `Assets/Pose/MaeGeriAnalyzer.cs`
- Modify: `Assets/Pose/Tests/LegTestFrames.cs` (добавить `Kick`)
- Test: `Assets/Pose/Tests/MaeGeriAnalyzerTests.cs`
- Modify: `Assets/Pose/ExerciseCatalog.cs`

**Interfaces:**
- Consumes: `LegLiftCycle`, `KickZone`, `KickHeightZone` (Task 3), `LegTestFrames` (Task 4).
- Produces: `public sealed class MaeGeriAnalyzer : IExerciseAnalyzer` — `MaeGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f, float chamberMaxKneeDeg = 110f, float smoothingAlpha = 0.6f)`; `Id == "maegeri-gedan" | "maegeri-chudan" | "maegeri-jodan"`; `KickZone BestZone` (максимальная зона за сессию, читает `Level0Results.Absorb`, Task 8).

- [ ] **Step 1: Добавить билдер и падающие тесты**

В `Assets/Pose/Tests/LegTestFrames.cs` добавить:

```csharp
        /// <summary>
        /// Side-on kicker. Support (right) leg fixed: ankle (0.6, 0.9), knee (0.6, 0.7),
        /// hip (0.6, 0.5), shoulder (0.6, 0.2). Kicking (left) leg: chambered — knee raised,
        /// shin hanging (bent ≈ 108°); otherwise ankle at <paramref name="kickAnkleY"/>
        /// with a straight leg (knee on the hip→ankle midpoint).
        /// Zones with these anchors: gedan 0.65, chudan 0.35, jodan 0.18, floor 0.9.
        /// </summary>
        public static PoseFrame Kick(float kickAnkleY, bool chambered = false, float visibility = 1f, double timestamp = 0)
        {
            var lm = Blank(visibility);

            void Set(PoseLandmarkType t, float x, float y) => lm[(int)t] = new PoseLandmark(x, y, 0f, visibility);

            Set(PoseLandmarkType.RightAnkle, 0.6f, 0.9f);
            Set(PoseLandmarkType.RightKnee, 0.6f, 0.7f);
            Set(PoseLandmarkType.RightHip, 0.6f, 0.5f);
            Set(PoseLandmarkType.RightShoulder, 0.6f, 0.2f);
            Set(PoseLandmarkType.LeftHip, 0.6f, 0.5f);
            Set(PoseLandmarkType.LeftShoulder, 0.6f, 0.2f);

            if (chambered)
            {
                Set(PoseLandmarkType.LeftKnee, 0.45f, 0.55f);
                Set(PoseLandmarkType.LeftAnkle, 0.45f, 0.75f);
            }
            else
            {
                Set(PoseLandmarkType.LeftAnkle, 0.3f, kickAnkleY);
                Set(PoseLandmarkType.LeftKnee, (0.6f + 0.3f) / 2f, (0.5f + kickAnkleY) / 2f);
            }

            return new PoseFrame(lm, timestamp);
        }
```

`Assets/Pose/Tests/MaeGeriAnalyzerTests.cs` (везде `smoothingAlpha: 1f`):

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class MaeGeriAnalyzerTests
    {
        private const float Floor = 0.9f, Gedan = 0.65f, Chudan = 0.35f, Jodan = 0.18f;

        private static MaeGeriAnalyzer NewAnalyzer(KickZone requested)
            => new MaeGeriAnalyzer(requested, smoothingAlpha: 1f);

        private static void Feed(MaeGeriAnalyzer a, float ankleY, double t, bool chambered = false, float vis = 1f)
            => a.ProcessFrame(LegTestFrames.Kick(ankleY, chambered, vis, t));

        // Полный удар с чамбером: пол → колено → выпрямление → колено → пол.
        private static void FullKick(MaeGeriAnalyzer a, float peakY, ref double t)
        {
            Feed(a, Floor, t); t += 0.2;
            Feed(a, 0f, t, chambered: true); t += 0.2;
            Feed(a, peakY, t); t += 0.2;
            Feed(a, 0f, t, chambered: true); t += 0.2;
            Feed(a, Floor, t); t += 0.2;
        }

        [Test]
        public void CountsKickAtRequestedLevel()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            double t = 0;
            FullKick(a, Chudan, ref t);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(KickZone.Chudan, a.BestZone);
        }

        [Test]
        public void HigherThanRequestedStillCounts()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            double t = 0;
            FullKick(a, Jodan, ref t);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(KickZone.Jodan, a.BestZone);
        }

        [Test]
        public void LowerThanRequestedIsNoRepWithCue()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            double t = 0;
            FullKick(a, Gedan, ref t);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выше", a.Cue);
            Assert.AreEqual(KickZone.Gedan, a.BestZone);   // гибкость меряем по факту
        }

        [Test]
        public void StraightLegLiftCountsButTalliesChamberFault()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            double t = 0;
            Feed(a, Floor, t); t += 0.3;
            Feed(a, Chudan, t); t += 0.3;                  // мах прямой ногой, без чамбера
            Feed(a, Floor, t);
            Assert.AreEqual(1, a.Reps);                    // мягкий скоринг: зачёт
            Assert.AreEqual(1, a.NoReps);                  // но огрех зафиксирован
            Assert.AreEqual("Сначала колено", a.Cue);
        }

        [Test]
        public void JitterLiftDoesNotCount()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0);
            Feed(a, Gedan, 0.05);
            Feed(a, Floor, 0.1);                            // < minLiftSeconds
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0, a.NoReps);
        }

        [Test]
        public void LowVisibilityReportsNotVisible()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0, vis: 0.3f);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
        }

        [Test]
        public void AllThreeLevelsRegisteredInCatalog()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("maegeri-gedan"));
            Assert.IsNotNull(ExerciseCatalog.Create("maegeri-chudan"));
            Assert.IsNotNull(ExerciseCatalog.Create("maegeri-jodan"));
        }
    }
}
```

- [ ] **Step 2: Прогнать — падают.** Run: команда тестов. Expected: `exit=2`.

- [ ] **Step 3: Реализация**

`Assets/Pose/MaeGeriAnalyzer.cs`:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores mae geri (front kick) at a requested height from a side-on view. The kicking
    /// leg is whichever ankle rises (no left/right choice in the UI); its lift, normalized
    /// against the support leg's shank (0 = floor, 1 = support-knee height), drives the
    /// shared <see cref="LegLiftCycle"/>. While the leg is lifted the analyzer samples the
    /// peak <see cref="KickZone"/> (same-frame hip/shoulder anchors) and the minimum knee
    /// bend. Lenient policy: a kick reaching the requested zone OR higher counts; below it
    /// is a no-rep ("Выше"); a straight-leg swing without a chamber counts but is tallied
    /// in <see cref="NoReps"/> ("Сначала колено"). <see cref="BestZone"/> keeps the highest
    /// zone reached this set regardless of the request — the flexibility stat reads it.
    /// Engine-free.
    /// </summary>
    public sealed class MaeGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (боком)";

        private readonly KickZone _requested;
        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly float _chamberMaxKneeDeg;
        private readonly float _smoothingAlpha;

        private float _smoothedLift = float.NaN;
        private KickZone _peakZone = KickZone.None;
        private float _minKneeDeg = 180f;
        private float _lastVis;

        public string Id => "maegeri-" + _requested.ToString().ToLowerInvariant();
        public string DisplayName => "Mae geri " + _requested.ToString().ToLowerInvariant();
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        /// <summary>Highest zone reached this set, independent of the requested level.</summary>
        public KickZone BestZone { get; private set; } = KickZone.None;

        public string DebugInfo =>
            $"lift {(float.IsNaN(_smoothedLift) ? "--" : _smoothedLift.ToString("0.00"))}  " +
            $"phase {_cycle.Phase}  peak {_peakZone}  minKnee {_minKneeDeg:0}°  vis {_lastVis:0.00}";

        public event Action Changed;

        public MaeGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f,
            float chamberMaxKneeDeg = 110f, float smoothingAlpha = 0.6f)
        {
            if (requested == KickZone.None)
                throw new ArgumentOutOfRangeException(nameof(requested));
            _requested = requested;
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _chamberMaxKneeDeg = chamberMaxKneeDeg;
            _smoothingAlpha = smoothingAlpha;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle);
            float rightVis = frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle);
            _lastVis = Math.Min(leftVis, rightVis);

            if (_lastVis < _minVisibility)
            {
                _smoothedLift = float.NaN;
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            // Kicking leg = the one lifted higher relative to the other leg's shank.
            float liftLeft = Lift01(frame, kickingLeft: true);
            float liftRight = Lift01(frame, kickingLeft: false);
            bool kickLeft = liftLeft >= liftRight;
            float lift = kickLeft ? liftLeft : liftRight;

            _smoothedLift = float.IsNaN(_smoothedLift)
                ? lift
                : _smoothedLift + _smoothingAlpha * (lift - _smoothedLift);

            LiftPhase prevPhase = _cycle.Phase;
            bool completed = _cycle.Update(_smoothedLift, frame.TimestampSeconds);

            if (_cycle.Phase == LiftPhase.Lifted)
            {
                if (prevPhase == LiftPhase.Grounded)
                {
                    _peakZone = KickZone.None;
                    _minKneeDeg = 180f;
                    Cue = string.Empty;
                }

                PoseLandmark ankle = frame.Get(kickLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
                PoseLandmark knee = frame.Get(kickLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
                PoseLandmark hip = frame.Get(kickLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
                PoseLandmark shoulder = frame.Get(kickLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);

                KickZone zone = KickHeightZone.Classify(ankle.Y, hip.Y, shoulder.Y);
                if (zone > _peakZone)
                    _peakZone = zone;

                float kneeDeg = PoseMath.AngleDeg3D(hip, knee, ankle);
                if (kneeDeg < _minKneeDeg)
                    _minKneeDeg = kneeDeg;
            }

            if (completed)
            {
                if (_peakZone > BestZone)
                    BestZone = _peakZone;

                if (_peakZone >= _requested)
                {
                    Reps++;
                    if (_minKneeDeg > _chamberMaxKneeDeg)
                    {
                        NoReps++;
                        Cue = "Сначала колено";
                    }
                }
                else
                {
                    NoReps++;
                    Cue = "Выше";
                }
            }

            FormState = string.IsNullOrEmpty(Cue) ? ExerciseFormState.GoodForm : ExerciseFormState.BadForm;
            Changed?.Invoke();
        }

        // Lift of one ankle normalized by the OTHER (support) leg's shank length.
        private static float Lift01(PoseFrame frame, bool kickingLeft)
        {
            PoseLandmark kickAnkle = frame.Get(kickingLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            PoseLandmark supportAnkle = frame.Get(kickingLeft ? PoseLandmarkType.RightAnkle : PoseLandmarkType.LeftAnkle);
            PoseLandmark supportKnee = frame.Get(kickingLeft ? PoseLandmarkType.RightKnee : PoseLandmarkType.LeftKnee);

            float shank = supportAnkle.Y - supportKnee.Y;   // > 0: колено выше лодыжки
            if (shank < 1e-4f)
                return 0f;
            return (supportAnkle.Y - kickAnkle.Y) / shank;
        }

        public void Reset()
        {
            _cycle.Reset();
            Reps = 0;
            NoReps = 0;
            BestZone = KickZone.None;
            _smoothedLift = float.NaN;
            _peakZone = KickZone.None;
            _minKneeDeg = 180f;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
```

В `ExerciseCatalog` добавить:

```csharp
new ExerciseDescriptor("maegeri-gedan", "Mae geri gedan", () => new MaeGeriAnalyzer(KickZone.Gedan)),
new ExerciseDescriptor("maegeri-chudan", "Mae geri chudan", () => new MaeGeriAnalyzer(KickZone.Chudan)),
new ExerciseDescriptor("maegeri-jodan", "Mae geri jodan", () => new MaeGeriAnalyzer(KickZone.Jodan)),
```

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/MaeGeriAnalyzer.cs* Assets/Pose/Tests/MaeGeriAnalyzerTests.cs* Assets/Pose/Tests/LegTestFrames.cs Assets/Pose/ExerciseCatalog.cs
git commit -m "feat: MaeGeriAnalyzer — мае-гери на три высоты, зачёт от запрошенной зоны и выше"
```

---

### Task 7: YokoGeriAnalyzer — медленный боковой подъём

**Files:**
- Create: `Assets/Pose/YokoGeriAnalyzer.cs`
- Test: `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs`
- Modify: `Assets/Pose/ExerciseCatalog.cs`

**Interfaces:**
- Consumes: `LegLiftCycle` (Task 3), `LegTestFrames.Kick` (Task 6 — геометрия сигнала одинакова, для тестов годится).
- Produces: `public sealed class YokoGeriAnalyzer : IExerciseAnalyzer` — `YokoGeriAnalyzer(LegLiftCycle cycle = null, float minVisibility = 0.6f, double slowMinSeconds = 2.0, float smoothingAlpha = 0.6f)`; `Id == "yokogeri-slow"`; `double TotalLiftedSeconds` (суммарное время с поднятой ногой по завершённым циклам — читает `Level0Results.Absorb`, Task 8).

- [ ] **Step 1: Написать падающие тесты**

`Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` (`smoothingAlpha: 1f`):

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class YokoGeriAnalyzerTests
    {
        private const float Floor = 0.9f, Raised = 0.45f;

        private static YokoGeriAnalyzer NewAnalyzer() => new YokoGeriAnalyzer(smoothingAlpha: 1f);

        private static void Feed(YokoGeriAnalyzer a, float ankleY, double t, float vis = 1f)
            => a.ProcessFrame(LegTestFrames.Kick(ankleY, chambered: false, vis, t));

        [Test]
        public void SlowRaiseCounts()
        {
            var a = NewAnalyzer();
            Feed(a, Floor, 0.0);
            Feed(a, Raised, 1.0);
            Feed(a, Raised, 3.5);      // держит
            Feed(a, Floor, 4.0);       // подъём длился 3.0 c ≥ 2.0
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(3.0, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void FastSwingIsNoRep()
        {
            var a = NewAnalyzer();
            Feed(a, Floor, 0.0);
            Feed(a, Raised, 0.3);
            Feed(a, Floor, 1.0);       // подъём 0.7 c < 2.0 — быстрый мах
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Медленнее", a.Cue);
        }

        [Test]
        public void HoldSecondsAccumulateAcrossReps()
        {
            var a = NewAnalyzer();
            double t = 0;
            for (int i = 0; i < 2; i++)
            {
                Feed(a, Floor, t); t += 0.5;
                Feed(a, Raised, t); t += 2.5;
                Feed(a, Floor, t); t += 0.5;
            }
            Assert.AreEqual(2, a.Reps);
            Assert.AreEqual(5.0, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void LowVisibilityReportsNotVisibleWithFrontCue()
        {
            var a = NewAnalyzer();
            Feed(a, Floor, 0.0, vis: 0.3f);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (лицом)", a.Cue);
        }

        [Test]
        public void RegisteredInCatalog()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("yokogeri-slow"));
        }
    }
}
```

- [ ] **Step 2: Прогнать — падают.** Run: команда тестов. Expected: `exit=2`.

- [ ] **Step 3: Реализация**

`Assets/Pose/YokoGeriAnalyzer.cs`:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores the slow yoko-geri (controlled side leg raise) facing the camera — the leg
    /// travels sideways, so a profile view would hide its height. Reuses the mae geri lift
    /// signal (kicking ankle vs the support shank) through <see cref="LegLiftCycle"/>; a
    /// cycle counts only when the leg stayed up at least <c>slowMinSeconds</c> — this is a
    /// balance drill, so a fast swing is the fault ("Медленнее"), not the height reached.
    /// <see cref="TotalLiftedSeconds"/> accumulates airtime across completed cycles for
    /// the balance stat. Engine-free.
    /// </summary>
    public sealed class YokoGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (лицом)";

        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly double _slowMinSeconds;
        private readonly float _smoothingAlpha;

        private float _smoothedLift = float.NaN;
        private float _lastVis;

        public string Id => "yokogeri-slow";
        public string DisplayName => "Yoko-geri slow";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        /// <summary>Total airtime across completed lift cycles, seconds (balance stat).</summary>
        public double TotalLiftedSeconds { get; private set; }

        public string DebugInfo =>
            $"lift {(float.IsNaN(_smoothedLift) ? "--" : _smoothedLift.ToString("0.00"))}  " +
            $"phase {_cycle.Phase}  air {_cycle.LiftedSeconds:0.0}s  total {TotalLiftedSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public YokoGeriAnalyzer(LegLiftCycle cycle = null, float minVisibility = 0.6f,
            double slowMinSeconds = 2.0, float smoothingAlpha = 0.6f)
        {
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _slowMinSeconds = slowMinSeconds;
            _smoothingAlpha = smoothingAlpha;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle);
            float rightVis = frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle);
            _lastVis = Math.Min(leftVis, rightVis);

            if (_lastVis < _minVisibility)
            {
                _smoothedLift = float.NaN;
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            float liftLeft = Lift01(frame, kickingLeft: true);
            float liftRight = Lift01(frame, kickingLeft: false);
            float lift = Math.Max(liftLeft, liftRight);

            _smoothedLift = float.IsNaN(_smoothedLift)
                ? lift
                : _smoothedLift + _smoothingAlpha * (lift - _smoothedLift);

            LiftPhase prevPhase = _cycle.Phase;
            bool completed = _cycle.Update(_smoothedLift, frame.TimestampSeconds);

            if (prevPhase == LiftPhase.Grounded && _cycle.Phase == LiftPhase.Lifted)
                Cue = string.Empty;

            if (completed)
            {
                TotalLiftedSeconds += _cycle.LiftedSeconds;
                if (_cycle.LiftedSeconds >= _slowMinSeconds)
                {
                    Reps++;
                }
                else
                {
                    NoReps++;
                    Cue = "Медленнее";
                }
            }

            FormState = string.IsNullOrEmpty(Cue) ? ExerciseFormState.GoodForm : ExerciseFormState.BadForm;
            Changed?.Invoke();
        }

        // Same normalized lift signal as mae geri: ankle height over the other leg's shank.
        private static float Lift01(PoseFrame frame, bool kickingLeft)
        {
            PoseLandmark kickAnkle = frame.Get(kickingLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            PoseLandmark supportAnkle = frame.Get(kickingLeft ? PoseLandmarkType.RightAnkle : PoseLandmarkType.LeftAnkle);
            PoseLandmark supportKnee = frame.Get(kickingLeft ? PoseLandmarkType.RightKnee : PoseLandmarkType.LeftKnee);

            float shank = supportAnkle.Y - supportKnee.Y;
            if (shank < 1e-4f)
                return 0f;
            return (supportAnkle.Y - kickAnkle.Y) / shank;
        }

        public void Reset()
        {
            _cycle.Reset();
            Reps = 0;
            NoReps = 0;
            TotalLiftedSeconds = 0;
            _smoothedLift = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
```

В `ExerciseCatalog` добавить: `new ExerciseDescriptor("yokogeri-slow", "Yoko-geri slow", () => new YokoGeriAnalyzer()),`

Итоговый список инициализатора каталога после Task 7 (порядок = порядок кнопок в песочнице):

```csharp
new ExerciseDescriptor("pushup", "Push-ups", () => new PushUpAnalyzer()),
new ExerciseDescriptor("squat", "Squats", () => new SquatAnalyzer()),
new ExerciseDescriptor("wallsit", "Wall-sit (сек)", () => new WallSitAnalyzer()),
new ExerciseDescriptor("yokogeri-slow", "Yoko-geri slow", () => new YokoGeriAnalyzer()),
new ExerciseDescriptor("maegeri-gedan", "Mae geri gedan", () => new MaeGeriAnalyzer(KickZone.Gedan)),
new ExerciseDescriptor("maegeri-chudan", "Mae geri chudan", () => new MaeGeriAnalyzer(KickZone.Chudan)),
new ExerciseDescriptor("maegeri-jodan", "Mae geri jodan", () => new MaeGeriAnalyzer(KickZone.Jodan)),
```

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/YokoGeriAnalyzer.cs* Assets/Pose/Tests/YokoGeriAnalyzerTests.cs* Assets/Pose/ExerciseCatalog.cs
git commit -m "feat: YokoGeriAnalyzer — медленный боковой подъём, зачёт только небыстрых"
```

---

### Task 8: Level0Results и StatCalculator

**Files:**
- Create: `Assets/Pose/Level0Results.cs`
- Create: `Assets/Pose/StatCalculator.cs`
- Test: `Assets/Pose/Tests/StatCalculatorTests.cs`

**Interfaces:**
- Consumes: все анализаторы (Tasks 4–7) и `PushUpAnalyzer`; их публичные свойства `Reps`, `BestHoldSeconds`, `TotalLiftedSeconds`, `BestZone`.
- Produces:
  - `[Serializable] public sealed class Level0Results` — поля `int PushUpReps, SquatReps, YokoGeriSlowReps, MaeGeriBestZone; float WallSitSeconds, YokoGeriHoldSeconds`; `void Absorb(IExerciseAnalyzer a)` (max-слияние — оценка отражает лучший результат); `static Level0Results Load()`, `void Save()` (PlayerPrefs, ключ `"level0.results"`).
  - `public readonly struct PlayerStats { int Strength, Endurance, Flexibility, Balance; }` (конструктор с 4 параметрами в этом порядке).
  - `public static class StatCalculator` — `static PlayerStats Compute(Level0Results r)`. Якоря: 30 пуш-апов = 100, 40 приседаний = 100 (сила — среднее двух), 120 с wall-sit = 100, зоны mae geri 0/33/66/100, 10 медленных yoko-geri = 70 + 20 c удержания = 30 (баланс — сумма, потолок 100).
  - Всё это читает песочница (Task 9).

- [ ] **Step 1: Написать падающие тесты**

`Assets/Pose/Tests/StatCalculatorTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class StatCalculatorTests
    {
        [Test]
        public void EmptyResultsGiveZeroStats()
        {
            PlayerStats s = StatCalculator.Compute(new Level0Results());
            Assert.AreEqual(0, s.Strength);
            Assert.AreEqual(0, s.Endurance);
            Assert.AreEqual(0, s.Flexibility);
            Assert.AreEqual(0, s.Balance);
        }

        [Test]
        public void AnchorsGiveExactly100()
        {
            var r = new Level0Results
            {
                PushUpReps = 30,
                SquatReps = 40,
                WallSitSeconds = 120f,
                MaeGeriBestZone = (int)KickZone.Jodan,
                YokoGeriSlowReps = 10,
                YokoGeriHoldSeconds = 20f,
            };
            PlayerStats s = StatCalculator.Compute(r);
            Assert.AreEqual(100, s.Strength);
            Assert.AreEqual(100, s.Endurance);
            Assert.AreEqual(100, s.Flexibility);
            Assert.AreEqual(100, s.Balance);
        }

        [Test]
        public void AboveAnchorClampsTo100()
        {
            var r = new Level0Results { PushUpReps = 90, SquatReps = 200, WallSitSeconds = 999f };
            PlayerStats s = StatCalculator.Compute(r);
            Assert.AreEqual(100, s.Strength);
            Assert.AreEqual(100, s.Endurance);
        }

        [Test]
        public void MidpointsScaleLinearly()
        {
            var r = new Level0Results
            {
                PushUpReps = 15,               // 50 из пуш-апов
                SquatReps = 10,                // 25 из приседаний
                WallSitSeconds = 60f,
                MaeGeriBestZone = (int)KickZone.Chudan,
                YokoGeriSlowReps = 5,          // 35 из повторов
                YokoGeriHoldSeconds = 10f,     // 15 из удержания
            };
            PlayerStats s = StatCalculator.Compute(r);
            Assert.AreEqual(38, s.Strength);   // (0.5 + 0.25) / 2 * 100 = 37.5 → 38
            Assert.AreEqual(50, s.Endurance);
            Assert.AreEqual(66, s.Flexibility);
            Assert.AreEqual(50, s.Balance);
        }

        [Test]
        public void AbsorbKeepsBestOfEachExercise()
        {
            var r = new Level0Results { SquatReps = 12 };

            var squat = new SquatAnalyzer(smoothingAlpha: 1f);
            // 1 повтор — меньше сохранённых 12: результат не ухудшается.
            squat.ProcessFrame(LegTestFrames.Squat(175f, timestamp: 0.0));
            squat.ProcessFrame(LegTestFrames.Squat(95f, timestamp: 1.0));
            squat.ProcessFrame(LegTestFrames.Squat(175f, timestamp: 2.0));
            r.Absorb(squat);
            Assert.AreEqual(12, r.SquatReps);

            var wallsit = new WallSitAnalyzer();
            for (double t = 0; t <= 42.0 + 1e-9; t += 0.5)     // кадры чаще грейса HoldTimer
                wallsit.ProcessFrame(LegTestFrames.WallSit(timestamp: t));
            r.Absorb(wallsit);
            Assert.AreEqual(42f, r.WallSitSeconds, 1e-3f);

            var mg = new MaeGeriAnalyzer(KickZone.Gedan, smoothingAlpha: 1f);
            mg.ProcessFrame(LegTestFrames.Kick(0.9f, timestamp: 0.0));
            mg.ProcessFrame(LegTestFrames.Kick(0.18f, timestamp: 0.5));    // jodan
            mg.ProcessFrame(LegTestFrames.Kick(0.9f, timestamp: 1.0));
            r.Absorb(mg);
            Assert.AreEqual((int)KickZone.Jodan, r.MaeGeriBestZone);
        }

        [Test]
        public void SaveLoadRoundTripsThroughPlayerPrefs()
        {
            var r = new Level0Results { PushUpReps = 7, WallSitSeconds = 33.5f };
            r.Save();
            Level0Results loaded = Level0Results.Load();
            Assert.AreEqual(7, loaded.PushUpReps);
            Assert.AreEqual(33.5f, loaded.WallSitSeconds, 1e-3f);
        }
    }
}
```

- [ ] **Step 2: Прогнать — падают.** Run: команда тестов. Expected: `exit=2`.

- [ ] **Step 3: Реализация**

`Assets/Pose/Level0Results.cs`:

```csharp
using System;
using UnityEngine;

namespace Mikey.Pose
{
    /// <summary>
    /// Raw level-0 assessment results, one field per exercise outcome. Absorb() max-merges
    /// a finished set into the stored results — the assessment reflects the player's best
    /// effort, so a weaker retry never downgrades it. Persisted as JSON in PlayerPrefs;
    /// the future game profile reads the same store. The only Pose class that touches
    /// UnityEngine (PlayerPrefs/JsonUtility).
    /// </summary>
    [Serializable]
    public sealed class Level0Results
    {
        private const string PrefsKey = "level0.results";

        public int PushUpReps;
        public int SquatReps;
        public int YokoGeriSlowReps;
        public int MaeGeriBestZone;        // (int)KickZone — JsonUtility дружит с int
        public float WallSitSeconds;
        public float YokoGeriHoldSeconds;

        /// <summary>Max-merges one finished set into these results.</summary>
        public void Absorb(IExerciseAnalyzer analyzer)
        {
            switch (analyzer)
            {
                case PushUpAnalyzer p:
                    PushUpReps = Math.Max(PushUpReps, p.Reps);
                    break;
                case SquatAnalyzer s:
                    SquatReps = Math.Max(SquatReps, s.Reps);
                    break;
                case WallSitAnalyzer w:
                    WallSitSeconds = Math.Max(WallSitSeconds, (float)w.BestHoldSeconds);
                    break;
                case YokoGeriAnalyzer y:
                    YokoGeriSlowReps = Math.Max(YokoGeriSlowReps, y.Reps);
                    YokoGeriHoldSeconds = Math.Max(YokoGeriHoldSeconds, (float)y.TotalLiftedSeconds);
                    break;
                case MaeGeriAnalyzer m:
                    MaeGeriBestZone = Math.Max(MaeGeriBestZone, (int)m.BestZone);
                    break;
            }
        }

        public static Level0Results Load()
        {
            string json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new Level0Results();
            Level0Results r = JsonUtility.FromJson<Level0Results>(json);
            return r ?? new Level0Results();
        }

        public void Save()
        {
            PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
```

`Assets/Pose/StatCalculator.cs`:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>Player stats derived from the level-0 assessment, each 0–100.</summary>
    public readonly struct PlayerStats
    {
        public readonly int Strength;
        public readonly int Endurance;
        public readonly int Flexibility;
        public readonly int Balance;

        public PlayerStats(int strength, int endurance, int flexibility, int balance)
        {
            Strength = strength;
            Endurance = endurance;
            Flexibility = flexibility;
            Balance = balance;
        }
    }

    /// <summary>
    /// Maps raw level-0 results to the four stats. All formulas are linear ramps to a
    /// named anchor ("30 push-ups = 100") clamped at 100 — deliberately simple until
    /// real-player data justifies anything fancier; tune the anchors, not the shape.
    /// </summary>
    public static class StatCalculator
    {
        private const float PushUpsFor100 = 30f;
        private const float SquatsFor100 = 40f;
        private const float WallSitSecondsFor100 = 120f;
        private const float SlowRepsFor70 = 10f;
        private const float HoldSecondsFor30 = 20f;
        private static readonly int[] FlexibilityByZone = { 0, 33, 66, 100 };

        public static PlayerStats Compute(Level0Results r)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            float strength = (Ramp(r.PushUpReps, PushUpsFor100) + Ramp(r.SquatReps, SquatsFor100)) / 2f * 100f;
            float endurance = Ramp(r.WallSitSeconds, WallSitSecondsFor100) * 100f;
            int zone = Math.Min(Math.Max(r.MaeGeriBestZone, 0), FlexibilityByZone.Length - 1);
            float balance = Ramp(r.YokoGeriSlowReps, SlowRepsFor70) * 70f
                          + Ramp(r.YokoGeriHoldSeconds, HoldSecondsFor30) * 30f;

            return new PlayerStats(
                (int)Math.Round(strength),
                (int)Math.Round(endurance),
                FlexibilityByZone[zone],
                (int)Math.Round(balance));
        }

        private static float Ramp(float value, float anchor) =>
            value <= 0f ? 0f : Math.Min(value / anchor, 1f);
    }
}
```

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/Level0Results.cs* Assets/Pose/StatCalculator.cs* Assets/Pose/Tests/StatCalculatorTests.cs*
git commit -m "feat: Level0Results и StatCalculator — статы 0–100 из итогов уровня 0"
```

---

### Task 9: Интеграция в песочницу — запись результатов и строка статов

**Files:**
- Modify: `Assets/Pose/DevSandbox/ExerciseSandbox.cs` (поля ~строка 38, `Awake` ~40-44, `DrawPicker` ~106-126, кнопка Back ~169-174)

**Interfaces:**
- Consumes: `Level0Results.Load/Absorb/Save`, `StatCalculator.Compute`, `PlayerStats` (Task 8).
- Produces: ничего нового наружу — только UI-обвязка.

- [ ] **Step 1: Внести изменения**

В `ExerciseSandbox` добавить поля и загрузку:

```csharp
        private Level0Results _results;
        private PlayerStats _stats;
```

В `Awake()` после `_voice = new AndroidVoice();`:

```csharp
            _results = Level0Results.Load();
            _stats = StatCalculator.Compute(_results);
```

В `DrawPicker()` после строки с советом (`GUILayout.Label("Совет: ..."`)):

```csharp
            GUILayout.Label(
                $"СТАТЫ  сила {_stats.Strength}  вынос {_stats.Endurance}  гибк {_stats.Flexibility}  баланс {_stats.Balance}",
                _mid);
```

В обработчике кнопки `Back (save)` перед `_controller.ClearExercise();`:

```csharp
                _results.Absorb(a);
                _results.Save();
                _stats = StatCalculator.Compute(_results);
```

- [ ] **Step 2: Прогнать тесты (компиляция всего проекта + регрессия)**

Run: команда тестов. Expected: `exit=0` (заодно подтверждает, что DevSandbox-asmdef компилируется с новыми вызовами).

- [ ] **Step 3: Ручная проверка в Editor (опционально, если Editor доступен)**

Меню *Mikey → Dev → Create or Open Exercise Sandbox Scene* → Play: на экране выбора видна строка «СТАТЫ …», в списке 7 кнопок упражнений. Выйти из упражнения по Back (save) → строка статов обновилась.

- [ ] **Step 4: Commit**

```powershell
git add Assets/Pose/DevSandbox/ExerciseSandbox.cs
git commit -m "feat: песочница пишет итоги уровня 0 и показывает статы на экране выбора"
```

---

### Task 10: APK и on-device smoke

Финальная верификация на устройстве. Требует подключённого Android-телефона — шаги съёмки выполняет пользователь; агентная часть — собрать и установить APK.

**Files:**
- никаких изменений кода; используется существующий `Assets/Pose/DevSandbox/Editor/AndroidBuilder.cs`

- [ ] **Step 1: Собрать APK** (Editor закрыт; сборка небыстрая — таймаут ставить широкий)

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe" -quit -batchmode -projectPath "C:\Users\user\Mikey" -buildTarget Android -executeMethod Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid -logFile "C:\Users\user\Mikey\Temp\apk_build.log"; "exit=$LASTEXITCODE"
```

Expected: `exit=0`, в логе `BUILD OK -> Builds/ExerciseSandbox.apk`.

- [ ] **Step 2: Установить на устройство**

```powershell
adb install -r Builds/ExerciseSandbox.apk
```

Expected: `Success`.

- [ ] **Step 3: Смоук-чеклист (выполняет пользователь с телефоном)**

По каждой технике — сделать подход и сверить с ручным подсчётом:

1. **Squats**: 5 приседаний сбоку → REPS = 5; недосед не считается; присед с сильным наклоном вперёд считается, но растит no-reps.
2. **Wall-sit (сек)**: сесть у стены ~30 с сбоку → REPS ≈ 30; привстать — таймер встаёт с cue «Ниже».
3. **Yoko-geri slow**: 3 медленных подъёма (2+ с каждый) лицом к камере → REPS = 3; быстрый мах → no-rep «Медленнее».
4. **Mae geri gedan/chudan/jodan**: по 3 удара на каждом уровне сбоку → REPS = 3 на своём уровне; удар ниже запрошенного → no-rep «Выше».
5. **Статы**: выйти по Back (save) после каждого подхода → на экране выбора строка «СТАТЫ …» растёт соответственно.

При расхождениях: SAVE LOG → `adb pull /storage/emulated/0/Android/data/com.mikey.equilibrium/files/pose_rec_*.csv` → разбор в *Mikey → Dev → Create or Open Pose Review Scene*, калибровка порогов конструктора по фактическим углам (это ожидаемая часть процесса, как с пуш-апом 140°/105°).

- [ ] **Step 4: Зафиксировать калибровки (если были)**

Если пороги менялись — прогнать команду тестов (Expected: `exit=0`, при изменении дефолтов поправить тестовые ожидания осознанно) и закоммитить:

```powershell
git add Assets/Pose
git commit -m "fix: калибровка порогов уровня 0 по съёмке на устройстве"
```

---

## Порядок и зависимости

```
Task 1 (RepCounter) ──→ Task 4 (Squat) ─┐
Task 2 (HoldTimer) ──→ Task 5 (WallSit) ─┤
Task 3 (LegLiftCycle,Zones) ─→ Task 6 (MaeGeri) ─→ Task 7 (YokoGeri) ─┤
                                         └────────→ Task 8 (Stats) ──→ Task 9 (Sandbox) ─→ Task 10 (APK)
```

Tasks 1–3 независимы друг от друга; Task 6 нужен раньше Task 7 только из-за общего билдера `Kick` в `LegTestFrames`. Task 8 требует все анализаторы (4–7).
