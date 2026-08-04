# Гейты пуш-апа по уликам — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Настоящие отжимания снова считаются, фантомы (ходьба, жесты, поза с точками за кадром) — нет; обе записи с устройства становятся вечным регрессионным тестом.

**Architecture:** Откат гейта наклона в `PushUpFormEvaluator`; вместо него гейт «все скорящие точки в кадре [0,1]» (кадровый, через существующий `BodyNotVisible`) и реп-уровневая проверка «в нижней фазе запястье ниже таза» в `PushUpAnalyzer` (паттерн `_formOkThisRep`). `FormAssessment` получает метрику `WristBelowHip`. Комбинация проверена реплеем записей: реальная → 2/2, ходьба → 0/11.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode, Unity CLI (`unity test` / `unity build`).

**Спека:** `docs/superpowers/specs/2026-08-04-pushup-evidence-gates-design.md`

## Global Constraints

- **Команда EditMode-тестов** (через Unity CLI; Editor с проектом ЗАКРЫТ; exit 0 = все прошли, exit 6 = есть упавшие; при падениях смотреть `Temp/pose_tests.xml`):

  ```powershell
  unity test "C:\Users\user\Mikey" --mode EditMode --filter "Mikey.Pose.Tests" --output "C:\Users\user\Mikey\Temp\pose_tests.xml" --timeout 900 --no-banner; "exit=$LASTEXITCODE"
  ```

- Пороги — параметры конструктора (`wristBelowHipMin = 0f`); границы кадра [0,1] — константа, не параметр.
- Если реализованный по плану код НЕ даёт на корпусе 2/0 — статус BLOCKED, не подгонять ни пороги, ни тесты.
- Исходные CSV лежат в `C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\` (`pose_rec_180434.csv`, `pose_rec_172053.csv`).
- Новые файлы получат `.meta` при прогоне тестов — добавлять в коммит; посторонние изменённые файлы (арена) не трогать.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

---

### Task 1: Откат наклона, гейт «в кадре», реп-проверка запястья

**Files:**
- Modify: `Assets/Pose/PushUpFormEvaluator.cs` (ctor ~61-85; `Evaluate` ~88-125; struct `FormAssessment` ~24-46)
- Modify: `Assets/Pose/PushUpAnalyzer.cs` (поля ~20-27; ctor ~52-57; `ProcessFrame` ~83-98; `Reset` ~103-115; `DebugInfo` ~37-40)
- Modify: `Assets/Pose/Tests/PoseTestFrames.cs` (метод `Build` — параметр `ankleX`)
- Test: `Assets/Pose/Tests/PushUpFormEvaluatorTests.cs`, `Assets/Pose/Tests/PushUpAnalyzerTests.cs`

**Interfaces:**
- Produces:
  - `FormAssessment` — новое поле `public readonly float WristBelowHip` (запас «запястье ниже таза» в долях длины корпуса; `float.NaN` в ранних `BodyNotVisible`-возвратах), ctor: `FormAssessment(PushUpFault fault, float elbowAngleDeg, float bodyAngleDeg, float wristBelowHip, string cue, float visibility)`.
  - `PushUpFormEvaluator(float minVisibility = 0.6f, float straightMinDeg = 160f, float positionMinDeg = 135f)` — параметр `maxTorsoTiltDeg` удалён.
  - `PushUpAnalyzer(RepCounter counter = null, PushUpFormEvaluator evaluator = null, float smoothingAlpha = 0.6f, float wristBelowHipMin = 0f)`.
  - `PoseTestFrames.Build(float elbowAngleDeg, float hipOffset = 0f, float visibility = 1f, double timestamp = 0, float ankleX = 0.8f)`.
- Consumes: Task 2 полагается на перечисленные сигнатуры и на дефолтные пороги (140/105/0.3, EMA 0.6, vis 0.6, position 135).

- [ ] **Step 1: Правки тестов (красная фаза)**

1. В `PushUpFormEvaluatorTests.cs` УДАЛИТЬ тест `StandingBody_IsNotAPushUpPosition`; ДОБАВИТЬ:

```csharp
        [Test]
        public void OutOfFrameAnkle_ReportsNotVisible()
        {
            var evaluator = new PushUpFormEvaluator();
            // Лодыжка «за кадром» (x > 1) с высокой visibility — как MediaPipe дорисовывает на устройстве.
            FormAssessment a = evaluator.Evaluate(PoseTestFrames.Build(170f, ankleX: 1.05f));
            Assert.AreEqual(PushUpFault.BodyNotVisible, a.Fault);
        }
