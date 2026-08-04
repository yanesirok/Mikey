# Wall-sit v2: маржа таза — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wall-sit засчитывает правильную посадку у стены (сбоку и анфас), не считает сидение на полу и переходы, ходьба не даёт секунд.

**Architecture:** `WallSitAnalyzer` меняет сигнал с шумных 3D-углов колена/бедра на маржу таза `(knee.Y − hip.Y)/голень` — ту же, что вылечила присед v2. Формула переезжает в `PoseMath.HipDropMargin`, `SquatAnalyzer` делегирует ей. «В позе» = max маржа по видимым ногам в окне [−0.45 … +0.5] + наклон торса ≤ 40° (прокси стены). `HoldTimer` и `Reps = (int)BestSeconds` не меняются.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-05-wallsit-margin-hold-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; при падениях смотреть `Temp/pose_tests.xml`):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Temp\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Если корпус wall-sit даёт НЕ 6 (сессия) / НЕ 0 (ходьба) — статус BLOCKED с фактическими числами, ожидание не подгонять (расхождение C# с эталонным Python-реплеем разбирает контролёр).
- Исходник записи: `C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_031226.csv`.
- Пороги: окно сидения `seatLowAt = -0.45f` / `seatHighAt = 0.5f`, наклон `maxTorsoLeanDeg = 40f`, видимость `minVisibility = 0.5f`, грейс `HoldTimer` 1.0 c — ровно эти значения, не «улучшать».
- Корпусы пуш-апа (5/4/0) и приседа (18/15/1), все остальные тесты — зелёные без правок их ассертов.
- Новые файлы получат `.meta` при прогоне — добавлять в коммит; для файлов в `Assets/Pose/Tests/Recordings/` использовать `git add -f` (правило gitignore `[Rr]ecordings/`). Посторонние изменённые файлы (арена, ProjectSettings) не трогать.
- Если код и тесты брифа противоречат друг другу — статус BLOCKED, не подгонять одно под другое.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: WallSitAnalyzer на марже таза + корпус

**Files:**
- Modify: `Assets/Pose/PoseMath.cs` (добавить метод в конец класса, после `HipVerticalOffset`)
- Modify: `Assets/Pose/SquatAnalyzer.cs:122-131` (приватный `Margin` делегирует `PoseMath`)
- Modify: `Assets/Pose/WallSitAnalyzer.cs` (полная замена содержимого класса)
- Modify: `Assets/Pose/Tests/WallSitAnalyzerTests.cs` (два новых теста; существующие шесть НЕ менять)
- Create: `Assets/Pose/Tests/Recordings/wallsit_session.csv` (копия исходника из Global Constraints)
- Test: `Assets/Pose/Tests/WallSitRecordingTests.cs` (новый)

**Interfaces:**
- Consumes: `HoldTimer(double graceSeconds = 1.0)` с `Update(bool, double)`, `CurrentSeconds`, `BestSeconds`, `Reset()`; `PoseFrame.MinVisibility(params PoseLandmarkType[])`; `PoseFrame.Get(PoseLandmarkType)`; `CsvPoseFrames.Load(string)`; `LegTestFrames.WallSit(float kneeAngleDeg = 90f, float hipAngleDeg = 90f, float visibility = 1f, double timestamp = 0)` — всё уже в кодовой базе.
- Produces: `PoseMath.HipDropMargin(PoseLandmark hip, PoseLandmark knee, PoseLandmark ankle)` → `float` (NaN при вырожденной голени); `WallSitAnalyzer(HoldTimer timer = null, float minVisibility = 0.5f, float seatLowAt = -0.45f, float seatHighAt = 0.5f, float maxTorsoLeanDeg = 40f)` — публичные свойства (`Reps`, `BestHoldSeconds`, `CurrentHoldSeconds`, `Cue`, `FormState`, `DebugInfo`) сохраняют имена.

- [ ] **Step 1: Корпус и новые тесты (красная фаза)**

1. Скопировать запись:

```powershell
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_031226.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\wallsit_session.csv"
```

2. Создать `Assets/Pose/Tests/WallSitRecordingTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Characterization corpus for the wall-sit hold: a real on-device session
    /// (2026-08-05, ground truth "several 5–10 s holds against the wall") plus the
    /// walking recording as a negative control. Guards the margin-window scoring
    /// against regressions, nothing more.
    /// </summary>
    public class WallSitRecordingTests
    {
        [Test]
        public void WallSitSession_BestHoldIsSixSeconds()
        {
            var analyzer = new WallSitAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/wallsit_session.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(6, analyzer.Reps);
        }

        [Test]
        public void WalkingRecording_AccumulatesNothing()
        {
            var analyzer = new WallSitAnalyzer();
            foreach (PoseFrame f in CsvPoseFrames.Load("Pose/Tests/Recordings/walking_noise.csv"))
                analyzer.ProcessFrame(f);
            Assert.AreEqual(0, analyzer.Reps);
        }
    }
}
```

3. В `Assets/Pose/Tests/WallSitAnalyzerTests.cs` добавить два теста в конец класса (существующие шесть тестов и хелпер `Sit` не трогать):

```csharp
        [Test]
        public void FloorSitDoesNotAccumulate()
        {
            var a = new WallSitAnalyzer();
            for (double t = 0.0; t <= 5.0 + 1e-9; t += 0.5)
                a.ProcessFrame(LegTestFrames.WallSit(kneeAngleDeg: 55f, timestamp: t));
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual("Выше", a.Cue);
        }

        [Test]
        public void HeavyLeanPausesTimerWithWallCue()
        {
            var a = new WallSitAnalyzer();
            for (double t = 0.0; t <= 5.0 + 1e-9; t += 0.5)
                a.ProcessFrame(LegTestFrames.WallSit(hipAngleDeg: 140f, timestamp: t));
            Assert.AreEqual(0, a.Reps);
            Assert.AreEqual(ExerciseFormState.BadForm, a.FormState);
            Assert.AreEqual("Спиной к стене", a.Cue);
        }
```

Геометрия билдера (для понимания, не менять): `WallSit(kneeAngleDeg)` двигает только высоту таза при вертикальной голени длиной 0.2 → маржа = 1.25·cos(180° − kneeAngle): 90° → 0.00, 150° → +1.08, 55° → −0.72. `hipAngleDeg: 140f` при колене 90° даёт наклон торса ≈ 50° от вертикали.

- [ ] **Step 2: Прогнать — красная фаза**

Run: команда тестов. Expected: `exit=6`, падают ровно три новых теста на старой угловой логике: `WallSitSession_BestHoldIsSixSeconds` (старая даёт 7, не 6), `WalkingRecording_AccumulatesNothing` (старая насчитывает на ходьбе 2, не 0), `HeavyLeanPausesTimerWithWallCue` (старая даёт cue «Ниже», не «Спиной к стене»). `FloorSitDoesNotAccumulate` зелёный уже на старой логике (регрессионный страж, обе конфигурации дают Reps 0 + «Выше»). Существующие шесть тестов — зелёные. Падает что-то сверх перечисленного — BLOCKED.

- [ ] **Step 3: Реализация**

1. `Assets/Pose/PoseMath.cs` — добавить в конец класса (после `HipVerticalOffset`):

```csharp
        /// <summary>
        /// How far the hip sits above the knee, in units of that leg's shank length
        /// (image space, Y grows down): ≈1 standing, ≈0 with the thigh at parallel,
        /// negative once the hip drops below the knee. Uses only Y coordinates, so it
        /// is view-independent (no depth). Returns NaN for a degenerate shank.
        /// </summary>
        public static float HipDropMargin(PoseLandmark hip, PoseLandmark knee, PoseLandmark ankle)
        {
            float shank = Math.Abs(ankle.Y - knee.Y);
            return shank < 1e-4f ? float.NaN : (knee.Y - hip.Y) / shank;
        }
```

2. `Assets/Pose/SquatAnalyzer.cs` — тело приватного `Margin` (строки ~122-131) заменить делегированием (комментарий над методом сохранить):

```csharp
        // Насколько таз выше колена, в долях длины голени этой ноги (image-space, Y вниз):
        // стоя ≈ 1, параллель ≈ 0, глубже — отрицательно.
        private static float Margin(PoseFrame frame, bool left)
        {
            return PoseMath.HipDropMargin(
                frame.Get(left ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip),
                frame.Get(left ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee),
                frame.Get(left ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle));
        }
```

3. `Assets/Pose/WallSitAnalyzer.cs` — заменить содержимое файла целиком:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores a wall-sit hold from the hip-over-knee margin (<see cref="PoseMath.HipDropMargin"/>)
    /// instead of noisy 3D joint angles, so both the side and the frontal view work. In pose =
    /// the MAX margin over the visible legs sits in [seatLowAt, seatHighAt]: ≈0 seated at
    /// parallel, ≈1 standing (above the window), and below the window the hips are a shin-length
    /// under the knees — sitting on the floor, which is not a wall-sit. Wall proxy: the torso
    /// must stay within <c>maxTorsoLeanDeg</c> of vertical (a back against the wall is upright),
    /// otherwise the timer pauses with a corrective cue. The result is the longest continuous
    /// hold (via <see cref="HoldTimer"/>, tracker blinks bridged), surfaced through
    /// <see cref="Reps"/> as whole seconds because the HUD contract has no time field.
    /// No <see cref="NoReps"/> for a hold. Engine-free.
    /// </summary>
    public sealed class WallSitAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр";

        private readonly HoldTimer _timer;
        private readonly float _minVisibility;
        private readonly float _seatLowAt;
        private readonly float _seatHighAt;
        private readonly float _maxTorsoLeanDeg;

        private float _lastSignal = float.NaN;
        private float _lastMarginLeft = float.NaN;
        private float _lastMarginRight = float.NaN;
        private float _lastLean = float.NaN;
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
            $"sig {(float.IsNaN(_lastSignal) ? "--" : _lastSignal.ToString("0.00"))}  " +
            $"L {(float.IsNaN(_lastMarginLeft) ? "--" : _lastMarginLeft.ToString("0.00"))}  " +
            $"R {(float.IsNaN(_lastMarginRight) ? "--" : _lastMarginRight.ToString("0.00"))}  " +
            $"lean {(float.IsNaN(_lastLean) ? "--" : _lastLean.ToString("0"))}°  " +
            $"hold {_timer.CurrentSeconds:0.0}s  best {_timer.BestSeconds:0.0}s  vis {_lastVis:0.00}";

        public event Action Changed;

        public WallSitAnalyzer(HoldTimer timer = null, float minVisibility = 0.5f,
            float seatLowAt = -0.45f, float seatHighAt = 0.5f, float maxTorsoLeanDeg = 40f)
        {
            _timer = timer ?? new HoldTimer(graceSeconds: 1.0);
            _minVisibility = minVisibility;
            _seatLowAt = seatLowAt;
            _seatHighAt = seatHighAt;
            _maxTorsoLeanDeg = maxTorsoLeanDeg;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle);
            float rightVis = frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle);
            _lastVis = Math.Max(leftVis, rightVis);

            _lastMarginLeft = leftVis >= _minVisibility
                ? PoseMath.HipDropMargin(frame.Get(PoseLandmarkType.LeftHip),
                    frame.Get(PoseLandmarkType.LeftKnee), frame.Get(PoseLandmarkType.LeftAnkle))
                : float.NaN;
            _lastMarginRight = rightVis >= _minVisibility
                ? PoseMath.HipDropMargin(frame.Get(PoseLandmarkType.RightHip),
                    frame.Get(PoseLandmarkType.RightKnee), frame.Get(PoseLandmarkType.RightAnkle))
                : float.NaN;

            bool anyLeg = !float.IsNaN(_lastMarginLeft) || !float.IsNaN(_lastMarginRight);
            if (!anyLeg)
            {
                // Не помечаем «не в позе»: HoldTimer сам сошьёт короткий провал грейсом.
                _lastSignal = float.NaN;
                _lastLean = float.NaN;
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            _lastSignal =
                float.IsNaN(_lastMarginLeft) ? _lastMarginRight
                : float.IsNaN(_lastMarginRight) ? _lastMarginLeft
                : Math.Max(_lastMarginLeft, _lastMarginRight);

            // Наклон торса от вертикали — прокси стены; плечо со стороны более видимой ноги,
            // невидимое плечо (vis < порога) проверку пропускает, а не блокирует.
            bool leanLeft = leftVis >= rightVis;
            PoseLandmark shoulder = frame.Get(leanLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(leanLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            _lastLean = shoulder.Visibility >= _minVisibility
                ? (float)(Math.Atan2(Math.Abs(shoulder.X - hip.X), Math.Max(1e-6f, hip.Y - shoulder.Y)) * 180.0 / Math.PI)
                : float.NaN;

            bool seated = _lastSignal >= _seatLowAt && _lastSignal <= _seatHighAt;
            bool leanOk = float.IsNaN(_lastLean) || _lastLean <= _maxTorsoLeanDeg;
            bool inPose = seated && leanOk;
            _timer.Update(inPose, frame.TimestampSeconds);

            if (inPose)
            {
                FormState = ExerciseFormState.GoodForm;
                Cue = string.Empty;
            }
            else
            {
                FormState = ExerciseFormState.BadForm;
                Cue = _lastSignal > _seatHighAt ? "Ниже"
                    : _lastSignal < _seatLowAt ? "Выше"
                    : "Спиной к стене";
            }

            Changed?.Invoke();
        }

        public void Reset()
        {
            _timer.Reset();
            _lastSignal = float.NaN;
            _lastMarginLeft = float.NaN;
            _lastMarginRight = float.NaN;
            _lastLean = float.NaN;
            _lastVis = 0f;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
```

- [ ] **Step 4: Прогнать — зелёные.** Run: команда тестов. Expected: `exit=0`; корпус wall-sit = 6 (сессия) и 0 (ходьба); существующие шесть `WallSitAnalyzerTests` зелёные без правок; корпусы пуш-апа (5/4/0) и приседа (18/15/1) без изменений. Числа НЕ совпали → BLOCKED с фактическими значениями.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/PoseMath.cs Assets/Pose/SquatAnalyzer.cs Assets/Pose/WallSitAnalyzer.cs Assets/Pose/Tests/WallSitAnalyzerTests.cs Assets/Pose/Tests/WallSitRecordingTests.cs
git add -f Assets/Pose/Tests/WallSitRecordingTests.cs.meta Assets/Pose/Tests/Recordings/wallsit_session.csv Assets/Pose/Tests/Recordings/wallsit_session.csv.meta
git commit -m @'
feat: wall-sit на марже таза — окно сидения, отсечка пола, прокси стены

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

(Если `.meta` для новых файлов ещё не созданы — они появятся после прогона тестов на Step 4; добавить их этим же коммитом.)

---

### Task 2: Подсказка про стену + пересборка и установка

**Files:**
- Modify: `Assets/Pose/DevSandbox/ExerciseSandbox.cs` (метод `DrawLive`, после строки `GUILayout.Label(a.DisplayName.ToUpperInvariant(), _mid);` ~строка 125)

**Interfaces:**
- Consumes: `IExerciseAnalyzer.Id` (`"wallsit"` у `WallSitAnalyzer`) — уже в кодовой базе.

- [ ] **Step 1: Подсказка**

В `DrawLive`, сразу после `GUILayout.Label(a.DisplayName.ToUpperInvariant(), _mid);` добавить:

```csharp
            if (a.Id == "wallsit")
                GUILayout.Label("Спиной к стене, бёдра параллельно полу", _mid);
```

- [ ] **Step 2: Прогнать тесты (компиляция + регрессия).** Run: команда тестов. Expected: `exit=0`.

- [ ] **Step 3: Commit**

```powershell
git add Assets/Pose/DevSandbox/ExerciseSandbox.cs
git commit -m @'
feat: подсказка wall-sit — спиной к стене, бёдра параллельно полу

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

- [ ] **Step 6: Пользовательская проверка** — сесть у стены как в эталонном видео → секунды идут непрерывно (сбоку и анфас); встать — пауза, лучший результат сохраняется; сесть на пол — секунды не идут.
