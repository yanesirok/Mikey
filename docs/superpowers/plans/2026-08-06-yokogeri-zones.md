# Yoko geri по трём высотам — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Вместо одного «Yoko-geri slow» — три отдельных упражнения по высоте удара (гэдан/чудан/дзёдан); зона засчитывается только с выпрямленной ногой; гибкость в статах — среднее mae geri и yoko geri.

**Architecture:** `YokoGeriAnalyzer` параметризуется зоной, зеркаля `MaeGeriAnalyzer`: тот же сигнал подъёма стопы + `LegLiftCycle` + `KickHeightZone`. Новое — фильтр выпрямленности: зона сэмплируется только в кадрах с 2D-углом колена (`PoseMath.AngleDeg`, плоскость кадра) ≥ 150°, чтобы поднятый замах не считался ударом. `Level0Results` получает `YokoGeriBestZone`; `StatCalculator` усредняет гибкость двух ударов.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-06-yokogeri-zones-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; при падениях смотреть `Temp/pose_tests.xml`):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Temp\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Пороги и строки — ровно эти значения, не «улучшать»: `minExtensionDeg = 150f`, `minVisibility = 0.6f`, `smoothingAlpha = 0.6f`, `LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2)`; cue «В кадр (лицом)», «Выше», «Выпрями ногу»; подсказка песочницы «Лицом к камере, можно держаться за стену».
- Поля `Level0Results.YokoGeriSlowReps` и `YokoGeriHoldSeconds` НЕ переименовывать — JsonUtility потеряет сохранённые результаты игрока.
- Корпусы пуш-апа (5/4/0), приседа (18/15/1), wall-sit (6/0), все тесты mae geri — зелёные без правок их ассертов.
- Новых файлов нет (правки только в существующих) — `.meta` не появятся. Посторонние изменённые файлы (арена, ProjectSettings) не трогать.
- Если код и тесты брифа противоречат друг другу — статус BLOCKED, не подгонять одно под другое.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: YokoGeriAnalyzer на зонах + каталог + статы

**Files:**
- Modify: `Assets/Pose/YokoGeriAnalyzer.cs` (полная замена содержимого класса)
- Modify: `Assets/Pose/ExerciseCatalog.cs:33` (одна строка `yokogeri-slow` → три записи)
- Modify: `Assets/Pose/Level0Results.cs` (поле `YokoGeriBestZone` + ветка Absorb)
- Modify: `Assets/Pose/StatCalculator.cs` (гибкость — среднее двух ударов)
- Modify: `Assets/Pose/Tests/LegTestFrames.cs` (билдер `ChamberHigh` в конец класса)
- Modify: `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` (полная замена: 8 тестов под зоны)
- Modify: `Assets/Pose/Tests/StatCalculatorTests.cs` (якоря/середины + новый тест гибкости + yoko-секция Absorb)

**Interfaces:**
- Consumes (всё уже в кодовой базе): `KickZone` (None/Gedan/Chudan/Jodan), `KickHeightZone.Classify(float ankleY, float hipY, float shoulderY)`, `LegLiftCycle` (`Phase`, `LiftedSeconds`, `Update(float, double)`, `Reset()`), `PoseMath.AngleDeg(PoseLandmark a, PoseLandmark b, PoseLandmark c)` (2D), `PoseFrame.MinVisibility(params PoseLandmarkType[])`, `PoseFrame.Get(PoseLandmarkType)`, `LegTestFrames.Kick(float kickAnkleY, bool chambered = false, float visibility = 1f, double timestamp = 0, float shoulderVisibility = 1f)` — прямая нога, зоны gedan 0.65 / chudan 0.35 / jodan 0.18, пол 0.9.
- Produces: `YokoGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f, float minExtensionDeg = 150f, float smoothingAlpha = 0.6f)` со свойствами `Id` (`"yokogeri-gedan"` и т.п.), `DisplayName` (`"Yoko geri gedan"` и т.п.), `Reps`, `NoReps`, `BestZone`, `TotalLiftedSeconds`, `Cue`, `FormState`, `DebugInfo`, событие `Changed`, `Reset()`; `Level0Results.YokoGeriBestZone` (int); `LegTestFrames.ChamberHigh(float visibility = 1f, double timestamp = 0)`.