```

2. В `PushUpAnalyzerTests.cs` УДАЛИТЬ тест `StandingArmSwing_WouldCountWithoutTheGate` (его роль возьмёт корпус из Task 2). Тест `StandingArmSwing_DoesNotCount` ОСТАВИТЬ без правок — после фикса он держится на реп-проверке запястья (стоя при согнутом локте запястье выше таза).

3. В `PoseTestFrames.cs` в сигнатуру `Build` добавить хвостовой параметр `float ankleX = 0.8f` и заменить строку `float ax = 0.8f, ay = sy;` на `float ax = ankleX, ay = sy;`.

- [ ] **Step 2: Прогнать — падают**

Run: команда тестов. Expected: `exit=6`; в `Temp/pose_tests.xml` упал ровно один тест — `OutOfFrameAnkle_ReportsNotVisible` (гейта «в кадре» ещё нет: лодыжка на x=1.05 даёт прямую линию тела и `Fault == None`). `StandingArmSwing_DoesNotCount` в красной фазе ЗЕЛЁНЫЙ — его пока держит старый гейт наклона; после Step 3 его будет держать реп-проверка запястья (переход без окна регрессии).

- [ ] **Step 3: Реализация**

`Assets/Pose/PushUpFormEvaluator.cs` — итоговое состояние ключевых мест:

Struct `FormAssessment`:

```csharp
    public readonly struct FormAssessment
    {
        public readonly PushUpFault Fault;
        public readonly float ElbowAngleDeg;
        public readonly float BodyAngleDeg;

        /// <summary>Насколько запястье ниже таза, в долях длины корпуса (плечо–таз).
        /// Положительно в упоре лёжа (ладони на полу), отрицательно у стоящего с согнутой
        /// рукой. NaN, когда тело не видно. Реп-проверку делает анализатор.</summary>
        public readonly float WristBelowHip;

        public readonly string Cue;
        public readonly float Visibility;

        public FormAssessment(PushUpFault fault, float elbowAngleDeg, float bodyAngleDeg, float wristBelowHip, string cue, float visibility)
        {
            Fault = fault;
            ElbowAngleDeg = elbowAngleDeg;
            BodyAngleDeg = bodyAngleDeg;
            WristBelowHip = wristBelowHip;
            Cue = cue;
            Visibility = visibility;
        }

        public bool BodyVisible => Fault != PushUpFault.BodyNotVisible;
        public bool PostureValid => Fault == PushUpFault.None || Fault == PushUpFault.NotStraight;
    }
```

Конструктор оценщика — вернуть к трём параметрам (удалить `maxTorsoTiltDeg` и `_maxTorsoTiltDeg`):

```csharp
        public PushUpFormEvaluator(float minVisibility = 0.6f, float straightMinDeg = 160f, float positionMinDeg = 135f)
        {
            _minVisibility = minVisibility;
            _straightMinDeg = straightMinDeg;
            _positionMinDeg = positionMinDeg;
        }
