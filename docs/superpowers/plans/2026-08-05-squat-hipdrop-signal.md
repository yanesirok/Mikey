# Присед v2: сигнал «таз над коленями» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Присед считается сбоку И анфас, ходьба не считается; корпус 18/15/1.

**Architecture:** `SquatAnalyzer` меняет сигнал: вместо 3D-угла колена — max по ногам `(knee.Y − hip.Y)/|ankle.Y − knee.Y|` («самая стоячая» нога; проваливается только когда сгибаются обе). `RepCounter` тот же (пороги standAt=0.7/deepAt=0.45, дебаунс 2), видимость по-ножно (0.5). Красная фаза — обновлённые корпус-ожидания.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI.

**Спека:** `docs/superpowers/specs/2026-08-05-squat-hipdrop-signal-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли; при падениях смотреть `Temp/pose_tests.xml`):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Temp\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Если корпус даёт НЕ 18/15/1 — BLOCKED с фактическими числами, не подгонять (расхождение с эталонным реплеем разбирает контролёр).
- Исходник новой записи: `C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_020718.csv`.
- `.meta` в `Recordings/` глушится гитигнором — новые меты добавлять через `git add -f` (образец: соседние CSV).
- Пуш-ап (корпус 5/4/0), RepCounter и остальные техники — без правок и зелёные.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Новый сигнал приседа + корпус 18/15/1

**Files:**
- Modify: `Assets/Pose/SquatAnalyzer.cs` (файл целиком заменяется версией ниже)
- Modify: `Assets/Pose/RepCounter.cs` (только докстрока класса)
- Modify: `Assets/Pose/Tests/LegTestFrames.cs` (добавить `Stride`)
- Modify: `Assets/Pose/Tests/SquatAnalyzerTests.cs` (добавить `WalkingStrideDoesNotCount`)
- Modify: `Assets/Pose/Tests/SquatRecordingTests.cs` (ожидания 18, +2 теста)
- Create: `Assets/Pose/Tests/Recordings/squats_side_and_front.csv` (копия исходника из Global Constraints)

**Interfaces:**
- Consumes: `RepCounter(upThresholdDeg, downThresholdDeg, minRepSeconds, downDebounceFrames)` + `ResetDownStreak()`; `CsvPoseFrames.Load`; `LegTestFrames.Squat/Blank`.
- Produces: `SquatAnalyzer(RepCounter counter = null, float minVisibility = 0.5f, float maxTorsoLeanDeg = 50f, float smoothingAlpha = 1f, float standAt = 0.7f, float deepAt = 0.45f)`; `LegTestFrames.Stride(float visibility = 1f, double timestamp = 0)`.

- [ ] **Step 1: Тесты и корпус (красная фаза)**

1. Скопировать запись:

```powershell
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_020718.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\squats_side_and_front.csv"
```

2. В `Assets/Pose/Tests/LegTestFrames.cs` добавить:

```csharp
        /// <summary>
        /// Walking stride: the right (support) leg stands straight, the left knee is lifted
        /// mid-swing. The squat signal (max leg margin) must stay "standing" on such frames —
        /// this is the signal's core anti-phantom property.
        /// </summary>
        public static PoseFrame Stride(float visibility = 1f, double timestamp = 0)
        {
            var lm = Blank(visibility);
            void Put(PoseLandmarkType t, float x, float y) => lm[(int)t] = new PoseLandmark(x, y, 0f, visibility);

            Put(PoseLandmarkType.RightAnkle, 0.55f, 0.9f);
            Put(PoseLandmarkType.RightKnee, 0.55f, 0.7f);
            Put(PoseLandmarkType.RightHip, 0.55f, 0.5f);
            Put(PoseLandmarkType.RightShoulder, 0.55f, 0.2f);
            Put(PoseLandmarkType.LeftHip, 0.55f, 0.5f);
            Put(PoseLandmarkType.LeftKnee, 0.45f, 0.55f);   // колено махом поднято к тазу
            Put(PoseLandmarkType.LeftAnkle, 0.45f, 0.9f);
            Put(PoseLandmarkType.LeftShoulder, 0.55f, 0.2f);
            return new PoseFrame(lm, timestamp);
        }
```

3. В `Assets/Pose/Tests/SquatAnalyzerTests.cs` добавить:

```csharp
        [Test]
        public void WalkingStrideDoesNotCount()
        {
            var a = NewAnalyzer();
            double t = 0;
            for (int i = 0; i < 6; i++)
            {
                a.ProcessFrame(LegTestFrames.Squat(175f, timestamp: t)); t += 0.4;
                a.ProcessFrame(LegTestFrames.Stride(timestamp: t)); t += 0.4;
            }
            Assert.AreEqual(0, a.Reps, "Шаг с прямой опорной ногой не должен считаться приседом.");
        }
