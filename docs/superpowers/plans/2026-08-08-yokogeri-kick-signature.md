# Yoko geri v3: сигнатура удара — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Yoko geri перестаёт засчитывать подъёмы колена, махи и опускания: зона идёт по сырому подъёму через быстрый путь (один выпрямленный кадр высоко) или медленный путь (≥ 2 выпрямленных кадров в рабочей полосе).

**Architecture:** `YokoGeriAnalyzer` меняет гейт зоны со сглаженного подъёма (лаг протаскивал опускания) на сырой подъём кадра с двумя путями: fastKickAt 1.2 (одного кадра достаточно) и kickBandAt 0.45 при minBandFrames 2 (сигнатура контролируемого удара). `LegLiftCycle.LiftedAt` больше не используется — удаляется. Три корпуса: размеченная сессия 4/3/Chudan (ассерты не меняются), смешанная 7/7/Chudan (новая), ходьба 0.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-08-yokogeri-kick-signature-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; выход НЕ писать в `Temp/` проекта — Unity чистит его на выходе):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Logs\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Исходник записи для нового корпуса: `C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_015348.csv`.
- Если корпуса дают НЕ (4/3/Chudan размеченный, 7/7/Chudan смешанный, 0 ходьба) — статус BLOCKED с фактическими числами, ожидания не подгонять.
- Пороги — ровно эти значения, не «улучшать»: `fastKickAt = 1.2f`, `kickBandAt = 0.45f`, `minBandFrames = 2`; существующие `minVisibility = 0.6f`, `minExtensionDeg = 150f`, `smoothingAlpha = 0.6f`, `LegLiftCycle(1.0f, 0.25f, 0.2)` не трогать.
- Корпусы пуш-апа (5/4/0), приседа (18/15/1), wall-sit (6/0) и все существующие тесты — зелёные без правок их ассертов (включая `GedanSession_CountsKicksNotKneeRaises` — его числа 4/3/Chudan сохраняются новым правилом).
- Файл в `Assets/Pose/Tests/Recordings/` добавлять через `git add -f`; новые `.meta` — в тот же коммит. Посторонние изменённые файлы (арена, ProjectSettings) не трогать.
- Если код и тесты брифа противоречат друг другу — статус BLOCKED, не подгонять одно под другое.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Сигнатура удара + корпуса + пересборка

**Files:**
- Modify: `Assets/Pose/YokoGeriAnalyzer.cs` (полная замена содержимого)
- Modify: `Assets/Pose/LegLiftCycle.cs` (удалить свойство `LiftedAt` с его doc-комментарием)
- Modify: `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` (один новый тест; существующие девять НЕ менять)
- Modify: `Assets/Pose/Tests/YokoGeriRecordingTests.cs` (два новых теста; существующий НЕ менять)
- Create: `Assets/Pose/Tests/Recordings/yoko_gedan_mixed.csv` (копия исходника из Global Constraints)

**Interfaces:**
- Consumes (всё уже в кодовой базе): `CsvPoseFrames.Load(string)`; `LegTestFrames.Kick` / `ChamberHigh`; `KickHeightZone.Classify`; `LegLiftCycle` (`Phase`, `LiftedSeconds`, `Update`, `Reset`); `PoseMath.AngleDeg`.
- Produces: `YokoGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f, float minExtensionDeg = 150f, float smoothingAlpha = 0.6f, float fastKickAt = 1.2f, float kickBandAt = 0.45f, int minBandFrames = 2)` — публичные свойства как раньше (`Id`, `DisplayName`, `Reps`, `NoReps`, `BestZone`, `TotalLiftedSeconds`, `Cue`, `FormState`, `DebugInfo`, `Changed`, `Reset`).

- [ ] **Step 1: Корпус и новые тесты (красная фаза)**

Скопировать запись (PowerShell):

```powershell
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_015348.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\yoko_gedan_mixed.csv"
```

В `Assets/Pose/Tests/YokoGeriRecordingTests.cs` добавить после `GedanSession_CountsKicksNotKneeRaises` (его НЕ менять):

```csharp
        [Test]
        public void MixedSession_CountsKicksRejectsRaisesAndSwings()
        {
            var analyzer = new YokoGeriAnalyzer(KickZone.Gedan);
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/yoko_gedan_mixed.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(7, analyzer.Reps);
            Assert.AreEqual(7, analyzer.NoReps);
            Assert.AreEqual(KickZone.Chudan, analyzer.BestZone);
        }

        [Test]
        public void WalkingRecording_CountsNothing()
        {
            var analyzer = new YokoGeriAnalyzer(KickZone.Gedan);
            foreach (PoseFrame f in CsvPoseFrames.Load("Pose/Tests/Recordings/walking_noise.csv"))
                analyzer.ProcessFrame(f);
            Assert.AreEqual(0, analyzer.Reps);
        }
```