```

`Evaluate` — после гейта видимости собрать все шесть точек, затем гейт «в кадре», затем углы и метрика запястья; блок tilt УДАЛИТЬ:

```csharp
            if (armVis < _minVisibility || bodyVis < _minVisibility)
                return new FormAssessment(PushUpFault.BodyNotVisible, float.NaN, float.NaN, float.NaN, "В кадр", vis);

            PoseLandmark shoulderA = frame.Get(useLeftArm ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark elbow = frame.Get(useLeftArm ? PoseLandmarkType.LeftElbow : PoseLandmarkType.RightElbow);
            PoseLandmark wrist = frame.Get(useLeftArm ? PoseLandmarkType.LeftWrist : PoseLandmarkType.RightWrist);
            PoseLandmark shoulderB = frame.Get(useLeftBody ? PoseLandmarkType.LeftShoulder : PoseLandmarkType.RightShoulder);
            PoseLandmark hip = frame.Get(useLeftBody ? PoseLandmarkType.LeftHip : PoseLandmarkType.RightHip);
            PoseLandmark ankle = frame.Get(useLeftBody ? PoseLandmarkType.LeftAnkle : PoseLandmarkType.RightAnkle);

            // Точка за пределами кадра — экстраполяция, а не наблюдение: MediaPipe дорисовывает
            // её с высокой visibility (на устройстве видели лодыжку на x≈1.05 с vis 0.95),
            // и такая «уверенная» точка порождает фантомные позы. Не видно — значит не видно.
            if (!InFrame(shoulderA) || !InFrame(elbow) || !InFrame(wrist)
                || !InFrame(shoulderB) || !InFrame(hip) || !InFrame(ankle))
                return new FormAssessment(PushUpFault.BodyNotVisible, float.NaN, float.NaN, float.NaN, "В кадр", vis);

            float elbowAngle = PoseMath.AngleDeg3D(shoulderA, elbow, wrist);
            float bodyAngle = PoseMath.AngleDeg3D(shoulderB, hip, ankle);

            // «Ладони на полу»: в упоре лёжа запястья ниже таза; нормируем на длину корпуса,
            // чтобы метрика не зависела от дистанции до камеры. Порог применяет анализатор
            // на уровне повтора (по нижней фазе), а не по кадру — кадровый вариант съедает
            // настоящие повторы из-за дрожания точек.
            float torso = Dist2D(shoulderB, hip);
            float wristBelowHip = torso < 1e-4f ? float.NaN : (wrist.Y - hip.Y) / torso;

            if (bodyAngle < _positionMinDeg)
                return new FormAssessment(PushUpFault.NotInPosition, elbowAngle, bodyAngle, wristBelowHip, "Прими упор лёжа", vis);
            if (bodyAngle < _straightMinDeg)
                return new FormAssessment(PushUpFault.NotStraight, elbowAngle, bodyAngle, wristBelowHip, "Держи тело прямым", vis);

            return new FormAssessment(PushUpFault.None, elbowAngle, bodyAngle, wristBelowHip, string.Empty, vis);
```

Приватные помощники в конце класса:

```csharp
        private static bool InFrame(PoseLandmark p) =>
            p.X >= 0f && p.X <= 1f && p.Y >= 0f && p.Y <= 1f;

        private static float Dist2D(PoseLandmark a, PoseLandmark b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
```

(`using System;` в файле уже есть после предыдущего фикса.)

`Assets/Pose/PushUpAnalyzer.cs`:

Поля и конструктор:

```csharp
        private readonly float _wristBelowHipMin;
        private int _wristOkFrames;
        private int _wristBadFrames;

        public PushUpAnalyzer(RepCounter counter = null, PushUpFormEvaluator evaluator = null,
            float smoothingAlpha = 0.6f, float wristBelowHipMin = 0f)
        {
            _counter = counter ?? new RepCounter();
            _evaluator = evaluator ?? new PushUpFormEvaluator();
            _smoothingAlpha = smoothingAlpha;
            _wristBelowHipMin = wristBelowHipMin;
        }
```

В `ProcessFrame` блок фаз/зачёта заменить на:

```csharp
            RepPhase prevPhase = _counter.Phase;
            bool completed = _counter.Update(_smoothedElbow, frame.TimestampSeconds);
            RepPhase phase = _counter.Phase;

            if (prevPhase != RepPhase.Down && phase == RepPhase.Down)
            {
                _formOkThisRep = true;
                _wristOkFrames = 0;
                _wristBadFrames = 0;
            }
            if (phase == RepPhase.Down)
            {
                if (assessment.Fault == PushUpFault.NotStraight)
                    _formOkThisRep = false;
                // NaN >= x == false, так что неопределённая метрика честно идёт в «плохие».
                if (assessment.WristBelowHip >= _wristBelowHipMin)
                    _wristOkFrames++;
                else
                    _wristBadFrames++;
            }

            if (completed)
            {
                // «Ладони на полу»: если в большинстве кадров нижней фазы запястье было НЕ ниже
                // таза — это не отжимание (стоя со сгибанием рук и т.п.), цикл молча игнорируется.
                if (_wristOkFrames >= _wristBadFrames)
                {
                    Reps++;
                    if (!_formOkThisRep)
                        NoReps++;
                }
                _formOkThisRep = true;
            }
```

`DebugInfo` — добавить в конец строки ` wrist {_wristOkFrames}/{_wristBadFrames}`:

```csharp
        public string DebugInfo =>
            $"elbow {(float.IsNaN(_smoothedElbow) ? "--" : _smoothedElbow.ToString("0"))}°  " +
            $"body {(float.IsNaN(_lastBodyAngle) ? "--" : _lastBodyAngle.ToString("0"))}°  " +
            $"phase {_counter.Phase}  vis {_lastVis:0.00}  {CurrentFault}  wrist {_wristOkFrames}/{_wristBadFrames}";
```

`Reset()` — добавить `_wristOkFrames = 0; _wristBadFrames = 0;`.

- [ ] **Step 4: Прогнать — зелёные**

Run: команда тестов. Expected: `exit=0`. Особо проверить в xml: `StandingArmSwing_DoesNotCount` — Passed (теперь через реп-проверку), `OutOfFrameAnkle_ReportsNotVisible` — Passed, старые пуш-ап тесты — Passed (их кадры целиком в кадре и с запястьем ниже таза, гейты прозрачны).

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/PushUpFormEvaluator.cs Assets/Pose/PushUpAnalyzer.cs Assets/Pose/Tests/PoseTestFrames.cs Assets/Pose/Tests/PushUpFormEvaluatorTests.cs Assets/Pose/Tests/PushUpAnalyzerTests.cs
git commit -m "fix: откат гейта наклона — вместо него гейт «в кадре» и реп-проверка «ладони на полу»"
```

---

### Task 2: Регрессионный корпус из записей устройства

**Files:**
- Create: `Assets/Pose/Tests/Recordings/real_pushups.csv` (копия `pose_rec_180434.csv` из scratchpad, путь в Global Constraints)
- Create: `Assets/Pose/Tests/Recordings/walking_noise.csv` (копия `pose_rec_172053.csv`)
- Create: `Assets/Pose/Tests/CsvPoseFrames.cs`
- Test: `Assets/Pose/Tests/PushUpRecordingTests.cs`

**Interfaces:**
- Consumes: `PushUpAnalyzer` с дефолтами из Task 1; `PoseFrame(PoseLandmark[], double)`; `PoseLandmark(float x, float y, float z, float visibility)`.
- Produces: `internal static class CsvPoseFrames` — `static List<PoseFrame> Load(string assetsRelativePath)`.

- [ ] **Step 1: Скопировать записи и написать загрузчик + падающие тесты**

```powershell
New-Item -ItemType Directory -Force "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings"
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_180434.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\real_pushups.csv"
Copy-Item "C:\Users\user\AppData\Local\Temp\claude\C--Users-user-Mikey\5bd71d42-7e68-4464-a0bd-236cd8508994\scratchpad\pose_rec_172053.csv" "C:\Users\user\Mikey\Assets\Pose\Tests\Recordings\walking_noise.csv"
```

`Assets/Pose/Tests/CsvPoseFrames.cs`:

```csharp
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Loads a pose recording CSV (the on-device format PoseController writes:
    /// t,x0,y0,z0,v0,…,x32,y32,z32,v32) into frames, so real captured movement can be
    /// replayed through the actual analyzers as a regression corpus.
    /// </summary>
    internal static class CsvPoseFrames
    {
        public static List<PoseFrame> Load(string assetsRelativePath)
        {
            string full = Path.Combine(Application.dataPath, assetsRelativePath);
            var frames = new List<PoseFrame>();
            foreach (string line in File.ReadLines(full))
            {
                string[] parts = line.Split(',');
                if (parts.Length < 1 + PoseFrame.LandmarkCount * 4)
                    continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double t))
                    continue; // заголовок или мусор
                var lm = new PoseLandmark[PoseFrame.LandmarkCount];
                bool ok = true;
                for (int i = 0; i < PoseFrame.LandmarkCount && ok; i++)
                {
                    ok = TryF(parts[1 + i * 4], out float x) && TryF(parts[2 + i * 4], out float y)
                      && TryF(parts[3 + i * 4], out float z) && TryF(parts[4 + i * 4], out float v);
                    if (ok)
                        lm[i] = new PoseLandmark(x, y, z, v);
                }
                if (ok)
                    frames.Add(new PoseFrame(lm, t));
            }
            return frames;
        }

        private static bool TryF(string s, out float value) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
```

`Assets/Pose/Tests/PushUpRecordingTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;

namespace Mikey.Pose.Tests
{
    /// <summary>
    /// Regression corpus: two real on-device recordings (2026-08-04). Any scoring change
    /// that breaks genuine push-ups or resurrects phantom reps fails here, not on a phone.
    /// </summary>
    public class PushUpRecordingTests
    {
        private static int Replay(string path)
        {
            var analyzer = new PushUpAnalyzer();
            List<PoseFrame> frames = CsvPoseFrames.Load(path);
            Assert.Greater(frames.Count, 100, "запись подозрительно короткая — файл не загрузился?");
            foreach (PoseFrame f in frames)
                analyzer.ProcessFrame(f);
            return analyzer.Reps;
        }

        [Test]
        public void RealRecording_CountsTwoReps()
        {
            Assert.AreEqual(2, Replay("Pose/Tests/Recordings/real_pushups.csv"));
        }

        [Test]
        public void WalkingRecording_CountsNothing()
        {
            Assert.AreEqual(0, Replay("Pose/Tests/Recordings/walking_noise.csv"));
        }
    }
}
```

- [ ] **Step 2: Прогнать**

Run: команда тестов. Expected: `exit=0` — корпус-тесты сразу зелёные, потому что Task 1 реализовал ровно ту комбинацию, что победила в реплее. Если 2/0 не сходится — BLOCKED (см. Global Constraints), не подгонять.

- [ ] **Step 3: Commit**

```powershell
git add Assets/Pose/Tests/Recordings Assets/Pose/Tests/CsvPoseFrames.cs* Assets/Pose/Tests/PushUpRecordingTests.cs*
git commit -m "test: регрессионный корпус пуш-апа из записей устройства — 2 реальных, 0 фантомов"
```

---

### Task 3: Пересборка APK и установка (Unity CLI)

**Files:** без изменений кода; артефакт `Builds/ExerciseSandbox.apk`.

- [ ] **Step 1: Сборка через unity build** (Editor закрыт; команда сама ждёт до конца — фонового Unity-процесса не остаётся)

```powershell
unity build "C:\Users\user\Mikey" --target Android --execute-method Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid --no-banner; "exit=$LASTEXITCODE"
```

Expected: `exit=0` и свежий mtime у `Builds/ExerciseSandbox.apk` (проверить `Get-Item Builds/ExerciseSandbox.apk | Select LastWriteTime` — время ПОСЛЕ начала сборки; несвежий файл = сборка не прошла, эскалировать, не устанавливать).

- [ ] **Step 2: Установка**

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\user\Mikey\Builds\ExerciseSandbox.apk"
```

Expected: `Success`.

- [ ] **Step 3: Пользовательская проверка** — настоящие отжимания сбоку считаются; ходьба/жесты/подходы к телефону — нет.