```

Остальные тесты `SquatAnalyzerTests` НЕ менять — геометрия билдера переживает смену сигнала (175° → margin ≈ 1.24 «стоя»; 95° → 0.11 «глубоко»; 120° → 0.63 межзонье).

4. В `Assets/Pose/Tests/SquatRecordingTests.cs`: у `MixedSessionsRecording_CountsSixteen` сменить имя на `MixedSessionsRecording_CountsEighteen`, ожидание `16` → `18`, дописать в комментарий «(18 — сигнал «таз над коленями», v2)». Добавить два теста (в тот же класс, `Replay`-подобной обвязки в классе нет — писать по образцу существующего):

```csharp
        [Test]
        public void SideAndFrontRecording_CountsFifteen()
        {
            // Сессия 2026-08-05 02:07: ~8–10 приседаний сбоку + 3–4 анфас (граунд-трус
            // пользователя «~12–14»). 15 — характеризация сигнала v2: анфас ловится.
            var analyzer = new SquatAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/squats_side_and_front.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(15, analyzer.Reps);
        }

        [Test]
        public void WalkingRecording_CountsOneKneel()
        {
            // Запись «хожу/делаю другое»: единственный спорный случай — опускание на пол,
            // где обе ноги реально согнуты (геометрически это присед). Щедрый уровень 0
            // это терпит; тест документирует ровно 1, чтобы регресс в обе стороны был виден.
            var analyzer = new SquatAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load("Pose/Tests/Recordings/walking_noise.csv");
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            Assert.AreEqual(1, analyzer.Reps);
        }
```

- [ ] **Step 2: Прогнать — красная фаза**

Run: команда тестов. Expected: `exit=6`. Упали: `MixedSessionsRecording_CountsEighteen` (старый сигнал даёт 16) и `SideAndFrontRecording_CountsFifteen` (старый даёт 9). Зелёные уже сейчас: `WalkingStrideDoesNotCount` (старый сигнал тоже не считает эту синтетику) и `WalkingRecording_CountsOneKneel` (старый тоже даёт 1) — их красная ценность в защите от будущих регрессов сигнала v2. Зафиксировать фактические числа из xml.

- [ ] **Step 3: Реализация**

1. `Assets/Pose/SquatAnalyzer.cs` — заменить файл ЦЕЛИКОМ:

```csharp
using System;

namespace Mikey.Pose
{
    /// <summary>
    /// Scores squats from the hip-over-knee margin instead of the knee angle, so both the
    /// side and the frontal view work without MediaPipe's noisy depth. Per leg the margin is
    /// (knee.Y − hip.Y) / |ankle.Y − knee.Y| — ≈1 standing, ≈0 at parallel, negative deeper.
    /// The rep signal is the MAX margin over the visible legs (the "most standing" leg): it
    /// collapses only when BOTH legs bend, so a walking stride (straight support leg) never
    /// looks deep. The shared <see cref="RepCounter"/> runs on this dimensionless signal
    /// (stand ≥ standAt → deep ≤ deepAt, debounced) — the pair that fixed push-up recall.
    /// Lenient policy: a heavy torso lean at the bottom is tallied in <see cref="NoReps"/>
    /// but does not block the count. Engine-free.
    /// </summary>
    public sealed class SquatAnalyzer : IExerciseAnalyzer
    {
        private const string NotVisibleCue = "В кадр";

        private readonly RepCounter _counter;
        private readonly float _minVisibility;
        private readonly float _maxTorsoLeanDeg;
        private readonly float _smoothingAlpha;

        private float _smoothedSignal = float.NaN;
        private float _lastLean = float.NaN;
        private float _lastVis;
        private float _lastMarginLeft = float.NaN;
        private float _lastMarginRight = float.NaN;
        private bool _leanFaultThisRep;

        public string Id => "squat";
        public string DisplayName => "Squats";
        public int Reps { get; private set; }
        public int NoReps { get; private set; }
        public string Cue { get; private set; } = NotVisibleCue;
        public ExerciseFormState FormState { get; private set; } = ExerciseFormState.NotVisible;

        public string DebugInfo =>
            $"sig {(float.IsNaN(_smoothedSignal) ? "--" : _smoothedSignal.ToString("0.00"))}  " +
            $"L {(float.IsNaN(_lastMarginLeft) ? "--" : _lastMarginLeft.ToString("0.00"))}  " +
            $"R {(float.IsNaN(_lastMarginRight) ? "--" : _lastMarginRight.ToString("0.00"))}  " +
            $"lean {(float.IsNaN(_lastLean) ? "--" : _lastLean.ToString("0"))}°  " +
            $"phase {_counter.Phase}  vis {_lastVis:0.00}";

        public event Action Changed;

        public SquatAnalyzer(RepCounter counter = null, float minVisibility = 0.5f,
            float maxTorsoLeanDeg = 50f, float smoothingAlpha = 1f,
            float standAt = 0.7f, float deepAt = 0.45f)
        {
            _counter = counter ?? new RepCounter(upThresholdDeg: standAt, downThresholdDeg: deepAt,
                minRepSeconds: 0.3, downDebounceFrames: 2);
            _minVisibility = minVisibility;
            _maxTorsoLeanDeg = maxTorsoLeanDeg;
            _smoothingAlpha = smoothingAlpha;
        }