В `Assets/Pose/Tests/YokoGeriAnalyzerTests.cs` добавить после `ExtensionOnlyOnDescentDoesNotAward`:

```csharp
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
```

- [ ] **Step 2: Прогнать — красная фаза.** Run: команда тестов. Expected: exit=6, ровно два падения: `MixedSession_CountsKicksRejectsRaisesAndSwings` (Reps 9 ≠ 7 — sm-гейт протаскивает опускания) и `SlowLowKickCountsThroughBand` (Reps 0 ≠ 1 — sm-гейт режет полосу). `GedanSession_CountsKicksNotKneeRaises` (4/3) и `WalkingRecording_CountsNothing` (0) зелёные уже на текущем коде. Другие падения или другие числа → BLOCKED с фактами.

- [ ] **Step 3: Реализация**

Полностью заменить содержимое `Assets/Pose/YokoGeriAnalyzer.cs`:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores yoko geri (side kick) at a requested height facing the camera — the leg
    /// travels sideways, so a profile view would hide its height. Same lift signal and
    /// <see cref="LegLiftCycle"/> as mae geri. The height zone is sampled only on frames
    /// where the leg is extended (in-plane knee angle ≥ minExtensionDeg — noisy z depth
    /// is not involved) and is gated by the RAW lift of that frame (the smoothed value
    /// lags and would leak descent frames): a single extended frame at ≥ fastKickAt
    /// scores immediately (fast kicks live for one frame; a dropping leg never extends
    /// that high), while frames in the working band ≥ kickBandAt score only when the
    /// cycle holds ≥ minBandFrames of them — a controlled kick keeps the leg extended
    /// at height, a pendulum drop passes through in one frame. Lenient policy: reaching
    /// the requested zone OR higher counts; below is a no-rep ("Выше"); a lift that
    /// never extends at height is a no-rep ("Выпрями ногу"). <see cref="BestZone"/>
    /// keeps the highest zone this set (flexibility stat); <see cref="TotalLiftedSeconds"/>
    /// accumulates airtime of counted reps (balance stat). Holding a wall for support
    /// is allowed and not checked. Engine-free.
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

        private float _smoothedLift = float.NaN;
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
            $"phase {_cycle.Phase}  fast {_fastPeak}  band {_bandPeak}x{_bandFrames}  " +
            $"knee {(float.IsNaN(_lastKneeDeg) ? "--" : _lastKneeDeg.ToString("0"))}°  " +
            $"total {TotalLiftedSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public YokoGeriAnalyzer(KickZone requested, LegLiftCycle cycle = null, float minVisibility = 0.6f,
            float minExtensionDeg = 150f, float smoothingAlpha = 0.6f,
            float fastKickAt = 1.2f, float kickBandAt = 0.45f, int minBandFrames = 2)
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
                // Гейт — по сырому подъёму кадра: сглаженный отстаёт и протаскивает
                // опускания. Опускающаяся нога-маятник не выпрямляется выше ~1.0 и
                // проносится через полосу за один кадр — сигнатуре удара не отвечает.
                if (_lastKneeDeg >= _minExtensionDeg)
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
                    Cue = peak == KickZone.None ? "Выпрями ногу" : "Выше";
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

В `Assets/Pose/LegLiftCycle.cs` удалить три строки (свойство больше никем не используется):

```csharp
        /// <summary>Lift threshold that starts a cycle; kick analyzers gate zone sampling on it.</summary>
        public float LiftedAt => _liftedAt;
```

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`, все зелёные (ориентир — 94 теста: 91 + смешанный корпус + ходьба + юнит полосы). Корпуса: размеченный 4/3/Chudan, смешанный 7/7/Chudan, ходьба 0. Числа НЕ совпали → BLOCKED с фактическими значениями.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/YokoGeriAnalyzer.cs Assets/Pose/LegLiftCycle.cs Assets/Pose/Tests/YokoGeriAnalyzerTests.cs Assets/Pose/Tests/YokoGeriRecordingTests.cs
git add -f Assets/Pose/Tests/Recordings/yoko_gedan_mixed.csv Assets/Pose/Tests/Recordings/yoko_gedan_mixed.csv.meta
git commit -m @'
fix: yoko geri v3 — сигнатура удара: сырой гейт, быстрый и медленный пути зоны

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

(Если `.meta` нового CSV ещё нет — он появится после прогона на Step 4; добавить тем же коммитом.)

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

- [ ] **Step 8: Пользовательская проверка** — гэдан-удары (медленные и быстрые) считаются; подъёмы колена, махи без выпрямления и просто опускание ноги — «Выпрями ногу», счёт стоит.
