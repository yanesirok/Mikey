# Гейт горизонтальности пуш-апа — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Стоящий/идущий человек больше не засчитывается как отжимающийся — корпус обязан быть горизонтален в кадре.

**Architecture:** Одна проверка в `PushUpFormEvaluator` (наклон вектора плечо→лодыжка к горизонтали кадра по 2D-нормализованным координатам; больше порога → существующий фолт `NotInPosition`). `PushUpAnalyzer`, HUD и каталог не меняются. Новый билдер вертикального скелета в `PoseTestFrames` для тестов.

**Tech Stack:** Unity 6000.3.18f1, C# (`Mikey.Pose`), NUnit EditMode-тесты.

**Спека:** `docs/superpowers/specs/2026-08-04-pushup-orientation-gate-design.md`

## Global Constraints

- **Команда EditMode-тестов** (Unity Editor с проектом ЗАКРЫТ; exit 0 = все прошли; ошибки компиляции дают exit=1; таймаут тулзы 600000):

  ```powershell
  & "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe" -batchmode -projectPath "C:\Users\user\Mikey" -runTests -testPlatform EditMode -testFilter "Mikey.Pose.Tests" -testResults "C:\Users\user\Mikey\Temp\pose_tests.xml" -logFile "C:\Users\user\Mikey\Temp\pose_tests.log" | Out-Null; "exit=$LASTEXITCODE"
  ```

- Поведение существующих тестов не меняется — они на горизонтальной раскладке (`PoseTestFrames.Build`: плечо и лодыжка на одном y), наклон 0°, гейт их не трогает.
- Порог — параметр конструктора с дефолтом `maxTorsoTiltDeg = 40f`; конфиг-файлов нет.
- Коммиты подписывать `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`; посторонние изменённые файлы (арена) не трогать.

---

### Task 1: Гейт наклона корпуса + тесты

**Files:**
- Modify: `Assets/Pose/PushUpFormEvaluator.cs` (конструктор ~строки 61-74; `Evaluate` ~строки 98-108)
- Modify: `Assets/Pose/Tests/PoseTestFrames.cs` (добавить метод `BuildStanding`)
- Test: `Assets/Pose/Tests/PushUpFormEvaluatorTests.cs`, `Assets/Pose/Tests/PushUpAnalyzerTests.cs` (по одному новому тесту)

**Interfaces:**
- Consumes: существующие `PushUpFormEvaluator`, `FormAssessment`, `PushUpFault`, `PoseTestFrames`, `PushUpAnalyzer(RepCounter counter = null, PushUpFormEvaluator evaluator = null, float smoothingAlpha = 0.6f)`.
- Produces: `PushUpFormEvaluator(float minVisibility = 0.6f, float straightMinDeg = 160f, float positionMinDeg = 135f, float maxTorsoTiltDeg = 40f)`; `PoseTestFrames.BuildStanding(float elbowAngleDeg, float visibility = 1f, double timestamp = 0)`.

- [ ] **Step 1: Написать падающие тесты и билдер**

В `Assets/Pose/Tests/PoseTestFrames.cs` добавить метод (после `Build`, используя существующий `Set`):

```csharp
        /// <summary>
        /// Upright (standing) figure: vertical shoulder→hip→ankle line with a
        /// controllable elbow angle, so arm swing while standing/walking can be
        /// simulated. Proves an upright body is never "in push-up position".
        /// </summary>
        public static PoseFrame BuildStanding(float elbowAngleDeg, float visibility = 1f, double timestamp = 0)
        {
            var lm = new PoseLandmark[PoseFrame.LandmarkCount];
            for (int i = 0; i < lm.Length; i++)
                lm[i] = new PoseLandmark(0f, 0f, 0f, visibility);

            // Vertical body line: shoulder on top, hip below, ankle at the bottom.
            float sx = 0.5f, sy = 0.2f;
            float hx = 0.5f, hy = 0.55f;
            float ax = 0.5f, ay = 0.9f;

            // Arm hangs beside the torso; wrist placed to realize the target elbow angle.
            float ex = sx + 0.15f, ey = sy + 0.15f;
            double wd = (-90.0 + elbowAngleDeg) * Deg2Rad;
            float wx = ex + 0.2f * (float)Math.Cos(wd);
            float wy = ey + 0.2f * (float)Math.Sin(wd);

            Set(lm, PoseLandmarkType.LeftShoulder, sx, sy, visibility);
            Set(lm, PoseLandmarkType.LeftElbow, ex, ey, visibility);
            Set(lm, PoseLandmarkType.LeftWrist, wx, wy, visibility);
            Set(lm, PoseLandmarkType.LeftHip, hx, hy, visibility);
            Set(lm, PoseLandmarkType.LeftAnkle, ax, ay, visibility);
            Set(lm, PoseLandmarkType.RightShoulder, sx, sy, visibility);
            Set(lm, PoseLandmarkType.RightElbow, ex, ey, visibility);
            Set(lm, PoseLandmarkType.RightWrist, wx, wy, visibility);
            Set(lm, PoseLandmarkType.RightHip, hx, hy, visibility);
            Set(lm, PoseLandmarkType.RightAnkle, ax, ay, visibility);

            return new PoseFrame(lm, timestamp);
        }
```

В `Assets/Pose/Tests/PushUpFormEvaluatorTests.cs` добавить тест:

