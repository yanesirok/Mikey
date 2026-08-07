# Yoko geri v4: обязательный замах — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Удар yoko geri считается только после замаха: в цикле подъёма сначала кадр с согнутым коленом (≤ 110°), потом выпрямление на высоте; мах прямой ногой без замаха — no-rep «Сначала колено».

**Architecture:** Поверх правил v3 (сырой гейт, fast 1.2 / band 0.45×2) добавляется флаг `_chambered`: зонные ветки исполняются только после кадра с 2D-углом колена ≤ `chamberMaxKneeDeg` (110°). Cue-приоритет на отказе: нет замаха → «Сначала колено», пик пуст → «Выпрями ногу», иначе «Выше». Корпус смешанной сессии перезакрепляется 5/9/Gedan.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-08-yokogeri-chamber-required-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; вывод НЕ в `Temp/` проекта):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Logs\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Если корпуса дают НЕ (4/3/Chudan размеченный, 5/9/Gedan смешанный, 0 ходьба) — статус BLOCKED с фактическими числами, ожидания не подгонять.
- Пороги — ровно эти, не «улучшать»: новый `chamberMaxKneeDeg = 110f`; существующие `fastKickAt = 1.2f`, `kickBandAt = 0.45f`, `minBandFrames = 2`, `minExtensionDeg = 150f`, `minVisibility = 0.6f`, `smoothingAlpha = 0.6f` не трогать. Cue-строки точные: «Сначала колено», «Выпрями ногу», «Выше», «В кадр (лицом)».
- В `YokoGeriRecordingTests` меняются ТОЛЬКО три числа ассертов `MixedSession…` (7/7/Chudan → 5/9/Gedan) — требование пользователя «замах обязателен»; `GedanSession…` (4/3/Chudan) и `WalkingRecording…` (0) не трогать.
- Корпусы пуш-апа (5/4/0), приседа (18/15/1), wall-sit (6/0) и все прочие тесты — зелёные без правок их ассертов.
- Новых файлов в `Recordings/` нет. Посторонние изменённые файлы (арена, ProjectSettings) не трогать.
- Если код и тесты брифа противоречат друг другу — статус BLOCKED, не подгонять одно под другое.
- Если тест-раннер завис: `Get-Process Unity*`, убить, повторить один раз; снова завис — BLOCKED. Сборка без зелёных тестов запрещена.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Обязательный замах + корпуса + пересборка

**Files:**
- Modify: `Assets/Pose/YokoGeriAnalyzer.cs` (полная замена содержимого)
- Modify: `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` (полная замена содержимого: 11 тестов)
- Modify: `Assets/Pose/Tests/YokoGeriRecordingTests.cs` (три числа в `MixedSession…`)

**Interfaces:**
- Consumes (всё уже в кодовой базе): `LegTestFrames.Kick` / `ChamberHigh`; `KickHeightZone.Classify`; `LegLiftCycle`; `PoseMath.AngleDeg`; `CsvPoseFrames.Load`.
- Produces: `YokoGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f, float minExtensionDeg = 150f, float smoothingAlpha = 0.6f, float fastKickAt = 1.2f, float kickBandAt = 0.45f, int minBandFrames = 2, float chamberMaxKneeDeg = 110f)` — публичные свойства без изменений.