- [ ] **Step 1: Новые тесты (красная фаза)**

Полностью заменить содержимое `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs`:

```csharp
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    public class YokoGeriAnalyzerTests
    {
        private const float Floor = 0.9f, GedanY = 0.65f, ChudanY = 0.35f, JodanY = 0.18f;

        private static YokoGeriAnalyzer NewAnalyzer(KickZone requested) =>
            new YokoGeriAnalyzer(requested, smoothingAlpha: 1f);

        private static void Feed(YokoGeriAnalyzer a, float ankleY, double t, float vis = 1f)
            => a.ProcessFrame(LegTestFrames.Kick(ankleY, chambered: false, vis, t));

        [Test]
        public void KickToRequestedZoneCountsAtAnyTempo()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            Feed(a, Floor, 0.0);
            Feed(a, ChudanY, 0.3);
            Feed(a, Floor, 0.6);       // быстрый мах — темп свободный
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void KickAboveRequestedZoneCounts()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0);
            Feed(a, JodanY, 0.3);
            Feed(a, Floor, 0.6);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(KickZone.Jodan, a.BestZone);
        }

        [Test]
        public void KickBelowRequestedZoneIsNoRepWithHigherCue()
        {
            var a = NewAnalyzer(KickZone.Jodan);
            Feed(a, Floor, 0.0);
            Feed(a, GedanY, 0.3);
            Feed(a, Floor, 0.6);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выше", a.Cue);
            Assert.AreEqual(KickZone.Gedan, a.BestZone);   // лучшая зона копится и на незачёте
        }

        [Test]
        public void HighChamberWithoutExtensionIsNoRepWithExtendCue()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            a.ProcessFrame(LegTestFrames.Kick(Floor, chambered: false, 1f, 0.0));
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));
            a.ProcessFrame(LegTestFrames.Kick(Floor, chambered: false, 1f, 0.6));
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выпрями ногу", a.Cue);
            Assert.AreEqual(KickZone.None, a.BestZone);
        }

        [Test]
        public void AirtimeAccumulatesForCountedReps()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            Feed(a, Floor, 0.0);
            Feed(a, ChudanY, 1.0);
            Feed(a, ChudanY, 3.0);
            Feed(a, Floor, 3.5);       // в воздухе 2.5 c
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(2.5, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void FailedKickAddsNoAirtime()
        {
            var a = NewAnalyzer(KickZone.Jodan);
            Feed(a, Floor, 0.0);
            Feed(a, GedanY, 1.0);
            Feed(a, Floor, 2.0);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(0.0, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void LowVisibilityReportsNotVisibleWithFrontCue()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0, vis: 0.3f);
            Assert.AreEqual(ExerciseFormState.NotVisible, a.FormState);
            Assert.AreEqual("В кадр (лицом)", a.Cue);
        }

        [Test]
        public void CatalogHasThreeZoneVariantsAndNoSlow()
        {
            Assert.IsNotNull(ExerciseCatalog.Create("yokogeri-gedan"));
            Assert.IsNotNull(ExerciseCatalog.Create("yokogeri-chudan"));
            Assert.IsNotNull(ExerciseCatalog.Create("yokogeri-jodan"));
            Assert.IsNull(ExerciseCatalog.Create("yokogeri-slow"));
        }
    }
}
```

В `Assets/Pose/Tests/LegTestFrames.cs` добавить в конец класса (после `Stride`):