        public void ProcessFrame(PoseFrame frame)
        {
            if (frame == null)
                throw new ArgumentNullException(nameof(frame));

            float leftVis = frame.MinVisibility(PoseLandmarkType.LeftHip, PoseLandmarkType.LeftKnee, PoseLandmarkType.LeftAnkle);
            float rightVis = frame.MinVisibility(PoseLandmarkType.RightHip, PoseLandmarkType.RightKnee, PoseLandmarkType.RightAnkle);
            _lastVis = Math.Max(leftVis, rightVis);

            _lastMarginLeft = leftVis >= _minVisibility ? Margin(frame, left: true) : float.NaN;
            _lastMarginRight = rightVis >= _minVisibility ? Margin(frame, left: false) : float.NaN;

            bool anyLeg = !float.IsNaN(_lastMarginLeft) || !float.IsNaN(_lastMarginRight);
            if (!anyLeg)
            {
                _smoothedSignal = float.NaN;
                _counter.ResetDownStreak();
                FormState = ExerciseFormState.NotVisible;
                Cue = NotVisibleCue;
                Changed?.Invoke();
                return;
            }

            float signal =
                float.IsNaN(_lastMarginLeft) ? _lastMarginRight
                : float.IsNaN(_lastMarginRight) ? _lastMarginLeft
                : Math.Max(_lastMarginLeft, _lastMarginRight);

            _smoothedSignal = float.IsNaN(_smoothedSignal)
                ? signal
                : _smoothedSignal + _smoothingAlpha * (signal - _smoothedSignal);

            // Наклон корпуса — только метка формы; плечо со стороны более видимой ноги.
            bool leanLeft = leftVis >= rightVis;
            PoseLandmark shoulder = frame.Get(leanLeft ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(leanLeft ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            _lastLean = shoulder.Visibility >= _minVisibility
                ? (float)(Math.Atan2(Math.Abs(shoulder.X - hip.X), Math.Max(1e-6f, hip.Y - shoulder.Y)) * 180.0 / Math.PI)
                : float.NaN;

            RepPhase prevPhase = _counter.Phase;
            bool completed = _counter.Update(_smoothedSignal, frame.TimestampSeconds);

            if (prevPhase != RepPhase.Down && _counter.Phase == RepPhase.Down)
                _leanFaultThisRep = false;
            bool leanFault = _counter.Phase == RepPhase.Down && !float.IsNaN(_lastLean) && _lastLean > _maxTorsoLeanDeg;
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

        // Насколько таз выше колена, в долях длины голени этой ноги (image-space, Y вниз):
        // стоя ≈ 1, параллель ≈ 0, глубже — отрицательно.
        private static float Margin(PoseFrame frame, bool left)
        {
            PoseLandmark hip = frame.Get(left ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark knee = frame.Get(left ? PoseLandmarkType.LeftKnee : PoseLandmarkType.RightKnee);
            PoseLandmark ankle = frame.Get(left ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);
            float shank = Math.Abs(ankle.Y - knee.Y);
            return shank < 1e-4f ? float.NaN : (knee.Y - hip.Y) / shank;
        }

        public void Reset()
        {
            _counter.Reset();
            Reps = 0;
            NoReps = 0;
            _smoothedSignal = float.NaN;
            _lastLean = float.NaN;
            _lastVis = 0f;
            _lastMarginLeft = float.NaN;
            _lastMarginRight = float.NaN;
            _leanFaultThisRep = false;
            FormState = ExerciseFormState.NotVisible;
            Cue = NotVisibleCue;
            Changed?.Invoke();
        }
    }
}
```

2. `Assets/Pose/RepCounter.cs` — в докстроке класса фразу «a joint angle in degrees where large = top/rest» заменить на «a joint angle in degrees or a normalized height where large = top/rest» (счётчик безразмерный; больше ничего не менять).

- [ ] **Step 4: Прогнать — зелёные**

Run: команда тестов. Expected: `exit=0`; корпус приседаний 18/15/1; пуш-ап 5/4/0 и все прочие — нетронуты.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/SquatAnalyzer.cs Assets/Pose/RepCounter.cs Assets/Pose/Tests/LegTestFrames.cs Assets/Pose/Tests/SquatAnalyzerTests.cs Assets/Pose/Tests/SquatRecordingTests.cs Assets/Pose/Tests/Recordings/squats_side_and_front.csv
git add -f Assets/Pose/Tests/Recordings/squats_side_and_front.csv.meta
git commit -m "feat: присед по сигналу «таз над коленями» — анфас работает, ходьба не считается"
```

(Если `.meta` для CSV ещё не сгенерирован — он появится после прогона тестов на Step 4; добавить тогда.)

---

### Task 2: Пересборка и установка

**Files:** без изменений кода.

- [ ] **Step 1: Сборка** (Editor закрыт)

```powershell
unity build "C:\Users\user\Mikey" --target Android --execute-method Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid --no-banner; "exit=$LASTEXITCODE"
```

Expected: `exit=0`, свежий mtime `Builds/ExerciseSandbox.apk`.

- [ ] **Step 2: Установка**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\user\Mikey\Builds\ExerciseSandbox.apk"
```

Expected: `Success`.

- [ ] **Step 3: Пользовательская проверка** — приседания сбоку и анфас считаются; ходьба между подходами не двигает счёт.