- [ ] **Step 1: Тесты (красная фаза)**

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

        // Замах теперь обязателен, поэтому зонные тесты бьют по схеме
        // Floor -> ChamberHigh (замах) -> удар -> Floor.

        [Test]
        public void KickToRequestedZoneCountsAtAnyTempo()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            Feed(a, Floor, 0.0);
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));
            Feed(a, ChudanY, 0.6);
            Feed(a, Floor, 0.9);       // быстрый удар — темп свободный
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void KickAboveRequestedZoneCounts()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0);
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));
            Feed(a, JodanY, 0.6);
            Feed(a, Floor, 0.9);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(KickZone.Jodan, a.BestZone);
        }

        [Test]
        public void KickBelowRequestedZoneIsNoRepWithHigherCue()
        {
            var a = NewAnalyzer(KickZone.Jodan);
            Feed(a, Floor, 0.0);
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));
            Feed(a, GedanY, 0.6);
            Feed(a, Floor, 0.9);
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
        public void ExtensionOnlyOnDescentDoesNotAward()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            a.ProcessFrame(LegTestFrames.Kick(Floor, chambered: false, 1f, 0.0));
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));   // вход в цикл, колено согнуто
            Feed(a, 0.78f, 0.6);      // нога прямая, но подъём 0.6 — уже опускается
            Feed(a, Floor, 0.9);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Выпрями ногу", a.Cue);
        }

        [Test]
        public void SlowLowKickCountsThroughBand()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            a.ProcessFrame(LegTestFrames.Kick(Floor, chambered: false, 1f, 0.0));
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.3));   // вход в цикл, колено согнуто
            Feed(a, 0.78f, 0.6);      // прямая нога в рабочей полосе (подъём 0.6)...
            Feed(a, 0.78f, 0.9);      // ...двумя кадрами — сигнатура медленного удара
            Feed(a, Floor, 1.2);
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(0, a.NoReps);
            Assert.AreEqual(ExerciseFormState.GoodForm, a.FormState);
        }

        [Test]
        public void StraightSwingWithoutChamberIsNoRep()
        {
            var a = NewAnalyzer(KickZone.Gedan);
            Feed(a, Floor, 0.0);
            Feed(a, JodanY, 0.3);     // прямая нога сразу — замаха не было
            Feed(a, Floor, 0.6);
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(1, a.NoReps);
            Assert.AreEqual("Сначала колено", a.Cue);
            Assert.AreEqual(KickZone.None, a.BestZone);
        }

        [Test]
        public void AirtimeAccumulatesForCountedReps()
        {
            var a = NewAnalyzer(KickZone.Chudan);
            Feed(a, Floor, 0.0);
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 1.0));
            Feed(a, ChudanY, 2.0);
            Feed(a, ChudanY, 3.0);
            Feed(a, Floor, 3.5);       // в воздухе 2.5 c (с входа в цикл на замахе)
            Assert.AreEqual(1, a.Reps);
            Assert.AreEqual(2.5, a.TotalLiftedSeconds, 1e-6);
        }

        [Test]
        public void FailedKickAddsNoAirtime()
        {
            var a = NewAnalyzer(KickZone.Jodan);
            Feed(a, Floor, 0.0);
            a.ProcessFrame(LegTestFrames.ChamberHigh(timestamp: 0.5));
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

В `Assets/Pose/Tests/YokoGeriRecordingTests.cs` в тесте `MixedSession_CountsKicksRejectsRaisesAndSwings` заменить три ассерта:

```csharp
            Assert.AreEqual(7, analyzer.Reps);
            Assert.AreEqual(7, analyzer.NoReps);
            Assert.AreEqual(KickZone.Chudan, analyzer.BestZone);
```

на

```csharp
            Assert.AreEqual(5, analyzer.Reps);
            Assert.AreEqual(9, analyzer.NoReps);
            Assert.AreEqual(KickZone.Gedan, analyzer.BestZone);   // махи прямой ногой без замаха не в зачёте
```

(`GedanSession…` и `WalkingRecording…` не трогать.)

- [ ] **Step 2: Прогнать — красная фаза.** Run: команда тестов. Expected: exit=6, ровно два падения: `MixedSession_CountsKicksRejectsRaisesAndSwings` (Reps 7 ≠ 5 — текущий код считает махи без замаха) и `StraightSwingWithoutChamberIsNoRep` (Reps 1 ≠ 0). Обновлённые зонные тесты с кадром замаха зелёные уже на текущем коде (замах ему не мешает). Другие падения или числа → BLOCKED с фактами.

- [ ] **Step 3: Реализация**

Полностью заменить содержимое `Assets/Pose/YokoGeriAnalyzer.cs`:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores yoko geri (side kick) at a requested height facing the camera — the leg
    /// travels sideways, so a profile view would hide its height. Same lift signal and
    /// <see cref="LegLiftCycle"/> as mae geri. A valid kick is CHAMBER THEN EXTENSION:
    /// the cycle must first show a bent knee (in-plane angle ≤ chamberMaxKneeDeg), and
    /// only after that do extended frames (angle ≥ minExtensionDeg) score a height zone,
    /// gated by the RAW lift of the frame (the smoothed value lags and would leak
    /// descent frames): a single extended frame at ≥ fastKickAt scores immediately
    /// (fast kicks live for one frame; a dropping leg never extends that high), while
    /// frames in the working band ≥ kickBandAt score only when the cycle holds
    /// ≥ minBandFrames of them — a controlled kick keeps the leg extended at height, a
    /// pendulum drop passes through in one frame. Lenient policy: reaching the requested
    /// zone OR higher counts. Cues on a failed cycle: no chamber → "Сначала колено";
    /// chambered but nothing extended at height → "Выпрями ногу"; below the requested
    /// zone → "Выше". <see cref="BestZone"/> keeps the highest zone this set
    /// (flexibility stat); <see cref="TotalLiftedSeconds"/> accumulates airtime of
    /// counted reps (balance stat). Holding a wall for support is allowed and not
    /// checked. Engine-free.
    /// </summary>
    public sealed class YokoGeriAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр (лицом)";

        private readonly KickZone _requested;
        private readonly LegLiftCycle _cycle;
        private readonly float _minVisibility;
        private readonly float _minExtensionDeg;
        private readonly float _smoothingAlpha;
        private readonly float _fastKickAt;
        private readonly float _kickBandAt;
        private readonly int _minBandFrames;
        private readonly float _chamberMaxKneeDeg;

        private float _smoothedLift = float.NaN;
        private bool _chambered;
        private KickZone _fastPeak = KickZone.None;
        private KickZone _bandPeak = KickZone.None;
        private int _bandFrames;
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
            $"phase {_cycle.Phase}  chamber {(_chambered ? "Y" : "n")}  fast {_fastPeak}  band {_bandPeak}x{_bandFrames}  " +
            $"knee {(float.IsNaN(_lastKneeDeg) ? "--" : _lastKneeDeg.ToString("0"))}°  " +
            $"total {TotalLiftedSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public YokoGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f,
            float minExtensionDeg = 150f, float smoothingAlpha = 0.6f,
            float fastKickAt = 1.2f, float kickBandAt = 0.45f, int minBandFrames = 2,
            float chamberMaxKneeDeg = 110f)
        {
            if (requested == KickZone.None)
                throw new ArgumentOutOfRangeException(nameof(requested));
            _requested = requested;
            _cycle = cycle ?? new LegLiftCycle(liftedAt: 1.0f, groundedAt: 0.25f, minLiftSeconds: 0.2);
            _minVisibility = minVisibility;
            _minExtensionDeg = minExtensionDeg;
            _smoothingAlpha = smoothingAlpha;
            _fastKickAt = fastKickAt;
            _kickBandAt = kickBandAt;
            _minBandFrames = minBandFrames;
            _chamberMaxKneeDeg = chamberMaxKneeDeg;
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
                    _chambered = false;
                    _fastPeak = KickZone.None;
                    _bandPeak = KickZone.None;
                    _bandFrames = 0;
                    Cue = string.Empty;
                }

                PoseLandmark ankle = frame.Get(kickLeft ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
                PoseLandmark knee = frame.Get(kickLeft ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
                PoseLandmark hip = frame.Get(kickLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
                PoseLandmark shoulder = frame.Get(kickLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);

                _lastKneeDeg = PoseMath.AngleDeg(hip, knee, ankle);
                if (_lastKneeDeg <= _chamberMaxKneeDeg)
                    _chambered = true;

                // Замах обязателен (порядок: согнутое колено -> выпрямление), гейт — по
                // сырому подъёму кадра: сглаженный отстаёт и протаскивает опускания.
                // Опускающаяся нога-маятник не выпрямляется выше ~1.0 и проносится через
                // полосу за один кадр — сигнатуре удара не отвечает.
                if (_chambered && _lastKneeDeg >= _minExtensionDeg)
                {
                    KickZone zone = KickHeightZone.Classify(ankle.Y, hip.Y, shoulder.Y);
                    if (lift >= _fastKickAt && zone > _fastPeak)
                        _fastPeak = zone;
                    if (lift >= _kickBandAt)
                    {
                        _bandFrames++;
                        if (zone > _bandPeak)
                            _bandPeak = zone;
                    }
                }
            }

            if (completed)
            {
                KickZone peak = _fastPeak;
                if (_bandFrames >= _minBandFrames && _bandPeak > peak)
                    peak = _bandPeak;

                if (peak > BestZone)
                    BestZone = peak;

                if (peak >= _requested)
                {
                    Reps++;
                    TotalLiftedSeconds += _cycle.LiftedSeconds;
                }
                else
                {
                    NoReps++;
                    Cue = !_chambered ? "Сначала колено"
                        : peak == KickZone.None ? "Выпрями ногу"
                        : "Выше";
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
            _chambered = false;
            _fastPeak = KickZone.None;
            _bandPeak = KickZone.None;
            _bandFrames = 0;
            _lastKneeDeg = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
```

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`, все зелёные (ориентир — 95 тестов: 94 + 1 новый). Корпуса: размеченный 4/3/Chudan, смешанный 5/9/Gedan, ходьба 0. Числа НЕ совпали → BLOCKED с фактическими значениями.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/YokoGeriAnalyzer.cs Assets/Pose/Tests/YokoGeriAnalyzerTests.cs Assets/Pose/Tests/YokoGeriRecordingTests.cs
git commit -m @'
feat: yoko geri v4 — обязательный замах: сначала колено, потом выпрямление

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

- [ ] **Step 6: Сборка** (Editor закрыт)

```powershell
unity build "C:\Users\user\Mikey" --target Android --execute-method Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid --no-banner; "exit=$LASTEXITCODE"
```

Expected: `exit=0`, свежий mtime `Builds/ExerciseSandbox.apk` (несвежий — не устанавливать, эскалировать).

- [ ] **Step 7: Установка**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\user\Mikey\Builds\ExerciseSandbox.apk"
```

Expected: `Success`.

- [ ] **Step 8: Пользовательская проверка** — удар с замахом (колено согнуто вверх, как на фото, затем выпрямление) считается; мах прямой ногой — «Сначала колено»; подъём колена без выпрямления — «Выпрями ногу»; опускание — не считается.