```csharp
        /// <summary>
        /// High chamber, no extension: the left knee is pulled up level with the hip, the
        /// shin hangs down — the ankle clears the lift threshold (lift 1.25) but the
        /// in-plane knee angle stays ≈72°. A kick analyzer must not award a height zone.
        /// </summary>
        public static PoseFrame ChamberHigh(float visibility = 1f, double timestamp = 0)
        {
            var lm = Blank(visibility);
            void Put(PoseLandmarkType t, float x, float y) => lm[(int)t] = new PoseLandmark(x, y, 0f, visibility);

            Put(PoseLandmarkType.RightAnkle, 0.6f, 0.9f);
            Put(PoseLandmarkType.RightKnee, 0.6f, 0.7f);
            Put(PoseLandmarkType.RightHip, 0.6f, 0.5f);
            Put(PoseLandmarkType.RightShoulder, 0.6f, 0.2f);
            Put(PoseLandmarkType.LeftHip, 0.6f, 0.5f);
            Put(PoseLandmarkType.LeftShoulder, 0.6f, 0.2f);
            Put(PoseLandmarkType.LeftKnee, 0.45f, 0.45f);
            Put(PoseLandmarkType.LeftAnkle, 0.45f, 0.65f);
            return new PoseFrame(lm, timestamp);
        }
```

В `Assets/Pose/Tests/StatCalculatorTests.cs`:

1. В `AnchorsGiveExactly100` в инициализатор `Level0Results` добавить строку:

```csharp
                YokoGeriBestZone = (int)KickZone.Jodan,
```

2. В `MidpointsScaleLinearly` в инициализатор добавить строку (ассерт `66` не меняется):

```csharp
                YokoGeriBestZone = (int)KickZone.Chudan,
```

3. Новый тест после `MidpointsScaleLinearly`:

```csharp
        [Test]
        public void FlexibilityAveragesFrontAndSideKicks()
        {
            var r = new Level0Results { MaeGeriBestZone = (int)KickZone.Jodan };
            Assert.AreEqual(50, StatCalculator.Compute(r).Flexibility);   // только передний удар
            r.YokoGeriBestZone = (int)KickZone.Jodan;
            Assert.AreEqual(100, StatCalculator.Compute(r).Flexibility);
        }
```

4. В `AbsorbKeepsBestOfEachExercise` заменить yoko-секцию (последний блок `var yoko … 1e-3f);`) на:

```csharp
            var yoko = new YokoGeriAnalyzer(KickZone.Gedan, smoothingAlpha: 1f);
            yoko.ProcessFrame(LegTestFrames.Kick(0.9f, timestamp: 0.0));
            yoko.ProcessFrame(LegTestFrames.Kick(0.35f, timestamp: 1.0));   // chudan
            yoko.ProcessFrame(LegTestFrames.Kick(0.35f, timestamp: 3.5));
            yoko.ProcessFrame(LegTestFrames.Kick(0.9f, timestamp: 4.0));
            r.Absorb(yoko);
            Assert.AreEqual(1, r.YokoGeriSlowReps);
            Assert.AreEqual(3.0f, r.YokoGeriHoldSeconds, 1e-3f);
            Assert.AreEqual((int)KickZone.Chudan, r.YokoGeriBestZone);
```

- [ ] **Step 2: Прогнать — красная фаза.** Run: команда тестов. Expected: exit ≠ 0 — **ошибка компиляции** (у `YokoGeriAnalyzer` ещё нет конструктора с `KickZone`, у `Level0Results` нет `YokoGeriBestZone`, у `LegTestFrames` нет `ChamberHigh` — он добавлен на Step 1 вместе с тестами, поэтому упадёт именно компиляция продакшен-ссылок). Если тесты вдруг СКОМПИЛИРОВАЛИСЬ — это ошибка транскрипции, BLOCKED.

- [ ] **Step 3: Реализация**