```csharp
        [Test]
        public void StandingBody_IsNotAPushUpPosition()
        {
            var evaluator = new PushUpFormEvaluator();
            FormAssessment a = evaluator.Evaluate(PoseTestFrames.BuildStanding(170f));
            Assert.AreEqual(PushUpFault.NotInPosition, a.Fault);
            Assert.IsFalse(a.PostureValid);
        }
```

В `Assets/Pose/Tests/PushUpAnalyzerTests.cs` добавить тест:

```csharp
        [Test]
        public void StandingArmSwing_DoesNotCount()
        {
            var analyzer = new PushUpAnalyzer(smoothingAlpha: 1f);
            analyzer.ProcessFrame(PoseTestFrames.BuildStanding(170f, timestamp: 0.0));
            analyzer.ProcessFrame(PoseTestFrames.BuildStanding(100f, timestamp: 1.0));
            analyzer.ProcessFrame(PoseTestFrames.BuildStanding(170f, timestamp: 2.0));
            Assert.AreEqual(0, analyzer.Reps);
        }
```

- [ ] **Step 2: Прогнать — новые тесты падают**

Run: команда тестов. Expected: `exit=2`, в `Temp/pose_tests.xml` ровно два `result="Failed"` — `StandingBody_IsNotAPushUpPosition` (фолт `None` вместо `NotInPosition`: вертикальная линия «прямая» и проходит) и `StandingArmSwing_DoesNotCount` (`Reps=1`). Это и есть воспроизведённый баг.

- [ ] **Step 3: Реализация гейта**

В `Assets/Pose/PushUpFormEvaluator.cs`:

Конструктор — добавить четвёртый параметр и поле:

```csharp
        private readonly float _maxTorsoTiltDeg;

        /// <param name="minVisibility">Lowest visibility a scored chain may have to be trusted.</param>
        /// <param name="straightMinDeg">Body angle at/above which the plank counts as straight.</param>
        /// <param name="positionMinDeg">Body angle below which it isn't a push-up position at all.</param>
        /// <param name="maxTorsoTiltDeg">Largest tilt of the shoulder→ankle line from the image horizontal that still counts as a plank; an upright body (~90°) is rejected.</param>
        public PushUpFormEvaluator(float minVisibility = 0.6f, float straightMinDeg = 160f, float positionMinDeg = 135f, float maxTorsoTiltDeg = 40f)
        {
            _minVisibility = minVisibility;
            _straightMinDeg = straightMinDeg;
            _positionMinDeg = positionMinDeg;
            _maxTorsoTiltDeg = maxTorsoTiltDeg;
        }
```

В `Evaluate`, сразу после вычисления `bodyAngle` (строка `float bodyAngle = PoseMath.AngleDeg3D(shoulderB, hip, ankle);`) и ПЕРЕД проверкой `if (bodyAngle < _positionMinDeg)` вставить:

```csharp
            // A plank is horizontal in the image; an upright body (standing, walking) has a
            // straight shoulder–hip–ankle line too, so the orientation-invariant 3D body
            // angle alone cannot tell them apart. Gate on the torso's tilt from the image
            // horizontal — coarse on purpose: it separates ~0-20° (plank) from ~70-90°
            // (upright), so aspect-ratio distortion of normalized coords doesn't matter.
            float tilt = (float)(Math.Atan2(Math.Abs(ankle.Y - shoulderB.Y), Math.Abs(ankle.X - shoulderB.X)) * 180.0 / Math.PI);
            if (tilt > _maxTorsoTiltDeg)
                return new FormAssessment(PushUpFault.NotInPosition, elbowAngle, bodyAngle, "Прими упор лёжа", vis);
```

В шапке файла сейчас нет `using System;` — добавить его первой строкой (по образцу соседних файлов `Mikey.Pose`), иначе `Math.Atan2` не скомпилируется.

- [ ] **Step 4: Прогнать — все зелёные**

Run: команда тестов. Expected: `exit=0`, ни одного `Failed`; существующие тесты (горизонтальная раскладка, наклон 0°) не тронуты.

- [ ] **Step 5: Commit**

```powershell
git add Assets/Pose/PushUpFormEvaluator.cs Assets/Pose/Tests/PoseTestFrames.cs Assets/Pose/Tests/PushUpFormEvaluatorTests.cs Assets/Pose/Tests/PushUpAnalyzerTests.cs
git commit -m "fix: стоя пуш-апы не считаются — гейт наклона корпуса к горизонтали"
```

---

### Task 2: Пересборка APK и переустановка

**Files:** без изменений кода; артефакт `Builds/ExerciseSandbox.apk`.

- [ ] **Step 1: Инкрементальная пересборка** (Editor закрыт; фон, ожидание завершения)

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Unity.exe" -quit -batchmode -projectPath "C:\Users\user\Mikey" -buildTarget Android -executeMethod Mikey.Pose.DevSandbox.EditorTools.AndroidBuilder.BuildAndroid -logFile "C:\Users\user\Mikey\Temp\apk_build2.log"; "exit=$LASTEXITCODE"
```

Expected: `exit=0`, свежий mtime у `Builds/ExerciseSandbox.apk`.

- [ ] **Step 2: Установка** (устройство уже авторизовано)

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.3.18f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe" install -r "C:\Users\user\Mikey\Builds\ExerciseSandbox.apk"
```

Expected: `Success`.

- [ ] **Step 3: Пользовательская проверка** — ходьба/произвольные движения перед камерой в Push-ups не двигают счёт («Прими упор лёжа» на экране); настоящие отжимания сбоку считаются как раньше.