Полностью заменить содержимое `Assets/Pose/YokoGeriAnalyzer.cs`:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores yoko geri (side kick) at a requested height facing the camera — the leg
    /// travels sideways, so a profile view would hide its height. Same lift signal and
    /// <see cref="LegLiftCycle"/> as mae geri; the height zone is sampled only on frames
    /// where the leg is extended (in-plane knee angle ≥ minExtensionDeg — noisy z depth
    /// is not involved), so a raised chamber alone is not a kick. Lenient policy: a kick
    /// reaching the requested zone OR higher counts; below it is a no-rep ("Выше"); a
    /// lift that never extends is a no-rep ("Выпрями ногу"). <see cref="BestZone"/> keeps
    /// the highest zone this set (flexibility stat); <see cref="TotalLiftedSeconds"/>
    /// accumulates airtime of counted reps (balance stat). Holding a wall for support is
    /// allowed and not checked. Engine-free.
    /// </summary>
    public sealed class YokoGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (лицом)";

        private readonly KickZone _requested;
        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly float _minExtensionDeg;
        private readonly float _smoothingAlpha;

        private float _smoothedLift = float.NaN;
        private KickZone _peakZone = KickZone.None;
        private float _lastKneeDeg = float.NaN;
        private float _lastVis;

        public string Id => "yokogeri-" + _requested.ToString().ToLowerInvariant();
        public string DisplayName => "Yoko geri " + _requested.ToString().ToLowerInvariant();
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        /// <summary>Highest zone reached this set, independent of the requested level.</summary>
        public KickZone BestZone { get; private set; } = KickZone.None;

        /// <summary>Total airtime across counted reps, seconds (balance stat).</summary>
        public double TotalLiftedSeconds { get; private set; }

        public string DebugInfo =>
            $"lift {(float.IsNaN(_smoothedLift) ? "--" : _smoothedLift.ToString("0.00"))}  " +
            $"phase {_cycle.Phase}  peak {_peakZone}  knee {(float.IsNaN(_lastKneeDeg) ? "--" : _lastKneeDeg.ToString("0"))}°  " +
            $"total {TotalLiftedSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public YokoGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f,
            float minExtensionDeg = 150f, float smoothingAlpha = 0.6f)
        {
            if (requested == KickZone.None)
                throw new ArgumentOutOfRangeException(nameof(requested));
            _requested = requested;
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _minExtensionDeg = minExtensionDeg;
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
                    Cue = string.Empty;
                }

                PoseLandmark ankle = frame.Get(kickLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
                PoseLandmark knee = frame.Get(kickLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
                PoseLandmark hip = frame.Get(kickLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
                PoseLandmark shoulder = frame.Get(kickLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);

                _lastKneeDeg = PoseMath.AngleDeg(hip, knee, ankle);
                if (_lastKneeDeg >= _minExtensionDeg)
                {
                    KickZone zone = KickHeightZone.Classify(ankle.Y, hip.Y, shoulder.Y);
                    if (zone > _peakZone)
                        _peakZone = zone;
                }
            }

            if (completed)
            {
                if (_peakZone > BestZone)
                    BestZone = _peakZone;

                if (_peakZone >= _requested)
                {
                    Reps++;
                    TotalLiftedSeconds += _cycle.LiftedSeconds;
                }
                else
                {
                    NoReps++;
                    Cue = _peakZone == KickZone.None ? "Выпрями ногу" : "Выше";
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
            TotalLiftedSeconds = 0;
            _smoothedLift = float.NaN;
            _peakZone = KickZone.None;
            _lastKneeDeg = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
```

В `Assets/Pose/ExerciseCatalog.cs` заменить строку

```csharp
            new ExerciseDescriptor("yokogeri-slow", "Yoko-geri slow", () => new YokoGeriAnalyzer()),
```

на

```csharp
            new ExerciseDescriptor("yokogeri-gedan", "Yoko geri gedan", () => new YokoGeriAnalyzer(KickZone.Gedan)),
            new ExerciseDescriptor("yokogeri-chudan", "Yoko geri chudan", () => new YokoGeriAnalyzer(KickZone.Chudan)),
            new ExerciseDescriptor("yokogeri-jodan", "Yoko geri jodan", () => new YokoGeriAnalyzer(KickZone.Jodan)),
```

В `Assets/Pose/Level0Results.cs`:

1. Заменить строку `public int YokoGeriSlowReps;` на:

```csharp
        public int YokoGeriSlowReps;       // имя для совместимости сейвов: лучший сет повторов любого варианта yoko
```

2. После строки `public int MaeGeriBestZone; …` добавить:

```csharp
        public int YokoGeriBestZone;       // (int)KickZone, лучшая зона yoko geri
```

3. Заменить ветку `case YokoGeriAnalyzer y:` в `Absorb` на:

```csharp
                case YokoGeriAnalyzer y:
                    YokoGeriSlowReps = Math.Max(YokoGeriSlowReps, y.Reps);
                    YokoGeriHoldSeconds = Math.Max(YokoGeriHoldSeconds, (float)y.TotalLiftedSeconds);
                    YokoGeriBestZone = Math.Max(YokoGeriBestZone, (int)y.BestZone);
                    break;
```

В `Assets/Pose/StatCalculator.cs` заменить тело `Compute` начиная со строки `int zone = …` и до `return new PlayerStats(…);` включительно на:

```csharp
            // Гибкость — среднее переднего (mae) и бокового (yoko) удара: это разные
            // растяжки, один вид удара не даёт 100.
            float flexibility = (FlexibilityByZone[ClampZone(r.MaeGeriBestZone)]
                               + FlexibilityByZone[ClampZone(r.YokoGeriBestZone)]) / 2f;
            float balance = Ramp(r.YokoGeriSlowReps, SlowRepsFor70) * 70f
                          + Ramp(r.YokoGeriHoldSeconds, HoldSecondsFor30) * 30f;

            return new PlayerStats(
                (int)Math.Round(strength),
                (int)Math.Round(endurance),
                (int)Math.Round(flexibility),
                (int)Math.Round(balance));
```

и добавить после метода `Compute` (перед `Ramp`):

```csharp
        private static int ClampZone(int zone) =>
            Math.Min(Math.Max(zone, 0), FlexibilityByZone.Length - 1);
```

(Строки `float strength = …` и `float endurance = …` не меняются.)

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`, все зелёные (ориентир — 89 тестов: 85 было − 5 старых yoko + 8 новых + 1 гибкость). Корпусы пуш-апа/приседа/wall-sit и тесты mae geri — без изменений ассертов.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/YokoGeriAnalyzer.cs Assets/Pose/ExerciseCatalog.cs Assets/Pose/Level0Results.cs Assets/Pose/StatCalculator.cs Assets/Pose/Tests/LegTestFrames.cs Assets/Pose/Tests/YokoGeriAnalyzerTests.cs Assets/Pose/Tests/StatCalculatorTests.cs
git commit -m @'
feat: yoko geri по трём высотам — зоны, выпрямленная нога, гибкость из двух ударов

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: Подсказка yoko geri + пересборка и установка

**Files:**
- Modify: `Assets/Pose/DevSandbox/ExerciseSandbox.cs` (метод `DrawLive`, сразу после блока подсказки wall-sit ~строка 127)

**Interfaces:**
- Consumes: `IExerciseAnalyzer.Id` (`"yokogeri-gedan"` / `"yokogeri-chudan"` / `"yokogeri-jodan"` после Task 1).

- [ ] **Step 1: Подсказка**

В `DrawLive`, сразу после

```csharp
            if (a.Id == "wallsit")
                GUILayout.Label("Спиной к стене, бёдра параллельно полу", _mid);
```

добавить:

```csharp
            if (a.Id.StartsWith("yokogeri"))
                GUILayout.Label("Лицом к камере, можно держаться за стену", _mid);
```

- [ ] **Step 2: Прогнать тесты (компиляция + регрессия).** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 3: Commit**

```powershell
git add Assets/Pose/DevSandbox/ExerciseSandbox.cs
git commit -m @'
feat: подсказка yoko geri — лицом к камере, можно держаться за стену

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

- [ ] **Step 4: Сборка** (Editor закрыт)

```powershell
unity build "C:\Users\user\Mikey" --target Android --execute-method Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid --no-banner; "exit=$LASTEXITCODE"
```

Expected: `exit=0`, свежий mtime `Builds/ExerciseSandbox.apk` (несвежий — не устанавливать, эскалировать).

- [ ] **Step 5: Установка**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\user\Mikey\Builds\ExerciseSandbox.apk"
```

Expected: `Success`.

- [ ] **Step 6: Пользовательская проверка** — по фото-эталону, лицом к камере, можно держаться за стену: пенок вниз считается в «Yoko geri gedan» (и в чудан/дзёдан при ударе выше), пенок в середину — в chudan, пенок в горло — в jodan; удар ниже заданного — «Выше» и no-rep; подъём колена без выпрямления — «Выпрями ногу» и no-rep.
